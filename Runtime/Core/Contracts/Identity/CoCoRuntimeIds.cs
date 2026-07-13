using System;
using System.Globalization;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoGraphInstanceId : IEquatable<CoCoGraphInstanceId>
    {
        private CoCoGraphInstanceId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out CoCoGraphInstanceId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoGraphInstanceId(value);
            return true;
        }

        public bool Equals(CoCoGraphInstanceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoGraphInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoGraphInstanceId left, CoCoGraphInstanceId right) => left.Equals(right);
        public static bool operator !=(CoCoGraphInstanceId left, CoCoGraphInstanceId right) => !left.Equals(right);
    }

    public readonly struct CoCoActivationId : IEquatable<CoCoActivationId>
    {
        private CoCoActivationId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out CoCoActivationId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoActivationId(value);
            return true;
        }

        public bool Equals(CoCoActivationId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoActivationId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoActivationId left, CoCoActivationId right) => left.Equals(right);
        public static bool operator !=(CoCoActivationId left, CoCoActivationId right) => !left.Equals(right);
    }
}
