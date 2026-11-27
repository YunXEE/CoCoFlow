using System;
using System.Collections.Generic;

// 编译器把层、活动路径与出边组织成不可变索引，同一图的多个实例共享这些定义。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存已验证的边端点、优先级和窗口供实例仲裁。</summary>
    public sealed class CompiledTransition
    {

        #region Public API

        public TransitionId TransitionId { get; }
        public int DenseIndex { get; }
        public int SourceStateIndex { get; }
        public int TargetStateIndex { get; }
        public int Priority { get; }
        public TransitionWindow Window { get; }

        #endregion

        #region Internal Logic

        internal CompiledTransition(
            TransitionId transitionId,
            int denseIndex,
            int sourceStateIndex,
            int targetStateIndex,
            int priority,
            TransitionWindow window)
        {
            TransitionId = transitionId;
            DenseIndex = denseIndex;
            SourceStateIndex = sourceStateIndex;
            TargetStateIndex = targetStateIndex;
            Priority = priority;
            Window = window;
        }

        #endregion
    }

    /// <summary>保存节点的描述符、配置、祖先路径及出边索引。</summary>
    public sealed class CompiledState
    {
        private readonly IReadOnlyList<int> _childStateIndices;
        private readonly IReadOnlyList<int> _rootPathStateIndices;

        public StateId StateId { get; }
        public int DenseIndex { get; }
        public int ParentStateIndex { get; }
        public int InitialChildStateIndex { get; }
        public StateDescriptor Descriptor { get; }
        public FrozenConfigSnapshot Config { get; }
        public IReadOnlyList<int> ChildStateIndices => _childStateIndices;
        public IReadOnlyList<int> RootPathStateIndices => _rootPathStateIndices;
        public int FirstOutgoingTransitionIndex { get; }
        public int OutgoingTransitionCount { get; }
        public bool IsAnyState { get; }
        public bool IsRoot => !IsAnyState && ParentStateIndex < 0;
        public bool IsLeaf => !IsAnyState && _childStateIndices.Count == 0;

        #region Internal Logic

        internal CompiledState(
            StateId stateId,
            int denseIndex,
            int parentStateIndex,
            int initialChildStateIndex,
            StateDescriptor descriptor,
            FrozenConfigSnapshot config,
            int[] childStateIndices,
            int[] rootPathStateIndices,
            int firstOutgoingTransitionIndex,
            int outgoingTransitionCount,
            bool isAnyState = false)
        {
            StateId = stateId;
            DenseIndex = denseIndex;
            ParentStateIndex = parentStateIndex;
            InitialChildStateIndex = initialChildStateIndex;
            Descriptor = descriptor;
            Config = config;
            _childStateIndices = Array.AsReadOnly((int[])childStateIndices.Clone());
            _rootPathStateIndices = Array.AsReadOnly((int[])rootPathStateIndices.Clone());
            FirstOutgoingTransitionIndex = firstOutgoingTransitionIndex;
            OutgoingTransitionCount = outgoingTransitionCount;
            IsAnyState = isAnyState;
        }

        #endregion
    }

    /// <summary>将同层节点、初始路径和可选 AnyState 映射到稳定 ID 与运行索引。</summary>
    public sealed class CompiledStateLayer
    {
        private readonly CompiledState[] _states;
        private readonly CompiledTransition[] _transitions;
        private readonly IReadOnlyList<CompiledState> _readOnlyStates;
        private readonly IReadOnlyList<CompiledTransition> _readOnlyTransitions;
        private readonly Dictionary<StateId, int> _stateIndices;
        private readonly Dictionary<TransitionId, int> _transitionIndices;

        public LayerId LayerId { get; }
        public int DenseIndex { get; }
        public int InitialStateIndex { get; }
        public int AnyStateIndex { get; }
        public bool HasAnyState => AnyStateIndex >= 0;
        public IReadOnlyList<CompiledState> States => _readOnlyStates;
        public IReadOnlyList<CompiledTransition> Transitions => _readOnlyTransitions;

        #region Public API

        /// <summary>在本层按节点 ID 查找已编译状态，层内不存在时返回 false。</summary>
        public bool TryGetState(StateId stateId, out CompiledState state)
        {
            if (_stateIndices.TryGetValue(stateId, out int index))
            {
                state = _states[index];
                return true;
            }

            state = null;
            return false;
        }

        /// <summary>在本层按边 ID 查找编译后的转换，层内不存在时返回 false。</summary>
        public bool TryGetTransition(
            TransitionId transitionId,
            out CompiledTransition transition)
        {
            if (_transitionIndices.TryGetValue(transitionId, out int index))
            {
                transition = _transitions[index];
                return true;
            }

            transition = null;
            return false;
        }

        #endregion

        #region Internal Logic

        internal CompiledStateLayer(
            LayerId layerId,
            int denseIndex,
            int initialStateIndex,
            CompiledState[] states,
            CompiledTransition[] transitions,
            int anyStateIndex = -1)
        {
            LayerId = layerId;
            DenseIndex = denseIndex;
            InitialStateIndex = initialStateIndex;
            AnyStateIndex = anyStateIndex;
            _states = (CompiledState[])states.Clone();
            _transitions = (CompiledTransition[])transitions.Clone();
            _readOnlyStates = Array.AsReadOnly(_states);
            _readOnlyTransitions = Array.AsReadOnly(_transitions);
            _stateIndices = new Dictionary<StateId, int>(_states.Length);
            for (int index = 0; index < _states.Length; index++)
            {
                _stateIndices.Add(_states[index].StateId, index);
            }

            _transitionIndices = new Dictionary<TransitionId, int>(_transitions.Length);
            for (int index = 0; index < _transitions.Length; index++)
            {
                _transitionIndices.Add(_transitions[index].TransitionId, index);
            }
        }

        #endregion
    }

    /// <summary>可被多个实例共享的不可变图，包含拓扑、要件及内容与目录指纹。</summary>
    public sealed class CompiledStateGraph
    {
        private readonly CompiledStateLayer[] _layers;
        private readonly IReadOnlyList<CompiledStateLayer> _readOnlyLayers;
        private readonly Dictionary<LayerId, int> _layerIndices;

        public uint SchemaVersion { get; }
        public ulong ContentFingerprint { get; }
        public GraphId GraphId { get; }
        public ulong CatalogFingerprint { get; }
        public IReadOnlyList<CompiledStateLayer> Layers => _readOnlyLayers;
        public IntentRequirementManifest IntentRequirements { get; }
        public GraphOperationProvisionManifest OperationProvides { get; }
        public ContextFrameStateRequirementManifest ContextStateRequirements { get; }

        #region Public API

        /// <summary>按层 ID 读取已编译图中的层，找不到时返回 false。</summary>
        public bool TryGetLayer(LayerId layerId, out CompiledStateLayer layer)
        {
            if (_layerIndices.TryGetValue(layerId, out int index))
            {
                layer = _layers[index];
                return true;
            }

            layer = null;
            return false;
        }

        #endregion

        #region Internal Logic

        internal CompiledStateGraph(
            uint schemaVersion,
            ulong contentFingerprint,
            GraphId graphId,
            ulong catalogFingerprint,
            CompiledStateLayer[] layers,
            IntentRequirementManifest intentRequirements,
            GraphOperationProvisionManifest operationProvides,
            ContextFrameStateRequirementManifest contextStateRequirements)
        {
            SchemaVersion = schemaVersion;
            ContentFingerprint = contentFingerprint;
            GraphId = graphId;
            CatalogFingerprint = catalogFingerprint;
            _layers = (CompiledStateLayer[])layers.Clone();
            _readOnlyLayers = Array.AsReadOnly(_layers);
            IntentRequirements = intentRequirements ?? throw new ArgumentNullException(nameof(intentRequirements));
            OperationProvides = operationProvides ?? throw new ArgumentNullException(nameof(operationProvides));
            ContextStateRequirements = contextStateRequirements ??
                                       throw new ArgumentNullException(nameof(contextStateRequirements));
            _layerIndices = new Dictionary<LayerId, int>(_layers.Length);
            for (int index = 0; index < _layers.Length; index++)
            {
                _layerIndices.Add(_layers[index].LayerId, index);
            }
        }

        #endregion
    }
}
