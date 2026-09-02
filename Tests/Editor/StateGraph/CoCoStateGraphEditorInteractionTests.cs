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
        private readonly List<GameObject> panelObjects = new List<GameObject>();
        private readonly List<PanelSettings> panelSettings = new List<PanelSettings>();
        private readonly List<CoCoStateGraphEditorCanvas> canvases =
            new List<CoCoStateGraphEditorCanvas>();
        private readonly List<CoCoStateGraphEditorController> controllers =
            new List<CoCoStateGraphEditorController>();

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
                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
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
            PropertyField config = root.Q<PropertyField>("state-graph-state-config");
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
            Assert.AreSame(config, root.Q<PropertyField>("state-graph-state-config"));
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

            Assert.AreSame(config, root.Q<PropertyField>("state-graph-state-config"));
            Assert.AreSame(feedback, root.Q<VisualElement>("state-graph-feedback"));

            Click(FindButton(root, "Analyze"));
            Assert.AreSame(config, root.Q<PropertyField>("state-graph-state-config"));
            Assert.AreSame(feedback, root.Q<VisualElement>("state-graph-feedback"));
            Assert.IsTrue(ContainsLabel(feedback, "Compilation succeeded."));
        }

        [UnityTest]
        public IEnumerator AddAndSelectedDescriptorControlsDoNotOverwriteEachOther()
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

            PopupField<string> addPopup =
                root.Q<PopupField<string>>("add-state-descriptor");
            PopupField<string> selectedPopup =
                root.Q<PopupField<string>>("selected-state-descriptor");
            Assert.NotNull(addPopup);
            Assert.NotNull(selectedPopup);
            string alternateLabel = addPopup.choices.Single(label =>
                label.Contains(nameof(RuntimeFixtureStateLogic)));
            string originalLabel = addPopup.choices.Single(label => label.Contains(nameof(TestStateLogic)));
            addPopup.value = alternateLabel;
            Assert.AreEqual(originalLabel, selectedPopup.value);

            Click(FindButton(root, "Add State Here"));
            CoCoStateGraphStateRecord added = asset.Layers[0].States.Single(state =>
                state.DisplayName == "State");
            Assert.AreEqual(Serialize(AlternateDescriptorId), added.StateDescriptorId);
            Assert.IsInstanceOf<RuntimeFixtureStateAuthoringConfig>(added.Config);

            selectedPopup = root.Q<PopupField<string>>("selected-state-descriptor");
            selectedPopup.value = originalLabel;
            Click(FindButton(root, "Set Descriptor"));
            added = asset.Layers[0].States.Single(state => state.StateId == added.StateId);
            Assert.AreEqual(Serialize(CoCoStateGraphTestFactory.StateDescriptorId), added.StateDescriptorId);
            Assert.IsInstanceOf<TestStateAuthoringConfig>(added.Config);

            selectedPopup = root.Q<PopupField<string>>("selected-state-descriptor");
            selectedPopup.value = originalLabel;
            Click(FindButton(root, "Add Child State"));
            CoCoStateGraphStateRecord child = asset.Layers[0].States.Single(state =>
                state.ParentStateId == added.StateId);
            Assert.AreEqual(Serialize(CoCoStateGraphTestFactory.StateDescriptorId), child.StateDescriptorId);
            Assert.IsInstanceOf<TestStateAuthoringConfig>(child.Config);

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
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            panelSettings.Add(settings);

            var panelObject = new GameObject("CoCoFlow StateGraph Interaction Panel");
            panelObject.SetActive(false);
            UIDocument document = panelObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            panelObject.SetActive(true);
            panelObjects.Add(panelObject);
            document.rootVisualElement.style.width = 900f;
            document.rootVisualElement.style.height = 600f;
            document.rootVisualElement.Add(content);
            return new InteractionPanelHost(document.rootVisualElement, content);
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

        private static void ClearAuthorAssemblyRootsForInjectedUiCatalog(
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

}
