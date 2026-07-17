using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public interface ICoCoStateRuntimeFactory
    {
        Type LogicType { get; }
        Type ActivationMemoryType { get; }

        CoCoStateLogic CreateLogic(CoCoStateFactoryContext context);
        CoCoActivationMemory CreateMemory();
        void CopyMemory(CoCoActivationMemory source, CoCoActivationMemory destination);
        void ResetMemory(CoCoActivationMemory memory);
        ulong GetMemoryFingerprint(CoCoActivationMemory memory);
    }

    /// <summary>
    /// AOT-safe typed factory adapter installed by the project before any Host starts.
    /// </summary>
    public sealed class CoCoStateRuntimeFactory<TLogic, TMemory> : ICoCoStateRuntimeFactory
        where TLogic : CoCoStateLogic, ICoCoStateUpdate
        where TMemory : CoCoActivationMemory
    {
        private readonly Func<CoCoStateFactoryContext, TLogic> _logicFactory;
        private readonly Func<TMemory> _memoryFactory;
        private readonly Action<TMemory, TMemory> _memoryCopier;
        private readonly Action<TMemory> _memoryResetter;
        private readonly Func<TMemory, ulong> _memoryFingerprinter;

        public CoCoStateRuntimeFactory(
            Func<CoCoStateFactoryContext, TLogic> logicFactory,
            Func<TMemory> memoryFactory,
            Action<TMemory, TMemory> memoryCopier,
            Action<TMemory> memoryResetter,
            Func<TMemory, ulong> memoryFingerprinter)
        {
            _logicFactory = logicFactory ?? throw new ArgumentNullException(nameof(logicFactory));
            _memoryFactory = memoryFactory ?? throw new ArgumentNullException(nameof(memoryFactory));
            _memoryCopier = memoryCopier ?? throw new ArgumentNullException(nameof(memoryCopier));
            _memoryResetter = memoryResetter ?? throw new ArgumentNullException(nameof(memoryResetter));
            _memoryFingerprinter = memoryFingerprinter ??
                                   throw new ArgumentNullException(nameof(memoryFingerprinter));
        }

        public Type LogicType => typeof(TLogic);
        public Type ActivationMemoryType => typeof(TMemory);

        public CoCoStateLogic CreateLogic(CoCoStateFactoryContext context) =>
            _logicFactory(context) ??
            throw new InvalidOperationException("A State runtime factory returned null logic.");

        public CoCoActivationMemory CreateMemory() =>
            _memoryFactory() ??
            throw new InvalidOperationException("A State runtime factory returned null memory.");

        public void CopyMemory(CoCoActivationMemory source, CoCoActivationMemory destination)
        {
            if (!(source is TMemory typedSource) || !(destination is TMemory typedDestination))
            {
                throw new InvalidOperationException("State memory does not match its runtime factory.");
            }

            _memoryCopier(typedSource, typedDestination);
        }

        public void ResetMemory(CoCoActivationMemory memory)
        {
            if (!(memory is TMemory typedMemory))
            {
                throw new InvalidOperationException("State memory does not match its runtime factory.");
            }

            _memoryResetter(typedMemory);
        }

        public ulong GetMemoryFingerprint(CoCoActivationMemory memory)
        {
            if (!(memory is TMemory typedMemory))
            {
                throw new InvalidOperationException("State memory does not match its runtime factory.");
            }

            return _memoryFingerprinter(typedMemory);
        }
    }

    public interface ICoCoConditionRuntimeFactory
    {
        Type ConditionType { get; }
        CoCoStateCondition CreateCondition(CoCoConditionFactoryContext context);
    }

    public sealed class CoCoConditionRuntimeFactory<TCondition> : ICoCoConditionRuntimeFactory
        where TCondition : CoCoStateCondition, ICoCoStateConditionEvaluator
    {
        private readonly Func<CoCoConditionFactoryContext, TCondition> _factory;

        public CoCoConditionRuntimeFactory(Func<CoCoConditionFactoryContext, TCondition> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public Type ConditionType => typeof(TCondition);

        public CoCoStateCondition CreateCondition(CoCoConditionFactoryContext context) =>
            _factory(context) ??
            throw new InvalidOperationException("A Condition runtime factory returned null.");
    }

    public sealed class CoCoStateGraphLogicBindingsBuilder
    {
        private readonly CoCoCompiledStateGraph _graph;
        private readonly Dictionary<CoCoStateDescriptorId, CoCoStateDescriptor> _requiredStates;
        private readonly Dictionary<CoCoConditionDescriptorId, CoCoConditionDescriptor> _requiredConditions;
        private readonly Dictionary<CoCoStateDescriptorId, ICoCoStateRuntimeFactory> _stateFactories;
        private readonly Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory> _conditionFactories;
        private bool _isFrozen;

        public CoCoStateGraphLogicBindingsBuilder(CoCoCompiledStateGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _requiredStates = new Dictionary<CoCoStateDescriptorId, CoCoStateDescriptor>();
            _requiredConditions = new Dictionary<CoCoConditionDescriptorId, CoCoConditionDescriptor>();
            _stateFactories = new Dictionary<CoCoStateDescriptorId, ICoCoStateRuntimeFactory>();
            _conditionFactories = new Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory>();
            CollectRequirements(graph, _requiredStates, _requiredConditions);
        }

        public CoCoCompiledStateGraph Graph => _graph;
        public bool IsFrozen => _isFrozen;
        public int RequiredStateFactoryCount => _requiredStates.Count;
        public int RequiredConditionFactoryCount => _requiredConditions.Count;

        public bool TryBindState(
            CoCoStateDescriptorId descriptorId,
            ICoCoStateRuntimeFactory factory,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanBind(out diagnostic))
            {
                return false;
            }

            if (!_requiredStates.TryGetValue(descriptorId, out CoCoStateDescriptor descriptor) || factory == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "State runtime bindings may contain only descriptors required by this Graph.");
                return false;
            }

            if (_stateFactories.ContainsKey(descriptorId))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "A Graph may bind each State descriptor exactly once.");
                return false;
            }

            if (factory.LogicType != descriptor.LogicType ||
                factory.ActivationMemoryType != descriptor.ActivationMemoryType ||
                !typeof(ICoCoStateUpdate).IsAssignableFrom(factory.LogicType))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "State logic or ActivationMemory factory type does not match the compiled descriptor.");
                return false;
            }

            _stateFactories.Add(descriptorId, factory);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryBindCondition(
            CoCoConditionDescriptorId descriptorId,
            ICoCoConditionRuntimeFactory factory,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanBind(out diagnostic))
            {
                return false;
            }

            if (!_requiredConditions.TryGetValue(
                    descriptorId,
                    out CoCoConditionDescriptor descriptor) ||
                factory == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "Condition runtime bindings may contain only descriptors required by this Graph.");
                return false;
            }

            if (_conditionFactories.ContainsKey(descriptorId))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "A Graph may bind each Condition descriptor exactly once.");
                return false;
            }

            if (factory.ConditionType != descriptor.ConditionType ||
                !typeof(ICoCoStateConditionEvaluator).IsAssignableFrom(factory.ConditionType))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Condition factory type does not match the compiled descriptor.");
                return false;
            }

            _conditionFactories.Add(descriptorId, factory);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryFreeze(
            out CoCoStateGraphLogicBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                bindings = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph logic bindings are already frozen.");
                return false;
            }

            _isFrozen = true;
            if (_stateFactories.Count != _requiredStates.Count ||
                _conditionFactories.Count != _requiredConditions.Count)
            {
                bindings = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "StateGraph runtime bindings must exactly cover every compiled State and Condition descriptor.");
                return false;
            }

            bindings = new CoCoStateGraphLogicBindings(
                _graph,
                _stateFactories,
                _conditionFactories);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool CanBind(out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph logic bindings are frozen.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static void CollectRequirements(
            CoCoCompiledStateGraph graph,
            Dictionary<CoCoStateDescriptorId, CoCoStateDescriptor> states,
            Dictionary<CoCoConditionDescriptorId, CoCoConditionDescriptor> conditions)
        {
            for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
            {
                CoCoCompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoStateDescriptor descriptor = layer.States[stateIndex].Descriptor;
                    if (!states.ContainsKey(descriptor.DescriptorId))
                    {
                        states.Add(descriptor.DescriptorId, descriptor);
                    }
                }

                for (int transitionIndex = 0;
                     transitionIndex < layer.Transitions.Count;
                     transitionIndex++)
                {
                    CoCoCompiledTransition transition = layer.Transitions[transitionIndex];
                    for (int conditionIndex = 0;
                         conditionIndex < transition.Conditions.Count;
                         conditionIndex++)
                    {
                        CoCoConditionDescriptor descriptor = transition.Conditions[conditionIndex].Descriptor;
                        if (!conditions.ContainsKey(descriptor.DescriptorId))
                        {
                            conditions.Add(descriptor.DescriptorId, descriptor);
                        }
                    }
                }
            }
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);
    }

    public sealed class CoCoStateGraphLogicBindings
    {
        private readonly Dictionary<CoCoStateDescriptorId, ICoCoStateRuntimeFactory> _stateFactories;
        private readonly Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory> _conditionFactories;

        internal CoCoStateGraphLogicBindings(
            CoCoCompiledStateGraph graph,
            Dictionary<CoCoStateDescriptorId, ICoCoStateRuntimeFactory> stateFactories,
            Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory> conditionFactories)
        {
            Graph = graph;
            _stateFactories = new Dictionary<CoCoStateDescriptorId, ICoCoStateRuntimeFactory>(stateFactories);
            _conditionFactories =
                new Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory>(conditionFactories);
        }

        public CoCoCompiledStateGraph Graph { get; }
        public int StateFactoryCount => _stateFactories.Count;
        public int ConditionFactoryCount => _conditionFactories.Count;

        internal bool TryGetStateFactory(
            CoCoStateDescriptorId descriptorId,
            out ICoCoStateRuntimeFactory factory) =>
            _stateFactories.TryGetValue(descriptorId, out factory);

        internal bool TryGetConditionFactory(
            CoCoConditionDescriptorId descriptorId,
            out ICoCoConditionRuntimeFactory factory) =>
            _conditionFactories.TryGetValue(descriptorId, out factory);
    }
}
