using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Automatic installation for the standard binding provider. Scans all
    /// loaded non-package assemblies for CoCoStateAttribute classes on play
    /// start; when any exist, installs CoCoStandardBindingProvider before
    /// any Host Start() reads it. Domain-reload safe by construction
    /// (re-runs every play). Projects with an explicit provider keep theirs:
    /// installation is skipped when one is already installed.
    /// </summary>
    public static class CoCoFlowRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallBeforeSceneLoad() => InstallStandardBinding();

        /// <summary>
        /// Scene-assembly safety net: at BeforeSceneLoad time a project's
        /// state-logic assembly may not be loaded yet (nothing referenced
        /// it — the scene itself is the first referencer, and it loads
        /// after this phase). By AfterSceneLoad every scene-referenced
        /// assembly is loaded, Awake has run, and Start has not — the
        /// second pass catches cold-start plays where the first found
        /// nothing and silently skipped installation, leaving Hosts to
        /// fail with "no provider installed".
        /// </summary>
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad() => InstallStandardBinding();

        private static void InstallStandardBinding()
        {
            try
            {
                if (CoCoStateGraphProjectBindings.IsInstalled)
                {
                    return;
                }

                var assemblies = new System.Collections.Generic.List<
                    System.Reflection.Assembly>();
                foreach (System.Reflection.Assembly assembly in
                         AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = assembly.GetName().Name ?? string.Empty;
                    if (name.StartsWith("CoCoFlow.", StringComparison.Ordinal) ||
                        name.StartsWith("Unity", StringComparison.Ordinal) ||
                        name.StartsWith("System", StringComparison.Ordinal) ||
                        name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                        name.StartsWith("netstandard", StringComparison.Ordinal) ||
                        name.StartsWith("Mono", StringComparison.Ordinal) ||
                        name.StartsWith("com.unity", StringComparison.Ordinal) ||
                        name.StartsWith("UnityEngine", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    assemblies.Add(assembly);
                }

                CoCoStandardBindingProvider provider =
                    CoCoStandardBindingProvider.Build(assemblies);
                if (!CoCoStateGraphProjectBindings.TryInstall(
                        provider,
                        out CoCoDiagnostic diagnostic))
                {
                    Debug.LogWarning(
                        "[CoCoFlow] standard binding not installed: " +
                        diagnostic.Message);
                }
            }
            catch (InvalidOperationException)
            {
                // No CoCoState classes found — this project does not use the
                // standard path; nothing to install.
            }
        }
    }
}
