using System;
using System.Globalization;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct GraphInstanceId : IEquatable<GraphInstanceId>
    {
        private GraphInstanceId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out GraphInstanceId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new GraphInstanceId(value);
            return true;
        }

        public bool Equals(GraphInstanceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GraphInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(GraphInstanceId left, GraphInstanceId right) => left.Equals(right);
        public static bool operator !=(GraphInstanceId left, GraphInstanceId right) => !left.Equals(right);
    }

    public readonly struct ActivationId : IEquatable<ActivationId>
    {
        private ActivationId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out ActivationId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new ActivationId(value);
            return true;
        }

        public bool Equals(ActivationId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ActivationId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ActivationId left, ActivationId right) => left.Equals(right);
        public static bool operator !=(ActivationId left, ActivationId right) => !left.Equals(right);
    }

    public readonly struct FrameLayoutId : IEquatable<FrameLayoutId>
    {
        private FrameLayoutId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out FrameLayoutId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new FrameLayoutId(high, low);
            return true;
        }

        public static bool TryParse(string value, out FrameLayoutId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(FrameLayoutId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is FrameLayoutId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(FrameLayoutId left, FrameLayoutId right) => left.Equals(right);
        public static bool operator !=(FrameLayoutId left, FrameLayoutId right) => !left.Equals(right);
    }

    public readonly struct OperationSectionId : IEquatable<OperationSectionId>
    {
        private OperationSectionId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out OperationSectionId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new OperationSectionId(high, low);
            return true;
        }

        public static bool TryParse(string value, out OperationSectionId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(OperationSectionId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is OperationSectionId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(OperationSectionId left, OperationSectionId right) => left.Equals(right);
        public static bool operator !=(OperationSectionId left, OperationSectionId right) => !left.Equals(right);
    }

    public readonly struct OperatorId : IEquatable<OperatorId>
    {
        private OperatorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out OperatorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new OperatorId(high, low);
            return true;
        }

        public static bool TryParse(string value, out OperatorId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(OperatorId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is OperatorId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(OperatorId left, OperatorId right) => left.Equals(right);
        public static bool operator !=(OperatorId left, OperatorId right) => !left.Equals(right);
    }

    public readonly struct OperatorClaimId : IEquatable<OperatorClaimId>
    {
        private OperatorClaimId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out OperatorClaimId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new OperatorClaimId(high, low);
            return true;
        }

        public static bool TryParse(string value, out OperatorClaimId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(OperatorClaimId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is OperatorClaimId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(OperatorClaimId left, OperatorClaimId right) => left.Equals(right);
        public static bool operator !=(OperatorClaimId left, OperatorClaimId right) => !left.Equals(right);
    }

    public readonly struct IntentId : IEquatable<IntentId>
    {
        private IntentId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out IntentId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new IntentId(high, low);
            return true;
        }

        public static bool TryParse(string value, out IntentId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(IntentId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is IntentId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(IntentId left, IntentId right) => left.Equals(right);
        public static bool operator !=(IntentId left, IntentId right) => !left.Equals(right);
    }

    public readonly struct StateBlockId : IEquatable<StateBlockId>
    {
        private StateBlockId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out StateBlockId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateBlockId(high, low);
            return true;
        }

        public static bool TryParse(string value, out StateBlockId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(StateBlockId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is StateBlockId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(StateBlockId left, StateBlockId right) => left.Equals(right);
        public static bool operator !=(StateBlockId left, StateBlockId right) => !left.Equals(right);
    }

    public readonly struct StateSlotId : IEquatable<StateSlotId>
    {
        private StateSlotId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out StateSlotId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateSlotId(high, low);
            return true;
        }

        public static bool TryParse(string value, out StateSlotId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(StateSlotId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is StateSlotId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(StateSlotId left, StateSlotId right) => left.Equals(right);
        public static bool operator !=(StateSlotId left, StateSlotId right) => !left.Equals(right);
    }

    public readonly struct EventTypeId : IEquatable<EventTypeId>
    {
        private EventTypeId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out EventTypeId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new EventTypeId(high, low);
            return true;
        }

        public static bool TryParse(string value, out EventTypeId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(EventTypeId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is EventTypeId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(EventTypeId left, EventTypeId right) => left.Equals(right);
        public static bool operator !=(EventTypeId left, EventTypeId right) => !left.Equals(right);
    }

    public readonly struct StableEntityId : IEquatable<StableEntityId>
    {
        private StableEntityId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out StableEntityId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StableEntityId(high, low);
            return true;
        }

        public static bool TryParse(string value, out StableEntityId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(StableEntityId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is StableEntityId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(StableEntityId left, StableEntityId right) => left.Equals(right);
        public static bool operator !=(StableEntityId left, StableEntityId right) => !left.Equals(right);
    }

    public readonly struct TemporalEntityId : IEquatable<TemporalEntityId>
    {
        private TemporalEntityId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out TemporalEntityId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new TemporalEntityId(high, low);
            return true;
        }

        public static bool TryParse(string value, out TemporalEntityId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(TemporalEntityId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is TemporalEntityId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(TemporalEntityId left, TemporalEntityId right) => left.Equals(right);
        public static bool operator !=(TemporalEntityId left, TemporalEntityId right) => !left.Equals(right);
    }

    public readonly struct CorrelationId : IEquatable<CorrelationId>
    {
        private CorrelationId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CorrelationId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CorrelationId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CorrelationId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CorrelationId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CorrelationId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CorrelationId left, CorrelationId right) => left.Equals(right);
        public static bool operator !=(CorrelationId left, CorrelationId right) => !left.Equals(right);
    }

    public readonly struct CodecId : IEquatable<CodecId>
    {
        private CodecId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        public static bool TryCreate(ulong high, ulong low, out CodecId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CodecId(high, low);
            return true;
        }

        public static bool TryParse(string value, out CodecId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(CodecId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is CodecId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CodecId left, CodecId right) => left.Equals(right);
        public static bool operator !=(CodecId left, CodecId right) => !left.Equals(right);
    }

    public readonly struct EventDomainId : IEquatable<EventDomainId>
    {
        private EventDomainId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out EventDomainId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new EventDomainId(value);
            return true;
        }

        public bool Equals(EventDomainId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EventDomainId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EventDomainId left, EventDomainId right) => left.Equals(right);
        public static bool operator !=(EventDomainId left, EventDomainId right) => !left.Equals(right);
    }

    public readonly struct OperationSequence : IEquatable<OperationSequence>
    {
        private OperationSequence(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out OperationSequence sequence)
        {
            if (value == 0UL)
            {
                sequence = default;
                return false;
            }

            sequence = new OperationSequence(value);
            return true;
        }

        public bool Equals(OperationSequence other) => Value == other.Value;
        public override bool Equals(object obj) => obj is OperationSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(OperationSequence left, OperationSequence right) => left.Equals(right);
        public static bool operator !=(OperationSequence left, OperationSequence right) => !left.Equals(right);
    }

    public readonly struct EventSequence : IEquatable<EventSequence>
    {
        private EventSequence(ulong value)
        {
            Value = value;
        }

        public static EventSequence Zero => default;

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out EventSequence sequence)
        {
            if (value == 0UL)
            {
                sequence = default;
                return false;
            }

            sequence = new EventSequence(value);
            return true;
        }

        public bool Equals(EventSequence other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EventSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EventSequence left, EventSequence right) => left.Equals(right);
        public static bool operator !=(EventSequence left, EventSequence right) => !left.Equals(right);
    }
}
