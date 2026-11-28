using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// Host 和编辑器调用快照构建器，将 Unity 图、节点和冻结后的配置转换为纯编译输入。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存资产编译时使用的纯图输入、内容身份与构建诊断。</summary>
    internal sealed class StateGraphAssetSnapshot
    {
        private readonly IReadOnlyList<GraphDiagnostic> preflightDiagnostics;
        private readonly IReadOnlyList<GraphDiagnostic> adapterDiagnostics;

        internal StateGraphAssetSnapshot(
            StateGraphSource source,
            ulong contentFingerprint,
            ulong cacheFingerprint,
            IReadOnlyList<GraphDiagnostic> preflightDiagnostics,
            IReadOnlyList<GraphDiagnostic> adapterDiagnostics,
            bool freezingSkippedGlobally)
        {
            Source = source;
            ContentFingerprint = contentFingerprint;
            CacheFingerprint = cacheFingerprint;
            this.preflightDiagnostics = CopyDiagnostics(preflightDiagnostics);
            this.adapterDiagnostics = CopyDiagnostics(adapterDiagnostics);
            FreezingSkippedGlobally = freezingSkippedGlobally;
        }

        internal StateGraphSource Source { get; }
        internal ulong ContentFingerprint { get; }
        internal ulong CacheFingerprint { get; }
        internal IReadOnlyList<GraphDiagnostic> PreflightDiagnostics => preflightDiagnostics;
        internal IReadOnlyList<GraphDiagnostic> AdapterDiagnostics => adapterDiagnostics;
        internal bool FreezingSkippedGlobally { get; }

        private static IReadOnlyList<GraphDiagnostic> CopyDiagnostics(
            IReadOnlyList<GraphDiagnostic> source)
        {
            var copy = new GraphDiagnostic[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>从 Unity 记录构造纯图源并在节点配置缺失时报告准确位置。</summary>
    internal static class StateGraphAssetSnapshotBuilder
    {
        /// <summary>将属于同一层的节点配置指纹汇总到图内容指纹。</summary>
        private sealed class LayerConfigFingerprints
        {
            internal LayerConfigFingerprints(int stateCount, int transitionCount)
            {
                States = new ulong[stateCount];
                InvalidWindows = new ulong[transitionCount];
            }

            internal ulong[] States { get; }
            internal ulong AnyState;
            internal ulong[] InvalidWindows { get; }
        }

        internal static StateGraphAssetSnapshot Build(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog,
            StateGraphManagedReferenceInspection managedReferenceInspection)
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

            var preflightDiagnostics = new List<GraphDiagnostic>();
            var adapterDiagnostics = new List<GraphDiagnostic>();
            var configFreezer = new StateGraphConfigFreezer(catalog);

            GraphId.TryCreate(
                asset.SerializedGraphId.High,
                asset.SerializedGraphId.Low,
                out GraphId graphId);
            bool allowFreezing = Preflight(
                asset,
                graphId,
                managedReferenceInspection,
                preflightDiagnostics);

            var layerSources = new StateLayerSource[asset.Layers.Count];
            var layerConfigFingerprints = new LayerConfigFingerprints[asset.Layers.Count];
            for (int layerIndex = 0; layerIndex < asset.Layers.Count; layerIndex++)
            {
                StateGraphLayerRecord layer = asset.Layers[layerIndex];
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

            EventToIntentDeclarationSource[] eventAdapterDeclarations =
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
            var source = new StateGraphSource(
                asset.SchemaVersion,
                contentFingerprint,
                graphId,
                layerSources,
                eventAdapterDeclarations);
            return new StateGraphAssetSnapshot(
                source,
                source.ContentFingerprint,
                cacheFingerprint,
                preflightDiagnostics,
                adapterDiagnostics,
                !allowFreezing);
        }

        private static bool Preflight(
            StateGraphAsset asset,
            GraphId graphId,
            StateGraphManagedReferenceInspection managedReferenceInspection,
            ICollection<GraphDiagnostic> diagnostics)
        {
            bool globalIdentityIsValid = managedReferenceInspection.Diagnostics.Count == 0;
            for (int diagnosticIndex = 0;
                 diagnosticIndex < managedReferenceInspection.Diagnostics.Count;
                 diagnosticIndex++)
            {
                diagnostics.Add(managedReferenceInspection.Diagnostics[diagnosticIndex]);
            }
            if (asset.SchemaVersion != StateGraphCompiler.CurrentSchemaVersion)
            {
                diagnostics.Add(Error(
                    GraphLocation(graphId, GraphField.SchemaVersion),
                    CoCoDiagnosticCode.UnsupportedSchemaVersion,
                    $"StateGraph Asset schema {asset.SchemaVersion} is not supported."));
                globalIdentityIsValid = false;
            }

            if (!graphId.IsValid)
            {
                diagnostics.Add(Error(
                    GraphLocation(graphId, GraphField.Identifier),
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "StateGraph Asset has an invalid Graph ID."));
                globalIdentityIsValid = false;
            }

            if (string.IsNullOrWhiteSpace(asset.AssetGuidStamp))
            {
                diagnostics.Add(Error(
                    GraphLocation(graphId, GraphField.AssetGuidStamp),
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "StateGraph Asset has no Asset GUID identity stamp."));
                globalIdentityIsValid = false;
            }

            for (int layerIndex = 0; layerIndex < asset.Layers.Count; layerIndex++)
            {
                StateGraphLayerRecord layer = asset.Layers[layerIndex];
                if (layer == null)
                {
                    diagnostics.Add(Error(
                        Location(
                            GraphElementKind.Layer,
                            GraphField.None,
                            graphId,
                            default,
                            default,
                            default,
                            layerIndex,
                            -1,
                            -1),
                        CoCoDiagnosticCode.MissingTopologyElement,
                        "StateGraph contains a null Layer record."));
                    continue;
                }

                LayerId.TryCreate(layer.LayerId.High, layer.LayerId.Low, out LayerId layerId);
                for (int stateIndex = -1; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateGraphStateRecord state = stateIndex < 0
                        ? layer.AnyState
                        : layer.States[stateIndex];
                    if (stateIndex < 0 && state == null)
                    {
                        continue;
                    }
                    if (state == null)
                    {
                        diagnostics.Add(Error(
                            Location(
                                GraphElementKind.State,
                                GraphField.None,
                                graphId,
                                layerId,
                                default,
                                default,
                                layerIndex,
                                stateIndex,
                                -1),
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null State record."));
                        continue;
                    }

                    if (state.Config == null)
                    {
                        StateId.TryCreate(
                            state.StateId.High,
                            state.StateId.Low,
                            out StateId stateId);
                        GraphDiagnosticLocation configLocation = Location(
                                GraphElementKind.State,
                                GraphField.Config,
                                graphId,
                                layerId,
                                stateId,
                                default,
                                layerIndex,
                                stateIndex,
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
                    StateGraphTransitionRecord transition = layer.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        diagnostics.Add(Error(
                            Location(
                                GraphElementKind.Transition,
                                GraphField.None,
                                graphId,
                                layerId,
                                default,
                                default,
                                layerIndex,
                                -1,
                                transitionIndex),
                            CoCoDiagnosticCode.MissingTopologyElement,
                            "Layer contains a null Transition record."));
                        continue;
                    }

                }
            }

            for (int declarationIndex = 0;
                 declarationIndex < asset.EventAdapterDeclarations.Count;
                 declarationIndex++)
            {
                StateGraphEventAdapterDeclarationRecord declaration =
                    asset.EventAdapterDeclarations[declarationIndex];
                GraphDiagnosticLocation location = EventAdapterDeclarationLocation(
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

        private static EventToIntentDeclarationSource[] BuildEventAdapterDeclarations(
            StateGraphAsset asset)
        {
            var declarations =
                new EventToIntentDeclarationSource[asset.EventAdapterDeclarations.Count];
            for (int index = 0; index < asset.EventAdapterDeclarations.Count; index++)
            {
                StateGraphEventAdapterDeclarationRecord declaration =
                    asset.EventAdapterDeclarations[index];
                if (declaration == null)
                {
                    continue;
                }

                EventTypeId.TryCreate(
                    declaration.EventTypeId.High,
                    declaration.EventTypeId.Low,
                    out EventTypeId eventTypeId);
                IntentId.TryCreate(
                    declaration.ProvidedIntentId.High,
                    declaration.ProvidedIntentId.Low,
                    out IntentId providedIntentId);
                declarations[index] = new EventToIntentDeclarationSource(
                    eventTypeId,
                    providedIntentId);
            }

            return declarations;
        }

        private static StateLayerSource BuildLayer(
            StateGraphLayerRecord layer,
            GraphId graphId,
            int layerIndex,
            bool allowFreezing,
            StateGraphConfigFreezer configFreezer,
            ICollection<GraphDiagnostic> preflightDiagnostics,
            ICollection<GraphDiagnostic> adapterDiagnostics,
            out LayerConfigFingerprints configFingerprints)
        {
            LayerId.TryCreate(layer.LayerId.High, layer.LayerId.Low, out LayerId layerId);
            StateId.TryCreate(
                layer.InitialStateId.High,
                layer.InitialStateId.Low,
                out StateId initialStateId);

            configFingerprints = new LayerConfigFingerprints(
                layer.States.Count,
                layer.Transitions.Count);
            var stateSources = new StateSource[layer.States.Count];
            for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
            {
                StateGraphStateRecord state = layer.States[stateIndex];
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

            var transitionSources = new TransitionSource[layer.Transitions.Count];
            for (int transitionIndex = 0; transitionIndex < layer.Transitions.Count; transitionIndex++)
            {
                StateGraphTransitionRecord transition = layer.Transitions[transitionIndex];
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
                    out configFingerprints.InvalidWindows[transitionIndex]);
            }

            StateSource anyState = layer.AnyState == null ? null : BuildState(
                layer.AnyState,
                graphId,
                layerId,
                layerIndex,
                -1,
                allowFreezing,
                configFreezer,
                preflightDiagnostics,
                adapterDiagnostics,
                out configFingerprints.AnyState);
            return new StateLayerSource(
                layerId,
                initialStateId,
                stateSources,
                transitionSources,
                anyState);
        }

        private static StateSource BuildState(
            StateGraphStateRecord state,
            GraphId graphId,
            LayerId layerId,
            int layerIndex,
            int stateIndex,
            bool allowFreezing,
            StateGraphConfigFreezer configFreezer,
            ICollection<GraphDiagnostic> preflightDiagnostics,
            ICollection<GraphDiagnostic> adapterDiagnostics,
            out ulong configFingerprint)
        {
            StateId.TryCreate(state.StateId.High, state.StateId.Low, out StateId stateId);
            StateId.TryCreate(
                state.ParentStateId.High,
                state.ParentStateId.Low,
                out StateId parentStateId);
            StateId.TryCreate(
                state.InitialChildStateId.High,
                state.InitialChildStateId.Low,
                out StateId initialChildStateId);
            StateDescriptorId.TryCreate(
                state.StateDescriptorId.High,
                state.StateDescriptorId.Low,
                out StateDescriptorId descriptorId);

            var configLocation = Location(
                GraphElementKind.State,
                GraphField.Config,
                graphId,
                layerId,
                stateId,
                default,
                layerIndex,
                stateIndex,
                -1);
            FrozenConfigSnapshot frozenConfig = null;
            ulong authoringFingerprint = state.Config == null
                ? 0UL
                : StateGraphConfigFreezer.ComputeAuthoringFingerprint(state.Config);
            configFingerprint = state.Config == null
                ? 0UL
                : ComputeFailedConfigFingerprint(authoringFingerprint, CoCoDiagnostic.None);
            if (!descriptorId.IsValid)
            {
                preflightDiagnostics.Add(Error(
                    Location(
                        GraphElementKind.State,
                        GraphField.Descriptor,
                        graphId,
                        layerId,
                        stateId,
                        default,
                        layerIndex,
                        stateIndex,
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
                        out GraphDiagnostic configDiagnostic))
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

            return new StateSource(
                stateId,
                parentStateId,
                initialChildStateId,
                descriptorId,
                frozenConfig);
        }

        private static TransitionSource BuildTransition(
            StateGraphTransitionRecord transition,
            GraphId graphId,
            LayerId layerId,
            int layerIndex,
            int transitionIndex,
            bool allowFreezing,
            StateGraphConfigFreezer configFreezer,
            ICollection<GraphDiagnostic> preflightDiagnostics,
            ICollection<GraphDiagnostic> adapterDiagnostics,
            out ulong invalidWindowFingerprint)
        {
            TransitionId.TryCreate(
                transition.TransitionId.High,
                transition.TransitionId.Low,
                out TransitionId transitionId);
            StateId.TryCreate(
                transition.SourceStateId.High,
                transition.SourceStateId.Low,
                out StateId sourceStateId);
            StateId.TryCreate(
                transition.TargetStateId.High,
                transition.TargetStateId.Low,
                out StateId targetStateId);

            bool hasValidWindow = TransitionWindow.TryCreate(
                transition.WindowMode,
                transition.WindowStartInclusive,
                transition.WindowEndExclusive,
                out TransitionWindow window);
            invalidWindowFingerprint = hasValidWindow
                ? 0UL
                : ComputeInvalidWindowFingerprint(
                    transition.WindowMode,
                    transition.WindowStartInclusive,
                    transition.WindowEndExclusive);

            return new TransitionSource(
                transitionId,
                sourceStateId,
                targetStateId,
                transition.Priority,
                window);
        }

        private static GraphDiagnosticLocation GraphLocation(
            GraphId graphId,
            GraphField field) =>
            Location(
                GraphElementKind.Graph,
                field,
                graphId,
                default,
                default,
                default,
                -1,
                -1,
                -1);

        private static GraphDiagnosticLocation EventAdapterDeclarationLocation(
            GraphId graphId,
            int declarationIndex) =>
            new GraphDiagnosticLocation(
                GraphElementKind.EventAdapterDeclaration,
                GraphField.EventAdapterDeclarations,
                graphId,
                default,
                default,
                default,
                -1,
                -1,
                -1,
                declarationIndex);

        private static GraphDiagnosticLocation Location(
            GraphElementKind elementKind,
            GraphField field,
            GraphId graphId,
            LayerId layerId,
            StateId stateId,
            TransitionId transitionId,
            int layerIndex,
            int stateIndex,
            int transitionIndex) =>
            new GraphDiagnosticLocation(
                elementKind,
                field,
                graphId,
                layerId,
                stateId,
                transitionId,
                layerIndex,
                stateIndex,
                transitionIndex);

        private static GraphDiagnostic Error(
            GraphDiagnosticLocation location,
            CoCoDiagnosticCode code,
            string message) =>
            new GraphDiagnostic(
                CoCoDiagnostic.Error(CoCoDiagnosticDomain.Topology, code, message),
                location);

        private static GraphDiagnostic AuthoringError(
            GraphDiagnosticLocation location,
            string message) =>
            new GraphDiagnostic(
                CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidAuthoringDependency,
                    message),
                location);

        private static ulong ComputeContentFingerprint(
            uint schemaVersion,
            GraphId graphId,
            IReadOnlyList<StateLayerSource> layers,
            IReadOnlyList<LayerConfigFingerprints> configFingerprints,
            IReadOnlyList<EventToIntentDeclarationSource> eventAdapterDeclarations)
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
                StateLayerSource layer = layers[layerIndex];
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
                        StateSource state = layer.States[stateIndex];
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

                if (layer.AnyState != null)
                {
                    StateSource anyState = layer.AnyState;
                    Add(ref hash, 0x414E595354415445UL);
                    Add(ref hash, anyState.StateId.High);
                    Add(ref hash, anyState.StateId.Low);
                    Add(ref hash, anyState.ParentStateId.High);
                    Add(ref hash, anyState.ParentStateId.Low);
                    Add(ref hash, anyState.InitialChildStateId.High);
                    Add(ref hash, anyState.InitialChildStateId.Low);
                    Add(ref hash, anyState.DescriptorId.High);
                    Add(ref hash, anyState.DescriptorId.Low);
                    Add(ref hash, layerConfigFingerprints?.AnyState ?? anyState.Config?.Fingerprint ?? 0UL);
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
                    TransitionSource transition = layer.Transitions[transitionIndex];
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
                    Add(ref hash, 0);
                }
            }

            AddEventAdapterDeclarations(ref hash, eventAdapterDeclarations);

            return NonZero(hash);
        }

        private static ulong ComputeCacheFingerprint(
            ulong contentFingerprint,
            IReadOnlyList<GraphDiagnostic> preflightDiagnostics,
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
                GraphDiagnostic diagnostic = preflightDiagnostics[index];
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
                Add(ref hash, -1);
                Add(ref hash, diagnostic.Location.EventAdapterDeclarationIndex);
            }

            return NonZero(hash);
        }

        private static void AddEventAdapterDeclarations(
            ref ulong hash,
            IReadOnlyList<EventToIntentDeclarationSource> source)
        {
            Add(ref hash, source?.Count ?? -1);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                EventToIntentDeclarationSource declaration = source[index];
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
            IReadOnlyList<EventToIntentDeclarationSource> source)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, source?.Count ?? -1);
            if (source == null)
            {
                return NonZero(hash);
            }

            for (int index = 0; index < source.Count; index++)
            {
                EventToIntentDeclarationSource declaration = source[index];
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
            TransitionWindowMode mode,
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
