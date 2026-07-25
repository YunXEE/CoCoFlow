using System;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Animation.Contracts;
using UnityEngine;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.Modules.Animation
{
    [Serializable]
    public sealed class AnimEventConfig
    {
        [SerializeField] private ulong bindingId;
        [SerializeField, FormerlySerializedAs("eventName")] private string eventName;
        [SerializeField, Range(0f, 1f), FormerlySerializedAs("triggerTime")]
        private float triggerTime;

        public ulong BindingId => bindingId;
        public string EventName => eventName ?? string.Empty;
        public float TriggerTime => triggerTime;
    }

    public sealed class AnimEventSmb : StateMachineBehaviour
    {
        [SerializeField, FormerlySerializedAs("events")]
        private AnimEventConfig[] eventConfigs = Array.Empty<AnimEventConfig>();

        private readonly ConditionalWeakTable<Animator, InstanceState> _instanceStates =
            new ConditionalWeakTable<Animator, InstanceState>();

        public AnimEventConfig[] EventConfigs => eventConfigs;

        public override void OnStateEnter(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            InstanceState state = GetOrCreateState(animator);
            state.Receiver = ResolveReceiver(animator);
            TryResolveStateSnapshot(
                state.Receiver,
                animator,
                layerIndex,
                out AnimSmbStateSnapshot snapshot);
            StateCursor cursor = state
                .GetLayer(layerIndex)
                .BeginEnter(
                    snapshot,
                    stateInfo);
            if (state.Receiver == null ||
                !IsSupportedNormalizedTime(stateInfo.normalizedTime))
            {
                return;
            }

            int loopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            if (!state.Receiver.TryReceiveSmbSignal(
                    AnimSmbSignal.State(
                        AnimSmbSignalKind.StateEnter,
                        stateInfo.fullPathHash,
                        layerIndex,
                        loopCount,
                        stateInfo.normalizedTime)))
            {
                cursor.ReceiverRejected = true;
            }
        }

        public override void OnStateUpdate(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            if (!_instanceStates.TryGetValue(animator, out InstanceState state) ||
                state.Receiver == null)
            {
                return;
            }

            LayerState layer = state.GetLayer(layerIndex);
            TryResolveStateSnapshot(
                state.Receiver,
                animator,
                layerIndex,
                out AnimSmbStateSnapshot snapshot);
            StateCursor cursor = layer.Resolve(
                snapshot,
                stateInfo,
                false);
            if (cursor == null)
            {
                return;
            }

            // Rejection stops only the current callback enumeration. A poisoned
            // Operator buffer continues rejecting immediately until its recovery
            // boundary, while a new Graph/Epoch must be allowed to try again.
            cursor.ReceiverRejected = false;
            if (!IsSupportedNormalizedTime(stateInfo.normalizedTime))
            {
                cursor.Rebase(stateInfo.normalizedTime);
                return;
            }

            if (!cursor.HasPreviousNormalizedTime ||
                stateInfo.normalizedTime < cursor.PreviousNormalizedTime)
            {
                // Controller reset, re-entry or a backwards seek: establish a
                // new baseline but never synthesize reverse/retroactive markers.
                cursor.Rebase(stateInfo.normalizedTime);
                return;
            }

            EmitCrossedMarkers(
                state,
                cursor,
                stateInfo,
                layerIndex,
                cursor.PreviousNormalizedTime,
                stateInfo.normalizedTime);
            cursor.Rebase(stateInfo.normalizedTime);
        }

        public override void OnStateExit(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            if (!_instanceStates.TryGetValue(animator, out InstanceState state))
            {
                return;
            }

            if (state.Receiver == null)
            {
                return;
            }

            LayerState layer = state.GetLayer(layerIndex);
            TryResolveStateSnapshot(
                state.Receiver,
                animator,
                layerIndex,
                out AnimSmbStateSnapshot snapshot);
            StateCursor cursor = layer.Resolve(
                snapshot,
                stateInfo,
                true);
            if (cursor == null)
            {
                return;
            }

            // See OnStateUpdate: this flag is callback-local, not a second
            // lifetime latch beside the Operator's feedback buffer.
            cursor.ReceiverRejected = false;
            if (!IsSupportedNormalizedTime(stateInfo.normalizedTime) ||
                (cursor.HasPreviousNormalizedTime &&
                 stateInfo.normalizedTime < cursor.PreviousNormalizedTime))
            {
                cursor.Rebase(stateInfo.normalizedTime);
                layer.Release(cursor, snapshot);
                return;
            }

            if (cursor.HasPreviousNormalizedTime)
            {
                EmitCrossedMarkers(
                    state,
                    cursor,
                    stateInfo,
                    layerIndex,
                    cursor.PreviousNormalizedTime,
                    stateInfo.normalizedTime);
                cursor.Rebase(stateInfo.normalizedTime);
            }

            int loopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            if (!cursor.ReceiverRejected &&
                !state.Receiver.TryReceiveSmbSignal(
                    AnimSmbSignal.State(
                        AnimSmbSignalKind.StateExit,
                        stateInfo.fullPathHash,
                        layerIndex,
                        loopCount,
                        stateInfo.normalizedTime)))
            {
                cursor.ReceiverRejected = true;
            }

            layer.Release(cursor, snapshot);
        }

        private void EmitCrossedMarkers(
            InstanceState state,
            StateCursor cursor,
            AnimatorStateInfo stateInfo,
            int layerIndex,
            float previousNormalizedTime,
            float currentNormalizedTime)
        {
            if (cursor.ReceiverRejected ||
                currentNormalizedTime <= previousNormalizedTime)
            {
                return;
            }

            cursor.ReceiverRejected = !TryEmitCrossedMarkers(
                eventConfigs,
                stateInfo.loop,
                stateInfo.fullPathHash,
                layerIndex,
                previousNormalizedTime,
                currentNormalizedTime,
                state.Receiver);
        }

        internal static bool TryEmitCrossedMarkers(
            AnimEventConfig[] configs,
            bool isLooping,
            int stateHash,
            int layerIndex,
            float previousNormalizedTime,
            float currentNormalizedTime,
            IAnimEventReceiver receiver)
        {
            if (receiver == null ||
                !IsSupportedNormalizedTime(previousNormalizedTime) ||
                !IsSupportedNormalizedTime(currentNormalizedTime) ||
                currentNormalizedTime <= previousNormalizedTime ||
                !HasValidMarker(configs))
            {
                return receiver != null;
            }

            if (!isLooping)
            {
                return TryEmitMarkersForLoop(
                    configs,
                    receiver,
                    stateHash,
                    layerIndex,
                    0,
                    Mathf.Clamp01(previousNormalizedTime),
                    Mathf.Clamp01(currentNormalizedTime));
            }

            int firstLoop = Mathf.FloorToInt(previousNormalizedTime);
            int lastLoop = Mathf.FloorToInt(currentNormalizedTime);
            for (int loop = firstLoop;; loop++)
            {
                float loopStart = loop == firstLoop
                    ? previousNormalizedTime - loop
                    // A newly entered loop starts strictly after the prior
                    // loop's end, so its 0.0 marker belongs to this interval.
                    : -float.Epsilon;
                float loopEnd = loop == lastLoop
                    ? currentNormalizedTime - loop
                    : 1f;
                if (!TryEmitMarkersForLoop(
                        configs,
                        receiver,
                        stateHash,
                        layerIndex,
                        loop,
                        loopStart,
                        loopEnd))
                {
                    return false;
                }

                if (loop == lastLoop)
                {
                    return true;
                }
            }
        }

        private static bool TryEmitMarkersForLoop(
            AnimEventConfig[] configs,
            IAnimEventReceiver receiver,
            int stateHash,
            int layerIndex,
            int loopCount,
            float intervalStart,
            float intervalEnd)
        {
            if (intervalEnd <= intervalStart)
            {
                return true;
            }

            int eventCount = configs?.Length ?? 0;
            float previousTriggerTime = float.NegativeInfinity;
            int previousConfigIndex = -1;
            while (true)
            {
                int selectedIndex = -1;
                float selectedTriggerTime = default;
                AnimBindingId selectedBindingId = default;
                for (int index = 0; index < eventCount; index++)
                {
                    AnimEventConfig config = configs[index];
                    if (config == null ||
                        !IsFinite(config.TriggerTime) ||
                        config.TriggerTime < 0f ||
                        config.TriggerTime > 1f ||
                        config.TriggerTime <= intervalStart ||
                        config.TriggerTime > intervalEnd ||
                        !AnimBindingId.TryCreate(
                            config.BindingId,
                            out AnimBindingId bindingId) ||
                        config.TriggerTime < previousTriggerTime ||
                        (config.TriggerTime == previousTriggerTime &&
                         index <= previousConfigIndex) ||
                        (selectedIndex >= 0 &&
                         (config.TriggerTime > selectedTriggerTime ||
                          (config.TriggerTime == selectedTriggerTime &&
                           index > selectedIndex))))
                    {
                        continue;
                    }

                    selectedIndex = index;
                    selectedTriggerTime = config.TriggerTime;
                    selectedBindingId = bindingId;
                }

                if (selectedIndex < 0)
                {
                    return true;
                }

                if (!receiver.TryReceiveSmbSignal(
                        AnimSmbSignal.Marker(
                            selectedBindingId,
                            stateHash,
                            layerIndex,
                            loopCount,
                            selectedTriggerTime)))
                {
                    return false;
                }

                previousTriggerTime = selectedTriggerTime;
                previousConfigIndex = selectedIndex;
            }
        }

        private InstanceState GetOrCreateState(Animator animator)
        {
            if (_instanceStates.TryGetValue(animator, out InstanceState state))
            {
                return state;
            }

            state = new InstanceState();
            _instanceStates.Add(animator, state);
            return state;
        }

        private static IAnimEventReceiver ResolveReceiver(Animator animator)
        {
            AnimOperator advanced = animator.GetComponent<AnimOperator>();
            if (advanced != null)
            {
                return advanced;
            }

            return animator.GetComponent<AnimAutoOperator>();
        }

        private static bool TryResolveStateSnapshot(
            IAnimEventReceiver receiver,
            Animator animator,
            int layerIndex,
            out AnimSmbStateSnapshot snapshot)
        {
            if (receiver is IAnimEventStateResolver resolver &&
                resolver.TryCaptureSmbState(
                    layerIndex,
                    out snapshot))
            {
                return true;
            }

            if (animator == null ||
                layerIndex < 0 ||
                layerIndex >= animator.layerCount)
            {
                snapshot = default;
                return false;
            }

            bool inTransition = animator.IsInTransition(layerIndex);
            snapshot = new AnimSmbStateSnapshot(
                animator.GetCurrentAnimatorStateInfo(layerIndex),
                inTransition,
                inTransition
                    ? animator.GetNextAnimatorStateInfo(layerIndex)
                    : default);
            return snapshot.IsValid;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsSupportedNormalizedTime(float value) =>
            IsFinite(value) &&
            value >= 0f &&
            value < (float)int.MaxValue;

        private static bool HasValidMarker(AnimEventConfig[] configs)
        {
            int eventCount = configs?.Length ?? 0;
            for (int index = 0; index < eventCount; index++)
            {
                AnimEventConfig config = configs[index];
                if (config != null &&
                    IsFinite(config.TriggerTime) &&
                    config.TriggerTime >= 0f &&
                    config.TriggerTime <= 1f &&
                    AnimBindingId.TryCreate(
                        config.BindingId,
                        out AnimBindingId _))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class InstanceState
        {
            internal IAnimEventReceiver Receiver;
            private LayerState[] _layers = Array.Empty<LayerState>();

            internal LayerState GetLayer(int layerIndex)
            {
                if (layerIndex < 0)
                {
                    return null;
                }

                if (layerIndex >= _layers.Length)
                {
                    Array.Resize(ref _layers, layerIndex + 1);
                }

                return _layers[layerIndex] ??=
                    new LayerState();
            }
        }

        private sealed class LayerState
        {
            private readonly StateCursor _current = new StateCursor();
            private readonly StateCursor _next = new StateCursor();
            private bool _wasInTransition;

            internal StateCursor BeginEnter(
                in AnimSmbStateSnapshot snapshot,
                AnimatorStateInfo stateInfo)
            {
                bool isIncoming =
                    snapshot.IsValid &&
                    snapshot.IsInTransition &&
                    snapshot.NextStateHash ==
                    stateInfo.fullPathHash;
                if (!isIncoming)
                {
                    _next.Clear();
                    _wasInTransition = false;
                    _current.ResetForEnter(
                        stateInfo.fullPathHash,
                        stateInfo.normalizedTime);
                    return _current;
                }

                _wasInTransition = true;
                StateCursor cursor;
                if (!_current.IsActive)
                {
                    cursor = _current;
                }
                else if (!_next.IsActive)
                {
                    cursor = _next;
                }
                else
                {
                    // Mecanim exposes only current and next state instances.
                    // A third enter means the previously tracked next instance
                    // can no longer receive an independently addressable callback.
                    cursor = _next;
                }

                cursor.ResetForEnter(
                    stateInfo.fullPathHash,
                    stateInfo.normalizedTime);
                return cursor;
            }

            internal StateCursor Resolve(
                in AnimSmbStateSnapshot snapshot,
                AnimatorStateInfo callbackState,
                bool forExit)
            {
                if (!forExit &&
                    _wasInTransition &&
                    snapshot.IsValid &&
                    !snapshot.IsInTransition &&
                    _next.IsActive)
                {
                    PromoteNext();
                    _wasInTransition = false;
                }
                else if (snapshot.IsValid &&
                         snapshot.IsInTransition)
                {
                    _wasInTransition = true;
                }

                bool currentMatches =
                    _current.IsActive &&
                    _current.StateHash == callbackState.fullPathHash;
                bool nextMatches =
                    _next.IsActive &&
                    _next.StateHash == callbackState.fullPathHash;
                if (!currentMatches && !nextMatches)
                {
                    return null;
                }

                bool inTransition =
                    snapshot.IsValid &&
                    snapshot.IsInTransition;
                if (!forExit &&
                    !inTransition &&
                    nextMatches &&
                    snapshot.CurrentStateHash == _next.StateHash)
                {
                    PromoteNext();
                    _wasInTransition = false;
                    return _current;
                }

                if (currentMatches && !nextMatches)
                {
                    return _current;
                }

                if (!currentMatches)
                {
                    if (!forExit && !inTransition)
                    {
                        PromoteNext();
                        return _current;
                    }

                    return _next;
                }

                if (inTransition)
                {
                    float currentDistance = StateDistance(
                        callbackState,
                        snapshot.CurrentStateHash,
                        snapshot.CurrentNormalizedTime,
                        _current);
                    float nextDistance = StateDistance(
                        callbackState,
                        snapshot.NextStateHash,
                        snapshot.NextNormalizedTime,
                        _next);
                    return nextDistance < currentDistance
                        ? _next
                        : _current;
                }

                if (forExit)
                {
                    return CursorDistance(callbackState, _next) <
                           CursorDistance(callbackState, _current)
                        ? _next
                        : _current;
                }

                return _current;
            }

            internal void Release(
                StateCursor cursor,
                in AnimSmbStateSnapshot snapshot)
            {
                bool releasedCurrent = ReferenceEquals(cursor, _current);
                cursor.Clear();
                if (releasedCurrent &&
                    _next.IsActive &&
                    (!snapshot.IsValid ||
                     !snapshot.IsInTransition))
                {
                    PromoteNext();
                    _wasInTransition = false;
                }
            }

            private void PromoteNext()
            {
                _current.CopyFrom(_next);
                _next.Clear();
            }

            private static float StateDistance(
                AnimatorStateInfo callbackState,
                int animatorStateHash,
                float animatorNormalizedTime,
                StateCursor cursor)
            {
                if (animatorStateHash != callbackState.fullPathHash ||
                    !IsSupportedNormalizedTime(animatorNormalizedTime))
                {
                    return float.PositiveInfinity;
                }

                float animatorDistance = Mathf.Abs(
                    callbackState.normalizedTime -
                    animatorNormalizedTime);
                return animatorDistance +
                       CursorDistance(callbackState, cursor) *
                       0.0001f;
            }

            private static float CursorDistance(
                AnimatorStateInfo callbackState,
                StateCursor cursor)
            {
                return cursor.HasPreviousNormalizedTime
                    ? Mathf.Abs(
                        callbackState.normalizedTime -
                        cursor.PreviousNormalizedTime)
                    : float.PositiveInfinity;
            }
        }

        private sealed class StateCursor
        {
            internal int StateHash { get; private set; }
            internal bool IsActive { get; private set; }
            internal bool HasPreviousNormalizedTime { get; private set; }
            internal float PreviousNormalizedTime { get; private set; }
            internal bool ReceiverRejected { get; set; }

            internal void ResetForEnter(
                int stateHash,
                float normalizedTime)
            {
                StateHash = stateHash;
                IsActive = true;
                ReceiverRejected = false;
                Rebase(normalizedTime);
            }

            internal void Rebase(float normalizedTime)
            {
                HasPreviousNormalizedTime =
                    IsSupportedNormalizedTime(normalizedTime);
                PreviousNormalizedTime = normalizedTime;
            }

            internal void CopyFrom(StateCursor source)
            {
                StateHash = source.StateHash;
                IsActive = source.IsActive;
                HasPreviousNormalizedTime =
                    source.HasPreviousNormalizedTime;
                PreviousNormalizedTime =
                    source.PreviousNormalizedTime;
                ReceiverRejected = source.ReceiverRejected;
            }

            internal void Clear()
            {
                StateHash = default;
                IsActive = false;
                HasPreviousNormalizedTime = false;
                PreviousNormalizedTime = default;
                ReceiverRejected = false;
            }
        }
    }

    internal enum AnimSmbSignalKind : byte
    {
        StateEnter = 1,
        Marker = 2,
        StateExit = 3
    }

    internal readonly struct AnimSmbSignal
    {
        private AnimSmbSignal(
            AnimSmbSignalKind kind,
            AnimBindingId bindingId,
            int stateHash,
            int layerIndex,
            int loopCount,
            float normalizedTime)
        {
            Kind = kind;
            BindingId = bindingId;
            StateHash = stateHash;
            LayerIndex = layerIndex;
            LoopCount = loopCount;
            NormalizedTime = normalizedTime;
        }

        internal AnimSmbSignalKind Kind { get; }
        internal AnimBindingId BindingId { get; }
        internal int StateHash { get; }
        internal int LayerIndex { get; }
        internal int LoopCount { get; }
        internal float NormalizedTime { get; }

        internal static AnimSmbSignal State(
            AnimSmbSignalKind kind,
            int stateHash,
            int layerIndex,
            int loopCount,
            float normalizedTime)
        {
            return new AnimSmbSignal(
                kind,
                default,
                stateHash,
                layerIndex,
                loopCount,
                normalizedTime);
        }

        internal static AnimSmbSignal Marker(
            AnimBindingId bindingId,
            int stateHash,
            int layerIndex,
            int loopCount,
            float normalizedTime)
        {
            return new AnimSmbSignal(
                AnimSmbSignalKind.Marker,
                bindingId,
                stateHash,
                layerIndex,
                loopCount,
                normalizedTime);
        }
    }

    internal interface IAnimEventReceiver
    {
        bool TryReceiveSmbSignal(in AnimSmbSignal signal);
    }

    internal interface IAnimEventStateResolver
    {
        bool TryCaptureSmbState(
            int layerIndex,
            out AnimSmbStateSnapshot snapshot);
    }

    internal readonly struct AnimSmbStateSnapshot
    {
        internal AnimSmbStateSnapshot(
            AnimatorStateInfo current,
            bool isInTransition,
            AnimatorStateInfo next)
        {
            CurrentStateHash = current.fullPathHash;
            CurrentNormalizedTime = current.normalizedTime;
            IsInTransition = isInTransition;
            NextStateHash = isInTransition
                ? next.fullPathHash
                : default;
            NextNormalizedTime = isInTransition
                ? next.normalizedTime
                : default;
            IsValid =
                CurrentStateHash != 0 &&
                IsFiniteNonNegative(CurrentNormalizedTime) &&
                (!isInTransition ||
                 (NextStateHash != 0 &&
                  IsFiniteNonNegative(NextNormalizedTime)));
        }

        internal int CurrentStateHash { get; }
        internal float CurrentNormalizedTime { get; }
        internal bool IsInTransition { get; }
        internal int NextStateHash { get; }
        internal float NextNormalizedTime { get; }
        internal bool IsValid { get; }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value >= 0f;
        }
    }
}
