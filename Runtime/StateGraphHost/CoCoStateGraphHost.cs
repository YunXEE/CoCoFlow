using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal enum CoCoStateGraphDriver
    {
        Update = 0,
        FixedUpdate = 1,
        Manual = 2
    }

    [DisallowMultipleComponent]
    public sealed class CoCoStateGraphHost : MonoBehaviour
    {
        [SerializeField] private CoCoStateGraphAsset stateGraphAsset;
        [SerializeField] private CoCoStateGraphDriver driver = CoCoStateGraphDriver.Update;
        [SerializeField] private bool autoStart = true;
        [SerializeField, Min(0.0001f)] private float timeScale = 1f;
        [SerializeField, Min(1)] private int eventLaneCapacity = 32;
        [SerializeField, Min(1)] private int eventSourceCapacity = 32;
        [SerializeField, Min(1)] private int eventDedupCapacity = 128;

        private CoCoStateGraphHostRuntimeBindings _bindings;
        private CoCoStateGraphRuntime _runtime;
        private CoCoContextFrame _committedContext;
        private bool _hasStoppedInstance;
        private bool _isDisposed;
        private CoCoDiagnostic _lastDiagnostic;
        private int _lastAutomaticFrame = -1;
        private bool _reliableOverflowPending;
        private bool _acceptsEventInput;
        private bool _isStarting;
        private bool _isAdvancing;
        private bool _destroyRequested;
        private CommitGuard _commitGuard;

        public CoCoStateGraphAsset StateGraphAsset => stateGraphAsset;
        internal CoCoStateGraphDriver Driver => driver;
        internal bool AutoStart => autoStart;
        internal float TimeScale => timeScale;
        internal int EventLaneCapacity => eventLaneCapacity;
        internal int EventSourceCapacity => eventSourceCapacity;
        internal int EventDedupCapacity => eventDedupCapacity;
        public CoCoRuntimeLifecycleState Lifecycle => _runtime?.Lifecycle ??
            (_isDisposed
                ? CoCoRuntimeLifecycleState.Disposed
                : _hasStoppedInstance
                    ? CoCoRuntimeLifecycleState.Stopped
                    : CoCoRuntimeLifecycleState.Created);
        public CoCoRuntimeFault Fault => _runtime?.Fault ?? default;
        public CoCoGraphInstanceId GraphInstanceId => _runtime?.GraphInstanceId ?? default;
        public IReadOnlyList<CoCoActivePath> ActivePaths => _runtime?.ActivePaths ?? Array.Empty<CoCoActivePath>();
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        internal bool CanAcceptEventInput =>
            _acceptsEventInput &&
            _runtime != null &&
            (_runtime.Lifecycle == CoCoRuntimeLifecycleState.Running ||
             _runtime.Lifecycle == CoCoRuntimeLifecycleState.Suspended) &&
            !_runtime.IsFaulted &&
            !_reliableOverflowPending;

        private void Start()
        {
            if (autoStart)
            {
                TryStart(out _lastDiagnostic);
            }
        }

        private void Update()
        {
            if (driver == CoCoStateGraphDriver.Update)
            {
                TryAutomaticStep(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (driver == CoCoStateGraphDriver.FixedUpdate)
            {
                TryAutomaticStep(Time.fixedDeltaTime);
            }
        }

        private void OnDestroy()
        {
            _acceptsEventInput = false;
            if (_isStarting || _isAdvancing)
            {
                _destroyRequested = true;
                return;
            }

            ForceDisposeHost();
        }

        public bool TryStart(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            _isStarting = true;
            try
            {
                bool started = TryStartCore(out diagnostic);
                if (_destroyRequested)
                {
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup before publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                return started;
            }
            finally
            {
                _isStarting = false;
                if (_destroyRequested)
                {
                    _destroyRequested = false;
                    ForceDisposeHost();
                }
            }
        }

        private bool TryStartCore(out CoCoDiagnostic diagnostic)
        {
            if (_runtime != null || _isDisposed)
            {
                diagnostic = LifecycleError("Host can start only from Created or Stopped.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (stateGraphAsset == null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "CoCoStateGraphHost requires one StateGraph Asset.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            ICoCoStateGraphProjectBindingProvider provider =
                CoCoStateGraphProjectBindings.Provider;
            if (provider == null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "No StateGraph project binding provider was installed.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!IsPositiveFinite(timeScale) ||
                !Enum.IsDefined(typeof(CoCoStateGraphDriver), driver))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "Host TimeScale must be finite and greater than zero, and Driver must be defined.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            CoCoStateGraphAssetCompileResult compileResult;
            try
            {
                compileResult = new CoCoStateGraphAssetCompiler().Compile(
                    stateGraphAsset,
                    provider.Catalog);
            }
            catch (Exception)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "StateGraph Asset compilation threw during Host setup.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!compileResult.Succeeded)
            {
                diagnostic = FirstCompileError(compileResult);
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_destroyRequested)
            {
                diagnostic = LifecycleError(
                    "Unity destruction cancelled StateGraph Host startup during Asset compilation.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            CoCoGraphInstanceId graphInstanceId = CoCoStateGraphHostIdentity.Next();
            var builder = new CoCoStateGraphHostBindingBuilder(
                compileResult.Graph,
                graphInstanceId);
            CoCoStateGraphHostRuntimeBindings bindings = null;
            CoCoStateGraphRuntime runtime = null;
            try
            {
                if (!provider.TryConfigure(builder, out diagnostic) || diagnostic.IsError)
                {
                    builder.Abandon();
                    _lastDiagnostic = diagnostic.IsError
                        ? diagnostic
                        : RegistryError(
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Project StateGraph bindings were incomplete.");
                    diagnostic = _lastDiagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    builder.Abandon();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup during project binding.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!builder.TryFreeze(
                        eventLaneCapacity,
                        eventSourceCapacity,
                        eventDedupCapacity,
                        out bindings,
                        out diagnostic))
                {
                    builder.Abandon();
                    _lastDiagnostic = diagnostic.IsError
                        ? diagnostic
                        : RegistryError(
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Project StateGraph bindings were incomplete.");
                    diagnostic = _lastDiagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup before Runtime creation.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!TryCreateTimelineId(
                        compileResult.Graph.GraphId,
                        graphInstanceId,
                        out CoCoTimelineId timelineId,
                        out diagnostic) ||
                    !CoCoClockDomainId.TryCreate(
                        (ulong)driver + 1UL,
                        out CoCoClockDomainId clockDomainId) ||
                    !CoCoActorClock.TryCreate(
                        timelineId,
                        clockDomainId,
                        new CoCoTimelineEpoch(0UL),
                        graphInstanceId,
                        out CoCoActorClock clock,
                        out diagnostic))
                {
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!CoCoStateGraphRuntime.TryCreate(
                        compileResult.Graph,
                        graphInstanceId,
                        bindings.Logic,
                        bindings.Operations,
                        clock,
                        out runtime,
                        out diagnostic))
                {
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    runtime.Dispose();
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup during Runtime creation.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!runtime.TryStart(out diagnostic))
                {
                    runtime.Dispose();
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    runtime.Dispose();
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup before Runtime publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _bindings = bindings;
                _runtime = runtime;
                if (_commitGuard == null)
                {
                    _commitGuard = new CommitGuard(this);
                }

                _committedContext = default;
                _reliableOverflowPending = false;
                _lastAutomaticFrame = -1;

                // Registration is deliberately last: no packet can reach a partially started Host.
                if (!_bindings.RegisterRouter(this))
                {
                    DisposeInstance();
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "Router rejected a duplicate GraphInstance event sink.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _acceptsEventInput = true;
                _hasStoppedInstance = false;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception)
            {
                if (runtime != null && !ReferenceEquals(_runtime, runtime))
                {
                    runtime.Dispose();
                }

                if (bindings == null)
                {
                    builder.Abandon();
                }
                else if (!ReferenceEquals(_bindings, bindings))
                {
                    bindings.Dispose();
                }

                DisposeInstance();
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Host setup failed before the StateGraph instance became observable.");
                _lastDiagnostic = diagnostic;
                return false;
            }
        }

        public bool TrySuspend(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            if (_runtime == null || _runtime.IsFaulted)
            {
                diagnostic = LifecycleError("Only a healthy Running Host can suspend.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!_runtime.TrySuspend(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_bindings.Inbox != null && !_bindings.Inbox.Suspend())
            {
                _runtime.TryResume(out _);
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Inbox could not enter Suspended with the Runtime.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        public bool TryResume(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            if (_runtime == null || _runtime.IsFaulted || LatchPendingOverflow(out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = LifecycleError("A Faulted or missing Host cannot resume.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_bindings.Inbox != null && !_bindings.Inbox.Resume())
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Inbox could not resume with the Runtime.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!_runtime.TryResume(out diagnostic))
            {
                _bindings.Inbox?.Suspend();
                _lastDiagnostic = diagnostic;
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        public bool TryStop(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            if (_runtime == null ||
                (_runtime.Lifecycle != CoCoRuntimeLifecycleState.Running &&
                 _runtime.Lifecycle != CoCoRuntimeLifecycleState.Suspended))
            {
                diagnostic = LifecycleError("Host has no live Graph instance to stop.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            // Unregister first so no packet can target a tearing-down instance.
            _acceptsEventInput = false;
            _bindings?.UnregisterRouter();
            if (!_runtime.TryStop(out diagnostic))
            {
                if (_bindings != null)
                {
                    if (!_bindings.RegisterRouter(this))
                    {
                        diagnostic = RegistryError(
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "Host could not restore Router registration after Stop was rejected.");
                        _runtime.TryLatchExternalFault(diagnostic);
                    }
                    else
                    {
                        _acceptsEventInput = true;
                    }
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            DisposeInstance();
            _hasStoppedInstance = true;
            _lastDiagnostic = diagnostic;
            return !diagnostic.IsError;
        }

        public bool TryDispose(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            if (_runtime != null || _isDisposed)
            {
                diagnostic = LifecycleError(
                    "Host can be disposed only from Created or Stopped; stop a live Graph instance first.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            DisposeHostFromLegalState();
            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        public bool TryStep(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (driver != CoCoStateGraphDriver.Manual)
            {
                diagnostic = LifecycleError("Public Manual Step requires the Host Manual driver.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            return TryAdvance(deltaTime, out diagnostic);
        }

        public CoCoInboxEnqueueResult TryEnqueueLocal<TEvent>(
            in CoCoEventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!CanAcceptEventInput || _bindings == null || !_bindings.HasEvents)
            {
                return CoCoInboxEnqueueResult.MailboxUnavailable;
            }

            if (!packet.IsValid ||
                packet.Envelope.SourceGraphInstanceId != GraphInstanceId)
            {
                return CoCoInboxEnqueueResult.InvalidPacket;
            }

            CoCoInboxEnqueueResult result = _bindings.TryEnqueueLocal(packet);
            if (result == CoCoInboxEnqueueResult.ReliableOverflowFaultRequired)
            {
                MarkReliableOverflowPending();
            }

            return result;
        }

        internal void MarkReliableOverflowPending()
        {
            if (_runtime != null && !_runtime.IsFaulted)
            {
                _reliableOverflowPending = true;
            }
        }

        private void TryAutomaticStep(float deltaTime)
        {
            if (_runtime == null ||
                _runtime.Lifecycle != CoCoRuntimeLifecycleState.Running ||
                _runtime.IsFaulted ||
                _lastAutomaticFrame == Time.frameCount ||
                !IsPositiveFinite(deltaTime))
            {
                return;
            }

            // Pre4 intentionally has no production coordinator; automatic stepping remains inert
            // until Pre5 installs the internal transactional coordinator.
            if (CoCoStateGraphTransactionCoordinatorRegistry.Current == null)
            {
                return;
            }

            _lastAutomaticFrame = Time.frameCount;
            TryAdvance(deltaTime, out _lastDiagnostic);
        }

        private bool TryAdvance(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (_isStarting || _isAdvancing)
            {
                diagnostic = LifecycleError(
                    "StateGraph Host cannot Step during startup or reenter its active Tick.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            _isAdvancing = true;
            try
            {
                return TryAdvanceCore(deltaTime, out diagnostic);
            }
            finally
            {
                _isAdvancing = false;
                if (_destroyRequested)
                {
                    _destroyRequested = false;
                    ForceDisposeHost();
                }
            }
        }

        private bool TryAdvanceCore(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            ICoCoStateGraphTransactionCoordinator coordinator =
                CoCoStateGraphTransactionCoordinatorRegistry.Current;
            if (_runtime == null ||
                _runtime.Lifecycle != CoCoRuntimeLifecycleState.Running ||
                _runtime.IsFaulted)
            {
                diagnostic = LifecycleError("Only a healthy Running Host can Step.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (coordinator == null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "Pre4 has no production transaction coordinator; Pre5 must finalize Context before commit.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            CoCoTickFrame tickFrame = default;
            CoCoStagedGraphStep stagedStep = default;
            try
            {
                if (!_runtime.TryPreviewNextTick(
                        deltaTime,
                        timeScale,
                        out tickFrame,
                        out diagnostic))
                {
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!_bindings.TryCollectIntents(
                        tickFrame,
                        out ICoCoIntentFrame intents,
                        out diagnostic))
                {
                    CoCoDiagnostic reason = diagnostic.IsError
                        ? diagnostic
                        : CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.CommitPreparationFailed,
                            "Intent collection failed after the Host sealed its Tick input.");
                    _runtime.TryLatchExternalFault(reason);
                    _bindings.ResolveIntentTick(tickFrame);
                    diagnostic = _runtime.IsFaulted
                        ? _runtime.Fault.Diagnostic
                        : reason;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        default,
                        tickFrame,
                        out diagnostic);
                }

                if (!_runtime.TryStageStep(
                        tickFrame,
                        intents,
                        _committedContext,
                        out stagedStep,
                        out diagnostic))
                {
                    if (_runtime.IsFaulted)
                    {
                        _bindings.ResolveIntentTick(tickFrame);
                    }

                    _lastDiagnostic = diagnostic;
                    return false;
                }
            }
            catch (Exception)
            {
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        stagedStep,
                        tickFrame,
                        out diagnostic);
                }

                CoCoDiagnostic reason = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Intent collection or Tick staging threw before transaction finalization.");
                bool rejected = stagedStep.IsValid &&
                                _runtime.TryRejectStagedStep(
                                    stagedStep,
                                    reason,
                                    true,
                                    _commitGuard,
                                    out diagnostic);
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        default,
                        tickFrame,
                        out diagnostic);
                }

                if (!rejected)
                {
                    _runtime.TryLatchExternalFault(reason);
                    diagnostic = _runtime.IsFaulted
                        ? _runtime.Fault.Diagnostic
                        : reason;
                }

                _bindings.ResolveIntentTick(tickFrame);
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_destroyRequested)
            {
                return CancelTickForPendingDestroy(
                    stagedStep,
                    tickFrame,
                    out diagnostic);
            }

            try
            {
                bool finalized = coordinator.TryFinalize(
                        this,
                        stagedStep,
                        _committedContext,
                        out CoCoStateGraphTransactionDecision decision,
                        out CoCoContextFrame committedContext,
                        out diagnostic);
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        stagedStep,
                        tickFrame,
                        out diagnostic);
                }

                if (!finalized)
                {
                    CoCoDiagnostic reason = diagnostic.IsError
                        ? diagnostic
                        : CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Operation,
                            CoCoDiagnosticCode.CommitCancelled,
                            "Transaction coordinator rejected the staged Tick.");
                    _runtime.TryRejectStagedStep(
                        stagedStep,
                        reason,
                        true,
                        _commitGuard,
                        out diagnostic);
                    if (_destroyRequested)
                    {
                        return CancelTickForPendingDestroy(
                            default,
                            tickFrame,
                            out diagnostic);
                    }

                    _bindings.ResolveIntentTick(tickFrame);
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (decision == CoCoStateGraphTransactionDecision.Accept)
                {
                    if (!_runtime.TryAcceptStagedStep(
                            stagedStep,
                            _commitGuard,
                            out diagnostic))
                    {
                        _bindings.ResolveIntentTick(tickFrame);
                        _lastDiagnostic = diagnostic;
                        return false;
                    }

                    _bindings.ResolveIntentTick(tickFrame);
                    _committedContext = committedContext;
                    _lastDiagnostic = CoCoDiagnostic.None;
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                bool latchFault = decision == CoCoStateGraphTransactionDecision.RejectAndFault;
                CoCoDiagnostic rejection = diagnostic.IsError
                    ? diagnostic
                    : CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Operation,
                        CoCoDiagnosticCode.CommitCancelled,
                        "Transaction coordinator cancelled the staged Tick.");
                bool rejected = _runtime.TryRejectStagedStep(
                    stagedStep,
                    rejection,
                    latchFault,
                    _commitGuard,
                    out diagnostic);
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        default,
                        tickFrame,
                        out diagnostic);
                }

                if (latchFault)
                {
                    _bindings.ResolveIntentTick(tickFrame);
                }

                _lastDiagnostic = diagnostic;
                return false;
            }
            catch (Exception)
            {
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        stagedStep,
                        tickFrame,
                        out diagnostic);
                }

                CoCoDiagnostic reason = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operation,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Transaction coordinator threw while finalizing the staged Tick.");
                _runtime.TryRejectStagedStep(
                    stagedStep,
                    reason,
                    true,
                    _commitGuard,
                    out diagnostic);
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        default,
                        tickFrame,
                        out diagnostic);
                }

                _bindings.ResolveIntentTick(tickFrame);
                _lastDiagnostic = diagnostic;
                return false;
            }
        }

        private bool CancelTickForPendingDestroy(
            in CoCoStagedGraphStep stagedStep,
            in CoCoTickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            CoCoDiagnostic reason = LifecycleError(
                "Unity destruction cancelled the unresolved Tick before commit.");
            diagnostic = reason;
            if (stagedStep.IsValid &&
                (!_runtime.TryCancelStagedStep(
                     stagedStep,
                     out CoCoDiagnostic cancellationDiagnostic) ||
                 cancellationDiagnostic.IsError))
            {
                diagnostic = cancellationDiagnostic.IsError
                    ? cancellationDiagnostic
                    : reason;
            }

            _bindings.ResolveIntentTick(tickFrame);
            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool LatchPendingOverflow(out CoCoDiagnostic diagnostic)
        {
            if (!_reliableOverflowPending || _runtime == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return false;
            }

            diagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Mailbox,
                CoCoDiagnosticCode.MailboxOverflow,
                "A reliable Event overflowed this Actor Inbox.");
            _runtime.TryLatchExternalFault(diagnostic);
            _reliableOverflowPending = false;
            return true;
        }

        private bool RejectLifecycleReentry(out CoCoDiagnostic diagnostic)
        {
            if (!_isStarting && !_isAdvancing)
            {
                diagnostic = CoCoDiagnostic.None;
                return false;
            }

            diagnostic = LifecycleError(
                "StateGraph Host lifecycle cannot change while startup or a Tick is advancing.");
            _lastDiagnostic = diagnostic;
            return true;
        }

        private void DisposeInstance()
        {
            _acceptsEventInput = false;
            _bindings?.UnregisterRouter();
            _bindings?.Dispose();
            _bindings = null;
            _runtime?.Dispose();
            _runtime = null;
            _committedContext = default;
            _reliableOverflowPending = false;
            _lastAutomaticFrame = -1;
        }

        private void DisposeHostFromLegalState()
        {
            if (_isDisposed)
            {
                return;
            }

            // Router unregistration remains the first observable teardown action.
            _acceptsEventInput = false;
            _bindings?.UnregisterRouter();
            DisposeInstance();
            _hasStoppedInstance = false;
            _isDisposed = true;
        }

        private void ForceDisposeHost()
        {
            if (_isDisposed)
            {
                return;
            }

            // Unity destruction cannot be rejected. It still closes a live instance through
            // the frozen Running/Suspended -> Stopped -> Disposed lifecycle edges.
            _acceptsEventInput = false;
            _bindings?.UnregisterRouter();
            if (_runtime != null &&
                (_runtime.Lifecycle == CoCoRuntimeLifecycleState.Running ||
                 _runtime.Lifecycle == CoCoRuntimeLifecycleState.Suspended))
            {
                if (!_runtime.TryStop(out _))
                {
                    _runtime.Dispose();
                }
            }

            DisposeInstance();
            _hasStoppedInstance = false;
            _isDisposed = true;
        }

        private static bool TryCreateTimelineId(
            CoCoGraphId graphId,
            CoCoGraphInstanceId graphInstanceId,
            out CoCoTimelineId timelineId,
            out CoCoDiagnostic diagnostic)
        {
            if (!graphId.IsValid ||
                !graphInstanceId.IsValid ||
                !CoCoTimelineId.TryCreate(
                    graphInstanceId.Value,
                    graphId.High ^ graphId.Low,
                    out timelineId))
            {
                timelineId = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "StateGraph Host could not derive a valid TimelineId from its Graph and instance identities.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic FirstCompileError(CoCoStateGraphAssetCompileResult result)
        {
            for (int index = 0; index < result.Diagnostics.Count; index++)
            {
                if (result.Diagnostics[index].IsError)
                {
                    return result.Diagnostics[index].Diagnostic;
                }
            }

            return RegistryError(
                CoCoDiagnosticCode.CommitPreparationFailed,
                "StateGraph Asset compilation did not produce a runnable Graph.");
        }

        private static bool IsPositiveFinite(double value) =>
            value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        private sealed class CommitGuard : ICoCoStateGraphCommitGuard
        {
            private readonly CoCoStateGraphHost _host;

            public CommitGuard(CoCoStateGraphHost host)
            {
                _host = host;
            }

            public bool IsCommitCancellationRequested => _host._destroyRequested;
        }
    }

    internal enum CoCoStateGraphTransactionDecision
    {
        Accept = 1,
        Cancel = 2,
        RejectAndFault = 3
    }

    internal interface ICoCoStateGraphTransactionCoordinator
    {
        bool TryFinalize(
            CoCoStateGraphHost host,
            in CoCoStagedGraphStep stagedStep,
            in CoCoContextFrame previousContext,
            out CoCoStateGraphTransactionDecision decision,
            out CoCoContextFrame committedContext,
            out CoCoDiagnostic diagnostic);
    }

    internal static class CoCoStateGraphTransactionCoordinatorRegistry
    {
        private static ICoCoStateGraphTransactionCoordinator _current;

        internal static ICoCoStateGraphTransactionCoordinator Current => _current;

        internal static bool TryInstall(
            ICoCoStateGraphTransactionCoordinator coordinator,
            out CoCoDiagnostic diagnostic)
        {
            if (coordinator == null || _current != null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    _current == null
                        ? CoCoDiagnosticCode.MissingDescriptor
                        : CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph transaction coordinator must be installed exactly once.");
                return false;
            }

            _current = coordinator;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static void Reset()
        {
            _current = null;
        }
    }

    internal static class CoCoStateGraphHostIdentity
    {
        private static ulong _nextValue = 1UL;

        public static CoCoGraphInstanceId Next()
        {
            ulong value = _nextValue++;
            if (value == 0UL)
            {
                value = _nextValue++;
            }

            CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id);
            return id;
        }

        public static void Reset()
        {
            _nextValue = 1UL;
        }
    }
}
