using System;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>
    /// Identifies one graph-level Event-to-Intent declaration without naming a concrete Adapter.
    /// </summary>
    public readonly struct EventToIntentDeclarationKey :
        IEquatable<EventToIntentDeclarationKey>,
        IComparable<EventToIntentDeclarationKey>
    {
        private EventToIntentDeclarationKey(
            EventTypeId eventTypeId,
            IntentId providedIntentId)
        {
            EventTypeId = eventTypeId;
            ProvidedIntentId = providedIntentId;
        }

        public EventTypeId EventTypeId { get; }
        public IntentId ProvidedIntentId { get; }
        public bool IsValid => EventTypeId.IsValid && ProvidedIntentId.IsValid;

        public static bool TryCreate(
            EventTypeId eventTypeId,
            IntentId providedIntentId,
            out EventToIntentDeclarationKey key)
        {
            if (!eventTypeId.IsValid || !providedIntentId.IsValid)
            {
                key = default;
                return false;
            }

            key = new EventToIntentDeclarationKey(eventTypeId, providedIntentId);
            return true;
        }

        public int CompareTo(EventToIntentDeclarationKey other)
        {
            int comparison = EventTypeId.High.CompareTo(other.EventTypeId.High);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = EventTypeId.Low.CompareTo(other.EventTypeId.Low);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = ProvidedIntentId.High.CompareTo(other.ProvidedIntentId.High);
            return comparison != 0
                ? comparison
                : ProvidedIntentId.Low.CompareTo(other.ProvidedIntentId.Low);
        }

        public bool Equals(EventToIntentDeclarationKey other) =>
            EventTypeId == other.EventTypeId && ProvidedIntentId == other.ProvidedIntentId;

        public override bool Equals(object obj) =>
            obj is EventToIntentDeclarationKey other && Equals(other);

        public override int GetHashCode() =>
            unchecked((EventTypeId.GetHashCode() * 397) ^ ProvidedIntentId.GetHashCode());

        public override string ToString() => $"{EventTypeId}:{ProvidedIntentId}";

        public static bool operator ==(
            EventToIntentDeclarationKey left,
            EventToIntentDeclarationKey right) => left.Equals(right);

        public static bool operator !=(
            EventToIntentDeclarationKey left,
            EventToIntentDeclarationKey right) => !left.Equals(right);
    }

    public sealed class CompiledEventToIntentDeclaration
    {
        internal CompiledEventToIntentDeclaration(
            IGraphEventToIntentDeclarationRegistration registration)
        {
            EventDomainId = registration.EventDomainId;
            EventTypeId = registration.EventTypeId;
            EventPayloadType = registration.EventPayloadType;
            ProvidedIntentId = registration.ProvidedIntentId;
            ProvidedIntentType = registration.ProvidedIntentType;
        }

        public EventDomainId EventDomainId { get; }
        public EventTypeId EventTypeId { get; }
        public Type EventPayloadType { get; }
        public IntentId ProvidedIntentId { get; }
        public Type ProvidedIntentType { get; }
    }

    internal interface IGraphEventToIntentDeclarationRegistration
    {
        EventToIntentDeclarationKey Key { get; }
        EventDomainId EventDomainId { get; }
        EventTypeId EventTypeId { get; }
        Type EventPayloadType { get; }
        IntentId ProvidedIntentId { get; }
        Type ProvidedIntentType { get; }
    }

    internal sealed class GraphEventToIntentDeclarationRegistration :
        IGraphEventToIntentDeclarationRegistration
    {
        internal GraphEventToIntentDeclarationRegistration(
            EventToIntentDeclarationKey key,
            EventDomainId eventDomainId,
            Type eventPayloadType,
            Type providedIntentType)
        {
            Key = key;
            EventDomainId = eventDomainId;
            EventPayloadType = eventPayloadType;
            ProvidedIntentType = providedIntentType;
        }

        public EventToIntentDeclarationKey Key { get; }
        public EventDomainId EventDomainId { get; }
        public EventTypeId EventTypeId => Key.EventTypeId;
        public Type EventPayloadType { get; }
        public IntentId ProvidedIntentId => Key.ProvidedIntentId;
        public Type ProvidedIntentType { get; }
    }
}
