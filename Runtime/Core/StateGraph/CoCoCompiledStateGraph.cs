using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public sealed class CoCoCompiledCondition
    {
        internal CoCoCompiledCondition(
            CoCoConditionDescriptor descriptor,
            CoCoFrozenConfigSnapshot config,
            int authoringIndex)
        {
            Descriptor = descriptor;
            Config = config;
            AuthoringIndex = authoringIndex;
        }

        public CoCoConditionDescriptor Descriptor { get; }
        public CoCoFrozenConfigSnapshot Config { get; }
        public int AuthoringIndex { get; }
    }

    public sealed class CoCoCompiledTransition
    {
        private readonly IReadOnlyList<CoCoCompiledCondition> _conditions;

        internal CoCoCompiledTransition(
            CoCoTransitionId transitionId,
            int denseIndex,
            int sourceStateIndex,
            int targetStateIndex,
            int priority,
            CoCoTransitionWindow window,
            CoCoCompiledCondition[] conditions)
        {
            TransitionId = transitionId;
            DenseIndex = denseIndex;
            SourceStateIndex = sourceStateIndex;
            TargetStateIndex = targetStateIndex;
            Priority = priority;
            Window = window;
            _conditions = Array.AsReadOnly((CoCoCompiledCondition[])conditions.Clone());
        }

        public CoCoTransitionId TransitionId { get; }
        public int DenseIndex { get; }
        public int SourceStateIndex { get; }
        public int TargetStateIndex { get; }
        public int Priority { get; }
        public CoCoTransitionWindow Window { get; }
        public IReadOnlyList<CoCoCompiledCondition> Conditions => _conditions;
    }

    public sealed class CoCoCompiledState
    {
        private readonly IReadOnlyList<int> _childStateIndices;
        private readonly IReadOnlyList<int> _rootPathStateIndices;

        internal CoCoCompiledState(
            CoCoStateId stateId,
            int denseIndex,
            int parentStateIndex,
            int initialChildStateIndex,
            CoCoStateDescriptor descriptor,
            CoCoFrozenConfigSnapshot config,
            int[] childStateIndices,
            int[] rootPathStateIndices,
            int firstOutgoingTransitionIndex,
            int outgoingTransitionCount)
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
        }

        public CoCoStateId StateId { get; }
        public int DenseIndex { get; }
        public int ParentStateIndex { get; }
        public int InitialChildStateIndex { get; }
        public CoCoStateDescriptor Descriptor { get; }
        public CoCoFrozenConfigSnapshot Config { get; }
        public IReadOnlyList<int> ChildStateIndices => _childStateIndices;
        public IReadOnlyList<int> RootPathStateIndices => _rootPathStateIndices;
        public int FirstOutgoingTransitionIndex { get; }
        public int OutgoingTransitionCount { get; }
        public bool IsRoot => ParentStateIndex < 0;
        public bool IsLeaf => _childStateIndices.Count == 0;
    }

    public sealed class CoCoCompiledStateLayer
    {
        private readonly CoCoCompiledState[] _states;
        private readonly CoCoCompiledTransition[] _transitions;
        private readonly IReadOnlyList<CoCoCompiledState> _readOnlyStates;
        private readonly IReadOnlyList<CoCoCompiledTransition> _readOnlyTransitions;
        private readonly Dictionary<CoCoStateId, int> _stateIndices;
        private readonly Dictionary<CoCoTransitionId, int> _transitionIndices;

        internal CoCoCompiledStateLayer(
            CoCoLayerId layerId,
            int denseIndex,
            int initialStateIndex,
            CoCoCompiledState[] states,
            CoCoCompiledTransition[] transitions)
        {
            LayerId = layerId;
            DenseIndex = denseIndex;
            InitialStateIndex = initialStateIndex;
            _states = (CoCoCompiledState[])states.Clone();
            _transitions = (CoCoCompiledTransition[])transitions.Clone();
            _readOnlyStates = Array.AsReadOnly(_states);
            _readOnlyTransitions = Array.AsReadOnly(_transitions);
            _stateIndices = new Dictionary<CoCoStateId, int>(_states.Length);
            for (int index = 0; index < _states.Length; index++)
            {
                _stateIndices.Add(_states[index].StateId, index);
            }

            _transitionIndices = new Dictionary<CoCoTransitionId, int>(_transitions.Length);
            for (int index = 0; index < _transitions.Length; index++)
            {
                _transitionIndices.Add(_transitions[index].TransitionId, index);
            }
        }

        public CoCoLayerId LayerId { get; }
        public int DenseIndex { get; }
        public int InitialStateIndex { get; }
        public IReadOnlyList<CoCoCompiledState> States => _readOnlyStates;
        public IReadOnlyList<CoCoCompiledTransition> Transitions => _readOnlyTransitions;

        public bool TryGetState(CoCoStateId stateId, out CoCoCompiledState state)
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
            CoCoTransitionId transitionId,
            out CoCoCompiledTransition transition)
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

    public sealed class CoCoCompiledStateGraph
    {
        private readonly CoCoCompiledStateLayer[] _layers;
        private readonly IReadOnlyList<CoCoCompiledStateLayer> _readOnlyLayers;
        private readonly Dictionary<CoCoLayerId, int> _layerIndices;

        internal CoCoCompiledStateGraph(
            uint schemaVersion,
            ulong contentFingerprint,
            CoCoGraphId graphId,
            ulong catalogFingerprint,
            CoCoCompiledStateLayer[] layers,
            CoCoIntentRequirementManifest intentRequirements,
            CoCoGraphOperationProvisionManifest operationProvides,
            CoCoContextFrameStateRequirementManifest contextStateRequirements)
        {
            SchemaVersion = schemaVersion;
            ContentFingerprint = contentFingerprint;
            GraphId = graphId;
            CatalogFingerprint = catalogFingerprint;
            _layers = (CoCoCompiledStateLayer[])layers.Clone();
            _readOnlyLayers = Array.AsReadOnly(_layers);
            IntentRequirements = intentRequirements ?? throw new ArgumentNullException(nameof(intentRequirements));
            OperationProvides = operationProvides ?? throw new ArgumentNullException(nameof(operationProvides));
            ContextStateRequirements = contextStateRequirements ??
                                       throw new ArgumentNullException(nameof(contextStateRequirements));
            _layerIndices = new Dictionary<CoCoLayerId, int>(_layers.Length);
            for (int index = 0; index < _layers.Length; index++)
            {
                _layerIndices.Add(_layers[index].LayerId, index);
            }
        }

        public uint SchemaVersion { get; }
        public ulong ContentFingerprint { get; }
        public CoCoGraphId GraphId { get; }
        public ulong CatalogFingerprint { get; }
        public IReadOnlyList<CoCoCompiledStateLayer> Layers => _readOnlyLayers;
        public CoCoIntentRequirementManifest IntentRequirements { get; }
        public CoCoGraphOperationProvisionManifest OperationProvides { get; }
        public CoCoContextFrameStateRequirementManifest ContextStateRequirements { get; }

        public bool TryGetLayer(CoCoLayerId layerId, out CoCoCompiledStateLayer layer)
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
