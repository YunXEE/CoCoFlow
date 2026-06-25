using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    [Serializable]
    public class CoCoStateLayer
    {
        [SerializeField] private string name = "State Layer";
        [SerializeField] private int order;
        [SerializeField] private bool update = true;
        [SerializeField] private bool fixedUpdate = true;
        [SerializeField] private CoCoStateBase defaultCoCoState;
        [SerializeField] private List<CoCoStateBase> availableStates = new List<CoCoStateBase>();
        [SerializeField] private List<CoCoStateChildMachine> childMachines =
            new List<CoCoStateChildMachine>();

        public CoCoStateLayer() { }

        public CoCoStateLayer(
            string name,
            CoCoStateBase defaultCoCoState,
            IEnumerable<CoCoStateBase> availableStates,
            int order = 0,
            bool update = true,
            bool fixedUpdate = true,
            IEnumerable<CoCoStateChildMachine> childMachines = null)
        {
            this.name = name;
            this.defaultCoCoState = defaultCoCoState;
            this.order = order;
            this.update = update;
            this.fixedUpdate = fixedUpdate;
            SetAvailableStates(availableStates);
            SetChildMachines(childMachines);
        }

        public string Name => string.IsNullOrWhiteSpace(name) ? "State Layer" : name;
        public int Order => order;
        public bool Update => update;
        public bool FixedUpdate => fixedUpdate;
        public CoCoStateBase DefaultCoCoState => defaultCoCoState;
        public IReadOnlyList<CoCoStateBase> AvailableStates => availableStates;
        public IReadOnlyList<CoCoStateChildMachine> ChildMachines => childMachines;

        public void SetAvailableStates(IEnumerable<CoCoStateBase> states)
        {
            availableStates.Clear();
            if (states == null) return;

            foreach (var state in states)
            {
                availableStates.Add(state);
            }
        }

        public void SetChildMachines(IEnumerable<CoCoStateChildMachine> machines)
        {
            childMachines.Clear();
            if (machines == null) return;

            foreach (var machine in machines)
            {
                childMachines.Add(machine);
            }
        }
    }

    [Serializable]
    public class CoCoStateChildMachine
    {
        [SerializeField] private CoCoStateBase parentCoCoState;
        [SerializeField] private CoCoStateBase defaultCoCoState;
        [SerializeField] private List<CoCoStateBase> availableStates = new List<CoCoStateBase>();

        public CoCoStateChildMachine() { }

        public CoCoStateChildMachine(
            CoCoStateBase parentCoCoState,
            CoCoStateBase defaultCoCoState,
            IEnumerable<CoCoStateBase> availableStates)
        {
            this.parentCoCoState = parentCoCoState;
            this.defaultCoCoState = defaultCoCoState;
            SetAvailableStates(availableStates);
        }

        public CoCoStateBase ParentCoCoState => parentCoCoState;
        public CoCoStateBase DefaultCoCoState => defaultCoCoState;
        public IReadOnlyList<CoCoStateBase> AvailableStates => availableStates;

        public void SetAvailableStates(IEnumerable<CoCoStateBase> states)
        {
            availableStates.Clear();
            if (states == null) return;

            foreach (var state in states)
            {
                availableStates.Add(state);
            }
        }
    }

    public class CoCoStateController : MonoBehaviour
    {
        [Tooltip("Whether this controller updates itself from Unity Update/FixedUpdate.")]
        [SerializeField] private bool autoUpdate = true;

        [Header("State Layers")]
        [Tooltip("Explicit state layers managed by this single controller. Create a Main layer explicitly when needed.")]
        [SerializeField] private List<CoCoStateLayer> stateLayers = new List<CoCoStateLayer>();

        [Header("Context (Optional)")]
        [Tooltip("Optional Context Provider. If empty, the controller searches the current GameObject.")]
        [CoCoContextProvider]
        [SerializeField] private MonoBehaviour contextProvider;

        private readonly Dictionary<CoCoStateBase, StateLayerRuntime> _layerByState =
            new Dictionary<CoCoStateBase, StateLayerRuntime>();
        private readonly Dictionary<CoCoStateBase, StateMachineRuntime> _machineByState =
            new Dictionary<CoCoStateBase, StateMachineRuntime>();
        private readonly Dictionary<Type, List<StateMachineRuntime>> _machinesByStateType =
            new Dictionary<Type, List<StateMachineRuntime>>();
        private readonly List<StateLayerRuntime> _orderedTopLevelLayers =
            new List<StateLayerRuntime>();
        private readonly HashSet<CoCoStateBase> _initializedStates =
            new HashSet<CoCoStateBase>();

        private ICoCoContext _context;
        private ICoCoContext _contextOverride;
        private bool _layersRegistered;

        #region Public API

        public ICoCoContext Context => _contextOverride ?? ResolveContext();
        public IReadOnlyList<CoCoStateLayer> StateLayers => stateLayers;
        public MonoBehaviour ContextProvider => contextProvider;

        /// <summary>
        /// Switches the layer that contains the requested state type.
        /// </summary>
        public void ChangeState<T>() where T : CoCoStateBase
        {
            ChangeState(typeof(T), null, Context);
        }

        public void ChangeState<T>(CoCoStateLayer layer) where T : CoCoStateBase
        {
            ChangeState(layer, typeof(T));
        }

        public void ChangeState(CoCoStateLayer layer, Type newStateType)
        {
            ChangeStateInLayer(layer, newStateType, Context);
        }

        /// <summary>
        /// Checks whether the requested state type resolves to one unambiguous layer.
        /// </summary>
        public bool IfHasState<T>() where T : CoCoStateBase
        {
            RegisterDeclaredLayers();
            return ResolveTargetMachine(typeof(T), null, null, out _) == StateResolveResult.Found;
        }

        public bool IfHasState<T>(CoCoStateLayer layer) where T : CoCoStateBase
        {
            RegisterDeclaredLayers();
            var runtime = FindLayerRuntime(layer);
            return runtime != null && runtime.ContainsState(typeof(T));
        }

        public CoCoStateBase GetCurrentState(CoCoStateLayer layer)
        {
            RegisterDeclaredLayers();
            var runtime = FindLayerRuntime(layer);
            return runtime?.RootMachine.CurrentState;
        }

        public Type GetCurrentStateType(CoCoStateLayer layer)
        {
            RegisterDeclaredLayers();
            var runtime = FindLayerRuntime(layer);
            return runtime?.RootMachine.CurrentStateType;
        }

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _context = null;
            ResolveContext();
        }

        public void SetStateLayers(IEnumerable<CoCoStateLayer> layers)
        {
            stateLayers.Clear();
            if (layers != null)
            {
                foreach (var layer in layers)
                {
                    stateLayers.Add(layer);
                }
            }

            ResetLayerRegistration();
            RegisterDeclaredLayers();
        }

        public void EnterState()
        {
            EnterState(null);
        }

        public void EnterState(ICoCoContext contextOverride)
        {
            var previousOverride = PushContextOverride(contextOverride);
            try
            {
                RegisterDeclaredLayers();

                var context = PrepareContextForTick();
                BeforeStateTick(context);

                var globalEvaluatedType = EvaluateStateType(context);
                foreach (var layer in _orderedTopLevelLayers)
                {
                    layer.HasExited = false;
                    var evaluatedType = ResolveEvaluatedStateType(layer, globalEvaluatedType, context);
                    var preferredType = layer.RootMachine.ContainsState(evaluatedType) ? evaluatedType : null;
                    EnterLayer(layer, context, preferredType);
                }
            }
            finally
            {
                RestoreContextOverride(previousOverride);
            }
        }

        public void ExitState()
        {
            ExitState(null);
        }

        public void ExitState(ICoCoContext contextOverride)
        {
            var previousOverride = PushContextOverride(contextOverride);
            try
            {
                RegisterDeclaredLayers();

                var context = Context;
                for (int i = _orderedTopLevelLayers.Count - 1; i >= 0; i--)
                {
                    ExitLayer(_orderedTopLevelLayers[i], context);
                }
            }
            finally
            {
                RestoreContextOverride(previousOverride);
            }
        }

        public void UpdateState()
        {
            UpdateState(null);
        }

        public void UpdateState(ICoCoContext contextOverride)
        {
            var previousOverride = PushContextOverride(contextOverride);
            try
            {
                RegisterDeclaredLayers();

                var context = PrepareContextForTick();
                BeforeStateTick(context);
                TryApplyEvaluatedTransition(context, false);

                foreach (var layer in _orderedTopLevelLayers)
                {
                    if (!ShouldTickLayer(layer, false)) continue;
                    UpdateLayer(layer, context, false);
                }
            }
            finally
            {
                RestoreContextOverride(previousOverride);
            }
        }

        public void FixedUpdateState()
        {
            FixedUpdateState(null);
        }

        public void FixedUpdateState(ICoCoContext contextOverride)
        {
            var previousOverride = PushContextOverride(contextOverride);
            try
            {
                RegisterDeclaredLayers();

                var context = PrepareContextForTick();
                BeforeStateTick(context);
                TryApplyEvaluatedTransition(context, true);

                foreach (var layer in _orderedTopLevelLayers)
                {
                    if (!ShouldTickLayer(layer, true)) continue;
                    UpdateLayer(layer, context, true);
                }
            }
            finally
            {
                RestoreContextOverride(previousOverride);
            }
        }

        #endregion

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            ResolveContext();
            RegisterDeclaredLayers();
        }

        protected virtual void Start()
        {
            if (autoUpdate)
            {
                EnterState();
                if (_orderedTopLevelLayers.Count == 0)
                {
                    CoCoLog.Error($"致命错误：{gameObject.name} 未声明任何 State Layer。请显式创建 Main Layer。");
                }
                else if (!HasAnyCurrentState())
                {
                    CoCoLog.Error($"致命错误：{gameObject.name} 未挂载/指定初始状态组件！");
                }
            }
        }

        protected virtual void Update()
        {
            if (autoUpdate) UpdateState();
        }

        protected virtual void FixedUpdate()
        {
            if (autoUpdate) FixedUpdateState();
        }

        #endregion

        #region Protected API

        // Override this to refresh Context from input, AI, timeline, or network before transition evaluation.
        protected virtual void BeforeStateTick(ICoCoContext context) { }

        // Legacy/global fallback. Return null to keep every layer's current state.
        protected virtual Type EvaluateStateType(ICoCoContext context)
        {
            return null;
        }

        // Override this when each State Layer needs an independent decision.
        protected virtual Type EvaluateStateType(CoCoStateLayer layer, ICoCoContext context)
        {
            return null;
        }

        #endregion

        #region Internal API

        internal void ChangeStateFrom(CoCoStateBase sourceState, Type newStateType)
        {
            ChangeState(newStateType, sourceState, Context);
        }

        internal bool IfHasStateFrom(CoCoStateBase sourceState, Type stateType)
        {
            RegisterDeclaredLayers();
            return ResolveTargetMachine(stateType, sourceState, null, out _) == StateResolveResult.Found;
        }

        #endregion

        #region Internal Logic

        private void ChangeState(
            Type newStateType,
            CoCoStateBase sourceState,
            ICoCoContext context)
        {
            if (newStateType == null) return;
            RegisterDeclaredLayers();

            var result = ResolveTargetMachine(newStateType, sourceState, null, out var machine);
            if (result == StateResolveResult.Missing)
            {
                CoCoLog.Warning($"{gameObject.name} 尝试切换到未注册的状态: {newStateType.Name}。已忽略。");
                return;
            }

            if (result == StateResolveResult.Ambiguous)
            {
                CoCoLog.Warning(
                    $"{gameObject.name} 尝试切换到存在于多个 State Machine 的状态: {newStateType.Name}。请指定目标 State Layer 或从当前状态发起切换。");
                return;
            }

            ChangeMachineState(machine, newStateType, context);
        }

        private void ChangeStateInLayer(
            CoCoStateLayer layer,
            Type newStateType,
            ICoCoContext context)
        {
            if (newStateType == null) return;
            RegisterDeclaredLayers();

            var runtime = FindLayerRuntime(layer);
            if (runtime == null)
            {
                CoCoLog.Warning($"{gameObject.name} 尝试切换未注册 State Layer 中的状态: {newStateType.Name}。已忽略。");
                return;
            }

            var result = ResolveTargetMachine(newStateType, null, runtime, out var machine);
            if (result == StateResolveResult.Missing)
            {
                CoCoLog.Warning($"{gameObject.name} 的 State Layer {runtime.Name} 未注册可切换状态: {newStateType.Name}。已忽略。");
                return;
            }

            if (result == StateResolveResult.Ambiguous)
            {
                CoCoLog.Warning(
                    $"{gameObject.name} 的 State Layer {runtime.Name} 存在多个可切换状态: {newStateType.Name}。请从当前状态发起切换。");
                return;
            }

            ChangeMachineState(machine, newStateType, context);
        }

        private void RegisterDeclaredLayers()
        {
            if (_layersRegistered) return;

            _orderedTopLevelLayers.Clear();
            for (int i = 0; i < stateLayers.Count; i++)
            {
                var runtime = BuildLayerRuntime(stateLayers[i], i);
                if (runtime == null) continue;
                _orderedTopLevelLayers.Add(runtime);
            }

            _orderedTopLevelLayers.Sort(CompareLayerOrder);
            _layersRegistered = true;
        }

        private StateLayerRuntime BuildLayerRuntime(
            CoCoStateLayer layer,
            int declarationIndex)
        {
            if (layer == null) return null;

            var runtime = new StateLayerRuntime(
                layer.Name,
                layer.Order,
                layer.Update,
                layer.FixedUpdate,
                layer.DefaultCoCoState,
                layer.AvailableStates,
                layer.ChildMachines,
                declarationIndex,
                layer);
            RegisterLayer(runtime);
            return runtime;
        }

        private void RegisterLayer(StateLayerRuntime layer)
        {
            RegisterMachineStates(layer.RootMachine);
            foreach (var machine in layer.ChildMachines)
            {
                RegisterMachineStates(machine);
            }

            LinkChildMachines(layer);
            ValidateMachine(layer.RootMachine);
            foreach (var machine in layer.ChildMachines)
            {
                ValidateMachine(machine);
            }
        }

        private void RegisterMachineStates(StateMachineRuntime machine)
        {
            foreach (var state in machine.AvailableStates)
            {
                if (state == null) continue;
                RegisterState(machine, state);
            }
        }

        private void RegisterState(StateMachineRuntime machine, CoCoStateBase state)
        {
            if (_layerByState.ContainsKey(state))
            {
                CoCoLog.Warning($"状态 {state.GetType().Name} 被多个 State Machine 复用，后续声明已忽略。");
                return;
            }

            var stateType = state.GetType();
            if (machine.States.ContainsKey(stateType))
            {
                CoCoLog.Warning($"State Machine {machine.Name} 中状态 {stateType.Name} 重复注册，已忽略。");
                return;
            }

            if (_initializedStates.Add(state))
            {
                state.Init(this);
            }

            machine.States.Add(stateType, state);
            _layerByState[state] = machine.Layer;
            _machineByState[state] = machine;
            if (!_machinesByStateType.TryGetValue(stateType, out var machines))
            {
                machines = new List<StateMachineRuntime>();
                _machinesByStateType[stateType] = machines;
            }
            machines.Add(machine);
        }

        private void LinkChildMachines(StateLayerRuntime layer)
        {
            foreach (var machine in layer.ChildMachines)
            {
                var parentState = machine.ParentState;
                if (parentState == null)
                {
                    CoCoLog.Warning($"State Layer {layer.Name} 中存在缺少父状态的子状态机，已忽略。");
                    continue;
                }

                if (!_machineByState.TryGetValue(parentState, out var parentMachine) ||
                    parentMachine.Layer != layer)
                {
                    CoCoLog.Warning($"State Layer {layer.Name} 的子状态机父状态未注册: {parentState.GetType().Name}。已忽略。");
                    continue;
                }

                if (layer.ChildMachineByParentState.ContainsKey(parentState))
                {
                    CoCoLog.Warning($"状态 {parentState.GetType().Name} 声明了多个子状态机，后续声明已忽略。");
                    continue;
                }

                machine.ParentMachine = parentMachine;
                machine.Depth = parentMachine.Depth + 1;
                layer.ChildMachineByParentState.Add(parentState, machine);
            }
        }

        private void ValidateMachine(StateMachineRuntime machine)
        {
            if (machine.DefaultState != null &&
                machine.AvailableStates.Count > 0 &&
                !machine.AvailableStates.Contains(machine.DefaultState))
            {
                CoCoLog.Warning($"{gameObject.name} 的 State Machine {machine.Name} 默认状态未包含在显式状态列表中。");
            }
        }

        private void ResetLayerRegistration()
        {
            _layerByState.Clear();
            _machineByState.Clear();
            _machinesByStateType.Clear();
            _orderedTopLevelLayers.Clear();
            _initializedStates.Clear();
            _layersRegistered = false;
        }

        private StateResolveResult ResolveTargetMachine(
            Type targetStateType,
            CoCoStateBase sourceState,
            StateLayerRuntime explicitLayer,
            out StateMachineRuntime machine)
        {
            machine = null;
            if (targetStateType == null) return StateResolveResult.Missing;

            if (sourceState != null)
            {
                if (!_machineByState.TryGetValue(sourceState, out var sourceMachine))
                {
                    return StateResolveResult.Missing;
                }

                if (sourceMachine.ContainsState(targetStateType))
                {
                    machine = sourceMachine;
                    return StateResolveResult.Found;
                }

                if (sourceMachine.Layer.ChildMachineByParentState.TryGetValue(sourceState, out var childMachine) &&
                    childMachine.ContainsState(targetStateType) &&
                    IsMachinePathActive(childMachine))
                {
                    machine = childMachine;
                    return StateResolveResult.Found;
                }

                if (sourceMachine.Layer.RootMachine.ContainsState(targetStateType))
                {
                    machine = sourceMachine.Layer.RootMachine;
                    return StateResolveResult.Found;
                }

                return StateResolveResult.Missing;
            }

            if (explicitLayer != null)
            {
                return ResolveTargetMachineInLayer(explicitLayer, targetStateType, out machine);
            }

            if (!_machinesByStateType.TryGetValue(targetStateType, out var machines) || machines.Count == 0)
            {
                return StateResolveResult.Missing;
            }

            StateMachineRuntime resolved = null;
            foreach (var candidate in machines)
            {
                if (!CanChangeMachine(candidate)) continue;

                if (resolved != null && !ReferenceEquals(resolved, candidate))
                {
                    return StateResolveResult.Ambiguous;
                }

                resolved = candidate;
            }

            if (resolved == null)
            {
                return StateResolveResult.Missing;
            }

            machine = resolved;
            return StateResolveResult.Found;
        }

        private StateResolveResult ResolveTargetMachineInLayer(
            StateLayerRuntime layer,
            Type targetStateType,
            out StateMachineRuntime machine)
        {
            machine = null;
            if (layer.RootMachine.ContainsState(targetStateType))
            {
                machine = layer.RootMachine;
                return StateResolveResult.Found;
            }

            StateMachineRuntime resolved = null;
            foreach (var candidate in layer.ChildMachines)
            {
                if (!candidate.ContainsState(targetStateType)) continue;
                if (!CanChangeMachine(candidate)) continue;

                if (resolved != null && !ReferenceEquals(resolved, candidate))
                {
                    return StateResolveResult.Ambiguous;
                }

                resolved = candidate;
            }

            if (resolved == null)
            {
                return StateResolveResult.Missing;
            }

            machine = resolved;
            return StateResolveResult.Found;
        }

        private StateLayerRuntime FindLayerRuntime(CoCoStateLayer layer)
        {
            if (layer == null) return null;

            foreach (var runtime in _orderedTopLevelLayers)
            {
                if (ReferenceEquals(runtime.SourceLayer, layer)) return runtime;
            }

            return null;
        }

        private void EnterLayer(
            StateLayerRuntime layer,
            ICoCoContext context,
            Type preferredType)
        {
            if (layer.HasExited) return;

            EnterMachine(layer.RootMachine, context, preferredType);
        }

        private void UpdateLayer(
            StateLayerRuntime layer,
            ICoCoContext context,
            bool fixedUpdate)
        {
            if (layer.HasExited) return;

            if (layer.RootMachine.CurrentState == null)
            {
                EnterLayer(layer, context, null);
            }

            UpdateMachine(layer.RootMachine, context, fixedUpdate);
        }

        private void ExitLayer(StateLayerRuntime layer, ICoCoContext context)
        {
            ExitMachine(layer.RootMachine, context);
            layer.HasExited = true;
        }

        private void EnterMachine(
            StateMachineRuntime machine,
            ICoCoContext context,
            Type preferredType)
        {
            if (machine.CurrentState != null) return;

            Type initialType = preferredType ?? machine.DefaultState?.GetType();
            if (initialType == null) return;

            ChangeMachineState(machine, initialType, context);
        }

        private void UpdateMachine(
            StateMachineRuntime machine,
            ICoCoContext context,
            bool fixedUpdate)
        {
            if (machine.HasExited) return;

            if (machine.CurrentState == null)
            {
                EnterMachine(machine, context, null);
            }

            var currentState = machine.CurrentState;
            if (currentState == null) return;

            if (fixedUpdate) InvokeStateFixedUpdate(currentState, context);
            else InvokeStateUpdate(currentState, context);

            if (!ReferenceEquals(machine.CurrentState, currentState)) return;
            if (!machine.Layer.ChildMachineByParentState.TryGetValue(currentState, out var childMachine)) return;

            if (childMachine.CurrentState == null)
            {
                EnterMachine(childMachine, context, null);
            }

            UpdateMachine(childMachine, context, fixedUpdate);
        }

        private void ExitMachine(StateMachineRuntime machine, ICoCoContext context)
        {
            if (machine.CurrentState != null)
            {
                ExitActiveChildMachine(machine.CurrentState, context);
                InvokeStateExit(machine.CurrentState, context);
            }

            machine.CurrentState = null;
            machine.CurrentStateType = null;
            machine.HasExited = true;
        }

        private void ChangeMachineState(
            StateMachineRuntime machine,
            Type newStateType,
            ICoCoContext context)
        {
            if (machine == null || machine.CurrentStateType == newStateType) return;

            if (!machine.States.TryGetValue(newStateType, out var newState))
            {
                CoCoLog.Warning($"{gameObject.name} 的 State Machine {machine.Name} 未注册状态: {newStateType.Name}。已忽略。");
                return;
            }

            if (machine.CurrentState != null)
            {
                ExitActiveChildMachine(machine.CurrentState, context);
                InvokeStateExit(machine.CurrentState, context);
            }

            machine.HasExited = false;
            machine.Layer.HasExited = false;
            machine.CurrentState = newState;
            machine.CurrentStateType = newStateType;
            InvokeStateEnter(machine.CurrentState, context);

            if (machine.Layer.ChildMachineByParentState.TryGetValue(newState, out var childMachine))
            {
                EnterMachine(childMachine, context, null);
            }
        }

        private void ExitActiveChildMachine(CoCoStateBase parentState, ICoCoContext context)
        {
            if (parentState == null) return;
            if (!_machineByState.TryGetValue(parentState, out var parentMachine)) return;
            if (!parentMachine.Layer.ChildMachineByParentState.TryGetValue(parentState, out var childMachine)) return;

            ExitMachine(childMachine, context);
        }

        private void TryApplyEvaluatedTransition(ICoCoContext context, bool fixedUpdate)
        {
            if (!HasTransitionEvaluationLayer(fixedUpdate)) return;

            var globalEvaluatedType = EvaluateStateType(context);
            foreach (var layer in _orderedTopLevelLayers)
            {
                if (!ShouldTickLayer(layer, fixedUpdate)) continue;

                var nextStateType = ResolveEvaluatedStateType(layer, globalEvaluatedType, context);
                if (nextStateType == null) continue;
                if (ResolveTargetMachine(nextStateType, null, layer, out var machine) != StateResolveResult.Found) continue;

                ChangeMachineState(machine, nextStateType, context);
            }
        }

        private Type ResolveEvaluatedStateType(
            StateLayerRuntime layer,
            Type globalEvaluatedType,
            ICoCoContext context)
        {
            var layerEvaluatedType = EvaluateStateType(layer.SourceLayer, context);
            if (layerEvaluatedType != null) return layerEvaluatedType;

            return layer.ContainsState(globalEvaluatedType) ? globalEvaluatedType : null;
        }

        private static int CompareLayerOrder(
            StateLayerRuntime left,
            StateLayerRuntime right)
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            return orderComparison != 0
                ? orderComparison
                : left.DeclarationIndex.CompareTo(right.DeclarationIndex);
        }

        private bool HasAnyCurrentState()
        {
            foreach (var layer in _orderedTopLevelLayers)
            {
                if (layer.RootMachine.CurrentState != null) return true;
            }

            return false;
        }

        private bool HasTransitionEvaluationLayer(bool fixedUpdate)
        {
            foreach (var layer in _orderedTopLevelLayers)
            {
                if (ShouldTickLayer(layer, fixedUpdate)) return true;
            }

            return false;
        }

        private static bool ShouldTickLayer(StateLayerRuntime layer, bool fixedUpdate)
        {
            return !layer.HasExited && (fixedUpdate ? layer.FixedUpdate : layer.Update);
        }

        private static bool CanChangeMachine(StateMachineRuntime machine)
        {
            return machine.ParentState == null || IsMachinePathActive(machine);
        }

        private static bool IsMachinePathActive(StateMachineRuntime machine)
        {
            if (machine.ParentState == null) return true;
            var parentMachine = machine.ParentMachine;
            if (parentMachine == null) return false;
            if (!ReferenceEquals(parentMachine.CurrentState, machine.ParentState)) return false;

            return IsMachinePathActive(parentMachine);
        }

        private ICoCoContext PushContextOverride(ICoCoContext contextOverride)
        {
            var previousOverride = _contextOverride;
            _contextOverride = contextOverride;
            return previousOverride;
        }

        private void RestoreContextOverride(ICoCoContext previousOverride)
        {
            _contextOverride = previousOverride;
        }

        private void InvokeStateEnter(CoCoStateBase state, ICoCoContext context)
        {
            if (context != null) state.Enter(context);
            else state.Enter();
        }

        private void InvokeStateUpdate(CoCoStateBase state, ICoCoContext context)
        {
            if (context != null) state.OnStateUpdate(context);
            else state.OnStateUpdate();
        }

        private void InvokeStateFixedUpdate(CoCoStateBase state, ICoCoContext context)
        {
            if (context != null) state.OnStateFixedUpdate(context);
            else state.OnStateFixedUpdate();
        }

        private void InvokeStateExit(CoCoStateBase state, ICoCoContext context)
        {
            if (context != null) state.Exit(context);
            else state.Exit();
        }

        private ICoCoContext PrepareContextForTick()
        {
            var context = Context;
            if (context != null && _contextOverride == null &&
                contextProvider is ICoCoContextFrameResolver resolver)
            {
                resolver.ResolveContextFrame(context);
            }

            return context;
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

        private enum StateResolveResult
        {
            Found,
            Missing,
            Ambiguous
        }

        private sealed class StateLayerRuntime
        {
            public StateLayerRuntime(
                string name,
                int order,
                bool update,
                bool fixedUpdate,
                CoCoStateBase defaultState,
                IReadOnlyList<CoCoStateBase> availableStates,
                IReadOnlyList<CoCoStateChildMachine> childMachines,
                int declarationIndex,
                CoCoStateLayer sourceLayer = null)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "State Layer" : name;
                Order = order;
                Update = update;
                FixedUpdate = fixedUpdate;
                DeclarationIndex = declarationIndex;
                SourceLayer = sourceLayer;
                RootMachine = new StateMachineRuntime(
                    this,
                    null,
                    Name,
                    defaultState,
                    availableStates,
                    0);

                if (childMachines == null) return;
                for (int i = 0; i < childMachines.Count; i++)
                {
                    var childMachine = childMachines[i];
                    if (childMachine == null) continue;
                    ChildMachines.Add(new StateMachineRuntime(
                        this,
                        childMachine.ParentCoCoState,
                        $"{Name}/{ResolveStateName(childMachine.ParentCoCoState)}",
                        childMachine.DefaultCoCoState,
                        childMachine.AvailableStates,
                        1));
                }
            }

            public string Name { get; }
            public int Order { get; }
            public bool Update { get; }
            public bool FixedUpdate { get; }
            public int DeclarationIndex { get; }
            public CoCoStateLayer SourceLayer { get; }
            public bool HasExited;
            public StateMachineRuntime RootMachine { get; }
            public readonly List<StateMachineRuntime> ChildMachines = new List<StateMachineRuntime>();
            public readonly Dictionary<CoCoStateBase, StateMachineRuntime> ChildMachineByParentState =
                new Dictionary<CoCoStateBase, StateMachineRuntime>();

            public bool ContainsState(Type stateType)
            {
                if (RootMachine.ContainsState(stateType)) return true;

                foreach (var machine in ChildMachines)
                {
                    if (machine.ContainsState(stateType)) return true;
                }

                return false;
            }

            private static string ResolveStateName(CoCoStateBase state)
            {
                return state != null ? state.GetType().Name : "Unbound";
            }
        }

        private sealed class StateMachineRuntime
        {
            public StateMachineRuntime(
                StateLayerRuntime layer,
                CoCoStateBase parentState,
                string name,
                CoCoStateBase defaultState,
                IReadOnlyList<CoCoStateBase> availableStates,
                int depth)
            {
                Layer = layer;
                ParentState = parentState;
                Name = string.IsNullOrWhiteSpace(name) ? "State Machine" : name;
                DefaultState = defaultState;
                Depth = depth;

                if (availableStates == null) return;
                foreach (var state in availableStates)
                {
                    AvailableStates.Add(state);
                }
            }

            public StateLayerRuntime Layer { get; }
            public CoCoStateBase ParentState { get; }
            public StateMachineRuntime ParentMachine { get; set; }
            public string Name { get; }
            public CoCoStateBase DefaultState { get; }
            public int Depth { get; set; }
            public CoCoStateBase CurrentState;
            public Type CurrentStateType;
            public bool HasExited;
            public readonly List<CoCoStateBase> AvailableStates = new List<CoCoStateBase>();
            public readonly Dictionary<Type, CoCoStateBase> States = new Dictionary<Type, CoCoStateBase>();

            public bool ContainsState(Type stateType)
            {
                return stateType != null && States.ContainsKey(stateType);
            }
        }
    }
}
