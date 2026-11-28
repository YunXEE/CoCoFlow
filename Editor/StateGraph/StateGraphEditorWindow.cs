using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UIElements;

// 维护者通过此窗口打开图资产、编辑拓扑并查看编译反馈。窗口负责连接画布、详情和控制器。
namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>连接当前图资产、画布和详情，在编辑与撤销后刷新受影响的视图。</summary>
    [MovedFrom(true, sourceClassName: "CoCoStateGraphEditorWindow")]
    internal sealed partial class StateGraphEditorWindow : EditorWindow
    {
        #region Public API

        [MenuItem("Window/CoCoFlow/State Graph Editor")]
        internal static void OpenWindow()
        {
            StateGraphAsset selected = Selection.activeObject as StateGraphAsset;
            Open(selected);
        }

        internal static void Open(StateGraphAsset target)
        {
            StateGraphEditorWindow window = GetWindow<StateGraphEditorWindow>();
            window.titleContent = new GUIContent("State Graph");
            window.minSize = new Vector2(840f, 520f);
            if (target != null)
            {
                if (window.asset != target && !window.ConfirmStateDraftChanges()) return;
                window.asset = target;
            }

            window.Show();
            window.Rebuild();
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        public override void SaveChanges()
        {
            rootVisualElement.focusController?.focusedElement?.Blur();
            if (controller == null || controller.SaveStateDraft()) base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            controller?.DiscardStateDraft();
            base.DiscardChanges();
        }

        internal bool TryAddStateAtCanvasPosition(Vector2 position)
        {
            if (controller == null || !StateGraphAuthoringOperations.CanEdit(out _))
            {
                return false;
            }

            StateDescriptor descriptor = ResolveStateDescriptor(addStateDescriptorId);
            return controller.AddState(
                default,
                descriptor,
                "State",
                position);
        }

        internal bool TryAddCompositeAtCanvasPosition(Vector2 position)
        {
            if (controller == null || !StateGraphAuthoringOperations.CanEdit(out string failure))
            {
                return false;
            }

            if (!controller.ResolveStateDraft()) return false;

            StateDescriptor descriptor = ResolveStateDescriptor(addStateDescriptorId);
            if (descriptor == null)
            {
                ShowNotification(new GUIContent(
                    L("A State descriptor is required (see Add State card).",
                        "需要可用的 State 描述符（见「添加 State」卡）。")));
                return false;
            }

            if (!StateGraphEditorController.TryCreateStateConfig(
                    descriptor,
                    out StateConfig containerConfig,
                    out failure) ||
                !StateGraphEditorController.TryCreateStateConfig(
                    descriptor,
                    out StateConfig childConfig,
                    out failure))
            {
                ShowNotification(new GUIContent(failure));
                return false;
            }

            LayerId layerId = controller.Session.SelectedLayerId;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            if (!StateGraphAuthoringOperations.TryAddState(
                    asset,
                    layerId,
                    default,
                    descriptor.DescriptorId,
                    containerConfig,
                    "Composite",
                    position,
                    out StateId containerId,
                    out failure) ||
                !StateGraphAuthoringOperations.TryAddState(
                    asset,
                    layerId,
                    containerId,
                    descriptor.DescriptorId,
                    childConfig,
                    "State",
                    new Vector2(80f, 80f),
                    out _,
                    out failure))
            {
                ShowNotification(new GUIContent(failure));
                return false;
            }

            Undo.CollapseUndoOperations(undoGroup);
            controller.SelectState(containerId);
            return true;
        }

        internal static bool TryExecuteCanvasAuthoringAction(Action action, out string failure)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!StateGraphAuthoringOperations.CanEdit(out failure))
            {
                return false;
            }

            action();
            failure = string.Empty;
            return true;
        }

        internal static string DeleteLayerMessage(StateGraphLayerRecord layer) =>
            L($"Delete '{layer.DisplayName}' with {layer.States.Count} State(s) and " +
              $"{layer.Transitions.Count} Transition(s)?" + (layer.AnyState == null ? string.Empty :
                  " The AnyState Logic binding and its Config will also be deleted."),
              $"删除「{layer.DisplayName}」及其 {layer.States.Count} 个 State 与 " +
              $"{layer.Transitions.Count} 个 Transition？" + (layer.AnyState == null ? string.Empty :
                  " 同时删除 AnyState 的 Logic 绑定及其 Config。"));

        internal static StateId ToStateId(SerializedId128 id)
        {
            StateId.TryCreate(id.High, id.Low, out StateId value);
            return value;
        }

        #endregion

        #region Internal Logic

        private const string StyleSheetPath =
            "Packages/com.yunxee.cocoflow/Editor/StateGraph/StateGraphEditor.uss";

        [SerializeField] private StateGraphAsset asset;
        [SerializeField] private StateGraphAsset stateDraftSource;
        [SerializeField] private string stateDraftJson;

        private StateGraphEditorController controller;
        private StateGraphEditorCanvas canvas;
        private VisualElement headerHost;
        private VisualElement toolbarHost;
        private ScrollView tree;
        private ScrollView details;
        private SerializedObject serializedAsset;
        private StateDescriptorId addStateDescriptorId;

        private string pendingCreateSelectName;
        private bool awaitingCompilation;
        private Vector2 contextPosition = new Vector2(80f, 80f);

        private readonly LocalizedTextRegistry texts = new LocalizedTextRegistry();

        private static string L(string english, string chinese) =>
            CoCoEditorLocalization.Text(english, chinese);

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged;
            if (awaitingCompilation)
            {

                awaitingCompilation = false;
                EditorApplication.delayCall += () =>
                {
                    StandardCatalogBootstrap.Rescan();
                    TryPreselectPendingDescriptor();
                };
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged;
            DisposeController();
        }

        private void OnLanguageChanged()
        {
            if (asset == null)
            {
                Rebuild();
                return;
            }

            texts.ApplyCurrentLanguage();
            RefreshStateDraftStatus();
            RefreshFeedback();
        }

        private void Rebuild()
        {
            if (rootVisualElement == null)
            {
                return;
            }

            DisposeController();
            rootVisualElement.Clear();
            texts.Clear();

            CoCoEditorElements.ApplyTheme(rootVisualElement);
            rootVisualElement.AddToClassList("sg-editor");
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet == null)
            {
                Debug.LogError(
                    $"[StateGraphEditorWindow] style sheet missing at {StyleSheetPath}; panel falls back to bare controls.");
            }
            else if (!rootVisualElement.styleSheets.Contains(styleSheet))
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            toolbarHost = new VisualElement { name = "state-graph-toolbar-host" };
            rootVisualElement.Add(toolbarHost);

            if (asset == null)
            {
                BuildEmptyState();
                RefreshToolbar();
                return;
            }

            controller = new StateGraphEditorController(asset);
            controller.ConfirmStateDraftChanges = ConfirmStateDraftChanges;
            if (stateDraftSource == asset) controller.RestoreStateDraft(stateDraftJson);
            stateDraftJson = string.Empty;
            stateDraftSource = null;
            serializedAsset = new SerializedObject(asset);
            controller.Changed += OnControllerChanged;
            var body = new TwoPaneSplitView(0, 230f, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1f;
            rootVisualElement.Add(body);

            var leftPane = new VisualElement { name = "state-graph-left-pane" };
            body.Add(leftPane);

            headerHost = new VisualElement { name = "state-graph-header-host" };
            leftPane.Add(headerHost);

            tree = new ScrollView { name = "state-graph-tree" };
            tree.style.flexGrow = 1f;
            tree.style.minWidth = 180f;
            leftPane.Add(tree);

            var diagnostics = new ScrollView { name = "state-graph-diagnostics-panel" };
            feedbackHost = new VisualElement { name = "state-graph-feedback" };
            diagnostics.Add(feedbackHost);
            leftPane.Add(diagnostics);

            var workspace = new TwoPaneSplitView(1, 350f, TwoPaneSplitViewOrientation.Horizontal);
            workspace.style.flexGrow = 1f;
            body.Add(workspace);

            canvas = new StateGraphEditorCanvas(controller);
            canvas.ContextRequested += OnCanvasContextRequested;
            canvas.CardContextRequested += OnCardContextRequested;
            workspace.Add(canvas);

            details = new ScrollView { name = "state-graph-details" };
            details.style.width = 350f;
            details.style.minWidth = 290f;
            details.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateAllTransitionsHeight());
            workspace.Add(details);

            RefreshHeader();
            RefreshToolbar();
            RefreshTree();
            RefreshDetails();
        }

        private void RefreshHeader()
        {
            if (headerHost == null)
            {
                return;
            }

            headerHost.Clear();
            if (controller == null)
            {
                return;
            }

            var header = new VisualElement { name = "state-graph-header-card" };
            var graphLabel = new Label("Graph");
            graphLabel.AddToClassList("sg-header-label");
            header.Add(graphLabel);

            var assetField = new ObjectField
            {
                objectType = typeof(StateGraphAsset),
                allowSceneObjects = false,
                tooltip = L("State Graph Asset", "StateGraph 资产")
            };
            assetField.name = "state-graph-header-asset-field";
            assetField.tooltip += $"\n{AssetDatabase.GetAssetPath(asset)}\nSchema {asset.SchemaVersion}\n" +
                (!string.IsNullOrEmpty(controller.CatalogStatus) ? controller.CatalogStatus
                    : L("Logic catalog ready", "Logic 目录已加载"));
            assetField.SetValueWithoutNotify(asset);
            assetField.RegisterValueChangedCallback(evt =>
            {
                if (!ConfirmStateDraftChanges())
                {
                    assetField.SetValueWithoutNotify(asset);
                    return;
                }
                asset = evt.newValue as StateGraphAsset;
                Rebuild();
            });
            header.Add(assetField);

            var layerLabel = new Label("Layer");
            layerLabel.AddToClassList("sg-header-label");
            header.Add(layerLabel);
            var layerRow = new VisualElement { name = "state-graph-header-layer-row" };
            AddLayerPopup(layerRow, compact: true);
            var addLayer = new Button(() => controller.AddLayer())
            {
                name = "state-graph-header-add-layer",
                text = "+",
                tooltip = L("Append a new Layer", "追加一个新 Layer")
            };
            addLayer.SetEnabled(StateGraphAuthoringOperations.CanEdit(out _));
            layerRow.Add(addLayer);
            header.Add(layerRow);

            header.Add(BuildHeaderStatus());
            headerHost.Add(header);
        }

        private VisualElement BuildHeaderStatus()
        {
            var status = new VisualElement { name = "state-graph-header-status" };
            StateGraphLayerRecord layer = controller.SelectedLayer;
            int stateCount = layer?.States.Count ?? 0;
            int transitionCount = layer?.Transitions.Count ?? 0;
            string stateText = stateCount == 1 ? "1 State" : $"{stateCount} States";
            string transitionText = transitionCount == 1 ? "1 Transition" : $"{transitionCount} Transitions";
            var counts = new Label(layer == null ? L("No Layer selected.", "未选择 Layer。")
                : L($"{stateText} · {transitionText}",
                    $"{stateCount} 个 State · {transitionCount} 条 Transition"))
            {
                name = "state-graph-header-counts",
                tooltip = L("Counts for the current Layer", "当前 Layer 的统计")
            };
            counts.AddToClassList("sg-header-counts");
            status.Add(counts);

            GraphAssetCompileResult analysis = controller.AnalysisResult;
            CoCoEditorBadgeKind analysisKind = CoCoEditorBadgeKind.Neutral;
            string analysisText = L("Not analyzed", "未分析");
            if (analysis != null)
            {
                if (analysis.Succeeded && analysis.Diagnostics.Count == 0)
                {
                    analysisKind = CoCoEditorBadgeKind.Success;
                    analysisText = L("Analysis passed", "分析通过");
                }
                else if (analysis.Succeeded)
                {
                    analysisKind = CoCoEditorBadgeKind.Warning;
                    analysisText = L($"Analysis passed · {analysis.Diagnostics.Count} diagnostic(s)",
                        $"分析通过 · {analysis.Diagnostics.Count} 条诊断");
                }
                else
                {
                    analysisKind = CoCoEditorBadgeKind.Error;
                    analysisText = L($"Blocked · {analysis.Diagnostics.Count} diagnostic(s)",
                        $"编译阻断 · {analysis.Diagnostics.Count} 条诊断");
                }
            }

            var analysisLabel = new Label(analysisText)
            {
                name = "state-graph-header-analysis",
                tooltip = L("Analysis of the saved Graph asset", "已保存 Graph 资产的分析结果")
            };
            analysisLabel.AddToClassList("sg-header-status");
            analysisLabel.AddToClassList("sg-header-status--" + analysisKind.ToString().ToLowerInvariant());
            status.Add(analysisLabel);
            if (!string.IsNullOrEmpty(controller.CatalogStatus) || controller.Catalog == null)
            {
                var warning = new Label(L("No Logic available", "没有可用的 Logic"))
                {
                    name = "state-graph-header-catalog-warning",
                    tooltip = controller.CatalogStatus
                };
                warning.AddToClassList("sg-header-status");
                warning.AddToClassList("sg-header-status--warning");
                status.Add(warning);
            }
            return status;
        }

        private void BuildEmptyState()
        {
            string title = L("No State Graph selected", "未选择 StateGraph");
            string message = L(
                "Select a StateGraphAsset to begin editing its Layers, States, and Transitions.",
                "选择一个 StateGraphAsset，开始编辑它的 Layer、State 与 Transition。");
            string firstStep = L(
                "Pick or drop an asset in the field below.",
                "在下方字段选择或拖入资产。");
            string alternative = L(
                "Or create a new graph from a preset.",
                "或从预设新建。");
            var empty = CoCoEditorElements.CreateEmptyState(title, message, firstStep, alternative);
            empty.name = "state-graph-empty";

            var field = new ObjectField(L("State Graph Asset", "StateGraph 资产"))
            {
                objectType = typeof(StateGraphAsset),
                allowSceneObjects = false
            };
            field.RegisterValueChangedCallback(evt =>
            {
                asset = evt.newValue as StateGraphAsset;
                Rebuild();
            });
            empty.Add(field);
            rootVisualElement.Add(empty);
        }

        private void RefreshToolbar()
        {
            if (toolbarHost == null)
            {
                return;
            }

            toolbarHost.Clear();
            var toolbar = new Toolbar();
            toolbarHost.Add(toolbar);

            if (controller == null)
            {
                var emptyCreatePreset = new ToolbarButton(() => StateGraphPresetWizard.Open())
                {
                    text = "Create Preset"
                };
                emptyCreatePreset.SetEnabled(StateGraphAuthoringOperations.CanEdit(out _));
                toolbar.Add(emptyCreatePreset);
                return;
            }

            var search = new ToolbarSearchField();
            search.name = "state-graph-search";
            search.SetValueWithoutNotify(controller.Session.SearchText);
            search.RegisterValueChangedCallback(evt => controller.SetSearch(evt.newValue));
            toolbar.Add(search);

            var spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            var analyze = new ToolbarButton(() => controller.Analyze()) { text = "Analyze" };
            toolbar.Add(analyze);

            var createPreset = new ToolbarButton(() => StateGraphPresetWizard.Open())
            {
                text = "Create Preset"
            };
            createPreset.SetEnabled(StateGraphAuthoringOperations.CanEdit(out _));
            toolbar.Add(createPreset);
        }

        private void AddLayerPopup(VisualElement slot, bool compact = false)
        {
            var layerIds = new List<LayerId>();
            var layerLabels = new List<string>();
            int selectedIndex = 0;
            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !TryLayerId(layer.LayerId, out LayerId layerId))
                {
                    continue;
                }

                if (layerId == controller.Session.SelectedLayerId)
                {
                    selectedIndex = layerIds.Count;
                }

                layerIds.Add(layerId);
                layerLabels.Add(compact
                    ? layer.DisplayName
                    : $"{layer.DisplayName}  [{ShortId(layerId.ToString())}]");
            }

            if (layerLabels.Count == 0)
            {
                return;
            }

            var layerPopup = new PopupField<string>(layerLabels, selectedIndex)
            {
                name = "state-graph-header-layer-field",
                tooltip = L("Layer", "Layer") + ": " + layerLabels[selectedIndex]
            };
            if (compact)
            {
                layerPopup.style.flexGrow = 1f;
                layerPopup.style.minWidth = 0f;
                layerPopup.style.flexShrink = 1f;
            }
            else
            {
                layerPopup.style.width = 190f;
            }
            layerPopup.RegisterValueChangedCallback(evt =>
            {
                int index = layerLabels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    controller.SelectLayer(layerIds[index]);
                    if (controller.Session.SelectedLayerId != layerIds[index])
                        layerPopup.SetValueWithoutNotify(evt.previousValue);
                }
            });
            slot.Add(layerPopup);
        }

        private void TryPreselectPendingDescriptor()
        {
            if (string.IsNullOrEmpty(pendingCreateSelectName))
            {
                return;
            }

            string logicName = pendingCreateSelectName;
            pendingCreateSelectName = null;

            IReadOnlyList<StateDescriptor> descriptors =
                controller != null && controller.Catalog != null
                    ? controller.Catalog.StateDescriptors
                    : Array.Empty<StateDescriptor>();
            foreach (StateDescriptor descriptor in descriptors)
            {
                if (descriptor.LogicType.Name == logicName)
                {
                    addStateDescriptorId = descriptor.DescriptorId;
                    Rebuild();
                    return;
                }
            }
        }

        private void RefreshTree()
        {
            if (tree == null || controller == null)
            {
                return;
            }

            tree.Clear();
            var heading = CoCoEditorElements.CreateHeading(L("States", "State 列表"));
            texts.Register(heading, "States", "State 列表");
            tree.Add(heading);

            StateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null)
            {
                var none = new Label(L("No Layer selected.", "未选择 Layer。"));
                texts.Register(none, "No Layer selected.", "未选择 Layer。");
                tree.Add(none);
                return;
            }

            IReadOnlyList<StateGraphStateRecord> searchResults = controller.SearchResults;
            if (!string.IsNullOrWhiteSpace(controller.Session.SearchText))
            {
                var count = new Label(
                    $"{L("Search results", "搜索结果")} ({searchResults.Count})");
                count.style.unityFontStyleAndWeight = FontStyle.Bold;
                count.style.marginTop = 12f;
                count.style.marginBottom = 5f;
                count.style.paddingBottom = 3f;
                count.style.borderBottomWidth = 1f;
                count.style.borderBottomColor = Color.grey;
                tree.Add(count);
                if (searchResults.Count == 0)
                {
                    var noMatch = new Label(L("No matching States.", "没有匹配的 State。"));
                    noMatch.AddToClassList("sg-muted");
                    texts.Register(noMatch, "No matching States.", "没有匹配的 State。");
                    tree.Add(noMatch);
                }

                foreach (StateGraphStateRecord result in searchResults)
                {
                    StateId stateId = ToStateId(result.StateId);
                    var row = new Button(() => controller.NavigateToState(stateId))
                    {
                        text = $"{result.DisplayName}  [{ShortId(stateId.ToString())}]"
                    };
                    row.AddToClassList("sg-tree-row");
                    tree.Add(row);
                }

                return;
            }

            if (layer.AnyState != null)
            {
                var anyRow = new Button(() => controller.NavigateToState(ToStateId(layer.AnyState.StateId)))
                {
                    text = $"AnyState · {layer.AnyState.DisplayName}"
                };
                anyRow.AddToClassList("sg-tree-row");
                tree.Add(anyRow);
            }
            AddTreeChildren(tree, layer, default, 0);
        }

        private void AddTreeChildren(
            VisualElement parent,
            StateGraphLayerRecord layer,
            SerializedId128 parentStateId,
            int depth)
        {
            if (depth > layer.States.Count)
            {
                parent.Add(new HelpBox(
                    L("State hierarchy contains a cycle.", "State 层级存在环。"),
                    HelpBoxMessageType.Error));
                return;
            }

            foreach (StateGraphStateRecord state in layer.States)
            {
                if (state == null || state.ParentStateId != parentStateId)
                {
                    continue;
                }

                StateId stateId = ToStateId(state.StateId);
                bool hasChildren = HasChildren(layer, state.StateId);
                if (!hasChildren)
                {
                    var leaf = new Button(() => controller.NavigateToState(stateId))
                    {
                        text = state.DisplayName
                    };
                    leaf.style.marginLeft = depth * 8f;
                    leaf.AddToClassList("sg-tree-row");
                    parent.Add(leaf);
                    continue;
                }

                var foldout = new Foldout
                {
                    text = state.DisplayName,
                    value = !controller.Session.IsCollapsed(stateId)
                };
                foldout.style.marginLeft = depth * 8f;
                foldout.AddToClassList("ccflow-foldout");
                foldout.RegisterValueChangedCallback(evt =>
                {
                    controller.Session.SetCollapsed(stateId, !evt.newValue);
                    controller.Session.Save();
                });
                var open = new Button(() => controller.NavigateToState(stateId))
                {
                    text = L("Select", "选中")
                };
                texts.Register(open, "Select", "选中");
                open.AddToClassList("sg-navigation");
                foldout.Add(open);
                AddTreeChildren(foldout, layer, state.StateId, depth + 1);
                parent.Add(foldout);
            }
        }

        private void OnControllerChanged(StateGraphEditorInvalidation invalidation)
        {
            if ((invalidation & StateGraphEditorInvalidation.StateDraft) != 0 &&
                serializedAsset != null && serializedAsset.targetObject != null)
            {

                serializedAsset?.Update();
            }
            RefreshStateDraftStatus();
            if ((invalidation & StateGraphEditorInvalidation.Toolbar) != 0)
            {
                RefreshHeader();
                RefreshToolbar();
            }

            if ((invalidation & StateGraphEditorInvalidation.Tree) != 0)
            {
                RefreshTree();
            }

            if ((invalidation & StateGraphEditorInvalidation.Details) != 0)
            {
                RefreshDetails();
            }
            else if ((invalidation & StateGraphEditorInvalidation.Feedback) != 0)
            {
                RefreshHeader();
                RefreshFeedback();
            }

            Repaint();
        }

        private bool ConfirmStateDraftChanges()
        {
            if (controller == null || !controller.HasStateDraftChanges) return true;
            int choice = EditorUtility.DisplayDialogComplex(
                L("Unsaved State changes", "State 有未保存的修改"),
                L("Save the name, Logic and StateConfig before continuing?", "是否先保存名称、Logic 和 StateConfig？"),
                L("Save", "保存"), L("Cancel", "取消"), L("Discard", "放弃修改"));
            if (choice == 0) return controller.SaveStateDraft();
            if (choice == 2)
            {
                controller.DiscardStateDraft();
                return true;
            }

            return false;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            RefreshHeader();
            RefreshToolbar();
            RefreshDetails();
            canvas?.Refresh();
        }

        private void OnCanvasContextRequested(Vector2 position)
        {
            if (!StateGraphAuthoringOperations.CanEdit(out _))
            {
                return;
            }

            contextPosition = position;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(L("Add State Here", "在此添加 State")), false, () =>
            {
                TryExecuteCanvasAuthoringAction(() => TryAddStateAtCanvasPosition(contextPosition));
            });
            menu.AddItem(
                new GUIContent(L("Paste Subtree Here", "粘贴子树到此处")),
                false,
                () => TryExecuteCanvasAuthoringAction(() => controller.PasteState(
                    default,
                    contextPosition)));
            menu.AddSeparator(string.Empty);
            menu.AddItem(
                new GUIContent(L("Add Composite (sub-state machine)", "添加 Composite（子状态机）")),
                false,
                () => TryExecuteCanvasAuthoringAction(() =>
                    TryAddCompositeAtCanvasPosition(contextPosition)));
            menu.ShowAsContext();
        }

        private void OnCardContextRequested(SerializedId128 stateId)
        {
            if (!StateId.TryCreate(stateId.High, stateId.Low, out StateId id))
            {
                return;
            }

            StateGraphLayerRecord layer = controller.SelectedLayer;
            StateGraphStateRecord state = FindState(layer, id);
            if (state == null)
            {
                return;
            }

            controller.SelectState(id);
            if (ReferenceEquals(state, layer.AnyState))
            {
                return;
            }
            bool hasIncidentTransition = HasIncidentTransition(layer, state.StateId);
            var menu = new GenericMenu();
            if (StateGraphAuthoringOperations.CanEdit(out _) && !hasIncidentTransition)
            {
                menu.AddItem(
                    new GUIContent(L("Add Child State (makes it a sub-state machine)",
                        "添加子 State（成为子状态机）")),
                    false,
                    () => TryExecuteCanvasAuthoringAction(() => controller.AddState(
                        id,
                        ResolveStateDescriptor(addStateDescriptorId),
                        "Child State",
                        new Vector2(80f, 80f))));
            }
            else if (StateGraphAuthoringOperations.CanEdit(out _))
            {

                menu.AddDisabledItem(new GUIContent(L(
                    "Add Child State (remove incident Transitions first)",
                    "添加子 State（需先移除相关 Transition）")));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(L("Add Child State", "添加子 State")));
            }

            menu.ShowAsContext();
        }

        private void TryExecuteCanvasAuthoringAction(Action action)
        {
            if (!TryExecuteCanvasAuthoringAction(action, out string failure))
            {
                ShowNotification(new GUIContent(failure));
            }
        }

        private void DeleteLayer()
        {
            StateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null ||
                !EditorUtility.DisplayDialog(
                    L("Delete State Graph Layer", "删除 State Graph Layer"),
                    DeleteLayerMessage(layer),
                    L("Delete", "删除"),
                    L("Cancel", "取消")))
            {
                return;
            }

            controller.DeleteLayer();
        }

        private void DeleteState(StateIdChoice replacement)
        {
            StateGraphLayerRecord layer = controller.SelectedLayer;
            StateGraphStateRecord selected = FindState(layer, controller.Session.SelectedStateId);
            if (!StateGraphAuthoringOperations.TryGetDeleteImpact(
                    asset,
                    controller.Session.SelectedLayerId,
                    controller.Session.SelectedStateId,
                    out StateGraphDeleteImpact impact,
                    out string failure))
            {
                ShowNotification(new GUIContent(failure));
                return;
            }

            bool deletingInitial = selected != null &&
                                   (selected.ParentStateId.IsValid
                                       ? FindState(layer, ToStateId(selected.ParentStateId))?.InitialChildStateId ==
                                         selected.StateId
                                       : layer.InitialStateId == selected.StateId);
            bool hasSurvivingSibling = selected != null && SiblingIds(layer, selected).Count > 0;
            if (deletingInitial && hasSurvivingSibling && !replacement.HasExplicitSelection)
            {
                ShowNotification(new GUIContent(
                    L("Choose an explicit replacement before deleting the initial State.",
                        "删除初始 State 前请选择显式替换。")));
                return;
            }

            string initialWarning = deletingInitial && !hasSurvivingSibling
                ? "\n\n" + L(
                    "This is the last initial State in its scope. Initial will be cleared and the saved " +
                    "draft may remain compiler-invalid until another State is assigned.",
                    "这是该作用域最后一个初始 State。Initial 将被清空，保存的草稿可能保持编译无效，直到指定新的 State。")
                : string.Empty;

            if (EditorUtility.DisplayDialog(
                    L("Delete State Subtree", "删除 State 子树"),
                    L(
                        $"Delete {impact.StateCount} State(s) and {impact.TransitionCount} incident Transition(s)?",
                        $"删除 {impact.StateCount} 个 State 与 {impact.TransitionCount} 个关联 Transition？") +
                    initialWarning,
                    L("Delete", "删除"),
                    L("Cancel", "取消")))
            {
                controller.DeleteSelectedState(replacement.Value);
            }
        }

        private static bool RequiresInitialReplacement(
            StateGraphLayerRecord layer,
            StateGraphStateRecord selected)
        {
            if (layer == null || selected == null || SiblingIds(layer, selected).Count == 0)
            {
                return false;
            }

            return selected.ParentStateId.IsValid
                ? FindState(layer, ToStateId(selected.ParentStateId))?.InitialChildStateId ==
                  selected.StateId
                : layer.InitialStateId == selected.StateId;
        }

        private StateDescriptor ResolveStateDescriptor(StateDescriptorId descriptorId)
        {
            if (controller?.Catalog != null)
            {
                foreach (StateDescriptor descriptor in controller.Catalog.StateDescriptors)
                {
                    if (descriptor.DescriptorId == descriptorId &&
                        !StateGraphEditorController.IsAnyStateDescriptor(descriptor))
                    {
                        return descriptor;
                    }
                }
            }

            return null;
        }

        private Vector2 NextPosition()
        {
            int index = controller.VisibleStates.Count;
            return new Vector2(40f + (index % 4) * 220f, 60f + (index / 4) * 150f);
        }

        private string StateLabel(StateId stateId)
        {
            if (!stateId.IsValid)
            {
                return L("Layer root", "Layer 根");
            }

            StateGraphStateRecord state = FindState(controller.SelectedLayer, stateId);
            return state == null
                ? stateId.ToString()
                : $"{state.DisplayName}  [{ShortId(stateId.ToString())}]";
        }

        private static string TransitionLabel(
            StateGraphLayerRecord layer,
            StateGraphTransitionRecord transition)
        {
            StateGraphStateRecord source = FindState(layer, ToStateId(transition.SourceStateId));
            StateGraphStateRecord target = FindState(layer, ToStateId(transition.TargetStateId));
            return $"{source?.DisplayName ?? "?"} → {target?.DisplayName ?? "?"}  " +
                   $"Priority {transition.Priority}";
        }

        private static List<StateId> SiblingIds(
            StateGraphLayerRecord layer,
            StateGraphStateRecord state)
        {
            var result = new List<StateId>();
            foreach (StateGraphStateRecord candidate in layer.States)
            {
                if (candidate != null &&
                    candidate.StateId != state.StateId &&
                    candidate.ParentStateId == state.ParentStateId)
                {
                    result.Add(ToStateId(candidate.StateId));
                }
            }

            return result;
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
                if (state != null && state.StateId.High == stateId.High && state.StateId.Low == stateId.Low)
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
                if (transition != null &&
                    transition.TransitionId.High == transitionId.High &&
                    transition.TransitionId.Low == transitionId.Low)
                {
                    return transition;
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

        private void DisposeController()
        {

            details?.Unbind();
            details?.Clear();
            if (canvas != null)
            {
                canvas.ContextRequested -= OnCanvasContextRequested;
                canvas.CardContextRequested -= OnCardContextRequested;
                canvas.Dispose();
                canvas = null;
            }

            if (controller != null)
            {
                stateDraftJson = controller.CaptureStateDraft();
                stateDraftSource = string.IsNullOrEmpty(stateDraftJson) ? null : controller.Asset;
                controller.Changed -= OnControllerChanged;
                controller.Dispose();
                controller = null;
            }

            serializedAsset?.Dispose();
            serializedAsset = null;
        }

        private static StateDescriptorId ToStateDescriptorId(SerializedId128 id)
        {
            StateDescriptorId.TryCreate(id.High, id.Low, out StateDescriptorId value);
            return value;
        }

        private static bool TryLayerId(SerializedId128 id, out LayerId value) =>
            LayerId.TryCreate(id.High, id.Low, out value);

        private static bool TryTransitionId(SerializedId128 id, out TransitionId value) =>
            TransitionId.TryCreate(id.High, id.Low, out value);

        private static string StableStateId(StateGraphStateRecord state) =>
            ToStateId(state.StateId).ToString();

        private static string ShortId(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);

        /// <summary>将可选节点的稳定标识与下拉列表中的显示内容关联。</summary>
        private sealed class StateIdChoice
        {
            internal StateId Value;
            internal bool HasExplicitSelection;
        }

        /// <summary>将创建节点所用的描述符与编辑器选项关联。</summary>
        private sealed class StateDescriptorChoice
        {
            internal StateDescriptor Value;
        }

        /// <summary>在切换显示语言后刷新已登记的可视元素文本。</summary>
        private sealed class LocalizedTextRegistry
        {
            private readonly List<(VisualElement Element, string English, string Chinese)> entries =
                new List<(VisualElement, string, string)>();

            internal void Register(VisualElement element, string english, string chinese)
            {
                if (element == null)
                {
                    return;
                }

                entries.Add((element, english, chinese));
            }

            internal void ApplyCurrentLanguage()
            {
                bool isChinese = CoCoEditorLocalization.CurrentLanguage ==
                                 CoCoEditorLanguage.SimplifiedChinese;
                foreach ((VisualElement element, string english, string zh) in entries)
                {
                    string text = isChinese ? zh : english;
                    switch (element)
                    {
                        case Button button:
                            button.text = text;
                            break;
                        case Label label:
                            label.text = text;
                            break;
                        case PopupField<string> popup:
                            popup.label = text;
                            break;
                    }
                }
            }

            internal void Clear()
            {
                entries.Clear();
            }
        }

        #endregion
    }

    /// <summary>接收 Unity 打开图资产的请求，并定位到对应的图编辑器窗口。</summary>
    internal static class StateGraphEditorAssetOpenHandler
    {
        #region Internal Logic

        [OnOpenAsset]
        private static bool OnOpenAsset(EntityId instanceId, int line)
        {
            var asset = EditorUtility.EntityIdToObject(instanceId) as StateGraphAsset;
            if (asset == null)
            {
                return false;
            }

            StateGraphEditorWindow.Open(asset);
            return true;
        }

        #endregion
    }
}
