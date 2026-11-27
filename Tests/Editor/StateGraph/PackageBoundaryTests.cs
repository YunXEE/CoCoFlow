using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEditor.Compilation;
using Assembly = System.Reflection.Assembly;
using UnityEngine;

// EditMode 对照 Unity 实际源码归属、程序集和导出成员，检验项目可用的编译边界。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>EditMode 对照 Unity 实际源码归属、程序集和导出成员，检验项目可用的编译边界。</summary>
    public sealed class PackageBoundaryTests
    {

        #region Public API

        /// <summary>枚举运行时导出类型，核对各模块遵守 Core 所用的 CoCo 前缀范围。</summary>
        [Test]
        public void OnlyCoreProductionAssembliesDeclareCoCoPrefixedTypes()
        {
            var outsideCore = AppDomain.CurrentDomain.GetAssemblies().Where(assembly =>
            {
                string name = assembly.GetName().Name;
                return (name.StartsWith("CoCoFlow.Runtime.", StringComparison.Ordinal) ||
                        name.StartsWith("CoCoFlow.Editor.", StringComparison.Ordinal)) &&
                       name != "CoCoFlow.Runtime.Core" && name != "CoCoFlow.Editor.Core" &&
                       PackageInfo.FindForAssembly(assembly)?.name == "com.yunxee.cocoflow";
            }).ToArray();
            Assert.That(outsideCore, Does.Contain(typeof(StateLogic).Assembly));
            var prefixedTypes = outsideCore.SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.Name.StartsWith("CoCo", StringComparison.Ordinal) ||
                               type.Name.StartsWith("ICoCo", StringComparison.Ordinal))
                .Select(type => type.FullName);
            Assert.That(prefixedTypes, Is.Empty);
        }

        /// <summary>同时检查 asmdef 和实际 DLL，比较纯合同、Unity 图程序集及 Core 的直接依赖。</summary>
        [Test]
        public void CoreAndStateGraphHaveOnlyTheirDeclaredRuntimeDependencies()
        {
            const string coreName = "CoCoFlow.Runtime.Core";
            const string graphName = "CoCoFlow.Runtime.StateGraph.Contracts";
            const string hostName = "CoCoFlow.Runtime.StateGraph";
            string root = PackageInfo.FindForAssembly(typeof(CoCoServices).Assembly).resolvedPath;
            AssertDefinition("Core", coreName, typeof(CoCoServices).Assembly, true);
            AssertDefinition("StateGraph/Contracts", graphName, typeof(StateLogic).Assembly, true, coreName);
            AssertDefinition("StateGraph", hostName, typeof(StateGraphAsset).Assembly, false, coreName, graphName);
            AssertReferences(typeof(CoCoServices).Assembly);
            AssertReferences(typeof(StateLogic).Assembly, coreName);
            Assert.That(typeof(ContextFrame).Assembly, Is.SameAs(typeof(StateLogic).Assembly));
            Assert.That(typeof(StateGraphAsset).Assembly.GetName().Name, Is.EqualTo(hostName));

            void AssertDefinition(string folder, string name, Assembly assembly, bool engineFree, params string[] references)
            {
                AssemblyDefinition definition = ReadAssemblyDefinition(root, $"Runtime/{folder}/{name}.asmdef");
                Assert.That(definition.name, Is.EqualTo(name));
                string expectedNamespace = name == graphName ? hostName : name;
                Assert.That(definition.rootNamespace, Is.EqualTo(expectedNamespace));
                Assert.That(assembly.GetName().Name, Is.EqualTo(name));
                CollectionAssert.AreEquivalent(new[] { expectedNamespace }, assembly.GetExportedTypes()
                    .Select(type => type.Namespace).Distinct());
                CollectionAssert.AreEquivalent(references, definition.references);
                Assert.That(definition.noEngineReferences, Is.EqualTo(engineFree));
            }
            void AssertReferences(Assembly assembly, params string[] expected)
            {
                var references = assembly.GetReferencedAssemblies().Select(item => item.Name);
                CollectionAssert.AreEquivalent(expected, references.Where(name =>
                    name.StartsWith("Unity", StringComparison.Ordinal) ||
                    name.StartsWith("CoCoFlow.", StringComparison.Ordinal)));
            }
        }

        /// <summary>从 Unity 的 Editor 或 Player 编译清单按源码路径解析程序集，并核对实际加载的核心类型。</summary>
        [TestCase(AssembliesType.Editor)]
        [TestCase(AssembliesType.Player)]
        public void UnityCompilesEachStateGraphSourceIntoItsDeclaredAssembly(AssembliesType target)
        {
            var package = PackageInfo.FindForAssembly(typeof(StateLogic).Assembly);
            string runtime = Path.Combine(package.resolvedPath, "Runtime");
            string pure = typeof(StateLogic).Assembly.GetName().Name;
            string host = typeof(StateGraphAsset).Assembly.GetName().Name;
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var assembly in CompilationPipeline.GetAssemblies(target).Where(item => item.name == pure || item.name == host))
            {
                foreach (string source in assembly.sourceFiles)
                {
                    string path = source.Replace('\\', '/');
                    string prefix = $"Packages/{package.name}/";
                    if (path.StartsWith(prefix, StringComparison.Ordinal))
                        path = Path.Combine(package.resolvedPath, path.Substring(prefix.Length));
                    owners.Add(Path.GetFullPath(path), assembly.name);
                }
            }
            foreach (string folder in new[] { "StateGraph", "StateGraphHost" })
            {
                string root = Path.Combine(runtime, folder);
                foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string relative = path.Substring(root.Length + 1).Replace('\\', '/');
                    string group = relative.Split('/')[0];
                    bool contracts = folder == "StateGraph" &&
                        (group == "Contracts" || group == "Graph" || group == "State" || group == "Instance" || group == "Temporal");
                    Assert.That(owners.TryGetValue(path, out string compiled), Is.True, path);
                    Assert.AreEqual(contracts ? pure : host, compiled, $"{target} {path}");
                }
            }
        }

        /// <summary>枚举友元声明并对照包内允许使用内部入口的消费者清单。</summary>
        [Test]
        public void StateGraphGrantsInternalAccessOnlyToItsDeclaredConsumers()
        {
            var friends = typeof(StateLogic).Assembly
                .GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName);

            CollectionAssert.AreEquivalent(new[]
            {
                "CoCoFlow.Editor.StateGraph",
                "CoCoFlow.Runtime.StateGraph",
                "CoCoFlow.Tests.Editor.StateGraph",
                "CoCoFlow.Tests.Runtime.StateGraphHost",
                "CoCoFlow.Runtime.Modules.Input",
                "CoCoFlow.Tests.StateGraphAuthoringFixtures",
                "CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures",
                "CoCoFlow.Runtime.Locomotion.Contracts"
            }, friends);
        }

        /// <summary>对照内容模块的引擎依赖及 Core、Temporal、Persistence 的独立编译边界。</summary>
        [Test]
        public void ContentIsUnityFacingAndCoreTemporalPersistenceRemainIndependent()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(StateLogic).Assembly);
            Assert.IsNotNull(packageInfo);

            AssemblyDefinition content = ReadAssemblyDefinition(
                packageInfo.resolvedPath,
                "Runtime/Content/CoCoFlow.Runtime.Content.asmdef");
            CollectionAssert.AreEqual(
                new[] { "CoCoFlow.Runtime.Core", "UniTask" },
                content.references);
            Assert.IsFalse(content.noEngineReferences);

            string[] independentAssemblyPaths =
            {
                "Runtime/StateGraph/CoCoFlow.Runtime.StateGraph.asmdef",
                "Runtime/Core/CoCoFlow.Runtime.Core.asmdef",
                "Runtime/StateGraph/Contracts/CoCoFlow.Runtime.StateGraph.Contracts.asmdef",
                "Runtime/Modules/Persistence/CoCoFlow.Runtime.Modules.Persistence.asmdef"
            };
            foreach (string relativePath in independentAssemblyPaths)
            {
                AssemblyDefinition definition = ReadAssemblyDefinition(
                    packageInfo.resolvedPath,
                    relativePath);
                CollectionAssert.DoesNotContain(
                    definition.references,
                    "CoCoFlow.Runtime.Content",
                    relativePath);
            }
        }

        /// <summary>从实际导出签名递归检查纯 Core 与 StateGraph 合同未暴露 Unity 对象。</summary>
        [Test]
        public void CoreAndStateGraphPublicTypesExposeNoUnityObjects()
        {
            AssertAssemblyExposesNoUnityObjects(typeof(CoCoServices).Assembly);
            AssertAssemblyExposesNoUnityObjects(typeof(StateGraphSource).Assembly);
        }

        /// <summary>检查状态和图层的公开签名，使运行时事件运输类型不会泄漏到玩法入口。</summary>
        [Test]
        public void StateAndLayerPublicApisDoNotExposeEventTransportTypes()
        {
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateLogic));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateConfig));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateActivationMemory));

            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateLayerSource));
        }

        /// <summary>从实际 DLL 检查回调持有的读写接口，避免再把实例、帧存储或整步提交凭据交给玩法。</summary>
        [Test]
        public void CallbacksUseScopedReadersAndTransitionRequests()
        {
            CheckFields(typeof(StateExecutionContext), typeof(IStateTransitionRequests), typeof(StateGraphRuntime));
            CheckFields(typeof(StateEventBatch<int>), typeof(IStateEventBatchReader<int>), typeof(StateEventLane<int>));
            CheckFields(typeof(ContextFrameReadView), typeof(ICommittedContextReadSource), typeof(ContextFrame));
            CheckFields(typeof(StagedOperationFrame), typeof(IStagedOperationReader), typeof(FinalizedOperationFrame));
            CheckFields(typeof(OperatorExecutionContext), typeof(StagedOperationFrame), typeof(FinalizedOperationFrame));

            void CheckFields(Type view, Type expected, Type forbidden)
            {
                var types = view.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType).ToArray();
                CollectionAssert.Contains(types, expected, view.FullName);
                CollectionAssert.DoesNotContain(types, forbidden, view.FullName);
            }
        }

        /// <summary>从实际 DLL 按旧全名查找 Context 与操作别名，验证这些入口已经退役。</summary>
        [Test]
        public void Pre1ContextAndOperationAliasesAreAbsent()
        {
            Assembly[] assemblies =
            {
                typeof(StateLogic).Assembly,
                typeof(ContextFrame).Assembly
            };
            string[] retiredTypeNames =
            {
                "ICoCoContextSection",
                "CoCoContextSectionRequirement",
                "ICoCoOperationPort",
                "CoCoOperationPortRequirement",
                "ICoCoOperationCommand",
                "ICoCoOperationCommandSink",
                "CoCoOperationCommandSink",
                "ICoCoNoOpOperation",
                "CoCoNoOpOperation"
            };

            foreach (Assembly assembly in assemblies)
            {
                Type[] exportedTypes = assembly.GetExportedTypes();
                foreach (string retiredTypeName in retiredTypeNames)
                {
                    Assert.IsFalse(
                        exportedTypes.Any(type => type.Name == retiredTypeName),
                        $"{assembly.GetName().Name} still exports retired type {retiredTypeName}.");
                }
            }
        }

        /// <summary>检查候选提交凭据的可见成员，使框架持有最终确认的权限。</summary>
        [Test]
        public void StateFlowPublicSurfaceFreezesGenerationScopedFinalizeAuthority()
        {
            Assembly stateFlowAssembly = typeof(ContextFrame).Assembly;
            Type frameType = RequireExportedType(stateFlowAssembly, "ContextFrame");
            Type arenaType = RequireExportedType(stateFlowAssembly, "ContextFrameArena");
            Type preparedType = RequireExportedType(stateFlowAssembly, "PreparedContextCommit");
            Type finalizedType = RequireExportedType(stateFlowAssembly, "FinalizedContextCommit");

            Assert.IsTrue(frameType.IsValueType, "ContextFrame must be a generation-scoped value handle.");
            Assert.IsTrue(
                frameType.GetCustomAttributesData().Any(attribute =>
                    attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
                "ContextFrame must remain a readonly struct handle.");
            Assert.AreEqual(typeof(bool), frameType.GetProperty("IsAlive")?.PropertyType);
            Assert.AreEqual(frameType, arenaType.GetProperty("Current")?.PropertyType);
            Assert.AreEqual(typeof(bool), arenaType.GetProperty("HasCurrent")?.PropertyType);

            Assert.IsNull(
                preparedType.GetMethod("Commit", BindingFlags.Public | BindingFlags.Instance),
                "A Prepared token must not bypass Derived finalization.");
            MethodInfo tryFinalize = preparedType.GetMethod(
                "TryFinalize",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(tryFinalize, "Prepared commits must cross an explicit Finalize boundary.");
            Assert.AreEqual(typeof(bool), tryFinalize.ReturnType);
            ParameterInfo[] finalizeParameters = tryFinalize.GetParameters();
            Assert.AreEqual(2, finalizeParameters.Length);
            Assert.IsTrue(finalizeParameters[0].IsOut);
            Assert.AreEqual(finalizedType.MakeByRefType(), finalizeParameters[0].ParameterType);
            Assert.IsTrue(finalizeParameters[1].IsOut);
            Assert.AreEqual(typeof(ContextCommitStatus).MakeByRefType(), finalizeParameters[1].ParameterType);
            Assert.IsTrue(finalizedType.IsValueType);
            Assert.IsTrue(
                finalizedType.GetCustomAttributesData().Any(attribute =>
                    attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
                "Finalized commits must remain readonly value tokens.");
            MethodInfo commit = finalizedType.GetMethod("Commit", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(commit);
            Assert.AreEqual(typeof(ContextCommitResult), commit.ReturnType);
            Assert.AreEqual(0, commit.GetParameters().Length);
        }

        /// <summary>检查跟踪写入与结果注册的 CLR 可见性，使项目无法改写这些运行时状态。</summary>
        [Test]
        public void PreFiveKeepsMutableTraceAndOutcomeRegistrationInternal()
        {
            Assembly stateFlowAssembly = typeof(ContextFrame).Assembly;
            Assert.IsNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type.Name == "StateFlowTraceBuffer"));
            Assert.IsNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type.Name == "OperatorOutcomeRequirement"));
            Assert.IsNotNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type == typeof(IStateFlowTrace)));
            Assert.IsNotNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type == typeof(StateFlowTraceEntry)));
            Assert.IsNotNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type == typeof(ContextFrameReadView)));

            Type arenaType = typeof(ContextFrameArena);
            Assert.IsNull(arenaType.GetProperty("HasAvailableCapacity"));
            Assert.IsNull(arenaType.GetProperty("IsDisposed"));
            Assert.IsNotNull(arenaType.GetProperty("Previous"));
            Assert.IsNotNull(arenaType.GetMethod("TryValidateRestore"));
            Assert.IsNotNull(arenaType.GetMethod("Dispose"));
        }

        /// <summary>对照 Intent 读接口与生命周期管理类型，检查包外可读及框架专用入口。</summary>
        [Test]
        public void StateFlowExposesIntentReadsAndKeepsRuntimeLifecycleInternal()
        {
            Assembly stateFlowAssembly = typeof(ContextFrame).Assembly;
            Type reducerFactory = RequireExportedType(stateFlowAssembly, "IIntentReducerFactory`2");
            Type runtimeType = typeof(IntentFrameRuntime);
            Type layoutType = typeof(IntentFrameLayout);
            _ = RequireExportedType(stateFlowAssembly, nameof(IIntentFrame));
            Type projectionCodec = stateFlowAssembly.GetType(
                "CoCoFlow.Runtime.StateGraph.ContextProjectionCodec",
                false);

            Assert.IsTrue(reducerFactory.IsInterface);
            Assert.IsFalse(runtimeType.IsVisible);
            Assert.IsFalse(layoutType.IsVisible);
            Assert.IsNotNull(projectionCodec, "The exact-layout Codec spike should remain available internally.");
            Assert.IsFalse(projectionCodec.IsPublic, "The Pre2 Codec spike is not a durable wire contract.");
        }

        /// <summary>扫描模块的导出名并检查旧编号批次类型已经退出。</summary>
        [Test]
        public void Pre14NonCoreTypesUseModulePrefixesAndRetireNumberedBatchName()
        {
            PackageInfo packageInfo =
                PackageInfo.FindForAssembly(typeof(StateLogic).Assembly);
            Assert.IsNotNull(packageInfo);

            string[] roots =
            {
                "Runtime/Modules/Input",
                "Runtime/Modules/Localization"
            };
            var forbiddenPublicPrefix = new Regex(
                @"\bpublic\s+(?:(?:sealed|readonly|static)\s+)*(?:class|struct|interface|enum)\s+(CoCo\w+)",
                RegexOptions.Compiled);

            foreach (string root in roots)
            {
                string absoluteRoot = Path.Combine(
                    packageInfo.resolvedPath,
                    root);
                foreach (string path in Directory.GetFiles(
                             absoluteRoot,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    string source = File.ReadAllText(path);
                    Match match = forbiddenPublicPrefix.Match(source);
                    Assert.IsFalse(
                        match.Success,
                        $"{root} exports Core-prefixed type {match.Groups[1].Value} in {path}.");
                    StringAssert.DoesNotContain(
                        "InputCommandBatch8",
                        source,
                        path);
                    StringAssert.DoesNotContain(
                        "CoCoProjectScaffold",
                        source,
                        path);
                }
            }
        }

        #endregion

        #region Internal Logic

        private static readonly string[] ForbiddenStateSurfaceTokens =
        {
            "EventBus",
            "EventAgent",
            "Envelope",
            "EventRouter",
            "Mailbox",
            "EventInbox",
            "EventOutbox",
            "Operator"
        };

        private static AssemblyDefinition ReadAssemblyDefinition(string packagePath, string relativePath)
        {
            string asmdefPath = Path.Combine(packagePath, relativePath);
            Assert.IsTrue(File.Exists(asmdefPath), asmdefPath);
            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(asmdefPath));
            Assert.IsNotNull(definition);
            Assert.IsNotNull(definition.references);
            return definition;
        }

        private static Type RequireExportedType(Assembly assembly, string typeName)
        {
            Type type = assembly.GetExportedTypes().SingleOrDefault(candidate => candidate.Name == typeName);
            Assert.IsNotNull(type, $"{assembly.GetName().Name} must export {typeName}.");
            return type;
        }

        private static void AssertAssemblyExposesNoUnityObjects(Assembly assembly)
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                Assert.IsFalse(
                    typeof(UnityEngine.Object).IsAssignableFrom(type),
                    type.FullName);
                foreach (Type surfaceType in GetPublicSurfaceTypes(type))
                {
                    Assert.IsFalse(
                        ContainsUnityObject(surfaceType),
                        $"{type.FullName} exposes Unity type {surfaceType}.");
                }
            }
        }

        private static void AssertPublicSurfaceHasNoForbiddenTransport(Type type)
        {
            foreach (Type surfaceType in GetPublicSurfaceTypes(type).Append(type))
            {
                if (surfaceType == typeof(StateEventOutboxWriter)) continue;
                string name = surfaceType.FullName ?? surfaceType.Name;
                foreach (string token in ForbiddenStateSurfaceTokens)
                {
                    Assert.IsFalse(
                        name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0,
                        $"{type.FullName} exposes forbidden transport type {name}.");
                }
            }
        }

        private static IEnumerable<Type> GetPublicSurfaceTypes(Type type)
        {
            const BindingFlags flags = BindingFlags.Public |
                                       BindingFlags.Instance |
                                       BindingFlags.Static |
                                       BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                yield return field.FieldType;
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                yield return property.PropertyType;
                foreach (ParameterInfo parameter in property.GetIndexParameters())
                {
                    yield return parameter.ParameterType;
                }
            }

            foreach (EventInfo eventInfo in type.GetEvents(flags))
            {
                yield return eventInfo.EventHandlerType;
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(flags))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    yield return parameter.ParameterType;
                }
            }

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                yield return method.ReturnType;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                foreach (Type genericArgument in method.GetGenericArguments())
                {
                    foreach (Type constraint in genericArgument.GetGenericParameterConstraints())
                    {
                        yield return constraint;
                    }
                }
            }
        }

        private static bool ContainsUnityObject(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.IsByRef || type.IsPointer || type.IsArray)
            {
                return ContainsUnityObject(type.GetElementType());
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return true;
            }

            return type.IsGenericType && type.GetGenericArguments().Any(ContainsUnityObject);
        }

        /// <summary>承接 Unity asmdef 的名称、引用及引擎标记，供包体边界测试对照实际 DLL。</summary>
        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public bool allowUnsafeCode;
            public bool noEngineReferences;
        }

        #endregion
    }
}
