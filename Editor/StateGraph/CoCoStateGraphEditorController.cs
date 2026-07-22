using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class CoCoStateGraphEditorController : IDisposable
    {
        private static CoCoStateGraphSubtreeClipboard clipboard;

        private CoCoGraphDescriptorCatalog catalog;
        private CoCoStateGraphAssetCompileResult analysisResult;

        internal CoCoStateGraphEditorController(CoCoStateGraphAsset asset)
        {
            Asset = asset != null ? asset : throw new ArgumentNullException(nameof(asset));
            Session = CoCoStateGraphEditorSessionState.Load(asset);
            ReloadCatalog();
            if (Session.AnalysisRequested && catalog != null)
            {
                try
                {
                    analysisResult = new CoCoStateGraphAssetCompiler().Compile(Asset, catalog);
                }
                catch (Exception exception)
                {
                    CommandFailure = $"StateGraph analysis failed after Domain Reload: {exception.Message}";
                }
            }

            Undo.undoRedoPerformed += OnUndoRedo;
            CoCoStateGraphEditorCatalogProvider.CatalogChanged += OnCatalogChanged;
        }

        internal event Action Changed;

        internal CoCoStateGraphAsset Asset { get; }
        internal CoCoStateGraphEditorSessionState Session { get; }
        internal CoCoGraphDescriptorCatalog Catalog => catalog;
        internal CoCoStateGraphAssetCompileResult AnalysisResult => analysisResult;
        internal string CatalogStatus { get; private set; } = string.Empty;
        internal string CommandFailure { get; private set; } = string.Empty;

        internal CoCoStateGraphLayerRecord SelectedLayer
        {
            get
            {
                foreach (CoCoStateGraphLayerRecord layer in Asset.Layers)
                {
                    if (layer != null && Matches(layer.LayerId, Session.SelectedLayerId))
                    {
                        return layer;
                    }
                }

                return null;
            }
        }

        internal IReadOnlyList<CoCoStateGraphStateRecord> VisibleStates
        {
            get
            {
                var result = new List<CoCoStateGraphStateRecord>();
                CoCoStateGraphLayerRecord layer = SelectedLayer;
                if (layer == null)
                {
                    return result;
                }

                CoCoSerializedId128 parentId = Serialize(Session.DrillRootStateId);
                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    if (state == null || state.ParentStateId != parentId)
                    {
                        continue;
                    }

                    result.Add(state);
                }

                return result;
            }
        }

        internal IReadOnlyList<CoCoStateGraphStateRecord> SearchResults
        {
            get
            {
                var result = new List<CoCoStateGraphStateRecord>();
                CoCoStateGraphLayerRecord layer = SelectedLayer;
                string search = Session.SearchText?.Trim() ?? string.Empty;
                if (layer == null || search.Length == 0)
                {
                    return result;
                }

                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    if (state == null)
                    {
                        continue;
                    }

                    string stateId = ToStateId(state.StateId).ToString();
                    if (state.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        stateId.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        StateDescriptorLabel(state).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(state);
                    }
                }

                return result;
            }
        }

        internal IReadOnlyList<CoCoStateGraphTransitionRecord> VisibleTransitions
        {
            get
            {
                var result = new List<CoCoStateGraphTransitionRecord>();
                CoCoStateGraphLayerRecord layer = SelectedLayer;
                if (layer == null)
                {
                    return result;
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
                {
                    if (transition != null)
                    {
                        result.Add(transition);
                    }
                }

                return result;
            }
        }

        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            CoCoStateGraphEditorCatalogProvider.CatalogChanged -= OnCatalogChanged;
            Session.Save();
        }

        internal void SelectLayer(CoCoLayerId layerId)
        {
            Session.SelectedLayerId = layerId;
            Session.DrillRootStateId = default;
            Session.SelectedStateId = default;
            Session.SelectedTransitionId = default;
            SaveAndNotify();
        }

        internal void SelectState(CoCoStateId stateId)
        {
            Session.SelectedStateId = stateId;
            Session.SelectedTransitionId = default;
            SaveAndNotify();
        }

        internal void NavigateToState(CoCoStateId stateId)
        {
            CoCoStateGraphStateRecord state = FindState(SelectedLayer, stateId);
            if (state == null)
            {
                return;
            }

            Session.DrillRootStateId = state.ParentStateId.IsValid
                ? ToStateId(state.ParentStateId)
                : default;
            Session.SelectedStateId = stateId;
            Session.SelectedTransitionId = default;
            SaveAndNotify();
        }

        internal void SelectTransition(CoCoTransitionId transitionId)
        {
            Session.SelectedTransitionId = transitionId;
            Session.SelectedStateId = default;
            SaveAndNotify();
        }

        internal void DrillInto(CoCoStateId stateId)
        {
            CoCoStateGraphLayerRecord layer = SelectedLayer;
            CoCoStateGraphStateRecord state = FindState(layer, stateId);
            if (state == null || !HasChildren(layer, state.StateId))
            {
                return;
            }

            Session.DrillRootStateId = stateId;
            Session.SelectedStateId = default;
            SaveAndNotify();
        }

        internal void DrillUp()
        {
            if (!Session.DrillRootStateId.IsValid)
            {
                return;
            }

            CoCoStateGraphStateRecord current = FindState(SelectedLayer, Session.DrillRootStateId);
            Session.DrillRootStateId = current != null && current.ParentStateId.IsValid
                ? ToStateId(current.ParentStateId)
                : default;
            Session.SelectedStateId = default;
            SaveAndNotify();
        }

        internal string BreadcrumbLabel
        {
            get
            {
                CoCoStateGraphLayerRecord layer = SelectedLayer;
                if (layer == null)
                {
                    return "No Layer";
                }

                var names = new List<string> { layer.DisplayName };
                var visited = new HashSet<CoCoSerializedId128>();
                CoCoStateGraphStateRecord current = FindState(layer, Session.DrillRootStateId);
                while (current != null)
                {
                    if (!visited.Add(current.StateId))
                    {
                        names.Insert(1, "<cycle>");
                        break;
                    }

                    names.Insert(1, current.DisplayName);
                    current = current.ParentStateId.IsValid
                        ? FindState(layer, current.ParentStateId)
                        : null;
                }

                return string.Join(" / ", names);
            }
        }

        internal void SetSearch(string value)
        {
            Session.SearchText = value ?? string.Empty;
            SaveAndNotify();
        }

        internal Vector2 GetPosition(CoCoStateGraphStateRecord state, int visibleIndex)
        {
            return state != null && Asset.EditorLayout.TryGetPosition(state.StateId, out Vector2 position)
                ? position
                : new Vector2(40f + (visibleIndex % 4) * 220f, 60f + (visibleIndex / 4) * 150f);
        }

        internal bool SetPosition(CoCoStateId stateId, Vector2 position)
        {
            if (!CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                    Asset,
                    Session.SelectedLayerId,
                    stateId,
                    position,
                    out string failure))
            {
                return SetFailure(failure);
            }

            return Succeed();
        }

        internal bool AddLayer()
        {
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(Asset);
            Session.SelectedLayerId = layerId;
            Session.DrillRootStateId = default;
            return Succeed();
        }

        internal bool RenameLayer(string displayName)
        {
            return Complete(CoCoStateGraphAuthoringOperations.TryRenameLayer(
                Asset,
                Session.SelectedLayerId,
                displayName,
                out string failure), failure);
        }

        internal bool MoveSelectedLayer(int delta)
        {
            CoCoStateGraphLayerRecord layer = SelectedLayer;
            if (layer == null)
            {
                return SetFailure("Select a Layer first.");
            }

            int currentIndex = Asset.Layers.IndexOf(layer);
            int targetIndex = Mathf.Clamp(currentIndex + delta, 0, Asset.Layers.Count - 1);
            return Complete(CoCoStateGraphAuthoringOperations.TryMoveLayer(
                Asset,
                Session.SelectedLayerId,
                targetIndex,
                out string failure), failure);
        }

        internal bool DuplicateSelectedLayer()
        {
            if (!CoCoStateGraphAuthoringOperations.DuplicateLayer(
                    Asset,
                    Session.SelectedLayerId,
                    out CoCoLayerId duplicatedLayerId))
            {
                return SetFailure("The selected Layer has invalid topology and could not be duplicated.");
            }

            Session.SelectedLayerId = duplicatedLayerId;
            Session.DrillRootStateId = default;
            return Succeed();
        }

        internal bool DeleteLayer()
        {
            bool succeeded = CoCoStateGraphAuthoringOperations.TryDeleteLayer(
                Asset,
                Session.SelectedLayerId,
                out _,
                out string failure);
            if (!succeeded)
            {
                return SetFailure(failure);
            }

            Session.Validate(Asset);
            return Succeed();
        }

        internal bool AddState(
            CoCoStateId parentStateId,
            CoCoStateDescriptor descriptor,
            string displayName,
            Vector2 position)
        {
            string failure = string.Empty;
            if (descriptor == null || !TryCreateStateConfig(descriptor, out CoCoStateConfig config, out failure))
            {
                return SetFailure(descriptor == null ? "Select a State descriptor first." : failure);
            }

            if (!CoCoStateGraphAuthoringOperations.TryAddState(
                    Asset,
                    Session.SelectedLayerId,
                    parentStateId,
                    descriptor.DescriptorId,
                    config,
                    displayName,
                    position,
                    out CoCoStateId stateId,
                    out failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedStateId = stateId;
            return Succeed();
        }

        internal bool RenameSelectedState(string displayName)
        {
            return Complete(CoCoStateGraphAuthoringOperations.TryRenameState(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                displayName,
                out string failure), failure);
        }

        internal bool MoveSelectedState(int delta)
        {
            CoCoStateGraphLayerRecord layer = SelectedLayer;
            CoCoStateGraphStateRecord state = FindState(layer, Session.SelectedStateId);
            if (state == null)
            {
                return SetFailure("Select a State first.");
            }

            int currentIndex = layer.States.IndexOf(state);
            int targetIndex = Mathf.Clamp(currentIndex + delta, 0, layer.States.Count - 1);
            return Complete(CoCoStateGraphAuthoringOperations.TryMoveState(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                targetIndex,
                out string failure), failure);
        }

        internal bool SetSelectedStateDescriptor(CoCoStateDescriptor descriptor)
        {
            string failure = string.Empty;
            if (descriptor == null || !TryCreateStateConfig(descriptor, out CoCoStateConfig config, out failure))
            {
                return SetFailure(descriptor == null ? "Select a State descriptor first." : failure);
            }

            return Complete(CoCoStateGraphAuthoringOperations.TrySetStateDescriptor(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                descriptor.DescriptorId,
                config,
                out failure), failure);
        }

        internal bool SetSelectedStateInitial()
        {
            CoCoStateGraphStateRecord state = FindState(SelectedLayer, Session.SelectedStateId);
            if (state == null)
            {
                return SetFailure("Select a State first.");
            }

            if (!state.ParentStateId.IsValid)
            {
                return Complete(CoCoStateGraphAuthoringOperations.TrySetInitialState(
                    Asset,
                    Session.SelectedLayerId,
                    Session.SelectedStateId,
                    out string failure), failure);
            }

            CoCoStateId parentStateId = ToStateId(state.ParentStateId);
            return Complete(CoCoStateGraphAuthoringOperations.TrySetInitialChildState(
                Asset,
                Session.SelectedLayerId,
                parentStateId,
                Session.SelectedStateId,
                out string childFailure), childFailure);
        }

        internal bool ReparentSelectedState(
            CoCoStateId targetParentStateId,
            CoCoStateId replacementOldInitialStateId,
            Vector2 position)
        {
            return Complete(CoCoStateGraphAuthoringOperations.TryReparentStateSubtree(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                targetParentStateId,
                replacementOldInitialStateId,
                position,
                out string failure), failure);
        }

        internal bool DeleteSelectedState(CoCoStateId replacementInitialStateId)
        {
            bool succeeded = CoCoStateGraphAuthoringOperations.TryDeleteStateSubtree(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                replacementInitialStateId,
                out _,
                out string failure);
            if (!succeeded)
            {
                return SetFailure(failure);
            }

            Session.SelectedStateId = default;
            Session.Validate(Asset);
            return Succeed();
        }

        internal bool CopySelectedState()
        {
            if (!CoCoStateGraphAuthoringOperations.TryCaptureSubtree(
                    Asset,
                    Session.SelectedLayerId,
                    Session.SelectedStateId,
                    out CoCoStateGraphSubtreeClipboard captured,
                    out string failure))
            {
                return SetFailure(failure);
            }

            clipboard?.Dispose();
            clipboard = captured;
            return Succeed();
        }

        internal bool PasteState(CoCoStateId parentStateId, Vector2 position)
        {
            if (!CoCoStateGraphAuthoringOperations.TryPasteSubtree(
                    Asset,
                    clipboard,
                    Session.SelectedLayerId,
                    parentStateId,
                    position,
                    out CoCoStateId pastedRoot,
                    out string failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedStateId = pastedRoot;
            return Succeed();
        }

        internal bool AddTransition(
            CoCoStateId sourceStateId,
            CoCoStateId targetStateId,
            int priority,
            CoCoTransitionWindow window)
        {
            if (!CoCoStateGraphAuthoringOperations.TryAddTransition(
                    Asset,
                    Session.SelectedLayerId,
                    sourceStateId,
                    targetStateId,
                    priority,
                    window,
                    catalog,
                    out CoCoTransitionId transitionId,
                    out string failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedTransitionId = transitionId;
            return Succeed();
        }

        internal bool DeleteSelectedTransition()
        {
            if (!CoCoStateGraphAuthoringOperations.TryDeleteTransition(
                    Asset,
                    Session.SelectedLayerId,
                    Session.SelectedTransitionId,
                    out string failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedTransitionId = default;
            return Succeed();
        }

        internal bool UpdateSelectedTransition(
            CoCoStateId sourceStateId,
            CoCoStateId targetStateId,
            int priority,
            CoCoTransitionWindow window)
        {
            return Complete(CoCoStateGraphAuthoringOperations.TryUpdateTransition(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedTransitionId,
                sourceStateId,
                targetStateId,
                priority,
                window,
                catalog,
                out string failure), failure);
        }

        internal bool AddCondition(CoCoConditionDescriptor descriptor)
        {
            string failure = string.Empty;
            if (descriptor == null ||
                !TryCreateConditionConfig(descriptor, out CoCoConditionConfig config, out failure))
            {
                return SetFailure(descriptor == null ? "Select a Condition descriptor first." : failure);
            }

            return Complete(CoCoStateGraphAuthoringOperations.TryAddCondition(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedTransitionId,
                descriptor.DescriptorId,
                config,
                out _,
                out failure), failure);
        }

        internal bool DeleteCondition(int conditionIndex)
        {
            return Complete(CoCoStateGraphAuthoringOperations.TryDeleteCondition(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedTransitionId,
                conditionIndex,
                out string failure), failure);
        }

        internal bool MoveCondition(int sourceIndex, int targetIndex)
        {
            return Complete(CoCoStateGraphAuthoringOperations.TryMoveCondition(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedTransitionId,
                sourceIndex,
                targetIndex,
                out string failure), failure);
        }

        internal void Locate(CoCoGraphDiagnosticLocation location)
        {
            if (location.LayerId.IsValid)
            {
                Session.SelectedLayerId = location.LayerId;
            }

            Session.SelectedStateId = location.StateId;
            Session.SelectedTransitionId = location.TransitionId;
            Session.SelectedDiagnosticLocation = location;
            if (location.StateId.IsValid)
            {
                CoCoStateGraphStateRecord state = FindState(SelectedLayer, location.StateId);
                Session.DrillRootStateId = state != null && state.ParentStateId.IsValid
                    ? ToStateId(state.ParentStateId)
                    : default;
            }

            SaveAndNotify();
        }

        internal void Analyze()
        {
            Session.AnalysisRequested = true;
            analysisResult = null;
            if (catalog == null)
            {
                SetFailure(string.IsNullOrEmpty(CatalogStatus)
                    ? "A frozen descriptor catalog is required for analysis."
                    : CatalogStatus);
                return;
            }

            try
            {
                analysisResult = new CoCoStateGraphAssetCompiler().Compile(Asset, catalog);
                CommandFailure = string.Empty;
            }
            catch (Exception exception)
            {
                CommandFailure = $"StateGraph analysis failed before compilation: {exception.Message}";
            }

            SaveAndNotify();
        }

        internal void NotifyConfigChanged()
        {
            analysisResult = null;
            CommandFailure = string.Empty;
            EditorUtility.SetDirty(Asset);
            if (Session.AnalysisRequested && catalog != null)
            {
                Analyze();
                return;
            }

            Changed?.Invoke();
        }

        internal string StateDescriptorLabel(CoCoStateGraphStateRecord state)
        {
            if (state == null ||
                !CoCoStateDescriptorId.TryCreate(
                    state.StateDescriptorId.High,
                    state.StateDescriptorId.Low,
                    out CoCoStateDescriptorId descriptorId))
            {
                return "Unassigned";
            }

            return catalog != null && catalog.TryGetStateDescriptor(descriptorId, out CoCoStateDescriptor descriptor)
                ? $"{descriptor.LogicType.Name}  {descriptorId}"
                : $"Unresolved  {descriptorId}";
        }

        internal string ConditionDescriptorLabel(CoCoStateGraphConditionRecord condition)
        {
            if (condition == null ||
                !CoCoConditionDescriptorId.TryCreate(
                    condition.ConditionDescriptorId.High,
                    condition.ConditionDescriptorId.Low,
                    out CoCoConditionDescriptorId descriptorId))
            {
                return "Unassigned";
            }

            return catalog != null &&
                   catalog.TryGetConditionDescriptor(descriptorId, out CoCoConditionDescriptor descriptor)
                ? $"{descriptor.ConditionType.Name}  {descriptorId}"
                : $"Unresolved  {descriptorId}";
        }

        internal IReadOnlyList<string> BuildRequirementOverlay()
        {
            var lines = new List<string>();
            CoCoStateGraphStateRecord state = FindState(SelectedLayer, Session.SelectedStateId);
            if (state != null &&
                CoCoStateDescriptorId.TryCreate(
                    state.StateDescriptorId.High,
                    state.StateDescriptorId.Low,
                    out CoCoStateDescriptorId stateDescriptorId) &&
                catalog != null &&
                catalog.TryGetStateDescriptor(stateDescriptorId, out CoCoStateDescriptor stateDescriptor))
            {
                lines.Add($"State: {stateDescriptor.LogicType.Name}  {stateDescriptor.DescriptorId}");
                AddIds(lines, "Intent requires", stateDescriptor.IntentRequirements);
                AddIds(lines, "Operation provides", stateDescriptor.OperationProvides);
                AddIds(lines, "Context requires", stateDescriptor.ContextStateRequirements);
            }

            CoCoStateGraphTransitionRecord transition =
                FindTransition(SelectedLayer, Session.SelectedTransitionId);
            if (transition != null && catalog != null)
            {
                for (int index = 0; index < transition.Conditions.Count; index++)
                {
                    CoCoStateGraphConditionRecord condition = transition.Conditions[index];
                    if (condition != null &&
                        CoCoConditionDescriptorId.TryCreate(
                            condition.ConditionDescriptorId.High,
                            condition.ConditionDescriptorId.Low,
                            out CoCoConditionDescriptorId conditionDescriptorId) &&
                        catalog.TryGetConditionDescriptor(
                            conditionDescriptorId,
                            out CoCoConditionDescriptor conditionDescriptor))
                    {
                        lines.Add(
                            $"Condition {index + 1}: {conditionDescriptor.ConditionType.Name}  " +
                            conditionDescriptor.DescriptorId);
                        AddIds(lines, "Intent requires", conditionDescriptor.IntentRequirements);
                        AddIds(lines, "Context requires", conditionDescriptor.ContextStateRequirements);
                    }
                }
            }

            if (analysisResult?.Succeeded == true)
            {
                lines.Add("Compiled host requirements:");
                foreach (CoCoIntentRequirement requirement in
                         analysisResult.Graph.IntentRequirements.Requirements)
                {
                    lines.Add($"Intent  {requirement.ValueType.Name}  {requirement.IntentId}");
                }

                foreach (CoCoGraphOperationProvideRequirement requirement in
                         analysisResult.Graph.OperationProvides.Provides)
                {
                    lines.Add($"Operation  {requirement.SectionType.Name}  {requirement.SectionId}");
                }

                foreach (CoCoContextStateBlockRequirement block in
                         analysisResult.Graph.ContextStateRequirements.Blocks)
                {
                    lines.Add($"Context block  {block.BlockId}");
                }
            }
            else
            {
                lines.Add("Compiled host requirements unavailable until analysis succeeds.");
            }

            return lines;
        }

        internal static bool TryCreateStateConfig(
            CoCoStateDescriptor descriptor,
            out CoCoStateConfig config,
            out string failure)
        {
            return TryCreateConfig(descriptor?.AuthoringConfigType, out config, out failure);
        }

        internal static bool TryCreateConditionConfig(
            CoCoConditionDescriptor descriptor,
            out CoCoConditionConfig config,
            out string failure)
        {
            return TryCreateConfig(descriptor?.AuthoringConfigType, out config, out failure);
        }

        private void ReloadCatalog()
        {
            catalog = null;
            analysisResult = null;
            CatalogStatus = string.Empty;
            Func<CoCoGraphDescriptorCatalog> provider = CoCoStateGraphEditorCatalogProvider.Provider;
            if (provider == null)
            {
                CatalogStatus =
                    "No descriptor catalog provider is registered. Descriptor overlays and presets are unavailable.";
                return;
            }

            try
            {
                catalog = provider();
                if (catalog == null)
                {
                    CatalogStatus = "The registered descriptor catalog provider returned null.";
                    return;
                }

                CoCoDiagnostic[] diagnostics =
                    CoCoStateGraphAuthoringDependencyClosureValidator.Validate(catalog);
                if (diagnostics.Length > 0)
                {
                    catalog = null;
                    CatalogStatus = diagnostics[0].Message;
                }
            }
            catch (Exception exception)
            {
                CatalogStatus = $"Descriptor catalog provider failed: {exception.Message}";
            }
        }

        private void OnCatalogChanged()
        {
            ReloadCatalog();
            if (Session.AnalysisRequested && catalog != null)
            {
                Analyze();
                return;
            }

            Changed?.Invoke();
        }

        private void OnUndoRedo()
        {
            Session.Validate(Asset);
            if (Session.AnalysisRequested && catalog != null)
            {
                try
                {
                    analysisResult = new CoCoStateGraphAssetCompiler().Compile(Asset, catalog);
                }
                catch (Exception exception)
                {
                    CommandFailure = $"StateGraph analysis failed after Undo/Redo: {exception.Message}";
                }
            }

            SaveAndNotify();
        }

        private bool Complete(bool succeeded, string failure) =>
            succeeded ? Succeed() : SetFailure(failure);

        private bool Succeed()
        {
            CommandFailure = string.Empty;
            analysisResult = null;
            Session.Validate(Asset);
            SaveAndNotify();
            return true;
        }

        private bool SetFailure(string failure)
        {
            CommandFailure = failure ?? "The StateGraph command failed.";
            Changed?.Invoke();
            return false;
        }

        private void SaveAndNotify()
        {
            Session.Save();
            Changed?.Invoke();
        }

        private static bool TryCreateConfig<TConfig>(
            Type configType,
            out TConfig config,
            out string failure)
            where TConfig : class
        {
            config = null;
            if (configType == null ||
                configType.IsAbstract ||
                !typeof(TConfig).IsAssignableFrom(configType) ||
                configType.GetConstructor(Type.EmptyTypes) == null)
            {
                failure = "The selected descriptor requires a concrete public parameterless authoring config.";
                return false;
            }

            try
            {
                config = (TConfig)Activator.CreateInstance(configType);
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"The authoring config could not be created: {exception.Message}";
                return false;
            }
        }

        private static CoCoStateGraphStateRecord FindState(
            CoCoStateGraphLayerRecord layer,
            CoCoStateId stateId)
        {
            if (layer == null || !stateId.IsValid)
            {
                return null;
            }

            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state != null && Matches(state.StateId, stateId))
                {
                    return state;
                }
            }

            return null;
        }

        private static CoCoStateGraphStateRecord FindState(
            CoCoStateGraphLayerRecord layer,
            CoCoSerializedId128 stateId)
        {
            if (layer == null || !stateId.IsValid)
            {
                return null;
            }

            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state != null && state.StateId == stateId)
                {
                    return state;
                }
            }

            return null;
        }

        private static CoCoStateGraphTransitionRecord FindTransition(
            CoCoStateGraphLayerRecord layer,
            CoCoTransitionId transitionId)
        {
            if (layer == null || !transitionId.IsValid)
            {
                return null;
            }

            foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null && Matches(transition.TransitionId, transitionId))
                {
                    return transition;
                }
            }

            return null;
        }

        private static bool HasChildren(CoCoStateGraphLayerRecord layer, CoCoSerializedId128 stateId)
        {
            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state != null && state.ParentStateId == stateId)
                {
                    return true;
                }
            }

            return false;
        }

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoStateId ToStateId(CoCoSerializedId128 id)
        {
            CoCoStateId.TryCreate(id.High, id.Low, out CoCoStateId result);
            return result;
        }

        private static bool Matches(CoCoSerializedId128 id, CoCoLayerId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

        private static bool Matches(CoCoSerializedId128 id, CoCoStateId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

        private static bool Matches(CoCoSerializedId128 id, CoCoTransitionId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

        private static void AddIds<T>(ICollection<string> lines, string label, IReadOnlyList<T> ids)
        {
            for (int index = 0; index < ids.Count; index++)
            {
                lines.Add($"{label}  {ids[index]}");
            }
        }
    }
}
