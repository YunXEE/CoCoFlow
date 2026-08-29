using System.IO;
using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphMissingManagedReferenceTests
    {
        private const string TestRoot = "Assets/__CoCoFlowMissingManagedReferenceTests";
        private const string AssetPath = TestRoot + "/MissingConfig.asset";
        private const string FixtureAssembly = "CoCoFlow.Tests.StateGraphAuthoringFixtures";
        private const string MissingAssembly = "Missing.StateGraph.Authoring";

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
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
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(guid);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 17 },
                "State");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            string yaml = File.ReadAllText(AssetPath);
            StringAssert.Contains(FixtureAssembly, yaml);
            File.WriteAllText(AssetPath, yaml.Replace(FixtureAssembly, MissingAssembly));
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            CoCoStateGraphAsset reloaded = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(AssetPath);

            Assert.IsTrue(SerializationUtility.HasManagedReferencesWithMissingTypes(reloaded));
            CoCoStateGraphAssetCompileResult result =
                new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(reloaded, CoCoStateGraphTestFactory.CreateCatalog(false));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            CoCoGraphDiagnostic[] missingTypeErrors = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidAuthoringDependency &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.State &&
                diagnostic.Location.Field == CoCoGraphField.Config).ToArray();
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
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            asset.EnsureAssetIdentity(guid);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 17 },
                "First");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                new TestStateAuthoringConfig { Value = 23 },
                "Second");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            string yaml = File.ReadAllText(AssetPath);
            StringAssert.Contains(FixtureAssembly, yaml);
            File.WriteAllText(AssetPath, yaml.Replace(FixtureAssembly, MissingAssembly));
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            CoCoStateGraphAsset reloaded =
                AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(AssetPath);

            ManagedReferenceMissingType[] missing =
                SerializationUtility.GetManagedReferencesWithMissingTypes(reloaded);
            Assert.AreEqual(2, missing.Length);
            CoCoStateGraphAssetCompileResult result =
                new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(reloaded, CoCoStateGraphTestFactory.CreateCatalog(false));

            CoCoGraphDiagnostic[] missingTypeErrors = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Diagnostic.Message.Contains("Managed-reference Config type") &&
                diagnostic.Diagnostic.Message.Contains(MissingAssembly)).ToArray();
            Assert.AreEqual(2, missingTypeErrors.Length);
            Assert.IsTrue(missingTypeErrors.All(diagnostic =>
                diagnostic.Location.ElementKind == CoCoGraphElementKind.Graph &&
                diagnostic.Location.Field == CoCoGraphField.Config));
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
        }
    }
}
