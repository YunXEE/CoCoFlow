using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// EditMode 分别改变图内容和布局，核对指纹差异及旧类型和外部类型的身份。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>EditMode 分别改变图内容和布局，核对指纹差异及旧类型和外部类型的身份。</summary>
    public sealed class StateGraphFingerprintTests
    {

        #region Public API

        /// <summary>清理上一次的临时资产并为本例创建独立测试目录。</summary>
        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowStateGraphFingerprintTests");
            catalog = StateGraphTestFactory.CreateCatalog(false);
        }

        /// <summary>释放本次资产测试使用的临时目录并刷新 AssetDatabase。</summary>
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        /// <summary>构造含泛型、数组和同名项目类型的类型树，逐一核对发布时程序集身份与项目原名。</summary>
        [Test]
        public void NestedFingerprintTypeNamesKeepTheirPublishedModuleIdentities()
        {
            string name = GraphTypeIdentity.FullName(typeof(Dictionary<StateId, StateGraphAsset[]>));
            Assert.That(name, Does.Contain("CoCoFlow.Runtime.Core.CoCoStateId, CoCoFlow.Runtime.Core.Contracts,"));
            Assert.That(name, Does.Contain("CoCoFlow.Runtime.Core.CoCoStateGraphAsset[], CoCoFlow.Runtime.Core.StateGraphAuthoring,"));
            Assert.That(GraphTypeIdentity.FullName(typeof(TestStateAuthoringConfig)),
                Is.EqualTo(typeof(TestStateAuthoringConfig).FullName));
            var projectType = typeof(ProjectTypes.StatelessMemory);
            Assert.AreEqual(projectType.FullName, GraphTypeIdentity.FullName(projectType));
            Assert.AreEqual(projectType.Assembly.GetName().Name, GraphTypeIdentity.AssemblyName(projectType));
            var projectArray = typeof(List<ProjectTypes.StatelessMemory[]>);
            Assert.AreEqual(projectArray.FullName, GraphTypeIdentity.FullName(projectArray));
        }

        /// <summary>导入具有固定身份的历史图，按已知值对照编译得到的内容指纹。</summary>
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

        /// <summary>只改变显示名称、资产路径与 GUID 标记，验证图内容指纹保持原值。</summary>
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

        /// <summary>分别改变配置、拓扑或转换窗口，验证每一项都会改变图内容指纹。</summary>
        [Test]
        public void ConfigTopologyAndWindowEachChangeContentFingerprint()
        {
            StateGraphAsset asset = CreateSavedAsset();
            ulong baseline = Snapshot(asset).ContentFingerprint;

            StateGraphAsset configClone = Object.Instantiate(asset);
            StateGraphAsset topologyClone = Object.Instantiate(asset);
            StateGraphAsset windowClone = Object.Instantiate(asset);
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

            }
            finally
            {
                Object.DestroyImmediate(configClone);
                Object.DestroyImmediate(topologyClone);
                Object.DestroyImmediate(windowClone);
            }
        }

        /// <summary>重排图层列表，核对指纹随顺序变化且快照保留新的层次序。</summary>
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
                GraphAssetSnapshot originalSnapshot = Snapshot(asset);
                reversed.Layers.Reverse();
                GraphAssetSnapshot reversedSnapshot = Snapshot(reversed);

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

        #endregion

        #region Internal Logic

        private const string TestRoot = "Assets/__CoCoFlowStateGraphFingerprintTests";
        private const string SourcePath = TestRoot + "/Source.asset";
        private const string MovedPath = TestRoot + "/Moved.asset";

        private GraphDescriptorCatalog catalog;

        /// <summary>为外部同名类型构造实际嵌套 CLR 身份，供历史指纹负例使用。</summary>
        private static class ProjectTypes
        {
            /// <summary>在测试程序集保留与标准空记忆相同的短名，检验发布身份转换只作用于包内类型。</summary>
            internal sealed class StatelessMemory { }
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
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private GraphAssetSnapshot Snapshot(StateGraphAsset asset) =>
            GraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                GraphAssetReferenceInspection.Empty);

        private static LayerId ToLayerId(SerializedId128 id)
        {
            Assert.IsTrue(LayerId.TryCreate(id.High, id.Low, out LayerId layerId));
            return layerId;
        }

        #endregion
    }
}
