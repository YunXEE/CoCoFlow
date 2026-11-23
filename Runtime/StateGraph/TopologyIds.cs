using System;
using System.Globalization;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct GraphId : IEquatable<GraphId>
    {
        private GraphId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out GraphId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new GraphId(high, low);
            return true;
        }

        public static bool TryParse(string value, out GraphId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(GraphId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is GraphId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(GraphId left, GraphId right) => left.Equals(right);
        public static bool operator !=(GraphId left, GraphId right) => !left.Equals(right);
    }

    public readonly struct LayerId : IEquatable<LayerId>
    {
        private LayerId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out LayerId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new LayerId(high, low);
            return true;
        }

        public static bool TryParse(string value, out LayerId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(LayerId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is LayerId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(LayerId left, LayerId right) => left.Equals(right);
        public static bool operator !=(LayerId left, LayerId right) => !left.Equals(right);
    }

    public readonly struct StateId : IEquatable<StateId>
    {
        private StateId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out StateId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateId(high, low);
            return true;
        }

        public static bool TryParse(string value, out StateId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(StateId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is StateId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(StateId left, StateId right) => left.Equals(right);
        public static bool operator !=(StateId left, StateId right) => !left.Equals(right);
    }

    public readonly struct TransitionId : IEquatable<TransitionId>
    {
        private TransitionId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out TransitionId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new TransitionId(high, low);
            return true;
        }

        public static bool TryParse(string value, out TransitionId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(TransitionId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is TransitionId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(TransitionId left, TransitionId right) => left.Equals(right);
        public static bool operator !=(TransitionId left, TransitionId right) => !left.Equals(right);
    }

    internal static class Id128Parser
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
