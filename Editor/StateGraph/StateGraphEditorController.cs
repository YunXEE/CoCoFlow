using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEditor;
using UnityEngine;

// 图编辑器从这里同步选择、要件与编译结果，在资产变化后使相关视图失效。
namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>标出哪些编辑器数据应在本次资产变化后重建。</summary>
    [Flags]
    internal enum StateGraphEditorInvalidation
    {
        None = 0,
        Toolbar = 1 << 0,
        Tree = 1 << 1,
        Details = 1 << 2,
        Canvas = 1 << 3,
        Feedback = 1 << 4,
        StateDraft = 1 << 5,
        All = Toolbar | Tree | Details | Canvas | Feedback
    }

    /// <summary>把编辑命令、选择和图编译结果同步到编辑器面板。</summary>
    internal sealed partial class StateGraphEditorController : IDisposable
    {
        private static StateGraphSubtreeClipboard clipboard;

        private GraphDescriptorCatalog catalog;
        private StateGraphAssetCompileResult analysisResult;
        private bool _analysisPending;
        private bool _disposed;
        private StateGraphAsset _stateDraftAsset;
        private LayerId _stateDraftLayerId;
        private StateId _stateDraftStateId;
        private string _stateDraftConfigSnapshot;

        internal StateGraphEditorController(StateGraphAsset asset)
        {
            Asset = asset != null ? asset : throw new ArgumentNullException(nameof(asset));
            Session = StateGraphEditorSessionState.Load(asset);

            Session.DrillRootStateId = default;
            ReloadCatalog();
            if (Session.AnalysisRequested && catalog != null)
            {
                try
                {
                    analysisResult = new StateGraphAssetCompiler().Compile(Asset, catalog);
                }
                catch (Exception exception)
                {
                    CommandFailure = $"StateGraph analysis failed after Domain Reload: {exception.Message}";
                }
            }

            Undo.undoRedoPerformed += OnUndoRedo;
            StateGraphEditorCatalogProvider.CatalogChanged += OnCatalogChanged;
        }

        internal event Action<StateGraphEditorInvalidation> Changed;

        internal StateGraphAsset Asset { get; }
        internal StateGraphEditorSessionState Session { get; }
        internal GraphDescriptorCatalog Catalog => catalog;
        internal StateGraphAssetCompileResult AnalysisResult => analysisResult;
        internal string CatalogStatus { get; private set; } = string.Empty;
        internal string CommandFailure { get; private set; } = string.Empty;
        internal Func<bool> ConfirmStateDraftChanges { get; set; }
        internal bool HasStateDraftChanges { get; private set; }
        internal StateGraphAsset StateEditingAsset => _stateDraftAsset != null ? _stateDraftAsset : Asset;
        internal bool IsAnyStateSelected => SelectedLayer?.AnyState != null &&
            Matches(SelectedLayer.AnyState.StateId, Session.SelectedStateId);

        internal static bool IsAnyStateDescriptor(StateDescriptor descriptor) =>
            descriptor != null && typeof(AnyStateLogic).IsAssignableFrom(descriptor.LogicType);

        internal StateGraphStateRecord BeginStateDraft()
        {
            StateGraphStateRecord state = FindState(SelectedLayer, Session.SelectedStateId);
            if (state == null || Session.SelectedTransitionId.IsValid)
            {
                return null;
            }

            if (_stateDraftAsset == null || _stateDraftStateId != Session.SelectedStateId ||
                _stateDraftLayerId != Session.SelectedLayerId)
            {
                ClearStateDraft();
                _stateDraftAsset = UnityEngine.Object.Instantiate(Asset);
                _stateDraftAsset.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                _stateDraftLayerId = Session.SelectedLayerId;
                _stateDraftStateId = Session.SelectedStateId;
                _stateDraftConfigSnapshot = JsonUtility.ToJson(state.Config);
            }

            foreach (StateGraphLayerRecord layer in _stateDraftAsset.Layers)
            {
                if (layer != null && Matches(layer.LayerId, _stateDraftLayerId))
                {
                    return FindState(layer, _stateDraftStateId);
                }
            }

            return null;
        }

        internal void SetStateDraftName(string value)
        {
            StateGraphStateRecord state = BeginStateDraft();
            if (state == null || state.DisplayName == value) return;
            Undo.RecordObject(_stateDraftAsset, "Edit State Name Draft");
            state.DisplayName = value;
            MarkStateDraftChanged();
        }

        internal bool SetStateDraftDescriptor(StateDescriptor descriptor)
        {
            StateGraphStateRecord state = BeginStateDraft();
            if (state == null || descriptor == null) return false;
            if (IsAnyStateDescriptor(descriptor) != IsAnyStateSelected)
                return SetFailure("The Logic must match the selected State role.");
            if (state.StateDescriptorId.High == descriptor.DescriptorId.High &&
                state.StateDescriptorId.Low == descriptor.DescriptorId.Low) return true;
            if (!TryCreateStateConfig(descriptor, out StateConfig config, out string failure))
            {
                return SetFailure(failure, StateGraphEditorInvalidation.Feedback);
            }

            Undo.RecordObject(_stateDraftAsset, "Edit State Logic Draft");
            state.StateDescriptorId = new SerializedId128(descriptor.DescriptorId.High, descriptor.DescriptorId.Low);
            state.Config = config;
            _stateDraftConfigSnapshot = JsonUtility.ToJson(config);
            MarkStateDraftChanged();
            return true;
        }

        private void MarkStateDraftChanged()
        {
            HasStateDraftChanges = true;
            Changed?.Invoke(StateGraphEditorInvalidation.StateDraft);
        }

        internal bool SaveStateDraft()
        {
            if (!HasStateDraftChanges) return true;
            if (!StateGraphAuthoringOperations.CanEdit(out string failure))
                return SetFailure(failure, StateGraphEditorInvalidation.Feedback);
            StateGraphStateRecord draft = BeginStateDraft();
            StateGraphStateRecord state = FindState(SelectedLayer, Session.SelectedStateId);
            if (draft == null || state == null || string.IsNullOrWhiteSpace(draft.DisplayName))
                return SetFailure("A non-empty State name is required.", StateGraphEditorInvalidation.Feedback);
            if (!StateDescriptorId.TryCreate(draft.StateDescriptorId.High, draft.StateDescriptorId.Low,
                    out StateDescriptorId descriptorId))
                return SetFailure("Select a valid Logic before saving.", StateGraphEditorInvalidation.Feedback);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            if (!StateGraphAuthoringOperations.TryRenameState(Asset, Session.SelectedLayerId,
                    Session.SelectedStateId, draft.DisplayName, out failure) ||
                !StateGraphAuthoringOperations.TrySetStateDescriptor(Asset, Session.SelectedLayerId,
                    Session.SelectedStateId, descriptorId, draft.Config, out failure))
                return SetFailure(failure, StateGraphEditorInvalidation.Feedback);
            Undo.SetCurrentGroupName("Save State Changes");
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(Asset);
            ClearStateDraft();
            if (AssetDatabase.Contains(Asset)) AssetDatabase.SaveAssetIfDirty(Asset);
            Analyze(StateGraphEditorInvalidation.All);
            return true;
        }

        internal void DiscardStateDraft()
        {
            ClearStateDraft();
            Changed?.Invoke(StateGraphEditorInvalidation.Details | StateGraphEditorInvalidation.StateDraft);
        }

        internal string CaptureStateDraft() => HasStateDraftChanges && _stateDraftAsset != null
            ? EditorJsonUtility.ToJson(_stateDraftAsset) : string.Empty;

        internal void RestoreStateDraft(string json)
        {
            if (string.IsNullOrEmpty(json) || BeginStateDraft() == null) return;
            EditorJsonUtility.FromJsonOverwrite(json, _stateDraftAsset);
            HasStateDraftChanges = true;
        }

        private void ClearStateDraft()
        {
            HasStateDraftChanges = false;
            if (_stateDraftAsset == null) return;
            Undo.ClearUndo(_stateDraftAsset);
            UnityEngine.Object.DestroyImmediate(_stateDraftAsset);
            _stateDraftAsset = null;
            _stateDraftConfigSnapshot = null;
        }

        internal bool ResolveStateDraft() => !HasStateDraftChanges ||
            (ConfirmStateDraftChanges != null && ConfirmStateDraftChanges());

        internal StateGraphLayerRecord SelectedLayer
        {
            get
            {
                foreach (StateGraphLayerRecord layer in Asset.Layers)
                {
                    if (layer != null && Matches(layer.LayerId, Session.SelectedLayerId))
                    {
                        return layer;
                    }
                }

                return null;
            }
        }

        internal IReadOnlyList<StateGraphStateRecord> VisibleStates
        {
            get
            {
                var result = new List<StateGraphStateRecord>();
                StateGraphLayerRecord layer = SelectedLayer;
                if (layer == null)
                {
                    return result;
                }

                foreach (StateGraphStateRecord state in layer.States)
                {
                    if (state == null || state.ParentStateId.IsValid)
                    {
                        continue;
                    }

                    result.Add(state);
                }

                return result;
            }
        }

        internal IReadOnlyList<StateGraphStateRecord> SearchResults
        {
            get
            {
                var result = new List<StateGraphStateRecord>();
                StateGraphLayerRecord layer = SelectedLayer;
                string search = Session.SearchText?.Trim() ?? string.Empty;
                if (layer == null || search.Length == 0)
                {
                    return result;
                }

                foreach (StateGraphStateRecord state in layer.AllStates)
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

        internal IReadOnlyList<StateGraphTransitionRecord> VisibleTransitions
        {
            get
            {
                var result = new List<StateGraphTransitionRecord>();
                StateGraphLayerRecord layer = SelectedLayer;
                if (layer == null)
                {
                    return result;
                }

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
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
            _disposed = true;
            CancelPendingAnalysis();
            ClearStateDraft();
            Undo.undoRedoPerformed -= OnUndoRedo;
            StateGraphEditorCatalogProvider.CatalogChanged -= OnCatalogChanged;
            Session.Save();
        }

        internal void SelectLayer(LayerId layerId)
        {
            if (!ResolveStateDraft()) return;
            ClearStateDraft();
            Session.SelectedLayerId = layerId;
            Session.DrillRootStateId = default;
            Session.SelectedStateId = default;
            Session.SelectedTransitionId = default;
            SaveAndNotify();
        }

        internal void SelectState(StateId stateId)
        {
            if (stateId != Session.SelectedStateId && !ResolveStateDraft()) return;
            if (stateId != Session.SelectedStateId) ClearStateDraft();
            Session.SelectedStateId = stateId;
            Session.SelectedTransitionId = default;
            SaveAndNotify();
        }

        internal void NavigateToState(StateId stateId)
        {
            if (stateId != Session.SelectedStateId && !ResolveStateDraft()) return;
            if (stateId != Session.SelectedStateId) ClearStateDraft();
            StateGraphStateRecord state = FindState(SelectedLayer, stateId);
            if (state == null)
            {
                return;
            }

            Session.DrillRootStateId = default;
            Session.SelectedStateId = stateId;
            Session.SelectedTransitionId = default;
            RevealCanvasAncestors(stateId);
            SaveAndNotify();
        }

        internal void ToggleCanvasCollapsed(StateId stateId)
        {
            StateGraphLayerRecord layer = SelectedLayer;
            if (FindState(layer, stateId) == null) return;
            bool composite = false;
            foreach (StateGraphStateRecord child in layer.States)
                if (child != null && Matches(child.ParentStateId, stateId)) composite = true;
            if (!composite) return;
            Session.SetCanvasCollapsed(stateId, !Session.IsCanvasCollapsed(stateId));

            SaveAndNotify(StateGraphEditorInvalidation.Canvas);
        }

        private void RevealCanvasAncestors(StateId stateId)
        {
            StateGraphStateRecord state = FindState(SelectedLayer, stateId);
            var visited = new HashSet<StateId>();
            while (state != null && StateId.TryCreate(state.ParentStateId.High,
                       state.ParentStateId.Low, out StateId parentId) && visited.Add(parentId))
            {
                Session.SetCanvasCollapsed(parentId, false);
                state = FindState(SelectedLayer, parentId);
            }
        }

        internal void SelectTransition(TransitionId transitionId)
        {
            if (!ResolveStateDraft()) return;
            ClearStateDraft();
            Session.SelectedTransitionId = transitionId;
            Session.SelectedStateId = default;
            SaveAndNotify();
        }
        internal void SetSearch(string value)
        {
            Session.SearchText = value ?? string.Empty;
            SaveAndNotify(StateGraphEditorInvalidation.Tree);
        }

        internal Vector2 GetPosition(StateGraphStateRecord state, int visibleIndex)
        {
            return state != null && Asset.EditorLayout.TryGetPosition(state.StateId, out Vector2 position)
                ? position
                : new Vector2(40f + (visibleIndex % 4) * 220f, 60f + (visibleIndex / 4) * 150f);
        }

        internal bool SetPosition(StateId stateId, Vector2 position)
        {
            if (!StateGraphAuthoringOperations.TrySetStatePosition(
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
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(Asset);
            Session.SelectedLayerId = layerId;
            Session.DrillRootStateId = default;
            return Succeed();
        }

        internal bool RenameLayer(string displayName)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            return Complete(StateGraphAuthoringOperations.TryRenameLayer(
                Asset,
                Session.SelectedLayerId,
                displayName,
                out string failure), failure);
        }

        internal bool MoveSelectedLayer(int delta)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            StateGraphLayerRecord layer = SelectedLayer;
            if (layer == null)
            {
                return SetFailure("Select a Layer first.");
            }

            int currentIndex = Asset.Layers.IndexOf(layer);
            int targetIndex = Mathf.Clamp(currentIndex + delta, 0, Asset.Layers.Count - 1);
            return Complete(StateGraphAuthoringOperations.TryMoveLayer(
                Asset,
                Session.SelectedLayerId,
                targetIndex,
                out string failure), failure);
        }

        internal bool DuplicateSelectedLayer()
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            if (!StateGraphAuthoringOperations.DuplicateLayer(
                    Asset,
                    Session.SelectedLayerId,
                    out LayerId duplicatedLayerId))
            {
                return SetFailure("The selected Layer has invalid topology and could not be duplicated.");
            }

            Session.SelectedLayerId = duplicatedLayerId;
            Session.DrillRootStateId = default;
            return Succeed();
        }

        internal bool DeleteLayer()
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            bool succeeded = StateGraphAuthoringOperations.TryDeleteLayer(
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
            StateId parentStateId,
            StateDescriptor descriptor,
            string displayName,
            Vector2 position)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            if (IsAnyStateDescriptor(descriptor))
                return SetFailure("Use the Layer AnyState binding for AnyStateLogic.");
            string failure = string.Empty;
            if (descriptor == null || !TryCreateStateConfig(descriptor, out StateConfig config, out failure))
            {
                return SetFailure(descriptor == null ? "Select a State descriptor first." : failure);
            }

            if (!StateGraphAuthoringOperations.TryAddState(
                    Asset,
                    Session.SelectedLayerId,
                    parentStateId,
                    descriptor.DescriptorId,
                    config,
                    displayName,
                    position,
                    out StateId stateId,
                    out failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedStateId = stateId;
            return Succeed();
        }

        internal bool AddAnyState(StateDescriptor descriptor, Vector2 position)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            string failure = string.Empty;
            if (!IsAnyStateDescriptor(descriptor) ||
                !TryCreateStateConfig(descriptor, out StateConfig config, out failure))
            {
                return SetFailure(string.IsNullOrEmpty(failure)
                    ? "Select a AnyStateLogic descriptor first." : failure);
            }

            if (!StateGraphAuthoringOperations.TryAddAnyState(
                    Asset, Session.SelectedLayerId, descriptor, config, position,
                    out StateId stateId, out failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedStateId = stateId;
            Session.SelectedTransitionId = default;
            return Succeed();
        }

        internal bool RenameSelectedState(string displayName)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            return Complete(StateGraphAuthoringOperations.TryRenameState(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                displayName,
                out string failure), failure);
        }

        internal bool MoveSelectedState(int delta)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            StateGraphLayerRecord layer = SelectedLayer;
            StateGraphStateRecord state = FindState(layer, Session.SelectedStateId);
            if (state == null)
            {
                return SetFailure("Select a State first.");
            }

            int currentIndex = layer.States.IndexOf(state);
            int targetIndex = Mathf.Clamp(currentIndex + delta, 0, layer.States.Count - 1);
            return Complete(StateGraphAuthoringOperations.TryMoveState(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                targetIndex,
                out string failure), failure);
        }

        internal bool SetSelectedStateDescriptor(StateDescriptor descriptor)
        {
            if (descriptor != null && IsAnyStateDescriptor(descriptor) != IsAnyStateSelected)
                return SetFailure("The Logic must match the selected State role.");
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            string failure = string.Empty;
            if (descriptor == null || !TryCreateStateConfig(descriptor, out StateConfig config, out failure))
            {
                return SetFailure(descriptor == null ? "Select a State descriptor first." : failure);
            }

            return Complete(StateGraphAuthoringOperations.TrySetStateDescriptor(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                descriptor.DescriptorId,
                config,
                out failure), failure);
        }

        internal bool SetSelectedStateInitial()
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            StateGraphStateRecord state = FindState(SelectedLayer, Session.SelectedStateId);
            if (state == null)
            {
                return SetFailure("Select a State first.");
            }

            if (!state.ParentStateId.IsValid)
            {
                return Complete(StateGraphAuthoringOperations.TrySetInitialState(
                    Asset,
                    Session.SelectedLayerId,
                    Session.SelectedStateId,
                    out string failure), failure);
            }

            StateId parentStateId = ToStateId(state.ParentStateId);
            return Complete(StateGraphAuthoringOperations.TrySetInitialChildState(
                Asset,
                Session.SelectedLayerId,
                parentStateId,
                Session.SelectedStateId,
                out string childFailure), childFailure);
        }

        internal bool ReparentSelectedState(
            StateId targetParentStateId,
            StateId replacementOldInitialStateId,
            Vector2 position)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            return Complete(StateGraphAuthoringOperations.TryReparentStateSubtree(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                targetParentStateId,
                replacementOldInitialStateId,
                position,
                out string failure), failure);
        }

        internal bool DeleteSelectedState(StateId replacementInitialStateId)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            string failure;
            bool succeeded = IsAnyStateSelected
                ? StateGraphAuthoringOperations.TryDeleteAnyState(
                    Asset, Session.SelectedLayerId, out failure)
                : StateGraphAuthoringOperations.TryDeleteStateSubtree(
                Asset,
                Session.SelectedLayerId,
                Session.SelectedStateId,
                replacementInitialStateId,
                out _,
                out failure);
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
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            if (!StateGraphAuthoringOperations.TryCaptureSubtree(
                    Asset,
                    Session.SelectedLayerId,
                    Session.SelectedStateId,
                    out StateGraphSubtreeClipboard captured,
                    out string failure))
            {
                return SetFailure(failure);
            }

            clipboard?.Dispose();
            clipboard = captured;
            return Succeed();
        }

        internal bool PasteState(StateId parentStateId, Vector2 position)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            if (!StateGraphAuthoringOperations.TryPasteSubtree(
                    Asset,
                    clipboard,
                    Session.SelectedLayerId,
                    parentStateId,
                    position,
                    out StateId pastedRoot,
                    out string failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedStateId = pastedRoot;
            return Succeed();
        }

        internal bool AddTransition(
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window)
        {
            if (!ResolveStateDraft()) return false;
            ClearStateDraft();
            if (!StateGraphAuthoringOperations.TryAddTransition(
                    Asset,
                    Session.SelectedLayerId,
                    sourceStateId,
                    targetStateId,
                    priority,
                    window,
                    catalog,
                    out TransitionId transitionId,
                    out string failure))
            {
                return SetFailure(failure);
            }

            Session.SelectedTransitionId = transitionId;
            Session.SelectedStateId = default;
            return Succeed();
        }

        internal bool DeleteSelectedTransition()
        {
            if (!StateGraphAuthoringOperations.TryDeleteTransition(
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
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window)
        {
            return Complete(StateGraphAuthoringOperations.TryUpdateTransition(
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

        internal void Locate(GraphDiagnosticLocation location)
        {
            if (!ResolveStateDraft()) return;
            ClearStateDraft();
            if (location.LayerId.IsValid)
            {
                Session.SelectedLayerId = location.LayerId;
            }

            Session.SelectedStateId = location.StateId;
            Session.SelectedTransitionId = location.TransitionId;
            Session.SelectedDiagnosticLocation = location;
            Session.DrillRootStateId = default;

            RevealCanvasAncestors(location.StateId);
            if (SelectedLayer != null && location.TransitionId.IsValid)
                foreach (StateGraphTransitionRecord edge in SelectedLayer.Transitions)
                    if (edge != null && Matches(edge.TransitionId, location.TransitionId))
                    {
                        RevealCanvasAncestors(ToStateId(edge.SourceStateId));
                        RevealCanvasAncestors(ToStateId(edge.TargetStateId));
                        break;
                    }

            SaveAndNotify();
        }

        internal void Analyze() => Analyze(StateGraphEditorInvalidation.Feedback);

        private void Analyze(StateGraphEditorInvalidation invalidation)
        {
            CancelPendingAnalysis();
            if (_disposed)
            {
                return;
            }

            Session.AnalysisRequested = true;
            analysisResult = null;
            if (catalog == null)
            {
                SetFailure(string.IsNullOrEmpty(CatalogStatus)
                    ? "A frozen descriptor catalog is required for analysis."
                    : CatalogStatus,
                    invalidation);
                return;
            }

            try
            {
                analysisResult = new StateGraphAssetCompiler().Compile(Asset, catalog);
                CommandFailure = string.Empty;
            }
            catch (Exception exception)
            {
                CommandFailure = $"StateGraph analysis failed before compilation: {exception.Message}";
            }

            SaveAndNotify(invalidation);
        }

        internal void NotifyConfigChanged()
        {
            if (_disposed)
            {
                return;
            }

            if (_stateDraftAsset != null && !Session.SelectedTransitionId.IsValid)
            {

                string snapshot = JsonUtility.ToJson(BeginStateDraft()?.Config);
                if (snapshot != _stateDraftConfigSnapshot)
                {
                    _stateDraftConfigSnapshot = snapshot;
                    MarkStateDraftChanged();
                }
                return;
            }

            analysisResult = null;
            CommandFailure = string.Empty;
            EditorUtility.SetDirty(Asset);
            if (Session.AnalysisRequested && catalog != null && !_analysisPending)
            {
                _analysisPending = true;
                EditorApplication.delayCall += RunPendingAnalysis;
            }

            Changed?.Invoke(StateGraphEditorInvalidation.Feedback);
        }

        private void RunPendingAnalysis()
        {
            _analysisPending = false;
            if (!_disposed && Asset != null)
            {
                Analyze();
            }
        }

        private void CancelPendingAnalysis()
        {
            EditorApplication.delayCall -= RunPendingAnalysis;
            _analysisPending = false;
        }

        internal string StateDescriptorLabel(StateGraphStateRecord state)
        {
            if (state == null ||
                !StateDescriptorId.TryCreate(
                    state.StateDescriptorId.High,
                    state.StateDescriptorId.Low,
                    out StateDescriptorId descriptorId))
            {
                return "Unassigned";
            }

            return catalog != null && catalog.TryGetStateDescriptor(descriptorId, out StateDescriptor descriptor)
                ? $"{descriptor.LogicType.Name}  {descriptorId}"
                : $"Unresolved  {descriptorId}";
        }

        internal static bool TryCreateStateConfig(
            StateDescriptor descriptor,
            out StateConfig config,
            out string failure)
        {
            return TryCreateConfig(descriptor?.AuthoringConfigType, out config, out failure);
        }

        private void ReloadCatalog()
        {
            CancelPendingAnalysis();
            catalog = null;
            analysisResult = null;
            CatalogStatus = string.Empty;
            Func<GraphDescriptorCatalog> provider = StateGraphEditorCatalogProvider.Provider;
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
                    StateGraphAuthoringDependencyClosureValidator.Validate(catalog);
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
                Analyze(StateGraphEditorInvalidation.All);
                return;
            }

            Changed?.Invoke(StateGraphEditorInvalidation.All);
        }

        private void OnUndoRedo()
        {
            if (!HasStateDraftChanges) ClearStateDraft();
            CancelPendingAnalysis();
            analysisResult = null;
            Session.Validate(Asset);
            if (Session.AnalysisRequested && catalog != null)
            {
                try
                {
                    analysisResult = new StateGraphAssetCompiler().Compile(Asset, catalog);
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

        private bool SetFailure(
            string failure,
            StateGraphEditorInvalidation invalidation = StateGraphEditorInvalidation.All)
        {
            CommandFailure = failure ?? "The StateGraph command failed.";
            Changed?.Invoke(invalidation);
            return false;
        }

        private void SaveAndNotify(
            StateGraphEditorInvalidation invalidation = StateGraphEditorInvalidation.All)
        {
            Session.Save();
            Changed?.Invoke(invalidation);
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

        private static StateGraphStateRecord FindState(
            StateGraphLayerRecord layer,
            StateId stateId)
        {
            if (layer == null || !stateId.IsValid)
            {
                return null;
            }

            foreach (StateGraphStateRecord state in layer.AllStates)
            {
                if (state != null && Matches(state.StateId, stateId))
                {
                    return state;
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

            foreach (StateGraphStateRecord state in layer.AllStates)
            {
                if (state != null && state.StateId == stateId)
                {
                    return state;
                }
            }

            return null;
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
                if (transition != null && Matches(transition.TransitionId, transitionId))
                {
                    return transition;
                }
            }

            return null;
        }

        private static SerializedId128 Serialize(StateId id) =>
            new SerializedId128(id.High, id.Low);

        private static StateId ToStateId(SerializedId128 id)
        {
            StateId.TryCreate(id.High, id.Low, out StateId result);
            return result;
        }

        private static bool Matches(SerializedId128 id, LayerId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

        private static bool Matches(SerializedId128 id, StateId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

        private static bool Matches(SerializedId128 id, TransitionId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

    }

    /// <summary>区分要件列表中的输入、输出与图状态数据。</summary>
    internal enum RequirementEntryKind
    {
        Intent,
        Operation,
        ContextBlock
    }

    /// <summary>为详情面板保存一项编译要件的标识、来源及显示内容。</summary>
    internal readonly struct RequirementEntry
    {
        internal RequirementEntry(
            RequirementEntryKind kind,
            string typeName,
            string fullId)
        {
            Kind = kind;
            TypeName = typeName;
            FullId = fullId;
        }

        internal RequirementEntryKind Kind { get; }
        internal string TypeName { get; }
        internal string FullId { get; }
    }

    /// <summary>区分要件所属的图、图层或所选节点。</summary>
    internal enum RequirementScopeKind
    {
        SelectedState,
        Compiled
    }

    /// <summary>将同一范围的要件组成详情面板的一个分组。</summary>
    internal sealed class RequirementSection
    {
        internal RequirementSection(
            RequirementScopeKind scopeKind,
            string scopeLabel,
            string scopeFullId,
            IReadOnlyList<RequirementEntry> entries)
        {
            ScopeKind = scopeKind;
            ScopeLabel = scopeLabel;
            ScopeFullId = scopeFullId;
            Entries = entries;
        }

        internal RequirementScopeKind ScopeKind { get; }
        internal string ScopeLabel { get; }
        internal string ScopeFullId { get; }
        internal IReadOnlyList<RequirementEntry> Entries { get; }
    }

    /// <summary>把编辑命令、选择和图编译结果同步到编辑器面板。</summary>
    internal sealed partial class StateGraphEditorController
    {
                internal IReadOnlyList<RequirementSection> BuildRequirementSections()
        {
            var sections = new List<RequirementSection>();

            StateGraphStateRecord state = FindState(SelectedLayer, Session.SelectedStateId);
            if (state != null &&
                StateDescriptorId.TryCreate(
                    state.StateDescriptorId.High,
                    state.StateDescriptorId.Low,
                    out StateDescriptorId stateDescriptorId) &&
                catalog != null &&
                catalog.TryGetStateDescriptor(stateDescriptorId, out StateDescriptor stateDescriptor))
            {
                sections.Add(new RequirementSection(
                    RequirementScopeKind.SelectedState,
                    stateDescriptor.LogicType.Name,
                    stateDescriptor.DescriptorId.ToString(),
                    DescriptorEntries(stateDescriptor)));
            }

            if (analysisResult?.Succeeded == true)
            {
                sections.Add(BuildCompiledSection(analysisResult, catalog));
            }

            return sections;
        }

                internal static RequirementSection BuildCompiledSection(
            StateGraphAssetCompileResult result,
            GraphDescriptorCatalog catalog = null)
        {
            var entries = new List<RequirementEntry>();
            foreach (IntentRequirement requirement in result.Graph.IntentRequirements.Requirements)
            {
                entries.Add(new RequirementEntry(
                    RequirementEntryKind.Intent,
                    requirement.ValueType.Name,
                    requirement.IntentId.ToString()));
            }

            foreach (GraphOperationProvision provision in result.Graph.OperationProvides.Provides)
            {
                entries.Add(new RequirementEntry(
                    RequirementEntryKind.Operation,
                    provision.OperationType.Name,
                    provision.OperationId.ToString()));
            }

            foreach (ContextStateBlockRequirement block in result.Graph.ContextStateRequirements.Blocks)
            {
                entries.Add(new RequirementEntry(
                    RequirementEntryKind.ContextBlock,
                    ResolveContextBlockName(catalog, block.BlockId),
                    block.BlockId.ToString()));
            }

            return new RequirementSection(
                RequirementScopeKind.Compiled,
                null,
                null,
                entries);
        }

                private static string ResolveContextBlockName(
            GraphDescriptorCatalog catalog,
            StateBlockId blockId)
        {
            if (catalog == null)
            {
                return null;
            }

            IGraphStateSlotRegistration[] slots = catalog.GetSlots(blockId);
            if (slots == null || slots.Length == 0)
            {
                return null;
            }

            var names = new List<string>(slots.Length);
            foreach (IGraphStateSlotRegistration slot in slots)
            {
                if (slot?.ValueType != null)
                {
                    names.Add(slot.ValueType.Name);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : null;
        }

        private IReadOnlyList<RequirementEntry> DescriptorEntries(
            StateDescriptor descriptor)
        {
            var entries = new List<RequirementEntry>();
            AddIdEntries(entries, RequirementEntryKind.Intent, descriptor.IntentRequirements);
            AddIdEntries(entries, RequirementEntryKind.Operation, descriptor.OperationProvides);
            AddIdEntries(entries, RequirementEntryKind.ContextBlock, descriptor.ContextStateRequirements);
            return entries;
        }

        private void AddIdEntries<TId>(
            List<RequirementEntry> entries,
            RequirementEntryKind kind,
            IReadOnlyList<TId> ids)
        {
            for (int index = 0; ids != null && index < ids.Count; index++)
            {
                entries.Add(new RequirementEntry(
                    kind,
                    ResolveIdDisplayName(kind, ids[index]),
                    ids[index].ToString()));
            }
        }

                private string ResolveIdDisplayName<TId>(RequirementEntryKind kind, TId id)
        {
            if (catalog == null)
            {
                return null;
            }

            switch (kind)
            {
                case RequirementEntryKind.Intent:
                    return id is IntentId intentId &&
                           catalog.TryGetIntent(intentId, out IGraphIntentRegistration intent)
                        ? intent.ValueType?.Name
                        : null;
                case RequirementEntryKind.Operation:
                    return id is OperationId operationId &&
                           catalog.TryGetOperation(operationId, out IGraphOperationRegistration operation)
                        ? operation.OperationType?.Name
                        : null;
                case RequirementEntryKind.ContextBlock:
                    return id is StateBlockId blockId
                        ? ResolveContextBlockName(catalog, blockId)
                        : null;
                default:
                    return null;
            }
        }
    }
}
