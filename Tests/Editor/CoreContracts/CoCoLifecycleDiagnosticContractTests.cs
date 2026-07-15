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
        [TestCase(CoCoRuntimeLifecycleState.Created, CoCoRuntimeLifecycleState.Disposed)]
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
        public void DiagnosticEnumsPreservePreOneValuesAndAppendStateFlowVocabulary()
        {
            Assert.AreEqual(8, (int)CoCoDiagnosticDomain.Operation);
            CoCoDiagnosticDomain[] appendedDomains =
            {
                CoCoDiagnosticDomain.Frame,
                CoCoDiagnosticDomain.Registry,
                CoCoDiagnosticDomain.Intent,
                CoCoDiagnosticDomain.Mailbox,
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticDomain.Codec
            };

            for (int index = 0; index < appendedDomains.Length; index++)
            {
                Assert.AreEqual(9 + index, (int)appendedDomains[index]);
            }

            Assert.AreEqual(11, (int)CoCoDiagnosticCode.InvalidTimelinePosition);
            CoCoDiagnosticCode[] appendedCodes =
            {
                CoCoDiagnosticCode.InvalidFrameLayout,
                CoCoDiagnosticCode.InvalidFrameHandle,
                CoCoDiagnosticCode.InvalidStateBlock,
                CoCoDiagnosticCode.InvalidStateSlot,
                CoCoDiagnosticCode.RegistryFrozen,
                CoCoDiagnosticCode.RegistryNotFrozen,
                CoCoDiagnosticCode.InvalidOperationSection,
                CoCoDiagnosticCode.MissingOperationSection,
                CoCoDiagnosticCode.InvalidIntentDescriptor,
                CoCoDiagnosticCode.MissingIntentReducer,
                CoCoDiagnosticCode.InvalidIntentContribution,
                CoCoDiagnosticCode.InvalidEventPacket,
                CoCoDiagnosticCode.EventDomainMismatch,
                CoCoDiagnosticCode.EventTargetMismatch,
                CoCoDiagnosticCode.UndeclaredBroadcast,
                CoCoDiagnosticCode.DuplicateEventPacket,
                CoCoDiagnosticCode.StaleTimelineEpoch,
                CoCoDiagnosticCode.EventSequenceConflict,
                CoCoDiagnosticCode.MailboxOverflow,
                CoCoDiagnosticCode.MailboxUnavailable,
                CoCoDiagnosticCode.InvalidRestoreMetadata,
                CoCoDiagnosticCode.DerivedDependencyCycle,
                CoCoDiagnosticCode.UnknownCodec,
                CoCoDiagnosticCode.UnsupportedCodecVersion,
                CoCoDiagnosticCode.CommitPreparationFailed,
                CoCoDiagnosticCode.CommitCancelled
            };

            for (int index = 0; index < appendedCodes.Length; index++)
            {
                Assert.AreEqual(12 + index, (int)appendedCodes[index]);
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

        [Test]
        public void ErrorDiagnosticRejectsNoneAndUndefinedStructure()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.None,
                CoCoDiagnosticCode.CrossLayerReference,
                "Missing domain."));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Error(
                (CoCoDiagnosticDomain)int.MaxValue,
                CoCoDiagnosticCode.CrossLayerReference,
                "Undefined domain."));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Topology,
                CoCoDiagnosticCode.None,
                "Missing code."));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Topology,
                (CoCoDiagnosticCode)int.MaxValue,
                "Undefined code."));
        }

        private static bool IsFrozenLifecycleEdge(
            CoCoRuntimeLifecycleState currentState,
            CoCoRuntimeLifecycleState nextState)
        {
            return currentState == CoCoRuntimeLifecycleState.Created &&
                   nextState == CoCoRuntimeLifecycleState.Running ||
                   currentState == CoCoRuntimeLifecycleState.Created &&
                   nextState == CoCoRuntimeLifecycleState.Disposed ||
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
