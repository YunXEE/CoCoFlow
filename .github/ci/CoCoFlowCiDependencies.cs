using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace CoCoFlow.CI
{
    // Copied into the disposable host only; never imported as product code.
    public sealed class CoCoFlowCiDependencies
    {
        [Serializable] public sealed class ResolvedPackage
        {
            public string name;
            public string version;
        }

        [Serializable] public sealed class Requirements
        {
            public string unityVersion;
            public string editorPlatform;
            public string[] assemblies;
            public string[] packages;
            public string[] defines;
            public string dotweenVersion;
        }

        [Serializable] public sealed class Evidence
        {
            public string unity;
            public string platform;
            public string[] assemblies;
            public ResolvedPackage[] packages;
            public string defines;
        }

        [Test]
        public void RequiredDependenciesAndOptionalModulesAreCompiled()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            var required = JsonUtility.FromJson<Requirements>(
                File.ReadAllText(Path.Combine(project, "CiRequirements.json")));
            string[] assemblies = CompilationPipeline.GetAssemblies()
                .Select(assembly => assembly.name)
                .Concat(AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetName().Name))
                .Distinct().OrderBy(name => name).ToArray();
            ResolvedPackage[] packages = PackageInfo.GetAllRegisteredPackages()
                .Select(package => new ResolvedPackage { name = package.name, version = package.version })
                .OrderBy(package => package.name).ToArray();
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            string output = Path.GetFullPath(Path.Combine(project, "../../ci-results"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "dependencies.json"), JsonUtility.ToJson(
                new Evidence { unity = Application.unityVersion, platform = Application.platform.ToString(),
                    assemblies = assemblies,
                    packages = packages, defines = defines }, true));

            Assert.That(Application.unityVersion, Is.EqualTo(required.unityVersion));
            Assert.That(Application.platform.ToString(), Is.EqualTo(required.editorPlatform));
            foreach (string name in required.assemblies)
                CollectionAssert.Contains(assemblies, name, "Required assembly was not compiled: " + name);
            // UPM resolves indirect minimum requirements; a valid upgrade is not a failure.
            // Assembly checks and package behavior tests exercise the resolved integrations.
            foreach (string name in required.packages)
                Assert.That(packages.Any(package => package.name == name),
                    Is.True, "Required package was not resolved: " + name);
            foreach (string define in required.defines)
                CollectionAssert.Contains(defines.Split(';'), define, "Missing support define: " + define);
            CollectionAssert.DoesNotContain(defines.Split(';'), "COCOFLOW_UNITASK_SUPPORT",
                "UPM UniTask must use versionDefines, not a copied manual define.");
            Type dotween = Type.GetType("DG.Tweening.DOTween, DOTween");
            Assert.IsNotNull(dotween, "DOTween's runtime DLL must be available.");
            Assert.That(dotween.GetField("Version")?.GetValue(null),
                Is.EqualTo(required.dotweenVersion));
        }
    }
}
