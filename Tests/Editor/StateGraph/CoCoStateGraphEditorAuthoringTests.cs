using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphEditorAuthoringTests
    {
        private readonly List<CoCoStateGraphAsset> assets = new List<CoCoStateGraphAsset>();

        [TearDown]
        public void TearDown()
        {
            CoCoStateGraphEditorCatalogProvider.Provider = null;
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
        public void EditorLayoutDoesNotAffectFingerprintsAndRemapsWithCopyAndUndo()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId stateId = AddState(asset, layerId, default, "Idle");
            CoCoStateGraphAssetSnapshot baseline = Snapshot(asset, catalog);
            Assert.IsTrue(asset.EditorLayout.TryGetPosition(Serialize(stateId), out Vector2 original));

            Undo.ClearAll();
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                stateId,
                new Vector2(321f, 123f),
                out string failure), failure);
            Undo.FlushUndoRecordObjects();
            CoCoStateGraphAssetSnapshot moved = Snapshot(asset, catalog);

            Assert.AreEqual(baseline.ContentFingerprint, moved.ContentFingerprint);
            Assert.AreEqual(baseline.CacheFingerprint, moved.CacheFingerprint);
            Undo.PerformUndo();
            Assert.IsTrue(asset.EditorLayout.TryGetPosition(Serialize(stateId), out Vector2 restored));
            Assert.AreEqual(original, restored);

            CoCoStateGraphAsset copy = UnityEngine.Object.Instantiate(asset);
            assets.Add(copy);
            copy.RegenerateTopologyIdsForAssetCopy("copy-guid");
            CoCoSerializedId128 copiedStateId = copy.Layers[0].States[0].StateId;
            Assert.AreNotEqual(Serialize(stateId), copiedStateId);
            Assert.IsTrue(copy.EditorLayout.TryGetPosition(copiedStateId, out Vector2 copiedPosition));
            Assert.AreEqual(original, copiedPosition);
            Assert.IsFalse(copy.EditorLayout.TryGetPosition(Serialize(stateId), out _));
        }

        [Test]
        public void StrictTransitionCommandsRejectInvalidEndpointsWindowsAndPrioritiesWithoutMutation()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layerId, default, "First");
            CoCoStateId second = AddState(asset, layerId, default, "Second");
            CoCoStateId composite = AddState(asset, layerId, default, "Composite");
            AddState(asset, layerId, composite, "Child");

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                second,
                5,
                CoCoTransitionWindow.Always,
                catalog,
                out _,
                out string failure), failure);
            Assert.AreEqual(1, asset.Layers[0].Transitions.Count);

            Assert.IsFalse(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                second,
                5,
                CoCoTransitionWindow.Always,
                catalog,
                out _,
                out _));
            Assert.IsFalse(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                composite,
                second,
                0,
                CoCoTransitionWindow.Always,
                catalog,
                out _,
                out _));
            Assert.IsFalse(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                second,
                6,
                default,
                catalog,
                out _,
                out _));

            CoCoLayerId otherLayer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Other");
            CoCoStateId otherState = AddState(asset, otherLayer, default, "Other");
            Assert.IsFalse(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                first,
                otherState,
                6,
                CoCoTransitionWindow.Always,
                catalog,
                out _,
                out _));
            Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                CoCoTransitionWindowMode.ActionProgress,
                0.5d,
                1d,
                out CoCoTransitionWindow progressWindow));
            Assert.IsFalse(CoCoStateGraphAuthoringOperations.TryAddTransition(
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
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId first = AddState(asset, layerId, default, "First");
            CoCoStateId second = AddState(asset, layerId, default, "Second");
            CoCoStateId third = AddState(asset, layerId, default, "Third");
            AddTransition(asset, catalog, layerId, first, second, 0);
            AddTransition(asset, catalog, layerId, third, first, 0);

            Assert.IsFalse(CoCoStateGraphAuthoringOperations.TryDeleteStateSubtree(
                asset,
                layerId,
                first,
                default,
                out _,
                out _));
            Assert.AreEqual(3, asset.Layers[0].States.Count);
            Assert.AreEqual(2, asset.Layers[0].Transitions.Count);

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryDeleteStateSubtree(
                asset,
                layerId,
                first,
                second,
                out CoCoStateGraphDeleteImpact impact,
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
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId root = AddState(asset, layerId, default, "Root");
            CoCoStateId child = AddState(asset, layerId, root, "Child");
            AddTransition(asset, catalog, layerId, child, child, 0);
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                root,
                new Vector2(10f, 20f),
                out _));
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TrySetStatePosition(
                asset,
                layerId,
                child,
                new Vector2(30f, 40f),
                out _));

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryCaptureSubtree(
                asset,
                layerId,
                root,
                out CoCoStateGraphSubtreeClipboard clipboard,
                out string captureFailure), captureFailure);
            using (clipboard)
            {
                Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryPasteSubtree(
                    asset,
                    clipboard,
                    layerId,
                    default,
                    new Vector2(400f, 200f),
                    out CoCoStateId pastedRoot,
                    out string pasteFailure), pasteFailure);

                CoCoStateGraphLayerRecord layer = asset.Layers[0];
                CoCoStateGraphStateRecord pastedRootRecord = layer.States.Single(state =>
                    state.StateId == Serialize(pastedRoot));
                CoCoStateGraphStateRecord pastedChild = layer.States.Single(state =>
                    state.ParentStateId == pastedRootRecord.StateId);
                Assert.AreNotEqual(Serialize(root), pastedRootRecord.StateId);
                Assert.AreNotEqual(Serialize(child), pastedChild.StateId);
                Assert.AreEqual(4, layer.States.Count);
                Assert.AreEqual(2, layer.Transitions.Count);
                CoCoStateGraphTransitionRecord pastedTransition = layer.Transitions.Single(transition =>
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
            CoCoGraphDescriptorCatalog simpleCatalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAsset simple = CreateAsset();
            Assert.IsTrue(CoCoStateGraphPresetWizard.TryPopulateSimple(
                simple,
                simpleCatalog,
                CoCoStateGraphTestFactory.StateDescriptorId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                out string simpleFailure), simpleFailure);
            Assert.AreEqual(1, simple.Layers.Count);
            Assert.AreEqual(2, simple.Layers[0].States.Count);
            Assert.AreEqual(1, simple.Layers[0].Transitions.Count);
            Assert.AreEqual(CoCoTransitionWindowMode.Always, simple.Layers[0].Transitions[0].WindowMode);

            CoCoGraphDescriptorCatalog comboCatalog =
                CoCoStateGraphTestFactory.CreateCatalog(false, providesActionProgress: true);
            CoCoStateGraphAsset combo = CreateAsset();
            Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                CoCoTransitionWindowMode.ActionProgress,
                0.9d,
                1d,
                out CoCoTransitionWindow window));
            Assert.IsTrue(CoCoStateGraphPresetWizard.TryPopulateCombo(
                combo,
                comboCatalog,
                CoCoStateGraphTestFactory.StateDescriptorId,
                CoCoStateGraphTestFactory.StateDescriptorId,
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
                Assert.AreEqual(CoCoTransitionWindowMode.ActionProgress,
                    combo.Layers[0].Transitions[index].WindowMode);
            }
        }

        [Test]
        public void SessionFallsBackToFirstAuthoredLayerRecomputesAnalysisAndRoutesInitialChild()
        {
            var catalogBuilder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(catalogBuilder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic catalogDiagnostic), catalogDiagnostic.Message);
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId firstLayer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "First");
            CoCoLayerId secondLayer = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Second");
            CoCoStateId root = AddState(asset, firstLayer, default, "Root");
            CoCoStateId firstChild = AddState(asset, firstLayer, root, "First Child");
            CoCoStateId secondChild = AddState(asset, firstLayer, root, "Second Child");

            CoCoStateGraphEditorSessionState session = CoCoStateGraphEditorSessionState.Load(asset);
            session.SelectedLayerId = CoCoStateGraphTestFactory.CreateLayerId(999UL);
            session.AnalysisRequested = true;
            session.Save();
            CoCoStateGraphEditorCatalogProvider.Provider = () => catalog;

            using (var controller = new CoCoStateGraphEditorController(asset))
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
        public void FlatCanvasDiscardsLegacyDrillRootAndNavigationKeepsLayerRootScope()
        {
            CoCoStateGraphAsset asset = CreateAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId root = AddState(asset, layerId, default, "Root");
            CoCoStateId child = AddState(asset, layerId, root, "Child");
            CoCoStateGraphEditorSessionState legacySession =
                CoCoStateGraphEditorSessionState.Load(asset);
            legacySession.SelectedLayerId = layerId;
            legacySession.DrillRootStateId = root;
            legacySession.Save();

            using (var controller = new CoCoStateGraphEditorController(asset))
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

        private CoCoStateGraphAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            assets.Add(asset);
            return asset;
        }

        private static CoCoStateId AddState(
            CoCoStateGraphAsset asset,
            CoCoLayerId layerId,
            CoCoStateId parentStateId,
            string name)
        {
            return CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                parentStateId,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                name);
        }

        private static void AddTransition(
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog,
            CoCoLayerId layerId,
            CoCoStateId source,
            CoCoStateId target,
            int priority)
        {
            Assert.IsTrue(CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                source,
                target,
                priority,
                CoCoTransitionWindow.Always,
                catalog,
                out _,
                out string failure), failure);
        }

        private static CoCoStateGraphAssetSnapshot Snapshot(
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog)
        {
            return CoCoStateGraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                CoCoStateGraphManagedReferenceInspection.Empty);
        }

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);
    }

    public sealed class CoCoStateGraphEditorPlayModeReadOnlyTests
    {
        [UnitySetUp]
        public IEnumerator EnterPlayModeForTest()
        {
            yield return new EnterPlayMode();
        }

        [UnityTearDown]
        public IEnumerator ExitPlayModeAfterTest()
        {
            CoCoStateGraphEditorCatalogProvider.Provider = null;
            Undo.ClearAll();
            yield return new ExitPlayMode();
        }

        [Test]
        public void CanvasContextAuthoringIsReadOnlyWithoutMutationDirtyOrUndo()
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            Undo.ClearAll();
            var controller = new CoCoStateGraphEditorController(asset);
            var canvas = new CoCoStateGraphEditorCanvas(controller);
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
                Assert.IsFalse(CoCoStateGraphEditorWindow.TryExecuteCanvasAuthoringAction(
                    () =>
                    {
                        actionInvoked = true;
                        Undo.RecordObject(asset, "Unexpected State Graph Context Action");
                        asset.Layers.Add(new CoCoStateGraphLayerRecord(
                            CoCoSerializedId128.NewId(),
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
