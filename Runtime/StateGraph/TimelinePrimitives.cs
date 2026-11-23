using System;
using System.Globalization;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct TimelineId : IEquatable<TimelineId>
    {
        private TimelineId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out TimelineId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new TimelineId(high, low);
            return true;
        }

        public static bool TryParse(string value, out TimelineId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(TimelineId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is TimelineId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(TimelineId left, TimelineId right) => left.Equals(right);
        public static bool operator !=(TimelineId left, TimelineId right) => !left.Equals(right);
    }

    public readonly struct ClockDomainId : IEquatable<ClockDomainId>
    {
        private ClockDomainId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out ClockDomainId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new ClockDomainId(value);
            return true;
        }

        public bool Equals(ClockDomainId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ClockDomainId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ClockDomainId left, ClockDomainId right) => left.Equals(right);
        public static bool operator !=(ClockDomainId left, ClockDomainId right) => !left.Equals(right);
    }

    public readonly struct ExecutionSequence : IEquatable<ExecutionSequence>
    {
        public ExecutionSequence(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(ExecutionSequence other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ExecutionSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ExecutionSequence left, ExecutionSequence right) => left.Equals(right);
        public static bool operator !=(ExecutionSequence left, ExecutionSequence right) => !left.Equals(right);
    }

    public readonly struct TimelineEpoch : IEquatable<TimelineEpoch>
    {
        public TimelineEpoch(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(TimelineEpoch other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TimelineEpoch other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(TimelineEpoch left, TimelineEpoch right) => left.Equals(right);
        public static bool operator !=(TimelineEpoch left, TimelineEpoch right) => !left.Equals(right);
    }
}
