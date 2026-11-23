using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphTransitiveDependencyClosureIntegrationTests
    {
        [Test]
        public void CatalogAndEditorAcceptGameplayUsingCoreThroughAHelper()
        {
            string[] authorReferences = ReferencedAssemblyNames(typeof(TransitiveDependencyLogic));
            CollectionAssert.Contains(
                authorReferences,
                "CoCoFlow.Tests.StateGraphTransitiveDependencyHelper");
            Assert.That(TransitiveDependencyLogic.ReadCoreViaHelper(), Is.EqualTo((int)CoCoLifecycleState.Uninitialized));

            Assert.IsTrue(StateDescriptorId.TryCreate(
                0xD15UL,
                0xA17UL,
                out StateDescriptorId descriptorId));
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TransitiveDependencyFreezer(),
                new StateRuntimeRegistration<
                    TransitiveDependencyLogic,
                    TransitiveDependencyConfigSchema,
                    TransitiveDependencyMemory>(TransitiveDependencySchemas.Schema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic),
                registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);

            CoCoDiagnostic[] diagnostics =
                StateGraphAuthoringDependencyClosureValidator.Validate(catalog);

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
