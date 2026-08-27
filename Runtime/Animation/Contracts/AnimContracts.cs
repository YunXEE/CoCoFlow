using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Animation.Contracts
{
    /// <summary>
    /// Fixed, allocation-free capacities of the Animation OperationFrame surface.
    /// </summary>
    public static class AnimContractLimits
    {
        public const int ParameterLaneCount = 16;
        public const int TriggerLaneCount = 8;
        public const int FeedbackCapacity = 16;
    }

    /// <summary>
    /// Stable package identifiers shared by StateGraph authoring, project bindings and Operators.
    /// </summary>
    public static class AnimContractIds
    {
        private const ulong CoCoFlowHigh = 0x434F434F464C4F57UL;

        public const ulong ParameterSectionSemanticFingerprint = 0x414E494D00001001UL;
        public const ulong TriggerSectionSemanticFingerprint = 0x414E494D00001002UL;
        public const ulong FeedbackReducerSemanticFingerprint = 0x414E494D00002001UL;
        public const ulong FeedbackAdapterSemanticFingerprint = 0x414E494D00002002UL;

        public static CoCoOperationSectionId ParameterSectionId { get; } =
            CreateOperationSectionId(0x414E494D00000001UL);

        public static CoCoOperationSectionId TriggerSectionId { get; } =
            CreateOperationSectionId(0x414E494D00000002UL);

        public static CoCoOperatorId OperatorId { get; } =
            CreateOperatorId(0x414E494D00000102UL);

        public static CoCoOperatorId AutoOperatorId => OperatorId;

        public static CoCoIntentId FeedbackIntentId { get; } =
            CreateIntentId(0x414E494D00000201UL);

        public static CoCoEventDomainId FeedbackEventDomainId { get; } =
            CreateEventDomainId(0x434F434F414E494DUL);

        public static CoCoEventTypeId FeedbackEventTypeId { get; } =
            CreateEventTypeId(0x414E494D00000301UL);

        public static CoCoStateBlockId SnapshotBlockId { get; } =
            CreateStateBlockId(0x414E494D00000401UL);

        public static CoCoStateSlotId SnapshotSlotId { get; } =
            CreateStateSlotId(0x414E494D00000402UL);

        private static CoCoStateBlockId CreateStateBlockId(ulong low)
        {
            if (!CoCoStateBlockId.TryCreate(CoCoFlowHigh, low, out CoCoStateBlockId id))
            {
                throw new InvalidOperationException("The fixed Animation Context Block id is invalid.");
            }

            return id;
        }

        private static CoCoStateSlotId CreateStateSlotId(ulong low)
        {
            if (!CoCoStateSlotId.TryCreate(CoCoFlowHigh, low, out CoCoStateSlotId id))
            {
                throw new InvalidOperationException("The fixed Animation Context Slot id is invalid.");
            }

            return id;
        }

        private static CoCoOperationSectionId CreateOperationSectionId(ulong low)
        {
            if (!CoCoOperationSectionId.TryCreate(CoCoFlowHigh, low, out CoCoOperationSectionId id))
            {
                throw new InvalidOperationException("The fixed Animation Operation Section id is invalid.");
            }

            return id;
        }

        private static CoCoOperatorId CreateOperatorId(ulong low)
        {
            if (!CoCoOperatorId.TryCreate(CoCoFlowHigh, low, out CoCoOperatorId id))
            {
                throw new InvalidOperationException("The fixed Animation Operator id is invalid.");
            }

            return id;
        }

        private static CoCoIntentId CreateIntentId(ulong low)
        {
            if (!CoCoIntentId.TryCreate(CoCoFlowHigh, low, out CoCoIntentId id))
            {
                throw new InvalidOperationException("The fixed Animation Intent id is invalid.");
            }

            return id;
        }

        private static CoCoEventDomainId CreateEventDomainId(ulong value)
        {
            if (!CoCoEventDomainId.TryCreate(value, out CoCoEventDomainId id))
            {
                throw new InvalidOperationException("The fixed Animation Event Domain id is invalid.");
            }

            return id;
        }

        private static CoCoEventTypeId CreateEventTypeId(ulong low)
        {
            if (!CoCoEventTypeId.TryCreate(CoCoFlowHigh, low, out CoCoEventTypeId id))
            {
                throw new InvalidOperationException("The fixed Animation Event Type id is invalid.");
            }

            return id;
        }
    }

    public readonly struct AnimBindingId : IEquatable<AnimBindingId>
    {
        private AnimBindingId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public static bool TryCreate(ulong value, out AnimBindingId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new AnimBindingId(value);
            return true;
        }

        public bool Equals(AnimBindingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AnimBindingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => IsValid ? Value.ToString() : "Invalid";

        public static bool operator ==(AnimBindingId left, AnimBindingId right) => left.Equals(right);
        public static bool operator !=(AnimBindingId left, AnimBindingId right) => !left.Equals(right);
    }

    public enum AnimParameterValueKind : byte
    {
        None = 0,
        Float = 1,
        Integer = 2,
        Boolean = 3
    }

    public readonly struct AnimParameterCommand
    {
        private AnimParameterCommand(
            AnimBindingId bindingId,
            AnimParameterValueKind kind,
            float floatValue,
            int integerValue,
            bool booleanValue)
        {
            BindingId = bindingId;
            Kind = kind;
            FloatValue = floatValue;
            IntegerValue = integerValue;
            BooleanValue = booleanValue;
        }

        public AnimBindingId BindingId { get; }
        public AnimParameterValueKind Kind { get; }
        public float FloatValue { get; }
        public int IntegerValue { get; }
        public bool BooleanValue { get; }
        public bool IsValid => BindingId.IsValid &&
                               Kind >= AnimParameterValueKind.Float &&
                               Kind <= AnimParameterValueKind.Boolean &&
                               (Kind != AnimParameterValueKind.Float || AnimMath.IsFinite(FloatValue));

        public static bool TryCreateFloat(
            AnimBindingId bindingId,
            float value,
            out AnimParameterCommand command)
        {
            if (!bindingId.IsValid || !AnimMath.IsFinite(value))
            {
                command = default;
                return false;
            }

            command = new AnimParameterCommand(
                bindingId,
                AnimParameterValueKind.Float,
                value,
                default,
                default);
            return true;
        }

        public static bool TryCreateInteger(
            AnimBindingId bindingId,
            int value,
            out AnimParameterCommand command)
        {
            if (!bindingId.IsValid)
            {
                command = default;
                return false;
            }

            command = new AnimParameterCommand(
                bindingId,
                AnimParameterValueKind.Integer,
                default,
                value,
                default);
            return true;
        }

        public static bool TryCreateBoolean(
            AnimBindingId bindingId,
            bool value,
            out AnimParameterCommand command)
        {
            if (!bindingId.IsValid)
            {
                command = default;
                return false;
            }

            command = new AnimParameterCommand(
                bindingId,
                AnimParameterValueKind.Boolean,
                default,
                default,
                value);
            return true;
        }
    }

    public enum AnimTriggerCommandKind : byte
    {
        None = 0,
        Set = 1,
        Reset = 2
    }

    public readonly struct AnimTriggerCommand
    {
        private AnimTriggerCommand(
            AnimTriggerCommandKind kind,
            AnimBindingId bindingId,
            CoCoActivationId sourceActivationId)
        {
            Kind = kind;
            BindingId = bindingId;
            SourceActivationId = sourceActivationId;
        }

        public AnimTriggerCommandKind Kind { get; }
        public AnimBindingId BindingId { get; }
        public CoCoActivationId SourceActivationId { get; }
        public bool IsValid => Kind >= AnimTriggerCommandKind.Set &&
                               Kind <= AnimTriggerCommandKind.Reset &&
                               BindingId.IsValid &&
                               SourceActivationId.IsValid;

        public static bool TryCreate(
            AnimTriggerCommandKind kind,
            AnimBindingId bindingId,
            CoCoActivationId sourceActivationId,
            out AnimTriggerCommand command)
        {
            if (kind < AnimTriggerCommandKind.Set ||
                kind > AnimTriggerCommandKind.Reset ||
                !bindingId.IsValid ||
                !sourceActivationId.IsValid)
            {
                command = default;
                return false;
            }

            command = new AnimTriggerCommand(kind, bindingId, sourceActivationId);
            return true;
        }
    }

    internal static class AnimMath
    {
        public static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool IsFiniteNonNegative(float value) =>
            IsFinite(value) && value >= 0f;

        public static bool IsFinitePositive(float value) =>
            IsFinite(value) && value > 0f;
    }

    /// <summary>
    /// Engine-fact snapshot of one Animator: per-layer state hash,
    /// normalized time and layer weight plus the engine's current
    /// parameter values. Fixed-size and unmanaged so it can live in one
    /// Context slot. Layout is bound to the controller the snapshot was
    /// taken from — projecting onto a mismatched controller fails loudly.
    /// </summary>
    public struct AnimSnapshotState
    {
        public const int MaxLayers = 4;
        public const int MaxParameterLanes = 16;

        public int Layer0StateHash;
        public float Layer0Time;
        public float Layer0Weight;
        public int Layer1StateHash;
        public float Layer1Time;
        public float Layer1Weight;
        public int Layer2StateHash;
        public float Layer2Time;
        public float Layer2Weight;
        public int Layer3StateHash;
        public float Layer3Time;
        public float Layer3Weight;
        public float Lane0;
        public float Lane1;
        public float Lane2;
        public float Lane3;
        public float Lane4;
        public float Lane5;
        public float Lane6;
        public float Lane7;
        public float Lane8;
        public float Lane9;
        public float Lane10;
        public float Lane11;
        public float Lane12;
        public float Lane13;
        public float Lane14;
        public float Lane15;
        public byte LayerCount;
        public byte LaneCount;

        public readonly float LayerTime(int index)
        {
            switch (index)
            {
                case 0: return Layer0Time;
                case 1: return Layer1Time;
                case 2: return Layer2Time;
                case 3: return Layer3Time;
                default: return 0f;
            }
        }

        public readonly int LayerStateHash(int index)
        {
            switch (index)
            {
                case 0: return Layer0StateHash;
                case 1: return Layer1StateHash;
                case 2: return Layer2StateHash;
                case 3: return Layer3StateHash;
                default: return 0;
            }
        }

        public readonly float LayerWeight(int index)
        {
            switch (index)
            {
                case 0: return Layer0Weight;
                case 1: return Layer1Weight;
                case 2: return Layer2Weight;
                case 3: return Layer3Weight;
                default: return 0f;
            }
        }

        public readonly float Lane(int index)
        {
            switch (index)
            {
                case 0: return Lane0;
                case 1: return Lane1;
                case 2: return Lane2;
                case 3: return Lane3;
                case 4: return Lane4;
                case 5: return Lane5;
                case 6: return Lane6;
                case 7: return Lane7;
                case 8: return Lane8;
                case 9: return Lane9;
                case 10: return Lane10;
                case 11: return Lane11;
                case 12: return Lane12;
                case 13: return Lane13;
                case 14: return Lane14;
                case 15: return Lane15;
                default: return 0f;
            }
        }

        public void SetLayer(int index, int hash, float time, float weight)
        {
            switch (index)
            {
                case 0: Layer0StateHash = hash; Layer0Time = time; Layer0Weight = weight; break;
                case 1: Layer1StateHash = hash; Layer1Time = time; Layer1Weight = weight; break;
                case 2: Layer2StateHash = hash; Layer2Time = time; Layer2Weight = weight; break;
                case 3: Layer3StateHash = hash; Layer3Time = time; Layer3Weight = weight; break;
            }
        }

        public void SetLane(int index, float value)
        {
            switch (index)
            {
                case 0: Lane0 = value; break;
                case 1: Lane1 = value; break;
                case 2: Lane2 = value; break;
                case 3: Lane3 = value; break;
                case 4: Lane4 = value; break;
                case 5: Lane5 = value; break;
                case 6: Lane6 = value; break;
                case 7: Lane7 = value; break;
                case 8: Lane8 = value; break;
                case 9: Lane9 = value; break;
                case 10: Lane10 = value; break;
                case 11: Lane11 = value; break;
                case 12: Lane12 = value; break;
                case 13: Lane13 = value; break;
                case 14: Lane14 = value; break;
                case 15: Lane15 = value; break;
            }
        }
    }
}
