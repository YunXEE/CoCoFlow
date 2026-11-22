using System;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoEventOutboxRequirement : IEquatable<CoCoEventOutboxRequirement>
    {
        private readonly ICoCoEventOutboxLaneFactory _laneFactory;

        private CoCoEventOutboxRequirement(
            CoCoEventTypeId eventTypeId,
            CoCoEventDomainId eventDomainId,
            Type payloadType,
            int capacity,
            ICoCoEventOutboxLaneFactory laneFactory)
        {
            EventTypeId = eventTypeId;
            EventDomainId = eventDomainId;
            PayloadType = payloadType;
            Capacity = capacity;
            _laneFactory = laneFactory;
        }

        public CoCoEventTypeId EventTypeId { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public Type PayloadType { get; }
        public int Capacity { get; }
        public bool IsValid => EventTypeId.IsValid &&
                               EventDomainId.IsValid &&
                               PayloadType != null &&
                               Capacity > 0;

        public static bool TryCreate<TEvent>(
            CoCoEventTypeId eventTypeId,
            CoCoEventDomainId eventDomainId,
            int capacity,
            out CoCoEventOutboxRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (!eventTypeId.IsValid ||
                !eventDomainId.IsValid ||
                capacity <= 0 ||
                !CoCoStateFlowTypeRules.IsReferenceFreeValueType(typeof(TEvent)))
            {
                requirement = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.InvalidEventPacket,
                    "An EventOutbox lane requires valid event/domain identities, an unmanaged payload, and positive capacity.");
                return false;
            }

            requirement = new CoCoEventOutboxRequirement(
                eventTypeId,
                eventDomainId,
                typeof(TEvent),
                capacity,
                CoCoEventOutboxLaneFactory<TEvent>.Instance);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Equals(CoCoEventOutboxRequirement other)
        {
            return EventTypeId == other.EventTypeId &&
                   EventDomainId == other.EventDomainId &&
                   PayloadType == other.PayloadType &&
                   Capacity == other.Capacity;
        }

        public override bool Equals(object obj) =>
            obj is CoCoEventOutboxRequirement other && Equals(other);

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
            CoCoEventOutboxRequirement left,
            CoCoEventOutboxRequirement right) => left.Equals(right);

        public static bool operator !=(
            CoCoEventOutboxRequirement left,
            CoCoEventOutboxRequirement right) => !left.Equals(right);

        internal ICoCoEventOutboxLane CreateLane() =>
            IsValid && _laneFactory != null ? _laneFactory.Create(this) : null;
    }

    public readonly struct CoCoEventOutboxTarget : IEquatable<CoCoEventOutboxTarget>
    {
        private CoCoEventOutboxTarget(
            CoCoEventDeliveryMode deliveryMode,
            CoCoEventReliability reliability,
            CoCoGraphInstanceId targetGraphInstanceId,
            CoCoStableEntityId stableEntityId,
            CoCoActivationId activationId,
            CoCoCorrelationId correlationId)
        {
            DeliveryMode = deliveryMode;
            Reliability = reliability;
            TargetGraphInstanceId = targetGraphInstanceId;
            StableEntityId = stableEntityId;
            ActivationId = activationId;
            CorrelationId = correlationId;
        }

        public CoCoEventDeliveryMode DeliveryMode { get; }
        public CoCoEventReliability Reliability { get; }
        public CoCoGraphInstanceId TargetGraphInstanceId { get; }
        public CoCoStableEntityId StableEntityId { get; }
        public CoCoActivationId ActivationId { get; }
        public CoCoCorrelationId CorrelationId { get; }
        public bool IsValid =>
            (Reliability == CoCoEventReliability.Unreliable ||
             Reliability == CoCoEventReliability.Reliable) &&
            ((DeliveryMode == CoCoEventDeliveryMode.Targeted && TargetGraphInstanceId.IsValid) ||
             (DeliveryMode == CoCoEventDeliveryMode.DeclaredBroadcast && !TargetGraphInstanceId.IsValid));

        public static bool TryTargeted(
            CoCoGraphInstanceId targetGraphInstanceId,
            CoCoEventReliability reliability,
            CoCoStableEntityId stableEntityId,
            CoCoActivationId activationId,
            CoCoCorrelationId correlationId,
            out CoCoEventOutboxTarget target)
        {
            target = new CoCoEventOutboxTarget(
                CoCoEventDeliveryMode.Targeted,
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
            CoCoEventReliability reliability,
            CoCoStableEntityId stableEntityId,
            CoCoActivationId activationId,
            CoCoCorrelationId correlationId,
            out CoCoEventOutboxTarget target)
        {
            target = new CoCoEventOutboxTarget(
                CoCoEventDeliveryMode.DeclaredBroadcast,
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

        public bool Equals(CoCoEventOutboxTarget other)
        {
            return DeliveryMode == other.DeliveryMode &&
                   Reliability == other.Reliability &&
                   TargetGraphInstanceId == other.TargetGraphInstanceId &&
                   StableEntityId == other.StableEntityId &&
                   ActivationId == other.ActivationId &&
                   CorrelationId == other.CorrelationId;
        }

        public override bool Equals(object obj) => obj is CoCoEventOutboxTarget other && Equals(other);

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

        public static bool operator ==(CoCoEventOutboxTarget left, CoCoEventOutboxTarget right) =>
            left.Equals(right);

        public static bool operator !=(CoCoEventOutboxTarget left, CoCoEventOutboxTarget right) =>
            !left.Equals(right);
    }

    public enum CoCoEventOutboxWriteResult
    {
        None = 0,
        Accepted = 1,
        InvalidWriter = 2,
        UndeclaredEventType = 3,
        InvalidTarget = 4,
        PayloadTypeMismatch = 5,
        CapacityExceeded = 6
    }

    internal interface ICoCoEventOutboxSink
    {
        bool IsActive(ulong token, CoCoOperatorId operatorId);

        void RejectWrite(ulong token, CoCoOperatorId operatorId);

        CoCoEventOutboxWriteResult TryWrite<TEvent>(
            ulong token,
            CoCoOperatorId operatorId,
            CoCoEventOutboxRequirement requirement,
            CoCoEventOutboxTarget target,
            in TEvent payload)
            where TEvent : unmanaged;
    }

    internal readonly struct CoCoCommittedEventSource
    {
        public CoCoCommittedEventSource(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoEventSequence sequence)
        {
            GraphInstanceId = graphInstanceId;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            Sequence = sequence;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTimelineEpoch TimelineEpoch { get; }
        public CoCoTimelineTick Tick { get; }
        public CoCoEventSequence Sequence { get; }
        public bool IsValid => GraphInstanceId.IsValid && Sequence.IsValid;
    }

    internal interface ICoCoCommittedEventPublisher
    {
        bool TryPublish<TEvent>(in CoCoEventPacket<TEvent> packet)
            where TEvent : unmanaged;
    }

    internal interface ICoCoEventOutboxLaneFactory
    {
        ICoCoEventOutboxLane Create(CoCoEventOutboxRequirement requirement);
    }

    internal interface ICoCoEventOutboxLane
    {
        CoCoEventOutboxRequirement Requirement { get; }
        int Count { get; }
        void Reset();
        bool TryPublish(int itemIndex, in CoCoCommittedEventSource source, ICoCoCommittedEventPublisher publisher);
    }

    internal interface ICoCoEventOutboxLane<TEvent> : ICoCoEventOutboxLane
        where TEvent : unmanaged
    {
        bool TryAppend(CoCoEventOutboxTarget target, in TEvent payload, out int itemIndex);
    }

    internal sealed class CoCoEventOutboxLaneFactory<TEvent> : ICoCoEventOutboxLaneFactory
        where TEvent : unmanaged
    {
        public static readonly CoCoEventOutboxLaneFactory<TEvent> Instance =
            new CoCoEventOutboxLaneFactory<TEvent>();

        private CoCoEventOutboxLaneFactory()
        {
        }

        public ICoCoEventOutboxLane Create(CoCoEventOutboxRequirement requirement) =>
            requirement.IsValid && requirement.PayloadType == typeof(TEvent)
                ? new CoCoEventOutboxLane<TEvent>(requirement)
                : null;
    }

    internal sealed class CoCoEventOutboxLane<TEvent> : ICoCoEventOutboxLane<TEvent>
        where TEvent : unmanaged
    {
        private readonly CoCoEventOutboxTarget[] _targets;
        private readonly TEvent[] _payloads;

        public CoCoEventOutboxLane(CoCoEventOutboxRequirement requirement)
        {
            if (!requirement.IsValid || requirement.PayloadType != typeof(TEvent))
            {
                throw new ArgumentException("EventOutbox lane requirement does not match its payload type.", nameof(requirement));
            }

            Requirement = requirement;
            _targets = new CoCoEventOutboxTarget[requirement.Capacity];
            _payloads = new TEvent[requirement.Capacity];
        }

        public CoCoEventOutboxRequirement Requirement { get; }
        public int Count { get; private set; }

        public bool TryAppend(CoCoEventOutboxTarget target, in TEvent payload, out int itemIndex)
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
            in CoCoCommittedEventSource source,
            ICoCoCommittedEventPublisher publisher)
        {
            if (itemIndex < 0 || itemIndex >= Count || !source.IsValid || publisher == null)
            {
                return false;
            }

            CoCoEventOutboxTarget target = _targets[itemIndex];
            if (!CoCoActorEventEnvelope.TryCreate(
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
                    out CoCoActorEventEnvelope envelope) ||
                !CoCoEventPacket<TEvent>.TryCreate(
                    envelope,
                    _payloads[itemIndex],
                    out CoCoEventPacket<TEvent> packet))
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

    public readonly struct CoCoEventOutboxWriter
    {
        private readonly CoCoOperatorDescriptor _descriptor;
        private readonly ICoCoEventOutboxSink _sink;
        private readonly ulong _token;

        internal CoCoEventOutboxWriter(
            CoCoOperatorDescriptor descriptor,
            ICoCoEventOutboxSink sink,
            ulong token)
        {
            _descriptor = descriptor;
            _sink = sink;
            _token = token;
        }

        public CoCoOperatorId OperatorId => _descriptor?.OperatorId ?? default;
        public bool IsValid => _descriptor != null &&
                               _descriptor.IsValid &&
                               _sink != null &&
                               _sink.IsActive(_token, OperatorId);

        public CoCoEventOutboxWriteResult TryWrite<TEvent>(
            CoCoEventOutboxRequirement requirement,
            CoCoEventOutboxTarget target,
            in TEvent payload)
            where TEvent : unmanaged
        {
            if (!IsValid)
            {
                return RejectWrite(CoCoEventOutboxWriteResult.InvalidWriter);
            }

            if (!requirement.IsValid || !_descriptor.EmitsEvent(requirement))
            {
                return RejectWrite(CoCoEventOutboxWriteResult.UndeclaredEventType);
            }

            if (requirement.PayloadType != typeof(TEvent))
            {
                return RejectWrite(CoCoEventOutboxWriteResult.PayloadTypeMismatch);
            }

            if (!target.IsValid)
            {
                return RejectWrite(CoCoEventOutboxWriteResult.InvalidTarget);
            }

            CoCoEventOutboxWriteResult result =
                _sink.TryWrite(_token, OperatorId, requirement, target, payload);
            return result == CoCoEventOutboxWriteResult.Accepted
                ? result
                : RejectWrite(result);
        }

        private CoCoEventOutboxWriteResult RejectWrite(CoCoEventOutboxWriteResult result)
        {
            _sink?.RejectWrite(_token, OperatorId);
            return result;
        }
    }
}
