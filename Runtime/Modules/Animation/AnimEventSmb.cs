using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    /// <summary>
    /// 动画事件配置数据结构
    /// </summary>
    [Serializable]
    public class AnimEventConfig
    {
        [Tooltip("Event Name")]
        public string eventName;

        [Range(0f, 1f)]
        [Tooltip("Trigger Percent Of Animation")]
        public float triggerTime;
    }

    /// <summary>
    /// Animator SMB 行为扩展，用于精确触发自定义动画事件
    /// 解决原生 Animation Event 在过渡或循环时可能存在的失效问题
    /// </summary>
    public class AnimEventSmb : StateMachineBehaviour
    {
        /// <summary>
        /// 动画事件列表
        /// </summary>
        public AnimEventConfig[] events;

        private class InstanceState
        {
            public AnimHandler Handler;
            public bool[] TriggerFlags;
            public int LastLoopCount;
        }

        // 弱引用表：为每个使用该 SMB 的 Animator 实例独立存储状态
        // 理由：SMB 是资源级的（Asset），会被多个场景对象共享，不能直接存储实例变量
        private readonly ConditionalWeakTable<Animator, InstanceState> _instanceStates =
            new ConditionalWeakTable<Animator, InstanceState>();

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 首次进入时进行状态绑定
            if (!_instanceStates.TryGetValue(animator, out var state))
            {
                state = new InstanceState
                {
                    Handler = animator.GetComponent<AnimHandler>(),
                    TriggerFlags = new bool[events.Length]
                };
                _instanceStates.Add(animator, state); // 添加到弱引用表
            }

            // 初始化循环计数与触发标志
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

            #region Internal Logic
            // 处理循环逻辑：跨周目时重置所有事件的触发状态
            if (stateInfo.loop && currentLoopCount > state.LastLoopCount)
            {
                state.LastLoopCount = currentLoopCount;
                for (int i = 0; i < state.TriggerFlags.Length; i++)
                {
                    state.TriggerFlags[i] = false;
                }
            }

            // 进度检测触发
            for (int i = 0; i < events.Length; i++)
            {
                if (!state.TriggerFlags[i] && currentNormalizedTime >= events[i].triggerTime)
                {
                    state.TriggerFlags[i] = true;
                    state.Handler.OnAnimationEventTriggered(events[i].eventName);
                }
            }
            #endregion
        }
    }
}
