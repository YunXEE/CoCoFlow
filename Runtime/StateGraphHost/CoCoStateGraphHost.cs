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
        [SerializeField] private MonoBehaviour[] operators = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour actorContextBinding;
        [SerializeField, Min(2)] private int contextFrameCapacity = 3;
        [SerializeField, Min(0)] private int eventOutboxCapacity = 32;
        [SerializeField, Min(0)] private int traceCapacity;
        [SerializeField, Min(1)] private int eventLaneCapacity = 32;
        [SerializeField, Min(1)] private int eventSourceCapacity = 32;
        [SerializeField, Min(1)] private int eventDedupCapacity = 128;

        private CoCoStateGraphHostRuntimeBindings _bindings;
        private CoCoStateGraphRuntime _runtime;
        private CoCoStateGraphTransaction _transaction;
        private bool _hasStoppedInstance;
        private bool _isDisposed;
        private CoCoDiagnostic _lastDiagnostic;
        private int _lastAutomaticFrame = -1;
        private bool _reliableOverflowPending;
        private bool _acceptsEventInput;
        private bool _isStarting;
        private bool _isAdvancing;
        private bool _isPublishingCommittedEvents;
        private bool _destroyRequested;
        private bool _stopAfterPublish;
        private bool _disposeAfterPublish;
        private bool _requiresWorldCorrection;
        private CommitGuard _commitGuard;

        public CoCoStateGraphAsset StateGraphAsset => stateGraphAsset;
        internal CoCoStateGraphDriver Driver => driver;
        internal bool AutoStart => autoStart;
        internal float TimeScale => timeScale;
        internal IReadOnlyList<MonoBehaviour> Operators => operators ?? Array.Empty<MonoBehaviour>();
        internal MonoBehaviour ActorContextBinding => actorContextBinding;
        internal int ContextFrameCapacity => contextFrameCapacity;
        internal int EventOutboxCapacity => eventOutboxCapacity;
        internal int TraceCapacity => traceCapacity;
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
        public CoCoContextFrame CurrentContext => _transaction?.CurrentContext ?? default;
        public ICoCoStateFlowTrace Trace => _transaction?.Trace;
        public bool RequiresWorldCorrection => _requiresWorldCorrection;
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
                !Enum.IsDefined(typeof(CoCoStateGraphDriver), driver) ||
                contextFrameCapacity < 2 ||
                eventOutboxCapacity < 0 ||
                traceCapacity < 0)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "Host Driver, TimeScale, Context, Outbox, and Trace capacities are invalid.");
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
            CoCoStateGraphTransaction transaction = null;
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

                if (!CoCoStateGraphTransaction.TryPreflight(
                        this,
                        compileResult.Graph,
                        graphInstanceId,
                        bindings.ContextLayout,
                        bindings.Operations,
                        bindings.ContextProducers,
                        contextFrameCapacity,
                        eventOutboxCapacity,
                        traceCapacity,
                        out transaction,
                        out diagnostic))
                {
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    transaction.Dispose();
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup after transaction preflight.");
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
                    transaction.Dispose();
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
                    transaction.Dispose();
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    runtime.Dispose();
                    transaction.Dispose();
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup during Runtime creation.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!runtime.TryStart(out diagnostic))
                {
                    runtime.Dispose();
                    transaction.Dispose();
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    runtime.Dispose();
                    transaction.Dispose();
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup during Runtime start.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                CommitGuard commitGuard = _commitGuard ?? new CommitGuard(this);

                if (!transaction.TryValidateInitialGraphContextDefaults(
                        runtime,
                        commitGuard,
                        out diagnostic))
                {
                    runtime.Dispose();
                    transaction.Dispose();
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    transaction.Dispose();
                    runtime.Dispose();
                    bindings.Dispose();
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup before Runtime publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _bindings = bindings;
                _runtime = runtime;
                _transaction = transaction;
                _commitGuard = commitGuard;

                _requiresWorldCorrection = false;
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

                if (transaction != null && !ReferenceEquals(_transaction, transaction))
                {
                    transaction.Dispose();
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

            _transaction?.Suspend();

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
            if (_isPublishingCommittedEvents)
            {
                if (_runtime == null ||
                    (_runtime.Lifecycle != CoCoRuntimeLifecycleState.Running &&
                     _runtime.Lifecycle != CoCoRuntimeLifecycleState.Suspended))
                {
                    diagnostic = LifecycleError(
                        "Host has no live Graph instance to stop after committed Event publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _stopAfterPublish = true;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }

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
            if (_isPublishingCommittedEvents)
            {
                if (!_stopAfterPublish)
                {
                    diagnostic = LifecycleError(
                        "A live Host must first accept Stop before Dispose can be deferred from Event publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _disposeAfterPublish = true;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }

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

        public bool TryValidateRestore(
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoContextCommitStatus status)
        {
            if (_transaction == null)
            {
                status = CoCoContextCommitStatus.InvalidPreparation;
                return false;
            }

            return _transaction.TryValidateRestore(
                _runtime,
                source,
                resumedTickFrame,
                out status);
        }

        internal bool TryPrepareRestore(
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoPreparedActorRestore preparedRestore,
            out CoCoContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            if (_transaction == null || _runtime == null)
            {
                preparedRestore = default;
                status = CoCoContextCommitStatus.InvalidPreparation;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Actor restore preparation requires one live Host transaction.");
                return false;
            }

            return _transaction.TryPrepareRestore(
                _runtime,
                source,
                resumedTickFrame,
                out preparedRestore,
                out status,
                out diagnostic);
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
                    _stopAfterPublish = false;
                    _disposeAfterPublish = false;
                    ForceDisposeHost();
                }
                else
                {
                    CompleteDeferredPublishLifecycle();
                }
            }
        }

        internal void BeginCommittedEventPublication()
        {
            _isPublishingCommittedEvents = true;
        }

        internal void EndCommittedEventPublication()
        {
            _isPublishingCommittedEvents = false;
        }

        private bool TryAdvanceCore(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (_runtime == null ||
                _transaction == null ||
                _runtime.Lifecycle != CoCoRuntimeLifecycleState.Running ||
                _runtime.IsFaulted)
            {
                diagnostic = LifecycleError("Only a healthy Running Host can Step.");
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

                if (!_transaction.TryPrepareContext(
                        tickFrame,
                        out CoCoContextCommitStatus contextStatus,
                        out diagnostic))
                {
                    // Capacity exhaustion occurs before Inbox seal and is intentionally retryable.
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!_bindings.TryCollectIntents(
                        tickFrame,
                        out ICoCoIntentFrame intents,
                        out diagnostic))
                {
                    CoCoDiagnostic collectionFailure = diagnostic.IsError
                        ? diagnostic
                        : CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.CommitPreparationFailed,
                            "Intent collection failed after the Host sealed its Tick input.");
                    _transaction.Cancel(collectionFailure);
                    _runtime.TryLatchExternalFault(collectionFailure);
                    _bindings.ResolveIntentTick(tickFrame);
                    diagnostic = _runtime.IsFaulted
                        ? _runtime.Fault.Diagnostic
                        : collectionFailure;
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
                        _transaction.PreviousContext,
                        out stagedStep,
                        out diagnostic))
                {
                    if (_runtime.IsFaulted)
                    {
                        _bindings.ResolveIntentTick(tickFrame);
                    }

                    _transaction.Cancel(diagnostic);

                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _transaction.AppendStagedTrace(_runtime, stagedStep);
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

                CoCoDiagnostic stagingFailure = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Intent collection or Tick staging threw before transaction finalization.");
                _transaction.Cancel(stagingFailure);
                bool rejected = stagedStep.IsValid &&
                                _runtime.TryRejectStagedStep(
                                    stagedStep,
                                    stagingFailure,
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
                    _runtime.TryLatchExternalFault(stagingFailure);
                    diagnostic = _runtime.IsFaulted
                        ? _runtime.Fault.Diagnostic
                        : stagingFailure;
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

            bool finalized = _transaction.TryFinalizeAndCommit(
                _runtime,
                _bindings,
                stagedStep,
                _commitGuard,
                out bool authorityCommitted,
                out bool worldMayBeDirty,
                out diagnostic);
            if (finalized)
            {
                _lastDiagnostic = CoCoDiagnostic.None;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            CoCoDiagnostic reason = diagnostic.IsError
                ? diagnostic
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.CommitCancelled,
                    "Operator transaction cancelled the staged Tick.");
            if (authorityCommitted)
            {
                // Publication happens after the complete authority barrier. A publish fault
                // cannot roll back Context, Graph, Claims, Clock, or assigned Sequences.
                _runtime.TryLatchExternalFault(reason);
                diagnostic = _runtime.IsFaulted ? _runtime.Fault.Diagnostic : reason;
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (worldMayBeDirty)
            {
                _requiresWorldCorrection = true;
            }

            if (_destroyRequested)
            {
                return CancelTickForPendingDestroy(
                    stagedStep,
                    tickFrame,
                    out diagnostic);
            }

            if (stagedStep.IsValid)
            {
                _runtime.TryRejectStagedStep(
                    stagedStep,
                    reason,
                    true,
                    _commitGuard,
                    out diagnostic);
            }
            else if (!_runtime.IsFaulted)
            {
                _runtime.TryLatchExternalFault(reason);
                diagnostic = _runtime.IsFaulted ? _runtime.Fault.Diagnostic : reason;
            }

            _bindings.ResolveIntentTick(tickFrame);
            if (_requiresWorldCorrection)
            {
                CoCoDiagnostic correction = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.WorldCorrectionRequired,
                    "A failed Operator transaction may have changed Unity state; Restore correction is required before resuming authority.");
                if (!_runtime.IsFaulted)
                {
                    _runtime.TryLatchExternalFault(correction);
                }
            }

            diagnostic = _runtime.IsFaulted ? _runtime.Fault.Diagnostic : reason;
            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool CancelTickForPendingDestroy(
            in CoCoStagedGraphStep stagedStep,
            in CoCoTickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            CoCoDiagnostic reason = LifecycleError(
                "Unity destruction cancelled the unresolved Tick before commit.");
            _transaction?.Cancel(reason);
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

        private void CompleteDeferredPublishLifecycle()
        {
            bool stop = _stopAfterPublish;
            bool dispose = _disposeAfterPublish;
            _stopAfterPublish = false;
            _disposeAfterPublish = false;
            if (stop && _runtime != null)
            {
                TryStop(out _);
            }

            if (dispose && !_isDisposed)
            {
                TryDispose(out _);
            }
        }

        private void DisposeInstance()
        {
            _acceptsEventInput = false;
            _bindings?.UnregisterRouter();
            _transaction?.Dispose();
            _transaction = null;
            _bindings?.Dispose();
            _bindings = null;
            _runtime?.Dispose();
            _runtime = null;
            _isPublishingCommittedEvents = false;
            _stopAfterPublish = false;
            _disposeAfterPublish = false;
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
