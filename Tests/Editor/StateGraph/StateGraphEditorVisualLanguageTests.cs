using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.Core;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// 通过窗口中的视觉元素、样式与操作反馈复核当前图编辑器提供的创作入口。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>通过窗口中的视觉元素、样式与操作反馈复核当前图编辑器提供的创作入口。</summary>
    public sealed class StateGraphEditorVisualLanguageTests
    {

        #region Public API

        /// <summary>将编辑器语言设为该组文字与样式期望使用的英文。</summary>
        [SetUp]
        public void SetUp()
        {

            CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.English);
        }

        /// <summary>清理目录和测试窗口并恢复约定语言，使后续视觉用例可以独立运行。</summary>
        [TearDown]
        public void TearDown()
        {
            StateGraphEditorCatalogProvider.Provider = null;
            CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.English);
            foreach (EditorWindow window in windows)
            {
                if (window != null)
                {
                    window.rootVisualElement.RemoveFromHierarchy();
                    UnityEngine.Object.DestroyImmediate(window);
                }
            }

            windows.Clear();
            foreach (StateGraphEditorCanvas canvas in canvases)
            {
                canvas?.Dispose();
            }

            canvases.Clear();
            foreach (StateGraphEditorController controller in controllers)
            {
                controller?.Dispose();
            }

            controllers.Clear();
            Undo.ClearAll();
            foreach (StateGraphAsset asset in assets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            assets.Clear();
        }

        /// <summary>打开窗口并读取图层头部，核对主题类和当前层的节点、边数量。</summary>
        [Test]
        public void WindowAppliesThemeAndHeaderShowsCurrentLayerCounts()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId gameplay = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId first = AddState(asset, gameplay, "Idle", new Vector2(40f, 60f));
            StateId second = AddState(asset, gameplay, "Move", new Vector2(300f, 60f));
            StateGraphAuthoringOperations.AddTransition(asset, gameplay, first, second, 0);
            LayerId other = StateGraphAuthoringOperations.AddLayer(asset, "Other");
            AddState(asset, other, "Other State", new Vector2(40f, 60f));
            StateGraphEditorSessionState session = StateGraphEditorSessionState.Load(asset);
            session.SelectedLayerId = gameplay;
            session.Save();
            StateGraphEditorWindow window = CreateStateGraphWindow(asset);
            Assert.IsTrue(window.rootVisualElement.ClassListContains("ccflow-root"));

            StyleSheet shared = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yunxee.cocoflow/Editor/Core/CoCoEditorCommon.uss");
            StyleSheet own = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yunxee.cocoflow/Editor/StateGraph/StateGraphEditor.uss");
            Assert.NotNull(shared);
            Assert.NotNull(own);
            Assert.IsTrue(window.rootVisualElement.styleSheets.Contains(shared),
                "shared ccflow sheet must be attached");
            Assert.IsTrue(window.rootVisualElement.styleSheets.Contains(own),
                "sg sheet must be attached");
            VisualElement root = window.rootVisualElement;
            VisualElement header = root.Q<VisualElement>("state-graph-header-card");
            Assert.IsFalse(header.ClassListContains("ccflow-card"));
            Assert.IsNull(header.Q(className: "ccflow-badge"));
            Assert.AreEqual("+", root.Q<Button>("state-graph-header-add-layer").text);
            Assert.AreEqual("2 States · 1 Transition", root.Q<Label>("state-graph-header-counts").text);
            var controller = (StateGraphEditorController)typeof(StateGraphEditorWindow)
                .GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            controller.SelectLayer(other);
            Assert.AreEqual("1 State · 0 Transitions", root.Q<Label>("state-graph-header-counts").text);
            Assert.That(root.Q<ObjectField>("state-graph-header-asset-field").tooltip,
                Does.Contain($"Schema {asset.SchemaVersion}"));
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphEditorInteractionTests.ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
            StateGraphEditorCatalogProvider.Provider = () => catalog;
            Assert.IsNull(root.Q<Label>("state-graph-header-catalog-warning"));
            StateGraphEditorCatalogProvider.Provider = null;
            Assert.AreEqual("No Logic available", root.Q<Label>("state-graph-header-catalog-warning").text);
        }

        /// <summary>遍历诊断严重级别，核对每项的徽标样式。</summary>
        [Test]
        public void SeverityToBadgeKindMapsAllSeverities()
        {
            Assert.AreEqual(
                CoCoEditorBadgeKind.Info,
                StateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.Information));
            Assert.AreEqual(
                CoCoEditorBadgeKind.Warning,
                StateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.Warning));
            Assert.AreEqual(
                CoCoEditorBadgeKind.Error,
                StateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.Error));
            Assert.AreEqual(
                CoCoEditorBadgeKind.Neutral,
                StateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.None));
        }

        /// <summary>打开无内容资产，检查窗口给出空状态和创建预设入口。</summary>
        [Test]
        public void EmptyAssetWindowShowsEmptyStateWithPresetEntry()
        {
            StateGraphEditorWindow window = CreateStateGraphWindow(null);
            VisualElement empty = window.rootVisualElement.Q(className: "ccflow-empty");
            Assert.NotNull(empty, "ccflow empty state must be present without an asset");
            Assert.NotNull(window.rootVisualElement.Q<ObjectField>());
        }

        /// <summary>打开无图层的窗口，核对空状态能够引导创建图层。</summary>
        [Test]
        public void NoLayerWindowShowsEmptyStateInsteadOfRawFields()
        {
            StateGraphAsset asset = CreateAsset();
            StateGraphEditorWindow window = CreateStateGraphWindow(asset);
            Assert.NotNull(window.rootVisualElement.Q(className: "ccflow-empty"));
            Assert.NotNull(window.rootVisualElement.Q<VisualElement>("state-graph-feedback"));
        }

        /// <summary>切换显示语言，核对已挂载的静态界面文字随设置更新。</summary>
        [Test]
        public void BilingualStaticTextFollowsLanguagePreference()
        {
            try
            {
                StateGraphEditorWindow english = CreateStateGraphWindow(null);
                Label englishTitle = FindLabelWithText(
                    english.rootVisualElement,
                    "No State Graph selected");
                Assert.NotNull(englishTitle, "English empty-state title expected");

                CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.SimplifiedChinese);
                Label chineseTitle = FindLabelWithText(
                    english.rootVisualElement,
                    "未选择 StateGraph");
                Assert.NotNull(chineseTitle, "Chinese empty-state title expected after language change");
            }
            finally
            {
                CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.English);
            }
        }

        /// <summary>查询详情中的树、转换和诊断反馈元素，确认现有创作入口可定位。</summary>
        [Test]
        public void DetailsExposeStableTreeTransitionAndFeedbackElements()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphEditorInteractionTests.ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
            StateGraphEditorCatalogProvider.Provider = () => catalog;
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId first = AddState(asset, layerId, "First", new Vector2(40f, 60f));
            StateId second = AddState(asset, layerId, "Second", new Vector2(300f, 60f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, first, second, 0, TransitionWindow.Always, null,
                out TransitionId transitionId, out string failure), failure);
            SelectTransition(asset, layerId, transitionId);

            StateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = HostWindow(window);
            Assert.NotNull(root.Q<VisualElement>("state-graph-tree"));
            Assert.NotNull(root.Q<VisualElement>("state-graph-details"));
            var details = root.Q<ScrollView>("state-graph-details");
            VisualElement selected = root.Q<VisualElement>("state-graph-selected-details");
            Assert.AreSame(selected, details[0]);
            Assert.IsFalse(details.Contains(root.Q<VisualElement>("state-graph-feedback")));
            Assert.IsTrue(root.Q<ScrollView>("state-graph-diagnostics-panel").Contains(
                root.Q<VisualElement>("state-graph-feedback")));
            Assert.IsNull(root.Q<VisualElement>("state-graph-layer-management"));
            Assert.IsNull(root.Q<VisualElement>("state-graph-add-state"));
            Assert.IsNull(root.Q<VisualElement>("state-graph-add-logic"));
            Assert.NotNull(root.Q<VisualElement>("state-graph-transition-list"));
            Assert.IsNull(root.Q<Foldout>("state-graph-transition-list"));
            Assert.IsTrue(root.Q<VisualElement>("state-graph-feedback").Contains(
                root.Query<Label>().ToList().First(label => label.text == "Requirements / Host Suggestions")));
            var controller = (StateGraphEditorController)typeof(StateGraphEditorWindow)
                .GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            SerializedId128[] stateOrder = asset.Layers[0].States.Select(state => state.StateId).ToArray();
            controller.SelectState(first);
            selected = root.Q<VisualElement>("state-graph-selected-details");
            Assert.IsTrue(selected.Contains(root.Q<VisualElement>("state-graph-state-config")));
            Assert.NotNull(root.Q<VisualElement>("state-graph-transitions"));
            Assert.IsNull(root.Q<Foldout>("state-graph-transitions"));
            Assert.IsNull(root.Q<VisualElement>("state-graph-layer-management"));
            controller.SelectState(default);
            var management = root.Q<VisualElement>("state-graph-layer-management");
            Assert.AreSame(management, details[0]);
            Assert.AreEqual("Layer", management[0].Q<Label>("ccflow-card-title").text);
            VisualElement addState = root.Q<VisualElement>("state-graph-add-state");
            VisualElement addLogic = root.Q<VisualElement>("state-graph-add-logic");
            Assert.AreSame(addState, management[1]);
            Assert.AreSame(addLogic, management[2]);
            Assert.AreSame(root.Q<VisualElement>("state-graph-transitions"), details[1]);
            Assert.IsNull(root.Q<Foldout>("state-graph-layer-management"));
            Assert.IsTrue(addState.Contains(root.Q<PopupField<string>>("add-state-descriptor")));
            Assert.IsFalse(addState.Contains(root.Q<TextField>("state-graph-new-logic-name")));
            Assert.IsTrue(addLogic.Contains(root.Q<TextField>("state-graph-new-logic-name")));
            Assert.IsTrue(addLogic.Contains(root.Q<Button>("state-graph-create-logic")));
            Assert.IsFalse(addLogic.Contains(root.Q<PopupField<string>>("add-state-descriptor")));
            Assert.AreEqual(layerId, controller.Session.SelectedLayerId);
            CollectionAssert.AreEqual(stateOrder, asset.Layers[0].States.Select(state => state.StateId).ToArray());
            Assert.AreEqual(transitionId.High, asset.Layers[0].Transitions[0].TransitionId.High);
            Assert.AreEqual(transitionId.Low, asset.Layers[0].Transitions[0].TransitionId.Low);

        }

        /// <summary>打开图资产 Inspector，检查 UI Toolkit 展示图摘要、适配声明及创作入口。</summary>
        [Test]
        public void AssetInspectorUiToolkitShowsSummaryAdaptersAndEntries()
        {
            StateGraphAsset asset = CreateAsset();
            StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");

            var editor = (StateGraphAssetEditor)UnityEditor.Editor.CreateEditor(asset);
            Assert.NotNull(editor);
            VisualElement root = editor.CreateInspectorGUI();
            Assert.NotNull(root);

            Assert.NotNull(root.Q<VisualElement>(className: "ccflow-card"));
            Assert.NotNull(root.Q<VisualElement>("state-graph-event-adapters"),
                "Event Adapter Declarations must remain editable in the Inspector");
            Assert.NotNull(FindButtonWithText(root, "Open State Graph Editor"));
            Assert.NotNull(FindButtonWithText(root, "Add Layer"));
            Assert.NotNull(FindButtonWithText(root, "Analyze With Registered Catalog"));

            UnityEngine.Object.DestroyImmediate(editor);
        }

        /// <summary>在根画布创建多个复合节点，检查各节点配置对象独立。</summary>
        [Test]
        public void FlatCanvasCreationUsesRootScopeAndIndependentCompositeConfigs()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            FieldInfo authorRoots = typeof(GraphDescriptorCatalog).GetField(
                "_authorAssemblyRootNames",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(authorRoots);
            authorRoots.SetValue(catalog, Array.Empty<string>());
            StateGraphEditorCatalogProvider.Provider = () => catalog;
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId legacyRoot = AddState(
                asset, layerId, "Legacy Root", new Vector2(40f, 60f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddState(
                asset,
                layerId,
                legacyRoot,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(),
                "Legacy Child",
                new Vector2(80f, 80f),
                out _,
                out string failure), failure);

            StateGraphEditorSessionState legacySession =
                StateGraphEditorSessionState.Load(asset);
            legacySession.SelectedLayerId = layerId;
            legacySession.DrillRootStateId = legacyRoot;
            legacySession.Save();

            StateGraphEditorWindow window = CreateStateGraphWindow(asset);
            var windowController = (StateGraphEditorController)typeof(StateGraphEditorWindow)
                .GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window);
            Assert.NotNull(windowController);
            var statePosition = new Vector2(640f, 320f);
            Assert.IsTrue(
                window.TryAddStateAtCanvasPosition(statePosition),
                $"Catalog: {windowController.CatalogStatus}; command: {windowController.CommandFailure}");
            StateGraphStateRecord added = asset.Layers[0].States.Last();
            Assert.IsFalse(added.ParentStateId.IsValid,
                "canvas creation must ignore a legacy nested drill scope");
            Assert.IsTrue(asset.EditorLayout.TryGetPosition(added.StateId, out Vector2 actualPosition));
            Assert.AreEqual(statePosition, actualPosition);

            Assert.IsTrue(
                window.TryAddCompositeAtCanvasPosition(new Vector2(300f, 200f)),
                $"Catalog: {windowController.CatalogStatus}; command: {windowController.CommandFailure}");
            StateGraphStateRecord composite = asset.Layers[0].States.Single(state =>
                state.DisplayName == "Composite");
            StateGraphStateRecord child = asset.Layers[0].States.Single(state =>
                state.ParentStateId == composite.StateId);
            Assert.IsFalse(composite.ParentStateId.IsValid,
                "a canvas-created Composite must be rooted at the Layer");
            Assert.AreNotSame(composite.Config, child.Config,
                "Composite and first child must own independent managed-reference configs");

            var compositeConfig = (TestStateAuthoringConfig)composite.Config;
            var childConfig = (TestStateAuthoringConfig)child.Config;
            compositeConfig.Value = 41;
            Assert.AreEqual(0, childConfig.Value,
                "editing the Composite config must not mutate its first child");
        }

        /// <summary>在边上及边外各派发一次点击，核对仅命中时改变转换选择。</summary>
        [UnityTest]
        public IEnumerator EdgeClickSelectsTransitionOnHitAndIgnoresMiss()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId first = AddState(asset, layerId, "First", new Vector2(40f, 60f));
            StateId second = AddState(asset, layerId, "Second", new Vector2(300f, 60f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, first, second, 0, TransitionWindow.Always, null,
                out TransitionId transitionId, out string failure), failure);

            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(canvas.EdgeHitCount, 1, "edge geometry must exist after repaint");
            Vector2 hitPoint = new Vector2(264f, 108f);
            Assert.IsTrue(canvas.TryHitEdge(hitPoint, 6f, out SerializedId128 hitId));
            Assert.AreEqual(
                new SerializedId128(transitionId.High, transitionId.Low),
                hitId);

            Assert.IsFalse(canvas.TryHitEdge(new Vector2(1000f, 1000f), 6f, out _));
            Assert.IsFalse(controller.Session.SelectedTransitionId.IsValid);

            SendPointerDown(canvas, 0, canvas.worldBound.position + hitPoint, hitPoint);
            Assert.AreEqual(
                transitionId,
                controller.Session.SelectedTransitionId);
        }

        /// <summary>呈现方向相反的两条边，分别命中两条平行线并核对所选方向。</summary>
        [UnityTest]
        public IEnumerator BidirectionalEdgesRenderAsParallelHittableLines()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId first = AddState(asset, layerId, "First", new Vector2(40f, 60f));
            StateId second = AddState(asset, layerId, "Second", new Vector2(300f, 60f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, first, second, 0, TransitionWindow.Always, null,
                out TransitionId forward, out string failure), failure);
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, second, first, 0, TransitionWindow.Always, null,
                out TransitionId backward, out failure), failure);

            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(2, canvas.EdgeHitCount, "bidirectional pair must render two parallel edges");

            Vector2 midpoint = new Vector2(264f, 108f);
            Vector2 normal = new Vector2(0f, 1f);
            Assert.IsTrue(canvas.TryHitEdge(midpoint + normal * 8f, 3f, out SerializedId128 upper));
            Assert.IsTrue(canvas.TryHitEdge(midpoint - normal * 8f, 3f, out SerializedId128 lower));
            Assert.AreNotEqual(upper, lower, "parallel offsets must map to distinct transitions");
            bool coversForward = Matches(upper, forward) || Matches(lower, forward);
            bool coversBackward = Matches(upper, backward) || Matches(lower, backward);
            Assert.IsTrue(coversForward && coversBackward, "both directions must be hittable");
        }

        /// <summary>折叠子树后检查各箭头仍可见且两个方向均能被命中。</summary>
        [UnityTest]
        public IEnumerator FoldedEdgesKeepAllTrianglesAndReverseDirectionsHittable()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layer = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId parent = AddState(asset, layer, "OnFoot", new Vector2(40, 60));
            StateId child = StateGraphAuthoringOperations.AddState(asset, layer, parent,
                StateGraphTestFactory.StateDescriptorId, new TestStateAuthoringConfig(), "Idle");
            StateId target = AddState(asset, layer, "Death", new Vector2(500, 75));
            for (int priority = 0; priority < 3; priority++)
                StateGraphAuthoringOperations.AddTransition(asset, layer, child, target, priority);
            TransitionId reverse = StateGraphAuthoringOperations.AddTransition(asset, layer, target, child, 0);
            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            controller.ToggleCanvasCollapsed(parent);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++) yield return null;
            Assert.AreEqual(6, canvas.EdgeHitCount, "Two lines plus 3+1 triangle hit regions.");
            FieldInfo rectangleField = typeof(StateGraphEditorCanvas)
                .GetField("stateRects", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(rectangleField);
            var rectangles = (IDictionary)rectangleField.GetValue(canvas);
            Rect sourceRect = (Rect)rectangles[new SerializedId128(parent.High, parent.Low)];
            Rect targetRect = (Rect)rectangles[new SerializedId128(target.High, target.Low)];
            Assert.AreEqual(sourceRect.center.y, targetRect.center.y);
            Vector2 middle = (sourceRect.center + targetRect.center) * 0.5f;
            foreach (float x in new[] { -28f, 0f, 28f })
            {
                Assert.IsTrue(canvas.TryHitEdge(middle + new Vector2(x, 8), 3, out var hit));
                Assert.AreEqual(asset.Layers[0].Transitions[0].TransitionId, hit);
            }
            Assert.IsTrue(canvas.TryHitEdge(middle - new Vector2(0, 8), 3, out var back));
            Assert.IsTrue(Matches(back, reverse));
            controller.SelectState(target);
            Assert.IsTrue(controller.SetPosition(target, new Vector2(240, 75)));
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++) yield return null;
            Assert.AreEqual(6, canvas.EdgeHitCount, "A short line keeps every triangle, in adjacent rows.");
            for (int row = 0; row < 3; row++)
            {
                Assert.IsTrue(canvas.TryHitEdge(new Vector2(234, 131 + row * 24), 3, out var hit));
                Assert.AreEqual(asset.Layers[0].Transitions[0].TransitionId, hit);
            }
            for (int row = 1; row < 3; row++)
            {
                foreach (float zoom in new[] { 1f, 2f })
                {
                    Vector2 center = new Vector2(234, 131 + row * 24);
                    float threshold = 6f / zoom;
                    foreach (Vector2 offset in new[] { new Vector2(-5, 4), new Vector2(-8, -6), new Vector2(8, 0) })
                    {
                        Assert.IsTrue(canvas.TryHitEdge(center + offset, threshold, out var hit),
                            $"Visible triangle interior must be hittable at zoom {zoom}: {offset}");
                        Assert.AreEqual(asset.Layers[0].Transitions[0].TransitionId, hit);
                    }
                    Vector2 edgeMidpoint = center + new Vector2(0, 3.5f);
                    Vector2 outwardNormal = new Vector2(7, 18).normalized;
                    Assert.IsTrue(canvas.TryHitEdge(edgeMidpoint + outwardNormal * (threshold - 0.5f),
                        threshold, out var nearEdge), "Screen-space tolerance must extend from the triangle edge.");
                    Assert.AreEqual(asset.Layers[0].Transitions[0].TransitionId, nearEdge);
                    Assert.IsFalse(canvas.TryHitEdge(edgeMidpoint + outwardNormal * (threshold + 0.5f),
                        threshold, out _), "Points outside the triangle tolerance must still miss.");
                }
            }
            controller.SelectState(target);
            Assert.IsTrue(canvas.Q<VisualElement>("state-card").ClassListContains("state-card--transition-target"));
            controller.ToggleCanvasCollapsed(parent);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++) yield return null;
            Assert.AreEqual(4, canvas.EdgeHitCount, "Unfolded edges retain individual geometry.");
        }

        /// <summary>向自环线段派发点击，检查选中的转换回到源节点。</summary>
        [UnityTest]
        public IEnumerator SelfLoopEdgeIsHittable()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId state = AddState(asset, layerId, "Idle", new Vector2(120f, 120f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, state, state, 0, TransitionWindow.Always, null,
                out TransitionId selfLoop, out string failure), failure);

            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(canvas.EdgeHitCount, 1);

            Vector2 loopCenter = new Vector2(120f + 94f, 120f - 27f);
            Assert.IsTrue(canvas.TryHitEdge(loopCenter, 12f, out SerializedId128 hitId));
            Assert.AreEqual(new SerializedId128(selfLoop.High, selfLoop.Low), hitId);
        }

        /// <summary>选择叶节点后核对祖先和直接出边目标的高亮。</summary>
        [UnityTest]
        public IEnumerator StateSelectionLightsAncestryAndDirectOutgoingTargets()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphEditorInteractionTests.ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
            StateGraphEditorCatalogProvider.Provider = () => catalog;
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId rootLeaf = AddState(asset, layerId, "RootLeaf", new Vector2(40f, 60f));
            StateId composite = AddState(asset, layerId, "Parent", new Vector2(300f, 60f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddState(
                asset, layerId, composite,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Child",
                new Vector2(80f, 80f), out StateId child, out string failure), failure);
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, rootLeaf, child, 0, TransitionWindow.Always, null,
                out TransitionId transitionId, out failure), failure);
            StateId indirect = AddState(asset, layerId, "Indirect", new Vector2(600f, 60f));
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, child, rootLeaf, 0, TransitionWindow.Always, null,
                out _, out failure), failure);
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, child, child, 1, TransitionWindow.Always, null,
                out _, out failure), failure);
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, rootLeaf, indirect, 1, TransitionWindow.Always, null,
                out _, out failure), failure);

            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            canvas.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yunxee.cocoflow/Editor/StateGraph/StateGraphEditor.uss"));
            Host(canvas);
            yield return null;

            controller.SelectState(child);
            yield return null;
            VisualElement childCard = CardWithTitle(canvas, "Child");
            VisualElement compositeCard = CardWithTitle(canvas, "Parent");
            VisualElement rootLeafCard = CardWithTitle(canvas, "RootLeaf");
            Assert.NotNull(childCard);
            Assert.NotNull(compositeCard);
            Assert.NotNull(rootLeafCard);
            Assert.IsTrue(childCard.ClassListContains("state-card--ancestry"), "self must be lit");
            Assert.IsTrue(compositeCard.ClassListContains("state-card--ancestry"), "ancestor must be lit");
            Assert.IsFalse(rootLeafCard.ClassListContains("state-card--ancestry"), "unrelated must stay unlit");
            Assert.IsTrue(canvas.ChainGenealogyChildren.Contains(
                new SerializedId128(child.High, child.Low)),
                "the child<-composite genealogy segment must be lit");
            Assert.IsFalse(canvas.ChainGenealogyChildren.Contains(
                new SerializedId128(composite.High, composite.Low)),
                "root composite has no genealogy segment");

            Assert.IsTrue(rootLeafCard.ClassListContains("state-card--transition-target"));
            Assert.IsTrue(childCard.ClassListContains("state-card--transition-target"));
            Assert.IsFalse(compositeCard.ClassListContains("state-card--transition-target"));
            Assert.IsFalse(CardWithTitle(canvas, "Indirect").ClassListContains("state-card--transition-target"));
            Color purple = new Color(184f / 255f, 122f / 255f, 1f, 1f);
            Assert.IsNull(rootLeafCard.Q(className: "state-card__transition-target-ring"));
            Assert.AreEqual(childCard.layout.size, rootLeafCard.layout.size);
            Assert.AreEqual(2f, rootLeafCard.resolvedStyle.borderLeftWidth);
            Assert.AreEqual(childCard.resolvedStyle.borderLeftWidth, rootLeafCard.resolvedStyle.borderLeftWidth);
            Assert.AreEqual(childCard.resolvedStyle.borderTopWidth, rootLeafCard.resolvedStyle.borderTopWidth);
            Assert.AreEqual(purple, rootLeafCard.resolvedStyle.borderLeftColor);
            Assert.AreEqual(new Color(1f, 184f / 255f, 51f / 255f, 1f), childCard.resolvedStyle.borderLeftColor);
            StateGraphLayerRecord layer = asset.Layers[0];
            Color normal = canvas.GetTransitionColor(layer.Transitions[0]);
            Assert.AreNotEqual(purple, normal, "Incoming edge must keep its normal color.");
            Assert.AreEqual(purple, canvas.GetTransitionColor(layer.Transitions[1]));
            Assert.AreEqual(purple, canvas.GetTransitionColor(layer.Transitions[2]), "Self-loop is outgoing too.");
            Assert.AreEqual(normal, canvas.GetTransitionColor(layer.Transitions[3]));

            controller.SelectState(rootLeaf);
            yield return null;
            Assert.IsFalse(CardWithTitle(canvas, "RootLeaf").ClassListContains("state-card--transition-target"));
            Assert.IsTrue(CardWithTitle(canvas, "Child").ClassListContains("state-card--transition-target"));
            Assert.IsTrue(CardWithTitle(canvas, "Indirect").ClassListContains("state-card--transition-target"));
            Assert.AreEqual(normal, canvas.GetTransitionColor(layer.Transitions[1]));

            controller.SelectTransition(transitionId);
            yield return null;
            Assert.IsFalse(CardWithTitle(canvas, "RootLeaf").ClassListContains("state-card--ancestry"),
                "transition selection must not light the source card");
            Assert.IsFalse(CardWithTitle(canvas, "Child").ClassListContains("state-card--ancestry"),
                "transition selection must not light the target card");
            Assert.AreEqual(0, canvas.ChainGenealogyChildren.Count,
                "transition selection must not light genealogy segments");
            Assert.IsFalse(canvas.Query(className: "state-card--transition-target").ToList().Any());
            Assert.AreEqual(new Color(1f, 0.72f, 0.2f, 1f), canvas.GetTransitionColor(layer.Transitions[0]));
            controller.SelectState(rootLeaf);
            Assert.IsTrue(controller.AddTransition(rootLeaf, rootLeaf, 2, TransitionWindow.Always));
            yield return null;
            Assert.IsFalse(controller.Session.SelectedStateId.IsValid);
            Assert.IsTrue(controller.Session.SelectedTransitionId.IsValid);
            Assert.IsFalse(canvas.Query(className: "state-card--transition-target").ToList().Any());
            Assert.IsFalse(canvas.Query(className: "state-card--ancestry").ToList().Any());
            Assert.AreEqual(new Color(1f, 0.72f, 0.2f, 1f), canvas.GetTransitionColor(layer.Transitions.Last()));
            Assert.IsTrue(layer.Transitions.Take(4).All(edge => canvas.GetTransitionColor(edge) == normal));
            controller.SelectState(default);
            yield return null;
            Assert.IsFalse(canvas.Query(className: "state-card--transition-target").ToList().Any());
            Assert.IsTrue(layer.Transitions.All(edge => canvas.GetTransitionColor(edge) == normal));
        }

        /// <summary>按图层默认路径区分可达叶节点，检查画布样式反映当前拓扑。</summary>
        [Test]
        public void LeafFlowStatesFollowLayerDefaultReachability()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId entry = AddState(asset, layerId, "Entry", new Vector2(40f, 60f));
            StateId mid = AddState(asset, layerId, "Mid", new Vector2(300f, 60f));
            StateId dead = AddState(asset, layerId, "Dead", new Vector2(560f, 60f));
            StateId isolated = AddState(asset, layerId, "Isolated", new Vector2(820f, 60f));
            StateGraphLayerRecord layer = asset.Layers[0];
            layer.InitialStateId = new SerializedId128(entry.High, entry.Low);
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, entry, mid, 0, TransitionWindow.Always, null, out _, out string failure),
                failure);
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, mid, dead, 0, TransitionWindow.Always, null, out _, out failure),
                failure);
            StateGraphTransitionRecord firstTransition = asset.Layers[0].Transitions[0];

            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);

            Assert.AreEqual(
                StateGraphEditorCanvas.LeafFlowState.None,
                canvas.LeafFlowStates[new SerializedId128(entry.High, entry.Low)],
                "layer default with outgoing edges is valid");
            Assert.AreEqual(
                StateGraphEditorCanvas.LeafFlowState.None,
                canvas.LeafFlowStates[new SerializedId128(mid.High, mid.Low)],
                "reachable with in and out is valid");
            Assert.AreEqual(
                StateGraphEditorCanvas.LeafFlowState.DeadEnd,
                canvas.LeafFlowStates[new SerializedId128(dead.High, dead.Low)],
                "reachable without outgoing is a dead end (orange)");
            Assert.AreEqual(
                StateGraphEditorCanvas.LeafFlowState.Unreachable,
                canvas.LeafFlowStates[new SerializedId128(isolated.High, isolated.Low)],
                "isolated leaf is topologically unreachable (red)");

            Assert.IsTrue(TransitionId.TryCreate(
                firstTransition.TransitionId.High, firstTransition.TransitionId.Low,
                out TransitionId entryTransition));
            Assert.IsTrue(StateGraphAuthoringOperations.TryDeleteTransition(
                asset, layerId, entryTransition, out failure), failure);
            controller.Dispose();
            controllers.RemoveAt(controllers.Count - 1);
            canvas.Dispose();
            canvases.RemoveAt(canvases.Count - 1);
            var controller2 = new StateGraphEditorController(asset);
            controllers.Add(controller2);
            var canvas2 = new StateGraphEditorCanvas(controller2);
            canvases.Add(canvas2);
            Assert.AreEqual(
                StateGraphEditorCanvas.LeafFlowState.Unreachable,
                canvas2.LeafFlowStates[new SerializedId128(entry.High, entry.Low)],
                "layer default without outgoing edges is invalid (red)");
        }

        /// <summary>在画布空白处点击，检查先前的节点或转换选择已清除。</summary>
        [UnityTest]
        public IEnumerator ClickingEmptyCanvasClearsSelection()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId idle = AddState(asset, layerId, "Idle", new Vector2(40f, 60f));

            var controller = new StateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new StateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            yield return null;

            controller.SelectState(idle);
            yield return null;
            Assert.IsTrue(controller.Session.SelectedStateId.IsValid);

            Vector2 empty = canvas.worldBound.center;
            SendPointerDown(canvas, 21, empty, empty - canvas.worldBound.position);
            yield return null;
            Assert.IsFalse(controller.Session.SelectedStateId.IsValid,
                "empty-canvas click must clear selection");
        }

        #endregion

        #region Internal Logic

        private readonly System.Collections.Generic.List<StateGraphAsset> assets =
            new System.Collections.Generic.List<StateGraphAsset>();
        private readonly System.Collections.Generic.List<EditorWindow> windows =
            new System.Collections.Generic.List<EditorWindow>();
        private readonly System.Collections.Generic.List<StateGraphEditorCanvas> canvases =
            new System.Collections.Generic.List<StateGraphEditorCanvas>();
        private readonly System.Collections.Generic.List<StateGraphEditorController> controllers =
            new System.Collections.Generic.List<StateGraphEditorController>();

        private StateGraphAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            assets.Add(asset);
            return asset;
        }

        private StateId AddState(
            StateGraphAsset asset,
            LayerId layerId,
            string name,
            Vector2 position)
        {
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddState(
                asset, layerId, default, StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), name, position,
                out StateId stateId, out string failure), failure);
            return stateId;
        }

        private StateGraphEditorWindow CreateStateGraphWindow(StateGraphAsset asset)
        {
            var window = ScriptableObject.CreateInstance<StateGraphEditorWindow>();
            windows.Add(window);
            if (asset != null)
            {
                var serializedWindow = new SerializedObject(window);
                serializedWindow.FindProperty("asset").objectReferenceValue = asset;
                serializedWindow.ApplyModifiedPropertiesWithoutUndo();
            }

            window.CreateGUI();
            return window;
        }

        private VisualElement HostWindow(StateGraphEditorWindow window)
        {
            var content = new VisualElement { name = "state-graph-window-test-content" };
            content.style.flexGrow = 1f;
            while (window.rootVisualElement.childCount > 0)
            {
                content.Add(window.rootVisualElement[0]);
            }

            Host(content);
            return content;
        }

        private VisualElement Host(VisualElement content)
        {
            var window = ScriptableObject.CreateInstance<StateGraphTestPanelWindow>();
            windows.Add(window);
            window.position = new Rect(80f, 80f, 900f, 600f);
            window.Show();
            window.rootVisualElement.Add(content);
            window.Repaint();
            return content;
        }

        private static void SelectTransition(
            StateGraphAsset asset,
            LayerId layerId,
            TransitionId transitionId)
        {
            StateGraphEditorSessionState session = StateGraphEditorSessionState.Load(asset);
            session.SelectedLayerId = layerId;
            session.SelectedTransitionId = transitionId;
            session.Save();
        }

        private static Label FindLabelWithText(VisualElement root, string text)
        {
            Label result = null;
            root.Query<Label>().ForEach(label =>
            {
                if (result == null && label.text == text)
                {
                    result = label;
                }
            });
            return result;
        }

        private static Button FindButtonWithText(VisualElement root, string text)
        {
            Button result = null;
            root.Query<Button>().ForEach(button =>
            {
                if (result == null && button.text == text)
                {
                    result = button;
                }
            });
            return result;
        }

        private static VisualElement CardWithTitle(VisualElement canvas, string title)
        {
            VisualElement result = null;
            canvas.Query<VisualElement>("state-card").ForEach(card =>
            {
                if (result == null)
                {
                    Label first = card.Q<Label>(className: "state-card__title");
                    if (first != null && first.text == title)
                    {
                        result = card;
                    }
                }
            });
            return result;
        }

        private static bool Matches(SerializedId128 id, TransitionId value) =>
            id.High == value.High && id.Low == value.Low;

        private static void SendPointerDown(
            VisualElement target,
            int pointerId,
            Vector2 position,
            Vector2 localPosition,
            int clickCount = 1)
        {
            using (PointerDownEvent evt = PointerDownEvent.GetPooled(
                       new ClickablePointerEvent(pointerId, position, localPosition, clickCount)))
            {
                target.SendEvent(evt);
            }
        }

        /// <summary>让画布命中测试重放给定位置的指针点击。</summary>
        private sealed class ClickablePointerEvent : IPointerEvent
        {
            internal ClickablePointerEvent(int pointerId, Vector2 position, Vector2 localPosition, int clickCount)
            {
                this.pointerId = pointerId;
                button = 0;
                pressedButtons = 1;
                this.position = position;
                this.localPosition = localPosition;
                this.clickCount = clickCount;
                pointerType = UnityEngine.UIElements.PointerType.touch;
            }

            public int pointerId { get; }
            public string pointerType { get; }
            public bool isPrimary => true;
            public int button { get; }
            public int pressedButtons { get; }
            public Vector3 position { get; }
            public Vector3 localPosition { get; }
            public Vector3 deltaPosition => default;
            public float deltaTime => 0f;
            public int clickCount { get; }
            public float pressure => 1f;
            public float tangentialPressure => 0f;
            public float altitudeAngle => 0f;
            public float azimuthAngle => 0f;
            public float twist => 0f;
            public Vector2 tilt => default;
            public PenStatus penStatus => default;
            public Vector2 radius => Vector2.one;
            public Vector2 radiusVariance => default;
            public EventModifiers modifiers => EventModifiers.None;
            public bool shiftKey => false;
            public bool ctrlKey => false;
            public bool commandKey => false;
            public bool altKey => false;
            public bool actionKey => false;
        }

        #endregion
    }
}
