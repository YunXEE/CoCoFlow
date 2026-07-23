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
        ICoCoStateGraphTemporalParticipant
    {
        [SerializeField] private CoCoStateGraphHost stateGraphHost;
        [SerializeField] private CoCoPoolHost poolHost;
        [SerializeField] private MonoBehaviour downstreamRestoreBinding;

        private PoolTemporalRuntime _runtime;
        private PoolRuntime _attachedPoolRuntime;
        private CoCoDiagnostic _lastDiagnostic;

        public CoCoStateGraphHost StateGraphHost => stateGraphHost;
        public CoCoPoolHost PoolHost => poolHost;
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
            if (!TryRequireRuntime(out diagnostic))
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
            if (!TryRequireRuntime(out diagnostic))
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
            if (!TryRequireRuntime(out diagnostic))
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
            if (!TryRequireRuntime(out diagnostic) ||
                !context.IsValid ||
                !_runtime.TryApplyPreparedBeforeRestore(out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = ProjectionError(
                        "Pool Temporal Restore Binding received an invalid projection context.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            if (downstreamRestoreBinding != null)
            {
                if (!(downstreamRestoreBinding is ICoCoContextRestoreBinding downstream) ||
                    !CoCoStateGraphHostBoundary.Contains(
                        stateGraphHost,
                        downstreamRestoreBinding))
                {
                    diagnostic = ProjectionError(
                        "The downstream Restore Binding is no longer live inside the Host boundary.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                bool applied;
                try
                {
                    applied = downstream.TryApply(context, out diagnostic);
                }
                catch (Exception)
                {
                    applied = false;
                    diagnostic = ProjectionError(
                        "The downstream Restore Binding threw during Pool Temporal projection.");
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

            bool completed =
                _runtime.TryApplyPreparedAfterRestore(out diagnostic);
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
                host == null ||
                !ReferenceEquals(host, stateGraphHost) ||
                historyCapacity < 2 ||
                poolHost == null ||
                poolHost.Runtime == null ||
                poolHost.Runtime.IsDisposed ||
                !CoCoStateGraphHostBoundary.Contains(host, this) ||
                !TryValidateDownstream(host, out diagnostic))
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
                _attachedPoolRuntime = poolHost.Runtime;
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
                _attachedPoolRuntime = null;
                diagnostic = ProjectionError(
                    "Pool Temporal sidecar allocation failed during Host startup.");
                _lastDiagnostic = diagnostic;
                return false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.IsTemporalParticipantLive(
            CoCoStateGraphHost host) =>
            _runtime != null &&
            !_runtime.IsDisposed &&
            _attachedPoolRuntime != null &&
            !_attachedPoolRuntime.IsDisposed &&
            ReferenceEquals(host, stateGraphHost) &&
            poolHost != null &&
            ReferenceEquals(poolHost.Runtime, _attachedPoolRuntime) &&
            CoCoStateGraphHostBoundary.Contains(host, this);

        bool ICoCoStateGraphTemporalParticipant.TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic) =>
            _runtime.TryPrepareForwardCapture(candidate, out diagnostic);

        void ICoCoStateGraphTemporalParticipant.PublishForwardCaptureNoFail() =>
            _runtime?.PublishForwardCaptureNoFail();

        void ICoCoStateGraphTemporalParticipant.CancelPreparedCaptureNoFail() =>
            _runtime?.CancelPreparedCaptureNoFail();

        bool ICoCoStateGraphTemporalParticipant.TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic) =>
            _runtime.TryBeginPreview(historyCount, out diagnostic);

        bool ICoCoStateGraphTemporalParticipant.TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic) =>
            _runtime.TryPrepareProjection(
                applyKind,
                historyDepth,
                source,
                targetTickFrame,
                out diagnostic);

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
            out CoCoDiagnostic diagnostic) =>
            _runtime.TryPrepareBranchCapture(
                historyDepth,
                branchHead,
                out diagnostic);

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
            if (_runtime != null &&
                !_runtime.IsDisposed &&
                _attachedPoolRuntime != null &&
                !_attachedPoolRuntime.IsDisposed &&
                stateGraphHost != null &&
                poolHost != null &&
                ReferenceEquals(poolHost.Runtime, _attachedPoolRuntime) &&
                CoCoStateGraphHostBoundary.Contains(
                    stateGraphHost,
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

        private bool TryValidateDownstream(
            CoCoStateGraphHost host,
            out CoCoDiagnostic diagnostic)
        {
            if (downstreamRestoreBinding == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (ReferenceEquals(downstreamRestoreBinding, this) ||
                !(downstreamRestoreBinding is ICoCoContextRestoreBinding) ||
                !CoCoStateGraphHostBoundary.Contains(
                    host,
                    downstreamRestoreBinding))
            {
                diagnostic = ConflictError(
                    "Downstream Restore Binding must be a different live component inside the same Host boundary.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
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
                _attachedPoolRuntime = null;
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
