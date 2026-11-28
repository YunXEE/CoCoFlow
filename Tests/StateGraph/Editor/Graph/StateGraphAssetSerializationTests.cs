using System.Collections.Generic;
using System.IO;
using CoCoFlow.Runtime.Input.Contracts;
using System.Linq;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateGraphAssetSerializationTests
    {

        #region Public API

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
        public void RetiredPrefixedRecordIsReportedAsMissing()
        {
            const string fixture = "Packages/com.yunxee.cocoflow/Tests/StateGraph/Runtime/Legacy/BeforePrefixRemoval.asset.bytes";
            File.WriteAllBytes(AssetPath, AssetDatabase.LoadAssetAtPath<TextAsset>(fixture).bytes);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            var graph = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            var missing = SerializationUtility.GetManagedReferencesWithMissingTypes(graph);
            Assert.That(missing.Select(item => item.className), Does.Contain("CoCoStateGraphStateRecord"));
            Assert.That(missing.Select(item => item.className), Does.Contain("CoCoFieldConfig"));
            Assert.IsFalse(new GraphAssetCompiler().Compile(graph, StateGraphTestFactory.CreateCatalog(false)).Succeeded);
        }

        [Test]
        public void Released0410FieldConfigKeepsItsSharedReferenceAcrossCopyAndResave()
        {
            const string fixture = "Packages/com.yunxee.cocoflow/Tests/StateGraph/Runtime/Legacy/Released0410FieldConfig.asset.bytes";
            const string originalGuid = "bb252358fa1b641289742688e2010a99";
            bool originalIsPresent = !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(originalGuid));
            File.WriteAllBytes(AssetPath, AssetDatabase.LoadAssetAtPath<TextAsset>(fixture).bytes);
            if (!originalIsPresent)
                File.WriteAllText(AssetPath + ".meta", $"fileFormatVersion: 2\nguid: {originalGuid}\n");
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            var graph = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            Assert.IsTrue(GraphId.TryCreate(3816462399227382036UL, 12994049928394231811UL, out var releasedId));
            if (originalIsPresent)
            {
                var original = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetDatabase.GUIDToAssetPath(originalGuid));
                Assert.IsNotNull(original);
                Assert.AreEqual(releasedId, original.GraphId);
                CheckValues(original);
                Assert.AreNotEqual(releasedId, graph.GraphId, "导入副本应获得自己的图身份。");
            }
            else
                Assert.AreEqual(releasedId, graph.GraphId);
            var identity = graph.GraphId;
            CheckValues(graph);
            string duplicate = TestRoot + "/duplicate.asset";
            Assert.IsTrue(AssetDatabase.CopyAsset(AssetPath, duplicate));
            AssetDatabase.ImportAsset(duplicate, ImportAssetOptions.ForceSynchronousImport);
            var copy = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(duplicate);
            Assert.AreNotEqual(identity, copy.GraphId);
            CheckValues(copy);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssetIfDirty(graph);
            Resources.UnloadAsset(graph);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            graph = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            Assert.AreEqual(identity, graph.GraphId);
            CheckValues(graph);

            static void CheckValues(StateGraphAsset asset)
            {
                Assert.IsFalse(SerializationUtility.HasManagedReferencesWithMissingTypes(asset));
                var layer = asset.Layers.Single();
                Assert.That(layer.AnyState.DisplayName, Is.EqualTo("Any"));
                Assert.That(layer.AnyState.Config, Is.TypeOf<EmptyStateConfig>());
                Assert.That(layer.States[0].Config, Is.TypeOf<FieldConfig>());
                Assert.AreSame(layer.States[0].Config, layer.States[1].Config);
                CollectionAssert.AreEqual(new[] { "Speed=3" }, ((FieldConfig)layer.States[0].Config).Values);
            }
        }

        [Test]
        public void Released0410SharedConfigAndGraphIdentitiesSurviveImportAndResave()
        {
            const string fixture = "Packages/com.yunxee.cocoflow/Tests/StateGraph/Runtime/Legacy/Released0410Shared.asset.bytes";
            File.WriteAllBytes(AssetPath, AssetDatabase.LoadAssetAtPath<TextAsset>(fixture).bytes);
            File.WriteAllText(AssetPath + ".meta", "fileFormatVersion: 2\nguid: c7723ff27124f4f0fba916bff8269504\n");
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            Verify(asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            Verify(AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath));

            static void Verify(StateGraphAsset graph)
            {
                Assert.IsFalse(SerializationUtility.HasManagedReferencesWithMissingTypes(graph));
                var layer = graph.Layers.Single();
                Assert.That(layer.States.Select(state => state.DisplayName), Is.EqualTo(new[] { "First", "Second" }));
                Assert.That(layer.States[0].Config, Is.SameAs(layer.States[1].Config));
                Assert.AreEqual(27, ((TestStateAuthoringConfig)layer.States[0].Config).Value);
                Assert.AreEqual(73, ((TestStateAuthoringConfig)layer.AnyState.Config).Value);
                Assert.AreEqual(layer.States[0].StateId, layer.InitialStateId);
                var edge = layer.Transitions.Single();
                Assert.AreEqual(layer.InitialStateId, edge.SourceStateId);
                Assert.AreEqual(layer.States[1].StateId, edge.TargetStateId);
                Assert.AreEqual(7, edge.Priority);
                var catalog = StateGraphTestFactory.CreateCatalog(false, includeAnyState: true);
                var result = new GraphAssetCompiler(new GraphAssetCompilationCache()).Compile(graph, catalog);
                Assert.IsTrue(result.Succeeded, string.Join(", ", result.Diagnostics.Select(d => d.Diagnostic.Message)));
                Assert.AreEqual(17614652859590910492UL, result.ContentFingerprint);
                Assert.AreEqual(11069643992310798211UL, result.Graph.CatalogFingerprint);
                Assert.AreEqual("b4a2b4ceea50e156e4f3596ca4fe617b", result.Graph.IntentRequirements.LayoutId.ToString());
                Assert.AreEqual("4c71f553f86c1a55fcd776c4e0493bb7", result.Graph.OperationProvides.LayoutId.ToString());
                Assert.AreEqual("fd73df8be5570214ad2b2a775e3bb0d6", result.Graph.ContextStateRequirements.LayoutId.ToString());
            }
        }

        [Test]
        public void Released0410InputAndMemoryKeepTheirPublishedTypeIdentity()
        {
            Assert.AreEqual("CoCoFlow.Runtime.Core.StateGraph", GraphTypeIdentity.AssemblyName(typeof(StatelessMemory)));
            Assert.AreEqual("CoCoFlow.Runtime.Core.StatelessMemory", GraphTypeIdentity.FullName(typeof(StatelessMemory)));
            Assert.AreEqual("CoCoFlow.Runtime.Core.Contracts", GraphTypeIdentity.AssemblyName(typeof(InputIntentContract)));
            Assert.AreEqual("CoCoFlow.Runtime.Core.InputIntentData", GraphTypeIdentity.FullName(typeof(InputIntentContract)));
            Assert.AreEqual(3660957098222782772UL, HostBindingIds.Hash(typeof(InputIntentContract)));
            Assert.AreEqual("System.Collections.Generic.Dictionary`2[[CoCoFlow.Runtime.Core.CoCoStateId, CoCoFlow.Runtime.Core.Contracts, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null],[CoCoFlow.Runtime.Core.StatelessMemory[], CoCoFlow.Runtime.Core.StateGraph, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]", GraphTypeIdentity.FullName(typeof(Dictionary<StateId, StatelessMemory[]>)));
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void OptionalAnyStateSurvivesSaveReloadAndUndo(bool configured)
        {
            StateGraphAsset asset = CreateSavedAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Any");
            StateId leaf = StateGraphAuthoringOperations.AddState(asset, layerId, default,
                StateGraphTestFactory.StateDescriptorId, new TestStateAuthoringConfig { Value = 1 });
            StateId anyId = default;
            if (configured)
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false, includeAnyState: true);
                Assert.IsTrue(catalog.TryGetStateDescriptor(StateGraphTestFactory.AnyStateDescriptorId, out var descriptor));
                Assert.IsTrue(StateGraphAuthoringOperations.TryAddAnyState(asset, layerId, descriptor,
                    new TestStateAuthoringConfig { Value = 7 }, Vector2.zero, out anyId, out _));
                StateGraphAuthoringOperations.AddTransition(asset, layerId, anyId, leaf, 0);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                Assert.IsTrue(StateGraphAuthoringOperations.TryDeleteAnyState(asset, layerId, out _));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(anyId, ToStateId(asset.Layers[0].AnyState.StateId));
                Undo.PerformRedo();
                Assert.IsNull(asset.Layers[0].AnyState);
                Undo.PerformUndo();
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            asset = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            Assert.AreEqual(configured, asset.Layers[0].AnyState != null);
            Assert.AreEqual(1, asset.Layers[0].States.Count);
            if (!configured) return;
            Assert.AreEqual(anyId, ToStateId(asset.Layers[0].AnyState.StateId));
            Assert.AreEqual(7, ((TestStateAuthoringConfig)asset.Layers[0].AnyState.Config).Value);
            Assert.AreEqual(asset.Layers[0].AnyState.StateId, asset.Layers[0].Transitions[0].SourceStateId);
            Assert.IsTrue(new GraphAssetCompiler(new GraphAssetCompilationCache()).Compile(
                asset, StateGraphTestFactory.CreateCatalog(false, includeAnyState: true)).Succeeded);
        }

        [Test]
        public void SaveReimportRenameMoveAndReorderPreserveStableIds()
        {
            StateGraphAsset asset = CreateSavedAsset();
            StateDescriptorId descriptorId = CreateStateDescriptorId(10UL);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            var firstConfig = new TestStateAuthoringConfig { Value = 7 };
            StateId firstStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                firstConfig,
                displayName: "Idle");
            StateId secondStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Run");
            GraphId graphId = asset.GraphId;
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
            StateGraphAsset reloaded = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(movedPath);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(graphId, reloaded.GraphId);
            Assert.AreEqual(layerId, ToLayerId(reloaded.Layers[0].LayerId));
            Assert.AreEqual(secondStateId, ToStateId(reloaded.Layers[0].States[0].StateId));
            Assert.AreEqual(firstStateId, ToStateId(reloaded.Layers[0].States[1].StateId));
            Assert.AreEqual(11, ((TestStateAuthoringConfig)reloaded.Layers[0].States[1].Config).Value);
        }

        [Test]
        public void TransitionContractChangesInPlaceWithinSchemaV1()
        {
            StateGraphAsset asset = CreateSavedAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId stateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CreateStateDescriptorId(11UL),
                displayName: "Action");
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                stateId,
                stateId,
                priority: 4);
            var serialized = new SerializedObject(asset);
            SerializedProperty transition = serialized.FindProperty("layers")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("transitions")
                .GetArrayElementAtIndex(0);

            Assert.AreEqual(1U, asset.SchemaVersion);
            Assert.IsNull(transition.FindPropertyRelative("interruptPolicy"));
            transition.FindPropertyRelative("windowMode").intValue =
                (int)TransitionWindowMode.ActionProgress;
            transition.FindPropertyRelative("windowStartInclusive").doubleValue = 0.25d;
            transition.FindPropertyRelative("windowEndExclusive").doubleValue = 0.75d;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            StateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            Assert.AreEqual(1U, reloaded.SchemaVersion);
            Assert.AreEqual(
                TransitionWindowMode.ActionProgress,
                reloaded.Layers[0].Transitions[0].WindowMode);
        }

        [Test]
        public void AuthoringOperationsParticipateInUndoAndRedoWithoutChangingRestoredIds()
        {
            StateGraphAsset asset = CreateSavedAsset();
            LayerId addedLayerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
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
            StateGraphAsset asset = CreateSavedAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateDescriptorId descriptorId = CreateStateDescriptorId(20UL);
            StateId rootStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Root");
            StateId childStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                rootStateId,
                descriptorId,
                displayName: "Child");
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                childStateId,
                childStateId,
                priority: 0);
            Undo.FlushUndoRecordObjects();
            Undo.ClearAll();
            StateGraphLayerRecord layer = asset.Layers[0];
            var originalStateIds = new HashSet<SerializedId128>(
                layer.States.Select(state => state.StateId));
            var originalTransitionIds = new HashSet<SerializedId128>(
                layer.Transitions.Select(transition => transition.TransitionId));

            Assert.IsTrue(StateGraphAuthoringOperations.DuplicateStateSubtree(
                asset,
                layerId,
                rootStateId,
                out StateId duplicatedRootId));
            Undo.FlushUndoRecordObjects();
            var duplicatedStateIds = new HashSet<SerializedId128>(
                layer.States.Where(state => !originalStateIds.Contains(state.StateId))
                    .Select(state => state.StateId));
            var duplicatedTransitionIds = new HashSet<SerializedId128>(
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
            StateGraphAsset asset = CreateSavedAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateDescriptorId descriptorId = CreateStateDescriptorId(30UL);
            StateId rootStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Root");
            StateId childStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                rootStateId,
                descriptorId,
                displayName: "Child");
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                childStateId,
                childStateId,
                priority: 0);
            Undo.FlushUndoRecordObjects();
            Undo.ClearAll();

            Assert.IsTrue(StateGraphAuthoringOperations.DuplicateLayer(
                asset,
                layerId,
                out LayerId duplicatedLayerId));
            Undo.FlushUndoRecordObjects();
            Assert.AreEqual(2, asset.Layers.Count);
            StateGraphLayerRecord duplicate = asset.Layers[1];
            SerializedId128 duplicatedSerializedLayerId = duplicate.LayerId;
            SerializedId128[] duplicatedStateIds = duplicate.States
                .Select(state => state.StateId)
                .ToArray();
            SerializedId128[] duplicatedTransitionIds = duplicate.Transitions
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
            StateGraphAsset asset = CreateSavedAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            var sharedConfig = new TestStateAuthoringConfig { Value = 23 };
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                sharedConfig,
                "First");
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                sharedConfig,
                "Second");
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            GraphAssetCompileResult beforeReload =
                new GraphAssetCompiler(new GraphAssetCompilationCache())
                    .Compile(asset, catalog);
            Assert.IsTrue(beforeReload.Succeeded);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            StateGraphAsset reloaded = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            GraphAssetCompileResult afterReload =
                new GraphAssetCompiler(new GraphAssetCompilationCache())
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

        [System.Serializable]
        public struct ConfigCollectionSerializationWindow
        {
            public float startSeconds;
            public double endSeconds;
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(3, true)]
        public void StandardConfigCollectionsSaveReloadAndUndoReset(int count, bool list)
        {
            StateGraphAsset asset = CreateSavedAsset();
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Collections");
            var windows = Enumerable.Range(0, count).Select(i => new ConfigCollectionSerializationWindow
                { startSeconds = i + 0.25f, endSeconds = i + 0.75d }).Reverse().Take(2).ToArray();
            var config = new FieldConfig();
            string clipKey = count == 0 ? string.Empty : "  Attack=Light.$count[0]  ";
            config.Set("ClipKey", clipKey);
            config.Set("Windows", list ? windows.ToList() : windows);
            config.Set("Numbers", list ? new List<float> { 2.5f } : new[] { 2.5f });
            string[] expected = config.Values.ToArray();
            StateGraphAuthoringOperations.AddState(asset, layerId, default, CreateStateDescriptorId(31UL), config);
            Undo.ClearAll();
            var serialized = new SerializedObject(asset);
            var property = serialized.FindProperty("layers").GetArrayElementAtIndex(0)
                .FindPropertyRelative("states").GetArrayElementAtIndex(0).FindPropertyRelative("config");
            property.managedReferenceValue = new FieldConfig();
            Assert.IsTrue(serialized.ApplyModifiedProperties());
            Assert.IsEmpty(((FieldConfig)asset.Layers[0].States[0].Config).Values);
            Undo.PerformUndo();
            CollectionAssert.AreEqual(expected, ((FieldConfig)asset.Layers[0].States[0].Config).Values);
            Undo.PerformRedo();
            Assert.IsEmpty(((FieldConfig)asset.Layers[0].States[0].Config).Values);
            Undo.PerformUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            var restored = (FieldConfig)reloaded.Layers[0].States[0].Config;
            CollectionAssert.AreEqual(expected, restored.Values);
            Assert.Contains("ClipKey=" + clipKey, restored.Values);
            Assert.Contains("Windows.$count=" + windows.Length, restored.Values);
            Assert.IsFalse(restored.Values.Any(entry => entry.StartsWith("Windows[2]")));
        }

        #endregion

        #region Internal Logic

        private const string TestRoot = "Assets/__CoCoFlowStateGraphSerializationTests";
        private const string AssetPath = TestRoot + "/StateGraph.asset";

        private static StateGraphAsset CreateSavedAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(guid);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Assert.IsTrue(asset.GraphId.IsValid);
            Assert.AreEqual(guid, asset.AssetGuidStamp);
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

        #endregion
    }
}
