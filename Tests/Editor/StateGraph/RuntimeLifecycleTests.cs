using System;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class RuntimeLifecycleTests
    {
        [Test]
        public void RuntimeLifecycleVocabularyIsExact()
        {
            string[] names = Enum.GetNames(typeof(RuntimeLifecycleState));
            string[] expected = { "Created", "Running", "Suspended", "Stopped", "Disposed" };

            CollectionAssert.AreEqual(expected, names);
            Assert.AreEqual(0, (int)RuntimeLifecycleState.Created);
            Assert.AreEqual(1, (int)RuntimeLifecycleState.Running);
            Assert.AreEqual(2, (int)RuntimeLifecycleState.Suspended);
            Assert.AreEqual(3, (int)RuntimeLifecycleState.Stopped);
            Assert.AreEqual(4, (int)RuntimeLifecycleState.Disposed);
        }

        [TestCase(RuntimeLifecycleState.Created, RuntimeLifecycleState.Running)]
        [TestCase(RuntimeLifecycleState.Created, RuntimeLifecycleState.Disposed)]
        [TestCase(RuntimeLifecycleState.Running, RuntimeLifecycleState.Suspended)]
        [TestCase(RuntimeLifecycleState.Suspended, RuntimeLifecycleState.Running)]
        [TestCase(RuntimeLifecycleState.Running, RuntimeLifecycleState.Stopped)]
        [TestCase(RuntimeLifecycleState.Suspended, RuntimeLifecycleState.Stopped)]
        [TestCase(RuntimeLifecycleState.Stopped, RuntimeLifecycleState.Disposed)]
        public void RuntimeLifecycleAllowsOnlyFrozenForwardEdges(
            RuntimeLifecycleState currentState,
            RuntimeLifecycleState nextState)
        {
            Assert.IsTrue(currentState.CanTransitionTo(nextState));
        }

        [Test]
        public void RuntimeLifecycleRejectsEveryOtherDeclaredStatePair()
        {
            var states = (RuntimeLifecycleState[])Enum.GetValues(
                typeof(RuntimeLifecycleState));

            foreach (RuntimeLifecycleState currentState in states)
            {
                foreach (RuntimeLifecycleState nextState in states)
                {
                    if (IsFrozenLifecycleEdge(currentState, nextState)) continue;

                    Assert.IsFalse(
                        currentState.CanTransitionTo(nextState),
                        $"Unexpected lifecycle edge: {currentState} -> {nextState}");
                }
            }
        }

        [Test]
        public void RuntimeLifecycleRejectsUndefinedStateValues()
        {
            var undefinedState = (RuntimeLifecycleState)int.MaxValue;

            Assert.IsFalse(undefinedState.CanTransitionTo(RuntimeLifecycleState.Created));
            Assert.IsFalse(RuntimeLifecycleState.Created.CanTransitionTo(undefinedState));
        }

        private static bool IsFrozenLifecycleEdge(
            RuntimeLifecycleState currentState,
            RuntimeLifecycleState nextState)
        {
            return currentState == RuntimeLifecycleState.Created &&
                   nextState == RuntimeLifecycleState.Running ||
                   currentState == RuntimeLifecycleState.Created &&
                   nextState == RuntimeLifecycleState.Disposed ||
                   currentState == RuntimeLifecycleState.Running &&
                   nextState == RuntimeLifecycleState.Suspended ||
                   currentState == RuntimeLifecycleState.Suspended &&
                   nextState == RuntimeLifecycleState.Running ||
                   currentState == RuntimeLifecycleState.Running &&
                   nextState == RuntimeLifecycleState.Stopped ||
                   currentState == RuntimeLifecycleState.Suspended &&
                   nextState == RuntimeLifecycleState.Stopped ||
                   currentState == RuntimeLifecycleState.Stopped &&
                   nextState == RuntimeLifecycleState.Disposed;
        }
    }
}
