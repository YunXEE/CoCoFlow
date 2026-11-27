using System;
using CoCoFlow.Runtime.Core;

// Operator 声明可发送的事件类型和容量，Host 接受本步后才由对应的通道发布。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>声明 Operator 输出事件的类型、事件域与可用容量。</summary>
    public readonly struct EventOutboxRequirement : IEquatable<EventOutboxRequirement>
    {
        private readonly IEventOutboxLaneFactory _laneFactory;

        public EventTypeId EventTypeId { get; }
        public EventDomainId EventDomainId { get; }
        public Type PayloadType { get; }
        public int Capacity { get; }
        public bool IsValid => EventTypeId.IsValid &&
                               EventDomainId.IsValid &&
                               PayloadType != null &&
                               Capacity > 0;

        #region Public API

        /// <summary>用载荷类型、事件域和正容量创建出件声明，标识或容量不合法时返回 false 并提供诊断。</summary>
        public static bool TryCreate<TEvent>(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            int capacity,
            out EventOutboxRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (!eventTypeId.IsValid ||
                !eventDomainId.IsValid ||
                capacity <= 0 ||
                !StateFlowTypeRules.IsReferenceFreeValueType(typeof(TEvent)))
            {
                requirement = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.InvalidEventPacket,
                    "An EventOutbox lane requires valid event/domain identities, an unmanaged payload, and positive capacity.");
                return false;
            }

            requirement = new EventOutboxRequirement(
                eventTypeId,
                eventDomainId,
                typeof(TEvent),
                capacity,
                EventOutboxLaneFactory<TEvent>.Instance);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>按<c>EventTypeId</c>、<c>EventDomainId</c>、<c>PayloadType</c>、<c>Capacity</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(EventOutboxRequirement other)
        {
            return EventTypeId == other.EventTypeId &&
                   EventDomainId == other.EventDomainId &&
                   PayloadType == other.PayloadType &&
                   Capacity == other.Capacity;
        }

        /// <summary>仅当对象属于同类型并且<c>EventTypeId</c>、<c>EventDomainId</c>、<c>PayloadType</c>、<c>Capacity</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is EventOutboxRequirement other && Equals(other);

        /// <summary>为哈希集合计算与<c>EventTypeId</c>、<c>EventDomainId</c>、<c>PayloadType</c>、<c>Capacity</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ EventDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ (PayloadType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ Capacity;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>EventTypeId</c>、<c>EventDomainId</c>、<c>PayloadType</c>、<c>Capacity</c>，相等时返回 true。</summary>
        public static bool operator ==(
            EventOutboxRequirement left,
            EventOutboxRequirement right) => left.Equals(right);

        /// <summary>比较两端的<c>EventTypeId</c>、<c>EventDomainId</c>、<c>PayloadType</c>、<c>Capacity</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            EventOutboxRequirement left,
            EventOutboxRequirement right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private EventOutboxRequirement(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            Type payloadType,
            int capacity,
            IEventOutboxLaneFactory laneFactory)
        {
            EventTypeId = eventTypeId;
            EventDomainId = eventDomainId;
            PayloadType = payloadType;
            Capacity = capacity;
            _laneFactory = laneFactory;
        }

        internal IEventOutboxLane CreateLane() =>
            IsValid && _laneFactory != null ? _laneFactory.Create(this) : null;

        #endregion
    }

    /// <summary>描述事件的寻址方式、目标实例及可选的关联和可靠性信息。</summary>
    public readonly struct EventOutboxTarget : IEquatable<EventOutboxTarget>
    {

        #region Public API

        public EventDeliveryMode DeliveryMode { get; }
        public EventReliability Reliability { get; }
        public GraphInstanceId TargetGraphInstanceId { get; }
        public StableEntityId StableEntityId { get; }
        public ActivationId ActivationId { get; }
        public CorrelationId CorrelationId { get; }
        public bool IsValid =>
            (Reliability == EventReliability.Unreliable ||
             Reliability == EventReliability.Reliable) &&
            ((DeliveryMode == EventDeliveryMode.Targeted && TargetGraphInstanceId.IsValid) ||
             (DeliveryMode == EventDeliveryMode.DeclaredBroadcast && !TargetGraphInstanceId.IsValid));

        /// <summary>为指定图实例创建定向投递目标，目标或可靠性字段无效时返回 false。</summary>
        public static bool TryTargeted(
            GraphInstanceId targetGraphInstanceId,
            EventReliability reliability,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId,
            out EventOutboxTarget target)
        {
            target = new EventOutboxTarget(
                EventDeliveryMode.Targeted,
                reliability,
                targetGraphInstanceId,
                stableEntityId,
                activationId,
                correlationId);
            if (target.IsValid)
            {
                return true;
            }

            target = default;
            return false;
        }

        /// <summary>创建按声明域广播的投递目标，参数不符合广播合同则返回 false。</summary>
        public static bool TryDeclaredBroadcast(
            EventReliability reliability,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId,
            out EventOutboxTarget target)
        {
            target = new EventOutboxTarget(
                EventDeliveryMode.DeclaredBroadcast,
                reliability,
                default,
                stableEntityId,
                activationId,
                correlationId);
            if (target.IsValid)
            {
                return true;
            }

            target = default;
            return false;
        }

        /// <summary>按<c>DeliveryMode</c>、<c>Reliability</c>、<c>TargetGraphInstanceId</c>、<c>StableEntityId</c>、<c>ActivationId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(EventOutboxTarget other)
        {
            return DeliveryMode == other.DeliveryMode &&
                   Reliability == other.Reliability &&
                   TargetGraphInstanceId == other.TargetGraphInstanceId &&
                   StableEntityId == other.StableEntityId &&
                   ActivationId == other.ActivationId &&
                   CorrelationId == other.CorrelationId;
        }

        /// <summary>仅当对象属于同类型并且<c>DeliveryMode</c>、<c>Reliability</c>、<c>TargetGraphInstanceId</c>、<c>StableEntityId</c>、<c>ActivationId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is EventOutboxTarget other && Equals(other);

        /// <summary>为哈希集合计算与<c>DeliveryMode</c>、<c>Reliability</c>、<c>TargetGraphInstanceId</c>、<c>StableEntityId</c>、<c>ActivationId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)DeliveryMode;
                hashCode = (hashCode * 397) ^ (int)Reliability;
                hashCode = (hashCode * 397) ^ TargetGraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ StableEntityId.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ CorrelationId.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>DeliveryMode</c>、<c>Reliability</c>、<c>TargetGraphInstanceId</c>、<c>StableEntityId</c>、<c>ActivationId</c>，相等时返回 true。</summary>
        public static bool operator ==(EventOutboxTarget left, EventOutboxTarget right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>DeliveryMode</c>、<c>Reliability</c>、<c>TargetGraphInstanceId</c>、<c>StableEntityId</c>、<c>ActivationId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(EventOutboxTarget left, EventOutboxTarget right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        private EventOutboxTarget(
            EventDeliveryMode deliveryMode,
            EventReliability reliability,
            GraphInstanceId targetGraphInstanceId,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId)
        {
            DeliveryMode = deliveryMode;
            Reliability = reliability;
            TargetGraphInstanceId = targetGraphInstanceId;
            StableEntityId = stableEntityId;
            ActivationId = activationId;
            CorrelationId = correlationId;
        }

        #endregion
    }

    /// <summary>标明输出通道是否接受该事件，供 Operator 处理容量和合同错误。</summary>
    public enum EventOutboxWriteResult
    {
        None = 0,
        Accepted = 1,
        InvalidWriter = 2,
        UndeclaredEventType = 3,
        InvalidTarget = 4,
        PayloadTypeMismatch = 5,
        CapacityExceeded = 6
    }

    /// <summary>按当前事务验证事件声明与目标，并暂存通过验证的事件。</summary>
    internal interface IEventOutboxSink
    {
        bool IsActive(ulong token, OperatorId operatorId);

        void RejectWrite(ulong token, OperatorId operatorId);

        EventOutboxWriteResult TryWrite<TEvent>(
            ulong token,
            OperatorId operatorId,
            EventOutboxRequirement requirement,
            EventOutboxTarget target,
            in TEvent payload)
            where TEvent : unmanaged;
    }

    /// <summary>让 Operator 在一次有效回调内按已声明的类型和目标暂存事件。</summary>
    public readonly struct EventOutboxWriter
    {
        private readonly OperatorDescriptor _descriptor;
        private readonly IEventOutboxSink _sink;
        private readonly ulong _token;

        public OperatorId OperatorId => _descriptor?.OperatorId ?? default;
        public bool IsValid => _descriptor != null &&
                               _descriptor.IsValid &&
                               _sink != null &&
                               _sink.IsActive(_token, OperatorId);

        #region Public API

        /// <summary>在当前候选为已声明的事件暂存目标与载荷，返回值区分接收、容量耗尽和写入无效。</summary>
        public EventOutboxWriteResult TryWrite<TEvent>(
            EventOutboxRequirement requirement,
            EventOutboxTarget target,
            in TEvent payload)
            where TEvent : unmanaged
        {
            if (!IsValid)
            {
                return RejectWrite(EventOutboxWriteResult.InvalidWriter);
            }

            if (!requirement.IsValid || !_descriptor.EmitsEvent(requirement))
            {
                return RejectWrite(EventOutboxWriteResult.UndeclaredEventType);
            }

            if (requirement.PayloadType != typeof(TEvent))
            {
                return RejectWrite(EventOutboxWriteResult.PayloadTypeMismatch);
            }

            if (!target.IsValid)
            {
                return RejectWrite(EventOutboxWriteResult.InvalidTarget);
            }

            EventOutboxWriteResult result =
                _sink.TryWrite(_token, OperatorId, requirement, target, payload);
            return result == EventOutboxWriteResult.Accepted
                ? result
                : RejectWrite(result);
        }

        #endregion

        #region Internal Logic

        internal EventOutboxWriter(
            OperatorDescriptor descriptor,
            IEventOutboxSink sink,
            ulong token)
        {
            _descriptor = descriptor;
            _sink = sink;
            _token = token;
        }

        private EventOutboxWriteResult RejectWrite(EventOutboxWriteResult result)
        {
            _sink?.RejectWrite(_token, OperatorId);
            return result;
        }

        #endregion
    }
}
