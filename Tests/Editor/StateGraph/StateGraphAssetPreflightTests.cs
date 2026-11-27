using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 图预检读取实际 Unity 资产中的节点与配置，将丢失引用或无法冻结的字段定位到来源记录。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>图预检读取实际 Unity 资产中的节点与配置，将丢失引用或无法冻结的字段定位到来源记录。</summary>
    public sealed class StateGraphAssetPreflightTests
    {

        #region Public API

        /// <summary>重置 StateGraphFixtureCounters 的测试状态，确保该例能独立检查工厂或回调次数。</summary>
        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
            catalog = StateGraphTestFactory.CreateCatalog(false);
        }

        /// <summary>清理本用例创建的 Undo 记录，避免下一次图预检使用旧撤销状态。</summary>
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        /// <summary>用计数 freezer 编译合法资产，核对每份节点配置只冻结一次。</summary>
        [Test]
        public void ValidAssetFreezesEachStateConfigExactlyOnce()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(1, StateGraphFixtureCounters.StateFreezeCalls);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>给图声明不支持的 schema，核对返回诊断并且没有调用任何节点 freezer。</summary>
        [TestCase(0U)]
        [TestCase(2U)]
        public void UnsupportedSchemaSkipsEveryFreezer(uint schemaVersion)
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                SetUInt128OrScalar(asset, "schemaVersion", schemaVersion);

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.UnsupportedSchemaVersion &&
                    diagnostic.Location.Field == GraphField.SchemaVersion));
                Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DescriptorTypeMismatch));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>使图身份无效，检查在冻结节点配置前就报告资产错误。</summary>
        [Test]
        public void InvalidGraphIdentitySkipsEveryFreezer()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                var serialized = new SerializedObject(asset);
                SerializedProperty graphId = serialized.FindProperty("graphId");
                graphId.FindPropertyRelative("high").ulongValue = 0UL;
                graphId.FindPropertyRelative("low").ulongValue = 0UL;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                    diagnostic.Location.Field == GraphField.Identifier));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>清空资产 GUID 标记，核对诊断精确定位该字段且 freezer 调用数为零。</summary>
        [Test]
        public void BlankAssetGuidStampSkipsEveryFreezerAndUsesItsOwnLocationField()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("assetGuidStamp").stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                    diagnostic.Location.Field == GraphField.AssetGuidStamp));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>移除一个节点的配置，确认诊断定位该节点，同时其他有效节点仍能冻结。</summary>
        [Test]
        public void MissingStateConfigSkipsOnlyThatStateAndFreezesOtherValidStates()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                StateGraphAuthoringOperations.AddState(asset, ToLayerId(asset.Layers[0].LayerId),
                    default, StateGraphTestFactory.StateDescriptorId, null, "Missing Config");
                StateGraphAssetCompileResult result = Compile(asset);
                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(1, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.IsError &&
                    diagnostic.Location.ElementKind == GraphElementKind.State &&
                    diagnostic.Location.Field == GraphField.Config));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        #endregion

        #region Internal Logic

        private GraphDescriptorCatalog catalog;

        private StateGraphAssetCompileResult Compile(StateGraphAsset asset) =>
            new StateGraphAssetCompiler(new StateGraphCompilationCache()).Compile(asset, catalog);

        private static StateGraphAsset CreateValidAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity("preflight-test-guid"));
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId stateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                "Idle");
            StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                stateId,
                stateId,
                priority: 0);
            return asset;
        }

        private static void SetUInt128OrScalar(
            StateGraphAsset asset,
            string propertyName,
            uint value)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty(propertyName).longValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LayerId ToLayerId(SerializedId128 id)
        {
            Assert.IsTrue(LayerId.TryCreate(id.High, id.Low, out LayerId layerId));
            return layerId;
        }

        private static StateId ToStateId(SerializedId128 id)
        {
            Assert.IsTrue(StateId.TryCreate(id.High, id.Low, out StateId stateId));
            return stateId;
        }

        #endregion
    }
}
