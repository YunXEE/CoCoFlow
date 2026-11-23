using System;
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
    public sealed class StateGraphEventAdapterAuthoringTests
    {
        private const string TestRoot = "Assets/__CoCoFlowEventAdapterAuthoringTests";
        private const string AssetPath = TestRoot + "/StateGraph.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowEventAdapterAuthoringTests");
            StateGraphFixtureCounters.Reset();
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
            StateGraphAsset asset = CreateSavedAsset();
            EventTypeId eventTypeId = EventType(101UL);
            IntentId intentId = StateGraphTestFactory.IntentId;
            asset.EventAdapterDeclarations.Add(Declaration(eventTypeId, intentId));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);

            StateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(1, reloaded.EventAdapterDeclarations.Count);
            Assert.AreEqual(Serialize(eventTypeId), reloaded.EventAdapterDeclarations[0].EventTypeId);
            Assert.AreEqual(Serialize(intentId), reloaded.EventAdapterDeclarations[0].ProvidedIntentId);

            StateGraphAssetCompileResult result =
                new StateGraphAssetCompiler(new StateGraphCompilationCache())
                    .Compile(reloaded, CreateCatalog());

            Assert.IsTrue(result.Succeeded, JoinDiagnostics(result));
            Assert.AreEqual(1, result.Graph.IntentRequirements.AdapterCount);
            CompiledEventToIntentDeclaration compiled =
                result.Graph.IntentRequirements.EventAdapterDeclarations[0];
            Assert.AreEqual(eventTypeId, compiled.EventTypeId);
            Assert.AreEqual(intentId, compiled.ProvidedIntentId);
            Assert.AreEqual(typeof(TestGraphEvent), compiled.EventPayloadType);
            Assert.AreEqual(typeof(TestIntent), compiled.ProvidedIntentType);
        }

        [Test]
        public void ValidDeclarationOrderChangesContentFingerprintAndIntentLayout()
        {
            StateGraphAsset first = CreateTransientAsset("valid-order-guid");
            StateGraphAsset second = null;
            try
            {
                AddDeclaration(first, EventType(101UL), StateGraphTestFactory.IntentId);
                AddDeclaration(first, EventType(102UL), AlternateIntentId);
                second = UnityEngine.Object.Instantiate(first);
                second.EventAdapterDeclarations.Reverse();

                GraphDescriptorCatalog catalog = CreateCatalog();
                StateGraphAssetSnapshot firstSnapshot = Snapshot(first, catalog);
                StateGraphAssetSnapshot secondSnapshot = Snapshot(second, catalog);

                Assert.AreNotEqual(firstSnapshot.ContentFingerprint, secondSnapshot.ContentFingerprint);
                Assert.AreNotEqual(firstSnapshot.CacheFingerprint, secondSnapshot.CacheFingerprint);

                var compiler = new StateGraphAssetCompiler(
                    new StateGraphCompilationCache());
                StateGraphAssetCompileResult firstResult = compiler.Compile(first, catalog);
                StateGraphAssetCompileResult secondResult = compiler.Compile(second, catalog);

                Assert.IsTrue(firstResult.Succeeded, JoinDiagnostics(firstResult));
                Assert.IsTrue(secondResult.Succeeded, JoinDiagnostics(secondResult));
                Assert.AreNotEqual(firstResult.ContentFingerprint, secondResult.ContentFingerprint);
                Assert.AreNotEqual(
                    firstResult.Graph.IntentRequirements.LayoutId,
                    secondResult.Graph.IntentRequirements.LayoutId);
                CollectionAssert.AreEqual(
                    new[] { EventType(101UL), EventType(102UL) },
                    firstResult.Graph.IntentRequirements.EventAdapterDeclarations
                        .Select(declaration => declaration.EventTypeId));
                CollectionAssert.AreEqual(
                    new[] { EventType(102UL), EventType(101UL) },
                    secondResult.Graph.IntentRequirements.EventAdapterDeclarations
                        .Select(declaration => declaration.EventTypeId));
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
        public void ContentFingerprintPreservesAuthorOrderAndDuplicateLocation()
        {
            StateGraphAsset first = CreateTransientAsset("duplicate-order-guid");
            StateGraphAsset second = null;
            try
            {
                AddDeclaration(first, EventType(101UL), StateGraphTestFactory.IntentId);
                AddDeclaration(first, EventType(102UL), AlternateIntentId);
                AddDeclaration(first, EventType(101UL), StateGraphTestFactory.IntentId);
                second = UnityEngine.Object.Instantiate(first);
                StateGraphEventAdapterDeclarationRecord unique =
                    second.EventAdapterDeclarations[1];
                second.EventAdapterDeclarations[1] = second.EventAdapterDeclarations[2];
                second.EventAdapterDeclarations[2] = unique;

                GraphDescriptorCatalog catalog = CreateCatalog();
                StateGraphAssetSnapshot firstSnapshot = Snapshot(first, catalog);
                StateGraphAssetSnapshot secondSnapshot = Snapshot(second, catalog);

                Assert.AreNotEqual(firstSnapshot.ContentFingerprint, secondSnapshot.ContentFingerprint);
                Assert.AreNotEqual(firstSnapshot.CacheFingerprint, secondSnapshot.CacheFingerprint);

                var compiler = new StateGraphAssetCompiler(
                    new StateGraphCompilationCache());
                StateGraphAssetCompileResult firstResult = compiler.Compile(first, catalog);
                StateGraphAssetCompileResult secondResult = compiler.Compile(second, catalog);

                Assert.AreNotEqual(firstResult.ContentFingerprint, secondResult.ContentFingerprint);
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
        public void ContentFingerprintPreservesAuthorOrderAndInvalidLocation()
        {
            StateGraphAsset first = CreateTransientAsset("invalid-order-guid");
            StateGraphAsset second = null;
            try
            {
                AddDeclaration(first, EventType(101UL), StateGraphTestFactory.IntentId);
                first.EventAdapterDeclarations.Add(new StateGraphEventAdapterDeclarationRecord(
                    default,
                    Serialize(StateGraphTestFactory.IntentId)));
                second = UnityEngine.Object.Instantiate(first);
                second.EventAdapterDeclarations.Reverse();

                GraphDescriptorCatalog catalog = CreateCatalog();
                StateGraphAssetSnapshot firstSnapshot = Snapshot(first, catalog);
                StateGraphAssetSnapshot secondSnapshot = Snapshot(second, catalog);

                Assert.AreNotEqual(firstSnapshot.ContentFingerprint, secondSnapshot.ContentFingerprint);
                Assert.AreNotEqual(firstSnapshot.CacheFingerprint, secondSnapshot.CacheFingerprint);

                var compiler = new StateGraphAssetCompiler(
                    new StateGraphCompilationCache());
                StateGraphAssetCompileResult firstResult = compiler.Compile(first, catalog);
                StateGraphAssetCompileResult secondResult = compiler.Compile(second, catalog);

                Assert.AreNotEqual(firstResult.ContentFingerprint, secondResult.ContentFingerprint);
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
            StateGraphAsset asset = CreateTransientAsset("duplicate-layer-guid");
            try
            {
                AddDeclaration(asset, EventType(101UL), StateGraphTestFactory.IntentId);
                AddDeclaration(asset, EventType(102UL), AlternateIntentId);
                StateGraphEventAdapterDeclarationRecord first =
                    asset.EventAdapterDeclarations[0];
                StateGraphEventAdapterDeclarationRecord second =
                    asset.EventAdapterDeclarations[1];
                GraphId graphId = asset.GraphId;
                LayerId layerId = ToLayerId(asset.Layers[0].LayerId);

                Assert.IsTrue(StateGraphAuthoringOperations.DuplicateLayer(
                    asset,
                    layerId,
                    out LayerId duplicatedLayerId));

                Assert.IsTrue(duplicatedLayerId.IsValid);
                Assert.AreEqual(graphId, asset.GraphId);
                Assert.AreEqual(2, asset.Layers.Count);
                Assert.AreEqual(2, asset.EventAdapterDeclarations.Count);
                Assert.AreSame(first, asset.EventAdapterDeclarations[0]);
                Assert.AreSame(second, asset.EventAdapterDeclarations[1]);
                Assert.AreEqual(Serialize(EventType(101UL)), first.EventTypeId);
                Assert.AreEqual(Serialize(StateGraphTestFactory.IntentId), first.ProvidedIntentId);
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
            StateGraphAsset asset = CreateTransientAsset("navigator-guid");
            try
            {
                AddDeclaration(asset, EventType(101UL), StateGraphTestFactory.IntentId);
                AddDeclaration(asset, EventType(102UL), AlternateIntentId);
                var location = new GraphDiagnosticLocation(
                    GraphElementKind.EventAdapterDeclaration,
                    GraphField.EventAdapterDeclarations,
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

                Assert.IsTrue(StateGraphDiagnosticNavigator.TryFindProperty(
                    serialized,
                    location,
                    out SerializedProperty property));
                Assert.AreEqual("eventAdapterDeclarations.Array.data[1]", property.propertyPath);
                Assert.IsTrue(StateGraphDiagnosticNavigator.TrySelect(
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

        private static IntentId AlternateIntentId =>
            StateGraphTestFactory.CreateIntentId(2UL);

        private static StateGraphAsset CreateSavedAsset()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string assetGuid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(assetGuid);
            AddTerminalLayer(asset);
            return asset;
        }

        private static StateGraphAsset CreateTransientAsset(string assetGuid)
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity(assetGuid));
            AddTerminalLayer(asset);
            return asset;
        }

        private static void AddTerminalLayer(StateGraphAsset asset)
        {
            LayerId layerId =
                StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 7 },
                "Idle");
        }

        private static GraphDescriptorCatalog CreateCatalog()
        {
            var builder = new GraphDescriptorCatalogBuilder();
            Require(builder.TryRegisterIntent(
                StateGraphTestFactory.IntentId,
                4,
                new IntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(901UL),
                out CoCoDiagnostic primaryIntentDiagnostic), primaryIntentDiagnostic);
            Require(builder.TryRegisterIntent(
                AlternateIntentId,
                4,
                new IntentReducerFactoryToken<
                    AlternateTestIntent,
                    AlternateTestIntentReducer,
                    AlternateTestIntentReducerFactory>(902UL),
                out CoCoDiagnostic alternateIntentDiagnostic), alternateIntentDiagnostic);
            Require(builder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                EventDomain,
                EventType(101UL),
                StateGraphTestFactory.IntentId,
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
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic);
            Require(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic);
            return catalog;
        }

        private static EventDomainId EventDomain
        {
            get
            {
                Assert.IsTrue(EventDomainId.TryCreate(11UL, out EventDomainId id));
                return id;
            }
        }

        private static EventTypeId EventType(ulong low)
        {
            Assert.IsTrue(EventTypeId.TryCreate(13UL, low, out EventTypeId id));
            return id;
        }

        private static void AddDeclaration(
            StateGraphAsset asset,
            EventTypeId eventTypeId,
            IntentId intentId) =>
            asset.EventAdapterDeclarations.Add(Declaration(eventTypeId, intentId));

        private static StateGraphEventAdapterDeclarationRecord Declaration(
            EventTypeId eventTypeId,
            IntentId intentId) =>
            new StateGraphEventAdapterDeclarationRecord(
                Serialize(eventTypeId),
                Serialize(intentId));

        private static StateGraphAssetSnapshot Snapshot(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog) =>
            StateGraphAssetSnapshotBuilder.Build(
                asset,
                catalog,
                StateGraphManagedReferenceInspection.Empty);

        private static int FindDiagnosticIndex(
            StateGraphAssetCompileResult result,
            CoCoDiagnosticCode code)
        {
            GraphDiagnostic diagnostic = result.Diagnostics.Single(candidate =>
                candidate.Diagnostic.Code == code &&
                candidate.Location.ElementKind ==
                GraphElementKind.EventAdapterDeclaration);
            return diagnostic.Location.EventAdapterDeclarationIndex;
        }

        private static string JoinDiagnostics(StateGraphAssetCompileResult result) =>
            string.Join("\n", result.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Diagnostic.Code}: {diagnostic.Diagnostic.Message} " +
                $"({diagnostic.Location.ElementKind}/" +
                $"{diagnostic.Location.EventAdapterDeclarationIndex})"));

        private static SerializedId128 Serialize(EventTypeId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(IntentId id) =>
            new SerializedId128(id.High, id.Low);

        private static LayerId ToLayerId(SerializedId128 id)
        {
            Assert.IsTrue(LayerId.TryCreate(id.High, id.Low, out LayerId layerId));
            return layerId;
        }

        private static void Require(bool condition, CoCoDiagnostic diagnostic)
        {
            Assert.IsTrue(condition, diagnostic.Message);
        }
    }
}
