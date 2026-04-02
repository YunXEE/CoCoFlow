using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    [RequireComponent(typeof(Animator))]
    public class AnimationHandler : MonoBehaviour
    {
        private Animator _animator;

        public event Action<string> OnAnimStateEnter;
        public event Action<string> OnAnimStateExit;
        
        // 用于分发具体帧事件的委托
        public event Action<string> OnSpecificFrameEvent;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        #region 基础动画播放控制

        public void PlayAnimation(string stateName, int layer = 0)
        {
            _animator.Play(stateName, layer);
        }

        public void CrossFadeAnimation(string stateName, float transitionDuration = 0.1f, int layer = 0)
        {
            _animator.CrossFadeInFixedTime(stateName, transitionDuration, layer);
        }

        #endregion

        #region 高级合成控制 (BlendTree & Layers)

        public void SetFloat(string paramName, float value) => _animator.SetFloat(paramName, value);
        
        public void SetBool(string paramName, bool value) => _animator.SetBool(paramName, value);

        public void SetLayerWeight(int layerIndex, float weight) => _animator.SetLayerWeight(layerIndex, weight);

        public void SetSpeed(float speed) => _animator.speed = speed;

        #endregion

        #region SMB 事件接收器

        public void OnAnimationStateEnter(string stateName) => OnAnimStateEnter?.Invoke(stateName);

        public void OnAnimationStateExit(string stateName) => OnAnimStateExit?.Invoke(stateName);

        // 广播事件
        public void OnAnimationEventTriggered(string eventName)
        {
            OnSpecificFrameEvent?.Invoke(eventName);
            // Debug.Log($"[AnimationHandler] Triggered Specific Frame Event: {eventName}");
        }

        #endregion
    }
}