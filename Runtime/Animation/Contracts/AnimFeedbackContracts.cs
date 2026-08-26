using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Animation.Contracts
{
    public enum AnimFeedbackKind : byte
    {
        None = 0,
        StateEnter = 1,
        StateMarker = 2,
        StateExit = 3,
        RootMotion = 7
    }

    /// <summary>
    /// Unified allocation-free feedback record for SMB, playback lifecycle and root motion.
    /// </summary>
    public readonly struct AnimFeedbackRecord
    {
        private AnimFeedbackRecord(
            AnimFeedbackKind kind,
            AnimBindingId eventBindingId,
            int stateFullPathHash,
            int layerIndex,
            int loopCount,
            float normalizedTime,
            float positionX,
            float positionY,
            float positionZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW)
        {
            Kind = kind;
            EventBindingId = eventBindingId;
            StateFullPathHash = stateFullPathHash;
            LayerIndex = layerIndex;
            LoopCount = loopCount;
            NormalizedTime = normalizedTime;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
        }

        public AnimFeedbackKind Kind { get; }
        public AnimBindingId EventBindingId { get; }
        public int StateFullPathHash { get; }
        public int LayerIndex { get; }
        public int LoopCount { get; }
        public float NormalizedTime { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }

        public bool IsValid
        {
            get
            {
                switch (Kind)
                {
                    case AnimFeedbackKind.StateEnter:
                    case AnimFeedbackKind.StateExit:
                        return StateFullPathHash != 0 &&
                               LayerIndex >= 0 &&
                               LoopCount >= 0 &&
                               AnimMath.IsFiniteNonNegative(NormalizedTime);
                    case AnimFeedbackKind.StateMarker:
                        return EventBindingId.IsValid &&
                               StateFullPathHash != 0 &&
                               LayerIndex >= 0 &&
                               LoopCount >= 0 &&
                               AnimMath.IsFiniteNonNegative(NormalizedTime);
                    case AnimFeedbackKind.RootMotion:
                        return AnimMath.IsFinite(PositionX) &&
                               AnimMath.IsFinite(PositionY) &&
                               AnimMath.IsFinite(PositionZ) &&
                               AnimMath.IsFinite(RotationX) &&
                               AnimMath.IsFinite(RotationY) &&
                               AnimMath.IsFinite(RotationZ) &&
                               AnimMath.IsFinite(RotationW) &&
                               RotationX * RotationX +
                               RotationY * RotationY +
                               RotationZ * RotationZ +
                               RotationW * RotationW > 0.000000000001f;
                    default:
                        return false;
                }
            }
        }

        public static bool TryCreateState(
            AnimFeedbackKind kind,
            AnimBindingId eventBindingId,
            int stateFullPathHash,
            int layerIndex,
            int loopCount,
            float normalizedTime,
            out AnimFeedbackRecord record)
        {
            if (kind != AnimFeedbackKind.StateEnter &&
                kind != AnimFeedbackKind.StateMarker &&
                kind != AnimFeedbackKind.StateExit)
            {
                record = default;
                return false;
            }

            record = new AnimFeedbackRecord(
                kind,
                eventBindingId,
                stateFullPathHash,
                layerIndex,
                loopCount,
                normalizedTime,
                default,
                default,
                default,
                default,
                default,
                default,
                default);
            if (!record.IsValid)
            {
                record = default;
                return false;
            }

            return true;
        }

        public static bool TryCreateRootMotion(
            float positionX,
            float positionY,
            float positionZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW,
            out AnimFeedbackRecord record)
        {
            record = new AnimFeedbackRecord(
                AnimFeedbackKind.RootMotion,
                default,
                default,
                default,
                default,
                default,
                positionX,
                positionY,
                positionZ,
                rotationX,
                rotationY,
                rotationZ,
                rotationW);
            if (!record.IsValid)
            {
                record = default;
                return false;
            }

            return true;
        }
    }

    public readonly struct AnimFeedbackEvent
    {
        private AnimFeedbackEvent(AnimFeedbackRecord record)
        {
            Record = record;
        }

        public AnimFeedbackRecord Record { get; }
        public bool IsValid => Record.IsValid;

        public static bool TryCreate(
            in AnimFeedbackRecord record,
            out AnimFeedbackEvent feedbackEvent)
        {
            if (!record.IsValid)
            {
                feedbackEvent = default;
                return false;
            }

            feedbackEvent = new AnimFeedbackEvent(record);
            return true;
        }
    }

    /// <summary>
    /// Fixed-capacity batch reduced from all Animation feedback packets visible this Tick.
    /// </summary>
    public struct AnimFeedbackIntent
    {
        private byte _count;
        private bool _overflowed;
        private AnimFeedbackRecord _record00;
        private AnimFeedbackRecord _record01;
        private AnimFeedbackRecord _record02;
        private AnimFeedbackRecord _record03;
        private AnimFeedbackRecord _record04;
        private AnimFeedbackRecord _record05;
        private AnimFeedbackRecord _record06;
        private AnimFeedbackRecord _record07;
        private AnimFeedbackRecord _record08;
        private AnimFeedbackRecord _record09;
        private AnimFeedbackRecord _record10;
        private AnimFeedbackRecord _record11;
        private AnimFeedbackRecord _record12;
        private AnimFeedbackRecord _record13;
        private AnimFeedbackRecord _record14;
        private AnimFeedbackRecord _record15;

        public int Count => _count;
        public bool Overflowed => _overflowed;
        public bool IsEmpty => _count == 0;

        public static bool TryCreateSingle(
            in AnimFeedbackRecord record,
            out AnimFeedbackIntent intent)
        {
            if (!record.IsValid)
            {
                intent = default;
                return false;
            }

            intent = default;
            intent._record00 = record;
            intent._count = 1;
            return true;
        }

        public bool TryGetRecord(int index, out AnimFeedbackRecord record)
        {
            if (index < 0 || index >= _count)
            {
                record = default;
                return false;
            }

            switch (index)
            {
                case 0:
                    record = _record00;
                    break;
                case 1:
                    record = _record01;
                    break;
                case 2:
                    record = _record02;
                    break;
                case 3:
                    record = _record03;
                    break;
                case 4:
                    record = _record04;
                    break;
                case 5:
                    record = _record05;
                    break;
                case 6:
                    record = _record06;
                    break;
                case 7:
                    record = _record07;
                    break;
                case 8:
                    record = _record08;
                    break;
                case 9:
                    record = _record09;
                    break;
                case 10:
                    record = _record10;
                    break;
                case 11:
                    record = _record11;
                    break;
                case 12:
                    record = _record12;
                    break;
                case 13:
                    record = _record13;
                    break;
                case 14:
                    record = _record14;
                    break;
                case 15:
                    record = _record15;
                    break;
                default:
                    record = default;
                    return false;
            }

            return true;
        }

        public AnimFeedbackIntent Append(in AnimFeedbackRecord record)
        {
            if (!record.IsValid)
            {
                return this;
            }

            AnimFeedbackIntent result = this;
            if (result._count >= AnimContractLimits.FeedbackCapacity)
            {
                result._overflowed = true;
                return result;
            }

            switch (result._count)
            {
                case 0:
                    result._record00 = record;
                    break;
                case 1:
                    result._record01 = record;
                    break;
                case 2:
                    result._record02 = record;
                    break;
                case 3:
                    result._record03 = record;
                    break;
                case 4:
                    result._record04 = record;
                    break;
                case 5:
                    result._record05 = record;
                    break;
                case 6:
                    result._record06 = record;
                    break;
                case 7:
                    result._record07 = record;
                    break;
                case 8:
                    result._record08 = record;
                    break;
                case 9:
                    result._record09 = record;
                    break;
                case 10:
                    result._record10 = record;
                    break;
                case 11:
                    result._record11 = record;
                    break;
                case 12:
                    result._record12 = record;
                    break;
                case 13:
                    result._record13 = record;
                    break;
                case 14:
                    result._record14 = record;
                    break;
                case 15:
                    result._record15 = record;
                    break;
            }

            result._count++;
            return result;
        }

        public AnimFeedbackIntent Merge(in AnimFeedbackIntent candidate)
        {
            AnimFeedbackIntent result = this;
            for (int index = 0; index < candidate.Count; index++)
            {
                if (candidate.TryGetRecord(index, out AnimFeedbackRecord record))
                {
                    result = result.Append(record);
                }
            }

            if (candidate.Overflowed)
            {
                result._overflowed = true;
            }

            return result;
        }
    }

    public struct AnimFeedbackIntentReducer : ICoCoIntentReducer<AnimFeedbackIntent>
    {
        public AnimFeedbackIntent Reduce(
            in AnimFeedbackIntent current,
            in AnimFeedbackIntent candidate) =>
            current.Merge(candidate);
    }

    public sealed class AnimFeedbackIntentReducerFactory :
        ICoCoIntentReducerFactory<AnimFeedbackIntent, AnimFeedbackIntentReducer>
    {
        public AnimFeedbackIntentReducer Create(CoCoGraphInstanceId graphInstanceId) => default;
    }

    public sealed class AnimFeedbackEventToIntentAdapter :
        ICoCoEventToIntentAdapter<AnimFeedbackEvent, AnimFeedbackIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<AnimFeedbackEvent> packet,
            out AnimFeedbackIntent intent)
        {
            if (!packet.IsValid || !packet.Payload.IsValid)
            {
                intent = default;
                return false;
            }

            return AnimFeedbackIntent.TryCreateSingle(packet.Payload.Record, out intent);
        }
    }
}
