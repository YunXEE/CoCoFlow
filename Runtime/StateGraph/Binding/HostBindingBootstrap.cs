using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在场景加载前后发现项目状态并安装绑定，保留先行显式安装的 Provider。</summary>
    internal static class HostBindingBootstrap
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
                if (HostProjectBindings.IsInstalled)
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
                        name.StartsWith("com.unity", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    assemblies.Add(assembly);
                }

                if (!HostBindingProvider.HasStateDeclarations(assemblies)) return;

                HostBindingProvider provider =
                    HostBindingProvider.Build(assemblies);
                if (!HostProjectBindings.TryInstall(
                        provider,
                        out CoCoDiagnostic diagnostic))
                {
                    Debug.LogWarning(
                        "[CoCoFlow] standard binding not installed: " +
                        diagnostic.Message);
                }
            }
            catch (InvalidOperationException error)
            {
                Debug.LogError("[CoCoFlow] " + error.Message);
            }
        }
    }
}
