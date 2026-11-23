using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphFingerprintTests
    {
        private const string TestRoot = "Assets/__CoCoFlowStateGraphFingerprintTests";
        private const string SourcePath = TestRoot + "/Source.asset";
        private const string MovedPath = TestRoot + "/Moved.asset";

        private GraphDescriptorCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowStateGraphFingerprintTests");
            catalog = StateGraphTestFactory.CreateCatalog(false);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void NestedFingerprintTypeNamesKeepTheirPublishedModuleIdentities()
        {
            string name = GraphTypeIdentity.FullName(typeof(Dictionary<StateId, StateGraphAsset[]>));
            Assert.That(name, Does.Contain("CoCoFlow.Runtime.Core.CoCoStateId, CoCoFlow.Runtime.Core.Contracts,"));
            Assert.That(name, Does.Contain("CoCoFlow.Runtime.Core.CoCoStateGraphAsset[], CoCoFlow.Runtime.Core.StateGraphAuthoring,"));
            Assert.That(GraphTypeIdentity.FullName(typeof(TestStateAuthoringConfig)),
                Is.EqualTo(typeof(TestStateAuthoringConfig).FullName));
        }

        [Test]
        public void FixedLegacyGraphRetainsContentFingerprint()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            try
            {
                asset.EnsureAssetIdentity("fixed-legacy-fingerprint");
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("graphId").FindPropertyRelative("high").ulongValue = 1UL;
                serialized.FindProperty("graphId").FindPropertyRelative("low").ulongValue = 1UL;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var layer = new StateGraphLayerRecord(new SerializedId128(2UL, 1UL), "Legacy");
                layer.InitialStateId = new SerializedId128(3UL, 10UL);
                layer.States.Add(new StateGraphStateRecord(layer.InitialStateId, default, "Idle",
                    new SerializedId128(5UL, 1UL), new TestStateAuthoringConfig { Value = 10 }));
                asset.Layers.Add(layer);
                const ulong legacyFingerprint = 9279485480042731979UL;
                Assert.AreEqual(legacyFingerprint, Snapshot(asset).ContentFingerprint);
                catalog = StateGraphTestFactory.CreateCatalog(false, includeAnyState: true);
                layer.AnyState = new StateGraphStateRecord(new SerializedId128(3UL, 40UL), default,
                    "Any", new SerializedId128(5UL, 2UL), new TestStateAuthoringConfig { Value = 4 });
                ulong configuredFingerprint = Snapshot(asset).ContentFingerprint;
                Assert.AreNotEqual(legacyFingerprint, configuredFingerprint);
                ((TestStateAuthoringConfig)layer.AnyState.Config).Value = 5;
                Assert.AreNotEqual(configuredFingerprint, Snapshot(asset).ContentFingerprint);
                layer.AnyState = null;
                Assert.AreEqual(legacyFingerprint, Snapshot(asset).ContentFingerprint);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DisplayNamesAssetPathAndGuidStampDoNotChangeContentFingerprint()
        {
            StateGraphAsset asset = CreateSavedAsset();
            ulong baseline = Snapshot(asset).ContentFingerprint;

            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(SourcePath, MovedPath));
            AssetDatabase.ImportAsset(MovedPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphAsset moved = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(MovedPath);
            Assert.AreEqual(baseline, Snapshot(moved).ContentFingerprint);

            StateGraphAsset presentationClone = Object.Instantiate(moved);
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
            StateGraphAsset asset = CreateSavedAsset();
            ulong baseline = Snapshot(asset).ContentFingerprint;

            StateGraphAsset configClone = Object.Instantiate(asset);
            StateGraphAsset topologyClone = Object.Instantiate(asset);
            StateGraphAsset windowClone = Object.Instantiate(asset);
            StateGraphAsset conditionOrderClone = Object.Instantiate(asset);
            try
            {
                ((TestStateAuthoringConfig)configClone.Layers[0].States[0].Config).Value = 99;
                Assert.AreNotEqual(baseline, Snapshot(configClone).ContentFingerprint);

                StateGraphAuthoringOperations.AddState(
                    topologyClone,
                    ToLayerId(topologyClone.Layers[0].LayerId),
                    default,
                    StateGraphTestFactory.StateDescriptorId,
                    new TestStateAuthoringConfig { Value = 3 },
                    "Extra");
                Assert.AreNotEqual(baseline, Snapshot(topologyClone).ContentFingerprint);

                var serializedWindow = new SerializedObject(windowClone);
                SerializedProperty transition = serializedWindow.FindProperty("layers")
                    .GetArrayElementAtIndex(0)
                    .FindPropertyRelative("transitions")
                    .GetArrayElementAtIndex(0);
                transition.FindPropertyRelative("windowMode").enumValueIndex =
                    (int)TransitionWindowMode.LocalSeconds;
                transition.FindPropertyRelative("windowStartInclusive").doubleValue = 0.1d;
                transition.FindPropertyRelative("windowEndExclusive").doubleValue = 0.5d;
                serializedWindow.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreNotEqual(baseline, Snapshot(windowClone).ContentFingerprint);

                StateGraphConditionRecord first =
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
            StateGraphAsset asset = CreateSavedAsset();
            LayerId secondLayerId = StateGraphAuthoringOperations.AddLayer(asset, "Upper");
            StateGraphAuthoringOperations.AddState(
                asset,
                secondLayerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 3 },
                "Upper State");
            StateGraphAsset reversed = Object.Instantiate(asset);
            try
            {
                StateGraphAssetSnapshot originalSnapshot = Snapshot(asset);
                reversed.Layers.Reverse();
                StateGraphAssetSnapshot reversedSnapshot = Snapshot(reversed);

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

        private static StateGraphAsset CreateSavedAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(asset, SourcePath);
            string guid = AssetDatabase.AssetPathToGUID(SourcePath);
            asset.EnsureAssetIdentity(guid);
            Assert.IsTrue(asset.GraphId.IsValid);
            Assert.AreEqual(guid, asset.AssetGuidStamp);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId firstStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                "Idle");
            StateId secondStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 2 },
                "Run");
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                firstStateId,
                secondStateId,
                priority: 0);
            StateGraphTransitionRecord transition = asset.Layers[0].Transitions[0];
            transition.Conditions.Add(new StateGraphConditionRecord(
                Serialize(StateGraphTestFactory.ConditionDescriptorId),
                new TestConditionAuthoringConfig { Threshold = 2 }));
            transition.Conditions.Add(new StateGraphConditionRecord(
                Serialize(StateGraphTestFactory.ConditionDescriptorId),
                new TestConditionAuthoringConfig { Threshold = 8 }));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private StateGraphAssetSnapshot Snapshot(StateGraphAsset asset) =>
            StateGraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                StateGraphManagedReferenceInspection.Empty);

        private static SerializedId128 Serialize(ConditionDescriptorId id) =>
            new SerializedId128(id.High, id.Low);

        private static LayerId ToLayerId(SerializedId128 id)
        {
            Assert.IsTrue(LayerId.TryCreate(id.High, id.Low, out LayerId layerId));
            return layerId;
        }
    }
}
