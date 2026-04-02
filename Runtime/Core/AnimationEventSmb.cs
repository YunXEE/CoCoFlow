using System;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Modules.Animation;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    [Serializable]
    public class AnimEventConfig
    {
        [Tooltip("Event Name")]
        public string eventName;
        
        [Range(0f, 1f)]
        [Tooltip("Trigger Percent Of Animation")]
        public float triggerTime;
    }

    public class AnimationEventSmb : StateMachineBehaviour
    {
        public AnimEventConfig[] events;

        private class InstanceState
        {
            public AnimationHandler Handler;
            public bool[] TriggerFlags;
            public int LastLoopCount;
        }
        
        // 弱引用表+实例状态
        private readonly ConditionalWeakTable<Animator, InstanceState> _instanceStates = 
            new ConditionalWeakTable<Animator, InstanceState>();

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!_instanceStates.TryGetValue(animator, out var state))
            {
                state = new InstanceState
                {
                    Handler = animator.GetComponent<AnimationHandler>(),
                    TriggerFlags = new bool[events.Length]
                };
                _instanceStates.Add(animator, state); // 添加到弱引用表
            }

            state.LastLoopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            for (var i = 0; i < state.TriggerFlags.Length; i++)
            {
                state.TriggerFlags[i] = false;
            }
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!_instanceStates.TryGetValue(animator, out var state) || state.Handler == null) return;

            int currentLoopCount = Mathf.FloorToInt(stateInfo.normalizedTime);
            float currentNormalizedTime = stateInfo.normalizedTime - currentLoopCount;
            
            // 处理循环动画
            if (stateInfo.loop && currentLoopCount > state.LastLoopCount)
            {
                state.LastLoopCount = currentLoopCount;
                for (int i = 0; i < state.TriggerFlags.Length; i++)
                {
                    state.TriggerFlags[i] = false;
                }
            }

            for (int i = 0; i < events.Length; i++)
            {
                if (!state.TriggerFlags[i] && currentNormalizedTime >= events[i].triggerTime)
                {
                    state.TriggerFlags[i] = true;
                    state.Handler.OnAnimationEventTriggered(events[i].eventName);
                }
            }
        }
    }
}