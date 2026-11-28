using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// 冻结 Inspector 选中的恢复能力，管理场景参与者，并只在同步回调中开放帧读取。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>模块随同 Host 为场景侧的私有状态准备、发布或取消历史与存档变更。</summary>
    internal interface IHostHistoryParticipant
    {
        bool TryAttachTemporalHost(
            Component host,
            int historyCapacity,
            out CoCoDiagnostic diagnostic);

        bool IsTemporalParticipantLive(Component host);

        bool TryPrepareForwardCapture(
            in HostHistoryFrame candidate,
            out CoCoDiagnostic diagnostic);

        void PublishForwardCaptureNoFail();

        void CancelPreparedCaptureNoFail();

        bool TryPrepareAuthorityReset(
            in HostHistoryFrame targetAuthority,
            out CoCoDiagnostic diagnostic);

        void CommitPreparedAuthorityResetNoFail();

        void CancelPreparedAuthorityResetNoFail();

        bool TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic);

        bool TryPrepareProjection(
            HostRestoreKind applyKind,
            int historyDepth,
            in HostHistoryFrame source,
            in TickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic);

        void FinishProjectionNoFail(bool succeeded);

        bool CanConfirmPreview(int historyDepth);

        bool TryPrepareBranchCapture(
            int historyDepth,
            in HostHistoryFrame branchHead,
            out CoCoDiagnostic diagnostic);

        void PublishBranchCaptureNoFail();

        void CompletePreviewNoFail(HostRestoreKind applyKind);

        void DrainPublishedCleanupNoFail();

        void DetachTemporalHostNoFail();
    }

    /// <summary>保存 Inspector 中一个 Operator 的引用及两种恢复开关。</summary>
    [Serializable]
    internal sealed class HostOperatorRecovery
    {
        public MonoBehaviour component;
        public bool persistence;
        public bool temporal;
    }

    /// <summary>按启动时冻结的顺序，把时间或存档投影应用到实际场景组件。</summary>
    internal sealed class HostRestoreBindings : IDisposable
    {
        private readonly HostComponentBoundary _boundary;
        private readonly MonoBehaviour _bindingComponent;
        private readonly ApplyProjection _applyBinding;
        private readonly Func<bool> _isCancelled;
        private readonly HostRestoreReadLease _readLease = new HostRestoreReadLease();
        private readonly FrozenOperator[] _operators;
        private readonly StateSlotDescriptor[] _retainPersistence;
        private readonly StateSlotDescriptor[] _retainTemporal;
        private bool _participantAttached;

        internal HostRestoreBindings(HostComponentBoundary boundary, IReadOnlyList<MonoBehaviour> operators,
            HostOperatorRecovery[] savedRecovery, ContextFrameLayout layout, MonoBehaviour binding,
            Func<bool> isCancelled)
        {
            _boundary = boundary;
            _isCancelled = isCancelled;
            if (binding is IHostRestoreBinding restoreBinding && boundary.Contains(binding))
            {
                _bindingComponent = binding;
                _applyBinding = restoreBinding.TryApply;
                Participant = binding as IHostHistoryParticipant;
            }

            var persistenceSlots = new List<StateSlotDescriptor>();
            var temporalSlots = new List<StateSlotDescriptor>();
            _operators = new FrozenOperator[operators.Count];
            for (int i = 0; i < operators.Count; i++)
            {
                MonoBehaviour component = operators[i];
                HostOperatorRecovery saved = Array.Find(savedRecovery, entry =>
                    entry != null && ReferenceEquals(entry.component, component));
                var frozen = new FrozenOperator(component, saved);
                _operators[i] = frozen;
                foreach (OperatorOutcomeRequirement outcome in ((IOperator)component).Descriptor.OutcomeRequirements)
                {
                    foreach (StateSlotDescriptor slot in layout.Slots)
                    {
                        if (slot.SlotId != outcome.SlotId) continue;
                        if (frozen.Persistence == null) persistenceSlots.Add(slot);
                        if (frozen.Temporal == null) temporalSlots.Add(slot);
                    }
                }
            }

            _retainPersistence = persistenceSlots.ToArray();
            _retainTemporal = temporalSlots.ToArray();
        }

        internal ContextProjection? Projection { get; set; }
        internal IReadOnlyList<StateSlotDescriptor> RetainedSlots =>
            Projection == ContextProjection.Durable ? _retainPersistence : _retainTemporal;

        internal IHostHistoryParticipant Participant { get; }
        internal bool IsLive => OperatorsLive() && (_applyBinding == null ||
            (_boundary.Contains(_bindingComponent) && (!_participantAttached || IsParticipantLive())));

        internal static bool TryValidateBinding(HostComponentBoundary boundary, MonoBehaviour binding,
            int capacity, out CoCoDiagnostic diagnostic)
        {
            if (binding is IOperator)
            {
                diagnostic = ConfigurationError("Operator recovery must use the Host Operator switches.");
                return false;
            }

            if (!ReferenceEquals(binding, null) &&
                (capacity > 0 || (binding is IHostRestoreBinding && boundary.Contains(binding))))
            {
                return HostRestoreChain.TryValidate(boundary, binding, out diagnostic);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryAttachParticipant(int capacity, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (Participant == null) return true;
            if (_participantAttached)
            {
                if (IsParticipantLive()) return true;
                diagnostic = ConfigurationError("Restore participant is no longer attached to its original Host.");
                return false;
            }

            try
            {
                _participantAttached = Participant.TryAttachTemporalHost(_boundary.Owner, capacity, out diagnostic);
            }
            catch (Exception)
            {
                DetachParticipant();
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore, CoCoDiagnosticCode.CommitPreparationFailed,
                    "Restore participant threw while attaching to the Host.");
                return false;
            }

            if (!_participantAttached || diagnostic.IsError)
            {
                if (_participantAttached) DetachParticipant();
                if (!diagnostic.IsError)
                {
                    diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore,
                        CoCoDiagnosticCode.CommitPreparationFailed, "Restore participant rejected the Host attachment.");
                }

                return false;
            }

            if (capacity > 0 || IsParticipantLive()) return true;
            DetachParticipant();
            diagnostic = ConfigurationError("Persistence participant became invalid during reset-only attachment.");
            return false;
        }

        internal bool TryPrepareForwardCapture(in FinalizedContextCommit candidate, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (Participant == null) return true;
            try
            {
                if (Participant.IsTemporalParticipantLive(_boundary.Owner) &&
                    candidate.TryGetMetadata(out StateFlowFrameHeader header, out ContextRevision revision, out ContextFrameOrigin origin) &&
                    Participant.TryPrepareForwardCapture(
                        new HostHistoryFrame(header.Identity.GraphInstanceId, header.TickFrame, revision, origin), out diagnostic))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore, CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal participant threw before forward capture was committed.");
            }

            Participant.CancelPreparedCaptureNoFail();
            if (!diagnostic.IsError)
            {
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore, CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal participant rejected the finalized capture candidate.");
            }

            return false;
        }

        internal bool OperatorsLive(bool requireCoverage = false)
        {
            foreach (FrozenOperator entry in _operators)
            {
                bool enabled = entry.Callback(Projection) != null;
                if (requireCoverage && !enabled) return false;
                if (enabled && !_boundary.Contains(entry.Component)) return false;
            }

            return true;
        }

        internal bool TryApply(HostRestoreKind kind, in HostHistoryFrame source, in TickFrame target,
            in ContextFrameReadView authority, out bool worldMayBeDirty, out CoCoDiagnostic diagnostic)
        {
            return TryApply(kind, source, target, new ProjectionValues(authority, default),
                out worldMayBeDirty, out diagnostic);
        }

        internal bool TryApply(HostRestoreKind kind, in HostHistoryFrame source, in TickFrame target,
            in ContextRestoreReadView restore, out bool worldMayBeDirty, out CoCoDiagnostic diagnostic)
        {
            return TryApply(kind, source, target, new ProjectionValues(default, restore),
                out worldMayBeDirty, out diagnostic);
        }

        private bool TryApply(HostRestoreKind kind, in HostHistoryFrame source, in TickFrame target,
            in ProjectionValues values, out bool worldMayBeDirty, out CoCoDiagnostic diagnostic)
        {
            worldMayBeDirty = false;
            diagnostic = CoCoDiagnostic.None;
            if (!values.IsValid)
            {
                diagnostic = InvalidFrame("Context is unavailable for scene projection.");
                return false;
            }

            if (!IsLive)
            {
                diagnostic = ConfigurationError("Scene recovery requires the original live Host bindings.");
                return false;
            }

            foreach (FrozenOperator entry in _operators)
            {
                ApplyProjection callback = entry.Callback(Projection);
                if (callback == null) continue;
                if (!TryInvoke(callback, kind, source, target, values, ref worldMayBeDirty, out diagnostic) ||
                    !IsLive || _isCancelled())
                {
                    return Reject(ref diagnostic);
                }
            }

            if (_applyBinding != null &&
                !TryInvoke(_applyBinding, kind, source, target, values, ref worldMayBeDirty, out diagnostic))
            {
                return Reject(ref diagnostic);
            }

            if (!IsLive || _isCancelled()) return Reject(ref diagnostic);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryInvoke(ApplyProjection callback, HostRestoreKind kind, in HostHistoryFrame source,
            in TickFrame target, in ProjectionValues values, ref bool worldMayBeDirty, out CoCoDiagnostic diagnostic)
        {
            if (!values.TryOpen(_readLease, out HostRestoreReader reader))
            {
                diagnostic = InvalidFrame("Could not open the Context reader for a scene restore callback.");
                return false;
            }

            try
            {
                worldMayBeDirty = true;
                var context = new HostRestoreContext(kind, source, target, reader);
                return callback(context, out diagnostic) && !diagnostic.IsError;
            }
            catch (Exception)
            {
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore, CoCoDiagnosticCode.WorldCorrectionRequired,
                    "Restore Binding threw during synchronous Unity projection.");
                return false;
            }
            finally
            {
                _readLease.Close();
            }
        }

        internal void DrainParticipantCleanup()
        {
            try
            {
                Participant?.DrainPublishedCleanupNoFail();
            }
            catch (Exception)
            {
                // 已提交的图和历史不能因场景清理失败而回滚。
            }
        }

        public void Dispose()
        {
            _readLease.Close();
            if (_participantAttached) DetachParticipant();
        }

        private bool IsParticipantLive()
        {
            try
            {
                return Participant.IsTemporalParticipantLive(_boundary.Owner);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void DetachParticipant()
        {
            _participantAttached = false;
            try
            {
                Participant.DetachTemporalHostNoFail();
            }
            catch (Exception)
            {
                // 启动回滚或停止时，组件已失去该次运行的所有权。
            }
        }

        private static bool Reject(ref CoCoDiagnostic diagnostic)
        {
            if (!diagnostic.IsError)
            {
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore, CoCoDiagnosticCode.WorldCorrectionRequired,
                    "Restore Binding rejected or invalidated synchronous Unity projection.");
            }

            return false;
        }

        private static CoCoDiagnostic ConfigurationError(string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, CoCoDiagnosticCode.InvalidHostConfiguration, message);

        private static CoCoDiagnostic InvalidFrame(string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore, CoCoDiagnosticCode.InvalidFrameHandle, message);

        /// <summary>场景组件接受单次同步恢复的入口。</summary>
        private delegate bool ApplyProjection(in HostRestoreContext context, out CoCoDiagnostic diagnostic);

        /// <summary>保存启动时的组件和已启用的恢复入口，避免读档时重新扫描组件。</summary>
        private readonly struct FrozenOperator
        {
            internal readonly MonoBehaviour Component;
            internal readonly ApplyProjection Persistence;
            internal readonly ApplyProjection Temporal;

            internal FrozenOperator(MonoBehaviour component, HostOperatorRecovery saved)
            {
                Component = component;
                Persistence = saved?.persistence == true && component is IHostPersistenceRestoreOperator persistent
                    ? persistent.TryRestore : (ApplyProjection)null;
                Temporal = saved?.temporal == true && component is IHostTemporalRestoreOperator temporal
                    ? temporal.TryRestore : (ApplyProjection)null;
            }

            internal ApplyProjection Callback(ContextProjection? projection)
            {
                if (projection == ContextProjection.Durable) return Persistence;
                if (projection == ContextProjection.Temporal) return Temporal;
                return Temporal ?? Persistence;
            }
        }

        /// <summary>保存已提交帧或恢复候选，在每个组件回调开始时单独授予读取权限。</summary>
        private readonly struct ProjectionValues
        {
            private readonly ContextFrameReadView _authority;
            private readonly ContextRestoreReadView _restore;

            internal ProjectionValues(ContextFrameReadView authority, ContextRestoreReadView restore)
            {
                _authority = authority;
                _restore = restore;
            }

            internal bool IsValid => _authority.IsValid || _restore.IsValid;

            internal bool TryOpen(HostRestoreReadLease lease, out HostRestoreReader reader) =>
                _authority.IsValid ? lease.TryOpen(_authority, out reader) : lease.TryOpen(_restore, out reader);
        }
    }
}
