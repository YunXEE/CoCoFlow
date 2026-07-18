using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostOperatorCommitPlayModeTests
    {
        private const ulong ContextDefaultFingerprint = 5051UL;
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            HostTestLogic.Reset();
            OperatorCommitClaimLogic.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
            HostTestLogic.Reset();
            OperatorCommitClaimLogic.Reset();
        }

        [Test]
        public void FirstTickReadsLayoutDefaultsAndCommitsRevisionOne()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);

            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(runtimeOperator.PreviousHadCommittedFrame, Is.False);
            Assert.That(runtimeOperator.PreviousValue, Is.EqualTo(5));
            Assert.That(host.CurrentContext.IsAlive, Is.True);
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(host.CurrentContext.Header.TickFrame.Tick.Value, Is.EqualTo(1UL));
            Require(host.CurrentContext.Layout.TryResolveSlot(
                ids.StateSlotId,
                out CoCoStateSlot<int> slot));
            Assert.That(host.CurrentContext.Read(slot), Is.EqualTo(6));
        }

        [Test]
        public void OperatorFailureAfterWorldMutationKeepsOldAuthorityAndMarksCorrection()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            runtimeOperator.FailAfterWorldMutation = true;
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);

            Assert.That(failure.IsError, Is.True);
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(gameObject.transform.localPosition.x, Is.EqualTo(17f));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.Zero);
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.RequiresWorldCorrection, Is.True);
        }

        [Test]
        public void IgnoredUnauthorizedOutcomeWriteFaultsAndPreservesCommittedRevision()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame committed = host.CurrentContext;
            runtimeOperator.AttemptUnauthorizedOutcome = true;

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            Assert.That(host.CurrentContext, Is.EqualTo(committed));
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(host.Fault.IsFaulted, Is.True);
        }

        [Test]
        public void PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);
            var listener = new PublishLifecycleListener(host);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventB>>(listener);
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);
            }
            finally
            {
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventB>>(listener);
            }

            CollectionAssert.AreEqual(new[] { "A", "B" }, listener.Order);
            CollectionAssert.AreEqual(new[] { 1UL, 2UL }, listener.Sequences);
            Assert.That(listener.ObservedRevision, Is.EqualTo(1UL));
            Assert.That(listener.ObservedTick, Is.EqualTo(1UL));
            Assert.That(listener.ReentrantStepAccepted, Is.False);
            Assert.That(listener.StopAccepted, Is.True);
            Assert.That(listener.DisposeAccepted, Is.True);
            Assert.That(listener.LifecycleDuringPublish, Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Disposed));
        }

        [Test]
        public void IgnoredInvalidOutboxWriteFaultsAndPublishesNothing()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            runtimeOperator.AttemptInvalidTarget = true;
            SetOperators(host, runtimeOperator);
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            }
            finally
            {
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            }

            Assert.That(listener.Order, Is.Empty);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.Zero);
            Assert.That(host.Fault.IsFaulted, Is.True);
        }

        [Test]
        public void StartupRejectsGlobalOutboxCapacityBelowUniqueLaneReservation()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 3);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.EventOutbox));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.EventOutboxOverflow));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
        }

        [Test]
        public void ExactCustomContextCodecIsFrozenIntoRuntimeBindings()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, CodecBindingMode.ExactCustom);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            CoCoContextCodecRegistry codecs = GetBindings(host).ContextCodecs;
            Assert.That(codecs.IsFrozen, Is.True);
            Assert.That(codecs.Count, Is.EqualTo(1));
            Assert.That(codecs.TryResolve(
                provider.ManifestCodec,
                out ICoCoContextValueCodec<int> resolved,
                out CoCoDiagnosticCode diagnosticCode), Is.True, diagnosticCode.ToString());
            Assert.That(resolved, Is.SameAs(provider.BoundCodec));
        }

        [TestCase(CodecBindingMode.MissingCustom)]
        [TestCase(CodecBindingMode.MismatchedCustom)]
        [TestCase(CodecBindingMode.ExtraOnRaw)]
        public void ContextCodecBindingMustExactlyMatchManifest(CodecBindingMode mode)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, mode);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(host, runtimeOperator);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Registry));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
        }

        [Test]
        public void OperatorListRejectsInvalidEntriesDuplicateIdsAndNestedHostCrossing()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);

            CoCoStateGraphHost nullHost = CreateHost(ids, out GameObject nullObject);
            SetOperators(nullHost, (MonoBehaviour)null);
            AssertStartupOperatorFailure(nullHost);

            CoCoStateGraphHost nonOperatorHost = CreateHost(ids, out GameObject nonOperatorObject);
            SetOperators(nonOperatorHost, nonOperatorHost);
            AssertStartupOperatorFailure(nonOperatorHost);

            CoCoStateGraphHost duplicateHost = CreateHost(ids, out GameObject duplicateObject);
            var duplicate = duplicateObject.AddComponent<ContextWritingOperator>();
            duplicate.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(duplicateHost, duplicate, duplicate);
            AssertStartupOperatorFailure(duplicateHost);

            CoCoStateGraphHost destroyedHost = CreateHost(ids, out GameObject destroyedObject);
            var destroyed = destroyedObject.AddComponent<ContextWritingOperator>();
            destroyed.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(destroyedHost, destroyed);
            UnityEngine.Object.DestroyImmediate(destroyed);
            AssertStartupOperatorFailure(destroyedHost);

            CoCoStateGraphHost duplicateIdHost = CreateHost(ids, out GameObject duplicateIdObject);
            var owner = duplicateIdObject.AddComponent<ContextWritingOperator>();
            owner.Configure(ids.FirstOperatorId, ids.StateSlotId);
            var duplicateId = duplicateIdObject.AddComponent<DuplicateIdOperator>();
            duplicateId.Configure(ids.FirstOperatorId);
            SetOperators(duplicateIdHost, owner, duplicateId);
            AssertStartupOperatorFailure(duplicateIdHost);

            CoCoStateGraphHost parentHost = CreateHost(ids, out GameObject parentObject);
            var nestedObject = new GameObject("Nested Host Boundary");
            _objects.Add(nestedObject);
            nestedObject.transform.SetParent(parentObject.transform);
            nestedObject.AddComponent<CoCoStateGraphHost>();
            var nestedOperator = nestedObject.AddComponent<ContextWritingOperator>();
            nestedOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(parentHost, nestedOperator);
            AssertStartupOperatorFailure(parentHost);
        }

        [Test]
        public void OperatorRequiresMustExactlyCoverCompiledOperationProvides()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost extraHost = CreateHost(ids, out GameObject extraObject);
            var extra = extraObject.AddComponent<ExtraneousRequirementOperator>();
            extra.Configure(ids);
            SetOperators(extraHost, extra);
            Assert.That(extraHost.TryStart(out CoCoDiagnostic extraFailure), Is.False);
            Assert.That(extraFailure.Code, Is.EqualTo(CoCoDiagnosticCode.MissingOperatorBinding));

            CoCoStateGraphProjectBindings.ResetForTests();
            InstallClaimProvider(ids);
            CoCoStateGraphHost missingHost = CreateClaimHost(ids, out GameObject missingObject, 0);
            var low = missingObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            SetOperators(missingHost, low);
            Assert.That(missingHost.TryStart(out CoCoDiagnostic missingFailure), Is.False);
            Assert.That(missingFailure.Code, Is.EqualTo(CoCoDiagnosticCode.MissingOperatorBinding));
        }

        [Test]
        public void ClaimDeniedIsRecordedBeforeCallbacksAndDisabledSectionUsesExplicitNoOp()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            var noOp = gameObject.AddComponent<NoOpSecondaryOperator>();
            noOp.Configure(ids);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            SetOperators(host, high, noOp, low);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);

            Assert.That(high.ExecuteCount, Is.Zero);
            Assert.That(noOp.ExecuteCount, Is.EqualTo(1));
            Assert.That(noOp.SawDisabledSection, Is.True);
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            var outcomes = new CoCoStateFlowTraceEntry[4];
            int count = host.Trace.CopyLatestTo(
                outcomes,
                new CoCoStateFlowTraceFilter(CoCoStateFlowTraceKind.OperatorOutcome));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(outcomes[0].OperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.ClaimDenied));
            Assert.That(
                outcomes[1].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.NoOp));
            Assert.That(
                outcomes[2].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.Succeeded));
        }

        [Test]
        public void ClaimDeniedIsMaterializedBeforeEarlierWinnerCallbackCanFail()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 16);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            low.FailExecution = true;
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(
                failure.Code,
                Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.Zero);

            var outcomes = new CoCoStateFlowTraceEntry[4];
            int count = host.Trace.CopyLatestTo(
                outcomes,
                new CoCoStateFlowTraceFilter(CoCoStateFlowTraceKind.OperatorOutcome));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(outcomes[0].OperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.ClaimDenied));
        }

        [Test]
        public void RestoreCompatibilityValidationIsPureReadOnlyHostSeam()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, traceCapacity: 16);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);
            CoCoContextFrame current = host.CurrentContext;
            CoCoTickFrame sourceTick = current.Header.TickFrame;
            Require(CoCoTimelinePosition.TryCreate(
                sourceTick.TimelinePosition.Seconds + 1d,
                out CoCoTimelinePosition resumedPosition));
            Require(CoCoTickFrame.TryCreate(
                0.02d,
                sourceTick.TimelineId,
                resumedPosition,
                new CoCoTimelineTick(sourceTick.Tick.Value + 1UL),
                sourceTick.ClockDomainId,
                new CoCoExecutionSequence(sourceTick.ExecutionSequence.Value + 1UL),
                new CoCoTimelineEpoch(sourceTick.TimelineEpoch.Value + 1UL),
                out CoCoTickFrame resumed,
                out CoCoDiagnostic frame), frame);
            ulong traceCount = host.Trace.TotalWritten;

            Assert.That(host.TryValidateRestore(
                current,
                resumed,
                out CoCoContextCommitStatus status), Is.True);
            Assert.That(status, Is.EqualTo(CoCoContextCommitStatus.None));
            Assert.That(host.TryValidateRestore(
                default,
                resumed,
                out CoCoContextCommitStatus invalidStatus), Is.False);
            Assert.That(invalidStatus, Is.EqualTo(CoCoContextCommitStatus.LayoutMismatch));
            Assert.That(host.CurrentContext, Is.EqualTo(current));
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Running));
        }

        [Test]
        public void TraceOverwritesInOrderAndFiltersCommittedPathAndOperator()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(ids.FirstOperatorId, ids.StateSlotId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);

            ICoCoStateFlowTrace trace = host.Trace;
            Assert.That(trace, Is.Not.Null);
            Assert.That(trace.Capacity, Is.EqualTo(4));
            Assert.That(trace.Count, Is.EqualTo(4));
            Assert.That(trace.TotalWritten, Is.EqualTo(8UL));
            var latest = new CoCoStateFlowTraceEntry[4];
            Assert.That(trace.CopyLatestTo(latest), Is.EqualTo(4));
            CollectionAssert.AreEqual(
                new[]
                {
                    CoCoStateFlowTraceKind.Tick,
                    CoCoStateFlowTraceKind.OperatorOutcome,
                    CoCoStateFlowTraceKind.ActivePath,
                    CoCoStateFlowTraceKind.ContextCommit
                },
                Array.ConvertAll(latest, entry => entry.Kind));
            Assert.That(latest[0].TickFrame.Tick.Value, Is.EqualTo(2UL));

            var path = new CoCoStateFlowTraceEntry[2];
            int pathCount = trace.CopyLatestTo(
                path,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.ActivePath,
                    layerId: ids.LayerId));
            Assert.That(pathCount, Is.EqualTo(1));
            Assert.That(path[0].LayerId, Is.EqualTo(ids.LayerId));
            Assert.That(path[0].StateId, Is.EqualTo(ids.StateId));

            var outcomes = new CoCoStateFlowTraceEntry[2];
            int outcomeCount = trace.CopyLatestTo(
                outcomes,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.OperatorOutcome,
                    operatorId: ids.FirstOperatorId));
            Assert.That(outcomeCount, Is.EqualTo(1));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.Succeeded));
        }

        [Test]
        public void CommittedClaimRetainsSameActivationThenReArbitratesAfterActivationChange()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 64);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.Zero);

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic retained), retained);
            Assert.That(low.ExecuteCount, Is.EqualTo(2));
            Assert.That(high.ExecuteCount, Is.Zero);

            OperatorCommitClaimLogic.RequestTransition = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic transition), transition);
            Assert.That(low.ExecuteCount, Is.EqualTo(3));
            Assert.That(high.ExecuteCount, Is.Zero);
            OperatorCommitClaimLogic.RequestTransition = false;

            Require(host.TryStep(0.02d, out CoCoDiagnostic changed), changed);
            Assert.That(low.ExecuteCount, Is.EqualTo(3));
            Assert.That(high.ExecuteCount, Is.EqualTo(1));

            var entries = new CoCoStateFlowTraceEntry[8];
            int transitionCount = host.Trace.CopyLatestTo(
                entries,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.Transition,
                    layerId: ids.LayerId));
            Assert.That(transitionCount, Is.EqualTo(1));
            Assert.That(entries[0].TransitionId, Is.EqualTo(ids.TransitionId));
            int primarySections = host.Trace.CopyLatestTo(
                entries,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.OperationSection,
                    sectionId: ids.PrimarySectionId));
            int secondarySections = host.Trace.CopyLatestTo(
                entries,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.OperationSection,
                    sectionId: ids.SecondarySectionId));
            Assert.That(primarySections, Is.EqualTo(4));
            Assert.That(secondarySections, Is.EqualTo(4));
        }

        [TestCase(CoCoOperatorClaimSuspendPolicy.Retain, 2, 0)]
        [TestCase(CoCoOperatorClaimSuspendPolicy.Release, 1, 1)]
        public void SuspendPolicyPreservesOrReleasesCompleteClaimSet(
            CoCoOperatorClaimSuspendPolicy suspendPolicy,
            int expectedLowExecutions,
            int expectedHighExecutions)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, suspendPolicy);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);

            Assert.That(low.ExecuteCount, Is.EqualTo(expectedLowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(expectedHighExecutions));
        }

        private void InstallProvider(OperatorCommitTestIds ids)
        {
            var provider = new OperatorCommitBindingProvider(ids);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
        }

        private void InstallClaimProvider(OperatorCommitTestIds ids)
        {
            var provider = new ClaimBindingProvider(ids);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
        }

        private CoCoStateGraphHost CreateHost(
            OperatorCommitTestIds ids,
            out GameObject gameObject,
            int eventOutboxCapacity = 0,
            int traceCapacity = 0)
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(ids.StateId.High, ids.StateId.Low),
                default,
                "Operator Commit Leaf",
                new CoCoSerializedId128(ids.StateDescriptorId.High, ids.StateDescriptorId.Low),
                new HostTestStateConfig { Value = 5 });
            var layer = new CoCoStateGraphLayerRecord(
                new CoCoSerializedId128(ids.LayerId.High, ids.LayerId.Low),
                "Base");
            layer.InitialStateId = new CoCoSerializedId128(ids.StateId.High, ids.StateId.Low);
            layer.States.Add(state);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Operator Commit Host");
            _objects.Add(gameObject);
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 3);
            SetField(host, "eventOutboxCapacity", eventOutboxCapacity);
            SetField(host, "traceCapacity", traceCapacity);
            return host;
        }

        private CoCoStateGraphHost CreateClaimHost(
            OperatorCommitTestIds ids,
            out GameObject gameObject,
            int traceCapacity)
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var firstState = new CoCoStateGraphStateRecord(
                Serialize(ids.StateId),
                default,
                "Claim Source",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var secondState = new CoCoStateGraphStateRecord(
                Serialize(ids.SecondStateId),
                default,
                "Claim Target",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 2 });
            var transition = new CoCoStateGraphTransitionRecord(
                Serialize(ids.TransitionId),
                Serialize(ids.StateId),
                Serialize(ids.SecondStateId),
                1);
            var layer = new CoCoStateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.StateId);
            layer.States.Add(firstState);
            layer.States.Add(secondState);
            layer.Transitions.Add(transition);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Claim Host");
            _objects.Add(gameObject);
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 3);
            SetField(host, "eventOutboxCapacity", 0);
            SetField(host, "traceCapacity", traceCapacity);
            return host;
        }

        private static CoCoSerializedId128 Serialize(CoCoLayerId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoTransitionId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoStateGraphRuntime GetRuntime(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphRuntime)field.GetValue(host);
        }

        private static CoCoStateGraphHostRuntimeBindings GetBindings(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphHostRuntimeBindings)field.GetValue(host);
        }

        private static void SetOperators(CoCoStateGraphHost host, params MonoBehaviour[] operators) =>
            SetField(host, "operators", operators);

        private static void SetField<TValue>(
            CoCoStateGraphHost host,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(host, value);
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }

        private static void AssertStartupOperatorFailure(CoCoStateGraphHost host)
        {
            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidOperatorDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
        }

        private sealed class OperatorCommitBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private readonly OperatorCommitTestIds _ids;

            public OperatorCommitBindingProvider(OperatorCommitTestIds ids)
            {
                _ids = ids;
                Catalog = BuildCatalog(ids);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                const int defaultValue = 5;
                if (!builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        out diagnostic))
                {
                    return false;
                }

                var stateFactory = new CoCoStateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context => new HostTestLogic(context.GraphInstanceId),
                    () => new HostTestMemory(),
                    (source, destination) => destination.Value = source.Value,
                    memory => memory.Value = 0,
                    HostTestLogic.GetMemoryFingerprint);
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static CoCoGraphDescriptorCatalog BuildCatalog(OperatorCommitTestIds ids)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    CoCoStateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    null,
                    null,
                    new[] { ids.StateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        public enum CodecBindingMode
        {
            ExactCustom = 0,
            MissingCustom = 1,
            MismatchedCustom = 2,
            ExtraOnRaw = 3
        }

        private sealed class CodecBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private readonly OperatorCommitTestIds _ids;
            private readonly CodecBindingMode _mode;

            public CodecBindingProvider(OperatorCommitTestIds ids, CodecBindingMode mode)
            {
                _ids = ids;
                _mode = mode;
                if (!CoCoCodecId.TryCreate(520UL, 1UL, out CoCoCodecId manifestCodecId) ||
                    !CoCoCodecId.TryCreate(520UL, 2UL, out CoCoCodecId mismatchCodecId))
                {
                    throw new InvalidOperationException("Context codec fixture ids are invalid.");
                }

                ManifestCodec = mode == CodecBindingMode.ExtraOnRaw
                    ? default
                    : new CoCoCodecDescriptor(manifestCodecId, 1U);
                CoCoCodecDescriptor boundDescriptor = mode == CodecBindingMode.MismatchedCustom
                    ? new CoCoCodecDescriptor(mismatchCodecId, 1U)
                    : new CoCoCodecDescriptor(manifestCodecId, 1U);
                BoundCodec = mode == CodecBindingMode.MissingCustom
                    ? null
                    : new Int32ContextCodec(boundDescriptor);
                Catalog = BuildCatalog(ids, ManifestCodec);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }
            public CoCoCodecDescriptor ManifestCodec { get; }
            public Int32ContextCodec BoundCodec { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                const int defaultValue = 5;
                bool bound = _mode == CodecBindingMode.MissingCustom
                    ? builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        out diagnostic)
                    : builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        BoundCodec,
                        out diagnostic);
                if (!bound)
                {
                    return false;
                }

                var stateFactory = new CoCoStateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context => new HostTestLogic(context.GraphInstanceId),
                    () => new HostTestMemory(),
                    (source, destination) => destination.Value = source.Value,
                    memory => memory.Value = 0,
                    HostTestLogic.GetMemoryFingerprint);
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static CoCoGraphDescriptorCatalog BuildCatalog(
                OperatorCommitTestIds ids,
                CoCoCodecDescriptor codec)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    CoCoStateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    codec,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    null,
                    null,
                    new[] { ids.StateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        public sealed class Int32ContextCodec : ICoCoContextValueCodec<int>
        {
            public Int32ContextCodec(CoCoCodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CoCoCodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;

            public bool TryEncode(
                in int value,
                Span<byte> destination,
                out int bytesWritten)
            {
                if (destination.Length < 4)
                {
                    bytesWritten = 0;
                    return false;
                }

                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                destination[3] = (byte)(value >> 24);
                bytesWritten = 4;
                return true;
            }

            public bool TryDecode(
                ReadOnlySpan<byte> source,
                out int value,
                out int bytesRead)
            {
                if (source.Length < 4)
                {
                    value = 0;
                    bytesRead = 0;
                    return false;
                }

                value = source[0] |
                        (source[1] << 8) |
                        (source[2] << 16) |
                        (source[3] << 24);
                bytesRead = 4;
                return true;
            }
        }

        private sealed class ClaimBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private const ulong PrimaryFactoryFingerprint = 5091UL;
            private const ulong SecondaryFactoryFingerprint = 5092UL;
            private readonly OperatorCommitTestIds _ids;
            private readonly OperatorCommitPrimarySectionFactory _primary =
                new OperatorCommitPrimarySectionFactory();
            private readonly OperatorCommitSecondarySectionFactory _secondary =
                new OperatorCommitSecondarySectionFactory();

            public ClaimBindingProvider(OperatorCommitTestIds ids)
            {
                _ids = ids;
                Catalog = BuildCatalog(ids);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                if (!builder.TryRegisterOperation(
                        _ids.PrimarySectionId,
                        CoCoOperationSectionMode.Discrete,
                        _primary,
                        PrimaryFactoryFingerprint,
                        out CoCoOperationSectionRequirement primary,
                        out diagnostic) ||
                    !builder.TryRegisterOperation(
                        _ids.SecondarySectionId,
                        CoCoOperationSectionMode.Discrete,
                        _secondary,
                        SecondaryFactoryFingerprint,
                        out CoCoOperationSectionRequirement secondary,
                        out diagnostic))
                {
                    return false;
                }

                const int defaultValue = 5;
                if (!builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        out diagnostic))
                {
                    return false;
                }

                var stateFactory = new CoCoStateRuntimeFactory<
                    OperatorCommitClaimLogic,
                    OperatorCommitClaimMemory>(
                    context => new OperatorCommitClaimLogic(
                        context,
                        _primary.Handle,
                        _primary.ValueField,
                        _secondary.Handle,
                        _secondary.ValueField),
                    () => new OperatorCommitClaimMemory(),
                    (source, destination) => { },
                    memory => { },
                    memory => 0UL);
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static CoCoGraphDescriptorCatalog BuildCatalog(OperatorCommitTestIds ids)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Require(builder.TryRegisterOperationSection(
                    ids.PrimarySectionId,
                    CoCoOperationSectionMode.Discrete,
                    new CoCoOperationSectionViewFactoryToken<
                        IOperatorCommitPrimarySection,
                        OperatorCommitPrimarySectionFactory>(PrimaryFactoryFingerprint),
                    out CoCoDiagnostic primary), primary);
                Require(builder.TryRegisterOperationSection(
                    ids.SecondarySectionId,
                    CoCoOperationSectionMode.Discrete,
                    new CoCoOperationSectionViewFactoryToken<
                        IOperatorCommitSecondarySection,
                        OperatorCommitSecondarySectionFactory>(SecondaryFactoryFingerprint),
                    out CoCoDiagnostic secondary), secondary);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    CoCoStateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        OperatorCommitClaimLogic,
                        HostTestStateConfigSchema,
                        OperatorCommitClaimMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId, ids.SecondarySectionId },
                    new[] { ids.StateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        private sealed class LowClaimOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperatorClaimSuspendPolicy _suspendPolicy;
            private CoCoOperationSectionRequirement _primary;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }

            public bool FailExecution { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic require = default;
                    CoCoDiagnostic claimDiagnostic = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _primary,
                            out require) ||
                        !builder.TryClaim(
                            _ids.PrimaryClaimId,
                            _primary,
                            1,
                            _suspendPolicy,
                            out CoCoOperatorClaimRequirement claim,
                            out claimDiagnostic) ||
                        !builder.TryOwnOutcome<int>(
                            _ids.StateSlotId,
                            out outcome) ||
                        !builder.TryFreeze<LowClaimOperator>(
                            _ids.FirstOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            FirstError(require, claimDiagnostic, outcome, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(
                OperatorCommitTestIds ids,
                CoCoOperatorClaimSuspendPolicy suspendPolicy)
            {
                _ids = ids;
                _suspendPolicy = suspendPolicy;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (FailExecution)
                {
                    outcome = default;
                    return false;
                }

                if (!context.TryGet(
                        _primary,
                        out CoCoOperationSectionEntry<IOperatorCommitPrimarySection> section) ||
                    !section.Header.Enabled ||
                    !context.PreviousContext.Layout.TryResolveSlot(
                        _ids.StateSlotId,
                        out CoCoStateSlot<int> slot) ||
                    !context.TryWriteOutcome(slot, context.PreviousContext.Read(slot) + 1))
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class HighClaimOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperationSectionRequirement _primary;
            private CoCoOperationSectionRequirement _secondary;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic primaryDiagnostic = default;
                    CoCoDiagnostic secondaryDiagnostic = default;
                    CoCoDiagnostic primaryClaimDiagnostic = default;
                    CoCoDiagnostic secondaryClaimDiagnostic = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _primary,
                            out primaryDiagnostic) ||
                        !builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _secondary,
                            out secondaryDiagnostic) ||
                        !builder.TryClaim(
                            _ids.PrimaryClaimId,
                            _primary,
                            100,
                            CoCoOperatorClaimSuspendPolicy.Retain,
                            out CoCoOperatorClaimRequirement primaryClaim,
                            out primaryClaimDiagnostic) ||
                        !builder.TryClaim(
                            _ids.SecondaryClaimId,
                            _secondary,
                            100,
                            CoCoOperatorClaimSuspendPolicy.Retain,
                            out CoCoOperatorClaimRequirement secondaryClaim,
                            out secondaryClaimDiagnostic) ||
                        !builder.TryFreeze<HighClaimOperator>(
                            _ids.SecondOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(FirstError(
                            primaryDiagnostic,
                            secondaryDiagnostic,
                            primaryClaimDiagnostic,
                            secondaryClaimDiagnostic,
                            freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(OperatorCommitTestIds ids)
            {
                _ids = ids;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.TryGet(
                        _primary,
                        out CoCoOperationSectionEntry<IOperatorCommitPrimarySection> primary) ||
                    !context.TryGet(
                        _secondary,
                        out CoCoOperationSectionEntry<IOperatorCommitSecondarySection> secondary) ||
                    !primary.Header.Enabled ||
                    !secondary.Header.Enabled)
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class DuplicateIdOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoOperatorId _operatorId;
            private CoCoOperatorDescriptor _descriptor;

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor == null)
                    {
                        var builder = new CoCoOperatorDescriptorBuilder();
                        if (!builder.TryFreeze<DuplicateIdOperator>(
                                _operatorId,
                                out _descriptor,
                                out CoCoDiagnostic diagnostic))
                        {
                            throw new InvalidOperationException(diagnostic.Message);
                        }
                    }

                    return _descriptor;
                }
            }

            public void Configure(CoCoOperatorId operatorId) => _operatorId = operatorId;

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class ExtraneousRequirementOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperatorDescriptor _descriptor;

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out CoCoOperationSectionRequirement ignored,
                            out requirement) ||
                        !builder.TryOwnOutcome<int>(_ids.StateSlotId, out outcome) ||
                        !builder.TryFreeze<ExtraneousRequirementOperator>(
                            _ids.FirstOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            FirstError(requirement, outcome, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(OperatorCommitTestIds ids) => _ids = ids;

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class NoOpSecondaryOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperatorId _operatorId;
            private CoCoOperationSectionRequirement _secondary;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public bool SawDisabledSection { get; private set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _secondary,
                            out requirement) ||
                        !builder.TryFreeze<NoOpSecondaryOperator>(
                            _operatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(FirstError(requirement, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(OperatorCommitTestIds ids)
            {
                _ids = ids;
                if (!CoCoOperatorId.TryCreate(506UL, 3UL, out _operatorId))
                {
                    throw new InvalidOperationException("No-Op Operator fixture id is invalid.");
                }
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.TryGet(
                        _secondary,
                        out CoCoOperationSectionEntry<IOperatorCommitSecondarySection> section))
                {
                    outcome = default;
                    return false;
                }

                SawDisabledSection = !section.Header.Enabled;
                outcome = SawDisabledSection
                    ? CoCoOperatorOutcome.NoOp
                    : CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private static string FirstError(params CoCoDiagnostic[] diagnostics)
        {
            for (int index = 0; index < diagnostics.Length; index++)
            {
                if (diagnostics[index].IsError)
                {
                    return diagnostics[index].Message;
                }
            }

            return "Operator descriptor setup failed without an error diagnostic.";
        }

        private sealed class ContextWritingOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoOperatorId _operatorId;
            private CoCoStateSlotId _slotId;
            private CoCoStateSlotId _unauthorizedSlotId;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public int PreviousValue { get; private set; }
            public bool PreviousHadCommittedFrame { get; private set; }
            public bool FailAfterWorldMutation { get; set; }
            public bool AttemptUnauthorizedOutcome { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryOwnOutcome<int>(_slotId, out outcome) ||
                        !builder.TryFreeze<ContextWritingOperator>(
                            _operatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            outcome.IsError ? outcome.Message : freeze.Message);
                    }

                    return _descriptor;
                }
            }

            public void Configure(CoCoOperatorId operatorId, CoCoStateSlotId slotId)
            {
                _operatorId = operatorId;
                _slotId = slotId;
                if (!CoCoStateSlotId.TryCreate(999UL, 1UL, out _unauthorizedSlotId))
                {
                    throw new InvalidOperationException("Unauthorized Slot fixture id is invalid.");
                }
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                PreviousHadCommittedFrame = context.PreviousContext.HasCommittedFrame;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _slotId,
                        out CoCoStateSlot<int> slot))
                {
                    outcome = default;
                    return false;
                }

                PreviousValue = context.PreviousContext.Read(slot);
                if (FailAfterWorldMutation)
                {
                    transform.localPosition = new Vector3(17f, 0f, 0f);
                    outcome = default;
                    return false;
                }

                if (AttemptUnauthorizedOutcome)
                {
                    context.TryWriteOutcome(_unauthorizedSlotId, PreviousValue + 10);
                    outcome = CoCoOperatorOutcome.Success;
                    return true;
                }

                if (!context.TryWriteOutcome(slot, PreviousValue + 1))
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class OutboxOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoStateGraphHost _host;
            private OperatorCommitTestIds _ids;
            private CoCoOperatorDescriptor _descriptor;
            private CoCoEventOutboxRequirement _eventA;
            private CoCoEventOutboxRequirement _eventB;

            public int ExecuteCount { get; private set; }
            public bool AttemptInvalidTarget { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic eventA = default;
                    CoCoDiagnostic eventB = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryOwnOutcome<int>(_ids.StateSlotId, out outcome) ||
                        !builder.TryEmit<OperatorCommitEventA>(
                            _ids.EventTypeA,
                            _ids.EventDomainId,
                            2,
                            out _eventA,
                            out eventA) ||
                        !builder.TryEmit<OperatorCommitEventB>(
                            _ids.EventTypeB,
                            _ids.EventDomainId,
                            2,
                            out _eventB,
                            out eventB) ||
                        !builder.TryFreeze<OutboxOperator>(
                            _ids.FirstOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            outcome.IsError ? outcome.Message :
                            eventA.IsError ? eventA.Message :
                            eventB.IsError ? eventB.Message : freeze.Message);
                    }

                    return _descriptor;
                }
            }

            public void Configure(CoCoStateGraphHost host, OperatorCommitTestIds ids)
            {
                _host = host;
                _ids = ids;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _ids.StateSlotId,
                        out CoCoStateSlot<int> slot) ||
                    !context.TryWriteOutcome(slot, context.PreviousContext.Read(slot) + 1))
                {
                    outcome = default;
                    return false;
                }

                if (AttemptInvalidTarget)
                {
                    var invalidPayload = new OperatorCommitEventA { Value = 99 };
                    context.EventOutbox.TryWrite(_eventA, default, invalidPayload);
                    outcome = CoCoOperatorOutcome.Success;
                    return true;
                }

                if (!CoCoEventOutboxTarget.TryTargeted(
                        _host.GraphInstanceId,
                        CoCoEventReliability.Reliable,
                        default,
                        default,
                        default,
                        out CoCoEventOutboxTarget target) ||
                    context.EventOutbox.TryWrite(
                        _eventA,
                        target,
                        new OperatorCommitEventA { Value = 1 }) !=
                    CoCoEventOutboxWriteResult.Accepted ||
                    context.EventOutbox.TryWrite(
                        _eventB,
                        target,
                        new OperatorCommitEventB { Value = 2 }) !=
                    CoCoEventOutboxWriteResult.Accepted)
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class PublishLifecycleListener :
            IEventListener<CoCoEventPacket<OperatorCommitEventA>>,
            IEventListener<CoCoEventPacket<OperatorCommitEventB>>
        {
            private readonly CoCoStateGraphHost _host;
            private readonly bool _requestLifecycle;

            public PublishLifecycleListener(
                CoCoStateGraphHost host,
                bool requestLifecycle = true)
            {
                _host = host;
                _requestLifecycle = requestLifecycle;
            }

            public List<string> Order { get; } = new List<string>();
            public List<ulong> Sequences { get; } = new List<ulong>();
            public ulong ObservedRevision { get; private set; }
            public ulong ObservedTick { get; private set; }
            public bool ReentrantStepAccepted { get; private set; }
            public bool StopAccepted { get; private set; }
            public bool DisposeAccepted { get; private set; }
            public CoCoRuntimeLifecycleState LifecycleDuringPublish { get; private set; }

            public void OnEvent(ref CoCoEventPacket<OperatorCommitEventA> eventData)
            {
                Order.Add("A");
                Sequences.Add(eventData.Envelope.SourceEventSequence.Value);
                ObservedRevision = _host.CurrentContext.Revision.Value;
                ObservedTick = _host.CurrentContext.Header.TickFrame.Tick.Value;
                if (!_requestLifecycle)
                {
                    return;
                }

                ReentrantStepAccepted = _host.TryStep(0.01d, out _);
                StopAccepted = _host.TryStop(out _);
                DisposeAccepted = _host.TryDispose(out _);
                LifecycleDuringPublish = _host.Lifecycle;
            }

            public void OnEvent(ref CoCoEventPacket<OperatorCommitEventB> eventData)
            {
                Order.Add("B");
                Sequences.Add(eventData.Envelope.SourceEventSequence.Value);
            }
        }
    }
}
