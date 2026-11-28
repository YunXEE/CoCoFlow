using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// Host 启动前用本构建器把已编译图所需的来源、Operator 与 Context 编解码器接入运行实例。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>将图编译所需的项目绑定、场景来源与 Context 编解码器接入某个 Host。</summary>
    internal sealed class HostBindingBuilder
    {
        private readonly CompiledStateGraph _graph;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly HostComponentBoundary _boundary;
        private readonly IReadOnlyList<MonoBehaviour> _intentSourceComponents;

        private readonly StateGraphLogicBindingsBuilder _logic;
        private readonly OperationRegistryBuilder _operations;
        private readonly ContextFrameLayoutBuilder _contextLayout;
        private readonly ContextCodecRegistry _contextCodecs;
        private readonly List<ContextCodecBinding> _contextCodecBindings;
        private readonly IntentFrameLayout _intentLayout;
        private readonly bool[] _registeredIntents;
        private readonly int[] _intentProducerCounts;
        private readonly bool[] _boundIntentSourceSlots;

        private readonly HashSet<EntityId> _boundIntentComponentIds;
        private readonly List<OperationRequirement> _operationRequirements;
        private readonly List<IGraphContextProducerBinding> _graphContextProducers;
        private readonly List<IHostIntentContribution> _intentContributions;
        private int _nextContextSlotIndex;
        private IntentFrameRuntime _intentRuntime;
        private CoCoDiagnostic _setupFailure;
        private bool _intentBindingsBegun;
        private bool _isFrozen;

        internal HostBindingBuilder(
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            HostComponentBoundary boundary,
            IReadOnlyList<MonoBehaviour> intentSources)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _graphInstanceId = graphInstanceId;
            _boundary = boundary;
            _intentSourceComponents = intentSources ?? Array.Empty<MonoBehaviour>();

            _logic = new StateGraphLogicBindingsBuilder(graph);
            _operations = new OperationRegistryBuilder();
            _contextLayout = new ContextFrameLayoutBuilder();
            _contextCodecs = new ContextCodecRegistry();
            _contextCodecBindings = new List<ContextCodecBinding>();
            IReadOnlyList<ContextStateBlockRequirement> contextBlocks =
                graph.ContextStateRequirements.Blocks;
            for (int blockIndex = 0; blockIndex < contextBlocks.Count; blockIndex++)
            {
                ContextStateBlockRequirement block = contextBlocks[blockIndex];
                if (!_contextLayout.TryAddBlock(
                        block.BlockId,
                        block.Owner,
                        out CoCoDiagnosticCode diagnosticCode))
                {
                    _setupFailure = RegistryError(
                        diagnosticCode,
                        "Compiled Context StateBlock requirements could not initialize the Host layout.");
                    break;
                }
            }

            _intentLayout = new IntentFrameLayout(
                graph.IntentRequirements.LayoutId,
                graph.IntentRequirements.Count);
            _registeredIntents = new bool[graph.IntentRequirements.Count];
            _intentProducerCounts = new int[graph.IntentRequirements.Count];
            _boundIntentSourceSlots = new bool[_intentSourceComponents.Count];

            _boundIntentComponentIds = new HashSet<EntityId>();
            _operationRequirements = new List<OperationRequirement>(
                graph.OperationProvides.Count);
            _graphContextProducers = new List<IGraphContextProducerBinding>();
            _intentContributions = new List<IHostIntentContribution>();

        }

        internal IReadOnlyList<MonoBehaviour> IntentSourceComponents => _intentSourceComponents;
        public CompiledStateGraph Graph => _graph;
        public GraphInstanceId GraphInstanceId => _graphInstanceId;
        public bool IsFrozen => _isFrozen;

        public bool TryBindState(
            StateDescriptorId descriptorId,
            IStateRuntimeFactory factory,
            out CoCoDiagnostic diagnostic)
        {
            bool succeeded = _isFrozen
                ? FrozenFailure(out diagnostic)
                : _logic.TryBindState(descriptorId, factory, out diagnostic);
            return succeeded || LatchFailure(diagnostic);
        }

        public bool TryRegisterIntent<TIntent, TReducer, TFactory>(
            IntentId intentId,
            TFactory reducerFactory,
            ulong semanticFingerprint,
            out IntentHandle<TIntent> handle,
            out CoCoDiagnostic diagnostic)
            where TReducer : unmanaged, IIntentReducer<TIntent>
            where TFactory : IIntentReducerFactory<TIntent, TReducer>
        {
            handle = default;
            if (_isFrozen || _intentBindingsBegun)
            {
                diagnostic = RegistryFrozen("Intent declarations are already frozen.");
                return LatchFailure(diagnostic);
            }

            if (ReferenceEquals(reducerFactory, null) || semanticFingerprint == 0UL ||
                !TryFindIntent(intentId, out IntentRequirement requirement) ||
                requirement.DenseIndex != _intentLayout.Count ||
                requirement.ValueType != typeof(TIntent) ||
                requirement.ReducerType != typeof(TReducer) ||
                requirement.ReducerFactoryType != typeof(TFactory) ||
                requirement.ReducerFactorySemanticFingerprint != semanticFingerprint ||
                _registeredIntents[requirement.DenseIndex])
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Intent reducer binding must exactly match the compiled Intent manifest and dense order.");
                return LatchFailure(diagnostic);
            }

            if (!_intentLayout.TryRegister(
                    intentId,
                    requirement.MaxContributions,
                    reducerFactory,
                    out handle,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _registeredIntents[requirement.DenseIndex] = true;
            return true;
        }

        public bool TryBeginIntentBindings(out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen || _intentBindingsBegun || _graph.IntentRequirements.Count == 0)
            {
                diagnostic = RegistryFrozen("Intent runtime bindings may begin exactly once.");
                return LatchFailure(diagnostic);
            }

            for (int index = 0; index < _registeredIntents.Length; index++)
            {
                if (!_registeredIntents[index])
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingIntentReducer,
                        "Every compiled Intent requires one exact reducer factory before binding sources.");
                    return LatchFailure(diagnostic);
                }
            }

            if (!_intentLayout.Freeze(out diagnostic) ||
                !_intentLayout.TryCreateRuntime(
                    _graphInstanceId,
                    _intentSourceComponents.Count,
                    out _intentRuntime,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _intentBindingsBegun = true;
            return true;
        }

        public bool TryBindIntentSource<TIntent>(
            int hostSourceIndex,
            IntentSourceRequirement<TIntent> requirement,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanBindIntent(requirement.Handle.IntentId, typeof(TIntent), out int denseIndex, out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            if (!TryResolveIntentSource(
                    hostSourceIndex,
                    out IIntentFrameSource<TIntent> source,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            if (!_intentRuntime.TryBindSource(
                    requirement,
                    source,
                    out IntentSourceBinding<TIntent> binding,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _intentContributions.Add(
                new HostIntentSourceContribution<TIntent>(binding, hostSourceIndex));
            _boundIntentSourceSlots[hostSourceIndex] = true;
            _intentProducerCounts[denseIndex]++;
            return true;
        }

        internal bool TryRegisterOperation<TOperation>(
            OperationId operationId,
            Mode mode,
            IOperationViewFactory<TOperation> viewFactory,
            ulong semanticFingerprint,
            out OperationRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TOperation : class, IOperation
        {
            requirement = default;
            if (_isFrozen || semanticFingerprint == 0UL ||
                !TryFindOperation(operationId, out GraphOperationProvision compiled) ||
                compiled.Mode != mode ||
                compiled.OperationType != typeof(TOperation) ||
                compiled.ViewFactoryType != viewFactory?.GetType() ||
                compiled.ViewFactorySemanticFingerprint != semanticFingerprint)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.MissingOperationBinding,
                    "Operation factory must exactly match one compiled Section declaration.");
                return LatchFailure(diagnostic);
            }

            for (int index = 0; index < _operationRequirements.Count; index++)
            {
                if (_operationRequirements[index].OperationId == operationId)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "Each Operation Section may be bound exactly once.");
                    return LatchFailure(diagnostic);
                }
            }

            if (!_operations.TryRegister(
                    operationId,
                    mode,
                    viewFactory,
                    out requirement,
                    out diagnostic) ||
                requirement.Shape.ShapeFingerprint != compiled.Shape.ShapeFingerprint)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.DescriptorTypeMismatch,
                        "Operation Section shape does not match the compiled declaration.");
                }

                return LatchFailure(diagnostic);
            }

            _operationRequirements.Add(requirement);
            return true;
        }

        public bool TryBindContextSlot<TValue>(
            StateBlockId blockId,
            StateSlotId slotId,
            in TValue defaultValue,
            ulong defaultValueFingerprint,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            return TryBindContextSlot(
                blockId,
                slotId,
                defaultValue,
                defaultValueFingerprint,
                null,
                out diagnostic);
        }

        public bool TryBindContextSlot<TValue>(
            StateBlockId blockId,
            StateSlotId slotId,
            in TValue defaultValue,
            ulong defaultValueFingerprint,
            IContextValueCodec<TValue> codec,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            if (!TryGetNextContextSlot(
                    blockId,
                    slotId,
                    typeof(TValue),
                    defaultValueFingerprint,
                    false,
                    null,
                    0UL,
                    out ContextStateSlotRequirement requirement,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            if (!TryBindContextCodec(requirement, codec, out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            if (!_contextLayout.TryAddSlot(
                    blockId,
                    slotId,
                    requirement.Projection,
                    requirement.RestorePolicy,
                    defaultValue,
                    requirement.Codec,
                    CopyDependencies(requirement.DerivedDependencies),
                    out CoCoDiagnosticCode diagnosticCode))
            {
                diagnostic = RegistryError(
                    diagnosticCode,
                    "Context StateSlot binding could not be materialized in the Host layout.");
                return LatchFailure(diagnostic);
            }

            _nextContextSlotIndex++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryBindGraphStateSlot<TMemory, TState, TBinding>(
            LayerId layerId,
            StateId stateId,
            StateBlockId blockId,
            StateSlotId slotId,
            in GraphStateRecord<TState> defaultValue,
            ulong defaultValueFingerprint,
            TBinding memoryBinding,
            out CoCoDiagnostic diagnostic)
            where TMemory : StateActivationMemory
            where TState : unmanaged
            where TBinding : IActivationMemoryStateBinding<TMemory, TState>
        {
            return TryBindGraphStateSlot<TMemory, TState, TBinding>(
                layerId,
                stateId,
                blockId,
                slotId,
                defaultValue,
                defaultValueFingerprint,
                null,
                memoryBinding,
                out diagnostic);
        }

        public bool TryBindGraphStateSlot<TMemory, TState, TBinding>(
            LayerId layerId,
            StateId stateId,
            StateBlockId blockId,
            StateSlotId slotId,
            in GraphStateRecord<TState> defaultValue,
            ulong defaultValueFingerprint,
            IContextValueCodec<GraphStateRecord<TState>> codec,
            TBinding memoryBinding,
            out CoCoDiagnostic diagnostic)
            where TMemory : StateActivationMemory
            where TState : unmanaged
            where TBinding : IActivationMemoryStateBinding<TMemory, TState>
        {
            if (_isFrozen ||
                !defaultValue.IsValid ||
                defaultValue.LayerId != layerId ||
                defaultValue.StateId != stateId ||
                !TryFindState(layerId, stateId, out CompiledState compiledState) ||
                compiledState.Descriptor.ActivationMemoryType != typeof(TMemory) ||
                ReferenceEquals(memoryBinding, null) ||
                memoryBinding.SemanticFingerprint == 0UL ||
                HasGraphProducer(slotId) ||
                HasGraphStateProducer(layerId, stateId))
            {
                diagnostic = RegistryError(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph State binding must uniquely match one compiled State, memory type, trusted default, and Context Slot.");
                return LatchFailure(diagnostic);
            }

            if (!TryBindContextSlot(
                    blockId,
                    slotId,
                    defaultValue,
                    defaultValueFingerprint,
                    codec,
                    out diagnostic))
            {
                return false;
            }

            _graphContextProducers.Add(
                new GraphStateContextBinding<TMemory, TState, TBinding>(
                    layerId,
                    stateId,
                    blockId,
                    slotId,
                    memoryBinding));
            return true;
        }

        public bool TryBindGraphValueSlot<TValue, TProducer>(
            StateBlockId blockId,
            StateSlotId slotId,
            in TValue defaultValue,
            ulong defaultValueFingerprint,
            TProducer producer,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
            where TProducer : IGraphContextValueProducer<TValue>
        {
            return TryBindGraphValueSlot(
                blockId,
                slotId,
                defaultValue,
                defaultValueFingerprint,
                null,
                producer,
                out diagnostic);
        }

        public bool TryBindGraphValueSlot<TValue, TProducer>(
            StateBlockId blockId,
            StateSlotId slotId,
            in TValue defaultValue,
            ulong defaultValueFingerprint,
            IContextValueCodec<TValue> codec,
            TProducer producer,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
            where TProducer : IGraphContextValueProducer<TValue>
        {
            if (_isFrozen || ReferenceEquals(producer, null) ||
                producer.SemanticFingerprint == 0UL || HasGraphProducer(slotId))
            {
                diagnostic = RegistryError(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph value binding requires one unique producer with a non-zero semantic fingerprint.");
                return LatchFailure(diagnostic);
            }

            if (!TryBindContextSlot(
                    blockId,
                    slotId,
                    defaultValue,
                    defaultValueFingerprint,
                    codec,
                    out diagnostic))
            {
                return false;
            }

            _graphContextProducers.Add(
                new GraphValueContextBinding<TValue, TProducer>(
                    blockId,
                    slotId,
                    producer));
            return true;
        }

        public bool TryBindClaimStateSlot(
            StateBlockId blockId,
            StateSlotId slotId,
            in OperatorClaimState defaultValue,
            ulong defaultValueFingerprint,
            out CoCoDiagnostic diagnostic)
        {
            return TryBindClaimStateSlot(
                blockId,
                slotId,
                defaultValue,
                defaultValueFingerprint,
                null,
                out diagnostic);
        }

        public bool TryBindClaimStateSlot(
            StateBlockId blockId,
            StateSlotId slotId,
            in OperatorClaimState defaultValue,
            ulong defaultValueFingerprint,
            IContextValueCodec<OperatorClaimState> codec,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen || !defaultValue.IsValid || defaultValue.IsHeld ||
                HasGraphProducer(slotId))
            {
                diagnostic = RegistryError(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidContextProducer,
                    "Claim State binding requires one unique Graph-owned Slot with an unheld trusted default.");
                return LatchFailure(diagnostic);
            }

            if (!TryBindContextSlot(
                    blockId,
                    slotId,
                    defaultValue,
                    defaultValueFingerprint,
                    codec,
                    out diagnostic))
            {
                return false;
            }

            _graphContextProducers.Add(new GraphClaimContextBinding(blockId, slotId));
            return true;
        }

        public bool TryBindDerivedContextSlot<TValue, TRebuilder>(
            StateBlockId blockId,
            StateSlotId slotId,
            in TValue defaultValue,
            ulong defaultValueFingerprint,
            TRebuilder rebuilder,
            ulong rebuilderSemanticFingerprint,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
            where TRebuilder : IDerivedStateRebuilder<TValue>
        {
            return TryBindDerivedContextSlot(
                blockId,
                slotId,
                defaultValue,
                defaultValueFingerprint,
                null,
                rebuilder,
                rebuilderSemanticFingerprint,
                out diagnostic);
        }

        public bool TryBindDerivedContextSlot<TValue, TRebuilder>(
            StateBlockId blockId,
            StateSlotId slotId,
            in TValue defaultValue,
            ulong defaultValueFingerprint,
            IContextValueCodec<TValue> codec,
            TRebuilder rebuilder,
            ulong rebuilderSemanticFingerprint,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
            where TRebuilder : IDerivedStateRebuilder<TValue>
        {
            diagnostic = CoCoDiagnostic.None;
            if (ReferenceEquals(rebuilder, null) ||
                !TryGetNextContextSlot(
                    blockId,
                    slotId,
                    typeof(TValue),
                    defaultValueFingerprint,
                    true,
                    typeof(TRebuilder),
                    rebuilderSemanticFingerprint,
                    out ContextStateSlotRequirement requirement,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "Derived Context StateSlot binding requires its exact rebuilder instance.");
                }

                return LatchFailure(diagnostic);
            }

            if (!TryBindContextCodec(requirement, codec, out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            if (!_contextLayout.TryAddDerivedSlot(
                    blockId,
                    slotId,
                    requirement.Projection,
                    defaultValue,
                    requirement.Codec,
                    CopyDependencies(requirement.DerivedDependencies),
                    rebuilder,
                    out CoCoDiagnosticCode diagnosticCode))
            {
                diagnostic = RegistryError(
                    diagnosticCode,
                    "Derived Context StateSlot binding could not be materialized in the Host layout.");
                return LatchFailure(diagnostic);
            }

            _nextContextSlotIndex++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryFreeze(
            out HostRuntimeBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            bindings = null;
            if (_setupFailure.IsError)
            {
                _isFrozen = true;
                DisposeIntentRuntime();
                diagnostic = _setupFailure;
                return false;
            }

            if (_isFrozen)
            {
                diagnostic = RegistryFrozen("Host bindings may only be frozen once.");
                return false;
            }

            _isFrozen = true;
            if (!_logic.TryFreeze(out StateGraphLogicBindings logicBindings, out diagnostic))
            {
                DisposeIntentRuntime();
                return false;
            }

            for (int index = 0; index < _boundIntentSourceSlots.Length; index++)
            {
                if (_boundIntentSourceSlots[index])
                {
                    continue;
                }

                diagnostic = RegistryError(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "Every Host Intent Source reference must be bound exactly once by its Host index.");
                DisposeIntentRuntime();
                return false;
            }

            if (_graph.IntentRequirements.Count > 0)
            {
                if (!_intentBindingsBegun || _intentRuntime == null)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingIntentReducer,
                        "The project provider did not create the required Intent runtime.");
                    return false;
                }

                for (int index = 0; index < _intentProducerCounts.Length; index++)
                {
                    if (_intentProducerCounts[index] == 0)
                    {
                        diagnostic = RegistryError(
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Every compiled Intent must have at least one declared Source or Event Adapter binding.");
                        DisposeIntentRuntime();
                        return false;
                    }
                }
            }

            if (!TryFinalizeIntentContributionOrder(
                    out IHostIntentContribution[] intentContributions,
                    out diagnostic) ||
                (_intentRuntime != null && !_intentRuntime.FreezeBindings(out diagnostic)))
            {
                DisposeIntentRuntime();
                return false;
            }

            if (_operationRequirements.Count != _graph.OperationProvides.Count ||
                !_operations.TryFreeze(
                    _graph.OperationProvides.LayoutId,
                    out OperationRegistry operationRegistry,
                    out diagnostic) ||
                !OperationFrame.TryCreate(
                    operationRegistry,
                    _graphInstanceId,
                    _operationRequirements,
                    out OperationFrame operationFrame,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingOperationBinding,
                        "Operation bindings must exactly cover the compiled Provides manifest.");
                }

                DisposeIntentRuntime();
                return false;
            }

            CoCoDiagnosticCode contextDiagnosticCode = CoCoDiagnosticCode.None;
            if (_nextContextSlotIndex != _graph.ContextStateRequirements.SlotCount ||
                !_contextLayout.TryFreeze(
                    _graph.ContextStateRequirements.LayoutId,
                    _graph.ContextStateRequirements.LayoutVersion,
                    out ContextFrameLayout contextLayout,
                    out contextDiagnosticCode) ||
                !_contextCodecs.TryFreeze(out contextDiagnosticCode))
            {
                diagnostic = RegistryError(
                    _nextContextSlotIndex != _graph.ContextStateRequirements.SlotCount
                        ? CoCoDiagnosticCode.MissingDescriptor
                        : contextDiagnosticCode,
                    "Context bindings must exactly cover the compiled State Requirement manifest.");
                DisposeIntentRuntime();
                return false;
            }

            bindings = new HostRuntimeBindings(
                logicBindings,
                operationFrame,
                contextLayout,
                _contextCodecs,
                _graphContextProducers.ToArray(),
                _intentRuntime,
                intentContributions);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void Abandon()
        {
            _isFrozen = true;
            DisposeIntentRuntime();
        }

        private bool CanBindIntent(
            IntentId intentId,
            Type intentType,
            out int denseIndex,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen || !_intentBindingsBegun || _intentRuntime == null ||
                !TryFindIntent(intentId, out IntentRequirement requirement) ||
                requirement.ValueType != intentType ||
                !_registeredIntents[requirement.DenseIndex])
            {
                denseIndex = -1;
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "Intent Sources and Adapters require one exact registered Intent runtime.");
                return false;
            }

            denseIndex = requirement.DenseIndex;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryResolveIntentSource<TIntent>(
            int hostSourceIndex,
            out IIntentFrameSource<TIntent> source,
            out CoCoDiagnostic diagnostic)
        {
            source = null;
            if (!_boundary.HasOwner)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.ManifestConflict,
                    "Binding validation without a Host has an empty explicit Intent Source contract.");
                return false;
            }

            if (hostSourceIndex < 0 ||
                hostSourceIndex >= _intentSourceComponents.Count ||
                _boundIntentSourceSlots[hostSourceIndex])
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Intent Source binding must target one unbound Host source slot.");
                return false;
            }

            MonoBehaviour component = _intentSourceComponents[hostSourceIndex];
            if (component == null ||
                !_boundary.Contains(component) ||
                component is not IIntentFrameSource<TIntent> typedSource ||
                !_boundIntentComponentIds.Add(GetObjectEntityId(component)))
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Intent Source must be one unique live component inside this Host boundary with the exact generic contract.");
                return false;
            }

            source = typedSource;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryFindIntent(IntentId intentId, out IntentRequirement requirement)
        {
            IReadOnlyList<IntentRequirement> requirements = _graph.IntentRequirements.Requirements;
            for (int index = 0; index < requirements.Count; index++)
            {
                if (requirements[index].IntentId == intentId)
                {
                    requirement = requirements[index];
                    return true;
                }
            }

            requirement = null;
            return false;
        }

        private bool TryFinalizeIntentContributionOrder(
            out IHostIntentContribution[] contributions,
            out CoCoDiagnostic diagnostic)
        {
            contributions = new IHostIntentContribution[
                _intentSourceComponents.Count];
            for (int index = 0; index < _intentContributions.Count; index++)
            {
                IHostIntentContribution contribution = _intentContributions[index];
                int targetIndex = contribution.IntentSourceHostIndex;

                if (targetIndex < 0 ||
                    targetIndex >= contributions.Length ||
                    contributions[targetIndex] != null)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.InvalidIntentContribution,
                        "Intent contributions could not map uniquely to Host and manifest order.");
                    return false;
                }

                contributions[targetIndex] = contribution;
            }

            for (int index = 0; index < contributions.Length; index++)
            {
                if (contributions[index] == null ||
                    !contributions[index].TryAssignRegistrationOrder(index))
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.InvalidIntentContribution,
                        "Intent contribution order could not be frozen before Runtime startup.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryFindOperation(
            OperationId operationId,
            out GraphOperationProvision requirement)
        {
            IReadOnlyList<GraphOperationProvision> provides =
                _graph.OperationProvides.Provides;
            for (int index = 0; index < provides.Count; index++)
            {
                if (provides[index].OperationId == operationId)
                {
                    requirement = provides[index];
                    return true;
                }
            }

            requirement = null;
            return false;
        }

        private bool TryFindState(
            LayerId layerId,
            StateId stateId,
            out CompiledState state)
        {
            if (_graph.TryGetLayer(layerId, out CompiledStateLayer layer) &&
                layer.TryGetState(stateId, out state))
            {
                return true;
            }

            state = null;
            return false;
        }

        private bool HasGraphProducer(StateSlotId slotId)
        {
            for (int index = 0; index < _graphContextProducers.Count; index++)
            {
                if (_graphContextProducers[index].SlotId == slotId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasGraphStateProducer(LayerId layerId, StateId stateId)
        {
            for (int index = 0; index < _graphContextProducers.Count; index++)
            {
                if (_graphContextProducers[index] is IGraphStateContextBinding state &&
                    state.LayerId == layerId && state.StateId == stateId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetNextContextSlot(
            StateBlockId blockId,
            StateSlotId slotId,
            Type valueType,
            ulong defaultValueFingerprint,
            bool derived,
            Type rebuilderType,
            ulong rebuilderSemanticFingerprint,
            out ContextStateSlotRequirement requirement,
            out CoCoDiagnostic diagnostic)
        {
            requirement = null;
            if (_isFrozen || defaultValueFingerprint == 0UL)
            {
                diagnostic = RegistryFrozen(
                    "Context bindings require an unfrozen Host builder and non-zero fingerprints.");
                return false;
            }

            int flatIndex = 0;
            IReadOnlyList<ContextStateBlockRequirement> blocks =
                _graph.ContextStateRequirements.Blocks;
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                ContextStateBlockRequirement block = blocks[blockIndex];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++, flatIndex++)
                {
                    if (flatIndex != _nextContextSlotIndex)
                    {
                        continue;
                    }

                    ContextStateSlotRequirement next = block.Slots[slotIndex];
                    bool matches = block.BlockId == blockId &&
                                   next.SlotId == slotId &&
                                   next.WriterBlockId == blockId &&
                                   next.ValueType == valueType &&
                                   next.DefaultValueFingerprint == defaultValueFingerprint &&
                                   (derived
                                       ? next.RestorePolicy == ContextRestorePolicy.Derived &&
                                         next.RebuilderType == rebuilderType &&
                                         next.RebuilderSemanticFingerprint ==
                                             rebuilderSemanticFingerprint
                                       : next.RestorePolicy != ContextRestorePolicy.Derived &&
                                         next.RebuilderType == null &&
                                         next.RebuilderSemanticFingerprint == 0UL);
                    if (!matches)
                    {
                        diagnostic = RegistryError(
                            CoCoDiagnosticCode.DescriptorTypeMismatch,
                            "Context StateSlot binding must match the next compiled manifest entry exactly.");
                        return false;
                    }

                    requirement = next;
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }
            }

            diagnostic = RegistryError(
                CoCoDiagnosticCode.ManifestConflict,
                "Context bindings cannot add entries beyond the compiled manifest.");
            return false;
        }

        private static StateSlotId[] CopyDependencies(
            IReadOnlyList<StateSlotId> dependencies)
        {
            if (dependencies.Count == 0)
            {
                return Array.Empty<StateSlotId>();
            }

            var copy = new StateSlotId[dependencies.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = dependencies[index];
            }

            return copy;
        }

        private bool TryBindContextCodec<TValue>(
            ContextStateSlotRequirement requirement,
            IContextValueCodec<TValue> codec,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            if (!requirement.Codec.UsesCustomCodec)
            {
                if (codec == null)
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                diagnostic = RegistryError(
                    CoCoDiagnosticCode.ManifestConflict,
                    "A raw Context StateSlot cannot bind an extra custom codec.");
                return false;
            }

            if (codec == null || !codec.Descriptor.Equals(requirement.Codec))
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "A custom Context StateSlot requires its exact typed codec descriptor.");
                return false;
            }

            for (int index = 0; index < _contextCodecBindings.Count; index++)
            {
                ContextCodecBinding existing = _contextCodecBindings[index];
                if (existing.ValueType == typeof(TValue) &&
                    existing.Descriptor.Equals(codec.Descriptor))
                {
                    if (ReferenceEquals(existing.Codec, codec))
                    {
                        diagnostic = CoCoDiagnostic.None;
                        return true;
                    }

                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.ManifestConflict,
                        "Repeated Context codec bindings must reuse one exact project codec instance.");
                    return false;
                }
            }

            if (!_contextCodecs.TryRegister(codec, out CoCoDiagnosticCode diagnosticCode))
            {
                diagnostic = RegistryError(
                    diagnosticCode,
                    "The custom Context codec could not be registered for its exact value type.");
                return false;
            }

            _contextCodecBindings.Add(
                new ContextCodecBinding(typeof(TValue), codec.Descriptor, codec));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>将项目 codec 注册到该实例对应的 Context 编解码目录。</summary>
        private readonly struct ContextCodecBinding
        {
            public ContextCodecBinding(
                Type valueType,
                CodecDescriptor descriptor,
                object codec)
            {
                ValueType = valueType;
                Descriptor = descriptor;
                Codec = codec;
            }

            public Type ValueType { get; }
            public CodecDescriptor Descriptor { get; }
            public object Codec { get; }
        }

        private bool FrozenFailure(out CoCoDiagnostic diagnostic)
        {
            diagnostic = RegistryFrozen("Host binding builder is frozen.");
            return false;
        }

        private bool LatchFailure(CoCoDiagnostic diagnostic)
        {
            if (!_setupFailure.IsError)
            {
                _setupFailure = diagnostic.IsError
                    ? diagnostic
                    : RegistryError(
                        CoCoDiagnosticCode.ManifestConflict,
                        "A project binding setup call failed without an error diagnostic.");
            }

            return false;
        }

        private void DisposeIntentRuntime()
        {
            _intentRuntime?.Dispose();
            _intentRuntime = null;
        }

        private static EntityId GetObjectEntityId(UnityEngine.Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }

        private static CoCoDiagnostic RegistryFrozen(string message) =>
            RegistryError(CoCoDiagnosticCode.RegistryFrozen, message);

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);
    }
}
