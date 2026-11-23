using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal readonly struct PreparedActorRestore
    {
        private readonly StateGraphTransaction _transaction;
        private readonly ulong _token;

        internal PreparedActorRestore(
            StateGraphTransaction transaction,
            ulong token)
        {
            _transaction = transaction;
            _token = token;
        }

        internal bool IsValid =>
            _transaction != null &&
            _transaction.IsPreparedRestoreTokenCurrent(_token);

        internal void CommitNoFail()
        {
            _transaction.CommitPreparedRestoreNoFail(_token);
        }

        internal bool Cancel() =>
            _transaction != null && _transaction.CancelPreparedRestore(_token);
    }

    internal sealed class StateGraphTransaction :
        IEventOutboxSink,
        ICommittedEventPublisher,
        IStateEventOutbox,
        IDisposable
    {
        private readonly GraphInstanceId _graphInstanceId;
        private readonly StateGraphHost _host;
        private readonly ContextFrameArena _contextArena;
        private readonly StateGraphContextRuntime _contextRuntime;
        private readonly StateGraphOperatorRuntime _operators;
        private readonly IEventOutboxLane[] _outboxLanes;
        private readonly OutboxLedgerEntry[] _outboxLedger;
        private readonly List<IPendingStateEvent> _stateEvents = new List<IPendingStateEvent>();
        private readonly Dictionary<Type, IStateEventPool> _stateEventPools = new Dictionary<Type, IStateEventPool>();
        private readonly StateFlowTraceBuffer _trace;
        private PreparedContextCommit _preparedContext;
        private FinalizedContextCommit _preparedRestoreContext;
        private PreparedGraphRestore _preparedGraphRestore;
        private TickFrame _activeTickFrame;
        private StateFlowTraceFrameReference _activePreviousContextTrace;
        private ulong _nextTransactionToken;
        private ulong _activeToken;
        private ulong _nextRestoreToken;
        private ulong _activeRestoreToken;
        private ulong _lastEventSequence;
        private int _outboxLedgerCount;
        private bool _outboxWriteFault;
        private bool _stagedTraceAppended;
        private bool _isDisposed;

        private StateGraphTransaction(
            StateGraphHost host,
            GraphInstanceId graphInstanceId,
            ContextFrameArena contextArena,
            StateGraphContextRuntime contextRuntime,
            StateGraphOperatorRuntime operators,
            IEventOutboxLane[] outboxLanes,
            int outboxCapacity,
            int traceCapacity)
        {
            _host = host;
            _graphInstanceId = graphInstanceId;
            _contextArena = contextArena;
            _contextRuntime = contextRuntime;
            _operators = operators;
            _outboxLanes = outboxLanes;
            _outboxLedger = outboxCapacity == 0
                ? Array.Empty<OutboxLedgerEntry>()
                : new OutboxLedgerEntry[outboxCapacity];
            _trace = traceCapacity == 0 ? null : new StateFlowTraceBuffer(traceCapacity);
        }

        internal ContextFrame CurrentContext => _contextArena.Current;
        internal ContextFrameReadView PreviousContext => _contextArena.Previous;
        internal IStateFlowTrace Trace => _trace;
        internal int CandidateEventCount => _outboxLedgerCount + _stateEvents.Count;
        internal bool HasOutboxFailure => _outboxWriteFault;

        internal bool TryCaptureCommittedDebugState(
            out ContextFrame context,
            out CoCoDiagnostic diagnostic)
        {
            context = default;
            if (_isDisposed || _activeToken != 0UL || _activeRestoreToken != 0UL)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "Committed debugger state requires one idle Context transaction.");
                return false;
            }

            context = _contextArena.Current;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryCreate(
            StateGraphHost host,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout contextLayout,
            OperationFrame operations,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out StateGraphTransaction transaction,
            out CoCoDiagnostic diagnostic) =>
            TryPreflight(
                host,
                graph,
                graphInstanceId,
                contextLayout,
                operations,
                Array.Empty<IGraphContextProducerBinding>(),
                contextFrameCapacity,
                eventOutboxCapacity,
                traceCapacity,
                out transaction,
                out diagnostic);

        internal static bool TryCreate(
            StateGraphHost host,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout contextLayout,
            OperationFrame operations,
            IReadOnlyList<IGraphContextProducerBinding> contextProducers,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out StateGraphTransaction transaction,
            out CoCoDiagnostic diagnostic) =>
            TryPreflight(
                host,
                graph,
                graphInstanceId,
                contextLayout,
                operations,
                contextProducers,
                contextFrameCapacity,
                eventOutboxCapacity,
                traceCapacity,
                out transaction,
                out diagnostic);

        // This seam must run before StateGraphRuntime.TryCreate. It validates the
        // complete Operator transaction surface without invoking State factory,
        // Memory reset, or Memory fingerprint callbacks. On success, the returned
        // transaction owns all preallocated resources and is reused by the Host.
        internal static bool TryPreflight(
            StateGraphHost host,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout contextLayout,
            OperationFrame operations,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out StateGraphTransaction transaction,
            out CoCoDiagnostic diagnostic) =>
            TryPreflight(
                host,
                graph,
                graphInstanceId,
                contextLayout,
                operations,
                Array.Empty<IGraphContextProducerBinding>(),
                contextFrameCapacity,
                eventOutboxCapacity,
                traceCapacity,
                out transaction,
                out diagnostic);

        internal static bool TryPreflight(
            StateGraphHost host,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout contextLayout,
            OperationFrame operations,
            IReadOnlyList<IGraphContextProducerBinding> contextProducers,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out StateGraphTransaction transaction,
            out CoCoDiagnostic diagnostic)
        {
            transaction = null;
            if (host == null ||
                graph == null ||
                !graphInstanceId.IsValid ||
                contextLayout == null ||
                operations == null ||
                contextProducers == null ||
                contextFrameCapacity < 2 ||
                eventOutboxCapacity < 0 ||
                traceCapacity < 0)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    "StateGraph transaction capacities and Runtime dependencies are invalid.");
                return false;
            }

            ContextFrameArena arena = null;
            StateGraphContextRuntime contextRuntime = null;
            StateGraphOperatorRuntime operatorRuntime = null;
            try
            {
                arena = new ContextFrameArena(
                    graphInstanceId,
                    contextLayout,
                    contextFrameCapacity);
                if (!StateGraphContextRuntime.TryCreate(
                        host,
                        graph,
                        graphInstanceId,
                        contextLayout,
                        contextProducers,
                        host.ActorContextBinding,
                        out contextRuntime,
                        out diagnostic) ||
                    !StateGraphOperatorRuntime.TryCreate(
                        host,
                        graph,
                        graphInstanceId,
                        contextLayout,
                        operations.Registry,
                        host.Operators,
                        contextRuntime.ClaimSlots,
                        out operatorRuntime,
                        out diagnostic) ||
                    !operatorRuntime.TryCreateOutboxLanes(
                        eventOutboxCapacity,
                        out IEventOutboxLane[] lanes,
                        out diagnostic))
                {
                    operatorRuntime?.Dispose();
                    contextRuntime?.Dispose();
                    arena.Dispose();
                    return false;
                }

                transaction = new StateGraphTransaction(
                    host,
                    graphInstanceId,
                    arena,
                    contextRuntime,
                    operatorRuntime,
                    lanes,
                    eventOutboxCapacity,
                    traceCapacity);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception)
            {
                operatorRuntime?.Dispose();
                contextRuntime?.Dispose();
                arena?.Dispose();
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "StateGraph transaction setup threw before Runtime publication.");
                return false;
            }
        }

        internal bool TryPrepareContext(
            in TickFrame tickFrame,
            out ContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _activeToken != 0UL ||
                _activeRestoreToken != 0UL ||
                !tickFrame.IsValid ||
                _nextTransactionToken == ulong.MaxValue)
            {
                status = ContextCommitStatus.InvalidPreparation;
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Context transaction could not begin a fresh candidate Tick.");
                return false;
            }

            if (!_contextArena.HasAvailableCapacity)
            {
                status = ContextCommitStatus.CapacityExhausted;
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "ContextFrame arena has no reusable cell; release retained frames before retrying this Tick.");
                return false;
            }

            if (!_contextArena.TryPrepare(tickFrame, out _preparedContext, out status))
            {
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "ContextFrame arena rejected the candidate Tick.");
                return false;
            }

            _activePreviousContextTrace = default;
            if (_trace != null &&
                !StateFlowTraceFrameReference.TryCreate(
                    _contextArena.Previous,
                    out _activePreviousContextTrace))
            {
                _preparedContext.Cancel();
                _preparedContext = default;
                status = ContextCommitStatus.InvalidPreparation;
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Trace could not capture the exact Previous Context identity.");
                return false;
            }

            _nextTransactionToken++;
            _activeToken = _nextTransactionToken;
            _activeTickFrame = tickFrame;
            _stagedTraceAppended = false;
            ResetOutbox();
            _trace?.Append(StateFlowTraceEntry.Tick(
                _graphInstanceId,
                tickFrame,
                _activePreviousContextTrace));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void AppendStagedTrace(
            StateGraphRuntime runtime,
            in StagedGraphStep stagedStep)
        {
            if (_stagedTraceAppended || runtime == null || _activeToken == 0UL ||
                !stagedStep.IsValid || stagedStep.TickFrame != _activeTickFrame)
            {
                return;
            }

            runtime.AppendStagedTransitionTrace(
                _trace,
                stagedStep,
                _activePreviousContextTrace);
            AppendStagedOperationTrace(stagedStep);
            _stagedTraceAppended = true;
        }

        internal bool TryFinalizeAndCommit(
            StateGraphRuntime runtime,
            StateGraphHostRuntimeBindings bindings,
            StateGraphTemporalController temporal,
            in StagedGraphStep stagedStep,
            IStateGraphCommitGuard commitGuard,
            out bool authorityCommitted,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            authorityCommitted = false;
            worldMayBeDirty = false;
            if (_isDisposed ||
                runtime == null ||
                bindings == null ||
                temporal == null ||
                _activeToken == 0UL ||
                !_preparedContext.IsValid ||
                !stagedStep.IsValid ||
                stagedStep.TickFrame != _activeTickFrame)
            {
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "StateGraph transaction token no longer matches its staged Tick.");
                CancelCandidate(diagnostic);
                return false;
            }

            AppendStagedTrace(runtime, stagedStep);

            if (!runtime.TryCaptureGraphContext(
                    _contextRuntime,
                    stagedStep,
                    _contextArena.Previous,
                    _preparedContext,
                    _activeToken,
                    out diagnostic))
            {
                CancelCandidate(diagnostic);
                return false;
            }

            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the transaction after Graph Context capture.");
                CancelCandidate(diagnostic);
                return false;
            }

            if (!_operators.TryExecute(
                    stagedStep,
                    _contextArena.Previous,
                    _preparedContext,
                    this,
                    _activeToken,
                    commitGuard,
                    _trace,
                    out diagnostic))
            {
                worldMayBeDirty = _operators.WorldMayBeDirty;
                CancelCandidate(diagnostic);
                return false;
            }

            worldMayBeDirty = _operators.WorldMayBeDirty;
            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the transaction after Operator execution.");
                CancelCandidate(diagnostic);
                return false;
            }

            if (!_contextRuntime.TryCaptureActor(
                    stagedStep.TickFrame,
                    _contextArena.Previous,
                    _preparedContext,
                    _activeToken,
                    out bool actorMayBeDirty,
                    out diagnostic))
            {
                worldMayBeDirty |= actorMayBeDirty;
                CancelCandidate(diagnostic);
                return false;
            }

            worldMayBeDirty |= actorMayBeDirty;
            if (_outboxWriteFault)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.EventOutboxOverflow,
                    "EventOutbox capacity or its declared typed lane was exceeded.");
                CancelCandidate(diagnostic);
                return false;
            }

            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the Operator transaction before Context finalization.");
                CancelCandidate(diagnostic);
                return false;
            }

            FinalizedContextCommit finalizedContext;
            ContextCommitStatus contextStatus;
            try
            {
                if (!_preparedContext.TryFinalize(out finalizedContext, out contextStatus))
                {
                    diagnostic = ContextError(
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        contextStatus == ContextCommitStatus.DerivedRebuildFailed
                            ? "A derived Context StateSlot could not rebuild the candidate frame."
                            : "ContextFrame finalization rejected the candidate transaction.");
                    CancelCandidate(diagnostic);
                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "A derived Context StateSlot rebuilder threw during finalization.");
                CancelCandidate(diagnostic);
                return false;
            }

            if (!runtime.TryPrepareStagedCommit(
                    stagedStep,
                    commitGuard,
                    out PreparedGraphCommit preparedGraph,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            if (!TryPrepareEventSequenceRange(
                    out EventSequence firstEventSequence,
                    out EventSequence lastEventSequence,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            if (!finalizedContext.IsCommitReady ||
                !bindings.IsIntentTickCommitReady(stagedStep.TickFrame))
            {
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Context or Intent authority was no longer commit-ready during composite preflight.");
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            ContextRevision previousRevision = _contextArena.Current.Revision;
            StateFlowTraceFrameReference committedTraceFrame = default;
            if (_trace != null)
            {
                ulong nextRevisionValue = previousRevision.IsValid
                    ? previousRevision.Value + 1UL
                    : 1UL;
                if (!StateFlowTraceFrameReference.TryCreateCommitted(
                        _graphInstanceId,
                        _contextArena.Layout,
                        stagedStep.TickFrame,
                        new ContextRevision(nextRevisionValue),
                        out committedTraceFrame))
                {
                    diagnostic = ContextError(
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        "Trace could not preflight the committed Context identity.");
                    finalizedContext.Cancel();
                    CancelCandidate(diagnostic);
                    return false;
                }
            }

            ulong committedEventSequenceValue = _outboxLedgerCount == 0
                ? _lastEventSequence
                : lastEventSequence.Value;

            if (!temporal.TryPrepareCapture(
                    finalizedContext,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                temporal.CancelPreparedCapture();
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the transaction after Temporal projection capture.");
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            // D11 authority barrier. Every participant was validated above. These calls
            // contain no project callbacks, allocations, capacity checks, or failure branches.
            ContextFrame committedContext = finalizedContext.CommitNoFailUnchecked();
            preparedGraph.CommitNoFail();
            _operators.CommitClaimsNoFail();
            _lastEventSequence = committedEventSequenceValue;
            bindings.CommitIntentTickNoFail();
            temporal.PublishCaptureNoFail();
            authorityCommitted = true;
            _trace?.Append(StateFlowTraceEntry.Commit(
                _graphInstanceId,
                stagedStep.TickFrame,
                previousRevision,
                committedContext.Revision,
                _activePreviousContextTrace,
                committedTraceFrame));
            runtime.AppendCommittedPathTrace(
                _trace,
                stagedStep.TickFrame,
                committedTraceFrame);
            if (firstEventSequence.IsValid)
            {
                _trace?.Append(StateFlowTraceEntry.Sequence(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    firstEventSequence,
                    lastEventSequence,
                    committedTraceFrame));
            }

            bool published = PublishCommittedOutbox(
                stagedStep.TickFrame,
                firstEventSequence,
                committedTraceFrame,
                out diagnostic);
            ResetOutbox();
            ClearActiveTransaction();
            return published;
        }

        private void AppendStagedOperationTrace(
            in StagedGraphStep stagedStep)
        {
            if (_trace == null)
            {
                return;
            }

            OperationSectionRegistry registry =
                stagedStep.FinalizedOperationFrame.Registry;
            for (int sectionIndex = 0; sectionIndex < registry.Sections.Count; sectionIndex++)
            {
                _trace.Append(StateFlowTraceEntry.Operation(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    registry.Sections[sectionIndex].SectionId,
                    _activePreviousContextTrace));
            }
        }

        internal void Cancel(CoCoDiagnostic diagnostic)
        {
            if (_activeToken != 0UL)
            {
                CancelCandidate(diagnostic);
            }
        }

        internal bool TryValidateRestore(
            StateGraphRuntime runtime,
            ContextFrame source,
            TickFrame resumedTickFrame,
            out ContextCommitStatus status) =>
            TryValidateRestoreCore(
                runtime,
                source,
                resumedTickFrame,
                out _,
                out status);

        private bool TryValidateRestoreCore(
            StateGraphRuntime runtime,
            ContextFrame source,
            TickFrame resumedTickFrame,
            out ContextRestoreReadView restoreSource,
            out ContextCommitStatus status)
        {
            restoreSource = default;
            if (_isDisposed || runtime == null || _activeToken != 0UL ||
                _activeRestoreToken != 0UL)
            {
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (!_contextArena.TryValidateRestore(source, resumedTickFrame, out status))
            {
                return false;
            }

            restoreSource = new ContextRestoreReadView(source, _contextArena.Layout);
            if (!restoreSource.IsValid)
            {
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            if (!runtime.TryValidateRestore(
                    _contextRuntime,
                    restoreSource,
                    resumedTickFrame,
                    out _) ||
                !_operators.TryValidateRestore(
                    restoreSource,
                    _contextRuntime,
                    out _))
            {
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            status = ContextCommitStatus.None;
            return true;
        }

        internal bool TryPrepareRestore(
            StateGraphRuntime runtime,
            ContextFrame source,
            TickFrame resumedTickFrame,
            out PreparedActorRestore preparedRestore,
            out ContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            preparedRestore = default;
            diagnostic = CoCoDiagnostic.None;
            if (!TryValidateRestoreCore(
                    runtime,
                    source,
                    resumedTickFrame,
                    out ContextRestoreReadView restoreSource,
                    out status))
            {
                diagnostic = RestoreError(
                    "Actor restore validation rejected Context, Graph, Clock, or Claim authority.");
                return false;
            }

            if (_nextRestoreToken == ulong.MaxValue)
            {
                status = ContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError("Actor restore transaction tokens are exhausted.");
                return false;
            }

            FinalizedContextCommit finalizedContext;
            try
            {
                if (!_contextArena.TryPrepareRestore(
                        source,
                        resumedTickFrame,
                        out finalizedContext,
                        out status))
                {
                    diagnostic = RestoreError(
                        status == ContextCommitStatus.DerivedRebuildFailed
                            ? "Derived Context rebuild rejected restore preparation."
                            : "Context arena rejected restore preparation.");
                    return false;
                }
            }
            catch (Exception)
            {
                status = ContextCommitStatus.DerivedRebuildFailed;
                diagnostic = RestoreError(
                    "Derived Context rebuild threw during restore preparation.");
                return false;
            }

            if (!runtime.TryPrepareRestore(
                    _contextRuntime,
                    restoreSource,
                    resumedTickFrame,
                    out PreparedGraphRestore graphRestore,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            ulong token = ++_nextRestoreToken;
            ClaimRestoreOverlayPolicy overlayPolicy =
                runtime.Lifecycle == RuntimeLifecycleState.Suspended
                    ? ClaimRestoreOverlayPolicy.RebuildForSuspended
                    : ClaimRestoreOverlayPolicy.PreservePendingRelease;
            if (!_operators.TryPrepareRestore(
                    restoreSource,
                    _contextRuntime,
                    token,
                    overlayPolicy,
                    out diagnostic))
            {
                graphRestore.Cancel();
                finalizedContext.Cancel();
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            _preparedRestoreContext = finalizedContext;
            _preparedGraphRestore = graphRestore;
            _activeRestoreToken = token;
            preparedRestore = new PreparedActorRestore(this, token);
            status = ContextCommitStatus.None;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareTemporalRestore(
            StateGraphRuntime runtime,
            TemporalHistory history,
            in TemporalSelection selection,
            in TickFrame resumedTickFrame,
            out PreparedActorRestore preparedRestore,
            out ContextRestoreReadView restoreSource,
            out ContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            preparedRestore = default;
            restoreSource = default;
            diagnostic = CoCoDiagnostic.None;
            if (_isDisposed ||
                runtime == null ||
                history == null ||
                _activeToken != 0UL ||
                _activeRestoreToken != 0UL ||
                !selection.IsValid ||
                !resumedTickFrame.IsValid)
            {
                status = ContextCommitStatus.InvalidPreparation;
                diagnostic = RestoreError(
                    "Temporal restore requires one live idle transaction and one current history selection.");
                return false;
            }

            if (_nextRestoreToken == ulong.MaxValue)
            {
                status = ContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError("Actor restore transaction tokens are exhausted.");
                return false;
            }

            FinalizedContextCommit finalizedContext;
            try
            {
                _contextArena.RetainedRestoreSlots = _host.RetainedRecoverySlots;
                if (!history.TryPrepareRestore(
                        selection,
                        _contextArena,
                        resumedTickFrame,
                        out finalizedContext,
                        out status,
                        out CoCoDiagnosticCode diagnosticCode))
                {
                    diagnostic = RestoreError(
                        diagnosticCode,
                        status == ContextCommitStatus.DerivedRebuildFailed
                            ? "Derived Context rebuild rejected the Temporal restore candidate."
                            : "Temporal history could not materialize a complete restore candidate.");
                    return false;
                }
            }
            catch (Exception)
            {
                status = ContextCommitStatus.DerivedRebuildFailed;
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal history materialization threw during restore preparation.");
                return false;
            }

            if (!finalizedContext.TryCreateRestoreReadView(out restoreSource) ||
                !restoreSource.IsValid)
            {
                finalizedContext.Cancel();
                restoreSource = default;
                status = ContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError(
                    "Temporal restore candidate did not expose one valid policy-effective read view.");
                return false;
            }

            if (!runtime.TryPrepareRestore(
                    _contextRuntime,
                    restoreSource,
                    resumedTickFrame,
                    out PreparedGraphRestore graphRestore,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                restoreSource = default;
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            ulong token = ++_nextRestoreToken;
            if (!_operators.TryPrepareRestore(
                    restoreSource,
                    _contextRuntime,
                    token,
                    ClaimRestoreOverlayPolicy.DiscardAbandonedFuture,
                    out diagnostic))
            {
                graphRestore.Cancel();
                finalizedContext.Cancel();
                restoreSource = default;
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            try
            {
                if (!history.TryPrepareBranchCapture(
                        finalizedContext,
                        selection,
                        out CoCoDiagnosticCode diagnosticCode))
                {
                    history.CancelPreparedCapture();
                    _operators.CancelPreparedRestore();
                    graphRestore.Cancel();
                    finalizedContext.Cancel();
                    restoreSource = default;
                    status = ContextCommitStatus.RestoreFailed;
                    diagnostic = RestoreError(
                        diagnosticCode,
                        "Temporal history could not preflight the restored branch head.");
                    return false;
                }
            }
            catch (Exception)
            {
                history.CancelPreparedCapture();
                _operators.CancelPreparedRestore();
                graphRestore.Cancel();
                finalizedContext.Cancel();
                restoreSource = default;
                status = ContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal branch capture threw before the restore authority barrier.");
                return false;
            }

            _preparedRestoreContext = finalizedContext;
            _preparedGraphRestore = graphRestore;
            _activeRestoreToken = token;
            preparedRestore = new PreparedActorRestore(this, token);
            status = ContextCommitStatus.None;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPreparePersistenceRestore(
            StateGraphRuntime runtime,
            ContextProjectionCodec codec,
            ReadOnlySpan<byte> encoded,
            in TickFrame importedTickFrame,
            TemporalHistory history,
            out PreparedActorRestore preparedRestore,
            out ContextRestoreReadView restoreSource,
            out ProjectionRestoreSource persistedSource,
            out TemporalFrameInfo targetAuthority,
            out ContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            preparedRestore = default;
            restoreSource = default;
            persistedSource = default;
            targetAuthority = default;
            diagnostic = CoCoDiagnostic.None;
            if (_isDisposed ||
                runtime == null ||
                codec == null ||
                encoded.IsEmpty ||
                _activeToken != 0UL ||
                _activeRestoreToken != 0UL ||
                !importedTickFrame.IsValid)
            {
                status = ContextCommitStatus.InvalidPreparation;
                diagnostic = RestoreError(
                    "Persistence import requires one live idle transaction and one valid Durable payload.");
                return false;
            }

            if (_nextRestoreToken == ulong.MaxValue)
            {
                status = ContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError(
                    "Actor restore transaction tokens are exhausted.");
                return false;
            }

            FinalizedContextCommit finalizedContext;
            try
            {
                _contextArena.RetainedRestoreSlots = _host.RetainedRecoverySlots;
                if (!codec.TryDecodeAndPrepareImport(
                        encoded,
                        _contextArena,
                        importedTickFrame,
                        out finalizedContext,
                        out persistedSource,
                        out int bytesRead,
                        out status,
                        out CoCoDiagnosticCode diagnosticCode) ||
                    bytesRead != encoded.Length)
                {
                    diagnostic = RestoreError(
                        diagnosticCode,
                        status == ContextCommitStatus.DerivedRebuildFailed
                            ? "Derived Context rebuild rejected the Persistence import candidate."
                            : "Durable Context payload could not materialize a complete import candidate.");
                    return false;
                }
            }
            catch (Exception)
            {
                status = ContextCommitStatus.DerivedRebuildFailed;
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Persistence Context materialization threw during restore preparation.");
                return false;
            }

            if (!finalizedContext.TryCreateRestoreReadView(out restoreSource) ||
                !restoreSource.IsValid ||
                !finalizedContext.TryGetMetadata(
                    out StateFlowFrameHeader targetHeader,
                    out ContextRevision targetRevision,
                    out ContextFrameOrigin targetOrigin))
            {
                finalizedContext.Cancel();
                restoreSource = default;
                persistedSource = default;
                status = ContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError(
                    "Persistence import candidate did not expose valid target metadata and read authority.");
                return false;
            }

            targetAuthority = new TemporalFrameInfo(
                targetHeader.Identity.GraphInstanceId,
                targetHeader.TickFrame,
                targetRevision,
                targetOrigin);
            if (!targetAuthority.IsValid ||
                !runtime.TryPrepareRestore(
                    _contextRuntime,
                    restoreSource,
                    importedTickFrame,
                    out PreparedGraphRestore graphRestore,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                restoreSource = default;
                persistedSource = default;
                targetAuthority = default;
                status = ContextCommitStatus.RestoreFailed;
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError(
                        "Graph and Clock authority rejected the Persistence import candidate.");
                }

                return false;
            }

            ulong token = ++_nextRestoreToken;
            ClaimRestoreOverlayPolicy overlayPolicy =
                runtime.Lifecycle == RuntimeLifecycleState.Suspended
                    ? ClaimRestoreOverlayPolicy.RebuildForSuspended
                    : ClaimRestoreOverlayPolicy.DiscardAbandonedFuture;
            if (!_operators.TryPrepareRestore(
                    restoreSource,
                    _contextRuntime,
                    token,
                    overlayPolicy,
                    out diagnostic))
            {
                graphRestore.Cancel();
                finalizedContext.Cancel();
                restoreSource = default;
                persistedSource = default;
                targetAuthority = default;
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            if (history != null)
            {
                try
                {
                    if (!history.TryPrepareAuthorityReset(
                            finalizedContext,
                            out CoCoDiagnosticCode diagnosticCode))
                    {
                        history.CancelPreparedCapture();
                        _operators.CancelPreparedRestore();
                        graphRestore.Cancel();
                        finalizedContext.Cancel();
                        restoreSource = default;
                        persistedSource = default;
                        targetAuthority = default;
                        status = ContextCommitStatus.RestoreFailed;
                        diagnostic = RestoreError(
                            diagnosticCode,
                            "Temporal history could not stage the imported authority baseline.");
                        return false;
                    }
                }
                catch (Exception)
                {
                    history.CancelPreparedCapture();
                    _operators.CancelPreparedRestore();
                    graphRestore.Cancel();
                    finalizedContext.Cancel();
                    restoreSource = default;
                    persistedSource = default;
                    targetAuthority = default;
                    status = ContextCommitStatus.RestoreFailed;
                    diagnostic = RestoreError(
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        "Temporal authority reset staging threw before the Persistence authority barrier.");
                    return false;
                }
            }

            _preparedRestoreContext = finalizedContext;
            _preparedGraphRestore = graphRestore;
            _activeRestoreToken = token;
            preparedRestore = new PreparedActorRestore(this, token);
            status = ContextCommitStatus.None;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool IsPreparedRestoreTokenCurrent(ulong token) =>
            !_isDisposed &&
            token != 0UL &&
            token == _activeRestoreToken &&
            _preparedRestoreContext.IsCommitReady &&
            _preparedGraphRestore.IsValid &&
            _operators.IsPreparedRestoreTokenCurrent(token);

        internal void CommitPreparedRestoreNoFail(ulong token)
        {
            if (!IsPreparedRestoreTokenCurrent(token))
            {
                return;
            }

            _preparedRestoreContext.CommitNoFailUnchecked();
            _preparedGraphRestore.ApplyNoFail();
            _operators.CommitPreparedRestoreNoFail(token);
            ClearPreparedRestore();
        }

        internal bool CancelPreparedRestore(ulong token)
        {
            if (!IsPreparedRestoreTokenCurrent(token))
            {
                return false;
            }

            _preparedRestoreContext.Cancel();
            _preparedGraphRestore.Cancel();
            _operators.CancelPreparedRestore();
            ClearPreparedRestore();
            return true;
        }

        internal bool TryValidateInitialGraphContextDefaults(
            StateGraphRuntime runtime,
            IStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || runtime == null)
            {
                diagnostic = ContextError(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Initial Graph Context validation requires one live Runtime transaction.");
                return false;
            }

            return runtime.TryValidateInitialGraphContextDefaults(
                _contextRuntime,
                _contextArena.Previous,
                commitGuard,
                out diagnostic);
        }

        internal void Suspend() => _operators.Suspend();

        bool IEventOutboxSink.IsActive(ulong token, OperatorId operatorId) =>
            !_isDisposed && _operators.IsOperatorActive(token, operatorId);

        void IEventOutboxSink.RejectWrite(
            ulong token,
            OperatorId operatorId)
        {
            if (_activeToken != 0UL)
            {
                _outboxWriteFault = true;
            }
        }

        EventOutboxWriteResult IEventOutboxSink.TryWrite<TEvent>(
            ulong token,
            OperatorId operatorId,
            EventOutboxRequirement requirement,
            EventOutboxTarget target,
            in TEvent payload)
        {
            if (_isDisposed || !_operators.IsOperatorActive(token, operatorId))
            {
                return EventOutboxWriteResult.InvalidWriter;
            }

            if (CandidateEventCount >= _outboxLedger.Length)
            {
                _outboxWriteFault = true;
                return EventOutboxWriteResult.CapacityExceeded;
            }

            for (int laneIndex = 0; laneIndex < _outboxLanes.Length; laneIndex++)
            {
                IEventOutboxLane lane = _outboxLanes[laneIndex];
                if (lane.Requirement != requirement)
                {
                    continue;
                }

                if (!(lane is IEventOutboxLane<TEvent> typedLane))
                {
                    _outboxWriteFault = true;
                    return EventOutboxWriteResult.PayloadTypeMismatch;
                }

                if (!typedLane.TryAppend(target, payload, out int itemIndex))
                {
                    _outboxWriteFault = true;
                    return EventOutboxWriteResult.CapacityExceeded;
                }

                _outboxLedger[_outboxLedgerCount++] =
                    new OutboxLedgerEntry(laneIndex, itemIndex);
                return EventOutboxWriteResult.Accepted;
            }

            _outboxWriteFault = true;
            return EventOutboxWriteResult.UndeclaredEventType;
        }

        void IStateEventOutbox.Publish<TEvent>(in TEvent payload)
        {
            if (CandidateEventCount >= _outboxLedger.Length)
            {
                _outboxWriteFault = true;
                return;
            }
            if (!_stateEventPools.TryGetValue(typeof(TEvent), out var pool))
            {
                pool = new StateEventPool<TEvent>();
                _stateEventPools.Add(typeof(TEvent), pool);
            }
            _stateEvents.Add(((StateEventPool<TEvent>)pool).Append(in payload));
        }

        private interface IPendingStateEvent { void Publish(); }
        private interface IStateEventPool { void Reset(); }

        private sealed class StateEventPool<TEvent> : IStateEventPool where TEvent : unmanaged
        {
            private readonly List<PendingStateEvent<TEvent>> _items = new List<PendingStateEvent<TEvent>>();
            private int _count;
            internal IPendingStateEvent Append(in TEvent payload)
            {
                if (_count == _items.Count) _items.Add(new PendingStateEvent<TEvent>());
                var item = _items[_count++];
                item.Payload = payload;
                return item;
            }
            public void Reset() => _count = 0;
        }

        private sealed class PendingStateEvent<TEvent> : IPendingStateEvent where TEvent : unmanaged
        {
            internal TEvent Payload;
            public void Publish()
            {
                var copy = Payload;
                CoCoEventBus.Publish(ref copy);
            }
        }

        bool ICommittedEventPublisher.TryPublish<TEvent>(
            in EventPacket<TEvent> packet)
        {
            try
            {
                EventPacket<TEvent> committedPacket = packet;
                CoCoEventBus.Publish(ref committedPacket);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_activeToken != 0UL)
            {
                _preparedContext.Cancel();
            }

            if (_activeRestoreToken != 0UL)
            {
                CancelPreparedRestore(_activeRestoreToken);
            }

            ResetOutbox();
            _operators.Dispose();
            _contextRuntime.Dispose();
            _contextArena.Dispose();
            _trace?.Clear();
            ClearActiveTransaction();
            _isDisposed = true;
        }

        private bool PublishCommittedOutbox(
            in TickFrame tickFrame,
            EventSequence firstEventSequence,
            StateFlowTraceFrameReference committedFrame,
            out CoCoDiagnostic diagnostic)
        {
            bool allPublished = true;
            _host.BeginCommittedEventPublication();
            try
            {
                foreach (var pending in _stateEvents)
                {
                    try { pending.Publish(); }
                    catch (Exception) { allPublished = false; }
                }
                for (int ledgerIndex = 0; ledgerIndex < _outboxLedgerCount; ledgerIndex++)
                {
                    OutboxLedgerEntry entry = _outboxLedger[ledgerIndex];
                    ulong sequenceValue = firstEventSequence.Value + (ulong)ledgerIndex;
                    EventSequence sequence = default;
                    bool published;
                    try
                    {
                        published = EventSequence.TryCreate(
                                        sequenceValue,
                                        out sequence) &&
                                    _outboxLanes[entry.LaneIndex].TryPublish(
                                        entry.ItemIndex,
                                        new CommittedEventSource(
                                            _graphInstanceId,
                                            tickFrame.TimelineEpoch,
                                            tickFrame.Tick,
                                            sequence),
                                        this);
                    }
                    catch (Exception)
                    {
                        published = false;
                    }

                    if (!published)
                    {
                        allPublished = false;
                        continue;
                    }

                    _trace?.Append(StateFlowTraceEntry.Published(
                        _graphInstanceId,
                        tickFrame,
                        sequence,
                        committedFrame));
                }
            }
            finally
            {
                _host.EndCommittedEventPublication();
            }

            diagnostic = allPublished
                ? CoCoDiagnostic.None
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.EventPublishFailed,
                    "One or more committed EventOutbox packets could not be published; assigned Sequences remain consumed.");
            if (!allPublished)
            {
                _trace?.Append(StateFlowTraceEntry.Diagnostic(
                    _graphInstanceId,
                    tickFrame,
                    diagnostic,
                    frame: committedFrame));
            }

            return allPublished;
        }

        private bool TryPrepareEventSequenceRange(
            out EventSequence first,
            out EventSequence last,
            out CoCoDiagnostic diagnostic)
        {
            if (_outboxLedgerCount == 0)
            {
                first = default;
                last = default;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            ulong count = (ulong)_outboxLedgerCount;
            if (_lastEventSequence > ulong.MaxValue - count ||
                !EventSequence.TryCreate(_lastEventSequence + 1UL, out first) ||
                !EventSequence.TryCreate(_lastEventSequence + count, out last))
            {
                first = default;
                last = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.EventSequenceExhausted,
                    "EventSequence cannot allocate the complete candidate Outbox range.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void CancelCandidate(CoCoDiagnostic diagnostic)
        {
            if (!diagnostic.IsError)
            {
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitCancelled,
                    "The candidate transaction was cancelled before authority commit.");
            }

            _preparedContext.Cancel();
            _operators.Cancel();
            ResetOutbox();
            if (_activeTickFrame.IsValid)
            {
                _trace?.Append(StateFlowTraceEntry.Cancelled(
                    _graphInstanceId,
                    _activeTickFrame,
                    diagnostic,
                    _activePreviousContextTrace));
            }

            ClearActiveTransaction();
        }

        private void ResetOutbox()
        {
            _stateEvents.Clear();
            foreach (var pool in _stateEventPools.Values) pool.Reset();
            for (int index = 0; index < _outboxLanes.Length; index++)
            {
                _outboxLanes[index].Reset();
            }

            Array.Clear(_outboxLedger, 0, _outboxLedgerCount);
            _outboxLedgerCount = 0;
            _outboxWriteFault = false;
        }

        private void ClearActiveTransaction()
        {
            _preparedContext = default;
            _activeTickFrame = default;
            _activePreviousContextTrace = default;
            _activeToken = 0UL;
            _stagedTraceAppended = false;
        }

        private void ClearPreparedRestore()
        {
            _preparedRestoreContext = default;
            _preparedGraphRestore = default;
            _activeRestoreToken = 0UL;
        }

        private static CoCoDiagnostic ContextError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Frame, code, message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        private static CoCoDiagnostic RestoreError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.InvalidGraphRestore,
                message);

        private static CoCoDiagnostic RestoreError(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                code == CoCoDiagnosticCode.None
                    ? CoCoDiagnosticCode.InvalidGraphRestore
                    : code,
                message);

        private readonly struct OutboxLedgerEntry
        {
            public OutboxLedgerEntry(int laneIndex, int itemIndex)
            {
                LaneIndex = laneIndex;
                ItemIndex = itemIndex;
            }

            public int LaneIndex { get; }
            public int ItemIndex { get; }
        }
    }
}
