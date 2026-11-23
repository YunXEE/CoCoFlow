using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphAssetPreflightTests
    {
        private GraphDescriptorCatalog catalog;

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
        public void ValidAssetFreezesEachStateAndConditionConfigExactlyOnce()
        {
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(1, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(1, StateGraphFixtureCounters.ConditionFreezeCalls);
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

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(0, StateGraphFixtureCounters.ConditionFreezeCalls);
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

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(0, StateGraphFixtureCounters.ConditionFreezeCalls);
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

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(0, StateGraphFixtureCounters.ConditionFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                    diagnostic.Location.Field == GraphField.AssetGuidStamp));
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
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                if (missingState)
                {
                    StateGraphAuthoringOperations.AddState(
                        asset,
                        ToLayerId(asset.Layers[0].LayerId),
                        default,
                        StateGraphTestFactory.StateDescriptorId,
                        null,
                        "Missing Config");
                }
                else
                {
                    asset.Layers[0].Transitions[0].Conditions.Add(
                        new StateGraphConditionRecord(
                            Serialize(StateGraphTestFactory.ConditionDescriptorId),
                            null));
                }

                StateGraphAssetCompileResult result = Compile(asset);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(1, StateGraphFixtureCounters.StateFreezeCalls);
                Assert.AreEqual(1, StateGraphFixtureCounters.ConditionFreezeCalls);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Location.ElementKind == (missingState
                        ? GraphElementKind.State
                        : GraphElementKind.Condition) &&
                    diagnostic.Location.Field == GraphField.Config));
                if (!missingState)
                {
                    GraphDiagnostic[] preciseErrors = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsError &&
                        diagnostic.Location.ElementKind == GraphElementKind.Condition &&
                        diagnostic.Location.Field == GraphField.Config).ToArray();
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
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                catalog = CreateCatalog(new ThrowingConditionConfigFreezer());

                StateGraphAssetCompileResult result = Compile(asset);

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
            StateGraphAsset asset = CreateValidAsset();
            try
            {
                StateGraphConditionRecord condition =
                    asset.Layers[0].Transitions[0].Conditions[0];
                asset.Layers[0].Transitions[0].Conditions[0] =
                    new StateGraphConditionRecord(
                        condition.ConditionDescriptorId,
                        new AlternateConditionAuthoringConfig { Value = 7 });

                StateGraphAssetCompileResult result = Compile(asset);

                AssertSinglePreciseConditionConfigError(asset, result);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private StateGraphAssetCompileResult Compile(StateGraphAsset asset) =>
            new StateGraphAssetCompiler(new StateGraphCompilationCache()).Compile(asset, catalog);

        private static void AssertSinglePreciseConditionConfigError(
            StateGraphAsset asset,
            StateGraphAssetCompileResult result)
        {
            GraphDiagnostic[] preciseErrors = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Location.ElementKind == GraphElementKind.Condition &&
                diagnostic.Location.Field == GraphField.Config).ToArray();
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
                diagnostic.Location.ElementKind == GraphElementKind.Condition &&
                diagnostic.Location.Field == GraphField.Config));
        }

        private static GraphDescriptorCatalog CreateCatalog(
            IConfigFreezer<TestConditionAuthoringConfig, TestConditionConfigSchema>
                conditionFreezer)
        {
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                StateGraphTestFactory.StateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterCondition(
                StateGraphTestFactory.ConditionDescriptorId,
                1U,
                conditionFreezer,
                new ConditionRuntimeRegistration<
                    TestStateCondition,
                    TestConditionConfigSchema>(TestFrozenConfigSchemas.ConditionSchema),
                null,
                null,
                out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog result,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return result;
        }

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
            asset.Layers[0].Transitions[0].Conditions.Add(new StateGraphConditionRecord(
                Serialize(StateGraphTestFactory.ConditionDescriptorId),
                new TestConditionAuthoringConfig { Threshold = 1 }));
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

        private static SerializedId128 Serialize(ConditionDescriptorId id) =>
            new SerializedId128(id.High, id.Low);

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
    }
}
