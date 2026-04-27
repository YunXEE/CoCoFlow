using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    /// <summary>
    /// 动画处理器组件，负责封装 Animator 的操作并作为 SMB 事件的中转站
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimHandler : MonoBehaviour
    {
        private Animator _animator;

        /// <summary>
        /// 当动画状态进入时触发
        /// </summary>
        public event Action<string> OnAnimStateEnter;

        /// <summary>
        /// 当动画状态退出时触发
        /// </summary>
        public event Action<string> OnAnimStateExit;

        /// <summary>
        /// 用于分发具体帧事件的委托
        /// </summary>
        public event Action<string> OnSpecificFrameEvent;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        #region Public API

        /// <summary>
        /// 播放指定动画状态
        /// </summary>
        public void PlayAnimation(string stateName, int layer = 0)
        {
            _animator.Play(stateName, layer);
        }

        /// <summary>
        /// 平滑过渡到指定动画状态
        /// </summary>
        public void CrossFadeAnimation(string stateName, float transitionDuration = 0.1f, int layer = 0)
        {
            _animator.CrossFadeInFixedTime(stateName, transitionDuration, layer);
        }

        public void SetFloat(string paramName, float value) => _animator.SetFloat(paramName, value);

        public void SetBool(string paramName, bool value) => _animator.SetBool(paramName, value);

        public void SetLayerWeight(int layerIndex, float weight) => _animator.SetLayerWeight(layerIndex, weight);

        public void SetSpeed(float speed) => _animator.speed = speed;

        /// <summary>
        /// 由 SMB (AnimEventSmb) 调用，触发状态进入通知
        /// </summary>
        public void OnAnimationStateEnter(string stateName) => OnAnimStateEnter?.Invoke(stateName);

        /// <summary>
        /// 由 SMB (AnimEventSmb) 调用，触发状态退出通知
        /// </summary>
        public void OnAnimationStateExit(string stateName) => OnAnimStateExit?.Invoke(stateName);

        /// <summary>
        /// 接收并广播特定帧的动画事件
        /// </summary>
        public void OnAnimationEventTriggered(string eventName)
        {
            OnSpecificFrameEvent?.Invoke(eventName);
        }

        #endregion
    }
}


