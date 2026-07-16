using System;
using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphEventAdapterAuthoringTests
    {
        private const string TestRoot = "Assets/__CoCoFlowEventAdapterAuthoringTests";
        private const string AssetPath = TestRoot + "/StateGraph.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowEventAdapterAuthoringTests");
            CoCoStateGraphFixtureCounters.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = null;
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void EventAndIntentIdsSurviveSaveReimportAndCompileAsAGraphDeclaration()
        {
            CoCoStateGraphAsset asset = CreateSavedAsset();
            CoCoEventTypeId eventTypeId = EventType(101UL);
            CoCoIntentId intentId = CoCoStateGraphTestFactory.IntentId;
            asset.EventAdapterDeclarations.Add(Declaration(eventTypeId, intentId));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);

            CoCoStateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(AssetPath);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(1, reloaded.EventAdapterDeclarations.Count);
            Assert.AreEqual(Serialize(eventTypeId), reloaded.EventAdapterDeclarations[0].EventTypeId);
            Assert.AreEqual(Serialize(intentId), reloaded.EventAdapterDeclarations[0].ProvidedIntentId);

            CoCoStateGraphAssetCompileResult result =
                new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(reloaded, CreateCatalog());

            Assert.IsTrue(result.Succeeded, JoinDiagnostics(result));
            Assert.AreEqual(1, result.Graph.IntentRequirements.AdapterCount);
            CoCoCompiledEventToIntentDeclaration compiled =
                result.Graph.IntentRequirements.EventAdapterDeclarations[0];
            Assert.AreEqual(eventTypeId, compiled.EventTypeId);
            Assert.AreEqual(intentId, compiled.ProvidedIntentId);
            Assert.AreEqual(typeof(TestGraphEvent), compiled.EventPayloadType);
            Assert.AreEqual(typeof(TestIntent), compiled.ProvidedIntentType);
        }

        [Test]
        public void CanonicalContentFingerprintIgnoresAuthorOrderButDuplicateLocationDoesNot()
        {
            CoCoStateGraphAsset first = CreateTransientAsset("duplicate-order-guid");
            CoCoStateGraphAsset second = null;
            try
            {
                AddDeclaration(first, EventType(101UL), CoCoStateGraphTestFactory.IntentId);
                AddDeclaration(first, EventType(102UL), AlternateIntentId);
                AddDeclaration(first, EventType(101UL), CoCoStateGraphTestFactory.IntentId);
                second = UnityEngine.Object.Instantiate(first);
                CoCoStateGraphEventAdapterDeclarationRecord unique =
                    second.EventAdapterDeclarations[1];
                second.EventAdapterDeclarations[1] = second.EventAdapterDeclarations[2];
                second.EventAdapterDeclarations[2] = unique;

                CoCoGraphDescriptorCatalog catalog = CreateCatalog();
                CoCoStateGraphAssetSnapshot firstSnapshot = Snapshot(first, catalog);
                CoCoStateGraphAssetSnapshot secondSnapshot = Snapshot(second, catalog);

                Assert.AreEqual(firstSnapshot.ContentFingerprint, secondSnapshot.ContentFingerprint);
                Assert.AreNotEqual(firstSnapshot.CacheFingerprint, secondSnapshot.CacheFingerprint);

                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());
                CoCoStateGraphAssetCompileResult firstResult = compiler.Compile(first, catalog);
                CoCoStateGraphAssetCompileResult secondResult = compiler.Compile(second, catalog);

                Assert.AreEqual(firstResult.ContentFingerprint, secondResult.ContentFingerprint);
                Assert.AreEqual(2, FindDiagnosticIndex(
                    firstResult,
                    CoCoDiagnosticCode.DuplicateIdentifier));
                Assert.AreEqual(1, FindDiagnosticIndex(
                    secondResult,
                    CoCoDiagnosticCode.DuplicateIdentifier));
            }
            finally
            {
                if (second != null)
                {
                    UnityEngine.Object.DestroyImmediate(second);
                }

                UnityEngine.Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void CanonicalContentFingerprintIgnoresAuthorOrderButInvalidLocationDoesNot()
        {
            CoCoStateGraphAsset first = CreateTransientAsset("invalid-order-guid");
            CoCoStateGraphAsset second = null;
            try
            {
                AddDeclaration(first, EventType(101UL), CoCoStateGraphTestFactory.IntentId);
                first.EventAdapterDeclarations.Add(new CoCoStateGraphEventAdapterDeclarationRecord(
                    default,
                    Serialize(CoCoStateGraphTestFactory.IntentId)));
                second = UnityEngine.Object.Instantiate(first);
                second.EventAdapterDeclarations.Reverse();

                CoCoGraphDescriptorCatalog catalog = CreateCatalog();
                CoCoStateGraphAssetSnapshot firstSnapshot = Snapshot(first, catalog);
                CoCoStateGraphAssetSnapshot secondSnapshot = Snapshot(second, catalog);

                Assert.AreEqual(firstSnapshot.ContentFingerprint, secondSnapshot.ContentFingerprint);
                Assert.AreNotEqual(firstSnapshot.CacheFingerprint, secondSnapshot.CacheFingerprint);

                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());
                CoCoStateGraphAssetCompileResult firstResult = compiler.Compile(first, catalog);
                CoCoStateGraphAssetCompileResult secondResult = compiler.Compile(second, catalog);

                Assert.AreEqual(firstResult.ContentFingerprint, secondResult.ContentFingerprint);
                Assert.AreEqual(1, FindDiagnosticIndex(
                    firstResult,
                    CoCoDiagnosticCode.InvalidIdentifier));
                Assert.AreEqual(0, FindDiagnosticIndex(
                    secondResult,
                    CoCoDiagnosticCode.InvalidIdentifier));
            }
            finally
            {
                if (second != null)
                {
                    UnityEngine.Object.DestroyImmediate(second);
                }

                UnityEngine.Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void DuplicateLayerLeavesGraphLevelDeclarationsUntouched()
        {
            CoCoStateGraphAsset asset = CreateTransientAsset("duplicate-layer-guid");
            try
            {
                AddDeclaration(asset, EventType(101UL), CoCoStateGraphTestFactory.IntentId);
                AddDeclaration(asset, EventType(102UL), AlternateIntentId);
                CoCoStateGraphEventAdapterDeclarationRecord first =
                    asset.EventAdapterDeclarations[0];
                CoCoStateGraphEventAdapterDeclarationRecord second =
                    asset.EventAdapterDeclarations[1];
                CoCoGraphId graphId = asset.GraphId;
                CoCoLayerId layerId = ToLayerId(asset.Layers[0].LayerId);

                Assert.IsTrue(CoCoStateGraphAuthoringOperations.DuplicateLayer(
                    asset,
                    layerId,
                    out CoCoLayerId duplicatedLayerId));

                Assert.IsTrue(duplicatedLayerId.IsValid);
                Assert.AreEqual(graphId, asset.GraphId);
                Assert.AreEqual(2, asset.Layers.Count);
                Assert.AreEqual(2, asset.EventAdapterDeclarations.Count);
                Assert.AreSame(first, asset.EventAdapterDeclarations[0]);
                Assert.AreSame(second, asset.EventAdapterDeclarations[1]);
                Assert.AreEqual(Serialize(EventType(101UL)), first.EventTypeId);
                Assert.AreEqual(Serialize(CoCoStateGraphTestFactory.IntentId), first.ProvidedIntentId);
                Assert.AreEqual(Serialize(EventType(102UL)), second.EventTypeId);
                Assert.AreEqual(Serialize(AlternateIntentId), second.ProvidedIntentId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DiagnosticNavigatorSelectsTheExactDeclarationArrayItem()
        {
            CoCoStateGraphAsset asset = CreateTransientAsset("navigator-guid");
            try
            {
                AddDeclaration(asset, EventType(101UL), CoCoStateGraphTestFactory.IntentId);
                AddDeclaration(asset, EventType(102UL), AlternateIntentId);
                var location = new CoCoGraphDiagnosticLocation(
                    CoCoGraphElementKind.EventAdapterDeclaration,
                    CoCoGraphField.EventAdapterDeclarations,
                    asset.GraphId,
                    default,
                    default,
                    default,
                    -1,
                    -1,
                    -1,
                    -1,
                    1);
                var serialized = new SerializedObject(asset);

                Assert.IsTrue(CoCoStateGraphDiagnosticNavigator.TryFindProperty(
                    serialized,
                    location,
                    out SerializedProperty property));
                Assert.AreEqual("eventAdapterDeclarations.Array.data[1]", property.propertyPath);
                Assert.IsTrue(CoCoStateGraphDiagnosticNavigator.TrySelect(
                    asset,
                    location,
                    out string selectedPath));
                Assert.AreEqual("eventAdapterDeclarations.Array.data[1]", selectedPath);
                Assert.AreSame(asset, Selection.activeObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static CoCoIntentId AlternateIntentId =>
            CoCoStateGraphTestFactory.CreateIntentId(2UL);

        private static CoCoStateGraphAsset CreateSavedAsset()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string assetGuid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(assetGuid);
            AddTerminalLayer(asset);
            return asset;
        }

        private static CoCoStateGraphAsset CreateTransientAsset(string assetGuid)
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity(assetGuid));
            AddTerminalLayer(asset);
            return asset;
        }

        private static void AddTerminalLayer(CoCoStateGraphAsset asset)
        {
            CoCoLayerId layerId =
                CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 7 },
                "Idle");
        }

        private static CoCoGraphDescriptorCatalog CreateCatalog()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Require(builder.TryRegisterIntent(
                CoCoStateGraphTestFactory.IntentId,
                4,
                new CoCoIntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(901UL),
                out CoCoDiagnostic primaryIntentDiagnostic), primaryIntentDiagnostic);
            Require(builder.TryRegisterIntent(
                AlternateIntentId,
                4,
                new CoCoIntentReducerFactoryToken<
                    AlternateTestIntent,
                    AlternateTestIntentReducer,
                    AlternateTestIntentReducerFactory>(902UL),
                out CoCoDiagnostic alternateIntentDiagnostic), alternateIntentDiagnostic);
            Require(builder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                EventDomain,
                EventType(101UL),
                CoCoStateGraphTestFactory.IntentId,
                out CoCoDiagnostic primaryDeclarationDiagnostic), primaryDeclarationDiagnostic);
            Require(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                AlternateTestIntent>(
                EventDomain,
                EventType(102UL),
                AlternateIntentId,
                out CoCoDiagnostic alternateDeclarationDiagnostic),
                alternateDeclarationDiagnostic);
            Require(builder.TryRegisterState(
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
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic);
            Require(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic);
            return catalog;
        }

        private static CoCoEventDomainId EventDomain
        {
            get
            {
                Assert.IsTrue(CoCoEventDomainId.TryCreate(11UL, out CoCoEventDomainId id));
                return id;
            }
        }

        private static CoCoEventTypeId EventType(ulong low)
        {
            Assert.IsTrue(CoCoEventTypeId.TryCreate(13UL, low, out CoCoEventTypeId id));
            return id;
        }

        private static void AddDeclaration(
            CoCoStateGraphAsset asset,
            CoCoEventTypeId eventTypeId,
            CoCoIntentId intentId) =>
            asset.EventAdapterDeclarations.Add(Declaration(eventTypeId, intentId));

        private static CoCoStateGraphEventAdapterDeclarationRecord Declaration(
            CoCoEventTypeId eventTypeId,
            CoCoIntentId intentId) =>
            new CoCoStateGraphEventAdapterDeclarationRecord(
                Serialize(eventTypeId),
                Serialize(intentId));

        private static CoCoStateGraphAssetSnapshot Snapshot(
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog) =>
            CoCoStateGraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                CoCoStateGraphManagedReferenceInspection.Empty);

        private static int FindDiagnosticIndex(
            CoCoStateGraphAssetCompileResult result,
            CoCoDiagnosticCode code)
        {
            CoCoGraphDiagnostic diagnostic = result.Diagnostics.Single(candidate =>
                candidate.Diagnostic.Code == code &&
                candidate.Location.ElementKind ==
                CoCoGraphElementKind.EventAdapterDeclaration);
            return diagnostic.Location.EventAdapterDeclarationIndex;
        }

        private static string JoinDiagnostics(CoCoStateGraphAssetCompileResult result) =>
            string.Join("\n", result.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Diagnostic.Code}: {diagnostic.Diagnostic.Message} " +
                $"({diagnostic.Location.ElementKind}/" +
                $"{diagnostic.Location.EventAdapterDeclarationIndex})"));

        private static CoCoSerializedId128 Serialize(CoCoEventTypeId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoIntentId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoLayerId ToLayerId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(id.High, id.Low, out CoCoLayerId layerId));
            return layerId;
        }

        private static void Require(bool condition, CoCoDiagnostic diagnostic)
        {
            Assert.IsTrue(condition, diagnostic.Message);
        }
    }
}
