using System;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoIntentMailboxContractTests
    {
        [Test]
        public void DefaultMailboxValuesNeverRepresentValidRuntimeStatesOrSuccess()
        {
            Assert.AreEqual(CoCoEventDeliveryMode.None, default(CoCoEventDeliveryMode));
            Assert.AreEqual(CoCoEventReliability.None, default(CoCoEventReliability));
            Assert.AreEqual(CoCoActorEventInboxState.None, default(CoCoActorEventInboxState));
            Assert.AreEqual(CoCoInboxEnqueueResult.None, default(CoCoInboxEnqueueResult));
            Assert.AreEqual(CoCoIntentContributionResult.None, default(CoCoIntentContributionResult));
            Assert.IsFalse(default(CoCoActorEventEnvelope).IsValid);
            Assert.IsFalse(default(CoCoEventPacket<TestEvent>).IsValid);
        }

        [Test]
        public void IntentLayoutRequiresReducerAndFreezesRegistration()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(1UL);
            CoCoIntentId intentId = IntentId(10UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);

            Assert.IsFalse(layout.TryRegister(
                intentId,
                2,
                (ICoCoIntentReducer<TestIntent>)null,
                out CoCoIntentHandle<TestIntent> missingReducerHandle,
                out CoCoDiagnostic missingReducerDiagnostic));
            Assert.IsFalse(missingReducerHandle.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.MissingIntentReducer, missingReducerDiagnostic.Code);

            Assert.IsTrue(layout.TryRegister(
                intentId,
                3,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> handle,
                out CoCoDiagnostic registerDiagnostic));
            Assert.IsTrue(registerDiagnostic.IsNone);
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(layout.Freeze(out CoCoDiagnostic freezeDiagnostic));
            Assert.IsTrue(freezeDiagnostic.IsNone);

            Assert.IsFalse(layout.TryRegister(
                IntentId(11UL),
                1,
                new OrderedIntentReducer(),
                out _,
                out CoCoDiagnostic frozenDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, frozenDiagnostic.Code);
        }

        [Test]
        public void IntentArbitrationUsesPresenceAndDeterministicPriorityOrder()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(2UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                IntentId(20UL),
                3,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> orderedHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(21UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> absentHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));

            CoCoGraphInstanceId graphInstanceId = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(
                graphInstanceId,
                3,
                out CoCoIntentFrameRuntime runtime,
                out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                orderedHandle,
                10,
                out CoCoIntentSourceRequirement<TestIntent> highRequirement));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                orderedHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> lowRequirement));
            Assert.IsTrue(runtime.TryBindSource(
                highRequirement,
                new CountingIntentSource(3),
                out CoCoIntentSourceBinding<TestIntent> highEarlier,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                lowRequirement,
                new CountingIntentSource(1),
                out CoCoIntentSourceBinding<TestIntent> low,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                highRequirement,
                new CountingIntentSource(2),
                out CoCoIntentSourceBinding<TestIntent> highLater,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
            CoCoStateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, runtime.TrySample(low, header.TickFrame));
            Assert.AreEqual(
                CoCoIntentSourceSampleResult.Contributed,
                runtime.TrySample(highLater, header.TickFrame));
            Assert.AreEqual(
                CoCoIntentSourceSampleResult.Contributed,
                runtime.TrySample(highEarlier, header.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));

            Assert.IsTrue(runtime.Frame.TryGet(orderedHandle, out TestIntent resolved));
            Assert.AreEqual(321, resolved.Value);
            Assert.IsFalse(runtime.Frame.IsPresent(absentHandle));
            Assert.IsFalse(runtime.Frame.TryGet(absentHandle, out _));
            Assert.AreEqual(header, runtime.Frame.Header);
        }

        [Test]
        public void EventProjectionIsDeterministicAcrossArrivalOrderAndSingleUse()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(3UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                IntentId(30UL),
                2,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(31UL),
                2,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> secondHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 2, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                5,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                secondHandle,
                5,
                out CoCoIntentSourceRequirement<TestIntent> secondRequirement));
            CoCoEventDomainId domain = DomainId(30UL);
            CoCoEventTypeId eventType = EventTypeId(30UL);
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                requirement,
                2,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                secondRequirement,
                2,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> secondBinding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            var inbox = CreateProjectionInbox(
                owner,
                domain,
                eventType,
                2,
                runtime,
                out CoCoActorEventLaneHandle<TestEvent> lane);
            CoCoGraphInstanceId lowerSource = GraphId(910UL);
            CoCoGraphInstanceId higherSource = GraphId(920UL);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, higherSource, owner, 1UL, 1UL, 2)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, lowerSource, owner, 1UL, 1UL, 1)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(lane, out CoCoActorEventSealedBatch<TestEvent> firstBatch));
            Assert.IsTrue(runtime.TryBegin(IntentHeader(layoutId, 1UL, 1UL), out _));
            Assert.AreEqual(CoCoIntentEventProjectionResult.Contributed, runtime.TryProject(binding, firstBatch));
            Assert.AreEqual(
                CoCoIntentEventProjectionResult.Contributed,
                runtime.TryProject(secondBinding, firstBatch));
            Assert.AreEqual(CoCoIntentEventProjectionResult.AlreadyProjected, runtime.TryProject(binding, firstBatch));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent first));
            Assert.IsTrue(runtime.Frame.TryGet(secondHandle, out TestIntent firstSecondIntent));
            Assert.AreEqual(12, first.Value);
            Assert.AreEqual(12, firstSecondIntent.Value);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, lowerSource, owner, 1UL, 2UL, 1)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, higherSource, owner, 1UL, 2UL, 2)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(lane, out CoCoActorEventSealedBatch<TestEvent> secondBatch));
            Assert.IsTrue(runtime.TryBegin(IntentHeader(layoutId, 2UL, 1UL), out _));
            Assert.AreEqual(CoCoIntentEventProjectionResult.Contributed, runtime.TryProject(binding, secondBatch));
            Assert.AreEqual(
                CoCoIntentEventProjectionResult.Contributed,
                runtime.TryProject(secondBinding, secondBatch));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent second));
            Assert.IsTrue(runtime.Frame.TryGet(secondHandle, out TestIntent secondSecondIntent));
            Assert.AreEqual(12, second.Value);
            Assert.AreEqual(12, secondSecondIntent.Value);
        }

        [Test]
        public void EventProjectionOrdersSourceEpochBeforeItsRestartedSequence()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(4UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(40UL),
                2,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                5,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            CoCoEventDomainId domain = DomainId(40UL);
            CoCoEventTypeId eventType = EventTypeId(40UL);
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                requirement,
                2,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            var inbox = CreateProjectionInbox(
                owner,
                domain,
                eventType,
                2,
                runtime,
                out CoCoActorEventLaneHandle<TestEvent> lane);
            CoCoGraphInstanceId source = GraphId(940UL);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, source, owner, 1UL, 7UL, 7)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, source, owner, 2UL, 1UL, 1)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(lane, out CoCoActorEventSealedBatch<TestEvent> batch));
            Assert.IsTrue(runtime.TryBegin(IntentHeader(layoutId, 1UL, 1UL), out _));
            Assert.AreEqual(CoCoIntentEventProjectionResult.Contributed, runtime.TryProject(binding, batch));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent resolved));
            Assert.AreEqual(71, resolved.Value);
        }

        [Test]
        public void IntentSourceBindingSamplesAtMostOncePerStartedTick()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(5UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(31UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                priority: 10,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(layout.Freeze(out _));

            var separateLayout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(separateLayout.TryRegister(
                IntentId(31UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> foreignHandle,
                out _));
            Assert.IsTrue(separateLayout.Freeze(out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                foreignHandle,
                priority: 10,
                out CoCoIntentSourceRequirement<TestIntent> foreignRequirement));

            var source = new CountingIntentSource(7);
            CoCoGraphInstanceId graphInstanceId = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(
                graphInstanceId,
                2,
                out CoCoIntentFrameRuntime runtime,
                out _));
            Assert.IsFalse(layout.TryCreateRuntime(graphInstanceId, 2, out _, out _));
            Assert.IsFalse(runtime.TryBindSource(
                foreignRequirement,
                new CountingIntentSource(99),
                out _,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                source,
                out CoCoIntentSourceBinding<TestIntent> binding,
                out _));
            Assert.IsFalse(runtime.TryBindSource(requirement, source, out _, out _));
            Assert.IsFalse(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(8),
                out _,
                out CoCoDiagnostic capacityDiagnostic));
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidIntentContribution,
                capacityDiagnostic.Code);
            Assert.IsTrue(runtime.FreezeBindings(out _));
            CoCoStateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL);

            Assert.AreEqual(
                CoCoIntentSourceSampleResult.ArbiterNotCollecting,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(0, source.SampleCount);

            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(
                CoCoIntentSourceSampleResult.Contributed,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(
                CoCoIntentSourceSampleResult.AlreadySampled,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(1, source.SampleCount);
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent first));
            Assert.AreEqual(7, first.Value);

            Assert.IsFalse(runtime.TryBegin(header, out _));
            Assert.IsFalse(runtime.TryBegin(IntentHeader(layoutId, 1UL, 1UL, 2UL), out _));
            Assert.IsFalse(runtime.TryBegin(IntentHeader(layoutId, 0UL, 1UL, 3UL), out _));
            header = IntentHeader(layoutId, 2UL, 1UL);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(
                CoCoIntentSourceSampleResult.Contributed,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(2, source.SampleCount);
            Assert.IsTrue(runtime.TryFreeze(out _));

            runtime.Dispose();
            Assert.IsTrue(runtime.IsDisposed);
            Assert.IsFalse(runtime.TryBegin(IntentHeader(layoutId, 3UL, 1UL), out _));
            Assert.IsTrue(layout.TryCreateRuntime(graphInstanceId, 0, out CoCoIntentFrameRuntime replacement, out _));
            replacement.Dispose();
        }

        [Test]
        public void TargetedPacketOnlyEntersMatchingActorAndDomain()
        {
            CoCoGraphInstanceId owner = GraphId(100UL);
            CoCoGraphInstanceId source = GraphId(101UL);
            CoCoEventDomainId domain = DomainId(1UL);
            CoCoEventTypeId eventType = EventTypeId(1UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out CoCoActorEventLaneHandle<TestEvent> handle);

            CoCoEventPacket<TestEvent> wrongTarget = Packet(
                eventType,
                domain,
                source,
                GraphId(999UL),
                epoch: 1UL,
                sequence: 1UL,
                value: 1);
            Assert.AreEqual(CoCoInboxEnqueueResult.EventTargetMismatch, inbox.TryEnqueue(handle, wrongTarget));

            CoCoEventPacket<TestEvent> wrongDomain = Packet(
                eventType,
                DomainId(2UL),
                source,
                owner,
                epoch: 1UL,
                sequence: 2UL,
                value: 2);
            Assert.AreEqual(CoCoInboxEnqueueResult.EventDomainMismatch, inbox.TryEnqueue(handle, wrongDomain));

            CoCoEventPacket<TestEvent> accepted = Packet(
                eventType,
                domain,
                source,
                owner,
                epoch: 1UL,
                sequence: 3UL,
                value: 3);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, accepted));
            Assert.AreEqual(1UL, inbox.Counters.Accepted);
        }

        [Test]
        public void EventProjectionRejectsForeignActorDomainAndRuntimeWithoutClaimingTheBatch()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(6UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                IntentId(60UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> firstHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(61UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> secondHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(160UL);
            CoCoGraphInstanceId foreignOwner = GraphId(161UL);
            CoCoEventDomainId domain = DomainId(16UL);
            CoCoEventDomainId foreignDomain = DomainId(17UL);
            CoCoEventTypeId eventType = EventTypeId(16UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 2, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(layout.TryCreateRuntime(
                foreignOwner,
                1,
                out CoCoIntentFrameRuntime foreignRuntime,
                out _));
            var separateLayout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(separateLayout.TryRegister(
                IntentId(60UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> separateHandle,
                out _));
            Assert.IsTrue(separateLayout.Freeze(out _));
            Assert.IsTrue(separateLayout.TryCreateRuntime(
                owner,
                1,
                out CoCoIntentFrameRuntime sameOwnerRuntime,
                out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                firstHandle,
                1,
                out CoCoIntentSourceRequirement<TestIntent> firstRequirement));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                secondHandle,
                1,
                out CoCoIntentSourceRequirement<TestIntent> secondRequirement));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                separateHandle,
                1,
                out CoCoIntentSourceRequirement<TestIntent> separateRequirement));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                firstRequirement,
                1,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                foreignDomain,
                eventType,
                secondRequirement,
                1,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> wrongDomainBinding,
                out _));
            Assert.IsTrue(foreignRuntime.TryBindEventAdapter(
                domain,
                eventType,
                firstRequirement,
                1,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> foreignBinding,
                out _));
            Assert.IsTrue(sameOwnerRuntime.TryBindEventAdapter(
                domain,
                eventType,
                separateRequirement,
                1,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> sameOwnerBinding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
            Assert.IsTrue(foreignRuntime.FreezeBindings(out _));
            Assert.IsTrue(sameOwnerRuntime.FreezeBindings(out _));

            var inbox = CreateProjectionInbox(
                owner,
                domain,
                eventType,
                1,
                runtime,
                out CoCoActorEventLaneHandle<TestEvent> lane);
            var competingInbox = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(competingInbox.TryRegisterLane(
                eventType,
                1,
                false,
                out CoCoActorEventLaneHandle<TestEvent> competingLane,
                out _));
            Assert.IsTrue(competingLane.IsValid);
            Assert.IsFalse(competingInbox.TryBindIntentRuntime(runtime, out _));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(162UL), owner, 1UL, 1UL, 8)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(lane, out CoCoActorEventSealedBatch<TestEvent> batch));

            Assert.IsTrue(foreignRuntime.TryBegin(
                IntentHeader(layoutId, 1UL, 1UL, foreignOwner),
                out _));
            Assert.AreEqual(
                CoCoIntentEventProjectionResult.InvalidBatch,
                foreignRuntime.TryProject(foreignBinding, batch));
            Assert.IsTrue(sameOwnerRuntime.TryBegin(
                IntentHeader(layoutId, 1UL, 1UL, owner),
                out _));
            Assert.AreEqual(
                CoCoIntentEventProjectionResult.InvalidBatch,
                sameOwnerRuntime.TryProject(sameOwnerBinding, batch));
            Assert.IsTrue(runtime.TryBegin(IntentHeader(layoutId, 1UL, 1UL, owner), out _));
            Assert.AreEqual(
                CoCoIntentEventProjectionResult.InvalidBatch,
                runtime.TryProject(wrongDomainBinding, batch));
            Assert.AreEqual(
                CoCoIntentEventProjectionResult.Contributed,
                runtime.TryProject(binding, batch));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(firstHandle, out TestIntent resolved));
            Assert.AreEqual(8, resolved.Value);
            inbox.Stop();
            Assert.IsTrue(competingInbox.TryBindIntentRuntime(runtime, out _));
        }

        [Test]
        public void LaneHandleCannotCrossInboxInstancesWithTheSameIdentity()
        {
            CoCoGraphInstanceId owner = GraphId(170UL);
            CoCoEventDomainId domain = DomainId(18UL);
            CoCoEventTypeId eventType = EventTypeId(18UL);
            var first = CreateInbox(
                owner,
                domain,
                eventType,
                1,
                out CoCoActorEventLaneHandle<TestEvent> firstHandle);
            var second = CreateInbox(
                owner,
                domain,
                eventType,
                1,
                out CoCoActorEventLaneHandle<TestEvent> secondHandle);
            CoCoEventPacket<TestEvent> packet = Packet(
                eventType,
                domain,
                GraphId(171UL),
                owner,
                1UL,
                1UL,
                1);

            Assert.AreEqual(
                CoCoInboxEnqueueResult.InvalidPacket,
                first.TryEnqueue(secondHandle, packet));
            Assert.AreEqual(
                CoCoInboxEnqueueResult.InvalidPacket,
                second.TryEnqueue(firstHandle, packet));
            Assert.IsFalse(firstHandle.Equals(secondHandle));
        }

        [Test]
        public void InboxUsesSealedDoubleBufferAndNextTickVisibility()
        {
            CoCoGraphInstanceId owner = GraphId(200UL);
            CoCoGraphInstanceId source = GraphId(201UL);
            CoCoEventDomainId domain = DomainId(2UL);
            CoCoEventTypeId eventType = EventTypeId(2UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                handle,
                out CoCoActorEventSealedBatch<TestEvent> firstBatch));
            Assert.IsTrue(firstBatch.IsValid);
            Assert.AreEqual(1, firstBatch.Count);
            Assert.IsTrue(firstBatch.TryRead(0, out CoCoEventPacket<TestEvent> firstFromBatch));
            Assert.AreEqual(10, firstFromBatch.Payload.Value);
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> first));
            Assert.AreEqual(10, first.Payload.Value);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> stillFirst));
            Assert.AreEqual(10, stillFirst.Payload.Value);

            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> second));
            Assert.AreEqual(20, second.Payload.Value);
            Assert.IsFalse(firstBatch.IsValid);
            Assert.AreEqual(0, firstBatch.Count);
            Assert.IsFalse(firstBatch.TryRead(0, out _));
        }

        [Test]
        public void InboxRejectsSecondSealForTheSameTick()
        {
            CoCoGraphInstanceId owner = GraphId(210UL);
            CoCoGraphInstanceId source = GraphId(211UL);
            CoCoEventDomainId domain = DomainId(21UL);
            CoCoEventTypeId eventType = EventTypeId(21UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            CoCoTickFrame firstTick = MailboxTick(1UL);
            Assert.IsTrue(inbox.SealForTick(firstTick));

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));
            Assert.IsFalse(inbox.SealForTick(firstTick));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(1UL, 1UL, 2UL)));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(0UL, 1UL, 3UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> first));
            Assert.AreEqual(10, first.Payload.Value);

            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> second));
            Assert.AreEqual(20, second.Payload.Value);
        }

        [Test]
        public void SealedBatchNeverRevivesAfterTimelineReset()
        {
            CoCoGraphInstanceId owner = GraphId(220UL);
            CoCoGraphInstanceId source = GraphId(221UL);
            CoCoEventDomainId domain = DomainId(22UL);
            CoCoEventTypeId eventType = EventTypeId(22UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out CoCoActorEventSealedBatch<TestEvent> oldBatch));
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out CoCoActorEventSealedBatch<TestEvent> alias));
            Assert.IsTrue(oldBatch.IsValid);
            Assert.IsTrue(alias.IsValid);

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsFalse(oldBatch.IsValid);
            Assert.IsTrue(inbox.ResumeAfterTimelineReset());
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL, 2UL, 2UL)));

            Assert.IsFalse(oldBatch.IsValid);
            Assert.IsFalse(oldBatch.TryRead(0, out _));
            Assert.IsFalse(alias.IsValid);
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out CoCoActorEventSealedBatch<TestEvent> newBatch));
            Assert.IsTrue(newBatch.IsValid);
            Assert.IsTrue(newBatch.TryRead(0, out CoCoEventPacket<TestEvent> packet));
            Assert.AreEqual(20, packet.Payload.Value);
        }

        [Test]
        public void DeclaredBroadcastRejectsSourceEchoByDefault()
        {
            CoCoGraphInstanceId owner = GraphId(300UL);
            CoCoEventDomainId domain = DomainId(3UL);
            CoCoEventTypeId eventType = EventTypeId(3UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out CoCoActorEventLaneHandle<TestEvent> handle);

            CoCoEventPacket<TestEvent> echo = Packet(
                eventType,
                domain,
                owner,
                default,
                epoch: 1UL,
                sequence: 1UL,
                value: 1,
                mode: CoCoEventDeliveryMode.DeclaredBroadcast);
            Assert.AreEqual(CoCoInboxEnqueueResult.SourceEchoRejected, inbox.TryEnqueue(handle, echo));

            CoCoEventPacket<TestEvent> external = Packet(
                eventType,
                domain,
                GraphId(301UL),
                default,
                epoch: 1UL,
                sequence: 1UL,
                value: 2,
                mode: CoCoEventDeliveryMode.DeclaredBroadcast);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, external));
        }

        [Test]
        public void UndeclaredBroadcastNeverEntersAGameplayLane()
        {
            CoCoGraphInstanceId owner = GraphId(310UL);
            CoCoEventDomainId domain = DomainId(31UL);
            CoCoEventTypeId declaredType = EventTypeId(31UL);
            CoCoEventTypeId undeclaredType = EventTypeId(32UL);
            var inbox = CreateInbox(
                owner,
                domain,
                declaredType,
                2,
                out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.UndeclaredEventType, inbox.TryEnqueue(
                handle,
                Packet(
                    undeclaredType,
                    domain,
                    GraphId(311UL),
                    default,
                    1UL,
                    1UL,
                    1,
                    CoCoEventDeliveryMode.DeclaredBroadcast)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.AreEqual(0UL, inbox.Counters.Accepted);
        }

        [Test]
        public void InboxRejectsDuplicatesSequenceReuseAndStaleEpochs()
        {
            CoCoGraphInstanceId owner = GraphId(400UL);
            CoCoGraphInstanceId source = GraphId(401UL);
            CoCoEventDomainId domain = DomainId(4UL);
            CoCoEventTypeId firstType = EventTypeId(4UL);
            CoCoEventTypeId secondType = EventTypeId(5UL);
            var inbox = new CoCoActorEventInboxCore(owner, domain, 2, 2, 8);
            Assert.IsTrue(inbox.TryRegisterLane(
                firstType,
                4,
                false,
                out CoCoActorEventLaneHandle<TestEvent> firstHandle,
                out _));
            Assert.IsTrue(inbox.TryRegisterLane(
                secondType,
                4,
                false,
                out CoCoActorEventLaneHandle<OtherTestEvent> secondHandle,
                out _));
            Assert.IsTrue(inbox.Start(out _));

            CoCoEventPacket<TestEvent> original = Packet(
                firstType,
                domain,
                source,
                owner,
                epoch: 2UL,
                sequence: 7UL,
                value: 1);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(firstHandle, original));
            Assert.AreEqual(CoCoInboxEnqueueResult.Duplicate, inbox.TryEnqueue(firstHandle, original));

            CoCoEventPacket<TestEvent> outOfOrder = Packet(
                firstType,
                domain,
                source,
                owner,
                epoch: 2UL,
                sequence: 6UL,
                value: 2);
            Assert.AreEqual(
                CoCoInboxEnqueueResult.EventSequenceConflict,
                inbox.TryEnqueue(firstHandle, outOfOrder));

            CoCoEventPacket<OtherTestEvent> reusedSequence = OtherPacket(
                secondType,
                domain,
                source,
                owner,
                epoch: 2UL,
                sequence: 7UL,
                value: 2);
            Assert.AreEqual(
                CoCoInboxEnqueueResult.EventSequenceConflict,
                inbox.TryEnqueue(secondHandle, reusedSequence));

            CoCoEventPacket<TestEvent> stale = Packet(
                firstType,
                domain,
                source,
                owner,
                epoch: 1UL,
                sequence: 8UL,
                value: 3);
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(firstHandle, stale));
            Assert.AreEqual(1UL, inbox.Counters.Duplicate);
        }

        [Test]
        public void SuspendAccumulatesWhileRewindDropsAndClears()
        {
            CoCoGraphInstanceId owner = GraphId(500UL);
            CoCoGraphInstanceId source = GraphId(501UL);
            CoCoEventDomainId domain = DomainId(5UL);
            CoCoEventTypeId eventType = EventTypeId(6UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.IsTrue(inbox.Suspend());
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 1)));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.AreEqual(CoCoInboxEnqueueResult.RewindOrRestoreDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 2UL, 1UL, 2)));
            Assert.AreEqual(1UL, inbox.Counters.RewindRestoreDropped);
            Assert.IsTrue(inbox.ResumeAfterTimelineReset());
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(2UL, 1UL, 2UL)));

            Assert.AreEqual(CoCoInboxEnqueueResult.EventSequenceConflict, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 3)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 4)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL, 2UL, 2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> resumed));
            Assert.AreEqual(4, resumed.Payload.Value);
        }

        [Test]
        public void ReliableAndUnreliableOverflowHaveDifferentOutcomes()
        {
            CoCoGraphInstanceId owner = GraphId(600UL);
            CoCoGraphInstanceId source = GraphId(601UL);
            CoCoEventDomainId domain = DomainId(6UL);
            CoCoEventTypeId eventType = EventTypeId(7UL);
            var inbox = CreateInbox(owner, domain, eventType, 1, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 1)));
            Assert.AreEqual(CoCoInboxEnqueueResult.UnreliableOverflowDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 2)));
            Assert.AreEqual(CoCoInboxEnqueueResult.ReliableOverflowFaultRequired, inbox.TryEnqueue(
                handle,
                Packet(
                    eventType,
                    domain,
                    source,
                    owner,
                    1UL,
                    3UL,
                    3,
                    reliability: CoCoEventReliability.Reliable)));

            Assert.AreEqual(1UL, inbox.Counters.UnreliableOverflowDropped);
            Assert.AreEqual(1UL, inbox.Counters.ReliableOverflowFaults);
            Assert.IsTrue(inbox.HasReliableOverflowFault);
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> retained));
            Assert.AreEqual(1, retained.Payload.Value);

            Assert.IsTrue(inbox.Suspend());
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 4UL, 4)));
            Assert.AreEqual(CoCoInboxEnqueueResult.UnreliableOverflowDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 5UL, 5)));
            Assert.AreEqual(CoCoInboxEnqueueResult.ReliableOverflowFaultRequired, inbox.TryEnqueue(
                handle,
                Packet(
                    eventType,
                    domain,
                    source,
                    owner,
                    1UL,
                    6UL,
                    6,
                    reliability: CoCoEventReliability.Reliable)));
            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out retained));
            Assert.AreEqual(4, retained.Payload.Value);
        }

        [Test]
        public void StopAndDisposeClearBatchesAndRejectFurtherEvents()
        {
            CoCoGraphInstanceId owner = GraphId(700UL);
            CoCoGraphInstanceId source = GraphId(701UL);
            CoCoEventDomainId domain = DomainId(7UL);
            CoCoEventTypeId eventType = EventTypeId(8UL);
            var inbox = CreateInbox(owner, domain, eventType, 1, out CoCoActorEventLaneHandle<TestEvent> handle);
            CoCoEventPacket<TestEvent> packet = Packet(eventType, domain, source, owner, 1UL, 1UL, 1);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, packet));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                handle,
                out CoCoActorEventSealedBatch<TestEvent> retainedBatch));
            inbox.Stop();
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.IsFalse(retainedBatch.IsValid);
            Assert.IsFalse(retainedBatch.TryRead(0, out _));
            Assert.AreEqual(CoCoInboxEnqueueResult.MailboxUnavailable, inbox.TryEnqueue(handle, packet));

            inbox.Dispose();
            Assert.AreEqual(CoCoActorEventInboxState.Disposed, inbox.State);
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
        }

        [Test]
        public void IntentAndInboxHotPathsAllocateNoManagedMemoryAfterWarmup()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(80UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(80UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> intentHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId intentOwner = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(
                intentOwner,
                1,
                out CoCoIntentFrameRuntime intentRuntime,
                out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                intentHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> sourceRequirement));
            Assert.IsTrue(intentRuntime.TryBindSource(
                sourceRequirement,
                new CountingIntentSource(1),
                out CoCoIntentSourceBinding<TestIntent> sourceBinding,
                out _));
            Assert.IsTrue(intentRuntime.FreezeBindings(out _));
            var headers = new CoCoStateFlowFrameHeader[10100];
            for (int index = 0; index < headers.Length; index++)
            {
                headers[index] = IntentHeader(layoutId, (ulong)index + 1UL, 1UL);
            }

            CoCoGraphInstanceId owner = GraphId(800UL);
            CoCoGraphInstanceId source = GraphId(801UL);
            CoCoEventDomainId domain = DomainId(8UL);
            CoCoEventTypeId eventType = EventTypeId(80UL);
            CoCoFrameLayoutId eventLayoutId = FrameLayoutId(81UL);
            var eventLayout = new CoCoIntentFrameLayout(eventLayoutId, 1);
            Assert.IsTrue(eventLayout.TryRegister(
                IntentId(81UL),
                1,
                new OrderedIntentReducer(),
                out CoCoIntentHandle<TestIntent> projectedHandle,
                out _));
            Assert.IsTrue(eventLayout.Freeze(out _));
            Assert.IsTrue(eventLayout.TryCreateRuntime(
                owner,
                1,
                out CoCoIntentFrameRuntime eventRuntime,
                out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                projectedHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> eventRequirement));
            Assert.IsTrue(eventRuntime.TryBindEventAdapter(
                domain,
                eventType,
                eventRequirement,
                1,
                new FirstEventAdapter(),
                out CoCoEventToIntentBinding<TestEvent, TestIntent> eventBinding,
                out _));
            Assert.IsTrue(eventRuntime.FreezeBindings(out _));
            var eventHeaders = new CoCoStateFlowFrameHeader[10100];
            for (int index = 0; index < eventHeaders.Length; index++)
            {
                eventHeaders[index] = IntentHeader(
                    eventLayoutId,
                    (ulong)index + 1UL,
                    1UL,
                    owner);
            }

            var inbox = new CoCoActorEventInboxCore(owner, domain, 1, 1, 64);
            Assert.IsTrue(inbox.TryRegisterLane(
                eventType,
                1,
                false,
                out CoCoActorEventLaneHandle<TestEvent> eventHandle,
                out _));
            Assert.IsTrue(inbox.TryBindIntentRuntime(eventRuntime, out _));
            Assert.IsTrue(inbox.Start(out _));

            bool succeeded = true;
            for (int index = 0; index < 100; index++)
            {
                succeeded &= RunIntentCycle(
                    intentRuntime,
                    sourceBinding,
                    headers[index],
                    intentHandle);
                succeeded &= RunInboxCycle(
                    inbox,
                    eventHandle,
                    eventType,
                    domain,
                    source,
                    owner,
                    eventRuntime,
                    eventBinding,
                    eventHeaders[index],
                    projectedHandle,
                    (ulong)index + 1UL);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= RunIntentCycle(
                    intentRuntime,
                    sourceBinding,
                    headers[index + 100],
                    intentHandle);
                succeeded &= RunInboxCycle(
                    inbox,
                    eventHandle,
                    eventType,
                    domain,
                    source,
                    owner,
                    eventRuntime,
                    eventBinding,
                    eventHeaders[index + 100],
                    projectedHandle,
                    (ulong)index + 101UL);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, allocated);
        }

        private static CoCoActorEventInboxCore CreateInbox(
            CoCoGraphInstanceId owner,
            CoCoEventDomainId domain,
            CoCoEventTypeId eventType,
            int laneCapacity,
            out CoCoActorEventLaneHandle<TestEvent> handle)
        {
            var inbox = new CoCoActorEventInboxCore(owner, domain, 1, 4, 16);
            Assert.IsTrue(inbox.TryRegisterLane(
                eventType,
                laneCapacity,
                false,
                out handle,
                out CoCoDiagnostic registerDiagnostic));
            Assert.IsTrue(registerDiagnostic.IsNone);
            Assert.IsTrue(inbox.Start(out CoCoDiagnostic startDiagnostic));
            Assert.IsTrue(startDiagnostic.IsNone);
            return inbox;
        }

        private static CoCoActorEventInboxCore CreateProjectionInbox(
            CoCoGraphInstanceId owner,
            CoCoEventDomainId domain,
            CoCoEventTypeId eventType,
            int laneCapacity,
            CoCoIntentFrameRuntime runtime,
            out CoCoActorEventLaneHandle<TestEvent> handle)
        {
            var inbox = new CoCoActorEventInboxCore(owner, domain, 1, 4, 16);
            Assert.IsTrue(inbox.TryRegisterLane(
                eventType,
                laneCapacity,
                false,
                out handle,
                out CoCoDiagnostic registerDiagnostic));
            Assert.IsTrue(registerDiagnostic.IsNone);
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out CoCoDiagnostic bindDiagnostic));
            Assert.IsTrue(bindDiagnostic.IsNone);
            Assert.IsTrue(inbox.Start(out CoCoDiagnostic startDiagnostic));
            Assert.IsTrue(startDiagnostic.IsNone);
            return inbox;
        }

        private static bool RunIntentCycle(
            CoCoIntentFrameRuntime runtime,
            CoCoIntentSourceBinding<TestIntent> binding,
            CoCoStateFlowFrameHeader header,
            CoCoIntentHandle<TestIntent> handle)
        {
            return runtime.TryBegin(header, out _) &&
                   runtime.TrySample(binding, header.TickFrame) ==
                   CoCoIntentSourceSampleResult.Contributed &&
                   runtime.TryFreeze(out _) &&
                   runtime.Frame.TryGet(handle, out TestIntent resolved) &&
                   resolved.Value == 1;
        }

        private static bool RunInboxCycle(
            CoCoActorEventInboxCore inbox,
            CoCoActorEventLaneHandle<TestEvent> handle,
            CoCoEventTypeId eventType,
            CoCoEventDomainId domain,
            CoCoGraphInstanceId source,
            CoCoGraphInstanceId owner,
            CoCoIntentFrameRuntime runtime,
            CoCoEventToIntentBinding<TestEvent, TestIntent> binding,
            CoCoStateFlowFrameHeader header,
            CoCoIntentHandle<TestIntent> intentHandle,
            ulong sequence)
        {
            if (!CoCoEventSequence.TryCreate(sequence, out CoCoEventSequence eventSequence) ||
                !CoCoActorEventEnvelope.TryCreate(
                    eventType,
                    domain,
                    source,
                    owner,
                    new CoCoTimelineEpoch(1UL),
                    new CoCoTimelineTick(sequence),
                    eventSequence,
                    CoCoEventDeliveryMode.Targeted,
                    CoCoEventReliability.Unreliable,
                    default,
                    default,
                    default,
                    out CoCoActorEventEnvelope envelope))
            {
                return false;
            }

            var payload = new TestEvent((int)sequence);
            return CoCoEventPacket<TestEvent>.TryCreate(envelope, payload, out CoCoEventPacket<TestEvent> packet) &&
                   inbox.TryEnqueue(handle, packet) == CoCoInboxEnqueueResult.Accepted &&
                   inbox.SealForTick(header.TickFrame) &&
                   inbox.TryGetSealedBatch(handle, out CoCoActorEventSealedBatch<TestEvent> batch) &&
                   runtime.TryBegin(header, out _) &&
                   runtime.TryProject(binding, batch) == CoCoIntentEventProjectionResult.Contributed &&
                   runtime.TryFreeze(out _) &&
                   runtime.Frame.TryGet(intentHandle, out TestIntent projected) &&
                   projected.Value == (int)sequence;
        }

        private static CoCoStateFlowFrameHeader IntentHeader(
            CoCoFrameLayoutId layoutId,
            ulong tick,
            ulong epoch)
        {
            return IntentHeader(layoutId, tick, epoch, GraphId(900UL));
        }

        private static CoCoStateFlowFrameHeader IntentHeader(
            CoCoFrameLayoutId layoutId,
            ulong tick,
            ulong epoch,
            ulong executionSequence)
        {
            return IntentHeader(
                layoutId,
                tick,
                epoch,
                executionSequence,
                GraphId(900UL));
        }

        private static CoCoStateFlowFrameHeader IntentHeader(
            CoCoFrameLayoutId layoutId,
            ulong tick,
            ulong epoch,
            CoCoGraphInstanceId graphInstanceId)
        {
            return IntentHeader(layoutId, tick, epoch, tick, graphInstanceId);
        }

        private static CoCoStateFlowFrameHeader IntentHeader(
            CoCoFrameLayoutId layoutId,
            ulong tick,
            ulong epoch,
            ulong executionSequence,
            CoCoGraphInstanceId graphInstanceId)
        {
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out CoCoTimelineId timelineId));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(tick, out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out CoCoClockDomainId clockDomainId));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                1d / 60d,
                timelineId,
                position,
                new CoCoTimelineTick(tick),
                clockDomainId,
                new CoCoExecutionSequence(executionSequence),
                new CoCoTimelineEpoch(epoch),
                out CoCoTickFrame tickFrame,
                out _));
            Assert.IsTrue(CoCoStateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                CoCoStateFlowFrameKind.Intent,
                tickFrame,
                out CoCoStateFlowFrameHeader header));
            return header;
        }

        private static CoCoTickFrame MailboxTick(
            ulong tick,
            ulong epoch = 1UL,
            ulong executionSequence = 0UL)
        {
            ulong sequence = executionSequence == 0UL ? tick : executionSequence;
            if (!CoCoTimelineId.TryCreate(1UL, 1UL, out CoCoTimelineId timelineId) ||
                !CoCoTimelinePosition.TryCreate(tick, out CoCoTimelinePosition position) ||
                !CoCoClockDomainId.TryCreate(1UL, out CoCoClockDomainId clockDomainId) ||
                !CoCoTickFrame.TryCreate(
                    1d / 60d,
                    timelineId,
                    position,
                    new CoCoTimelineTick(tick),
                    clockDomainId,
                    new CoCoExecutionSequence(sequence),
                    new CoCoTimelineEpoch(epoch),
                    out CoCoTickFrame tickFrame,
                    out _))
            {
                return default;
            }

            return tickFrame;
        }

        private static CoCoEventPacket<TestEvent> Packet(
            CoCoEventTypeId eventType,
            CoCoEventDomainId domain,
            CoCoGraphInstanceId source,
            CoCoGraphInstanceId target,
            ulong epoch,
            ulong sequence,
            int value,
            CoCoEventDeliveryMode mode = CoCoEventDeliveryMode.Targeted,
            CoCoEventReliability reliability = CoCoEventReliability.Unreliable)
        {
            CoCoActorEventEnvelope envelope = Envelope(
                eventType,
                domain,
                source,
                target,
                epoch,
                sequence,
                mode,
                reliability);
            var payload = new TestEvent(value);
            Assert.IsTrue(CoCoEventPacket<TestEvent>.TryCreate(envelope, payload, out CoCoEventPacket<TestEvent> packet));
            return packet;
        }

        private static CoCoEventPacket<OtherTestEvent> OtherPacket(
            CoCoEventTypeId eventType,
            CoCoEventDomainId domain,
            CoCoGraphInstanceId source,
            CoCoGraphInstanceId target,
            ulong epoch,
            ulong sequence,
            int value)
        {
            CoCoActorEventEnvelope envelope = Envelope(
                eventType,
                domain,
                source,
                target,
                epoch,
                sequence,
                CoCoEventDeliveryMode.Targeted,
                CoCoEventReliability.Unreliable);
            var payload = new OtherTestEvent(value);
            Assert.IsTrue(CoCoEventPacket<OtherTestEvent>.TryCreate(
                envelope,
                payload,
                out CoCoEventPacket<OtherTestEvent> packet));
            return packet;
        }

        private static CoCoActorEventEnvelope Envelope(
            CoCoEventTypeId eventType,
            CoCoEventDomainId domain,
            CoCoGraphInstanceId source,
            CoCoGraphInstanceId target,
            ulong epoch,
            ulong sequence,
            CoCoEventDeliveryMode mode,
            CoCoEventReliability reliability)
        {
            Assert.IsTrue(CoCoEventSequence.TryCreate(sequence, out CoCoEventSequence eventSequence));
            Assert.IsTrue(CoCoActorEventEnvelope.TryCreate(
                eventType,
                domain,
                source,
                target,
                new CoCoTimelineEpoch(epoch),
                new CoCoTimelineTick(10UL),
                eventSequence,
                mode,
                reliability,
                default,
                default,
                default,
                out CoCoActorEventEnvelope envelope));
            return envelope;
        }

        private static CoCoGraphInstanceId GraphId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoFrameLayoutId FrameLayoutId(ulong low)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(1UL, low, out CoCoFrameLayoutId id));
            return id;
        }

        private static CoCoIntentId IntentId(ulong low)
        {
            Assert.IsTrue(CoCoIntentId.TryCreate(2UL, low, out CoCoIntentId id));
            return id;
        }

        private static CoCoEventTypeId EventTypeId(ulong low)
        {
            Assert.IsTrue(CoCoEventTypeId.TryCreate(3UL, low, out CoCoEventTypeId id));
            return id;
        }

        private static CoCoEventDomainId DomainId(ulong value)
        {
            Assert.IsTrue(CoCoEventDomainId.TryCreate(value, out CoCoEventDomainId id));
            return id;
        }

        private readonly struct TestIntent
        {
            public TestIntent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private readonly struct TestEvent
        {
            public TestEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private readonly struct OtherTestEvent
        {
            public OtherTestEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private sealed class OrderedIntentReducer : ICoCoIntentReducer<TestIntent>
        {
            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                return new TestIntent((current.Value * 10) + candidate.Value);
            }
        }

        private sealed class CountingIntentSource : ICoCoIntentFrameSource<TestIntent>
        {
            private readonly int _value;

            public CountingIntentSource(int value)
            {
                _value = value;
            }

            public int SampleCount { get; private set; }

            public bool TrySample(in CoCoTickFrame tickFrame, out TestIntent intent)
            {
                SampleCount++;
                intent = new TestIntent(_value);
                return true;
            }
        }

        private sealed class FirstEventAdapter : ICoCoEventToIntentAdapter<TestEvent, TestIntent>
        {
            public bool TryProject(
                in CoCoEventPacket<TestEvent> packet,
                out TestIntent intent)
            {
                intent = new TestIntent(packet.Payload.Value);
                return true;
            }
        }
    }
}
