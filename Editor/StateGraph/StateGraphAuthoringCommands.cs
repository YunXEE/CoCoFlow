using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    internal readonly struct StateGraphDeleteImpact
    {
        internal StateGraphDeleteImpact(int stateCount, int transitionCount)
        {
            StateCount = stateCount;
            TransitionCount = transitionCount;
        }

        internal int StateCount { get; }
        internal int TransitionCount { get; }
    }

    internal sealed class StateGraphSubtreeClipboard : IDisposable
    {
        internal StateGraphSubtreeClipboard(
            StateGraphAsset snapshot,
            string assetGuid,
            EntityId sourceAssetEntityId,
            GraphId graphId,
            LayerId layerId,
            StateId rootStateId)
        {
            Snapshot = snapshot;
            AssetGuid = assetGuid ?? string.Empty;
            SourceAssetEntityId = sourceAssetEntityId;
            GraphId = graphId;
            LayerId = layerId;
            RootStateId = rootStateId;
        }

        internal StateGraphAsset Snapshot { get; private set; }
        internal string AssetGuid { get; }
        internal EntityId SourceAssetEntityId { get; }
        internal GraphId GraphId { get; }
        internal LayerId LayerId { get; }
        internal StateId RootStateId { get; }

        public void Dispose()
        {
            if (Snapshot != null)
            {
                UnityEngine.Object.DestroyImmediate(Snapshot);
                Snapshot = null;
            }
        }
    }

    public static partial class StateGraphAuthoringOperations
    {
        internal static bool CanEdit(out string failure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure = "StateGraph authoring is read-only while entering or running Play Mode.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        internal static bool TryRenameLayer(
            StateGraphAsset asset,
            LayerId layerId,
            string displayName,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            if (layer == null)
            {
                return Fail("The Layer ID does not belong to the supplied StateGraph Asset.", out failure);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                return Fail("A non-empty Layer name is required.", out failure);
            }

            if (string.Equals(layer.DisplayName, displayName, StringComparison.Ordinal))
            {
                failure = string.Empty;
                return true;
            }

            Record(asset, "Rename State Graph Layer");
            layer.DisplayName = displayName.Trim();
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryMoveLayer(
            StateGraphAsset asset,
            LayerId layerId,
            int targetIndex,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            int sourceIndex = layer == null ? -1 : asset.Layers.IndexOf(layer);
            if (sourceIndex < 0)
            {
                return Fail("The Layer ID does not belong to the supplied StateGraph Asset.", out failure);
            }

            if (targetIndex < 0 || targetIndex >= asset.Layers.Count)
            {
                return Fail("The target Layer index is outside the Asset.", out failure);
            }

            if (sourceIndex == targetIndex)
            {
                failure = string.Empty;
                return true;
            }

            Record(asset, "Reorder State Graph Layer");
            asset.Layers.RemoveAt(sourceIndex);
            asset.Layers.Insert(targetIndex, layer);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryDeleteLayer(
            StateGraphAsset asset,
            LayerId layerId,
            out StateGraphDeleteImpact impact,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            int layerIndex = layer == null ? -1 : asset.Layers.IndexOf(layer);
            if (layerIndex < 0)
            {
                impact = default;
                return Fail("The Layer ID does not belong to the supplied StateGraph Asset.", out failure);
            }

            if (!asset.EditorLayout.IsSupported)
            {
                impact = default;
                return Fail(
                    $"EditorLayout version {asset.EditorLayout.Version} is not supported by this Editor.",
                    out failure);
            }

            impact = new StateGraphDeleteImpact(
                layer.States.Count + (layer.AnyState == null ? 0 : 1), layer.Transitions.Count);
            var stateIds = new List<SerializedId128>(layer.States.Count);
            foreach (StateGraphStateRecord state in layer.AllStates)
            {
                if (state != null)
                {
                    stateIds.Add(state.StateId);
                }
            }

            Record(asset, "Delete State Graph Layer");
            asset.Layers.RemoveAt(layerIndex);
            foreach (SerializedId128 stateId in stateIds)
            {
                asset.EditorLayout.Remove(stateId);
            }

            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryAddState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId parentStateId,
            StateDescriptorId descriptorId,
            StateConfig config,
            string displayName,
            Vector2 localPosition,
            out StateId stateId,
            out string failure)
        {
            RequireAsset(asset);
            stateId = default;
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            if (layer == null)
            {
                return Fail("The Layer ID does not belong to the supplied StateGraph Asset.", out failure);
            }

            if (!asset.EditorLayout.IsSupported)
            {
                return Fail(
                    $"EditorLayout version {asset.EditorLayout.Version} is not supported by this Editor.",
                    out failure);
            }

            if (!IsFinite(localPosition))
            {
                return Fail("A finite local State position is required.", out failure);
            }

            StateGraphStateRecord parent = null;
            if (parentStateId.IsValid)
            {
                parent = FindState(layer, parentStateId);
                if (parent == null)
                {
                    return Fail("The parent State does not belong to the target Layer.", out failure);
                }

                if (!HasChildren(layer, parent.StateId) && HasIncidentTransition(layer, parent.StateId))
                {
                    return Fail(
                        "A leaf State with an incident Transition cannot become a composite State.",
                        out failure);
                }
            }

            SerializedId128 serializedId = SerializedId128.NewId();
            Record(asset, "Add State Graph State");
            layer.States.Add(new StateGraphStateRecord(
                serializedId,
                Serialize(parentStateId),
                string.IsNullOrWhiteSpace(displayName)
                    ? $"State {layer.States.Count + 1}"
                    : displayName.Trim(),
                Serialize(descriptorId),
                config));
            if (parent == null && !layer.InitialStateId.IsValid)
            {
                layer.InitialStateId = serializedId;
            }
            else if (parent != null && !parent.InitialChildStateId.IsValid)
            {
                parent.InitialChildStateId = serializedId;
            }

            asset.EditorLayout.SetPosition(serializedId, localPosition);
            Dirty(asset);
            StateId.TryCreate(serializedId.High, serializedId.Low, out stateId);
            failure = string.Empty;
            return true;
        }

        internal static bool TryAddAnyState(
            StateGraphAsset asset,
            LayerId layerId,
            StateDescriptor descriptor,
            StateConfig config,
            Vector2 position,
            out StateId stateId,
            out string failure)
        {
            RequireAsset(asset);
            stateId = default;
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            if (layer == null || layer.AnyState != null)
            {
                return Fail("Select a Layer without an AnyState binding.", out failure);
            }

            if (descriptor == null || !typeof(AnyStateLogic).IsAssignableFrom(descriptor.LogicType))
            {
                return Fail("AnyState requires a AnyStateLogic descriptor.", out failure);
            }

            if (!asset.EditorLayout.IsSupported || !IsFinite(position))
            {
                return Fail("AnyState requires a supported layout and finite position.", out failure);
            }

            SerializedId128 id = SerializedId128.NewId();
            Record(asset, "Add State Graph AnyState");
            layer.AnyState = new StateGraphStateRecord(
                id, default, "AnyState", Serialize(descriptor.DescriptorId), config);
            asset.EditorLayout.SetPosition(id, position);
            Dirty(asset);
            StateId.TryCreate(id.High, id.Low, out stateId);
            failure = string.Empty;
            return true;
        }

        internal static bool TryDeleteAnyState(
            StateGraphAsset asset,
            LayerId layerId,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            if (layer?.AnyState == null || !asset.EditorLayout.IsSupported)
            {
                return Fail("Select a configured AnyState with a supported layout.", out failure);
            }

            SerializedId128 id = layer.AnyState.StateId;
            Record(asset, "Delete State Graph AnyState");
            layer.Transitions.RemoveAll(edge => edge != null &&
                (edge.SourceStateId == id || edge.TargetStateId == id));
            layer.AnyState = null;
            asset.EditorLayout.Remove(id);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        private static StateGraphStateRecord FindEditableState(
            StateGraphLayerRecord layer,
            StateId stateId)
        {
            return layer?.AnyState != null && layer.AnyState.StateId == Serialize(stateId)
                ? layer.AnyState
                : layer == null ? null : FindState(layer, stateId);
        }

        internal static bool TryRenameState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId stateId,
            string displayName,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord state = FindEditableState(layer, stateId);
            if (state == null)
            {
                return Fail("The State ID does not belong to the target Layer.", out failure);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                return Fail("A non-empty State name is required.", out failure);
            }

            if (string.Equals(state.DisplayName, displayName, StringComparison.Ordinal))
            {
                failure = string.Empty;
                return true;
            }

            Record(asset, "Rename State Graph State");
            state.DisplayName = displayName.Trim();
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TrySetStateDescriptor(
            StateGraphAsset asset,
            LayerId layerId,
            StateId stateId,
            StateDescriptorId descriptorId,
            StateConfig config,
            out string failure)
        {
            RequireAsset(asset);
            if (!descriptorId.IsValid)
            {
                return Fail("A valid State descriptor ID is required.", out failure);
            }

            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord state = FindEditableState(layer, stateId);
            if (state == null)
            {
                return Fail("The State ID does not belong to the target Layer.", out failure);
            }

            Record(asset, "Set State Graph Descriptor");
            state.StateDescriptorId = Serialize(descriptorId);
            state.Config = config;
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryMoveState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId stateId,
            int targetIndex,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord state = layer == null ? null : FindState(layer, stateId);
            int sourceIndex = state == null ? -1 : layer.States.IndexOf(state);
            if (sourceIndex < 0)
            {
                return Fail("The State ID does not belong to the target Layer.", out failure);
            }

            if (targetIndex < 0 || targetIndex >= layer.States.Count)
            {
                return Fail("The target State index is outside the Layer.", out failure);
            }

            if (sourceIndex == targetIndex)
            {
                failure = string.Empty;
                return true;
            }

            Record(asset, "Reorder State Graph State");
            layer.States.RemoveAt(sourceIndex);
            layer.States.Insert(targetIndex, state);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TrySetInitialState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId stateId,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord state = layer == null ? null : FindState(layer, stateId);
            if (state == null || state.ParentStateId.IsValid)
            {
                return Fail("The Layer initial State must be a root State in that Layer.", out failure);
            }

            Record(asset, "Set Initial State");
            layer.InitialStateId = state.StateId;
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TrySetInitialChildState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId parentStateId,
            StateId childStateId,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord parent = layer == null ? null : FindState(layer, parentStateId);
            StateGraphStateRecord child = layer == null ? null : FindState(layer, childStateId);
            if (parent == null || child == null || child.ParentStateId != parent.StateId)
            {
                return Fail("The initial child must be a direct child of the selected State.", out failure);
            }

            Record(asset, "Set Initial Child State");
            parent.InitialChildStateId = child.StateId;
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryReparentStateSubtree(
            StateGraphAsset asset,
            LayerId layerId,
            StateId rootStateId,
            StateId targetParentStateId,
            StateId replacementOldInitialStateId,
            Vector2 localPosition,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord root = layer == null ? null : FindState(layer, rootStateId);
            if (root == null)
            {
                return Fail("The subtree root does not belong to the target Layer.", out failure);
            }

            if (!asset.EditorLayout.IsSupported || !IsFinite(localPosition))
            {
                return Fail("The EditorLayout is unsupported or the target position is invalid.", out failure);
            }

            StateGraphStateRecord targetParent = null;
            if (targetParentStateId.IsValid)
            {
                targetParent = FindState(layer, targetParentStateId);
                if (targetParent == null)
                {
                    return Fail("The target parent does not belong to the target Layer.", out failure);
                }

                HashSet<SerializedId128> subtreeIds = CollectSubtreeIds(layer, root.StateId);
                if (subtreeIds.Contains(targetParent.StateId))
                {
                    return Fail("A State subtree cannot be reparented beneath itself.", out failure);
                }

                if (!HasChildren(layer, targetParent.StateId) && HasIncidentTransition(layer, targetParent.StateId))
                {
                    return Fail(
                        "A leaf State with an incident Transition cannot become a composite State.",
                        out failure);
                }
            }

            SerializedId128 targetParentId = targetParent == null
                ? default
                : targetParent.StateId;
            if (root.ParentStateId == targetParentId)
            {
                Record(asset, "Move State Graph Subtree");
                asset.EditorLayout.SetPosition(root.StateId, localPosition);
                Dirty(asset);
                failure = string.Empty;
                return true;
            }

            if (!TryValidateInitialReplacement(
                    layer,
                    root,
                    replacementOldInitialStateId,
                    out StateGraphStateRecord oldParent,
                    out SerializedId128 replacement,
                    out failure))
            {
                return false;
            }

            Record(asset, "Reparent State Graph Subtree");
            if (oldParent == null)
            {
                if (layer.InitialStateId == root.StateId)
                {
                    layer.InitialStateId = replacement;
                }
            }
            else if (oldParent.InitialChildStateId == root.StateId)
            {
                oldParent.InitialChildStateId = replacement;
            }

            root.ParentStateId = targetParent == null ? default : targetParent.StateId;
            if (targetParent == null)
            {
                if (!layer.InitialStateId.IsValid)
                {
                    layer.InitialStateId = root.StateId;
                }
            }
            else if (!targetParent.InitialChildStateId.IsValid)
            {
                targetParent.InitialChildStateId = root.StateId;
            }

            asset.EditorLayout.SetPosition(root.StateId, localPosition);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryGetDeleteImpact(
            StateGraphAsset asset,
            LayerId layerId,
            StateId rootStateId,
            out StateGraphDeleteImpact impact,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord root = layer == null ? null : FindState(layer, rootStateId);
            if (root == null)
            {
                impact = default;
                return Fail("The subtree root does not belong to the target Layer.", out failure);
            }

            if (!asset.EditorLayout.IsSupported)
            {
                impact = default;
                return Fail(
                    $"EditorLayout version {asset.EditorLayout.Version} is not supported by this Editor.",
                    out failure);
            }

            HashSet<SerializedId128> subtreeIds = CollectSubtreeIds(layer, root.StateId);
            int transitionCount = 0;
            foreach (StateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null &&
                    (subtreeIds.Contains(transition.SourceStateId) ||
                     subtreeIds.Contains(transition.TargetStateId)))
                {
                    transitionCount++;
                }
            }

            impact = new StateGraphDeleteImpact(subtreeIds.Count, transitionCount);
            failure = string.Empty;
            return true;
        }

        internal static bool TryDeleteStateSubtree(
            StateGraphAsset asset,
            LayerId layerId,
            StateId rootStateId,
            StateId replacementInitialStateId,
            out StateGraphDeleteImpact impact,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord root = layer == null ? null : FindState(layer, rootStateId);
            if (root == null)
            {
                impact = default;
                return Fail("The subtree root does not belong to the target Layer.", out failure);
            }

            if (!asset.EditorLayout.IsSupported)
            {
                impact = default;
                return Fail(
                    $"EditorLayout version {asset.EditorLayout.Version} is not supported by this Editor.",
                    out failure);
            }

            HashSet<SerializedId128> subtreeIds = CollectSubtreeIds(layer, root.StateId);
            var candidates = new List<StateGraphStateRecord>();
            foreach (StateGraphStateRecord state in layer.States)
            {
                if (state != null &&
                    !subtreeIds.Contains(state.StateId) &&
                    state.ParentStateId == root.ParentStateId)
                {
                    candidates.Add(state);
                }
            }

            bool deletingInitial = root.ParentStateId.IsValid
                ? FindState(layer, root.ParentStateId)?.InitialChildStateId == root.StateId
                : layer.InitialStateId == root.StateId;
            SerializedId128 replacement = default;
            if (deletingInitial && candidates.Count > 0)
            {
                StateGraphStateRecord replacementState = FindState(layer, replacementInitialStateId);
                if (replacementState == null ||
                    replacementState.ParentStateId != root.ParentStateId ||
                    subtreeIds.Contains(replacementState.StateId))
                {
                    impact = default;
                    return Fail(
                        "Deleting an initial State requires an explicit replacement from the same scope.",
                        out failure);
                }

                replacement = replacementState.StateId;
            }

            int transitionCount = 0;
            foreach (StateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null &&
                    (subtreeIds.Contains(transition.SourceStateId) ||
                     subtreeIds.Contains(transition.TargetStateId)))
                {
                    transitionCount++;
                }
            }

            impact = new StateGraphDeleteImpact(subtreeIds.Count, transitionCount);
            Record(asset, "Delete State Graph Subtree");
            for (int index = layer.Transitions.Count - 1; index >= 0; index--)
            {
                StateGraphTransitionRecord transition = layer.Transitions[index];
                if (transition == null ||
                    subtreeIds.Contains(transition.SourceStateId) ||
                    subtreeIds.Contains(transition.TargetStateId))
                {
                    layer.Transitions.RemoveAt(index);
                }
            }

            for (int index = layer.States.Count - 1; index >= 0; index--)
            {
                StateGraphStateRecord state = layer.States[index];
                if (state != null && subtreeIds.Contains(state.StateId))
                {
                    layer.States.RemoveAt(index);
                }
            }

            if (deletingInitial)
            {
                if (root.ParentStateId.IsValid)
                {
                    StateGraphStateRecord parent = FindState(layer, root.ParentStateId);
                    if (parent != null)
                    {
                        parent.InitialChildStateId = replacement;
                    }
                }
                else
                {
                    layer.InitialStateId = replacement;
                }
            }

            foreach (SerializedId128 stateId in subtreeIds)
            {
                asset.EditorLayout.Remove(stateId);
            }

            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TrySetStatePosition(
            StateGraphAsset asset,
            LayerId layerId,
            StateId stateId,
            Vector2 localPosition,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphStateRecord state = FindEditableState(layer, stateId);
            if (state == null)
            {
                return Fail("The State ID does not belong to the target Layer.", out failure);
            }

            if (!asset.EditorLayout.IsSupported)
            {
                return Fail(
                    $"EditorLayout version {asset.EditorLayout.Version} is not supported by this Editor.",
                    out failure);
            }

            if (!IsFinite(localPosition))
            {
                return Fail("A finite local State position is required.", out failure);
            }

            if (asset.EditorLayout.TryGetPosition(state.StateId, out Vector2 current) &&
                current == localPosition)
            {
                failure = string.Empty;
                return true;
            }

            Record(asset, "Move State Graph State");
            asset.EditorLayout.SetPosition(state.StateId, localPosition);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryRepairEditorLayout(StateGraphAsset asset, out string failure)
        {
            RequireAsset(asset);
            var validStateIds = new HashSet<SerializedId128>();
            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null)
                {
                    continue;
                }

                foreach (StateGraphStateRecord state in layer.AllStates)
                {
                    if (state != null && state.StateId.IsValid)
                    {
                        validStateIds.Add(state.StateId);
                    }
                }
            }

            Record(asset, "Repair State Graph Editor Layout");
            asset.EditorLayout.Repair(validStateIds);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryAddTransition(
            StateGraphAsset asset,
            LayerId layerId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window,
            GraphDescriptorCatalog catalog,
            out TransitionId transitionId,
            out string failure)
        {
            RequireAsset(asset);
            transitionId = default;
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            if (!ValidateTransition(
                    layer,
                    sourceStateId,
                    targetStateId,
                    priority,
                    window,
                    catalog,
                    ignoredTransition: null,
                    out failure))
            {
                return false;
            }

            SerializedId128 serializedId = SerializedId128.NewId();
            Record(asset, "Add State Graph Transition");
            var transition = new StateGraphTransitionRecord(
                serializedId,
                Serialize(sourceStateId),
                Serialize(targetStateId),
                priority)
            {
                WindowMode = window.Mode,
                WindowStartInclusive = window.StartInclusive,
                WindowEndExclusive = window.EndExclusive
            };
            layer.Transitions.Add(transition);
            Dirty(asset);
            TransitionId.TryCreate(serializedId.High, serializedId.Low, out transitionId);
            failure = string.Empty;
            return true;
        }

        internal static bool TryUpdateTransition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window,
            GraphDescriptorCatalog catalog,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphTransitionRecord transition =
                layer == null ? null : FindTransition(layer, transitionId);
            if (transition == null)
            {
                return Fail("The Transition ID does not belong to the target Layer.", out failure);
            }

            if (!ValidateTransition(
                    layer,
                    sourceStateId,
                    targetStateId,
                    priority,
                    window,
                    catalog,
                    transition,
                    out failure))
            {
                return false;
            }

            Record(asset, "Update State Graph Transition");
            transition.SourceStateId = Serialize(sourceStateId);
            transition.TargetStateId = Serialize(targetStateId);
            transition.Priority = priority;
            transition.WindowMode = window.Mode;
            transition.WindowStartInclusive = window.StartInclusive;
            transition.WindowEndExclusive = window.EndExclusive;
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryDeleteTransition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphTransitionRecord transition =
                layer == null ? null : FindTransition(layer, transitionId);
            int index = transition == null ? -1 : layer.Transitions.IndexOf(transition);
            if (index < 0)
            {
                return Fail("The Transition ID does not belong to the target Layer.", out failure);
            }

            Record(asset, "Delete State Graph Transition");
            layer.Transitions.RemoveAt(index);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryAddCondition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            ConditionDescriptorId descriptorId,
            ConditionConfig config,
            out int conditionIndex,
            out string failure)
        {
            RequireAsset(asset);
            conditionIndex = -1;
            if (!descriptorId.IsValid)
            {
                return Fail("A valid Condition descriptor ID is required.", out failure);
            }

            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphTransitionRecord transition =
                layer == null ? null : FindTransition(layer, transitionId);
            if (transition == null)
            {
                return Fail("The Transition ID does not belong to the target Layer.", out failure);
            }

            Record(asset, "Add State Graph Condition");
            transition.Conditions.Add(new StateGraphConditionRecord(Serialize(descriptorId), config));
            conditionIndex = transition.Conditions.Count - 1;
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TrySetCondition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            int conditionIndex,
            ConditionDescriptorId descriptorId,
            ConditionConfig config,
            out string failure)
        {
            RequireAsset(asset);
            if (!descriptorId.IsValid)
            {
                return Fail("A valid Condition descriptor ID is required.", out failure);
            }

            if (!TryGetCondition(
                    asset,
                    layerId,
                    transitionId,
                    conditionIndex,
                    out StateGraphConditionRecord condition,
                    out failure))
            {
                return false;
            }

            Record(asset, "Update State Graph Condition");
            condition.ConditionDescriptorId = Serialize(descriptorId);
            condition.Config = config;
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryMoveCondition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            int sourceIndex,
            int targetIndex,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphTransitionRecord transition =
                layer == null ? null : FindTransition(layer, transitionId);
            if (transition == null ||
                sourceIndex < 0 ||
                sourceIndex >= transition.Conditions.Count ||
                targetIndex < 0 ||
                targetIndex >= transition.Conditions.Count)
            {
                return Fail("The Condition reorder indices are invalid.", out failure);
            }

            if (sourceIndex == targetIndex)
            {
                failure = string.Empty;
                return true;
            }

            StateGraphConditionRecord condition = transition.Conditions[sourceIndex];
            Record(asset, "Reorder State Graph Condition");
            transition.Conditions.RemoveAt(sourceIndex);
            transition.Conditions.Insert(targetIndex, condition);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryDeleteCondition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            int conditionIndex,
            out string failure)
        {
            RequireAsset(asset);
            if (!TryGetCondition(
                    asset,
                    layerId,
                    transitionId,
                    conditionIndex,
                    out _,
                    out failure))
            {
                return false;
            }

            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphTransitionRecord transition = FindTransition(layer, transitionId);
            Record(asset, "Delete State Graph Condition");
            transition.Conditions.RemoveAt(conditionIndex);
            Dirty(asset);
            failure = string.Empty;
            return true;
        }

        internal static bool TryCaptureSubtree(
            StateGraphAsset asset,
            LayerId layerId,
            StateId rootStateId,
            out StateGraphSubtreeClipboard clipboard,
            out string failure)
        {
            RequireAsset(asset);
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            if (layer == null || FindState(layer, rootStateId) == null)
            {
                clipboard = null;
                return Fail("The subtree root does not belong to the target Layer.", out failure);
            }

            StateGraphAsset snapshot = UnityEngine.Object.Instantiate(asset);
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string assetGuid = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
            clipboard = new StateGraphSubtreeClipboard(
                snapshot,
                assetGuid,
                GetObjectEntityId(asset),
                asset.GraphId,
                layerId,
                rootStateId);
            failure = string.Empty;
            return true;
        }

        internal static bool TryPasteSubtree(
            StateGraphAsset asset,
            StateGraphSubtreeClipboard clipboard,
            LayerId targetLayerId,
            StateId targetParentStateId,
            Vector2 rootLocalPosition,
            out StateId pastedRootStateId,
            out string failure)
        {
            RequireAsset(asset);
            pastedRootStateId = default;
            if (clipboard?.Snapshot == null)
            {
                return Fail("The State Graph clipboard is empty.", out failure);
            }

            string targetPath = AssetDatabase.GetAssetPath(asset);
            string targetGuid = string.IsNullOrEmpty(targetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(targetPath);
            bool sameAsset = clipboard.GraphId == asset.GraphId &&
                             ((!string.IsNullOrEmpty(clipboard.AssetGuid) &&
                               string.Equals(clipboard.AssetGuid, targetGuid, StringComparison.Ordinal)) ||
                              (string.IsNullOrEmpty(clipboard.AssetGuid) &&
                               clipboard.SourceAssetEntityId == GetObjectEntityId(asset)));
            if (!sameAsset)
            {
                return Fail("Subtree paste is limited to the same StateGraph Asset and Editor session.", out failure);
            }

            StateGraphLayerRecord targetLayer = FindLayer(asset, targetLayerId);
            if (targetLayer == null)
            {
                return Fail("The target Layer does not belong to the supplied StateGraph Asset.", out failure);
            }

            if (!asset.EditorLayout.IsSupported || !IsFinite(rootLocalPosition))
            {
                return Fail("The EditorLayout is unsupported or the paste position is invalid.", out failure);
            }

            StateGraphStateRecord targetParent = null;
            if (targetParentStateId.IsValid)
            {
                targetParent = FindState(targetLayer, targetParentStateId);
                if (targetParent == null)
                {
                    return Fail("The target parent does not belong to the target Layer.", out failure);
                }

                if (!HasChildren(targetLayer, targetParent.StateId) &&
                    HasIncidentTransition(targetLayer, targetParent.StateId))
                {
                    return Fail(
                        "A leaf State with an incident Transition cannot become a composite State.",
                        out failure);
                }
            }

            StateGraphAsset pasteSource = UnityEngine.Object.Instantiate(clipboard.Snapshot);
            try
            {
                StateGraphLayerRecord sourceLayer = FindLayer(pasteSource, clipboard.LayerId);
                StateGraphStateRecord sourceRoot =
                    sourceLayer == null ? null : FindState(sourceLayer, clipboard.RootStateId);
                if (sourceRoot == null)
                {
                    return Fail("The copied subtree is no longer valid.", out failure);
                }

                SerializedId128 sourceRootId = sourceRoot.StateId;

                HashSet<SerializedId128> subtreeIds = CollectSubtreeIds(sourceLayer, sourceRootId);
                var stateIdRemaps = new Dictionary<SerializedId128, SerializedId128>();
                foreach (SerializedId128 sourceId in subtreeIds)
                {
                    stateIdRemaps.Add(sourceId, SerializedId128.NewId());
                }

                var copiedStates = new List<StateGraphStateRecord>();
                foreach (StateGraphStateRecord state in sourceLayer.States)
                {
                    if (state != null && subtreeIds.Contains(state.StateId))
                    {
                        copiedStates.Add(state);
                    }
                }

                var copiedTransitions = new List<StateGraphTransitionRecord>();
                foreach (StateGraphTransitionRecord transition in sourceLayer.Transitions)
                {
                    if (transition != null &&
                        subtreeIds.Contains(transition.SourceStateId) &&
                        subtreeIds.Contains(transition.TargetStateId))
                    {
                        copiedTransitions.Add(transition);
                    }
                }

                Record(asset, "Paste State Graph Subtree");
                foreach (StateGraphStateRecord state in copiedStates)
                {
                    SerializedId128 sourceId = state.StateId;
                    state.StateId = stateIdRemaps[sourceId];
                    state.ParentStateId = sourceId == sourceRootId
                        ? targetParent == null ? default : targetParent.StateId
                        : RemapIfPresent(state.ParentStateId, stateIdRemaps);
                    state.InitialChildStateId = RemapIfPresent(
                        state.InitialChildStateId,
                        stateIdRemaps);
                    targetLayer.States.Add(state);

                    Vector2 position;
                    if (sourceId == sourceRootId)
                    {
                        position = rootLocalPosition;
                    }
                    else if (!pasteSource.EditorLayout.TryGetPosition(sourceId, out position))
                    {
                        position = DefaultPosition(targetLayer.States.Count - 1);
                    }

                    asset.EditorLayout.SetPosition(state.StateId, position);
                }

                foreach (StateGraphTransitionRecord transition in copiedTransitions)
                {
                    transition.TransitionId = SerializedId128.NewId();
                    transition.SourceStateId = stateIdRemaps[transition.SourceStateId];
                    transition.TargetStateId = stateIdRemaps[transition.TargetStateId];
                    targetLayer.Transitions.Add(transition);
                }

                SerializedId128 pastedRoot = stateIdRemaps[sourceRootId];
                if (targetParent == null && !targetLayer.InitialStateId.IsValid)
                {
                    targetLayer.InitialStateId = pastedRoot;
                }
                else if (targetParent != null && !targetParent.InitialChildStateId.IsValid)
                {
                    targetParent.InitialChildStateId = pastedRoot;
                }

                Dirty(asset);
                StateId.TryCreate(pastedRoot.High, pastedRoot.Low, out pastedRootStateId);
                failure = string.Empty;
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pasteSource);
            }
        }

        private static bool ValidateTransition(
            StateGraphLayerRecord layer,
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window,
            GraphDescriptorCatalog catalog,
            StateGraphTransitionRecord ignoredTransition,
            out string failure)
        {
            if (layer == null)
            {
                return Fail("The Layer ID does not belong to the supplied StateGraph Asset.", out failure);
            }

            StateGraphStateRecord source = FindEditableState(layer, sourceStateId);
            StateGraphStateRecord target = FindState(layer, targetStateId);
            if (source == null || target == null)
            {
                return Fail("Transition endpoints must belong to the same Layer.", out failure);
            }

            if (HasChildren(layer, source.StateId) || HasChildren(layer, target.StateId))
            {
                return Fail("Transition endpoints must be terminal leaf States.", out failure);
            }

            if (!window.IsValid)
            {
                return Fail("Transition Window must be Always or a valid [start, end) interval.", out failure);
            }

            foreach (StateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null &&
                    !ReferenceEquals(transition, ignoredTransition) &&
                    transition.SourceStateId == source.StateId &&
                    transition.Priority == priority)
                {
                    return Fail(
                        "Outgoing Transitions from one source State must use unique Priority values.",
                        out failure);
                }
            }

            if (window.Mode == TransitionWindowMode.ActionProgress)
            {
                StateDescriptorId.TryCreate(
                    source.StateDescriptorId.High,
                    source.StateDescriptorId.Low,
                    out StateDescriptorId descriptorId);
                if (catalog == null ||
                    !catalog.TryGetStateDescriptor(descriptorId, out StateDescriptor descriptor) ||
                    !descriptor.ProvidesActionProgress)
                {
                    return Fail(
                        "An ActionProgress Window requires a source descriptor that provides ActionProgress.",
                        out failure);
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryValidateInitialReplacement(
            StateGraphLayerRecord layer,
            StateGraphStateRecord root,
            StateId replacementStateId,
            out StateGraphStateRecord oldParent,
            out SerializedId128 replacement,
            out string failure)
        {
            oldParent = root.ParentStateId.IsValid ? FindState(layer, root.ParentStateId) : null;
            bool movingInitial = oldParent == null
                ? layer.InitialStateId == root.StateId
                : oldParent.InitialChildStateId == root.StateId;
            replacement = default;
            if (!movingInitial)
            {
                failure = string.Empty;
                return true;
            }

            var candidates = new List<StateGraphStateRecord>();
            foreach (StateGraphStateRecord state in layer.States)
            {
                if (state != null &&
                    state.StateId != root.StateId &&
                    state.ParentStateId == root.ParentStateId)
                {
                    candidates.Add(state);
                }
            }

            if (candidates.Count == 0)
            {
                failure = string.Empty;
                return true;
            }

            StateGraphStateRecord candidate = FindState(layer, replacementStateId);
            if (candidate == null ||
                candidate.StateId == root.StateId ||
                candidate.ParentStateId != root.ParentStateId)
            {
                return Fail(
                    "Moving an initial State requires an explicit replacement from the old scope.",
                    out failure);
            }

            replacement = candidate.StateId;
            failure = string.Empty;
            return true;
        }

        private static bool TryGetCondition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId,
            int conditionIndex,
            out StateGraphConditionRecord condition,
            out string failure)
        {
            StateGraphLayerRecord layer = FindLayer(asset, layerId);
            StateGraphTransitionRecord transition =
                layer == null ? null : FindTransition(layer, transitionId);
            if (transition == null ||
                conditionIndex < 0 ||
                conditionIndex >= transition.Conditions.Count)
            {
                condition = null;
                return Fail("The Condition index does not belong to the target Transition.", out failure);
            }

            condition = transition.Conditions[conditionIndex];
            if (condition == null)
            {
                return Fail("The selected Condition record is null.", out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static StateGraphTransitionRecord FindTransition(
            StateGraphLayerRecord layer,
            TransitionId transitionId)
        {
            if (layer == null || !transitionId.IsValid)
            {
                return null;
            }

            foreach (StateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null &&
                    transition.TransitionId.High == transitionId.High &&
                    transition.TransitionId.Low == transitionId.Low)
                {
                    return transition;
                }
            }

            return null;
        }

        private static StateGraphStateRecord FindState(
            StateGraphLayerRecord layer,
            SerializedId128 stateId)
        {
            if (layer == null || !stateId.IsValid)
            {
                return null;
            }

            foreach (StateGraphStateRecord state in layer.States)
            {
                if (state != null && state.StateId == stateId)
                {
                    return state;
                }
            }

            return null;
        }

        private static bool HasChildren(StateGraphLayerRecord layer, SerializedId128 stateId)
        {
            foreach (StateGraphStateRecord candidate in layer.States)
            {
                if (candidate != null && candidate.ParentStateId == stateId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasIncidentTransition(
            StateGraphLayerRecord layer,
            SerializedId128 stateId)
        {
            foreach (StateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null &&
                    (transition.SourceStateId == stateId || transition.TargetStateId == stateId))
                {
                    return true;
                }
            }

            return false;
        }

        private static SerializedId128 Serialize(TransitionId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(ConditionDescriptorId id) =>
            new SerializedId128(id.High, id.Low);

        private static Vector2 DefaultPosition(int index) =>
            new Vector2(40f + (index % 4) * 220f, 60f + (index / 4) * 150f);

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) &&
            !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y);

        private static EntityId GetObjectEntityId(UnityEngine.Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }

        private static void Record(StateGraphAsset asset, string name) => Undo.RecordObject(asset, name);

        private static void Dirty(StateGraphAsset asset) => EditorUtility.SetDirty(asset);

        private static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }
}
