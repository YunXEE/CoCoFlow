using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public sealed class CoCoStateGraphEditorInteractionTests
    {
        private static readonly MethodInfo InvokeClickable = typeof(Clickable).GetMethod(
            "Invoke",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<CoCoStateGraphAsset> assets = new List<CoCoStateGraphAsset>();
        private readonly List<EditorWindow> windows = new List<EditorWindow>();
        private readonly List<CoCoStateGraphEditorCanvas> canvases =
            new List<CoCoStateGraphEditorCanvas>();
        private readonly List<CoCoStateGraphEditorController> controllers =
            new List<CoCoStateGraphEditorController>();

        [SetUp]
        public void SetUp()
        {
            // 语言自钉：反馈文案断言依赖英文态；EditorPrefs 机器级共享。
            CoCoFlow.Editor.Common.CoCoEditorLocalization.SetLanguage(
                CoCoFlow.Editor.Common.CoCoEditorLanguage.English);
        }

        [TearDown]
        public void TearDown()
        {
            CoCoStateGraphEditorCatalogProvider.Provider = null;
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

        [UnityTest]
        public IEnumerator CancelledDraftNavigationKeepsLayerAndCompositeUnchanged()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoLayerId other = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Other");
            CoCoStateId state = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Idle");
            SelectState(asset, layer, default);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = CreateWindowPanelHost(window).contentRoot;
            var controller = WindowController(window);
            controller.SelectState(state);
            yield return null;
            controller.SetStateDraftName("Idle draft");
            int confirmations = 0;
            controller.ConfirmStateDraftChanges = () => { confirmations++; return false; };
            string original = EditorJsonUtility.ToJson(asset);
            var picker = root.Q<PopupField<string>>("state-graph-header-layer-field");
            picker.value = "Other";
            Assert.AreEqual("Gameplay", picker.value);
            Assert.AreEqual(layer, controller.Session.SelectedLayerId);
            Assert.IsFalse(window.TryAddCompositeAtCanvasPosition(new Vector2(350f, 100f)));
            Assert.AreEqual(2, confirmations);
            Assert.AreEqual(original, EditorJsonUtility.ToJson(asset), "Cancel must not write the asset.");
            Assert.AreEqual(state, controller.Session.SelectedStateId);
            Assert.AreEqual("Idle draft", controller.BeginStateDraft().DisplayName);
            Assert.IsTrue(controller.HasStateDraftChanges);

            controller.ConfirmStateDraftChanges = () => { controller.DiscardStateDraft(); return true; };
            Assert.IsTrue(window.TryAddCompositeAtCanvasPosition(new Vector2(350f, 100f)));
            Assert.AreEqual(3, asset.Layers[0].States.Count);
            Assert.AreEqual("Composite", asset.Layers[0].States[1].DisplayName);
            Assert.AreEqual(asset.Layers[0].States[1].StateId, Serialize(controller.Session.SelectedStateId));
            root.Q<PopupField<string>>("state-graph-header-layer-field").value = "Other";
            Assert.AreEqual(other, controller.Session.SelectedLayerId);
        }

        [UnityTest]
        public IEnumerator UnresolvedLogicStaysExplicitUntilTheUserSelectsAReplacement()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateDescriptorId missing = CoCoStateGraphTestFactory.CreateStateDescriptorId(999UL);
            CoCoStateId state = AddState(asset, layer, missing,
                new TestStateAuthoringConfig { Value = 7 }, "Idle");
            SelectState(asset, layer, state);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = CreateWindowPanelHost(window).contentRoot;
            var controller = WindowController(window);
            yield return null;
            var picker = root.Q<PopupField<string>>("selected-state-descriptor");
            Assert.That(picker.value, Does.StartWith("Unresolved Logic ["));
            Assert.AreEqual(catalog.StateDescriptors.Count + 1, picker.choices.Count);
            controller.SetStateDraftName("Renamed");
            Assert.IsTrue(controller.SaveStateDraft());
            Assert.AreEqual(Serialize(missing), asset.Layers[0].States[0].StateDescriptorId);
            Assert.AreEqual(7, ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value);
            Assert.AreEqual("Renamed", asset.Layers[0].States[0].DisplayName);

            picker = root.Q<PopupField<string>>("selected-state-descriptor");
            string unresolved = picker.value;
            string replacement = picker.choices[1];
            picker.value = replacement;
            Assert.IsTrue(controller.HasStateDraftChanges);
            Assert.AreEqual(Serialize(missing), asset.Layers[0].States[0].StateDescriptorId);
            picker.value = unresolved;
            Assert.AreEqual(replacement, picker.value, "The unresolved entry is a read-only placeholder.");
            Assert.IsTrue(controller.SaveStateDraft());
            Assert.AreEqual(Serialize(catalog.StateDescriptors[0].DescriptorId),
                asset.Layers[0].States[0].StateDescriptorId);
        }

        [UnityTest]
        public IEnumerator StateDraftEditsWaitForSaveAndPreserveControls()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            var config = new TestStateAuthoringConfig();
            CoCoStateId state = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId, config, "Idle");
            SelectState(asset, layer, state);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = CreateWindowPanelHost(window).contentRoot;
            var controller = WindowController(window);
            yield return null;
            Assert.IsFalse(controller.HasStateDraftChanges, "Binding the initial Config must stay clean.");
            controller.Analyze();
            Assert.IsTrue(controller.AnalysisResult.Succeeded);
            VisualElement input = root.Q<VisualElement>("state-graph-state-config");
            Assert.NotNull(input);
            int before = CoCoStateGraphFixtureCounters.StateFreezeCalls;
            int rebuilds = 0;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            IntegerField configInput = input.Q<IntegerField>();
            Assert.NotNull(configInput);
            Assert.IsTrue(configInput.enabledInHierarchy, "Config must be editable by the user, not just writable by tests.");
            root.Q<TextField>("state-graph-state-name").value = "Idle renamed";
            for (int index = 1; index <= 8; index++)
            {
                configInput.value = index;
                if (root.Q<VisualElement>("state-graph-state-config") != input) rebuilds++;
            }

            yield return null;
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls - before,
                "Typing must not analyze the draft.");
            Assert.AreEqual(0, config.Value, "Unsaved Config must not touch the asset.");
            Assert.AreEqual("Idle", asset.Layers[0].States[0].DisplayName);
            Assert.IsTrue(window.hasUnsavedChanges);
            Assert.AreEqual("Unsaved changes", root.Q<Label>("state-graph-state-save-status").text);
            Assert.IsFalse(root.Q<Foldout>("state-graph-state-structure").enabledSelf);
            Assert.AreEqual(0, rebuilds);
            Assert.IsTrue(input.Children().All(child => child is PropertyField),
                "StateConfig fields are direct children, without an enclosing foldout.");
            Assert.IsTrue(configInput.visible);

            Click(root.Q<Button>("state-graph-save-state"));
            timer.Stop();
            int analyses = CoCoStateGraphFixtureCounters.StateFreezeCalls - before;
            TestContext.WriteLine($"003 draftEdits=9 analysesBeforeSave=0 analysesOnSave={analyses} detailsRebuildsWhileTyping={rebuilds} elapsedMs={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.AreEqual(1, analyses);
            Assert.IsFalse(window.hasUnsavedChanges);
            Assert.AreEqual("Idle renamed", asset.Layers[0].States[0].DisplayName);
            Assert.IsTrue(controller.AnalysisResult.Graph.Layers[0].States[0].Config.TryRead(
                TestFrozenConfigSchemas.StateValue, out int latest));
            Assert.AreEqual(8, latest);
            Assert.AreEqual(8, ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value);
            Assert.IsTrue(ContainsLabel(root.Q<VisualElement>("state-graph-feedback"), "Compilation succeeded."));

            string[] args = Environment.GetCommandLineArgs();
            int screenshotIndex = Array.IndexOf(args, "-cocoflow003Screenshot");
            if (!Application.isBatchMode && screenshotIndex >= 0)
            {
                bool transitionPreview = args.Contains("-cocoflow003TransitionPreview");
                if (transitionPreview)
                {
                    catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                    ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
                    CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
                }
                window.CreateGUI();
                window.titleContent = new GUIContent("003 Layout Preview");
                window.Show();
                window.position = new Rect(40f, 60f, 1360f, 800f);
                window.Focus();
                yield return null;
                window.Repaint();
                yield return null;
                CoCoStateId movePreviewState = default;
                for (int preview = transitionPreview ? 3 : 0; preview < (transitionPreview ? 6 : 3); preview++)
                {
                    if (preview == 1)
                        window.rootVisualElement.Q<TextField>("state-graph-state-name").value = "Idle draft";
                    if (preview == 2)
                    {
                        window.DiscardChanges();
                        window.rootVisualElement.Q<Foldout>("state-graph-state-structure").value = true;
                    }
                    if (preview == 3)
                    {
                        var previewController = WindowController(window);
                        Assert.IsTrue(catalog.TryGetStateDescriptor(CoCoStateGraphTestFactory.StateDescriptorId,
                            out CoCoStateDescriptor descriptor));
                        Assert.IsTrue(previewController.AddState(default, descriptor, "Move", new Vector2(350f, 60f)));
                        movePreviewState = previewController.Session.SelectedStateId;
                        Assert.IsTrue(previewController.AddState(default, descriptor, "Jump", new Vector2(350f, 280f)));
                        CoCoStateId jumpState = previewController.Session.SelectedStateId;
                        Assert.IsTrue(previewController.AddTransition(state, movePreviewState, 0, CoCoTransitionWindow.Always));
                        CoCoTransitionId focusedTransition = previewController.Session.SelectedTransitionId;
                        Assert.IsTrue(previewController.AddTransition(state, jumpState, 1, CoCoTransitionWindow.Always));
                        for (int index = 0; index < 30; index++)
                            Assert.IsTrue(previewController.AddTransition(movePreviewState, state, index, CoCoTransitionWindow.Always));
                        previewController.SelectTransition(focusedTransition);
                        Assert.IsTrue(catalog.TryGetConditionDescriptor(CoCoStateGraphTestFactory.ConditionDescriptorId,
                            out CoCoConditionDescriptor condition));
                        Assert.IsTrue(previewController.AddCondition(condition));
                        previewController.Analyze();
                    }
                    if (preview >= 4)
                    {
                        WindowController(window).SelectState(preview == 4 ? state : movePreviewState);
                    }
                    window.Repaint();
                    yield return null;
                    yield return null;
                    if (preview >= 4)
                    {
                        window.rootVisualElement.Q<ScrollView>("state-graph-details").ScrollTo(
                            window.rootVisualElement.Q<VisualElement>("state-graph-transitions"));
                        yield return null;
                    }
                    Rect bounds = window.position;
                    float scale = EditorGUIUtility.pixelsPerPoint;
                    int width = Mathf.RoundToInt(bounds.width * scale);
                    int height = Mathf.RoundToInt(bounds.height * scale);
                    object view = typeof(EditorWindow).GetField("m_Parent",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Type viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView");
                    viewType.GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public)
                        .Invoke(view, null);
                    var buffer = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                    buffer.Create();
                    viewType.GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(view, new object[] { buffer, new Rect(0f, 0f, width, height) });
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = buffer;
                    var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    // Metal's Editor back buffer has a top-origin pixel layout.
                    Color[] pixels = image.GetPixels();
                    for (int row = 0; row < height / 2; row++)
                    {
                        int top = row * width;
                        int bottom = (height - row - 1) * width;
                        for (int column = 0; column < width; column++)
                            (pixels[top + column], pixels[bottom + column]) =
                                (pixels[bottom + column], pixels[top + column]);
                    }
                    image.SetPixels(pixels);
                    image.Apply();
                    RenderTexture.active = previous;
                    buffer.Release();
                    UnityEngine.Object.DestroyImmediate(buffer);
                    string path = args[screenshotIndex + 1];
                    if (preview == 1) path = path.Replace(".png", "-unsaved.png");
                    if (preview == 2) path = path.Replace(".png", "-structure.png");
                    if (preview == 3) path = path.Replace(".png", "-transition-selected.png");
                    if (preview == 4) path = path.Replace(".png", "-transition-create.png");
                    if (preview == 5) path = path.Replace(".png", "-transition-many.png");
                    System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
        }

        [UnityTest]
        public IEnumerator StateDetailsShowOnlyOutgoingTransitionsAndKeepDiagnosticsOnLeft()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "First");
            CoCoStateId second = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Second");
            CoCoStateId third = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Third");
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(asset, layer, first, second,
                7, CoCoTransitionWindow.Always, catalog, out _, out string failure), failure);
            for (int index = 0; index < 24; index++)
                Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(asset, layer, second, first,
                    index, CoCoTransitionWindow.Always, catalog, out _, out failure), failure);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(asset, layer, third, second,
                0, CoCoTransitionWindow.Always, catalog, out _, out failure), failure);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(asset, layer, first, third,
                2, CoCoTransitionWindow.Always, catalog, out CoCoTransitionId selected, out failure), failure);
            CoCoSerializedId128[] order = asset.Layers[0].Transitions.Select(edge => edge.TransitionId).ToArray();
            SelectState(asset, layer, first);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = CreateWindowPanelHost(window).contentRoot;
            var controller = WindowController(window);
            var outgoing = root.Q<ScrollView>("state-graph-outgoing-transitions");
            Assert.IsNull(outgoing.GetFirstAncestorOfType<Foldout>(), "Outgoing controls and list stay expanded.");
            var rows = outgoing.Query<Button>(className: "sg-transition-row").ToList();
            Assert.AreEqual(2, rows.Count, "Incoming and unrelated edges must be hidden.");
            Assert.AreEqual("First → Second  Priority 7", rows[0].text);
            Assert.AreEqual("First → Third  Priority 2", rows[1].text);
            Assert.IsNull(root.Q<VisualElement>("state-graph-layer-management"));
            var source = root.Query<PopupField<string>>().ToList().Single(field => field.label == "Source State");
            Assert.AreEqual(1, source.choices.Count);
            Assert.IsFalse(source.enabledSelf, "Adding an edge stays scoped to the selected State.");
            VisualElement feedback = root.Q<VisualElement>("state-graph-feedback");
            Assert.IsTrue(root.Q<VisualElement>("state-graph-left-pane").Contains(feedback));
            Assert.IsTrue(root.Q<ScrollView>("state-graph-diagnostics-panel").Contains(feedback));
            Assert.IsFalse(root.Q<ScrollView>("state-graph-details").Contains(feedback));
            Assert.IsFalse(feedback.ClassListContains("ccflow-feedback"));
            Assert.IsNull(feedback.Q(className: "ccflow-card"), "Diagnostics use plain sections.");
            controller.SelectState(second);
            rows = root.Q<ScrollView>("state-graph-outgoing-transitions")
                .Query<Button>(className: "sg-transition-row").ToList();
            Assert.AreEqual(24, rows.Count);
            Assert.IsTrue(rows.All(row => row.text.StartsWith("Second → First", StringComparison.Ordinal)));
            controller.SelectTransition(selected);
            rows = root.Q<ScrollView>("state-graph-outgoing-transitions")
                .Query<Button>(className: "sg-transition-row").ToList();
            Assert.AreEqual(2, rows.Count, "An edge editor keeps its source State's scope.");
            Assert.IsTrue(rows.All(row => row.text.StartsWith("First →", StringComparison.Ordinal)));
            Assert.IsFalse(controller.Session.SelectedStateId.IsValid);
            Assert.IsNull(root.Q<VisualElement>("state-graph-layer-management"), "Selecting an edge is not empty selection.");
            Assert.AreSame(feedback, root.Q<VisualElement>("state-graph-feedback"));
            controller.SelectState(default);
            yield return null;
            yield return null;
            outgoing = root.Q<ScrollView>("state-graph-outgoing-transitions");
            rows = outgoing.Query<Button>(className: "sg-transition-row").ToList();
            Assert.AreEqual(27, rows.Count);
            Assert.IsTrue(rows.Take(24).All(row => row.text.StartsWith("Second → First", StringComparison.Ordinal)));
            Assert.AreEqual("First → Second  Priority 7", rows[24].text);
            Assert.AreEqual("First → Third  Priority 2", rows[25].text);
            Assert.AreEqual("Third → Second  Priority 0", rows[26].text);
            Assert.IsFalse(root.Query<PopupField<string>>().ToList().Any(field => field.label == "Source State"));
            var allCard = root.Q<VisualElement>("state-graph-all-transitions");
            var sidebar = root.Q<ScrollView>("state-graph-details");
            Assert.AreSame(root.Q<VisualElement>("state-graph-layer-management"), sidebar[0]);
            Assert.IsTrue(sidebar[1].Contains(allCard));
            Assert.AreEqual(layer, controller.Session.SelectedLayerId);
            var viewport = root.Q<ScrollView>("state-graph-details").contentViewport;
            Assert.That(allCard.layout.height, Is.LessThanOrEqualTo(viewport.layout.height * 0.5f + 1f));
            Assert.Greater(outgoing.verticalScroller.highValue, 0f, "Large global lists must scroll.");
            CollectionAssert.AreEqual(order, asset.Layers[0].Transitions.Select(edge => edge.TransitionId).ToArray());
        }

        [UnityTest]
        public IEnumerator ExplicitAnalysisAndWindowLifecycleCancelPendingWork()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            var config = new TestStateAuthoringConfig();
            CoCoStateId state = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId, config, "Idle");
            SelectState(asset, layer, state);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            var controller = WindowController(window);
            controller.SelectState(default);
            int before = CoCoStateGraphFixtureCounters.StateFreezeCalls;
            controller.NotifyConfigChanged();
            yield return null;
            yield return null;
            Assert.IsNull(controller.AnalysisResult, "Unrequested analysis must stay off.");
            Assert.AreEqual(before, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            controller.Analyze();
            config.Value = 31;
            controller.NotifyConfigChanged();
            controller.Analyze();
            Assert.IsTrue(controller.AnalysisResult.Graph.Layers[0].States[0].Config.TryRead(
                TestFrozenConfigSchemas.StateValue, out int latest));
            Assert.AreEqual(31, latest);
            before = CoCoStateGraphFixtureCounters.StateFreezeCalls;
            yield return null;
            yield return null;
            Assert.AreEqual(before, CoCoStateGraphFixtureCounters.StateFreezeCalls, "Explicit Analyze cancels the queued call.");

            config.Value = 32;
            controller.NotifyConfigChanged();
            CoCoStateGraphAsset nextAsset = CreateAsset();
            CoCoLayerId nextLayer = CoCoStateGraphAuthoringOperations.AddLayer(nextAsset, "Next");
            AddState(nextAsset, nextLayer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Next");
            var serializedWindow = new SerializedObject(window);
            serializedWindow.FindProperty("asset").objectReferenceValue = nextAsset;
            serializedWindow.ApplyModifiedPropertiesWithoutUndo();
            window.CreateGUI();
            var next = WindowController(window);
            Assert.AreNotSame(controller, next);
            before = CoCoStateGraphFixtureCounters.StateFreezeCalls;
            yield return null;
            yield return null;
            Assert.IsNull(controller.AnalysisResult, "Switched-away asset must not receive a result.");
            Assert.IsNull(next.AnalysisResult);
            Assert.AreEqual(before, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            next.Analyze();
            next.NotifyConfigChanged();
            UnityEngine.Object.DestroyImmediate(window);
            before = CoCoStateGraphFixtureCounters.StateFreezeCalls;
            yield return null;
            yield return null;
            Assert.IsNull(next.AnalysisResult, "Closed window must not receive a result.");
            Assert.AreEqual(before, CoCoStateGraphFixtureCounters.StateFreezeCalls);
        }

        [UnityTest]
        public IEnumerator StateDraftSaveDiscardUndoAndSelectionAreAtomic()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 3 }, "First");
            CoCoStateId second = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(), "Second");
            SelectState(asset, layer, first);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            var controller = WindowController(window);
            CoCoStateGraphStateRecord original = asset.Layers[0].States[0];
            CoCoSerializedId128[] order = asset.Layers[0].States.Select(state => state.StateId).ToArray();
            controller.SetStateDraftName("Renamed");
            Assert.IsTrue(controller.SetStateDraftDescriptor(catalog.StateDescriptors.Single(
                descriptor => descriptor.DescriptorId == AlternateDescriptorId)));
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(controller.BeginStateDraft().Config);
            Assert.AreEqual("First", original.DisplayName);
            Assert.IsInstanceOf<TestStateAuthoringConfig>(original.Config);
            controller.ConfirmStateDraftChanges = () => false;
            controller.SelectState(second);
            Assert.AreEqual(first, controller.Session.SelectedStateId, "Cancel keeps the selected draft.");
            Assert.IsTrue(controller.HasStateDraftChanges);
            Assert.IsTrue(controller.SaveStateDraft());
            Assert.AreEqual("Renamed", original.DisplayName);
            Assert.AreEqual(Serialize(AlternateDescriptorId), original.StateDescriptorId);
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(original.Config);
            yield return null;
            Undo.PerformUndo();
            Assert.AreEqual("First", asset.Layers[0].States[0].DisplayName);
            Assert.AreEqual(Serialize(CoCoStateGraphTestFactory.StateDescriptorId), asset.Layers[0].States[0].StateDescriptorId);
            Assert.AreEqual(3, ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value);
            Undo.PerformRedo();
            Assert.AreEqual("Renamed", asset.Layers[0].States[0].DisplayName);
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(asset.Layers[0].States[0].Config);
            controller.SetStateDraftName("Discard me");
            window.DiscardChanges();
            Assert.AreEqual("Renamed", controller.BeginStateDraft().DisplayName);
            Assert.IsFalse(window.hasUnsavedChanges);
            controller.SetStateDraftName("Survives rebuild");
            window.CreateGUI();
            controller = WindowController(window);
            Assert.IsTrue(controller.HasStateDraftChanges);
            Assert.AreEqual("Survives rebuild", controller.BeginStateDraft().DisplayName);
            controller.ConfirmStateDraftChanges = () => { controller.DiscardStateDraft(); return true; };
            controller.SelectState(second);
            Assert.AreEqual(second, controller.Session.SelectedStateId);
            Assert.AreEqual("Renamed", asset.Layers[0].States[0].DisplayName);
            CollectionAssert.AreEqual(order, asset.Layers[0].States.Select(state => state.StateId).ToArray());
        }

        private static CoCoStateGraphEditorController WindowController(CoCoStateGraphEditorWindow window) =>
            (CoCoStateGraphEditorController)typeof(CoCoStateGraphEditorWindow)
                .GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

        [UnityTest]
        public IEnumerator SearchAndConfigChangesPreserveLiveControls()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId stateId = AddState(
                asset,
                layerId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 7 },
                "Idle");
            SelectState(asset, layerId, stateId);
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;

            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            CoCoStateGraphEditorController controller =
                ReadPrivateField<CoCoStateGraphEditorController>(
                    window,
                    "controller");
            VisualElement root = CreateWindowPanelHost(window).contentRoot;
            Assert.NotNull(root.panel);
            ToolbarSearchField search = root.Q<ToolbarSearchField>("state-graph-search");
            VisualElement config = root.Q<VisualElement>("state-graph-state-config");
            VisualElement feedback = root.Q<VisualElement>("state-graph-feedback");
            Assert.NotNull(search);
            Assert.NotNull(config);
            Assert.NotNull(feedback);

            search.value = "Idle";
            yield return null;
            TextField searchInput = search.Q<TextField>();
            Assert.NotNull(searchInput);
            search.Focus();
            Focusable focusedBefore = root.focusController.focusedElement;
            Assert.AreSame(search, focusedBefore);
            searchInput.textSelection.SelectRange(
                search.value.Length,
                search.value.Length);
            Assert.AreEqual(4, searchInput.textSelection.cursorIndex);
            Assert.AreEqual(4, searchInput.textSelection.selectIndex);
            controller.SetSearch("Idle pasted");

            Assert.AreSame(focusedBefore, root.focusController.focusedElement);
            Assert.AreEqual(4, searchInput.textSelection.cursorIndex);
            Assert.AreEqual(4, searchInput.textSelection.selectIndex);
            Assert.AreEqual("Idle pasted", controller.Session.SearchText);
            Assert.AreSame(search, root.Q<ToolbarSearchField>("state-graph-search"));
            Assert.AreSame(config, root.Q<VisualElement>("state-graph-state-config"));
            Assert.AreEqual("Idle", search.value);
            search.value = "Idle pasted";
            search.value = "Idle";
            Assert.AreEqual("Idle", controller.Session.SearchText);
            Assert.AreSame(search, root.Q<ToolbarSearchField>("state-graph-search"));
            Assert.AreEqual("Idle", search.value);

            using (SerializedPropertyChangeEvent evt = SerializedPropertyChangeEvent.GetPooled())
            {
                config.SendEvent(evt);
            }

            Assert.AreSame(config, root.Q<VisualElement>("state-graph-state-config"));
            Assert.AreSame(feedback, root.Q<VisualElement>("state-graph-feedback"));

            Click(FindButton(root, "Analyze"));
            Assert.AreSame(config, root.Q<VisualElement>("state-graph-state-config"));
            Assert.AreSame(feedback, root.Q<VisualElement>("state-graph-feedback"));
            Assert.IsTrue(ContainsLabel(feedback, "Compilation succeeded."));
        }

        [UnityTest]
        public IEnumerator AddDescriptorChoiceSurvivesSelectionAndSelectedLogicChanges()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoStateCatalog();
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId original = AddState(
                asset,
                layerId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                "Original");
            SelectState(asset, layerId, original);
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            VisualElement root = CreateWindowPanelHost(window).contentRoot;
            yield return null;
            Assert.NotNull(root.panel);
            Click(FindButton(root, "Original"));

            var controller = WindowController(window);
            controller.SelectState(default);
            PopupField<string> addPopup =
                root.Q<PopupField<string>>("add-state-descriptor");
            Assert.NotNull(addPopup);
            Assert.IsNull(root.Q<PopupField<string>>("selected-state-descriptor"));
            string alternateLabel = addPopup.choices.Single(label =>
                label.Contains(nameof(RuntimeFixtureStateLogic)));
            string originalLabel = addPopup.choices.Single(label => label.Contains(nameof(TestStateLogic)));
            addPopup.value = alternateLabel;
            controller.SelectState(original);
            PopupField<string> selectedPopup = root.Q<PopupField<string>>("selected-state-descriptor");
            Assert.NotNull(selectedPopup);
            Assert.IsNull(root.Q<PopupField<string>>("add-state-descriptor"));
            Assert.AreEqual(originalLabel, selectedPopup.value);

            controller.SelectState(default);
            Click(FindButton(root, "Add State Here"));
            CoCoStateGraphStateRecord added = asset.Layers[0].States.Single(state =>
                state.DisplayName == "State");
            Assert.AreEqual(Serialize(AlternateDescriptorId), added.StateDescriptorId);
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(added.Config);

            selectedPopup = root.Q<PopupField<string>>("selected-state-descriptor");
            selectedPopup.value = originalLabel;
            Click(root.Q<Button>("state-graph-save-state"));
            added = asset.Layers[0].States.Single(state => state.StateId == added.StateId);
            Assert.AreEqual(Serialize(CoCoStateGraphTestFactory.StateDescriptorId), added.StateDescriptorId);
            Assert.IsInstanceOf<TestStateAuthoringConfig>(added.Config);

            selectedPopup = root.Q<PopupField<string>>("selected-state-descriptor");
            selectedPopup.value = originalLabel;
            Click(FindButton(root, "Create child State"));
            CoCoStateGraphStateRecord child = asset.Layers[0].States.Single(state =>
                state.ParentStateId == added.StateId);
            Assert.AreEqual(Serialize(CoCoStateGraphTestFactory.StateDescriptorId), child.StateDescriptorId);
            Assert.IsInstanceOf<TestStateAuthoringConfig>(child.Config);

            controller.SelectState(default);
            Click(FindButton(root, "Add State Here"));
            CoCoStateGraphStateRecord secondAdded = asset.Layers[0].States.Last(state =>
                !state.ParentStateId.IsValid && state.StateId != Serialize(original));
            Assert.AreEqual(Serialize(AlternateDescriptorId), secondAdded.StateDescriptorId);
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(secondAdded.Config);

            int stateCountBeforeContextAdd = asset.Layers[0].States.Count;
            Assert.IsTrue(window.TryAddStateAtCanvasPosition(new Vector2(640f, 420f)));
            Assert.AreEqual(stateCountBeforeContextAdd + 1, asset.Layers[0].States.Count);
            CoCoStateGraphStateRecord contextAdded = asset.Layers[0].States.Last();
            Assert.AreEqual(Serialize(AlternateDescriptorId), contextAdded.StateDescriptorId);
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(contextAdded.Config);
        }

        [UnityTest]
        public IEnumerator StateDragCaptureHandlesWrongPointerCancelCaptureOutAndSingleCommit()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId stateId = AddState(
                asset,
                layerId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(),
                "Idle");
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                stateId,
                new Vector2(40f, 60f),
                out string positionFailure), positionFailure);
            Undo.ClearAll();

            var controller = new CoCoStateGraphEditorController(asset);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            controllers.Add(controller);
            canvases.Add(canvas);
            InteractionPanelHost host = CreatePanelHost(canvas);
            yield return null;
            Assert.NotNull(canvas.panel);

            VisualElement card = canvas.Q<VisualElement>("state-card");
            Vector2 start = card.worldBound.center;
            SendDown(card, 5, 0, start);
            Assert.IsTrue(card.HasPointerCapture(5));
            SendMove(card, 6, start + new Vector2(80f, 30f));
            Assert.AreEqual(40f, card.style.left.value.value);
            SendMove(card, 5, start + new Vector2(80f, 30f));
            Assert.AreEqual(120f, card.style.left.value.value);
            SendUp(card, 6, 0, start + new Vector2(80f, 30f));
            Assert.IsTrue(card.HasPointerCapture(5));
            SendCancel(card, 5, start + new Vector2(80f, 30f));
            Assert.IsFalse(card.HasPointerCapture(5));
            Assert.AreEqual(40f, card.style.left.value.value);
            AssertPosition(asset, stateId, new Vector2(40f, 60f));

            SendDown(card, 7, 0, start);
            SendMove(card, 7, start + new Vector2(30f, 20f));
            SendCaptureOut(card, host.rootVisualElement, 7);
            Assert.IsFalse(card.HasPointerCapture(7));
            Assert.AreEqual(40f, card.style.left.value.value);
            AssertPosition(asset, stateId, new Vector2(40f, 60f));

            SendDown(card, 8, 0, start);
            SendMove(card, 8, start + new Vector2(50f, 25f));
            Vector2 outsidePanel = OutsidePanel(host.rootVisualElement);
            SendUp(host.rootVisualElement, 8, 0, outsidePanel);
            yield return null;
            Assert.IsFalse(card.HasPointerCapture(8));
            AssertPosition(asset, stateId, new Vector2(90f, 85f));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            AssertPosition(asset, stateId, new Vector2(40f, 60f));
            Undo.PerformUndo();
            AssertPosition(asset, stateId, new Vector2(40f, 60f));

        }

        [UnityTest]
        public IEnumerator WheelZoomKeepsThePointUnderThePointerFixed()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId stateId = AddState(asset, layerId,
                CoCoStateGraphTestFactory.StateDescriptorId, new TestStateAuthoringConfig(), "Idle");
            Vector2 statePosition = new Vector2(200f, 140f);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset, layerId, stateId, statePosition, out _));
            var controller = new CoCoStateGraphEditorController(asset);
            controllers.Add(controller);
            controller.Session.SetCanvasView(layerId, default,
                new CoCoStateGraphCanvasView(new Vector2(75f, -25f), 1.4f));
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            canvases.Add(canvas);
            canvas.style.marginLeft = 67f;
            canvas.style.marginTop = 43f;
            CreatePanelHost(canvas);
            yield return null;

            Assert.That(canvas.worldBound.x, Is.GreaterThan(0f));
            Assert.That(canvas.worldBound.y, Is.GreaterThan(0f));
            VisualElement card = canvas.Q<VisualElement>("state-card");
            VisualElement content = canvas.Q<VisualElement>("state-graph-canvas-content");
            var cardPoint = new Vector2(45f, 30f);
            var blankPoint = new Vector2(480f, 260f);
            foreach (bool overCard in new[] { true, false })
            {
                foreach (float delta in new[] { -1f, 1f })
                {
                    Vector2 pointer = overCard ? card.LocalToWorld(cardPoint) : content.LocalToWorld(blankPoint);
                    float previousZoom = controller.Session.GetCanvasView(layerId, default).Zoom;
                    SendWheel(overCard ? card : canvas, pointer, delta);
                    yield return null;
                    Vector2 actual = overCard ? card.LocalToWorld(cardPoint) : content.LocalToWorld(blankPoint);
                    Assert.That(Vector2.Distance(pointer, actual), Is.LessThan(0.01f),
                        $"The graph point moved while zooming {(overCard ? "over a State" : "over empty canvas")}.");
                    float zoom = controller.Session.GetCanvasView(layerId, default).Zoom;
                    Assert.That(delta < 0f ? zoom > previousZoom : zoom < previousZoom, Is.True);
                }
            }

            foreach (float limit in new[] { 0.25f, 2f })
            {
                Vector2 pan = new Vector2(55f, -35f);
                controller.Session.SetCanvasView(layerId, default, new CoCoStateGraphCanvasView(pan, limit));
                canvas.Refresh();
                yield return null;
                SendWheel(canvas, canvas.worldBound.center, limit == 2f ? -1f : 1f);
                CoCoStateGraphCanvasView view = controller.Session.GetCanvasView(layerId, default);
                Assert.AreEqual(limit, view.Zoom);
                Assert.AreEqual(pan, view.Pan, "Clamped wheel zoom must not drift the canvas.");
            }

            AssertPosition(asset, stateId, statePosition);
        }

        [UnityTest]
        public IEnumerator TransitionAndPanCaptureCancelWithoutCommittingAndCompleteOnce()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(
                asset,
                layerId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(),
                "First");
            CoCoStateId second = AddState(
                asset,
                layerId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig(),
                "Second");
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset, layerId, first, new Vector2(40f, 60f), out _));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset, layerId, second, new Vector2(300f, 60f), out _));

            var controller = new CoCoStateGraphEditorController(asset);
            controller.Session.SetCanvasView(layerId, default, new CoCoStateGraphCanvasView(
                new Vector2(10f, 20f), 1f));
            controller.Session.Save();
            var canvas = new CoCoStateGraphEditorCanvas(controller);
            controllers.Add(controller);
            canvases.Add(canvas);
            InteractionPanelHost host = CreatePanelHost(canvas);
            yield return null;
            Assert.NotNull(canvas.panel);

            // 维护者反馈：连接点已移除，改为右键从源 State 卡拖拽建 Transition。
            VisualElement sourceCard = FirstSourceCard(canvas);
            Vector2 source = sourceCard.worldBound.center;
            Vector2 target = canvas.worldBound.position + new Vector2(10f, 20f) + new Vector2(320f, 80f);
            SendDown(sourceCard, 9, 1, source);
            Assert.IsTrue(canvas.HasPointerCapture(9));
            SendUp(canvas, 10, 0, target);
            Assert.IsTrue(canvas.HasPointerCapture(9));
            Assert.AreEqual(0, asset.Layers[0].Transitions.Count);
            SendCancel(canvas, 9, target);
            Assert.IsFalse(canvas.HasPointerCapture(9));
            Assert.AreEqual(0, asset.Layers[0].Transitions.Count);

            sourceCard = FirstSourceCard(canvas);
            SendDown(sourceCard, 11, 1, source);
            SendCaptureOut(canvas, host.rootVisualElement, 11);
            Assert.IsFalse(canvas.HasPointerCapture(11));
            Assert.AreEqual(0, asset.Layers[0].Transitions.Count);

            sourceCard = FirstSourceCard(canvas);
            SendDown(sourceCard, 12, 1, source);
            Vector2 outsidePanel = OutsidePanel(host.rootVisualElement);
            SendUp(host.rootVisualElement, 12, 0, outsidePanel);
            Assert.IsFalse(canvas.HasPointerCapture(12));
            Assert.AreEqual(0, asset.Layers[0].Transitions.Count);

            sourceCard = FirstSourceCard(canvas);
            SendDown(sourceCard, 13, 1, source);
            SendMove(canvas, 13, target);
            SendUp(canvas, 13, 0, target);
            yield return null;
            Assert.AreEqual(1, asset.Layers[0].Transitions.Count);
            SendUp(canvas, 13, 0, target);
            Assert.AreEqual(1, asset.Layers[0].Transitions.Count);

            CoCoStateGraphCanvasView original = controller.Session.GetCanvasView(layerId, default);
            Assert.That(canvas.worldBound.width, Is.GreaterThan(0f));
            Assert.That(canvas.worldBound.height, Is.GreaterThan(0f));
            Vector2 panStart = canvas.worldBound.center;
            canvas.Query<VisualElement>("state-card").ForEach(card =>
                Assert.IsFalse(card.worldBound.Contains(panStart)));
            int mousePointerId = UnityEngine.UIElements.PointerId.mousePointerId;
            int observedButton = -1;
            int observedPointerId = -1;
            canvas.RegisterCallback<PointerDownEvent>(evt =>
            {
                observedButton = evt.button;
                observedPointerId = evt.pointerId;
            }, TrickleDown.TrickleDown);
            SendDown(
                canvas,
                mousePointerId,
                2,
                panStart,
                UnityEngine.UIElements.PointerType.mouse);
            Assert.AreEqual(2, observedButton);
            Assert.AreEqual(mousePointerId, observedPointerId);
            Assert.IsTrue(ReadPrivateField<bool>(canvas, "panning"));
            Assert.AreEqual(mousePointerId, ReadPrivateField<int>(canvas, "panPointerId"));
            Assert.IsTrue(canvas.HasPointerCapture(mousePointerId));
            SendMove(
                canvas,
                mousePointerId,
                panStart + new Vector2(50f, 25f),
                button: 2,
                pointerType: UnityEngine.UIElements.PointerType.mouse);
            SendUp(canvas, 14, 2, panStart + new Vector2(50f, 25f));
            Assert.IsTrue(canvas.HasPointerCapture(mousePointerId));
            SendWheel(canvas, panStart, -1f);
            Assert.AreNotEqual(original.Zoom, controller.Session.GetCanvasView(layerId, default).Zoom);
            SendCancel(
                canvas,
                mousePointerId,
                panStart + new Vector2(50f, 25f),
                button: 2,
                pointerType: UnityEngine.UIElements.PointerType.mouse);
            Assert.AreEqual(original.Pan, controller.Session.GetCanvasView(layerId, default).Pan);
            Assert.AreEqual(original.Zoom, controller.Session.GetCanvasView(layerId, default).Zoom);

            SendDown(
                canvas,
                mousePointerId,
                2,
                panStart,
                UnityEngine.UIElements.PointerType.mouse);
            SendMove(
                canvas,
                mousePointerId,
                panStart + new Vector2(20f, 30f),
                button: 2,
                pointerType: UnityEngine.UIElements.PointerType.mouse);
            SendCaptureOut(canvas, host.rootVisualElement, mousePointerId);
            Assert.AreEqual(original.Pan, controller.Session.GetCanvasView(layerId, default).Pan);
            Assert.AreEqual(original.Zoom, controller.Session.GetCanvasView(layerId, default).Zoom);

            SendDown(
                canvas,
                mousePointerId,
                2,
                panStart,
                UnityEngine.UIElements.PointerType.mouse);
            SendMove(
                canvas,
                mousePointerId,
                panStart + new Vector2(30f, 40f),
                button: 2,
                pointerType: UnityEngine.UIElements.PointerType.mouse);
            SendUp(
                host.rootVisualElement,
                mousePointerId,
                2,
                outsidePanel,
                UnityEngine.UIElements.PointerType.mouse);
            Assert.IsFalse(canvas.HasPointerCapture(mousePointerId));
            Assert.IsFalse(ReadPrivateField<bool>(canvas, "panning"));
            Assert.AreEqual(0, ReadPrivateField<int>(canvas, "panPointerId"));
            Assert.AreEqual(original.Pan + new Vector2(30f, 40f),
                controller.Session.GetCanvasView(layerId, default).Pan);
            SendUp(
                host.rootVisualElement,
                mousePointerId,
                2,
                outsidePanel,
                UnityEngine.UIElements.PointerType.mouse);
            Assert.AreEqual(original.Pan + new Vector2(30f, 40f),
                controller.Session.GetCanvasView(layerId, default).Pan);

        }

        private CoCoStateGraphAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            assets.Add(asset);
            return asset;
        }

        private CoCoStateGraphEditorWindow CreateStateGraphWindow(CoCoStateGraphAsset asset)
        {
            var window = ScriptableObject.CreateInstance<CoCoStateGraphEditorWindow>();
            windows.Add(window);
            var serializedWindow = new SerializedObject(window);
            serializedWindow.FindProperty("asset").objectReferenceValue = asset;
            serializedWindow.ApplyModifiedPropertiesWithoutUndo();
            window.CreateGUI();
            return window;
        }

        private InteractionPanelHost CreatePanelHost(VisualElement content)
        {
            // Editor binding and pointer capture need an Editor panel, not a runtime UIDocument.
            var window = ScriptableObject.CreateInstance<CoCoStateGraphTestPanelWindow>();
            windows.Add(window);
            window.position = new Rect(80f, 80f, 900f, 600f);
            window.Show();
            window.rootVisualElement.Add(content);
            window.Repaint();
            return new InteractionPanelHost(window.rootVisualElement, content);
        }

        private InteractionPanelHost CreateWindowPanelHost(CoCoStateGraphEditorWindow window)
        {
            var content = new VisualElement { name = "state-graph-window-test-content" };
            content.style.flexGrow = 1f;
            while (window.rootVisualElement.childCount > 0)
            {
                content.Add(window.rootVisualElement[0]);
            }

            return CreatePanelHost(content);
        }

        private static CoCoStateId AddState(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoStateDescriptorId descriptorId,
            CoCoStateConfig config,
            string name)
        {
            return CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                config,
                name);
        }

        private static void SelectState(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoStateId stateId)
        {
            CoCoStateGraphEditorSessionState session = CoCoStateGraphEditorSessionState.Load(asset);
            session.SelectedLayerId = layerId;
            session.SelectedStateId = stateId;
            session.Save();
        }

        private static Button FindButton(VisualElement root, string text)
        {
            Button result = null;
            root.Query<Button>().ForEach(button =>
            {
                if (result == null && button.text == text)
                {
                    result = button;
                }
            });
            Assert.NotNull(result, $"Button '{text}' was not found.");
            return result;
        }

        private static bool ContainsLabel(VisualElement root, string text)
        {
            bool found = false;
            root.Query<Label>().ForEach(label => found |= label.text == text);
            return found;
        }

        private static VisualElement FirstSourceCard(VisualElement root)
        {
            VisualElement card = root.Q<VisualElement>("state-card");
            Assert.NotNull(card);
            return card;
        }

        private static void Click(Button target)
        {
            Assert.NotNull(InvokeClickable);
            InvokeClickable.Invoke(target.clickable, new object[] { null });
        }

        private static void SendDown(
            VisualElement target,
            int pointerId,
            int button,
            Vector2 position,
            string pointerType = null)
        {
            using (PointerDownEvent evt = PointerDownEvent.GetPooled(
                       new TestPointerEvent(
                           pointerId,
                           button,
                           position,
                           pressed: true,
                           pointerType: pointerType)))
            {
                target.SendEvent(evt);
            }
        }

        private static void SendMove(
            VisualElement target,
            int pointerId,
            Vector2 position,
            int button = 0,
            string pointerType = null)
        {
            using (PointerMoveEvent evt = PointerMoveEvent.GetPooled(
                       new TestPointerEvent(
                           pointerId,
                           button,
                           position,
                           pressed: true,
                           pointerType: pointerType)))
            {
                target.SendEvent(evt);
            }
        }

        private static void SendUp(
            VisualElement target,
            int pointerId,
            int button,
            Vector2 position,
            string pointerType = null)
        {
            using (PointerUpEvent evt = PointerUpEvent.GetPooled(
                       new TestPointerEvent(
                           pointerId,
                           button,
                           position,
                           pressed: false,
                           pointerType: pointerType)))
            {
                target.SendEvent(evt);
            }
        }

        private static void SendCancel(
            VisualElement target,
            int pointerId,
            Vector2 position,
            int button = 0,
            string pointerType = null)
        {
            using (PointerCancelEvent evt = PointerCancelEvent.GetPooled(
                       new TestPointerEvent(
                           pointerId,
                           button,
                           position,
                           pressed: false,
                           pointerType: pointerType)))
            {
                target.SendEvent(evt);
            }
        }

        private static void SendCaptureOut(
            VisualElement target,
            VisualElement newCapture,
            int pointerId)
        {
            Assert.IsTrue(target.HasPointerCapture(pointerId));
            newCapture.CapturePointer(pointerId);
            SendMove(
                newCapture,
                pointerId,
                newCapture.worldBound.center,
                button: pointerId == UnityEngine.UIElements.PointerId.mousePointerId ? 2 : 0,
                pointerType: pointerId == UnityEngine.UIElements.PointerId.mousePointerId
                    ? UnityEngine.UIElements.PointerType.mouse
                    : UnityEngine.UIElements.PointerType.touch);
            Assert.IsFalse(target.HasPointerCapture(pointerId));
            if (newCapture.HasPointerCapture(pointerId))
            {
                newCapture.ReleasePointer(pointerId);
            }
        }

        private static void SendWheel(VisualElement target, Vector2 position, float deltaY)
        {
            var systemEvent = new Event
            {
                type = EventType.ScrollWheel,
                mousePosition = position,
                delta = new Vector2(0f, deltaY)
            };
            using (WheelEvent evt = WheelEvent.GetPooled(systemEvent))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static void AssertPosition(
            CoCoStateGraphAsset asset,
            CoCoStateId stateId,
            Vector2 expected)
        {
            Assert.IsTrue(asset.EditorLayout.TryGetPosition(Serialize(stateId), out Vector2 actual));
            Assert.AreEqual(expected, actual);
        }

        private static Vector2 OutsidePanel(VisualElement root) =>
            root.worldBound.max + new Vector2(100f, 100f);

        [UnityTest]
        public IEnumerator StandardConfigDraftCommitsWithoutRebuildingAndAnalyzesOnceOnSave()
        {
            var original = new CoCoFieldConfig();
            original.Set("speed", 1f);
            CoCoStateGraphEditorWindow window = CreateStandardConfigWindow(original, out var asset, out var root);
            var controller = WindowController(window);
            yield return null;
            controller.Analyze();
            Assert.IsTrue(controller.AnalysisResult.Succeeded);
            ulong before = controller.AnalysisResult.ContentFingerprint;
            VisualElement configHost = root.Q<VisualElement>("state-graph-state-config-host");
            IMGUIContainer input = configHost.Q<IMGUIContainer>();
            Assert.NotNull(input, "The standard Config must use the real field editor.");
            Assert.AreNotSame(original, controller.BeginStateDraft().Config);
            Assert.AreNotSame(original.Values, ((CoCoFieldConfig)controller.BeginStateDraft().Config).Values);
            input.Focus();
            yield return null;
            Assert.AreSame(input, root.focusController.focusedElement);
            int analyses = 0;
            controller.Changed += invalidation =>
            {
                if (invalidation == CoCoStateGraphEditorInvalidation.All) analyses++;
            };

            var edited = new CoCoFieldConfig { Values = original.Values.ToArray() };
            edited.Set("speed", 2.5f);
            CommitStandardConfig(window, edited);
            yield return null;
            Assert.AreEqual(0, analyses, "Committing an input must only update the draft.");
            Assert.IsTrue(controller.HasStateDraftChanges);
            Assert.AreEqual(1f, ReadStandardConfig(asset).speed);
            Assert.AreSame(input, configHost.Q<IMGUIContainer>());
            Assert.AreSame(configHost, root.Q<VisualElement>("state-graph-state-config-host"));
            Assert.AreSame(input, root.focusController.focusedElement);

            window.SaveChanges();
            Assert.AreEqual(2.5f, ReadStandardConfig(asset).speed);
            Assert.AreEqual(1, analyses);
            Assert.IsFalse(controller.HasStateDraftChanges);
            Assert.IsTrue(controller.AnalysisResult.Succeeded);
            Assert.AreNotEqual(before, controller.AnalysisResult.ContentFingerprint);
        }

        [UnityTest]
        public IEnumerator StandardCollectionDraftDiscardSaveAndUndoKeepTheAssetIsolated()
        {
            var original = new CoCoFieldConfig();
            original.Set("windows", new[] { new StandardConfigWindow { start = 1f }, new StandardConfigWindow { start = 2f } });
            original.Set("samples", new List<float> { 3f, 4f });
            string[] saved = original.Values.ToArray();
            CoCoStateGraphEditorWindow window = CreateStandardConfigWindow(original, out var asset, out _);
            var controller = WindowController(window);
            yield return null;

            var edited = new CoCoFieldConfig { Values = saved.ToArray() };
            edited.Set("windows", new[] { new StandardConfigWindow { start = 2f }, new StandardConfigWindow { start = 0.25f } });
            edited.Set("samples", new List<float> { 4f, 5f, 3f });
            CommitStandardConfig(window, edited);
            CollectionAssert.AreEqual(saved, ((CoCoFieldConfig)asset.Layers[0].States[0].Config).Values);
            Assert.AreEqual(0.25f, ReadStandardConfig(controller.StateEditingAsset).windows[1].start);
            Assert.AreNotSame(((CoCoFieldConfig)controller.StateEditingAsset.Layers[0].States[0].Config).Values,
                ((CoCoFieldConfig)asset.Layers[0].States[0].Config).Values);
            window.DiscardChanges();
            CollectionAssert.AreEqual(saved, ((CoCoFieldConfig)asset.Layers[0].States[0].Config).Values);
            Assert.AreEqual(1f, ReadStandardConfig(controller.StateEditingAsset).windows[0].start);
            Assert.IsFalse(controller.HasStateDraftChanges);

            CommitStandardConfig(window, edited);
            window.SaveChanges();
            Assert.AreEqual(2f, ReadStandardConfig(asset).windows[0].start);
            CollectionAssert.AreEqual(new[] { 4f, 5f, 3f }, ReadStandardConfig(asset).samples);
            yield return null;
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            CollectionAssert.AreEqual(saved, ((CoCoFieldConfig)asset.Layers[0].States[0].Config).Values);
            Undo.PerformRedo();
            Assert.AreEqual(0.25f, ReadStandardConfig(asset).windows[1].start);
            CollectionAssert.AreEqual(new[] { 4f, 5f, 3f }, ReadStandardConfig(asset).samples);
        }

        private CoCoStateGraphEditorWindow CreateStandardConfigWindow(CoCoFieldConfig config,
            out CoCoStateGraphAsset asset, out VisualElement root)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(CoCoStateGraphTestFactory.StateDescriptorId, 1U,
                new CoCoFieldConfig.Freezer(typeof(StandardConfigDraftLogic)),
                new CoCoStateRuntimeRegistration<StandardConfigDraftLogic, CoCoFieldConfigSchema,
                    TestActivationMemory>(CoCoFieldConfigSchema.Schema), null, null, null,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(out CoCoGraphDescriptorCatalog catalog, out diagnostic), diagnostic.Message);
            ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;
            asset = CreateAsset();
            CoCoLayerId layer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId state = AddState(asset, layer, CoCoStateGraphTestFactory.StateDescriptorId, config, "Standard");
            SelectState(asset, layer, state);
            CoCoStateGraphEditorWindow window = CreateStateGraphWindow(asset);
            window.position = new Rect(80f, 80f, 1200f, 900f);
            window.Show();
            root = window.rootVisualElement;
            return window;
        }

        private static void CommitStandardConfig(CoCoStateGraphEditorWindow window, CoCoFieldConfig config)
        {
            var method = typeof(CoCoStateGraphEditorWindow).GetMethod("SaveFieldConfig",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(window, new object[] { WindowController(window).BeginStateDraft(), config });
        }

        private static StandardConfigDraftLogic ReadStandardConfig(CoCoStateGraphAsset asset)
        {
            var logic = new StandardConfigDraftLogic();
            ((CoCoFieldConfig)asset.Layers[0].States[0].Config).Apply(logic);
            return logic;
        }

        private static CoCoGraphDescriptorCatalog CreateTwoStateCatalog()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                CoCoStateGraphTestFactory.StateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic originalDiagnostic), originalDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                AlternateDescriptorId,
                1U,
                new RuntimeFixtureStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    RuntimeFixtureStateLogic,
                    RuntimeFixtureStateConfigSchema,
                    RuntimeFixtureMemory>(RuntimeFixtureSchemas.State),
                null,
                null,
                null,
                out CoCoDiagnostic alternateDiagnostic), alternateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            ClearAuthorAssemblyRootsForInjectedUiCatalog(catalog);
            return catalog;
        }

        internal static void ClearAuthorAssemblyRootsForInjectedUiCatalog(
            CoCoGraphDescriptorCatalog catalog)
        {
            FieldInfo field = typeof(CoCoGraphDescriptorCatalog).GetField(
                "_authorAssemblyRootNames",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(catalog, Array.Empty<string>());
            Assert.AreEqual(0, catalog.AuthorAssemblyRootNames.Count);
        }

        private static T ReadPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field.GetValue(target);
        }

        private static CoCoStateDescriptorId AlternateDescriptorId =>
            CoCoStateGraphTestFactory.CreateStateDescriptorId(2UL);

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private sealed class InteractionPanelHost
        {
            internal InteractionPanelHost(
                VisualElement rootVisualElement,
                VisualElement contentRoot)
            {
                this.rootVisualElement = rootVisualElement;
                this.contentRoot = contentRoot;
            }

            internal VisualElement rootVisualElement { get; }
            internal VisualElement contentRoot { get; }
        }

        private sealed class TestPointerEvent : IPointerEvent
        {
            internal TestPointerEvent(
                int pointerId,
                int button,
                Vector2 position,
                bool pressed,
                string pointerType)
            {
                this.pointerId = pointerId;
                this.button = button;
                pressedButtons = pressed ? 1 << button : 0;
                this.position = position;
                localPosition = position;
                this.pointerType = pointerType ?? UnityEngine.UIElements.PointerType.touch;
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
            public int clickCount => 1;
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

    internal sealed class CoCoStateGraphTestPanelWindow : EditorWindow { }
}
