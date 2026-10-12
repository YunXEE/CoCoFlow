using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;
using NUnit.Framework;
using UnityEditor;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStandardIntentAuthoringTests
    {
        [Test]
        public void WizardOutputIsTheEngineFreeCompiledFixtureAndHasNoDefaultIntent()
        {
            string script = CoCoStateScriptWizard.BuildScript("GeneratedIntentFree");
            Assert.That(script, Does.Not.Contain("CoCoIntentConsume"));
            Assert.That(script, Does.Not.Contain("RawInput"));
            Assert.That(script, Does.Not.Contain("UnityEngine"));
            string path = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("GeneratedIntentFreeLogic t:MonoScript").Single());
            Assert.That(File.ReadAllText(path).Replace("\r\n", "\n"), Is.EqualTo(script));
            Assert.That(typeof(GeneratedIntentFreeLogic).GetCustomAttributes(typeof(CoCoIntentConsumeAttribute), false), Is.Empty);
            Assert.That(StandardDescriptors.TryCreate(typeof(GeneratedIntentFreeLogic), "GeneratedIntentFree",
                out CoCoStateDescriptorId id), Is.True);
            CoCoStandardBindingProvider provider = CoCoStandardBindingProvider.BuildForAssemblyOf<GeneratedIntentFreeLogic>();
            Assert.That(provider.Catalog.TryGetStateDescriptor(id, out CoCoStateDescriptor state), Is.True);
            Assert.That(state.IntentRequirements, Is.Empty);
        }

        [Test]
        public void AuthorsAndGeneratedLogicHaveNoEngineOrInputModuleDependency()
        {
            foreach (Type type in new[] { typeof(GeneratedIntentFreeLogic), typeof(AnimOperationAuthor),
                         typeof(LocomotionOperationAuthor), typeof(CoCoStateExecutionContext) })
            {
                string[] references = type.Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
                Assert.That(references.Any(name => name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False, type.FullName);
                Assert.That(references.Any(name => name.Contains("Modules.Input") || name.Contains("Modules.Camera")), Is.False, type.FullName);
            }
        }

        [Test]
        public void MissingRegistrarAndDuplicateDeclarationsHaveStructuredDiagnostics()
        {
            Assembly missing = DeclareState(typeof(Guid), false);
            var missingError = Assert.Throws<CoCoStandardBindingException>(() =>
                CoCoStandardBindingProvider.Build(new[] { missing }));
            Assert.That(missingError.Diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.MissingIntentReducer));
            Assert.That(missingError.Message, Does.Contain(typeof(Guid).FullName));

            Assembly duplicate = DeclareState(typeof(RawInputIntent), true);
            var duplicateError = Assert.Throws<CoCoStandardBindingException>(() =>
                CoCoStandardBindingProvider.Build(new[] { duplicate }));
            Assert.That(duplicateError.Diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.DuplicateIdentifier));
        }

        [Test]
        public void InvalidAndConflictingRegistrarsRejectCatalogDiscovery()
        {
            var invalid = DeclareRegistrars(typeof(string));
            var invalidError = Assert.Throws<CoCoStandardBindingException>(() =>
                CoCoStandardBindingProvider.Build(new[] { invalid }));
            Assert.That(invalidError.Diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidIntentDescriptor));

            var duplicate = DeclareRegistrars(typeof(MetadataRegistrarProbe), typeof(DuplicateMetadataRegistrarProbe));
            var duplicateError = Assert.Throws<CoCoStandardBindingException>(() =>
                CoCoStandardBindingProvider.Build(new[] { duplicate }));
            Assert.That(duplicateError.Diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.DuplicateIdentifier));
        }

        private static TypeBuilder DefineProbe(Type baseType)
        {
            var name = new AssemblyName("IntentMetadataProbe" + Guid.NewGuid().ToString("N"));
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            return assembly.DefineDynamicModule(name.Name).DefineType("Probe", TypeAttributes.Public, baseType);
        }
        private static Assembly DeclareState(Type intent, bool duplicate)
        {
            TypeBuilder type = DefineProbe(typeof(CoCoStateLogic));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            type.SetCustomAttribute(new CustomAttributeBuilder(typeof(CoCoStateAttribute).GetConstructor(new[] { typeof(string) }),
                new object[] { "Probe" + Guid.NewGuid().ToString("N") }));
            var consume = new CustomAttributeBuilder(typeof(CoCoIntentConsumeAttribute).GetConstructor(new[] { typeof(Type) }),
                new object[] { intent });
            type.SetCustomAttribute(consume);
            if (duplicate) type.SetCustomAttribute(consume);
            return type.CreateType().Assembly;
        }
        private static Assembly DeclareRegistrars(params Type[] registrars)
        {
            TypeBuilder type = DefineProbe(typeof(object));
            foreach (Type registrar in registrars)
                type.SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(CoCoIntentRegistrationAttribute).GetConstructor(new[] { typeof(Type) }), new object[] { registrar }));
            return type.CreateType().Assembly;
        }

        public class MetadataRegistrarProbe : ICoCoStandardIntentRegistrar
        {
            public Type IntentType => typeof(Guid);
            public CoCoIntentId IntentId
            {
                get { CoCoIntentId.TryCreate(606, 606, out CoCoIntentId id); return id; }
            }
            public bool TryRegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder, out CoCoDiagnostic diagnostic)
            { diagnostic = CoCoDiagnostic.None; return true; }
            public bool TryRegisterRuntime(CoCoStateGraphHostBindingBuilder builder,
                out ICoCoStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic)
            { binding = null; diagnostic = CoCoDiagnostic.None; return false; }
        }
        public sealed class DuplicateMetadataRegistrarProbe : MetadataRegistrarProbe { }

        [Test]
        public void CatalogUsesTheSameIntentMetadataRegardlessOfAssemblyOrder()
        {
            var first = CoCoStandardBindingProvider.Build(new[] { typeof(GeneratedIntentFreeLogic).Assembly,
                typeof(CoCoStateExecutionContext).Assembly });
            var second = CoCoStandardBindingProvider.Build(new[] { typeof(CoCoStateExecutionContext).Assembly,
                typeof(GeneratedIntentFreeLogic).Assembly });
            Assert.That(first.Catalog.Fingerprint, Is.EqualTo(second.Catalog.Fingerprint));
        }
    }
}
