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

        [Tooltip("是否在 Unity 生命周期的 Update/FixedUpdate 中自动更新。对于子状态机，通常设为 false。")]
        [SerializeField] private bool autoUpdate = true;

        [Tooltip("预注册的状态列表。若为空，则会自动获取子物体下的所有状态组件")]
        [SerializeField] private List<CoCoStateMachineBase> availableStates = new List<CoCoStateMachineBase>();

        [Header("Context (Optional)")]
        [Tooltip("可选 Context Provider。若为空，会在当前 GameObject 上自动查找匹配的 Provider。")]
        [SerializeField] private MonoBehaviour contextProvider;

        private readonly Dictionary<Type, CoCoStateMachineBase> _states = new Dictionary<Type, CoCoStateMachineBase>();
        private ICoCoContext _context;

        #region Public API

        public CoCoStateMachineBase CurrentCoCoState { get; private set; }
        public Type CurrentStateType { get; private set; }
        public ICoCoContext Context => ResolveContext();

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

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _context = null;
            ResolveContext();
        }

        /// <summary>
        /// 进入状态机，启动默认状态。
        /// </summary>
        public void EnterStateMachine()
        {
            BeforeStateMachineTick(Context);

            var nextStateType = EvaluateStateType(Context);
            if (nextStateType != null)
            {
                ChangeState(nextStateType);
                return;
            }

            if (defaultCoCoState != null)
            {
                ChangeState(defaultCoCoState.GetType());
            }
        }

        /// <summary>
        /// 退出状态机，清理当前状态。
        /// </summary>
        public void ExitStateMachine()
        {
            if (CurrentCoCoState != null)
            {
                InvokeStateExit(CurrentCoCoState);
            }

            CurrentCoCoState = null;
            CurrentStateType = null;
        }

        /// <summary>
        /// 手动更新状态机逻辑。
        /// </summary>
        public void UpdateStateMachine()
        {
            BeforeStateMachineTick(Context);
            TryApplyEvaluatedTransition();

            if (CurrentCoCoState != null)
            {
                InvokeStateUpdate(CurrentCoCoState);
            }
        }

        /// <summary>
        /// 手动更新状态机物理逻辑。
        /// </summary>
        public void FixedUpdateStateMachine()
        {
            BeforeStateMachineTick(Context);
            TryApplyEvaluatedTransition();

            if (CurrentCoCoState != null)
            {
                InvokeStateFixedUpdate(CurrentCoCoState);
            }
        }

        #endregion

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            ResolveContext();

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
                var attachedStates = GetComponentsInChildren<CoCoStateMachineBase>(true);
                foreach (var state in attachedStates)
                {
                    if (state == null) continue;

                    var nearestController = state.GetComponentInParent<CoCoStateMachineController>(true);
                    if (nearestController != null && nearestController.gameObject == state.gameObject && nearestController != this)
                    {
                        nearestController = state.transform.parent != null
                            ? state.transform.parent.GetComponentInParent<CoCoStateMachineController>(true)
                            : null;
                    }

                    if (nearestController == this)
                    {
                        RegisterState(state);
                    }
                }
            }
        }

        protected virtual void Start()
        {
            if (autoUpdate)
            {
                EnterStateMachine();
                if (defaultCoCoState == null && CurrentCoCoState == null)
                {
                    CoCoLog.Error($"致命错误：{gameObject.name} 未挂载/指定初始状态组件！");
                }
            }
        }

        protected virtual void Update()
        {
            if (autoUpdate) UpdateStateMachine();
        }

        protected virtual void FixedUpdate()
        {
            if (autoUpdate) FixedUpdateStateMachine();
        }

        #endregion

        #region Protected API

        // Override this to refresh Context from input, AI, timeline, or network before transition evaluation.
        protected virtual void BeforeStateMachineTick(ICoCoContext context) { }

        // Return null to keep the current state.
        protected virtual Type EvaluateStateType(ICoCoContext context)
        {
            return null;
        }

        #endregion

        #region Internal Logic

        private void ChangeState(Type newStateType)
        {
            if (CurrentStateType == newStateType) return;

            if (_states.TryGetValue(newStateType, out var newState))
            {
                if (CurrentCoCoState != null)
                {
                    InvokeStateExit(CurrentCoCoState);
                }

                CurrentCoCoState = newState;
                CurrentStateType = newStateType;
                InvokeStateEnter(CurrentCoCoState);
            }
            else
            {
                CoCoLog.Warning($"{gameObject.name} 尝试切换到未注册的状态: {newStateType.Name}。已忽略。");
            }
        }

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
                CoCoLog.Warning($"状态 {stateType.Name} 重复注册，已忽略。");
            }
        }

        private void TryApplyEvaluatedTransition()
        {
            var nextStateType = EvaluateStateType(Context);
            if (nextStateType != null && nextStateType != CurrentStateType)
            {
                ChangeState(nextStateType);
            }
        }

        private void InvokeStateEnter(CoCoStateMachineBase state)
        {
            var context = Context;
            if (context != null) state.Enter(context);
            else state.Enter();
        }

        private void InvokeStateUpdate(CoCoStateMachineBase state)
        {
            var context = Context;
            if (context != null) state.OnStateUpdate(context);
            else state.OnStateUpdate();
        }

        private void InvokeStateFixedUpdate(CoCoStateMachineBase state)
        {
            var context = Context;
            if (context != null) state.OnStateFixedUpdate(context);
            else state.OnStateFixedUpdate();
        }

        private void InvokeStateExit(CoCoStateMachineBase state)
        {
            var context = Context;
            if (context != null) state.Exit(context);
            else state.Exit();
        }

        private ICoCoContext ResolveContext()
        {
            if (_context != null) return _context;

            if (TryGetContextFromProvider(contextProvider, out _context))
            {
                return _context;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _context))
                {
                    if (contextProvider == null)
                    {
                        contextProvider = behaviour;
                    }
                    return _context;
                }
            }

            return null;
        }

        private static bool TryGetContextFromProvider(
            object provider,
            out ICoCoContext context)
        {
            if (provider is ICoCoContextProvider<ICoCoContext> typedProvider)
            {
                context = typedProvider.Context;
                return context != null;
            }

            context = null;
            return false;
        }

        #endregion
    }
}
