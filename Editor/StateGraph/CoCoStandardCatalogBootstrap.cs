#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// Feeds the graph authoring window's descriptor lists from the standard
    /// catalog. Rebuilds on domain reload and on demand via Rescan().
    /// </summary>
    [InitializeOnLoad]
    internal static class CoCoStandardCatalogBootstrap
    {
        private static CoCoStandardBindingProvider _provider;

        static CoCoStandardCatalogBootstrap()
        {
            Rescan();
        }

        internal static void Rescan()
        {
            try
            {
                var assemblies = new List<Assembly>();
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
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

                _provider = CoCoStandardBindingProvider.Build(assemblies);
            }
            catch (InvalidOperationException)
            {
                _provider = null; // no CoCoState classes in this project
            }

            CoCoStateGraphEditorCatalogProvider.Provider =
                _provider != null ? (Func<CoCoGraphDescriptorCatalog>)(() => _provider.Catalog) : null;
        }

        [MenuItem("CoCoFlow/Setup/Rescan Standard States")]
        internal static void RescanMenu()
        {
            Rescan();
            Debug.Log(
                _provider != null
                    ? "[CoCoFlow] standard catalog refreshed."
                    : "[CoCoFlow] no CoCoState-attributed classes found.");
        }
    }
}
#endif
