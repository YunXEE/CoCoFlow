using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public class StateMachineController : MonoBehaviour
    {
        [Header("State Machine Settings")]
        [Tooltip("Default State")]
        [SerializeField] private StateMachineBase defaultState;

        [Tooltip("Available States")]
        [SerializeField] private List<StateMachineBase> availableStates = new List<StateMachineBase>();
        
        private readonly Dictionary<Type, StateMachineBase> _states = new Dictionary<Type, StateMachineBase>();

        public StateMachineBase CurrentState { get; private set; }
        public Type CurrentStateType { get; private set; } 

        private void Awake()
        {
            // 如果指定了具体列表，那么用具体列表
            if (availableStates.Count > 0)
            {
                foreach (var state in availableStates)
                {
                    if (state == null) continue;
                    RegisterState(state);
                }
            }
            //如果没有指定，那么自动获取
            else
            {
                var attachedStates = GetComponentsInChildren<StateMachineBase>();
                foreach (var state in attachedStates)
                {
                    RegisterState(state);
                }
            }
        }

        private void RegisterState(StateMachineBase state)
        {
            var stateType = state.GetType();
            if (!_states.ContainsKey(stateType))
            {
                state.Init(this);
                _states.Add(stateType, state);
            }
            else
            {
                Debug.LogWarning($"[CoCoFrame] 状态 {stateType.Name} 重复注册，已忽略。");
            }
        }

        private void Start()
        {
            if (defaultState != null)
            {
                ChangeState(defaultState.GetType());
            }
            else
            {
                Debug.LogError($"[CoCoFrame] 致命错误：{gameObject.name} 未挂载/指定初始状态组件！");
            }
        }
        
        public void ChangeState<T>() where T : StateMachineBase
        {
            ChangeState(typeof(T));
        }
        
        public bool IfHasState<T>() where T : StateMachineBase
        {
            return _states.ContainsKey(typeof(T));
        }
        

        // 内部实际执行切换的逻辑
        private void ChangeState(Type newStateType)
        {
            if (CurrentStateType == newStateType) return;

            if (_states.TryGetValue(newStateType, out var newState))
            {
                CurrentState?.Exit();
                CurrentState = newState;
                CurrentStateType = newStateType;
                CurrentState.Enter();
            }
            else
            {
                Debug.LogWarning($"[CoCoFrame] {gameObject.name} 尝试切换到未注册的状态: {newStateType.Name}。已忽略。");
            }
        }

        private void Update() { CurrentState?.OnStateUpdate(); }
        private void FixedUpdate() { CurrentState?.OnStateFixedUpdate(); }
    }
}