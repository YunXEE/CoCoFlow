#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoCoFlow.Runtime.Core;
using UnityEditor;

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

    internal sealed class ProjectScaffoldProviderDetector :
        IProjectScaffoldProviderDetector
    {
        public IReadOnlyList<ProjectScaffoldProviderIdentity> FindProviders(
            string workingDirectory)
        {
            var providerTypes = TypeCache
                .GetTypesDerivedFrom<ICoCoStateGraphProjectBindingProvider>();
            MonoScript[] scripts = MonoImporter.GetAllRuntimeMonoScripts();
            var providers = new List<ProjectScaffoldProviderIdentity>();
            foreach (Type type in providerTypes)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                for (int index = 0; index < scripts.Length; index++)
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
                        providers.Add(new ProjectScaffoldProviderIdentity(
                            path,
                            type.AssemblyQualifiedName));
                    }

                    break;
                }
            }

            return providers
                .OrderBy(
                    provider => provider.TypeIdentity,
                    StringComparer.Ordinal)
                .ThenBy(provider => provider.Path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    internal static class ProjectScaffoldPlanner
    {

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
