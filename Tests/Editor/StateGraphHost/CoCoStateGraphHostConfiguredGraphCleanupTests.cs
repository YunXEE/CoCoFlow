using System.Linq;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.StateGraphHost
{
    public sealed class CoCoStateGraphHostConfiguredGraphCleanupTests
    {
        [TestCase("Success")]
        [TestCase("BeforeReturn")]
        [TestCase("CompileFailure")]
        public void TryStartReleasesOnlyItsConfiguredCopy(string path)
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            string graphName = "CleanupProbeGraph_" + System.Guid.NewGuid().ToString("N");
            asset.name = graphName;
            var owner = new GameObject("CleanupProbeHost");
            try
            {
                var provider = CoCoStandardBindingProvider.Build(new[] { typeof(HostTestStartupLogic).Assembly });
                Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(provider, out _));
                Assert.IsTrue(StandardDescriptors.TryCreate(typeof(HostTestStartupLogic),
                    nameof(HostTestStartupLogic), out CoCoStateDescriptorId descriptor));
                var graph = new SerializedObject(asset);
                SetId(graph.FindProperty("graphId"), 1, 2);
                graph.FindProperty("assetGuidStamp").stringValue = "0123456789abcdef0123456789abcdef";
                var layers = graph.FindProperty("layers");
                layers.arraySize = 1;
                var layer = layers.GetArrayElementAtIndex(0);
                SetId(layer.FindPropertyRelative("layerId"), 3, 4);
                SetId(layer.FindPropertyRelative("initialStateId"), 5, path == "CompileFailure" ? 99UL : 6UL);
                var states = layer.FindPropertyRelative("states");
                states.arraySize = 1;
                var state = states.GetArrayElementAtIndex(0);
                SetId(state.FindPropertyRelative("stateId"), 5, 6);
                SetId(state.FindPropertyRelative("stateDescriptorId"), descriptor.High,
                    path == "BeforeReturn" ? 99UL : descriptor.Low);
                state.FindPropertyRelative("config").managedReferenceValue = new EmptyStateConfig();
                graph.ApplyModifiedPropertiesWithoutUndo();
                string original = EditorJsonUtility.ToJson(asset);
                var originals = Resources.FindObjectsOfTypeAll<CoCoStateGraphAsset>();
                var host = owner.AddComponent<CoCoStateGraphHost>();
                var settings = new SerializedObject(host);
                settings.FindProperty("stateGraphAsset").objectReferenceValue = asset;
                settings.FindProperty("autoStart").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                if (path == "BeforeReturn")
                    LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));

                bool started = host.TryStart(out CoCoDiagnostic diagnostic);

                Assert.AreEqual(path == "Success", started, diagnostic.Message);
                Assert.AreEqual(path != "Success", diagnostic.IsError);
                if (path == "BeforeReturn")
                    Assert.AreEqual(CoCoDiagnosticCode.CommitPreparationFailed, diagnostic.Code);
                if (path == "CompileFailure")
                    Assert.AreEqual(CoCoDiagnosticCode.InvalidInitialState, diagnostic.Code);
                Assert.AreEqual(diagnostic, host.LastDiagnostic);
                Assert.IsTrue(asset != null, "Original graph must survive.");
                Assert.AreEqual(original, EditorJsonUtility.ToJson(asset));
                CollectionAssert.AreEquivalent(originals, Resources.FindObjectsOfTypeAll<CoCoStateGraphAsset>(),
                    "Only the configured copy must be released before startup returns.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(owner);
                foreach (var graph in Resources.FindObjectsOfTypeAll<CoCoStateGraphAsset>()
                    .Where(graph => graph.name == graphName + "(Clone)"))
                    Object.DestroyImmediate(graph);
                Object.DestroyImmediate(asset);
                CoCoStateGraphProjectBindings.ResetForTests();
            }
        }

        private static void SetId(SerializedProperty property, ulong high, ulong low)
        {
            property.FindPropertyRelative("high").ulongValue = high;
            property.FindPropertyRelative("low").ulongValue = low;
        }
    }
}
