using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;

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
        public void InputReaderIsCoreIntentSourceWithoutGameplayOrStateAuthority()
        {
            var inputReaderType = typeof(InputReader);
            var intentSourceType = typeof(ICoCoIntentSource<CoCoInputIntent>);
            var referencedAssemblies = inputReaderType.Assembly.GetReferencedAssemblies();

            Assert.IsTrue(intentSourceType.IsAssignableFrom(inputReaderType));
            Assert.IsNull(inputReaderType.GetMethod(
                "ChangeState",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic));

            foreach (var assemblyName in referencedAssemblies)
            {
                Assert.AreNotEqual(
                    "CoCoFlow.Runtime.Gameplay.Character",
                    assemblyName.Name,
                    "Input module must not depend on Character gameplay.");
            }

            var intent = new CoCoInputIntent
            {
                move = Vector2.right,
                look = Vector2.up,
                zoom = Vector2.one,
                performedAction = "Attack",
                canceledAction = "Aim",
                performedSequence = 10,
                canceledSequence = 4
            };

            intent.ClearDiscrete();
            Assert.AreEqual(Vector2.right, intent.move);
            Assert.AreEqual(string.Empty, intent.performedAction);
            Assert.AreEqual(string.Empty, intent.canceledAction);

            intent.Clear();
            Assert.AreEqual(Vector2.zero, intent.move);
            Assert.AreEqual(0, intent.performedSequence);
            Assert.AreEqual(0, intent.canceledSequence);
        }
    }
}
