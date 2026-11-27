using System;
using CoCoFlow.Runtime.Core;

// 实例在有容量的通道里暂存一帧的事件，提交后 Host 向对应订阅者发布已接受的条目。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>记录已提交事件的来源实例与时间线身份，供 Host 发布时构造信封。</summary>
    internal readonly struct CommittedEventSource
    {
        public CommittedEventSource(
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            EventSequence sequence)
        {
            GraphInstanceId = graphInstanceId;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            Sequence = sequence;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TimelineEpoch TimelineEpoch { get; }
        public TimelineTick Tick { get; }
        public EventSequence Sequence { get; }
        public bool IsValid => GraphInstanceId.IsValid && Sequence.IsValid;
    }

    /// <summary>在整步成功后向已登记的目标发布暂存载荷。</summary>
    internal interface ICommittedEventPublisher
    {
        bool TryPublish<TEvent>(in EventPacket<TEvent> packet)
            where TEvent : unmanaged;
    }

    /// <summary>为声明的载荷类型和容量创建本实例的出件通道。</summary>
    internal interface IEventOutboxLaneFactory
    {
        IEventOutboxLane Create(EventOutboxRequirement requirement);
    }

    /// <summary>为事务清空、准备或发布各类型通道中待发的事件。</summary>
    internal interface IEventOutboxLane
    {
        EventOutboxRequirement Requirement { get; }
        int Count { get; }
        void Reset();
        bool TryPublish(int itemIndex, in CommittedEventSource source, ICommittedEventPublisher publisher);
    }

    /// <summary>为事务清空、准备或发布各类型通道中待发的事件。</summary>
    internal interface IEventOutboxLane<TEvent> : IEventOutboxLane
        where TEvent : unmanaged
    {
        bool TryAppend(EventOutboxTarget target, in TEvent payload, out int itemIndex);
    }

    /// <summary>按给定的非托管载荷类型实例化定长出件队列。</summary>
    internal sealed class EventOutboxLaneFactory<TEvent> : IEventOutboxLaneFactory
        where TEvent : unmanaged
    {
        public static readonly EventOutboxLaneFactory<TEvent> Instance =
            new EventOutboxLaneFactory<TEvent>();

        private EventOutboxLaneFactory()
        {
        }

        public IEventOutboxLane Create(EventOutboxRequirement requirement) =>
            requirement.IsValid && requirement.PayloadType == typeof(TEvent)
                ? new EventOutboxLane<TEvent>(requirement)
                : null;
    }

    /// <summary>按声明的容量暂存带目标的类型化事件，在提交后按序发布。</summary>
    internal sealed class EventOutboxLane<TEvent> : IEventOutboxLane<TEvent>
        where TEvent : unmanaged
    {
        private readonly EventOutboxTarget[] _targets;
        private readonly TEvent[] _payloads;

        public EventOutboxLane(EventOutboxRequirement requirement)
        {
            if (!requirement.IsValid || requirement.PayloadType != typeof(TEvent))
            {
                throw new ArgumentException("EventOutbox lane requirement does not match its payload type.", nameof(requirement));
            }

            Requirement = requirement;
            _targets = new EventOutboxTarget[requirement.Capacity];
            _payloads = new TEvent[requirement.Capacity];
        }

        public EventOutboxRequirement Requirement { get; }
        public int Count { get; private set; }

        public bool TryAppend(EventOutboxTarget target, in TEvent payload, out int itemIndex)
        {
            if (!target.IsValid || Count >= _payloads.Length)
            {
                itemIndex = -1;
                return false;
            }

            itemIndex = Count;
            _targets[itemIndex] = target;
            _payloads[itemIndex] = payload;
            Count++;
            return true;
        }

        public bool TryPublish(
            int itemIndex,
            in CommittedEventSource source,
            ICommittedEventPublisher publisher)
        {
            if (itemIndex < 0 || itemIndex >= Count || !source.IsValid || publisher == null)
            {
                return false;
            }

            EventOutboxTarget target = _targets[itemIndex];
            if (!GraphEventEnvelope.TryCreate(
                    Requirement.EventTypeId,
                    Requirement.EventDomainId,
                    source.GraphInstanceId,
                    target.TargetGraphInstanceId,
                    source.TimelineEpoch,
                    source.Tick,
                    source.Sequence,
                    target.DeliveryMode,
                    target.Reliability,
                    target.StableEntityId,
                    target.ActivationId,
                    target.CorrelationId,
                    out GraphEventEnvelope envelope) ||
                !EventPacket<TEvent>.TryCreate(
                    envelope,
                    _payloads[itemIndex],
                    out EventPacket<TEvent> packet))
            {
                return false;
            }

            return publisher.TryPublish(packet);
        }

        public void Reset()
        {
            Array.Clear(_targets, 0, Count);
            Array.Clear(_payloads, 0, Count);
            Count = 0;
        }
    }
}
