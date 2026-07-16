using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphAssetPreflightTests
    {
        private CoCoGraphDescriptorCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
            catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        [Test]
        public void ValidAssetFreezesEachStateAndConditionConfigExactlyOnce()
        {
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                CoCoStateGraphAssetCompileResult result = Compile(asset);

                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(1, CoCoStateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(1, CoCoStateGraphFixtureCounters.ConditionFreezeCalls);
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
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                SetUInt128OrScalar(asset, "schemaVersion", schemaVersion);

                CoCoStateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ConditionFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.UnsupportedSchemaVersion &&
                    diagnostic.Location.Field == CoCoGraphField.SchemaVersion));
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
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                var serialized = new SerializedObject(asset);
                SerializedProperty graphId = serialized.FindProperty("graphId");
                graphId.FindPropertyRelative("high").ulongValue = 0UL;
                graphId.FindPropertyRelative("low").ulongValue = 0UL;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                CoCoStateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ConditionFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                    diagnostic.Location.Field == CoCoGraphField.Identifier));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void BlankAssetGuidStampSkipsEveryFreezerAndUsesItsOwnLocationField()
        {
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("assetGuidStamp").stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                CoCoStateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ConditionFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                    diagnostic.Location.Field == CoCoGraphField.AssetGuidStamp));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingSingleConfigSkipsOnlyThatItemAndFreezesOtherValidItems(bool missingState)
        {
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                if (missingState)
                {
                    CoCoStateGraphAuthoringOperations.AddState(
                        asset,
                        ToLayerId(asset.Layers[0].LayerId),
                        default,
                        CoCoStateGraphTestFactory.StateDescriptorId,
                        null,
                        "Missing Config");
                }
                else
                {
                    asset.Layers[0].Transitions[0].Conditions.Add(
                        new CoCoStateGraphConditionRecord(
                            Serialize(CoCoStateGraphTestFactory.ConditionDescriptorId),
                            null));
                }

                CoCoStateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(1, CoCoStateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(1, CoCoStateGraphFixtureCounters.ConditionFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Location.ElementKind == (missingState
                        ? CoCoGraphElementKind.State
                        : CoCoGraphElementKind.Condition) &&
                    diagnostic.Location.Field == CoCoGraphField.Config));
                if (!missingState)
                {
                    CoCoGraphDiagnostic[] preciseErrors = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsError &&
                        diagnostic.Location.ElementKind == CoCoGraphElementKind.Condition &&
                        diagnostic.Location.Field == CoCoGraphField.Config).ToArray();
                    Assert.AreEqual(1, preciseErrors.Length);
                    Assert.AreEqual(
                        ToStateId(asset.Layers[0].Transitions[0].SourceStateId),
                        preciseErrors[0].Location.StateId);
                    Assert.AreNotEqual(
                        CoCoDiagnosticCode.DescriptorTypeMismatch,
                        preciseErrors[0].Diagnostic.Code);
                }
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ConditionFreezerFailureUsesOnePreciseSourceStateLocation()
        {
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                catalog = CreateCatalog(new ThrowingConditionConfigFreezer());

                CoCoStateGraphAssetCompileResult result = Compile(asset);

                AssertSinglePreciseConditionConfigError(asset, result);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ConditionAuthoringTypeMismatchUsesOnePreciseSourceStateLocation()
        {
            CoCoStateGraphAsset asset = CreateValidAsset();
            try
            {
                CoCoStateGraphConditionRecord condition =
                    asset.Layers[0].Transitions[0].Conditions[0];
                asset.Layers[0].Transitions[0].Conditions[0] =
                    new CoCoStateGraphConditionRecord(
                        condition.ConditionDescriptorId,
                        new AlternateConditionAuthoringConfig { Value = 7 });

                CoCoStateGraphAssetCompileResult result = Compile(asset);

                AssertSinglePreciseConditionConfigError(asset, result);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private CoCoStateGraphAssetCompileResult Compile(CoCoStateGraphAsset asset) =>
            new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache()).Compile(asset, catalog);

        private static void AssertSinglePreciseConditionConfigError(
            CoCoStateGraphAsset asset,
            CoCoStateGraphAssetCompileResult result)
        {
            CoCoGraphDiagnostic[] preciseErrors = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.Condition &&
                diagnostic.Location.Field == CoCoGraphField.Config).ToArray();
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, preciseErrors.Length);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidFrozenConfig,
                preciseErrors[0].Diagnostic.Code);
            Assert.AreEqual(
                ToStateId(asset.Layers[0].Transitions[0].SourceStateId),
                preciseErrors[0].Location.StateId);
            Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DescriptorTypeMismatch &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.Condition &&
                diagnostic.Location.Field == CoCoGraphField.Config));
        }

        private static CoCoGraphDescriptorCatalog CreateCatalog(
            ICoCoConfigFreezer<TestConditionAuthoringConfig, TestConditionConfigSchema>
                conditionFreezer)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                CoCoStateGraphTestFactory.StateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterCondition(
                CoCoStateGraphTestFactory.ConditionDescriptorId,
                1U,
                conditionFreezer,
                new CoCoConditionRuntimeRegistration<
                    TestStateCondition,
                    TestConditionConfigSchema>(TestFrozenConfigSchemas.ConditionSchema),
                null,
                null,
                null,
                out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog result,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return result;
        }

        private static CoCoStateGraphAsset CreateValidAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity("preflight-test-guid"));
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId stateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                "Idle");
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                stateId,
                stateId);
            asset.Layers[0].Transitions[0].Conditions.Add(new CoCoStateGraphConditionRecord(
                Serialize(CoCoStateGraphTestFactory.ConditionDescriptorId),
                new TestConditionAuthoringConfig { Threshold = 1 }));
            return asset;
        }

        private static void SetUInt128OrScalar(
            CoCoStateGraphAsset asset,
            string propertyName,
            uint value)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty(propertyName).longValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static CoCoSerializedId128 Serialize(CoCoConditionDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoLayerId ToLayerId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(id.High, id.Low, out CoCoLayerId layerId));
            return layerId;
        }

        private static CoCoStateId ToStateId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoStateId.TryCreate(id.High, id.Low, out CoCoStateId stateId));
            return stateId;
        }
    }
}
