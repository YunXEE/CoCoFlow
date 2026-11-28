using CoCoFlow.Runtime.Core;

// 场景组件通过这些接口，在 Host 的同步恢复回调里读取自己的 Context 槽。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>说明 Host 本次要求场景应用哪一种状态。</summary>
    public enum HostRestoreKind
    {
        /// <summary>把历史选择投影到场景。</summary>
        Preview = 1,
        /// <summary>应用即将提交的恢复帧，包括从存档恢复。</summary>
        Confirm = 2,
        /// <summary>取消预览，把场景恢复到当前已提交帧。</summary>
        Cancel = 3,
        /// <summary>在场景副作用失败后，按已提交帧校正场景。</summary>
        Correction = 4
    }

    /// <summary>提供本次恢复回调的只读槽访问。保存的副本随回调结束而失效。</summary>
    public readonly struct HostRestoreReader
    {
        #region Public API

        /// <summary>创建此 Reader 的同步恢复回调仍在执行时为真。</summary>
        public bool IsValid => _lease != null && _lease.IsActive(_token);

        /// <summary>在本次回调中按槽标识和确切类型读取恢复值。</summary>
        /// <typeparam name="TValue">槽登记的非托管值类型。</typeparam>
        /// <param name="slotId">要读取的 Context 槽。</param>
        /// <param name="value">成功时为该槽的值，失败时为默认值。</param>
        /// <returns>槽存在、类型匹配且 Reader 仍有效时为真。</returns>
        public bool TryRead<TValue>(StateSlotId slotId, out TValue value) where TValue : unmanaged
        {
            if (_lease != null)
            {
                return _lease.TryRead(_token, slotId, out value);
            }

            value = default;
            return false;
        }

        #endregion

        #region Internal Logic

        private readonly HostRestoreReadLease _lease;
        private readonly ulong _token;

        internal HostRestoreReader(HostRestoreReadLease lease, ulong token)
        {
            _lease = lease;
            _token = token;
        }

        #endregion
    }

    /// <summary>提供场景恢复的原因、源帧、目标时钟和本次回调使用的 Reader。</summary>
    public readonly struct HostRestoreContext
    {
        #region Public API

        /// <summary>本次投影的操作种类。</summary>
        public HostRestoreKind ApplyKind { get; }
        /// <summary>投影值的来源帧。</summary>
        public HostHistoryFrame Source { get; }
        /// <summary>场景将要应用的时钟，确认恢复时可具有新的时间线代次。</summary>
        public TickFrame TargetTickFrame { get; }
        /// <summary>只在本次回调期间允许读取槽。</summary>
        public HostRestoreReader Reader { get; }
        /// <summary>操作已定义且当前回调仍有效时为真。</summary>
        public bool IsValid => ApplyKind >= HostRestoreKind.Preview &&
                               ApplyKind <= HostRestoreKind.Correction && Reader.IsValid;

        #endregion

        #region Internal Logic

        internal HostRestoreContext(HostRestoreKind applyKind, in HostHistoryFrame source,
            in TickFrame targetTickFrame, HostRestoreReader reader)
        {
            ApplyKind = applyKind;
            Source = source;
            TargetTickFrame = targetTickFrame;
            Reader = reader;
        }

        #endregion
    }

    /// <summary>让已勾选存档恢复的 Operator 应用自己负责的场景状态。</summary>
    public interface IHostPersistenceRestoreOperator : IOperator
    {
        /// <summary>按存档投影同步恢复本组件。</summary>
        /// <param name="context">本次恢复的值和时钟，槽读取限本次调用。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>成功应用场景状态时为真。拒绝会中止本次投影。</returns>
        bool TryRestore(in HostRestoreContext context, out CoCoDiagnostic diagnostic);
    }

    /// <summary>让已勾选时间恢复的 Operator 应用预览、恢复和校正时的场景状态。</summary>
    public interface IHostTemporalRestoreOperator : IOperator
    {
        /// <summary>根据本次操作和提供的槽值同步更新场景。</summary>
        /// <param name="context">本次恢复的值和时钟，槽读取限本次调用。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>成功应用场景状态时为真。拒绝会中止本次投影。</returns>
        bool TryRestore(in HostRestoreContext context, out CoCoDiagnostic diagnostic);
    }

    /// <summary>场景组件实现此接口以接收 Host 的恢复投影。</summary>
    public interface IHostRestoreBinding
    {
        /// <summary>在 Operator 恢复后同步应用本组件负责的值。</summary>
        /// <param name="context">本次操作、帧身份和回调期间可用的 Reader。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>投影成功时为真。若回调已产生场景副作用，失败时由 Host 安排校正。</returns>
        bool TryApply(in HostRestoreContext context, out CoCoDiagnostic diagnostic);
    }

    /// <summary>在一次场景回调期间绑定已提交视图或待恢复视图，按代次使旧 Reader 失效。</summary>
    internal sealed class HostRestoreReadLease
    {
        private ContextFrameReadView _authority;
        private ContextRestoreReadView _restore;
        private ulong _generation;
        private bool _active;
        private bool _readsAuthority;

        internal bool TryOpen(in ContextFrameReadView authority, out HostRestoreReader reader)
        {
            reader = default;
            if (!authority.IsValid || !TryBegin(out reader))
            {
                return false;
            }

            _authority = authority;
            _readsAuthority = true;
            return true;
        }

        internal bool TryOpen(in ContextRestoreReadView restore, out HostRestoreReader reader)
        {
            reader = default;
            if (!restore.IsValid || !TryBegin(out reader))
            {
                return false;
            }

            _restore = restore;
            _readsAuthority = false;
            return true;
        }

        internal void Close()
        {
            _active = false;
            _authority = default;
            _restore = default;
        }

        internal bool IsActive(ulong token) => _active && token == _generation;

        internal bool TryRead<TValue>(ulong token, StateSlotId slotId, out TValue value)
            where TValue : unmanaged
        {
            value = default;
            if (!IsActive(token))
            {
                return false;
            }

            if (!_readsAuthority)
            {
                return _restore.TryRead(slotId, out value);
            }

            ContextFrameLayout layout = _authority.Layout;
            if (layout == null || !layout.TryResolveSlot(slotId, out StateSlot<TValue> slot))
            {
                return false;
            }

            value = _authority.Read(slot);
            return true;
        }

        private bool TryBegin(out HostRestoreReader reader)
        {
            if (_active || _generation == ulong.MaxValue)
            {
                reader = default;
                return false;
            }

            _active = true;
            reader = new HostRestoreReader(this, ++_generation);
            return true;
        }
    }
}
