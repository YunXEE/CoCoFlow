using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public sealed class CoCoStateGraphCompileResult
    {
        private readonly IReadOnlyList<CoCoGraphDiagnostic> _diagnostics;

        internal CoCoStateGraphCompileResult(
            CoCoCompiledStateGraph graph,
            ulong contentFingerprint,
            CoCoGraphDiagnostic[] diagnostics)
        {
            Graph = graph;
            ContentFingerprint = contentFingerprint;
            _diagnostics = Array.AsReadOnly((CoCoGraphDiagnostic[])diagnostics.Clone());
        }

        public CoCoCompiledStateGraph Graph { get; }
        public ulong ContentFingerprint { get; }
        public IReadOnlyList<CoCoGraphDiagnostic> Diagnostics => _diagnostics;
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
    }

    public sealed class CoCoStateGraphValidator
    {
        public IReadOnlyList<CoCoGraphDiagnostic> Validate(
            CoCoStateGraphSource source,
            CoCoGraphDescriptorCatalog catalog)
        {
            return new CoCoStateGraphCompiler().Compile(source, catalog).Diagnostics;
        }
    }

    public sealed class CoCoStateGraphCompiler
    {
        public const uint CurrentSchemaVersion = 1U;

        public CoCoStateGraphCompileResult Compile(
            CoCoStateGraphSource source,
            CoCoGraphDescriptorCatalog catalog)
        {
            var context = new CompilationContext(source, catalog);
            context.Validate();
            CoCoCompiledStateGraph graph = context.Build();
            if (context.HasErrors || graph == null)
            {
                return new CoCoStateGraphCompileResult(
                    null,
                    source?.ContentFingerprint ?? 0UL,
                    context.Diagnostics.ToArray());
            }

            return new CoCoStateGraphCompileResult(
                graph,
                source.ContentFingerprint,
                context.Diagnostics.ToArray());
        }

        private sealed class CompilationContext
        {
            private readonly CoCoStateGraphSource _source;
            private readonly CoCoGraphDescriptorCatalog _catalog;
            private readonly Dictionary<CoCoStateId, int> _stateLayers = new Dictionary<CoCoStateId, int>();
            private readonly HashSet<CoCoTransitionId> _transitionIds = new HashSet<CoCoTransitionId>();
            private readonly HashSet<CoCoIntentId> _intentRequirements = new HashSet<CoCoIntentId>();
            private readonly HashSet<CoCoOperationSectionId> _operationProvides =
                new HashSet<CoCoOperationSectionId>();
            private readonly HashSet<CoCoStateBlockId> _contextStateRequirements =
                new HashSet<CoCoStateBlockId>();
            private readonly List<ICoCoGraphEventToIntentDeclarationRegistration>
                _eventAdapterDeclarations =
                    new List<ICoCoGraphEventToIntentDeclarationRegistration>();
            private CoCoIntentRequirementManifest _intentManifest;
            private CoCoGraphOperationProvidesManifest _operationManifest;
            private CoCoContextFrameStateRequirementManifest _contextManifest;
            private int _structuralDiagnosticCount;

            public CompilationContext(CoCoStateGraphSource source, CoCoGraphDescriptorCatalog catalog)
            {
                _source = source;
                _catalog = catalog;
            }

            public List<CoCoGraphDiagnostic> Diagnostics { get; } = new List<CoCoGraphDiagnostic>();

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
                        GraphLocation(CoCoGraphField.None));
                    return;
                }

                // Phase 1: schema and stable identities.
                if (_source.SchemaVersion != CurrentSchemaVersion)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.UnsupportedSchemaVersion,
                        "StateGraph schema version is not supported.",
                        GraphLocation(CoCoGraphField.SchemaVersion));
                }

                if (_source.ContentFingerprint == 0UL)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.UnsupportedSchemaVersion,
                        "StateGraph content fingerprint must be non-zero.",
                        GraphLocation(CoCoGraphField.ContentFingerprint));
                }

                if (!_source.GraphId.IsValid)
                {
                    AddError(
                        CoCoDiagnosticDomain.Identity,
                        CoCoDiagnosticCode.InvalidIdentifier,
                        "StateGraph GraphId must be valid.",
                        GraphLocation(CoCoGraphField.Identifier));
                }

                ValidateLayerAndGlobalIdentities();

                // Phase 2: topology shape and all graph-local references.
                ValidateTopologyAndReferences();
                _structuralDiagnosticCount = Diagnostics.Count;

                // Phase 3: descriptor resolution and frozen-config compatibility.
                ValidateDescriptorsAndConfigs();
                ValidateEventAdapterDeclarations();

                // Phase 4: reachability warnings, only for structurally coherent layers.
                ValidateReachability();

                // Phase 5: aggregate every manifest that remains safe to derive. This runs even
                // when an earlier phase emitted errors so manifest conflicts are not hidden.
                AggregateManifests();
            }

            public CoCoCompiledStateGraph Build()
            {
                // Phase 6: no compiled artifact exists unless every earlier phase is error-free.
                if (HasErrors || _intentManifest == null || _operationManifest == null || _contextManifest == null)
                {
                    return null;
                }

                var layerSources = new CoCoStateLayerSource[_source.Layers.Count];
                for (int index = 0; index < _source.Layers.Count; index++)
                {
                    layerSources[index] = _source.Layers[index];
                }

                Array.Sort(layerSources, (left, right) => StringComparer.Ordinal.Compare(
                    left.LayerId.ToString(),
                    right.LayerId.ToString()));
                var layers = new CoCoCompiledStateLayer[layerSources.Length];
                for (int layerIndex = 0; layerIndex < layerSources.Length; layerIndex++)
                {
                    layers[layerIndex] = BuildLayer(layerSources[layerIndex], layerIndex);
                }

                return new CoCoCompiledStateGraph(
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

                var layerIds = new HashSet<CoCoLayerId>();
                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    CoCoStateLayerSource layer = _source.Layers[layerIndex];
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
                            LayerLocation(layerIndex, layer, CoCoGraphField.Identifier));
                    }
                    else if (!layerIds.Add(layer.LayerId))
                    {
                        AddError(
                            CoCoDiagnosticDomain.Identity,
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "LayerIds must be unique within a Graph.",
                            LayerLocation(layerIndex, layer, CoCoGraphField.Identifier));
                    }

                    if (layer.States == null)
                    {
                        continue;
                    }

                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        CoCoStateSource state = layer.States[stateIndex];
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
                                StateLocation(layerIndex, stateIndex, layer, state, CoCoGraphField.Identifier));
                        }
                        else if (_stateLayers.ContainsKey(state.StateId))
                        {
                            AddError(
                                CoCoDiagnosticDomain.Identity,
                                CoCoDiagnosticCode.DuplicateIdentifier,
                                "StateIds must be unique across the complete Graph.",
                                StateLocation(layerIndex, stateIndex, layer, state, CoCoGraphField.Identifier));
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
                        CoCoTransitionSource transition = layer.Transitions[transitionIndex];
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
                                    CoCoGraphField.Identifier));
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
                                    CoCoGraphField.Identifier));
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
                        GraphLocation(CoCoGraphField.None));
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    ValidateLayerTopology(_source.Layers[layerIndex], layerIndex);
                }
            }

            private void ValidateLayerTopology(CoCoStateLayerSource layer, int layerIndex)
            {
                if (layer == null)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph contains a null Layer.",
                        LayerLocation(layerIndex, null, CoCoGraphField.None));
                    return;
                }

                if (layer.States == null || layer.States.Count == 0)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "A Layer must declare at least one State.",
                        LayerLocation(layerIndex, layer, CoCoGraphField.None));
                    return;
                }

                if (layer.Transitions == null)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "A Layer Transition collection must be declared; use an empty collection when needed.",
                        LayerLocation(layerIndex, layer, CoCoGraphField.None));
                }

                var localStates = new Dictionary<CoCoStateId, int>();
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoStateSource state = layer.States[stateIndex];
                    if (state == null)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null State.",
                            StateLocation(layerIndex, stateIndex, layer, null, CoCoGraphField.None));
                        continue;
                    }

                    if (state.StateId.IsValid && !localStates.ContainsKey(state.StateId))
                    {
                        localStates.Add(state.StateId, stateIndex);
                    }
                }

                ValidateLayerInitialState(layer, layerIndex, localStates);
                ValidateHierarchy(layer, layerIndex, localStates);
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
                        GraphLocation(CoCoGraphField.Descriptor));
                    return;
                }

                if (_source.Layers == null)
                {
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    CoCoStateLayerSource layer = _source.Layers[layerIndex];
                    if (layer?.States != null)
                    {
                        for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                        {
                            CoCoStateSource state = layer.States[stateIndex];
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
                        CoCoTransitionSource transition = layer.Transitions[transitionIndex];
                        if (transition != null)
                        {
                            ValidateConditionDescriptors(layer, layerIndex, transition, transitionIndex);
                        }
                    }
                }
            }

            private void ValidateStateDescriptor(
                CoCoStateLayerSource layer,
                int layerIndex,
                CoCoStateSource state,
                int stateIndex)
            {
                if (!state.DescriptorId.IsValid ||
                    !_catalog.TryGetStateDescriptor(state.DescriptorId, out CoCoStateDescriptor descriptor))
                {
                    AddError(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.MissingDescriptor,
                        "State descriptor is not registered.",
                        StateLocation(layerIndex, stateIndex, layer, state, CoCoGraphField.Descriptor));
                    return;
                }

                AddRequirements(descriptor);
                if (!_catalog.AcceptsStateConfig(state.DescriptorId, state.Config))
                {
                    AddError(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.DescriptorTypeMismatch,
                        "State frozen config does not match its descriptor.",
                        StateLocation(layerIndex, stateIndex, layer, state, CoCoGraphField.Config));
                }
            }

            private void ValidateEventAdapterDeclarations()
            {
                if (_catalog == null || !_catalog.IsFrozen)
                {
                    return;
                }

                var seen = new HashSet<CoCoEventToIntentDeclarationKey>();
                var countsByIntent = new Dictionary<CoCoIntentId, int>();
                for (int declarationIndex = 0;
                     declarationIndex < _source.EventAdapterDeclarations.Count;
                     declarationIndex++)
                {
                    CoCoEventToIntentDeclarationSource declaration =
                        _source.EventAdapterDeclarations[declarationIndex];
                    CoCoGraphDiagnosticLocation location =
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

                    if (!CoCoEventToIntentDeclarationKey.TryCreate(
                            declaration.EventTypeId,
                            declaration.ProvidedIntentId,
                            out CoCoEventToIntentDeclarationKey key))
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
                            out ICoCoGraphEventToIntentDeclarationRegistration registration))
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
                            out ICoCoGraphIntentRegistration intentRegistration) ||
                        intentRegistration.ValueType != registration.ProvidedIntentType)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.ManifestConflict,
                            "Event Adapter declaration does not match its provided Intent registration.",
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

                _eventAdapterDeclarations.Sort((left, right) => left.Key.CompareTo(right.Key));
            }

            private void ValidateLayerInitialState(
                CoCoStateLayerSource layer,
                int layerIndex,
                Dictionary<CoCoStateId, int> localStates)
            {
                if (!layer.InitialStateId.IsValid ||
                    !localStates.TryGetValue(layer.InitialStateId, out int initialIndex))
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.InvalidInitialState,
                        "Layer InitialStateId must resolve to a State in the same Layer.",
                        LayerLocation(layerIndex, layer, CoCoGraphField.InitialState));
                    return;
                }

                CoCoStateSource initial = layer.States[initialIndex];
                if (initial == null || initial.ParentStateId.IsValid)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.InvalidInitialState,
                        "Layer InitialStateId must point to a root State.",
                        LayerLocation(layerIndex, layer, CoCoGraphField.InitialState));
                }
            }

            private void ValidateHierarchy(
                CoCoStateLayerSource layer,
                int layerIndex,
                Dictionary<CoCoStateId, int> localStates)
            {
                var children = new Dictionary<CoCoStateId, List<CoCoStateId>>();
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoStateSource state = layer.States[stateIndex];
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
                                    CoCoGraphField.ParentState));
                        }
                        else
                        {
                            if (!children.TryGetValue(state.ParentStateId, out List<CoCoStateId> list))
                            {
                                list = new List<CoCoStateId>();
                                children.Add(state.ParentStateId, list);
                            }

                            list.Add(state.StateId);
                        }
                    }

                    DetectParentCycle(layer, layerIndex, stateIndex, state, localStates);
                }

                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoStateSource state = layer.States[stateIndex];
                    if (state == null || !state.StateId.IsValid)
                    {
                        continue;
                    }

                    bool hasChildren = children.TryGetValue(state.StateId, out List<CoCoStateId> childIds) &&
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
                                CoCoGraphField.InitialChildState));
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
                                CoCoGraphField.InitialChildState));
                    }
                }
            }

            private void DetectParentCycle(
                CoCoStateLayerSource layer,
                int layerIndex,
                int stateIndex,
                CoCoStateSource state,
                Dictionary<CoCoStateId, int> localStates)
            {
                var visited = new HashSet<CoCoStateId>();
                CoCoStateSource current = state;
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
                                CoCoGraphField.ParentState));
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
                CoCoStateLayerSource layer,
                int layerIndex,
                Dictionary<CoCoStateId, int> localStates)
            {
                if (layer.Transitions == null)
                {
                    return;
                }

                for (int transitionIndex = 0;
                     transitionIndex < layer.Transitions.Count;
                     transitionIndex++)
                {
                    CoCoTransitionSource transition = layer.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null Transition.",
                            TransitionLocation(layerIndex, transitionIndex, layer, null, CoCoGraphField.None));
                        continue;
                    }

                    ValidateTransitionEndpoint(
                        layer,
                        layerIndex,
                        transition,
                        transitionIndex,
                        transition.SourceStateId,
                        CoCoGraphField.SourceState,
                        localStates);
                    ValidateTransitionEndpoint(
                        layer,
                        layerIndex,
                        transition,
                        transitionIndex,
                        transition.TargetStateId,
                        CoCoGraphField.TargetState,
                        localStates);

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
                                CoCoGraphField.Window));
                    }

                    bool validInterrupt =
                        (transition.InterruptPolicy ==
                         CoCoTransitionInterruptPolicy.RequireSourceCompletion &&
                         transition.Window.Mode == CoCoTransitionWindowMode.Always) ||
                        (transition.InterruptPolicy ==
                         CoCoTransitionInterruptPolicy.AllowDuringSourceActivation &&
                         transition.Window.IsValid);
                    if (!validInterrupt)
                    {
                        AddError(
                            CoCoDiagnosticDomain.State,
                            CoCoDiagnosticCode.InvalidInterruptPolicy,
                            "Transition InterruptPolicy is incompatible with its Window.",
                            TransitionLocation(
                                layerIndex,
                                transitionIndex,
                                layer,
                                transition,
                                CoCoGraphField.InterruptPolicy));
                    }

                    ValidateConditionTopology(layer, layerIndex, transition, transitionIndex);
                }
            }

            private void ValidateTransitionEndpoint(
                CoCoStateLayerSource layer,
                int layerIndex,
                CoCoTransitionSource transition,
                int transitionIndex,
                CoCoStateId stateId,
                CoCoGraphField field,
                Dictionary<CoCoStateId, int> localStates)
            {
                if (stateId.IsValid && localStates.ContainsKey(stateId))
                {
                    return;
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
            }

            private void ValidateConditionTopology(
                CoCoStateLayerSource layer,
                int layerIndex,
                CoCoTransitionSource transition,
                int transitionIndex)
            {
                if (transition.Conditions == null)
                {
                    AddError(
                        CoCoDiagnosticDomain.Topology,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "Transition Conditions must be declared; use an empty collection for logical true.",
                        TransitionLocation(
                            layerIndex,
                            transitionIndex,
                            layer,
                            transition,
                            CoCoGraphField.Conditions));
                    return;
                }

                for (int conditionIndex = 0;
                     conditionIndex < transition.Conditions.Count;
                     conditionIndex++)
                {
                    CoCoConditionSource condition = transition.Conditions[conditionIndex];
                    if (condition == null)
                    {
                        AddError(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Transition contains a null Condition.",
                            ConditionLocation(
                                layerIndex,
                                transitionIndex,
                                conditionIndex,
                                layer,
                                transition,
                                CoCoGraphField.None));
                    }
                }
            }

            private void ValidateConditionDescriptors(
                CoCoStateLayerSource layer,
                int layerIndex,
                CoCoTransitionSource transition,
                int transitionIndex)
            {
                if (transition.Conditions == null)
                {
                    return;
                }

                for (int conditionIndex = 0;
                     conditionIndex < transition.Conditions.Count;
                     conditionIndex++)
                {
                    CoCoConditionSource condition = transition.Conditions[conditionIndex];
                    if (condition == null)
                    {
                        continue;
                    }

                    if (!condition.DescriptorId.IsValid ||
                        !_catalog.TryGetConditionDescriptor(
                            condition.DescriptorId,
                            out CoCoConditionDescriptor descriptor))
                    {
                        AddError(
                            CoCoDiagnosticDomain.State,
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Condition descriptor is not registered.",
                            ConditionLocation(
                                layerIndex,
                                transitionIndex,
                                conditionIndex,
                                layer,
                                transition,
                                CoCoGraphField.Descriptor));
                        continue;
                    }

                    AddRequirements(descriptor);
                    if (!_catalog.AcceptsConditionConfig(condition.DescriptorId, condition.Config))
                    {
                        AddError(
                            CoCoDiagnosticDomain.State,
                            CoCoDiagnosticCode.DescriptorTypeMismatch,
                            "Condition frozen config does not match its descriptor.",
                            ConditionLocation(
                                layerIndex,
                                transitionIndex,
                                conditionIndex,
                                layer,
                                transition,
                                CoCoGraphField.Config));
                    }
                }
            }

            private void ValidateReachability()
            {
                if (_source.Layers == null)
                {
                    return;
                }

                for (int layerIndex = 0; layerIndex < _source.Layers.Count; layerIndex++)
                {
                    CoCoStateLayerSource layer = _source.Layers[layerIndex];
                    if (layer?.States == null || layer.States.Count == 0 ||
                        HasLayerStructuralErrors(layerIndex))
                    {
                        continue;
                    }

                    var localStates = new Dictionary<CoCoStateId, int>();
                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        CoCoStateSource state = layer.States[stateIndex];
                        if (state != null && state.StateId.IsValid && !localStates.ContainsKey(state.StateId))
                        {
                            localStates.Add(state.StateId, stateIndex);
                        }
                    }

                    AddUnreachableWarnings(layer, layerIndex, localStates);
                }
            }

            private void AddUnreachableWarnings(
                CoCoStateLayerSource layer,
                int layerIndex,
                Dictionary<CoCoStateId, int> localStates)
            {
                var reachable = new HashSet<CoCoStateId>();
                var pending = new Queue<CoCoStateId>();
                MarkPathAndInitialChain(layer, localStates, layer.InitialStateId, reachable, pending);
                while (pending.Count > 0)
                {
                    CoCoStateId sourceId = pending.Dequeue();
                    if (layer.Transitions == null)
                    {
                        continue;
                    }

                    for (int index = 0; index < layer.Transitions.Count; index++)
                    {
                        CoCoTransitionSource transition = layer.Transitions[index];
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
                    CoCoStateSource state = layer.States[stateIndex];
                    if (state != null && state.StateId.IsValid && !reachable.Contains(state.StateId))
                    {
                        AddWarning(
                            CoCoDiagnosticDomain.Topology,
                            CoCoDiagnosticCode.UnreachableState,
                            "State is not reachable from the Layer InitialState.",
                            StateLocation(layerIndex, stateIndex, layer, state, CoCoGraphField.None));
                    }
                }
            }

            private static void MarkPathAndInitialChain(
                CoCoStateLayerSource layer,
                Dictionary<CoCoStateId, int> localStates,
                CoCoStateId stateId,
                HashSet<CoCoStateId> reachable,
                Queue<CoCoStateId> pending)
            {
                if (!localStates.TryGetValue(stateId, out int stateIndex))
                {
                    return;
                }

                var path = new Stack<CoCoStateId>();
                CoCoStateSource current = layer.States[stateIndex];
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
                CoCoStateId stateId,
                HashSet<CoCoStateId> reachable,
                Queue<CoCoStateId> pending)
            {
                if (reachable.Add(stateId))
                {
                    pending.Enqueue(stateId);
                }
            }

            private CoCoCompiledStateLayer BuildLayer(CoCoStateLayerSource layer, int layerIndex)
            {
                var stateSources = new CoCoStateSource[layer.States.Count];
                for (int index = 0; index < layer.States.Count; index++)
                {
                    stateSources[index] = layer.States[index];
                }

                Array.Sort(stateSources, (left, right) => StringComparer.Ordinal.Compare(
                    left.StateId.ToString(),
                    right.StateId.ToString()));
                var stateIndices = new Dictionary<CoCoStateId, int>(stateSources.Length);
                for (int index = 0; index < stateSources.Length; index++)
                {
                    stateIndices.Add(stateSources[index].StateId, index);
                }

                var transitionSources = new CoCoTransitionSource[layer.Transitions.Count];
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

                var transitions = new CoCoCompiledTransition[transitionSources.Length];
                for (int index = 0; index < transitionSources.Length; index++)
                {
                    CoCoTransitionSource source = transitionSources[index];
                    var conditions = new CoCoCompiledCondition[source.Conditions.Count];
                    for (int conditionIndex = 0; conditionIndex < source.Conditions.Count; conditionIndex++)
                    {
                        CoCoConditionSource condition = source.Conditions[conditionIndex];
                        _catalog.TryGetConditionDescriptor(condition.DescriptorId, out CoCoConditionDescriptor descriptor);
                        conditions[conditionIndex] = new CoCoCompiledCondition(
                            descriptor,
                            condition.Config,
                            conditionIndex);
                    }

                    transitions[index] = new CoCoCompiledTransition(
                        source.TransitionId,
                        index,
                        stateIndices[source.SourceStateId],
                        stateIndices[source.TargetStateId],
                        source.Priority,
                        source.Window,
                        source.InterruptPolicy,
                        conditions);
                }

                var states = new CoCoCompiledState[stateSources.Length];
                for (int index = 0; index < stateSources.Length; index++)
                {
                    CoCoStateSource source = stateSources[index];
                    _catalog.TryGetStateDescriptor(source.DescriptorId, out CoCoStateDescriptor descriptor);
                    int parentIndex = source.ParentStateId.IsValid ? stateIndices[source.ParentStateId] : -1;
                    int initialChildIndex = source.InitialChildStateId.IsValid
                        ? stateIndices[source.InitialChildStateId]
                        : -1;
                    int[] children = GetChildren(stateSources, stateIndices, source.StateId);
                    int[] path = GetRootPath(stateSources, stateIndices, index);
                    GetTransitionRange(transitions, index, out int firstTransition, out int transitionCount);
                    states[index] = new CoCoCompiledState(
                        source.StateId,
                        index,
                        parentIndex,
                        initialChildIndex,
                        descriptor,
                        source.Config,
                        children,
                        path,
                        firstTransition,
                        transitionCount);
                }

                return new CoCoCompiledStateLayer(
                    layer.LayerId,
                    layerIndex,
                    stateIndices[layer.InitialStateId],
                    states,
                    transitions);
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

            private CoCoIntentRequirementManifest BuildIntentManifest()
            {
                CoCoIntentId[] ids = Sorted(_intentRequirements);
                var registrations = new ICoCoGraphIntentRegistration[ids.Length];
                for (int index = 0; index < ids.Length; index++)
                {
                    if (!_catalog.TryGetIntent(ids[index], out registrations[index]))
                    {
                        AddManifestError("Intent Requirement is not registered.");
                        return null;
                    }
                }

                ICoCoGraphEventToIntentDeclarationRegistration[] declarations =
                    _eventAdapterDeclarations.ToArray();
                var declarationKeys = new CoCoEventToIntentDeclarationKey[declarations.Length];
                for (int index = 0; index < declarations.Length; index++)
                {
                    declarationKeys[index] = declarations[index].Key;
                }

                return new CoCoIntentRequirementManifest(
                    CoCoGraphLayoutIdentity.Create(
                        _source.GraphId,
                        _catalog.Fingerprint,
                        1UL,
                        ids,
                        declarationKeys),
                    registrations,
                    declarations);
            }

            private CoCoGraphOperationProvidesManifest BuildOperationManifest()
            {
                CoCoOperationSectionId[] ids = Sorted(_operationProvides);
                var registrations = new ICoCoGraphOperationRegistration[ids.Length];
                for (int index = 0; index < ids.Length; index++)
                {
                    if (!_catalog.TryGetOperation(ids[index], out registrations[index]))
                    {
                        AddManifestError("Graph Operation Provides entry is not registered.");
                        return null;
                    }
                }

                return new CoCoGraphOperationProvidesManifest(
                    CoCoGraphLayoutIdentity.Create(
                        _source.GraphId,
                        _catalog.Fingerprint,
                        2UL,
                        ids),
                    registrations);
            }

            private CoCoContextFrameStateRequirementManifest BuildContextManifest()
            {
                CoCoStateBlockId[] ids = Sorted(_contextStateRequirements);
                var blocks = new CoCoGraphStateBlockRegistration[ids.Length];
                var slots = new ICoCoGraphStateSlotRegistration[ids.Length][];
                for (int index = 0; index < ids.Length; index++)
                {
                    if (!_catalog.TryGetBlock(ids[index], out blocks[index]))
                    {
                        AddManifestError("ContextFrame StateBlock Requirement is not registered.");
                        return null;
                    }

                    slots[index] = _catalog.GetSlots(ids[index]);
                }

                var manifest = new CoCoContextFrameStateRequirementManifest(
                    CoCoGraphLayoutIdentity.Create(
                        _source.GraphId,
                        _catalog.Fingerprint,
                        3UL,
                        ids),
                    _source.SchemaVersion,
                    blocks,
                    slots);
                if (!manifest.TryValidate(out CoCoDiagnostic diagnostic))
                {
                    Diagnostics.Add(new CoCoGraphDiagnostic(
                        diagnostic,
                        new CoCoGraphDiagnosticLocation(
                            CoCoGraphElementKind.Manifest,
                            CoCoGraphField.Manifest,
                            CurrentGraphId,
                            default,
                            default,
                            default,
                            -1,
                            -1,
                            -1,
                            -1)));
                    return null;
                }

                return manifest;
            }

            private void AddRequirements(CoCoStateDescriptor descriptor)
            {
                AddAll(_intentRequirements, descriptor.IntentRequirements);
                AddAll(_operationProvides, descriptor.OperationProvides);
                AddAll(_contextStateRequirements, descriptor.ContextStateRequirements);
            }

            private void AddRequirements(CoCoConditionDescriptor descriptor)
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
                    new CoCoGraphDiagnosticLocation(
                        CoCoGraphElementKind.Manifest,
                        CoCoGraphField.Manifest,
                        CurrentGraphId,
                        default,
                        default,
                        default,
                        -1,
                        -1,
                        -1,
                        -1));
            }

            private void AddError(
                CoCoDiagnosticDomain domain,
                CoCoDiagnosticCode code,
                string message,
                CoCoGraphDiagnosticLocation location)
            {
                Diagnostics.Add(new CoCoGraphDiagnostic(CoCoDiagnostic.Error(domain, code, message), location));
            }

            private void AddWarning(
                CoCoDiagnosticDomain domain,
                CoCoDiagnosticCode code,
                string message,
                CoCoGraphDiagnosticLocation location)
            {
                Diagnostics.Add(new CoCoGraphDiagnostic(CoCoDiagnostic.Warning(domain, code, message), location));
            }

            private CoCoGraphId CurrentGraphId => _source?.GraphId ?? default;

            private CoCoGraphDiagnosticLocation GraphLocation(CoCoGraphField field) =>
                new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.Graph,
                    field,
                    CurrentGraphId,
                    default,
                    default,
                    default,
                    -1,
                    -1,
                    -1,
                    -1);

            private CoCoGraphDiagnosticLocation LayerLocation(
                int layerIndex,
                CoCoStateLayerSource layer,
                CoCoGraphField field) =>
                new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.Layer,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    default,
                    default,
                    layerIndex,
                    -1,
                    -1,
                    -1);

            private CoCoGraphDiagnosticLocation StateLocation(
                int layerIndex,
                int stateIndex,
                CoCoStateLayerSource layer,
                CoCoStateSource state,
                CoCoGraphField field) =>
                new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.State,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    state?.StateId ?? default,
                    default,
                    layerIndex,
                    stateIndex,
                    -1,
                    -1);

            private CoCoGraphDiagnosticLocation TransitionLocation(
                int layerIndex,
                int transitionIndex,
                CoCoStateLayerSource layer,
                CoCoTransitionSource transition,
                CoCoGraphField field) =>
                new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.Transition,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    transition?.SourceStateId ?? default,
                    transition?.TransitionId ?? default,
                    layerIndex,
                    -1,
                    transitionIndex,
                    -1);

            private CoCoGraphDiagnosticLocation ConditionLocation(
                int layerIndex,
                int transitionIndex,
                int conditionIndex,
                CoCoStateLayerSource layer,
                CoCoTransitionSource transition,
                CoCoGraphField field) =>
                new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.Condition,
                    field,
                    CurrentGraphId,
                    layer?.LayerId ?? default,
                    transition?.SourceStateId ?? default,
                    transition?.TransitionId ?? default,
                    layerIndex,
                    -1,
                    transitionIndex,
                    conditionIndex);

            private CoCoGraphDiagnosticLocation EventAdapterDeclarationLocation(
                int declarationIndex) =>
                new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.EventAdapterDeclaration,
                    CoCoGraphField.EventAdapterDeclarations,
                    CurrentGraphId,
                    default,
                    default,
                    default,
                    -1,
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

            private static int[] GetChildren(
                CoCoStateSource[] states,
                Dictionary<CoCoStateId, int> indices,
                CoCoStateId parentId)
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
                CoCoStateSource[] states,
                Dictionary<CoCoStateId, int> indices,
                int stateIndex)
            {
                var path = new Stack<int>();
                CoCoStateSource current = states[stateIndex];
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
                CoCoCompiledTransition[] transitions,
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

    internal static class CoCoGraphLayoutIdentity
    {
        public static CoCoFrameLayoutId Create<T>(
            CoCoGraphId graphId,
            ulong catalogFingerprint,
            ulong domainTag,
            T[] ids)
            where T : struct
        {
            ulong high = CoCoGraphCatalogHash.OffsetBasis;
            ulong low = CoCoGraphCatalogHash.OffsetBasis ^ 0x9E3779B97F4A7C15UL;
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

            if (!CoCoFrameLayoutId.TryCreate(high, low, out CoCoFrameLayoutId layoutId))
            {
                throw new InvalidOperationException("Deterministic Graph layout identity was invalid.");
            }

            return layoutId;
        }

        public static CoCoFrameLayoutId Create<TPrimary, TSecondary>(
            CoCoGraphId graphId,
            ulong catalogFingerprint,
            ulong domainTag,
            TPrimary[] primaryIds,
            TSecondary[] secondaryIds)
            where TPrimary : struct
            where TSecondary : struct
        {
            ulong high = CoCoGraphCatalogHash.OffsetBasis;
            ulong low = CoCoGraphCatalogHash.OffsetBasis ^ 0x9E3779B97F4A7C15UL;
            Add(ref high, ref low, graphId.High);
            Add(ref high, ref low, graphId.Low);
            Add(ref high, ref low, catalogFingerprint);
            Add(ref high, ref low, domainTag);
            AddValues(ref high, ref low, primaryIds);
            AddValues(ref high, ref low, secondaryIds);
            if (!CoCoFrameLayoutId.TryCreate(high, low, out CoCoFrameLayoutId layoutId))
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
            CoCoGraphCatalogHash.Add(ref high, value);
            CoCoGraphCatalogHash.Add(ref low, value ^ 0xA0761D6478BD642FUL);
        }
    }
}
