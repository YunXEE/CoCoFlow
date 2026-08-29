using System;
using CoCoFlow.Editor.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphDependencyClosureValidationTests
    {
        [Test]
        public void DirectForbiddenDependencyReportsTheCompletePath()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "CoCoFlow.Runtime.Core" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> CoCoFlow.Runtime.Core " +
                "references a forbidden framework boundary.");
        }

        [Test]
        public void TransitiveForbiddenDependencyReportsEveryPathSegment()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Helper", references: new[]
                {
                    "CoCoFlow.Runtime.Core.StateGraphAuthoring"
                }),
                Node("Author", references: new[] { "Helper" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> Helper -> " +
                "CoCoFlow.Runtime.Core.StateGraphAuthoring " +
                "references a forbidden framework boundary.");
        }

        [Test]
        public void SharedTransitiveViolationReportsEachDistinctCompletePath()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "Helper.B", "Helper.A" }),
                Node("Helper.A", references: new[] { "Shared" }),
                Node("Helper.B", references: new[] { "Shared" }),
                Node("Shared", references: new[] { "CoCoFlow.Runtime.Core" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> Helper.A -> Shared -> " +
                "CoCoFlow.Runtime.Core references a forbidden framework boundary.",
                "Graph author dependency closure Author -> Helper.B -> Shared -> " +
                "CoCoFlow.Runtime.Core references a forbidden framework boundary.");
        }

        [Test]
        public void CyclicSafeClosureTerminatesWithoutDiagnostics()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "Helper" }),
                Node("Helper", references: new[] { "Author" }));

            Assert.IsEmpty(diagnostics);
        }

        [Test]
        public void NoEngineReferencesFalseIsRejected()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", noEngineReferences: false));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author must set noEngineReferences:true.");
        }

        [Test]
        public void AssemblyWithoutAsmdefFailsClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", hasAssemblyDefinition: false));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author does not have an asmdef and cannot prove " +
                "noEngineReferences.");
        }

        [Test]
        public void UnresolvedCustomDependencyFailsClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "Missing.Helper" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> Missing.Helper cannot be resolved to a " +
                "Player asmdef.");
        }

        [Test]
        public void CustomAndForbiddenPrecompiledDependenciesFailClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node(
                    "Author",
                    precompiledReferences: new[]
                    {
                        "UnityEngine.CoreModule",
                        "Vendor.CustomRuntime"
                    }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> UnityEngine.CoreModule " +
                "references a forbidden precompiled assembly.",
                "Graph author dependency closure Author -> Vendor.CustomRuntime " +
                "references an unverifiable custom precompiled assembly.");
        }

        [Test]
        public void SystemPrefixedCustomAsmdefCannotBypassClosureValidation()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "SystemGameplay" }),
                Node("SystemGameplay", noEngineReferences: false));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> SystemGameplay must set " +
                "noEngineReferences:true.");
        }

        [Test]
        public void SystemPrefixedCustomPrecompiledDependencyFailsClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", precompiledReferences: new[] { "SystemGameplay" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> SystemGameplay references an " +
                "unverifiable custom precompiled assembly.");
        }

        [Test]
        public void SystemIdentityCustomPrecompiledDependencyCannotSpoofFrameworkProvenance()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", precompiledReferences: new[] { "System.Core" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> System.Core references an " +
                "unverifiable custom precompiled assembly.");
        }

        [Test]
        public void DiagnosticsAreCanonicalAcrossRootAndNodeInputOrder()
        {
            var authorA = Node("Author.A", references: new[] { "Missing.A" });
            var authorZ = Node("Author.Z", references: new[] { "Missing.Z" });

            CoCoDiagnostic[] forward = Validate(
                new[] { "Author.Z", "Author.A", "Author.A" },
                authorZ,
                authorA);
            CoCoDiagnostic[] reverse = Validate(
                new[] { "Author.A", "Author.Z" },
                authorA,
                authorZ);

            string[] expected =
            {
                "Graph author dependency closure Author.A -> Missing.A cannot be resolved to a " +
                "Player asmdef.",
                "Graph author dependency closure Author.Z -> Missing.Z cannot be resolved to a " +
                "Player asmdef."
            };
            AssertDiagnosticMessages(forward, expected);
            AssertDiagnosticMessages(reverse, expected);
        }

        [Test]
        public void NullCatalogProducesAClosedFailureDiagnostic()
        {
            CoCoDiagnostic[] diagnostics =
                CoCoStateGraphAuthoringDependencyClosureValidator.Validate(
                    null,
                    Array.Empty<UnityEditor.Compilation.Assembly>());

            AssertDiagnosticMessages(
                diagnostics,
                "A frozen Graph Descriptor Catalog is required for dependency closure validation.");
        }

        private static CoCoDiagnostic[] Validate(
            string[] roots,
            params CoCoStateGraphAssemblyGraphNode[] nodes) =>
            CoCoStateGraphAuthoringDependencyClosureValidator.Validate(roots, nodes);

        private static CoCoStateGraphAssemblyGraphNode Node(
            string name,
            bool hasAssemblyDefinition = true,
            bool noEngineReferences = true,
            string[] references = null,
            string[] precompiledReferences = null) =>
            new CoCoStateGraphAssemblyGraphNode(
                name,
                hasAssemblyDefinition,
                noEngineReferences,
                references,
                precompiledReferences);

        private static void AssertDiagnosticMessages(
            CoCoDiagnostic[] diagnostics,
            params string[] expectedMessages)
        {
            Assert.AreEqual(expectedMessages.Length, diagnostics.Length);
            for (int index = 0; index < diagnostics.Length; index++)
            {
                Assert.AreEqual(CoCoDiagnosticDomain.State, diagnostics[index].Domain);
                Assert.AreEqual(
                    CoCoDiagnosticCode.InvalidAuthoringDependency,
                    diagnostics[index].Code);
                Assert.IsTrue(diagnostics[index].IsError);
                Assert.AreEqual(expectedMessages[index], diagnostics[index].Message);
            }
        }
    }
}
