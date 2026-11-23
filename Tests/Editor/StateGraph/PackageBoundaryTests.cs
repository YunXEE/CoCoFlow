using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class PackageBoundaryTests
    {
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

        [Test]
        public void CoreAndStateGraphHaveOnlyTheirDeclaredRuntimeDependencies()
        {
            const string coreName = "CoCoFlow.Runtime.Core";
            const string graphName = "CoCoFlow.Runtime.StateGraph";
            const string hostName = "CoCoFlow.Runtime.StateGraphHost";
            string root = PackageInfo.FindForAssembly(typeof(CoCoServices).Assembly).resolvedPath;
            AssertDefinition("Core", coreName, typeof(CoCoServices).Assembly, true);
            AssertDefinition("StateGraph", graphName, typeof(StateLogic).Assembly, true, coreName);
            AssertDefinition("StateGraphHost", hostName, typeof(StateGraphAsset).Assembly, false, coreName, graphName);
            AssertReferences(typeof(CoCoServices).Assembly);
            AssertReferences(typeof(StateLogic).Assembly, coreName);
            Assert.That(typeof(ContextFrame).Assembly, Is.SameAs(typeof(StateLogic).Assembly));
            Assert.That(typeof(StateGraphAsset).Assembly.GetName().Name, Is.EqualTo(hostName));

            void AssertDefinition(string folder, string name, Assembly assembly, bool engineFree, params string[] references)
            {
                AssemblyDefinition definition = ReadAssemblyDefinition(root, $"Runtime/{folder}/{name}.asmdef");
                Assert.That(name, Is.EqualTo($"CoCoFlow.Runtime.{folder}"));
                Assert.That(definition.name, Is.EqualTo(name));
                Assert.That(definition.rootNamespace, Is.EqualTo(name));
                Assert.That(assembly.GetName().Name, Is.EqualTo(name));
                CollectionAssert.AreEquivalent(new[] { name }, assembly.GetExportedTypes()
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

        [Test]
        public void StateGraphGrantsInternalAccessOnlyToItsDeclaredConsumers()
        {
            var friends = typeof(StateLogic).Assembly
                .GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName);

            CollectionAssert.AreEquivalent(new[]
            {
                "CoCoFlow.Editor.StateGraph",
                "CoCoFlow.Runtime.StateGraphHost",
                "CoCoFlow.Tests.Editor.StateGraph",
                "CoCoFlow.Tests.Runtime.StateGraphHost",
                "CoCoFlow.Runtime.Modules.Input",
                "CoCoFlow.Tests.StateGraphAuthoringFixtures",
                "CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures"
            }, friends);
        }

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
                "Runtime/StateGraphHost/CoCoFlow.Runtime.StateGraphHost.asmdef",
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

        [Test]
        public void CoreAndStateGraphPublicTypesExposeNoUnityObjects()
        {
            AssertAssemblyExposesNoUnityObjects(typeof(CoCoServices).Assembly);
            AssertAssemblyExposesNoUnityObjects(typeof(StateGraphSource).Assembly);
        }

        [Test]
        public void StateAndLayerPublicApisDoNotExposeEventTransportTypes()
        {
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateLogic));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateConfig));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(ActivationMemory));

            AssertPublicSurfaceHasNoForbiddenTransport(typeof(StateLayerSource));
        }

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

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public bool allowUnsafeCode;
            public bool noEngineReferences;
        }
    }
}
