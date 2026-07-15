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

    public readonly struct CoCoFrameLayoutId : IEquatable<CoCoFrameLayoutId>
    {
        private CoCoFrameLayoutId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoFrameLayoutId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoFrameLayoutId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoFrameLayoutId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoFrameLayoutId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoFrameLayoutId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoFrameLayoutId left, CoCoFrameLayoutId right) => left.Equals(right);
        public static bool operator !=(CoCoFrameLayoutId left, CoCoFrameLayoutId right) => !left.Equals(right);
    }

    public readonly struct CoCoOperationSectionId : IEquatable<CoCoOperationSectionId>
    {
        private CoCoOperationSectionId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoOperationSectionId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoOperationSectionId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoOperationSectionId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoOperationSectionId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoOperationSectionId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoOperationSectionId left, CoCoOperationSectionId right) => left.Equals(right);
        public static bool operator !=(CoCoOperationSectionId left, CoCoOperationSectionId right) => !left.Equals(right);
    }

    public readonly struct CoCoIntentId : IEquatable<CoCoIntentId>
    {
        private CoCoIntentId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoIntentId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoIntentId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoIntentId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoIntentId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoIntentId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoIntentId left, CoCoIntentId right) => left.Equals(right);
        public static bool operator !=(CoCoIntentId left, CoCoIntentId right) => !left.Equals(right);
    }

    public readonly struct CoCoStateBlockId : IEquatable<CoCoStateBlockId>
    {
        private CoCoStateBlockId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoStateBlockId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoStateBlockId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoStateBlockId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoStateBlockId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoStateBlockId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoStateBlockId left, CoCoStateBlockId right) => left.Equals(right);
        public static bool operator !=(CoCoStateBlockId left, CoCoStateBlockId right) => !left.Equals(right);
    }

    public readonly struct CoCoStateSlotId : IEquatable<CoCoStateSlotId>
    {
        private CoCoStateSlotId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoStateSlotId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoStateSlotId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoStateSlotId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoStateSlotId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoStateSlotId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoStateSlotId left, CoCoStateSlotId right) => left.Equals(right);
        public static bool operator !=(CoCoStateSlotId left, CoCoStateSlotId right) => !left.Equals(right);
    }

    public readonly struct CoCoEventTypeId : IEquatable<CoCoEventTypeId>
    {
        private CoCoEventTypeId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoEventTypeId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoEventTypeId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoEventTypeId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoEventTypeId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoEventTypeId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoEventTypeId left, CoCoEventTypeId right) => left.Equals(right);
        public static bool operator !=(CoCoEventTypeId left, CoCoEventTypeId right) => !left.Equals(right);
    }

    public readonly struct CoCoStableEntityId : IEquatable<CoCoStableEntityId>
    {
        private CoCoStableEntityId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoStableEntityId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoStableEntityId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoStableEntityId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoStableEntityId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoStableEntityId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoStableEntityId left, CoCoStableEntityId right) => left.Equals(right);
        public static bool operator !=(CoCoStableEntityId left, CoCoStableEntityId right) => !left.Equals(right);
    }

    public readonly struct CoCoCorrelationId : IEquatable<CoCoCorrelationId>
    {
        private CoCoCorrelationId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoCorrelationId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoCorrelationId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoCorrelationId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoCorrelationId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoCorrelationId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoCorrelationId left, CoCoCorrelationId right) => left.Equals(right);
        public static bool operator !=(CoCoCorrelationId left, CoCoCorrelationId right) => !left.Equals(right);
    }

    public readonly struct CoCoCodecId : IEquatable<CoCoCodecId>
    {
        private CoCoCodecId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CoCoCodecId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoCodecId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CoCoCodecId id)
        {
            if (!CoCoId128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CoCoCodecId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CoCoCodecId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoCodecId left, CoCoCodecId right) => left.Equals(right);
        public static bool operator !=(CoCoCodecId left, CoCoCodecId right) => !left.Equals(right);
    }

    public readonly struct CoCoEventDomainId : IEquatable<CoCoEventDomainId>
    {
        private CoCoEventDomainId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out CoCoEventDomainId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new CoCoEventDomainId(value);
            return true;
        }

        public bool Equals(CoCoEventDomainId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoEventDomainId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoEventDomainId left, CoCoEventDomainId right) => left.Equals(right);
        public static bool operator !=(CoCoEventDomainId left, CoCoEventDomainId right) => !left.Equals(right);
    }

    public readonly struct CoCoOperationSequence : IEquatable<CoCoOperationSequence>
    {
        private CoCoOperationSequence(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out CoCoOperationSequence sequence)
        {
            if (value == 0UL)
            {
                sequence = default;
                return false;
            }

            sequence = new CoCoOperationSequence(value);
            return true;
        }

        public bool Equals(CoCoOperationSequence other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoOperationSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoOperationSequence left, CoCoOperationSequence right) => left.Equals(right);
        public static bool operator !=(CoCoOperationSequence left, CoCoOperationSequence right) => !left.Equals(right);
    }

    public readonly struct CoCoEventSequence : IEquatable<CoCoEventSequence>
    {
        private CoCoEventSequence(ulong value)
        {
            Value = value;
        }

        public static CoCoEventSequence Zero => default;

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out CoCoEventSequence sequence)
        {
            if (value == 0UL)
            {
                sequence = default;
                return false;
            }

            sequence = new CoCoEventSequence(value);
            return true;
        }

        public bool Equals(CoCoEventSequence other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoEventSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoEventSequence left, CoCoEventSequence right) => left.Equals(right);
        public static bool operator !=(CoCoEventSequence left, CoCoEventSequence right) => !left.Equals(right);
    }
}
