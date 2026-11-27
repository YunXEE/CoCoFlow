using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraph
{
    internal enum StateGraphDriver
    {
        Update = 0,
        FixedUpdate = 1,
        Manual = 2
    }

    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoStateGraphHost")]
    [DisallowMultipleComponent]
    public sealed partial class StateGraphHost : MonoBehaviour
    {
        [SerializeField] private StateGraphAsset stateGraphAsset;
        [SerializeField] private StateGraphDriver driver = StateGraphDriver.Update;
        [SerializeField] private bool autoStart = true;
        [SerializeField, Min(0.0001f)] private float timeScale = 1f;
        [SerializeField] private MonoBehaviour[] intentSources = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour[] eventAdapters = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour[] operators = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour actorContextBinding;
        [SerializeField] private MonoBehaviour contextRestoreBinding;
        [SerializeField, Min(0)] private int temporalHistoryCapacity;
        [SerializeField, Min(2)] private int contextFrameCapacity = 3;
        [SerializeField, Min(0)] private int eventOutboxCapacity = 32;
        [SerializeField, Min(0)] private int traceCapacity;
        [SerializeField, Min(1)] private int eventLaneCapacity = 32;

        private StateGraphHostRuntimeBindings _bindings;
        private StateGraphRuntime _runtime;
        private StateGraphTransaction _transaction;
        private StateGraphTemporalController _temporal;
        private bool _hasStoppedInstance;
        private bool _isDisposed;
        private CoCoDiagnostic _lastDiagnostic;
        private int _lastAutomaticFrame = -1;
        private bool _reliableOverflowPending;
        private bool _acceptsEventInput;
        private bool _isStarting;
        private bool _isAdvancing;
        private bool _isTemporalOperation;
        private bool _isPublishingCommittedEvents;
        private bool _destroyRequested;
        private bool _stopAfterPublish;
        private bool _disposeAfterPublish;
        private bool _requiresWorldCorrection;
        private ulong _inputAuthorityRevision = 1UL;
        private CommitGuard _commitGuard;

        public StateGraphAsset StateGraphAsset => stateGraphAsset;
        internal StateGraphDriver Driver => driver;
        internal bool AutoStart => autoStart;
        internal float TimeScale => timeScale;
        internal IReadOnlyList<MonoBehaviour> IntentSources =>
            intentSources ?? Array.Empty<MonoBehaviour>();
        internal IReadOnlyList<MonoBehaviour> EventAdapters =>
            eventAdapters ?? Array.Empty<MonoBehaviour>();
        internal IReadOnlyList<MonoBehaviour> Operators => operators ?? Array.Empty<MonoBehaviour>();
        internal MonoBehaviour ActorContextBinding => actorContextBinding;
        internal MonoBehaviour ContextRestoreBinding => contextRestoreBinding;
        internal int ContextFrameCapacity => contextFrameCapacity;
        internal int EventOutboxCapacity => eventOutboxCapacity;
        internal int TraceCapacity => traceCapacity;
        internal bool HasLiveRuntime => _runtime != null;
        internal bool IsRuntimeFaulted => _runtime?.IsFaulted ?? false;
        public int TemporalHistoryCapacity => temporalHistoryCapacity;
        public RuntimeLifecycleState Lifecycle => _runtime?.Lifecycle ??
            (_isDisposed
                ? RuntimeLifecycleState.Disposed
                : _hasStoppedInstance
                    ? RuntimeLifecycleState.Stopped
                    : RuntimeLifecycleState.Created);
        public RuntimeFault Fault => _runtime?.Fault ?? default;
        public GraphInstanceId GraphInstanceId => _runtime?.GraphInstanceId ?? default;
        public IReadOnlyList<ActivePath> ActivePaths => _runtime?.ActivePaths ?? Array.Empty<ActivePath>();
        public ContextFrame CurrentContext => _transaction?.CurrentContext ?? default;
        public IStateFlowTrace Trace => _transaction?.Trace;
        public bool RequiresWorldCorrection => _requiresWorldCorrection;
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;
        public ulong InputAuthorityRevision => _inputAuthorityRevision;
        public TemporalState TemporalState => _temporal?.State ??
            new TemporalState(
                TemporalMode.Disabled,
                temporalHistoryCapacity < 0 ? 0 : temporalHistoryCapacity,
                0,
                0,
                default,
                default,
                0UL,
                false);

        internal bool CanAcceptEventInput =>
            _acceptsEventInput &&
            _runtime != null &&
            (_runtime.Lifecycle == RuntimeLifecycleState.Running ||
             _runtime.Lifecycle == RuntimeLifecycleState.Suspended) &&
            (!_runtime.IsFaulted ||
             (_temporal != null && _temporal.Mode == TemporalMode.Previewing)) &&
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
            if (driver == StateGraphDriver.Update)
            {
                TryAutomaticStep(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (driver == StateGraphDriver.FixedUpdate)
            {
                TryAutomaticStep(Time.fixedDeltaTime);
            }
        }

        private void OnDestroy()
        {
            _acceptsEventInput = false;
            if (_isStarting || _isAdvancing || _isTemporalOperation)
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
                    "StateGraphHost requires one StateGraph Asset.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            IStateGraphProjectBindingProvider provider =
                StateGraphProjectBindings.Provider;
            if (provider == null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "No StateGraph project binding provider was installed.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!IsPositiveFinite(timeScale) ||
                !Enum.IsDefined(typeof(StateGraphDriver), driver) ||
                temporalHistoryCapacity < 0 ||
                contextFrameCapacity < 2 ||
                eventOutboxCapacity < 0 ||
                traceCapacity < 0)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "Host Driver, TimeScale, Temporal, Context, Outbox, and Trace capacities are invalid.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!TryValidateEventAdapterBoundaries(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (provider is StandardBindingProvider && stateGraphAsset.EventAdapterDeclarations.Count != 0)
            {
                diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                    "Remove the old Event-to-Intent declarations from this graph and read events through State Inbox.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            StateGraphAssetCompileResult compileResult;
            StateGraphAsset configuredGraph = null;
            try
            {
                configuredGraph = ConfiguredGraph(ref provider);
                compileResult = new StateGraphAssetCompiler().Compile(
                    configuredGraph,
                    provider.Catalog);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "StateGraph Asset compilation threw during Host setup.");
                _lastDiagnostic = diagnostic;
                return false;
            }
            finally
            {
                // Ownership transfers here only after ConfiguredGraph returns.
                ReleaseConfiguredGraph(configuredGraph);
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

            GraphInstanceId graphInstanceId = StateGraphHostIdentity.Next();
            var builder = new StateGraphHostBindingBuilder(
                compileResult.Graph,
                graphInstanceId,
                this);
            StateGraphHostRuntimeBindings bindings = null;
            StateGraphRuntime runtime = null;
            StateGraphTransaction transaction = null;
            StateGraphTemporalController temporal = null;
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

                if (!StateGraphTemporalController.TryValidateConfiguration(
                        this,
                        bindings.ContextLayout,
                        bindings.ContextCodecs,
                        contextRestoreBinding,
                        temporalHistoryCapacity,
                        out diagnostic))
                {
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!StateGraphTransaction.TryPreflight(
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

                FreezeOperatorRecovery(bindings.ContextLayout);
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
                        out TimelineId timelineId,
                        out diagnostic) ||
                    !ClockDomainId.TryCreate(
                        (ulong)driver + 1UL,
                        out ClockDomainId clockDomainId) ||
                    !ActorClock.TryCreate(
                        timelineId,
                        clockDomainId,
                        new TimelineEpoch(0UL),
                        graphInstanceId,
                        out ActorClock clock,
                        out diagnostic))
                {
                    transaction.Dispose();
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!StateGraphRuntime.TryCreate(
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

                if (!StateGraphTemporalController.TryCreate(
                        this,
                        bindings.ContextLayout,
                        bindings.ContextCodecs,
                        transaction,
                        contextRestoreBinding,
                        temporalHistoryCapacity,
                        out temporal,
                        out diagnostic))
                {
                    transaction.Dispose();
                    runtime.Dispose();
                    bindings.Dispose();
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    temporal.Dispose();
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
                _temporal = temporal;
                _commitGuard = commitGuard;

                _requiresWorldCorrection = false;
                _reliableOverflowPending = false;
                _lastAutomaticFrame = -1;

                // Registration is deliberately last: no packet can reach a partially started Host.
                StartStateEvents(provider, compileResult.Graph);
                _acceptsEventInput = true;
                _hasStoppedInstance = false;
                AdvanceInputAuthorityRevision();
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (runtime != null && !ReferenceEquals(_runtime, runtime))
                {
                    runtime.Dispose();
                }

                if (temporal != null && !ReferenceEquals(_temporal, temporal))
                {
                    temporal.Dispose();
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

            bool succeeded = TrySuspendCore(true, out diagnostic);
            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        private bool TrySuspendCore(
            bool checkPendingOverflow,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_runtime == null ||
                _runtime.IsFaulted ||
                (_temporal != null && _temporal.Mode == TemporalMode.Previewing))
            {
                diagnostic = LifecycleError("Only a healthy Running Host can suspend.");
                return false;
            }

            if (checkPendingOverflow && LatchPendingOverflow(out diagnostic))
            {
                return false;
            }

            if (!_runtime.TrySuspend(out diagnostic))
            {
                return false;
            }

            AdvanceInputAuthorityRevision();
            _transaction?.Suspend();

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryResume(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            bool succeeded = TryResumeCore(true, out diagnostic);
            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        private bool TryResumeCore(
            bool checkPendingOverflow,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_runtime == null ||
                _runtime.IsFaulted ||
                (checkPendingOverflow && LatchPendingOverflow(out diagnostic)))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = LifecycleError("A Faulted or missing Host cannot resume.");
                }

                return false;
            }

            if (!_runtime.TryResume(out diagnostic))
            {
                return false;
            }

            AdvanceInputAuthorityRevision();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryStop(out CoCoDiagnostic diagnostic)
        {
            if (_isPublishingCommittedEvents)
            {
                if (_runtime == null ||
                    (_runtime.Lifecycle != RuntimeLifecycleState.Running &&
                     _runtime.Lifecycle != RuntimeLifecycleState.Suspended))
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
                (_runtime.Lifecycle != RuntimeLifecycleState.Running &&
                 _runtime.Lifecycle != RuntimeLifecycleState.Suspended))
            {
                diagnostic = LifecycleError("Host has no live Graph instance to stop.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            // Close the Inbox before stopping the instance.
            _acceptsEventInput = false;
            if (!_runtime.TryStop(out diagnostic))
            {
                _acceptsEventInput = true;
                _lastDiagnostic = diagnostic;
                return false;
            }

            AdvanceInputAuthorityRevision();
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

        internal bool TryCaptureDebugSnapshot(
            out StateGraphHostDebugSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (_runtime == null ||
                _transaction == null ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents ||
                (_temporal != null && _temporal.Mode == TemporalMode.Previewing))
            {
                diagnostic = LifecycleError(
                    "Debugger snapshot capture requires one live idle committed Host boundary.");
                return false;
            }

            if (!_runtime.TryCaptureCommittedDebugState(
                    out StateGraphCommittedDebugState graphState,
                    out diagnostic) ||
                !_transaction.TryCaptureCommittedDebugState(
                    out ContextFrame context,
                    out diagnostic))
            {
                return false;
            }

            if (context.IsAlive)
            {
                StateFlowFrameHeader header = context.Header;
                TickFrame tickFrame = header.TickFrame;
                if (!header.IsValid ||
                    header.Identity.GraphInstanceId != graphState.GraphInstanceId ||
                    tickFrame.TimelineId != graphState.TimelineId ||
                    tickFrame.ClockDomainId != graphState.ClockDomainId ||
                    tickFrame.TimelineEpoch != graphState.TimelineEpoch ||
                    tickFrame.Tick != graphState.Tick ||
                    tickFrame.ExecutionSequence != graphState.ExecutionSequence ||
                    !tickFrame.TimelinePosition.Seconds.Equals(graphState.Seconds))
                {
                    diagnostic = LifecycleError(
                        "Committed Graph, Clock, and Context identities were not one atomic debugger boundary.");
                    return false;
                }
            }

            snapshot = StateGraphHostDebugSnapshot.CopyFrom(
                graphState,
                _runtime.Lifecycle,
                _runtime.Fault,
                _requiresWorldCorrection,
                _lastDiagnostic,
                context);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Captures one immutable committed debugger snapshot together with a
        /// logical-depth copy of the Temporal ring metadata. No retained
        /// payload or mutable Context handle crosses this boundary.
        /// </summary>
        internal bool TryCaptureTemporalDebugSnapshot(
            out StateGraphHostTemporalDebugSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (!TryCaptureDebugSnapshot(
                    out StateGraphHostDebugSnapshot current,
                    out diagnostic))
            {
                return false;
            }

            int capacity;
            TemporalFrameInfo[] frames;
            if (_temporal == null)
            {
                capacity = temporalHistoryCapacity < 0
                    ? 0
                    : temporalHistoryCapacity;
                frames = Array.Empty<TemporalFrameInfo>();
            }
            else if (!_temporal.TryCaptureDebugFrameInfos(
                         out capacity,
                         out frames))
            {
                diagnostic = LifecycleError(
                    "Temporal debugger metadata changed outside one committed Host boundary.");
                return false;
            }

            if (frames.Length > 0 &&
                (current.ContextHeader.Identity.GraphInstanceId !=
                 frames[0].GraphInstanceId ||
                 current.ContextHeader.TickFrame != frames[0].TickFrame ||
                 current.ContextRevision != frames[0].Revision))
            {
                diagnostic = LifecycleError(
                    "Current Context and Temporal ring head were not one atomic debugger boundary.");
                return false;
            }

            snapshot = new StateGraphHostTemporalDebugSnapshot(
                current,
                capacity,
                frames);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>
        /// Validates and decodes only the source-frame metadata in an existing
        /// persistence payload. It never imports the payload and never updates
        /// Host diagnostics or lifecycle state.
        /// </summary>
        internal bool TryDecodePersistenceDebugFrame(
            byte[] payload,
            out TemporalFrameInfo frame,
            out CoCoDiagnostic diagnostic)
        {
            frame = default;
            if (payload == null ||
                payload.Length == 0 ||
                _runtime == null ||
                _bindings == null ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents)
            {
                diagnostic = LifecycleError(
                    "Persistence debugger decoding requires one live idle Host and one existing payload.");
                return false;
            }

            try
            {
                return StateGraphPersistencePayloadCodec.TryCreate(
                           _runtime.Graph.GraphId,
                           _bindings.ContextLayout,
                           _bindings.ContextCodecs,
                           out StateGraphPersistencePayloadCodec codec,
                           out diagnostic) &&
                       codec.TryDecode(
                           payload,
                           out StateGraphPersistenceEnvelope envelope,
                           out ProjectionRestoreSource source,
                           out diagnostic) &&
                       StateGraphPersistencePayloadCodec.TryCreatePersistedSourceInfo(
                           envelope,
                           source,
                           out frame,
                           out diagnostic);
            }
            catch (Exception)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "Persistence debugger decoding rejected an unreadable payload.");
                return false;
            }
        }
#endif

        internal bool TryDebugStepWhileSuspended(
            double deltaTime,
            out CoCoDiagnostic diagnostic)
        {
            if (_runtime == null ||
                _transaction == null ||
                _runtime.Lifecycle != RuntimeLifecycleState.Suspended ||
                _runtime.IsFaulted ||
                !IsPositiveFinite(deltaTime) ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents ||
                (_temporal != null && _temporal.Mode == TemporalMode.Previewing) ||
                _requiresWorldCorrection)
            {
                diagnostic = LifecycleError(
                    "Debug Step requires one healthy idle Suspended Host and a positive finite DeltaTime.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            _isAdvancing = true;
            try
            {
                if (!TryResumeCore(false, out diagnostic))
                {
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                bool advanced = TryAdvanceCore(deltaTime, out diagnostic);
                CoCoDiagnostic advanceDiagnostic = diagnostic;
                bool canReturnToSuspended =
                    _runtime != null &&
                    _runtime.Lifecycle == RuntimeLifecycleState.Running &&
                    !_runtime.IsFaulted &&
                    !_destroyRequested &&
                    !_stopAfterPublish &&
                    !_disposeAfterPublish &&
                    !_requiresWorldCorrection;
                if (!canReturnToSuspended)
                {
                    if (advanced)
                    {
                        diagnostic = LifecycleError(
                            "Debug Step committed, but deferred lifecycle or fault handling prevented a healthy Suspended return.");
                        _lastDiagnostic = diagnostic;
                    }

                    return false;
                }

                if (!TrySuspendCore(true, out CoCoDiagnostic suspendDiagnostic))
                {
                    if (!_runtime.IsFaulted)
                    {
                        CoCoDiagnostic synchronizationFailure = suspendDiagnostic.IsError
                            ? suspendDiagnostic
                            : LifecycleError(
                                "Debug Step could not synchronize Runtime back to Suspended.");
                        _runtime.TryLatchExternalFault(synchronizationFailure);
                    }

                    diagnostic = _runtime.IsFaulted
                        ? _runtime.Fault.Diagnostic
                        : suspendDiagnostic;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!advanced)
                {
                    diagnostic = advanceDiagnostic;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
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

        public bool TryStep(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (driver != StateGraphDriver.Manual)
            {
                diagnostic = LifecycleError("Public Manual Step requires the Host Manual driver.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            return TryAdvance(deltaTime, out diagnostic);
        }

        public bool TryBeginTemporalPreview(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Begin, 0, out diagnostic);

        public bool TryPreviewTemporal(
            int historyDepth,
            out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Preview, historyDepth, out diagnostic);

        public bool TryConfirmTemporalRestore(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Confirm, 0, out diagnostic);

        public bool TryCancelTemporalPreview(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Cancel, 0, out diagnostic);

        public bool TryCorrectWorld(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Correct, 0, out diagnostic);

        public bool TryValidateRestore(
            ContextFrame source,
            TickFrame resumedTickFrame,
            out ContextCommitStatus status)
        {
            if (_transaction == null)
            {
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            return _transaction.TryValidateRestore(
                _runtime,
                source,
                resumedTickFrame,
                out status);
        }

        internal bool TryPrepareRestore(
            ContextFrame source,
            TickFrame resumedTickFrame,
            out PreparedActorRestore preparedRestore,
            out ContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            if (_transaction == null || _runtime == null)
            {
                preparedRestore = default;
                status = ContextCommitStatus.InvalidPreparation;
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

        internal bool TryCapturePersistencePayload(
            out byte[] payload,
            out CoCoDiagnostic diagnostic)
        {
            payload = null;
            if (_isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents ||
                _runtime == null ||
                _transaction == null ||
                _bindings == null ||
                _temporal == null ||
                _runtime.IsFaulted ||
                (_runtime.Lifecycle != RuntimeLifecycleState.Running &&
                 _runtime.Lifecycle != RuntimeLifecycleState.Suspended) ||
                _temporal.Mode == TemporalMode.Previewing ||
                !_transaction.CurrentContext.IsAlive)
            {
                diagnostic = LifecycleError(
                    "StateGraph Persistence capture requires one idle healthy Host with a committed ContextFrame.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            bool succeeded;
            _isTemporalOperation = true;
            try
            {
                try
                {
                    succeeded =
                        StateGraphPersistencePayloadCodec.TryCreate(
                            _runtime.Graph.GraphId,
                            _bindings.ContextLayout,
                            _bindings.ContextCodecs,
                            out StateGraphPersistencePayloadCodec codec,
                            out diagnostic) &&
                        codec.TryEncode(
                            _transaction.CurrentContext,
                            out payload,
                            out diagnostic);
                }
                catch (Exception)
                {
                    succeeded = false;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Restore,
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        "StateGraph Persistence capture threw inside its synchronous Durable codec boundary.");
                }
            }
            finally
            {
                _isTemporalOperation = false;
            }

            if (_destroyRequested)
            {
                _destroyRequested = false;
                _stopAfterPublish = false;
                _disposeAfterPublish = false;
                ForceDisposeHost();
                payload = null;
                diagnostic = LifecycleError(
                    "Unity destruction cancelled StateGraph Persistence capture before publication.");
                succeeded = false;
            }

            if (!succeeded || diagnostic.IsError)
            {
                payload = null;
                _lastDiagnostic = diagnostic;
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryApplyPersistencePayload(
            byte[] payload,
            out CoCoDiagnostic diagnostic)
        {
            if (payload == null ||
                payload.Length == 0 ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents)
            {
                diagnostic = LifecycleError(
                    "StateGraph Persistence import requires one idle Host and one non-empty payload.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_runtime == null ||
                _transaction == null ||
                _bindings == null ||
                _temporal == null)
            {
                diagnostic = LifecycleError(
                    "StateGraph Persistence import requires one live Host instance.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!StateGraphPersistencePayloadCodec.TryCreate(
                    _runtime.Graph.GraphId,
                    _bindings.ContextLayout,
                    _bindings.ContextCodecs,
                    out StateGraphPersistencePayloadCodec codec,
                    out diagnostic) ||
                !codec.TryDecode(
                    payload,
                    out StateGraphPersistenceEnvelope envelope,
                    out ProjectionRestoreSource persistedSource,
                    out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            AdvanceInputAuthorityRevision();
            bool succeeded;
            _isTemporalOperation = true;
            try
            {
                succeeded = _temporal.TryImportPersistence(
                    _runtime,
                    codec,
                    envelope,
                    persistedSource,
                    out diagnostic);
            }
            finally
            {
                _isTemporalOperation = false;
            }

            if (_destroyRequested)
            {
                _destroyRequested = false;
                _stopAfterPublish = false;
                _disposeAfterPublish = false;
                ForceDisposeHost();
                diagnostic = LifecycleError(
                    "Unity destruction cancelled StateGraph Persistence import before publication.");
                succeeded = false;
            }

            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        public InboxEnqueueResult TryEnqueueLocal<TEvent>(
            in EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!CanAcceptEventInput || _bindings == null ||
                _stateInbox?.Find<TEvent>() == null)
            {
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (!packet.IsValid ||
                packet.Envelope.SourceGraphInstanceId != GraphInstanceId)
            {
                return InboxEnqueueResult.InvalidPacket;
            }

            InboxEnqueueResult result = TryReceiveStatePacket(in packet);
            if (result == InboxEnqueueResult.ReliableOverflowFaultRequired)
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

        internal bool IsTemporalOperationCancellationRequested => _destroyRequested;

        internal void LatchWorldCorrectionFault(CoCoDiagnostic diagnostic)
        {
            _requiresWorldCorrection = true;
            CoCoDiagnostic reason = diagnostic.IsError
                ? diagnostic
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.WorldCorrectionRequired,
                    "Temporal Unity projection failed and requires correction from current authority.");
            _runtime?.TryLatchExternalFault(reason);
        }

        internal void ClearWorldCorrectionRequirementNoFail()
        {
            _requiresWorldCorrection = false;
        }

        private void TryAutomaticStep(float deltaTime)
        {
            if (_runtime == null ||
                _runtime.Lifecycle != RuntimeLifecycleState.Running ||
                _runtime.IsFaulted ||
                (_temporal != null && _temporal.Mode == TemporalMode.Previewing) ||
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
            if (_isStarting || _isAdvancing || _isTemporalOperation)
            {
                diagnostic = LifecycleError(
                    "StateGraph Host cannot Step during startup, Temporal projection, or its active Tick.");
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
                _runtime.Lifecycle != RuntimeLifecycleState.Running ||
                _runtime.IsFaulted ||
                (_temporal != null && _temporal.Mode == TemporalMode.Previewing))
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

            TickFrame tickFrame = default;
            StagedGraphStep stagedStep = default;
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
                        out ContextCommitStatus contextStatus,
                        out diagnostic))
                {
                    // Capacity exhaustion occurs before Inbox seal and is intentionally retryable.
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _stateInbox.Seal(tickFrame);
                if (!_bindings.TryCollectIntents(
                        tickFrame,
                        out IIntentFrame intents,
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
                _temporal,
                stagedStep,
                _commitGuard,
                out bool authorityCommitted,
                out bool worldMayBeDirty,
                out diagnostic);
            _temporal.DrainPublishedCleanupNoFail();
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
                RecoveryProjection = null;
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
            in StagedGraphStep stagedStep,
            in TickFrame tickFrame,
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
            if (!_isStarting && !_isAdvancing && !_isTemporalOperation)
            {
                diagnostic = CoCoDiagnostic.None;
                return false;
            }

            diagnostic = LifecycleError(
                "StateGraph Host lifecycle cannot change while startup or a Tick is advancing.");
            _lastDiagnostic = diagnostic;
            return true;
        }

        private bool TryRunTemporalOperation(
            TemporalOperation operation,
            int historyDepth,
            out CoCoDiagnostic diagnostic)
        {
            if (_isStarting || _isAdvancing || _isTemporalOperation ||
                _isPublishingCommittedEvents)
            {
                diagnostic = LifecycleError(
                    "Temporal control requires one idle Host boundary and cannot reenter project callbacks.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_temporal == null || _runtime == null || _transaction == null)
            {
                diagnostic = LifecycleError(
                    "Temporal control requires one live StateGraph Host instance.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (operation != TemporalOperation.Correct &&
                LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            AdvanceInputAuthorityRevision(clearStateInbox: false);
            bool succeeded;
            _isTemporalOperation = true;
            try
            {
                switch (operation)
                {
                    case TemporalOperation.Begin:
                        succeeded = _temporal.TryBegin(_runtime, out diagnostic);
                        break;
                    case TemporalOperation.Preview:
                        succeeded = _temporal.TryPreview(
                            _runtime,
                            historyDepth,
                            out diagnostic);
                        break;
                    case TemporalOperation.Confirm:
                        succeeded = _temporal.TryConfirm(_runtime, out diagnostic);
                        break;
                    case TemporalOperation.Cancel:
                        succeeded = _temporal.TryCancel(_runtime, out diagnostic);
                        break;
                    case TemporalOperation.Correct:
                        succeeded = _temporal.TryCorrectWorld(
                            _runtime,
                            _requiresWorldCorrection,
                            out diagnostic);
                        break;
                    default:
                        succeeded = false;
                        diagnostic = LifecycleError("Temporal operation is not defined.");
                        break;
                }

                if (succeeded)
                {
                    _stateInbox?.Clear();
                }
            }
            finally
            {
                _isTemporalOperation = false;
            }

            bool destroyRequested = _destroyRequested;
            if (destroyRequested)
            {
                _destroyRequested = false;
                _stopAfterPublish = false;
                _disposeAfterPublish = false;
                ForceDisposeHost();
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the Temporal operation before publication.");
                succeeded = false;
            }

            _lastDiagnostic = diagnostic;
            return succeeded;
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
            if (_runtime != null &&
                (_runtime.Lifecycle == RuntimeLifecycleState.Running ||
                 _runtime.Lifecycle == RuntimeLifecycleState.Suspended))
            {
                AdvanceInputAuthorityRevision();
            }

            _acceptsEventInput = false;
            _stateEventAgent?.UnsubscribeAll();
            _stateEventAgent = null;
            _stateInbox = null;
            _temporal?.Dispose();
            _temporal = null;
            _transaction?.Dispose();
            _transaction = null;
            _bindings?.Dispose();
            _bindings = null;
            _runtime?.Dispose();
            _runtime = null;
            _recovery = Array.Empty<OperatorRecovery>();
            _retainPersistence = _retainTemporal = null;
            RecoveryProjection = null;
            _isPublishingCommittedEvents = false;
            _stopAfterPublish = false;
            _disposeAfterPublish = false;
            _reliableOverflowPending = false;
            _requiresWorldCorrection = false;
            _lastAutomaticFrame = -1;
        }

        private void DisposeHostFromLegalState()
        {
            if (_isDisposed)
            {
                return;
            }

            _acceptsEventInput = false;
            DisposeInstance();
            _hasStoppedInstance = false;
            _isDisposed = true;
            AdvanceInputAuthorityRevision();
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
            if (_runtime != null &&
                (_runtime.Lifecycle == RuntimeLifecycleState.Running ||
                 _runtime.Lifecycle == RuntimeLifecycleState.Suspended))
            {
                if (!_runtime.TryStop(out _))
                {
                    _runtime.Dispose();
                }

                AdvanceInputAuthorityRevision();
            }

            DisposeInstance();
            _hasStoppedInstance = false;
            _isDisposed = true;
            AdvanceInputAuthorityRevision();
        }

        private void AdvanceInputAuthorityRevision(bool clearStateInbox = true)
        {
            if (clearStateInbox)
            {
                _stateInbox?.Clear();
            }
            _inputAuthorityRevision =
                _inputAuthorityRevision == ulong.MaxValue
                    ? 1UL
                    : _inputAuthorityRevision + 1UL;
        }

        private static bool TryCreateTimelineId(
            GraphId graphId,
            GraphInstanceId graphInstanceId,
            out TimelineId timelineId,
            out CoCoDiagnostic diagnostic)
        {
            if (!graphId.IsValid ||
                !graphInstanceId.IsValid ||
                !TimelineId.TryCreate(
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

        private static CoCoDiagnostic FirstCompileError(StateGraphAssetCompileResult result)
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

        private enum TemporalOperation
        {
            Begin = 0,
            Preview = 1,
            Confirm = 2,
            Cancel = 3,
            Correct = 4
        }

        private sealed class CommitGuard : IStateGraphCommitGuard
        {
            private readonly StateGraphHost _host;

            public CommitGuard(StateGraphHost host)
            {
                _host = host;
            }

            public bool IsCommitCancellationRequested => _host._destroyRequested;
        }
    }

    internal static class StateGraphHostIdentity
    {
        private static ulong _nextValue = 1UL;

        public static GraphInstanceId Next()
        {
            ulong value = _nextValue++;
            if (value == 0UL)
            {
                value = _nextValue++;
            }

            GraphInstanceId.TryCreate(value, out GraphInstanceId id);
            return id;
        }

        public static void Reset()
        {
            _nextValue = 1UL;
        }
    }
}
