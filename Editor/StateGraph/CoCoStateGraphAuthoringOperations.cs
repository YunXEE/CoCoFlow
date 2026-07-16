using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    public static class CoCoStateGraphAuthoringOperations
    {
        public static CoCoLayerId AddLayer(CoCoStateGraphAsset asset, string displayName = null)
        {
            RequireAsset(asset);
            Undo.RecordObject(asset, "Add State Graph Layer");

            CoCoSerializedId128 serializedId = CoCoSerializedId128.NewId();
            var layer = new CoCoStateGraphLayerRecord(
                serializedId,
                string.IsNullOrWhiteSpace(displayName)
                    ? $"Layer {asset.Layers.Count + 1}"
                    : displayName);
            asset.Layers.Add(layer);
            EditorUtility.SetDirty(asset);

            CoCoLayerId.TryCreate(serializedId.High, serializedId.Low, out CoCoLayerId layerId);
            return layerId;
        }

        public static CoCoStateId AddState(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoStateId parentStateId,
            CoCoStateDescriptorId descriptorId,
            CoCoStateConfig config = null,
            string displayName = null)
        {
            RequireAsset(asset);
            CoCoStateGraphLayerRecord layer = RequireLayer(asset, layerId);
            CoCoSerializedId128 serializedParentId = Serialize(parentStateId);
            if (parentStateId.IsValid && FindState(layer, parentStateId) == null)
            {
                throw new ArgumentException("The parent State does not belong to the target Layer.", nameof(parentStateId));
            }

            Undo.RecordObject(asset, "Add State Graph State");
            CoCoSerializedId128 serializedId = CoCoSerializedId128.NewId();
            var state = new CoCoStateGraphStateRecord(
                serializedId,
                serializedParentId,
                string.IsNullOrWhiteSpace(displayName)
                    ? $"State {layer.States.Count + 1}"
                    : displayName,
                Serialize(descriptorId),
                config);
            layer.States.Add(state);

            if (!parentStateId.IsValid && !layer.InitialStateId.IsValid)
            {
                layer.InitialStateId = serializedId;
            }
            else if (parentStateId.IsValid)
            {
                CoCoStateGraphStateRecord parent = FindState(layer, parentStateId);
                if (parent != null && !parent.InitialChildStateId.IsValid)
                {
                    parent.InitialChildStateId = serializedId;
                }
            }

            EditorUtility.SetDirty(asset);

            CoCoStateId.TryCreate(serializedId.High, serializedId.Low, out CoCoStateId stateId);
            return stateId;
        }

        public static CoCoTransitionId AddTransition(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoStateId sourceStateId,
            CoCoStateId targetStateId,
            int priority = 0)
        {
            RequireAsset(asset);
            CoCoStateGraphLayerRecord layer = RequireLayer(asset, layerId);
            if (FindState(layer, sourceStateId) == null)
            {
                throw new ArgumentException("The source State does not belong to the target Layer.", nameof(sourceStateId));
            }

            if (FindState(layer, targetStateId) == null)
            {
                throw new ArgumentException("The target State does not belong to the target Layer.", nameof(targetStateId));
            }

            Undo.RecordObject(asset, "Add State Graph Transition");
            CoCoSerializedId128 serializedId = CoCoSerializedId128.NewId();
            layer.Transitions.Add(new CoCoStateGraphTransitionRecord(
                serializedId,
                Serialize(sourceStateId),
                Serialize(targetStateId),
                priority));
            EditorUtility.SetDirty(asset);

            CoCoTransitionId.TryCreate(
                serializedId.High,
                serializedId.Low,
                out CoCoTransitionId transitionId);
            return transitionId;
        }

        public static bool DuplicateStateSubtree(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoStateId sourceRootStateId,
            out CoCoStateId duplicatedRootStateId)
        {
            RequireAsset(asset);
            CoCoStateGraphLayerRecord targetLayer = RequireLayer(asset, layerId);
            CoCoStateGraphStateRecord sourceRoot = FindState(targetLayer, sourceRootStateId);
            if (sourceRoot == null)
            {
                duplicatedRootStateId = default;
                return false;
            }

            HashSet<CoCoSerializedId128> subtreeIds = CollectSubtreeIds(targetLayer, sourceRoot.StateId);
            var stateIdRemaps = new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
            foreach (CoCoSerializedId128 sourceId in subtreeIds)
            {
                stateIdRemaps.Add(sourceId, CoCoSerializedId128.NewId());
            }

            CoCoStateGraphAsset clone = UnityEngine.Object.Instantiate(asset);
            try
            {
                CoCoStateGraphLayerRecord clonedLayer = FindLayer(clone, layerId);
                if (clonedLayer == null)
                {
                    duplicatedRootStateId = default;
                    return false;
                }

                Undo.RecordObject(asset, "Duplicate State Graph Subtree");
                foreach (CoCoStateGraphStateRecord clonedState in clonedLayer.States)
                {
                    if (clonedState == null || !subtreeIds.Contains(clonedState.StateId))
                    {
                        continue;
                    }

                    CoCoSerializedId128 sourceId = clonedState.StateId;
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

                foreach (CoCoStateGraphTransitionRecord clonedTransition in clonedLayer.Transitions)
                {
                    if (clonedTransition == null ||
                        !subtreeIds.Contains(clonedTransition.SourceStateId) ||
                        !subtreeIds.Contains(clonedTransition.TargetStateId))
                    {
                        continue;
                    }

                    clonedTransition.TransitionId = CoCoSerializedId128.NewId();
                    clonedTransition.SourceStateId = stateIdRemaps[clonedTransition.SourceStateId];
                    clonedTransition.TargetStateId = stateIdRemaps[clonedTransition.TargetStateId];
                    targetLayer.Transitions.Add(clonedTransition);
                }

                EditorUtility.SetDirty(asset);

                CoCoSerializedId128 duplicatedRoot = stateIdRemaps[sourceRoot.StateId];
                CoCoStateId.TryCreate(
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
            CoCoStateGraphAsset asset,
            CoCoLayerId sourceLayerId,
            out CoCoLayerId duplicatedLayerId)
        {
            RequireAsset(asset);
            duplicatedLayerId = default;
            if (!TryGetUniqueLayer(asset, sourceLayerId, out CoCoStateGraphLayerRecord sourceLayer) ||
                !TryValidateLayerForDuplication(
                    asset,
                    sourceLayer,
                    out HashSet<CoCoSerializedId128> layerIds,
                    out HashSet<CoCoSerializedId128> stateIds,
                    out HashSet<CoCoSerializedId128> transitionIds))
            {
                return false;
            }

            CoCoSerializedId128 duplicatedSerializedLayerId = CreateUniqueId(layerIds);
            var stateIdRemaps = new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
            foreach (CoCoStateGraphStateRecord state in sourceLayer.States)
            {
                CoCoSerializedId128 duplicatedStateId = CreateUniqueId(stateIds);
                stateIds.Add(duplicatedStateId);
                stateIdRemaps.Add(state.StateId, duplicatedStateId);
            }

            var transitionIdRemaps = new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
            foreach (CoCoStateGraphTransitionRecord transition in sourceLayer.Transitions)
            {
                CoCoSerializedId128 duplicatedTransitionId = CreateUniqueId(transitionIds);
                transitionIds.Add(duplicatedTransitionId);
                transitionIdRemaps.Add(transition.TransitionId, duplicatedTransitionId);
            }

            int sourceLayerIndex = asset.Layers.IndexOf(sourceLayer);
            CoCoStateGraphAsset clone = UnityEngine.Object.Instantiate(asset);
            try
            {
                if (sourceLayerIndex < 0 || sourceLayerIndex >= clone.Layers.Count)
                {
                    return false;
                }

                CoCoStateGraphLayerRecord duplicatedLayer = clone.Layers[sourceLayerIndex];
                if (duplicatedLayer == null)
                {
                    return false;
                }

                duplicatedLayer.LayerId = duplicatedSerializedLayerId;
                duplicatedLayer.InitialStateId = stateIdRemaps[duplicatedLayer.InitialStateId];
                foreach (CoCoStateGraphStateRecord state in duplicatedLayer.States)
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

                foreach (CoCoStateGraphTransitionRecord transition in duplicatedLayer.Transitions)
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
                EditorUtility.SetDirty(asset);
                Undo.CollapseUndoOperations(undoGroup);

                CoCoLayerId.TryCreate(
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
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            out CoCoStateGraphLayerRecord result)
        {
            result = null;
            if (!layerId.IsValid)
            {
                return false;
            }

            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
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
            CoCoStateGraphAsset asset,
            CoCoStateGraphLayerRecord sourceLayer,
            out HashSet<CoCoSerializedId128> layerIds,
            out HashSet<CoCoSerializedId128> stateIds,
            out HashSet<CoCoSerializedId128> transitionIds)
        {
            layerIds = new HashSet<CoCoSerializedId128>();
            stateIds = new HashSet<CoCoSerializedId128>();
            transitionIds = new HashSet<CoCoSerializedId128>();

            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !layer.LayerId.IsValid || !layerIds.Add(layer.LayerId))
                {
                    return false;
                }

                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    if (state == null || !state.StateId.IsValid || !stateIds.Add(state.StateId))
                    {
                        return false;
                    }
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
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

            var sourceStates = new Dictionary<CoCoSerializedId128, CoCoStateGraphStateRecord>();
            var childCounts = new Dictionary<CoCoSerializedId128, int>();
            foreach (CoCoStateGraphStateRecord state in sourceLayer.States)
            {
                sourceStates.Add(state.StateId, state);
            }

            if (!sourceLayer.InitialStateId.IsValid ||
                !sourceStates.TryGetValue(sourceLayer.InitialStateId, out CoCoStateGraphStateRecord initialState) ||
                initialState.ParentStateId.IsValid)
            {
                return false;
            }

            foreach (CoCoStateGraphStateRecord state in sourceLayer.States)
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

            foreach (CoCoStateGraphStateRecord state in sourceLayer.States)
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
                            out CoCoStateGraphStateRecord initialChild) ||
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

            foreach (CoCoStateGraphTransitionRecord transition in sourceLayer.Transitions)
            {
                if (!sourceStates.ContainsKey(transition.SourceStateId) ||
                    !sourceStates.ContainsKey(transition.TargetStateId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsParentCycle(
            CoCoStateGraphStateRecord state,
            IReadOnlyDictionary<CoCoSerializedId128, CoCoStateGraphStateRecord> states)
        {
            var visited = new HashSet<CoCoSerializedId128>();
            CoCoStateGraphStateRecord current = state;
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

        private static CoCoSerializedId128 CreateUniqueId(ISet<CoCoSerializedId128> existingIds)
        {
            CoCoSerializedId128 result;
            do
            {
                result = CoCoSerializedId128.NewId();
            }
            while (existingIds.Contains(result));

            return result;
        }

        private static HashSet<CoCoSerializedId128> CollectSubtreeIds(
            CoCoStateGraphLayerRecord layer,
            CoCoSerializedId128 rootStateId)
        {
            var result = new HashSet<CoCoSerializedId128> { rootStateId };
            bool added;
            do
            {
                added = false;
                foreach (CoCoStateGraphStateRecord state in layer.States)
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

        private static CoCoStateGraphLayerRecord RequireLayer(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId)
        {
            if (!layerId.IsValid)
            {
                throw new ArgumentException("A valid Layer ID is required.", nameof(layerId));
            }

            CoCoStateGraphLayerRecord layer = FindLayer(asset, layerId);
            return layer ?? throw new ArgumentException(
                "The Layer ID does not belong to the supplied StateGraph Asset.",
                nameof(layerId));
        }

        private static CoCoStateGraphLayerRecord FindLayer(CoCoStateGraphAsset asset, CoCoLayerId layerId)
        {
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
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

        private static CoCoStateGraphStateRecord FindState(
            CoCoStateGraphLayerRecord layer,
            CoCoStateId stateId)
        {
            if (!stateId.IsValid)
            {
                return null;
            }

            foreach (CoCoStateGraphStateRecord state in layer.States)
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

        private static CoCoSerializedId128 RemapIfPresent(
            CoCoSerializedId128 source,
            IReadOnlyDictionary<CoCoSerializedId128, CoCoSerializedId128> remaps)
        {
            return source.IsValid && remaps.TryGetValue(source, out CoCoSerializedId128 remapped)
                ? remapped
                : source;
        }

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static void RequireAsset(CoCoStateGraphAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }
        }
    }
}
