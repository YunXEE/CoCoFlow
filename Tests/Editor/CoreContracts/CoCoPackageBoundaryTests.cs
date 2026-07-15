using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.Tests
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
            "EventOutbox"
        };

        [Test]
        public void ContractsAssemblyHasNoEngineGameplayModuleOrEditorReferences()
        {
            string[] assemblyReferences = typeof(CoCoStateLogic).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.IsFalse(assemblyReferences.Any(name =>
                name.StartsWith("Unity", StringComparison.Ordinal) ||
                name.StartsWith("CoCoFlow.Runtime.Gameplay", StringComparison.Ordinal) ||
                name.StartsWith("CoCoFlow.Runtime.Modules", StringComparison.Ordinal) ||
                name.StartsWith("CoCoFlow.Editor", StringComparison.Ordinal)));
        }

        [Test]
        public void StateFlowAssemblyReferencesOnlyContractsWithinCoCoFlow()
        {
            Assembly stateFlowAssembly = typeof(CoCoContextFrame).Assembly;
            string[] cocoFlowReferences = stateFlowAssembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name.StartsWith("CoCoFlow.", StringComparison.Ordinal))
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "CoCoFlow.Runtime.Core.Contracts" },
                cocoFlowReferences);
            Assert.IsFalse(stateFlowAssembly.GetReferencedAssemblies().Any(reference =>
                reference.Name.StartsWith("Unity", StringComparison.Ordinal) ||
                reference.Name.StartsWith("CoCoFlow.Runtime.Gameplay", StringComparison.Ordinal) ||
                reference.Name.StartsWith("CoCoFlow.Runtime.Modules", StringComparison.Ordinal) ||
                reference.Name.StartsWith("CoCoFlow.Editor", StringComparison.Ordinal)));
        }

        [Test]
        public void ContractsAndStateFlowAsmdefsDeclareEngineIndependentBoundaries()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(CoCoStateLogic).Assembly);
            Assert.IsNotNull(packageInfo);

            AssemblyDefinition contracts = ReadAssemblyDefinition(
                packageInfo.resolvedPath,
                "Runtime/Core/Contracts/CoCoFlow.Runtime.Core.Contracts.asmdef");
            Assert.AreEqual("CoCoFlow.Runtime.Core.Contracts", contracts.name);
            Assert.AreEqual("CoCoFlow.Runtime.Core", contracts.rootNamespace);
            Assert.IsEmpty(contracts.references);
            Assert.IsTrue(contracts.noEngineReferences);
            Assert.IsFalse(contracts.allowUnsafeCode);

            AssemblyDefinition stateFlow = ReadAssemblyDefinition(
                packageInfo.resolvedPath,
                "Runtime/Core/StateFlow/CoCoFlow.Runtime.Core.StateFlow.asmdef");
            Assert.AreEqual("CoCoFlow.Runtime.Core.StateFlow", stateFlow.name);
            Assert.AreEqual("CoCoFlow.Runtime.Core", stateFlow.rootNamespace);
            CollectionAssert.AreEqual(
                new[] { "CoCoFlow.Runtime.Core.Contracts" },
                stateFlow.references);
            Assert.IsTrue(stateFlow.noEngineReferences);
            Assert.IsFalse(stateFlow.allowUnsafeCode);
        }

        [Test]
        public void ContractsAndStateFlowPublicTypesExposeNoUnityObjects()
        {
            AssertAssemblyExposesNoUnityObjects(typeof(CoCoStateLogic).Assembly);
            AssertAssemblyExposesNoUnityObjects(typeof(CoCoContextFrame).Assembly);
        }

        [Test]
        public void StateAndLayerPublicApisDoNotExposeEventTransportTypes()
        {
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoStateLogic));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoStateConfig));
            AssertPublicSurfaceHasNoForbiddenTransport(typeof(CoCoActivationMemory));

            Assembly runtimeCoreAssembly = Assembly.Load("CoCoFlow.Runtime.Core");
            Type stateLayer = runtimeCoreAssembly.GetType("CoCoFlow.Runtime.Core.CoCoStateLayer", true);
            AssertPublicSurfaceHasNoForbiddenTransport(stateLayer);
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
        public void PublicContractsContainNoMachineNodeOrOptionalSurface()
        {
            Type[] contractTypes = typeof(CoCoStateLogic).Assembly.GetExportedTypes();

            Assert.IsFalse(contractTypes.Any(type =>
                type.Name.IndexOf("Machine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.Name.IndexOf("Node", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.Name.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(contractTypes.SelectMany(type => type.GetMembers()).Any(member =>
                member.Name.IndexOf("Machine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Node", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0));
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
