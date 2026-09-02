using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// StateGraph 主编辑器窗口（P03 重做）。
    /// 视图=纯重建：拓扑写全部经 Controller→AuthoringOperations；本类不直接改序列化数据。
    /// 视觉语言复用 Editor/Common（ccflow-），画布/布局专用样式走 sg-*（方案 D3）。
    /// </summary>
    internal sealed partial class CoCoStateGraphEditorWindow : EditorWindow
    {
        private const string StyleSheetPath =
            "Packages/com.yunxee.cocoflow/Editor/StateGraph/CoCoStateGraphEditor.uss";

        [SerializeField] private CoCoStateGraphAsset asset;

        private CoCoStateGraphEditorController controller;
        private CoCoStateGraphEditorCanvas canvas;
        private VisualElement headerHost;
        private VisualElement toolbarHost;
        private ScrollView tree;
        private ScrollView details;
        private VisualElement feedbackHost;
        private SerializedObject serializedAsset;
        private CoCoStateDescriptorId addStateDescriptorId;

        // Set when a "Create New Logic Script" generation is awaiting the
        // script compilation; on the next editor update after the domain
        // reload the Add State panel preselects this logic's descriptor.
        private string pendingCreateSelectName;
        private bool awaitingCompilation;
        private CoCoConditionDescriptorId addConditionDescriptorId;
        private Vector2 contextPosition = new Vector2(80f, 80f);

        private readonly LocalizedTextRegistry texts = new LocalizedTextRegistry();

        private static string L(string english, string chinese) =>
            CoCoEditorLocalization.Text(english, chinese);

        [MenuItem("Window/CoCoFlow/State Graph Editor")]
        internal static void OpenWindow()
        {
            CoCoStateGraphAsset selected = Selection.activeObject as CoCoStateGraphAsset;
            Open(selected);
        }

        internal static void Open(CoCoStateGraphAsset target)
        {
            CoCoStateGraphEditorWindow window = GetWindow<CoCoStateGraphEditorWindow>();
            window.titleContent = new GUIContent("State Graph");
            window.minSize = new Vector2(840f, 520f);
            if (target != null)
            {
                window.asset = target;
            }

            window.Show();
            window.Rebuild();
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged; // 唯一订阅点（P02 §3.2）
            if (awaitingCompilation)
            {
                // The domain reloaded and this window reopened: the generated
                // script compiled. Rescan the standard catalog and preselect
                // the fresh descriptor in the Add State panel.
                awaitingCompilation = false;
                EditorApplication.delayCall += () =>
                {
                    CoCoStandardCatalogBootstrap.Rescan();
                    TryPreselectPendingDescriptor();
                };
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged; // 对称退订
            DisposeController();
        }

        private void OnLanguageChanged()
        {
            if (asset == null)
            {
                Rebuild();
                return;
            }

            // 只刷新静态文案，不重建结构（方案 §3.2）；
            // 反馈区内容在下次 RefreshFeedback 时按当前语言重绘。
            texts.ApplyCurrentLanguage();
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
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet == null)
            {
                Debug.LogError(
                    $"[CoCoStateGraphEditorWindow] style sheet missing at {StyleSheetPath}; panel falls back to bare controls.");
            }
            else if (!rootVisualElement.styleSheets.Contains(styleSheet))
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            headerHost = new VisualElement { name = "state-graph-header-host" };
            rootVisualElement.Add(headerHost);
            toolbarHost = new VisualElement { name = "state-graph-toolbar-host" };
            rootVisualElement.Add(toolbarHost);

            if (asset == null)
            {
                BuildEmptyState();
                RefreshToolbar();
                return;
            }

            controller = new CoCoStateGraphEditorController(asset);
            serializedAsset = new SerializedObject(asset);
            controller.Changed += OnControllerChanged;
            var body = new TwoPaneSplitView(0, 230f, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1f;
            rootVisualElement.Add(body);

            tree = new ScrollView { name = "state-graph-tree" };
            tree.style.minWidth = 180f;
            body.Add(tree);

            var workspace = new TwoPaneSplitView(1, 350f, TwoPaneSplitViewOrientation.Horizontal);
            workspace.style.flexGrow = 1f;
            body.Add(workspace);

            canvas = new CoCoStateGraphEditorCanvas(controller);
            canvas.ContextRequested += OnCanvasContextRequested;
            workspace.Add(canvas);

            details = new ScrollView { name = "state-graph-details" };
            details.style.width = 350f;
            details.style.minWidth = 290f;
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

            var card = CoCoEditorElements.CreateCard(string.Empty);
            card.name = "state-graph-header-card";

            var eyebrow = new Label(L("CoCoFlow · State Graph Editor", "CoCoFlow · StateGraph 编辑器"))
            {
                name = "state-graph-header-eyebrow"
            };
            texts.Register(eyebrow, "CoCoFlow · State Graph Editor", "CoCoFlow · StateGraph 编辑器");
            card.Add(eyebrow);

            var subtitle = new Label(L(
                "Author layered state graphs; every write goes through the authoring command boundary.",
                "编辑分层状态图；所有写入均经过授权命令边界。"))
            {
                name = "state-graph-header-subtitle"
            };
            texts.Register(
                subtitle,
                "Author layered state graphs; every write goes through the authoring command boundary.",
                "编辑分层状态图；所有写入均经过授权命令边界。");
            card.Add(subtitle);

            var meta = new VisualElement { name = "state-graph-header-meta" };
            var assetField = new ObjectField
            {
                objectType = typeof(CoCoStateGraphAsset),
                allowSceneObjects = false,
                tooltip = L("State Graph Asset", "StateGraph 资产")
            };
            assetField.name = "state-graph-header-asset-field";
            assetField.SetValueWithoutNotify(asset);
            assetField.RegisterValueChangedCallback(evt =>
            {
                asset = evt.newValue as CoCoStateGraphAsset;
                Rebuild();
            });
            meta.Add(assetField);
            meta.Add(BuildHeaderBadges());
            card.Add(meta);
            headerHost.Add(card);
        }

        private VisualElement BuildHeaderBadges()
        {
            var badges = new VisualElement { name = "state-graph-header-badges" };

            badges.Add(CoCoEditorElements.CreateBadge(
                $"Schema {asset.SchemaVersion}",
                CoCoEditorBadgeKind.Neutral));

            int stateCount = 0;
            int transitionCount = 0;
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null)
                {
                    continue;
                }

                stateCount += layer.States.Count;
                transitionCount += layer.Transitions.Count;
            }

            badges.Add(CoCoEditorElements.CreateBadge(
                L($"{asset.Layers.Count} Layer(s)", $"{asset.Layers.Count} 层") +
                " · " +
                L($"{stateCount} State(s)", $"{stateCount} 个 State") +
                " · " +
                L($"{transitionCount} Transition(s)", $"{transitionCount} 个 Transition"),
                CoCoEditorBadgeKind.Info));

            if (!string.IsNullOrEmpty(controller.CatalogStatus))
            {
                badges.Add(CoCoEditorElements.CreateBadge(
                    L("Catalog degraded", "目录降级"),
                    CoCoEditorBadgeKind.Warning));
            }
            else if (controller.Catalog != null)
            {
                badges.Add(CoCoEditorElements.CreateBadge(
                    L("Catalog ready", "目录就绪"),
                    CoCoEditorBadgeKind.Success));
            }
            else
            {
                badges.Add(CoCoEditorElements.CreateBadge(
                    L("No catalog", "无目录"),
                    CoCoEditorBadgeKind.Neutral));
            }

            CoCoStateGraphAssetCompileResult analysis = controller.AnalysisResult;
            CoCoEditorBadgeKind analysisKind = CoCoEditorBadgeKind.Neutral;
            string analysisText = L("Not analyzed", "未分析");
            if (analysis != null)
            {
                if (analysis.Succeeded && analysis.Diagnostics.Count == 0)
                {
                    analysisKind = CoCoEditorBadgeKind.Success;
                    analysisText = L("Compiled", "编译通过");
                }
                else if (analysis.Succeeded)
                {
                    analysisKind = CoCoEditorBadgeKind.Warning;
                    analysisText = L($"Compiled · {analysis.Diagnostics.Count} diagnostic(s)",
                        $"编译通过 · {analysis.Diagnostics.Count} 条诊断");
                }
                else
                {
                    analysisKind = CoCoEditorBadgeKind.Error;
                    analysisText = L($"Blocked · {analysis.Diagnostics.Count} diagnostic(s)",
                        $"编译阻断 · {analysis.Diagnostics.Count} 条诊断");
                }
            }

            badges.Add(CoCoEditorElements.CreateBadge(analysisText, analysisKind));
            return badges;
        }

        private void BuildEmptyState()
        {
            string title = L("No State Graph selected", "未选择 StateGraph");
            string message = L(
                "Select a CoCoStateGraphAsset to begin editing its Layers, States, and Transitions.",
                "选择一个 CoCoStateGraphAsset 开始编辑它的 Layer、State 与 Transition。");
            string firstStep = L(
                "Pick or drop an asset in the field below.",
                "在下方字段中选择或拖入资产。");
            string alternative = L(
                "Or create a new graph from a preset.",
                "或从预设新建一个图。");
            var empty = CoCoEditorElements.CreateEmptyState(title, message, firstStep, alternative);
            empty.name = "state-graph-empty";

            var field = new ObjectField(L("State Graph Asset", "StateGraph 资产"))
            {
                objectType = typeof(CoCoStateGraphAsset),
                allowSceneObjects = false
            };
            field.RegisterValueChangedCallback(evt =>
            {
                asset = evt.newValue as CoCoStateGraphAsset;
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

            var layerPopupSlot = new VisualElement
            {
                name = "state-graph-layer-popup-slot",
                style = { flexDirection = FlexDirection.Row }
            };
            toolbar.Add(layerPopupSlot);

            if (controller == null)
            {
                var emptyCreatePreset = new ToolbarButton(() => CoCoStateGraphPresetWizard.Open())
                {
                    text = "Create Preset"
                };
                emptyCreatePreset.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
                toolbar.Add(emptyCreatePreset);
                return;
            }

            AddLayerPopup(layerPopupSlot);

            var addLayer = new ToolbarButton(() => controller.AddLayer())
            {
                text = L("+ Layer", "+ 层")
            };
            texts.Register(addLayer, "+ Layer", "+ 层");
            addLayer.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
            toolbar.Add(addLayer);

            var up = new ToolbarButton(() => controller.DrillUp())
            {
                text = L("Up", "上一级"),
                tooltip = L("Move to the parent State canvas", "返回父 State 画布")
            };
            texts.Register(up, "Up", "上一级");
            toolbar.Add(up);

            toolbar.Add(BuildBreadcrumb());

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

            var createPreset = new ToolbarButton(() => CoCoStateGraphPresetWizard.Open())
            {
                text = "Create Preset"
            };
            createPreset.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
            toolbar.Add(createPreset);
        }

        private void AddLayerPopup(VisualElement slot)
        {
            var layerIds = new List<CoCoLayerId>();
            var layerLabels = new List<string>();
            int selectedIndex = 0;
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !TryLayerId(layer.LayerId, out CoCoLayerId layerId))
                {
                    continue;
                }

                if (layerId == controller.Session.SelectedLayerId)
                {
                    selectedIndex = layerIds.Count;
                }

                layerIds.Add(layerId);
                layerLabels.Add($"{layer.DisplayName}  [{ShortId(layerId.ToString())}]");
            }

            if (layerLabels.Count == 0)
            {
                return;
            }

            var layerPopup = new PopupField<string>(layerLabels, selectedIndex)
            {
                tooltip = L("Layer", "Layer")
            };
            layerPopup.style.width = 190f;
            layerPopup.RegisterValueChangedCallback(evt =>
            {
                int index = layerLabels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    controller.SelectLayer(layerIds[index]);
                }
            });
            slot.Add(layerPopup);
        }

        /// <summary>
        /// 可点击分段面包屑（D6）：每段执行既有 DrillUp 语义链；
        /// 不新增导航数据或公共 API。环段禁用。
        /// </summary>
        private VisualElement BuildBreadcrumb()
        {
            var breadcrumb = new VisualElement { name = "state-graph-breadcrumb" };
            breadcrumb.AddToClassList("sg-breadcrumb");

            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null)
            {
                var none = new Label(L("No Layer", "无 Layer"));
                texts.Register(none, "No Layer", "无 Layer");
                breadcrumb.Add(none);
                return breadcrumb;
            }

            // 与 controller.BreadcrumbLabel 相同的遍历：沿父链上溯 + 环检测。
            var segments = new List<(CoCoSerializedId128 ScopeId, string Label, bool Cycle)>();
            var visited = new HashSet<CoCoSerializedId128>();
            CoCoStateGraphStateRecord current = FindState(layer, controller.Session.DrillRootStateId);
            while (current != null)
            {
                if (!visited.Add(current.StateId))
                {
                    segments.Insert(0, (default, "<cycle>", true));
                    break;
                }

                segments.Insert(0, (current.StateId, current.DisplayName, false));
                current = current.ParentStateId.IsValid
                    ? FindState(layer, ToStateId(current.ParentStateId))
                    : null;
            }

            void AddSeparator()
            {
                var separator = new Label("›");
                separator.AddToClassList("sg-breadcrumb__separator");
                breadcrumb.Add(separator);
            }

            var rootButton = new ToolbarButton(() => NavigateBreadcrumbTo(default))
            {
                text = layer.DisplayName
            };
            rootButton.AddToClassList("sg-breadcrumb__segment");
            breadcrumb.Add(rootButton);

            for (int index = 0; index < segments.Count; index++)
            {
                (CoCoSerializedId128 scopeId, string label, bool cycle) = segments[index];
                AddSeparator();
                if (cycle || index == segments.Count - 1)
                {
                    var currentLabel = new Label(cycle ? "<cycle>" : label);
                    currentLabel.AddToClassList("sg-breadcrumb__current");
                    if (cycle)
                    {
                        currentLabel.AddToClassList("sg-muted");
                    }

                    breadcrumb.Add(currentLabel);
                    break;
                }

                CoCoSerializedId128 target = scopeId;
                var segment = new ToolbarButton(() => NavigateBreadcrumbTo(target))
                {
                    text = label
                };
                segment.AddToClassList("sg-breadcrumb__segment");
                breadcrumb.Add(segment);
            }

            breadcrumb.style.minWidth = 120f;
            return breadcrumb;
        }

        private void NavigateBreadcrumbTo(CoCoSerializedId128 targetScopeId)
        {
            if (controller == null)
            {
                return;
            }

            // 目标=root（default）或祖先段：重复 DrillUp（既有语义，纯 Session 导航）。
            int guard = 256;
            while (guard-- > 0 &&
                   controller.Session.DrillRootStateId.IsValid &&
                   new CoCoSerializedId128(
                       controller.Session.DrillRootStateId.High,
                       controller.Session.DrillRootStateId.Low) != targetScopeId)
            {
                controller.DrillUp();
            }
        }

        /// <summary>
        /// After a generated state script compiles, finds its descriptor in
        /// the refreshed catalog and stores it as the Add State default so
        /// the reopened panel preselects it.
        /// </summary>
        private void TryPreselectPendingDescriptor()
        {
            if (string.IsNullOrEmpty(pendingCreateSelectName))
            {
                return;
            }

            string logicName = pendingCreateSelectName;
            pendingCreateSelectName = null;

            IReadOnlyList<CoCoStateDescriptor> descriptors =
                controller != null && controller.Catalog != null
                    ? controller.Catalog.StateDescriptors
                    : Array.Empty<CoCoStateDescriptor>();
            foreach (CoCoStateDescriptor descriptor in descriptors)
            {
                if (descriptor.LogicType.Name == logicName)
                {
                    addStateDescriptorId = descriptor.DescriptorId;
                    RefreshFeedback();
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
            var heading = CoCoEditorElements.CreateHeading(L("State Tree", "State 树"));
            texts.Register(heading, "State Tree", "State 树");
            tree.Add(heading);

            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null)
            {
                var none = new Label(L("No Layer selected.", "未选择 Layer。"));
                texts.Register(none, "No Layer selected.", "未选择 Layer。");
                tree.Add(none);
                return;
            }

            IReadOnlyList<CoCoStateGraphStateRecord> searchResults = controller.SearchResults;
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

                foreach (CoCoStateGraphStateRecord result in searchResults)
                {
                    CoCoStateId stateId = ToStateId(result.StateId);
                    var row = new Button(() => controller.NavigateToState(stateId))
                    {
                        text = $"{result.DisplayName}  [{ShortId(stateId.ToString())}]"
                    };
                    row.AddToClassList("sg-tree-row");
                    tree.Add(row);
                }

                return;
            }

            AddTreeChildren(tree, layer, default, 0);
        }

        private void AddTreeChildren(
            VisualElement parent,
            CoCoStateGraphLayerRecord layer,
            CoCoSerializedId128 parentStateId,
            int depth)
        {
            if (depth > layer.States.Count)
            {
                parent.Add(new HelpBox(
                    L("State hierarchy contains a cycle.", "State 层级存在环。"),
                    HelpBoxMessageType.Error));
                return;
            }

            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state == null || state.ParentStateId != parentStateId)
                {
                    continue;
                }

                CoCoStateId stateId = ToStateId(state.StateId);
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
                    text = L("Select / Open Parent Canvas", "选择 / 打开父画布")
                };
                texts.Register(open, "Select / Open Parent Canvas", "选择 / 打开父画布");
                open.AddToClassList("sg-navigation");
                foldout.Add(open);
                AddTreeChildren(foldout, layer, state.StateId, depth + 1);
                parent.Add(foldout);
            }
        }

        private void OnControllerChanged(CoCoStateGraphEditorInvalidation invalidation)
        {
            if ((invalidation & CoCoStateGraphEditorInvalidation.Toolbar) != 0)
            {
                RefreshHeader();
                RefreshToolbar();
            }

            if ((invalidation & CoCoStateGraphEditorInvalidation.Tree) != 0)
            {
                RefreshTree();
            }

            if ((invalidation & CoCoStateGraphEditorInvalidation.Details) != 0)
            {
                RefreshDetails();
            }
            else if ((invalidation & CoCoStateGraphEditorInvalidation.Feedback) != 0)
            {
                RefreshHeader(); // 分析状态徽章随 Feedback 失效更新
                RefreshFeedback();
            }

            Repaint();
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
            if (!CoCoStateGraphAuthoringOperations.CanEdit(out _))
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
                new GUIContent(L("Paste Subtree Here", "在此粘贴子树")),
                false,
                () => TryExecuteCanvasAuthoringAction(() => controller.PasteState(
                    controller.Session.DrillRootStateId,
                    contextPosition)));
            menu.ShowAsContext();
        }

        internal bool TryAddStateAtCanvasPosition(Vector2 position)
        {
            if (controller == null || !CoCoStateGraphAuthoringOperations.CanEdit(out _))
            {
                return false;
            }

            CoCoStateDescriptor descriptor = ResolveStateDescriptor(addStateDescriptorId);
            return controller.AddState(
                controller.Session.DrillRootStateId,
                descriptor,
                "State",
                position);
        }

        private void TryExecuteCanvasAuthoringAction(Action action)
        {
            if (!TryExecuteCanvasAuthoringAction(action, out string failure))
            {
                ShowNotification(new GUIContent(failure));
            }
        }

        internal static bool TryExecuteCanvasAuthoringAction(Action action, out string failure)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!CoCoStateGraphAuthoringOperations.CanEdit(out failure))
            {
                return false;
            }

            action();
            failure = string.Empty;
            return true;
        }

        private void DeleteLayer()
        {
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null ||
                !EditorUtility.DisplayDialog(
                    L("Delete State Graph Layer", "删除 State Graph Layer"),
                    L(
                        $"Delete '{layer.DisplayName}' with {layer.States.Count} State(s) and " +
                        $"{layer.Transitions.Count} Transition(s)?",
                        $"删除「{layer.DisplayName}」及其 {layer.States.Count} 个 State 与 " +
                        $"{layer.Transitions.Count} 个 Transition？"),
                    L("Delete", "删除"),
                    L("Cancel", "取消")))
            {
                return;
            }

            controller.DeleteLayer();
        }

        private void DeleteState(StateIdChoice replacement)
        {
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            CoCoStateGraphStateRecord selected = FindState(layer, controller.Session.SelectedStateId);
            if (!CoCoStateGraphAuthoringOperations.TryGetDeleteImpact(
                    asset,
                    controller.Session.SelectedLayerId,
                    controller.Session.SelectedStateId,
                    out CoCoStateGraphDeleteImpact impact,
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
            CoCoStateGraphLayerRecord layer,
            CoCoStateGraphStateRecord selected)
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

        private CoCoStateDescriptor ResolveStateDescriptor(CoCoStateDescriptorId descriptorId)
        {
            if (controller?.Catalog != null)
            {
                foreach (CoCoStateDescriptor descriptor in controller.Catalog.StateDescriptors)
                {
                    if (descriptor.DescriptorId == descriptorId)
                    {
                        return descriptor;
                    }
                }
            }

            return null;
        }

        private CoCoConditionDescriptor ResolveConditionDescriptor(CoCoConditionDescriptor fallback)
        {
            if (controller?.Catalog != null)
            {
                foreach (CoCoConditionDescriptor descriptor in controller.Catalog.ConditionDescriptors)
                {
                    if (descriptor.DescriptorId == addConditionDescriptorId)
                    {
                        return descriptor;
                    }
                }
            }

            return fallback;
        }

        private Vector2 NextPosition()
        {
            int index = controller.VisibleStates.Count;
            return new Vector2(40f + (index % 4) * 220f, 60f + (index / 4) * 150f);
        }

        private string StateLabel(CoCoStateId stateId)
        {
            if (!stateId.IsValid)
            {
                return L("Layer root", "Layer 根");
            }

            CoCoStateGraphStateRecord state = FindState(controller.SelectedLayer, stateId);
            return state == null
                ? stateId.ToString()
                : $"{state.DisplayName}  [{ShortId(stateId.ToString())}]";
        }

        private static string TransitionLabel(
            CoCoStateGraphLayerRecord layer,
            CoCoStateGraphTransitionRecord transition)
        {
            CoCoStateGraphStateRecord source = FindState(layer, ToStateId(transition.SourceStateId));
            CoCoStateGraphStateRecord target = FindState(layer, ToStateId(transition.TargetStateId));
            return $"{source?.DisplayName ?? "?"} → {target?.DisplayName ?? "?"}  " +
                   $"Priority {transition.Priority}";
        }

        private static List<CoCoStateId> SiblingIds(
            CoCoStateGraphLayerRecord layer,
            CoCoStateGraphStateRecord state)
        {
            var result = new List<CoCoStateId>();
            foreach (CoCoStateGraphStateRecord candidate in layer.States)
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
                if (state != null && state.StateId.High == stateId.High && state.StateId.Low == stateId.Low)
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
                if (transition != null &&
                    transition.TransitionId.High == transitionId.High &&
                    transition.TransitionId.Low == transitionId.Low)
                {
                    return transition;
                }
            }

            return null;
        }

        private static bool HasChildren(CoCoStateGraphLayerRecord layer, CoCoSerializedId128 stateId)
        {
            foreach (CoCoStateGraphStateRecord candidate in layer.States)
            {
                if (candidate != null && candidate.ParentStateId == stateId)
                {
                    return true;
                }
            }

            return false;
        }

        private void DisposeController()
        {
            if (canvas != null)
            {
                canvas.ContextRequested -= OnCanvasContextRequested;
                canvas.Dispose();
                canvas = null;
            }

            if (controller != null)
            {
                controller.Changed -= OnControllerChanged;
                controller.Dispose();
                controller = null;
            }

            serializedAsset = null;
        }

        internal static CoCoStateId ToStateId(CoCoSerializedId128 id)
        {
            CoCoStateId.TryCreate(id.High, id.Low, out CoCoStateId value);
            return value;
        }

        private static CoCoStateDescriptorId ToStateDescriptorId(CoCoSerializedId128 id)
        {
            CoCoStateDescriptorId.TryCreate(id.High, id.Low, out CoCoStateDescriptorId value);
            return value;
        }

        private static bool TryLayerId(CoCoSerializedId128 id, out CoCoLayerId value) =>
            CoCoLayerId.TryCreate(id.High, id.Low, out value);

        private static bool TryTransitionId(CoCoSerializedId128 id, out CoCoTransitionId value) =>
            CoCoTransitionId.TryCreate(id.High, id.Low, out value);

        private static string StableStateId(CoCoStateGraphStateRecord state) =>
            ToStateId(state.StateId).ToString();

        private static string ShortId(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);

        private sealed class StateIdChoice
        {
            internal CoCoStateId Value;
            internal bool HasExplicitSelection;
        }

        private sealed class StateDescriptorChoice
        {
            internal CoCoStateDescriptor Value;
        }

        /// <summary>
        /// 静态文案注册表：语言切换时只刷新文本，不重建结构（方案 §3.2；
        /// P02 D14 无 deferral，切换后即时生效）。
        /// </summary>
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
                        case Button button: // ToolbarButton : Button，同一分支覆盖
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
    }

    internal static class CoCoStateGraphEditorAssetOpenHandler
    {
        [OnOpenAsset]
        private static bool OnOpenAsset(EntityId instanceId, int line)
        {
            var asset = EditorUtility.EntityIdToObject(instanceId) as CoCoStateGraphAsset;
            if (asset == null)
            {
                return false;
            }

            CoCoStateGraphEditorWindow.Open(asset);
            return true;
        }
    }
}
