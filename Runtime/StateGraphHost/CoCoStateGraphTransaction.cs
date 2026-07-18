using System;

namespace CoCoFlow.Runtime.Core
{
    internal sealed class CoCoStateGraphTransaction :
        ICoCoEventOutboxSink,
        ICoCoCommittedEventPublisher,
        IDisposable
    {
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoStateGraphHost _host;
        private readonly CoCoContextFrameArena _contextArena;
        private readonly CoCoStateGraphOperatorRuntime _operators;
        private readonly ICoCoEventOutboxLane[] _outboxLanes;
        private readonly OutboxLedgerEntry[] _outboxLedger;
        private readonly CoCoStateFlowTraceBuffer _trace;
        private CoCoPreparedContextCommit _preparedContext;
        private CoCoTickFrame _activeTickFrame;
        private ulong _nextTransactionToken;
        private ulong _activeToken;
        private ulong _lastEventSequence;
        private int _outboxLedgerCount;
        private bool _outboxWriteFault;
        private bool _isDisposed;

        private CoCoStateGraphTransaction(
            CoCoStateGraphHost host,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameArena contextArena,
            CoCoStateGraphOperatorRuntime operators,
            ICoCoEventOutboxLane[] outboxLanes,
            int outboxCapacity,
            int traceCapacity)
        {
            _host = host;
            _graphInstanceId = graphInstanceId;
            _contextArena = contextArena;
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
            out CoCoDiagnostic diagnostic)
        {
            transaction = null;
            if (host == null ||
                graph == null ||
                !graphInstanceId.IsValid ||
                contextLayout == null ||
                operations == null ||
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
            CoCoStateGraphOperatorRuntime operatorRuntime = null;
            try
            {
                arena = new CoCoContextFrameArena(
                    graphInstanceId,
                    contextLayout,
                    contextFrameCapacity);
                if (!CoCoStateGraphOperatorRuntime.TryCreate(
                        host,
                        graph,
                        graphInstanceId,
                        contextLayout,
                        operations.Registry,
                        host.Operators,
                        out operatorRuntime,
                        out diagnostic) ||
                    !operatorRuntime.TryCreateOutboxLanes(
                        eventOutboxCapacity,
                        out ICoCoEventOutboxLane[] lanes,
                        out diagnostic))
                {
                    operatorRuntime?.Dispose();
                    arena.Dispose();
                    return false;
                }

                transaction = new CoCoStateGraphTransaction(
                    host,
                    graphInstanceId,
                    arena,
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

            _nextTransactionToken++;
            _activeToken = _nextTransactionToken;
            _activeTickFrame = tickFrame;
            ResetOutbox();
            _trace?.Append(CoCoStateFlowTraceEntry.Tick(_graphInstanceId, tickFrame));
            diagnostic = CoCoDiagnostic.None;
            return true;
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

            if (!_operators.TryExecute(
                    stagedStep,
                    _contextArena.Previous,
                    _preparedContext,
                    this,
                    _activeToken,
                    _trace,
                    out diagnostic))
            {
                worldMayBeDirty = _operators.WorldMayBeDirty;
                CancelCandidate(diagnostic);
                return false;
            }

            worldMayBeDirty = _operators.WorldMayBeDirty;
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
            AppendCommittedAuthorityTrace(runtime, stagedStep);
            _trace?.Append(CoCoStateFlowTraceEntry.Commit(
                _graphInstanceId,
                stagedStep.TickFrame,
                previousRevision,
                committedContext.Revision));
            if (firstEventSequence.IsValid)
            {
                _trace?.Append(CoCoStateFlowTraceEntry.Sequence(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    firstEventSequence,
                    lastEventSequence));
            }

            bool published = PublishCommittedOutbox(
                stagedStep.TickFrame,
                firstEventSequence,
                out diagnostic);
            ResetOutbox();
            ClearActiveTransaction();
            return published;
        }

        private void AppendCommittedAuthorityTrace(
            CoCoStateGraphRuntime runtime,
            in CoCoStagedGraphStep stagedStep)
        {
            if (_trace == null)
            {
                return;
            }

            runtime.AppendCommittedStateTrace(_trace, stagedStep.TickFrame);

            CoCoOperationSectionRegistry registry =
                stagedStep.FinalizedOperationFrame.Registry;
            for (int sectionIndex = 0; sectionIndex < registry.Sections.Count; sectionIndex++)
            {
                _trace.Append(CoCoStateFlowTraceEntry.Operation(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    registry.Sections[sectionIndex].SectionId));
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
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoContextCommitStatus status) =>
            _contextArena.TryValidateRestore(source, resumedTickFrame, out status);

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

            ResetOutbox();
            _operators.Dispose();
            _contextArena.Dispose();
            _trace?.Clear();
            ClearActiveTransaction();
            _isDisposed = true;
        }

        private bool PublishCommittedOutbox(
            in CoCoTickFrame tickFrame,
            CoCoEventSequence firstEventSequence,
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
                        sequence));
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
                    diagnostic));
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
            _preparedContext.Cancel();
            _operators.Cancel();
            ResetOutbox();
            if (_activeTickFrame.IsValid)
            {
                _trace?.Append(CoCoStateFlowTraceEntry.Cancelled(
                    _graphInstanceId,
                    _activeTickFrame,
                    diagnostic));
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
            _activeToken = 0UL;
        }

        private static CoCoDiagnostic ContextError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Frame, code, message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
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
