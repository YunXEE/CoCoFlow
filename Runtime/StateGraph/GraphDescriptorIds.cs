using System;
using System.Globalization;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct StateDescriptorId : IEquatable<StateDescriptorId>
    {
        private StateDescriptorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out StateDescriptorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateDescriptorId(high, low);
            return true;
        }

        public static bool TryParse(string value, out StateDescriptorId id)
        {
            if (!GraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(StateDescriptorId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is StateDescriptorId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(StateDescriptorId left, StateDescriptorId right) =>
            left.Equals(right);

        public static bool operator !=(StateDescriptorId left, StateDescriptorId right) =>
            !left.Equals(right);
    }

    public readonly struct ConditionDescriptorId : IEquatable<ConditionDescriptorId>
    {
        private ConditionDescriptorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out ConditionDescriptorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new ConditionDescriptorId(high, low);
            return true;
        }

        public static bool TryParse(string value, out ConditionDescriptorId id)
        {
            if (!GraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(ConditionDescriptorId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is ConditionDescriptorId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(ConditionDescriptorId left, ConditionDescriptorId right) =>
            left.Equals(right);

        public static bool operator !=(ConditionDescriptorId left, ConditionDescriptorId right) =>
            !left.Equals(right);
    }

    internal static class GraphDescriptorIdParser
    {
        public static bool TryParse(string value, out ulong high, out ulong low)
        {
            high = 0UL;
            low = 0UL;
            if (value == null || value.Length != 32)
            {
                return false;
            }

            return ulong.TryParse(
                       value.Substring(0, 16),
                       NumberStyles.AllowHexSpecifier,
                       CultureInfo.InvariantCulture,
                       out high) &&
                   ulong.TryParse(
                       value.Substring(16, 16),
                       NumberStyles.AllowHexSpecifier,
                       CultureInfo.InvariantCulture,
                       out low);
        }
    }
}
