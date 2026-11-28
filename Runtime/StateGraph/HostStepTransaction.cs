using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// 在全部参与者准备成功后提交原实例、Context 和时钟，最后才向场景发布事件。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>通知 Host 已提交事件开始或结束发布，以便推迟回调期内请求的释放。</summary>
    internal interface IHostEventPublication
    {
        void BeginCommittedEventPublication();
        void EndCommittedEventPublication();
    }

    /// <summary>一个已准备的恢复操作，仅持有本次事务的身份，不复制图实例。</summary>
    internal readonly struct PreparedHostRestore
    {
        private readonly HostStepTransaction _transaction;
        private readonly ulong _token;

        internal PreparedHostRestore(
            HostStepTransaction transaction,
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

    /// <summary>协调本 Host 的候选 Context、实例、Operator、输入、历史与事件。</summary>
    internal sealed class HostStepTransaction : IDisposable
    {
        private readonly GraphInstanceId _graphInstanceId;
        private readonly ContextFrameArena _contextArena;
        private readonly HostContextCapture _contextCapture;
        private readonly HostOperatorRunner _operators;
        private readonly Func<ContextRestoreReadView, ActivationId, bool> _isRestoredActivation;
        private readonly IHostEventPublication _publication;
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
        private bool _stagedTraceAppended;
        private bool _isDisposed;

        private HostStepTransaction(IHostEventPublication publication, GraphInstanceId graphInstanceId,
            ContextFrameArena arena, HostContextCapture contextCapture, HostOperatorRunner operators,
            HostEventOutbox outbox, int traceCapacity)
        {
            _publication = publication;
            _graphInstanceId = graphInstanceId;
            _contextArena = arena;
            _contextCapture = contextCapture;
            _isRestoredActivation = contextCapture.IsRestoredActiveActivation;
            _operators = operators;
            Outbox = outbox;
            _trace = traceCapacity == 0 ? null : new StateFlowTraceBuffer(traceCapacity);
        }

        internal HostEventOutbox Outbox { get; }
        internal ContextFrame CurrentContext => _contextArena.Current;
        internal ContextFrameReadView PreviousContext => _contextArena.Previous;
        internal IStateFlowTrace Trace => _trace;

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

        // 在创建任何项目 State 或激活记忆前，核验组件、Context 和 Operator 所需容量。
        internal static bool TryPreflight(
            HostComponentBoundary boundary,
            IHostEventPublication publication,
            MonoBehaviour sceneBinding,
            IReadOnlyList<MonoBehaviour> operatorComponents,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout contextLayout,
            OperationFrame operations,
            IReadOnlyList<IGraphContextProducerBinding> contextProducers,
            int contextFrameCapacity,
            int eventOutboxCapacity,
            int traceCapacity,
            out HostStepTransaction transaction,
            out CoCoDiagnostic diagnostic)
        {
            transaction = null;
            if (!boundary.HasOwner || publication == null ||
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
            HostContextCapture contextRuntime = null;
            HostOperatorRunner operatorRuntime = null;
            try
            {
                arena = new ContextFrameArena(
                    graphInstanceId,
                    contextLayout,
                    contextFrameCapacity);
                if (!HostContextCapture.TryCreate(
                        boundary,
                        graph,
                        graphInstanceId,
                        contextLayout,
                        contextProducers,
                        sceneBinding,
                        out contextRuntime,
                        out diagnostic) ||
                    !HostOperatorRunner.TryCreate(
                        boundary,
                        graph,
                        graphInstanceId,
                        contextLayout,
                        operations.Registry,
                        operatorComponents,
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

                transaction = new HostStepTransaction(
                    publication,
                    graphInstanceId,
                    arena,
                    contextRuntime,
                    operatorRuntime,
                    new HostEventOutbox(lanes, eventOutboxCapacity),
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
            Outbox.Clear();
            _operators.BeginStep(Outbox);
            _trace?.Append(StateFlowTraceEntry.Tick(
                _graphInstanceId,
                tickFrame,
                _activePreviousContextTrace));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void AppendStagedTrace(
            StateGraphInstance runtime,
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
            StateGraphInstance runtime,
            HostRuntimeBindings bindings,
            HostHistoryController temporal,
            HostRestoreBindings scene,
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
                temporal == null || scene == null ||
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
                    _contextCapture,
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

            if (!_contextCapture.TryCaptureHostContext(
                    stagedStep.TickFrame,
                    _contextArena.Previous,
                    _preparedContext,
                    _activeToken,
                    out bool sceneMayBeDirty,
                    out diagnostic))
            {
                worldMayBeDirty |= sceneMayBeDirty;
                CancelCandidate(diagnostic);
                return false;
            }

            worldMayBeDirty |= sceneMayBeDirty;
            if (Outbox.HasWriteFault)
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

            if (!Outbox.TryReserveSequences(_lastEventSequence,
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

            ulong committedEventSequenceValue = lastEventSequence.IsValid ? lastEventSequence.Value : _lastEventSequence;

            if (!temporal.TryPrepareCapture(finalizedContext, out diagnostic))
            {
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            TemporalHistory history = temporal.History;
            if (history != null && !scene.TryPrepareForwardCapture(finalizedContext, out diagnostic))
            {
                history.CancelPreparedCapture();
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                if (history != null)
                {
                    history.CancelPreparedCapture();
                    scene.Participant?.CancelPreparedCaptureNoFail();
                }
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the transaction after Temporal projection capture.");
                finalizedContext.Cancel();
                CancelCandidate(diagnostic);
                return false;
            }

            // 所有候选在此之前已完成校验，按序提交，再释放本步产生的事件。
            ContextFrame committedContext = finalizedContext.CommitNoFailUnchecked();
            preparedGraph.CommitNoFail();
            _operators.CommitClaimsNoFail();
            _lastEventSequence = committedEventSequenceValue;
            bindings.CommitIntentTickNoFail();
            if (history != null)
            {
                history.PublishCaptureNoFail();
                scene.Participant?.PublishForwardCaptureNoFail();
            }
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

            _publication.BeginCommittedEventPublication();
            try
            {
                return Outbox.PublishCommitted(_graphInstanceId, stagedStep.TickFrame, firstEventSequence,
                    _trace, committedTraceFrame, out diagnostic);
            }
            finally
            {
                _publication.EndCommittedEventPublication();
                Outbox.Clear();
                ClearActiveTransaction();
            }
        }

        private void AppendStagedOperationTrace(
            in StagedGraphStep stagedStep)
        {
            if (_trace == null)
            {
                return;
            }

            OperationRegistry registry =
                stagedStep.FinalizedOperationFrame.Registry;
            for (int sectionIndex = 0; sectionIndex < registry.Operations.Count; sectionIndex++)
            {
                _trace.Append(StateFlowTraceEntry.Operation(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    registry.Operations[sectionIndex].OperationId,
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
            StateGraphInstance runtime,
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
            StateGraphInstance runtime,
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
                    _contextCapture,
                    restoreSource,
                    resumedTickFrame,
                    out _) ||
                !_operators.Claims.TryValidateRestore(
                    restoreSource,
                    _isRestoredActivation,
                    out _))
            {
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            status = ContextCommitStatus.None;
            return true;
        }

        internal bool TryPrepareRestore(
            StateGraphInstance runtime,
            ContextFrame source,
            TickFrame resumedTickFrame,
            out PreparedHostRestore preparedRestore,
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
                    _contextCapture,
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
            HostOperatorClaims.RestorePolicy overlayPolicy =
                runtime.Lifecycle == RuntimeLifecycleState.Suspended
                    ? HostOperatorClaims.RestorePolicy.RebuildForSuspended
                    : HostOperatorClaims.RestorePolicy.PreservePendingRelease;
            if (!_operators.Claims.TryPrepareRestore(
                    restoreSource,
                    _isRestoredActivation,
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
            preparedRestore = new PreparedHostRestore(this, token);
            status = ContextCommitStatus.None;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareTemporalRestore(
            StateGraphInstance runtime,
            TemporalHistory history,
            in TemporalSelection selection,
            IReadOnlyList<StateSlotDescriptor> retainedSlots,
            in TickFrame resumedTickFrame,
            out PreparedHostRestore preparedRestore,
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
                _contextArena.RetainedRestoreSlots = retainedSlots;
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
                    _contextCapture,
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
            if (!_operators.Claims.TryPrepareRestore(
                    restoreSource,
                    _isRestoredActivation,
                    token,
                    HostOperatorClaims.RestorePolicy.DiscardAbandonedFuture,
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
                    _operators.Claims.CancelPreparedRestore();
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
                _operators.Claims.CancelPreparedRestore();
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
            preparedRestore = new PreparedHostRestore(this, token);
            status = ContextCommitStatus.None;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPreparePersistenceRestore(
            StateGraphInstance runtime,
            ContextProjectionCodec codec,
            ReadOnlySpan<byte> encoded,
            in TickFrame importedTickFrame,
            IReadOnlyList<StateSlotDescriptor> retainedSlots,
            TemporalHistory history,
            out PreparedHostRestore preparedRestore,
            out ContextRestoreReadView restoreSource,
            out ProjectionRestoreSource persistedSource,
            out HostHistoryFrame targetAuthority,
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
                _contextArena.RetainedRestoreSlots = retainedSlots;
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

            targetAuthority = new HostHistoryFrame(
                targetHeader.Identity.GraphInstanceId,
                targetHeader.TickFrame,
                targetRevision,
                targetOrigin);
            if (!targetAuthority.IsValid ||
                !runtime.TryPrepareRestore(
                    _contextCapture,
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
            HostOperatorClaims.RestorePolicy overlayPolicy =
                runtime.Lifecycle == RuntimeLifecycleState.Suspended
                    ? HostOperatorClaims.RestorePolicy.RebuildForSuspended
                    : HostOperatorClaims.RestorePolicy.DiscardAbandonedFuture;
            if (!_operators.Claims.TryPrepareRestore(
                    restoreSource,
                    _isRestoredActivation,
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
                        _operators.Claims.CancelPreparedRestore();
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
                    _operators.Claims.CancelPreparedRestore();
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
            preparedRestore = new PreparedHostRestore(this, token);
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
            _operators.Claims.IsPreparedRestoreTokenCurrent(token);

        internal void CommitPreparedRestoreNoFail(ulong token)
        {
            if (!IsPreparedRestoreTokenCurrent(token))
            {
                return;
            }

            _preparedRestoreContext.CommitNoFailUnchecked();
            _preparedGraphRestore.ApplyNoFail();
            _operators.Claims.CommitPreparedRestoreNoFail(token);
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
            _operators.Claims.CancelPreparedRestore();
            ClearPreparedRestore();
            return true;
        }

        internal bool TryValidateInitialGraphContextDefaults(
            StateGraphInstance runtime,
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
                _contextCapture,
                _contextArena.Previous,
                commitGuard,
                out diagnostic);
        }

        internal void Suspend() => _operators.Claims.Suspend();

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

            Outbox.Clear();
            _operators.Dispose();
            _contextCapture.Dispose();
            _contextArena.Dispose();
            _trace?.Clear();
            ClearActiveTransaction();
            _isDisposed = true;
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
            Outbox.Clear();
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
    }
}
