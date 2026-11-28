using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Host 将定向或广播事件交给实例收件箱。封存步骤校验目标、代次和序号，再允许读取固定批次。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>区分实例收件箱的配置、运行、暂停、回退与终止阶段。</summary>
    public enum InstanceInboxState
    {
        /// <summary>尚无指定值。</summary>
        None = 0,
        /// <summary>已创建，等待启动。</summary>
        Created = 1,
        /// <summary>已启动并可接收本步工作。</summary>
        Running = 2,
        /// <summary>已暂停，等待恢复。</summary>
        Suspended = 3,
        /// <summary>正在回退或恢复时间线。</summary>
        RewindingOrRestoring = 4,
        /// <summary>已停止运行。</summary>
        Stopped = 5,
        /// <summary>资源已释放。</summary>
        Disposed = 6
    }

    /// <summary>描述收件通道的事件类型、容量和寻址要求，供绑定与诊断核查。</summary>
    public readonly struct InstanceInboxManifestEntry : IEquatable<InstanceInboxManifestEntry>
    {

        #region Public API

        /// <summary>此通道登记的事件类型标识。</summary>
        public EventTypeId EventTypeId { get; }
        /// <summary>通道接收的非托管载荷类型。</summary>
        public Type PayloadType { get; }
        /// <summary>此存储可接纳的最大条目数。</summary>
        public int Capacity { get; }
        /// <summary>允许属于同一实例的来源回投此事件时为真。</summary>
        public bool AllowSourceEcho { get; }
        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => EventTypeId.IsValid && PayloadType != null && Capacity > 0;

        /// <summary>按<c>EventTypeId</c>、<c>PayloadType</c>、<c>Capacity</c>、<c>AllowSourceEcho</c>比较两个值，全部相等时返回 true。</summary>
        /// <param name="other">待比较的同类值。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public bool Equals(InstanceInboxManifestEntry other)
        {
            return EventTypeId == other.EventTypeId &&
                   PayloadType == other.PayloadType &&
                   Capacity == other.Capacity &&
                   AllowSourceEcho == other.AllowSourceEcho;
        }

        /// <summary>仅当对象属于同类型并且<c>EventTypeId</c>、<c>PayloadType</c>、<c>Capacity</c>、<c>AllowSourceEcho</c>相等时返回 true。</summary>
        /// <param name="obj">待比较的对象。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public override bool Equals(object obj) =>
            obj is InstanceInboxManifestEntry other && Equals(other);

        /// <summary>为哈希集合计算与<c>EventTypeId</c>、<c>PayloadType</c>、<c>Capacity</c>、<c>AllowSourceEcho</c>的相等判定一致的散列值。</summary>
        /// <returns>与相等判定一致的哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ (PayloadType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ Capacity;
                hashCode = (hashCode * 397) ^ AllowSourceEcho.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>EventTypeId</c>、<c>PayloadType</c>、<c>Capacity</c>、<c>AllowSourceEcho</c>，相等时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator ==(
            InstanceInboxManifestEntry left,
            InstanceInboxManifestEntry right) => left.Equals(right);

        /// <summary>比较两端的<c>EventTypeId</c>、<c>PayloadType</c>、<c>Capacity</c>、<c>AllowSourceEcho</c>，存在差异时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator !=(
            InstanceInboxManifestEntry left,
            InstanceInboxManifestEntry right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal InstanceInboxManifestEntry(
            EventTypeId eventTypeId,
            Type payloadType,
            int capacity,
            bool allowSourceEcho)
        {
            EventTypeId = eventTypeId;
            PayloadType = payloadType;
            Capacity = capacity;
            AllowSourceEcho = allowSourceEcho;
        }

        #endregion
    }

    /// <summary>将类型化载荷绑定到所属收件箱的具体通道。</summary>
    /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
    public readonly struct InstanceInboxLaneHandle<TEvent> :
        IEquatable<InstanceInboxLaneHandle<TEvent>>
        where TEvent : unmanaged
    {
        private readonly InstanceInbox _inbox;

        /// <summary>此收件箱或其通道句柄所属的图实例。</summary>
        public GraphInstanceId Owner { get; }
        /// <summary>这些事件使用的域标识。</summary>
        public EventDomainId EventDomainId { get; }
        /// <summary>此通道登记的事件类型标识。</summary>
        public EventTypeId EventTypeId { get; }
        /// <summary>通道在冻结清单中的零基位置。</summary>
        public int DenseIndex { get; }
        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => _inbox != null &&
                               Owner.IsValid &&
                               EventDomainId.IsValid &&
                               EventTypeId.IsValid &&
                               DenseIndex >= 0;

        #region Public API

        /// <summary>按<c>inbox</c>、<c>Owner</c>、<c>EventDomainId</c>、<c>EventTypeId</c>、<c>DenseIndex</c>比较两个值，全部相等时返回 true。</summary>
        /// <param name="other">待比较的同类值。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public bool Equals(InstanceInboxLaneHandle<TEvent> other)
        {
            return ReferenceEquals(_inbox, other._inbox) &&
                   Owner == other.Owner &&
                   EventDomainId == other.EventDomainId &&
                   EventTypeId == other.EventTypeId &&
                   DenseIndex == other.DenseIndex;
        }

        /// <summary>仅当对象属于同类型并且<c>inbox</c>、<c>Owner</c>、<c>EventDomainId</c>、<c>EventTypeId</c>、<c>DenseIndex</c>相等时返回 true。</summary>
        /// <param name="obj">待比较的对象。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public override bool Equals(object obj) =>
            obj is InstanceInboxLaneHandle<TEvent> other && Equals(other);

        /// <summary>为哈希集合计算与<c>inbox</c>、<c>Owner</c>、<c>EventDomainId</c>、<c>EventTypeId</c>、<c>DenseIndex</c>的相等判定一致的散列值。</summary>
        /// <returns>与相等判定一致的哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _inbox?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ Owner.GetHashCode();
                hashCode = (hashCode * 397) ^ EventDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ DenseIndex;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>inbox</c>、<c>Owner</c>、<c>EventDomainId</c>、<c>EventTypeId</c>、<c>DenseIndex</c>，相等时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator ==(
            InstanceInboxLaneHandle<TEvent> left,
            InstanceInboxLaneHandle<TEvent> right) => left.Equals(right);

        /// <summary>比较两端的<c>inbox</c>、<c>Owner</c>、<c>EventDomainId</c>、<c>EventTypeId</c>、<c>DenseIndex</c>，存在差异时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator !=(
            InstanceInboxLaneHandle<TEvent> left,
            InstanceInboxLaneHandle<TEvent> right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal InstanceInboxLaneHandle(
            InstanceInbox inbox,
            GraphInstanceId owner,
            EventDomainId eventDomainId,
            EventTypeId eventTypeId,
            int denseIndex)
        {
            _inbox = inbox;
            Owner = owner;
            EventDomainId = eventDomainId;
            EventTypeId = eventTypeId;
            DenseIndex = denseIndex;
        }

        internal bool IsOwnedBy(InstanceInbox inbox) =>
            ReferenceEquals(_inbox, inbox);

        #endregion
    }

    /// <summary>为已封存的那一步提供按序读取的收件快照。</summary>
    /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
    public readonly struct InstanceInboxSealedBatch<TEvent>
        where TEvent : unmanaged
    {
        private readonly InstanceInbox _inbox;
        private readonly InstanceInboxLaneHandle<TEvent> _handle;
        private readonly ulong _generation;

        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => _inbox != null &&
                               _inbox.IsSealedBatchCurrent(_handle, _generation);
        /// <summary>当前可读取的条目数量。</summary>
        public int Count => _inbox?.GetSealedCount(_handle, _generation) ?? 0;
        internal GraphInstanceId Owner => _handle.Owner;
        internal EventDomainId EventDomainId => _handle.EventDomainId;
        internal EventTypeId EventTypeId => _handle.EventTypeId;

        #region Public API

        /// <summary>从创建批次的收件箱读取指定索引的包，代次已改变或索引越界时返回 false。</summary>
        /// <param name="index">在当前批次中从零开始的索引。</param>
        /// <param name="packet">成功时为读取的事件包，失败时为默认值。</param>
        /// <returns>批次代次和索引有效且成功读到事件时为真。</returns>
        public bool TryRead(int index, out EventPacket<TEvent> packet)
        {
            if (_inbox == null)
            {
                packet = default;
                return false;
            }

            return _inbox.TryReadSealed(_handle, _generation, index, out packet);
        }

        #endregion

        #region Internal Logic

        internal InstanceInboxSealedBatch(
            InstanceInbox inbox,
            InstanceInboxLaneHandle<TEvent> handle,
            ulong generation)
        {
            _inbox = inbox;
            _handle = handle;
            _generation = generation;
        }

        #endregion
    }

    /// <summary>汇总接收、丢弃和拒绝的事件数，供诊断区分投递结果。</summary>
    public readonly struct InstanceInboxCounters
    {

        #region Public API

        /// <summary>已接收的事件包总数。</summary>
        public ulong Accepted { get; }
        /// <summary>已识别为重复的事件包总数。</summary>
        public ulong Duplicate { get; }
        /// <summary>已拒绝的事件包总数。</summary>
        public ulong Rejected { get; }
        /// <summary>在回退或恢复时丢弃的事件包总数。</summary>
        public ulong RewindRestoreDropped { get; }
        /// <summary>因容量耗尽而丢弃的不可靠事件数。</summary>
        public ulong UnreliableOverflowDropped { get; }
        /// <summary>因可靠事件溢出产生的故障次数。</summary>
        public ulong ReliableOverflowFaults { get; }

        #endregion

        #region Internal Logic

        internal InstanceInboxCounters(
            ulong accepted,
            ulong duplicate,
            ulong rejected,
            ulong rewindRestoreDropped,
            ulong unreliableOverflowDropped,
            ulong reliableOverflowFaults)
        {
            Accepted = accepted;
            Duplicate = duplicate;
            Rejected = rejected;
            RewindRestoreDropped = rewindRestoreDropped;
            UnreliableOverflowDropped = unreliableOverflowDropped;
            ReliableOverflowFaults = reliableOverflowFaults;
        }

        #endregion
    }

    /// <summary>邮箱在认领、封存和时间线重置时读取输入所有者的状态，并通知它取消采集或释放邮箱。</summary>
    internal interface IInboxIntentOwner
    {
        GraphInstanceId GraphInstanceId { get; }
        bool AreBindingsFrozen { get; }
        bool IsCollecting { get; }
        bool IsDisposed { get; }
        bool IsExecutingUserCallback { get; }
        bool TryClaimInbox(InstanceInbox inbox);
        void ReleaseInbox(InstanceInbox inbox);
        bool TryResetForTimelineChange();
        bool CancelCollection();
    }

    /// <summary>按实例和时间线接纳事件，封存后为当前步提供类型化批次。</summary>
    public sealed class InstanceInbox : IDisposable
    {
        private readonly IInstanceInboxLane[] _lanes;
        private readonly InboxSourceEpoch[] _sourceEpochs;
        private readonly InboxDedupEntry[] _dedupEntries;
        private int _laneCount;
        private int _sourceCount;
        private int _dedupWriteIndex;
        private InstanceInboxState _state;
        private TickFrame _lastSealedTickFrame;
        private bool _hasLastSealedTick;
        private bool _requiresNewTimelineEpoch;
        private TimelineEpoch _requiredOwnerTimelineEpoch;
        private bool _hasRequiredOwnerTimelineEpoch;
        private IInboxIntentOwner _intentRuntime;
        private InstanceInboxState _deferredLifecycleState;
        private ulong _accepted;
        private ulong _duplicate;
        private ulong _rejected;
        private ulong _rewindRestoreDropped;
        private ulong _unreliableOverflowDropped;
        private ulong _reliableOverflowFaults;

        /// <summary>此收件箱或其通道句柄所属的图实例。</summary>
        public GraphInstanceId Owner { get; }
        /// <summary>这些事件使用的域标识。</summary>
        public EventDomainId EventDomainId { get; }
        /// <summary>已注册的接收通道数。</summary>
        public int ManifestCount => _laneCount;
        /// <summary>本实例收件箱的当前接收阶段。</summary>
        public InstanceInboxState State => _state;
        /// <summary>至少发生过一次可靠事件溢出时为真。</summary>
        public bool HasReliableOverflowFault => _reliableOverflowFaults != 0UL;
        /// <summary>查询时的收件、去重、拒绝与溢出计数。</summary>
        public InstanceInboxCounters Counters => new InstanceInboxCounters(
            _accepted,
            _duplicate,
            _rejected,
            _rewindRestoreDropped,
            _unreliableOverflowDropped,
            _reliableOverflowFaults);

        internal bool CanResumeAfterTimelineReset =>
            _state == InstanceInboxState.RewindingOrRestoring &&
            HasLiveIntentRuntime() &&
            !_intentRuntime.IsCollecting &&
            !IsLifecycleTransitionBlocked();

        internal bool CanCancelRewindOrRestore =>
            _state == InstanceInboxState.RewindingOrRestoring &&
            HasLiveIntentRuntime() &&
            !_intentRuntime.IsCollecting &&
            !IsLifecycleTransitionBlocked();

        #region Public API

        /// <summary>为指定实例和容量分配收件通道及去重存储，容量或实例身份无效时拒绝构造。</summary>
        /// <param name="owner">拥有此收件箱的图实例。</param>
        /// <param name="eventDomainId">收件箱接收的事件域。</param>
        /// <param name="maxEventTypes">允许登记的事件类型数。</param>
        /// <param name="maxSources">可跟踪的来源数量上限。</param>
        /// <param name="dedupWindowCapacity">保留用于识别重复事件的记录数。</param>
        public InstanceInbox(
            GraphInstanceId owner,
            EventDomainId eventDomainId,
            int maxEventTypes,
            int maxSources,
            int dedupWindowCapacity)
        {
            if (!owner.IsValid)
            {
                throw new ArgumentException("Inbox owner must be valid.", nameof(owner));
            }

            if (!eventDomainId.IsValid)
            {
                throw new ArgumentException("Inbox event domain must be valid.", nameof(eventDomainId));
            }

            if (maxEventTypes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEventTypes));
            }

            if (maxSources <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSources));
            }

            if (dedupWindowCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dedupWindowCapacity));
            }

            Owner = owner;
            EventDomainId = eventDomainId;
            _lanes = new IInstanceInboxLane[maxEventTypes];
            _sourceEpochs = new InboxSourceEpoch[maxSources];
            _dedupEntries = new InboxDedupEntry[dedupWindowCapacity];
            _state = InstanceInboxState.Created;
        }

        /// <summary>在启动前登记载荷类型、标识和容量，成功返回此收件箱专用的句柄，冲突或容量不足则返回 false。</summary>
        /// <param name="eventTypeId">通道的事件类型标识。</param>
        /// <param name="capacity">要分配的固定容量。</param>
        /// <param name="allowSourceEcho">是否接受来源实例回投的同类事件。</param>
        /// <param name="handle">成功时为可用于后续查询的句柄。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>创建状态下成功注册了匹配的唯一通道时为真。</returns>
        public bool TryRegisterLane<TEvent>(
            EventTypeId eventTypeId,
            int capacity,
            bool allowSourceEcho,
            out InstanceInboxLaneHandle<TEvent> handle,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (_state != InstanceInboxState.Created ||
                _deferredLifecycleState != InstanceInboxState.None)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Mailbox manifest is frozen after Start.");
                return false;
            }

            if (!eventTypeId.IsValid || capacity <= 0)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.InvalidEventPacket,
                    "Event type and lane capacity must be valid.");
                return false;
            }

            if (_laneCount >= _lanes.Length)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxOverflow,
                    "Mailbox manifest capacity is exhausted.");
                return false;
            }

            for (int index = 0; index < _laneCount; index++)
            {
                if (_lanes[index].Manifest.EventTypeId == eventTypeId)
                {
                    handle = default;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Identity,
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "Mailbox event type ids must be unique.");
                    return false;
                }
            }

            handle = new InstanceInboxLaneHandle<TEvent>(
                this,
                Owner,
                EventDomainId,
                eventTypeId,
                _laneCount);
            _lanes[_laneCount] = new InstanceInboxLane<TEvent>(
                eventTypeId,
                capacity,
                allowSourceEcho);
            _laneCount++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>按索引读取这一个收件箱的已注册通道信息，索引越界时返回 false。</summary>
        /// <param name="denseIndex">清单内从零开始的槽位。</param>
        /// <param name="entry">成功时为该索引对应的通道声明。</param>
        /// <returns>索引落在已登记范围并取得声明时为真。</returns>
        public bool TryGetManifestEntry(int denseIndex, out InstanceInboxManifestEntry entry)
        {
            if (denseIndex < 0 || denseIndex >= _laneCount)
            {
                entry = default;
                return false;
            }

            entry = _lanes[denseIndex].Manifest;
            return true;
        }

        /// <summary>在配置阶段结束后启用收件，当前阶段无法启动时返回 false。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>收件箱已就绪且所有者绑定已冻结时为真。</returns>
        public bool Start(out CoCoDiagnostic diagnostic)
        {
            if (_state != InstanceInboxState.Created ||
                _deferredLifecycleState != InstanceInboxState.None)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Mailbox can only start from Created.");
                return false;
            }

            if (_intentRuntime == null || _intentRuntime.IsDisposed ||
                _intentRuntime.IsExecutingUserCallback || _intentRuntime.IsCollecting)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Mailbox requires one idle live Intent runtime before Start.");
                return false;
            }

            if (!_intentRuntime.AreBindingsFrozen)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "Intent bindings must be frozen before Inbox Start.");
                return false;
            }

            _state = InstanceInboxState.Running;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>从运行状态暂停收件。不能暂停时返回 false。</summary>
        /// <returns>从接收状态成功进入暂停时为真。</returns>
        public bool Suspend()
        {
            if (_state != InstanceInboxState.Running ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked())
            {
                return false;
            }

            _state = InstanceInboxState.Suspended;
            return true;
        }

        /// <summary>从暂停状态恢复收件。当前阶段不允许恢复时返回 false。</summary>
        /// <returns>成功恢复收件时为真。</returns>
        public bool Resume()
        {
            if (_state != InstanceInboxState.Suspended ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked())
            {
                return false;
            }

            _state = InstanceInboxState.Running;
            return true;
        }

        /// <summary>切换到回退或恢复阶段，使普通投递遵守恢复期间的收件限制。</summary>
        /// <returns>成功清理本代输入并进入回退或恢复状态时为真。</returns>
        public bool BeginRewindOrRestore()
        {
            if (_state != InstanceInboxState.Running ||
                !HasLiveIntentRuntime() ||
                IsLifecycleTransitionBlocked() ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            ClearQueuedState();
            _state = InstanceInboxState.RewindingOrRestoring;
            return true;
        }

        /// <summary>在时间线已切换后清理旧批次与去重记录，重新允许新时间线的投递。</summary>
        /// <returns>完成时间线重置并可重新收件时为真。</returns>
        public bool ResumeAfterTimelineReset()
        {
            if (!CanResumeAfterTimelineReset ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            ResumeAfterTimelineResetNoFail();
            return true;
        }

        /// <summary>取消进行中的回退或恢复并回到原接收阶段，当前没有恢复操作时返回 false。</summary>
        /// <returns>取消本次恢复并回到接收状态时为真。</returns>
        public bool CancelRewindOrRestore()
        {
            if (!CanCancelRewindOrRestore ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            CancelRewindOrRestoreNoFail();
            return true;
        }

        /// <summary>停止当前收件箱并清理待收及封存的事件。</summary>
        public void Stop()
        {
            if (_state == InstanceInboxState.Disposed ||
                _deferredLifecycleState == InstanceInboxState.Disposed)
            {
                return;
            }

            if (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback)
            {
                if (_deferredLifecycleState == InstanceInboxState.None)
                {
                    _deferredLifecycleState = InstanceInboxState.Stopped;
                }

                return;
            }

            CancelActiveIntentCollection();

            ClearAllRuntimeState();
            ReleaseIntentRuntimeBinding();
            _deferredLifecycleState = InstanceInboxState.None;
            _state = InstanceInboxState.Stopped;
        }

        /// <summary>终止接收并释放本实例收件状态，已取得的通道和批次不能继续读取。</summary>
        public void Dispose()
        {
            if (_state == InstanceInboxState.Disposed)
            {
                return;
            }

            if (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback)
            {
                _deferredLifecycleState = InstanceInboxState.Disposed;
                return;
            }

            CancelActiveIntentCollection();

            ClearAllRuntimeState();
            ReleaseIntentRuntimeBinding();
            _deferredLifecycleState = InstanceInboxState.None;
            _state = InstanceInboxState.Disposed;
        }

        /// <summary>将带信封的事件交给匹配的通道，返回结果区分接收、丢弃、阶段或目标不符。</summary>
        /// <param name="handle">用于定位本次操作对应通道或出边的句柄。</param>
        /// <param name="packet">当前事件包，包含投递元数据和类型化载荷。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>此事件包的接收、去重、拒绝或溢出结果。</returns>
        public InboxEnqueueResult TryEnqueue<TEvent>(
            InstanceInboxLaneHandle<TEvent> handle,
            in EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (_state == InstanceInboxState.RewindingOrRestoring)
            {
                _rewindRestoreDropped++;
                _rejected++;
                return InboxEnqueueResult.RewindOrRestoreDropped;
            }

            if (_state != InstanceInboxState.Running &&
                _state != InstanceInboxState.Suspended)
            {
                _rejected++;
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (_deferredLifecycleState != InstanceInboxState.None)
            {
                _rejected++;
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (!HasLiveIntentRuntime())
            {
                _rejected++;
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (!packet.IsValid || !Matches(handle))
            {
                _rejected++;
                return InboxEnqueueResult.InvalidPacket;
            }

            GraphEventEnvelope envelope = packet.Envelope;
            if (envelope.EventDomainId != EventDomainId)
            {
                _rejected++;
                return InboxEnqueueResult.EventDomainMismatch;
            }

            var lane = _lanes[handle.DenseIndex] as InstanceInboxLane<TEvent>;
            if (lane == null || envelope.EventTypeId != handle.EventTypeId)
            {
                _rejected++;
                return InboxEnqueueResult.UndeclaredEventType;
            }

            if (envelope.DeliveryMode == EventDeliveryMode.Targeted)
            {
                if (envelope.TargetGraphInstanceId != Owner)
                {
                    _rejected++;
                    return InboxEnqueueResult.EventTargetMismatch;
                }
            }
            else
            {
                if (envelope.TargetGraphInstanceId.IsValid)
                {
                    _rejected++;
                    return InboxEnqueueResult.EventTargetMismatch;
                }

                if (!lane.Manifest.AllowSourceEcho && envelope.SourceGraphInstanceId == Owner)
                {
                    _rejected++;
                    return InboxEnqueueResult.SourceEchoRejected;
                }
            }

            InboxEnqueueResult sourceResult = InspectSourceEpoch(
                envelope.SourceGraphInstanceId,
                envelope.SourceTimelineEpoch,
                out int sourceIndex,
                out bool sourceIsNew,
                out bool epochIsNew);
            if (sourceResult != InboxEnqueueResult.Accepted)
            {
                if (sourceResult == InboxEnqueueResult.SourceWindowFull)
                {
                    return RejectOverflow(envelope.Reliability);
                }

                _rejected++;
                return sourceResult;
            }

            InboxEnqueueResult dedupResult = InspectDedup(envelope);
            if (dedupResult != InboxEnqueueResult.Accepted)
            {
                if (dedupResult == InboxEnqueueResult.Duplicate)
                {
                    _duplicate++;
                }

                _rejected++;
                return dedupResult;
            }

            if (!sourceIsNew &&
                !epochIsNew &&
                envelope.SourceEventSequence.Value <= _sourceEpochs[sourceIndex].LastSequence.Value)
            {
                _rejected++;
                return InboxEnqueueResult.EventSequenceConflict;
            }

            if (!lane.HasIncomingCapacity)
            {
                return RejectOverflow(envelope.Reliability);
            }

            if (sourceIsNew)
            {
                _sourceEpochs[sourceIndex] = new InboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
                _sourceCount++;
            }
            else if (epochIsNew)
            {
                ClearDedupForSource(envelope.SourceGraphInstanceId);
                _sourceEpochs[sourceIndex] = new InboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
            }
            else
            {
                _sourceEpochs[sourceIndex] = new InboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
            }

            lane.Enqueue(packet);
            RecordDedup(envelope);
            _accepted++;
            return InboxEnqueueResult.Accepted;
        }

        /// <summary>将待收通道封存为给定步的批次，步身份或当前阶段无效时返回 false。</summary>
        /// <param name="tickFrame">本次封存事件对应的实例时钟。</param>
        /// <returns>成功为该步封存待处理输入时为真。</returns>
        public bool SealForTick(in TickFrame tickFrame)
        {
            if (_state != InstanceInboxState.Running ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked() ||
                !tickFrame.IsValid ||
                (_requiresNewTimelineEpoch &&
                 _hasLastSealedTick &&
                 tickFrame.TimelineEpoch.Value <=
                 _lastSealedTickFrame.TimelineEpoch.Value) ||
                (_hasLastSealedTick &&
                 !StateFlowTickOrder.IsStrictlyAfter(
                     tickFrame,
                     _lastSealedTickFrame)))
            {
                return false;
            }

            for (int index = 0; index < _laneCount; index++)
            {
                if (!_lanes[index].CanAdvanceGeneration)
                {
                    return false;
                }
            }

            for (int index = 0; index < _laneCount; index++)
            {
                _lanes[index].SealForTick();
            }

            _lastSealedTickFrame = tickFrame;
            _hasLastSealedTick = true;
            _requiresNewTimelineEpoch = false;
            return true;
        }

        /// <summary>返回有效句柄的本步封存数，句柄不属于本实例或无法读取时返回零。</summary>
        /// <param name="handle">用于定位本次操作对应通道或出边的句柄。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>指定通道本步封存的事件数量，句柄无效时为零。</returns>
        public int GetSealedCount<TEvent>(InstanceInboxLaneHandle<TEvent> handle)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                return 0;
            }

            return (_lanes[handle.DenseIndex] as InstanceInboxLane<TEvent>)?.SealedCount ?? 0;
        }

        /// <summary>按句柄和索引读取本步封存的包，批次失效或越界时返回 false。</summary>
        /// <param name="handle">用于定位本次操作对应通道或出边的句柄。</param>
        /// <param name="index">在当前批次中从零开始的索引。</param>
        /// <param name="packet">成功时为读取的事件包，失败时为默认值。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>成功从该通道当前封存批次中读取事件时为真。</returns>
        public bool TryReadSealed<TEvent>(
            InstanceInboxLaneHandle<TEvent> handle,
            int index,
            out EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                packet = default;
                return false;
            }

            var lane = _lanes[handle.DenseIndex] as InstanceInboxLane<TEvent>;
            if (lane == null)
            {
                packet = default;
                return false;
            }

            return lane.TryReadSealed(index, out packet);
        }

        /// <summary>返回绑定到本实例本次封存的批次，通道或封存状态无效时返回 false。</summary>
        /// <param name="handle">用于定位本次操作对应通道或出边的句柄。</param>
        /// <param name="batch">成功时为该通道本次封存的批次，下一次封存后失效。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>成功取得该通道当前代次的批次时为真。</returns>
        public bool TryGetSealedBatch<TEvent>(
            InstanceInboxLaneHandle<TEvent> handle,
            out InstanceInboxSealedBatch<TEvent> batch)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                batch = default;
                return false;
            }

            IInstanceInboxLane lane = _lanes[handle.DenseIndex];
            ulong generation = lane.SealedGeneration;
            if (!lane.HasSealedBatch)
            {
                batch = default;
                return false;
            }

            batch = new InstanceInboxSealedBatch<TEvent>(this, handle, generation);
            return true;
        }

        #endregion

        #region Internal Logic

        internal bool TryBindIntentRuntime(
            IInboxIntentOwner runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (_state != InstanceInboxState.Created ||
                _deferredLifecycleState != InstanceInboxState.None ||
                runtime == null ||
                runtime.IsDisposed ||
                runtime.GraphInstanceId != Owner ||
                (_intentRuntime != null && !ReferenceEquals(_intentRuntime, runtime)) ||
                !runtime.TryClaimInbox(this))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "An Inbox may bind exactly one live Intent runtime owned by the same GraphInstance before Start.");
                return false;
            }

            _intentRuntime = runtime;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void ResumeAfterTimelineResetNoFail()
        {
            if (!CanResumeAfterTimelineReset)
            {
                return;
            }

            ClearQueuedState();
            _requiresNewTimelineEpoch = true;
            _state = InstanceInboxState.Running;
        }

        internal void ResumeAfterTimelineResetNoFail(TimelineEpoch ownerTimelineEpoch)
        {
            if (!CanResumeAfterTimelineReset)
            {
                return;
            }

            ClearQueuedState();
            ClearOwnerSourceEpoch();
            _requiredOwnerTimelineEpoch = ownerTimelineEpoch;
            _hasRequiredOwnerTimelineEpoch = true;
            _requiresNewTimelineEpoch = true;
            _state = InstanceInboxState.Running;
        }

        internal void CancelRewindOrRestoreNoFail()
        {
            if (!CanCancelRewindOrRestore)
            {
                return;
            }

            ClearQueuedState();
            _state = InstanceInboxState.Running;
        }

        internal int GetSealedCount<TEvent>(
            InstanceInboxLaneHandle<TEvent> handle,
            ulong generation)
            where TEvent : unmanaged
        {
            if (!Matches(handle) ||
                !_lanes[handle.DenseIndex].HasSealedBatch ||
                _lanes[handle.DenseIndex].SealedGeneration != generation)
            {
                return 0;
            }

            return GetSealedCount(handle);
        }

        internal bool IsSealedBatchCurrent<TEvent>(
            InstanceInboxLaneHandle<TEvent> handle,
            ulong generation)
            where TEvent : unmanaged
        {
            return generation != 0UL && Matches(handle) &&
                   _lanes[handle.DenseIndex].HasSealedBatch &&
                   _lanes[handle.DenseIndex].SealedGeneration == generation;
        }

        internal void OnIntentRuntimeDisposed(IInboxIntentOwner runtime)
        {
            if (!ReferenceEquals(_intentRuntime, runtime))
            {
                return;
            }

            InstanceInboxState deferredState = _deferredLifecycleState;
            _deferredLifecycleState = InstanceInboxState.None;
            _intentRuntime = null;
            if (_state == InstanceInboxState.Created)
            {
                if (deferredState == InstanceInboxState.Disposed)
                {
                    ClearAllRuntimeState();
                    _state = InstanceInboxState.Disposed;
                }

                return;
            }

            if (_state != InstanceInboxState.Disposed)
            {
                ClearAllRuntimeState();
                _state = deferredState == InstanceInboxState.Disposed
                    ? InstanceInboxState.Disposed
                    : InstanceInboxState.Stopped;
            }
        }

        internal bool HasDeferredLifecycle(IInboxIntentOwner runtime)
        {
            return runtime != null &&
                   ReferenceEquals(_intentRuntime, runtime) &&
                   _deferredLifecycleState != InstanceInboxState.None;
        }

        internal void CompleteDeferredLifecycle(IInboxIntentOwner runtime)
        {
            if (!HasDeferredLifecycle(runtime) || runtime.IsExecutingUserCallback)
            {
                return;
            }

            InstanceInboxState targetState = _deferredLifecycleState;
            _deferredLifecycleState = InstanceInboxState.None;
            if (targetState == InstanceInboxState.Disposed)
            {
                Dispose();
            }
            else
            {
                Stop();
            }
        }

        internal bool TryReadSealed<TEvent>(
            InstanceInboxLaneHandle<TEvent> handle,
            ulong generation,
            int index,
            out EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!Matches(handle) ||
                !_lanes[handle.DenseIndex].HasSealedBatch ||
                _lanes[handle.DenseIndex].SealedGeneration != generation)
            {
                packet = default;
                return false;
            }

            return TryReadSealed(handle, index, out packet);
        }

        private bool Matches<TEvent>(InstanceInboxLaneHandle<TEvent> handle)
            where TEvent : unmanaged
        {
            if (!handle.IsValid ||
                !handle.IsOwnedBy(this) ||
                handle.Owner != Owner ||
                handle.EventDomainId != EventDomainId ||
                handle.DenseIndex < 0 ||
                handle.DenseIndex >= _laneCount)
            {
                return false;
            }

            InstanceInboxManifestEntry manifest = _lanes[handle.DenseIndex].Manifest;
            return manifest.EventTypeId == handle.EventTypeId;
        }

        private InboxEnqueueResult InspectSourceEpoch(
            GraphInstanceId source,
            TimelineEpoch epoch,
            out int sourceIndex,
            out bool sourceIsNew,
            out bool epochIsNew)
        {
            if (_hasRequiredOwnerTimelineEpoch && source == Owner)
            {
                sourceIndex = -1;
                sourceIsNew = false;
                epochIsNew = false;
                if (epoch.Value < _requiredOwnerTimelineEpoch.Value)
                {
                    return InboxEnqueueResult.StaleTimelineEpoch;
                }

                if (epoch.Value > _requiredOwnerTimelineEpoch.Value)
                {
                    return InboxEnqueueResult.InvalidPacket;
                }
            }

            for (int index = 0; index < _sourceCount; index++)
            {
                if (_sourceEpochs[index].Source != source)
                {
                    continue;
                }

                sourceIndex = index;
                sourceIsNew = false;
                epochIsNew = epoch.Value > _sourceEpochs[index].Epoch.Value;
                return epoch.Value < _sourceEpochs[index].Epoch.Value
                    ? InboxEnqueueResult.StaleTimelineEpoch
                    : InboxEnqueueResult.Accepted;
            }

            if (_sourceCount >= _sourceEpochs.Length)
            {
                sourceIndex = -1;
                sourceIsNew = false;
                epochIsNew = false;
                return InboxEnqueueResult.SourceWindowFull;
            }

            sourceIndex = _sourceCount;
            sourceIsNew = true;
            epochIsNew = false;
            return InboxEnqueueResult.Accepted;
        }

        private InboxEnqueueResult InspectDedup(in GraphEventEnvelope envelope)
        {
            for (int index = 0; index < _dedupEntries.Length; index++)
            {
                InboxDedupEntry entry = _dedupEntries[index];
                if (!entry.IsValid ||
                    entry.Source != envelope.SourceGraphInstanceId ||
                    entry.Epoch != envelope.SourceTimelineEpoch ||
                    entry.Sequence != envelope.SourceEventSequence)
                {
                    continue;
                }

                return entry.EventTypeId == envelope.EventTypeId
                    ? InboxEnqueueResult.Duplicate
                    : InboxEnqueueResult.EventSequenceConflict;
            }

            return InboxEnqueueResult.Accepted;
        }

        private void RecordDedup(in GraphEventEnvelope envelope)
        {
            _dedupEntries[_dedupWriteIndex] = new InboxDedupEntry(
                envelope.SourceGraphInstanceId,
                envelope.SourceTimelineEpoch,
                envelope.SourceEventSequence,
                envelope.EventTypeId);
            _dedupWriteIndex++;
            if (_dedupWriteIndex == _dedupEntries.Length)
            {
                _dedupWriteIndex = 0;
            }
        }

        private void ClearDedupForSource(GraphInstanceId source)
        {
            for (int index = 0; index < _dedupEntries.Length; index++)
            {
                if (_dedupEntries[index].Source == source)
                {
                    _dedupEntries[index] = default;
                }
            }
        }

        private void ClearOwnerSourceEpoch()
        {
            for (int index = 0; index < _sourceCount; index++)
            {
                if (_sourceEpochs[index].Source != Owner)
                {
                    continue;
                }

                _sourceCount--;
                for (int moveIndex = index; moveIndex < _sourceCount; moveIndex++)
                {
                    _sourceEpochs[moveIndex] = _sourceEpochs[moveIndex + 1];
                }

                _sourceEpochs[_sourceCount] = default;
                return;
            }
        }

        private InboxEnqueueResult RejectOverflow(EventReliability reliability)
        {
            _rejected++;
            if (reliability == EventReliability.Reliable)
            {
                _reliableOverflowFaults++;
                return InboxEnqueueResult.ReliableOverflowFaultRequired;
            }

            _unreliableOverflowDropped++;
            return InboxEnqueueResult.UnreliableOverflowDropped;
        }

        private void ClearQueuedState()
        {
            for (int index = 0; index < _laneCount; index++)
            {
                _lanes[index].Clear();
            }

            Array.Clear(_dedupEntries, 0, _dedupEntries.Length);
            _dedupWriteIndex = 0;
        }

        private void ClearAllRuntimeState()
        {
            ClearQueuedState();
            Array.Clear(_sourceEpochs, 0, _sourceEpochs.Length);
            _sourceCount = 0;
            _lastSealedTickFrame = default;
            _hasLastSealedTick = false;
            _requiresNewTimelineEpoch = false;
            _requiredOwnerTimelineEpoch = default;
            _hasRequiredOwnerTimelineEpoch = false;
        }

        private bool HasLiveIntentRuntime()
        {
            return _intentRuntime != null &&
                   !_intentRuntime.IsDisposed &&
                   _intentRuntime.GraphInstanceId == Owner;
        }

        private bool IsLifecycleTransitionBlocked()
        {
            return _deferredLifecycleState != InstanceInboxState.None ||
                   (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback);
        }

        private void CancelActiveIntentCollection()
        {
            if (HasLiveIntentRuntime() &&
                !_intentRuntime.IsExecutingUserCallback &&
                _intentRuntime.IsCollecting)
            {
                _intentRuntime.CancelCollection();
            }
        }

        private void ReleaseIntentRuntimeBinding()
        {
            IInboxIntentOwner runtime = _intentRuntime;
            _intentRuntime = null;
            runtime?.ReleaseInbox(this);
        }

        #endregion
    }

    /// <summary>供实例控制器清理或封存各个类型的收件通道。</summary>
    internal interface IInstanceInboxLane
    {
        InstanceInboxManifestEntry Manifest { get; }
        ulong SealedGeneration { get; }
        bool HasSealedBatch { get; }
        bool CanAdvanceGeneration { get; }
        void SealForTick();
        void Clear();
    }

    /// <summary>以定长存储分开保存待处理和本步已封存的事件。</summary>
    internal sealed class InstanceInboxLane<TEvent> : IInstanceInboxLane
        where TEvent : unmanaged
    {
        private EventPacket<TEvent>[] _incoming;
        private EventPacket<TEvent>[] _sealed;
        private int _incomingCount;
        private int _sealedCount;
        private ulong _sealedGeneration;
        private bool _hasSealedBatch;

        public InstanceInboxLane(
            EventTypeId eventTypeId,
            int capacity,
            bool allowSourceEcho)
        {
            Manifest = new InstanceInboxManifestEntry(
                eventTypeId,
                typeof(TEvent),
                capacity,
                allowSourceEcho);
            _incoming = new EventPacket<TEvent>[capacity];
            _sealed = new EventPacket<TEvent>[capacity];
        }

        public InstanceInboxManifestEntry Manifest { get; }
        public ulong SealedGeneration => _sealedGeneration;
        public bool HasSealedBatch => _hasSealedBatch;
        public bool CanAdvanceGeneration => _sealedGeneration != ulong.MaxValue;
        public bool HasIncomingCapacity => _incomingCount < _incoming.Length;
        public int SealedCount => _sealedCount;

        public void Enqueue(in EventPacket<TEvent> packet)
        {
            _incoming[_incomingCount] = packet;
            _incomingCount++;
        }

        public void SealForTick()
        {
            Array.Clear(_sealed, 0, _sealedCount);
            EventPacket<TEvent>[] previousSealed = _sealed;
            _sealed = _incoming;
            _incoming = previousSealed;
            _sealedCount = _incomingCount;
            _incomingCount = 0;
            _sealedGeneration++;
            _hasSealedBatch = true;
        }

        public bool TryReadSealed(int index, out EventPacket<TEvent> packet)
        {
            if (index < 0 || index >= _sealedCount)
            {
                packet = default;
                return false;
            }

            packet = _sealed[index];
            return true;
        }

        public void Clear()
        {
            Array.Clear(_incoming, 0, _incoming.Length);
            Array.Clear(_sealed, 0, _sealed.Length);
            _incomingCount = 0;
            _sealedCount = 0;
            if (_sealedGeneration != ulong.MaxValue)
            {
                _sealedGeneration++;
            }

            _hasSealedBatch = false;
        }
    }

    /// <summary>为投递去重区分同一来源在不同时间线代次的事件。</summary>
    internal readonly struct InboxSourceEpoch
    {
        public InboxSourceEpoch(
            GraphInstanceId source,
            TimelineEpoch epoch,
            EventSequence lastSequence)
        {
            Source = source;
            Epoch = epoch;
            LastSequence = lastSequence;
        }

        public GraphInstanceId Source { get; }
        public TimelineEpoch Epoch { get; }
        public EventSequence LastSequence { get; }
    }

    /// <summary>记录已处理的来源与事件序号，供收件时拒绝重复投递。</summary>
    internal readonly struct InboxDedupEntry
    {
        public InboxDedupEntry(
            GraphInstanceId source,
            TimelineEpoch epoch,
            EventSequence sequence,
            EventTypeId eventTypeId)
        {
            Source = source;
            Epoch = epoch;
            Sequence = sequence;
            EventTypeId = eventTypeId;
        }

        public GraphInstanceId Source { get; }
        public TimelineEpoch Epoch { get; }
        public EventSequence Sequence { get; }
        public EventTypeId EventTypeId { get; }
        public bool IsValid => Source.IsValid && Sequence.IsValid && EventTypeId.IsValid;
    }
}

namespace CoCoFlow.Runtime.StateGraph
{
}
