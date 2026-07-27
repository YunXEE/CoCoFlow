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
    }

    internal sealed class ProjectScaffoldFileSystem :
        IProjectScaffoldFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteCreateNew(string path, string content)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(false)))
            {
                writer.Write(content);
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
    }

    public sealed class ProjectScaffoldWriter
    {
        private readonly IProjectScaffoldFileSystem _fileSystem;

        public ProjectScaffoldWriter()
            : this(new ProjectScaffoldFileSystem())
        {
        }

        internal ProjectScaffoldWriter(IProjectScaffoldFileSystem fileSystem)
        {
            _fileSystem = fileSystem ??
                          throw new ArgumentNullException(nameof(fileSystem));
        }

        public ProjectScaffoldApplyResult Apply(
            ProjectScaffoldPlan plan,
            string workingDirectory)
        {
            if (plan == null ||
                string.IsNullOrWhiteSpace(workingDirectory) ||
                !plan.CanApply)
            {
                return Failure("The scaffold Preview is missing or blocked.");
            }

            string normalizedWorkingDirectory =
                Path.GetFullPath(workingDirectory);
            string stagingRoot = Path.Combine(
                normalizedWorkingDirectory,
                "Library",
                "CoCoFlow",
                "ProjectScaffold",
                Guid.NewGuid().ToString("N"));
            var published = new List<string>();
            var createdDirectories = new List<string>();

            try
            {
                _fileSystem.CreateDirectory(stagingRoot);
                foreach (ProjectScaffoldFile file in plan.Files)
                {
                    string target = ResolveSafePath(
                        normalizedWorkingDirectory,
                        file.RelativePath);
                    if (_fileSystem.FileExists(target))
                    {
                        return Failure(
                            "A target changed after Preview and now exists: " +
                            file.RelativePath);
                    }

                    string staged = ResolveSafePath(
                        stagingRoot,
                        file.RelativePath);
                    EnsureDirectory(Path.GetDirectoryName(staged), createdDirectories);
                    _fileSystem.WriteCreateNew(staged, file.Content);
                    string reread = _fileSystem.ReadAllText(staged);
                    string validationError = string.Empty;
                    if (!string.Equals(reread, file.Content, StringComparison.Ordinal) ||
                        !Validate(file.RelativePath, reread, out validationError))
                    {
                        return Failure(
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
                    _fileSystem.WriteCreateNew(target, file.Content);
                    published.Add(target);
                }

                AssetDatabase.Refresh();
                return new ProjectScaffoldApplyResult(
                    true,
                    published.Select(path =>
                            ProjectScaffoldRequest.Normalize(path.Substring(
                                normalizedWorkingDirectory
                                    .TrimEnd(Path.DirectorySeparatorChar)
                                    .Length + 1)))
                        .ToArray(),
                    string.Empty);
            }
            catch (Exception exception)
            {
                RollBackPublished(published, createdDirectories);
                return Failure(
                    "Publishing failed and this Apply was rolled back: " +
                    exception.Message);
            }
            finally
            {
                if (_fileSystem.DirectoryExists(stagingRoot))
                {
                    try
                    {
                        _fileSystem.DeleteDirectory(stagingRoot, true);
                    }
                    catch (IOException)
                    {
                        // Staging cleanup is best effort; project files are unaffected.
                    }
                }
            }
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

        private void RollBackPublished(
            IReadOnlyList<string> published,
            IReadOnlyList<string> createdDirectories)
        {
            for (int index = published.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (_fileSystem.FileExists(published[index]))
                    {
                        _fileSystem.DeleteFile(published[index]);
                    }
                }
                catch (IOException)
                {
                    // Continue attempting to remove the rest of this Apply.
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
                catch (IOException)
                {
                    // Continue attempting to remove the rest of this Apply.
                }
            }
        }

        private static string ResolveSafePath(string root, string relativePath)
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

            return fullPath;
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

        private static ProjectScaffoldApplyResult Failure(string error) =>
            new ProjectScaffoldApplyResult(
                false,
                Array.Empty<string>(),
                error);

        [Serializable]
        private sealed class ProjectScaffoldAssemblyDefinition
        {
            public string name;
        }
    }
}
#endif
