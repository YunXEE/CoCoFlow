using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraphHost;
using NUnit.Framework;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class LifecycleContextTests
    {
        [Test]
        public void LifecycleRejectsDestroyedToActive()
        {
            var lifecycle = new LifecycleContext();

            Assert.IsTrue(lifecycle.TryTransitionTo(CoCoLifecycleState.Active));
            Assert.IsTrue(lifecycle.TryTransitionTo(CoCoLifecycleState.Destroyed));
            Assert.IsFalse(lifecycle.TryTransitionTo(CoCoLifecycleState.Active));
            Assert.Throws<InvalidOperationException>(
                () => lifecycle.TransitionTo(CoCoLifecycleState.Active));
        }
    }
}
