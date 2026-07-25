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
            state.ResetForEnter(stateInfo.normalizedTime);
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
                state.ReceiverRejected = true;
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

            // Rejection stops only the current callback enumeration. A poisoned
            // Operator buffer continues rejecting immediately until its recovery
            // boundary, while a new Graph/Epoch must be allowed to try again.
            state.ReceiverRejected = false;
            if (!IsSupportedNormalizedTime(stateInfo.normalizedTime))
            {
                state.Rebase(stateInfo.normalizedTime);
                return;
            }

            if (!state.HasPreviousNormalizedTime ||
                stateInfo.normalizedTime < state.PreviousNormalizedTime)
            {
                // Controller reset, re-entry or a backwards seek: establish a
                // new baseline but never synthesize reverse/retroactive markers.
                state.Rebase(stateInfo.normalizedTime);
                return;
            }

            EmitCrossedMarkers(
                state,
                stateInfo,
                layerIndex,
                state.PreviousNormalizedTime,
                stateInfo.normalizedTime);
            state.Rebase(stateInfo.normalizedTime);
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

            // See OnStateUpdate: this flag is callback-local, not a second
            // lifetime latch beside the Operator's feedback buffer.
            state.ReceiverRejected = false;
            if (!IsSupportedNormalizedTime(stateInfo.normalizedTime) ||
                (state.HasPreviousNormalizedTime &&
                 stateInfo.normalizedTime < state.PreviousNormalizedTime))
            {
                state.Rebase(stateInfo.normalizedTime);
                return;
            }

            if (state.HasPreviousNormalizedTime)
            {
                EmitCrossedMarkers(
                    state,
                    stateInfo,
                    layerIndex,
                    state.PreviousNormalizedTime,
                    stateInfo.normalizedTime);
                state.Rebase(stateInfo.normalizedTime);
            }

            int loopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            if (!state.ReceiverRejected &&
                !state.Receiver.TryReceiveSmbSignal(
                    AnimSmbSignal.State(
                        AnimSmbSignalKind.StateExit,
                        stateInfo.fullPathHash,
                        layerIndex,
                        loopCount,
                        stateInfo.normalizedTime)))
            {
                state.ReceiverRejected = true;
            }
        }

        private void EmitCrossedMarkers(
            InstanceState state,
            AnimatorStateInfo stateInfo,
            int layerIndex,
            float previousNormalizedTime,
            float currentNormalizedTime)
        {
            if (state.ReceiverRejected || currentNormalizedTime <= previousNormalizedTime)
            {
                return;
            }

            state.ReceiverRejected = !TryEmitCrossedMarkers(
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
            internal bool HasPreviousNormalizedTime;
            internal float PreviousNormalizedTime;
            internal bool ReceiverRejected;

            internal void ResetForEnter(float normalizedTime)
            {
                ReceiverRejected = false;
                Rebase(normalizedTime);
            }

            internal void Rebase(float normalizedTime)
            {
                HasPreviousNormalizedTime =
                    IsSupportedNormalizedTime(normalizedTime);
                PreviousNormalizedTime = normalizedTime;
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
}
