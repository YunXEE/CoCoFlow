using System;
using System.IO;
using System.Linq;
using CoCoFlow.Editor.ProjectScaffold;
using NUnit.Framework;
using UnityEditor;

namespace CoCoFlow.Tests.Editor.ProjectScaffold
{
    public sealed class ProjectScaffoldTests
    {
        private const string ValidationRoot =
            "Assets/CoCoFlowProjectPre14Validation";
        private const string ValidationMarker =
            ValidationRoot + "/.pre14-validation";

        private string _project;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(
                Path.GetTempPath(),
                "CoCoFlow-ProjectScaffold-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_project, "Assets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_project))
            {
                Directory.Delete(_project, true);
            }
        }

        [Test]
        public void PreviewIncludesProviderOnlyWhenProjectHasNone()
        {
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);

            Assert.IsTrue(plan.CanApply);
            Assert.IsTrue(plan.Files.Any(file =>
                file.RelativePath.EndsWith(
                    "ProjectStateGraphBindings.cs",
                    StringComparison.Ordinal)));

            WriteProvider("Assets/ExistingProvider.cs");
            plan = Build(ProjectScaffoldAssemblyMode.AssemblyCSharp);

            Assert.IsTrue(plan.CanApply);
            Assert.IsFalse(plan.Files.Any(file =>
                file.RelativePath.EndsWith(
                    "ProjectStateGraphBindings.cs",
                    StringComparison.Ordinal)));
            StringAssert.Contains(
                "Do not install a second provider",
                plan.IntegrationGuidance);
            StringAssert.Contains(
                "TryBindIntentSource",
                plan.IntegrationGuidance);
            StringAssert.Contains(
                "TryRegisterOperation",
                plan.IntegrationGuidance);
        }

        [Test]
        public void MultipleProvidersOrExistingTargetsBlockApply()
        {
            WriteProvider("Assets/ProviderOne.cs");
            WriteProvider("Assets/ProviderTwo.cs");
            Assert.IsFalse(
                Build(ProjectScaffoldAssemblyMode.AssemblyCSharp).CanApply);

            Directory.Delete(Path.Combine(_project, "Assets"), true);
            Directory.CreateDirectory(
                Path.Combine(
                    _project,
                    "Assets",
                    "CoCoFlowProject",
                    "Runtime"));
            File.WriteAllText(
                Path.Combine(
                    _project,
                    "Assets",
                    "CoCoFlowProject",
                    "Runtime",
                    "ProjectIntent.cs"),
                "// existing");
            Assert.IsFalse(
                Build(ProjectScaffoldAssemblyMode.AssemblyCSharp).CanApply);
        }

        [Test]
        public void ApplyCreatesEveryPreviewedFileAndSecondPreviewIsBlocked()
        {
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.CustomAssemblyDefinition);
            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter().Apply(plan, _project);

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.AreEqual(plan.Files.Count, result.CreatedPaths.Count);
            foreach (ProjectScaffoldFile file in plan.Files)
            {
                Assert.IsTrue(File.Exists(
                    Path.Combine(
                        _project,
                        file.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar))));
            }

            Assert.IsFalse(
                Build(ProjectScaffoldAssemblyMode.CustomAssemblyDefinition)
                    .CanApply);
        }

        [Test]
        public void InjectedPublishFailureRollsBackOnlyFilesFromThatApply()
        {
            string sentinel = Path.Combine(_project, "Assets", "Keep.txt");
            File.WriteAllText(sentinel, "keep");
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            var fileSystem = new FaultingFileSystem(
                new ProjectScaffoldFileSystem(),
                plan.Files.Count + 2);

            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter(fileSystem).Apply(plan, _project);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("keep", File.ReadAllText(sentinel));
            foreach (ProjectScaffoldFile file in plan.Files)
            {
                Assert.IsFalse(File.Exists(
                    Path.Combine(
                        _project,
                        file.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar))));
            }
        }

        private ProjectScaffoldPlan Build(
            ProjectScaffoldAssemblyMode mode) =>
            ProjectScaffoldPlanner.Build(
                new ProjectScaffoldRequest(
                    ProjectScaffoldRequest.DefaultRoot,
                    mode),
                _project);

        private void WriteProvider(string relativePath)
        {
            string path = Path.Combine(
                _project,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                "public sealed class ExistingProvider : " +
                "ICoCoStateGraphProjectBindingProvider { }");
        }

        private sealed class FaultingFileSystem :
            IProjectScaffoldFileSystem
        {
            private readonly IProjectScaffoldFileSystem _inner;
            private readonly int _failAtWrite;
            private int _writeCount;

            public FaultingFileSystem(
                IProjectScaffoldFileSystem inner,
                int failAtWrite)
            {
                _inner = inner;
                _failAtWrite = failAtWrite;
            }

            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) =>
                _inner.CreateDirectory(path);

            public void WriteCreateNew(string path, string content)
            {
                _writeCount++;
                if (_writeCount == _failAtWrite)
                {
                    throw new IOException("Injected write failure.");
                }

                _inner.WriteCreateNew(path, content);
            }

            public string ReadAllText(string path) =>
                _inner.ReadAllText(path);

            public void DeleteFile(string path) => _inner.DeleteFile(path);

            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);

            public bool DirectoryExists(string path) =>
                _inner.DirectoryExists(path);

            public bool IsDirectoryEmpty(string path) =>
                _inner.IsDirectoryEmpty(path);
        }

        public static void GenerateAssemblyCSharpValidation()
        {
            GenerateValidation(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
        }

        public static void GenerateCustomAssemblyValidation()
        {
            GenerateValidation(
                ProjectScaffoldAssemblyMode.CustomAssemblyDefinition);
        }

        public static void CleanValidation()
        {
            if (!File.Exists(ValidationMarker))
            {
                throw new InvalidOperationException(
                    "Refusing to clean a Scaffold validation root without its marker.");
            }

            FileUtil.DeleteFileOrDirectory(ValidationRoot);
            FileUtil.DeleteFileOrDirectory(ValidationRoot + ".meta");
            AssetDatabase.Refresh();
        }

        private static void GenerateValidation(
            ProjectScaffoldAssemblyMode mode)
        {
            if (Directory.Exists(ValidationRoot))
            {
                throw new InvalidOperationException(
                    "The Scaffold validation root already exists.");
            }

            string workingDirectory = Directory.GetCurrentDirectory();
            var request = new ProjectScaffoldRequest(ValidationRoot, mode);
            var files = ProjectScaffoldTemplates.Create(
                    request,
                    true)
                .ToArray();
            var plan = new ProjectScaffoldPlan(
                request,
                files,
                Array.Empty<string>(),
                Array.Empty<string>(),
                string.Empty);
            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter().Apply(plan, workingDirectory);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error);
            }

            File.WriteAllText(ValidationMarker, mode.ToString());
            AssetDatabase.Refresh();
        }
    }
}
