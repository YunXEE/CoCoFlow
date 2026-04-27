using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.Core
{
    public class CoCoStateMachineController : MonoBehaviour
    {
        [FormerlySerializedAs("defaultState")]
        [Header("State Machine Settings")]
        [Tooltip("默认启动状态")]
        [SerializeField] private CoCoStateMachineBase defaultCoCoState;

        [Tooltip("预注册的状态列表。若为空，则会自动获取子物体下的所有状态组件")]
        [SerializeField] private List<CoCoStateMachineBase> availableStates = new List<CoCoStateMachineBase>();

        private readonly Dictionary<Type, CoCoStateMachineBase> _states = new Dictionary<Type, CoCoStateMachineBase>();

        /// <summary>
        /// 当前激活的状态实例。
        /// </summary>
        public CoCoStateMachineBase CurrentCoCoState { get; private set; }

        /// <summary>
        /// 当前激活的状态类型。
        /// </summary>
        public Type CurrentStateType { get; private set; }

        private void Awake()
        {
            // 初始化状态机，优先使用手动指定的列表，否则自动扫描子物体
            if (availableStates.Count > 0)
            {
                foreach (var state in availableStates)
                {
                    if (state == null) continue;
                    RegisterState(state);
                }
            }
            else
            {
                var attachedStates = GetComponentsInChildren<CoCoStateMachineBase>();
                foreach (var state in attachedStates)
                {
                    RegisterState(state);
                }
            }
        }

        private void Start()
        {
            if (defaultCoCoState != null)
            {
                ChangeState(defaultCoCoState.GetType());
            }
            else
            {
                Debug.LogError($"[CoCoFlow] 致命错误：{gameObject.name} 未挂载/指定初始状态组件！");
            }
        }

        private void Update() { CurrentCoCoState?.OnStateUpdate(); }

        private void FixedUpdate() { CurrentCoCoState?.OnStateFixedUpdate(); }

        #region Public API

        /// <summary>
        /// 切换到指定类型的状态。状态必须已在 _states 中注册。
        /// </summary>
        public void ChangeState<T>() where T : CoCoStateMachineBase
        {
            ChangeState(typeof(T));
        }

        /// <summary>
        /// 检查是否已注册指定类型的状态。
        /// </summary>
        public bool IfHasState<T>() where T : CoCoStateMachineBase
        {
            return _states.ContainsKey(typeof(T));
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// 注册状态实例到状态机字典中。
        /// </summary>
        private void RegisterState(CoCoStateMachineBase coCoState)
        {
            var stateType = coCoState.GetType();
            if (!_states.ContainsKey(stateType))
            {
                coCoState.Init(this);
                _states.Add(stateType, coCoState);
            }
            else
            {
                Debug.LogWarning($"[CoCoFlow] 状态 {stateType.Name} 重复注册，已忽略。");
            }
        }

        /// <summary>
        /// 内部执行状态切换的具体逻辑。
        /// </summary>
        private void ChangeState(Type newStateType)
        {
            if (CurrentStateType == newStateType) return;

            if (_states.TryGetValue(newStateType, out var newState))
            {
                CurrentCoCoState?.Exit();
                CurrentCoCoState = newState;
                CurrentStateType = newStateType;
                CurrentCoCoState.Enter();
            }
            else
            {
                Debug.LogWarning($"[CoCoFlow] {gameObject.name} 尝试切换到未注册的状态: {newStateType.Name}。已忽略。");
            }
        }

        #endregion
    }
}
