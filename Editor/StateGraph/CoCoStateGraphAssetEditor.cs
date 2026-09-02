using System;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// 测试/宿主目录注入点（既有契约，原样保留）：Provider 变更触发 CatalogChanged，
    /// controller 订阅以重载目录。
    /// </summary>
    public static class CoCoStateGraphEditorCatalogProvider
    {
        private static Func<CoCoGraphDescriptorCatalog> provider;

        public static Func<CoCoGraphDescriptorCatalog> Provider
        {
            get => provider;
            set
            {
                if (ReferenceEquals(provider, value))
                {
                    return;
                }

                provider = value;
                CatalogChanged?.Invoke();
            }
        }

        internal static event Action CatalogChanged;
    }

    /// <summary>
    /// CoCoStateGraphAsset 的 Inspector（P03：IMGUI→UITK 迁移，方案 D5）。
    /// 真实可达面等价：摘要统计 / Event Adapter Declarations（唯一编辑面）/
    /// Open Editor / Add Layer / Analyze+Locate / Play Mode 只读；
    /// 新增：三 manifest 需求摘要卡。不提供第二裸拓扑编辑面；
    /// 不可达的层/状态操作死代码随迁移退出（审计 B1 处置）。
    /// </summary>
    [CustomEditor(typeof(CoCoStateGraphAsset))]
    internal sealed class CoCoStateGraphAssetEditor : UnityEditor.Editor
    {
        private CoCoStateGraphAssetCompileResult analysisResult;
        private string analysisFailure = string.Empty;
        private string locatedPropertyPath = string.Empty;
        private VisualElement root;
        private VisualElement manifestCard;

        private static string L(string english, string chinese) =>
            CoCoEditorLocalization.Text(english, chinese);

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            Rebuild();
        }

        public override VisualElement CreateInspectorGUI()
        {
            root = new VisualElement();
            CoCoEditorElements.ApplyTheme(root);
            Rebuild();
            return root;
        }

        private void Rebuild()
        {
            if (root == null)
            {
                return;
            }

            root.Clear();
            var asset = (CoCoStateGraphAsset)target;
            serializedObject.UpdateIfRequiredOrScript();
            bool authoringReadOnly = EditorApplication.isPlayingOrWillChangePlaymode;

            DrawSummaryCard(asset);
            DrawEventAdapterCard(authoringReadOnly);
            DrawManifestCard(asset);
            DrawEntryCard(asset, authoringReadOnly);

            if (authoringReadOnly)
            {
                root.Add(new HelpBox(
                    L(
                        "StateGraph authoring is read-only while entering or running Play Mode.",
                        "进入或运行 Play Mode 期间 StateGraph 授权为只读。"),
                    HelpBoxMessageType.Info));
            }

            DrawAnalysisCard(asset);
            root.Add(new Label(
                L(
                    "Topology is edited through the State Graph Editor so stable IDs, EditorLayout, and Undo remain atomic. Event Adapter declarations remain non-topology authoring data in this Inspector.",
                    "拓扑通过 State Graph 编辑器编辑，以保证稳定 ID、EditorLayout 与 Undo 的原子性。Event Adapter 声明仍为本 Inspector 内的非拓扑授权数据。"))
            {
                name = "state-graph-inspector-boundary-note"
            });
            root.Query<Label>("state-graph-inspector-boundary-note").ForEach(note =>
                note.AddToClassList("sg-muted"));
        }

        private void DrawSummaryCard(CoCoStateGraphAsset asset)
        {
            var card = CoCoEditorElements.CreateCard(L("Summary", "概要"));
            card.Add(new Label(L("Schema", "Schema 版本") + ": " + asset.SchemaVersion));
            card.Add(new Label(L("Graph ID", "Graph ID") + ": " +
                (asset.GraphId.IsValid ? asset.GraphId.ToString() : L("Invalid", "无效"))));

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

            var badges = new VisualElement();
            badges.style.flexDirection = FlexDirection.Row;
            badges.style.flexWrap = Wrap.Wrap;
            badges.Add(CoCoEditorElements.CreateBadge(
                L($"{asset.Layers.Count} Layer(s)", $"{asset.Layers.Count} 层"),
                CoCoEditorBadgeKind.Neutral));
            badges.Add(CoCoEditorElements.CreateBadge(
                L($"{stateCount} State(s)", $"{stateCount} 个 State"),
                CoCoEditorBadgeKind.Neutral));
            badges.Add(CoCoEditorElements.CreateBadge(
                L($"{transitionCount} Transition(s)", $"{transitionCount} 个 Transition"),
                CoCoEditorBadgeKind.Neutral));
            card.Add(badges);
            root.Add(card);
        }

        /// <summary>Event Adapter Declarations：全产品唯一编辑面（等价迁移）。</summary>
        private void DrawEventAdapterCard(bool authoringReadOnly)
        {
            var card = CoCoEditorElements.CreateCard(L("Event Adapter Declarations", "Event Adapter 声明"));
            SerializedProperty eventAdapterDeclarations =
                serializedObject.FindProperty("eventAdapterDeclarations");
            var field = new PropertyField(
                eventAdapterDeclarations,
                L("Event Adapter Declarations", "Event Adapter 声明"));
            field.name = "state-graph-event-adapters";
            field.Bind(serializedObject);
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                // 等价旧链：绑定编辑 → Apply → 分析结果失效。
                serializedObject.ApplyModifiedProperties();
                analysisResult = null;
            });
            if (authoringReadOnly)
            {
                field.SetEnabled(false);
            }

            card.Add(field);
            root.Add(card);
        }

        /// <summary>新增（维护者 D5 裁决）：三 manifest 需求摘要，只读，结构化短 ID 呈现。</summary>
        private void DrawManifestCard(CoCoStateGraphAsset asset)
        {
            manifestCard = CoCoEditorElements.CreateCard(
                L("Host Requirements (manifests)", "Host 需求（manifest）"));

            if (analysisResult?.Succeeded != true)
            {
                var hint = new Label(L(
                    "Run Analyze to compute the Intent, Operation, and Context requirements this graph expects from its Host.",
                    "运行 Analyze 计算此图对 Host 的 Intent、Operation 与 Context 需求。"));
                hint.AddToClassList("sg-muted");
                manifestCard.Add(hint);
                root.Add(manifestCard);
                return;
            }

            CoCoStateGraphRequirementPresenter.FillCard(
                manifestCard,
                new[] { CoCoStateGraphEditorController.BuildCompiledSection(analysisResult) },
                compiledAvailable: true);
            root.Add(manifestCard);
        }

        private void DrawEntryCard(CoCoStateGraphAsset asset, bool authoringReadOnly)
        {
            var card = CoCoEditorElements.CreateCard(L("Actions", "操作"));

            var open = CoCoEditorElements.CreatePrimaryButton(
                L("Open State Graph Editor", "打开 State Graph 编辑器"),
                () => CoCoStateGraphEditorWindow.Open(asset));
            card.Add(open);

            var addLayer = new Button(() =>
            {
                CoCoStateGraphAuthoringOperations.AddLayer(asset);
                serializedObject.UpdateIfRequiredOrScript();
                analysisResult = null;
                Rebuild();
            })
            {
                text = L("Add Layer", "添加 Layer")
            };
            if (authoringReadOnly)
            {
                addLayer.SetEnabled(false);
            }

            card.Add(addLayer);

            var analyze = new Button(() => Analyze(asset))
            {
                text = L("Analyze With Registered Catalog", "使用注册目录分析")
            };
            card.Add(analyze);
            root.Add(card);
        }

        private void DrawAnalysisCard(CoCoStateGraphAsset asset)
        {
            var card = CoCoEditorElements.CreateCard(L("Analysis", "分析"));

            if (!string.IsNullOrEmpty(analysisFailure))
            {
                card.Add(new HelpBox(analysisFailure, HelpBoxMessageType.Warning));
            }

            if (analysisResult == null)
            {
                card.Add(new Label(L("Not analyzed yet.", "尚未分析。")));
                root.Add(card);
                return;
            }

            card.Add(new Label(analysisResult.Succeeded
                ? L("Compilation succeeded", "编译成功")
                : L("Compilation blocked", "编译阻断") + $" · {analysisResult.Diagnostics.Count} " +
                  L("diagnostic(s)", "条诊断")));

            foreach (CoCoGraphDiagnostic graphDiagnostic in analysisResult.Diagnostics)
            {
                CoCoGraphDiagnostic captured = graphDiagnostic;
                card.Add(CoCoEditorElements.CreateDiagnosticRow(
                    captured.Diagnostic.Message,
                    CoCoStateGraphEditorWindow.SeverityToBadgeKind(captured.Diagnostic.Severity),
                    () =>
                    {
                        locatedPropertyPath = CoCoStateGraphDiagnosticNavigator.TrySelect(
                            asset,
                            captured.Location,
                            out string propertyPath)
                            ? propertyPath
                            : string.Empty;
                        Rebuild();
                    }));
            }

            if (!string.IsNullOrEmpty(locatedPropertyPath))
            {
                var located = new Label(
                    L("Located Property", "定位到的属性") + ": " + locatedPropertyPath);
                located.AddToClassList("sg-id-label");
                card.Add(located);
            }

            root.Add(card);
        }

        private void Analyze(CoCoStateGraphAsset asset)
        {
            analysisResult = null;
            analysisFailure = string.Empty;
            locatedPropertyPath = string.Empty;
            Func<CoCoGraphDescriptorCatalog> provider = CoCoStateGraphEditorCatalogProvider.Provider;
            if (provider == null)
            {
                analysisFailure = L(
                    "No descriptor catalog provider is registered. Project Editor setup must inject a frozen catalog.",
                    "未注册描述符目录 Provider。项目 Editor 设置必须注入冻结目录。");
                Rebuild();
                return;
            }

            try
            {
                CoCoGraphDescriptorCatalog catalog = provider();
                if (catalog == null)
                {
                    analysisFailure = L(
                        "The registered descriptor catalog provider returned null.",
                        "注册的描述符目录 Provider 返回了 null。");
                    Rebuild();
                    return;
                }

                CoCoDiagnostic[] closureDiagnostics =
                    CoCoStateGraphAuthoringDependencyClosureValidator.Validate(catalog);
                if (closureDiagnostics.Length > 0)
                {
                    var messages = new string[closureDiagnostics.Length];
                    for (int index = 0; index < closureDiagnostics.Length; index++)
                    {
                        messages[index] = closureDiagnostics[index].Message;
                    }

                    analysisFailure = string.Join(Environment.NewLine, messages);
                    Rebuild();
                    return;
                }

                analysisResult = new CoCoStateGraphAssetCompiler().Compile(asset, catalog);
            }
            catch (Exception exception)
            {
                analysisFailure = L(
                    $"StateGraph analysis failed before compilation: {exception.Message}",
                    $"StateGraph 分析在编译前失败：{exception.Message}");
            }

            Rebuild();
        }
    }
}
