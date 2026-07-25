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
            state.LastLoopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            state.EnsureCapacity(eventConfigs?.Length ?? 0);
            state.ClearFlags();
            state.Receiver?.ReceiveSmbSignal(
                AnimSmbSignal.State(
                    AnimSmbSignalKind.StateEnter,
                    stateInfo.fullPathHash,
                    layerIndex,
                    state.LastLoopCount,
                    stateInfo.normalizedTime));
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

            int eventCount = eventConfigs?.Length ?? 0;
            state.EnsureCapacity(eventCount);
            int loopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            float normalizedTime = stateInfo.normalizedTime - loopCount;
            if (stateInfo.loop && loopCount != state.LastLoopCount)
            {
                state.LastLoopCount = loopCount;
                state.ClearFlags();
            }

            for (int index = 0; index < eventCount; index++)
            {
                AnimEventConfig config = eventConfigs[index];
                if (config == null ||
                    state.TriggerFlags[index] ||
                    normalizedTime < config.TriggerTime ||
                    !AnimBindingId.TryCreate(
                        config.BindingId,
                        out AnimBindingId bindingId))
                {
                    continue;
                }

                state.TriggerFlags[index] = true;
                state.Receiver.ReceiveSmbSignal(
                    AnimSmbSignal.Marker(
                        bindingId,
                        stateInfo.fullPathHash,
                        layerIndex,
                        loopCount,
                        normalizedTime));
            }
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

            state.Receiver?.ReceiveSmbSignal(
                AnimSmbSignal.State(
                    AnimSmbSignalKind.StateExit,
                    stateInfo.fullPathHash,
                    layerIndex,
                    Mathf.FloorToInt(stateInfo.normalizedTime),
                    stateInfo.normalizedTime));
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

        private sealed class InstanceState
        {
            internal IAnimEventReceiver Receiver;
            internal bool[] TriggerFlags = Array.Empty<bool>();
            internal int LastLoopCount;

            internal void EnsureCapacity(int count)
            {
                if (TriggerFlags.Length != count)
                {
                    TriggerFlags = count == 0 ? Array.Empty<bool>() : new bool[count];
                }
            }

            internal void ClearFlags()
            {
                Array.Clear(TriggerFlags, 0, TriggerFlags.Length);
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
        void ReceiveSmbSignal(in AnimSmbSignal signal);
    }
}
