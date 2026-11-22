using System;
using System.Reflection;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.TransitiveDependencyAuthor;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphTransitiveDependencyClosureIntegrationTests
    {
        [Test]
        public void CatalogAndEditorAcceptGameplayUsingCoreThroughAHelper()
        {
            string[] authorReferences = ReferencedAssemblyNames(typeof(TransitiveDependencyLogic));
            CollectionAssert.Contains(
                authorReferences,
                "CoCoFlow.Tests.StateGraphTransitiveDependencyHelper");
            Assert.That(TransitiveDependencyLogic.ReadCoreViaHelper(), Is.EqualTo((int)CoCoLifecycleState.Uninitialized));

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
                CoCoStateGraphAuthoringDependencyClosureValidator.Validate(catalog);

            Assert.IsEmpty(diagnostics, string.Join("\n", Array.ConvertAll(diagnostics, item => item.Message)));
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
