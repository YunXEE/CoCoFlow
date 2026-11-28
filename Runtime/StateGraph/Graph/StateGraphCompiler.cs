using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Host 和图编辑器调用纯编译器，验证拓扑、描述符和布局，返回可共享的图或带位置的诊断。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>汇总编译诊断和成功时可被实例使用的不可变图。</summary>
    public sealed class StateGraphCompileResult
    {
        private readonly IReadOnlyList<GraphDiagnostic> _diagnostics;

        public CompiledStateGraph Graph { get; }
        public ulong ContentFingerprint { get; }
        public IReadOnlyList<GraphDiagnostic> Diagnostics => _diagnostics;
        public bool Succeeded => Graph != null && !HasErrors;

        public bool HasErrors
        {
            get
            {
                for (int index = 0; index < _diagnostics.Count; index++)
                {
                    if (_diagnostics[index].IsError)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        #region Internal Logic

        internal StateGraphCompileResult(
            CompiledStateGraph graph,
            ulong contentFingerprint,
            GraphDiagnostic[] diagnostics)
        {
            Graph = graph;
            ContentFingerprint = contentFingerprint;
            _diagnostics = Array.AsReadOnly((GraphDiagnostic[])diagnostics.Clone());
        }

        #endregion
    }

    /// <summary>验证纯图输入和冻结目录，并产出可共享的运行拓扑。</summary>
    internal sealed class StateGraphCompiler
    {
        public const uint CurrentSchemaVersion = 1U;

        public StateGraphCompileResult Compile(
            StateGraphSource source,
            GraphDescriptorCatalog catalog)
        {
            var context = new CompilationContext(source, catalog);
            context.Validate();
            CompiledStateGraph graph = context.Build();
            if (context.HasErrors || graph == null)
            {
                return new StateGraphCompileResult(
                    null,
                    source?.ContentFingerprint ?? 0UL,
                    context.Diagnostics.ToArray());
            }

            return new StateGraphCompileResult(
                graph,
                source.ContentFingerprint,
                context.Diagnostics.ToArray());
        }

        /// <summary>保存一次编译中的源索引、诊断和要件汇总。</summary>
        private sealed class CompilationContext
        {
            private readonly StateGraphSource _source;
            private readonly GraphDescriptorCatalog _catalog;
            private readonly Dictionary<StateId, int> _stateLayers = new Dictionary<StateId, int>();
            private readonly HashSet<TransitionId> _transitionIds = new HashSet<TransitionId>();
            private readonly HashSet<IntentId> _intentRequirements = new HashSet<IntentId>();
            private readonly HashSet<OperationId> _operationProvides =
                new HashSet<OperationId>();
            private readonly HashSet<StateBlockId> _contextStateRequirements =
                new HashSet<StateBlockId>();
            private readonly List<IGraphEventToIntentDeclarationRegistration>
                _eventAdapterDeclarations =
                    new List<IGraphEventToIntentDeclarationRegistration>();
            private IntentRequirementManifest _intentManifest;
            private GraphOperationProvisionManifest _operationManifest;
            private ContextFrameStateRequirementManifest _contextManifest;
            private int _structuralDiagnosticCount;

            public CompilationContext(StateGraphSource source, GraphDescriptorCatalog catalog)
            {
                _source = source;
                _catalog = catalog;
            }

            public List<GraphDiagnostic> Diagnostics { get; } = new List<GraphDiagnostic>();

            public bool HasErrors
            {
                get
                {
                    for (int index = 0; index < Diagnostics.Count; index++)
                    {
                        if (Diagnostics[index].IsError)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            public void Validate()
            {
                if (_source == null)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph source is required.",
                        GraphLocation(GraphField.None));
                    return;
                }

                if (_source.SchemaVersion != CurrentSchemaVersion)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.UnsupportedSchemaVersion,
                        "StateGraph schema version is not supported.",
                        GraphLocation(GraphField.SchemaVersion));
                }

                if (_source.ContentFingerprint == 0UL)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.UnsupportedSchemaVersion,
                        "StateGraph content fingerprint must be non-zero.",
                        GraphLocation(GraphField.ContentFingerprint));
                }

                if (!_source.GraphId.IsValid)
                {
                    AddError(
                        CoCoDiagnosticDomain.Identity,
                        CoCoDiagnosticCode.InvalidIdentifier,
                        "StateGraph GraphId must be valid.",
                        GraphLocation(GraphField.Identifier));
                }

                ValidateLayerAndGlobalIdentities();

                ValidateTopologyAndReferences();
                _structuralDiagnosticCount = Diagnostics.Count;

                ValidateDescriptorsAndConfigs();
                ValidateActivePathOperationOverlaps();
                ValidateEventAdapterDeclarations();

                ValidateReachability();

                AggregateManifests();
            }

            public CompiledStateGraph Build()
            {

                if (HasErrors || _intentManifest == null || _operationManifest == null || _contextManifest == null)
                {
                    return null;
                }

                var layers = new CompiledStateLayer[_source.Layers.Count];
                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    layers[layerIndex] = BuildLayer(_source.Layers[layerIndex], layerIndex);
                }

                return new CompiledStateGraph(
                    _source.SchemaVersion,
                    _source.ContentFingerprint,
                    _source.GraphId,
                    _catalog.Fingerprint,
                    layers,
                    _intentManifest,
                    _operationManifest,
                    _contextManifest);
            }

            private void ValidateLayerAndGlobalIdentities()
            {
                if (_source.Layers == null || _source.Layers.Count == 0)
                {
                    return;
                }

                var layerIds = new HashSet<LayerId>();
                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    StateLayerSource layer = _source.Layers[layerIndex];
                    if (layer == null)
                    {
                        continue;
                    }

                    if (!layer.LayerId.IsValid)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Identity,
                            CoCoDiagnosticCode.InvalidIdentifier,
                            "LayerId must be valid.",
                            LayerLocation(layerIndex, layer, GraphField.Identifier));
                    }
                    else if (!layerIds.Add(layer.LayerId))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Identity,
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "LayerIds must be unique within a Graph.",
                            LayerLocation(layerIndex, layer, GraphField.Identifier));
                    }

                    if (layer.States == null)
                    {
                        continue;
                    }

                    int stateCount = layer.States.Count + (layer.AnyState == null ? 0 : 1);
                    for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
                    {
                        StateSource state = stateIndex < layer.States.Count
                            ? layer.States[stateIndex]
                            : layer.AnyState;
                        if (state == null)
                        {
                            continue;
                        }

                        if (!state.StateId.IsValid)
                        {
                            AddError(
                                CoCoDiagnosticDomain.Identity,
                                CoCoDiagnosticCode.InvalidIdentifier,
                                "StateId must be valid.",
                                StateLocation(layerIndex, stateIndex, layer, state, GraphField.Identifier));
                        }
                        else if (_stateLayers.ContainsKey(state.StateId))
                        {
                            AddError(
                                CoCoDiagnosticDomain.Identity,
                                CoCoDiagnosticCode.DuplicateIdentifier,
                                "StateIds must be unique across the complete Graph.",
                                StateLocation(layerIndex, stateIndex, layer, state, GraphField.Identifier));
                        }
                        else
                        {
                            _stateLayers.Add(state.StateId, layerIndex);
                        }
                    }

                    if (layer.Transitions == null)
                    {
                        continue;
                    }

                    for (int transitionIndex = 0;
                         transitionIndex < layer.Transitions.Count;
                         transitionIndex++)
                    {
                        TransitionSource transition = layer.Transitions[transitionIndex];
                        if (transition == null)
                        {
                            continue;
                        }

                        if (!transition.TransitionId.IsValid)
                        {
                            AddError(
                                CoCoDiagnosticDomain.Identity,
                                CoCoDiagnosticCode.InvalidIdentifier,
                                "TransitionId must be valid.",
                                TransitionLocation(
                                    layerIndex,
                                    transitionIndex,
                                    layer,
                                    transition,
                                    GraphField.Identifier));
                        }
                        else if (!_transitionIds.Add(transition.TransitionId))
                        {
                            AddError(
                                CoCoDiagnosticDomain.Identity,
                                CoCoDiagnosticCode.DuplicateIdentifier,
                                "TransitionIds must be unique across the complete Graph.",
                                TransitionLocation(
                                    layerIndex,
                                    transitionIndex,
                                    layer,
                                    transition,
                                    GraphField.Identifier));
                        }
                    }
                }
            }

            private void ValidateTopologyAndReferences()
            {
                if (_source.Layers == null || _source.Layers.Count == 0)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph must declare at least one Layer.",
                        GraphLocation(GraphField.None));
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    ValidateLayerTopology(_source.Layers[layerIndex], layerIndex);
                }
            }

            private void ValidateLayerTopology(StateLayerSource layer, int layerIndex)
            {
                if (layer == null)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph contains a null Layer.",
                        LayerLocation(layerIndex, null, GraphField.None));
                    return;
                }

                if (layer.States == null || layer.States.Count == 0)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "A Layer must declare at least one State.",
                        LayerLocation(layerIndex, layer, GraphField.None));
                    return;
                }

                if (layer.Transitions == null)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "A Layer Transition collection must be declared; use an empty collection when needed.",
                        LayerLocation(layerIndex, layer, GraphField.None));
                }

                var localStates = new Dictionary<StateId, int>();
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateSource state = layer.States[stateIndex];
                    if (state == null)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null State.",
                            StateLocation(layerIndex, stateIndex, layer, null, GraphField.None));
                        continue;
                    }

                    if (state.StateId.IsValid && !localStates.ContainsKey(state.StateId))
                    {
                        localStates.Add(state.StateId, stateIndex);
                    }
                }

                ValidateLayerInitialState(layer, layerIndex, localStates);
                ValidateHierarchy(layer, layerIndex, localStates);
                if (layer.AnyState != null &&
                    (layer.AnyState.ParentStateId.IsValid || layer.AnyState.InitialChildStateId.IsValid))
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.InvalidInitialState,
                        "AnyState cannot have a parent or an initial child.",
                        StateLocation(layerIndex, -1, layer, layer.AnyState, GraphField.ParentState));
                }

                ValidateTransitions(layer, layerIndex, localStates);
            }

            private void ValidateDescriptorsAndConfigs()
            {
                if (_catalog == null || !_catalog.IsFrozen)
                {
                    AddError(
                        CoCoDiagnosticDomain.Registry,
                        CoCoDiagnosticCode.RegistryNotFrozen,
                        "A frozen Graph Descriptor Catalog is required.",
                        GraphLocation(GraphField.Descriptor));
                    return;
                }

                if (_source.Layers == null)
                {
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    StateLayerSource layer = _source.Layers[layerIndex];
                    if (layer?.States != null)
                    {
                        int stateCount = layer.States.Count + (layer.AnyState == null ? 0 : 1);
                        for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
                        {
                            StateSource state = stateIndex < layer.States.Count
                                ? layer.States[stateIndex]
                                : layer.AnyState;
                            if (state != null)
                            {
                                ValidateStateDescriptor(layer, layerIndex, state, stateIndex);
                            }
                        }
                    }

                    if (layer?.Transitions == null)
                    {
                        continue;
                    }

                    for (int transitionIndex = 0;
                         transitionIndex < layer.Transitions.Count;
                         transitionIndex++)
                    {
                        TransitionSource transition = layer.Transitions[transitionIndex];
                        if (transition != null)
                        {
                            ValidateActionProgressProvider(layer, layerIndex, transition, transitionIndex);
                        }
                    }
                }
            }

            private void ValidateActionProgressProvider(
                StateLayerSource layer,
                int layerIndex,
                TransitionSource transition,
                int transitionIndex)
            {
                if (transition.Window.Mode != TransitionWindowMode.ActionProgress ||
                    !TryFindState(layer, transition.SourceStateId, out StateSource sourceState, out _) ||
                    !_catalog.TryGetStateDescriptor(
                        sourceState.DescriptorId,
                        out StateDescriptor descriptor))
                {
                    return;
                }

                if (!descriptor.ProvidesActionProgress)
                {
                    AddError(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.MissingActionProgressProvider,
                        "An ActionProgress Window requires its source State runtime registration to provide ActionProgress.",
                        TransitionLocation(
                            layerIndex,
                            transitionIndex,
                            layer,
                            transition,
                            GraphField.Window));
                }
            }

            private void ValidateActivePathOperationOverlaps()
            {
                if (_catalog == null || !_catalog.IsFrozen || _source.Layers == null)
                {
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    StateLayerSource layer = _source.Layers[layerIndex];
                    if (layer?.States == null)
                    {
                        continue;
                    }

                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        StateSource state = layer.States[stateIndex];
                        if (state == null ||
                            !_catalog.TryGetStateDescriptor(
                                state.DescriptorId,
                                out StateDescriptor descriptor) ||
                            descriptor.OperationProvides.Count == 0)
                        {
                            continue;
                        }

                        var providedByState = new HashSet<OperationId>(descriptor.OperationProvides);
                        if (layer.AnyState != null && _catalog.TryGetStateDescriptor(
                                layer.AnyState.DescriptorId, out StateDescriptor anyDescriptor))
                        {
                            foreach (OperationId operationId in anyDescriptor.OperationProvides)
                            {
                                if (providedByState.Contains(operationId))
                                {
                                    AddWarning(
                                        CoCoDiagnosticDomain.Operation,
                                        CoCoDiagnosticCode.ActivePathOperationOverlap,
                                        $"AnyState and State {state.StateId} both provide Operation Section {operationId}.",
                                        StateLocation(layerIndex, -1, layer, layer.AnyState, GraphField.Descriptor));
                                }
                            }
                        }

                        var reported = new HashSet<OperationId>();
                        var visited = new HashSet<StateId>();
                        StateId ancestorId = state.ParentStateId;
                        while (ancestorId.IsValid && visited.Add(ancestorId))
                        {
                            if (!TryFindState(layer, ancestorId, out StateSource ancestor, out _))
                            {
                                break;
                            }

                            if (_catalog.TryGetStateDescriptor(
                                    ancestor.DescriptorId,
                                    out StateDescriptor ancestorDescriptor))
                            {
                                for (int operationIndex = 0;
                                     operationIndex < ancestorDescriptor.OperationProvides.Count;
                                     operationIndex++)
                                {
                                    OperationId operationId =
                                        ancestorDescriptor.OperationProvides[operationIndex];
                                    if (providedByState.Contains(operationId) && reported.Add(operationId))
                                    {
                                        AddWarning(
                                            CoCoDiagnosticDomain.Operation,
                                            CoCoDiagnosticCode.ActivePathOperationOverlap,
                                            $"State and ancestor {ancestor.StateId} both provide Operation Section {operationId} on one ActivePath.",
                                            StateLocation(
                                                layerIndex,
                                                stateIndex,
                                                layer,
                                                state,
                                                GraphField.Descriptor));
                                    }
                                }
                            }

                            ancestorId = ancestor.ParentStateId;
                        }
                    }
                }
            }

            private void ValidateStateDescriptor(
                StateLayerSource layer,
                int layerIndex,
                StateSource state,
                int stateIndex)
            {
                if (!state.DescriptorId.IsValid ||
                    !_catalog.TryGetStateDescriptor(state.DescriptorId, out StateDescriptor descriptor))
                {
                    AddError(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.MissingDescriptor,
                        "State descriptor is not registered.",
                        StateLocation(layerIndex, stateIndex, layer, state, GraphField.Descriptor));
                    return;
                }

                bool isAnyState = ReferenceEquals(state, layer.AnyState);
                bool isAnyLogic = typeof(AnyStateLogic).IsAssignableFrom(descriptor.LogicType);
                if (isAnyState != isAnyLogic ||
                    (isAnyState && (typeof(IStateEnter).IsAssignableFrom(descriptor.LogicType) ||
                                    typeof(IStateExit).IsAssignableFrom(descriptor.LogicType))))
                {
                    AddError(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.DescriptorTypeMismatch,
                        "AnyState requires AnyStateLogic without Enter or Exit; ordinary States cannot use it.",
                        StateLocation(layerIndex, stateIndex, layer, state, GraphField.Descriptor));
                }

                AddRequirements(descriptor);
                if (!_catalog.AcceptsStateConfig(state.DescriptorId, state.Config))
                {
                    AddError(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.DescriptorTypeMismatch,
                        "State frozen config does not match its descriptor.",
                        StateLocation(layerIndex, stateIndex, layer, state, GraphField.Config));
                }
            }

            private void ValidateEventAdapterDeclarations()
            {
                if (_catalog == null || !_catalog.IsFrozen)
                {
                    return;
                }

                var seen = new HashSet<EventToIntentDeclarationKey>();
                var countsByIntent = new Dictionary<IntentId, int>();
                EventDomainId graphEventDomain = default;
                for (int declarationIndex = 0;
                     declarationIndex < _source.EventAdapterDeclarations.Count;
                     declarationIndex++)
                {
                    EventToIntentDeclarationSource declaration =
                        _source.EventAdapterDeclarations[declarationIndex];
                    GraphDiagnosticLocation location =
                        EventAdapterDeclarationLocation(declarationIndex);
                    if (declaration == null)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "StateGraph contains a null Event Adapter declaration.",
                            location);
                        continue;
                    }

                    if (!EventToIntentDeclarationKey.TryCreate(
                            declaration.EventTypeId,
                            declaration.ProvidedIntentId,
                            out EventToIntentDeclarationKey key))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Identity,
                            CoCoDiagnosticCode.InvalidIdentifier,
                            "Event Adapter declaration requires valid EventTypeId and ProvidedIntentId values.",
                            location);
                        continue;
                    }

                    if (!seen.Add(key))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Identity,
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "Event Adapter declarations must use unique EventTypeId and ProvidedIntentId pairs.",
                            location);
                        continue;
                    }

                    if (!_catalog.TryGetEventToIntentDeclaration(
                            key,
                            out IGraphEventToIntentDeclarationRegistration registration))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Event Adapter declaration is not registered in the Graph Descriptor Catalog.",
                            location);
                        continue;
                    }

                    if (!_catalog.TryGetIntent(
                            declaration.ProvidedIntentId,
                            out IGraphIntentRegistration intentRegistration) ||
                        intentRegistration.ValueType != registration.ProvidedIntentType)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.ManifestConflict,
                            "Event Adapter declaration does not match its provided Intent registration.",
                            location);
                        continue;
                    }

                    if (!graphEventDomain.IsValid)
                    {
                        graphEventDomain = registration.EventDomainId;
                    }
                    else if (graphEventDomain != registration.EventDomainId)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.EventDomainMismatch,
                            "All Event Adapter declarations in one StateGraph must use the same EventDomain.",
                            location);
                        continue;
                    }

                    countsByIntent.TryGetValue(
                        declaration.ProvidedIntentId,
                        out int declarationCount);
                    declarationCount++;
                    countsByIntent[declaration.ProvidedIntentId] = declarationCount;
                    if (declarationCount > intentRegistration.MaxContributions)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.ManifestConflict,
                            "Event Adapter declarations exceed the provided Intent contribution capacity.",
                            location);
                        continue;
                    }

                    _intentRequirements.Add(declaration.ProvidedIntentId);
                    _eventAdapterDeclarations.Add(registration);
                }
            }

            private void ValidateLayerInitialState(
                StateLayerSource layer,
                int layerIndex,
                Dictionary<StateId, int> localStates)
            {
                if (!layer.InitialStateId.IsValid ||
                    !localStates.TryGetValue(layer.InitialStateId, out int initialIndex))
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.InvalidInitialState,
                        "Layer InitialStateId must resolve to a State in the same Layer.",
                        LayerLocation(layerIndex, layer, GraphField.InitialState));
                    return;
                }

                StateSource initial = layer.States[initialIndex];
                if (initial == null || initial.ParentStateId.IsValid)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.InvalidInitialState,
                        "Layer InitialStateId must point to a root State.",
                        LayerLocation(layerIndex, layer, GraphField.InitialState));
                }
            }

            private void ValidateHierarchy(
                StateLayerSource layer,
                int layerIndex,
                Dictionary<StateId, int> localStates)
            {
                var children = new Dictionary<StateId, List<StateId>>();
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateSource state = layer.States[stateIndex];
                    if (state == null || !state.StateId.IsValid)
                    {
                        continue;
                    }

                    if (state.ParentStateId.IsValid)
                    {
                        if (!localStates.ContainsKey(state.ParentStateId) || state.ParentStateId == state.StateId)
                        {
                            AddError(
                                CoCoDiagnosticDomain.Topology,
                                CoCoDiagnosticCode.MissingTopologyElement,
                                "State ParentStateId must resolve to another State in the same Layer.",
                                StateLocation(
                                    layerIndex,
                                    stateIndex,
                                    layer,
                                    state,
                                    GraphField.ParentState));
                        }
                        else
                        {
                            if (!children.TryGetValue(state.ParentStateId, out List<StateId> list))
                            {
                                list = new List<StateId>();
                                children.Add(state.ParentStateId, list);
                            }

                            list.Add(state.StateId);
                        }
                    }

                    DetectParentCycle(layer, layerIndex, stateIndex, state, localStates);
                }

                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateSource state = layer.States[stateIndex];
                    if (state == null || !state.StateId.IsValid)
                    {
                        continue;
                    }

                    bool hasChildren = children.TryGetValue(state.StateId, out List<StateId> childIds) &&
                                       childIds.Count > 0;
                    if (hasChildren &&
                        (!state.InitialChildStateId.IsValid ||
                         !localStates.ContainsKey(state.InitialChildStateId) ||
                         layer.States[localStates[state.InitialChildStateId]].ParentStateId != state.StateId))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.InvalidInitialState,
                            "A composite State requires an InitialChildStateId that points to a direct child.",
                            StateLocation(
                                layerIndex,
                                stateIndex,
                                layer,
                                state,
                                GraphField.InitialChildState));
                    }
                    else if (!hasChildren && state.InitialChildStateId.IsValid)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.InvalidInitialState,
                            "A leaf State cannot declare InitialChildStateId.",
                            StateLocation(
                                layerIndex,
                                stateIndex,
                                layer,
                                state,
                                GraphField.InitialChildState));
                    }
                }
            }

            private void DetectParentCycle(
                StateLayerSource layer,
                int layerIndex,
                int stateIndex,
                StateSource state,
                Dictionary<StateId, int> localStates)
            {
                var visited = new HashSet<StateId>();
                StateSource current = state;
                while (current != null && current.ParentStateId.IsValid)
                {
                    if (!visited.Add(current.StateId))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.ParentStateCycle,
                            "State parent hierarchy contains a cycle.",
                            StateLocation(
                                layerIndex,
                                stateIndex,
                                layer,
                                state,
                                GraphField.ParentState));
                        return;
                    }

                    if (!localStates.TryGetValue(current.ParentStateId, out int parentIndex))
                    {
                        return;
                    }

                    current = layer.States[parentIndex];
                }
            }

            private void ValidateTransitions(
                StateLayerSource layer,
                int layerIndex,
                Dictionary<StateId, int> localStates)
            {
                if (layer.Transitions == null)
                {
                    return;
                }

                var firstPriorityOwners =
                    new Dictionary<StateId, Dictionary<int, int>>();
                var reportedPriorityOwners = new HashSet<int>();
                for (int transitionIndex = 0;
                     transitionIndex < layer.Transitions.Count;
                     transitionIndex++)
                {
                    TransitionSource transition = layer.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null Transition.",
                            TransitionLocation(layerIndex, transitionIndex, layer, null, GraphField.None));
                        continue;
                    }

                    bool validSource = ValidateTransitionEndpoint(
                        layer,
                        layerIndex,
                        transition,
                        transitionIndex,
                        transition.SourceStateId,
                        GraphField.SourceState,
                        localStates);
                    ValidateTransitionEndpoint(
                        layer,
                        layerIndex,
                        transition,
                        transitionIndex,
                        transition.TargetStateId,
                        GraphField.TargetState,
                        localStates);

                    if (validSource)
                    {
                        if (!firstPriorityOwners.TryGetValue(
                                transition.SourceStateId,
                                out Dictionary<int, int> priorities))
                        {
                            priorities = new Dictionary<int, int>();
                            firstPriorityOwners.Add(transition.SourceStateId, priorities);
                        }

                        if (priorities.TryGetValue(transition.Priority, out int firstTransitionIndex))
                        {
                            if (reportedPriorityOwners.Add(firstTransitionIndex))
                            {
                                AddDuplicateTransitionPriorityError(
                                    layer,
                                    layerIndex,
                                    firstTransitionIndex,
                                    layer.Transitions[firstTransitionIndex]);
                            }

                            AddDuplicateTransitionPriorityError(
                                layer,
                                layerIndex,
                                transitionIndex,
                                transition);
                        }
                        else
                        {
                            priorities.Add(transition.Priority, transitionIndex);
                        }
                    }

                    if (!transition.Window.IsValid)
                    {
                        AddError(
                            CoCoDiagnosticDomain.State,
                            CoCoDiagnosticCode.InvalidTransitionWindow,
                            "Transition Window is invalid.",
                            TransitionLocation(
                                layerIndex,
                                transitionIndex,
                                layer,
                                transition,
                                GraphField.Window));
                    }

                }
            }

            private bool ValidateTransitionEndpoint(
                StateLayerSource layer,
                int layerIndex,
                TransitionSource transition,
                int transitionIndex,
                StateId stateId,
                GraphField field,
                Dictionary<StateId, int> localStates)
            {
                if (stateId.IsValid && layer.AnyState?.StateId == stateId)
                {
                    if (field == GraphField.SourceState)
                    {
                        return true;
                    }

                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.NonLeafTransitionEndpoint,
                        "AnyState can only be a Transition source, never a target.",
                        TransitionLocation(layerIndex, transitionIndex, layer, transition, field));
                    return false;
                }

                if (stateId.IsValid && localStates.ContainsKey(stateId))
                {
                    if (!HasChildState(layer, stateId))
                    {
                        return true;
                    }

                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.NonLeafTransitionEndpoint,
                        "Transition endpoints must be terminal leaf States.",
                        TransitionLocation(layerIndex, transitionIndex, layer, transition, field));
                    return false;
                }

                bool belongsToOtherLayer = stateId.IsValid &&
                                           _stateLayers.TryGetValue(stateId, out int ownerLayer) &&
                                           ownerLayer != layerIndex;
                AddError(
                    CoCoDiagnosticDomain.Topology,
                    belongsToOtherLayer
                        ? CoCoDiagnosticCode.CrossLayerReference
                        : CoCoDiagnosticCode.MissingTopologyElement,
                    belongsToOtherLayer
                        ? "Transition endpoints must belong to the same Layer."
                        : "Transition endpoint does not resolve to a State in its Layer.",
                    TransitionLocation(layerIndex, transitionIndex, layer, transition, field));
                return false;
            }

            private void AddDuplicateTransitionPriorityError(
                StateLayerSource layer,
                int layerIndex,
                int transitionIndex,
                TransitionSource transition)
            {
                AddError(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.DuplicateTransitionPriority,
                    "Outgoing Transitions from one source leaf State must use unique priorities.",
                    TransitionLocation(
                        layerIndex,
                        transitionIndex,
                        layer,
                        transition,
                        GraphField.Priority));
            }

            private void ValidateReachability()
            {
                if (_source.Layers == null)
                {
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    StateLayerSource layer = _source.Layers[layerIndex];
                    if (layer?.States == null || layer.States.Count == 0 ||
                        HasLayerStructuralErrors(layerIndex))
                    {
                        continue;
                    }

                    var localStates = new Dictionary<StateId, int>();
                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        StateSource state = layer.States[stateIndex];
                        if (state != null && state.StateId.IsValid && !localStates.ContainsKey(state.StateId))
                        {
                            localStates.Add(state.StateId, stateIndex);
                        }
                    }

                    AddUnreachableWarnings(layer, layerIndex, localStates);
                }
            }

            private void AddUnreachableWarnings(
                StateLayerSource layer,
                int layerIndex,
                Dictionary<StateId, int> localStates)
            {
                var reachable = new HashSet<StateId>();
                var pending = new Queue<StateId>();
                MarkPathAndInitialChain(layer, localStates, layer.InitialStateId, reachable, pending);
                if (layer.AnyState != null)
                {
                    pending.Enqueue(layer.AnyState.StateId);
                }

                while (pending.Count > 0)
                {
                    StateId sourceId = pending.Dequeue();
                    if (layer.Transitions == null)
                    {
                        continue;
                    }

                    for (int index = 0; index < layer.Transitions.Count; index++)
                    {
                        TransitionSource transition = layer.Transitions[index];
                        if (transition != null && transition.SourceStateId == sourceId)
                        {
                            MarkPathAndInitialChain(
                                layer,
                                localStates,
                                transition.TargetStateId,
                                reachable,
                                pending);
                        }
                    }
                }

                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateSource state = layer.States[stateIndex];
                    if (state != null && state.StateId.IsValid && !reachable.Contains(state.StateId))
                    {
                        AddWarning(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.UnreachableState,
                            "State is not reachable from the Layer InitialState.",
                            StateLocation(layerIndex, stateIndex, layer, state, GraphField.None));
                    }
                }
            }

            private static void MarkPathAndInitialChain(
                StateLayerSource layer,
                Dictionary<StateId, int> localStates,
                StateId stateId,
                HashSet<StateId> reachable,
                Queue<StateId> pending)
            {
                if (!localStates.TryGetValue(stateId, out int stateIndex))
                {
                    return;
                }

                var path = new Stack<StateId>();
                StateSource current = layer.States[stateIndex];
                while (current != null)
                {
                    path.Push(current.StateId);
                    if (!current.ParentStateId.IsValid ||
                        !localStates.TryGetValue(current.ParentStateId, out int parentIndex))
                    {
                        break;
                    }

                    current = layer.States[parentIndex];
                }

                while (path.Count > 0)
                {
                    AddReachable(path.Pop(), reachable, pending);
                }

                current = layer.States[stateIndex];
                while (current != null &&
                       current.InitialChildStateId.IsValid &&
                       localStates.TryGetValue(current.InitialChildStateId, out int childIndex))
                {
                    current = layer.States[childIndex];
                    AddReachable(current.StateId, reachable, pending);
                }
            }

            private static void AddReachable(
                StateId stateId,
                HashSet<StateId> reachable,
                Queue<StateId> pending)
            {
                if (reachable.Add(stateId))
                {
                    pending.Enqueue(stateId);
                }
            }

            private CompiledStateLayer BuildLayer(StateLayerSource layer, int layerIndex)
            {
                var stateSources = new StateSource[layer.States.Count];
                for (int index = 0; index < layer.States.Count; index++)
                {
                    stateSources[index] = layer.States[index];
                }

                Array.Sort(stateSources, (left, right) => StringComparer.Ordinal.Compare(
                    left.StateId.ToString(),
                    right.StateId.ToString()));
                int anyStateIndex = -1;
                if (layer.AnyState != null)
                {
                    anyStateIndex = stateSources.Length;
                    Array.Resize(ref stateSources, stateSources.Length + 1);
                    stateSources[anyStateIndex] = layer.AnyState;
                }

                var stateIndices = new Dictionary<StateId, int>(stateSources.Length);
                for (int index = 0; index < stateSources.Length; index++)
                {
                    stateIndices.Add(stateSources[index].StateId, index);
                }

                var transitionSources = new TransitionSource[layer.Transitions.Count];
                for (int index = 0; index < layer.Transitions.Count; index++)
                {
                    transitionSources[index] = layer.Transitions[index];
                }

                Array.Sort(transitionSources, (left, right) =>
                {
                    int sourceComparison = stateIndices[left.SourceStateId].CompareTo(
                        stateIndices[right.SourceStateId]);
                    if (sourceComparison != 0)
                    {
                        return sourceComparison;
                    }

                    int priorityComparison = right.Priority.CompareTo(left.Priority);
                    return priorityComparison != 0
                        ? priorityComparison
                        : StringComparer.Ordinal.Compare(
                            left.TransitionId.ToString(),
                            right.TransitionId.ToString());
                });

                var transitions = new CompiledTransition[transitionSources.Length];
                for (int index = 0; index < transitionSources.Length; index++)
                {
                    TransitionSource source = transitionSources[index];
                    transitions[index] = new CompiledTransition(
                        source.TransitionId,
                        index,
                        stateIndices[source.SourceStateId],
                        stateIndices[source.TargetStateId],
                        source.Priority,
                        source.Window);
                }

                var states = new CompiledState[stateSources.Length];
                for (int index = 0; index < stateSources.Length; index++)
                {
                    StateSource source = stateSources[index];
                    bool isAnyState = index == anyStateIndex;
                    _catalog.TryGetStateDescriptor(source.DescriptorId, out StateDescriptor descriptor);
                    int parentIndex = source.ParentStateId.IsValid ? stateIndices[source.ParentStateId] : -1;
                    int initialChildIndex = source.InitialChildStateId.IsValid
                        ? stateIndices[source.InitialChildStateId]
                        : -1;
                    int[] children = GetChildren(stateSources, stateIndices, source.StateId);
                    int[] path = isAnyState ? Array.Empty<int>() : GetRootPath(stateSources, stateIndices, index);
                    GetTransitionRange(transitions, index, out int firstTransition, out int transitionCount);
                    states[index] = new CompiledState(
                        source.StateId,
                        index,
                        parentIndex,
                        initialChildIndex,
                        descriptor,
                        source.Config,
                        children,
                        path,
                        firstTransition,
                        transitionCount,
                        isAnyState);
                }

                return new CompiledStateLayer(
                    layer.LayerId,
                    layerIndex,
                    stateIndices[layer.InitialStateId],
                    states,
                    transitions,
                    anyStateIndex);
            }

            private void AggregateManifests()
            {
                if (_catalog == null || !_catalog.IsFrozen)
                {
                    return;
                }

                _intentManifest = BuildIntentManifest();
                _operationManifest = BuildOperationManifest();
                _contextManifest = BuildContextManifest();
            }

            private IntentRequirementManifest BuildIntentManifest()
            {
                IntentId[] ids = Sorted(_intentRequirements);
                var registrations = new IGraphIntentRegistration[ids.Length];
                for (int index = 0; index < ids.Length; index++)
                {
                    if (!_catalog.TryGetIntent(ids[index], out registrations[index]))
                    {
                        AddManifestError("Intent Requirement is not registered.");
                        return null;
                    }
                }

                IGraphEventToIntentDeclarationRegistration[] declarations =
                    _eventAdapterDeclarations.ToArray();
                var declarationKeys = new EventToIntentDeclarationKey[declarations.Length];
                for (int index = 0; index < declarations.Length; index++)
                {
                    declarationKeys[index] = declarations[index].Key;
                }

                return new IntentRequirementManifest(
                    GraphLayoutIdentity.Create(
                        _source.GraphId,
                        _catalog.Fingerprint,
                        1UL,
                        ids,
                        declarationKeys),
                    registrations,
                    declarations);
            }

            private GraphOperationProvisionManifest BuildOperationManifest()
            {
                OperationId[] ids = Sorted(_operationProvides);
                var registrations = new IGraphOperationRegistration[ids.Length];
                for (int index = 0; index < ids.Length; index++)
                {
                    if (!_catalog.TryGetOperation(ids[index], out registrations[index]))
                    {
                        AddManifestError("Graph Operation Provides entry is not registered.");
                        return null;
                    }
                }

                return new GraphOperationProvisionManifest(
                    GraphLayoutIdentity.Create(
                        _source.GraphId,
                        _catalog.Fingerprint,
                        2UL,
                        ids),
                    registrations);
            }

            private ContextFrameStateRequirementManifest BuildContextManifest()
            {
                StateBlockId[] ids = Sorted(_contextStateRequirements);
                var blocks = new GraphStateBlockRegistration[ids.Length];
                var slots = new IGraphStateSlotRegistration[ids.Length][];
                for (int index = 0; index < ids.Length; index++)
                {
                    if (!_catalog.TryGetBlock(ids[index], out blocks[index]))
                    {
                        AddManifestError("ContextFrame StateBlock Requirement is not registered.");
                        return null;
                    }

                    slots[index] = _catalog.GetSlots(ids[index]);
                }

                var manifest = new ContextFrameStateRequirementManifest(
                    GraphLayoutIdentity.Create(
                        _source.GraphId,
                        _catalog.Fingerprint,
                        3UL,
                        ids),
                    _source.SchemaVersion,
                    blocks,
                    slots);
                if (!manifest.TryValidate(out CoCoDiagnostic diagnostic))
                {
                    Diagnostics.Add(new GraphDiagnostic(
                        diagnostic,
                        new GraphDiagnosticLocation(
                            GraphElementKind.Manifest,
                            GraphField.Manifest,
                            CurrentGraphId,
                            default,
                            default,
                            default,
                            -1,
                            -1,
                            -1)));
                    return null;
                }

                return manifest;
            }

            private void AddRequirements(StateDescriptor descriptor)
            {
                AddAll(_intentRequirements, descriptor.IntentRequirements);
                AddAll(_operationProvides, descriptor.OperationProvides);
                AddAll(_contextStateRequirements, descriptor.ContextStateRequirements);
            }

            private static void AddAll<T>(HashSet<T> destination, IReadOnlyList<T> source)
            {
                for (int index = 0; index < source.Count; index++)
                {
                    destination.Add(source[index]);
                }
            }

            private bool HasLayerStructuralErrors(int layerIndex)
            {
                for (int index = 0; index < _structuralDiagnosticCount; index++)
                {
                    if (Diagnostics[index].IsError && Diagnostics[index].Location.LayerIndex == layerIndex)
                    {
                        return true;
                    }
                }

                return false;
            }

            private void AddManifestError(string message)
            {
                AddError(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.ManifestConflict,
                    message,
                    new GraphDiagnosticLocation(
                        GraphElementKind.Manifest,
                        GraphField.Manifest,
                        CurrentGraphId,
                        default,
                        default,
                        default,
                        -1,
                        -1,
                        -1));
            }

            private void AddError(
                CoCoDiagnosticDomain domain,
                CoCoDiagnosticCode code,
                string message,
                GraphDiagnosticLocation location)
            {
                Diagnostics.Add(new GraphDiagnostic(CoCoDiagnostic.Error(domain, code, message), location));
            }

            private void AddWarning(
                CoCoDiagnosticDomain domain,
                CoCoDiagnosticCode code,
                string message,
                GraphDiagnosticLocation location)
            {
                Diagnostics.Add(new GraphDiagnostic(CoCoDiagnostic.Warning(domain, code, message), location));
            }

            private GraphId CurrentGraphId => _source?.GraphId ?? default;

            private GraphDiagnosticLocation GraphLocation(GraphField field) =>
                new GraphDiagnosticLocation(
                    GraphElementKind.Graph,
                    field,
                    CurrentGraphId,
                    default,
                    default,
                    default,
                    -1,
                    -1,
                    -1);

            private GraphDiagnosticLocation LayerLocation(
                int layerIndex,
                StateLayerSource layer,
                GraphField field) =>
                new GraphDiagnosticLocation(
                    GraphElementKind.Layer,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    default,
                    default,
                    layerIndex,
                    -1,
                    -1);

            private GraphDiagnosticLocation StateLocation(
                int layerIndex,
                int stateIndex,
                StateLayerSource layer,
                StateSource state,
                GraphField field) =>
                new GraphDiagnosticLocation(
                    GraphElementKind.State,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    state?.StateId ?? default,
                    default,
                    layerIndex,
                    state != null && ReferenceEquals(state, layer?.AnyState) ? -1 : stateIndex,
                    -1);

            private GraphDiagnosticLocation TransitionLocation(
                int layerIndex,
                int transitionIndex,
                StateLayerSource layer,
                TransitionSource transition,
                GraphField field) =>
                new GraphDiagnosticLocation(
                    GraphElementKind.Transition,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    transition?.SourceStateId ?? default,
                    transition?.TransitionId ?? default,
                    layerIndex,
                    -1,
                    transitionIndex);

            private GraphDiagnosticLocation EventAdapterDeclarationLocation(
                int declarationIndex) =>
                new GraphDiagnosticLocation(
                    GraphElementKind.EventAdapterDeclaration,
                    GraphField.EventAdapterDeclarations,
                    CurrentGraphId,
                    default,
                    default,
                    default,
                    -1,
                    -1,
                    -1,
                    declarationIndex);

            private static T[] Sorted<T>(HashSet<T> source)
            {
                var values = new T[source.Count];
                source.CopyTo(values);
                Array.Sort(values, (left, right) => StringComparer.Ordinal.Compare(
                    left.ToString(),
                    right.ToString()));
                return values;
            }

            private static bool TryFindState(
                StateLayerSource layer,
                StateId stateId,
                out StateSource state,
                out int stateIndex)
            {
                if (layer?.States != null)
                {
                    for (int index = 0; index < layer.States.Count; index++)
                    {
                        StateSource candidate = layer.States[index];
                        if (candidate != null && candidate.StateId == stateId)
                        {
                            state = candidate;
                            stateIndex = index;
                            return true;
                        }
                    }
                }

                state = null;
                stateIndex = -1;
                if (layer?.AnyState != null && layer.AnyState.StateId == stateId)
                {
                    state = layer.AnyState;
                    return true;
                }

                return false;
            }

            private static bool HasChildState(StateLayerSource layer, StateId stateId)
            {
                if (layer?.States == null)
                {
                    return false;
                }

                for (int index = 0; index < layer.States.Count; index++)
                {
                    StateSource candidate = layer.States[index];
                    if (candidate != null && candidate.ParentStateId == stateId)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static int[] GetChildren(
                StateSource[] states,
                Dictionary<StateId, int> indices,
                StateId parentId)
            {
                var children = new List<int>();
                for (int index = 0; index < states.Length; index++)
                {
                    if (states[index].ParentStateId == parentId)
                    {
                        children.Add(indices[states[index].StateId]);
                    }
                }

                children.Sort();
                return children.ToArray();
            }

            private static int[] GetRootPath(
                StateSource[] states,
                Dictionary<StateId, int> indices,
                int stateIndex)
            {
                var path = new Stack<int>();
                StateSource current = states[stateIndex];
                while (current != null)
                {
                    int index = indices[current.StateId];
                    path.Push(index);
                    if (!current.ParentStateId.IsValid)
                    {
                        break;
                    }

                    current = states[indices[current.ParentStateId]];
                }

                return path.ToArray();
            }

            private static void GetTransitionRange(
                CompiledTransition[] transitions,
                int stateIndex,
                out int first,
                out int count)
            {
                first = -1;
                count = 0;
                for (int index = 0; index < transitions.Length; index++)
                {
                    if (transitions[index].SourceStateIndex != stateIndex)
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = index;
                    }

                    count++;
                }
            }
        }
    }

    /// <summary>从实际的图合同计算各输入、输出和数据帧的布局标识。</summary>
    internal static class GraphLayoutIdentity
    {
        public static FrameLayoutId Create<T>(
            GraphId graphId,
            ulong catalogFingerprint,
            ulong domainTag,
            T[] ids)
            where T : struct
        {
            ulong high = GraphCatalogHash.OffsetBasis;
            ulong low = GraphCatalogHash.OffsetBasis ^ 0x9E3779B97F4A7C15UL;
            Add(ref high, ref low, graphId.High);
            Add(ref high, ref low, graphId.Low);
            Add(ref high, ref low, catalogFingerprint);
            Add(ref high, ref low, domainTag);
            Add(ref high, ref low, (ulong)ids.Length);
            for (int index = 0; index < ids.Length; index++)
            {
                string value = ids[index].ToString();
                for (int charIndex = 0; charIndex < value.Length; charIndex++)
                {
                    Add(ref high, ref low, value[charIndex]);
                }
            }

            if (!FrameLayoutId.TryCreate(high, low, out FrameLayoutId layoutId))
            {
                throw new InvalidOperationException("Deterministic Graph layout identity was invalid.");
            }

            return layoutId;
        }

        public static FrameLayoutId Create<TPrimary, TSecondary>(
            GraphId graphId,
            ulong catalogFingerprint,
            ulong domainTag,
            TPrimary[] primaryIds,
            TSecondary[] secondaryIds)
            where TPrimary : struct
            where TSecondary : struct
        {
            ulong high = GraphCatalogHash.OffsetBasis;
            ulong low = GraphCatalogHash.OffsetBasis ^ 0x9E3779B97F4A7C15UL;
            Add(ref high, ref low, graphId.High);
            Add(ref high, ref low, graphId.Low);
            Add(ref high, ref low, catalogFingerprint);
            Add(ref high, ref low, domainTag);
            AddValues(ref high, ref low, primaryIds);
            AddValues(ref high, ref low, secondaryIds);
            if (!FrameLayoutId.TryCreate(high, low, out FrameLayoutId layoutId))
            {
                throw new InvalidOperationException("Deterministic Graph layout identity was invalid.");
            }

            return layoutId;
        }

        private static void AddValues<T>(ref ulong high, ref ulong low, T[] values)
            where T : struct
        {
            Add(ref high, ref low, (ulong)values.Length);
            for (int index = 0; index < values.Length; index++)
            {
                string value = values[index].ToString();
                Add(ref high, ref low, (ulong)value.Length);
                for (int charIndex = 0; charIndex < value.Length; charIndex++)
                {
                    Add(ref high, ref low, value[charIndex]);
                }
            }
        }

        private static void Add(ref ulong high, ref ulong low, ulong value)
        {
            GraphCatalogHash.Add(ref high, value);
            GraphCatalogHash.Add(ref low, value ^ 0xA0761D6478BD642FUL);
        }
    }
}
