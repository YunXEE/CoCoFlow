using System.IO;
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
    public sealed class StateGraphMissingManagedReferenceTests
    {
        private const string TestRoot = "Assets/__CoCoFlowMissingManagedReferenceTests";
        private const string AssetPath = TestRoot + "/MissingConfig.asset";
        private const string FixtureAssembly = "CoCoFlow.Tests.StateGraphAuthoringFixtures";
        private const string MissingAssembly = "Missing.StateGraph.Authoring";

        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__CoCoFlowMissingManagedReferenceTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void MissingManagedReferenceTypeBlocksAllFreezersWithOnePreciseError()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(guid);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 17 },
                "State");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            string yaml = File.ReadAllText(AssetPath);
            StringAssert.Contains(FixtureAssembly, yaml);
            File.WriteAllText(AssetPath, yaml.Replace(FixtureAssembly, MissingAssembly));
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphAsset reloaded = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);

            Assert.IsTrue(SerializationUtility.HasManagedReferencesWithMissingTypes(reloaded));
            StateGraphAssetCompileResult result =
                new StateGraphAssetCompiler(new StateGraphCompilationCache())
                    .Compile(reloaded, StateGraphTestFactory.CreateCatalog(false));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
            GraphDiagnostic[] missingTypeErrors = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidAuthoringDependency &&
                diagnostic.Location.ElementKind == GraphElementKind.State &&
                diagnostic.Location.Field == GraphField.Config).ToArray();
            Assert.AreEqual(1, missingTypeErrors.Length);
            ManagedReferenceMissingType missing =
                SerializationUtility.GetManagedReferencesWithMissingTypes(reloaded)[0];
            SerializedProperty serializedConfig = new SerializedObject(reloaded)
                .FindProperty("layers")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("states")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("config");
            StringAssert.Contains(
                MissingAssembly,
                missingTypeErrors[0].Diagnostic.Message,
                $"SerializedProperty id={serializedConfig.managedReferenceId}, " +
                $"type='{serializedConfig.managedReferenceFullTypename}', " +
                $"missing id={missing.referenceId}. All diagnostics: " +
                string.Join(" | ", result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Location.ElementKind}/{diagnostic.Location.Field}:" +
                    diagnostic.Diagnostic.Message)));
            Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Message.Contains("is missing its managed-reference Config")));
        }

        [Test]
        public void MultipleMissingConfigsUseGraphLocationInsteadOfGuessingAState()
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(guid);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 17 },
                "First");
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 23 },
                "Second");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            string yaml = File.ReadAllText(AssetPath);
            StringAssert.Contains(FixtureAssembly, yaml);
            File.WriteAllText(AssetPath, yaml.Replace(FixtureAssembly, MissingAssembly));
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            StateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<StateGraphAsset>(AssetPath);

            ManagedReferenceMissingType[] missing =
                SerializationUtility.GetManagedReferencesWithMissingTypes(reloaded);
            Assert.AreEqual(2, missing.Length);
            StateGraphAssetCompileResult result =
                new StateGraphAssetCompiler(new StateGraphCompilationCache())
                    .Compile(reloaded, StateGraphTestFactory.CreateCatalog(false));

            GraphDiagnostic[] missingTypeErrors = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Diagnostic.Message.Contains("Managed-reference Config type") &&
                diagnostic.Diagnostic.Message.Contains(MissingAssembly)).ToArray();
            Assert.AreEqual(2, missingTypeErrors.Length);
            Assert.IsTrue(missingTypeErrors.All(diagnostic =>
                diagnostic.Location.ElementKind == GraphElementKind.Graph &&
                diagnostic.Location.Field == GraphField.Config));
            Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
        }
    }
}
