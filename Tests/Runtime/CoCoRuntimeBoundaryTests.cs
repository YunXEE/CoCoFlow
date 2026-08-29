using System;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public sealed class CoCoRuntimeBoundaryTests
    {
        [Test]
        public void LifecycleRejectsDestroyedToActive()
        {
            var lifecycle = new CoCoLifecycleContext();

            Assert.IsTrue(lifecycle.TryTransitionTo(CoCoLifecycleState.Active));
            Assert.IsTrue(lifecycle.TryTransitionTo(CoCoLifecycleState.Destroyed));
            Assert.IsFalse(lifecycle.TryTransitionTo(CoCoLifecycleState.Active));
            Assert.Throws<InvalidOperationException>(
                () => lifecycle.TransitionTo(CoCoLifecycleState.Active));
        }

        [Test]
        public void LegacyMonoStateTypesDoNotExist()
        {
            var coreAssembly = typeof(CoCoServices).Assembly;
            string[] retiredTypeNames =
            {
                "CoCoStateController",
                "CoCoStateBase",
                "CoCoStateDefinition",
                "CoCoStateLayer",
                "CoCoStateChildMachine",
                "CoCoStateContextAccess",
                "CoCoStateContextDependency",
                "CoCoStateOperationDependency",
                "CoCoStateTransitionTarget",
                "CoCoStateDefinitionBuilder"
            };

            foreach (string retiredTypeName in retiredTypeNames)
            {
                Assert.IsNull(
                    coreAssembly.GetType($"CoCoFlow.Runtime.Core.{retiredTypeName}"),
                    retiredTypeName);
            }
        }

        [Test]
        public void InputReaderIsTheOnlyInputAuthorityWithoutCoreBridge()
        {
            var inputReaderType = typeof(InputReader);
            var inputAssembly = inputReaderType.Assembly;
            var coreAssembly = typeof(CoCoServices).Assembly;
            var referencedAssemblies = inputReaderType.Assembly.GetReferencedAssemblies();

            Assert.IsTrue(inputReaderType.IsSealed);
            RequireComponent requirement = inputReaderType
                .GetCustomAttributes(typeof(RequireComponent), false)
                .Cast<RequireComponent>()
                .Single();
            Assert.AreSame(typeof(PlayerInput), requirement.m_Type0);
            Assert.IsFalse(referencedAssemblies.Any(assemblyName =>
                assemblyName.Name == "CoCoFlow.Runtime.Core"));
            Assert.IsNull(inputAssembly.GetType(
                "CoCoFlow.Runtime.Modules.Input.InputRuntime"));
            Assert.IsNull(inputAssembly.GetType(
                "CoCoFlow.Runtime.Modules.Input.InputMapType"));

            string[] retiredCoreInputTypes =
            {
                "CoCoInputIntent",
                "IInputStateProvider",
                "IInputEventSource",
                "IInputModeController",
                "InputMapNames"
            };
            foreach (string retiredTypeName in retiredCoreInputTypes)
            {
                Assert.IsNull(coreAssembly.GetType(
                    $"CoCoFlow.Runtime.Core.{retiredTypeName}"),
                    retiredTypeName);
            }
        }
    }
}
