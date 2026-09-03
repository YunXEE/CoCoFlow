using System;
using System.Collections;
using System.Linq;
using CoCoFlow.Editor.Common;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    /// <summary>
    /// P03 视觉语言与 D8 边交互测试：
    /// ccflow 主题接入 / severity→kind 映射 / 空状态 / 双语 /
    /// Animator 边（点击选中、双向平行、自环、双击下钻）/
    /// Inspector UITK 等价 / N2 元素名升级断言。
    /// </summary>
    public sealed class CoCoStateGraphEditorVisualLanguageTests
    {
        private readonly System.Collections.Generic.List<CoCoStateGraphAsset> assets =
            new System.Collections.Generic.List<CoCoStateGraphAsset>();
        private readonly System.Collections.Generic.List<EditorWindow> windows =
            new System.Collections.Generic.List<EditorWindow>();
        private readonly System.Collections.Generic.List<GameObject> panelObjects =
            new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<PanelSettings> panelSettings =
            new System.Collections.Generic.List<PanelSettings>();
        private readonly System.Collections.Generic.List<CoCoStateGraphEditorCanvas> canvases =
            new System.Collections.Generic.List<CoCoStateGraphEditorCanvas>();
        private readonly System.Collections.Generic.List<CoCoStateGraphEditorController> controllers =
            new System.Collections.Generic.List<CoCoStateGraphEditorController>();

        [SetUp]
        public void SetUp()
        {
            // 语言自钉：EditorPrefs 为机器级共享（维护者会话可能停在中文）。
            CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.English);
        }

        [TearDown]
        public void TearDown()
        {
            CoCoStateGraphEditorCatalogProvider.Provider = null;
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
            foreach (CoCoStateGraphEditorCanvas canvas in canvases)
            {
                canvas?.Dispose();
            }

            canvases.Clear();
            foreach (CoCoStateGraphEditorController controller in controllers)
            {
                controller?.Dispose();
            }

            controllers.Clear();
            foreach (GameObject panelObject in panelObjects)
            {
                if (panelObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(panelObject);
                }
            }

            panelObjects.Clear();
            foreach (PanelSettings settings in panelSettings)
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }

            panelSettings.Clear();
            Undo.ClearAll();
            foreach (CoCoStateGraphAsset asset in assets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            assets.Clear();
        }

        [Test]
        public void WindowAppliesCcflowThemeAndSharedStyleSheet()
        {
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(CreateAsset());
            Assert.IsTrue(window.rootVisualElement.ClassListContains("ccflow-root"));
            // 主题与 P03 专用样式表均已挂载（ApplyTheme + 包路径加载）。
            StyleSheet shared = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yunxee.cocoflow/Editor/Common/CoCoEditorCommon.uss");
            StyleSheet own = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yunxee.cocoflow/Editor/StateGraph/CoCoStateGraphEditor.uss");
            Assert.NotNull(shared);
            Assert.NotNull(own);
            Assert.IsTrue(window.rootVisualElement.styleSheets.Contains(shared),
                "shared ccflow sheet must be attached");
            Assert.IsTrue(window.rootVisualElement.styleSheets.Contains(own),
                "sg sheet must be attached");
        }

        [Test]
        public void SeverityToBadgeKindMapsAllSeverities()
        {
            Assert.AreEqual(
                CoCoEditorBadgeKind.Info,
                CoCoStateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.Information));
            Assert.AreEqual(
                CoCoEditorBadgeKind.Warning,
                CoCoStateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.Warning));
            Assert.AreEqual(
                CoCoEditorBadgeKind.Error,
                CoCoStateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.Error));
            Assert.AreEqual(
                CoCoEditorBadgeKind.Neutral,
                CoCoStateGraphEditorWindow.SeverityToBadgeKind(CoCoDiagnosticSeverity.None));
        }

        [Test]
        public void EmptyAssetWindowShowsEmptyStateWithPresetEntry()
        {
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(null);
            VisualElement empty = window.rootVisualElement.Q(className: "ccflow-empty");
            Assert.NotNull(empty, "ccflow empty state must be present without an asset");
            Assert.NotNull(window.rootVisualElement.Q<ObjectField>());
        }

        [Test]
        public void NoLayerWindowShowsEmptyStateInsteadOfRawFields()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            Assert.NotNull(window.rootVisualElement.Q(className: "ccflow-empty"));
            Assert.NotNull(window.rootVisualElement.Q<VisualElement>("state-graph-feedback"));
        }

        [Test]
        public void BilingualStaticTextFollowsLanguagePreference()
        {
            try
            {
                CoCoStateGraphEditorWindow english = CreateStateGraphWindow(null);
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

        [Test]
        public void DetailsExposeStableElementNamesIncludingConditionConfig()
        {
            CoCoStateGraphEditorCatalogProvider.Provider =
                () => CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layerId, "First", new Vector2(40f, 60f));
            CoCoStateId second = AddState(asset, layerId, "Second", new Vector2(300f, 60f));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, first, second, 0, CoCoTransitionWindow.Always, null,
                out CoCoTransitionId transitionId, out string failure), failure);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddCondition(
                asset, layerId, transitionId, CoCoStateGraphTestFactory.ConditionDescriptorId,
                new TestConditionAuthoringConfig { Threshold = 5 }, out _, out failure), failure);
            SelectTransition(asset, layerId, transitionId);

            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = HostWindow(window);
            // N2 升级断言：三个此前仅存在于源码的挂点名全部可查询。
            Assert.NotNull(root.Q<VisualElement>("state-graph-tree"));
            Assert.NotNull(root.Q<VisualElement>("state-graph-details"));
            Assert.NotNull(root.Q<VisualElement>("state-graph-condition-config"));
        }

        [Test]
        public void AssetInspectorUiToolkitShowsSummaryAdaptersAndEntries()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");

            var editor = (CoCoStateGraphAssetEditor)UnityEditor.Editor.CreateEditor(asset);
            Assert.NotNull(editor);
            VisualElement root = editor.CreateInspectorGUI();
            Assert.NotNull(root);

            Assert.NotNull(root.Q<VisualElement>(className: "ccflow-card"));
            Assert.NotNull(root.Q<VisualElement>("state-graph-event-adapters"),
                "Event Adapter Declarations must remain editable in the Inspector");
            Assert.NotNull(FindButtonWithText(root, "Open State Graph Editor"));
            Assert.NotNull(FindButtonWithText(root, "Add Layer"));
            Assert.NotNull(FindButtonWithText(root, "Analyze With Registered Catalog"));

            // 不提供第二裸拓扑编辑面：不得出现绑定 layers/states/transitions 的 PropertyField。
            UnityEngine.Object.DestroyImmediate(editor);
        }

        [UnityTest]
        public IEnumerator EdgeClickSelectsTransitionOnHitAndIgnoresMiss()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layerId, "First", new Vector2(40f, 60f));
            CoCoStateId second = AddState(asset, layerId, "Second", new Vector2(300f, 60f));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, first, second, 0, CoCoTransitionWindow.Always, null,
                out CoCoTransitionId transitionId, out string failure), failure);

            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++)
            {
                yield return null; // 等待首次重绘生成边几何缓存
            }

            Assert.GreaterOrEqual(canvas.EdgeHitCount, 1, "edge geometry must exist after repaint");
            Vector2 hitPoint = new Vector2(264f, 108f); // 两卡中心连线中点
            Assert.IsTrue(canvas.TryHitEdge(hitPoint, 6f, out CoCoSerializedId128 hitId));
            Assert.AreEqual(
                new CoCoSerializedId128(transitionId.High, transitionId.Low),
                hitId);

            // 未命中分支：远离任何边。
            Assert.IsFalse(canvas.TryHitEdge(new Vector2(1000f, 1000f), 6f, out _));
            Assert.IsFalse(controller.Session.SelectedTransitionId.IsValid);

            // 端到端：点击边面板位置 → SelectTransition。
            SendPointerDown(canvas, 0, canvas.worldBound.position + hitPoint, hitPoint);
            Assert.AreEqual(
                transitionId,
                controller.Session.SelectedTransitionId);
        }

        [UnityTest]
        public IEnumerator BidirectionalEdgesRenderAsParallelHittableLines()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layerId, "First", new Vector2(40f, 60f));
            CoCoStateId second = AddState(asset, layerId, "Second", new Vector2(300f, 60f));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, first, second, 0, CoCoTransitionWindow.Always, null,
                out CoCoTransitionId forward, out string failure), failure);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, second, first, 0, CoCoTransitionWindow.Always, null,
                out CoCoTransitionId backward, out failure), failure);

            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(2, canvas.EdgeHitCount, "bidirectional pair must render two parallel edges");
            // 中心连线法线方向 ±ParallelSpacing/2 处各自命中对应方向的边。
            Vector2 midpoint = new Vector2(264f, 108f);
            Vector2 normal = new Vector2(0f, 1f); // 水平连线的法线
            Assert.IsTrue(canvas.TryHitEdge(midpoint + normal * 8f, 3f, out CoCoSerializedId128 upper));
            Assert.IsTrue(canvas.TryHitEdge(midpoint - normal * 8f, 3f, out CoCoSerializedId128 lower));
            Assert.AreNotEqual(upper, lower, "parallel offsets must map to distinct transitions");
            bool coversForward = Matches(upper, forward) || Matches(lower, forward);
            bool coversBackward = Matches(upper, backward) || Matches(lower, backward);
            Assert.IsTrue(coversForward && coversBackward, "both directions must be hittable");
        }

        [UnityTest]
        public IEnumerator SelfLoopEdgeIsHittable()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId state = AddState(asset, layerId, "Idle", new Vector2(120f, 120f));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, state, state, 0, CoCoTransitionWindow.Always, null,
                out CoCoTransitionId selfLoop, out string failure), failure);

            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            for (int frame = 0; frame < 10 && canvas.EdgeHitCount == 0; frame++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(canvas.EdgeHitCount, 1);
            // 回环中心（卡片顶上方）必命中。
            Vector2 loopCenter = new Vector2(120f + 94f, 120f - 27f);
            Assert.IsTrue(canvas.TryHitEdge(loopCenter, 12f, out CoCoSerializedId128 hitId));
            Assert.AreEqual(new CoCoSerializedId128(selfLoop.High, selfLoop.Low), hitId);
        }

        [UnityTest]
        public IEnumerator SelectingStateLightsAncestryChainAndTransitionLightsOnlyItself()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId rootLeaf = AddState(asset, layerId, "RootLeaf", new Vector2(40f, 60f));
            CoCoStateId composite = AddState(asset, layerId, "Parent", new Vector2(300f, 60f));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddState(
                asset, layerId, composite,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Child",
                new Vector2(80f, 80f), out CoCoStateId child, out string failure), failure);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, rootLeaf, child, 0, CoCoTransitionWindow.Always, null,
                out CoCoTransitionId transitionId, out failure), failure);

            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            yield return null;

            // 血统链：选中子 State → 自己+全部祖先外描边；谱系段 Child←Parent 点亮。
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
                new CoCoSerializedId128(child.High, child.Low)),
                "the child<-composite genealogy segment must be lit");
            Assert.IsFalse(canvas.ChainGenealogyChildren.Contains(
                new CoCoSerializedId128(composite.High, composite.Low)),
                "root composite has no genealogy segment");

            // Transition 选中只亮线本身：两端卡片不得点亮。
            controller.SelectTransition(transitionId);
            yield return null;
            Assert.IsFalse(CardWithTitle(canvas, "RootLeaf").ClassListContains("state-card--ancestry"),
                "transition selection must not light the source card");
            Assert.IsFalse(CardWithTitle(canvas, "Child").ClassListContains("state-card--ancestry"),
                "transition selection must not light the target card");
            Assert.AreEqual(0, canvas.ChainGenealogyChildren.Count,
                "transition selection must not light genealogy segments");
        }

        [Test]
        public void LeafFlowStatesFollowLayerDefaultReachability()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId entry = AddState(asset, layerId, "Entry", new Vector2(40f, 60f));
            CoCoStateId mid = AddState(asset, layerId, "Mid", new Vector2(300f, 60f));
            CoCoStateId dead = AddState(asset, layerId, "Dead", new Vector2(560f, 60f));
            CoCoStateId isolated = AddState(asset, layerId, "Isolated", new Vector2(820f, 60f));
            CoCoStateGraphLayerRecord layer = asset.Layers[0];
            layer.InitialStateId = new CoCoSerializedId128(entry.High, entry.Low);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, entry, mid, 0, CoCoTransitionWindow.Always, null, out _, out string failure),
                failure);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset, layerId, mid, dead, 0, CoCoTransitionWindow.Always, null, out _, out failure),
                failure);
            CoCoStateGraphTransitionRecord firstTransition = asset.Layers[0].Transitions[0];

            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);

            Assert.AreEqual(
                CoCoStateGraphEditorCanvas.LeafFlowState.None,
                canvas.LeafFlowStates[new CoCoSerializedId128(entry.High, entry.Low)],
                "layer default with outgoing edges is valid");
            Assert.AreEqual(
                CoCoStateGraphEditorCanvas.LeafFlowState.None,
                canvas.LeafFlowStates[new CoCoSerializedId128(mid.High, mid.Low)],
                "reachable with in and out is valid");
            Assert.AreEqual(
                CoCoStateGraphEditorCanvas.LeafFlowState.DeadEnd,
                canvas.LeafFlowStates[new CoCoSerializedId128(dead.High, dead.Low)],
                "reachable without outgoing is a dead end (orange)");
            Assert.AreEqual(
                CoCoStateGraphEditorCanvas.LeafFlowState.Unreachable,
                canvas.LeafFlowStates[new CoCoSerializedId128(isolated.High, isolated.Low)],
                "isolated leaf is topologically unreachable (red)");

            // Default 无出边 → 红。
            Assert.IsTrue(CoCoTransitionId.TryCreate(
                firstTransition.TransitionId.High, firstTransition.TransitionId.Low,
                out CoCoTransitionId entryTransition));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryDeleteTransition(
                asset, layerId, entryTransition, out failure), failure);
            controller.Dispose();
            controllers.RemoveAt(controllers.Count - 1);
            canvas.Dispose();
            canvases.RemoveAt(canvases.Count - 1);
            var controller2 = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller2);
            var canvas2 = new CoCoStateGraphEditorCanvas(controller2);
            canvases.Add(canvas2);
            Assert.AreEqual(
                CoCoStateGraphEditorCanvas.LeafFlowState.Unreachable,
                canvas2.LeafFlowStates[new CoCoSerializedId128(entry.High, entry.Low)],
                "layer default without outgoing edges is invalid (red)");
        }

        [UnityTest]
        public IEnumerator ClickingEmptyCanvasClearsSelection()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId idle = AddState(asset, layerId, "Idle", new Vector2(40f, 60f));

            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            Host(canvas);
            yield return null;

            controller.SelectState(idle);
            yield return null;
            Assert.IsTrue(controller.Session.SelectedStateId.IsValid);

            // 空白处（画布中心，远离卡/线/右下缩放控件）左键 → 清除选中。
            Vector2 empty = canvas.worldBound.center;
            SendPointerDown(canvas, 21, empty, empty - canvas.worldBound.position);
            yield return null;
            Assert.IsFalse(controller.Session.SelectedStateId.IsValid,
                "empty-canvas click must clear selection");
        }

        // ── 助手 ───────────────────────────────────────────

        private CoCoStateGraphAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            assets.Add(asset);
            return asset;
        }

        private CoCoStateId AddState(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            string name,
            Vector2 position)
        {
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddState(
                asset, layerId, default, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), name, position,
                out CoCoStateId stateId, out string failure), failure);
            return stateId;
        }

        private CoCoStateGraphEditorWindow CreateStateGraphWindow(CoCoStateGraphAsset asset)
        {
            var window = ScriptableObject.CreateInstance<CoCoStateGraphEditorWindow>();
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

        private VisualElement HostWindow(CoCoStateGraphEditorWindow window)
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
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            panelSettings.Add(settings);

            var panelObject = new GameObject("CoCoFlow StateGraph Visual Test Panel");
            panelObject.SetActive(false);
            UIDocument document = panelObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            panelObject.SetActive(true);
            panelObjects.Add(panelObject);
            document.rootVisualElement.style.width = 900f;
            document.rootVisualElement.style.height = 600f;
            document.rootVisualElement.Add(content);
            return content;
        }

        private static void SelectTransition(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoTransitionId transitionId)
        {
            CoCoStateGraphEditorSessionState session = CoCoStateGraphEditorSessionState.Load(asset);
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

        private static bool Matches(CoCoSerializedId128 id, CoCoTransitionId value) =>
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
    }
}
