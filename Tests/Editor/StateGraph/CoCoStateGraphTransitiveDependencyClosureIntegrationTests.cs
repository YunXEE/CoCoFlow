using System;
using System.Reflection;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.TransitiveDependencyAuthor;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphTransitiveDependencyClosureIntegrationTests
    {
        [Test]
        public void DirectRuntimeGuardAcceptsWhileEditorClosureRejectsTransitiveLegacyCorePath()
        {
            string[] authorReferences = ReferencedAssemblyNames(typeof(TransitiveDependencyLogic));
            CollectionAssert.Contains(
                authorReferences,
                "CoCoFlow.Tests.StateGraphTransitiveDependencyHelper");
            CollectionAssert.DoesNotContain(authorReferences, "CoCoFlow.Runtime.Core");

            Assert.IsTrue(CoCoStateDescriptorId.TryCreate(
                0xD15UL,
                0xA17UL,
                out CoCoStateDescriptorId descriptorId));
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TransitiveDependencyFreezer(),
                new CoCoStateRuntimeRegistration<
                    TransitiveDependencyLogic,
                    TransitiveDependencyConfigSchema,
                    TransitiveDependencyMemory>(TransitiveDependencySchemas.Schema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic),
                registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);

            CoCoDiagnostic[] diagnostics =
                CoCoStateGraphAuthoringDependencyClosureValidator.Validate(
                    catalog,
                    AssembliesType.Editor);

            const string expectedMessage =
                "Graph author dependency closure " +
                "CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor -> " +
                "CoCoFlow.Tests.StateGraphTransitiveDependencyHelper -> " +
                "CoCoFlow.Runtime.Core references a forbidden framework boundary.";
            int expectedIndex = Array.FindIndex(
                diagnostics,
                item => string.Equals(item.Message, expectedMessage, StringComparison.Ordinal));
            Assert.GreaterOrEqual(
                expectedIndex,
                0,
                string.Join("\n", Array.ConvertAll(diagnostics, item => item.Message)));

            CoCoDiagnostic expected = diagnostics[expectedIndex];
            Assert.AreEqual(CoCoDiagnosticDomain.State, expected.Domain);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidAuthoringDependency,
                expected.Code);
            Assert.IsTrue(expected.IsError);
        }

        private static string[] ReferencedAssemblyNames(Type type)
        {
            AssemblyName[] references = type.Assembly.GetReferencedAssemblies();
            var names = new string[references.Length];
            for (int index = 0; index < references.Length; index++)
            {
                names[index] = references[index].Name;
            }

            return names;
        }
    }
}
