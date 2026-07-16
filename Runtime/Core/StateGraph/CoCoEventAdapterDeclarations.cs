using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Identifies one graph-level Event-to-Intent declaration without naming a concrete Adapter.
    /// </summary>
    public readonly struct CoCoEventToIntentDeclarationKey :
        IEquatable<CoCoEventToIntentDeclarationKey>,
        IComparable<CoCoEventToIntentDeclarationKey>
    {
        private CoCoEventToIntentDeclarationKey(
            CoCoEventTypeId eventTypeId,
            CoCoIntentId providedIntentId)
        {
            EventTypeId = eventTypeId;
            ProvidedIntentId = providedIntentId;
        }

        public CoCoEventTypeId EventTypeId { get; }
        public CoCoIntentId ProvidedIntentId { get; }
        public bool IsValid => EventTypeId.IsValid && ProvidedIntentId.IsValid;

        public static bool TryCreate(
            CoCoEventTypeId eventTypeId,
            CoCoIntentId providedIntentId,
            out CoCoEventToIntentDeclarationKey key)
        {
            if (!eventTypeId.IsValid || !providedIntentId.IsValid)
            {
                key = default;
                return false;
            }

            key = new CoCoEventToIntentDeclarationKey(eventTypeId, providedIntentId);
            return true;
        }

        public int CompareTo(CoCoEventToIntentDeclarationKey other)
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

        public bool Equals(CoCoEventToIntentDeclarationKey other) =>
            EventTypeId == other.EventTypeId && ProvidedIntentId == other.ProvidedIntentId;

        public override bool Equals(object obj) =>
            obj is CoCoEventToIntentDeclarationKey other && Equals(other);

        public override int GetHashCode() =>
            unchecked((EventTypeId.GetHashCode() * 397) ^ ProvidedIntentId.GetHashCode());

        public override string ToString() => $"{EventTypeId}:{ProvidedIntentId}";

        public static bool operator ==(
            CoCoEventToIntentDeclarationKey left,
            CoCoEventToIntentDeclarationKey right) => left.Equals(right);

        public static bool operator !=(
            CoCoEventToIntentDeclarationKey left,
            CoCoEventToIntentDeclarationKey right) => !left.Equals(right);
    }

    public sealed class CoCoCompiledEventToIntentDeclaration
    {
        internal CoCoCompiledEventToIntentDeclaration(
            ICoCoGraphEventToIntentDeclarationRegistration registration)
        {
            EventDomainId = registration.EventDomainId;
            EventTypeId = registration.EventTypeId;
            EventPayloadType = registration.EventPayloadType;
            ProvidedIntentId = registration.ProvidedIntentId;
            ProvidedIntentType = registration.ProvidedIntentType;
        }

        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeId { get; }
        public Type EventPayloadType { get; }
        public CoCoIntentId ProvidedIntentId { get; }
        public Type ProvidedIntentType { get; }
    }

    internal interface ICoCoGraphEventToIntentDeclarationRegistration
    {
        CoCoEventToIntentDeclarationKey Key { get; }
        CoCoEventDomainId EventDomainId { get; }
        CoCoEventTypeId EventTypeId { get; }
        Type EventPayloadType { get; }
        CoCoIntentId ProvidedIntentId { get; }
        Type ProvidedIntentType { get; }
    }

    internal sealed class CoCoGraphEventToIntentDeclarationRegistration :
        ICoCoGraphEventToIntentDeclarationRegistration
    {
        internal CoCoGraphEventToIntentDeclarationRegistration(
            CoCoEventToIntentDeclarationKey key,
            CoCoEventDomainId eventDomainId,
            Type eventPayloadType,
            Type providedIntentType)
        {
            Key = key;
            EventDomainId = eventDomainId;
            EventPayloadType = eventPayloadType;
            ProvidedIntentType = providedIntentType;
        }

        public CoCoEventToIntentDeclarationKey Key { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeId => Key.EventTypeId;
        public Type EventPayloadType { get; }
        public CoCoIntentId ProvidedIntentId => Key.ProvidedIntentId;
        public Type ProvidedIntentType { get; }
    }
}
