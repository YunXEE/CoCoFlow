using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal sealed class CoCoStateGraphTemporalController :
        ICoCoContextRestoreReadSource,
        IDisposable
    {
        private readonly CoCoStateGraphHost _host;
        private readonly CoCoContextFrameLayout _layout;
        private readonly CoCoStateGraphTransaction _transaction;
        private readonly CoCoActorEventInboxCore _inbox;
        private readonly MonoBehaviour _bindingComponent;
        private readonly ICoCoContextRestoreBinding _binding;
        private readonly CoCoTemporalHistory _history;
        private readonly CoCoContextRestoreReadLease _readLease;
        private CoCoTemporalMode _mode;
        private CoCoTemporalFrameInfo _previewInfo;
        private CoCoContextFrameReadView _activeAuthorityView;
        private CoCoContextRestoreReadView _activeRestoreView;
        private ulong _nextReadToken;
        private ulong _activeReadToken;
        private int _previewDepth;
        private bool _hasAppliedPreviewProjection;
        private bool _readsRestoreCandidate;
        private bool _isDisposed;

        private CoCoStateGraphTemporalController(
            CoCoStateGraphHost host,
            CoCoContextFrameLayout layout,
            CoCoStateGraphTransaction transaction,
            CoCoActorEventInboxCore inbox,
            MonoBehaviour bindingComponent,
            ICoCoContextRestoreBinding binding,
            CoCoTemporalHistory history)
        {
            _host = host;
            _layout = layout;
            _transaction = transaction;
            _inbox = inbox;
            _bindingComponent = bindingComponent;
            _binding = binding;
            _history = history;
            _readLease = new CoCoContextRestoreReadLease();
            _mode = history == null
                ? CoCoTemporalMode.Disabled
                : CoCoTemporalMode.Ready;
        }

        internal CoCoTemporalMode Mode => _mode;

        internal CoCoTemporalState State
        {
            get
            {
                CoCoTemporalFrameInfo current = ToPublicInfo(_transaction.CurrentContext);
                CoCoTemporalFrameInfo preview = _mode == CoCoTemporalMode.Previewing &&
                                                    _previewDepth > 0
                    ? _previewInfo
                    : current;
                ulong dropped = _inbox?.Counters.RewindRestoreDropped ?? 0UL;
                bool canConfirm = _mode == CoCoTemporalMode.Previewing &&
                                  _previewDepth > 0 &&
                                  _history != null &&
                                  _previewDepth < _history.Count &&
                                  !_host.Fault.IsFaulted &&
                                  IsBindingLive;
                return new CoCoTemporalState(
                    _mode,
                    _history?.Capacity ?? 0,
                    _history?.Count ?? 0,
                    _previewDepth,
                    current,
                    preview,
                    dropped,
                    canConfirm);
            }
        }

        internal static bool TryValidateConfiguration(
            CoCoStateGraphHost host,
            CoCoContextFrameLayout layout,
            CoCoContextCodecRegistry codecs,
            MonoBehaviour bindingComponent,
            int capacity,
            out CoCoDiagnostic diagnostic)
        {
            if (host == null ||
                layout == null ||
                codecs == null ||
                (capacity != 0 && capacity < 2))
            {
                diagnostic = ConfigurationError(
                    "Temporal configuration requires one Host, frozen Context layout and codec registry, and a capacity of zero or at least two committed entries.");
                return false;
            }

            bool hasBinding = bindingComponent != null;
            if (capacity > 0 &&
                (!hasBinding ||
                 !(bindingComponent is ICoCoContextRestoreBinding) ||
                 !IsInsideHostBoundary(host, bindingComponent)))
            {
                diagnostic = ConfigurationError(
                    "Enabled Temporal history requires one live Restore Binding inside the Host boundary.");
                return false;
            }

            CoCoContextProjectionCodec codec = null;
            if (capacity > 0 &&
                !CoCoContextProjectionCodec.TryCreate(
                    layout,
                    codecs,
                    CoCoContextProjection.Temporal,
                    out codec,
                    out CoCoDiagnosticCode diagnosticCode))
            {
                diagnostic = HistoryError(
                    diagnosticCode,
                    "Temporal projection codec preflight rejected the Context layout or codec registry.");
                return false;
            }

            if (capacity > 0 && codec != null &&
                ((long)capacity + 1L) * codec.MaxEncodedSize + layout.ByteSize > int.MaxValue)
            {
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "Temporal history exceeds the checked managed payload budget.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryCreate(
            CoCoStateGraphHost host,
            CoCoContextFrameLayout layout,
            CoCoContextCodecRegistry codecs,
            CoCoStateGraphTransaction transaction,
            CoCoActorEventInboxCore inbox,
            MonoBehaviour bindingComponent,
            int capacity,
            out CoCoStateGraphTemporalController controller,
            out CoCoDiagnostic diagnostic)
        {
            controller = null;
            diagnostic = CoCoDiagnostic.None;
            if (transaction == null ||
                !TryValidateConfiguration(
                    host,
                    layout,
                    codecs,
                    bindingComponent,
                    capacity,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = ConfigurationError(
                        "Temporal controller requires one live Host transaction.");
                }

                return false;
            }

            CoCoTemporalHistory history = null;
            try
            {
                if (capacity > 0 &&
                    !CoCoTemporalHistory.TryCreate(
                        layout,
                        codecs,
                        capacity,
                        out history,
                        out CoCoDiagnosticCode diagnosticCode))
                {
                    diagnostic = HistoryError(
                        diagnosticCode,
                        "Temporal history could not allocate its fixed projection payload.");
                    return false;
                }

                MonoBehaviour activeBindingComponent =
                    bindingComponent != null &&
                    bindingComponent is ICoCoContextRestoreBinding &&
                    IsInsideHostBoundary(host, bindingComponent)
                        ? bindingComponent
                        : null;
                controller = new CoCoStateGraphTemporalController(
                    host,
                    layout,
                    transaction,
                    inbox,
                    activeBindingComponent,
                    activeBindingComponent as ICoCoContextRestoreBinding,
                    history);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception)
            {
                history?.Dispose();
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal history setup threw before Host publication.");
                return false;
            }
        }

        internal bool TryPrepareCapture(
            in CoCoFinalizedContextCommit candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (_history == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                if (_history.TryPrepareCapture(candidate, out CoCoDiagnosticCode diagnosticCode))
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                _history.CancelPreparedCapture();
                diagnostic = HistoryError(
                    diagnosticCode,
                    "Temporal projection capture rejected the finalized Context candidate.");
                return false;
            }
            catch (Exception)
            {
                _history.CancelPreparedCapture();
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal projection capture threw before the authority barrier.");
                return false;
            }
        }

        internal void PublishCaptureNoFail()
        {
            _history?.PublishCaptureNoFail();
        }

        internal void CancelPreparedCapture()
        {
            _history?.CancelPreparedCapture();
        }

        internal bool TryBegin(
            CoCoStateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, CoCoTemporalMode.Ready, out diagnostic) ||
                _history == null ||
                _history.Count < 2)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TemporalLifecycleError(
                        "Temporal preview requires at least current authority and one older committed entry.");
                }

                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                return false;
            }

            if (_inbox != null && !_inbox.BeginRewindOrRestore())
            {
                diagnostic = MailboxError(
                    "Inbox could not enter Rewind mode at the resolved Running boundary.");
                return false;
            }

            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = ToPublicInfo(_transaction.CurrentContext);
            _mode = CoCoTemporalMode.Previewing;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPreview(
            CoCoStateGraphRuntime runtime,
            int historyDepth,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, CoCoTemporalMode.Previewing, out diagnostic) ||
                _history == null ||
                historyDepth < 0 ||
                historyDepth >= _history.Count)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = HistoryError(
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "Temporal preview depth is outside the current bounded history.");
                }

                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                if (_hasAppliedPreviewProjection)
                {
                    _host.LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            bool applied;
            bool worldMayBeDirty;
            CoCoTemporalFrameInfo previewInfo;
            if (historyDepth == 0)
            {
                CoCoContextFrameReadView authority = _transaction.PreviousContext;
                previewInfo = ToPublicInfo(_transaction.CurrentContext);
                applied = TryApplyAuthority(
                    CoCoContextRestoreApplyKind.Preview,
                    previewInfo,
                    previewInfo.TickFrame,
                    authority,
                    out worldMayBeDirty,
                    out diagnostic);
            }
            else
            {
                CoCoTemporalSelection selection;
                try
                {
                    if (!_history.TrySelect(
                            historyDepth,
                            out selection,
                            out CoCoDiagnosticCode diagnosticCode))
                    {
                        diagnostic = HistoryError(
                            diagnosticCode,
                            "Temporal history could not materialize the requested preview.");
                        return false;
                    }
                }
                catch (Exception)
                {
                    diagnostic = HistoryError(
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        "Temporal history preview decoding threw before Unity projection.");
                    return false;
                }

                previewInfo = ToPublicInfo(selection.Info);
                applied = TryApplyRestore(
                    CoCoContextRestoreApplyKind.Preview,
                    previewInfo,
                    previewInfo.TickFrame,
                    selection.RestoreView,
                    out worldMayBeDirty,
                    out diagnostic);
            }

            if (!applied)
            {
                if (_hasAppliedPreviewProjection || worldMayBeDirty)
                {
                    _host.LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            _previewDepth = historyDepth;
            _hasAppliedPreviewProjection = true;
            _previewInfo = previewInfo;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryConfirm(
            CoCoStateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, CoCoTemporalMode.Previewing, out diagnostic) ||
                _history == null ||
                _previewDepth <= 0 ||
                _previewDepth >= _history.Count)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TemporalLifecycleError(
                        "Temporal Confirm requires one successfully previewed historical depth greater than zero.");
                }

                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            if (_inbox != null && !_inbox.CanResumeAfterTimelineReset)
            {
                diagnostic = MailboxError(
                    "Inbox cannot complete the Temporal timeline reset.");
                return false;
            }

            CoCoTemporalSelection selection;
            try
            {
                if (!_history.TrySelect(
                        _previewDepth,
                        out selection,
                        out CoCoDiagnosticCode diagnosticCode))
                {
                    diagnostic = HistoryError(
                        diagnosticCode,
                        "Temporal Confirm could not reacquire the previewed history entry.");
                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal Confirm decoding threw before restore preparation.");
                return false;
            }

            if (!TryCreateResumedTickFrame(
                    runtime,
                    selection.Info,
                    out CoCoTickFrame resumedTickFrame,
                    out diagnostic) ||
                !_transaction.TryPrepareTemporalRestore(
                    runtime,
                    _history,
                    selection,
                    resumedTickFrame,
                    out CoCoPreparedActorRestore preparedRestore,
                    out CoCoContextRestoreReadView restoreSource,
                    out _,
                    out diagnostic))
            {
                _history.CancelPreparedCapture();
                return false;
            }

            CoCoTemporalFrameInfo sourceInfo = ToPublicInfo(selection.Info);
            if (!TryApplyRestore(
                    CoCoContextRestoreApplyKind.Confirm,
                    sourceInfo,
                    resumedTickFrame,
                    restoreSource,
                    out _,
                    out diagnostic))
            {
                preparedRestore.Cancel();
                _history.CancelPreparedCapture();
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            if (_host.IsTemporalOperationCancellationRequested ||
                !preparedRestore.IsValid ||
                !_history.HasPreparedCapture ||
                (_inbox != null && !_inbox.CanResumeAfterTimelineReset))
            {
                preparedRestore.Cancel();
                _history.CancelPreparedCapture();
                diagnostic = TemporalLifecycleError(
                    "Temporal restore proof changed during Unity projection before authority publication.");
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            preparedRestore.CommitNoFail();
            _history.PublishBranchCaptureNoFail();
            _inbox?.ResumeAfterTimelineResetNoFail(resumedTickFrame.TimelineEpoch);
            _mode = CoCoTemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = ToPublicInfo(_transaction.CurrentContext);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCancel(
            CoCoStateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, CoCoTemporalMode.Previewing, out diagnostic))
            {
                return false;
            }

            if (_hasAppliedPreviewProjection &&
                !TryRequireLiveBinding(out diagnostic))
            {
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            if (_inbox != null && !_inbox.CanCancelRewindOrRestore)
            {
                diagnostic = MailboxError(
                    "Inbox cannot cancel the current Temporal preview session.");
                return false;
            }

            CoCoTemporalFrameInfo current = ToPublicInfo(_transaction.CurrentContext);
            if (_hasAppliedPreviewProjection)
            {
                CoCoContextFrameReadView authority = _transaction.PreviousContext;
                if (!TryApplyAuthority(
                        CoCoContextRestoreApplyKind.Cancel,
                        current,
                        current.TickFrame,
                        authority,
                        out _,
                        out diagnostic))
                {
                    _host.LatchWorldCorrectionFault(diagnostic);
                    return false;
                }
            }

            _inbox?.CancelRewindOrRestoreNoFail();
            _mode = CoCoTemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = current;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCorrectWorld(
            CoCoStateGraphRuntime runtime,
            bool requiresWorldCorrection,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                runtime == null ||
                !requiresWorldCorrection ||
                !runtime.IsFaulted ||
                (runtime.Lifecycle != CoCoRuntimeLifecycleState.Running &&
                 runtime.Lifecycle != CoCoRuntimeLifecycleState.Suspended))
            {
                diagnostic = TemporalLifecycleError(
                    "World correction requires one live faulted Host with an explicit correction requirement.");
                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                return false;
            }

            bool closesPreview = _mode == CoCoTemporalMode.Previewing;
            if (closesPreview &&
                _inbox != null &&
                !_inbox.CanCancelRewindOrRestore)
            {
                diagnostic = MailboxError(
                    "Inbox cannot close the faulted Temporal preview during world correction.");
                return false;
            }

            if (!runtime.TryPrepareTemporalRecovery(
                    out CoCoPreparedTemporalRecovery recovery,
                    out diagnostic))
            {
                return false;
            }

            CoCoContextFrameReadView authority = _transaction.PreviousContext;
            CoCoTemporalFrameInfo current = ToPublicInfo(_transaction.CurrentContext);
            if (!TryApplyAuthority(
                    CoCoContextRestoreApplyKind.Correction,
                    current,
                    current.TickFrame,
                    authority,
                    out _,
                    out diagnostic))
            {
                recovery.Cancel();
                return false;
            }

            if (_host.IsTemporalOperationCancellationRequested ||
                !recovery.IsValid ||
                (closesPreview &&
                 _inbox != null &&
                 !_inbox.CanCancelRewindOrRestore))
            {
                recovery.Cancel();
                diagnostic = TemporalLifecycleError(
                    "World correction proof changed during Unity projection.");
                return false;
            }

            if (closesPreview)
            {
                _inbox?.CancelRewindOrRestoreNoFail();
            }

            recovery.CompleteNoFail();
            _host.ClearWorldCorrectionRequirementNoFail();
            _mode = _history == null
                ? CoCoTemporalMode.Disabled
                : CoCoTemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = current;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            ClearActiveRead();
            _history?.Dispose();
            _previewInfo = default;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _mode = CoCoTemporalMode.Disabled;
        }

        bool ICoCoContextRestoreReadSource.IsReadActive(ulong token) =>
            !_isDisposed && token != 0UL && token == _activeReadToken;

        bool ICoCoContextRestoreReadSource.TryRead<TValue>(
            ulong token,
            CoCoStateSlotId slotId,
            out TValue value)
        {
            value = default;
            if (_isDisposed || token == 0UL || token != _activeReadToken)
            {
                return false;
            }

            if (_readsRestoreCandidate)
            {
                return _activeRestoreView.TryRead(slotId, out value);
            }

            try
            {
                if (!_layout.TryResolveSlot(
                        slotId,
                        out CoCoStateSlot<TValue> slot))
                {
                    return false;
                }

                value = _activeAuthorityView.Read(slot);
                return true;
            }
            catch (Exception)
            {
                value = default;
                return false;
            }
        }

        private bool TryApplyAuthority(
            CoCoContextRestoreApplyKind applyKind,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            in CoCoContextFrameReadView authority,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            worldMayBeDirty = false;
            if (!authority.IsValid)
            {
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.InvalidFrameHandle,
                    "Current Context authority is not available for Unity projection.");
                return false;
            }

            _activeAuthorityView = authority;
            _readsRestoreCandidate = false;
            return TryInvokeBinding(
                applyKind,
                source,
                targetTickFrame,
                out worldMayBeDirty,
                out diagnostic);
        }

        private bool TryApplyRestore(
            CoCoContextRestoreApplyKind applyKind,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            in CoCoContextRestoreReadView restore,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            worldMayBeDirty = false;
            if (!restore.IsValid)
            {
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.InvalidFrameHandle,
                    "Temporal restore view expired before Unity projection.");
                return false;
            }

            _activeRestoreView = restore;
            _readsRestoreCandidate = true;
            return TryInvokeBinding(
                applyKind,
                source,
                targetTickFrame,
                out worldMayBeDirty,
                out diagnostic);
        }

        private bool TryInvokeBinding(
            CoCoContextRestoreApplyKind applyKind,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            worldMayBeDirty = false;
            if (!TryRequireLiveBinding(out diagnostic) ||
                _nextReadToken == ulong.MaxValue)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TemporalLifecycleError(
                        "Restore Binding callback tokens are exhausted.");
                }

                ClearActiveRead();
                return false;
            }

            ulong token = ++_nextReadToken;
            _activeReadToken = token;
            if (!_readLease.TryAttach(this, token))
            {
                ClearActiveRead();
                diagnostic = TemporalLifecycleError(
                    "Restore Binding reader lease could not attach at the synchronous callback boundary.");
                return false;
            }

            var context = new CoCoContextRestoreBindingContext(
                applyKind,
                source,
                targetTickFrame,
                new CoCoContextRestoreReader(_readLease, token));
            bool applied;
            worldMayBeDirty = true;
            try
            {
                applied = _binding.TryApply(context, out diagnostic);
            }
            catch (Exception)
            {
                applied = false;
                diagnostic = BindingError(
                    "Restore Binding threw during synchronous Unity projection.");
            }
            finally
            {
                ClearActiveRead();
            }

            if (!applied || diagnostic.IsError || !IsBindingLive ||
                _host.IsTemporalOperationCancellationRequested)
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = BindingError(
                        !IsBindingLive
                            ? "Restore Binding was destroyed during Unity projection."
                            : "Restore Binding rejected or invalidated synchronous Unity projection.");
                }

                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryRequireHealthyRunning(
            CoCoStateGraphRuntime runtime,
            CoCoTemporalMode requiredMode,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                runtime == null ||
                runtime.Lifecycle != CoCoRuntimeLifecycleState.Running ||
                runtime.IsFaulted ||
                _mode != requiredMode)
            {
                diagnostic = TemporalLifecycleError(
                    "Temporal control requires one healthy Running Host in the expected Temporal mode.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryRequireLiveBinding(out CoCoDiagnostic diagnostic)
        {
            if (IsBindingLive)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            diagnostic = ConfigurationError(
                "Temporal Unity projection requires its original live Restore Binding inside the Host boundary.");
            return false;
        }

        private bool IsBindingLive =>
            _binding != null &&
            _bindingComponent != null &&
            IsInsideHostBoundary(_host, _bindingComponent);

        private void ClearActiveRead()
        {
            _readLease.Detach();
            _activeReadToken = 0UL;
            _activeAuthorityView = default;
            _activeRestoreView = default;
            _readsRestoreCandidate = false;
        }

        private static bool TryCreateResumedTickFrame(
            CoCoStateGraphRuntime runtime,
            in CoCoTemporalHistoryEntryInfo source,
            out CoCoTickFrame resumed,
            out CoCoDiagnostic diagnostic)
        {
            resumed = default;
            diagnostic = CoCoDiagnostic.None;
            if (runtime == null || !source.IsValid)
            {
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "Temporal restore source metadata is incomplete.");
                return false;
            }

            CoCoTickFrame sourceTick = source.Header.TickFrame;
            ulong currentEpoch = runtime.Clock.TimelineEpoch.Value;
            ulong currentSequence = runtime.Clock.ExecutionSequence.Value;
            ulong sourceEpoch = sourceTick.TimelineEpoch.Value;
            ulong sourceSequence = sourceTick.ExecutionSequence.Value;
            ulong maximumEpoch = currentEpoch > sourceEpoch ? currentEpoch : sourceEpoch;
            ulong maximumSequence = currentSequence > sourceSequence
                ? currentSequence
                : sourceSequence;
            if (maximumEpoch == ulong.MaxValue || maximumSequence == ulong.MaxValue ||
                !CoCoTickFrame.TryCreate(
                    sourceTick.DeltaTime,
                    sourceTick.TimelineId,
                    sourceTick.TimelinePosition,
                    sourceTick.Tick,
                    sourceTick.ClockDomainId,
                    new CoCoExecutionSequence(maximumSequence + 1UL),
                    new CoCoTimelineEpoch(maximumEpoch + 1UL),
                    out resumed,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = HistoryError(
                        CoCoDiagnosticCode.InvalidTimelinePosition,
                        "Temporal restore cannot allocate a strictly newer Epoch and ExecutionSequence.");
                }

                return false;
            }

            return true;
        }

        private static CoCoTemporalFrameInfo ToPublicInfo(
            in CoCoTemporalHistoryEntryInfo info) =>
            info.IsValid
                ? new CoCoTemporalFrameInfo(
                    info.Header.Identity.GraphInstanceId,
                    info.Header.TickFrame,
                    info.Revision,
                    info.Origin)
                : default;

        private static CoCoTemporalFrameInfo ToPublicInfo(CoCoContextFrame frame) =>
            frame.IsAlive
                ? new CoCoTemporalFrameInfo(
                    frame.Header.Identity.GraphInstanceId,
                    frame.Header.TickFrame,
                    frame.Revision,
                    frame.Origin)
                : default;

        private static bool IsInsideHostBoundary(
            CoCoStateGraphHost host,
            MonoBehaviour component)
        {
            if (host == null || component == null)
            {
                return false;
            }

            Transform current = component.transform;
            while (current != null)
            {
                CoCoStateGraphHost boundary = current.GetComponent<CoCoStateGraphHost>();
                if (boundary != null)
                {
                    return ReferenceEquals(boundary, host);
                }

                current = current.parent;
            }

            return false;
        }

        private static CoCoDiagnostic ConfigurationError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.InvalidActorBinding,
                message);

        private static CoCoDiagnostic HistoryError(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                code == CoCoDiagnosticCode.None
                    ? CoCoDiagnosticCode.InvalidRestoreMetadata
                    : code,
                message);

        private static CoCoDiagnostic BindingError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.WorldCorrectionRequired,
                message);

        private static CoCoDiagnostic MailboxError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Mailbox,
                CoCoDiagnosticCode.MailboxUnavailable,
                message);

        private static CoCoDiagnostic TemporalLifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);
    }
}
