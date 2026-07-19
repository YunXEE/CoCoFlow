using System;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoIntentMailboxContractTests
    {
        [Test]
        public void EmptyIntentLayoutFreezesAndProducesAValidEmptyFrame()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(1UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 0);

            Assert.AreEqual(0, layout.Count);
            Assert.AreEqual(0, layout.Capacity);
            Assert.IsTrue(layout.Freeze(out CoCoDiagnostic freezeDiagnostic));
            Assert.IsTrue(freezeDiagnostic.IsNone);
            Assert.IsTrue(layout.TryCreateRuntime(
                GraphId(900UL),
                0,
                out CoCoIntentFrameRuntime runtime,
                out CoCoDiagnostic createDiagnostic),
                createDiagnostic.Message);
            Assert.IsTrue(runtime.FreezeBindings(out CoCoDiagnostic bindingDiagnostic));
            Assert.IsTrue(bindingDiagnostic.IsNone);
            Assert.IsTrue(runtime.TryBegin(
                IntentHeader(layoutId, 1UL, 1UL),
                out CoCoDiagnostic beginDiagnostic),
                beginDiagnostic.Message);
            Assert.IsTrue(runtime.TryFreeze(out CoCoDiagnostic frameDiagnostic));
            Assert.IsTrue(frameDiagnostic.IsNone);
            Assert.IsTrue(runtime.Frame.IsFrozen);

            runtime.Dispose();
        }

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
                (ICoCoIntentReducerFactory<TestIntent, OrderedIntentReducer>)null,
                out CoCoIntentHandle<TestIntent> missingReducerHandle,
                out CoCoDiagnostic missingReducerDiagnostic));
            Assert.IsFalse(missingReducerHandle.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.MissingIntentReducer, missingReducerDiagnostic.Code);

            Assert.IsTrue(layout.TryRegister(
                intentId,
                3,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out CoCoDiagnostic registerDiagnostic));
            Assert.IsTrue(registerDiagnostic.IsNone);
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(layout.Freeze(out CoCoDiagnostic freezeDiagnostic));
            Assert.IsTrue(freezeDiagnostic.IsNone);

            Assert.IsFalse(layout.TryRegister(
                IntentId(11UL),
                1,
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> orderedHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(21UL),
                1,
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(31UL),
                2,
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
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
        public void ReducerFactoryCreatesIndependentValueReducersPerGraphRuntime()
        {
            var factory = new StatefulReducerFactory();
            CoCoFrameLayoutId layoutId = FrameLayoutId(51UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(51UL),
                2,
                factory,
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));

            Assert.IsTrue(layout.TryCreateRuntime(GraphId(951UL), 2, out CoCoIntentFrameRuntime first, out _));
            Assert.IsTrue(layout.TryCreateRuntime(GraphId(952UL), 2, out CoCoIntentFrameRuntime second, out _));
            BindTwoSources(first, handle, out CoCoIntentSourceBinding<TestIntent> firstA,
                out CoCoIntentSourceBinding<TestIntent> firstB);
            BindTwoSources(second, handle, out CoCoIntentSourceBinding<TestIntent> secondA,
                out CoCoIntentSourceBinding<TestIntent> secondB);
            Assert.AreEqual(2, factory.CreateCount);

            CoCoStateFlowFrameHeader firstHeader = IntentHeader(layoutId, 1UL, 1UL, GraphId(951UL));
            Assert.IsTrue(first.TryBegin(firstHeader, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                first.TrySample(firstA, firstHeader.TickFrame));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                first.TrySample(firstB, firstHeader.TickFrame));
            Assert.IsTrue(first.TryFreeze(out _));

            CoCoStateFlowFrameHeader secondHeader = IntentHeader(layoutId, 1UL, 1UL, GraphId(952UL));
            Assert.IsTrue(second.TryBegin(secondHeader, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, second.TrySample(secondA, secondHeader.TickFrame));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, second.TrySample(secondB, secondHeader.TickFrame));
            Assert.IsTrue(second.TryFreeze(out _));

            Assert.IsTrue(first.Frame.TryGet(handle, out TestIntent firstValue));
            Assert.IsTrue(second.Frame.TryGet(handle, out TestIntent secondValue));
            Assert.AreEqual(103, firstValue.Value);
            Assert.AreEqual(103, secondValue.Value);
        }

        [Test]
        public void IntentFrameInvalidatesOnBeginCancelTimelineResetAndDispose()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(52UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(52UL),
                1,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(953UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(7),
                out CoCoIntentSourceBinding<TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            CoCoStateFlowFrameHeader first = IntentHeader(layoutId, 1UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(first, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, runtime.TrySample(binding, first.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.IsFrozen);

            CoCoStateFlowFrameHeader second = IntentHeader(layoutId, 2UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(second, out _));
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsFalse(runtime.Frame.TryGet(handle, out _));
            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsFalse(runtime.TryBegin(second, out _));

            CoCoStateFlowFrameHeader third = IntentHeader(layoutId, 3UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(third, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, runtime.TrySample(binding, third.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));
            var inbox = new CoCoActorEventInboxCore(owner, DomainId(52UL), 1, 1, 4);
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(inbox.Start(out _));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsTrue(inbox.ResumeAfterTimelineReset());

            runtime.Dispose();
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.AreEqual(CoCoActorEventInboxState.Stopped, inbox.State);
        }

        [Test]
        public void RejectedSampleOutsideCollectionPreservesFrozenIntentFrame()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(520UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(520UL),
                1,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(9520UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(7),
                out CoCoIntentSourceBinding<TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            CoCoStateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, runtime.TrySample(binding, header.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent before));

            Assert.AreEqual(
                CoCoIntentSourceSampleResult.ArbiterNotCollecting,
                runtime.TrySample(binding, header.TickFrame));
            Assert.IsTrue(runtime.Frame.IsFrozen);
            Assert.AreEqual(header, runtime.Frame.Header);
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent after));
            Assert.AreEqual(before.Value, after.Value);
        }

        [Test]
        public void InboxStartRequiresLiveFrozenRuntimeAndExactAdapterManifest()
        {
            CoCoEventDomainId domain = DomainId(53UL);
            CoCoEventTypeId eventType = EventTypeId(53UL);

            CoCoGraphInstanceId owner = GraphId(960UL);
            var unbound = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(unbound.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsFalse(unbound.Start(out _));
            Assert.AreEqual(CoCoActorEventInboxState.Created, unbound.State);

            CoCoIntentFrameRuntime unfrozenRuntime = CreateAdapterRuntime(
                owner,
                domain,
                eventType,
                1,
                freezeBindings: false);
            Assert.IsTrue(unbound.TryBindIntentRuntime(unfrozenRuntime, out _));
            Assert.IsFalse(unbound.Start(out CoCoDiagnostic notFrozen));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryNotFrozen, notFrozen.Code);
            Assert.IsTrue(unfrozenRuntime.FreezeBindings(out _));
            Assert.IsTrue(unbound.Start(out _));
            unbound.Stop();

            owner = GraphId(961UL);
            CoCoIntentFrameRuntime missingLaneRuntime = CreateAdapterRuntime(owner, domain, eventType, 1);
            var missingLane = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(missingLane.TryBindIntentRuntime(missingLaneRuntime, out _));
            Assert.IsFalse(missingLane.Start(out _));

            owner = GraphId(962UL);
            CoCoIntentFrameRuntime overflowRuntime = CreateAdapterRuntime(owner, domain, eventType, 1);
            var oversizedLane = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(oversizedLane.TryRegisterLane<TestEvent>(eventType, 2, false, out _, out _));
            Assert.IsTrue(oversizedLane.TryBindIntentRuntime(overflowRuntime, out _));
            Assert.IsFalse(oversizedLane.Start(out _));

            owner = GraphId(963UL);
            CoCoIntentFrameRuntime payloadRuntime = CreateAdapterRuntime(owner, domain, eventType, 1);
            var wrongPayload = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(wrongPayload.TryRegisterLane<OtherTestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(wrongPayload.TryBindIntentRuntime(payloadRuntime, out _));
            Assert.IsFalse(wrongPayload.Start(out _));

            owner = GraphId(964UL);
            CoCoIntentFrameRuntime domainRuntime = CreateAdapterRuntime(owner, domain, eventType, 1);
            var wrongDomain = new CoCoActorEventInboxCore(owner, DomainId(54UL), 1, 1, 4);
            Assert.IsTrue(wrongDomain.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(wrongDomain.TryBindIntentRuntime(domainRuntime, out _));
            Assert.IsFalse(wrongDomain.Start(out _));
        }

        [Test]
        public void AdapterManifestDeduplicatesEventTypeAndUsesSmallestProjectionCapacity()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(65UL);
            CoCoEventDomainId domain = DomainId(65UL);
            CoCoEventTypeId eventType = EventTypeId(65UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                IntentId(65UL),
                3,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> wideHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(66UL),
                1,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> narrowHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                wideHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> wideRequirement));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                narrowHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> narrowRequirement));

            CoCoGraphInstanceId owner = GraphId(978UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 4, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                wideRequirement,
                3,
                new FirstEventAdapter(),
                out _,
                out _));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                narrowRequirement,
                1,
                new FirstEventAdapter(),
                out _,
                out _));
            Assert.IsFalse(runtime.TryBindEventAdapter(
                DomainId(66UL),
                eventType,
                narrowRequirement,
                1,
                new FirstEventAdapter(),
                out _,
                out CoCoDiagnostic domainConflict));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, domainConflict.Code);
            Assert.IsFalse(runtime.TryBindEventAdapter(
                domain,
                eventType,
                narrowRequirement,
                1,
                new OtherEventAdapter(),
                out _,
                out CoCoDiagnostic payloadConflict));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, payloadConflict.Code);
            Assert.AreEqual(2, runtime.BindingCount);
            Assert.IsTrue(runtime.FreezeBindings(out _));

            var exactInbox = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(exactInbox.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(exactInbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(exactInbox.Start(out _));
            exactInbox.Stop();

            var oversizedInbox = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(oversizedInbox.TryRegisterLane<TestEvent>(eventType, 2, false, out _, out _));
            Assert.IsTrue(oversizedInbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsFalse(oversizedInbox.Start(out _));
            Assert.AreEqual(CoCoActorEventInboxState.Created, oversizedInbox.State);
            oversizedInbox.Dispose();

            CoCoGraphInstanceId extraOwner = GraphId(979UL);
            Assert.IsTrue(layout.TryCreateRuntime(extraOwner, 1, out CoCoIntentFrameRuntime extraRuntime, out _));
            Assert.IsTrue(extraRuntime.TryBindEventAdapter(
                domain,
                eventType,
                narrowRequirement,
                1,
                new FirstEventAdapter(),
                out _,
                out _));
            Assert.IsTrue(extraRuntime.FreezeBindings(out _));
            var extraLaneInbox = new CoCoActorEventInboxCore(extraOwner, domain, 2, 1, 4);
            Assert.IsTrue(extraLaneInbox.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(extraLaneInbox.TryRegisterLane<OtherTestEvent>(
                EventTypeId(66UL),
                1,
                false,
                out _,
                out _));
            Assert.IsTrue(extraLaneInbox.TryBindIntentRuntime(extraRuntime, out _));
            Assert.IsFalse(extraLaneInbox.Start(out _));
            Assert.AreEqual(CoCoActorEventInboxState.Created, extraLaneInbox.State);
        }

        [Test]
        public void RuntimeDisposeUnbindsCreatedInboxAndStopsRunningInbox()
        {
            CoCoEventDomainId domain = DomainId(55UL);
            CoCoEventTypeId eventType = EventTypeId(55UL);
            CoCoGraphInstanceId createdOwner = GraphId(965UL);
            CoCoIntentFrameRuntime createdRuntime = CreateAdapterRuntime(createdOwner, domain, eventType, 1);
            var createdInbox = new CoCoActorEventInboxCore(createdOwner, domain, 1, 1, 4);
            Assert.IsTrue(createdInbox.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(createdInbox.TryBindIntentRuntime(createdRuntime, out _));
            createdRuntime.Dispose();
            Assert.AreEqual(CoCoActorEventInboxState.Created, createdInbox.State);
            CoCoIntentFrameRuntime replacement = CreateAdapterRuntime(createdOwner, domain, eventType, 1);
            Assert.IsTrue(createdInbox.TryBindIntentRuntime(replacement, out _));
            Assert.IsTrue(createdInbox.Start(out _));

            CoCoGraphInstanceId runningOwner = GraphId(966UL);
            CoCoIntentFrameRuntime runningRuntime = CreateAdapterRuntime(runningOwner, domain, eventType, 1);
            var runningInbox = new CoCoActorEventInboxCore(runningOwner, domain, 1, 1, 4);
            Assert.IsTrue(runningInbox.TryRegisterLane(
                eventType,
                1,
                false,
                out CoCoActorEventLaneHandle<TestEvent> lane,
                out _));
            Assert.IsTrue(runningInbox.TryBindIntentRuntime(runningRuntime, out _));
            Assert.IsTrue(runningInbox.Start(out _));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, runningInbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(967UL), runningOwner, 1UL, 1UL, 1)));
            runningRuntime.Dispose();
            Assert.AreEqual(CoCoActorEventInboxState.Stopped, runningInbox.State);
            Assert.AreEqual(0, runningInbox.GetSealedCount(lane));
            Assert.AreEqual(CoCoInboxEnqueueResult.MailboxUnavailable, runningInbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(967UL), runningOwner, 1UL, 2UL, 2)));
        }

        [Test]
        public void UserCallbackExceptionsCancelCollectionAndReleaseProjectionClaims()
        {
            CoCoFrameLayoutId sourceLayoutId = FrameLayoutId(56UL);
            var sourceLayout = new CoCoIntentFrameLayout(sourceLayoutId, 1);
            Assert.IsTrue(sourceLayout.TryRegister(
                IntentId(56UL),
                1,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> sourceHandle,
                out _));
            Assert.IsTrue(sourceLayout.Freeze(out _));
            CoCoGraphInstanceId sourceOwner = GraphId(968UL);
            Assert.IsTrue(sourceLayout.TryCreateRuntime(
                sourceOwner,
                1,
                out CoCoIntentFrameRuntime sourceRuntime,
                out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                sourceHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> sourceRequirement));
            var sourceException = new InvalidOperationException("source failure");
            Assert.IsTrue(sourceRuntime.TryBindSource(
                sourceRequirement,
                new ThrowingIntentSource(sourceException),
                out CoCoIntentSourceBinding<TestIntent> sourceBinding,
                out _));
            Assert.IsTrue(sourceRuntime.FreezeBindings(out _));
            CoCoStateFlowFrameHeader sourceHeader = IntentHeader(sourceLayoutId, 1UL, 1UL, sourceOwner);
            Assert.IsTrue(sourceRuntime.TryBegin(sourceHeader, out _));
            Assert.AreSame(sourceException, Assert.Throws<InvalidOperationException>(
                () => sourceRuntime.TrySample(sourceBinding, sourceHeader.TickFrame)));
            Assert.IsFalse(sourceRuntime.IsCollecting);
            Assert.IsFalse(sourceRuntime.Frame.IsFrozen);

            CoCoFrameLayoutId eventLayoutId = FrameLayoutId(57UL);
            var eventLayout = new CoCoIntentFrameLayout(eventLayoutId, 1);
            Assert.IsTrue(eventLayout.TryRegister(
                IntentId(57UL),
                1,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> eventHandle,
                out _));
            Assert.IsTrue(eventLayout.Freeze(out _));
            CoCoGraphInstanceId eventOwner = GraphId(969UL);
            Assert.IsTrue(eventLayout.TryCreateRuntime(eventOwner, 1, out CoCoIntentFrameRuntime eventRuntime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                eventHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> eventRequirement));
            var adapter = new ThrowOnceEventAdapter();
            CoCoEventDomainId domain = DomainId(57UL);
            CoCoEventTypeId eventType = EventTypeId(57UL);
            Assert.IsTrue(eventRuntime.TryBindEventAdapter(
                domain,
                eventType,
                eventRequirement,
                1,
                adapter,
                out CoCoEventToIntentBinding<TestEvent, TestIntent> eventBinding,
                out _));
            Assert.IsTrue(eventRuntime.FreezeBindings(out _));
            var inbox = CreateProjectionInbox(
                eventOwner,
                domain,
                eventType,
                1,
                eventRuntime,
                out CoCoActorEventLaneHandle<TestEvent> lane);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(970UL), eventOwner, 1UL, 1UL, 9)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(lane, out CoCoActorEventSealedBatch<TestEvent> batch));
            CoCoStateFlowFrameHeader firstEventHeader = IntentHeader(eventLayoutId, 1UL, 1UL, eventOwner);
            Assert.IsTrue(eventRuntime.TryBegin(firstEventHeader, out _));
            InvalidOperationException adapterFailure = Assert.Throws<InvalidOperationException>(
                () => eventRuntime.TryProject(eventBinding, batch));
            Assert.AreEqual("adapter failure", adapterFailure.Message);
            Assert.IsFalse(eventRuntime.IsCollecting);

            CoCoStateFlowFrameHeader retryHeader = IntentHeader(eventLayoutId, 2UL, 1UL, eventOwner);
            Assert.IsTrue(eventRuntime.TryBegin(retryHeader, out _));
            Assert.AreEqual(CoCoIntentEventProjectionResult.Contributed,
                eventRuntime.TryProject(eventBinding, batch));
            Assert.IsTrue(eventRuntime.TryFreeze(out _));
            Assert.IsTrue(eventRuntime.Frame.TryGet(eventHandle, out TestIntent retried));
            Assert.AreEqual(9, retried.Value);

            CoCoFrameLayoutId reducerLayoutId = FrameLayoutId(58UL);
            var reducerLayout = new CoCoIntentFrameLayout(reducerLayoutId, 1);
            Assert.IsTrue(reducerLayout.TryRegister(
                IntentId(58UL),
                2,
                new ThrowOnceReducerFactory(),
                out CoCoIntentHandle<TestIntent> reducerHandle,
                out _));
            Assert.IsTrue(reducerLayout.Freeze(out _));
            CoCoGraphInstanceId reducerOwner = GraphId(971UL);
            Assert.IsTrue(reducerLayout.TryCreateRuntime(
                reducerOwner,
                2,
                out CoCoIntentFrameRuntime reducerRuntime,
                out _));
            BindTwoSources(reducerRuntime, reducerHandle,
                out CoCoIntentSourceBinding<TestIntent> reducerA,
                out CoCoIntentSourceBinding<TestIntent> reducerB);
            CoCoStateFlowFrameHeader reducerHeader = IntentHeader(reducerLayoutId, 1UL, 1UL, reducerOwner);
            Assert.IsTrue(reducerRuntime.TryBegin(reducerHeader, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerA, reducerHeader.TickFrame));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerB, reducerHeader.TickFrame));
            Assert.AreEqual("reducer failure", Assert.Throws<InvalidOperationException>(
                () => reducerRuntime.TryFreeze(out _)).Message);
            Assert.IsFalse(reducerRuntime.IsCollecting);

            CoCoStateFlowFrameHeader reducerRetry = IntentHeader(reducerLayoutId, 2UL, 1UL, reducerOwner);
            Assert.IsTrue(reducerRuntime.TryBegin(reducerRetry, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerA, reducerRetry.TickFrame));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerB, reducerRetry.TickFrame));
            Assert.AreEqual("reducer failure", Assert.Throws<InvalidOperationException>(
                () => reducerRuntime.TryFreeze(out _)).Message);
            Assert.IsFalse(reducerRuntime.IsCollecting);
            Assert.IsFalse(reducerRuntime.Frame.IsFrozen);
        }

        [Test]
        public void CallbackReentryIsRejectedAndDisposeIsDeferredUntilCallbackReturns()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(59UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(59UL),
                2,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(972UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 2, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            var source = new ReentrantIntentSource();
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                source,
                out CoCoIntentSourceBinding<TestIntent> binding,
                out _));
            CoCoEventDomainId domain = DomainId(59UL);
            CoCoEventTypeId eventType = EventTypeId(59UL);
            var adapter = new ReentrantEventAdapter();
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                requirement,
                1,
                adapter,
                out CoCoEventToIntentBinding<TestEvent, TestIntent> eventBinding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
            var inbox = CreateProjectionInbox(
                owner,
                domain,
                eventType,
                1,
                runtime,
                out CoCoActorEventLaneHandle<TestEvent> lane);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(980UL), owner, 1UL, 1UL, 2)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                lane,
                out CoCoActorEventSealedBatch<TestEvent> batch));
            source.Runtime = runtime;
            source.ReentrantHeader = IntentHeader(layoutId, 2UL, 1UL, owner);
            source.SourceBinding = binding;
            source.EventBinding = eventBinding;
            source.Batch = batch;
            adapter.Runtime = runtime;
            adapter.SourceBinding = binding;
            adapter.EventBinding = eventBinding;
            adapter.Batch = batch;
            adapter.TickFrame = MailboxTick(1UL);
            CoCoStateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed, runtime.TrySample(binding, header.TickFrame));
            Assert.IsFalse(source.BeginResult);
            Assert.IsFalse(source.FreezeResult);
            Assert.IsFalse(source.CancelResult);
            Assert.AreEqual(CoCoIntentSourceSampleResult.InvalidBinding, source.SampleResult);
            Assert.AreEqual(CoCoIntentEventProjectionResult.InvalidBinding, source.ProjectResult);
            Assert.AreEqual(CoCoIntentEventProjectionResult.Contributed, runtime.TryProject(eventBinding, batch));
            Assert.AreEqual(CoCoIntentSourceSampleResult.InvalidBinding, adapter.SampleResult);
            Assert.AreEqual(CoCoIntentEventProjectionResult.InvalidBinding, adapter.ProjectResult);
            Assert.IsTrue(runtime.TryFreeze(out _));

            Assert.IsTrue(layout.TryCreateRuntime(GraphId(973UL), 1, out CoCoIntentFrameRuntime disposingRuntime,
                out _));
            var disposingSource = new DisposingIntentSource();
            Assert.IsTrue(disposingRuntime.TryBindSource(
                requirement,
                disposingSource,
                out CoCoIntentSourceBinding<TestIntent> disposingBinding,
                out _));
            Assert.IsTrue(disposingRuntime.FreezeBindings(out _));
            disposingSource.Runtime = disposingRuntime;
            CoCoStateFlowFrameHeader disposingHeader = IntentHeader(layoutId, 1UL, 1UL, GraphId(973UL));
            Assert.IsTrue(disposingRuntime.TryBegin(disposingHeader, out _));
            Assert.AreEqual(CoCoIntentSourceSampleResult.InvalidBinding,
                disposingRuntime.TrySample(disposingBinding, disposingHeader.TickFrame));
            Assert.IsTrue(disposingSource.ReturnedFromDispose);
            Assert.IsTrue(disposingRuntime.IsDisposed);
            Assert.IsFalse(disposingRuntime.IsCollecting);
            Assert.IsFalse(disposingRuntime.Frame.IsFrozen);
        }

        [Test]
        public void InboxBindingAndLifecycleTransitionsCannotLeaveACollectingRuntimeHalfBound()
        {
            CoCoGraphInstanceId owner = GraphId(974UL);
            CoCoEventDomainId domain = DomainId(60UL);
            CoCoEventTypeId eventType = EventTypeId(60UL);
            CoCoIntentFrameRuntime runtime = CreateAdapterRuntime(owner, domain, eventType, 1);
            var inbox = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(inbox.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 1UL, 1UL, owner), out _));
            Assert.IsFalse(inbox.Start(out _));
            Assert.AreEqual(CoCoActorEventInboxState.Created, inbox.State);
            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsTrue(inbox.Start(out _));

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 2UL, 1UL, owner), out _));
            inbox.Stop();
            Assert.IsFalse(runtime.IsCollecting);
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.AreEqual(CoCoActorEventInboxState.Stopped, inbox.State);

            var replacement = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(replacement.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 3UL, 1UL, owner), out _));
            Assert.IsFalse(replacement.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsTrue(replacement.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(replacement.Start(out _));

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 4UL, 1UL, owner), out _));
            replacement.Dispose();
            Assert.IsFalse(runtime.IsCollecting);
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.AreEqual(CoCoActorEventInboxState.Disposed, replacement.State);
        }

        [Test]
        public void InboxCannotResealSuspendOrResumeWhileIntentRuntimeIsCollecting()
        {
            CoCoGraphInstanceId owner = GraphId(981UL);
            CoCoGraphInstanceId source = GraphId(982UL);
            CoCoEventDomainId domain = DomainId(67UL);
            CoCoEventTypeId eventType = EventTypeId(67UL);
            CoCoIntentFrameRuntime runtime = CreateAdapterRuntime(owner, domain, eventType, 2);
            var inbox = CreateProjectionInbox(
                owner,
                domain,
                eventType,
                2,
                runtime,
                out CoCoActorEventLaneHandle<TestEvent> lane);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 1)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                lane,
                out CoCoActorEventSealedBatch<TestEvent> firstBatch));
            Assert.IsTrue(firstBatch.TryRead(0, out CoCoEventPacket<TestEvent> firstPacket));
            Assert.AreEqual(1, firstPacket.Payload.Value);

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 1UL, 1UL, owner), out _));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 2)));
            Assert.IsFalse(inbox.Suspend());
            Assert.IsFalse(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(firstBatch.IsValid);
            Assert.AreEqual(1, firstBatch.Count);
            Assert.IsTrue(firstBatch.TryRead(0, out firstPacket));
            Assert.AreEqual(1, firstPacket.Payload.Value);

            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsTrue(inbox.Suspend());
            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 2UL, 1UL, owner), out _));
            Assert.IsFalse(inbox.Resume());
            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsFalse(firstBatch.IsValid);
            Assert.IsTrue(inbox.TryGetSealedBatch(
                lane,
                out CoCoActorEventSealedBatch<TestEvent> secondBatch));
            Assert.AreEqual(1, secondBatch.Count);
            Assert.IsTrue(secondBatch.TryRead(0, out CoCoEventPacket<TestEvent> secondPacket));
            Assert.AreEqual(2, secondPacket.Payload.Value);
        }

        [Test]
        public void AdapterInboxLifecycleRequestsCancelProjectionWithoutHalfUnbinding()
        {
            AssertAdapterInboxLifecycleRequest(dispose: false);
            AssertAdapterInboxLifecycleRequest(dispose: true);
        }

        [Test]
        public void ReducerDisposeRequestStopsLaterReducersAndInvalidatesPartialFrame()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(62UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                IntentId(62UL),
                2,
                new DisposingReducerFactory(),
                out CoCoIntentHandle<TestIntent> disposingHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(63UL),
                2,
                new CountingReducerFactory(),
                out CoCoIntentHandle<TestIntent> countingHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            CoCoGraphInstanceId owner = GraphId(976UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 4, out CoCoIntentFrameRuntime runtime, out _));
            BindTwoSourcesWithoutFreezing(runtime, disposingHandle,
                out CoCoIntentSourceBinding<TestIntent> disposingA,
                out CoCoIntentSourceBinding<TestIntent> disposingB);
            BindTwoSourcesWithoutFreezing(runtime, countingHandle,
                out CoCoIntentSourceBinding<TestIntent> countingA,
                out CoCoIntentSourceBinding<TestIntent> countingB);
            Assert.IsTrue(runtime.FreezeBindings(out _));

            DisposingIntentReducer.Runtime = runtime;
            DisposingIntentReducer.ReduceCount = 0;
            CountingIntentReducer.ReduceCount = 0;
            try
            {
                CoCoStateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL, owner);
                Assert.IsTrue(runtime.TryBegin(header, out _));
                Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                    runtime.TrySample(disposingA, header.TickFrame));
                Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                    runtime.TrySample(disposingB, header.TickFrame));
                Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                    runtime.TrySample(countingA, header.TickFrame));
                Assert.AreEqual(CoCoIntentSourceSampleResult.Contributed,
                    runtime.TrySample(countingB, header.TickFrame));

                Assert.IsFalse(runtime.TryFreeze(out CoCoDiagnostic diagnostic));
                Assert.AreEqual(CoCoDiagnosticCode.InvalidLifecycleTransition, diagnostic.Code);
                Assert.IsTrue(runtime.IsDisposed);
                Assert.IsFalse(runtime.IsCollecting);
                Assert.IsFalse(runtime.Frame.IsFrozen);
                Assert.AreEqual(1, DisposingIntentReducer.ReduceCount);
                Assert.AreEqual(0, CountingIntentReducer.ReduceCount);
            }
            finally
            {
                DisposingIntentReducer.Runtime = null;
                DisposingIntentReducer.ReduceCount = 0;
                CountingIntentReducer.ReduceCount = 0;
            }
        }

        [Test]
        public void ReducerFactoryReentryForTheSameGraphReturnsDeterministicDiagnostic()
        {
            CoCoFrameLayoutId layoutId = FrameLayoutId(64UL);
            CoCoGraphInstanceId owner = GraphId(977UL);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            var factory = new ReentrantReducerFactory(layout, owner);
            Assert.IsTrue(layout.TryRegister(
                IntentId(64UL),
                1,
                factory,
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(layout.Freeze(out _));

            Assert.IsTrue(layout.TryCreateRuntime(owner, 0, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsFalse(factory.ReentrantCreateResult);
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, factory.ReentrantDiagnostic.Code);
            runtime.Dispose();
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
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> firstHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(61UL),
                1,
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
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
            Assert.IsFalse(runtime.TryBindEventAdapter(
                foreignDomain,
                eventType,
                secondRequirement,
                1,
                new FirstEventAdapter(),
                out _,
                out CoCoDiagnostic conflictingManifestDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, conflictingManifestDiagnostic.Code);
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
            var layout = new CoCoIntentFrameLayout(FrameLayoutId(405UL), 2);
            Assert.IsTrue(layout.TryRegister(
                IntentId(405UL),
                4,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> firstIntent,
                out _));
            Assert.IsTrue(layout.TryRegister(
                IntentId(406UL),
                4,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> secondIntent,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            Assert.IsTrue(layout.TryCreateRuntime(owner, 2, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                firstIntent,
                0,
                out CoCoIntentSourceRequirement<TestIntent> firstRequirement));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                secondIntent,
                0,
                out CoCoIntentSourceRequirement<TestIntent> secondRequirement));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                firstType,
                firstRequirement,
                4,
                new FirstEventAdapter(),
                out _,
                out _));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                secondType,
                secondRequirement,
                4,
                new OtherEventAdapter(),
                out _,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));
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
        public void SuspendedInboxRejectsRewindBeginAndPreservesLegalBacklog()
        {
            CoCoGraphInstanceId owner = GraphId(510UL);
            CoCoGraphInstanceId source = GraphId(511UL);
            CoCoEventDomainId domain = DomainId(51UL);
            CoCoEventTypeId eventType = EventTypeId(51UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.IsTrue(inbox.Suspend());
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 11)));
            Assert.IsFalse(inbox.BeginRewindOrRestore());
            Assert.AreEqual(CoCoActorEventInboxState.Suspended, inbox.State);

            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> packet));
            Assert.AreEqual(11, packet.Payload.Value);
        }

        [Test]
        public void RewindCancelKeepsEpochAndNeverRevivesClearedBacklog()
        {
            CoCoGraphInstanceId owner = GraphId(520UL);
            CoCoGraphInstanceId source = GraphId(521UL);
            CoCoEventDomainId domain = DomainId(52UL);
            CoCoEventTypeId eventType = EventTypeId(52UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out CoCoActorEventSealedBatch<TestEvent> oldBatch));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsFalse(oldBatch.IsValid);
            Assert.IsTrue(inbox.CanCancelRewindOrRestore);
            Assert.AreEqual(CoCoInboxEnqueueResult.RewindOrRestoreDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));

            inbox.CancelRewindOrRestoreNoFail();
            Assert.AreEqual(CoCoActorEventInboxState.Running, inbox.State);
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.AreEqual(1UL, inbox.Counters.RewindRestoreDropped);
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 30)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL, 1UL, 2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out CoCoEventPacket<TestEvent> resumed));
            Assert.AreEqual(30, resumed.Payload.Value);
        }

        [Test]
        public void TimelineResetPreflightAndNoFailCompletionRequireNewEpoch()
        {
            CoCoGraphInstanceId owner = GraphId(530UL);
            CoCoGraphInstanceId source = GraphId(531UL);
            CoCoEventDomainId domain = DomainId(53UL);
            CoCoEventTypeId eventType = EventTypeId(53UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsTrue(inbox.CanResumeAfterTimelineReset);
            inbox.ResumeAfterTimelineResetNoFail();
            Assert.AreEqual(CoCoActorEventInboxState.Running, inbox.State);
            Assert.IsFalse(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL, 2UL, 2UL)));
        }

        [Test]
        public void TimelineResetOwnerEpochBarrierRejectsSeenAndUnseenOldOwnerPackets()
        {
            CoCoGraphInstanceId owner = GraphId(540UL);
            CoCoEventDomainId domain = DomainId(54UL);
            CoCoEventTypeId eventType = EventTypeId(54UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);
            CoCoEventPacket<TestEvent> seen = Packet(
                eventType,
                domain,
                owner,
                owner,
                1UL,
                1UL,
                10);
            CoCoEventPacket<TestEvent> unseen = Packet(
                eventType,
                domain,
                owner,
                owner,
                1UL,
                2UL,
                20);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, seen));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new CoCoTimelineEpoch(2UL));

            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(handle, seen));
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(handle, unseen));
            Assert.AreEqual(CoCoInboxEnqueueResult.InvalidPacket, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 3UL, 30)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 2UL, 3UL, 40)));
        }

        [Test]
        public void TimelineResetOwnerEpochBarrierLeavesRemoteSourceWatermarksUnchanged()
        {
            CoCoGraphInstanceId owner = GraphId(550UL);
            CoCoGraphInstanceId remote = GraphId(551UL);
            CoCoEventDomainId domain = DomainId(55UL);
            CoCoEventTypeId eventType = EventTypeId(55UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 7UL, 1UL, 10)));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new CoCoTimelineEpoch(2UL));

            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 6UL, 2UL, 15)));
            Assert.AreEqual(CoCoInboxEnqueueResult.EventSequenceConflict, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 7UL, 1UL, 15)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 7UL, 2UL, 20)));
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 1UL, 1UL, 30)));
        }

        [Test]
        public void TimelineResetRebasesOwnerWatermarkWithoutChangingRemoteWatermarks()
        {
            CoCoGraphInstanceId owner = GraphId(570UL);
            CoCoGraphInstanceId firstRemote = GraphId(571UL);
            CoCoGraphInstanceId secondRemote = GraphId(572UL);
            CoCoEventDomainId domain = DomainId(57UL);
            CoCoEventTypeId eventType = EventTypeId(57UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, firstRemote, owner, 7UL, 1UL, 10)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 9UL, 1UL, 20)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, secondRemote, owner, 5UL, 1UL, 30)));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new CoCoTimelineEpoch(2UL));

            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 2UL, 2UL, 40)));
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 1UL, 3UL, 50)));
            Assert.AreEqual(CoCoInboxEnqueueResult.InvalidPacket, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 3UL, 60)));
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, firstRemote, owner, 6UL, 2UL, 65)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, firstRemote, owner, 7UL, 2UL, 70)));
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, secondRemote, owner, 4UL, 2UL, 75)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, secondRemote, owner, 5UL, 2UL, 80)));
        }

        [Test]
        public void OwnerEpochBarrierOnlyChangesOnConfirmedTimelineReset()
        {
            CoCoGraphInstanceId owner = GraphId(560UL);
            CoCoEventDomainId domain = DomainId(56UL);
            CoCoEventTypeId eventType = EventTypeId(56UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out CoCoActorEventLaneHandle<TestEvent> handle);

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new CoCoTimelineEpoch(2UL));
            Assert.IsTrue(inbox.Suspend());
            Assert.IsTrue(inbox.Resume());
            Assert.AreEqual(CoCoInboxEnqueueResult.InvalidPacket, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 1UL, 10)));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsTrue(inbox.CancelRewindOrRestore());
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 1UL, 1UL, 20)));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new CoCoTimelineEpoch(3UL));
            Assert.AreEqual(CoCoInboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 2UL, 1UL, 30)));
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 1UL, 40)));
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
                new OrderedIntentReducerFactory(),
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
                new OrderedIntentReducerFactory(),
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
            var layout = new CoCoIntentFrameLayout(FrameLayoutId(eventType.Low + 1000UL), 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(eventType.Low + 1000UL),
                laneCapacity,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> intentHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                intentHandle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                requirement,
                laneCapacity,
                new FirstEventAdapter(),
                out _,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            var inbox = new CoCoActorEventInboxCore(owner, domain, 1, 4, 16);
            Assert.IsTrue(inbox.TryRegisterLane(
                eventType,
                laneCapacity,
                false,
                out handle,
                out CoCoDiagnostic registerDiagnostic));
            Assert.IsTrue(registerDiagnostic.IsNone);
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(inbox.Start(out CoCoDiagnostic startDiagnostic));
            Assert.IsTrue(startDiagnostic.IsNone);
            return inbox;
        }

        private static void AssertAdapterInboxLifecycleRequest(bool dispose)
        {
            ulong suffix = dispose ? 611UL : 610UL;
            CoCoFrameLayoutId layoutId = FrameLayoutId(suffix);
            CoCoGraphInstanceId owner = GraphId(9600UL + suffix);
            CoCoEventDomainId domain = DomainId(suffix);
            CoCoEventTypeId eventType = EventTypeId(suffix);
            var layout = new CoCoIntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(suffix),
                1,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            var adapter = new InboxLifecycleEventAdapter(dispose);
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                requirement,
                1,
                adapter,
                out CoCoEventToIntentBinding<TestEvent, TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            var inbox = new CoCoActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(inbox.TryRegisterLane(
                eventType,
                1,
                false,
                out CoCoActorEventLaneHandle<TestEvent> lane,
                out _));
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(inbox.Start(out _));
            adapter.Inbox = inbox;
            Assert.AreEqual(CoCoInboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(9700UL + suffix), owner, 1UL, 1UL, 5)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                lane,
                out CoCoActorEventSealedBatch<TestEvent> batch));
            Assert.IsTrue(runtime.TryBegin(IntentHeader(layoutId, 1UL, 1UL, owner), out _));

            Assert.AreEqual(
                CoCoIntentEventProjectionResult.ArbiterNotCollecting,
                runtime.TryProject(binding, batch));
            Assert.IsTrue(adapter.ReturnedFromLifecycleRequest);
            Assert.AreEqual(CoCoActorEventInboxState.Running, adapter.StateDuringCallback);
            Assert.IsFalse(adapter.SealDuringCallbackResult);
            Assert.IsFalse(runtime.IsCollecting);
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsFalse(batch.IsValid);
            Assert.AreEqual(
                dispose ? CoCoActorEventInboxState.Disposed : CoCoActorEventInboxState.Stopped,
                inbox.State);
            Assert.AreEqual(CoCoInboxEnqueueResult.MailboxUnavailable, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(9700UL + suffix), owner, 1UL, 2UL, 6)));
        }

        private static void BindTwoSources(
            CoCoIntentFrameRuntime runtime,
            CoCoIntentHandle<TestIntent> handle,
            out CoCoIntentSourceBinding<TestIntent> first,
            out CoCoIntentSourceBinding<TestIntent> second)
        {
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(1),
                out first,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(2),
                out second,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
        }

        private static void BindTwoSourcesWithoutFreezing(
            CoCoIntentFrameRuntime runtime,
            CoCoIntentHandle<TestIntent> handle,
            out CoCoIntentSourceBinding<TestIntent> first,
            out CoCoIntentSourceBinding<TestIntent> second)
        {
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(1),
                out first,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(2),
                out second,
                out _));
        }

        private static CoCoIntentFrameRuntime CreateAdapterRuntime(
            CoCoGraphInstanceId owner,
            CoCoEventDomainId domain,
            CoCoEventTypeId eventType,
            int projectionCapacity,
            bool freezeBindings = true)
        {
            var layout = new CoCoIntentFrameLayout(
                FrameLayoutId(owner.Value + eventType.Low + 2000UL),
                1);
            Assert.IsTrue(layout.TryRegister(
                IntentId(owner.Value + eventType.Low + 2000UL),
                projectionCapacity,
                new OrderedIntentReducerFactory(),
                out CoCoIntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out CoCoIntentFrameRuntime runtime, out _));
            Assert.IsTrue(CoCoIntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out CoCoIntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindEventAdapter(
                domain,
                eventType,
                requirement,
                projectionCapacity,
                new FirstEventAdapter(),
                out _,
                out _));
            if (freezeBindings)
            {
                Assert.IsTrue(runtime.FreezeBindings(out _));
            }

            return runtime;
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

        private readonly struct OrderedIntentReducer : ICoCoIntentReducer<TestIntent>
        {
            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                return new TestIntent((current.Value * 10) + candidate.Value);
            }
        }

        private sealed class OrderedIntentReducerFactory :
            ICoCoIntentReducerFactory<TestIntent, OrderedIntentReducer>
        {
            public OrderedIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
            {
                return new OrderedIntentReducer();
            }
        }

        private struct StatefulIntentReducer : ICoCoIntentReducer<TestIntent>
        {
            private int _reductionCount;

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                _reductionCount++;
                return new TestIntent(current.Value + candidate.Value + (_reductionCount * 100));
            }
        }

        private sealed class StatefulReducerFactory :
            ICoCoIntentReducerFactory<TestIntent, StatefulIntentReducer>
        {
            public int CreateCount { get; private set; }

            public StatefulIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
            {
                CreateCount++;
                return default;
            }
        }

        private struct ThrowOnceIntentReducer : ICoCoIntentReducer<TestIntent>
        {
            private bool _hasThrown;

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                if (!_hasThrown)
                {
                    _hasThrown = true;
                    throw new InvalidOperationException("reducer failure");
                }

                return new TestIntent(current.Value + candidate.Value);
            }
        }

        private sealed class ThrowOnceReducerFactory :
            ICoCoIntentReducerFactory<TestIntent, ThrowOnceIntentReducer>
        {
            public ThrowOnceIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
            {
                return default;
            }
        }

        private struct DisposingIntentReducer : ICoCoIntentReducer<TestIntent>
        {
            public static CoCoIntentFrameRuntime Runtime { get; set; }
            public static int ReduceCount { get; set; }

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                ReduceCount++;
                Runtime.Dispose();
                return new TestIntent(current.Value + candidate.Value);
            }
        }

        private sealed class DisposingReducerFactory :
            ICoCoIntentReducerFactory<TestIntent, DisposingIntentReducer>
        {
            public DisposingIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
            {
                return default;
            }
        }

        private struct CountingIntentReducer : ICoCoIntentReducer<TestIntent>
        {
            public static int ReduceCount { get; set; }

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                ReduceCount++;
                return new TestIntent(current.Value + candidate.Value);
            }
        }

        private sealed class CountingReducerFactory :
            ICoCoIntentReducerFactory<TestIntent, CountingIntentReducer>
        {
            public CountingIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
            {
                return default;
            }
        }

        private sealed class ReentrantReducerFactory :
            ICoCoIntentReducerFactory<TestIntent, OrderedIntentReducer>
        {
            private readonly CoCoIntentFrameLayout _layout;
            private readonly CoCoGraphInstanceId _graphInstanceId;

            public ReentrantReducerFactory(
                CoCoIntentFrameLayout layout,
                CoCoGraphInstanceId graphInstanceId)
            {
                _layout = layout;
                _graphInstanceId = graphInstanceId;
            }

            public bool ReentrantCreateResult { get; private set; }
            public CoCoDiagnostic ReentrantDiagnostic { get; private set; }

            public OrderedIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
            {
                ReentrantCreateResult = _layout.TryCreateRuntime(
                    _graphInstanceId,
                    0,
                    out CoCoIntentFrameRuntime nested,
                    out CoCoDiagnostic diagnostic);
                ReentrantDiagnostic = diagnostic;
                nested?.Dispose();
                return default;
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

        private sealed class ThrowingIntentSource : ICoCoIntentFrameSource<TestIntent>
        {
            private readonly InvalidOperationException _exception;

            public ThrowingIntentSource(InvalidOperationException exception)
            {
                _exception = exception;
            }

            public bool TrySample(in CoCoTickFrame tickFrame, out TestIntent intent)
            {
                intent = default;
                throw _exception;
            }
        }

        private sealed class ReentrantIntentSource : ICoCoIntentFrameSource<TestIntent>
        {
            public CoCoIntentFrameRuntime Runtime { get; set; }
            public CoCoStateFlowFrameHeader ReentrantHeader { get; set; }
            public CoCoIntentSourceBinding<TestIntent> SourceBinding { get; set; }
            public CoCoEventToIntentBinding<TestEvent, TestIntent> EventBinding { get; set; }
            public CoCoActorEventSealedBatch<TestEvent> Batch { get; set; }
            public bool BeginResult { get; private set; }
            public bool FreezeResult { get; private set; }
            public bool CancelResult { get; private set; }
            public CoCoIntentSourceSampleResult SampleResult { get; private set; }
            public CoCoIntentEventProjectionResult ProjectResult { get; private set; }

            public bool TrySample(in CoCoTickFrame tickFrame, out TestIntent intent)
            {
                BeginResult = Runtime.TryBegin(ReentrantHeader, out _);
                FreezeResult = Runtime.TryFreeze(out _);
                CancelResult = Runtime.CancelCollection();
                SampleResult = Runtime.TrySample(SourceBinding, tickFrame);
                ProjectResult = Runtime.TryProject(EventBinding, Batch);
                intent = new TestIntent(1);
                return true;
            }
        }

        private sealed class DisposingIntentSource : ICoCoIntentFrameSource<TestIntent>
        {
            public CoCoIntentFrameRuntime Runtime { get; set; }
            public bool ReturnedFromDispose { get; private set; }

            public bool TrySample(in CoCoTickFrame tickFrame, out TestIntent intent)
            {
                Runtime.Dispose();
                ReturnedFromDispose = !Runtime.IsDisposed;
                intent = new TestIntent(1);
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

        private sealed class ReentrantEventAdapter :
            ICoCoEventToIntentAdapter<TestEvent, TestIntent>
        {
            public CoCoIntentFrameRuntime Runtime { get; set; }
            public CoCoIntentSourceBinding<TestIntent> SourceBinding { get; set; }
            public CoCoEventToIntentBinding<TestEvent, TestIntent> EventBinding { get; set; }
            public CoCoActorEventSealedBatch<TestEvent> Batch { get; set; }
            public CoCoTickFrame TickFrame { get; set; }
            public CoCoIntentSourceSampleResult SampleResult { get; private set; }
            public CoCoIntentEventProjectionResult ProjectResult { get; private set; }

            public bool TryProject(
                in CoCoEventPacket<TestEvent> packet,
                out TestIntent intent)
            {
                SampleResult = Runtime.TrySample(SourceBinding, TickFrame);
                ProjectResult = Runtime.TryProject(EventBinding, Batch);
                intent = new TestIntent(packet.Payload.Value);
                return true;
            }
        }

        private sealed class InboxLifecycleEventAdapter :
            ICoCoEventToIntentAdapter<TestEvent, TestIntent>
        {
            private readonly bool _dispose;

            public InboxLifecycleEventAdapter(bool dispose)
            {
                _dispose = dispose;
            }

            public CoCoActorEventInboxCore Inbox { get; set; }
            public bool ReturnedFromLifecycleRequest { get; private set; }
            public CoCoActorEventInboxState StateDuringCallback { get; private set; }
            public bool SealDuringCallbackResult { get; private set; }

            public bool TryProject(in CoCoEventPacket<TestEvent> packet, out TestIntent intent)
            {
                if (_dispose)
                {
                    Inbox.Dispose();
                }
                else
                {
                    Inbox.Stop();
                }

                ReturnedFromLifecycleRequest = true;
                StateDuringCallback = Inbox.State;
                SealDuringCallbackResult = Inbox.SealForTick(MailboxTick(2UL));
                intent = new TestIntent(packet.Payload.Value);
                return true;
            }
        }

        private sealed class ThrowOnceEventAdapter : ICoCoEventToIntentAdapter<TestEvent, TestIntent>
        {
            private bool _hasThrown;

            public bool TryProject(in CoCoEventPacket<TestEvent> packet, out TestIntent intent)
            {
                if (!_hasThrown)
                {
                    _hasThrown = true;
                    intent = default;
                    throw new InvalidOperationException("adapter failure");
                }

                intent = new TestIntent(packet.Payload.Value);
                return true;
            }
        }

        private sealed class OtherEventAdapter : ICoCoEventToIntentAdapter<OtherTestEvent, TestIntent>
        {
            public bool TryProject(
                in CoCoEventPacket<OtherTestEvent> packet,
                out TestIntent intent)
            {
                intent = new TestIntent(packet.Payload.Value);
                return true;
            }
        }
    }
}
