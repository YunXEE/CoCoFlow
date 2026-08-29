using System;
using System.Collections.Generic;
using System.IO;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityAssembly = UnityEditor.Compilation.Assembly;

namespace CoCoFlow.Editor.StateGraph
{
    internal static class CoCoStateGraphAuthoringDependencyClosureValidator
    {
        private static readonly string[] AllowedFrameworkAssemblies =
        {
            "CoCoFlow.Runtime.Core.Contracts",
            "CoCoFlow.Runtime.Core.StateFlow",
            "CoCoFlow.Runtime.Core.StateGraph"
        };

        private static readonly string[] ForbiddenExactAssemblies =
        {
            "CoCoFlow.Runtime.Core",
            "CoCoFlow.Runtime.Core.StateGraphAuthoring"
        };

        private static readonly string[] ForbiddenAssemblyPrefixes =
        {
            "CoCoFlow.Editor",
            "CoCoFlow.Runtime.Gameplay",
            "CoCoFlow.Runtime.Modules",
            "UnityEditor",
            "UnityEngine",
            "Unity."
        };

        internal static CoCoDiagnostic[] Validate(
            CoCoGraphDescriptorCatalog catalog,
            AssembliesType assembliesType = AssembliesType.Player)
        {
            return Validate(catalog, CompilationPipeline.GetAssemblies(assembliesType));
        }

        internal static CoCoDiagnostic[] Validate(
            CoCoGraphDescriptorCatalog catalog,
            UnityAssembly[] assemblies)
        {
            if (catalog == null || !catalog.IsFrozen)
            {
                return new[]
                {
                    Error("A frozen Graph Descriptor Catalog is required for dependency closure validation.")
                };
            }

            return Validate(catalog.AuthorAssemblyRootNames, Capture(assemblies));
        }

        internal static CoCoDiagnostic[] Validate(
            IReadOnlyList<string> rootAssemblyNames,
            IReadOnlyList<CoCoStateGraphAssemblyGraphNode> assemblies)
        {
            var nodes = new Dictionary<string, CoCoStateGraphAssemblyGraphNode>(StringComparer.Ordinal);
            if (assemblies != null)
            {
                for (int index = 0; index < assemblies.Count; index++)
                {
                    CoCoStateGraphAssemblyGraphNode assembly = assemblies[index];
                    if (assembly == null || string.IsNullOrEmpty(assembly.Name))
                    {
                        continue;
                    }

                    nodes[assembly.Name] = assembly;
                }
            }

            var messages = new SortedSet<string>(StringComparer.Ordinal);
            if (rootAssemblyNames != null)
            {
                var roots = new string[rootAssemblyNames.Count];
                for (int index = 0; index < roots.Length; index++)
                {
                    roots[index] = rootAssemblyNames[index];
                }

                Array.Sort(roots, StringComparer.Ordinal);
                for (int index = 0; index < roots.Length; index++)
                {
                    ValidateRoot(roots[index], nodes, messages);
                }
            }

            var diagnostics = new CoCoDiagnostic[messages.Count];
            int diagnosticIndex = 0;
            foreach (string message in messages)
            {
                diagnostics[diagnosticIndex++] = Error(message);
            }

            return diagnostics;
        }

        private static void ValidateRoot(
            string rootAssemblyName,
            IReadOnlyDictionary<string, CoCoStateGraphAssemblyGraphNode> nodes,
            ISet<string> messages)
        {
            if (string.IsNullOrWhiteSpace(rootAssemblyName))
            {
                messages.Add("Author assembly root is missing its assembly name.");
                return;
            }

            var visitedPath = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            Visit(rootAssemblyName, nodes, visitedPath, path, messages);
        }

        private static void Visit(
            string assemblyName,
            IReadOnlyDictionary<string, CoCoStateGraphAssemblyGraphNode> nodes,
            ISet<string> visitedPath,
            IList<string> path,
            ISet<string> messages)
        {
            path.Add(assemblyName);
            if (IsForbidden(assemblyName))
            {
                messages.Add(FormatPath(path, "references a forbidden framework boundary"));
                path.RemoveAt(path.Count - 1);
                return;
            }

            if (IsAllowedFrameworkBoundary(assemblyName))
            {
                path.RemoveAt(path.Count - 1);
                return;
            }

            if (!nodes.TryGetValue(assemblyName, out CoCoStateGraphAssemblyGraphNode node))
            {
                if (IsBaseClassLibrary(assemblyName))
                {
                    path.RemoveAt(path.Count - 1);
                    return;
                }

                messages.Add(FormatPath(path, "cannot be resolved to a Player asmdef"));
                path.RemoveAt(path.Count - 1);
                return;
            }

            if (!visitedPath.Add(assemblyName))
            {
                path.RemoveAt(path.Count - 1);
                return;
            }

            if (!node.HasAssemblyDefinition)
            {
                messages.Add(FormatPath(path, "does not have an asmdef and cannot prove noEngineReferences"));
            }
            else if (!node.NoEngineReferences)
            {
                messages.Add(FormatPath(path, "must set noEngineReferences:true"));
            }

            for (int index = 0; index < node.PrecompiledReferences.Length; index++)
            {
                string reference = node.PrecompiledReferences[index];
                var referencePath = new List<string>(path) { reference };
                messages.Add(FormatPath(
                    referencePath,
                    IsForbidden(reference)
                        ? "references a forbidden precompiled assembly"
                        : "references an unverifiable custom precompiled assembly"));
            }

            for (int index = 0; index < node.AssemblyReferences.Length; index++)
            {
                Visit(
                    node.AssemblyReferences[index],
                    nodes,
                    visitedPath,
                    path,
                    messages);
            }

            visitedPath.Remove(assemblyName);
            path.RemoveAt(path.Count - 1);
        }

        private static string FormatPath(IList<string> path, string reason) =>
            $"Graph author dependency closure {string.Join(" -> ", path)} {reason}.";

        private static bool IsForbidden(string assemblyName)
        {
            for (int index = 0; index < ForbiddenExactAssemblies.Length; index++)
            {
                if (string.Equals(
                        assemblyName,
                        ForbiddenExactAssemblies[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            for (int index = 0; index < ForbiddenAssemblyPrefixes.Length; index++)
            {
                if (assemblyName.StartsWith(
                        ForbiddenAssemblyPrefixes[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static CoCoStateGraphAssemblyGraphNode[] Capture(UnityAssembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                return Array.Empty<CoCoStateGraphAssemblyGraphNode>();
            }

            StringComparer pathComparer = Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var systemAssemblyPaths = new HashSet<string>(pathComparer);
            string[] systemAssemblies = CompilationPipeline.GetPrecompiledAssemblyPaths(
                CompilationPipeline.PrecompiledAssemblySources.SystemAssembly);
            for (int index = 0; index < systemAssemblies.Length; index++)
            {
                systemAssemblyPaths.Add(Path.GetFullPath(systemAssemblies[index]));
            }

            var systemAssemblyDirectories = new HashSet<string>(pathComparer);
            ApiCompatibilityLevel[] compatibilityLevels =
            {
                ApiCompatibilityLevel.NET_Standard,
                ApiCompatibilityLevel.NET_Unity_4_8
            };
            for (int levelIndex = 0; levelIndex < compatibilityLevels.Length; levelIndex++)
            {
                string[] directories = CompilationPipeline.GetSystemAssemblyDirectories(
                    compatibilityLevels[levelIndex]);
                for (int directoryIndex = 0;
                     directoryIndex < directories.Length;
                     directoryIndex++)
                {
                    string directory = Path.GetFullPath(directories[directoryIndex])
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                        Path.DirectorySeparatorChar;
                    systemAssemblyDirectories.Add(directory);
                }
            }

            var trustedDirectories = new string[systemAssemblyDirectories.Count];
            systemAssemblyDirectories.CopyTo(trustedDirectories);
            Array.Sort(trustedDirectories, pathComparer);

            var nodes = new CoCoStateGraphAssemblyGraphNode[assemblies.Length];
            for (int index = 0; index < assemblies.Length; index++)
            {
                nodes[index] = CoCoStateGraphAssemblyGraphNode.Capture(
                    assemblies[index],
                    systemAssemblyPaths,
                    trustedDirectories,
                    pathComparison);
            }

            return nodes;
        }

        private static bool IsBaseClassLibrary(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return false;
            }

            if (string.Equals(assemblyName, "mscorlib", StringComparison.Ordinal) ||
                string.Equals(assemblyName, "netstandard", StringComparison.Ordinal) ||
                string.Equals(assemblyName, "Mono.Security", StringComparison.Ordinal) ||
                string.Equals(assemblyName, "Microsoft.CSharp", StringComparison.Ordinal) ||
                string.Equals(assemblyName, "System", StringComparison.Ordinal) ||
                string.Equals(assemblyName, "System.Core", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static bool IsAllowedFrameworkBoundary(string assemblyName)
        {
            for (int index = 0; index < AllowedFrameworkAssemblies.Length; index++)
            {
                if (string.Equals(
                        assemblyName,
                        AllowedFrameworkAssemblies[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static CoCoDiagnostic Error(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidAuthoringDependency,
                message);

    }

    internal sealed class CoCoStateGraphAssemblyGraphNode
    {
        internal CoCoStateGraphAssemblyGraphNode(
            string name,
            bool hasAssemblyDefinition,
            bool noEngineReferences,
            string[] assemblyReferences,
            string[] precompiledReferences)
        {
            Name = name;
            HasAssemblyDefinition = hasAssemblyDefinition;
            NoEngineReferences = noEngineReferences;
            AssemblyReferences = CloneAndSort(assemblyReferences);
            PrecompiledReferences = CloneAndSort(precompiledReferences);
        }

        internal string Name { get; }
        internal bool HasAssemblyDefinition { get; }
        internal bool NoEngineReferences { get; }
        internal string[] AssemblyReferences { get; }
        internal string[] PrecompiledReferences { get; }

        internal static CoCoStateGraphAssemblyGraphNode Capture(
            UnityAssembly assembly,
            ISet<string> systemAssemblyPaths,
            IReadOnlyList<string> systemAssemblyDirectories,
            StringComparison pathComparison)
        {
            if (assembly == null)
            {
                return null;
            }

            string asmdefPath =
                CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
            bool hasAssemblyDefinition = !string.IsNullOrEmpty(asmdefPath);
            bool noEngineReferences = false;
            if (hasAssemblyDefinition)
            {
                try
                {
                    string absolutePath = Path.GetFullPath(asmdefPath);
                    AssemblyDefinitionData data =
                        JsonUtility.FromJson<AssemblyDefinitionData>(File.ReadAllText(absolutePath));
                    noEngineReferences = data != null && data.noEngineReferences;
                }
                catch (Exception)
                {
                    noEngineReferences = false;
                }
            }

            var references = new string[assembly.assemblyReferences.Length];
            for (int index = 0; index < references.Length; index++)
            {
                references[index] = assembly.assemblyReferences[index].name;
            }

            var precompiled = new List<string>();
            for (int index = 0; index < assembly.compiledAssemblyReferences.Length; index++)
            {
                string referencePath = Path.GetFullPath(
                    assembly.compiledAssemblyReferences[index]);
                bool trustedSystemPath =
                    systemAssemblyPaths != null && systemAssemblyPaths.Contains(referencePath);
                if (!trustedSystemPath && systemAssemblyDirectories != null)
                {
                    for (int directoryIndex = 0;
                         directoryIndex < systemAssemblyDirectories.Count;
                         directoryIndex++)
                    {
                        if (referencePath.StartsWith(
                                systemAssemblyDirectories[directoryIndex],
                                pathComparison))
                        {
                            trustedSystemPath = true;
                            break;
                        }
                    }
                }

                if (!trustedSystemPath)
                {
                    precompiled.Add(Path.GetFileNameWithoutExtension(referencePath));
                }
            }

            return new CoCoStateGraphAssemblyGraphNode(
                assembly.name,
                hasAssemblyDefinition,
                noEngineReferences,
                references,
                precompiled.ToArray());
        }

        private static string[] CloneAndSort(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return Array.Empty<string>();
            }

            var clone = (string[])values.Clone();
            Array.Sort(clone, StringComparer.Ordinal);
            return clone;
        }

        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            public bool noEngineReferences;
        }
    }
}
