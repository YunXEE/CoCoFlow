using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 编辑器测试对图及 Unity 引用执行复制、移动或重导入，对照资产身份和引用目标。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>编辑器测试对图及 Unity 引用执行复制、移动或重导入，对照资产身份和引用目标。</summary>
    public sealed class StateGraphCopyIdentityTests
    {

        #region Public API

        /// <summary>清理上一次的临时资产并为本例创建独立测试目录。</summary>
        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowStateGraphCopyTests");
        }

        /// <summary>释放本次资产测试使用的临时目录并刷新 AssetDatabase。</summary>
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        /// <summary>复制含 AnyState 的层或子树，检查所有者重新关联且配置对象相互独立。</summary>
        [Test]
        public void AnyStateCopiesRemapOwnerAndKeepSubtreeIndependent()
        {
            StateGraphAsset asset = CreatePopulatedAsset();
            StateGraphLayerRecord source = asset.Layers[0];
            source.AnyState = new StateGraphStateRecord(SerializedId128.NewId(), default, "Any",
                new SerializedId128(5UL, 2UL), new TestStateAuthoringConfig { Value = 9 });
            SerializedId128 anyId = source.AnyState.StateId;
            StateGraphAuthoringOperations.AddTransition(asset, ToLayerId(source.LayerId),
                ToStateId(anyId), ToStateId(source.States[1].StateId), 0);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Assert.IsTrue(AssetDatabase.CopyAsset(SourcePath, CopyPath));
            AssetDatabase.ImportAsset(CopyPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphLayerRecord copied = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(CopyPath).Layers[0];
            Assert.AreNotEqual(anyId, copied.AnyState.StateId);
            Assert.AreEqual(copied.AnyState.StateId, copied.Transitions.Last().SourceStateId);
            Assert.AreEqual(copied.States[1].StateId, copied.Transitions.Last().TargetStateId);
            Assert.IsTrue(StateGraphAuthoringOperations.DuplicateLayer(asset, ToLayerId(source.LayerId), out _));
            StateGraphLayerRecord duplicate = asset.Layers[1];
            Assert.AreNotEqual(anyId, duplicate.AnyState.StateId);
            Assert.AreNotSame(source.AnyState.Config, duplicate.AnyState.Config);
            Assert.AreEqual(duplicate.AnyState.StateId, duplicate.Transitions.Last().SourceStateId);
            Assert.AreEqual(duplicate.States[1].StateId, duplicate.Transitions.Last().TargetStateId);
            Assert.IsTrue(StateGraphAuthoringOperations.DuplicateStateSubtree(asset,
                ToLayerId(source.LayerId), ToStateId(source.States[0].StateId), out _));
            Assert.AreEqual(anyId, source.AnyState.StateId);
            Assert.AreEqual(1, source.Transitions.Count(edge => edge.SourceStateId == anyId));
        }

        /// <summary>复制完整资产，逐一对照图、层、节点、边的新 ID 及其内部引用。</summary>
        [Test]
        public void WholeAssetCopyRemapsEveryTopologyIdAndInternalReference()
        {
            StateGraphAsset source = CreatePopulatedAsset();
            Assert.IsTrue(AssetDatabase.CopyAsset(SourcePath, CopyPath));
            AssetDatabase.ImportAsset(CopyPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphAsset copy = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(CopyPath);

            Assert.IsNotNull(copy);
            Assert.AreNotEqual(source.GraphId, copy.GraphId);
            Assert.AreEqual(source.Layers.Count, copy.Layers.Count);

            for (int layerIndex = 0; layerIndex < source.Layers.Count; layerIndex++)
            {
                StateGraphLayerRecord sourceLayer = source.Layers[layerIndex];
                StateGraphLayerRecord copyLayer = copy.Layers[layerIndex];
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
                    StateGraphTransitionRecord sourceTransition =
                        sourceLayer.Transitions[transitionIndex];
                    StateGraphTransitionRecord copyTransition =
                        copyLayer.Transitions[transitionIndex];
                }
            }

            var sourceLayerIds = new HashSet<SerializedId128>(source.Layers.Select(layer => layer.LayerId));
            var sourceStateIds = new HashSet<SerializedId128>(
                source.Layers.SelectMany(layer => layer.States).Select(state => state.StateId));
            var sourceTransitionIds = new HashSet<SerializedId128>(
                source.Layers.SelectMany(layer => layer.Transitions).Select(transition => transition.TransitionId));

            foreach (StateGraphLayerRecord layer in copy.Layers)
            {
                Assert.IsFalse(sourceLayerIds.Contains(layer.LayerId));
                Assert.IsTrue(layer.States.Any(state => state.StateId == layer.InitialStateId));
                foreach (StateGraphStateRecord state in layer.States)
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

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
                {
                    Assert.IsFalse(sourceTransitionIds.Contains(transition.TransitionId));
                    Assert.IsTrue(layer.States.Any(state => state.StateId == transition.SourceStateId));
                    Assert.IsTrue(layer.States.Any(state => state.StateId == transition.TargetStateId));
                }
            }
        }

        /// <summary>仅复制选中子树，检查其节点和内部转换生成新 ID，外部节点保持原身份。</summary>
        [Test]
        public void SubtreeCopyRemapsOnlyCopiedStatesAndInternalTransitions()
        {
            StateGraphAsset asset = CreatePopulatedAsset();
            StateGraphLayerRecord layer = asset.Layers[0];
            StateId sourceRootId = ToStateId(layer.States[0].StateId);
            var originalStateIds = new HashSet<SerializedId128>(layer.States.Select(state => state.StateId));
            var originalTransitionIds = new HashSet<SerializedId128>(
                layer.Transitions.Select(transition => transition.TransitionId));
            SerializedId128 outsideStateId = layer.States.Single(
                state => state.DisplayName == "Outside").StateId;

            Assert.IsTrue(StateGraphAuthoringOperations.DuplicateStateSubtree(
                asset,
                ToLayerId(layer.LayerId),
                sourceRootId,
                out StateId duplicatedRootId));

            Assert.AreEqual(5, layer.States.Count);
            Assert.AreEqual(4, layer.Transitions.Count);
            StateGraphStateRecord duplicatedRoot = layer.States.Single(state =>
                state.StateId.High == duplicatedRootId.High && state.StateId.Low == duplicatedRootId.Low);
            StateGraphStateRecord duplicatedChild = layer.States.Single(state =>
                !originalStateIds.Contains(state.StateId) && state.StateId != duplicatedRoot.StateId);
            StateGraphTransitionRecord duplicatedTransition = layer.Transitions.Single(transition =>
                !originalTransitionIds.Contains(transition.TransitionId));

            Assert.AreEqual(layer.States[0].ParentStateId, duplicatedRoot.ParentStateId);
            Assert.AreEqual(duplicatedChild.StateId, duplicatedRoot.InitialChildStateId);
            Assert.AreEqual(duplicatedRoot.StateId, duplicatedChild.ParentStateId);
            Assert.AreEqual(duplicatedChild.StateId, duplicatedTransition.SourceStateId);
            Assert.AreEqual(duplicatedChild.StateId, duplicatedTransition.TargetStateId);
            Assert.AreNotEqual(outsideStateId, duplicatedTransition.SourceStateId);
            Assert.AreNotEqual(outsideStateId, duplicatedTransition.TargetStateId);
            Assert.AreEqual(1, layer.Transitions.Count(transition =>
                !originalTransitionIds.Contains(transition.TransitionId)));
            foreach (SerializedId128 transitionId in originalTransitionIds)
            {
                Assert.AreEqual(
                    1,
                    layer.Transitions.Count(transition => transition.TransitionId == transitionId));
            }
        }

        /// <summary>复制图层并改动副本，验证所有拓扑引用已重映射，源配置不受修改。</summary>
        [Test]
        public void DuplicateLayerDeepCopiesAuthoringDataAndRemapsEveryTopologyReference()
        {
            StateGraphAsset asset = CreatePopulatedAsset();
            StateGraphLayerRecord source = asset.Layers[0];
            var sourceStateIds = new HashSet<SerializedId128>(
                source.States.Select(state => state.StateId));
            var sourceTransitionIds = new HashSet<SerializedId128>(
                source.Transitions.Select(transition => transition.TransitionId));
            StateGraphAuthoringOperations.AddLayer(asset, "Following");
            StateGraphLayerRecord following = asset.Layers[1];

            Assert.IsTrue(StateGraphAuthoringOperations.DuplicateLayer(
                asset,
                ToLayerId(source.LayerId),
                out LayerId duplicatedLayerId));

            Assert.AreEqual(3, asset.Layers.Count);
            Assert.AreSame(source, asset.Layers[0]);
            StateGraphLayerRecord duplicate = asset.Layers[1];
            Assert.AreSame(following, asset.Layers[2]);
            Assert.AreEqual(duplicatedLayerId, ToLayerId(duplicate.LayerId));
            Assert.AreNotEqual(source.LayerId, duplicate.LayerId);
            Assert.AreEqual(source.DisplayName, duplicate.DisplayName);
            Assert.AreEqual(source.States.Count, duplicate.States.Count);
            Assert.AreEqual(source.Transitions.Count, duplicate.Transitions.Count);

            var stateRemaps = new Dictionary<SerializedId128, SerializedId128>();
            for (int stateIndex = 0; stateIndex < source.States.Count; stateIndex++)
            {
                StateGraphStateRecord sourceState = source.States[stateIndex];
                StateGraphStateRecord duplicatedState = duplicate.States[stateIndex];
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
                StateGraphStateRecord sourceState = source.States[stateIndex];
                StateGraphStateRecord duplicatedState = duplicate.States[stateIndex];
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
                StateGraphTransitionRecord sourceTransition = source.Transitions[transitionIndex];
                StateGraphTransitionRecord duplicatedTransition =
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
            }
        }

        /// <summary>使用存在重复 ID 的图层执行复制，核对失败后源资产各项仍为原值。</summary>
        [Test]
        public void DuplicateLayerRejectsDuplicateTopologyIdsWithoutMutatingTheAsset()
        {
            StateGraphAsset asset = CreatePopulatedAsset();
            StateGraphLayerRecord source = asset.Layers[0];
            source.States[1].StateId = source.States[0].StateId;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            string[] topologyBefore = CaptureTopology(asset);

            Assert.IsFalse(StateGraphAuthoringOperations.DuplicateLayer(
                asset,
                ToLayerId(source.LayerId),
                out LayerId duplicatedLayerId));

            Assert.IsFalse(duplicatedLayerId.IsValid);
            Assert.AreEqual(1, asset.Layers.Count);
            CollectionAssert.AreEqual(topologyBefore, CaptureTopology(asset));
            Assert.IsFalse(EditorUtility.IsDirty(asset));
        }

        /// <summary>对含悬空引用的图层执行复制，验证编辑器拒绝操作并保留原资产。</summary>
        [Test]
        public void DuplicateLayerRejectsDanglingTopologyReferencesWithoutMutatingTheAsset()
        {
            StateGraphAsset asset = CreatePopulatedAsset();
            StateGraphLayerRecord source = asset.Layers[0];
            source.Transitions[0].TargetStateId = SerializedId128.NewId();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            string[] topologyBefore = CaptureTopology(asset);

            Assert.IsFalse(StateGraphAuthoringOperations.DuplicateLayer(
                asset,
                ToLayerId(source.LayerId),
                out LayerId duplicatedLayerId));

            Assert.IsFalse(duplicatedLayerId.IsValid);
            Assert.AreEqual(1, asset.Layers.Count);
            CollectionAssert.AreEqual(topologyBefore, CaptureTopology(asset));
            Assert.IsFalse(EditorUtility.IsDirty(asset));
        }

        /// <summary>复制资产后立即移动并重导入，对照首次复制得到的 ID 没有再次重映射。</summary>
        [Test]
        public void WholeAssetCopyKeepsItsFirstRemapAcrossImmediateMoveAndReimport()
        {
            CreatePopulatedAsset();
            Assert.IsTrue(AssetDatabase.CopyAsset(SourcePath, CopyPath));
            AssetDatabase.ImportAsset(CopyPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphAsset copy = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(CopyPath);
            string copyGuid = AssetDatabase.AssetPathToGUID(CopyPath);
            Assert.AreEqual(copyGuid, copy.AssetGuidStamp);
            string[] firstRemap = CaptureTopology(copy);

            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(CopyPath, MovedCopyPath));
            AssetDatabase.ImportAsset(MovedCopyPath, ImportAssetOptions.ForceSynchronousImport);
            InvokePendingIdentitySave();
            Resources.UnloadAsset(copy);
            AssetDatabase.ImportAsset(MovedCopyPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<StateGraphAsset>(MovedCopyPath);

            Assert.AreEqual(copyGuid, AssetDatabase.AssetPathToGUID(MovedCopyPath));
            Assert.AreEqual(copyGuid, reloaded.AssetGuidStamp);
            CollectionAssert.AreEqual(firstRemap, CaptureTopology(reloaded));
        }

        #endregion

        #region Internal Logic

        private const string TestRoot = "Assets/__CoCoFlowStateGraphCopyTests";
        private const string SourcePath = TestRoot + "/Source.asset";
        private const string CopyPath = TestRoot + "/Copy.asset";
        private const string MovedCopyPath = TestRoot + "/MovedCopy.asset";

        private static StateGraphAsset CreatePopulatedAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(asset, SourcePath);
            string guid = AssetDatabase.AssetPathToGUID(SourcePath);
            asset.EnsureAssetIdentity(guid);
            Assert.IsTrue(asset.GraphId.IsValid);
            Assert.AreEqual(guid, asset.AssetGuidStamp);
            StateDescriptorId descriptorId = CreateStateDescriptorId(10UL);
            var sharedConfig = new TestStateAuthoringConfig { Value = 17 };
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId rootStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                sharedConfig,
                displayName: "Root");
            StateId childStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                rootStateId,
                descriptorId,
                sharedConfig,
                displayName: "Child");
            StateId outsideStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Outside");
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                childStateId,
                outsideStateId,
                30);
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                outsideStateId,
                childStateId,
                20);
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                childStateId,
                childStateId,
                10);
            var serialized = new SerializedObject(asset);
            SerializedProperty firstTransition = serialized.FindProperty("layers")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("transitions")
                .GetArrayElementAtIndex(0);
            firstTransition.FindPropertyRelative("windowMode").enumValueIndex =
                (int)TransitionWindowMode.ActionProgress;
            firstTransition.FindPropertyRelative("windowStartInclusive").doubleValue = 0.25d;
            firstTransition.FindPropertyRelative("windowEndExclusive").doubleValue = 0.75d;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private static StateDescriptorId CreateStateDescriptorId(ulong low)
        {
            Assert.IsTrue(StateDescriptorId.TryCreate(1UL, low, out StateDescriptorId id));
            return id;
        }

        private static LayerId ToLayerId(SerializedId128 id)
        {
            Assert.IsTrue(LayerId.TryCreate(id.High, id.Low, out LayerId value));
            return value;
        }

        private static StateId ToStateId(SerializedId128 id)
        {
            Assert.IsTrue(StateId.TryCreate(id.High, id.Low, out StateId value));
            return value;
        }

        private static string[] CaptureTopology(StateGraphAsset asset)
        {
            var values = new List<string>
            {
                asset.GraphId.ToString(),
                asset.AssetGuidStamp
            };
            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                values.Add(Format(layer.LayerId));
                values.Add(Format(layer.InitialStateId));
                foreach (StateGraphStateRecord state in layer.States)
                {
                    values.Add(Format(state.StateId));
                    values.Add(Format(state.ParentStateId));
                    values.Add(Format(state.InitialChildStateId));
                }

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
                {
                    values.Add(Format(transition.TransitionId));
                    values.Add(Format(transition.SourceStateId));
                    values.Add(Format(transition.TargetStateId));
                }
            }

            return values.ToArray();
        }

        private static string Format(SerializedId128 id) =>
            $"{id.High:x16}{id.Low:x16}";

        private static void InvokePendingIdentitySave()
        {
            Type postprocessorType = typeof(StateGraphAuthoringOperations).Assembly.GetType(
                "CoCoFlow.Editor.StateGraph.StateGraphAssetIdentityPostprocessor",
                throwOnError: true);
            MethodInfo saveMethod = postprocessorType.GetMethod(
                "SavePendingAssets",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(saveMethod);
            saveMethod.Invoke(null, null);
        }

        #endregion
    }
}
