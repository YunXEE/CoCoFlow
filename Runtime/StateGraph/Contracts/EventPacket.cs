using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 发送方把类型化载荷与路由、目标、可靠性和序号一并传给接收实例，收件箱按这些信息选择队列。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>区分给指定图实例和按声明广播的事件。</summary>
    public enum EventDeliveryMode
    {
        None = 0,
        Targeted = 1,
        DeclaredBroadcast = 2
    }

    /// <summary>指定队列压力下事件可以丢弃还是需要报告可靠投递故障。</summary>
    public enum EventReliability
    {
        None = 0,
        Unreliable = 1,
        Reliable = 2
    }

    /// <summary>表示实例接收事件的结果，包括接收、丢弃和无效目标。</summary>
    public enum InboxEnqueueResult
    {
        None = 0,
        Accepted = 1,
        InvalidPacket = 2,
        MailboxUnavailable = 3,
        EventDomainMismatch = 4,
        EventTargetMismatch = 5,
        UndeclaredEventType = 6,
        SourceEchoRejected = 7,
        Duplicate = 8,
        StaleTimelineEpoch = 9,
        EventSequenceConflict = 10,
        SourceWindowFull = 11,
        UnreliableOverflowDropped = 12,
        ReliableOverflowFaultRequired = 13,
        RewindOrRestoreDropped = 14
    }

    /// <summary>携带定向或广播事件的身份、时间线、顺序及投递信息。</summary>
    public readonly struct GraphEventEnvelope : IEquatable<GraphEventEnvelope>
    {

        #region Public API

        public EventTypeId EventTypeId { get; }
        public EventDomainId EventDomainId { get; }
        public GraphInstanceId SourceGraphInstanceId { get; }
        public GraphInstanceId TargetGraphInstanceId { get; }
        public TimelineEpoch SourceTimelineEpoch { get; }
        public TimelineTick SourceTick { get; }
        public EventSequence SourceEventSequence { get; }
        public EventDeliveryMode DeliveryMode { get; }
        public EventReliability Reliability { get; }
        public StableEntityId StableEntityId { get; }
        public ActivationId ActivationId { get; }
        public CorrelationId CorrelationId { get; }

        public bool IsValid => EventTypeId.IsValid &&
                               EventDomainId.IsValid &&
                               SourceGraphInstanceId.IsValid &&
                               SourceEventSequence.IsValid &&
                               (DeliveryMode == EventDeliveryMode.Targeted ||
                                DeliveryMode == EventDeliveryMode.DeclaredBroadcast) &&
                               (Reliability == EventReliability.Unreliable ||
                                Reliability == EventReliability.Reliable) &&
                               ((DeliveryMode == EventDeliveryMode.Targeted &&
                                 TargetGraphInstanceId.IsValid) ||
                                (DeliveryMode == EventDeliveryMode.DeclaredBroadcast &&
                                 !TargetGraphInstanceId.IsValid));

        /// <summary>记录来源、目标、时间线和序号，寻址和身份字段不符合投递模式时返回 false。</summary>
        public static bool TryCreate(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            GraphInstanceId sourceGraphInstanceId,
            GraphInstanceId targetGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            EventSequence sourceEventSequence,
            EventDeliveryMode deliveryMode,
            EventReliability reliability,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId,
            out GraphEventEnvelope envelope)
        {
            envelope = new GraphEventEnvelope(
                eventTypeId,
                eventDomainId,
                sourceGraphInstanceId,
                targetGraphInstanceId,
                sourceTimelineEpoch,
                sourceTick,
                sourceEventSequence,
                deliveryMode,
                reliability,
                stableEntityId,
                activationId,
                correlationId);

            if (envelope.IsValid)
            {
                return true;
            }

            envelope = default;
            return false;
        }

        /// <summary>按<c>EventTypeId</c>、<c>EventDomainId</c>、<c>SourceGraphInstanceId</c>、<c>TargetGraphInstanceId</c>、<c>SourceTimelineEpoch</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(GraphEventEnvelope other)
        {
            return EventTypeId == other.EventTypeId &&
                   EventDomainId == other.EventDomainId &&
                   SourceGraphInstanceId == other.SourceGraphInstanceId &&
                   TargetGraphInstanceId == other.TargetGraphInstanceId &&
                   SourceTimelineEpoch == other.SourceTimelineEpoch &&
                   SourceTick == other.SourceTick &&
                   SourceEventSequence == other.SourceEventSequence &&
                   DeliveryMode == other.DeliveryMode &&
                   Reliability == other.Reliability &&
                   StableEntityId == other.StableEntityId &&
                   ActivationId == other.ActivationId &&
                   CorrelationId == other.CorrelationId;
        }

        /// <summary>仅当对象属于同类型并且<c>EventTypeId</c>、<c>EventDomainId</c>、<c>SourceGraphInstanceId</c>、<c>TargetGraphInstanceId</c>、<c>SourceTimelineEpoch</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is GraphEventEnvelope other && Equals(other);

        /// <summary>为哈希集合计算与<c>EventTypeId</c>、<c>EventDomainId</c>、<c>SourceGraphInstanceId</c>、<c>TargetGraphInstanceId</c>、<c>SourceTimelineEpoch</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ EventDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceGraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ TargetGraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceTimelineEpoch.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceTick.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceEventSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)DeliveryMode;
                hashCode = (hashCode * 397) ^ (int)Reliability;
                hashCode = (hashCode * 397) ^ StableEntityId.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ CorrelationId.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>EventTypeId</c>、<c>EventDomainId</c>、<c>SourceGraphInstanceId</c>、<c>TargetGraphInstanceId</c>、<c>SourceTimelineEpoch</c>，相等时返回 true。</summary>
        public static bool operator ==(GraphEventEnvelope left, GraphEventEnvelope right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>EventTypeId</c>、<c>EventDomainId</c>、<c>SourceGraphInstanceId</c>、<c>TargetGraphInstanceId</c>、<c>SourceTimelineEpoch</c>，存在差异时返回 true。</summary>
        public static bool operator !=(GraphEventEnvelope left, GraphEventEnvelope right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        private GraphEventEnvelope(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            GraphInstanceId sourceGraphInstanceId,
            GraphInstanceId targetGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            EventSequence sourceEventSequence,
            EventDeliveryMode deliveryMode,
            EventReliability reliability,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId)
        {
            EventTypeId = eventTypeId;
            EventDomainId = eventDomainId;
            SourceGraphInstanceId = sourceGraphInstanceId;
            TargetGraphInstanceId = targetGraphInstanceId;
            SourceTimelineEpoch = sourceTimelineEpoch;
            SourceTick = sourceTick;
            SourceEventSequence = sourceEventSequence;
            DeliveryMode = deliveryMode;
            Reliability = reliability;
            StableEntityId = stableEntityId;
            ActivationId = activationId;
            CorrelationId = correlationId;
        }

        #endregion
    }

    /// <summary>将路由信封和一个不可变的类型化载荷一同交给接收方。</summary>
    public readonly struct EventPacket<TEvent>
        where TEvent : unmanaged
    {

        #region Public API

        public GraphEventEnvelope Envelope { get; }
        public TEvent Payload { get; }
        public bool IsValid => Envelope.IsValid;

        /// <summary>将有效信封与非托管载荷配成数据包。信封无效时输出默认值并返回 false。</summary>
        public static bool TryCreate(
            GraphEventEnvelope envelope,
            in TEvent payload,
            out EventPacket<TEvent> packet)
        {
            if (!envelope.IsValid)
            {
                packet = default;
                return false;
            }

            packet = new EventPacket<TEvent>(envelope, payload);
            return true;
        }

        #endregion

        #region Internal Logic

        private EventPacket(GraphEventEnvelope envelope, TEvent payload)
        {
            Envelope = envelope;
            Payload = payload;
        }

        #endregion
    }
}
