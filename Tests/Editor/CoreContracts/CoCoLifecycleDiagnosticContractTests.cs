using System;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoLifecycleDiagnosticContractTests
    {
        [Test]
        public void RuntimeLifecycleVocabularyIsExact()
        {
            string[] names = Enum.GetNames(typeof(CoCoRuntimeLifecycleState));
            string[] expected = { "Created", "Running", "Suspended", "Stopped", "Disposed" };

            CollectionAssert.AreEqual(expected, names);
            Assert.AreEqual(0, (int)CoCoRuntimeLifecycleState.Created);
            Assert.AreEqual(1, (int)CoCoRuntimeLifecycleState.Running);
            Assert.AreEqual(2, (int)CoCoRuntimeLifecycleState.Suspended);
            Assert.AreEqual(3, (int)CoCoRuntimeLifecycleState.Stopped);
            Assert.AreEqual(4, (int)CoCoRuntimeLifecycleState.Disposed);
        }

        [TestCase(CoCoRuntimeLifecycleState.Created, CoCoRuntimeLifecycleState.Running)]
        [TestCase(CoCoRuntimeLifecycleState.Running, CoCoRuntimeLifecycleState.Suspended)]
        [TestCase(CoCoRuntimeLifecycleState.Suspended, CoCoRuntimeLifecycleState.Running)]
        [TestCase(CoCoRuntimeLifecycleState.Running, CoCoRuntimeLifecycleState.Stopped)]
        [TestCase(CoCoRuntimeLifecycleState.Suspended, CoCoRuntimeLifecycleState.Stopped)]
        [TestCase(CoCoRuntimeLifecycleState.Stopped, CoCoRuntimeLifecycleState.Disposed)]
        public void RuntimeLifecycleAllowsOnlyFrozenForwardEdges(
            CoCoRuntimeLifecycleState currentState,
            CoCoRuntimeLifecycleState nextState)
        {
            Assert.IsTrue(currentState.CanTransitionTo(nextState));
        }

        [Test]
        public void RuntimeLifecycleRejectsEveryOtherDeclaredStatePair()
        {
            var states = (CoCoRuntimeLifecycleState[])Enum.GetValues(
                typeof(CoCoRuntimeLifecycleState));

            foreach (CoCoRuntimeLifecycleState currentState in states)
            {
                foreach (CoCoRuntimeLifecycleState nextState in states)
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
            var undefinedState = (CoCoRuntimeLifecycleState)int.MaxValue;

            Assert.IsFalse(undefinedState.CanTransitionTo(CoCoRuntimeLifecycleState.Created));
            Assert.IsFalse(CoCoRuntimeLifecycleState.Created.CanTransitionTo(undefinedState));
        }

        [Test]
        public void DiagnosticCodesCoverFrozenContractFailures()
        {
            CoCoDiagnosticCode[] requiredCodes =
            {
                CoCoDiagnosticCode.InvalidIdentifier,
                CoCoDiagnosticCode.DuplicateIdentifier,
                CoCoDiagnosticCode.CrossLayerReference,
                CoCoDiagnosticCode.NonPositiveDeltaTime,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                CoCoDiagnosticCode.MissingContext,
                CoCoDiagnosticCode.MissingOperationBinding,
                CoCoDiagnosticCode.IllegalPublicTopology
            };

            foreach (CoCoDiagnosticCode code in requiredCodes)
            {
                Assert.IsTrue(Enum.IsDefined(typeof(CoCoDiagnosticCode), code), code.ToString());
            }
        }

        [Test]
        public void DiagnosticPreservesStructuredErrorAndDefaultIsNone()
        {
            CoCoDiagnostic none = default;
            CoCoDiagnostic error = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Topology,
                CoCoDiagnosticCode.CrossLayerReference,
                "Transition crosses a Layer boundary.");

            Assert.IsTrue(none.IsNone);
            Assert.AreEqual(string.Empty, none.Message);
            Assert.IsFalse(error.IsNone);
            Assert.IsTrue(error.IsError);
            Assert.AreEqual(CoCoDiagnosticDomain.Topology, error.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.CrossLayerReference, error.Code);
        }

        private static bool IsFrozenLifecycleEdge(
            CoCoRuntimeLifecycleState currentState,
            CoCoRuntimeLifecycleState nextState)
        {
            return currentState == CoCoRuntimeLifecycleState.Created &&
                   nextState == CoCoRuntimeLifecycleState.Running ||
                   currentState == CoCoRuntimeLifecycleState.Running &&
                   nextState == CoCoRuntimeLifecycleState.Suspended ||
                   currentState == CoCoRuntimeLifecycleState.Suspended &&
                   nextState == CoCoRuntimeLifecycleState.Running ||
                   currentState == CoCoRuntimeLifecycleState.Running &&
                   nextState == CoCoRuntimeLifecycleState.Stopped ||
                   currentState == CoCoRuntimeLifecycleState.Suspended &&
                   nextState == CoCoRuntimeLifecycleState.Stopped ||
                   currentState == CoCoRuntimeLifecycleState.Stopped &&
                   nextState == CoCoRuntimeLifecycleState.Disposed;
        }
    }
}
