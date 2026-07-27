using System;
using CoCoFlow.Runtime.Pooling;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling.Temporal
{
    [DisallowMultipleComponent]
    public sealed class CoCoPoolTemporalBinding :
        MonoBehaviour,
        ICoCoContextRestoreBinding,
        ICoCoStateGraphTemporalParticipant,
        ICoCoTemporalDecoratorBinding
    {
        [SerializeField] private CoCoStateGraphHost stateGraphHost;
        [SerializeField] private CoCoPoolHost poolHost;
        [SerializeField] private MonoBehaviour downstreamRestoreBinding;

        private PoolTemporalRuntime _runtime;
        private CoCoStateGraphHost _attachedStateGraphHost;
        private PoolRuntime _attachedPoolRuntime;
        private bool _resetOnlyAttachment;
        private bool _resetOnlyAuthorityResetPrepared;
        private bool _downstreamWasConfigured;
        private MonoBehaviour _attachedDownstreamComponent;
        private ICoCoContextRestoreBinding _attachedDownstreamBinding;
        private CoCoDiagnostic _lastDiagnostic;

        public CoCoStateGraphHost StateGraphHost => stateGraphHost;
        public CoCoPoolHost PoolHost => poolHost;
        MonoBehaviour ICoCoTemporalDecoratorBinding
            .DownstreamRestoreBinding => downstreamRestoreBinding;

        public CoCoDiagnostic LastDiagnostic
        {
            get
            {
                CoCoDiagnostic runtimeDiagnostic = _runtime == null
                    ? CoCoDiagnostic.None
                    : _runtime.LastDiagnostic;
                return runtimeDiagnostic.IsError
                    ? runtimeDiagnostic
                    : _lastDiagnostic;
            }
        }

        public bool TryAdopt(
            CoCoTemporalEntityId entityId,
            ref PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireMutationRuntime(out diagnostic))
            {
                return false;
            }

            bool adopted = _runtime.TryAdopt(
                entityId,
                ref handle,
                out diagnostic);
            _lastDiagnostic = diagnostic;
            return adopted;
        }

        public bool TryActivate(
            CoCoTemporalEntityId entityId,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireMutationRuntime(out diagnostic))
            {
                return false;
            }

            bool activated = _runtime.TryActivate(entityId, out diagnostic);
            _lastDiagnostic = diagnostic;
            return activated;
        }

        public bool TryDespawn(
            CoCoTemporalEntityId entityId,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireMutationRuntime(out diagnostic))
            {
                return false;
            }

            bool despawned = _runtime.TryDespawn(entityId, out diagnostic);
            _lastDiagnostic = diagnostic;
            return despawned;
        }

        public bool TryResolveInstance(
            CoCoTemporalEntityId entityId,
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireRuntime(out diagnostic))
            {
                instance = null;
                return false;
            }

            bool resolved = _runtime.TryResolveInstance(
                entityId,
                out instance,
                out diagnostic);
            _lastDiagnostic = diagnostic;
            return resolved;
        }

        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireAttachment(out diagnostic) ||
                !context.IsValid ||
                (_resetOnlyAttachment
                    ? !_resetOnlyAuthorityResetPrepared ||
                      context.ApplyKind !=
                      CoCoContextRestoreApplyKind.Confirm
                    : (_runtime.IsAuthorityResetPrepared &&
                       context.ApplyKind !=
                       CoCoContextRestoreApplyKind.Confirm)) ||
                !TryValidateFrozenDownstream(
                    _attachedStateGraphHost,
                    out diagnostic) ||
                (!_resetOnlyAttachment &&
                 !_runtime.TryApplyPreparedBeforeRestore(
                     out diagnostic)))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = ProjectionError(
                        "Pool Temporal Restore Binding received an invalid projection context.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!TryValidateFrozenDownstream(
                    _attachedStateGraphHost,
                    out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_downstreamWasConfigured)
            {
                bool applied;
                try
                {
                    applied = _attachedDownstreamBinding.TryApply(
                        context,
                        out diagnostic);
                }
                catch (Exception)
                {
                    applied = false;
                    diagnostic = ProjectionError(
                        "The downstream Restore Binding threw during Pool Temporal projection.");
                }

                if (!TryValidateFrozenDownstream(
                        _attachedStateGraphHost,
                        out CoCoDiagnostic livenessDiagnostic))
                {
                    diagnostic = livenessDiagnostic;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!applied || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = ProjectionError(
                            "The downstream Restore Binding rejected Pool Temporal projection.");
                    }

                    _lastDiagnostic = diagnostic;
                    return false;
                }
            }

            bool completed = _resetOnlyAttachment ||
                             _runtime.TryApplyPreparedAfterRestore(
                                 out diagnostic);
            if (_resetOnlyAttachment)
            {
                diagnostic = CoCoDiagnostic.None;
            }

            _lastDiagnostic = diagnostic;
            return completed;
        }

        private void OnDestroy()
        {
            DetachTemporalHostNoFail();
        }

        bool ICoCoStateGraphTemporalParticipant.TryAttachTemporalHost(
            CoCoStateGraphHost host,
            int historyCapacity,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_runtime != null ||
                _attachedStateGraphHost != null ||
                _attachedPoolRuntime != null ||
                _resetOnlyAttachment ||
                _resetOnlyAuthorityResetPrepared ||
                host == null ||
                !ReferenceEquals(host, stateGraphHost) ||
                historyCapacity < 0 ||
                historyCapacity == 1 ||
                poolHost == null ||
                poolHost.Runtime == null ||
                poolHost.Runtime.IsDisposed ||
                !CoCoStateGraphHostBoundary.Contains(host, this) ||
                !CoCoTemporalDecoratorChain.TryValidate(
                    host,
                    this,
                    out diagnostic) ||
                !TryFreezeDownstream(host, out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = ConflictError(
                        "Pool Temporal Binding requires exact live StateGraph and Pool Hosts plus one valid optional downstream Restore Binding.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            try
            {
                _attachedStateGraphHost = host;
                _attachedPoolRuntime = poolHost.Runtime;
                _resetOnlyAttachment = historyCapacity == 0;
                if (_resetOnlyAttachment)
                {
                    diagnostic = CoCoDiagnostic.None;
                    _lastDiagnostic = diagnostic;
                    return true;
                }

                _runtime = new PoolTemporalRuntime(
                    host,
                    _attachedPoolRuntime,
                    historyCapacity);
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception)
            {
                _runtime?.Dispose();
                _runtime = null;
                _attachedStateGraphHost = null;
                _attachedPoolRuntime = null;
                _resetOnlyAttachment = false;
                _resetOnlyAuthorityResetPrepared = false;
                ClearFrozenDownstream();
                diagnostic = ProjectionError(
                    "Pool Temporal sidecar allocation failed during Host startup.");
                _lastDiagnostic = diagnostic;
                return false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.IsTemporalParticipantLive(
            CoCoStateGraphHost host) =>
            (_resetOnlyAttachment ||
             (_runtime != null && !_runtime.IsDisposed)) &&
            _attachedStateGraphHost != null &&
            ReferenceEquals(host, _attachedStateGraphHost) &&
            ReferenceEquals(stateGraphHost, _attachedStateGraphHost) &&
            _attachedPoolRuntime != null &&
            !_attachedPoolRuntime.IsDisposed &&
            poolHost != null &&
            ReferenceEquals(poolHost.Runtime, _attachedPoolRuntime) &&
            CoCoStateGraphHostBoundary.Contains(
                _attachedStateGraphHost,
                this) &&
            TryValidateFrozenDownstream(
                _attachedStateGraphHost,
                out _);

        bool ICoCoStateGraphTemporalParticipant.TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireRuntime(out diagnostic))
            {
                return false;
            }

            return _runtime.TryPrepareForwardCapture(
                candidate,
                out diagnostic);
        }

        void ICoCoStateGraphTemporalParticipant.PublishForwardCaptureNoFail() =>
            _runtime?.PublishForwardCaptureNoFail();

        void ICoCoStateGraphTemporalParticipant.CancelPreparedCaptureNoFail() =>
            _runtime?.CancelPreparedCaptureNoFail();

        bool ICoCoStateGraphTemporalParticipant.TryPrepareAuthorityReset(
            in CoCoTemporalFrameInfo targetAuthority,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireAttachment(out diagnostic) ||
                !targetAuthority.IsValid)
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = ConflictError(
                        "Pool Temporal authority reset requires one valid target authority.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_resetOnlyAttachment)
            {
                if (_resetOnlyAuthorityResetPrepared)
                {
                    diagnostic = ConflictError(
                        "Pool Temporal authority reset is already prepared.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _resetOnlyAuthorityResetPrepared = true;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }

            bool prepared = _runtime.TryPrepareAuthorityReset(
                targetAuthority,
                out diagnostic);
            _lastDiagnostic = diagnostic;
            return prepared;
        }

        void ICoCoStateGraphTemporalParticipant
            .CommitPreparedAuthorityResetNoFail()
        {
            if (_resetOnlyAttachment)
            {
                _resetOnlyAuthorityResetPrepared = false;
                return;
            }

            _runtime?.CommitPreparedAuthorityResetNoFail();
        }

        void ICoCoStateGraphTemporalParticipant
            .CancelPreparedAuthorityResetNoFail()
        {
            if (_resetOnlyAttachment)
            {
                _resetOnlyAuthorityResetPrepared = false;
                return;
            }

            _runtime?.CancelPreparedAuthorityResetNoFail();
        }

        bool ICoCoStateGraphTemporalParticipant.TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireRuntime(out diagnostic))
            {
                return false;
            }

            return _runtime.TryBeginPreview(historyCount, out diagnostic);
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireRuntime(out diagnostic))
            {
                return false;
            }

            return _runtime.TryPrepareProjection(
                applyKind,
                historyDepth,
                source,
                targetTickFrame,
                out diagnostic);
        }

        void ICoCoStateGraphTemporalParticipant.FinishProjectionNoFail(
            bool succeeded) =>
            _runtime?.FinishProjectionNoFail(succeeded);

        bool ICoCoStateGraphTemporalParticipant.CanConfirmPreview(
            int historyDepth) =>
            _runtime != null &&
            _runtime.CanConfirmPreview(historyDepth);

        bool ICoCoStateGraphTemporalParticipant.TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireRuntime(out diagnostic))
            {
                return false;
            }

            return _runtime.TryPrepareBranchCapture(
                historyDepth,
                branchHead,
                out diagnostic);
        }

        void ICoCoStateGraphTemporalParticipant.PublishBranchCaptureNoFail() =>
            _runtime?.PublishBranchCaptureNoFail();

        void ICoCoStateGraphTemporalParticipant.CompletePreviewNoFail(
            CoCoContextRestoreApplyKind applyKind) =>
            _runtime?.CompletePreviewNoFail(applyKind);

        void ICoCoStateGraphTemporalParticipant.DrainPublishedCleanupNoFail() =>
            _runtime?.DrainPublishedCleanupNoFail();

        void ICoCoStateGraphTemporalParticipant.DetachTemporalHostNoFail() =>
            DetachTemporalHostNoFail();

        private bool TryRequireRuntime(out CoCoDiagnostic diagnostic)
        {
            if (!_resetOnlyAttachment &&
                _runtime != null &&
                !_runtime.IsDisposed &&
                TryRequireAttachment(out diagnostic))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            diagnostic = ConflictError(
                "Pool Temporal Binding is not attached to live StateGraph and Pool Hosts.");
            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool TryRequireAttachment(
            out CoCoDiagnostic diagnostic)
        {
            if ((_resetOnlyAttachment ||
                 (_runtime != null && !_runtime.IsDisposed)) &&
                _attachedStateGraphHost != null &&
                ReferenceEquals(
                    stateGraphHost,
                    _attachedStateGraphHost) &&
                _attachedPoolRuntime != null &&
                !_attachedPoolRuntime.IsDisposed &&
                poolHost != null &&
                ReferenceEquals(
                    poolHost.Runtime,
                    _attachedPoolRuntime) &&
                CoCoStateGraphHostBoundary.Contains(
                    _attachedStateGraphHost,
                    this))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            diagnostic = ConflictError(
                "Pool Temporal Binding is not attached to live StateGraph and Pool Hosts.");
            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool TryRequireMutationRuntime(
            out CoCoDiagnostic diagnostic)
        {
            if (TryRequireRuntime(out diagnostic) &&
                TryValidateFrozenDownstream(
                    _attachedStateGraphHost,
                    out diagnostic))
            {
                return true;
            }

            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool TryFreezeDownstream(
            CoCoStateGraphHost host,
            out CoCoDiagnostic diagnostic)
        {
            if (ReferenceEquals(downstreamRestoreBinding, null))
            {
                ClearFrozenDownstream();
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (ReferenceEquals(downstreamRestoreBinding, this) ||
                downstreamRestoreBinding == null ||
                !(downstreamRestoreBinding is ICoCoContextRestoreBinding downstream) ||
                !CoCoStateGraphHostBoundary.Contains(
                    host,
                    downstreamRestoreBinding))
            {
                diagnostic = ConflictError(
                    "Downstream Restore Binding must be a different live component inside the same Host boundary.");
                return false;
            }

            _downstreamWasConfigured = true;
            _attachedDownstreamComponent = downstreamRestoreBinding;
            _attachedDownstreamBinding = downstream;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateFrozenDownstream(
            CoCoStateGraphHost host,
            out CoCoDiagnostic diagnostic)
        {
            bool configuredNow =
                !ReferenceEquals(downstreamRestoreBinding, null);
            if (configuredNow != _downstreamWasConfigured)
            {
                diagnostic = ProjectionError(
                    "The downstream Restore Binding assignment changed after Host attachment.");
                return false;
            }

            if (!_downstreamWasConfigured)
            {
                bool remainsEmpty =
                    ReferenceEquals(_attachedDownstreamComponent, null) &&
                    ReferenceEquals(_attachedDownstreamBinding, null);
                diagnostic = remainsEmpty
                    ? CoCoDiagnostic.None
                    : ProjectionError(
                        "The optional downstream Restore Binding lost its frozen attachment state.");
                return remainsEmpty;
            }

            if (!ReferenceEquals(
                    downstreamRestoreBinding,
                    _attachedDownstreamComponent) ||
                ReferenceEquals(_attachedDownstreamComponent, null) ||
                _attachedDownstreamComponent == null ||
                ReferenceEquals(_attachedDownstreamComponent, this) ||
                ReferenceEquals(_attachedDownstreamBinding, null) ||
                !(_attachedDownstreamComponent is
                    ICoCoContextRestoreBinding currentBinding) ||
                !ReferenceEquals(currentBinding, _attachedDownstreamBinding) ||
                !CoCoStateGraphHostBoundary.Contains(
                    host,
                    _attachedDownstreamComponent))
            {
                diagnostic = ProjectionError(
                    "The original downstream Restore Binding is no longer live inside the Host boundary.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void ClearFrozenDownstream()
        {
            _downstreamWasConfigured = false;
            _attachedDownstreamComponent = null;
            _attachedDownstreamBinding = null;
        }

        private void DetachTemporalHostNoFail()
        {
            try
            {
                _runtime?.Dispose();
            }
            catch (Exception)
            {
                _lastDiagnostic = CleanupError(
                    "Pool Temporal Binding required a terminal teardown fallback.");
            }
            finally
            {
                _runtime = null;
                _attachedStateGraphHost = null;
                _attachedPoolRuntime = null;
                _resetOnlyAttachment = false;
                _resetOnlyAuthorityResetPrepared = false;
                ClearFrozenDownstream();
            }
        }

        private static CoCoDiagnostic ConflictError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolTemporalConflict,
                message);

        private static CoCoDiagnostic ProjectionError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                message);

        private static CoCoDiagnostic CleanupError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolTemporalCleanupFailed,
                message);
    }
}
