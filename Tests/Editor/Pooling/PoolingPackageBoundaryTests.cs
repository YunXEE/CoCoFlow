using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling.Tests
{
    public sealed class PoolingPackageBoundaryTests
    {
        [Test]
        public void BasePoolingReferencesOnlyContentContractsAndUniTask()
        {
            string packagePath = PackageInfo.FindForAssembly(typeof(PoolId).Assembly).resolvedPath;
            AssemblyDefinition definition = ReadAssemblyDefinition(
                packagePath,
                "Runtime/Pooling/CoCoFlow.Runtime.Pooling.asmdef");

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "CoCoFlow.Runtime.Core.Contracts",
                    "CoCoFlow.Runtime.Content",
                    "UniTask"
                },
                definition.references);
            Assert.That(
                definition.references.Any(reference =>
                    reference.IndexOf("Addressables", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reference.IndexOf("StateGraphHost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reference.IndexOf("Modules", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.False);
        }

        [Test]
        public void TemporalSidecarAddsOnlyPoolingStateGraphHostAndContracts()
        {
            string packagePath = PackageInfo.FindForAssembly(typeof(PoolId).Assembly).resolvedPath;
            AssemblyDefinition definition = ReadAssemblyDefinition(
                packagePath,
                "Runtime/Pooling/Temporal/CoCoFlow.Runtime.Pooling.Temporal.asmdef");

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "CoCoFlow.Runtime.Core.Contracts",
                    "CoCoFlow.Runtime.StateGraphHost",
                    "CoCoFlow.Runtime.Pooling"
                },
                definition.references);
            Assert.That(
                definition.references.Any(reference =>
                    reference.IndexOf("Addressables", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reference.IndexOf("Content", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.False);
        }

        [Test]
        public void PackageDoesNotDeclareAddressablesAsRequiredDependency()
        {
            string packagePath = PackageInfo.FindForAssembly(typeof(PoolId).Assembly).resolvedPath;
            string packageJson = File.ReadAllText(Path.Combine(packagePath, "package.json"));
            var manifest = JsonUtility.FromJson<PackageManifest>(packageJson);

            Assert.That(manifest, Is.Not.Null);
            Assert.That(
                manifest.dependencies == null ||
                manifest.dependencies.All(dependency =>
                    dependency.IndexOf(
                        "com.unity.addressables",
                        StringComparison.OrdinalIgnoreCase) < 0),
                Is.True);
            StringAssert.DoesNotContain("\"com.unity.addressables\"", packageJson);
        }

        [Test]
        public void UiModuleDoesNotReferencePoolingRuntime()
        {
            string packagePath = PackageInfo.FindForAssembly(typeof(PoolId).Assembly).resolvedPath;
            const string relativeRoot = "Runtime/Modules/UI";
            string root = Path.Combine(packagePath, relativeRoot);
            Assert.That(Directory.Exists(root), Is.True, root);
            foreach (string file in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".cs", StringComparison.Ordinal) &&
                    !string.Equals(extension, ".asmdef", StringComparison.Ordinal))
                {
                    continue;
                }

                string source = File.ReadAllText(file);
                StringAssert.DoesNotContain(
                    "CoCoFlow.Runtime.Pooling",
                    source,
                    file);
            }
        }

        private static AssemblyDefinition ReadAssemblyDefinition(
            string packagePath,
            string relativePath)
        {
            string path = Path.Combine(packagePath, relativePath);
            Assert.That(File.Exists(path), Is.True, path);
            AssemblyDefinition definition =
                JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path));
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.references, Is.Not.Null);
            return definition;
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string[] references;
        }

        [Serializable]
        private sealed class PackageManifest
        {
            public string[] dependencies;
        }
    }
}
