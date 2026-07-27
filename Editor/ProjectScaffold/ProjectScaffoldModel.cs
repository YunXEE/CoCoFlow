#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
            string integrationGuidance)
        {
            Request = request;
            Files = files;
            ExistingProviderPaths = providerPaths;
            Conflicts = conflicts;
            IntegrationGuidance = integrationGuidance ?? string.Empty;
        }

        public ProjectScaffoldRequest Request { get; }
        public IReadOnlyList<ProjectScaffoldFile> Files { get; }
        public IReadOnlyList<string> ExistingProviderPaths { get; }
        public IReadOnlyList<string> Conflicts { get; }
        public string IntegrationGuidance { get; }
        public bool CanApply => Conflicts.Count == 0 &&
                                ExistingProviderPaths.Count <= 1 &&
                                Files.Count > 0;
    }

    public readonly struct ProjectScaffoldApplyResult
    {
        internal ProjectScaffoldApplyResult(
            bool succeeded,
            IReadOnlyList<string> createdPaths,
            string error)
        {
            Succeeded = succeeded;
            CreatedPaths = createdPaths ?? Array.Empty<string>();
            Error = error ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<string> CreatedPaths { get; }
        public string Error { get; }
    }

    internal static class ProjectScaffoldPlanner
    {
        private static readonly Regex ProviderPattern = new Regex(
            @"\bclass\s+\w+[^{;]*:\s*[^{;]*\bICoCoStateGraphProjectBindingProvider\b",
            RegexOptions.Compiled | RegexOptions.Singleline);

        internal static ProjectScaffoldPlan Build(
            ProjectScaffoldRequest request,
            string workingDirectory)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string root = request.ProjectRoot;
            var conflicts = new List<string>();
            if (!IsSafeAssetRoot(root))
            {
                conflicts.Add(
                    "The project root must be a relative path below Assets and may not contain traversal segments.");
            }

            IReadOnlyList<string> providers = FindProviderPaths(workingDirectory);
            if (providers.Count > 1)
            {
                conflicts.Add(
                    "Multiple ICoCoStateGraphProjectBindingProvider implementations were detected.");
            }

            bool generateProvider = providers.Count == 0;
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

            string guidance = providers.Count == 1
                ? ProjectScaffoldTemplates.ExistingProviderGuidance(providers[0])
                : string.Empty;
            return new ProjectScaffoldPlan(
                request,
                files,
                providers,
                conflicts,
                guidance);
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

        private static IReadOnlyList<string> FindProviderPaths(string workingDirectory)
        {
            string assets = Path.Combine(workingDirectory, "Assets");
            if (!Directory.Exists(assets))
            {
                return Array.Empty<string>();
            }

            var providers = new List<string>();
            foreach (string path in Directory.GetFiles(
                         assets,
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                string source;
                try
                {
                    source = File.ReadAllText(path);
                }
                catch (IOException)
                {
                    continue;
                }

                if (!ProviderPattern.IsMatch(source))
                {
                    continue;
                }

                providers.Add(ProjectScaffoldRequest.Normalize(
                    path.Substring(workingDirectory.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }

            providers.Sort(StringComparer.Ordinal);
            return providers;
        }
    }
}
#endif
