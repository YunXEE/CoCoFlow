using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphCopyIdentityTests
    {
        private const string TestRoot = "Assets/__CoCoFlowStateGraphCopyTests";
        private const string SourcePath = TestRoot + "/Source.asset";
        private const string CopyPath = TestRoot + "/Copy.asset";
        private const string MovedCopyPath = TestRoot + "/MovedCopy.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowStateGraphCopyTests");
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void WholeAssetCopyRemapsEveryTopologyIdAndInternalReference()
        {
            CoCoStateGraphAsset source = CreatePopulatedAsset();
            Assert.IsTrue(AssetDatabase.CopyAsset(SourcePath, CopyPath));
            AssetDatabase.ImportAsset(CopyPath, ImportAssetOptions.ForceSynchronousImport);
            CoCoStateGraphAsset copy = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(CopyPath);

            Assert.IsNotNull(copy);
            Assert.AreNotEqual(source.GraphId, copy.GraphId);
            Assert.AreEqual(source.Layers.Count, copy.Layers.Count);

            for (int layerIndex = 0; layerIndex < source.Layers.Count; layerIndex++)
            {
                CoCoStateGraphLayerRecord sourceLayer = source.Layers[layerIndex];
                CoCoStateGraphLayerRecord copyLayer = copy.Layers[layerIndex];
                Assert.AreEqual(sourceLayer.States.Count, copyLayer.States.Count);
                for (int stateIndex = 0; stateIndex < sourceLayer.States.Count; stateIndex++)
                {
                    Assert.AreEqual(
                        sourceLayer.States[stateIndex].StateDescriptorId,
                        copyLayer.States[stateIndex].StateDescriptorId);
                }

                Assert.AreEqual(sourceLayer.Transitions.Count, copyLayer.Transitions.Count);
                for (int transitionIndex = 0;
                     transitionIndex < sourceLayer.Transitions.Count;
                     transitionIndex++)
                {
                    CoCoStateGraphTransitionRecord sourceTransition =
                        sourceLayer.Transitions[transitionIndex];
                    CoCoStateGraphTransitionRecord copyTransition =
                        copyLayer.Transitions[transitionIndex];
                    Assert.AreEqual(
                        sourceTransition.Conditions.Count,
                        copyTransition.Conditions.Count);
                    for (int conditionIndex = 0;
                         conditionIndex < sourceTransition.Conditions.Count;
                         conditionIndex++)
                    {
                        Assert.AreEqual(
                            sourceTransition.Conditions[conditionIndex].ConditionDescriptorId,
                            copyTransition.Conditions[conditionIndex].ConditionDescriptorId);
                    }
                }
            }

            var sourceLayerIds = new HashSet<CoCoSerializedId128>(source.Layers.Select(layer => layer.LayerId));
            var sourceStateIds = new HashSet<CoCoSerializedId128>(
                source.Layers.SelectMany(layer => layer.States).Select(state => state.StateId));
            var sourceTransitionIds = new HashSet<CoCoSerializedId128>(
                source.Layers.SelectMany(layer => layer.Transitions).Select(transition => transition.TransitionId));

            foreach (CoCoStateGraphLayerRecord layer in copy.Layers)
            {
                Assert.IsFalse(sourceLayerIds.Contains(layer.LayerId));
                Assert.IsTrue(layer.States.Any(state => state.StateId == layer.InitialStateId));
                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    Assert.IsFalse(sourceStateIds.Contains(state.StateId));
                    if (state.ParentStateId.IsValid)
                    {
                        Assert.IsTrue(layer.States.Any(candidate => candidate.StateId == state.ParentStateId));
                    }

                    if (state.InitialChildStateId.IsValid)
                    {
                        Assert.IsTrue(layer.States.Any(candidate => candidate.StateId == state.InitialChildStateId));
                    }
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
                {
                    Assert.IsFalse(sourceTransitionIds.Contains(transition.TransitionId));
                    Assert.IsTrue(layer.States.Any(state => state.StateId == transition.SourceStateId));
                    Assert.IsTrue(layer.States.Any(state => state.StateId == transition.TargetStateId));
                }
            }
        }

        [Test]
        public void SubtreeCopyRemapsOnlyCopiedStatesAndInternalTransitions()
        {
            CoCoStateGraphAsset asset = CreatePopulatedAsset();
            CoCoStateGraphLayerRecord layer = asset.Layers[0];
            CoCoStateId sourceRootId = ToStateId(layer.States[0].StateId);
            var originalStateIds = new HashSet<CoCoSerializedId128>(layer.States.Select(state => state.StateId));
            var originalTransitionIds = new HashSet<CoCoSerializedId128>(
                layer.Transitions.Select(transition => transition.TransitionId));
            CoCoSerializedId128 outsideStateId = layer.States.Single(
                state => state.DisplayName == "Outside").StateId;

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.DuplicateStateSubtree(
                asset,
                ToLayerId(layer.LayerId),
                sourceRootId,
                out CoCoStateId duplicatedRootId));

            Assert.AreEqual(5, layer.States.Count);
            Assert.AreEqual(4, layer.Transitions.Count);
            CoCoStateGraphStateRecord duplicatedRoot = layer.States.Single(state =>
                state.StateId.High == duplicatedRootId.High && state.StateId.Low == duplicatedRootId.Low);
            CoCoStateGraphStateRecord duplicatedChild = layer.States.Single(state =>
                !originalStateIds.Contains(state.StateId) && state.StateId != duplicatedRoot.StateId);
            CoCoStateGraphTransitionRecord duplicatedTransition = layer.Transitions.Single(transition =>
                !originalTransitionIds.Contains(transition.TransitionId));

            Assert.AreEqual(layer.States[0].ParentStateId, duplicatedRoot.ParentStateId);
            Assert.AreEqual(duplicatedChild.StateId, duplicatedRoot.InitialChildStateId);
            Assert.AreEqual(duplicatedRoot.StateId, duplicatedChild.ParentStateId);
            Assert.AreEqual(duplicatedRoot.StateId, duplicatedTransition.SourceStateId);
            Assert.AreEqual(duplicatedChild.StateId, duplicatedTransition.TargetStateId);
            Assert.AreNotEqual(outsideStateId, duplicatedTransition.SourceStateId);
            Assert.AreNotEqual(outsideStateId, duplicatedTransition.TargetStateId);
            Assert.AreEqual(1, layer.Transitions.Count(transition =>
                !originalTransitionIds.Contains(transition.TransitionId)));
            foreach (CoCoSerializedId128 transitionId in originalTransitionIds)
            {
                Assert.AreEqual(
                    1,
                    layer.Transitions.Count(transition => transition.TransitionId == transitionId));
            }
        }

        [Test]
        public void DuplicateLayerDeepCopiesAuthoringDataAndRemapsEveryTopologyReference()
        {
            CoCoStateGraphAsset asset = CreatePopulatedAsset();
            CoCoStateGraphLayerRecord source = asset.Layers[0];
            var sourceStateIds = new HashSet<CoCoSerializedId128>(
                source.States.Select(state => state.StateId));
            var sourceTransitionIds = new HashSet<CoCoSerializedId128>(
                source.Transitions.Select(transition => transition.TransitionId));
            CoCoStateGraphAuthoringOperations.AddLayer(asset, "Following");
            CoCoStateGraphLayerRecord following = asset.Layers[1];

            Assert.IsTrue(CoCoStateGraphAuthoringOperations.DuplicateLayer(
                asset,
                ToLayerId(source.LayerId),
                out CoCoLayerId duplicatedLayerId));

            Assert.AreEqual(3, asset.Layers.Count);
            Assert.AreSame(source, asset.Layers[0]);
            CoCoStateGraphLayerRecord duplicate = asset.Layers[1];
            Assert.AreSame(following, asset.Layers[2]);
            Assert.AreEqual(duplicatedLayerId, ToLayerId(duplicate.LayerId));
            Assert.AreNotEqual(source.LayerId, duplicate.LayerId);
            Assert.AreEqual(source.DisplayName, duplicate.DisplayName);
            Assert.AreEqual(source.States.Count, duplicate.States.Count);
            Assert.AreEqual(source.Transitions.Count, duplicate.Transitions.Count);

            var stateRemaps = new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
            for (int stateIndex = 0; stateIndex < source.States.Count; stateIndex++)
            {
                CoCoStateGraphStateRecord sourceState = source.States[stateIndex];
                CoCoStateGraphStateRecord duplicatedState = duplicate.States[stateIndex];
                Assert.IsFalse(sourceStateIds.Contains(duplicatedState.StateId));
                Assert.AreEqual(sourceState.DisplayName, duplicatedState.DisplayName);
                Assert.AreEqual(sourceState.StateDescriptorId, duplicatedState.StateDescriptorId);
                Assert.AreEqual(sourceState.Config?.GetType(), duplicatedState.Config?.GetType());
                if (sourceState.Config != null)
                {
                    Assert.AreNotSame(sourceState.Config, duplicatedState.Config);
                }

                stateRemaps.Add(sourceState.StateId, duplicatedState.StateId);
            }

            Assert.AreSame(duplicate.States[0].Config, duplicate.States[1].Config);
            Assert.AreNotSame(source.States[0].Config, duplicate.States[0].Config);
            Assert.AreEqual(
                ((TestStateAuthoringConfig)source.States[0].Config).Value,
                ((TestStateAuthoringConfig)duplicate.States[0].Config).Value);
            Assert.AreEqual(stateRemaps[source.InitialStateId], duplicate.InitialStateId);

            for (int stateIndex = 0; stateIndex < source.States.Count; stateIndex++)
            {
                CoCoStateGraphStateRecord sourceState = source.States[stateIndex];
                CoCoStateGraphStateRecord duplicatedState = duplicate.States[stateIndex];
                Assert.AreEqual(
                    sourceState.ParentStateId.IsValid
                        ? stateRemaps[sourceState.ParentStateId]
                        : default,
                    duplicatedState.ParentStateId);
                Assert.AreEqual(
                    sourceState.InitialChildStateId.IsValid
                        ? stateRemaps[sourceState.InitialChildStateId]
                        : default,
                    duplicatedState.InitialChildStateId);
            }

            for (int transitionIndex = 0;
                 transitionIndex < source.Transitions.Count;
                 transitionIndex++)
            {
                CoCoStateGraphTransitionRecord sourceTransition = source.Transitions[transitionIndex];
                CoCoStateGraphTransitionRecord duplicatedTransition =
                    duplicate.Transitions[transitionIndex];
                Assert.IsFalse(sourceTransitionIds.Contains(duplicatedTransition.TransitionId));
                Assert.AreEqual(
                    stateRemaps[sourceTransition.SourceStateId],
                    duplicatedTransition.SourceStateId);
                Assert.AreEqual(
                    stateRemaps[sourceTransition.TargetStateId],
                    duplicatedTransition.TargetStateId);
                Assert.AreEqual(sourceTransition.Priority, duplicatedTransition.Priority);
                Assert.AreEqual(sourceTransition.WindowMode, duplicatedTransition.WindowMode);
                Assert.AreEqual(
                    sourceTransition.WindowStartInclusive,
                    duplicatedTransition.WindowStartInclusive);
                Assert.AreEqual(
                    sourceTransition.WindowEndExclusive,
                    duplicatedTransition.WindowEndExclusive);
                Assert.AreEqual(
                    sourceTransition.InterruptPolicy,
                    duplicatedTransition.InterruptPolicy);
                Assert.AreEqual(
                    sourceTransition.Conditions.Count,
                    duplicatedTransition.Conditions.Count);
                for (int conditionIndex = 0;
                     conditionIndex < sourceTransition.Conditions.Count;
                     conditionIndex++)
                {
                    CoCoStateGraphConditionRecord sourceCondition =
                        sourceTransition.Conditions[conditionIndex];
                    CoCoStateGraphConditionRecord duplicatedCondition =
                        duplicatedTransition.Conditions[conditionIndex];
                    Assert.AreEqual(
                        sourceCondition.ConditionDescriptorId,
                        duplicatedCondition.ConditionDescriptorId);
                    Assert.AreEqual(sourceCondition.Config?.GetType(), duplicatedCondition.Config?.GetType());
                    if (sourceCondition.Config != null)
                    {
                        Assert.AreNotSame(sourceCondition.Config, duplicatedCondition.Config);
                        Assert.AreEqual(
                            ((TestConditionAuthoringConfig)sourceCondition.Config).Threshold,
                            ((TestConditionAuthoringConfig)duplicatedCondition.Config).Threshold);
                    }
                }
            }
        }

        [Test]
        public void DuplicateLayerRejectsDuplicateTopologyIdsWithoutMutatingTheAsset()
        {
            CoCoStateGraphAsset asset = CreatePopulatedAsset();
            CoCoStateGraphLayerRecord source = asset.Layers[0];
            source.States[1].StateId = source.States[0].StateId;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            string[] topologyBefore = CaptureTopology(asset);

            Assert.IsFalse(CoCoStateGraphAuthoringOperations.DuplicateLayer(
                asset,
                ToLayerId(source.LayerId),
                out CoCoLayerId duplicatedLayerId));

            Assert.IsFalse(duplicatedLayerId.IsValid);
            Assert.AreEqual(1, asset.Layers.Count);
            CollectionAssert.AreEqual(topologyBefore, CaptureTopology(asset));
            Assert.IsFalse(EditorUtility.IsDirty(asset));
        }

        [Test]
        public void DuplicateLayerRejectsDanglingTopologyReferencesWithoutMutatingTheAsset()
        {
            CoCoStateGraphAsset asset = CreatePopulatedAsset();
            CoCoStateGraphLayerRecord source = asset.Layers[0];
            source.Transitions[0].TargetStateId = CoCoSerializedId128.NewId();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            string[] topologyBefore = CaptureTopology(asset);

            Assert.IsFalse(CoCoStateGraphAuthoringOperations.DuplicateLayer(
                asset,
                ToLayerId(source.LayerId),
                out CoCoLayerId duplicatedLayerId));

            Assert.IsFalse(duplicatedLayerId.IsValid);
            Assert.AreEqual(1, asset.Layers.Count);
            CollectionAssert.AreEqual(topologyBefore, CaptureTopology(asset));
            Assert.IsFalse(EditorUtility.IsDirty(asset));
        }

        [Test]
        public void WholeAssetCopyKeepsItsFirstRemapAcrossImmediateMoveAndReimport()
        {
            CreatePopulatedAsset();
            Assert.IsTrue(AssetDatabase.CopyAsset(SourcePath, CopyPath));
            AssetDatabase.ImportAsset(CopyPath, ImportAssetOptions.ForceSynchronousImport);
            CoCoStateGraphAsset copy = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(CopyPath);
            string copyGuid = AssetDatabase.AssetPathToGUID(CopyPath);
            Assert.AreEqual(copyGuid, copy.AssetGuidStamp);
            string[] firstRemap = CaptureTopology(copy);

            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(CopyPath, MovedCopyPath));
            AssetDatabase.ImportAsset(MovedCopyPath, ImportAssetOptions.ForceSynchronousImport);
            InvokePendingIdentitySave();
            Resources.UnloadAsset(copy);
            AssetDatabase.ImportAsset(MovedCopyPath, ImportAssetOptions.ForceSynchronousImport);
            CoCoStateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(MovedCopyPath);

            Assert.AreEqual(copyGuid, AssetDatabase.AssetPathToGUID(MovedCopyPath));
            Assert.AreEqual(copyGuid, reloaded.AssetGuidStamp);
            CollectionAssert.AreEqual(firstRemap, CaptureTopology(reloaded));
        }

        private static CoCoStateGraphAsset CreatePopulatedAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            AssetDatabase.CreateAsset(asset, SourcePath);
            string guid = AssetDatabase.AssetPathToGUID(SourcePath);
            asset.EnsureAssetIdentity(guid);
            Assert.IsTrue(asset.GraphId.IsValid);
            Assert.AreEqual(guid, asset.AssetGuidStamp);
            CoCoStateDescriptorId descriptorId = StateDescriptorId(10UL);
            var sharedConfig = new TestStateAuthoringConfig { Value = 17 };
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId rootStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                sharedConfig,
                displayName: "Root");
            CoCoStateId childStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                rootStateId,
                descriptorId,
                sharedConfig,
                displayName: "Child");
            CoCoStateId outsideStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Outside");
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                rootStateId,
                childStateId,
                30);
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                outsideStateId,
                rootStateId,
                20);
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                childStateId,
                outsideStateId,
                10);
            var serialized = new SerializedObject(asset);
            SerializedProperty firstTransition = serialized.FindProperty("layers")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("transitions")
                .GetArrayElementAtIndex(0);
            firstTransition.FindPropertyRelative("windowMode").enumValueIndex =
                (int)CoCoTransitionWindowMode.Normalized;
            firstTransition.FindPropertyRelative("windowStartInclusive").doubleValue = 0.25d;
            firstTransition.FindPropertyRelative("windowEndExclusive").doubleValue = 0.75d;
            firstTransition.FindPropertyRelative("interruptPolicy").enumValueIndex =
                (int)CoCoTransitionInterruptPolicy.AllowDuringSourceActivation;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            CoCoConditionDescriptorId conditionDescriptorId = ConditionDescriptorId(11UL);
            asset.Layers[0].Transitions[0].Conditions.Add(new CoCoStateGraphConditionRecord(
                new CoCoSerializedId128(conditionDescriptorId.High, conditionDescriptorId.Low),
                new TestConditionAuthoringConfig { Threshold = 3 }));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private static CoCoStateDescriptorId StateDescriptorId(ulong low)
        {
            Assert.IsTrue(CoCoStateDescriptorId.TryCreate(1UL, low, out CoCoStateDescriptorId id));
            return id;
        }

        private static CoCoConditionDescriptorId ConditionDescriptorId(ulong low)
        {
            Assert.IsTrue(CoCoConditionDescriptorId.TryCreate(
                2UL,
                low,
                out CoCoConditionDescriptorId id));
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

        private static string[] CaptureTopology(CoCoStateGraphAsset asset)
        {
            var values = new List<string>
            {
                asset.GraphId.ToString(),
                asset.AssetGuidStamp
            };
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                values.Add(Format(layer.LayerId));
                values.Add(Format(layer.InitialStateId));
                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    values.Add(Format(state.StateId));
                    values.Add(Format(state.ParentStateId));
                    values.Add(Format(state.InitialChildStateId));
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
                {
                    values.Add(Format(transition.TransitionId));
                    values.Add(Format(transition.SourceStateId));
                    values.Add(Format(transition.TargetStateId));
                }
            }

            return values.ToArray();
        }

        private static string Format(CoCoSerializedId128 id) =>
            $"{id.High:x16}{id.Low:x16}";

        private static void InvokePendingIdentitySave()
        {
            Type postprocessorType = typeof(CoCoStateGraphAuthoringOperations).Assembly.GetType(
                "CoCoFlow.Editor.StateGraph.CoCoStateGraphAssetIdentityPostprocessor",
                throwOnError: true);
            MethodInfo saveMethod = postprocessorType.GetMethod(
                "SavePendingAssets",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(saveMethod);
            saveMethod.Invoke(null, null);
        }
    }
}
