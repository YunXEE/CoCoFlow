using System;
using CoCoFlow.Runtime.StateGraphHost;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class LifecycleContextTests
    {
        [TestCase(0, EntityLifecycleState.Uninitialized)]
        [TestCase(1, EntityLifecycleState.Spawning)]
        [TestCase(2, EntityLifecycleState.Active)]
        [TestCase(3, EntityLifecycleState.Disabled)]
        [TestCase(4, EntityLifecycleState.Consumed)]
        [TestCase(5, EntityLifecycleState.Despawning)]
        [TestCase(6, EntityLifecycleState.Destroyed)]
        public void LifecycleReadsExistingSerializedState(int serializedState, EntityLifecycleState expected)
        {
            var lifecycle = JsonUtility.FromJson<LifecycleContext>($"{{\"state\":{serializedState}}}");

            Assert.That((int)expected, Is.EqualTo(serializedState));
            Assert.That(lifecycle.State, Is.EqualTo(expected));
        }

        [Test]
        public void LifecycleRejectsDestroyedToActive()
        {
            var lifecycle = new LifecycleContext();

            Assert.IsTrue(lifecycle.TryTransitionTo(EntityLifecycleState.Active));
            Assert.IsTrue(lifecycle.TryTransitionTo(EntityLifecycleState.Destroyed));
            Assert.IsFalse(lifecycle.TryTransitionTo(EntityLifecycleState.Active));
            Assert.Throws<InvalidOperationException>(
                () => lifecycle.TransitionTo(EntityLifecycleState.Active));
        }
    }
}
