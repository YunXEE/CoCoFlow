using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling.Temporal
{
    internal sealed class PoolTemporalRecord
    {
        internal CoCoTemporalEntityId EntityId;
        internal PoolTemporalToken Token;
        internal IPoolTemporalApply[] ApplyCallbacks =
            Array.Empty<IPoolTemporalApply>();
        internal bool AuthorityPresent;
        internal bool ProjectedPresent;
        internal bool BaselinePresent;
        internal bool PreparedInactive;
        internal bool PendingActivation;
        internal bool Unavailable;

        internal bool IsRetained => EntityId.IsValid;
    }

    internal sealed class PoolTemporalRuntime : IDisposable
    {
        private readonly CoCoStateGraphHost _stateGraphHost;
        private readonly PoolRuntime _poolRuntime;
        private readonly PoolTemporalSidecar _history;
        private PoolTemporalRecord[] _records = Array.Empty<PoolTemporalRecord>();
        private int _recordCount;
        private CoCoContextRestoreApplyKind _preparedApplyKind;
        private CoCoTemporalFrameInfo _preparedSource;
        private CoCoTickFrame _preparedTargetTickFrame;
        private int _preparedDepth;
        private bool _projectionPrepared;
        private bool _isPreviewing;
        private bool _isDisposed;
        private bool _externalMutationActive;
        private CoCoDiagnostic _lastDiagnostic;

        internal PoolTemporalRuntime(
            CoCoStateGraphHost stateGraphHost,
            PoolRuntime poolRuntime,
            int historyCapacity)
        {
            _stateGraphHost = stateGraphHost;
            _poolRuntime = poolRuntime;
            _history = new PoolTemporalSidecar(historyCapacity);
        }

        internal bool IsDisposed => _isDisposed;
        internal bool IsPreviewing => !_isDisposed && _isPreviewing;
        internal CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        internal bool TryAdopt(
            CoCoTemporalEntityId entityId,
            ref PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterExternalMutation(
                    entityId,
                    true,
                    out diagnostic))
            {
                return false;
            }

            try
            {
                return TryAdoptCore(
                    entityId,
                    ref handle,
                    out diagnostic);
            }
            finally
            {
                _externalMutationActive = false;
            }
        }

        private bool TryAdoptCore(
            CoCoTemporalEntityId entityId,
            ref PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (FindRecord(entityId) != null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal entity identity is already retained by this Host.");
                return RecordFailure(diagnostic);
            }

            if (!handle.TryGetInstance(
                    out GameObject instance,
                    out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            IPoolTemporalApply[] callbacks;
            try
            {
                callbacks = CaptureApplyCallbacks(instance);
                EnsureRecordCapacity(_recordCount + 1);
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal adoption could not allocate its live record.");
                return RecordFailure(diagnostic);
            }

            if (!PoolTemporalAccess.TryAdopt(
                    _poolRuntime,
                    ref handle,
                    out PoolTemporalToken token,
                    out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            _records[_recordCount++] = new PoolTemporalRecord
            {
                EntityId = entityId,
                Token = token,
                ApplyCallbacks = callbacks,
                AuthorityPresent = false,
                ProjectedPresent = false,
                BaselinePresent = false,
                PreparedInactive = false,
                PendingActivation = true,
                Unavailable = false
            };
            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryActivate(
            CoCoTemporalEntityId entityId,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterExternalMutation(
                    entityId,
                    true,
                    out diagnostic))
            {
                return false;
            }

            try
            {
                return TryActivateCore(entityId, out diagnostic);
            }
            finally
            {
                _externalMutationActive = false;
            }
        }

        private bool TryActivateCore(
            CoCoTemporalEntityId entityId,
            out CoCoDiagnostic diagnostic)
        {
            PoolTemporalRecord record = FindRecord(entityId);
            if (record == null ||
                record.Unavailable ||
                record.AuthorityPresent ||
                !record.PendingActivation)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal activation requires one retained inactive entity.");
                return RecordFailure(diagnostic);
            }

            if (!PoolTemporalAccess.TryActivate(
                    ref record.Token,
                    out diagnostic))
            {
                MarkUnavailable(record, diagnostic);
                return false;
            }

            record.AuthorityPresent = true;
            record.ProjectedPresent = true;
            record.BaselinePresent = true;
            record.PreparedInactive = false;
            record.PendingActivation = false;
            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryDespawn(
            CoCoTemporalEntityId entityId,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterExternalMutation(
                    entityId,
                    false,
                    out diagnostic))
            {
                return false;
            }

            try
            {
                return TryDespawnCore(entityId, out diagnostic);
            }
            finally
            {
                _externalMutationActive = false;
            }
        }

        private bool TryDespawnCore(
            CoCoTemporalEntityId entityId,
            out CoCoDiagnostic diagnostic)
        {
            PoolTemporalRecord record = FindRecord(entityId);
            if (record == null ||
                record.Unavailable ||
                !record.AuthorityPresent ||
                record.PendingActivation)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal despawn requires one retained active entity.");
                return RecordFailure(diagnostic);
            }

            if (!PoolTemporalAccess.TryDespawn(
                    ref record.Token,
                    out diagnostic))
            {
                MarkUnavailable(record, diagnostic);
                return false;
            }

            record.AuthorityPresent = false;
            record.ProjectedPresent = false;
            record.BaselinePresent = false;
            record.PreparedInactive = false;
            record.PendingActivation = false;
            _lastDiagnostic = CoCoDiagnostic.None;
            DrainPublishedCleanupNoFail();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryResolveInstance(
            CoCoTemporalEntityId entityId,
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            instance = null;
            if (_isDisposed || !entityId.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal instance resolution requires one attached runtime and valid entity identity.");
                return RecordFailure(diagnostic);
            }

            PoolTemporalRecord record = FindRecord(entityId);
            bool preparedPresent =
                _projectionPrepared && IsDesiredPresent(record);
            if (record == null ||
                record.Unavailable ||
                (!record.ProjectedPresent && !preparedPresent))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                    "Temporal entity does not currently project a physical instance.");
                return RecordFailure(diagnostic);
            }

            if (!PoolTemporalAccess.TryGetInstance(
                    record.Token,
                    out instance,
                    out diagnostic))
            {
                MarkUnavailable(record, diagnostic);
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _isPreviewing ||
                _projectionPrepared ||
                _history.HasPreparedCapture)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal entity capture is not available in the current sidecar state.");
                return RecordFailure(diagnostic);
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null ||
                    !record.IsRetained ||
                    !record.AuthorityPresent)
                {
                    continue;
                }

                if (!TryValidatePhysical(record, out diagnostic))
                {
                    return false;
                }
            }

            try
            {
                if (!_history.TryPrepareForwardCapture(
                        _records,
                        _recordCount,
                        candidate))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                        "Temporal entity presence could not stage the forward capture.");
                    return RecordFailure(diagnostic);
                }
            }
            catch (Exception)
            {
                _history.CancelPreparedCaptureNoFail();
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Temporal entity high-water allocation failed before the authority barrier.");
                return RecordFailure(diagnostic);
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal void PublishForwardCaptureNoFail()
        {
            if (!_isDisposed)
            {
                _history.PublishForwardCaptureNoFail();
            }
        }

        internal void CancelPreparedCaptureNoFail()
        {
            if (!_isDisposed)
            {
                _history.CancelPreparedCaptureNoFail();
            }
        }

        internal bool TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _isPreviewing ||
                _projectionPrepared ||
                _history.HasPreparedCapture ||
                !_history.IsAlignedWith(historyCount))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Pool Temporal history is not aligned with the Host history.");
                return RecordFailure(diagnostic);
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null || !record.IsRetained)
                {
                    continue;
                }

                if (record.AuthorityPresent &&
                    !TryValidatePhysical(record, out diagnostic))
                {
                    return false;
                }

                record.BaselinePresent = record.AuthorityPresent;
                record.ProjectedPresent = record.AuthorityPresent;
            }

            _isPreviewing = true;
            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                (!_isPreviewing &&
                 applyKind != CoCoContextRestoreApplyKind.Correction) ||
                _projectionPrepared ||
                !source.IsValid ||
                !targetTickFrame.IsValid ||
                !TryValidateProjectionDepth(applyKind, historyDepth))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Pool Temporal projection metadata is invalid for the current Host state.");
                return RecordFailure(diagnostic);
            }

            _preparedApplyKind = applyKind;
            _preparedDepth = historyDepth;
            _preparedSource = source;
            _preparedTargetTickFrame = targetTickFrame;
            _projectionPrepared = true;

            if (!TryValidateDesiredRecords(out diagnostic))
            {
                ClearPreparedProjection();
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryApplyPreparedBeforeRestore(
            out CoCoDiagnostic diagnostic)
        {
            if (!_projectionPrepared)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Restore Binding did not receive one prepared Pool Temporal projection.");
                return RecordFailure(diagnostic);
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null || !record.IsRetained)
                {
                    continue;
                }

                bool desiredPresent = IsDesiredPresent(record);
                if (desiredPresent)
                {
                    if (record.ProjectedPresent || record.PreparedInactive)
                    {
                        continue;
                    }

                    if (!PoolTemporalAccess.TryPreparePresence(
                            ref record.Token,
                            true,
                            out diagnostic))
                    {
                        MarkUnavailable(record, diagnostic);
                        return false;
                    }

                    record.PreparedInactive = true;
                    continue;
                }

                if (!record.ProjectedPresent && !record.PreparedInactive)
                {
                    continue;
                }

                if (TargetsRecoverableAbsence(record, desiredPresent) &&
                    !PoolTemporalAccess.TryGetInstance(
                        record.Token,
                        out _,
                        out CoCoDiagnostic physicalDiagnostic))
                {
                    MarkUnavailable(record, physicalDiagnostic);
                    diagnostic = CoCoDiagnostic.None;
                    continue;
                }

                if (!PoolTemporalAccess.TryPreparePresence(
                        ref record.Token,
                        false,
                        out diagnostic))
                {
                    MarkUnavailable(record, diagnostic);
                    return false;
                }

                record.ProjectedPresent = false;
                record.PreparedInactive = false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryApplyPreparedAfterRestore(
            out CoCoDiagnostic diagnostic)
        {
            if (!_projectionPrepared)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Restore Binding lost its prepared Pool Temporal projection.");
                return RecordFailure(diagnostic);
            }

            PoolTemporalApplyKind applyKind =
                (PoolTemporalApplyKind)_preparedApplyKind;
            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null ||
                    !record.IsRetained ||
                    record.Unavailable ||
                    record.PendingActivation)
                {
                    continue;
                }

                bool desiredPresent = IsDesiredPresent(record);
                var context = new PoolTemporalApplyContext(
                    record.EntityId,
                    applyKind,
                    _preparedSource,
                    _preparedTargetTickFrame,
                    desiredPresent);
                if (!TryInvokeApplyCallbacks(record, context, out diagnostic))
                {
                    return false;
                }
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null ||
                    !record.IsRetained ||
                    record.ProjectedPresent ||
                    !IsDesiredPresent(record))
                {
                    continue;
                }

                if (!PoolTemporalAccess.TryActivate(
                        ref record.Token,
                        out diagnostic))
                {
                    MarkUnavailable(record, diagnostic);
                    return false;
                }

                record.ProjectedPresent = true;
                record.PreparedInactive = false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal void FinishProjectionNoFail(bool succeeded)
        {
            if (_isDisposed)
            {
                return;
            }

            if (!succeeded && _lastDiagnostic.IsNone)
            {
                _lastDiagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Pool Temporal projection failed after Unity state may have changed.");
            }

            ClearPreparedProjection();
        }

        internal bool CanConfirmPreview(int historyDepth)
        {
            if (_isDisposed ||
                !_isPreviewing ||
                historyDepth <= 0 ||
                historyDepth >= _history.Count)
            {
                return false;
            }

            int desiredCount = _history.GetEntityCountAtDepth(historyDepth);
            for (int index = 0; index < desiredCount; index++)
            {
                if (!_history.TryGetEntityAtDepth(
                        historyDepth,
                        index,
                        out CoCoTemporalEntityId entityId))
                {
                    return false;
                }

                PoolTemporalRecord record = FindRecord(entityId);
                if (record == null || record.Unavailable)
                {
                    return false;
                }
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null || !record.ProjectedPresent)
                {
                    continue;
                }

                if (record.Unavailable)
                {
                    return false;
                }
            }

            return true;
        }

        internal bool TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                !_isPreviewing ||
                _projectionPrepared)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Pool Temporal sidecar could not stage the restored branch head.");
                return RecordFailure(diagnostic);
            }

            try
            {
                if (!_history.TryPrepareBranchCapture(
                        historyDepth,
                        branchHead))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                        "Pool Temporal sidecar could not stage the restored branch head.");
                    return RecordFailure(diagnostic);
                }
            }
            catch (Exception)
            {
                _history.CancelPreparedCaptureNoFail();
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                    "Pool Temporal branch staging failed before the authority barrier.");
                return RecordFailure(diagnostic);
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal void PublishBranchCaptureNoFail()
        {
            if (_isDisposed)
            {
                return;
            }

            _history.PublishBranchCaptureNoFail();
            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record != null && record.IsRetained)
                {
                    record.AuthorityPresent =
                        _history.ContainsAtDepth(0, record.EntityId);
                }
            }
        }

        internal void CompletePreviewNoFail(
            CoCoContextRestoreApplyKind applyKind)
        {
            if (_isDisposed)
            {
                return;
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null || !record.IsRetained)
                {
                    continue;
                }

                record.ProjectedPresent = record.AuthorityPresent;
                record.BaselinePresent = record.AuthorityPresent;
                record.PreparedInactive = false;
                if (applyKind == CoCoContextRestoreApplyKind.Confirm &&
                    !record.AuthorityPresent)
                {
                    record.PendingActivation = false;
                }
            }

            _isPreviewing = false;
            ClearPreparedProjection();
        }

        internal void DrainPublishedCleanupNoFail()
        {
            if (_isDisposed || _isPreviewing || _history.HasPreparedCapture)
            {
                return;
            }

            for (int index = _recordCount - 1; index >= 0; index--)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null ||
                    record.AuthorityPresent ||
                    record.ProjectedPresent ||
                    record.PendingActivation ||
                    _history.IsReachable(record.EntityId))
                {
                    continue;
                }

                CoCoDiagnostic diagnostic;
                if (!record.Unavailable &&
                    PoolTemporalAccess.TryRelease(
                        ref record.Token,
                        out diagnostic))
                {
                    RemoveRecordAt(index);
                    continue;
                }

                PoolTemporalAccess.ForceDestroy(
                    ref record.Token,
                    out _);
                _lastDiagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalCleanupFailed,
                    "Temporal physical cleanup required the terminal fallback.");
                RemoveRecordAt(index);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            for (int index = _recordCount - 1; index >= 0; index--)
            {
                PoolTemporalRecord record = _records[index];
                if (record != null)
                {
                    bool released = false;
                    if (!record.Unavailable && !_externalMutationActive)
                    {
                        try
                        {
                            released = PoolTemporalAccess.TryRelease(
                                ref record.Token,
                                out _);
                        }
                        catch (Exception)
                        {
                            released = false;
                        }
                    }

                    if (!released)
                    {
                        PoolTemporalAccess.ForceDestroy(
                            ref record.Token,
                            out _);
                    }
                }

                _records[index] = null;
            }

            _recordCount = 0;
            _history.Dispose();
            ClearPreparedProjection();
            _isPreviewing = false;
        }

        private bool TryEnterExternalMutation(
            CoCoTemporalEntityId entityId,
            bool requiresOpenPoolRuntime,
            out CoCoDiagnostic diagnostic)
        {
            if (_externalMutationActive)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Another Temporal entity mutation is already in progress.");
                return RecordFailure(diagnostic);
            }

            if (_isDisposed ||
                !entityId.IsValid ||
                _stateGraphHost == null ||
                _stateGraphHost.Lifecycle != CoCoRuntimeLifecycleState.Running ||
                _stateGraphHost.IsRuntimeFaulted ||
                _isPreviewing ||
                _projectionPrepared ||
                _history.HasPreparedCapture ||
                (requiresOpenPoolRuntime &&
                 (_poolRuntime == null ||
                  _poolRuntime.IsShuttingDown ||
                  _poolRuntime.IsDisposed)))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalConflict,
                    "Temporal entity mutation is frozen by lifecycle, capture, or preview authority.");
                return RecordFailure(diagnostic);
            }

            _externalMutationActive = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateProjectionDepth(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth)
        {
            if (applyKind == CoCoContextRestoreApplyKind.Preview)
            {
                return historyDepth >= 0 && historyDepth < _history.Count;
            }

            if (applyKind == CoCoContextRestoreApplyKind.Confirm)
            {
                return historyDepth > 0 && historyDepth < _history.Count;
            }

            return historyDepth == 0;
        }

        private bool TryValidateDesiredRecords(out CoCoDiagnostic diagnostic)
        {
            if (UsesHistoryFrame)
            {
                int desiredCount =
                    _history.GetEntityCountAtDepth(_preparedDepth);
                for (int index = 0; index < desiredCount; index++)
                {
                    if (!_history.TryGetEntityAtDepth(
                            _preparedDepth,
                            index,
                            out CoCoTemporalEntityId entityId) ||
                        FindRecord(entityId) == null)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                            "A historical Temporal entity no longer has its retained physical record.");
                        return RecordFailure(diagnostic);
                    }
                }
            }

            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record == null || !record.IsRetained)
                {
                    continue;
                }

                bool desiredPresent = IsDesiredPresent(record);
                if ((desiredPresent ||
                     record.ProjectedPresent ||
                     record.PreparedInactive) &&
                    !TryValidatePhysical(record, out diagnostic))
                {
                    if (CanTreatUnavailableAsDesiredAbsence(
                            record,
                            desiredPresent))
                    {
                        diagnostic = CoCoDiagnostic.None;
                        continue;
                    }

                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidatePhysical(
            PoolTemporalRecord record,
            out CoCoDiagnostic diagnostic)
        {
            if (record.Unavailable)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                    "Temporal physical identity is unavailable.");
                return RecordFailure(diagnostic);
            }

            if (!PoolTemporalAccess.TryGetInstance(
                    record.Token,
                    out _,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                        "Temporal physical identity is unavailable.");
                }

                MarkUnavailable(record, diagnostic);
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool IsDesiredPresent(PoolTemporalRecord record)
        {
            if (record == null || !record.IsRetained)
            {
                return false;
            }

            if (UsesHistoryFrame)
            {
                return _history.ContainsAtDepth(
                    _preparedDepth,
                    record.EntityId);
            }

            return _preparedApplyKind == CoCoContextRestoreApplyKind.Cancel
                ? record.BaselinePresent
                : record.AuthorityPresent;
        }

        private bool CanTreatUnavailableAsDesiredAbsence(
            PoolTemporalRecord record,
            bool desiredPresent) =>
            record != null &&
            record.Unavailable &&
            TargetsRecoverableAbsence(record, desiredPresent);

        private bool TargetsRecoverableAbsence(
            PoolTemporalRecord record,
            bool desiredPresent) =>
            record != null &&
            !desiredPresent &&
            !record.AuthorityPresent &&
            (_preparedApplyKind == CoCoContextRestoreApplyKind.Cancel ||
             _preparedApplyKind == CoCoContextRestoreApplyKind.Correction);

        private bool UsesHistoryFrame =>
            (_preparedApplyKind == CoCoContextRestoreApplyKind.Preview ||
             _preparedApplyKind == CoCoContextRestoreApplyKind.Confirm) &&
            _preparedDepth > 0;

        private bool TryInvokeApplyCallbacks(
            PoolTemporalRecord record,
            in PoolTemporalApplyContext context,
            out CoCoDiagnostic diagnostic)
        {
            for (int index = 0; index < record.ApplyCallbacks.Length; index++)
            {
                IPoolTemporalApply callback = record.ApplyCallbacks[index];
                if (callback == null ||
                    callback is UnityEngine.Object unityObject &&
                    unityObject == null)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                        "A cached Pool Temporal apply participant was destroyed.");
                    return RecordFailure(diagnostic);
                }

                bool applied;
                try
                {
                    applied = callback.TryApply(context, out diagnostic);
                }
                catch (Exception)
                {
                    applied = false;
                    diagnostic = Error(
                        CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                        "A Pool Temporal apply participant threw during projection.");
                }

                if (!applied || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                            "A Pool Temporal apply participant rejected projection.");
                    }

                    return RecordFailure(diagnostic);
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void MarkUnavailable(
            PoolTemporalRecord record,
            CoCoDiagnostic diagnostic)
        {
            record.Unavailable = true;
            record.ProjectedPresent = false;
            record.PreparedInactive = false;
            record.PendingActivation = false;

            _lastDiagnostic = diagnostic.IsError
                ? diagnostic
                : Error(
                    CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                    "Temporal physical identity became unavailable.");
            if (record.AuthorityPresent)
            {
                _stateGraphHost.LatchWorldCorrectionFault(_lastDiagnostic);
            }
        }

        private bool RecordFailure(CoCoDiagnostic diagnostic)
        {
            _lastDiagnostic = diagnostic;
            return false;
        }

        private PoolTemporalRecord FindRecord(CoCoTemporalEntityId entityId)
        {
            for (int index = 0; index < _recordCount; index++)
            {
                PoolTemporalRecord record = _records[index];
                if (record != null && record.EntityId == entityId)
                {
                    return record;
                }
            }

            return null;
        }

        private void EnsureRecordCapacity(int required)
        {
            if (_records.Length >= required)
            {
                return;
            }

            int capacity;
            if (_records.Length == 0)
            {
                capacity = 4;
            }
            else
            {
                if (_records.Length > int.MaxValue / 2)
                {
                    throw new InvalidOperationException(
                        "Temporal entity record capacity is exhausted.");
                }

                capacity = _records.Length * 2;
            }

            while (capacity < required)
            {
                if (capacity > int.MaxValue / 2)
                {
                    throw new InvalidOperationException(
                        "Temporal entity record capacity is exhausted.");
                }

                capacity *= 2;
            }

            var expanded = new PoolTemporalRecord[capacity];
            Array.Copy(_records, expanded, _recordCount);
            _records = expanded;
        }

        private void RemoveRecordAt(int index)
        {
            int last = _recordCount - 1;
            _records[index] = _records[last];
            _records[last] = null;
            _recordCount = last;
        }

        private void ClearPreparedProjection()
        {
            _preparedApplyKind = default;
            _preparedSource = default;
            _preparedTargetTickFrame = default;
            _preparedDepth = 0;
            _projectionPrepared = false;
        }

        private static IPoolTemporalApply[] CaptureApplyCallbacks(
            GameObject instance)
        {
            MonoBehaviour[] components =
                instance.GetComponentsInChildren<MonoBehaviour>(true);
            int count = 0;
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index] is IPoolTemporalApply)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return Array.Empty<IPoolTemporalApply>();
            }

            var callbacks = new IPoolTemporalApply[count];
            int writeIndex = 0;
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index] is IPoolTemporalApply callback)
                {
                    callbacks[writeIndex++] = callback;
                }
            }

            return callbacks;
        }

        private static CoCoDiagnostic Error(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Pooling,
                code,
                message);
    }
}
