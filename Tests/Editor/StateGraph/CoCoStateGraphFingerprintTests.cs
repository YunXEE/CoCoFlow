using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphFingerprintTests
    {
        private const string TestRoot = "Assets/__CoCoFlowStateGraphFingerprintTests";
        private const string SourcePath = TestRoot + "/Source.asset";
        private const string MovedPath = TestRoot + "/Moved.asset";

        private CoCoGraphDescriptorCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowStateGraphFingerprintTests");
            catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void DisplayNamesAssetPathAndGuidStampDoNotChangeContentFingerprint()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            ulong baseline = Snapshot(asset).ContentFingerprint;

            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(SourcePath, MovedPath));
            AssetDatabase.ImportAsset(MovedPath, ImportAssetOptions.ForceSynchronousImport);
            CoCoStateGraphAsset moved = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(MovedPath);
            Assert.AreEqual(baseline, Snapshot(moved).ContentFingerprint);

            CoCoStateGraphAsset presentationClone = Object.Instantiate(moved);
            try
            {
                var serialized = new SerializedObject(presentationClone);
                serialized.FindProperty("assetGuidStamp").stringValue = "presentation-only-stamp";
                SerializedProperty layer = serialized.FindProperty("layers").GetArrayElementAtIndex(0);
                layer.FindPropertyRelative("displayName").stringValue = "Renamed Layer";
                layer.FindPropertyRelative("states")
                    .GetArrayElementAtIndex(0)
                    .FindPropertyRelative("displayName")
                    .stringValue = "Renamed State";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(baseline, Snapshot(presentationClone).ContentFingerprint);
            }
            finally
            {
                Object.DestroyImmediate(presentationClone);
            }
        }

        [Test]
        public void ConfigTopologyWindowAndConditionOrderEachChangeContentFingerprint()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            ulong baseline = Snapshot(asset).ContentFingerprint;

            CoCoStateGraphAsset configClone = Object.Instantiate(asset);
            CoCoStateGraphAsset topologyClone = Object.Instantiate(asset);
            CoCoStateGraphAsset windowClone = Object.Instantiate(asset);
            CoCoStateGraphAsset conditionOrderClone = Object.Instantiate(asset);
            try
            {
                ((TestStateAuthoringConfig)configClone.Layers[0].States[0].Config).Value = 99;
                Assert.AreNotEqual(baseline, Snapshot(configClone).ContentFingerprint);

                CoCoStateGraphAuthoringOperations.AddState(
                    topologyClone,
                    ToLayerId(topologyClone.Layers[0].LayerId),
                    default,
                    CoCoStateGraphTestFactory.StateDescriptorId,
                    new TestStateAuthoringConfig { Value = 3 },
                    "Extra");
                Assert.AreNotEqual(baseline, Snapshot(topologyClone).ContentFingerprint);

                var serializedWindow = new SerializedObject(windowClone);
                SerializedProperty transition = serializedWindow.FindProperty("layers")
                    .GetArrayElementAtIndex(0)
                    .FindPropertyRelative("transitions")
                    .GetArrayElementAtIndex(0);
                transition.FindPropertyRelative("windowMode").enumValueIndex =
                    (int)CoCoTransitionWindowMode.LocalSeconds;
                transition.FindPropertyRelative("windowStartInclusive").doubleValue = 0.1d;
                transition.FindPropertyRelative("windowEndExclusive").doubleValue = 0.5d;
                serializedWindow.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreNotEqual(baseline, Snapshot(windowClone).ContentFingerprint);

                CoCoStateGraphConditionRecord first =
                    conditionOrderClone.Layers[0].Transitions[0].Conditions[0];
                conditionOrderClone.Layers[0].Transitions[0].Conditions[0] =
                    conditionOrderClone.Layers[0].Transitions[0].Conditions[1];
                conditionOrderClone.Layers[0].Transitions[0].Conditions[1] = first;
                Assert.AreNotEqual(baseline, Snapshot(conditionOrderClone).ContentFingerprint);
            }
            finally
            {
                Object.DestroyImmediate(configClone);
                Object.DestroyImmediate(topologyClone);
                Object.DestroyImmediate(windowClone);
                Object.DestroyImmediate(conditionOrderClone);
            }
        }

        [Test]
        public void LayerListOrderChangesContentFingerprintAndSnapshotOrder()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoLayerId secondLayerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Upper");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                secondLayerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 3 },
                "Upper State");
            CoCoStateGraphAsset reversed = Object.Instantiate(asset);
            try
            {
                CoCoStateGraphAssetSnapshot originalSnapshot = Snapshot(asset);
                reversed.Layers.Reverse();
                CoCoStateGraphAssetSnapshot reversedSnapshot = Snapshot(reversed);

                Assert.AreNotEqual(
                    originalSnapshot.ContentFingerprint,
                    reversedSnapshot.ContentFingerprint);
                Assert.AreEqual(
                    ToLayerId(asset.Layers[0].LayerId),
                    originalSnapshot.Source.Layers[0].LayerId);
                Assert.AreEqual(
                    ToLayerId(reversed.Layers[0].LayerId),
                    reversedSnapshot.Source.Layers[0].LayerId);
            }
            finally
            {
                Object.DestroyImmediate(reversed);
            }
        }

        private static CoCoStateGraphAsset CreateSavedAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            AssetDatabase.CreateAsset(asset, SourcePath);
            string guid = AssetDatabase.AssetPathToGUID(SourcePath);
            asset.EnsureAssetIdentity(guid);
            Assert.IsTrue(asset.GraphId.IsValid);
            Assert.AreEqual(guid, asset.AssetGuidStamp);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId firstStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                "Idle");
            CoCoStateId secondStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 2 },
                "Run");
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                firstStateId,
                secondStateId,
                priority: 0);
            CoCoStateGraphTransitionRecord transition = asset.Layers[0].Transitions[0];
            transition.Conditions.Add(new CoCoStateGraphConditionRecord(
                Serialize(CoCoStateGraphTestFactory.ConditionDescriptorId),
                new TestConditionAuthoringConfig { Threshold = 2 }));
            transition.Conditions.Add(new CoCoStateGraphConditionRecord(
                Serialize(CoCoStateGraphTestFactory.ConditionDescriptorId),
                new TestConditionAuthoringConfig { Threshold = 8 }));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private CoCoStateGraphAssetSnapshot Snapshot(CoCoStateGraphAsset asset) =>
            CoCoStateGraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                CoCoStateGraphManagedReferenceInspection.Empty);

        private static CoCoSerializedId128 Serialize(CoCoConditionDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoLayerId ToLayerId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(id.High, id.Low, out CoCoLayerId layerId));
            return layerId;
        }
    }
}
