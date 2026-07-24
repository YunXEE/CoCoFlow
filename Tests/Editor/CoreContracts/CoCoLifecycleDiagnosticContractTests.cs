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

            Assert.AreEqual(15, (int)CoCoDiagnosticDomain.Operator);
            Assert.AreEqual(16, (int)CoCoDiagnosticDomain.EventOutbox);
            Assert.AreEqual(17, (int)CoCoDiagnosticDomain.Content);
            Assert.AreEqual(18, (int)CoCoDiagnosticDomain.Pooling);

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
                CoCoDiagnosticCode.CommitCancelled,
                CoCoDiagnosticCode.UnsupportedSchemaVersion,
                CoCoDiagnosticCode.MissingTopologyElement,
                CoCoDiagnosticCode.ParentStateCycle,
                CoCoDiagnosticCode.InvalidInitialState,
                CoCoDiagnosticCode.UnreachableState,
                CoCoDiagnosticCode.MissingDescriptor,
                CoCoDiagnosticCode.DescriptorTypeMismatch,
                CoCoDiagnosticCode.InvalidAuthoringDependency,
                CoCoDiagnosticCode.InvalidFrozenConfig,
                CoCoDiagnosticCode.ManifestConflict,
                CoCoDiagnosticCode.InvalidTransitionWindow
            };

            for (int index = 0; index < appendedCodes.Length; index++)
            {
                Assert.AreEqual(12 + index, (int)appendedCodes[index]);
            }

            Assert.IsFalse(Enum.IsDefined(typeof(CoCoDiagnosticCode), 49));
            Assert.AreEqual(50, (int)CoCoDiagnosticCode.NonLeafTransitionEndpoint);
            Assert.AreEqual(51, (int)CoCoDiagnosticCode.DuplicateTransitionPriority);
            Assert.AreEqual(52, (int)CoCoDiagnosticCode.MissingActionProgressProvider);
            Assert.AreEqual(53, (int)CoCoDiagnosticCode.ActivePathOperationOverlap);

            CoCoDiagnosticCode[] preFiveCodes =
            {
                CoCoDiagnosticCode.InvalidOperatorDescriptor,
                CoCoDiagnosticCode.MissingOperatorBinding,
                CoCoDiagnosticCode.OutcomeOwnershipConflict,
                CoCoDiagnosticCode.OperatorClaimConflict,
                CoCoDiagnosticCode.DuplicateOperatorActivation,
                CoCoDiagnosticCode.OperatorExecutionFailed,
                CoCoDiagnosticCode.EventOutboxOverflow,
                CoCoDiagnosticCode.EventSequenceExhausted,
                CoCoDiagnosticCode.EventPublishFailed,
                CoCoDiagnosticCode.WorldCorrectionRequired
            };
            for (int index = 0; index < preFiveCodes.Length; index++)
            {
                Assert.AreEqual(54 + index, (int)preFiveCodes[index]);
            }

            CoCoDiagnosticCode[] contextAuthorityCodes =
            {
                CoCoDiagnosticCode.InvalidContextProducer,
                CoCoDiagnosticCode.ContextCaptureFailed,
                CoCoDiagnosticCode.InvalidGraphRestore,
                CoCoDiagnosticCode.InvalidClaimRestore,
                CoCoDiagnosticCode.InvalidActorBinding
            };
            for (int index = 0; index < contextAuthorityCodes.Length; index++)
            {
                Assert.AreEqual(64 + index, (int)contextAuthorityCodes[index]);
            }

            CoCoDiagnosticCode[] contentCodes =
            {
                CoCoDiagnosticCode.InvalidContentId,
                CoCoDiagnosticCode.InvalidContentReference,
                CoCoDiagnosticCode.MissingContentBackend,
                CoCoDiagnosticCode.ContentTypeMismatch,
                CoCoDiagnosticCode.ContentLoadFailed,
                CoCoDiagnosticCode.ContentRequestCancelled,
                CoCoDiagnosticCode.ContentScopeDisposed,
                CoCoDiagnosticCode.ContentReleaseFailed,
                CoCoDiagnosticCode.ContentRuntimeDisposed,
                CoCoDiagnosticCode.ContentReferenceConflict,
                CoCoDiagnosticCode.ContentMainThreadRequired,
                CoCoDiagnosticCode.ContentBackendConflict
            };
            for (int index = 0; index < contentCodes.Length; index++)
            {
                Assert.AreEqual(69 + index, (int)contentCodes[index]);
            }

            CoCoDiagnosticCode[] poolingCodes =
            {
                CoCoDiagnosticCode.InvalidPoolId,
                CoCoDiagnosticCode.InvalidPoolProfile,
                CoCoDiagnosticCode.PoolProfileConflict,
                CoCoDiagnosticCode.PoolRuntimeDisposed,
                CoCoDiagnosticCode.PoolScopeClosing,
                CoCoDiagnosticCode.PoolNotReady,
                CoCoDiagnosticCode.PoolOperationInProgress,
                CoCoDiagnosticCode.PoolOperationCancelled,
                CoCoDiagnosticCode.PoolInstanceCreateFailed,
                CoCoDiagnosticCode.InvalidPooledHandle,
                CoCoDiagnosticCode.PooledHandleAlreadyReturned,
                CoCoDiagnosticCode.StalePooledHandle,
                CoCoDiagnosticCode.PooledHandleOwnerMismatch,
                CoCoDiagnosticCode.InvalidPoolTransition,
                CoCoDiagnosticCode.PoolActivationFailed,
                CoCoDiagnosticCode.PoolResetFailed,
                CoCoDiagnosticCode.PooledInstanceDestroyed,
                CoCoDiagnosticCode.PoolMainThreadRequired,
                CoCoDiagnosticCode.PoolCallbackReentry,
                CoCoDiagnosticCode.PoolHandleLeak,
                CoCoDiagnosticCode.PoolForcedShutdown,
                CoCoDiagnosticCode.PoolTemporalConflict,
                CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                CoCoDiagnosticCode.PoolTemporalCleanupFailed
            };
            for (int index = 0; index < poolingCodes.Length; index++)
            {
                Assert.AreEqual(81 + index, (int)poolingCodes[index]);
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
        public void WarningAndInfoDiagnosticsPreserveSeverityAndStructure()
        {
            CoCoDiagnostic warning = CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Topology,
                CoCoDiagnosticCode.UnreachableState,
                "State is unreachable from the Layer entry.");
            CoCoDiagnostic information = CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.Topology,
                CoCoDiagnosticCode.UnreachableState,
                "State reachability was inspected.");

            Assert.IsTrue(warning.IsWarning);
            Assert.IsFalse(warning.IsError);
            Assert.AreEqual(CoCoDiagnosticSeverity.Warning, warning.Severity);
            Assert.AreEqual(CoCoDiagnosticDomain.Topology, warning.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.UnreachableState, warning.Code);
            Assert.AreEqual(CoCoDiagnosticSeverity.Information, information.Severity);
            Assert.IsFalse(information.IsWarning);
            Assert.IsFalse(information.IsError);
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

            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.None,
                CoCoDiagnosticCode.UnreachableState,
                "Missing warning domain."));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Topology,
                CoCoDiagnosticCode.None,
                "Missing warning code."));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.None,
                CoCoDiagnosticCode.UnreachableState,
                "Missing information domain."));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.Topology,
                CoCoDiagnosticCode.None,
                "Missing information code."));
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
