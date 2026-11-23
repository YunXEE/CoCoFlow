using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct EventOutboxRequirement : IEquatable<EventOutboxRequirement>
    {
        private readonly IEventOutboxLaneFactory _laneFactory;

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

        public EventTypeId EventTypeId { get; }
        public EventDomainId EventDomainId { get; }
        public Type PayloadType { get; }
        public int Capacity { get; }
        public bool IsValid => EventTypeId.IsValid &&
                               EventDomainId.IsValid &&
                               PayloadType != null &&
                               Capacity > 0;

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

        public bool Equals(EventOutboxRequirement other)
        {
            return EventTypeId == other.EventTypeId &&
                   EventDomainId == other.EventDomainId &&
                   PayloadType == other.PayloadType &&
                   Capacity == other.Capacity;
        }

        public override bool Equals(object obj) =>
            obj is EventOutboxRequirement other && Equals(other);

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

        public static bool operator ==(
            EventOutboxRequirement left,
            EventOutboxRequirement right) => left.Equals(right);

        public static bool operator !=(
            EventOutboxRequirement left,
            EventOutboxRequirement right) => !left.Equals(right);

        internal IEventOutboxLane CreateLane() =>
            IsValid && _laneFactory != null ? _laneFactory.Create(this) : null;
    }

    public readonly struct EventOutboxTarget : IEquatable<EventOutboxTarget>
    {
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

        public bool Equals(EventOutboxTarget other)
        {
            return DeliveryMode == other.DeliveryMode &&
                   Reliability == other.Reliability &&
                   TargetGraphInstanceId == other.TargetGraphInstanceId &&
                   StableEntityId == other.StableEntityId &&
                   ActivationId == other.ActivationId &&
                   CorrelationId == other.CorrelationId;
        }

        public override bool Equals(object obj) => obj is EventOutboxTarget other && Equals(other);

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

        public static bool operator ==(EventOutboxTarget left, EventOutboxTarget right) =>
            left.Equals(right);

        public static bool operator !=(EventOutboxTarget left, EventOutboxTarget right) =>
            !left.Equals(right);
    }

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

    internal interface ICommittedEventPublisher
    {
        bool TryPublish<TEvent>(in EventPacket<TEvent> packet)
            where TEvent : unmanaged;
    }

    internal interface IEventOutboxLaneFactory
    {
        IEventOutboxLane Create(EventOutboxRequirement requirement);
    }

    internal interface IEventOutboxLane
    {
        EventOutboxRequirement Requirement { get; }
        int Count { get; }
        void Reset();
        bool TryPublish(int itemIndex, in CommittedEventSource source, ICommittedEventPublisher publisher);
    }

    internal interface IEventOutboxLane<TEvent> : IEventOutboxLane
        where TEvent : unmanaged
    {
        bool TryAppend(EventOutboxTarget target, in TEvent payload, out int itemIndex);
    }

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
            if (!ActorEventEnvelope.TryCreate(
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
                    out ActorEventEnvelope envelope) ||
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

    public readonly struct EventOutboxWriter
    {
        private readonly OperatorDescriptor _descriptor;
        private readonly IEventOutboxSink _sink;
        private readonly ulong _token;

        internal EventOutboxWriter(
            OperatorDescriptor descriptor,
            IEventOutboxSink sink,
            ulong token)
        {
            _descriptor = descriptor;
            _sink = sink;
            _token = token;
        }

        public OperatorId OperatorId => _descriptor?.OperatorId ?? default;
        public bool IsValid => _descriptor != null &&
                               _descriptor.IsValid &&
                               _sink != null &&
                               _sink.IsActive(_token, OperatorId);

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

        private EventOutboxWriteResult RejectWrite(EventOutboxWriteResult result)
        {
            _sink?.RejectWrite(_token, OperatorId);
            return result;
        }
    }
}
