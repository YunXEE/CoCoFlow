using System;
using System.IO;
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
        }

        public int callbackOrder => -2000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = new BuildSettings
            {
                target = report.summary.platform.ToString(),
                backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Standalone).ToString(),
                unity = Application.unityVersion,
                outputPath = report.summary.outputPath
            };
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                settings.backend != "Mono" || settings.stripping != "Disabled")
                throw new BuildFailedException("The Player probe requires Windows x64 / Mono / Disabled stripping.");

            string project = Path.GetDirectoryName(Application.dataPath);
            string output = Path.GetFullPath(Path.Combine(project, "../../ci-results"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "player-build-settings.json"), JsonUtility.ToJson(settings, true));
        }
    }
}
