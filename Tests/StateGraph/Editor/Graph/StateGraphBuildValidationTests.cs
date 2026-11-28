using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Editor.PlayerMetadata;
using CoCoFlow.Tests.StateGraphPlayerMetadataFixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateGraphBuildValidationTests
    {
        private Func<GraphDescriptorCatalog> originalProvider;

        [SetUp]
        public void SetUp()
        {
            originalProvider = StateGraphEditorCatalogProvider.Provider;
            StateGraphEditorCatalogProvider.Provider = null;
        }

        [TearDown]
        public void TearDown()
        {
            StateGraphEditorCatalogProvider.Provider = originalProvider;
        }

        [Test]
        public void RequireCatalogRejectsMissingProvider()
        {
            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => StateGraphBuildValidation.RequireCatalog());

            StringAssert.Contains("require a registered", exception.Message);
        }

        [Test]
        public void RequireCatalogWrapsProviderFailure()
        {
            StateGraphEditorCatalogProvider.Provider =
                () => throw new InvalidOperationException("synthetic provider failure");

            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => StateGraphBuildValidation.RequireCatalog());

            StringAssert.Contains("provider threw", exception.Message);
            StringAssert.Contains("synthetic provider failure", exception.Message);
        }

        [Test]
        public void RequireCatalogRejectsNullProviderResult()
        {
            StateGraphEditorCatalogProvider.Provider = () => null;

            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => StateGraphBuildValidation.RequireCatalog());

            StringAssert.Contains("frozen, non-null catalog", exception.Message);
        }

        [Test]
        public void RequireCatalogReturnsFrozenProviderResult()
        {
            GraphDescriptorCatalog catalog =
                StateGraphTestFactory.CreateCatalog(includeManifestRequirements: false);
            StateGraphEditorCatalogProvider.Provider = () => catalog;

            Assert.AreSame(catalog, StateGraphBuildValidation.RequireCatalog());
            Assert.IsTrue(catalog.IsFrozen);
        }

        [Test]
        public void BuildPreprocessorRequiresProviderWhenAStateGraphAssetExists()
        {
            WithTemporaryStateGraphAsset(() =>
            {
                BuildFailedException exception = Assert.Throws<BuildFailedException>(
                    () => new StateGraphBuildPreprocessor().OnPreprocessBuild(null));

                StringAssert.Contains("require a registered", exception.Message);
            });
        }

        [Test]
        public void BuildPreprocessorAcceptsFrozenCatalogWithSafeEmptyClosure()
        {
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            StateGraphEditorCatalogProvider.Provider = () => catalog;

            WithTemporaryStateGraphAsset(() =>
                Assert.DoesNotThrow(
                    () => new StateGraphBuildPreprocessor().OnPreprocessBuild(null)));
        }

        [Test]
        public void BuildCallbacksNoOpWithoutStateGraphAssetsOrProvider()
        {
            if (StateGraphBuildValidation.HasStateGraphAssets())
            {
                Assert.Ignore("The current host already contains a StateGraph Asset.");
            }

            Assert.DoesNotThrow(
                () => new StateGraphBuildPreprocessor().OnPreprocessBuild(null));
            Assert.IsNull(
                new StateGraphOperationLinkerProcessor().GenerateAdditionalLinkXmlFile(
                    null,
                    null));
        }

        [Test]
        public void EditorAssemblyScanFindsValidOperationSectionShape()
        {
            Type[] sections = StateGraphBuildValidation.CollectAndValidateOperations(
                AssembliesType.Editor,
                out _);

            CollectionAssert.Contains(sections, typeof(ILinkerTestSection));
        }

        [Test]
        public void UnityLinkerDirectoryScanReadsExactAssemblyBytes()
        {
            UnityEditor.Compilation.Assembly[] editorAssemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            UnityEditor.Compilation.Assembly sectionAssembly = Array.Find(
                editorAssemblies,
                item => string.Equals(
                    item.name,
                    typeof(IPlayerMetadataGenericSection).Assembly.GetName().Name,
                    StringComparison.Ordinal));
            Assert.IsNotNull(sectionAssembly);
            string inputDirectory = Path.GetDirectoryName(
                Path.GetFullPath(sectionAssembly.outputPath));
            UnityEditor.Compilation.Assembly[] scanAssemblies =
                CollectAssemblyClosure(sectionAssembly);

            PlayerOperationMetadataEntry[] entries =
                StateGraphBuildValidation.CollectAndValidatePlayerOperationMetadata(
                    scanAssemblies,
                    inputDirectory,
                    out CoCoDiagnostic[] diagnostics);

            Assert.IsEmpty(
                diagnostics,
                string.Join(
                    Environment.NewLine,
                    Array.ConvertAll(diagnostics, item => item.Message)));
            Assert.IsTrue(Array.Exists(
                entries,
                item => string.Equals(
                            item.TypeFullName,
                            typeof(IPlayerMetadataGenericSection).FullName,
                            StringComparison.Ordinal) &&
                        string.Equals(item.Preserve, "all", StringComparison.Ordinal)));
            Assert.IsTrue(Array.Exists(
                entries,
                item => string.Equals(
                            item.TypeFullName,
                            typeof(PlayerMetadataNestedValue).FullName,
                            StringComparison.Ordinal) &&
                        string.Equals(item.Preserve, "fields", StringComparison.Ordinal)));
            Assert.IsFalse(Array.Exists(
                entries,
                item => string.Equals(
                    item.TypeFullName,
                    typeof(ValueTuple<,>).FullName,
                    StringComparison.Ordinal)));

            string path = StateGraphBuildValidation.WriteOperationLinkXml(entries);
            var document = new XmlDocument();
            document.Load(path);
            AssertPreserve(
                document,
                typeof(IPlayerMetadataGenericSection).Assembly.GetName().Name,
                typeof(IPlayerMetadataGenericSection),
                "all");
            AssertPreserve(
                document,
                typeof(PlayerMetadataNestedValue).Assembly.GetName().Name,
                typeof(PlayerMetadataNestedValue),
                "fields");
        }

        [Test]
        public void UnityLinkerDirectoryScanFailsClosedWithStableDiagnostic()
        {
            PlayerOperationMetadataEntry[] entries =
                StateGraphBuildValidation.CollectAndValidatePlayerOperationMetadata(
                    Array.Empty<UnityEditor.Compilation.Assembly>(),
                    (string)null,
                    out CoCoDiagnostic[] diagnostics);

            Assert.IsEmpty(entries);
            Assert.AreEqual(1, diagnostics.Length);
            Assert.AreEqual(
                "UnityLinker Player assembly metadata directory is unavailable.",
                diagnostics[0].Message);
        }

        [Test]
        public void RuntimeGraphMetadataRetainsInvalidDeclarationsForRuntimeDiagnostics()
        {
            var scanner = new PlayerOperationMetadataScanner();
            string assemblyPath = typeof(ILinkerTestSection).Assembly.Location;
            var result = scanner.Scan(
                Path.GetDirectoryName(assemblyPath),
                new[] { typeof(ILinkerTestSection).Assembly.GetName().Name },
                false);

            Assert.IsEmpty(result.Diagnostics);
            Assert.IsTrue(Array.Exists(result.Entries, entry =>
                entry.TypeFullName == PlayerOperationMetadataNaming.GetLinkerTypeFullName(
                    typeof(IInvalidRuntimeLinkerSection)) && entry.Preserve == "all"));
            Assert.IsTrue(Array.Exists(result.Entries, entry =>
                entry.TypeFullName == PlayerOperationMetadataNaming.GetLinkerTypeFullName(
                    typeof(LinkerAttributedState)) && entry.Preserve == "all"));

            var validation = scanner.Scan(
                Path.GetDirectoryName(assemblyPath),
                new[] { typeof(ILinkerTestSection).Assembly.GetName().Name });
            Assert.IsTrue(Array.Exists(validation.Diagnostics, message =>
                message.Contains(nameof(IInvalidRuntimeLinkerSection))));
        }

        [Test]
        public void PlayerMetadataScanFailsClosedWhenSelectedDependencyIsUnavailable()
        {
            UnityEditor.Compilation.Assembly sectionAssembly = Array.Find(
                CompilationPipeline.GetAssemblies(AssembliesType.Editor),
                item => string.Equals(
                    item.name,
                    typeof(IPlayerMetadataGenericSection).Assembly.GetName().Name,
                    StringComparison.Ordinal));
            Assert.IsNotNull(sectionAssembly);
            UnityEditor.Compilation.Assembly[] scanAssemblies =
                CollectAssemblyClosure(sectionAssembly);

            PlayerOperationMetadataEntry[] entries =
                StateGraphBuildValidation.CollectAndValidatePlayerOperationMetadata(
                    scanAssemblies,
                    new[] { sectionAssembly.outputPath },
                    out CoCoDiagnostic[] diagnostics);

            Assert.IsEmpty(entries);
            Assert.IsNotEmpty(diagnostics);
        }

        [Test]
        public void LinkXmlPreservesSectionAndRecursiveValueMetadataDeterministically()
        {
            string path = StateGraphBuildValidation.WriteOperationLinkXml(
                new[] { typeof(ILinkerTestSection) });
            string firstContents = File.ReadAllText(path);
            string secondPath = StateGraphBuildValidation.WriteOperationLinkXml(
                new[] { typeof(ILinkerTestSection) });
            string secondContents = File.ReadAllText(secondPath);

            Assert.AreEqual(path, secondPath);
            Assert.AreEqual(firstContents, secondContents);

            var document = new XmlDocument();
            document.Load(path);
            string assemblyName = typeof(ILinkerTestSection).Assembly.GetName().Name;
            AssertPreserve(document, assemblyName, typeof(ILinkerTestSection), "all");
            AssertPreserve(document, assemblyName, typeof(LinkerOuterValue), "fields");
            AssertPreserve(document, assemblyName, typeof(LinkerInnerValue), "fields");
            Assert.IsNull(FindTypeNode(document, typeof(int).Assembly.GetName().Name, typeof(int)));
            StringAssert.Contains(
                "/ILinkerTestSection",
                PlayerOperationMetadataNaming.GetLinkerTypeFullName(
                    typeof(ILinkerTestSection)));
            StringAssert.DoesNotContain(
                typeof(ILinkerTestSection).FullName,
                firstContents);
        }

        [Test]
        public void LinkXmlRecursesIntoGenericBclValueArguments()
        {
            string path = StateGraphBuildValidation.WriteOperationLinkXml(
                new[] { typeof(IGenericLinkerSection) });

            var document = new XmlDocument();
            document.Load(path);
            string assemblyName = typeof(IGenericLinkerSection).Assembly.GetName().Name;
            AssertPreserve(document, assemblyName, typeof(IGenericLinkerSection), "all");
            AssertPreserve(document, assemblyName, typeof(GenericNestedValue), "fields");
            Assert.IsNull(FindTypeNode(
                document,
                typeof(ValueTuple<,>).Assembly.GetName().Name,
                typeof(ValueTuple<,>)));
        }

        [Test]
        public void MetadataLinkXmlWritesCanonicalEntriesDeterministically()
        {
            var entries = new[]
            {
                new PlayerOperationMetadataEntry(
                    "Synthetic.Values",
                    "Synthetic.Values.Payload",
                    "fields"),
                new PlayerOperationMetadataEntry(
                    "Synthetic.Sections",
                    "Synthetic.Sections.ICommands",
                    "fields"),
                new PlayerOperationMetadataEntry(
                    "Synthetic.Sections",
                    "Synthetic.Sections.ICommands",
                    "all")
            };
            string path = StateGraphBuildValidation.WriteOperationLinkXml(entries);
            string firstContents = File.ReadAllText(path);

            Array.Reverse(entries);
            string secondPath = StateGraphBuildValidation.WriteOperationLinkXml(entries);
            string secondContents = File.ReadAllText(secondPath);

            Assert.AreEqual(path, secondPath);
            Assert.AreEqual(firstContents, secondContents);
            var document = new XmlDocument();
            document.Load(path);
            Assert.AreEqual(
                "all",
                FindTypeNode(
                    document,
                    "Synthetic.Sections",
                    "Synthetic.Sections.ICommands")?.Attributes?["preserve"]?.Value);
            Assert.AreEqual(
                "fields",
                FindTypeNode(
                    document,
                    "Synthetic.Values",
                    "Synthetic.Values.Payload")?.Attributes?["preserve"]?.Value);
        }

        public interface ILinkerTestSection : IOperation
        {
            LinkerOuterValue Payload { get; }
        }

        public interface IInvalidRuntimeLinkerSection : IOperation
        {
            string Value { get; }
        }

        [State("linker-attributed-state")]
        public sealed class LinkerAttributedState
        {
            [StateConfig] public LinkerOuterValue Config;
        }

        public struct LinkerOuterValue
        {
            public LinkerInnerValue Inner;
            public long Sequence;
        }

        public struct LinkerInnerValue
        {
            public int Value;
        }

        public interface IGenericLinkerSection : IOperation
        {
            ValueTuple<GenericNestedValue, int> Payload { get; }
        }

        public struct GenericNestedValue
        {
            public long Value;
        }

        private static void AssertPreserve(
            XmlDocument document,
            string assemblyName,
            Type type,
            string expectedPreserve)
        {
            XmlNode node = FindTypeNode(document, assemblyName, type);
            Assert.IsNotNull(node, type.FullName);
            Assert.AreEqual(expectedPreserve, node.Attributes?["preserve"]?.Value);
        }

        private static XmlNode FindTypeNode(
            XmlDocument document,
            string assemblyName,
            Type type) => FindTypeNode(
                document,
                assemblyName,
                PlayerOperationMetadataNaming.GetLinkerTypeFullName(type));

        private static XmlNode FindTypeNode(
            XmlDocument document,
            string assemblyName,
            string typeFullName) =>
            document.SelectSingleNode(
                $"/linker/assembly[@fullname='{assemblyName}']/type[@fullname='{typeFullName}']");

        private static UnityEditor.Compilation.Assembly[] CollectAssemblyClosure(
            UnityEditor.Compilation.Assembly root)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<UnityEditor.Compilation.Assembly>();
            var closure = new List<UnityEditor.Compilation.Assembly>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                UnityEditor.Compilation.Assembly current = pending.Pop();
                if (current == null || !visited.Add(current.name))
                {
                    continue;
                }

                closure.Add(current);
                UnityEditor.Compilation.Assembly[] references = current.assemblyReferences;
                for (int index = 0; index < references.Length; index++)
                {
                    pending.Push(references[index]);
                }
            }

            closure.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.name, right.name));
            return closure.ToArray();
        }

        private static void WithTemporaryStateGraphAsset(Action assertion)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/CoCoFlowD15BuildGateTest.asset");
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                Assert.IsTrue(StateGraphBuildValidation.HasStateGraphAssets());
                assertion();
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
