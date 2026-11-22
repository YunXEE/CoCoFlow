using System;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoLifecycleContextTests
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
    }
}
