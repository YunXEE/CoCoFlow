using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    internal sealed class CoCoStateGraphAssetSnapshot
    {
        private readonly IReadOnlyList<CoCoGraphDiagnostic> preflightDiagnostics;
        private readonly IReadOnlyList<CoCoGraphDiagnostic> adapterDiagnostics;

        internal CoCoStateGraphAssetSnapshot(
            CoCoStateGraphSource source,
            ulong contentFingerprint,
            ulong cacheFingerprint,
            IReadOnlyList<CoCoGraphDiagnostic> preflightDiagnostics,
            IReadOnlyList<CoCoGraphDiagnostic> adapterDiagnostics,
            bool freezingSkippedGlobally)
        {
            Source = source;
            ContentFingerprint = contentFingerprint;
            CacheFingerprint = cacheFingerprint;
            this.preflightDiagnostics = CopyDiagnostics(preflightDiagnostics);
            this.adapterDiagnostics = CopyDiagnostics(adapterDiagnostics);
            FreezingSkippedGlobally = freezingSkippedGlobally;
        }

        internal CoCoStateGraphSource Source { get; }
        internal ulong ContentFingerprint { get; }
        internal ulong CacheFingerprint { get; }
        internal IReadOnlyList<CoCoGraphDiagnostic> PreflightDiagnostics => preflightDiagnostics;
        internal IReadOnlyList<CoCoGraphDiagnostic> AdapterDiagnostics => adapterDiagnostics;
        internal bool FreezingSkippedGlobally { get; }

        private static IReadOnlyList<CoCoGraphDiagnostic> CopyDiagnostics(
            IReadOnlyList<CoCoGraphDiagnostic> source)
        {
            var copy = new CoCoGraphDiagnostic[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }

    internal static class CoCoStateGraphAssetSnapshotBuilder
    {
        private sealed class LayerConfigFingerprints
        {
            internal LayerConfigFingerprints(int stateCount, int transitionCount)
            {
                States = new ulong[stateCount];
                Conditions = new ulong[transitionCount][];
                InvalidWindows = new ulong[transitionCount];
            }

            internal ulong[] States { get; }
            internal ulong[][] Conditions { get; }
            internal ulong[] InvalidWindows { get; }
        }

        internal static CoCoStateGraphAssetSnapshot Build(
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog,
            CoCoStateGraphManagedReferenceInspection managedReferenceInspection)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (managedReferenceInspection == null)
            {
                throw new ArgumentNullException(nameof(managedReferenceInspection));
            }

            var preflightDiagnostics = new List<CoCoGraphDiagnostic>();
            var adapterDiagnostics = new List<CoCoGraphDiagnostic>();
            var configFreezer = new CoCoStateGraphConfigFreezer(catalog);

            CoCoGraphId.TryCreate(
                asset.SerializedGraphId.High,
                asset.SerializedGraphId.Low,
                out CoCoGraphId graphId);
            bool allowFreezing = Preflight(
                asset,
                graphId,
                managedReferenceInspection,
                preflightDiagnostics);

            var layerSources = new CoCoStateLayerSource[asset.Layers.Count];
            var layerConfigFingerprints = new LayerConfigFingerprints[asset.Layers.Count];
            for (int layerIndex = 0; layerIndex < asset.Layers.Count; layerIndex++)
            {
                CoCoStateGraphLayerRecord layer = asset.Layers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                layerSources[layerIndex] = BuildLayer(
                    layer,
                    graphId,
                    layerIndex,
                    allowFreezing,
                    configFreezer,
                    preflightDiagnostics,
                    adapterDiagnostics,
                    out layerConfigFingerprints[layerIndex]);
            }

            CoCoEventToIntentDeclarationSource[] eventAdapterDeclarations =
                BuildEventAdapterDeclarations(asset);

            ulong contentFingerprint = ComputeContentFingerprint(
                asset.SchemaVersion,
                graphId,
                layerSources,
                layerConfigFingerprints,
                eventAdapterDeclarations);
            ulong cacheFingerprint = ComputeCacheFingerprint(
                contentFingerprint,
                preflightDiagnostics,
                managedReferenceInspection.Fingerprint,
                ComputeEventAdapterAuthoringFingerprint(eventAdapterDeclarations));
            var source = new CoCoStateGraphSource(
                asset.SchemaVersion,
                contentFingerprint,
                graphId,
                layerSources,
                eventAdapterDeclarations);
            return new CoCoStateGraphAssetSnapshot(
                source,
                source.ContentFingerprint,
                cacheFingerprint,
                preflightDiagnostics,
                adapterDiagnostics,
                !allowFreezing);
        }

        private static bool Preflight(
            CoCoStateGraphAsset asset,
            CoCoGraphId graphId,
            CoCoStateGraphManagedReferenceInspection managedReferenceInspection,
            ICollection<CoCoGraphDiagnostic> diagnostics)
        {
            bool globalIdentityIsValid = managedReferenceInspection.Diagnostics.Count == 0;
            for (int diagnosticIndex = 0;
                 diagnosticIndex < managedReferenceInspection.Diagnostics.Count;
                 diagnosticIndex++)
            {
                diagnostics.Add(managedReferenceInspection.Diagnostics[diagnosticIndex]);
            }
            if (asset.SchemaVersion != CoCoStateGraphCompiler.CurrentSchemaVersion)
            {
                diagnostics.Add(Error(
                    GraphLocation(graphId, CoCoGraphField.SchemaVersion),
                    CoCoDiagnosticCode.UnsupportedSchemaVersion,
                    $"StateGraph Asset schema {asset.SchemaVersion} is not supported."));
                globalIdentityIsValid = false;
            }

            if (!graphId.IsValid)
            {
                diagnostics.Add(Error(
                    GraphLocation(graphId, CoCoGraphField.Identifier),
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "StateGraph Asset has an invalid Graph ID."));
                globalIdentityIsValid = false;
            }

            if (string.IsNullOrWhiteSpace(asset.AssetGuidStamp))
            {
                diagnostics.Add(Error(
                    GraphLocation(graphId, CoCoGraphField.AssetGuidStamp),
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "StateGraph Asset has no Asset GUID identity stamp."));
                globalIdentityIsValid = false;
            }

            for (int layerIndex = 0; layerIndex < asset.Layers.Count; layerIndex++)
            {
                CoCoStateGraphLayerRecord layer = asset.Layers[layerIndex];
                if (layer == null)
                {
                    diagnostics.Add(Error(
                        Location(
                            CoCoGraphElementKind.Layer,
                            CoCoGraphField.None,
                            graphId,
                            default,
                            default,
                            default,
                            layerIndex,
                            -1,
                            -1,
                            -1),
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph contains a null Layer record."));
                    continue;
                }

                CoCoLayerId.TryCreate(layer.LayerId.High, layer.LayerId.Low, out CoCoLayerId layerId);
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoStateGraphStateRecord state = layer.States[stateIndex];
                    if (state == null)
                    {
                        diagnostics.Add(Error(
                            Location(
                                CoCoGraphElementKind.State,
                                CoCoGraphField.None,
                                graphId,
                                layerId,
                                default,
                                default,
                                layerIndex,
                                stateIndex,
                                -1,
                                -1),
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null State record."));
                        continue;
                    }

                    if (state.Config == null)
                    {
                        CoCoStateId.TryCreate(
                            state.StateId.High,
                            state.StateId.Low,
                            out CoCoStateId stateId);
                        CoCoGraphDiagnosticLocation configLocation = Location(
                                CoCoGraphElementKind.State,
                                CoCoGraphField.Config,
                                graphId,
                                layerId,
                                stateId,
                                default,
                                layerIndex,
                                stateIndex,
                                -1,
                                -1);
                        if (!managedReferenceInspection.ContainsLocation(configLocation))
                        {
                            diagnostics.Add(AuthoringError(
                                configLocation,
                                "State is missing its managed-reference Config."));
                        }
                    }
                }

                for (int transitionIndex = 0;
                     transitionIndex < layer.Transitions.Count;
                     transitionIndex++)
                {
                    CoCoStateGraphTransitionRecord transition = layer.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        diagnostics.Add(Error(
                            Location(
                                CoCoGraphElementKind.Transition,
                                CoCoGraphField.None,
                                graphId,
                                layerId,
                                default,
                                default,
                                layerIndex,
                                -1,
                                transitionIndex,
                                -1),
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null Transition record."));
                        continue;
                    }

                    CoCoTransitionId.TryCreate(
                        transition.TransitionId.High,
                        transition.TransitionId.Low,
                        out CoCoTransitionId transitionId);
                    CoCoStateId.TryCreate(
                        transition.SourceStateId.High,
                        transition.SourceStateId.Low,
                        out CoCoStateId sourceStateId);
                    for (int conditionIndex = 0;
                         conditionIndex < transition.Conditions.Count;
                         conditionIndex++)
                    {
                        CoCoStateGraphConditionRecord condition = transition.Conditions[conditionIndex];
                        if (condition == null)
                        {
                            diagnostics.Add(Error(
                                Location(
                                    CoCoGraphElementKind.Condition,
                                    CoCoGraphField.None,
                                    graphId,
                                    layerId,
                                    sourceStateId,
                                    transitionId,
                                    layerIndex,
                                    -1,
                                    transitionIndex,
                                    conditionIndex),
                                CoCoDiagnosticCode.MissingTopologyElement,
                                "Transition contains a null Condition record."));
                            continue;
                        }

                        if (condition.Config == null)
                        {
                            CoCoGraphDiagnosticLocation configLocation = Location(
                                    CoCoGraphElementKind.Condition,
                                    CoCoGraphField.Config,
                                    graphId,
                                    layerId,
                                    sourceStateId,
                                    transitionId,
                                    layerIndex,
                                    -1,
                                    transitionIndex,
                                    conditionIndex);
                            if (!managedReferenceInspection.ContainsLocation(configLocation))
                            {
                                diagnostics.Add(AuthoringError(
                                    configLocation,
                                    "Condition is missing its managed-reference Config."));
                            }
                        }
                    }
                }
            }

            for (int declarationIndex = 0;
                 declarationIndex < asset.EventAdapterDeclarations.Count;
                 declarationIndex++)
            {
                CoCoStateGraphEventAdapterDeclarationRecord declaration =
                    asset.EventAdapterDeclarations[declarationIndex];
                CoCoGraphDiagnosticLocation location = EventAdapterDeclarationLocation(
                    graphId,
                    declarationIndex);
                if (declaration == null)
                {
                    diagnostics.Add(Error(
                        location,
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph contains a null Event Adapter declaration."));
                    continue;
                }

                if (!declaration.EventTypeId.IsValid || !declaration.ProvidedIntentId.IsValid)
                {
                    diagnostics.Add(Error(
                        location,
                        CoCoDiagnosticCode.InvalidIdentifier,
                        "Event Adapter declaration requires valid EventTypeId and ProvidedIntentId values."));
                }
            }

            return globalIdentityIsValid;
        }

        private static CoCoEventToIntentDeclarationSource[] BuildEventAdapterDeclarations(
            CoCoStateGraphAsset asset)
        {
            var declarations =
                new CoCoEventToIntentDeclarationSource[asset.EventAdapterDeclarations.Count];
            for (int index = 0; index < asset.EventAdapterDeclarations.Count; index++)
            {
                CoCoStateGraphEventAdapterDeclarationRecord declaration =
                    asset.EventAdapterDeclarations[index];
                if (declaration == null)
                {
                    continue;
                }

                CoCoEventTypeId.TryCreate(
                    declaration.EventTypeId.High,
                    declaration.EventTypeId.Low,
                    out CoCoEventTypeId eventTypeId);
                CoCoIntentId.TryCreate(
                    declaration.ProvidedIntentId.High,
                    declaration.ProvidedIntentId.Low,
                    out CoCoIntentId providedIntentId);
                declarations[index] = new CoCoEventToIntentDeclarationSource(
                    eventTypeId,
                    providedIntentId);
            }

            return declarations;
        }

        private static CoCoStateLayerSource BuildLayer(
            CoCoStateGraphLayerRecord layer,
            CoCoGraphId graphId,
            int layerIndex,
            bool allowFreezing,
            CoCoStateGraphConfigFreezer configFreezer,
            ICollection<CoCoGraphDiagnostic> preflightDiagnostics,
            ICollection<CoCoGraphDiagnostic> adapterDiagnostics,
            out LayerConfigFingerprints configFingerprints)
        {
            CoCoLayerId.TryCreate(layer.LayerId.High, layer.LayerId.Low, out CoCoLayerId layerId);
            CoCoStateId.TryCreate(
                layer.InitialStateId.High,
                layer.InitialStateId.Low,
                out CoCoStateId initialStateId);

            configFingerprints = new LayerConfigFingerprints(
                layer.States.Count,
                layer.Transitions.Count);
            var stateSources = new CoCoStateSource[layer.States.Count];
            for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
            {
                CoCoStateGraphStateRecord state = layer.States[stateIndex];
                if (state == null)
                {
                    continue;
                }

                stateSources[stateIndex] = BuildState(
                    state,
                    graphId,
                    layerId,
                    layerIndex,
                    stateIndex,
                    allowFreezing,
                    configFreezer,
                    preflightDiagnostics,
                    adapterDiagnostics,
                    out configFingerprints.States[stateIndex]);
            }

            var transitionSources = new CoCoTransitionSource[layer.Transitions.Count];
            for (int transitionIndex = 0; transitionIndex < layer.Transitions.Count; transitionIndex++)
            {
                CoCoStateGraphTransitionRecord transition = layer.Transitions[transitionIndex];
                if (transition == null)
                {
                    continue;
                }

                transitionSources[transitionIndex] = BuildTransition(
                    transition,
                    graphId,
                    layerId,
                    layerIndex,
                    transitionIndex,
                    allowFreezing,
                    configFreezer,
                    preflightDiagnostics,
                    adapterDiagnostics,
                    out configFingerprints.Conditions[transitionIndex],
                    out configFingerprints.InvalidWindows[transitionIndex]);
            }

            return new CoCoStateLayerSource(
                layerId,
                initialStateId,
                stateSources,
                transitionSources);
        }

        private static CoCoStateSource BuildState(
            CoCoStateGraphStateRecord state,
            CoCoGraphId graphId,
            CoCoLayerId layerId,
            int layerIndex,
            int stateIndex,
            bool allowFreezing,
            CoCoStateGraphConfigFreezer configFreezer,
            ICollection<CoCoGraphDiagnostic> preflightDiagnostics,
            ICollection<CoCoGraphDiagnostic> adapterDiagnostics,
            out ulong configFingerprint)
        {
            CoCoStateId.TryCreate(state.StateId.High, state.StateId.Low, out CoCoStateId stateId);
            CoCoStateId.TryCreate(
                state.ParentStateId.High,
                state.ParentStateId.Low,
                out CoCoStateId parentStateId);
            CoCoStateId.TryCreate(
                state.InitialChildStateId.High,
                state.InitialChildStateId.Low,
                out CoCoStateId initialChildStateId);
            CoCoStateDescriptorId.TryCreate(
                state.StateDescriptorId.High,
                state.StateDescriptorId.Low,
                out CoCoStateDescriptorId descriptorId);

            var configLocation = Location(
                CoCoGraphElementKind.State,
                CoCoGraphField.Config,
                graphId,
                layerId,
                stateId,
                default,
                layerIndex,
                stateIndex,
                -1,
                -1);
            CoCoFrozenConfigSnapshot frozenConfig = null;
            ulong authoringFingerprint = state.Config == null
                ? 0UL
                : CoCoStateGraphConfigFreezer.ComputeAuthoringFingerprint(state.Config);
            configFingerprint = state.Config == null
                ? 0UL
                : ComputeFailedConfigFingerprint(authoringFingerprint, CoCoDiagnostic.None);
            if (!descriptorId.IsValid)
            {
                preflightDiagnostics.Add(Error(
                    Location(
                        CoCoGraphElementKind.State,
                        CoCoGraphField.Descriptor,
                        graphId,
                        layerId,
                        stateId,
                        default,
                        layerIndex,
                        stateIndex,
                        -1,
                        -1),
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "State has an invalid descriptor ID."));
            }
            else if (allowFreezing && state.Config != null)
            {
                if (!configFreezer.TryFreezeState(
                        descriptorId,
                        state.Config,
                        configLocation,
                        out frozenConfig,
                        out CoCoGraphDiagnostic configDiagnostic))
                {
                    adapterDiagnostics.Add(configDiagnostic);
                    configFingerprint = ComputeFailedConfigFingerprint(
                        authoringFingerprint,
                        configDiagnostic.Diagnostic);
                }
                else
                {
                    configFingerprint = frozenConfig.Fingerprint;
                }
            }

            return new CoCoStateSource(
                stateId,
                parentStateId,
                initialChildStateId,
                descriptorId,
                frozenConfig);
        }

        private static CoCoTransitionSource BuildTransition(
            CoCoStateGraphTransitionRecord transition,
            CoCoGraphId graphId,
            CoCoLayerId layerId,
            int layerIndex,
            int transitionIndex,
            bool allowFreezing,
            CoCoStateGraphConfigFreezer configFreezer,
            ICollection<CoCoGraphDiagnostic> preflightDiagnostics,
            ICollection<CoCoGraphDiagnostic> adapterDiagnostics,
            out ulong[] conditionFingerprints,
            out ulong invalidWindowFingerprint)
        {
            CoCoTransitionId.TryCreate(
                transition.TransitionId.High,
                transition.TransitionId.Low,
                out CoCoTransitionId transitionId);
            CoCoStateId.TryCreate(
                transition.SourceStateId.High,
                transition.SourceStateId.Low,
                out CoCoStateId sourceStateId);
            CoCoStateId.TryCreate(
                transition.TargetStateId.High,
                transition.TargetStateId.Low,
                out CoCoStateId targetStateId);

            bool hasValidWindow = CoCoTransitionWindow.TryCreate(
                transition.WindowMode,
                transition.WindowStartInclusive,
                transition.WindowEndExclusive,
                out CoCoTransitionWindow window);
            invalidWindowFingerprint = hasValidWindow
                ? 0UL
                : ComputeInvalidWindowFingerprint(
                    transition.WindowMode,
                    transition.WindowStartInclusive,
                    transition.WindowEndExclusive);

            conditionFingerprints = new ulong[transition.Conditions.Count];
            var conditionSources = new CoCoConditionSource[transition.Conditions.Count];
            for (int conditionIndex = 0; conditionIndex < transition.Conditions.Count; conditionIndex++)
            {
                CoCoStateGraphConditionRecord condition = transition.Conditions[conditionIndex];
                if (condition == null)
                {
                    continue;
                }

                conditionSources[conditionIndex] = BuildCondition(
                    condition,
                    graphId,
                    layerId,
                    sourceStateId,
                    transitionId,
                    layerIndex,
                    transitionIndex,
                    conditionIndex,
                    allowFreezing,
                    configFreezer,
                    preflightDiagnostics,
                    adapterDiagnostics,
                    out conditionFingerprints[conditionIndex]);
            }

            return new CoCoTransitionSource(
                transitionId,
                sourceStateId,
                targetStateId,
                transition.Priority,
                window,
                conditionSources);
        }

        private static CoCoConditionSource BuildCondition(
            CoCoStateGraphConditionRecord condition,
            CoCoGraphId graphId,
            CoCoLayerId layerId,
            CoCoStateId sourceStateId,
            CoCoTransitionId transitionId,
            int layerIndex,
            int transitionIndex,
            int conditionIndex,
            bool allowFreezing,
            CoCoStateGraphConfigFreezer configFreezer,
            ICollection<CoCoGraphDiagnostic> preflightDiagnostics,
            ICollection<CoCoGraphDiagnostic> adapterDiagnostics,
            out ulong configFingerprint)
        {
            CoCoConditionDescriptorId.TryCreate(
                condition.ConditionDescriptorId.High,
                condition.ConditionDescriptorId.Low,
                out CoCoConditionDescriptorId descriptorId);

            CoCoFrozenConfigSnapshot frozenConfig = null;
            ulong authoringFingerprint = condition.Config == null
                ? 0UL
                : CoCoStateGraphConfigFreezer.ComputeAuthoringFingerprint(condition.Config);
            configFingerprint = condition.Config == null
                ? 0UL
                : ComputeFailedConfigFingerprint(authoringFingerprint, CoCoDiagnostic.None);
            if (!descriptorId.IsValid)
            {
                preflightDiagnostics.Add(Error(
                    Location(
                        CoCoGraphElementKind.Condition,
                        CoCoGraphField.Descriptor,
                        graphId,
                        layerId,
                        sourceStateId,
                        transitionId,
                        layerIndex,
                        -1,
                        transitionIndex,
                        conditionIndex),
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "Condition has an invalid descriptor ID."));
            }
            else if (allowFreezing && condition.Config != null)
            {
                if (!configFreezer.TryFreezeCondition(
                        descriptorId,
                        condition.Config,
                        Location(
                            CoCoGraphElementKind.Condition,
                            CoCoGraphField.Config,
                            graphId,
                            layerId,
                            sourceStateId,
                            transitionId,
                            layerIndex,
                            -1,
                            transitionIndex,
                            conditionIndex),
                        out frozenConfig,
                        out CoCoGraphDiagnostic configDiagnostic))
                {
                    adapterDiagnostics.Add(configDiagnostic);
                    configFingerprint = ComputeFailedConfigFingerprint(
                        authoringFingerprint,
                        configDiagnostic.Diagnostic);
                }
                else
                {
                    configFingerprint = frozenConfig.Fingerprint;
                }
            }

            return new CoCoConditionSource(descriptorId, frozenConfig);
        }

        private static CoCoGraphDiagnosticLocation GraphLocation(
            CoCoGraphId graphId,
            CoCoGraphField field) =>
            Location(
                CoCoGraphElementKind.Graph,
                field,
                graphId,
                default,
                default,
                default,
                -1,
                -1,
                -1,
                -1);

        private static CoCoGraphDiagnosticLocation EventAdapterDeclarationLocation(
            CoCoGraphId graphId,
            int declarationIndex) =>
            new CoCoGraphDiagnosticLocation(
                CoCoGraphElementKind.EventAdapterDeclaration,
                CoCoGraphField.EventAdapterDeclarations,
                graphId,
                default,
                default,
                default,
                -1,
                -1,
                -1,
                -1,
                declarationIndex);

        private static CoCoGraphDiagnosticLocation Location(
            CoCoGraphElementKind elementKind,
            CoCoGraphField field,
            CoCoGraphId graphId,
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoTransitionId transitionId,
            int layerIndex,
            int stateIndex,
            int transitionIndex,
            int conditionIndex) =>
            new CoCoGraphDiagnosticLocation(
                elementKind,
                field,
                graphId,
                layerId,
                stateId,
                transitionId,
                layerIndex,
                stateIndex,
                transitionIndex,
                conditionIndex);

        private static CoCoGraphDiagnostic Error(
            CoCoGraphDiagnosticLocation location,
            CoCoDiagnosticCode code,
            string message) =>
            new CoCoGraphDiagnostic(
                CoCoDiagnostic.Error(CoCoDiagnosticDomain.Topology, code, message),
                location);

        private static CoCoGraphDiagnostic AuthoringError(
            CoCoGraphDiagnosticLocation location,
            string message) =>
            new CoCoGraphDiagnostic(
                CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidAuthoringDependency,
                    message),
                location);

        private static ulong ComputeContentFingerprint(
            uint schemaVersion,
            CoCoGraphId graphId,
            IReadOnlyList<CoCoStateLayerSource> layers,
            IReadOnlyList<LayerConfigFingerprints> configFingerprints,
            IReadOnlyList<CoCoEventToIntentDeclarationSource> eventAdapterDeclarations)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, schemaVersion);
            Add(ref hash, graphId.High);
            Add(ref hash, graphId.Low);
            Add(ref hash, layers?.Count ?? -1);
            if (layers == null)
            {
                AddEventAdapterDeclarations(ref hash, eventAdapterDeclarations);
                return NonZero(hash);
            }

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                CoCoStateLayerSource layer = layers[layerIndex];
                LayerConfigFingerprints layerConfigFingerprints =
                    configFingerprints != null && layerIndex < configFingerprints.Count
                        ? configFingerprints[layerIndex]
                        : null;
                if (layer == null)
                {
                    Add(ref hash, ulong.MaxValue);
                    continue;
                }

                Add(ref hash, layer.LayerId.High);
                Add(ref hash, layer.LayerId.Low);
                Add(ref hash, layer.InitialStateId.High);
                Add(ref hash, layer.InitialStateId.Low);
                Add(ref hash, layer.States?.Count ?? -1);
                if (layer.States != null)
                {
                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        CoCoStateSource state = layer.States[stateIndex];
                        if (state == null)
                        {
                            Add(ref hash, ulong.MaxValue);
                            continue;
                        }

                        Add(ref hash, state.StateId.High);
                        Add(ref hash, state.StateId.Low);
                        Add(ref hash, state.ParentStateId.High);
                        Add(ref hash, state.ParentStateId.Low);
                        Add(ref hash, state.InitialChildStateId.High);
                        Add(ref hash, state.InitialChildStateId.Low);
                        Add(ref hash, state.DescriptorId.High);
                        Add(ref hash, state.DescriptorId.Low);
                        Add(
                            ref hash,
                            layerConfigFingerprints != null &&
                            stateIndex < layerConfigFingerprints.States.Length
                                ? layerConfigFingerprints.States[stateIndex]
                                : state.Config?.Fingerprint ?? 0UL);
                    }
                }

                Add(ref hash, layer.Transitions?.Count ?? -1);
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
                        Add(ref hash, ulong.MaxValue);
                        continue;
                    }

                    Add(ref hash, transition.TransitionId.High);
                    Add(ref hash, transition.TransitionId.Low);
                    Add(ref hash, transition.SourceStateId.High);
                    Add(ref hash, transition.SourceStateId.Low);
                    Add(ref hash, transition.TargetStateId.High);
                    Add(ref hash, transition.TargetStateId.Low);
                    Add(ref hash, transition.Priority);
                    Add(ref hash, (int)transition.Window.Mode);
                    Add(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        transition.Window.StartInclusive)));
                    Add(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        transition.Window.EndExclusive)));
                    ulong invalidWindowFingerprint =
                        layerConfigFingerprints != null &&
                        transitionIndex < layerConfigFingerprints.InvalidWindows.Length
                            ? layerConfigFingerprints.InvalidWindows[transitionIndex]
                            : 0UL;
                    if (invalidWindowFingerprint != 0UL)
                    {
                        Add(ref hash, 0x494E56414C494457UL);
                        Add(ref hash, invalidWindowFingerprint);
                    }
                    Add(ref hash, transition.Conditions?.Count ?? -1);
                    if (transition.Conditions == null)
                    {
                        continue;
                    }

                    ulong[] transitionConditionFingerprints =
                        layerConfigFingerprints != null &&
                        transitionIndex < layerConfigFingerprints.Conditions.Length
                            ? layerConfigFingerprints.Conditions[transitionIndex]
                            : null;
                    for (int conditionIndex = 0;
                         conditionIndex < transition.Conditions.Count;
                         conditionIndex++)
                    {
                        CoCoConditionSource condition = transition.Conditions[conditionIndex];
                        if (condition == null)
                        {
                            Add(ref hash, ulong.MaxValue);
                            continue;
                        }

                        Add(ref hash, condition.DescriptorId.High);
                        Add(ref hash, condition.DescriptorId.Low);
                        Add(
                            ref hash,
                            transitionConditionFingerprints != null &&
                            conditionIndex < transitionConditionFingerprints.Length
                                ? transitionConditionFingerprints[conditionIndex]
                                : condition.Config?.Fingerprint ?? 0UL);
                    }
                }
            }

            AddEventAdapterDeclarations(ref hash, eventAdapterDeclarations);

            return NonZero(hash);
        }

        private static ulong ComputeCacheFingerprint(
            ulong contentFingerprint,
            IReadOnlyList<CoCoGraphDiagnostic> preflightDiagnostics,
            ulong managedReferenceFailureFingerprint,
            ulong eventAdapterAuthoringFingerprint)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, 0x43414348454B4559UL);
            Add(ref hash, contentFingerprint);
            Add(ref hash, managedReferenceFailureFingerprint);
            Add(ref hash, eventAdapterAuthoringFingerprint);
            Add(ref hash, preflightDiagnostics.Count);
            for (int index = 0; index < preflightDiagnostics.Count; index++)
            {
                CoCoGraphDiagnostic diagnostic = preflightDiagnostics[index];
                Add(ref hash, (int)diagnostic.Diagnostic.Domain);
                Add(ref hash, (int)diagnostic.Diagnostic.Code);
                Add(ref hash, (int)diagnostic.Diagnostic.Severity);
                Add(ref hash, diagnostic.Diagnostic.Message);
                Add(ref hash, (int)diagnostic.Location.ElementKind);
                Add(ref hash, (int)diagnostic.Location.Field);
                Add(ref hash, diagnostic.Location.GraphId.High);
                Add(ref hash, diagnostic.Location.GraphId.Low);
                Add(ref hash, diagnostic.Location.LayerId.High);
                Add(ref hash, diagnostic.Location.LayerId.Low);
                Add(ref hash, diagnostic.Location.StateId.High);
                Add(ref hash, diagnostic.Location.StateId.Low);
                Add(ref hash, diagnostic.Location.TransitionId.High);
                Add(ref hash, diagnostic.Location.TransitionId.Low);
                Add(ref hash, diagnostic.Location.LayerIndex);
                Add(ref hash, diagnostic.Location.StateIndex);
                Add(ref hash, diagnostic.Location.TransitionIndex);
                Add(ref hash, diagnostic.Location.ConditionIndex);
                Add(ref hash, diagnostic.Location.EventAdapterDeclarationIndex);
            }

            return NonZero(hash);
        }

        private static void AddEventAdapterDeclarations(
            ref ulong hash,
            IReadOnlyList<CoCoEventToIntentDeclarationSource> source)
        {
            Add(ref hash, source?.Count ?? -1);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                CoCoEventToIntentDeclarationSource declaration = source[index];
                if (declaration == null)
                {
                    Add(ref hash, ulong.MaxValue);
                    continue;
                }

                Add(ref hash, declaration.EventTypeId.High);
                Add(ref hash, declaration.EventTypeId.Low);
                Add(ref hash, declaration.ProvidedIntentId.High);
                Add(ref hash, declaration.ProvidedIntentId.Low);
            }
        }

        private static ulong ComputeEventAdapterAuthoringFingerprint(
            IReadOnlyList<CoCoEventToIntentDeclarationSource> source)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, source?.Count ?? -1);
            if (source == null)
            {
                return NonZero(hash);
            }

            for (int index = 0; index < source.Count; index++)
            {
                CoCoEventToIntentDeclarationSource declaration = source[index];
                if (declaration == null)
                {
                    Add(ref hash, ulong.MaxValue);
                    continue;
                }

                Add(ref hash, declaration.EventTypeId.High);
                Add(ref hash, declaration.EventTypeId.Low);
                Add(ref hash, declaration.ProvidedIntentId.High);
                Add(ref hash, declaration.ProvidedIntentId.Low);
            }

            return NonZero(hash);
        }

        private static ulong ComputeFailedConfigFingerprint(
            ulong authoringFingerprint,
            CoCoDiagnostic diagnostic)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, 0x4641494C45444346UL);
            Add(ref hash, authoringFingerprint);
            Add(ref hash, (int)diagnostic.Domain);
            Add(ref hash, (int)diagnostic.Code);
            Add(ref hash, (int)diagnostic.Severity);
            Add(ref hash, diagnostic.Message);
            return NonZero(hash);
        }

        private static ulong ComputeInvalidWindowFingerprint(
            CoCoTransitionWindowMode mode,
            double startInclusive,
            double endExclusive)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, (int)mode);
            Add(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(startInclusive)));
            Add(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(endExclusive)));
            return NonZero(hash);
        }

        private static void Add(ref ulong hash, int value) => Add(ref hash, unchecked((ulong)value));
        private static void Add(ref ulong hash, uint value) => Add(ref hash, (ulong)value);

        private static void Add(ref ulong hash, string value)
        {
            if (value == null)
            {
                Add(ref hash, -1);
                return;
            }

            Add(ref hash, value.Length);
            foreach (char character in value)
            {
                Add(ref hash, (ulong)character);
            }
        }

        private static void Add(ref ulong hash, ulong value)
        {
            const ulong prime = 1099511628211UL;
            for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
            {
                hash ^= (byte)(value >> (byteIndex * 8));
                hash *= prime;
            }
        }

        private static ulong NonZero(ulong value) => value == 0UL ? 14695981039346656037UL : value;
    }
}
