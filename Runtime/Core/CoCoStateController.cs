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

        public CoCoStateLayer() { }

        public CoCoStateLayer(
            string name,
            CoCoStateBase defaultCoCoState,
            IEnumerable<CoCoStateBase> availableStates,
            int order = 0,
            bool update = true,
            bool fixedUpdate = true)
        {
            this.name = name;
            this.defaultCoCoState = defaultCoCoState;
            this.order = order;
            this.update = update;
            this.fixedUpdate = fixedUpdate;
            SetAvailableStates(availableStates);
        }

        public string Name => string.IsNullOrWhiteSpace(name) ? "State Layer" : name;
        public int Order => order;
        public bool Update => update;
        public bool FixedUpdate => fixedUpdate;
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
        [SerializeField] private MonoBehaviour contextProvider;

        private readonly Dictionary<CoCoStateBase, StateLayerRuntime> _layerByState =
            new Dictionary<CoCoStateBase, StateLayerRuntime>();
        private readonly Dictionary<Type, List<StateLayerRuntime>> _layersByStateType =
            new Dictionary<Type, List<StateLayerRuntime>>();
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
            return ResolveTargetLayer(typeof(T), null, out _) == StateLayerResolveResult.Found;
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
            return runtime?.CurrentState;
        }

        public Type GetCurrentStateType(CoCoStateLayer layer)
        {
            RegisterDeclaredLayers();
            var runtime = FindLayerRuntime(layer);
            return runtime?.CurrentStateType;
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
                    var preferredType = layer.ContainsState(evaluatedType) ? evaluatedType : null;
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
            return ResolveTargetLayer(stateType, sourceState, out _) == StateLayerResolveResult.Found;
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

            var result = ResolveTargetLayer(newStateType, sourceState, out var layer);
            if (result == StateLayerResolveResult.Missing)
            {
                CoCoLog.Warning($"{gameObject.name} 尝试切换到未注册的状态: {newStateType.Name}。已忽略。");
                return;
            }

            if (result == StateLayerResolveResult.Ambiguous)
            {
                CoCoLog.Warning(
                    $"{gameObject.name} 尝试切换到存在于多个 State Layer 的状态: {newStateType.Name}。请指定目标 State Layer。");
                return;
            }

            ChangeLayerState(layer, newStateType, context);
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

            ChangeLayerState(runtime, newStateType, context);
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
                0,
                declarationIndex,
                layer);
            RegisterLayer(runtime);
            return runtime;
        }

        private void RegisterLayer(StateLayerRuntime layer)
        {
            foreach (var state in layer.AvailableStates)
            {
                if (state == null) continue;
                RegisterState(layer, state);
            }

            if (layer.DefaultState != null &&
                layer.AvailableStates.Count > 0 &&
                !layer.AvailableStates.Contains(layer.DefaultState))
            {
                CoCoLog.Warning($"{gameObject.name} 的 State Layer {layer.Name} 默认状态未包含在显式状态列表中。");
            }
        }

        private void RegisterState(StateLayerRuntime layer, CoCoStateBase state)
        {
            if (_layerByState.ContainsKey(state))
            {
                CoCoLog.Warning($"状态 {state.GetType().Name} 被多个 State Layer 复用，后续声明已忽略。");
                return;
            }

            var stateType = state.GetType();
            if (layer.States.ContainsKey(stateType))
            {
                CoCoLog.Warning($"State Layer {layer.Name} 中状态 {stateType.Name} 重复注册，已忽略。");
                return;
            }

            if (_initializedStates.Add(state))
            {
                state.Init(this);
            }

            layer.States.Add(stateType, state);
            _layerByState[state] = layer;
            if (!_layersByStateType.TryGetValue(stateType, out var layers))
            {
                layers = new List<StateLayerRuntime>();
                _layersByStateType[stateType] = layers;
            }
            layers.Add(layer);
        }

        private void ResetLayerRegistration()
        {
            _layerByState.Clear();
            _layersByStateType.Clear();
            _orderedTopLevelLayers.Clear();
            _initializedStates.Clear();
            _layersRegistered = false;
        }

        private StateLayerResolveResult ResolveTargetLayer(
            Type targetStateType,
            CoCoStateBase sourceState,
            out StateLayerRuntime layer)
        {
            layer = null;
            if (targetStateType == null) return StateLayerResolveResult.Missing;

            if (sourceState != null)
            {
                if (!_layerByState.TryGetValue(sourceState, out var sourceLayer))
                {
                    return StateLayerResolveResult.Missing;
                }

                if (!sourceLayer.ContainsState(targetStateType))
                {
                    return StateLayerResolveResult.Missing;
                }

                layer = sourceLayer;
                return StateLayerResolveResult.Found;
            }

            if (!_layersByStateType.TryGetValue(targetStateType, out var layers) || layers.Count == 0)
            {
                return StateLayerResolveResult.Missing;
            }

            if (layers.Count == 1)
            {
                layer = layers[0];
                return StateLayerResolveResult.Found;
            }

            return StateLayerResolveResult.Ambiguous;
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
            if (layer.CurrentState != null) return;
            if (layer.HasExited) return;

            Type initialType = preferredType ?? layer.DefaultState?.GetType();
            if (initialType == null) return;

            ChangeLayerState(layer, initialType, context);
        }

        private void UpdateLayer(
            StateLayerRuntime layer,
            ICoCoContext context,
            bool fixedUpdate)
        {
            if (layer.HasExited) return;

            if (layer.CurrentState == null)
            {
                EnterLayer(layer, context, null);
            }

            if (layer.CurrentState != null)
            {
                if (fixedUpdate) InvokeStateFixedUpdate(layer.CurrentState, context);
                else InvokeStateUpdate(layer.CurrentState, context);
            }
        }

        private void ExitLayer(StateLayerRuntime layer, ICoCoContext context)
        {
            if (layer.CurrentState != null)
            {
                InvokeStateExit(layer.CurrentState, context);
            }

            layer.CurrentState = null;
            layer.CurrentStateType = null;
            layer.HasExited = true;
        }

        private void ChangeLayerState(
            StateLayerRuntime layer,
            Type newStateType,
            ICoCoContext context)
        {
            if (layer.CurrentStateType == newStateType) return;

            if (!layer.States.TryGetValue(newStateType, out var newState))
            {
                CoCoLog.Warning($"{gameObject.name} 的 State Layer {layer.Name} 未注册状态: {newStateType.Name}。已忽略。");
                return;
            }

            if (layer.CurrentState != null)
            {
                InvokeStateExit(layer.CurrentState, context);
            }

            layer.HasExited = false;
            layer.CurrentState = newState;
            layer.CurrentStateType = newStateType;
            InvokeStateEnter(layer.CurrentState, context);
        }

        private void TryApplyEvaluatedTransition(ICoCoContext context, bool fixedUpdate)
        {
            if (!HasTransitionEvaluationLayer(fixedUpdate)) return;

            var globalEvaluatedType = EvaluateStateType(context);
            foreach (var layer in _orderedTopLevelLayers)
            {
                if (!ShouldTickLayer(layer, fixedUpdate)) continue;

                var nextStateType = ResolveEvaluatedStateType(layer, globalEvaluatedType, context);
                if (nextStateType == null || !layer.ContainsState(nextStateType)) continue;

                ChangeLayerState(layer, nextStateType, context);
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
                if (layer.CurrentState != null) return true;
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

        private enum StateLayerResolveResult
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
                int depth,
                int declarationIndex,
                CoCoStateLayer sourceLayer = null)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "State Layer" : name;
                Order = order;
                Update = update;
                FixedUpdate = fixedUpdate;
                DefaultState = defaultState;
                Depth = depth;
                DeclarationIndex = declarationIndex;
                SourceLayer = sourceLayer;

                if (availableStates == null) return;
                foreach (var state in availableStates)
                {
                    AvailableStates.Add(state);
                }
            }

            public string Name { get; }
            public int Order { get; }
            public bool Update { get; }
            public bool FixedUpdate { get; }
            public CoCoStateBase DefaultState { get; }
            public int Depth { get; }
            public int DeclarationIndex { get; }
            public CoCoStateLayer SourceLayer { get; }
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
