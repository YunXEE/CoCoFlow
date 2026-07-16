using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphAssetSerializationTests
    {
        private const string TestRoot = "Assets/__CoCoFlowStateGraphSerializationTests";
        private const string AssetPath = TestRoot + "/StateGraph.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowStateGraphSerializationTests");
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void SaveReimportRenameMoveAndReorderPreserveStableIds()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoStateDescriptorId descriptorId = StateDescriptorId(10UL);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            var firstConfig = new TestStateAuthoringConfig { Value = 7 };
            CoCoStateId firstStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                firstConfig,
                displayName: "Idle");
            CoCoStateId secondStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Run");
            CoCoGraphId graphId = asset.GraphId;
            firstConfig.Value = 11;

            var serialized = new SerializedObject(asset);
            SerializedProperty states = serialized.FindProperty("layers")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("states");
            states.MoveArrayElement(0, 1);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            string movedPath = TestRoot + "/RenamedStateGraph.asset";
            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(AssetPath, movedPath));
            AssetDatabase.ImportAsset(movedPath, ImportAssetOptions.ForceUpdate);
            CoCoStateGraphAsset reloaded = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(movedPath);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(graphId, reloaded.GraphId);
            Assert.AreEqual(layerId, ToLayerId(reloaded.Layers[0].LayerId));
            Assert.AreEqual(secondStateId, ToStateId(reloaded.Layers[0].States[0].StateId));
            Assert.AreEqual(firstStateId, ToStateId(reloaded.Layers[0].States[1].StateId));
            Assert.AreEqual(11, ((TestStateAuthoringConfig)reloaded.Layers[0].States[1].Config).Value);
        }

        [Test]
        public void AuthoringOperationsParticipateInUndoAndRedoWithoutChangingRestoredIds()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoLayerId addedLayerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            Undo.FlushUndoRecordObjects();

            Assert.AreEqual(1, asset.Layers.Count);
            Assert.AreEqual(addedLayerId, ToLayerId(asset.Layers[0].LayerId));

            Undo.PerformUndo();
            Assert.AreEqual(0, asset.Layers.Count);

            Undo.PerformRedo();
            Assert.AreEqual(1, asset.Layers.Count);
            Assert.AreEqual(addedLayerId, ToLayerId(asset.Layers[0].LayerId));
        }

        [Test]
        public void DuplicateSubtreeUndoRedoRestoresTheSameNewStateAndTransitionIds()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateDescriptorId descriptorId = StateDescriptorId(20UL);
            CoCoStateId rootStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Root");
            CoCoStateId childStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                rootStateId,
                descriptorId,
                displayName: "Child");
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                rootStateId,
                childStateId);
            Undo.FlushUndoRecordObjects();
            Undo.ClearAll();
            CoCoStateGraphLayerRecord layer = asset.Layers[0];
            var originalStateIds = new HashSet<CoCoSerializedId128>(
                layer.States.Select(state => state.StateId));
            var originalTransitionIds = new HashSet<CoCoSerializedId128>(
                layer.Transitions.Select(transition => transition.TransitionId));

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.DuplicateStateSubtree(
                asset,
                layerId,
                rootStateId,
                out CoCoStateId duplicatedRootId));
            Undo.FlushUndoRecordObjects();
            var duplicatedStateIds = new HashSet<CoCoSerializedId128>(
                layer.States.Where(state => !originalStateIds.Contains(state.StateId))
                    .Select(state => state.StateId));
            var duplicatedTransitionIds = new HashSet<CoCoSerializedId128>(
                layer.Transitions.Where(transition =>
                        !originalTransitionIds.Contains(transition.TransitionId))
                    .Select(transition => transition.TransitionId));

            Assert.AreEqual(2, duplicatedStateIds.Count);
            Assert.AreEqual(1, duplicatedTransitionIds.Count);
            Assert.IsTrue(duplicatedStateIds.Any(id =>
                id.High == duplicatedRootId.High && id.Low == duplicatedRootId.Low));

            Undo.PerformUndo();
            layer = asset.Layers[0];
            Assert.AreEqual(2, layer.States.Count);
            Assert.AreEqual(1, layer.Transitions.Count);

            Undo.PerformRedo();
            layer = asset.Layers[0];
            Assert.AreEqual(4, layer.States.Count);
            Assert.AreEqual(2, layer.Transitions.Count);
            CollectionAssert.AreEquivalent(
                duplicatedStateIds,
                layer.States.Where(state => !originalStateIds.Contains(state.StateId))
                    .Select(state => state.StateId));
            CollectionAssert.AreEquivalent(
                duplicatedTransitionIds,
                layer.Transitions.Where(transition =>
                        !originalTransitionIds.Contains(transition.TransitionId))
                    .Select(transition => transition.TransitionId));
        }

        [Test]
        public void DuplicateLayerUndoRedoRestoresTheSameGeneratedTopologyIds()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateDescriptorId descriptorId = StateDescriptorId(30UL);
            CoCoStateId rootStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Root");
            CoCoStateId childStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                rootStateId,
                descriptorId,
                displayName: "Child");
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                rootStateId,
                childStateId);
            Undo.FlushUndoRecordObjects();
            Undo.ClearAll();

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.DuplicateLayer(
                asset,
                layerId,
                out CoCoLayerId duplicatedLayerId));
            Undo.FlushUndoRecordObjects();
            Assert.AreEqual(2, asset.Layers.Count);
            CoCoStateGraphLayerRecord duplicate = asset.Layers[1];
            CoCoSerializedId128 duplicatedSerializedLayerId = duplicate.LayerId;
            CoCoSerializedId128[] duplicatedStateIds = duplicate.States
                .Select(state => state.StateId)
                .ToArray();
            CoCoSerializedId128[] duplicatedTransitionIds = duplicate.Transitions
                .Select(transition => transition.TransitionId)
                .ToArray();
            Assert.AreEqual(duplicatedLayerId, ToLayerId(duplicatedSerializedLayerId));

            Undo.PerformUndo();
            Assert.AreEqual(1, asset.Layers.Count);

            Undo.PerformRedo();
            Assert.AreEqual(2, asset.Layers.Count);
            duplicate = asset.Layers[1];
            Assert.AreEqual(duplicatedSerializedLayerId, duplicate.LayerId);
            CollectionAssert.AreEqual(
                duplicatedStateIds,
                duplicate.States.Select(state => state.StateId));
            CollectionAssert.AreEqual(
                duplicatedTransitionIds,
                duplicate.Transitions.Select(transition => transition.TransitionId));
        }

        [Test]
        public void SharedTopLevelConfigKeepsIdentityAndCompileFingerprintAfterReload()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            var sharedConfig = new TestStateAuthoringConfig { Value = 23 };
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                sharedConfig,
                "First");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                sharedConfig,
                "Second");
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphAssetCompileResult beforeReload =
                new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(asset, catalog);
            Assert.IsTrue(beforeReload.Succeeded);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            CoCoStateGraphAsset reloaded = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(AssetPath);
            CoCoStateGraphAssetCompileResult afterReload =
                new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(reloaded, catalog);

            Assert.IsTrue(
                afterReload.Succeeded,
                string.Join(
                    "\n",
                    afterReload.Diagnostics.Select(diagnostic =>
                        $"{diagnostic.Diagnostic.Code}: {diagnostic.Diagnostic.Message} " +
                        $"({diagnostic.Location.ElementKind}/{diagnostic.Location.Field})")));
            Assert.AreEqual(beforeReload.ContentFingerprint, afterReload.ContentFingerprint);
            Assert.AreEqual(beforeReload.Diagnostics.Count, afterReload.Diagnostics.Count);
            Assert.AreSame(
                reloaded.Layers[0].States[0].Config,
                reloaded.Layers[0].States[1].Config);
        }

        private static CoCoStateGraphAsset CreateSavedAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(guid);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Assert.IsTrue(asset.GraphId.IsValid);
            Assert.AreEqual(guid, asset.AssetGuidStamp);
            return asset;
        }

        private static CoCoStateDescriptorId StateDescriptorId(ulong low)
        {
            Assert.IsTrue(CoCoStateDescriptorId.TryCreate(1UL, low, out CoCoStateDescriptorId id));
            return id;
        }

        private static CoCoLayerId ToLayerId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(id.High, id.Low, out CoCoLayerId value));
            return value;
        }

        private static CoCoStateId ToStateId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoStateId.TryCreate(id.High, id.Low, out CoCoStateId value));
            return value;
        }
    }
}
