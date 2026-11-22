using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoPackageBoundaryTests
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
        public void CoreAndStateGraphHaveOnlyTheirDeclaredRuntimeDependencies()
        {
            const string coreName = "CoCoFlow.Runtime.Core";
            const string graphName = "CoCoFlow.Runtime.Core.StateGraph";
            const string hostName = "CoCoFlow.Runtime.StateGraphHost";
            string root = PackageInfo.FindForAssembly(typeof(CoCoServices).Assembly).resolvedPath;
            AssertDefinition("Core", coreName, true);
            AssertDefinition("StateGraph", graphName, true, coreName);
            AssertDefinition("StateGraphHost", hostName, false, coreName, graphName);
            AssertReferences(typeof(CoCoServices).Assembly);
            AssertReferences(typeof(CoCoStateLogic).Assembly, coreName);
            Assert.That(typeof(CoCoContextFrame).Assembly, Is.SameAs(typeof(CoCoStateLogic).Assembly));
            Assert.That(typeof(CoCoStateGraphAsset).Assembly.GetName().Name, Is.EqualTo(hostName));

            void AssertDefinition(string folder, string name, bool engineFree, params string[] references)
            {
                AssemblyDefinition definition = ReadAssemblyDefinition(root, $"Runtime/{folder}/{name}.asmdef");
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
        public void ContentIsUnityFacingAndCoreTemporalPersistenceRemainIndependent()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(CoCoStateLogic).Assembly);
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
                "Runtime/StateGraph/CoCoFlow.Runtime.Core.StateGraph.asmdef",
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
            AssertAssemblyExposesNoUnityObjects(typeof(CoCoStateGraphSource).Assembly);
        }

        [Test]
        public void StateAndLayerPublicApisDoNotExposeEventTransportTypes()
        {
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoStateLogic));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoStateConfig));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoActivationMemory));

            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoStateLayerSource));
        }

        [Test]
        public void Pre1ContextAndOperationAliasesAreAbsent()
        {
            Assembly[] assemblies =
            {
                typeof(CoCoStateLogic).Assembly,
                typeof(CoCoContextFrame).Assembly
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
            Assembly stateFlowAssembly = typeof(CoCoContextFrame).Assembly;
            Type frameType = RequireExportedType(stateFlowAssembly, "CoCoContextFrame");
            Type arenaType = RequireExportedType(stateFlowAssembly, "CoCoContextFrameArena");
            Type preparedType = RequireExportedType(stateFlowAssembly, "CoCoPreparedContextCommit");
            Type finalizedType = RequireExportedType(stateFlowAssembly, "CoCoFinalizedContextCommit");

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
            Assert.AreEqual(typeof(CoCoContextCommitStatus).MakeByRefType(), finalizeParameters[1].ParameterType);
            Assert.IsTrue(finalizedType.IsValueType);
            Assert.IsTrue(
                finalizedType.GetCustomAttributesData().Any(attribute =>
                    attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
                "Finalized commits must remain readonly value tokens.");
            MethodInfo commit = finalizedType.GetMethod("Commit", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(commit);
            Assert.AreEqual(typeof(CoCoContextCommitResult), commit.ReturnType);
            Assert.AreEqual(0, commit.GetParameters().Length);
        }

        [Test]
        public void PreFiveKeepsMutableTraceAndOutcomeRegistrationInternal()
        {
            Assembly stateFlowAssembly = typeof(CoCoContextFrame).Assembly;
            Assert.IsNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type.Name == "CoCoStateFlowTraceBuffer"));
            Assert.IsNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type.Name == "CoCoOperatorOutcomeRequirement"));
            Assert.IsNotNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type == typeof(ICoCoStateFlowTrace)));
            Assert.IsNotNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type == typeof(CoCoStateFlowTraceEntry)));
            Assert.IsNotNull(stateFlowAssembly.GetExportedTypes().FirstOrDefault(type =>
                type == typeof(CoCoContextFrameReadView)));

            Type arenaType = typeof(CoCoContextFrameArena);
            Assert.IsNull(arenaType.GetProperty("HasAvailableCapacity"));
            Assert.IsNull(arenaType.GetProperty("IsDisposed"));
            Assert.IsNotNull(arenaType.GetProperty("Previous"));
            Assert.IsNotNull(arenaType.GetMethod("TryValidateRestore"));
            Assert.IsNotNull(arenaType.GetMethod("Dispose"));
        }

        [Test]
        public void StateFlowExposesIntentReadsAndKeepsRuntimeLifecycleInternal()
        {
            Assembly stateFlowAssembly = typeof(CoCoContextFrame).Assembly;
            Type reducerFactory = RequireExportedType(stateFlowAssembly, "ICoCoIntentReducerFactory`2");
            Type runtimeType = typeof(CoCoIntentFrameRuntime);
            Type layoutType = typeof(CoCoIntentFrameLayout);
            _ = RequireExportedType(stateFlowAssembly, nameof(ICoCoIntentFrame));
            Type projectionCodec = stateFlowAssembly.GetType(
                "CoCoFlow.Runtime.Core.CoCoContextProjectionCodec",
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
                PackageInfo.FindForAssembly(typeof(CoCoStateLogic).Assembly);
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
                if (surfaceType == typeof(CoCoStateEventOutboxWriter)) continue;
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
