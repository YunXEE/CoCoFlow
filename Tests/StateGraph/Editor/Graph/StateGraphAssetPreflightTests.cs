using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateGraphAssetPreflightTests
    {

        #region Public API

        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
            catalog = StateGraphTestFactory.CreateCatalog(false);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        [Test]
        public void ValidAssetFreezesEachStateConfigExactlyOnce()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                GraphAssetCompileResult result = Compile(asset);

                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(1, StateGraphFixtureCounters.StateFreezeCalls);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [TestCase(0U)]
        [TestCase(2U)]
        public void UnsupportedSchemaSkipsEveryFreezer(uint schemaVersion)
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                SetUInt128OrScalar(asset, "schemaVersion", schemaVersion);

                GraphAssetCompileResult result = Compile(asset);

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

                GraphAssetCompileResult result = Compile(asset);

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

        [Test]
        public void BlankAssetGuidStampSkipsEveryFreezerAndUsesItsOwnLocationField()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("assetGuidStamp").stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GraphAssetCompileResult result = Compile(asset);

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

        [Test]
        public void MissingStateConfigSkipsOnlyThatStateAndFreezesOtherValidStates()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                StateGraphAuthoringOperations.AddState(asset, ToLayerId(asset.Layers[0].LayerId),
                    default, StateGraphTestFactory.StateDescriptorId, null, "Missing Config");
                GraphAssetCompileResult result = Compile(asset);
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

        private GraphAssetCompileResult Compile(StateGraphAsset asset) =>
            new GraphAssetCompiler(new GraphAssetCompilationCache()).Compile(asset, catalog);

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
