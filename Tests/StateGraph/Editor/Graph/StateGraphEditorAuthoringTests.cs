using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.Core;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateGraphEditorAuthoringTests
    {
        private readonly List<StateGraphAsset> assets = new List<StateGraphAsset>();

        [TearDown]
        public void TearDown()
        {
            StateGraphEditorCatalogProvider.Provider = null;
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

        [Test]
        public void EditorLayoutDoesNotAffectFingerprintsAndRemapsWithCopyAndUndo()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId stateId = AddState(asset, layerId, default, "Idle");
            GraphAssetSnapshot baseline = Snapshot(asset, catalog);
            Assert.IsTrue(asset.EditorLayout.TryGetPosition(Serialize(stateId), out Vector2 original));

            Undo.ClearAll();
            Assert.IsTrue(StateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                stateId,
                new Vector2(321f, 123f),
                out string failure), failure);
            Undo.FlushUndoRecordObjects();
            GraphAssetSnapshot moved = Snapshot(asset, catalog);

            Assert.AreEqual(baseline.ContentFingerprint, moved.ContentFingerprint);
            Assert.AreEqual(baseline.CacheFingerprint, moved.CacheFingerprint);
            Undo.PerformUndo();
            Assert.IsTrue(asset.EditorLayout.TryGetPosition(Serialize(stateId), out Vector2 restored));
            Assert.AreEqual(original, restored);

            StateGraphAsset copy = UnityEngine.Object.Instantiate(asset);
            assets.Add(copy);
            copy.RegenerateTopologyIdsForAssetCopy("copy-guid");
            SerializedId128 copiedStateId = copy.Layers[0].States[0].StateId;
            Assert.AreNotEqual(Serialize(stateId), copiedStateId);
            Assert.IsTrue(copy.EditorLayout.TryGetPosition(copiedStateId, out Vector2 copiedPosition));
            Assert.AreEqual(original, copiedPosition);
            Assert.IsFalse(copy.EditorLayout.TryGetPosition(Serialize(stateId), out _));
        }

        [Test]
        public void StrictTransitionCommandsRejectInvalidEndpointsWindowsAndPrioritiesWithoutMutation()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId first = AddState(asset, layerId, default, "First");
            StateId second = AddState(asset, layerId, default, "Second");
            StateId composite = AddState(asset, layerId, default, "Composite");
            AddState(asset, layerId, composite, "Child");

            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                second,
                5,
                TransitionWindow.Always,
                catalog,
                out _,
                out string failure), failure);
            Assert.AreEqual(1, asset.Layers[0].Transitions.Count);

            Assert.IsFalse(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                second,
                5,
                TransitionWindow.Always,
                catalog,
                out _,
                out _));
            Assert.IsFalse(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                composite,
                second,
                0,
                TransitionWindow.Always,
                catalog,
                out _,
                out _));
            Assert.IsFalse(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                second,
                6,
                default,
                catalog,
                out _,
                out _));

            LayerId otherLayer = StateGraphAuthoringOperations.AddLayer(asset, "Other");
            StateId otherState = AddState(asset, otherLayer, default, "Other");
            Assert.IsFalse(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                otherState,
                6,
                TransitionWindow.Always,
                catalog,
                out _,
                out _));
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.ActionProgress,
                0.5d,
                1d,
                out TransitionWindow progressWindow));
            Assert.IsFalse(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                second,
                first,
                0,
                progressWindow,
                catalog,
                out _,
                out _));
            Assert.AreEqual(1, asset.Layers[0].Transitions.Count);
        }

        [Test]
        public void DeleteSubtreeRequiresInitialReplacementAndRemovesIncidentTransitionsAndLayout()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId first = AddState(asset, layerId, default, "First");
            StateId second = AddState(asset, layerId, default, "Second");
            StateId third = AddState(asset, layerId, default, "Third");
            AddTransition(asset, catalog, layerId, first, second, 0);
            AddTransition(asset, catalog, layerId, third, first, 0);

            Assert.IsFalse(StateGraphAuthoringOperations.TryDeleteStateSubtree(
                asset,
                layerId,
                first,
                default,
                out _,
                out _));
            Assert.AreEqual(3, asset.Layers[0].States.Count);
            Assert.AreEqual(2, asset.Layers[0].Transitions.Count);

            Assert.IsTrue(StateGraphAuthoringOperations.TryDeleteStateSubtree(
                asset,
                layerId,
                first,
                second,
                out StateGraphDeleteImpact impact,
                out string failure), failure);
            Assert.AreEqual(1, impact.StateCount);
            Assert.AreEqual(2, impact.TransitionCount);
            Assert.AreEqual(2, asset.Layers[0].States.Count);
            Assert.AreEqual(0, asset.Layers[0].Transitions.Count);
            Assert.AreEqual(Serialize(second), asset.Layers[0].InitialStateId);
            Assert.IsFalse(asset.EditorLayout.TryGetPosition(Serialize(first), out _));
        }

        [Test]
        public void SameAssetClipboardRemapsIdsKeepsInternalEdgesAndPreservesDescendantLocalPositions()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId root = AddState(asset, layerId, default, "Root");
            StateId child = AddState(asset, layerId, root, "Child");
            AddTransition(asset, catalog, layerId, child, child, 0);
            Assert.IsTrue(StateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                root,
                new Vector2(10f, 20f),
                out _));
            Assert.IsTrue(StateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                child,
                new Vector2(30f, 40f),
                out _));

            Assert.IsTrue(StateGraphAuthoringOperations.TryCaptureSubtree(
                asset,
                layerId,
                root,
                out StateGraphSubtreeClipboard clipboard,
                out string captureFailure), captureFailure);
            using (clipboard)
            {
                Assert.IsTrue(StateGraphAuthoringOperations.TryPasteSubtree(
                    asset,
                    clipboard,
                    layerId,
                    default,
                    new Vector2(400f, 200f),
                    out StateId pastedRoot,
                    out string pasteFailure), pasteFailure);

                StateGraphLayerRecord layer = asset.Layers[0];
                StateGraphStateRecord pastedRootRecord = layer.States.Single(state =>
                    state.StateId == Serialize(pastedRoot));
                StateGraphStateRecord pastedChild = layer.States.Single(state =>
                    state.ParentStateId == pastedRootRecord.StateId);
                Assert.AreNotEqual(Serialize(root), pastedRootRecord.StateId);
                Assert.AreNotEqual(Serialize(child), pastedChild.StateId);
                Assert.AreEqual(4, layer.States.Count);
                Assert.AreEqual(2, layer.Transitions.Count);
                StateGraphTransitionRecord pastedTransition = layer.Transitions.Single(transition =>
                    transition.TransitionId != layer.Transitions[0].TransitionId &&
                    transition.SourceStateId == pastedChild.StateId);
                Assert.AreEqual(pastedChild.StateId, pastedTransition.TargetStateId);
                Assert.IsTrue(asset.EditorLayout.TryGetPosition(
                    pastedRootRecord.StateId,
                    out Vector2 pastedRootPosition));
                Assert.AreEqual(new Vector2(400f, 200f), pastedRootPosition);
                Assert.IsTrue(asset.EditorLayout.TryGetPosition(
                    pastedChild.StateId,
                    out Vector2 pastedChildPosition));
                Assert.AreEqual(new Vector2(30f, 40f), pastedChildPosition);
            }
        }

        [Test]
        public void SimpleAndComboPresetBuildersCreateTheFrozenTopologies()
        {
            GraphDescriptorCatalog simpleCatalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphAsset simple = CreateAsset();
            Assert.IsTrue(StateGraphPresetWizard.TryPopulateSimple(
                simple,
                simpleCatalog,
                StateGraphTestFactory.StateDescriptorId,
                StateGraphTestFactory.StateDescriptorId,
                out string simpleFailure), simpleFailure);
            Assert.AreEqual(1, simple.Layers.Count);
            Assert.AreEqual(2, simple.Layers[0].States.Count);
            Assert.AreEqual(1, simple.Layers[0].Transitions.Count);
            Assert.AreEqual(TransitionWindowMode.Always, simple.Layers[0].Transitions[0].WindowMode);

            GraphDescriptorCatalog comboCatalog =
                StateGraphTestFactory.CreateCatalog(false, providesActionProgress: true);
            StateGraphAsset combo = CreateAsset();
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.ActionProgress,
                0.9d,
                1d,
                out TransitionWindow window));
            Assert.IsTrue(StateGraphPresetWizard.TryPopulateCombo(
                combo,
                comboCatalog,
                StateGraphTestFactory.StateDescriptorId,
                StateGraphTestFactory.StateDescriptorId,
                window,
                out string comboFailure), comboFailure);
            Assert.AreEqual(1, combo.Layers.Count);
            Assert.AreEqual(
                new[] { "Step1", "Step2", "Step3", "Step4", "Exit" },
                combo.Layers[0].States.Select(state => state.DisplayName));
            Assert.AreEqual(4, combo.Layers[0].Transitions.Count);
            for (int index = 0; index < 4; index++)
            {
                Assert.AreEqual(combo.Layers[0].States[index].StateId,
                    combo.Layers[0].Transitions[index].SourceStateId);
                Assert.AreEqual(combo.Layers[0].States[index + 1].StateId,
                    combo.Layers[0].Transitions[index].TargetStateId);
                Assert.AreEqual(TransitionWindowMode.ActionProgress,
                    combo.Layers[0].Transitions[index].WindowMode);
            }
        }

        [Test]
        public void SessionFallsBackToFirstAuthoredLayerRecomputesAnalysisAndRoutesInitialChild()
        {
            var catalogBuilder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(catalogBuilder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic catalogDiagnostic), catalogDiagnostic.Message);
            StateGraphAsset asset = CreateAsset();
            LayerId firstLayer = StateGraphAuthoringOperations.AddLayer(asset, "First");
            LayerId secondLayer = StateGraphAuthoringOperations.AddLayer(asset, "Second");
            StateId root = AddState(asset, firstLayer, default, "Root");
            StateId firstChild = AddState(asset, firstLayer, root, "First Child");
            StateId secondChild = AddState(asset, firstLayer, root, "Second Child");

            StateGraphEditorSessionState session = StateGraphEditorSessionState.Load(asset);
            session.SelectedLayerId = StateGraphTestFactory.CreateLayerId(999UL);
            session.AnalysisRequested = true;
            session.Save();
            StateGraphEditorCatalogProvider.Provider = () => catalog;

            using (var controller = new StateGraphEditorController(asset))
            {
                Assert.AreEqual(firstLayer, controller.Session.SelectedLayerId);
                Assert.IsNotNull(
                    controller.AnalysisResult,
                    $"Catalog: {controller.CatalogStatus}; command: {controller.CommandFailure}");
                controller.SelectState(secondChild);
                Assert.IsTrue(controller.SetSelectedStateInitial());
            }

            Assert.AreEqual(Serialize(secondChild), asset.Layers[0].States.Single(state =>
                state.StateId == Serialize(root)).InitialChildStateId);
            Assert.AreNotEqual(firstLayer, secondLayer);
            Assert.AreNotEqual(firstChild, secondChild);
        }

        [Test]
        public void LegacyDiagnosticSessionRestoresSelectionAndViewportAndStillLocatesStates()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId parent = AddState(asset, layerId, default, "Parent");
            StateId child = AddState(asset, layerId, parent, "Child");
            string sessionKey = "CoCoFlow.StateGraph.Editor." + asset.GraphId;
            SessionState.SetString(sessionKey, "{" +
                $"\"selectedLayerId\":\"{layerId}\",\"selectedStateId\":\"{parent}\"," +
                $"\"canvasCollapsedStateIds\":[\"{parent}\"]," +
                $"\"canvasViews\":[{{\"key\":\"{layerId}:\",\"panX\":12,\"panY\":34,\"zoom\":1.25}}]," +
                $"\"selectedDiagnostic\":{{\"elementKind\":3,\"layerId\":\"{layerId}\",\"stateId\":\"{parent}\"}}" +
                "}");

            try
            {
                using var controller = new StateGraphEditorController(asset);
                StateGraphEditorSessionState session = controller.Session;
                Assert.AreEqual(layerId, session.SelectedLayerId);
                Assert.AreEqual(parent, session.SelectedStateId);
                Assert.IsTrue(session.IsCanvasCollapsed(parent));
                StateGraphCanvasView view = session.GetCanvasView(layerId, default);
                Assert.AreEqual(new Vector2(12f, 34f), view.Pan);
                Assert.AreEqual(1.25f, view.Zoom);

                controller.BeginStateDraft();
                controller.SetStateDraftName("Unsaved");
                controller.ConfirmStateDraftChanges = () => false;
                var location = new GraphDiagnosticLocation(
                    GraphElementKind.State, GraphField.Config, asset.GraphId, layerId, child,
                    default, layerIndex: 0, stateIndex: 1, transitionIndex: -1);
                controller.Locate(location);
                Assert.AreEqual(parent, session.SelectedStateId);
                Assert.IsTrue(controller.HasStateDraftChanges);

                controller.ConfirmStateDraftChanges = () => true;
                controller.Locate(location);
                Assert.AreEqual(child, session.SelectedStateId);
                Assert.IsFalse(session.IsCanvasCollapsed(parent));
                Assert.IsFalse(controller.HasStateDraftChanges);
                Assert.AreEqual(child, StateGraphEditorSessionState.Load(asset).SelectedStateId);
            }
            finally
            {
                SessionState.EraseString(sessionKey);
            }
        }

        [Test]
        public void FlatCanvasDiscardsLegacyDrillRootAndNavigationKeepsLayerRootScope()
        {
            StateGraphAsset asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId root = AddState(asset, layerId, default, "Root");
            StateId child = AddState(asset, layerId, root, "Child");
            StateGraphEditorSessionState legacySession =
                StateGraphEditorSessionState.Load(asset);
            legacySession.SelectedLayerId = layerId;
            legacySession.DrillRootStateId = root;
            legacySession.Save();

            using (var controller = new StateGraphEditorController(asset))
            {
                Assert.IsFalse(controller.Session.DrillRootStateId.IsValid,
                    "D9 flat canvas must discard a legacy nested drill scope");
                CollectionAssert.AreEqual(
                    new[] { Serialize(root) },
                    controller.VisibleStates.Select(state => state.StateId).ToArray());

                controller.NavigateToState(child);

                Assert.AreEqual(child, controller.Session.SelectedStateId);
                Assert.IsFalse(controller.Session.DrillRootStateId.IsValid,
                    "navigating to a nested State must keep the flat canvas at Layer root");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeleteLayerDisclosureMatchesAnyStateBindingAndUndoRestoresIt(bool configured)
        {
            var asset = CreateAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            AddState(asset, layerId, default, "Idle");
            if (configured)
            {
                var catalog = StateGraphTestFactory.CreateCatalog(false, includeAnyState: true);
                catalog.TryGetStateDescriptor(StateGraphTestFactory.AnyStateDescriptorId, out var descriptor);
                Assert.IsTrue(StateGraphAuthoringOperations.TryAddAnyState(asset, layerId, descriptor,
                    new TestStateAuthoringConfig { Value = 7 }, Vector2.zero, out _, out _));
            }
            try
            {
                foreach (var language in new[] { CoCoEditorLanguage.English, CoCoEditorLanguage.SimplifiedChinese })
                {
                    CoCoEditorLocalization.SetLanguage(language);
                    string message = StateGraphEditorWindow.DeleteLayerMessage(asset.Layers[0]);
                    Assert.AreEqual(configured, message.Contains("AnyState"));
                    Assert.AreEqual(configured, message.Contains("Config"));
                    StringAssert.Contains("1", message);
                }
                Undo.ClearAll();
                using (var controller = new StateGraphEditorController(asset))
                {
                    Assert.IsTrue(controller.DeleteLayer());
                    Undo.FlushUndoRecordObjects();
                    Assert.IsEmpty(asset.Layers);
                    Undo.PerformUndo();
                    Assert.AreEqual(1, asset.Layers.Count);
                    Assert.AreEqual(configured, asset.Layers[0].AnyState != null);
                    if (configured)
                    {
                        var any = asset.Layers[0].AnyState;
                        Assert.NotNull(any);
                        Assert.AreEqual(7, ((TestStateAuthoringConfig)any.Config).Value);
                    }
                }
            }
            finally { CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.English); }
        }

        private StateGraphAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            assets.Add(asset);
            return asset;
        }

        private static StateId AddState(
            StateGraphAsset asset,
            LayerId layerId,
            StateId parentStateId,
            string name)
        {
            return StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                parentStateId,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                name);
        }

        private static void AddTransition(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog,
            LayerId layerId,
            StateId source,
            StateId target,
            int priority)
        {
            Assert.IsTrue(StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                source,
                target,
                priority,
                TransitionWindow.Always,
                catalog,
                out _,
                out string failure), failure);
        }

        private static GraphAssetSnapshot Snapshot(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog)
        {
            return GraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                GraphAssetReferenceInspection.Empty);
        }

        private static SerializedId128 Serialize(StateId id) =>
            new SerializedId128(id.High, id.Low);
    }

    public sealed class StateGraphEditorPlayModeReadOnlyTests
    {
        [UnitySetUp]
        public IEnumerator EnterPlayModeForTest()
        {
            yield return new EnterPlayMode();
        }

        [UnityTearDown]
        public IEnumerator ExitPlayModeAfterTest()
        {
            StateGraphEditorCatalogProvider.Provider = null;
            Undo.ClearAll();
            yield return new ExitPlayMode();
        }

        [Test]
        public void CanvasContextAuthoringIsReadOnlyWithoutMutationDirtyOrUndo()
        {
            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            Undo.ClearAll();
            var controller = new StateGraphEditorController(asset);
            var canvas = new StateGraphEditorCanvas(controller);
            int contextRequestCount = 0;
            canvas.ContextRequested += _ => contextRequestCount++;

            try
            {
                string assetBefore = EditorJsonUtility.ToJson(asset);
                bool dirtyBefore = EditorUtility.IsDirty(asset);
                int undoGroupBefore = Undo.GetCurrentGroup();

                Assert.IsTrue(EditorApplication.isPlayingOrWillChangePlaymode);
                Assert.IsFalse(canvas.TryRequestContext(new Vector2(120f, 80f)));
                Assert.AreEqual(0, contextRequestCount);

                bool actionInvoked = false;
                Assert.IsFalse(StateGraphEditorWindow.TryExecuteCanvasAuthoringAction(
                    () =>
                    {
                        actionInvoked = true;
                        Undo.RecordObject(asset, "Unexpected State Graph Context Action");
                        asset.Layers.Add(new StateGraphLayerRecord(
                            SerializedId128.NewId(),
                            "Unexpected"));
                        EditorUtility.SetDirty(asset);
                    },
                    out string failure));
                StringAssert.Contains("read-only", failure);
                Assert.IsFalse(actionInvoked);
                Assert.AreEqual(assetBefore, EditorJsonUtility.ToJson(asset));
                Assert.AreEqual(dirtyBefore, EditorUtility.IsDirty(asset));
                Assert.AreEqual(undoGroupBefore, Undo.GetCurrentGroup());
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                canvas.Dispose();
                controller.Dispose();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
