using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CoCoFlow.CI
{
    // Disposable-host evidence only; never imported as product code.
    public sealed class CoCoFlowCiPlayerBuild : IPreprocessBuildWithReport
    {
        [Serializable]
        private sealed class BuildSettings
        {
            public string target;
            public string backend;
            public string stripping;
            public string unity;
            public string outputPath;
            public string codeGeneration;
            public string diagnosticPreservationSha256;
        }

        [Serializable]
        private sealed class Requirements
        {
            public string backend = string.Empty;
            public string stripping = string.Empty;
        }

        public int callbackOrder => -2000;

        public void OnPreprocessBuild(BuildReport report)
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            var required = JsonUtility.FromJson<Requirements>(
                File.ReadAllText(Path.Combine(project, "CiPlayerProbeRequirements.json")));
            var requiredBackend = (ScriptingImplementation)Enum.Parse(typeof(ScriptingImplementation), required.backend);
            var requiredStripping = (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), required.stripping);
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            ManagedStrippingLevel stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Standalone);
            var settings = new BuildSettings
            {
                target = report.summary.platform.ToString(),
                backend = backend.ToString(),
                stripping = stripping.ToString(),
                unity = Application.unityVersion,
                outputPath = report.summary.outputPath,
                codeGeneration = PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.Standalone).ToString()
            };
            string diagnosticLink = Path.Combine(project, "Assets/PlayerProbe/player-probe-metadata.link.xml");
            if (File.Exists(diagnosticLink))
                using (var sha256 = SHA256.Create())
                    settings.diagnosticPreservationSha256 = BitConverter.ToString(
                        sha256.ComputeHash(File.ReadAllBytes(diagnosticLink))).Replace("-", "").ToLowerInvariant();
            string output = Path.GetFullPath(Path.Combine(project, "../../ci-results"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "player-build-settings.json"), JsonUtility.ToJson(settings, true));
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                backend != requiredBackend || stripping != requiredStripping)
                throw new BuildFailedException($"The Player probe requires Windows x64 / {required.backend} / {required.stripping} stripping. " +
                    $"Observed: target={settings.target}, backend={settings.backend}, stripping={settings.stripping}.");
        }
    }
}
