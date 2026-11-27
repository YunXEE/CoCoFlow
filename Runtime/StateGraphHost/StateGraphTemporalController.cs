using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    internal sealed class StateGraphTemporalController :
        IContextRestoreReadSource,
        IDisposable
    {
        private readonly StateGraphHost _host;
        private readonly ContextFrameLayout _layout;
        private readonly StateGraphTransaction _transaction;
        private readonly MonoBehaviour _bindingComponent;
        private readonly IContextRestoreBinding _binding;
        private readonly IStateGraphTemporalParticipant _participant;
        private readonly TemporalHistory _history;
        private readonly ContextRestoreReadLease _readLease;
        private bool _participantAttached;
        private TemporalMode _mode;
        private TemporalFrameInfo _previewInfo;
        private ContextFrameReadView _activeAuthorityView;
        private ContextRestoreReadView _activeRestoreView;
        private ulong _nextReadToken;
        private ulong _activeReadToken;
        private int _previewDepth;
        private bool _hasAppliedPreviewProjection;
        private bool _readsRestoreCandidate;
        private bool _isDisposed;

        private StateGraphTemporalController(
            StateGraphHost host,
            ContextFrameLayout layout,
            StateGraphTransaction transaction,
            MonoBehaviour bindingComponent,
            IContextRestoreBinding binding,
            IStateGraphTemporalParticipant participant,
            bool participantAttached,
            TemporalHistory history)
        {
            _host = host;
            _layout = layout;
            _transaction = transaction;
            _bindingComponent = bindingComponent;
            _binding = binding;
            _participant = participant;
            _participantAttached = participantAttached;
            _history = history;
            _readLease = new ContextRestoreReadLease();
            _mode = history == null
                ? TemporalMode.Disabled
                : TemporalMode.Ready;
        }

        internal TemporalMode Mode => _mode;

        internal TemporalState State
        {
            get
            {
                TemporalFrameInfo current = ToPublicInfo(_transaction.CurrentContext);
                TemporalFrameInfo preview = _mode == TemporalMode.Previewing &&
                                                    _previewDepth > 0
                    ? _previewInfo
                    : current;
                bool canConfirm = _mode == TemporalMode.Previewing &&
                                  _previewDepth > 0 &&
                                  _history != null &&
                                  _previewDepth < _history.Count &&
                                  !_host.Fault.IsFaulted &&
                                  IsBindingLive &&
                                  (_participant == null ||
                                   _participant.CanConfirmPreview(_previewDepth));
                return new TemporalState(
                    _mode,
                    _history?.Capacity ?? 0,
                    _history?.Count ?? 0,
                    _previewDepth,
                    current,
                    preview,
                    0UL,
                    canConfirm);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Copies only immutable metadata from the Temporal ring. This path
        /// never selects or decodes a retained payload and never creates a
        /// Context read token.
        /// </summary>
        internal bool TryCaptureDebugFrameInfos(
            out int capacity,
            out TemporalFrameInfo[] frames)
        {
            capacity = _history?.Capacity ?? 0;
            frames = Array.Empty<TemporalFrameInfo>();
            if (_isDisposed)
            {
                return false;
            }

            if (_history == null)
            {
                return true;
            }

            int count = _history.Count;
            if (count == 0)
            {
                return true;
            }

            var copy = new TemporalFrameInfo[count];
            for (int depth = 0; depth < count; depth++)
            {
                if (!_history.TryGetInfo(
                        depth,
                        out TemporalHistoryEntryInfo info))
                {
                    return false;
                }

                copy[depth] = ToPublicInfo(info);
                if (!copy[depth].IsValid)
                {
                    return false;
                }
            }

            frames = copy;
            return true;
        }
#endif

        internal static bool TryValidateConfiguration(
            StateGraphHost host,
            ContextFrameLayout layout,
            ContextCodecRegistry codecs,
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

            if (bindingComponent is IOperator)
            {
                diagnostic = ConfigurationError("Operator recovery must use the Host Operator switches.");
                return false;
            }
            if (!ReferenceEquals(bindingComponent, null) &&
                (capacity > 0 || (bindingComponent != null &&
                 bindingComponent is IContextRestoreBinding &&
                 StateGraphHostBoundary.Contains(host, bindingComponent))) &&
                !TemporalDecoratorChain.TryValidate(host, bindingComponent, out diagnostic))
            {
                return false;
            }

            ContextProjectionCodec codec = null;
            if (capacity > 0 &&
                !ContextProjectionCodec.TryCreate(
                    layout,
                    codecs,
                    ContextProjection.Temporal,
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
            StateGraphHost host,
            ContextFrameLayout layout,
            ContextCodecRegistry codecs,
            StateGraphTransaction transaction,
            MonoBehaviour bindingComponent,
            int capacity,
            out StateGraphTemporalController controller,
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

            TemporalHistory history = null;
            try
            {
                if (capacity > 0 &&
                    !TemporalHistory.TryCreate(
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
                    bindingComponent is IContextRestoreBinding &&
                    StateGraphHostBoundary.Contains(host, bindingComponent)
                        ? bindingComponent
                        : null;
                var participant =
                    activeBindingComponent as IStateGraphTemporalParticipant;
                bool participantAttached = false;
                if (capacity > 0 && participant != null)
                {
                    try
                    {
                        participantAttached =
                            participant.TryAttachTemporalHost(
                                host,
                                capacity,
                                out diagnostic);
                    }
                    catch (Exception)
                    {
                        TryDetachParticipantNoFail(participant);
                        diagnostic = HistoryError(
                            CoCoDiagnosticCode.CommitPreparationFailed,
                            "Temporal participant attachment threw before Host publication.");
                    }

                    if (!participantAttached || diagnostic.IsError)
                    {
                        if (participantAttached)
                        {
                            TryDetachParticipantNoFail(participant);
                        }

                        history?.Dispose();
                        return false;
                    }
                }

                controller = new StateGraphTemporalController(
                    host,
                    layout,
                    transaction,
                    activeBindingComponent,
                    activeBindingComponent as IContextRestoreBinding,
                    participant,
                    participantAttached,
                    history);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception)
            {
                TryDetachParticipantNoFail(
                    bindingComponent as IStateGraphTemporalParticipant);
                history?.Dispose();
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal history setup threw before Host publication.");
                return false;
            }
        }

        internal bool TryPrepareCapture(
            in FinalizedContextCommit candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (_history == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                if (!_history.TryPrepareCapture(
                        candidate,
                        out CoCoDiagnosticCode diagnosticCode))
                {
                    _history.CancelPreparedCapture();
                    diagnostic = HistoryError(
                        diagnosticCode,
                        "Temporal projection capture rejected the finalized Context candidate.");
                    return false;
                }

                if (_participant != null)
                {
                    diagnostic = CoCoDiagnostic.None;
                    if (!_participant.IsTemporalParticipantLive(_host) ||
                        !candidate.TryGetMetadata(
                            out StateFlowFrameHeader header,
                            out ContextRevision revision,
                            out ContextFrameOrigin origin) ||
                        !_participant.TryPrepareForwardCapture(
                            new TemporalFrameInfo(
                                header.Identity.GraphInstanceId,
                                header.TickFrame,
                                revision,
                                origin),
                            out diagnostic))
                    {
                        _participant.CancelPreparedCaptureNoFail();
                        _history.CancelPreparedCapture();
                        if (!diagnostic.IsError)
                        {
                            diagnostic = HistoryError(
                                CoCoDiagnosticCode.CommitPreparationFailed,
                                "Temporal participant rejected the finalized capture candidate.");
                        }

                        return false;
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception)
            {
                _participant?.CancelPreparedCaptureNoFail();
                _history.CancelPreparedCapture();
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal projection capture threw before the authority barrier.");
                return false;
            }
        }

        internal void PublishCaptureNoFail()
        {
            if (_history == null)
            {
                return;
            }

            _history.PublishCaptureNoFail();
            _participant?.PublishForwardCaptureNoFail();
        }

        internal void CancelPreparedCapture()
        {
            if (_history == null)
            {
                return;
            }

            _history.CancelPreparedCapture();
            _participant?.CancelPreparedCaptureNoFail();
        }

        internal void DrainPublishedCleanupNoFail()
        {
            if (_history == null)
            {
                return;
            }

            DrainParticipantCleanupNoFail();
        }

        private void DrainParticipantCleanupNoFail()
        {
            try
            {
                _participant?.DrainPublishedCleanupNoFail();
            }
            catch (Exception)
            {
                // Post-barrier cleanup is diagnostics-only and cannot roll back authority.
            }
        }

        internal bool TryImportPersistence(
            StateGraphRuntime runtime,
            StateGraphPersistencePayloadCodec codec,
            in StateGraphPersistenceEnvelope envelope,
            in ProjectionRestoreSource persistedSource,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_isDisposed ||
                runtime == null ||
                codec == null ||
                !envelope.IsValid ||
                !persistedSource.IsValid ||
                runtime.IsFaulted ||
                (runtime.Lifecycle != RuntimeLifecycleState.Running &&
                 runtime.Lifecycle != RuntimeLifecycleState.Suspended) ||
                _mode == TemporalMode.Previewing)
            {
                diagnostic = TemporalLifecycleError(
                    "Persistence import requires one healthy Running or Suspended Host outside Temporal preview.");
                return false;
            }

            if (!StateGraphPersistencePayloadCodec.TryCreateImportedTickFrame(
                    runtime.Clock,
                    envelope,
                    persistedSource,
                    out TickFrame importedTickFrame,
                    out diagnostic) ||
                !StateGraphPersistencePayloadCodec.TryCreatePersistedSourceInfo(
                    envelope,
                    persistedSource,
                    out TemporalFrameInfo persistedInfo,
                    out diagnostic))
            {
                return false;
            }

            _host.RecoveryProjection = ContextProjection.Durable;
            if ((_binding != null &&
                (!IsBindingComponentLive ||
                 !TryEnsurePersistenceParticipantAttached(out diagnostic) ||
                 !TryRequireLiveBinding(out diagnostic))) || !_host.RecoveryOperatorsLive())
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = ConfigurationError(
                        "Persistence import requires its original live Restore Binding inside the Host boundary.");
                }

                return false;
            }

            if (!_transaction.TryPreparePersistenceRestore(
                    runtime,
                    codec.Projection,
                    envelope.DurablePayload,
                    importedTickFrame,
                    _history,
                    out PreparedActorRestore preparedRestore,
                    out ContextRestoreReadView restoreSource,
                    out _,
                    out TemporalFrameInfo targetAuthority,
                    out _,
                    out diagnostic))
            {
                _history?.CancelPreparedCapture();
                return false;
            }

            try
            {
                if (_participant != null &&
                    (!_participant.IsTemporalParticipantLive(_host) ||
                     !_participant.TryPrepareAuthorityReset(
                         targetAuthority,
                         out diagnostic)))
                {
                    _participant.CancelPreparedAuthorityResetNoFail();
                    _history?.CancelPreparedCapture();
                    preparedRestore.Cancel();
                    if (!diagnostic.IsError)
                    {
                        diagnostic = HistoryError(
                            CoCoDiagnosticCode.CommitPreparationFailed,
                            "Temporal participant rejected the imported authority baseline.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                _participant?.CancelPreparedAuthorityResetNoFail();
                _history?.CancelPreparedCapture();
                preparedRestore.Cancel();
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal participant authority reset threw before the Persistence authority barrier.");
                return false;
            }

            bool worldMayBeDirty = false;
            if (!TryApplyRestore(
                    ContextRestoreApplyKind.Confirm,
                    persistedInfo,
                    importedTickFrame,
                    restoreSource,
                    out worldMayBeDirty,
                    out diagnostic))
            {
                _participant?.CancelPreparedAuthorityResetNoFail();
                _history?.CancelPreparedCapture();
                preparedRestore.Cancel();
                if (worldMayBeDirty)
                {
                    _host.LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            if (_host.IsTemporalOperationCancellationRequested ||
                !preparedRestore.IsValid ||
                (_history != null && !_history.HasPreparedCapture) ||
                (_participant != null &&
                 !_participant.IsTemporalParticipantLive(_host)) ||
                !IsBindingLive)
            {
                _participant?.CancelPreparedAuthorityResetNoFail();
                _history?.CancelPreparedCapture();
                preparedRestore.Cancel();
                diagnostic = TemporalLifecycleError(
                    "Persistence import proof changed during Unity projection before authority publication.");
                if (worldMayBeDirty)
                {
                    _host.LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            preparedRestore.CommitNoFail();
            _history?.PublishAuthorityResetNoFail();
            _participant?.CommitPreparedAuthorityResetNoFail();
            _mode = _history == null
                ? TemporalMode.Disabled
                : TemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = ToPublicInfo(_transaction.CurrentContext);
            DrainParticipantCleanupNoFail();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryBegin(
            StateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, TemporalMode.Ready, out diagnostic) ||
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

            _host.RecoveryProjection = ContextProjection.Temporal;
            if (!TryRequireLiveBinding(out diagnostic))
            {
                return false;
            }

            if (_participant != null &&
                !_participant.TryBeginPreview(_history.Count, out diagnostic))
            {
                return false;
            }

            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = ToPublicInfo(_transaction.CurrentContext);
            _mode = TemporalMode.Previewing;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPreview(
            StateGraphRuntime runtime,
            int historyDepth,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, TemporalMode.Previewing, out diagnostic) ||
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
            TemporalFrameInfo previewInfo;
            if (historyDepth == 0)
            {
                ContextFrameReadView authority = _transaction.PreviousContext;
                previewInfo = ToPublicInfo(_transaction.CurrentContext);
                if (_participant != null &&
                    !_participant.TryPrepareProjection(
                        ContextRestoreApplyKind.Preview,
                        historyDepth,
                        previewInfo,
                        previewInfo.TickFrame,
                        out diagnostic))
                {
                    return false;
                }

                applied = TryApplyAuthority(
                    ContextRestoreApplyKind.Preview,
                    previewInfo,
                    previewInfo.TickFrame,
                    authority,
                    out worldMayBeDirty,
                    out diagnostic);
                _participant?.FinishProjectionNoFail(applied);
            }
            else
            {
                TemporalSelection selection;
                try
                {
                    if (!_history.TrySelect(
                            historyDepth,
                            out selection,
                            out CoCoDiagnosticCode diagnosticCode,
                            _host.RetainedRecoverySlots, _transaction.CurrentContext.Buffer))
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
                if (_participant != null &&
                    !_participant.TryPrepareProjection(
                        ContextRestoreApplyKind.Preview,
                        historyDepth,
                        previewInfo,
                        previewInfo.TickFrame,
                        out diagnostic))
                {
                    return false;
                }

                applied = TryApplyRestore(
                    ContextRestoreApplyKind.Preview,
                    previewInfo,
                    previewInfo.TickFrame,
                    selection.RestoreView,
                    out worldMayBeDirty,
                    out diagnostic);
                _participant?.FinishProjectionNoFail(applied);
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
            StateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, TemporalMode.Previewing, out diagnostic) ||
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

            TemporalSelection selection;
            try
            {
                if (!_history.TrySelect(
                        _previewDepth,
                        out selection,
                        out CoCoDiagnosticCode diagnosticCode,
                        _host.RetainedRecoverySlots, _transaction.CurrentContext.Buffer))
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
                    out TickFrame resumedTickFrame,
                    out diagnostic) ||
                !_transaction.TryPrepareTemporalRestore(
                    runtime,
                    _history,
                    selection,
                    resumedTickFrame,
                    out PreparedActorRestore preparedRestore,
                    out ContextRestoreReadView restoreSource,
                    out _,
                    out diagnostic))
            {
                _history.CancelPreparedCapture();
                return false;
            }

            TemporalFrameInfo sourceInfo = ToPublicInfo(selection.Info);
            if (_participant != null &&
                (!_participant.TryPrepareBranchCapture(
                     _previewDepth,
                     sourceInfo,
                     out diagnostic) ||
                 !_participant.TryPrepareProjection(
                     ContextRestoreApplyKind.Confirm,
                     _previewDepth,
                     sourceInfo,
                     resumedTickFrame,
                     out diagnostic)))
            {
                _participant.CancelPreparedCaptureNoFail();
                preparedRestore.Cancel();
                _history.CancelPreparedCapture();
                return false;
            }

            bool applied = TryApplyRestore(
                ContextRestoreApplyKind.Confirm,
                sourceInfo,
                resumedTickFrame,
                restoreSource,
                out _,
                out diagnostic);
            _participant?.FinishProjectionNoFail(applied);
            if (!applied)
            {
                _participant?.CancelPreparedCaptureNoFail();
                preparedRestore.Cancel();
                _history.CancelPreparedCapture();
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            if (_host.IsTemporalOperationCancellationRequested ||
                !preparedRestore.IsValid ||
                !_history.HasPreparedCapture)
            {
                _participant?.CancelPreparedCaptureNoFail();
                preparedRestore.Cancel();
                _history.CancelPreparedCapture();
                diagnostic = TemporalLifecycleError(
                    "Temporal restore proof changed during Unity projection before authority publication.");
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            preparedRestore.CommitNoFail();
            _history.PublishBranchCaptureNoFail();
            _participant?.PublishBranchCaptureNoFail();
            _participant?.CompletePreviewNoFail(ContextRestoreApplyKind.Confirm);
            _mode = TemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = ToPublicInfo(_transaction.CurrentContext);
            DrainPublishedCleanupNoFail();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCancel(
            StateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, TemporalMode.Previewing, out diagnostic))
            {
                return false;
            }

            if (_hasAppliedPreviewProjection &&
                !TryRequireLiveBinding(out diagnostic))
            {
                _host.LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            TemporalFrameInfo current = ToPublicInfo(_transaction.CurrentContext);
            if (_hasAppliedPreviewProjection)
            {
                ContextFrameReadView authority = _transaction.PreviousContext;
                if (_participant != null &&
                    !_participant.TryPrepareProjection(
                        ContextRestoreApplyKind.Cancel,
                        0,
                        current,
                        current.TickFrame,
                        out diagnostic))
                {
                    return false;
                }

                bool applied = TryApplyAuthority(
                        ContextRestoreApplyKind.Cancel,
                        current,
                        current.TickFrame,
                        authority,
                        out _,
                        out diagnostic);
                _participant?.FinishProjectionNoFail(applied);
                if (!applied)
                {
                    _host.LatchWorldCorrectionFault(diagnostic);
                    return false;
                }
            }

            _participant?.CompletePreviewNoFail(ContextRestoreApplyKind.Cancel);
            _mode = TemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = current;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCorrectWorld(
            StateGraphRuntime runtime,
            bool requiresWorldCorrection,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                runtime == null ||
                !requiresWorldCorrection ||
                !runtime.IsFaulted ||
                (runtime.Lifecycle != RuntimeLifecycleState.Running &&
                 runtime.Lifecycle != RuntimeLifecycleState.Suspended))
            {
                diagnostic = TemporalLifecycleError(
                    "World correction requires one live faulted Host with an explicit correction requirement.");
                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                return false;
            }

            if (!_host.RecoveryProjection.HasValue && !_host.RecoveryOperatorsLive(requireCoverage: true))
            {
                diagnostic = BindingError("Tick fault correction requires enabled recovery for every bound Operator.");
                return false;
            }

            if (!runtime.TryPrepareTemporalRecovery(
                    out PreparedTemporalRecovery recovery,
                    out diagnostic))
            {
                return false;
            }

            ContextFrameReadView authority = _transaction.PreviousContext;
            TemporalFrameInfo current = ToPublicInfo(_transaction.CurrentContext);
            if (_participant != null &&
                !_participant.TryPrepareProjection(
                    ContextRestoreApplyKind.Correction,
                    0,
                    current,
                    current.TickFrame,
                    out diagnostic))
            {
                recovery.Cancel();
                return false;
            }

            bool applied = TryApplyAuthority(
                ContextRestoreApplyKind.Correction,
                current,
                current.TickFrame,
                authority,
                out _,
                out diagnostic);
            _participant?.FinishProjectionNoFail(applied);
            if (!applied)
            {
                recovery.Cancel();
                return false;
            }

            if (_host.IsTemporalOperationCancellationRequested ||
                !recovery.IsValid)
            {
                recovery.Cancel();
                diagnostic = TemporalLifecycleError(
                    "World correction proof changed during Unity projection.");
                return false;
            }

            recovery.CompleteNoFail();
            _host.ClearWorldCorrectionRequirementNoFail();
            _participant?.CompletePreviewNoFail(
                ContextRestoreApplyKind.Correction);
            _mode = _history == null
                ? TemporalMode.Disabled
                : TemporalMode.Ready;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _previewInfo = current;
            DrainPublishedCleanupNoFail();
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
            if (_participantAttached)
            {
                TryDetachParticipantNoFail(_participant);
                _participantAttached = false;
            }

            _history?.Dispose();
            _previewInfo = default;
            _previewDepth = 0;
            _hasAppliedPreviewProjection = false;
            _mode = TemporalMode.Disabled;
        }

        bool IContextRestoreReadSource.IsReadActive(ulong token) =>
            !_isDisposed && token != 0UL && token == _activeReadToken;

        bool IContextRestoreReadSource.TryRead<TValue>(
            ulong token,
            StateSlotId slotId,
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
                        out StateSlot<TValue> slot))
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
            ContextRestoreApplyKind applyKind,
            in TemporalFrameInfo source,
            in TickFrame targetTickFrame,
            in ContextFrameReadView authority,
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
            ContextRestoreApplyKind applyKind,
            in TemporalFrameInfo source,
            in TickFrame targetTickFrame,
            in ContextRestoreReadView restore,
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
            ContextRestoreApplyKind applyKind,
            in TemporalFrameInfo source,
            in TickFrame targetTickFrame,
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

            var context = new ContextRestoreBindingContext(
                applyKind,
                source,
                targetTickFrame,
                new ContextRestoreReader(_readLease, token));
            bool applied;
            try
            {
                applied = _host.TryRestoreOperators(context, ref worldMayBeDirty, out diagnostic);
                if (applied && _binding != null)
                {
                    worldMayBeDirty = true;
                    applied = _binding.TryApply(context, out diagnostic);
                }
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
            StateGraphRuntime runtime,
            TemporalMode requiredMode,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                runtime == null ||
                runtime.Lifecycle != RuntimeLifecycleState.Running ||
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

        private bool IsBindingLive => _host.RecoveryOperatorsLive() &&
            (_binding == null || (IsBindingComponentLive &&
            (!_participantAttached || IsParticipantLiveNoThrow())));

        private bool IsBindingComponentLive =>
            _bindingComponent != null &&
            StateGraphHostBoundary.Contains(_host, _bindingComponent);

        private bool TryEnsurePersistenceParticipantAttached(
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_participant == null)
            {
                return true;
            }

            if (_participantAttached)
            {
                if (IsParticipantLiveNoThrow())
                {
                    return true;
                }

                diagnostic = ConfigurationError(
                    "Persistence Restore Binding participant is no longer attached to its exact Host.");
                return false;
            }

            bool attached;
            try
            {
                attached = _participant.TryAttachTemporalHost(
                    _host,
                    0,
                    out diagnostic);
            }
            catch (Exception)
            {
                TryDetachParticipantNoFail(_participant);
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Persistence Restore Binding participant attachment threw before the Inbox barrier.");
                return false;
            }

            if (!attached || diagnostic.IsError)
            {
                if (attached)
                {
                    TryDetachParticipantNoFail(_participant);
                }

                if (!diagnostic.IsError)
                {
                    diagnostic = HistoryError(
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        "Persistence Restore Binding participant rejected reset-only attachment.");
                }

                return false;
            }

            _participantAttached = true;
            if (IsParticipantLiveNoThrow())
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            TryDetachParticipantNoFail(_participant);
            _participantAttached = false;
            diagnostic = ConfigurationError(
                "Persistence Restore Binding participant became invalid during reset-only attachment.");
            return false;
        }

        private bool IsParticipantLiveNoThrow()
        {
            try
            {
                return _participant != null &&
                       _participant.IsTemporalParticipantLive(_host);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryDetachParticipantNoFail(
            IStateGraphTemporalParticipant participant)
        {
            try
            {
                participant?.DetachTemporalHostNoFail();
            }
            catch (Exception)
            {
                // Attachment rollback and Host teardown cannot be rejected.
            }
        }

        private void ClearActiveRead()
        {
            _readLease.Detach();
            _activeReadToken = 0UL;
            _activeAuthorityView = default;
            _activeRestoreView = default;
            _readsRestoreCandidate = false;
        }

        private static bool TryCreateResumedTickFrame(
            StateGraphRuntime runtime,
            in TemporalHistoryEntryInfo source,
            out TickFrame resumed,
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

            TickFrame sourceTick = source.Header.TickFrame;
            ulong currentEpoch = runtime.Clock.TimelineEpoch.Value;
            ulong currentSequence = runtime.Clock.ExecutionSequence.Value;
            ulong sourceEpoch = sourceTick.TimelineEpoch.Value;
            ulong sourceSequence = sourceTick.ExecutionSequence.Value;
            ulong maximumEpoch = currentEpoch > sourceEpoch ? currentEpoch : sourceEpoch;
            ulong maximumSequence = currentSequence > sourceSequence
                ? currentSequence
                : sourceSequence;
            if (maximumEpoch == ulong.MaxValue || maximumSequence == ulong.MaxValue ||
                !TickFrame.TryCreate(
                    sourceTick.DeltaTime,
                    sourceTick.TimelineId,
                    sourceTick.TimelinePosition,
                    sourceTick.Tick,
                    sourceTick.ClockDomainId,
                    new ExecutionSequence(maximumSequence + 1UL),
                    new TimelineEpoch(maximumEpoch + 1UL),
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

        private static TemporalFrameInfo ToPublicInfo(
            in TemporalHistoryEntryInfo info) =>
            info.IsValid
                ? new TemporalFrameInfo(
                    info.Header.Identity.GraphInstanceId,
                    info.Header.TickFrame,
                    info.Revision,
                    info.Origin)
                : default;

        private static TemporalFrameInfo ToPublicInfo(ContextFrame frame) =>
            frame.IsAlive
                ? new TemporalFrameInfo(
                    frame.Header.Identity.GraphInstanceId,
                    frame.Header.TickFrame,
                    frame.Revision,
                    frame.Origin)
                : default;

        private static CoCoDiagnostic ConfigurationError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.InvalidHostConfiguration,
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

        private static CoCoDiagnostic TemporalLifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);
    }
}
