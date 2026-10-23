using System;
using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphCollapseTests
    {
        private CoCoStateGraphAsset _asset;
        private CoCoLayerId _layerId;
        private CoCoStateGraphEditorController _controller;
        private CoCoStateId _root;
        private CoCoStateId _nested;
        private CoCoStateId _first;
        private CoCoStateId _second;
        private CoCoStateId _outside;
        private CoCoStateGraphLayerRecord Layer => _asset.Layers[0];

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            _layerId = CoCoStateGraphAuthoringOperations.AddLayer(_asset, "Gameplay");
            _root = AddState("OnFoot");
            _nested = AddState("Combat", _root);
            _first = AddState("Light1", _nested);
            _second = AddState("Light2", _nested);
            _outside = AddState("Death");
            CoCoStateGraphEditorCatalogProvider.Provider = () => CoCoStateGraphTestFactory.CreateCatalog(false);
            _controller = new CoCoStateGraphEditorController(_asset);
        }

        [TearDown]
        public void TearDown()
        {
            _controller.Dispose();
            CoCoStateGraphEditorCatalogProvider.Provider = null;
            Undo.ClearAll();
            UnityEngine.Object.DestroyImmediate(_asset);
        }

        [Test]
        public void NestedFoldingRetainsInnerChoiceAndLeavesAssetFingerprintsUntouched()
        {
            string original = EditorJsonUtility.ToJson(_asset);
            var catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            var before = CoCoStateGraphAssetSnapshotBuilder.Build(_asset, catalog,
                CoCoStateGraphManagedReferenceInspection.Empty);
            EditorUtility.ClearDirty(_asset);
            int undo = Undo.GetCurrentGroup();
            _controller.ToggleCanvasCollapsed(_nested);
            _controller.ToggleCanvasCollapsed(_root);
            var projection = Project();
            CollectionAssert.AreEquivalent(new[] { Id(_root), Id(_outside) }, projection.VisibleStateIds);
            Assert.AreEqual(Id(_root), projection.VisibleId(Id(_first)));
            _controller.ToggleCanvasCollapsed(_root);
            projection = Project();
            Assert.AreEqual(Id(_nested), projection.VisibleId(Id(_first)));
            Assert.IsTrue(_controller.Session.IsCanvasCollapsed(_nested));
            Assert.AreEqual("OnFoot › Combat › Light1", projection.StatePath(Id(_first)));
            Assert.AreEqual(original, EditorJsonUtility.ToJson(_asset));
            Assert.IsFalse(EditorUtility.IsDirty(_asset));
            Assert.AreEqual(undo, Undo.GetCurrentGroup());
            var after = CoCoStateGraphAssetSnapshotBuilder.Build(_asset, catalog,
                CoCoStateGraphManagedReferenceInspection.Empty);
            Assert.AreEqual(before.ContentFingerprint, after.ContentFingerprint);
            Assert.AreEqual(before.CacheFingerprint, after.CacheFingerprint);
        }

        [Test]
        public void DirectedGroupsKeepEveryRealIdWhileInternalEdgesAndHiddenLoopsDisappear()
        {
            var high = AddEdge(_first, _outside, 8);
            var low = AddEdge(_second, _outside, 0);
            var middle = AddEdge(_first, _outside, 3);
            var reverse = AddEdge(_outside, _second, 0);
            AddEdge(_first, _second, 9);
            AddEdge(_first, _first, 10);
            var visibleLoop = AddEdge(_outside, _outside, 1);
            _controller.ToggleCanvasCollapsed(_root);
            var projection = Project();
            Assert.AreEqual(2, projection.TransitionGroups.Count);
            var forward = projection.TransitionGroups.Single(group => group.SourceId == Id(_root));
            CollectionAssert.AreEqual(new[] { Id(low), Id(middle), Id(high) }, forward.TransitionIds);
            CollectionAssert.AreEqual(new[] { Id(reverse) },
                projection.TransitionGroups.Single(group => group.SourceId == Id(_outside)).TransitionIds);
            CollectionAssert.AreEqual(new[] { Id(visibleLoop) }, projection.OrdinaryTransitionIds);
            _controller.ToggleCanvasCollapsed(_root);
            Assert.IsEmpty(Project().TransitionGroups);
            Assert.AreEqual(7, Project().OrdinaryTransitionIds.Count);
        }

        [Test]
        public void AnyStateStaysIndependentAndProjectsTargetsWithoutRewritingThem()
        {
            CoCoStateId anyId = CoCoStateGraphTestFactory.CreateStateId(900UL);
            Layer.AnyState = new CoCoStateGraphStateRecord(Id(anyId), default, "AnyState", default, null);
            var edge = new CoCoStateGraphTransitionRecord(
                Id(CoCoStateGraphTestFactory.CreateTransitionId(900UL)), Id(anyId), Id(_first), 0);
            Layer.Transitions.Add(edge);
            _controller.ToggleCanvasCollapsed(_root);
            Assert.IsTrue(Project().IsVisible(Id(anyId)));
            var group = Project().TransitionGroups.Single();
            Assert.AreEqual(Id(anyId), group.SourceId);
            Assert.AreEqual(Id(_root), group.TargetId);
            Assert.AreEqual(Id(_first), edge.TargetStateId);
        }

        [Test]
        public void SessionRoundTripSeparatesDirectoryFoldingAndExplicitLocationRevealsAncestors()
        {
            _controller.Session.SetCollapsed(_root, true);
            _controller.Session.Save();
            Assert.IsEmpty(CoCoStateGraphEditorSessionState.Load(_asset).CanvasCollapsedStateIds,
                "Legacy directory collapse must not hide canvas children.");
            _controller.ToggleCanvasCollapsed(_root);
            _controller.ToggleCanvasCollapsed(_nested);
            var restored = CoCoStateGraphEditorSessionState.Load(_asset);
            CollectionAssert.AreEquivalent(new[] { _root, _nested }, restored.CanvasCollapsedStateIds);
            Assert.IsTrue(restored.IsCollapsed(_root));
            _controller.NavigateToState(_first);
            Assert.IsEmpty(_controller.Session.CanvasCollapsedStateIds);
            Assert.IsTrue(_controller.Session.IsCollapsed(_root));
            _controller.ToggleCanvasCollapsed(_root);
            var edge = AddEdge(_outside, _first, 0);
            _controller.SelectTransition(edge);
            Assert.IsTrue(_controller.Session.IsCanvasCollapsed(_root), "Member selection keeps the fold.");
            _controller.Locate(new CoCoGraphDiagnosticLocation(CoCoGraphElementKind.Transition,
                CoCoGraphField.None, _asset.GraphId, _layerId, default, edge, 0, -1, 0, -1));
            Assert.IsFalse(_controller.Session.IsCanvasCollapsed(_root), "Explicit Locate reveals endpoints.");
            Assert.AreEqual(edge, _controller.Session.SelectedTransitionId);
        }

        [Test]
        public void SessionValidationDropsDeletedAndLeafIdsWithoutChangingOtherLayers()
        {
            _controller.ToggleCanvasCollapsed(_nested);
            _controller.Session.SetCanvasCollapsed(_outside, true);
            Layer.States.RemoveAll(state => state.ParentStateId == Id(_nested));
            _controller.Session.Validate(_asset);
            Assert.IsEmpty(_controller.Session.CanvasCollapsedStateIds);
            Assert.IsEmpty(Project().TransitionGroups);
            _controller.ToggleCanvasCollapsed(_root);
            var other = CoCoStateGraphAuthoringOperations.AddLayer(_asset, "Other");
            _controller.SelectLayer(other);
            Assert.IsTrue(_controller.Session.IsCanvasCollapsed(_root));
            Assert.IsEmpty(CoCoStateGraphCanvasProjection.Build(_controller.SelectedLayer,
                _controller.Session.CanvasCollapsedStateIds).VisibleStateIds);
        }

        [Test]
        public void GroupSelectionEditsTheOriginalEdgeAndRejectsStaleMenuMembers()
        {
            var selected = AddEdge(_first, _outside, 0);
            AddEdge(_second, _outside, 0);
            _controller.ToggleCanvasCollapsed(_root);
            using (var canvas = new CoCoStateGraphEditorCanvas(_controller))
            {
                var edge = Layer.Transitions[0];
                string label = canvas.TransitionMenuLabel(Project(), edge);
                StringAssert.Contains("OnFoot › Combat › Light1 → Death", label);
                StringAssert.Contains(selected.ToString(), label);
                canvas.SelectGroupTransition(Id(_root), Id(_outside), Id(selected));
                Assert.AreEqual(selected, _controller.Session.SelectedTransitionId);
                Undo.ClearAll();
                Assert.IsTrue(_controller.UpdateSelectedTransition(_first, _outside, 5, CoCoTransitionWindow.Always));
                Undo.FlushUndoRecordObjects();
                Assert.AreEqual(5, Layer.Transitions[0].Priority);
                Undo.PerformUndo();
                Assert.AreEqual(0, Layer.Transitions[0].Priority);
                Assert.IsTrue(_controller.Session.IsCanvasCollapsed(_root));
                Layer.Transitions.RemoveAt(0);
                _controller.SelectState(_outside);
                canvas.SelectGroupTransition(Id(_root), Id(_outside), Id(selected));
                Assert.AreEqual(_outside, _controller.Session.SelectedStateId);
                Assert.IsFalse(_controller.Session.SelectedTransitionId.IsValid);
            }
        }

        private CoCoStateGraphCanvasProjection Project() =>
            CoCoStateGraphCanvasProjection.Build(Layer, _controller.Session.CanvasCollapsedStateIds);

        private CoCoStateId AddState(string name, CoCoStateId parent = default) =>
            CoCoStateGraphAuthoringOperations.AddState(_asset, _layerId, parent,
                CoCoStateGraphTestFactory.StateDescriptorId, new TestStateAuthoringConfig(), name);

        private CoCoTransitionId AddEdge(CoCoStateId source, CoCoStateId target, int priority) =>
            CoCoStateGraphAuthoringOperations.AddTransition(_asset, _layerId, source, target, priority);

        private static CoCoSerializedId128 Id(CoCoStateId id) => new CoCoSerializedId128(id.High, id.Low);
        private static CoCoSerializedId128 Id(CoCoTransitionId id) => new CoCoSerializedId128(id.High, id.Low);
    }
}
