using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.StateGraph
{
    public sealed class CompiledCondition
    {
        internal CompiledCondition(
            ConditionDescriptor descriptor,
            FrozenConfigSnapshot config,
            int authoringIndex)
        {
            Descriptor = descriptor;
            Config = config;
            AuthoringIndex = authoringIndex;
        }

        public ConditionDescriptor Descriptor { get; }
        public FrozenConfigSnapshot Config { get; }
        public int AuthoringIndex { get; }
    }

    public sealed class CompiledTransition
    {
        private readonly IReadOnlyList<CompiledCondition> _conditions;

        internal CompiledTransition(
            TransitionId transitionId,
            int denseIndex,
            int sourceStateIndex,
            int targetStateIndex,
            int priority,
            TransitionWindow window,
            CompiledCondition[] conditions)
        {
            TransitionId = transitionId;
            DenseIndex = denseIndex;
            SourceStateIndex = sourceStateIndex;
            TargetStateIndex = targetStateIndex;
            Priority = priority;
            Window = window;
            _conditions = Array.AsReadOnly((CompiledCondition[])conditions.Clone());
        }

        public TransitionId TransitionId { get; }
        public int DenseIndex { get; }
        public int SourceStateIndex { get; }
        public int TargetStateIndex { get; }
        public int Priority { get; }
        public TransitionWindow Window { get; }
        public IReadOnlyList<CompiledCondition> Conditions => _conditions;
    }

    public sealed class CompiledState
    {
        private readonly IReadOnlyList<int> _childStateIndices;
        private readonly IReadOnlyList<int> _rootPathStateIndices;

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
    }

    public sealed class CompiledStateLayer
    {
        private readonly CompiledState[] _states;
        private readonly CompiledTransition[] _transitions;
        private readonly IReadOnlyList<CompiledState> _readOnlyStates;
        private readonly IReadOnlyList<CompiledTransition> _readOnlyTransitions;
        private readonly Dictionary<StateId, int> _stateIndices;
        private readonly Dictionary<TransitionId, int> _transitionIndices;

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

        public LayerId LayerId { get; }
        public int DenseIndex { get; }
        public int InitialStateIndex { get; }
        public int AnyStateIndex { get; }
        public bool HasAnyState => AnyStateIndex >= 0;
        public IReadOnlyList<CompiledState> States => _readOnlyStates;
        public IReadOnlyList<CompiledTransition> Transitions => _readOnlyTransitions;

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
    }

    public sealed class CompiledStateGraph
    {
        private readonly CompiledStateLayer[] _layers;
        private readonly IReadOnlyList<CompiledStateLayer> _readOnlyLayers;
        private readonly Dictionary<LayerId, int> _layerIndices;

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

        public uint SchemaVersion { get; }
        public ulong ContentFingerprint { get; }
        public GraphId GraphId { get; }
        public ulong CatalogFingerprint { get; }
        public IReadOnlyList<CompiledStateLayer> Layers => _readOnlyLayers;
        public IntentRequirementManifest IntentRequirements { get; }
        public GraphOperationProvisionManifest OperationProvides { get; }
        public ContextFrameStateRequirementManifest ContextStateRequirements { get; }

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
    }
}
