#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.ProjectScaffold
{
    internal interface IProjectScaffoldFileSystem
    {
        bool FileExists(string path);
        void CreateDirectory(string path);
        void WriteCreateNew(string path, string content);
        string ReadAllText(string path);
        void DeleteFile(string path);
        void DeleteDirectory(string path, bool recursive);
        bool DirectoryExists(string path);
        bool IsDirectoryEmpty(string path);
        FileAttributes GetAttributes(string path);
    }

    internal sealed class ProjectScaffoldWriteException : IOException
    {
        internal ProjectScaffoldWriteException(
            string path,
            bool residual,
            Exception writeFailure,
            Exception cleanupFailure = null)
            : base(
                residual
                    ? "CreateNew failed and cleanup also failed for " + path +
                      ": " + cleanupFailure?.Message
                    : "CreateNew failed for " + path + ": " +
                      writeFailure?.Message,
                cleanupFailure ?? writeFailure)
        {
            Path = path ?? string.Empty;
            Residual = residual;
        }

        internal string Path { get; }
        internal bool Residual { get; }
    }

    internal sealed class ProjectScaffoldApplyException : Exception
    {
        internal ProjectScaffoldApplyException(
            ProjectScaffoldApplyFailureKind failureKind,
            string message)
            : base(message)
        {
            FailureKind = failureKind;
        }

        internal ProjectScaffoldApplyFailureKind FailureKind { get; }
    }

    internal sealed class ProjectScaffoldFileSystem :
        IProjectScaffoldFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteCreateNew(string path, string content)
        {
            bool ownsTarget = false;
            try
            {
                using (var stream = new FileStream(
                           path,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    ownsTarget = true;
                    using (var writer = new StreamWriter(
                               stream,
                               new UTF8Encoding(false)))
                    {
                        writer.Write(content);
                    }
                }
            }
            catch (Exception writeFailure)
            {
                if (!ownsTarget)
                {
                    throw;
                }

                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new ProjectScaffoldWriteException(
                        path,
                        true,
                        writeFailure,
                        cleanupFailure);
                }

                throw new ProjectScaffoldWriteException(
                    path,
                    false,
                    writeFailure);
            }
        }

        public string ReadAllText(string path) =>
            File.ReadAllText(path, Encoding.UTF8);

        public void DeleteFile(string path) => File.Delete(path);

        public void DeleteDirectory(string path, bool recursive) =>
            Directory.Delete(path, recursive);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool IsDirectoryEmpty(string path) =>
            !Directory.EnumerateFileSystemEntries(path).Any();

        public FileAttributes GetAttributes(string path) =>
            File.GetAttributes(path);
    }

    public sealed class ProjectScaffoldWriter
    {
        private readonly IProjectScaffoldFileSystem _fileSystem;
        private readonly IProjectScaffoldProviderDetector _providerDetector;

        public ProjectScaffoldWriter()
            : this(
                new ProjectScaffoldFileSystem(),
                new ProjectScaffoldProviderDetector())
        {
        }

        internal ProjectScaffoldWriter(
            IProjectScaffoldFileSystem fileSystem,
            IProjectScaffoldProviderDetector providerDetector = null)
        {
            _fileSystem = fileSystem ??
                          throw new ArgumentNullException(nameof(fileSystem));
            _providerDetector = providerDetector ??
                                new ProjectScaffoldProviderDetector();
        }

        public ProjectScaffoldApplyResult Apply(
            ProjectScaffoldPlan plan,
            string workingDirectory)
        {
            if (plan == null ||
                string.IsNullOrWhiteSpace(workingDirectory) ||
                !plan.CanApply)
            {
                return Failure(
                    ProjectScaffoldApplyFailureKind.InvalidPreview,
                    "The scaffold Preview is missing or blocked.");
            }

            string normalizedWorkingDirectory =
                Path.GetFullPath(workingDirectory);
            ProjectScaffoldPlan currentPlan = ProjectScaffoldPlanner.Build(
                plan.Request,
                normalizedWorkingDirectory,
                _providerDetector);
            if (!currentPlan.CanApply ||
                !string.Equals(
                    plan.Fingerprint,
                    currentPlan.Fingerprint,
                    StringComparison.Ordinal))
            {
                return Failure(
                    ProjectScaffoldApplyFailureKind.StalePreview,
                    "The scaffold Preview changed after confirmation. Refresh the full Preview and confirm again.");
            }

            string stagingRelative =
                "Library/CoCoFlow/ProjectScaffold/" +
                Guid.NewGuid().ToString("N");
            string stagingRoot;
            try
            {
                stagingRoot = ResolveSafePath(
                    normalizedWorkingDirectory,
                    stagingRelative);
            }
            catch (InvalidOperationException exception)
            {
                return Failure(
                    ProjectScaffoldApplyFailureKind.UnsafePath,
                    exception.Message);
            }

            var published = new List<string>();
            var createdDirectories = new List<string>();
            ProjectScaffoldApplyResult result;

            try
            {
                _fileSystem.CreateDirectory(stagingRoot);
                ResolveSafePath(
                    normalizedWorkingDirectory,
                    stagingRelative);
                foreach (ProjectScaffoldFile file in plan.Files)
                {
                    string target = ResolveSafePath(
                        normalizedWorkingDirectory,
                        file.RelativePath);
                    if (_fileSystem.FileExists(target))
                    {
                        throw new ProjectScaffoldApplyException(
                            ProjectScaffoldApplyFailureKind.StalePreview,
                            "A target changed after Preview and now exists: " +
                            file.RelativePath);
                    }

                    string staged = ResolveSafePath(
                        normalizedWorkingDirectory,
                        stagingRelative + "/" + file.RelativePath);
                    EnsureDirectory(Path.GetDirectoryName(staged), createdDirectories);
                    staged = ResolveSafePath(
                        normalizedWorkingDirectory,
                        stagingRelative + "/" + file.RelativePath);
                    _fileSystem.WriteCreateNew(staged, file.Content);
                    string reread = _fileSystem.ReadAllText(staged);
                    string validationError = string.Empty;
                    if (!string.Equals(reread, file.Content, StringComparison.Ordinal) ||
                        !Validate(file.RelativePath, reread, out validationError))
                    {
                        throw new ProjectScaffoldApplyException(
                            ProjectScaffoldApplyFailureKind.StagingValidation,
                            "Staged file validation failed for " +
                            file.RelativePath + ": " + validationError);
                    }
                }

                foreach (ProjectScaffoldFile file in plan.Files)
                {
                    string target = ResolveSafePath(
                        normalizedWorkingDirectory,
                        file.RelativePath);
                    EnsureDirectory(
                        Path.GetDirectoryName(target),
                        createdDirectories);
                    target = ResolveSafePath(
                        normalizedWorkingDirectory,
                        file.RelativePath);
                    _fileSystem.WriteCreateNew(target, file.Content);
                    published.Add(target);
                }

                AssetDatabase.Refresh();
                result = new ProjectScaffoldApplyResult(
                    true,
                    published.Select(path =>
                            ProjectScaffoldRequest.Normalize(path.Substring(
                                normalizedWorkingDirectory
                                    .TrimEnd(Path.DirectorySeparatorChar)
                                    .Length + 1)))
                        .ToArray(),
                    ProjectScaffoldApplyFailureKind.None,
                    true,
                    Array.Empty<string>(),
                    string.Empty,
                    string.Empty);
            }
            catch (ProjectScaffoldApplyException exception)
            {
                IReadOnlyList<string> residuals = RollBackPublished(
                    published,
                    createdDirectories,
                    normalizedWorkingDirectory);
                bool rollbackCompleted = residuals.Count == 0;
                result = Failure(
                    rollbackCompleted
                        ? exception.FailureKind
                        : ProjectScaffoldApplyFailureKind.RollbackIncomplete,
                    rollbackCompleted,
                    residuals,
                    exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                IReadOnlyList<string> residuals = RollBackPublished(
                    published,
                    createdDirectories,
                    normalizedWorkingDirectory);
                result = Failure(
                    residuals.Count == 0
                        ? ProjectScaffoldApplyFailureKind.UnsafePath
                        : ProjectScaffoldApplyFailureKind.RollbackIncomplete,
                    residuals.Count == 0,
                    residuals,
                    "Scaffold path validation failed: " +
                    exception.Message);
            }
            catch (Exception exception)
            {
                var residuals = new List<string>();
                if (exception is ProjectScaffoldWriteException writeFailure &&
                    writeFailure.Residual)
                {
                    AddRelativeResidual(
                        residuals,
                        writeFailure.Path,
                        normalizedWorkingDirectory);
                }

                residuals.AddRange(RollBackPublished(
                    published,
                    createdDirectories,
                    normalizedWorkingDirectory));
                bool rollbackCompleted = residuals.Count == 0;
                result = Failure(
                    rollbackCompleted
                        ? ProjectScaffoldApplyFailureKind.PublishFailed
                        : ProjectScaffoldApplyFailureKind.RollbackIncomplete,
                    rollbackCompleted,
                    residuals,
                    rollbackCompleted
                        ? "Publishing failed; all project files created by this Apply were rolled back: " +
                          exception.Message
                        : "Publishing failed and rollback left residual project paths: " +
                          exception.Message);
            }

            string warning = CleanupStaging(
                normalizedWorkingDirectory,
                stagingRelative,
                stagingRoot);
            return WithWarning(result, warning);
        }

        private void EnsureDirectory(
            string path,
            ICollection<string> createdDirectories)
        {
            if (string.IsNullOrEmpty(path) ||
                _fileSystem.DirectoryExists(path))
            {
                return;
            }

            var missing = new Stack<string>();
            string cursor = path;
            while (!string.IsNullOrEmpty(cursor) &&
                   !_fileSystem.DirectoryExists(cursor))
            {
                missing.Push(cursor);
                cursor = Path.GetDirectoryName(cursor);
            }

            while (missing.Count > 0)
            {
                string directory = missing.Pop();
                _fileSystem.CreateDirectory(directory);
                createdDirectories.Add(directory);
            }
        }

        private IReadOnlyList<string> RollBackPublished(
            IReadOnlyList<string> published,
            IReadOnlyList<string> createdDirectories,
            string workingDirectory)
        {
            var residuals = new List<string>();
            for (int index = published.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (_fileSystem.FileExists(published[index]))
                    {
                        _fileSystem.DeleteFile(published[index]);
                    }
                }
                catch (Exception)
                {
                    AddRelativeResidual(
                        residuals,
                        published[index],
                        workingDirectory);
                }
            }

            for (int index = createdDirectories.Count - 1; index >= 0; index--)
            {
                try
                {
                    string directory = createdDirectories[index];
                    if (_fileSystem.DirectoryExists(directory) &&
                        _fileSystem.IsDirectoryEmpty(directory))
                    {
                        _fileSystem.DeleteDirectory(directory, false);
                    }
                }
                catch (Exception)
                {
                    AddRelativeResidual(
                        residuals,
                        createdDirectories[index],
                        workingDirectory);
                }
            }

            return residuals
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private string CleanupStaging(
            string workingDirectory,
            string stagingRelative,
            string stagingRoot)
        {
            if (!_fileSystem.DirectoryExists(stagingRoot))
            {
                return string.Empty;
            }

            try
            {
                ResolveSafePath(workingDirectory, stagingRelative);
                _fileSystem.DeleteDirectory(stagingRoot, true);
                return string.Empty;
            }
            catch (Exception exception)
            {
                string warning =
                    "Project files are unaffected, but temporary Scaffold " +
                    "staging cleanup failed at " + stagingRoot + ": " +
                    exception.Message;
                Debug.LogWarning("[ProjectScaffold] " + warning);
                return warning;
            }
        }

        private string ResolveSafePath(string root, string relativePath)
        {
            string normalizedRelative =
                ProjectScaffoldRequest.Normalize(relativePath);
            if (string.IsNullOrEmpty(normalizedRelative) ||
                Path.IsPathRooted(normalizedRelative) ||
                normalizedRelative.Contains(".."))
            {
                throw new InvalidOperationException(
                    "Scaffold paths must be relative and traversal-free.");
            }

            string fullRoot =
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(
                Path.Combine(
                    fullRoot,
                    normalizedRelative.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A scaffold path escaped its allowed root.");
            }

            EnsureNoReparsePoint(
                fullRoot.TrimEnd(Path.DirectorySeparatorChar),
                fullPath);
            return fullPath;
        }

        private void EnsureNoReparsePoint(string root, string fullPath)
        {
            string relative = fullPath.Substring(
                root.TrimEnd(Path.DirectorySeparatorChar).Length)
                .TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string cursor = root;
            string[] segments = relative.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                cursor = Path.Combine(cursor, segments[index]);
                if (!_fileSystem.FileExists(cursor) &&
                    !_fileSystem.DirectoryExists(cursor))
                {
                    continue;
                }

                if ((_fileSystem.GetAttributes(cursor) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "Scaffold paths may not traverse a symbolic link or reparse point: " +
                        ProjectScaffoldRequest.Normalize(relative));
                }
            }
        }

        private static void AddRelativeResidual(
            ICollection<string> residuals,
            string path,
            string workingDirectory)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string root =
                Path.GetFullPath(workingDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            {
                residuals.Add(ProjectScaffoldRequest.Normalize(fullPath));
                return;
            }

            string relative = ProjectScaffoldRequest.Normalize(
                fullPath.Substring(root.Length));
            if (relative.StartsWith("Assets/", StringComparison.Ordinal) ||
                string.Equals(relative, "Assets", StringComparison.Ordinal))
            {
                residuals.Add(relative);
            }
        }

        private static bool Validate(
            string relativePath,
            string content,
            out string error)
        {
            if (relativePath.EndsWith(
                    ".asmdef",
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ProjectScaffoldAssemblyDefinition definition =
                        JsonUtility.FromJson<ProjectScaffoldAssemblyDefinition>(
                            content);
                    if (definition == null ||
                        string.IsNullOrWhiteSpace(definition.name))
                    {
                        error = "Assembly definition has no name.";
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }

                error = string.Empty;
                return true;
            }

            if (relativePath.EndsWith(
                    ".cs",
                    StringComparison.OrdinalIgnoreCase))
            {
                int braces = 0;
                foreach (char character in content)
                {
                    if (character == '{')
                    {
                        braces++;
                    }
                    else if (character == '}')
                    {
                        braces--;
                    }

                    if (braces < 0)
                    {
                        error = "C# braces are unbalanced.";
                        return false;
                    }
                }

                if (braces != 0 ||
                    content.IndexOf(
                        "namespace CoCoFlowProject",
                        StringComparison.Ordinal) < 0)
                {
                    error = "C# namespace or brace validation failed.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static ProjectScaffoldApplyResult Failure(
            ProjectScaffoldApplyFailureKind failureKind,
            string error) =>
            Failure(
                failureKind,
                true,
                Array.Empty<string>(),
                error);

        private static ProjectScaffoldApplyResult Failure(
            ProjectScaffoldApplyFailureKind failureKind,
            bool rollbackCompleted,
            IReadOnlyList<string> residualPaths,
            string error) =>
            new ProjectScaffoldApplyResult(
                false,
                Array.Empty<string>(),
                failureKind,
                rollbackCompleted,
                residualPaths,
                error,
                string.Empty);

        private static ProjectScaffoldApplyResult WithWarning(
            ProjectScaffoldApplyResult result,
            string warning) =>
            string.IsNullOrEmpty(warning)
                ? result
                : new ProjectScaffoldApplyResult(
                    result.Succeeded,
                    result.CreatedPaths,
                    result.FailureKind,
                    result.RollbackCompleted,
                    result.ResidualPaths,
                    result.Error,
                    warning);

        [Serializable]
        private sealed class ProjectScaffoldAssemblyDefinition
        {
            public string name;
        }
    }
}
#endif
