using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoPackageBoundaryTests
    {
        [Test]
        public void ContractsAssemblyHasNoEngineGameplayModuleOrEditorReferences()
        {
            string[] assemblyReferences = typeof(CoCoStateLogic).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.IsFalse(assemblyReferences.Any(name =>
                name.StartsWith("Unity", StringComparison.Ordinal) ||
                name.StartsWith("CoCoFlow.Runtime.Gameplay", StringComparison.Ordinal) ||
                name.StartsWith("CoCoFlow.Runtime.Modules", StringComparison.Ordinal) ||
                name.StartsWith("CoCoFlow.Editor", StringComparison.Ordinal)));
        }

        [Test]
        public void ContractsAsmdefDeclaresEngineIndependentBoundary()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(CoCoStateLogic).Assembly);
            Assert.IsNotNull(packageInfo);

            string asmdefPath = Path.Combine(
                packageInfo.resolvedPath,
                "Runtime",
                "Core",
                "Contracts",
                "CoCoFlow.Runtime.Core.Contracts.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), asmdefPath);

            var definition = JsonUtility.FromJson<ContractsAssemblyDefinition>(
                File.ReadAllText(asmdefPath));
            Assert.IsNotNull(definition);
            Assert.IsNotNull(definition.references);

            Assert.AreEqual("CoCoFlow.Runtime.Core.Contracts", definition.name);
            Assert.AreEqual("CoCoFlow.Runtime.Core", definition.rootNamespace);
            Assert.IsEmpty(definition.references);
            Assert.IsTrue(definition.noEngineReferences);
            Assert.IsFalse(definition.references.Any(reference =>
                reference.StartsWith("CoCoFlow.Runtime.Gameplay", StringComparison.Ordinal) ||
                reference.StartsWith("CoCoFlow.Runtime.Modules", StringComparison.Ordinal) ||
                reference.StartsWith("CoCoFlow.Editor", StringComparison.Ordinal)));
        }

        [Test]
        public void PublicContractsContainNoMachineNodeOrOptionalSurface()
        {
            Type[] contractTypes = typeof(CoCoStateLogic).Assembly.GetExportedTypes();

            Assert.IsFalse(contractTypes.Any(type =>
                type.Name.IndexOf("Machine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.Name.IndexOf("Node", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.Name.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(contractTypes.SelectMany(type => type.GetMembers()).Any(member =>
                member.Name.IndexOf("Machine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Node", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void PublicContractsExposeNoUnityObjectTypes()
        {
            Type[] contractTypes = typeof(CoCoStateLogic).Assembly.GetExportedTypes();

            Assert.IsFalse(contractTypes.Any(type => typeof(UnityEngine.Object).IsAssignableFrom(type)));
        }

        [Serializable]
        private sealed class ContractsAssemblyDefinition
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public bool noEngineReferences;
        }
    }
}
