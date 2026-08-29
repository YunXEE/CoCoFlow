using System;
using System.Globalization;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoGraphId : IEquatable<CoCoGraphId>
    {
        private CoCoGraphId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoGraphId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoGraphId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoGraphId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoGraphId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoGraphId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoGraphId left, CoCoGraphId right) => left.Equals(right);
        public static bool operator !=(CoCoGraphId left, CoCoGraphId right) => !left.Equals(right);
    }

    public readonly struct CoCoLayerId : IEquatable<CoCoLayerId>
    {
        private CoCoLayerId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoLayerId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoLayerId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoLayerId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoLayerId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoLayerId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoLayerId left, CoCoLayerId right) => left.Equals(right);
        public static bool operator !=(CoCoLayerId left, CoCoLayerId right) => !left.Equals(right);
    }

    public readonly struct CoCoStateId : IEquatable<CoCoStateId>
    {
        private CoCoStateId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoStateId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoStateId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoStateId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoStateId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoStateId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoStateId left, CoCoStateId right) => left.Equals(right);
        public static bool operator !=(CoCoStateId left, CoCoStateId right) => !left.Equals(right);
    }

    public readonly struct CoCoTransitionId : IEquatable<CoCoTransitionId>
    {
        private CoCoTransitionId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoTransitionId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoTransitionId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoTransitionId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoTransitionId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoTransitionId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoTransitionId left, CoCoTransitionId right) => left.Equals(right);
        public static bool operator !=(CoCoTransitionId left, CoCoTransitionId right) => !left.Equals(right);
    }

    internal static class CoCoId128Parser
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
