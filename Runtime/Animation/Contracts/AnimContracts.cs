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
}
