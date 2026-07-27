using System;
using System.Collections.Generic;
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
        private MutableProviderDetector _providerDetector;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(
                Path.GetTempPath(),
                "CoCoFlow-ProjectScaffold-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_project, "Assets"));
            _providerDetector = new MutableProviderDetector();
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
            StringAssert.Contains(
                "ICoCoStateGraphProjectBindingProvider",
                BindingContent(plan));
            StringAssert.Contains(
                "TryRegisterRuntimeDeclarations",
                BindingContent(plan));
            StringAssert.Contains(
                "TryBindRuntime",
                BindingContent(plan));
            StringAssert.Contains(
                "InputAuthorityRevision",
                plan.Files.Single(file =>
                    file.RelativePath.EndsWith(
                        "ProjectPlayerIntentSource.cs",
                        StringComparison.Ordinal)).Content);
            StringAssert.Contains(
                "ICoCoOperator",
                plan.Files.Single(file =>
                    file.RelativePath.EndsWith(
                        "ProjectOperations.cs",
                        StringComparison.Ordinal)).Content);
            StringAssert.Contains(
                "CoCoStateLogic",
                plan.Files.Single(file =>
                    file.RelativePath.EndsWith(
                        "ProjectStateLogic.cs",
                        StringComparison.Ordinal)).Content);
            StringAssert.Contains(
                "ProjectStateMemoryBinding",
                plan.Files.Single(file =>
                    file.RelativePath.EndsWith(
                        "ProjectStateLogic.cs",
                        StringComparison.Ordinal)).Content);
            StringAssert.Contains(
                "TryBindGraphStateSlot",
                BindingContent(plan));
            StringAssert.Contains(
                "GraphStateBlockId",
                BindingContent(plan));
            StringAssert.Contains(
                "GraphStateSlotId",
                BindingContent(plan));
            StringAssert.Contains(
                "Intent Source slot 0",
                plan.IntegrationGuidance);
            StringAssert.Contains(
                "ProjectOperations",
                plan.IntegrationGuidance);

            _providerDetector.Paths =
                new[] { "Assets/ExistingProvider.cs" };
            plan = Build(ProjectScaffoldAssemblyMode.AssemblyCSharp);

            Assert.IsTrue(plan.CanApply);
            Assert.IsTrue(plan.Files.Any(file =>
                file.RelativePath.EndsWith(
                    "ProjectStateGraphBindings.cs",
                    StringComparison.Ordinal)));
            StringAssert.DoesNotContain(
                "public sealed class ProjectStateGraphBindingProvider",
                BindingContent(plan));
            StringAssert.Contains(
                "Do not install a second provider",
                plan.IntegrationGuidance);
            StringAssert.Contains(
                "TryBindRuntime",
                plan.IntegrationGuidance);
            StringAssert.Contains(
                "TryRegisterRuntimeDeclarations",
                plan.IntegrationGuidance);
        }

        [TestCase(ProjectScaffoldAssemblyMode.AssemblyCSharp, false)]
        [TestCase(
            ProjectScaffoldAssemblyMode.CustomAssemblyDefinition,
            true)]
        public void GraphContractsAreAlwaysIsolatedFromUnityAndInput(
            ProjectScaffoldAssemblyMode mode,
            bool expectsRuntimeAssemblyDefinition)
        {
            ProjectScaffoldPlan plan = Build(mode);
            ProjectScaffoldFile graphAssembly = plan.Files.Single(file =>
                file.RelativePath.EndsWith(
                    "Graph/CoCoFlowProject.Graph.asmdef",
                    StringComparison.Ordinal));

            StringAssert.Contains(
                "\"noEngineReferences\": true",
                graphAssembly.Content);
            StringAssert.DoesNotContain(
                "CoCoFlow.Runtime.Modules",
                graphAssembly.Content);
            StringAssert.DoesNotContain(
                "Unity.",
                graphAssembly.Content);

            string[] pureNames =
            {
                "ProjectContractIds.cs",
                "ProjectIntent.cs",
                "ProjectStateLogic.cs",
                "ProjectOperationContracts.cs"
            };
            foreach (string pureName in pureNames)
            {
                ProjectScaffoldFile file = plan.Files.Single(candidate =>
                    candidate.RelativePath.EndsWith(
                        "Graph/" + pureName,
                        StringComparison.Ordinal));
                StringAssert.DoesNotContain("UnityEngine", file.Content);
                StringAssert.DoesNotContain("InputRuntime", file.Content);
                StringAssert.DoesNotContain(
                    "InputCommandBatch",
                    file.Content);
                StringAssert.DoesNotContain(
                    "UnityEngine.InputSystem",
                    file.Content);
            }

            ProjectScaffoldFile source = plan.Files.Single(file =>
                file.RelativePath.EndsWith(
                    "Runtime/ProjectPlayerIntentSource.cs",
                    StringComparison.Ordinal));
            StringAssert.Contains("InputCommandBatch", source.Content);
            StringAssert.Contains("ProjectPlayerCommandBatch", source.Content);
            StringAssert.Contains("ProjectMoveValue", source.Content);

            bool hasRuntimeAssemblyDefinition = plan.Files.Any(file =>
                file.RelativePath.EndsWith(
                    "CoCoFlowProject.Runtime.asmdef",
                    StringComparison.Ordinal));
            Assert.AreEqual(
                expectsRuntimeAssemblyDefinition,
                hasRuntimeAssemblyDefinition);
        }

        [Test]
        public void MultipleProvidersOrExistingTargetsBlockApply()
        {
            _providerDetector.Paths = new[]
            {
                "Assets/ProviderOne.cs",
                "Assets/ProviderTwo.cs"
            };
            Assert.IsFalse(
                Build(ProjectScaffoldAssemblyMode.AssemblyCSharp).CanApply);

            _providerDetector.Paths = Array.Empty<string>();
            Directory.Delete(Path.Combine(_project, "Assets"), true);
            Directory.CreateDirectory(
                Path.Combine(
                    _project,
                    "Assets",
                    "CoCoFlowProject",
                    "Graph"));
            File.WriteAllText(
                Path.Combine(
                    _project,
                    "Assets",
                    "CoCoFlowProject",
                    "Graph",
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
                CreateWriter().Apply(plan, _project);

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
                new ProjectScaffoldWriter(
                    fileSystem,
                    _providerDetector).Apply(plan, _project);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.RollbackCompleted);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.PublishFailed,
                result.FailureKind);
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

        [Test]
        public void ProviderChangeAfterPreviewRequiresFreshConfirmation()
        {
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            _providerDetector.Paths =
                new[] { "Assets/NewProvider.cs" };

            ProjectScaffoldApplyResult result =
                CreateWriter().Apply(plan, _project);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.StalePreview,
                result.FailureKind);
            Assert.IsFalse(Directory.Exists(
                Path.Combine(
                    _project,
                    ProjectScaffoldRequest.DefaultRoot,
                    "Runtime")));

            ProjectScaffoldPlan providerPlan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            _providerDetector.IdentityToken = "replacement-type";
            result = CreateWriter().Apply(providerPlan, _project);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.StalePreview,
                result.FailureKind);
        }

        [Test]
        public void ProviderLikeTextDoesNotChangeCompiledProviderTruth()
        {
            File.WriteAllText(
                Path.Combine(_project, "Assets", "CommentOnly.cs"),
                "// class Old : ICoCoStateGraphProjectBindingProvider { }");
            File.WriteAllText(
                Path.Combine(_project, "Assets", "LiteralOnly.cs"),
                "const string Value = \"class Old : " +
                "ICoCoStateGraphProjectBindingProvider\";");

            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);

            Assert.IsTrue(plan.CanApply);
            Assert.IsEmpty(plan.ExistingProviderPaths);
            StringAssert.Contains(
                "public sealed class ProjectStateGraphBindingProvider",
                BindingContent(plan));
        }

        [Test]
        public void PartialCreateNewFailureIsCleanedBeforeRollback()
        {
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            var fileSystem = new PartialWriteFileSystem(
                new ProjectScaffoldFileSystem(),
                plan.Files.Count + 1,
                false);

            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter(
                    fileSystem,
                    _providerDetector).Apply(plan, _project);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.RollbackCompleted);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.PublishFailed,
                result.FailureKind);
            Assert.IsEmpty(result.ResidualPaths);
            Assert.IsFalse(File.Exists(Path.Combine(
                _project,
                plan.Files[0].RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar))));
        }

        [Test]
        public void CleanupFailureReportsResidualProjectPath()
        {
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            var fileSystem = new PartialWriteFileSystem(
                new ProjectScaffoldFileSystem(),
                plan.Files.Count + 1,
                true);

            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter(
                    fileSystem,
                    _providerDetector).Apply(plan, _project);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.RollbackCompleted);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.RollbackIncomplete,
                result.FailureKind);
            CollectionAssert.Contains(
                result.ResidualPaths,
                plan.Files[0].RelativePath);
        }

        [Test]
        public void StagingCleanupFailureIsReportedAsIndependentWarning()
        {
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            var fileSystem = new StagingCleanupFailureFileSystem(
                new ProjectScaffoldFileSystem());

            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter(
                    fileSystem,
                    _providerDetector).Apply(plan, _project);

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.None,
                result.FailureKind);
            Assert.IsTrue(result.RollbackCompleted);
            StringAssert.Contains(
                "temporary Scaffold staging cleanup failed",
                result.Warning);
            Assert.IsTrue(File.Exists(Path.Combine(
                _project,
                plan.Files[0].RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar))));
        }

        [Test]
        public void ReparsePointBelowProjectRootBlocksApply()
        {
            string linkedRoot = Path.Combine(
                _project,
                ProjectScaffoldRequest.DefaultRoot.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(linkedRoot);
            ProjectScaffoldPlan plan = Build(
                ProjectScaffoldAssemblyMode.AssemblyCSharp);
            var fileSystem = new ReparsePointFileSystem(
                new ProjectScaffoldFileSystem(),
                linkedRoot);

            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter(
                    fileSystem,
                    _providerDetector).Apply(plan, _project);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                ProjectScaffoldApplyFailureKind.UnsafePath,
                result.FailureKind);
            Assert.IsFalse(Directory.Exists(
                Path.Combine(linkedRoot, "Runtime")));
        }

        private ProjectScaffoldPlan Build(
            ProjectScaffoldAssemblyMode mode) =>
            ProjectScaffoldPlanner.Build(
                new ProjectScaffoldRequest(
                    ProjectScaffoldRequest.DefaultRoot,
                    mode),
                _project,
                _providerDetector);

        private ProjectScaffoldWriter CreateWriter() =>
            new ProjectScaffoldWriter(
                new ProjectScaffoldFileSystem(),
                _providerDetector);

        private static string BindingContent(ProjectScaffoldPlan plan) =>
            plan.Files.Single(file =>
                file.RelativePath.EndsWith(
                    "ProjectStateGraphBindings.cs",
                    StringComparison.Ordinal)).Content;

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

            public FileAttributes GetAttributes(string path) =>
                _inner.GetAttributes(path);
        }

        private sealed class PartialWriteFileSystem :
            IProjectScaffoldFileSystem
        {
            private readonly IProjectScaffoldFileSystem _inner;
            private readonly int _failAtWrite;
            private readonly bool _leaveResidual;
            private int _writeCount;

            public PartialWriteFileSystem(
                IProjectScaffoldFileSystem inner,
                int failAtWrite,
                bool leaveResidual)
            {
                _inner = inner;
                _failAtWrite = failAtWrite;
                _leaveResidual = leaveResidual;
            }

            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) =>
                _inner.CreateDirectory(path);

            public void WriteCreateNew(string path, string content)
            {
                _writeCount++;
                if (_writeCount != _failAtWrite)
                {
                    _inner.WriteCreateNew(path, content);
                    return;
                }

                _inner.WriteCreateNew(path, content);
                var failure = new IOException(
                    "Injected failure after CreateNew.");
                if (!_leaveResidual)
                {
                    _inner.DeleteFile(path);
                }

                throw new ProjectScaffoldWriteException(
                    path,
                    _leaveResidual,
                    failure,
                    _leaveResidual
                        ? new IOException("Injected cleanup failure.")
                        : null);
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
            public FileAttributes GetAttributes(string path) =>
                _inner.GetAttributes(path);
        }

        private sealed class ReparsePointFileSystem :
            IProjectScaffoldFileSystem
        {
            private readonly IProjectScaffoldFileSystem _inner;
            private readonly string _reparsePoint;

            public ReparsePointFileSystem(
                IProjectScaffoldFileSystem inner,
                string reparsePoint)
            {
                _inner = inner;
                _reparsePoint = Path.GetFullPath(reparsePoint);
            }

            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) =>
                _inner.CreateDirectory(path);
            public void WriteCreateNew(string path, string content) =>
                _inner.WriteCreateNew(path, content);
            public string ReadAllText(string path) =>
                _inner.ReadAllText(path);
            public void DeleteFile(string path) => _inner.DeleteFile(path);
            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);
            public bool DirectoryExists(string path) =>
                _inner.DirectoryExists(path);
            public bool IsDirectoryEmpty(string path) =>
                _inner.IsDirectoryEmpty(path);

            public FileAttributes GetAttributes(string path)
            {
                FileAttributes attributes = _inner.GetAttributes(path);
                return string.Equals(
                    Path.GetFullPath(path),
                    _reparsePoint,
                    StringComparison.Ordinal)
                    ? attributes | FileAttributes.ReparsePoint
                    : attributes;
            }
        }

        private sealed class StagingCleanupFailureFileSystem :
            IProjectScaffoldFileSystem
        {
            private readonly IProjectScaffoldFileSystem _inner;

            public StagingCleanupFailureFileSystem(
                IProjectScaffoldFileSystem inner)
            {
                _inner = inner;
            }

            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) =>
                _inner.CreateDirectory(path);
            public void WriteCreateNew(string path, string content) =>
                _inner.WriteCreateNew(path, content);
            public string ReadAllText(string path) =>
                _inner.ReadAllText(path);
            public void DeleteFile(string path) => _inner.DeleteFile(path);

            public void DeleteDirectory(string path, bool recursive)
            {
                string stagingSegment = Path.Combine(
                    "Library",
                    "CoCoFlow",
                    "ProjectScaffold");
                if (recursive &&
                    path.IndexOf(
                        stagingSegment,
                        StringComparison.Ordinal) >= 0)
                {
                    throw new IOException(
                        "Injected staging cleanup failure.");
                }

                _inner.DeleteDirectory(path, recursive);
            }

            public bool DirectoryExists(string path) =>
                _inner.DirectoryExists(path);
            public bool IsDirectoryEmpty(string path) =>
                _inner.IsDirectoryEmpty(path);
            public FileAttributes GetAttributes(string path) =>
                _inner.GetAttributes(path);
        }

        private sealed class MutableProviderDetector :
            IProjectScaffoldProviderDetector
        {
            public IReadOnlyList<string> Paths { get; set; } =
                Array.Empty<string>();
            public string IdentityToken { get; set; } = "compiled-type";

            public IReadOnlyList<ProjectScaffoldProviderIdentity> FindProviders(
                string workingDirectory) => Paths
                .Select(path => new ProjectScaffoldProviderIdentity(
                    path,
                    IdentityToken + "|" + path))
                .ToArray();
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

        public static void ValidateGeneratedGraphCatalog()
        {
            Type bindings = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly =>
                    assembly.GetType(
                        "CoCoFlowProject.ProjectStateGraphBindings",
                        false))
                .FirstOrDefault(type => type != null);
            if (bindings == null)
            {
                throw new InvalidOperationException(
                    "Generated ProjectStateGraphBindings was not compiled.");
            }

            var createCatalog = bindings.GetMethod(
                "CreateCatalog",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);
            if (createCatalog == null ||
                createCatalog.Invoke(null, null) == null)
            {
                throw new InvalidOperationException(
                    "Generated Graph catalog could not be created.");
            }

            var graphAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                    string.Equals(
                        assembly.GetName().Name,
                        "CoCoFlowProject.Graph",
                        StringComparison.Ordinal));
            if (graphAssembly == null)
            {
                throw new InvalidOperationException(
                    "Generated pure Graph assembly was not loaded.");
            }

            string[] forbiddenPrefixes =
            {
                "Unity",
                "CoCoFlow.Runtime.Core.StateGraphAuthoring",
                "CoCoFlow.Runtime.Gameplay",
                "CoCoFlow.Runtime.Modules"
            };
            foreach (var reference in graphAssembly.GetReferencedAssemblies())
            {
                if (string.Equals(
                        reference.Name,
                        "CoCoFlow.Runtime.Core",
                        StringComparison.Ordinal) ||
                    forbiddenPrefixes.Any(prefix =>
                        reference.Name.StartsWith(
                            prefix,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "Generated Graph assembly has a forbidden reference: " +
                        reference.Name);
                }
            }
        }

        public static void ValidateRealGeneratedProviderDetection()
        {
            string workingDirectory = Directory.GetCurrentDirectory();
            var detector = new ProjectScaffoldProviderDetector();
            IReadOnlyList<ProjectScaffoldProviderIdentity> providers =
                detector.FindProviders(workingDirectory);
            ProjectScaffoldProviderIdentity[] generated = providers
                .Where(provider => provider.TypeIdentity.Contains(
                    "CoCoFlowProject.ProjectStateGraphBindingProvider"))
                .ToArray();
            if (generated.Length != 1)
            {
                throw new InvalidOperationException(
                    "The real detector did not find exactly one generated " +
                    "secondary ProjectStateGraphBindingProvider. Found: " +
                    generated.Length);
            }

            const string secondRoot =
                "Assets/CoCoFlowProjectPre14SecondRoot";
            var plan = ProjectScaffoldPlanner.Build(
                new ProjectScaffoldRequest(
                    secondRoot,
                    ProjectScaffoldAssemblyMode.AssemblyCSharp),
                workingDirectory,
                detector);
            if (plan.ExistingProviderPaths.Count != 1 ||
                BindingContent(plan).Contains(
                    "public sealed class ProjectStateGraphBindingProvider"))
            {
                throw new InvalidOperationException(
                    "A second Preview did not reuse the real compiled " +
                    "Provider identity.");
            }
        }

        public static void ValidateMultipleRealProvidersBlockApply()
        {
            string workingDirectory = Directory.GetCurrentDirectory();
            var detector = new ProjectScaffoldProviderDetector();
            var plan = ProjectScaffoldPlanner.Build(
                new ProjectScaffoldRequest(
                    "Assets/CoCoFlowProjectPre14MultipleProviderCheck",
                    ProjectScaffoldAssemblyMode.AssemblyCSharp),
                workingDirectory,
                detector);
            if (plan.ExistingProviderPaths.Count < 2 ||
                plan.CanApply ||
                !plan.Conflicts.Any(conflict => conflict.Contains(
                    "Multiple ICoCoStateGraphProjectBindingProvider")))
            {
                throw new InvalidOperationException(
                    "Two real compiled project Providers did not block Apply.");
            }
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
            var detector = new MutableProviderDetector();
            ProjectScaffoldPlan plan = ProjectScaffoldPlanner.Build(
                request,
                workingDirectory,
                detector);
            ProjectScaffoldApplyResult result =
                new ProjectScaffoldWriter(
                    new ProjectScaffoldFileSystem(),
                    detector).Apply(plan, workingDirectory);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error);
            }

            File.WriteAllText(ValidationMarker, mode.ToString());
            AssetDatabase.Refresh();
        }
    }
}
