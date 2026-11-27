using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Editor.StateGraph;
using NUnit.Framework;
using UnityEditor.Compilation;

// 创作依赖测试沿直接与传递程序集引用查找禁止的 Unity 依赖，并检查报告的来源。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>创作依赖测试沿直接与传递程序集引用查找禁止的 Unity 依赖，并检查报告的来源。</summary>
    public sealed class StateGraphDependencyClosureValidationTests
    {

        #region Public API

        /// <summary>沿实际移动合同的程序集引用检查纯系统依赖，确认通过创作校验。</summary>
        [Test]
        public void UnityLocomotionContractSystemReferencesAreAccepted()
        {
            var assembly = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Single(item => item.name == "CoCoFlow.Runtime.Locomotion.Contracts");
            var nodes = Capture(assembly);
            Assert.IsEmpty(nodes[0].PrecompiledReferences,
                "Unity's own BCL reference packs must not be treated as custom DLLs.");
        }

        /// <summary>使用与随包基础库相同文件名的项目 DLL，确认仍核对来源并报告违规依赖。</summary>
        [TestCase("Microsoft.Bcl.AsyncInterfaces")]
        [TestCase("Microsoft.Extensions.DependencyInjection.Abstractions")]
        public void ProjectDllWithBundledBclNameStillFailsClosed(string name)
        {
            var assembly = new UnityEditor.Compilation.Assembly(
                "CoCoFlow.Runtime.Locomotion.Contracts", string.Empty,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<UnityEditor.Compilation.Assembly>(),
                new[] { Path.Combine(Path.GetTempPath(), "CoCoFlow-ProjectDll", name + ".dll") }, default);
            var nodes = Capture(assembly);
            CollectionAssert.AreEqual(new[] { name }, nodes[0].PrecompiledReferences);
            Assert.AreEqual(1, StateGraphAuthoringDependencyClosureValidator.Validate(
                new[] { assembly.name }, nodes).Length);
        }

        /// <summary>从项目程序集直接引用禁止的 Unity 程序集，检查诊断列出完整路径。</summary>
        [Test]
        public void DirectForbiddenDependencyReportsTheCompletePath()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "CoCoFlow.Runtime.StateGraph" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> CoCoFlow.Runtime.StateGraph " +
                "references a forbidden framework boundary.");
        }

        /// <summary>沿多级依赖引入禁止程序集，检查诊断保留从根到目标的每一段。</summary>
        [Test]
        public void TransitiveForbiddenDependencyReportsEveryPathSegment()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Helper", references: new[]
                {
                    "CoCoFlow.Runtime.StateGraph"
                }),
                Node("Author", references: new[] { "Helper" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> Helper -> " +
                "CoCoFlow.Runtime.StateGraph " +
                "references a forbidden framework boundary.");
        }

        /// <summary>由两条路径汇合到同一违规依赖，核对每条不同路径都有记录。</summary>
        [Test]
        public void SharedTransitiveViolationReportsEachDistinctCompletePath()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "Helper.B", "Helper.A" }),
                Node("Helper.A", references: new[] { "Shared" }),
                Node("Helper.B", references: new[] { "Shared" }),
                Node("Shared", references: new[] { "CoCoFlow.Runtime.StateGraph" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> Helper.A -> Shared -> " +
                "CoCoFlow.Runtime.StateGraph references a forbidden framework boundary.",
                "Graph author dependency closure Author -> Helper.B -> Shared -> " +
                "CoCoFlow.Runtime.StateGraph references a forbidden framework boundary.");
        }

        /// <summary>构造允许的循环依赖，验证遍历能结束且没有额外诊断。</summary>
        [Test]
        public void CyclicSafeClosureTerminatesWithoutDiagnostics()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "Helper" }),
                Node("Helper", references: new[] { "Author" }));

            Assert.IsEmpty(diagnostics);
        }

        /// <summary>将作者程序集声明为允许引擎引用，检查闭包校验拒绝该根。</summary>
        [Test]
        public void NoEngineReferencesFalseIsRejected()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", noEngineReferences: false));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author must set noEngineReferences:true.");
        }

        /// <summary>对无法找到 asmdef 的项目程序集执行验证，核对产生可定位的失败。</summary>
        [Test]
        public void AssemblyWithoutAsmdefFailsClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", hasAssemblyDefinition: false));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author does not have an asmdef and cannot prove " +
                "noEngineReferences.");
        }

        /// <summary>从作者程序集引用无法解析的自定义依赖，核对失败的依赖身份。</summary>
        [Test]
        public void UnresolvedCustomDependencyFailsClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "Missing.Helper" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> Missing.Helper cannot be resolved to a " +
                "Player asmdef.");
        }

        /// <summary>给编译闭包加入自定义或禁止的预编译依赖，检查校验逐项拒绝。</summary>
        [Test]
        public void CustomAndForbiddenPrecompiledDependenciesFailClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node(
                    "Author",
                    precompiledReferences: new[]
                    {
                        "UnityEngine.CoreModule",
                        "Vendor.CustomRuntime"
                    }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> UnityEngine.CoreModule " +
                "references a forbidden precompiled assembly.",
                "Graph author dependency closure Author -> Vendor.CustomRuntime " +
                "references an unverifiable custom precompiled assembly.");
        }

        /// <summary>把带系统前缀的项目 asmdef 加入闭包，核对仍追踪其实际依赖。</summary>
        [Test]
        public void SystemPrefixedCustomAsmdefCannotBypassClosureValidation()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", references: new[] { "SystemGameplay" }),
                Node("SystemGameplay", noEngineReferences: false));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> SystemGameplay must set " +
                "noEngineReferences:true.");
        }

        /// <summary>将带系统前缀的自定义预编译 DLL 加入作者闭包，确认不能替代已知框架来源。</summary>
        [Test]
        public void SystemPrefixedCustomPrecompiledDependencyFailsClosed()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", precompiledReferences: new[] { "SystemGameplay" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> SystemGameplay references an " +
                "unverifiable custom precompiled assembly.");
        }

        /// <summary>给自定义程序集提供系统类型身份，核对其实际来源仍导致诊断。</summary>
        [Test]
        public void SystemIdentityCustomPrecompiledDependencyCannotSpoofFrameworkProvenance()
        {
            CoCoDiagnostic[] diagnostics = Validate(
                new[] { "Author" },
                Node("Author", precompiledReferences: new[] { "System.Core" }));

            AssertDiagnosticMessages(
                diagnostics,
                "Graph author dependency closure Author -> System.Core references an " +
                "unverifiable custom precompiled assembly.");
        }

        /// <summary>改变根和节点输入次序后复核诊断，确认报告的路径和顺序稳定。</summary>
        [Test]
        public void DiagnosticsAreCanonicalAcrossRootAndNodeInputOrder()
        {
            var authorA = Node("Author.A", references: new[] { "Missing.A" });
            var authorZ = Node("Author.Z", references: new[] { "Missing.Z" });

            CoCoDiagnostic[] forward = Validate(
                new[] { "Author.Z", "Author.A", "Author.A" },
                authorZ,
                authorA);
            CoCoDiagnostic[] reverse = Validate(
                new[] { "Author.A", "Author.Z" },
                authorA,
                authorZ);

            string[] expected =
            {
                "Graph author dependency closure Author.A -> Missing.A cannot be resolved to a " +
                "Player asmdef.",
                "Graph author dependency closure Author.Z -> Missing.Z cannot be resolved to a " +
                "Player asmdef."
            };
            AssertDiagnosticMessages(forward, expected);
            AssertDiagnosticMessages(reverse, expected);
        }

        /// <summary>传入空目录，检查明确报告无法验证的创作根。</summary>
        [Test]
        public void NullCatalogProducesAClosedFailureDiagnostic()
        {
            CoCoDiagnostic[] diagnostics =
                StateGraphAuthoringDependencyClosureValidator.Validate(
                    null,
                    Array.Empty<UnityEditor.Compilation.Assembly>());

            AssertDiagnosticMessages(
                diagnostics,
                "A frozen Graph Descriptor Catalog is required for dependency closure validation.");
        }

        #endregion

        #region Internal Logic

        private static StateGraphAssemblyGraphNode[] Capture(UnityEditor.Compilation.Assembly assembly) =>
            (StateGraphAssemblyGraphNode[])typeof(StateGraphAuthoringDependencyClosureValidator)
                .GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { new[] { assembly } });

        private static CoCoDiagnostic[] Validate(
            string[] roots,
            params StateGraphAssemblyGraphNode[] nodes) =>
            StateGraphAuthoringDependencyClosureValidator.Validate(roots, nodes);

        private static StateGraphAssemblyGraphNode Node(
            string name,
            bool hasAssemblyDefinition = true,
            bool noEngineReferences = true,
            string[] references = null,
            string[] precompiledReferences = null) =>
            new StateGraphAssemblyGraphNode(
                name,
                hasAssemblyDefinition,
                noEngineReferences,
                references,
                precompiledReferences);

        private static void AssertDiagnosticMessages(
            CoCoDiagnostic[] diagnostics,
            params string[] expectedMessages)
        {
            Assert.AreEqual(expectedMessages.Length, diagnostics.Length);
            for (int index = 0; index < diagnostics.Length; index++)
            {
                Assert.AreEqual(CoCoDiagnosticDomain.State, diagnostics[index].Domain);
                Assert.AreEqual(
                    CoCoDiagnosticCode.InvalidAuthoringDependency,
                    diagnostics[index].Code);
                Assert.IsTrue(diagnostics[index].IsError);
                Assert.AreEqual(expectedMessages[index], diagnostics[index].Message);
            }
        }

        #endregion
    }
}
