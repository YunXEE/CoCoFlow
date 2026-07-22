using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostTemporalPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
        }

        [Test]
        public void CapacityZeroDisablesHistoryAndDoesNotRequireRestoreBinding()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 0,
                    assignRestoreBinding: false));
            scenario.Binding.Value = 11;

            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            Require(scenario.Host.TryStep(0.1d, out CoCoDiagnostic first), first);
            scenario.Binding.Value = 12;
            Require(scenario.Host.TryStep(0.1d, out CoCoDiagnostic second), second);

            Assert.That(scenario.Host.TemporalHistoryCapacity, Is.Zero);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Disabled));
            Assert.That(scenario.Host.TemporalState.Capacity, Is.Zero);
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
            Assert.That(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic rejected),
                Is.False);
            Assert.That(rejected.IsError, Is.True);
            Assert.That(scenario.Host.CurrentContext.Revision.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void SuspendedDebugStepPreservesRealWorldCorrectionFailureSemantics()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 0));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            Require(scenario.Host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            scenario.Binding.Value = 23;
            scenario.Binding.FailCaptureAfterWorldMutation = true;

            Assert.That(
                scenario.Host.TryDebugStepWhileSuspended(
                    0.1d,
                    out CoCoDiagnostic failure),
                Is.False);

            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Running),
                "A dirty-world failure must not be presented as a healthy Suspended Host.");
            Assert.That(scenario.GameObject.transform.localPosition.x, Is.EqualTo(23f));
            Assert.That(
                scenario.Host.TryCaptureDebugSnapshot(
                    out _,
                    out CoCoDiagnostic snapshotDiagnostic),
                Is.False);
            Assert.That(
                snapshotDiagnostic.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
        }

        [Test]
        public void CapacityOneIsRejectedBeforeRunning()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 1));

            Assert.That(
                scenario.Host.TryStart(out CoCoDiagnostic failure),
                Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(scenario.Host.CurrentContext.IsAlive, Is.False);
            Assert.That(scenario.Binding.CaptureCount, Is.Zero);
        }

        [Test]
        public void CapacityTwoCanPreviewAfterCurrentAndOlderEntriesExist()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 2));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);

            StepWithActorValue(scenario, 10);
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(1));
            Assert.That(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic tooEarly),
                Is.False);
            Assert.That(tooEarly.IsError, Is.True);

            StepWithActorValue(scenario, 20);
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(2));
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(10));
            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(20));
        }

        [Test]
        public void SuccessfulCommitsRecordAutomaticallyAndOverwriteOldestEntry()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 3));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);
            StepWithActorValue(scenario, 40);

            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(3));
            Assert.That(scenario.Host.TemporalState.Capacity, Is.EqualTo(3));
            Assert.That(
                scenario.Host.TemporalState.Current.Revision.Value,
                Is.EqualTo(4UL));

            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(2, out CoCoDiagnostic preview),
                preview);

            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(20));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.EqualTo(2));
            Assert.That(
                scenario.Host.TemporalState.Preview.Revision.Value,
                Is.EqualTo(2UL));
            int previewCount = scenario.Binding.PreviewCount;
            Assert.That(
                scenario.Host.TryPreviewTemporal(3, out CoCoDiagnostic outOfRange),
                Is.False);
            Assert.That(outOfRange.IsError, Is.True);
            Assert.That(scenario.Binding.PreviewCount, Is.EqualTo(previewCount));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.EqualTo(2));

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(40));
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(3));
        }

        [Test]
        public void PreviewAndCancelRunNoStateGraphOrAuthorityWork()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);

            CoCoStateGraphRuntime runtime = TemporalHostTestHarness.GetRuntime(scenario.Host);
            CoCoContextFrame authority = scenario.Host.CurrentContext;
            CoCoTickFrame authorityTick = authority.Header.TickFrame;
            int updateCount = TemporalHostLogic.UpdateCount;
            int captureCount = scenario.Binding.CaptureCount;
            int traceCount = CopyTraceCount(scenario.Host);

            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic firstPreview),
                firstPreview);
            Require(
                scenario.Host.TryPreviewTemporal(2, out CoCoDiagnostic secondPreview),
                secondPreview);

            Assert.That(
                scenario.Host.TryStep(0.1d, out CoCoDiagnostic blockedStep),
                Is.False);
            Assert.That(blockedStep.IsError, Is.True);
            Assert.That(TemporalHostLogic.UpdateCount, Is.EqualTo(updateCount));
            Assert.That(scenario.Binding.CaptureCount, Is.EqualTo(captureCount));
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            AssertClock(runtime, authorityTick);
            Assert.That(CopyTraceCount(scenario.Host), Is.EqualTo(traceCount));

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
            Assert.That(TemporalHostLogic.UpdateCount, Is.EqualTo(updateCount));
            Assert.That(scenario.Binding.CaptureCount, Is.EqualTo(captureCount));
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            AssertClock(runtime, authorityTick);
            Assert.That(CopyTraceCount(scenario.Host), Is.EqualTo(traceCount));
            Assert.That(scenario.Binding.PreviewCount, Is.EqualTo(2));
            Assert.That(scenario.Binding.CancelCount, Is.EqualTo(1));
        }

        [Test]
        public void ConfirmRestoresContextGraphClockAndCreatesNewEpochBranch()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 5));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);

            CoCoStateGraphRuntime runtime = TemporalHostTestHarness.GetRuntime(scenario.Host);
            CoCoContextFrame oldAuthority = scenario.Host.CurrentContext;
            ulong oldRevision = oldAuthority.Revision.Value;
            CoCoTickFrame oldTick = oldAuthority.Header.TickFrame;
            int oldUpdateCount = TemporalHostLogic.UpdateCount;
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(2, out CoCoDiagnostic preview),
                preview);
            CoCoTemporalFrameInfo source = scenario.Host.TemporalState.Preview;

            Require(
                scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic confirm),
                confirm);

            CoCoContextFrame restored = scenario.Host.CurrentContext;
            CoCoTickFrame restoredTick = restored.Header.TickFrame;
            Assert.That(scenario.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.UpdateCount, Is.EqualTo(oldUpdateCount));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    restored,
                    scenario.Ids.ActorStateSlotId),
                Is.EqualTo(10));
            Assert.That(
                TemporalHostTestHarness.ReadGraphState(
                    restored,
                    scenario.Ids.GraphStateSlotId).State,
                Is.EqualTo(1));
            Assert.That(restored.Revision.Value, Is.EqualTo(oldRevision + 1UL));
            Assert.That(restored.Origin.IsRestore, Is.True);
            Assert.That(restored.Origin.SourceRevision, Is.EqualTo(source.Revision));
            Assert.That(restored.Origin.SourceTick, Is.EqualTo(source.TickFrame.Tick));
            Assert.That(restoredTick.TimelineId, Is.EqualTo(source.TickFrame.TimelineId));
            Assert.That(restoredTick.ClockDomainId, Is.EqualTo(source.TickFrame.ClockDomainId));
            Assert.That(restoredTick.Tick, Is.EqualTo(source.TickFrame.Tick));
            Assert.That(
                restoredTick.TimelinePosition,
                Is.EqualTo(source.TickFrame.TimelinePosition));
            Assert.That(
                restoredTick.TimelineEpoch.Value,
                Is.GreaterThan(oldTick.TimelineEpoch.Value));
            Assert.That(
                restoredTick.TimelineEpoch.Value,
                Is.GreaterThan(source.TickFrame.TimelineEpoch.Value));
            Assert.That(
                restoredTick.ExecutionSequence.Value,
                Is.GreaterThan(oldTick.ExecutionSequence.Value));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(2));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Ready));

            scenario.Binding.Value = 11;
            Require(scenario.Host.TryStep(0.1d, out CoCoDiagnostic resumed), resumed);
            Assert.That(TemporalHostLogic.UpdateCount, Is.EqualTo(oldUpdateCount + 1));
            Assert.That(
                runtime.Clock.TimelineEpoch,
                Is.EqualTo(restoredTick.TimelineEpoch));
            Assert.That(runtime.Clock.Tick.Value, Is.EqualTo(restoredTick.Tick.Value + 1UL));
        }

        [Test]
        public void BeginClearsMailboxAndCancelDoesNotReviveBacklog()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            CoCoStateGraphHostRuntimeBindings bindings =
                TemporalHostTestHarness.GetBindings(scenario.Host);
            CoCoActorEventInboxCore inbox = bindings.Inbox;
            CoCoTimelineEpoch epoch = scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch;
            CoCoEventPacket<TemporalHostEvent> queued = TemporalHostTestHarness.Packet(
                scenario,
                1UL,
                41,
                epoch);
            Assert.That(
                scenario.Host.TryEnqueueLocal(queued),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));

            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Assert.That(
                inbox.State,
                Is.EqualTo(CoCoActorEventInboxState.RewindingOrRestoring));
            CoCoEventPacket<TemporalHostEvent> duringPreview = TemporalHostTestHarness.Packet(
                scenario,
                2UL,
                42,
                epoch);
            Assert.That(
                scenario.Host.TryEnqueueLocal(duringPreview),
                Is.EqualTo(CoCoInboxEnqueueResult.RewindOrRestoreDropped));
            Assert.That(scenario.Host.TemporalState.RewindRestoreDropped, Is.EqualTo(1UL));

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
            Assert.That(inbox.State, Is.EqualTo(CoCoActorEventInboxState.Running));
            StepWithActorValue(scenario, 30);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.Zero);
            Assert.That(TemporalHostLogic.LastIntentValue, Is.Zero);

            CoCoEventPacket<TemporalHostEvent> afterCancel = TemporalHostTestHarness.Packet(
                scenario,
                3UL,
                99,
                epoch);
            Assert.That(
                scenario.Host.TryEnqueueLocal(afterCancel),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 40);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.LastIntentValue, Is.EqualTo(99));
        }

        [Test]
        public void ConfirmInvalidatesOldMailboxStateAndAcceptsNewEpochInput()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            CoCoTimelineEpoch oldEpoch =
                scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch;
            CoCoEventPacket<TemporalHostEvent> queued = TemporalHostTestHarness.Packet(
                scenario,
                1UL,
                41,
                oldEpoch);
            Assert.That(
                scenario.Host.TryEnqueueLocal(queued),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            CoCoEventPacket<TemporalHostEvent> preConfirmFutureEpoch = TemporalHostTestHarness.Packet(
                scenario,
                2UL,
                43,
                new CoCoTimelineEpoch(oldEpoch.Value + 8UL));
            Assert.That(
                scenario.Host.TryEnqueueLocal(preConfirmFutureEpoch),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));

            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            CoCoEventPacket<TemporalHostEvent> duringPreview = TemporalHostTestHarness.Packet(
                scenario,
                2UL,
                42,
                oldEpoch);
            Assert.That(
                scenario.Host.TryEnqueueLocal(duringPreview),
                Is.EqualTo(CoCoInboxEnqueueResult.RewindOrRestoreDropped));
            Require(
                scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic confirm),
                confirm);

            CoCoTimelineEpoch newEpoch =
                scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch;
            Assert.That(newEpoch.Value, Is.GreaterThan(oldEpoch.Value));
            Assert.That(
                TemporalHostTestHarness.GetBindings(scenario.Host).Inbox.State,
                Is.EqualTo(CoCoActorEventInboxState.Running));
            StepWithActorValue(scenario, 30);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.Zero);

            Assert.That(
                scenario.Host.TryEnqueueLocal(queued),
                Is.EqualTo(CoCoInboxEnqueueResult.StaleTimelineEpoch));
            Assert.That(
                scenario.Host.TryEnqueueLocal(duringPreview),
                Is.EqualTo(CoCoInboxEnqueueResult.StaleTimelineEpoch));
            Assert.That(
                scenario.Host.TryEnqueueLocal(preConfirmFutureEpoch),
                Is.EqualTo(CoCoInboxEnqueueResult.InvalidPacket));
            CoCoEventPacket<TemporalHostEvent> futureEpochPacket = TemporalHostTestHarness.Packet(
                scenario,
                3UL,
                77,
                new CoCoTimelineEpoch(newEpoch.Value + 1UL));
            Assert.That(
                scenario.Host.TryEnqueueLocal(futureEpochPacket),
                Is.EqualTo(CoCoInboxEnqueueResult.InvalidPacket));
            CoCoEventPacket<TemporalHostEvent> routedOldEpochPacket = TemporalHostTestHarness.Packet(
                scenario,
                4UL,
                79,
                oldEpoch);
            CoCoEventBus.Publish(ref routedOldEpochPacket);
            StepWithActorValue(scenario, 35);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.Zero);

            CoCoEventPacket<TemporalHostEvent> newEpochPacket = TemporalHostTestHarness.Packet(
                scenario,
                3UL,
                88,
                newEpoch);
            Assert.That(
                scenario.Host.TryEnqueueLocal(newEpochPacket),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 40);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.LastIntentValue, Is.EqualTo(88));
        }

        [Test]
        public void ConsecutiveConfirmsAdvanceTheExactOwnerEpochBarrier()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            CoCoTimelineEpoch firstEpoch =
                scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch;

            Require(scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic firstBegin), firstBegin);
            Require(scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic firstPreview), firstPreview);
            Require(scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic firstConfirm), firstConfirm);
            CoCoTimelineEpoch secondEpoch =
                scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch;
            Assert.That(secondEpoch.Value, Is.GreaterThan(firstEpoch.Value));

            StepWithActorValue(scenario, 30);
            Require(scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic secondBegin), secondBegin);
            Require(scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic secondPreview), secondPreview);
            Require(scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic secondConfirm), secondConfirm);
            CoCoTimelineEpoch thirdEpoch =
                scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch;
            Assert.That(thirdEpoch.Value, Is.GreaterThan(secondEpoch.Value));

            Assert.That(
                scenario.Host.TryEnqueueLocal(TemporalHostTestHarness.Packet(
                    scenario,
                    1UL,
                    81,
                    secondEpoch)),
                Is.EqualTo(CoCoInboxEnqueueResult.StaleTimelineEpoch));
            Assert.That(
                scenario.Host.TryEnqueueLocal(TemporalHostTestHarness.Packet(
                    scenario,
                    2UL,
                    82,
                    new CoCoTimelineEpoch(thirdEpoch.Value + 1UL))),
                Is.EqualTo(CoCoInboxEnqueueResult.InvalidPacket));
            Assert.That(
                scenario.Host.TryEnqueueLocal(TemporalHostTestHarness.Packet(
                    scenario,
                    3UL,
                    83,
                    thirdEpoch)),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 40);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.LastIntentValue, Is.EqualTo(83));
        }

        [Test]
        public void HostsSharingAssetAndProviderOwnIndependentHistoryContextAndClock()
        {
            TemporalHostTestScenario first = Track(
                TemporalHostTestHarness.Create(historyCapacity: 5));
            TemporalHostTestScenario second = Track(
                TemporalHostTestHarness.CreateSibling(first, historyCapacity: 5));
            Assert.That(second.Asset, Is.SameAs(first.Asset));
            Assert.That(second.Provider, Is.SameAs(first.Provider));

            Require(first.Host.TryStart(out CoCoDiagnostic firstStart), firstStart);
            Require(second.Host.TryStart(out CoCoDiagnostic secondStart), secondStart);
            Assert.That(second.Host.GraphInstanceId, Is.Not.EqualTo(first.Host.GraphInstanceId));
            StepWithActorValue(first, 10);
            StepWithActorValue(second, 100);
            StepWithActorValue(first, 20);
            StepWithActorValue(second, 200);
            StepWithActorValue(first, 30);
            StepWithActorValue(second, 300);

            CoCoContextFrame secondAuthority = second.Host.CurrentContext;
            CoCoTickFrame secondTick = secondAuthority.Header.TickFrame;
            CoCoTemporalState secondTemporal = second.Host.TemporalState;
            CoCoStateGraphRuntime secondRuntime =
                TemporalHostTestHarness.GetRuntime(second.Host);
            Require(first.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin), begin);
            Require(first.Host.TryPreviewTemporal(2, out CoCoDiagnostic preview), preview);
            Require(first.Host.TryConfirmTemporalRestore(out CoCoDiagnostic confirm), confirm);

            Assert.That(first.Host.TemporalState.Count, Is.EqualTo(2));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    first.Host.CurrentContext,
                    first.Ids.ActorStateSlotId),
                Is.EqualTo(10));
            Assert.That(secondAuthority.IsAlive, Is.True);
            Assert.That(second.Host.CurrentContext, Is.EqualTo(secondAuthority));
            Assert.That(second.Host.CurrentContext.Revision, Is.EqualTo(secondTemporal.Current.Revision));
            Assert.That(second.Host.TemporalState.Count, Is.EqualTo(secondTemporal.Count));
            Assert.That(second.Host.TemporalState.Mode, Is.EqualTo(secondTemporal.Mode));
            Assert.That(second.Host.TemporalState.Current.Revision, Is.EqualTo(secondTemporal.Current.Revision));
            Assert.That(second.Binding.Value, Is.EqualTo(300));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    second.Host.CurrentContext,
                    second.Ids.ActorStateSlotId),
                Is.EqualTo(300));
            AssertClock(secondRuntime, secondTick);

            CoCoContextFrame firstAuthority = first.Host.CurrentContext;
            CoCoTickFrame firstTick = firstAuthority.Header.TickFrame;
            CoCoStateGraphRuntime firstRuntime =
                TemporalHostTestHarness.GetRuntime(first.Host);
            StepWithActorValue(second, 400);
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    second.Host.CurrentContext,
                    second.Ids.ActorStateSlotId),
                Is.EqualTo(400));
            Assert.That(firstAuthority.IsAlive, Is.True);
            Assert.That(first.Host.CurrentContext, Is.EqualTo(firstAuthority));
            Assert.That(first.Host.TemporalState.Count, Is.EqualTo(2));
            AssertClock(firstRuntime, firstTick);
        }

        [Test]
        public void SecondBeginRequestIsRejectedWithoutFurtherAuthorityModeOrMailboxMutation()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);

            CoCoContextFrame authority = scenario.Host.CurrentContext;
            CoCoActorEventInboxCore inbox =
                TemporalHostTestHarness.GetBindings(scenario.Host).Inbox;
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic firstBegin),
                firstBegin);
            CoCoActorInboxCounters countersAfterFirst = inbox.Counters;
            CoCoTemporalState stateAfterFirst = scenario.Host.TemporalState;

            Assert.That(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic secondBegin),
                Is.False);

            Assert.That(secondBegin.IsError, Is.True);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(CoCoTemporalMode.Previewing));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(stateAfterFirst.Count));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);
            Assert.That(scenario.Binding.ApplyCount, Is.Zero);
            Assert.That(inbox.State, Is.EqualTo(CoCoActorEventInboxState.RewindingOrRestoring));
            AssertInboxCounters(inbox.Counters, countersAfterFirst);

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
            Assert.That(inbox.State, Is.EqualTo(CoCoActorEventInboxState.Running));
            CoCoTimelineEpoch epoch = authority.Header.TickFrame.TimelineEpoch;
            Assert.That(
                scenario.Host.TryEnqueueLocal(
                    TemporalHostTestHarness.Packet(scenario, 1UL, 70, epoch)),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 30);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.LastIntentValue, Is.EqualTo(70));
        }

        [Test]
        public void SecondConfirmRequestIsRejectedWithoutAuthorityModeOrMailboxPollution()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            Require(
                scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic firstConfirm),
                firstConfirm);

            CoCoContextFrame restoredAuthority = scenario.Host.CurrentContext;
            CoCoTemporalState stateAfterFirst = scenario.Host.TemporalState;
            CoCoActorEventInboxCore inbox =
                TemporalHostTestHarness.GetBindings(scenario.Host).Inbox;
            CoCoActorInboxCounters countersAfterFirst = inbox.Counters;

            Assert.That(
                scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic secondConfirm),
                Is.False);

            Assert.That(secondConfirm.IsError, Is.True);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(restoredAuthority));
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(stateAfterFirst.Count));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);
            Assert.That(scenario.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(inbox.State, Is.EqualTo(CoCoActorEventInboxState.Running));
            AssertInboxCounters(inbox.Counters, countersAfterFirst);

            CoCoTimelineEpoch epoch = restoredAuthority.Header.TickFrame.TimelineEpoch;
            Assert.That(
                scenario.Host.TryEnqueueLocal(
                    TemporalHostTestHarness.Packet(scenario, 1UL, 71, epoch)),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 40);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.LastIntentValue, Is.EqualTo(71));
        }

        [Test]
        public void SecondCancelRequestIsRejectedWithoutAuthorityModeOrMailboxPollution()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);
            CoCoContextFrame authority = scenario.Host.CurrentContext;
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic firstCancel),
                firstCancel);

            CoCoTemporalState stateAfterFirst = scenario.Host.TemporalState;
            CoCoActorEventInboxCore inbox =
                TemporalHostTestHarness.GetBindings(scenario.Host).Inbox;
            CoCoActorInboxCounters countersAfterFirst = inbox.Counters;

            Assert.That(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic secondCancel),
                Is.False);

            Assert.That(secondCancel.IsError, Is.True);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(stateAfterFirst.Count));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);
            Assert.That(scenario.Binding.CancelCount, Is.EqualTo(1));
            Assert.That(inbox.State, Is.EqualTo(CoCoActorEventInboxState.Running));
            AssertInboxCounters(inbox.Counters, countersAfterFirst);

            CoCoTimelineEpoch epoch = authority.Header.TickFrame.TimelineEpoch;
            Assert.That(
                scenario.Host.TryEnqueueLocal(
                    TemporalHostTestHarness.Packet(scenario, 1UL, 72, epoch)),
                Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 40);
            Assert.That(TemporalHostEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(TemporalHostLogic.LastIntentValue, Is.EqualTo(72));
        }

        [Test]
        public void EnabledHistoryTenThousandManualTicksAllocateNoManagedMemoryAndKeepFixedResources()
        {
            const int historyCapacity = 8;
            const int warmupIterations = 100;
            const int measuredIterations = 10000;
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: historyCapacity));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);

            bool failed = false;
            for (int index = 0; index < warmupIterations; index++)
            {
                scenario.Binding.Value = index;
                failed |= !scenario.Host.TryStep(0.02d, out _);
            }

            Assert.That(failed, Is.False);
            TemporalResourceSnapshot beforeResources =
                CaptureTemporalResources(scenario.Host);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < measuredIterations; index++)
            {
                scenario.Binding.Value = index;
                failed |= !scenario.Host.TryStep(0.02d, out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
            TemporalResourceSnapshot afterResources =
                CaptureTemporalResources(scenario.Host);

            Assert.That(failed, Is.False);
            Assert.That(allocated, Is.Zero);
            Assert.That(scenario.Host.TemporalState.Capacity, Is.EqualTo(historyCapacity));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(historyCapacity));
            Assert.That(
                scenario.Host.CurrentContext.Revision.Value,
                Is.EqualTo((ulong)(warmupIterations + measuredIterations)));
            Assert.That(
                scenario.Binding.CaptureCount,
                Is.EqualTo(warmupIterations + measuredIterations));
            AssertFixedTemporalResources(afterResources, beforeResources, historyCapacity);
            TestContext.Out.WriteLine(
                "Pre6 Temporal resource upper bound: " +
                $"arenaCells={afterResources.ArenaCells.Length}, " +
                $"historyEntries={afterResources.HistoryEntries.Length}, " +
                $"payloadPoolBuffers={afterResources.HistoryPayloads.Length + 1}, " +
                $"maxEncodedSize={afterResources.MaxEncodedSize}, " +
                $"previewScratchBytes={afterResources.PreviewScratchBytes}, " +
                $"allocatedPayloadBytes={afterResources.AllocatedHistoryPayloadBytes}, " +
                $"arenaRetain={afterResources.ArenaRetainCount}, " +
                $"externalRetain={afterResources.ExternalRetainCount}.");
        }

        [Test]
        public void PreviewCancelThousandCyclesAllocateNoManagedMemoryAfterWarmup()
        {
            const int warmupIterations = 100;
            const int measuredIterations = 1000;
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 5));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);
            CoCoContextFrame authority = scenario.Host.CurrentContext;

            bool failed = false;
            for (int index = 0; index < warmupIterations; index++)
            {
                failed |= !TryPreviewCancelCycle(scenario.Host);
            }

            Assert.That(failed, Is.False);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < measuredIterations; index++)
            {
                failed |= !TryPreviewCancelCycle(scenario.Host);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(failed, Is.False);
            Assert.That(allocated, Is.Zero);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(3));
            Assert.That(
                scenario.Binding.CancelCount,
                Is.EqualTo(warmupIterations + measuredIterations));
        }

        [Test]
        public void ConfirmAndNextTickThousandCyclesAllocateNoManagedMemoryAfterWarmup()
        {
            const int warmupIterations = 100;
            const int measuredIterations = 1000;
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 5));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            StepWithActorValue(scenario, 30);

            bool failed = false;
            for (int index = 0; index < warmupIterations; index++)
            {
                failed |= !TryConfirmAndNextTickCycle(scenario);
            }

            Assert.That(failed, Is.False);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < measuredIterations; index++)
            {
                failed |= !TryConfirmAndNextTickCycle(scenario);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(failed, Is.False);
            Assert.That(allocated, Is.Zero);
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(5));
            Assert.That(
                scenario.Binding.ConfirmCount,
                Is.EqualTo(warmupIterations + measuredIterations));
        }

        private TemporalHostTestScenario Track(TemporalHostTestScenario scenario)
        {
            _objects.Add(scenario.Asset);
            _objects.Add(scenario.GameObject);
            return scenario;
        }

        private static void StepWithActorValue(
            TemporalHostTestScenario scenario,
            int value)
        {
            scenario.Binding.Value = value;
            Require(
                scenario.Host.TryStep(0.1d, out CoCoDiagnostic diagnostic),
                diagnostic);
        }

        private static bool TryPreviewCancelCycle(CoCoStateGraphHost host) =>
            host.TryBeginTemporalPreview(out _) &&
            host.TryPreviewTemporal(1, out _) &&
            host.TryCancelTemporalPreview(out _);

        private static bool TryConfirmAndNextTickCycle(
            TemporalHostTestScenario scenario) =>
            scenario.Host.TryBeginTemporalPreview(out _) &&
            scenario.Host.TryPreviewTemporal(1, out _) &&
            scenario.Host.TryConfirmTemporalRestore(out _) &&
            scenario.Host.TryStep(0.1d, out _);

        private static TemporalResourceSnapshot CaptureTemporalResources(
            CoCoStateGraphHost host)
        {
            object transaction = ReadField(host, "_transaction");
            object arena = ReadField(transaction, "_contextArena");
            Array cells = (Array)ReadField(arena, "_frames");
            var cellObjects = new object[cells.Length];
            var cellBuffers = new object[cells.Length];
            long retainCount = 0L;
            int externalRetainCount = 0;
            for (int index = 0; index < cells.Length; index++)
            {
                object cell = cells.GetValue(index);
                cellObjects[index] = cell;
                cellBuffers[index] = ReadProperty(cell, "Buffer");
                retainCount += (long)ReadProperty(cell, "RetainCount");
                externalRetainCount += (int)ReadField(cell, "_externalRetainCount");
            }

            object temporal = ReadField(host, "_temporal");
            object history = ReadField(temporal, "_history");
            object historyLayout = ReadField(history, "_layout");
            Array entries = (Array)ReadField(history, "_entries");
            var entryObjects = new object[entries.Length];
            var entryPayloads = new object[entries.Length];
            for (int index = 0; index < entries.Length; index++)
            {
                object entry = entries.GetValue(index);
                entryObjects[index] = entry;
                entryPayloads[index] = ReadProperty(entry, "Payload");
            }

            return new TemporalResourceSnapshot(
                (int)ReadProperty(arena, "Capacity"),
                cellObjects,
                cellBuffers,
                retainCount,
                externalRetainCount,
                entryObjects,
                entryPayloads,
                ReadField(history, "_stagingPayload"),
                ReadField(history, "_previewBuffer"),
                (int)ReadProperty(history, "MaxEncodedSize"),
                (int)ReadProperty(historyLayout, "ByteSize"),
                (long)ReadProperty(history, "AllocatedPayloadBytes"));
        }

        private static void AssertFixedTemporalResources(
            TemporalResourceSnapshot actual,
            TemporalResourceSnapshot expected,
            int historyCapacity)
        {
            Assert.That(actual.ArenaCapacity, Is.EqualTo(4));
            Assert.That(actual.ArenaCapacity, Is.EqualTo(expected.ArenaCapacity));
            Assert.That(actual.ArenaCells, Has.Length.EqualTo(expected.ArenaCells.Length));
            Assert.That(actual.HistoryEntries, Has.Length.EqualTo(historyCapacity));
            Assert.That(actual.HistoryEntries, Has.Length.EqualTo(expected.HistoryEntries.Length));
            Assert.That(actual.ArenaRetainCount, Is.EqualTo(1L));
            Assert.That(actual.ExternalRetainCount, Is.Zero);
            Assert.That(actual.AllocatedHistoryPayloadBytes, Is.GreaterThan(0L));
            Assert.That(
                actual.AllocatedHistoryPayloadBytes,
                Is.EqualTo(expected.AllocatedHistoryPayloadBytes));
            Assert.That(actual.MaxEncodedSize, Is.EqualTo(expected.MaxEncodedSize));
            Assert.That(actual.PreviewScratchBytes, Is.EqualTo(expected.PreviewScratchBytes));
            Assert.That(
                actual.AllocatedHistoryPayloadBytes,
                Is.EqualTo(
                    ((long)historyCapacity + 1L) * actual.MaxEncodedSize +
                    actual.PreviewScratchBytes));
            AssertHistoryPayloadPool(actual, expected, historyCapacity + 1);
            Assert.That(actual.PreviewBuffer, Is.SameAs(expected.PreviewBuffer));
            for (int index = 0; index < actual.ArenaCells.Length; index++)
            {
                Assert.That(actual.ArenaCells[index], Is.SameAs(expected.ArenaCells[index]));
                Assert.That(actual.ArenaBuffers[index], Is.SameAs(expected.ArenaBuffers[index]));
            }

            for (int index = 0; index < actual.HistoryEntries.Length; index++)
            {
                Assert.That(
                    actual.HistoryEntries[index],
                    Is.SameAs(expected.HistoryEntries[index]));
            }
        }

        private static void AssertHistoryPayloadPool(
            TemporalResourceSnapshot actual,
            TemporalResourceSnapshot expected,
            int expectedCount)
        {
            Assert.That(actual.HistoryPayloads.Length + 1, Is.EqualTo(expectedCount));
            Assert.That(expected.HistoryPayloads.Length + 1, Is.EqualTo(expectedCount));
            for (int actualIndex = 0; actualIndex < expectedCount; actualIndex++)
            {
                object actualPayload = actualIndex == actual.HistoryPayloads.Length
                    ? actual.StagingPayload
                    : actual.HistoryPayloads[actualIndex];
                int matches = 0;
                for (int expectedIndex = 0; expectedIndex < expectedCount; expectedIndex++)
                {
                    object expectedPayload = expectedIndex == expected.HistoryPayloads.Length
                        ? expected.StagingPayload
                        : expected.HistoryPayloads[expectedIndex];
                    if (ReferenceEquals(actualPayload, expectedPayload))
                    {
                        matches++;
                    }
                }

                Assert.That(matches, Is.EqualTo(1));
            }

            for (int first = 0; first < expectedCount; first++)
            {
                object firstPayload = first == actual.HistoryPayloads.Length
                    ? actual.StagingPayload
                    : actual.HistoryPayloads[first];
                for (int second = first + 1; second < expectedCount; second++)
                {
                    object secondPayload = second == actual.HistoryPayloads.Length
                        ? actual.StagingPayload
                        : actual.HistoryPayloads[second];
                    Assert.That(ReferenceEquals(firstPayload, secondPayload), Is.False);
                }
            }
        }

        private static object ReadField(object target, string name)
        {
            Assert.That(target, Is.Not.Null, $"Reflection target for {name} is missing.");
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field {name} is missing.");
            return field.GetValue(target);
        }

        private static object ReadProperty(object target, string name)
        {
            Assert.That(target, Is.Not.Null, $"Reflection target for {name} is missing.");
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, $"Property {name} is missing.");
            return property.GetValue(target);
        }

        private static void AssertInboxCounters(
            in CoCoActorInboxCounters actual,
            in CoCoActorInboxCounters expected)
        {
            Assert.That(actual.Accepted, Is.EqualTo(expected.Accepted));
            Assert.That(actual.Duplicate, Is.EqualTo(expected.Duplicate));
            Assert.That(actual.Rejected, Is.EqualTo(expected.Rejected));
            Assert.That(
                actual.RewindRestoreDropped,
                Is.EqualTo(expected.RewindRestoreDropped));
            Assert.That(
                actual.UnreliableOverflowDropped,
                Is.EqualTo(expected.UnreliableOverflowDropped));
            Assert.That(
                actual.ReliableOverflowFaults,
                Is.EqualTo(expected.ReliableOverflowFaults));
        }

        private static int CopyTraceCount(CoCoStateGraphHost host)
        {
            var entries = new CoCoStateFlowTraceEntry[64];
            return host.Trace.CopyLatestTo(entries);
        }

        private static void AssertClock(
            CoCoStateGraphRuntime runtime,
            in CoCoTickFrame expected)
        {
            Assert.That(runtime.Clock.TimelineId, Is.EqualTo(expected.TimelineId));
            Assert.That(runtime.Clock.ClockDomainId, Is.EqualTo(expected.ClockDomainId));
            Assert.That(runtime.Clock.TimelineEpoch, Is.EqualTo(expected.TimelineEpoch));
            Assert.That(runtime.Clock.Tick, Is.EqualTo(expected.Tick));
            Assert.That(
                runtime.Clock.ExecutionSequence,
                Is.EqualTo(expected.ExecutionSequence));
            Assert.That(runtime.Clock.Seconds, Is.EqualTo(expected.TimelinePosition.Seconds));
        }

        private static void Require(
            bool succeeded,
            CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }

        private sealed class TemporalResourceSnapshot
        {
            internal TemporalResourceSnapshot(
                int arenaCapacity,
                object[] arenaCells,
                object[] arenaBuffers,
                long arenaRetainCount,
                int externalRetainCount,
                object[] historyEntries,
                object[] historyPayloads,
                object stagingPayload,
                object previewBuffer,
                int maxEncodedSize,
                int previewScratchBytes,
                long allocatedHistoryPayloadBytes)
            {
                ArenaCapacity = arenaCapacity;
                ArenaCells = arenaCells;
                ArenaBuffers = arenaBuffers;
                ArenaRetainCount = arenaRetainCount;
                ExternalRetainCount = externalRetainCount;
                HistoryEntries = historyEntries;
                HistoryPayloads = historyPayloads;
                StagingPayload = stagingPayload;
                PreviewBuffer = previewBuffer;
                MaxEncodedSize = maxEncodedSize;
                PreviewScratchBytes = previewScratchBytes;
                AllocatedHistoryPayloadBytes = allocatedHistoryPayloadBytes;
            }

            internal int ArenaCapacity { get; }
            internal object[] ArenaCells { get; }
            internal object[] ArenaBuffers { get; }
            internal long ArenaRetainCount { get; }
            internal int ExternalRetainCount { get; }
            internal object[] HistoryEntries { get; }
            internal object[] HistoryPayloads { get; }
            internal object StagingPayload { get; }
            internal object PreviewBuffer { get; }
            internal int MaxEncodedSize { get; }
            internal int PreviewScratchBytes { get; }
            internal long AllocatedHistoryPayloadBytes { get; }
        }
    }
}
