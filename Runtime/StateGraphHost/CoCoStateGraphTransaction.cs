using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    internal readonly struct CoCoPreparedActorRestore
    {
        private readonly CoCoStateGraphTransaction _transaction;
        private readonly ulong _token;

        internal CoCoPreparedActorRestore(
            CoCoStateGraphTransaction transaction,
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

    internal sealed class CoCoStateGraphTransaction :
        ICoCoEventOutboxSink,
        ICoCoCommittedEventPublisher,
        IDisposable
    {
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoStateGraphHost _host;
        private readonly CoCoContextFrameArena _contextArena;
        private readonly CoCoStateGraphContextRuntime _contextRuntime;
        private readonly CoCoStateGraphOperatorRuntime _operators;
        private readonly ICoCoEventOutboxLane[] _outboxLanes;
        private readonly OutboxLedgerEntry[] _outboxLedger;
        private readonly CoCoStateFlowTraceBuffer _trace;
        private CoCoPreparedContextCommit _preparedContext;
        private CoCoFinalizedContextCommit _preparedRestoreContext;
        private CoCoPreparedGraphRestore _preparedGraphRestore;
        private CoCoTickFrame _activeTickFrame;
        private CoCoStateFlowTraceFrameReference _activePreviousContextTrace;
        private ulong _nextTransactionToken;
        private ulong _activeToken;
        private ulong _nextRestoreToken;
        private ulong _activeRestoreToken;
        private ulong _lastEventSequence;
        private int _outboxLedgerCount;
        private bool _outboxWriteFault;
        private bool _stagedTraceAppended;
        private bool _isDisposed;

        private CoCoStateGraphTransaction(
            CoCoStateGraphHost host,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameArena contextArena,
            CoCoStateGraphContextRuntime contextRuntime,
            CoCoStateGraphOperatorRuntime operators,
            ICoCoEventOutboxLane[] outboxLanes,
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
            _trace = traceCapacity == 0 ? null : new CoCoStateFlowTraceBuffer(traceCapacity);
        }

        internal CoCoContextFrame CurrentContext => _contextArena.Current;
        internal CoCoContextFrameReadView PreviousContext => _contextArena.Previous;
        internal ICoCoStateFlowTrace Trace => _trace;
        internal int CandidateEventCount => _outboxLedgerCount;
        internal bool HasOutboxFailure => _outboxWriteFault;

        internal static bool TryCreate(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationFrame operations,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out CoCoStateGraphTransaction transaction,
            out CoCoDiagnostic diagnostic) =>
            TryPreflight(
                host,
                graph,
                graphInstanceId,
                contextLayout,
                operations,
                Array.Empty<ICoCoGraphContextProducerBinding>(),
                contextFrameCapacity,
                eventOutboxCapacity,
                traceCapacity,
                out transaction,
                out diagnostic);

        internal static bool TryCreate(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationFrame operations,
            IReadOnlyList<ICoCoGraphContextProducerBinding> contextProducers,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out CoCoStateGraphTransaction transaction,
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

        // This seam must run before CoCoStateGraphRuntime.TryCreate. It validates the
        // complete Operator transaction surface without invoking State factory,
        // Memory reset, or Memory fingerprint callbacks. On success, the returned
        // transaction owns all preallocated resources and is reused by the Host.
        internal static bool TryPreflight(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationFrame operations,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out CoCoStateGraphTransaction transaction,
            out CoCoDiagnostic diagnostic) =>
            TryPreflight(
                host,
                graph,
                graphInstanceId,
                contextLayout,
                operations,
                Array.Empty<ICoCoGraphContextProducerBinding>(),
                contextFrameCapacity,
                eventOutboxCapacity,
                traceCapacity,
                out transaction,
                out diagnostic);

        internal static bool TryPreflight(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationFrame operations,
            IReadOnlyList<ICoCoGraphContextProducerBinding> contextProducers,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out CoCoStateGraphTransaction transaction,
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

            CoCoContextFrameArena arena = null;
            CoCoStateGraphContextRuntime contextRuntime = null;
            CoCoStateGraphOperatorRuntime operatorRuntime = null;
            try
            {
                arena = new CoCoContextFrameArena(
                    graphInstanceId,
                    contextLayout,
                    contextFrameCapacity);
                if (!CoCoStateGraphContextRuntime.TryCreate(
                        host,
                        graph,
                        graphInstanceId,
                        contextLayout,
                        contextProducers,
                        host.ActorContextBinding,
                        out contextRuntime,
                        out diagnostic) ||
                    !CoCoStateGraphOperatorRuntime.TryCreate(
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
                        out ICoCoEventOutboxLane[] lanes,
                        out diagnostic))
                {
                    operatorRuntime?.Dispose();
                    contextRuntime?.Dispose();
                    arena.Dispose();
                    return false;
                }

                transaction = new CoCoStateGraphTransaction(
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
            in CoCoTickFrame tickFrame,
            out CoCoContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _activeToken != 0UL ||
                _activeRestoreToken != 0UL ||
                !tickFrame.IsValid ||
                _nextTransactionToken == ulong.MaxValue)
            {
                status = CoCoContextCommitStatus.InvalidPreparation;
                diagnostic = ContextError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Context transaction could not begin a fresh candidate Tick.");
                return false;
            }

            if (!_contextArena.HasAvailableCapacity)
            {
                status = CoCoContextCommitStatus.CapacityExhausted;
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
                !CoCoStateFlowTraceFrameReference.TryCreate(
                    _contextArena.Previous,
                    out _activePreviousContextTrace))
            {
                _preparedContext.Cancel();
                _preparedContext = default;
                status = CoCoContextCommitStatus.InvalidPreparation;
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
            _trace?.Append(CoCoStateFlowTraceEntry.Tick(
                _graphInstanceId,
                tickFrame,
                _activePreviousContextTrace));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void AppendStagedTrace(
            CoCoStateGraphRuntime runtime,
            in CoCoStagedGraphStep stagedStep)
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
            CoCoStateGraphRuntime runtime,
            CoCoStateGraphHostRuntimeBindings bindings,
            in CoCoStagedGraphStep stagedStep,
            ICoCoStateGraphCommitGuard commitGuard,
            out bool authorityCommitted,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            authorityCommitted = false;
            worldMayBeDirty = false;
            if (_isDisposed ||
                runtime == null ||
                bindings == null ||
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

            CoCoFinalizedContextCommit finalizedContext;
            CoCoContextCommitStatus contextStatus;
            try
            {
                if (!_preparedContext.TryFinalize(out finalizedContext, out contextStatus))
                {
                    diagnostic = ContextError(
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        contextStatus == CoCoContextCommitStatus.DerivedRebuildFailed
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
                    out CoCoPreparedGraphCommit preparedGraph,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            if (!TryPrepareEventSequenceRange(
                    out CoCoEventSequence firstEventSequence,
                    out CoCoEventSequence lastEventSequence,
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

            CoCoContextRevision previousRevision = _contextArena.Current.Revision;
            CoCoStateFlowTraceFrameReference committedTraceFrame = default;
            if (_trace != null)
            {
                ulong nextRevisionValue = previousRevision.IsValid
                    ? previousRevision.Value + 1UL
                    : 1UL;
                if (!CoCoStateFlowTraceFrameReference.TryCreateCommitted(
                        _graphInstanceId,
                        _contextArena.Layout,
                        stagedStep.TickFrame,
                        new CoCoContextRevision(nextRevisionValue),
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

            // D11 authority barrier. Every participant was validated above. These calls
            // contain no project callbacks, allocations, capacity checks, or failure branches.
            CoCoContextFrame committedContext = finalizedContext.CommitNoFailUnchecked();
            preparedGraph.CommitNoFail();
            _operators.CommitClaimsNoFail();
            _lastEventSequence = committedEventSequenceValue;
            bindings.ResolveIntentTickNoFail();
            authorityCommitted = true;
            _trace?.Append(CoCoStateFlowTraceEntry.Commit(
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
                _trace?.Append(CoCoStateFlowTraceEntry.Sequence(
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
            in CoCoStagedGraphStep stagedStep)
        {
            if (_trace == null)
            {
                return;
            }

            CoCoOperationSectionRegistry registry =
                stagedStep.FinalizedOperationFrame.Registry;
            for (int sectionIndex = 0; sectionIndex < registry.Sections.Count; sectionIndex++)
            {
                _trace.Append(CoCoStateFlowTraceEntry.Operation(
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
            CoCoStateGraphRuntime runtime,
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoContextCommitStatus status) =>
            TryValidateRestoreCore(
                runtime,
                source,
                resumedTickFrame,
                out _,
                out status);

        private bool TryValidateRestoreCore(
            CoCoStateGraphRuntime runtime,
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoContextRestoreReadView restoreSource,
            out CoCoContextCommitStatus status)
        {
            restoreSource = default;
            if (_isDisposed || runtime == null || _activeToken != 0UL ||
                _activeRestoreToken != 0UL)
            {
                status = CoCoContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (!_contextArena.TryValidateRestore(source, resumedTickFrame, out status))
            {
                return false;
            }

            restoreSource = new CoCoContextRestoreReadView(source, _contextArena.Layout);
            if (!restoreSource.IsValid)
            {
                status = CoCoContextCommitStatus.RestoreFailed;
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
                status = CoCoContextCommitStatus.RestoreFailed;
                return false;
            }

            status = CoCoContextCommitStatus.None;
            return true;
        }

        internal bool TryPrepareRestore(
            CoCoStateGraphRuntime runtime,
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoPreparedActorRestore preparedRestore,
            out CoCoContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            preparedRestore = default;
            diagnostic = CoCoDiagnostic.None;
            if (!TryValidateRestoreCore(
                    runtime,
                    source,
                    resumedTickFrame,
                    out CoCoContextRestoreReadView restoreSource,
                    out status))
            {
                diagnostic = RestoreError(
                    "Actor restore validation rejected Context, Graph, Clock, or Claim authority.");
                return false;
            }

            if (_nextRestoreToken == ulong.MaxValue)
            {
                status = CoCoContextCommitStatus.RestoreFailed;
                diagnostic = RestoreError("Actor restore transaction tokens are exhausted.");
                return false;
            }

            CoCoFinalizedContextCommit finalizedContext;
            try
            {
                if (!_contextArena.TryPrepareRestore(
                        source,
                        resumedTickFrame,
                        out finalizedContext,
                        out status))
                {
                    diagnostic = RestoreError(
                        status == CoCoContextCommitStatus.DerivedRebuildFailed
                            ? "Derived Context rebuild rejected restore preparation."
                            : "Context arena rejected restore preparation.");
                    return false;
                }
            }
            catch (Exception)
            {
                status = CoCoContextCommitStatus.DerivedRebuildFailed;
                diagnostic = RestoreError(
                    "Derived Context rebuild threw during restore preparation.");
                return false;
            }

            if (!runtime.TryPrepareRestore(
                    _contextRuntime,
                    restoreSource,
                    resumedTickFrame,
                    out CoCoPreparedGraphRestore graphRestore,
                    out diagnostic))
            {
                finalizedContext.Cancel();
                status = CoCoContextCommitStatus.RestoreFailed;
                return false;
            }

            ulong token = ++_nextRestoreToken;
            if (!_operators.TryPrepareRestore(
                    restoreSource,
                    _contextRuntime,
                    token,
                    runtime.Lifecycle == CoCoRuntimeLifecycleState.Suspended,
                    out diagnostic))
            {
                graphRestore.Cancel();
                finalizedContext.Cancel();
                status = CoCoContextCommitStatus.RestoreFailed;
                return false;
            }

            _preparedRestoreContext = finalizedContext;
            _preparedGraphRestore = graphRestore;
            _activeRestoreToken = token;
            preparedRestore = new CoCoPreparedActorRestore(this, token);
            status = CoCoContextCommitStatus.None;
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
            CoCoStateGraphRuntime runtime,
            ICoCoStateGraphCommitGuard commitGuard,
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

        bool ICoCoEventOutboxSink.IsActive(ulong token, CoCoOperatorId operatorId) =>
            !_isDisposed && _operators.IsOperatorActive(token, operatorId);

        void ICoCoEventOutboxSink.RejectWrite(
            ulong token,
            CoCoOperatorId operatorId)
        {
            if (_activeToken != 0UL)
            {
                _outboxWriteFault = true;
            }
        }

        CoCoEventOutboxWriteResult ICoCoEventOutboxSink.TryWrite<TEvent>(
            ulong token,
            CoCoOperatorId operatorId,
            CoCoEventOutboxRequirement requirement,
            CoCoEventOutboxTarget target,
            in TEvent payload)
        {
            if (_isDisposed || !_operators.IsOperatorActive(token, operatorId))
            {
                return CoCoEventOutboxWriteResult.InvalidWriter;
            }

            if (_outboxLedgerCount >= _outboxLedger.Length)
            {
                _outboxWriteFault = true;
                return CoCoEventOutboxWriteResult.CapacityExceeded;
            }

            for (int laneIndex = 0; laneIndex < _outboxLanes.Length; laneIndex++)
            {
                ICoCoEventOutboxLane lane = _outboxLanes[laneIndex];
                if (lane.Requirement != requirement)
                {
                    continue;
                }

                if (!(lane is ICoCoEventOutboxLane<TEvent> typedLane))
                {
                    _outboxWriteFault = true;
                    return CoCoEventOutboxWriteResult.PayloadTypeMismatch;
                }

                if (!typedLane.TryAppend(target, payload, out int itemIndex))
                {
                    _outboxWriteFault = true;
                    return CoCoEventOutboxWriteResult.CapacityExceeded;
                }

                _outboxLedger[_outboxLedgerCount++] =
                    new OutboxLedgerEntry(laneIndex, itemIndex);
                return CoCoEventOutboxWriteResult.Accepted;
            }

            _outboxWriteFault = true;
            return CoCoEventOutboxWriteResult.UndeclaredEventType;
        }

        bool ICoCoCommittedEventPublisher.TryPublish<TEvent>(
            in CoCoEventPacket<TEvent> packet)
        {
            try
            {
                CoCoEventPacket<TEvent> committedPacket = packet;
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
            in CoCoTickFrame tickFrame,
            CoCoEventSequence firstEventSequence,
            CoCoStateFlowTraceFrameReference committedFrame,
            out CoCoDiagnostic diagnostic)
        {
            bool allPublished = true;
            _host.BeginCommittedEventPublication();
            try
            {
                for (int ledgerIndex = 0; ledgerIndex < _outboxLedgerCount; ledgerIndex++)
                {
                    OutboxLedgerEntry entry = _outboxLedger[ledgerIndex];
                    ulong sequenceValue = firstEventSequence.Value + (ulong)ledgerIndex;
                    CoCoEventSequence sequence = default;
                    bool published;
                    try
                    {
                        published = CoCoEventSequence.TryCreate(
                                        sequenceValue,
                                        out sequence) &&
                                    _outboxLanes[entry.LaneIndex].TryPublish(
                                        entry.ItemIndex,
                                        new CoCoCommittedEventSource(
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

                    _trace?.Append(CoCoStateFlowTraceEntry.Published(
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
                _trace?.Append(CoCoStateFlowTraceEntry.Diagnostic(
                    _graphInstanceId,
                    tickFrame,
                    diagnostic,
                    frame: committedFrame));
            }

            return allPublished;
        }

        private bool TryPrepareEventSequenceRange(
            out CoCoEventSequence first,
            out CoCoEventSequence last,
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
                !CoCoEventSequence.TryCreate(_lastEventSequence + 1UL, out first) ||
                !CoCoEventSequence.TryCreate(_lastEventSequence + count, out last))
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
                _trace?.Append(CoCoStateFlowTraceEntry.Cancelled(
                    _graphInstanceId,
                    _activeTickFrame,
                    diagnostic,
                    _activePreviousContextTrace));
            }

            ClearActiveTransaction();
        }

        private void ResetOutbox()
        {
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
