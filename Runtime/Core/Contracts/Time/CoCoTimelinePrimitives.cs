using System;
using System.Globalization;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoTimelineId : IEquatable<CoCoTimelineId>
    {
        private CoCoTimelineId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoTimelineId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoTimelineId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoTimelineId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoTimelineId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoTimelineId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoTimelineId left, CoCoTimelineId right) => left.Equals(right);
        public static bool operator !=(CoCoTimelineId left, CoCoTimelineId right) => !left.Equals(right);
    }

    public readonly struct CoCoClockDomainId : IEquatable<CoCoClockDomainId>
    {
        private CoCoClockDomainId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out CoCoClockDomainId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoClockDomainId(value);
            return true;
        }

        public bool Equals(CoCoClockDomainId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoClockDomainId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoClockDomainId left, CoCoClockDomainId right) => left.Equals(right);
        public static bool operator !=(CoCoClockDomainId left, CoCoClockDomainId right) => !left.Equals(right);
    }

    public readonly struct CoCoExecutionSequence : IEquatable<CoCoExecutionSequence>
    {
        public CoCoExecutionSequence(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(CoCoExecutionSequence other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoExecutionSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoExecutionSequence left, CoCoExecutionSequence right) => left.Equals(right);
        public static bool operator !=(CoCoExecutionSequence left, CoCoExecutionSequence right) => !left.Equals(right);
    }

    public readonly struct CoCoTimelineEpoch : IEquatable<CoCoTimelineEpoch>
    {
        public CoCoTimelineEpoch(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(CoCoTimelineEpoch other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoTimelineEpoch other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoTimelineEpoch left, CoCoTimelineEpoch right) => left.Equals(right);
        public static bool operator !=(CoCoTimelineEpoch left, CoCoTimelineEpoch right) => !left.Equals(right);
    }
}
