#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "CoCoFlow.Tests.Editor.ProjectScaffold")]

namespace CoCoFlow.Editor.ProjectScaffold
{
    public enum ProjectScaffoldAssemblyMode
    {
        AssemblyCSharp = 0,
        CustomAssemblyDefinition = 1
    }

    public sealed class ProjectScaffoldRequest
    {
        public const string DefaultRoot = "Assets/CoCoFlowProject";

        public ProjectScaffoldRequest(
            string projectRoot,
            ProjectScaffoldAssemblyMode assemblyMode)
        {
            ProjectRoot = Normalize(projectRoot);
            AssemblyMode = assemblyMode;
        }

        public string ProjectRoot { get; }
        public ProjectScaffoldAssemblyMode AssemblyMode { get; }

        internal static string Normalize(string path) =>
            (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
    }

    public sealed class ProjectScaffoldFile
    {
        internal ProjectScaffoldFile(string relativePath, string content)
        {
            RelativePath = ProjectScaffoldRequest.Normalize(relativePath);
            Content = content ?? string.Empty;
        }

        public string RelativePath { get; }
        internal string Content { get; }
    }

    public sealed class ProjectScaffoldPlan
    {
        internal ProjectScaffoldPlan(
            ProjectScaffoldRequest request,
            IReadOnlyList<ProjectScaffoldFile> files,
            IReadOnlyList<string> providerPaths,
            IReadOnlyList<string> conflicts,
            string integrationGuidance,
            string fingerprint)
        {
            Request = request;
            Files = files;
            ExistingProviderPaths = providerPaths;
            Conflicts = conflicts;
            IntegrationGuidance = integrationGuidance ?? string.Empty;
            Fingerprint = fingerprint ?? string.Empty;
        }

        public ProjectScaffoldRequest Request { get; }
        public IReadOnlyList<ProjectScaffoldFile> Files { get; }
        public IReadOnlyList<string> ExistingProviderPaths { get; }
        public IReadOnlyList<string> Conflicts { get; }
        public string IntegrationGuidance { get; }
        internal string Fingerprint { get; }
        public bool CanApply => Conflicts.Count == 0 &&
                                ExistingProviderPaths.Count <= 1 &&
                                Files.Count > 0 &&
                                !string.IsNullOrEmpty(Fingerprint);
    }

    public enum ProjectScaffoldApplyFailureKind
    {
        None = 0,
        InvalidPreview = 1,
        StalePreview = 2,
        UnsafePath = 3,
        StagingValidation = 4,
        PublishFailed = 5,
        RollbackIncomplete = 6
    }

    public readonly struct ProjectScaffoldApplyResult
    {
        internal ProjectScaffoldApplyResult(
            bool succeeded,
            IReadOnlyList<string> createdPaths,
            ProjectScaffoldApplyFailureKind failureKind,
            bool rollbackCompleted,
            IReadOnlyList<string> residualPaths,
            string error,
            string warning)
        {
            Succeeded = succeeded;
            CreatedPaths = createdPaths ?? Array.Empty<string>();
            FailureKind = failureKind;
            RollbackCompleted = rollbackCompleted;
            ResidualPaths = residualPaths ?? Array.Empty<string>();
            Error = error ?? string.Empty;
            Warning = warning ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<string> CreatedPaths { get; }
        public ProjectScaffoldApplyFailureKind FailureKind { get; }
        public bool RollbackCompleted { get; }
        public IReadOnlyList<string> ResidualPaths { get; }
        public string Error { get; }
        public string Warning { get; }
    }

    internal interface IProjectScaffoldProviderDetector
    {
        IReadOnlyList<ProjectScaffoldProviderIdentity> FindProviders(
            string workingDirectory);
    }

    internal readonly struct ProjectScaffoldProviderIdentity
    {
        internal ProjectScaffoldProviderIdentity(
            string path,
            string typeIdentity)
        {
            Path = ProjectScaffoldRequest.Normalize(path);
            TypeIdentity = typeIdentity ?? string.Empty;
        }

        internal string Path { get; }
        internal string TypeIdentity { get; }
    }

    internal readonly struct ProjectScaffoldAssemblyIdentity
    {
        internal ProjectScaffoldAssemblyIdentity(
            string path,
            string assemblyName,
            string absolutePath,
            bool isReference)
        {
            Path = ProjectScaffoldRequest.Normalize(path);
            AssemblyName = assemblyName ?? string.Empty;
            AbsolutePath = absolutePath ?? string.Empty;
            IsReference = isReference;
        }

        internal string Path { get; }
        internal string AssemblyName { get; }
        internal string AbsolutePath { get; }
        internal bool IsReference { get; }

        internal string KindLabel =>
            IsReference ? "assembly reference" : "assembly definition";
    }

    internal sealed class ProjectScaffoldProviderDetector :
        IProjectScaffoldProviderDetector
    {
        public IReadOnlyList<ProjectScaffoldProviderIdentity> FindProviders(
            string workingDirectory)
        {
            var providerTypes = TypeCache
                .GetTypesDerivedFrom<ICoCoStateGraphProjectBindingProvider>();
            MonoScript[] scripts = MonoImporter.GetAllRuntimeMonoScripts();
            UnityEditor.Compilation.Assembly[] assemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var providers = new List<ProjectScaffoldProviderIdentity>();
            foreach (Type type in providerTypes)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                if (!TryFindProjectAssemblyProvenance(
                        type,
                        assemblies,
                        workingDirectory,
                        out string path))
                {
                    continue;
                }

                string exactPath = FindExactProjectScript(type, scripts);
                if (!string.IsNullOrEmpty(exactPath))
                {
                    path = exactPath;
                }

                providers.Add(new ProjectScaffoldProviderIdentity(
                    path,
                    type.AssemblyQualifiedName ??
                    type.FullName + ", " + type.Assembly.GetName().Name));
            }

            return providers
                .GroupBy(
                    provider => provider.TypeIdentity,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(
                    provider => provider.TypeIdentity,
                    StringComparer.Ordinal)
                .ThenBy(provider => provider.Path, StringComparer.Ordinal)
                .ToArray();
        }

        private static string FindExactProjectScript(
            Type type,
            IReadOnlyList<MonoScript> scripts)
        {
            for (int index = 0; index < scripts.Count; index++)
            {
                MonoScript script = scripts[index];
                if (script == null || script.GetClass() != type)
                {
                    continue;
                }

                string path = ProjectScaffoldRequest.Normalize(
                    AssetDatabase.GetAssetPath(script));
                if (path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static bool TryFindProjectAssemblyProvenance(
            Type type,
            IReadOnlyList<UnityEditor.Compilation.Assembly> assemblies,
            string workingDirectory,
            out string path)
        {
            path = string.Empty;
            string assemblyName = type.Assembly.GetName().Name;
            UnityEditor.Compilation.Assembly assembly = assemblies
                .FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.name,
                        assemblyName,
                        StringComparison.Ordinal));
            if (assembly == null)
            {
                return false;
            }

            bool hasProjectSource = assembly.sourceFiles.Any(source =>
                TryNormalizeProjectAssetPath(
                    source,
                    workingDirectory,
                    out _));
            if (!hasProjectSource)
            {
                return false;
            }

            string assemblyDefinitionPath =
                CompilationPipeline
                    .GetAssemblyDefinitionFilePathFromAssemblyName(
                        assemblyName);
            if (TryNormalizeProjectAssetPath(
                    assemblyDefinitionPath,
                    workingDirectory,
                    out string normalizedAssemblyDefinition))
            {
                path = normalizedAssemblyDefinition;
                return true;
            }

            path = "Assets/ (" + assemblyName + " project sources)";
            return true;
        }

        private static bool TryNormalizeProjectAssetPath(
            string candidate,
            string workingDirectory,
            out string assetPath)
        {
            assetPath = string.Empty;
            if (string.IsNullOrWhiteSpace(candidate) ||
                string.IsNullOrWhiteSpace(workingDirectory))
            {
                return false;
            }

            string normalizedCandidate =
                ProjectScaffoldRequest.Normalize(candidate);
            if (normalizedCandidate.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                assetPath = normalizedCandidate;
                return true;
            }

            string projectRoot;
            string absoluteCandidate;
            try
            {
                projectRoot = Path.GetFullPath(workingDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                absoluteCandidate = Path.GetFullPath(
                    Path.IsPathRooted(candidate)
                        ? candidate
                        : Path.Combine(workingDirectory, candidate));
            }
            catch (Exception)
            {
                return false;
            }

            string prefix = projectRoot + Path.DirectorySeparatorChar;
            StringComparison pathComparison =
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            if (!absoluteCandidate.StartsWith(
                    prefix,
                    pathComparison))
            {
                return false;
            }

            string relative = ProjectScaffoldRequest.Normalize(
                absoluteCandidate.Substring(prefix.Length));
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            assetPath = relative;
            return true;
        }
    }

    internal static class ProjectScaffoldPlanner
    {
        private const string GraphAssemblyName = "CoCoFlowProject.Graph";
        private const string RuntimeAssemblyName = "CoCoFlowProject.Runtime";

        [Serializable]
        private sealed class AssemblyDefinitionJson
        {
            [SerializeField] private string name;

            internal string Name => name;
        }

        [Serializable]
        private sealed class AssemblyReferenceJson
        {
            [SerializeField] private string reference;

            internal string Reference => reference;
        }

        internal static ProjectScaffoldPlan Build(
            ProjectScaffoldRequest request,
            string workingDirectory) =>
            Build(
                request,
                workingDirectory,
                new ProjectScaffoldProviderDetector());

        internal static ProjectScaffoldPlan Build(
            ProjectScaffoldRequest request,
            string workingDirectory,
            IProjectScaffoldProviderDetector providerDetector)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (providerDetector == null)
            {
                throw new ArgumentNullException(nameof(providerDetector));
            }

            string root = request.ProjectRoot;
            var conflicts = new List<string>();
            if (!IsSafeAssetRoot(root))
            {
                conflicts.Add(
                    "The project root must be a relative path below Assets and may not contain traversal segments.");
            }

            IReadOnlyList<ProjectScaffoldAssemblyIdentity> assemblyIdentities =
                InspectAssemblyDefinitions(workingDirectory, conflicts);
            if (IsSafeAssetRoot(root))
            {
                AddAssemblyConflicts(
                    request,
                    workingDirectory,
                    assemblyIdentities,
                    conflicts);
            }

            IReadOnlyList<ProjectScaffoldProviderIdentity> providerIdentities =
                providerDetector.FindProviders(workingDirectory) ??
                Array.Empty<ProjectScaffoldProviderIdentity>();
            string[] providers = providerIdentities
                .Select(provider => provider.Path)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (providerIdentities.Count > 1)
            {
                conflicts.Add(
                    "Multiple ICoCoStateGraphProjectBindingProvider implementations were detected.");
            }

            bool generateProvider = providers.Length == 0;
            var files = ProjectScaffoldTemplates.Create(request, generateProvider).ToList();
            foreach (ProjectScaffoldFile file in files)
            {
                string absolutePath = Path.Combine(
                    workingDirectory,
                    file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    conflicts.Add("Target already exists: " + file.RelativePath);
                }
            }

            string guidance = providers.Length == 1
                ? ProjectScaffoldTemplates.ExistingProviderGuidance(providers[0])
                : ProjectScaffoldTemplates.GeneratedProviderGuidance();
            string fingerprint = ComputeFingerprint(
                request,
                files,
                providerIdentities,
                assemblyIdentities,
                conflicts);
            return new ProjectScaffoldPlan(
                request,
                files,
                providers,
                conflicts,
                guidance,
                fingerprint);
        }

        internal static bool IsSafeAssetRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                Path.IsPathRooted(path) ||
                path.Contains("..") ||
                !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            return path.Split('/').All(segment =>
                !string.IsNullOrWhiteSpace(segment) &&
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        }

        private static string ComputeFingerprint(
            ProjectScaffoldRequest request,
            IReadOnlyList<ProjectScaffoldFile> files,
            IReadOnlyList<ProjectScaffoldProviderIdentity> providers,
            IReadOnlyList<ProjectScaffoldAssemblyIdentity> assemblies,
            IReadOnlyList<string> conflicts)
        {
            var source = new StringBuilder();
            Append(source, request.ProjectRoot);
            Append(source, ((int)request.AssemblyMode).ToString());
            for (int index = 0; index < providers.Count; index++)
            {
                Append(source, providers[index].TypeIdentity);
                Append(source, providers[index].Path);
            }

            for (int index = 0; index < assemblies.Count; index++)
            {
                Append(source, assemblies[index].IsReference ? "asmref" : "asmdef");
                Append(source, assemblies[index].AssemblyName);
                Append(source, assemblies[index].Path);
            }

            for (int index = 0; index < conflicts.Count; index++)
            {
                Append(source, conflicts[index]);
            }

            for (int index = 0; index < files.Count; index++)
            {
                Append(source, files[index].RelativePath);
                Append(source, files[index].Content);
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(source.ToString()));
                var fingerprint = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    fingerprint.Append(hash[index].ToString("x2"));
                }

                return fingerprint.ToString();
            }
        }

        private static IReadOnlyList<ProjectScaffoldAssemblyIdentity>
            InspectAssemblyDefinitions(
                string workingDirectory,
                ICollection<string> conflicts)
        {
            var identities = new List<ProjectScaffoldAssemblyIdentity>();
            string projectRoot;
            string assetsRoot;
            try
            {
                projectRoot = Path.GetFullPath(workingDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                assetsRoot = Path.Combine(projectRoot, "Assets");
            }
            catch (Exception exception)
            {
                conflicts.Add(
                    "Project assembly inspection failed: " +
                    exception.Message);
                return identities;
            }

            if (!Directory.Exists(assetsRoot))
            {
                conflicts.Add(
                    "Project assembly inspection failed because Assets does not exist.");
                return identities;
            }

            var pending = new Stack<string>();
            pending.Push(assetsRoot);
            try
            {
                while (pending.Count > 0)
                {
                    string directory = pending.Pop();
                    FileAttributes directoryAttributes =
                        File.GetAttributes(directory);
                    if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    foreach (string file in Directory.EnumerateFiles(
                                 directory,
                                 "*.asmdef",
                                 SearchOption.TopDirectoryOnly))
                    {
                        string relativePath = ProjectScaffoldRequest.Normalize(
                            file.Substring(projectRoot.Length + 1));
                        string content = File.ReadAllText(file, Encoding.UTF8);
                        AssemblyDefinitionJson definition =
                            JsonUtility.FromJson<AssemblyDefinitionJson>(content);
                        if (definition == null ||
                            string.IsNullOrWhiteSpace(definition.Name))
                        {
                            conflicts.Add(
                                "Assembly definition inspection failed for " +
                                relativePath + ": the name is missing.");
                            continue;
                        }

                        identities.Add(new ProjectScaffoldAssemblyIdentity(
                            relativePath,
                            definition.Name,
                            Path.GetFullPath(file),
                            false));
                    }

                    foreach (string file in Directory.EnumerateFiles(
                                 directory,
                                 "*.asmref",
                                 SearchOption.TopDirectoryOnly))
                    {
                        string relativePath = ProjectScaffoldRequest.Normalize(
                            file.Substring(projectRoot.Length + 1));
                        string content = File.ReadAllText(file, Encoding.UTF8);
                        AssemblyReferenceJson reference =
                            JsonUtility.FromJson<AssemblyReferenceJson>(content);
                        if (reference == null ||
                            string.IsNullOrWhiteSpace(reference.Reference))
                        {
                            conflicts.Add(
                                "Assembly reference inspection failed for " +
                                relativePath + ": the target reference is missing.");
                            continue;
                        }

                        identities.Add(new ProjectScaffoldAssemblyIdentity(
                            relativePath,
                            reference.Reference,
                            Path.GetFullPath(file),
                            true));
                    }

                    foreach (string child in Directory.EnumerateDirectories(
                                 directory,
                                 "*",
                                 SearchOption.TopDirectoryOnly))
                    {
                        FileAttributes childAttributes =
                            File.GetAttributes(child);
                        if ((childAttributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                conflicts.Add(
                    "Project assembly inspection failed: " +
                    exception.Message);
            }

            return identities
                .OrderBy(identity => identity.Path, StringComparer.Ordinal)
                .ThenBy(
                    identity => identity.AssemblyName,
                    StringComparer.Ordinal)
                .ToArray();
        }

        private static void AddAssemblyConflicts(
            ProjectScaffoldRequest request,
            string workingDirectory,
            IReadOnlyList<ProjectScaffoldAssemblyIdentity> assemblies,
            ICollection<string> conflicts)
        {
            string projectRoot = Path.GetFullPath(workingDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string assetsRoot = Path.Combine(projectRoot, "Assets");
            string scaffoldRoot = Path.GetFullPath(Path.Combine(
                projectRoot,
                request.ProjectRoot.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            string graphRoot = Path.Combine(scaffoldRoot, "Graph");
            string runtimeRoot = Path.Combine(scaffoldRoot, "Runtime");

            foreach (ProjectScaffoldAssemblyIdentity assembly in assemblies)
            {
                if (string.Equals(
                        assembly.AssemblyName,
                        GraphAssemblyName,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        assembly.AssemblyName,
                        RuntimeAssemblyName,
                        StringComparison.Ordinal))
                {
                    conflicts.Add(
                        "A project " + assembly.KindLabel +
                        " already uses the reserved Scaffold identity '" +
                        assembly.AssemblyName + "' at " + assembly.Path +
                        ". Only one CoCoFlow Project Scaffold may exist per Unity project.");
                }
            }

            foreach (ProjectScaffoldAssemblyIdentity assembly in assemblies)
            {
                if (PathsEqual(
                        Path.GetDirectoryName(assembly.AbsolutePath),
                        graphRoot))
                {
                    conflicts.Add(
                        "The generated Graph directory already contains an " +
                        assembly.KindLabel + " at " +
                        assembly.Path + ".");
                }
            }

            if (request.AssemblyMode ==
                ProjectScaffoldAssemblyMode.AssemblyCSharp)
            {
                var ancestorDirectories = new HashSet<string>(
                    Path.DirectorySeparatorChar == '\\'
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal);
                string current = runtimeRoot;
                while (!string.IsNullOrEmpty(current))
                {
                    ancestorDirectories.Add(current);
                    if (PathsEqual(current, assetsRoot))
                    {
                        break;
                    }

                    current = Path.GetDirectoryName(current);
                }

                foreach (ProjectScaffoldAssemblyIdentity assembly in assemblies)
                {
                    string assemblyDirectory =
                        Path.GetDirectoryName(assembly.AbsolutePath);
                    if (ancestorDirectories.Contains(assemblyDirectory))
                    {
                        conflicts.Add(
                            "Assembly-CSharp mode cannot generate Runtime files below the " +
                            assembly.KindLabel + " at " +
                            assembly.Path + ".");
                    }
                }

                return;
            }

            foreach (ProjectScaffoldAssemblyIdentity assembly in assemblies)
            {
                string assemblyDirectory =
                    Path.GetDirectoryName(assembly.AbsolutePath);
                if (PathsEqual(assemblyDirectory, scaffoldRoot) ||
                    PathsEqual(assemblyDirectory, runtimeRoot))
                {
                    conflicts.Add(
                        "Custom assembly mode cannot own the generated Runtime files because the project root or Runtime directory already contains an " +
                        assembly.KindLabel + " at " +
                        assembly.Path + ".");
                }
            }
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(
                Path.GetFullPath(left ?? string.Empty)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right ?? string.Empty)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);

        private static void Append(StringBuilder target, string value)
        {
            string normalized = value ?? string.Empty;
            target.Append(normalized.Length);
            target.Append(':');
            target.Append(normalized);
            target.Append('|');
        }
    }
}
#endif
