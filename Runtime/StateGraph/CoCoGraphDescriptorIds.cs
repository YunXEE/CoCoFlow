using System;
using System.Globalization;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoStateDescriptorId : IEquatable<CoCoStateDescriptorId>
    {
        private CoCoStateDescriptorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoStateDescriptorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoStateDescriptorId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoStateDescriptorId id)
        {
            if (!CoCoGraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoStateDescriptorId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoStateDescriptorId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoStateDescriptorId left, CoCoStateDescriptorId right) =>
            left.Equals(right);

        public static bool operator !=(CoCoStateDescriptorId left, CoCoStateDescriptorId right) =>
            !left.Equals(right);
    }

    public readonly struct CoCoConditionDescriptorId : IEquatable<CoCoConditionDescriptorId>
    {
        private CoCoConditionDescriptorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoConditionDescriptorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoConditionDescriptorId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoConditionDescriptorId id)
        {
            if (!CoCoGraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoConditionDescriptorId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoConditionDescriptorId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoConditionDescriptorId left, CoCoConditionDescriptorId right) =>
            left.Equals(right);

        public static bool operator !=(CoCoConditionDescriptorId left, CoCoConditionDescriptorId right) =>
            !left.Equals(right);
    }

    internal static class CoCoGraphDescriptorIdParser
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
