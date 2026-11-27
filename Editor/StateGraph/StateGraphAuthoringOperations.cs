using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    public static partial class StateGraphAuthoringOperations
    {
        public static LayerId AddLayer(StateGraphAsset asset, string displayName = null)
        {
            RequireAsset(asset);
            Undo.RecordObject(asset, "Add State Graph Layer");

            SerializedId128 serializedId = SerializedId128.NewId();
            var layer = new StateGraphLayerRecord(
                serializedId,
                string.IsNullOrWhiteSpace(displayName)
                    ? $"Layer {asset.Layers.Count + 1}"
                    : displayName);
            asset.Layers.Add(layer);
            EditorUtility.SetDirty(asset);

            LayerId.TryCreate(serializedId.High, serializedId.Low, out LayerId layerId);
            return layerId;
        }

        public static StateId AddState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId parentStateId,
            StateDescriptorId descriptorId,
            StateConfig config = null,
            string displayName = null)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = RequireLayer(asset, layerId);
            if (!TryAddState(
                    asset,
                    layerId,
                    parentStateId,
                    descriptorId,
                    config,
                    displayName,
                    DefaultPosition(layer.States.Count),
                    out StateId stateId,
                    out string failure))
            {
                throw new ArgumentException(failure, nameof(parentStateId));
            }

            return stateId;
        }

        public static TransitionId AddTransition(
            StateGraphAsset asset,
            LayerId layerId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority)
        {
            RequireAsset(asset);
            RequireLayer(asset, layerId);
            if (!TryAddTransition(
                    asset,
                    layerId,
                    sourceStateId,
                    targetStateId,
                    priority,
                    TransitionWindow.Always,
                    catalog: null,
                    out TransitionId transitionId,
                    out string failure))
            {
                throw new ArgumentException(failure, nameof(sourceStateId));
            }

            return transitionId;
        }

        public static bool DuplicateStateSubtree(
            StateGraphAsset asset,
            LayerId layerId,
            StateId sourceRootStateId,
            out StateId duplicatedRootStateId)
        {
            RequireAsset(asset);
            if (!asset.EditorLayout.IsSupported)
            {
                duplicatedRootStateId = default;
                return false;
            }

            StateGraphLayerRecord targetLayer = RequireLayer(asset, layerId);
            StateGraphStateRecord sourceRoot = FindState(targetLayer, sourceRootStateId);
            if (sourceRoot == null)
            {
                duplicatedRootStateId = default;
                return false;
            }

            HashSet<SerializedId128> subtreeIds = CollectSubtreeIds(targetLayer, sourceRoot.StateId);
            var stateIdRemaps = new Dictionary<SerializedId128, SerializedId128>();
            foreach (SerializedId128 sourceId in subtreeIds)
            {
                stateIdRemaps.Add(sourceId, SerializedId128.NewId());
            }

            StateGraphAsset clone = UnityEngine.Object.Instantiate(asset);
            try
            {
                StateGraphLayerRecord clonedLayer = FindLayer(clone, layerId);
                if (clonedLayer == null)
                {
                    duplicatedRootStateId = default;
                    return false;
                }

                Undo.RecordObject(asset, "Duplicate State Graph Subtree");
                foreach (StateGraphStateRecord clonedState in clonedLayer.States)
                {
                    if (clonedState == null || !subtreeIds.Contains(clonedState.StateId))
                    {
                        continue;
                    }

                    SerializedId128 sourceId = clonedState.StateId;
                    clonedState.StateId = stateIdRemaps[sourceId];
                    if (sourceId == sourceRoot.StateId)
                    {
                        clonedState.ParentStateId = sourceRoot.ParentStateId;
                    }
                    else
                    {
                        clonedState.ParentStateId = RemapIfPresent(
                            clonedState.ParentStateId,
                            stateIdRemaps);
                    }

                    clonedState.InitialChildStateId = RemapIfPresent(
                        clonedState.InitialChildStateId,
                        stateIdRemaps);
                    targetLayer.States.Add(clonedState);
                }

                foreach (StateGraphTransitionRecord clonedTransition in clonedLayer.Transitions)
                {
                    if (clonedTransition == null ||
                        !subtreeIds.Contains(clonedTransition.SourceStateId) ||
                        !subtreeIds.Contains(clonedTransition.TargetStateId))
                    {
                        continue;
                    }

                    clonedTransition.TransitionId = SerializedId128.NewId();
                    clonedTransition.SourceStateId = stateIdRemaps[clonedTransition.SourceStateId];
                    clonedTransition.TargetStateId = stateIdRemaps[clonedTransition.TargetStateId];
                    targetLayer.Transitions.Add(clonedTransition);
                }

                foreach (SerializedId128 sourceId in subtreeIds)
                {
                    if (clone.EditorLayout.TryGetPosition(sourceId, out Vector2 sourcePosition))
                    {
                        targetLayer = RequireLayer(asset, layerId);
                        asset.EditorLayout.SetPosition(
                            stateIdRemaps[sourceId],
                            sourceId == sourceRoot.StateId
                                ? sourcePosition + new Vector2(32f, 32f)
                                : sourcePosition);
                    }
                }

                EditorUtility.SetDirty(asset);

                SerializedId128 duplicatedRoot = stateIdRemaps[sourceRoot.StateId];
                StateId.TryCreate(
                    duplicatedRoot.High,
                    duplicatedRoot.Low,
                    out duplicatedRootStateId);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        public static bool DuplicateLayer(
            StateGraphAsset asset,
            LayerId sourceLayerId,
            out LayerId duplicatedLayerId)
        {
            RequireAsset(asset);
            duplicatedLayerId = default;
            if (!asset.EditorLayout.IsSupported)
            {
                return false;
            }

            if (!TryGetUniqueLayer(asset, sourceLayerId, out StateGraphLayerRecord sourceLayer) ||
                !TryValidateLayerForDuplication(
                    asset,
                    sourceLayer,
                    out HashSet<SerializedId128> layerIds,
                    out HashSet<SerializedId128> stateIds,
                    out HashSet<SerializedId128> transitionIds))
            {
                return false;
            }

            SerializedId128 duplicatedSerializedLayerId = CreateUniqueId(layerIds);
            var stateIdRemaps = new Dictionary<SerializedId128, SerializedId128>();
            foreach (StateGraphStateRecord state in sourceLayer.AllStates)
            {
                SerializedId128 duplicatedStateId = CreateUniqueId(stateIds);
                stateIds.Add(duplicatedStateId);
                stateIdRemaps.Add(state.StateId, duplicatedStateId);
            }

            var transitionIdRemaps = new Dictionary<SerializedId128, SerializedId128>();
            foreach (StateGraphTransitionRecord transition in sourceLayer.Transitions)
            {
                SerializedId128 duplicatedTransitionId = CreateUniqueId(transitionIds);
                transitionIds.Add(duplicatedTransitionId);
                transitionIdRemaps.Add(transition.TransitionId, duplicatedTransitionId);
            }

            int sourceLayerIndex = asset.Layers.IndexOf(sourceLayer);
            StateGraphAsset clone = UnityEngine.Object.Instantiate(asset);
            try
            {
                if (sourceLayerIndex < 0 || sourceLayerIndex >= clone.Layers.Count)
                {
                    return false;
                }

                StateGraphLayerRecord duplicatedLayer = clone.Layers[sourceLayerIndex];
                if (duplicatedLayer == null)
                {
                    return false;
                }

                duplicatedLayer.LayerId = duplicatedSerializedLayerId;
                duplicatedLayer.InitialStateId = stateIdRemaps[duplicatedLayer.InitialStateId];
                foreach (StateGraphStateRecord state in duplicatedLayer.AllStates)
                {
                    state.StateId = stateIdRemaps[state.StateId];
                    if (state.ParentStateId.IsValid)
                    {
                        state.ParentStateId = stateIdRemaps[state.ParentStateId];
                    }

                    if (state.InitialChildStateId.IsValid)
                    {
                        state.InitialChildStateId = stateIdRemaps[state.InitialChildStateId];
                    }
                }

                foreach (StateGraphTransitionRecord transition in duplicatedLayer.Transitions)
                {
                    transition.TransitionId = transitionIdRemaps[transition.TransitionId];
                    transition.SourceStateId = stateIdRemaps[transition.SourceStateId];
                    transition.TargetStateId = stateIdRemaps[transition.TargetStateId];
                }

                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Duplicate State Graph Layer");
                Undo.RecordObject(asset, "Duplicate State Graph Layer");
                asset.Layers.Insert(sourceLayerIndex + 1, duplicatedLayer);
                foreach (KeyValuePair<SerializedId128, SerializedId128> remap in stateIdRemaps)
                {
                    if (clone.EditorLayout.TryGetPosition(remap.Key, out Vector2 sourcePosition))
                    {
                        asset.EditorLayout.SetPosition(remap.Value, sourcePosition);
                    }
                }

                EditorUtility.SetDirty(asset);
                Undo.CollapseUndoOperations(undoGroup);

                LayerId.TryCreate(
                    duplicatedSerializedLayerId.High,
                    duplicatedSerializedLayerId.Low,
                    out duplicatedLayerId);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        private static bool TryGetUniqueLayer(
            StateGraphAsset asset,
            LayerId layerId,
            out StateGraphLayerRecord result)
        {
            result = null;
            if (!layerId.IsValid)
            {
                return false;
            }

            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null ||
                    layer.LayerId.High != layerId.High ||
                    layer.LayerId.Low != layerId.Low)
                {
                    continue;
                }

                if (result != null)
                {
                    result = null;
                    return false;
                }

                result = layer;
            }

            return result != null;
        }

        private static bool TryValidateLayerForDuplication(
            StateGraphAsset asset,
            StateGraphLayerRecord sourceLayer,
            out HashSet<SerializedId128> layerIds,
            out HashSet<SerializedId128> stateIds,
            out HashSet<SerializedId128> transitionIds)
        {
            layerIds = new HashSet<SerializedId128>();
            stateIds = new HashSet<SerializedId128>();
            transitionIds = new HashSet<SerializedId128>();

            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !layer.LayerId.IsValid || !layerIds.Add(layer.LayerId))
                {
                    return false;
                }

                foreach (StateGraphStateRecord state in layer.AllStates)
                {
                    if (state == null || !state.StateId.IsValid || !stateIds.Add(state.StateId))
                    {
                        return false;
                    }
                }

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
                {
                    if (transition == null ||
                        !transition.TransitionId.IsValid ||
                        !transitionIds.Add(transition.TransitionId))
                    {
                        return false;
                    }
                }
            }

            if (sourceLayer.States.Count == 0)
            {
                return false;
            }

            if (sourceLayer.AnyState != null &&
                (sourceLayer.AnyState.ParentStateId.IsValid || sourceLayer.AnyState.InitialChildStateId.IsValid))
            {
                return false;
            }

            var sourceStates = new Dictionary<SerializedId128, StateGraphStateRecord>();
            var childCounts = new Dictionary<SerializedId128, int>();
            foreach (StateGraphStateRecord state in sourceLayer.States)
            {
                sourceStates.Add(state.StateId, state);
            }

            if (!sourceLayer.InitialStateId.IsValid ||
                !sourceStates.TryGetValue(sourceLayer.InitialStateId, out StateGraphStateRecord initialState) ||
                initialState.ParentStateId.IsValid)
            {
                return false;
            }

            foreach (StateGraphStateRecord state in sourceLayer.States)
            {
                if (!state.ParentStateId.IsValid)
                {
                    continue;
                }

                if (state.ParentStateId == state.StateId || !sourceStates.ContainsKey(state.ParentStateId))
                {
                    return false;
                }

                childCounts.TryGetValue(state.ParentStateId, out int childCount);
                childCounts[state.ParentStateId] = childCount + 1;
            }

            foreach (StateGraphStateRecord state in sourceLayer.States)
            {
                if (ContainsParentCycle(state, sourceStates))
                {
                    return false;
                }

                bool hasChildren = childCounts.ContainsKey(state.StateId);
                if (hasChildren)
                {
                    if (!state.InitialChildStateId.IsValid ||
                        !sourceStates.TryGetValue(
                            state.InitialChildStateId,
                            out StateGraphStateRecord initialChild) ||
                        initialChild.ParentStateId != state.StateId)
                    {
                        return false;
                    }
                }
                else if (state.InitialChildStateId.IsValid)
                {
                    return false;
                }
            }

            foreach (StateGraphTransitionRecord transition in sourceLayer.Transitions)
            {
                if ((!sourceStates.ContainsKey(transition.SourceStateId) &&
                     sourceLayer.AnyState?.StateId != transition.SourceStateId) ||
                    !sourceStates.ContainsKey(transition.TargetStateId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsParentCycle(
            StateGraphStateRecord state,
            IReadOnlyDictionary<SerializedId128, StateGraphStateRecord> states)
        {
            var visited = new HashSet<SerializedId128>();
            StateGraphStateRecord current = state;
            while (current.ParentStateId.IsValid)
            {
                if (!visited.Add(current.StateId) ||
                    !states.TryGetValue(current.ParentStateId, out current))
                {
                    return true;
                }
            }

            return false;
        }

        private static SerializedId128 CreateUniqueId(ISet<SerializedId128> existingIds)
        {
            SerializedId128 result;
            do
            {
                result = SerializedId128.NewId();
            }
            while (existingIds.Contains(result));

            return result;
        }

        private static HashSet<SerializedId128> CollectSubtreeIds(
            StateGraphLayerRecord layer,
            SerializedId128 rootStateId)
        {
            var result = new HashSet<SerializedId128> { rootStateId };
            bool added;
            do
            {
                added = false;
                foreach (StateGraphStateRecord state in layer.States)
                {
                    if (state != null &&
                        !result.Contains(state.StateId) &&
                        result.Contains(state.ParentStateId))
                    {
                        result.Add(state.StateId);
                        added = true;
                    }
                }
            }
            while (added);

            return result;
        }

        private static StateGraphLayerRecord RequireLayer(
            StateGraphAsset asset,
            LayerId layerId)
        {
            if (!layerId.IsValid)
            {
                throw new ArgumentException("A valid Layer ID is required.", nameof(layerId));
            }

            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            return layer ?? throw new ArgumentException(
                "The Layer ID does not belong to the supplied StateGraph Asset.",
                nameof(layerId));
        }

        private static StateGraphLayerRecord FindLayer(StateGraphAsset asset, LayerId layerId)
        {
            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                if (layer != null &&
                    layer.LayerId.High == layerId.High &&
                    layer.LayerId.Low == layerId.Low)
                {
                    return layer;
                }
            }

            return null;
        }

        private static StateGraphStateRecord FindState(
            StateGraphLayerRecord layer,
            StateId stateId)
        {
            if (!stateId.IsValid)
            {
                return null;
            }

            foreach (StateGraphStateRecord state in layer.States)
            {
                if (state != null &&
                    state.StateId.High == stateId.High &&
                    state.StateId.Low == stateId.Low)
                {
                    return state;
                }
            }

            return null;
        }

        private static SerializedId128 RemapIfPresent(
            SerializedId128 source,
            IReadOnlyDictionary<SerializedId128, SerializedId128> remaps)
        {
            return source.IsValid && remaps.TryGetValue(source, out SerializedId128 remapped)
                ? remapped
                : source;
        }

        private static SerializedId128 Serialize(StateId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateDescriptorId id) =>
            new SerializedId128(id.High, id.Low);

        private static void RequireAsset(StateGraphAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "StateGraph authoring is read-only while entering or running Play Mode.");
            }
        }
    }
}
