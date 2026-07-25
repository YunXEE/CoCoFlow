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
        public const int PlaybackLayerCount = 4;
        public const int ModulationLaneCount = 8;
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
        public const ulong PlaybackSectionSemanticFingerprint = 0x414E494D00001003UL;
        public const ulong ModulationSectionSemanticFingerprint = 0x414E494D00001004UL;
        public const ulong FeedbackReducerSemanticFingerprint = 0x414E494D00002001UL;
        public const ulong FeedbackAdapterSemanticFingerprint = 0x414E494D00002002UL;

        public static CoCoOperationSectionId ParameterSectionId { get; } =
            CreateOperationSectionId(0x414E494D00000001UL);

        public static CoCoOperationSectionId TriggerSectionId { get; } =
            CreateOperationSectionId(0x414E494D00000002UL);

        public static CoCoOperationSectionId PlaybackSectionId { get; } =
            CreateOperationSectionId(0x414E494D00000003UL);

        public static CoCoOperationSectionId ModulationSectionId { get; } =
            CreateOperationSectionId(0x414E494D00000004UL);

        public static CoCoOperatorId OperatorId { get; } =
            CreateOperatorId(0x414E494D00000102UL);

        public static CoCoOperatorId AutoOperatorId => OperatorId;

        public static CoCoIntentId FeedbackIntentId { get; } =
            CreateIntentId(0x414E494D00000201UL);

        public static CoCoEventDomainId FeedbackEventDomainId { get; } =
            CreateEventDomainId(0x434F434F414E494DUL);

        public static CoCoEventTypeId FeedbackEventTypeId { get; } =
            CreateEventTypeId(0x414E494D00000301UL);

        public static CoCoStateBlockId PlaybackContextBlockId { get; } =
            CreateStateBlockId(0x414E494D00000401UL);

        public static CoCoStateSlotId PlaybackContextSlotId { get; } =
            CreateStateSlotId(0x414E494D00000402UL);

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

        private static CoCoStateBlockId CreateStateBlockId(ulong low)
        {
            if (!CoCoStateBlockId.TryCreate(CoCoFlowHigh, low, out CoCoStateBlockId id))
            {
                throw new InvalidOperationException("The fixed Animation State Block id is invalid.");
            }

            return id;
        }

        private static CoCoStateSlotId CreateStateSlotId(ulong low)
        {
            if (!CoCoStateSlotId.TryCreate(CoCoFlowHigh, low, out CoCoStateSlotId id))
            {
                throw new InvalidOperationException("The fixed Animation State Slot id is invalid.");
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

    public enum AnimPlaybackCommandKind : byte
    {
        None = 0,
        Play = 1,
        CrossFade = 2,
        Stop = 3,
        Step = 4
    }

    public enum AnimPlaybackLayerSlot : byte
    {
        None = 0,
        Layer00 = 1,
        Layer01 = 2,
        Layer02 = 3,
        Layer03 = 4
    }

    public enum AnimPlaybackStatus : byte
    {
        None = 0,
        Playing = 1,
        CrossFading = 2,
        Held = 3,
        Completed = 4,
        Interrupted = 5
    }

    public readonly struct AnimPlaybackCommand
    {
        private AnimPlaybackCommand(
            AnimPlaybackCommandKind kind,
            AnimBindingId stateBindingId,
            CoCoActivationId sourceActivationId,
            float startNormalizedTime,
            float transitionDurationSeconds,
            float stepDeltaSeconds)
        {
            Kind = kind;
            StateBindingId = stateBindingId;
            SourceActivationId = sourceActivationId;
            StartNormalizedTime = startNormalizedTime;
            TransitionDurationSeconds = transitionDurationSeconds;
            StepDeltaSeconds = stepDeltaSeconds;
        }

        public AnimPlaybackCommandKind Kind { get; }
        public AnimBindingId StateBindingId { get; }
        public CoCoActivationId SourceActivationId { get; }
        public float StartNormalizedTime { get; }
        public float TransitionDurationSeconds { get; }
        public float StepDeltaSeconds { get; }
        public bool IsLayerCommand => Kind == AnimPlaybackCommandKind.Play ||
                                      Kind == AnimPlaybackCommandKind.CrossFade;
        public bool IsControlCommand => Kind == AnimPlaybackCommandKind.Stop ||
                                        Kind == AnimPlaybackCommandKind.Step;
        public bool IsValid => SourceActivationId.IsValid &&
                               ((Kind == AnimPlaybackCommandKind.Play &&
                                 StateBindingId.IsValid &&
                                 AnimMath.IsFiniteNonNegative(StartNormalizedTime)) ||
                                (Kind == AnimPlaybackCommandKind.CrossFade &&
                                 StateBindingId.IsValid &&
                                 AnimMath.IsFiniteNonNegative(StartNormalizedTime) &&
                                 AnimMath.IsFiniteNonNegative(TransitionDurationSeconds)) ||
                                Kind == AnimPlaybackCommandKind.Stop ||
                                (Kind == AnimPlaybackCommandKind.Step &&
                                 AnimMath.IsFinitePositive(StepDeltaSeconds)));

        public static bool TryCreatePlay(
            AnimBindingId stateBindingId,
            CoCoActivationId sourceActivationId,
            float startNormalizedTime,
            out AnimPlaybackCommand command)
        {
            if (!stateBindingId.IsValid ||
                !sourceActivationId.IsValid ||
                !AnimMath.IsFiniteNonNegative(startNormalizedTime))
            {
                command = default;
                return false;
            }

            command = new AnimPlaybackCommand(
                AnimPlaybackCommandKind.Play,
                stateBindingId,
                sourceActivationId,
                startNormalizedTime,
                default,
                default);
            return true;
        }

        public static bool TryCreateCrossFade(
            AnimBindingId stateBindingId,
            CoCoActivationId sourceActivationId,
            float transitionDurationSeconds,
            float startNormalizedTime,
            out AnimPlaybackCommand command)
        {
            if (!stateBindingId.IsValid ||
                !sourceActivationId.IsValid ||
                !AnimMath.IsFiniteNonNegative(transitionDurationSeconds) ||
                !AnimMath.IsFiniteNonNegative(startNormalizedTime))
            {
                command = default;
                return false;
            }

            command = new AnimPlaybackCommand(
                AnimPlaybackCommandKind.CrossFade,
                stateBindingId,
                sourceActivationId,
                startNormalizedTime,
                transitionDurationSeconds,
                default);
            return true;
        }

        public static bool TryCreateStop(
            CoCoActivationId sourceActivationId,
            out AnimPlaybackCommand command)
        {
            if (!sourceActivationId.IsValid)
            {
                command = default;
                return false;
            }

            command = new AnimPlaybackCommand(
                AnimPlaybackCommandKind.Stop,
                default,
                sourceActivationId,
                default,
                default,
                default);
            return true;
        }

        public static bool TryCreateStep(
            CoCoActivationId sourceActivationId,
            float positiveDeltaSeconds,
            out AnimPlaybackCommand command)
        {
            if (!sourceActivationId.IsValid || !AnimMath.IsFinitePositive(positiveDeltaSeconds))
            {
                command = default;
                return false;
            }

            command = new AnimPlaybackCommand(
                AnimPlaybackCommandKind.Step,
                default,
                sourceActivationId,
                default,
                default,
                positiveDeltaSeconds);
            return true;
        }
    }

    public readonly struct AnimPlaybackToken : IEquatable<AnimPlaybackToken>
    {
        private readonly bool _hasTimelineEpoch;

        private AnimPlaybackToken(
            CoCoGraphInstanceId graphInstanceId,
            CoCoActivationId sourceActivationId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoOperationSequence operationSequence,
            AnimPlaybackLayerSlot layer)
        {
            GraphInstanceId = graphInstanceId;
            SourceActivationId = sourceActivationId;
            TimelineEpoch = timelineEpoch;
            OperationSequence = operationSequence;
            Layer = layer;
            _hasTimelineEpoch = true;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoActivationId SourceActivationId { get; }
        public CoCoTimelineEpoch TimelineEpoch { get; }
        public CoCoOperationSequence OperationSequence { get; }
        public AnimPlaybackLayerSlot Layer { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               SourceActivationId.IsValid &&
                               _hasTimelineEpoch &&
                               OperationSequence.IsValid &&
                               Layer >= AnimPlaybackLayerSlot.Layer00 &&
                               Layer <= AnimPlaybackLayerSlot.Layer03;

        public static bool TryCreate(
            CoCoGraphInstanceId graphInstanceId,
            CoCoActivationId sourceActivationId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoOperationSequence operationSequence,
            AnimPlaybackLayerSlot layer,
            out AnimPlaybackToken token)
        {
            if (!graphInstanceId.IsValid ||
                !sourceActivationId.IsValid ||
                !operationSequence.IsValid ||
                layer < AnimPlaybackLayerSlot.Layer00 ||
                layer > AnimPlaybackLayerSlot.Layer03)
            {
                token = default;
                return false;
            }

            token = new AnimPlaybackToken(
                graphInstanceId,
                sourceActivationId,
                timelineEpoch,
                operationSequence,
                layer);
            return true;
        }

        public bool Equals(AnimPlaybackToken other) =>
            GraphInstanceId == other.GraphInstanceId &&
            SourceActivationId == other.SourceActivationId &&
            TimelineEpoch == other.TimelineEpoch &&
            OperationSequence == other.OperationSequence &&
            Layer == other.Layer &&
            _hasTimelineEpoch == other._hasTimelineEpoch;

        public override bool Equals(object obj) => obj is AnimPlaybackToken other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = GraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineEpoch.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Layer;
                hashCode = (hashCode * 397) ^ _hasTimelineEpoch.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(AnimPlaybackToken left, AnimPlaybackToken right) =>
            left.Equals(right);

        public static bool operator !=(AnimPlaybackToken left, AnimPlaybackToken right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// One layer record published through the playback Context outcome.
    /// </summary>
    public readonly struct AnimPlaybackLayer
    {
        public AnimPlaybackLayer(
            AnimPlaybackLayerSlot slot,
            AnimPlaybackToken token,
            AnimBindingId stateBindingId,
            AnimPlaybackStatus status,
            float normalizedTime)
        {
            Slot = slot;
            Token = token;
            StateBindingId = stateBindingId;
            Status = status;
            NormalizedTime = normalizedTime;
        }

        public AnimPlaybackLayerSlot Slot { get; }
        public AnimPlaybackToken Token { get; }
        public AnimBindingId StateBindingId { get; }
        public AnimPlaybackStatus Status { get; }
        public float NormalizedTime { get; }
        public bool IsActive => Status == AnimPlaybackStatus.Playing ||
                                Status == AnimPlaybackStatus.CrossFading ||
                                Status == AnimPlaybackStatus.Held;
        public bool IsValid => Slot >= AnimPlaybackLayerSlot.Layer00 &&
                               Slot <= AnimPlaybackLayerSlot.Layer03 &&
                               Status >= AnimPlaybackStatus.None &&
                               Status <= AnimPlaybackStatus.Interrupted &&
                               AnimMath.IsFiniteNonNegative(NormalizedTime) &&
                               (Status == AnimPlaybackStatus.None ||
                                (Token.IsValid && StateBindingId.IsValid && Token.Layer == Slot));
    }

    /// <summary>
    /// Fixed four-layer playback snapshot written by AnimOperator as one atomic outcome.
    /// </summary>
    public readonly struct AnimPlaybackContext
    {
        public AnimPlaybackContext(
            AnimPlaybackLayer layer00,
            AnimPlaybackLayer layer01,
            AnimPlaybackLayer layer02,
            AnimPlaybackLayer layer03,
            bool isHeld)
        {
            Layer00 = layer00;
            Layer01 = layer01;
            Layer02 = layer02;
            Layer03 = layer03;
            IsHeld = isHeld;
        }

        public AnimPlaybackLayer Layer00 { get; }
        public AnimPlaybackLayer Layer01 { get; }
        public AnimPlaybackLayer Layer02 { get; }
        public AnimPlaybackLayer Layer03 { get; }
        public bool IsHeld { get; }

        public AnimPlaybackLayer GetLayer(AnimPlaybackLayerSlot slot)
        {
            switch (slot)
            {
                case AnimPlaybackLayerSlot.Layer00:
                    return Layer00;
                case AnimPlaybackLayerSlot.Layer01:
                    return Layer01;
                case AnimPlaybackLayerSlot.Layer02:
                    return Layer02;
                case AnimPlaybackLayerSlot.Layer03:
                    return Layer03;
                default:
                    return default;
            }
        }
    }

    public enum AnimModulationKind : byte
    {
        None = 0,
        FloatParameter = 1,
        LayerWeight = 2,
        PresentationOffsetPosition = 3,
        PresentationOffsetRotation = 4
    }

    public enum AnimModulationInterpolation : byte
    {
        None = 0,
        Immediate = 1,
        AdapterOwned = 2
    }

    public readonly struct AnimModulationCommand
    {
        private AnimModulationCommand(
            AnimModulationKind kind,
            AnimBindingId bindingId,
            AnimModulationInterpolation interpolation,
            CoCoActivationId sourceActivationId,
            uint serial,
            float durationSeconds,
            float valueX,
            float valueY,
            float valueZ,
            float valueW)
        {
            Kind = kind;
            BindingId = bindingId;
            Interpolation = interpolation;
            SourceActivationId = sourceActivationId;
            Serial = serial;
            DurationSeconds = durationSeconds;
            ValueX = valueX;
            ValueY = valueY;
            ValueZ = valueZ;
            ValueW = valueW;
        }

        public AnimModulationKind Kind { get; }
        public AnimBindingId BindingId { get; }
        public AnimModulationInterpolation Interpolation { get; }
        public CoCoActivationId SourceActivationId { get; }
        public uint Serial { get; }
        public float DurationSeconds { get; }
        public float ValueX { get; }
        public float ValueY { get; }
        public float ValueZ { get; }
        public float ValueW { get; }
        public bool IsValid => Kind >= AnimModulationKind.FloatParameter &&
                               Kind <= AnimModulationKind.PresentationOffsetRotation &&
                               BindingId.IsValid &&
                               Interpolation >= AnimModulationInterpolation.Immediate &&
                               Interpolation <= AnimModulationInterpolation.AdapterOwned &&
                               SourceActivationId.IsValid &&
                               Serial != 0U &&
                               AnimMath.IsFiniteNonNegative(DurationSeconds) &&
                               AnimMath.IsFinite(ValueX) &&
                               AnimMath.IsFinite(ValueY) &&
                               AnimMath.IsFinite(ValueZ) &&
                               AnimMath.IsFinite(ValueW) &&
                               (Interpolation != AnimModulationInterpolation.Immediate ||
                                DurationSeconds == 0f);

        public static bool TryCreate(
            AnimModulationKind kind,
            AnimBindingId bindingId,
            AnimModulationInterpolation interpolation,
            CoCoActivationId sourceActivationId,
            uint serial,
            float durationSeconds,
            float valueX,
            float valueY,
            float valueZ,
            float valueW,
            out AnimModulationCommand command)
        {
            command = new AnimModulationCommand(
                kind,
                bindingId,
                interpolation,
                sourceActivationId,
                serial,
                durationSeconds,
                valueX,
                valueY,
                valueZ,
                valueW);
            if (!command.IsValid)
            {
                command = default;
                return false;
            }

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
