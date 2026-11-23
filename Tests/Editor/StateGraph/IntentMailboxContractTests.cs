using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class IntentMailboxContractTests
    {
        [Test]
        public void EmptyIntentLayoutFreezesAndProducesAValidEmptyFrame()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(1UL);
            var layout = new IntentFrameLayout(layoutId, 0);

            Assert.AreEqual(0, layout.Count);
            Assert.AreEqual(0, layout.Capacity);
            Assert.IsTrue(layout.Freeze(out CoCoDiagnostic freezeDiagnostic));
            Assert.IsTrue(freezeDiagnostic.IsNone);
            Assert.IsTrue(layout.TryCreateRuntime(
                GraphId(900UL),
                0,
                out IntentFrameRuntime runtime,
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
            Assert.AreEqual(EventDeliveryMode.None, default(EventDeliveryMode));
            Assert.AreEqual(EventReliability.None, default(EventReliability));
            Assert.AreEqual(ActorEventInboxState.None, default(ActorEventInboxState));
            Assert.AreEqual(InboxEnqueueResult.None, default(InboxEnqueueResult));
            Assert.AreEqual(IntentContributionResult.None, default(IntentContributionResult));
            Assert.IsFalse(default(ActorEventEnvelope).IsValid);
            Assert.IsFalse(default(EventPacket<TestEvent>).IsValid);
        }

        [Test]
        public void IntentLayoutRequiresReducerAndFreezesRegistration()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(1UL);
            IntentId intentId = CreateIntentId(10UL);
            var layout = new IntentFrameLayout(layoutId, 1);

            Assert.IsFalse(layout.TryRegister(
                intentId,
                2,
                (IIntentReducerFactory<TestIntent, OrderedIntentReducer>)null,
                out IntentHandle<TestIntent> missingReducerHandle,
                out CoCoDiagnostic missingReducerDiagnostic));
            Assert.IsFalse(missingReducerHandle.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.MissingIntentReducer, missingReducerDiagnostic.Code);

            Assert.IsTrue(layout.TryRegister(
                intentId,
                3,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> handle,
                out CoCoDiagnostic registerDiagnostic));
            Assert.IsTrue(registerDiagnostic.IsNone);
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(layout.Freeze(out CoCoDiagnostic freezeDiagnostic));
            Assert.IsTrue(freezeDiagnostic.IsNone);

            Assert.IsFalse(layout.TryRegister(
                CreateIntentId(11UL),
                1,
                new OrderedIntentReducerFactory(),
                out _,
                out CoCoDiagnostic frozenDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, frozenDiagnostic.Code);
        }

        [Test]
        public void IntentArbitrationUsesPresenceAndDeterministicPriorityOrder()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(2UL);
            var layout = new IntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(20UL),
                3,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> orderedHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(21UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> absentHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));

            GraphInstanceId graphInstanceId = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(
                graphInstanceId,
                3,
                out IntentFrameRuntime runtime,
                out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                orderedHandle,
                10,
                out IntentSourceRequirement<TestIntent> highRequirement));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                orderedHandle,
                0,
                out IntentSourceRequirement<TestIntent> lowRequirement));
            Assert.IsTrue(runtime.TryBindSource(
                highRequirement,
                new CountingIntentSource(3),
                out IntentSourceBinding<TestIntent> highEarlier,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                lowRequirement,
                new CountingIntentSource(1),
                out IntentSourceBinding<TestIntent> low,
                out _));
            Assert.IsTrue(runtime.TryBindSource(
                highRequirement,
                new CountingIntentSource(2),
                out IntentSourceBinding<TestIntent> highLater,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
            StateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, runtime.TrySample(low, header.TickFrame));
            Assert.AreEqual(
                IntentSourceSampleResult.Contributed,
                runtime.TrySample(highLater, header.TickFrame));
            Assert.AreEqual(
                IntentSourceSampleResult.Contributed,
                runtime.TrySample(highEarlier, header.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));

            Assert.IsTrue(runtime.Frame.TryGet(orderedHandle, out TestIntent resolved));
            Assert.AreEqual(321, resolved.Value);
            Assert.IsFalse(runtime.Frame.IsPresent(absentHandle));
            Assert.IsFalse(runtime.Frame.TryGet(absentHandle, out _));
            Assert.AreEqual(header, runtime.Frame.Header);
        }

        [Test]
        public void IntentSourceBindingSamplesAtMostOncePerStartedTick()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(5UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(31UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                priority: 10,
                out IntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(layout.Freeze(out _));

            var separateLayout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(separateLayout.TryRegister(
                CreateIntentId(31UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> foreignHandle,
                out _));
            Assert.IsTrue(separateLayout.Freeze(out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                foreignHandle,
                priority: 10,
                out IntentSourceRequirement<TestIntent> foreignRequirement));

            var source = new CountingIntentSource(7);
            GraphInstanceId graphInstanceId = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(
                graphInstanceId,
                2,
                out IntentFrameRuntime runtime,
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
                out IntentSourceBinding<TestIntent> binding,
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
            StateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL);

            Assert.AreEqual(
                IntentSourceSampleResult.ArbiterNotCollecting,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(0, source.SampleCount);

            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(
                IntentSourceSampleResult.Contributed,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(
                IntentSourceSampleResult.AlreadySampled,
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
                IntentSourceSampleResult.Contributed,
                runtime.TrySample(binding, header.TickFrame));
            Assert.AreEqual(2, source.SampleCount);
            Assert.IsTrue(runtime.TryFreeze(out _));

            runtime.Dispose();
            Assert.IsTrue(runtime.IsDisposed);
            Assert.IsFalse(runtime.TryBegin(IntentHeader(layoutId, 3UL, 1UL), out _));
            Assert.IsTrue(layout.TryCreateRuntime(graphInstanceId, 0, out IntentFrameRuntime replacement, out _));
            replacement.Dispose();
        }

        [Test]
        public void ReducerFactoryCreatesIndependentValueReducersPerGraphRuntime()
        {
            var factory = new StatefulReducerFactory();
            FrameLayoutId layoutId = CreateFrameLayoutId(51UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(51UL),
                2,
                factory,
                out IntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));

            Assert.IsTrue(layout.TryCreateRuntime(GraphId(951UL), 2, out IntentFrameRuntime first, out _));
            Assert.IsTrue(layout.TryCreateRuntime(GraphId(952UL), 2, out IntentFrameRuntime second, out _));
            BindTwoSources(first, handle, out IntentSourceBinding<TestIntent> firstA,
                out IntentSourceBinding<TestIntent> firstB);
            BindTwoSources(second, handle, out IntentSourceBinding<TestIntent> secondA,
                out IntentSourceBinding<TestIntent> secondB);
            Assert.AreEqual(2, factory.CreateCount);

            StateFlowFrameHeader firstHeader = IntentHeader(layoutId, 1UL, 1UL, GraphId(951UL));
            Assert.IsTrue(first.TryBegin(firstHeader, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed,
                first.TrySample(firstA, firstHeader.TickFrame));
            Assert.AreEqual(IntentSourceSampleResult.Contributed,
                first.TrySample(firstB, firstHeader.TickFrame));
            Assert.IsTrue(first.TryFreeze(out _));

            StateFlowFrameHeader secondHeader = IntentHeader(layoutId, 1UL, 1UL, GraphId(952UL));
            Assert.IsTrue(second.TryBegin(secondHeader, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, second.TrySample(secondA, secondHeader.TickFrame));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, second.TrySample(secondB, secondHeader.TickFrame));
            Assert.IsTrue(second.TryFreeze(out _));

            Assert.IsTrue(first.Frame.TryGet(handle, out TestIntent firstValue));
            Assert.IsTrue(second.Frame.TryGet(handle, out TestIntent secondValue));
            Assert.AreEqual(103, firstValue.Value);
            Assert.AreEqual(103, secondValue.Value);
        }

        [Test]
        public void IntentFrameInvalidatesOnBeginCancelTimelineResetAndDispose()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(52UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(52UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            GraphInstanceId owner = GraphId(953UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out IntentFrameRuntime runtime, out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out IntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(7),
                out IntentSourceBinding<TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            StateFlowFrameHeader first = IntentHeader(layoutId, 1UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(first, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, runtime.TrySample(binding, first.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.IsFrozen);

            StateFlowFrameHeader second = IntentHeader(layoutId, 2UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(second, out _));
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsFalse(runtime.Frame.TryGet(handle, out _));
            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsFalse(runtime.TryBegin(second, out _));

            StateFlowFrameHeader third = IntentHeader(layoutId, 3UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(third, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, runtime.TrySample(binding, third.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));
            var inbox = new ActorEventInboxCore(owner, DomainId(52UL), 1, 1, 4);
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(inbox.Start(out _));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.IsTrue(inbox.ResumeAfterTimelineReset());

            runtime.Dispose();
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.AreEqual(ActorEventInboxState.Stopped, inbox.State);
        }

        [Test]
        public void RejectedSampleOutsideCollectionPreservesFrozenIntentFrame()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(520UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(520UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            GraphInstanceId owner = GraphId(9520UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 1, out IntentFrameRuntime runtime, out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out IntentSourceRequirement<TestIntent> requirement));
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                new CountingIntentSource(7),
                out IntentSourceBinding<TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));

            StateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, runtime.TrySample(binding, header.TickFrame));
            Assert.IsTrue(runtime.TryFreeze(out _));
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent before));

            Assert.AreEqual(
                IntentSourceSampleResult.ArbiterNotCollecting,
                runtime.TrySample(binding, header.TickFrame));
            Assert.IsTrue(runtime.Frame.IsFrozen);
            Assert.AreEqual(header, runtime.Frame.Header);
            Assert.IsTrue(runtime.Frame.TryGet(handle, out TestIntent after));
            Assert.AreEqual(before.Value, after.Value);
        }

        [Test]
        public void InboxStartRequiresLiveFrozenRuntime()
        {
            EventDomainId domain = DomainId(53UL);
            EventTypeId eventType = CreateEventTypeId(53UL);

            GraphInstanceId owner = GraphId(960UL);
            var unbound = new ActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(unbound.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsFalse(unbound.Start(out _));
            Assert.AreEqual(ActorEventInboxState.Created, unbound.State);

            IntentFrameRuntime unfrozenRuntime = CreateMailboxRuntime(owner, false);
            Assert.IsTrue(unbound.TryBindIntentRuntime(unfrozenRuntime, out _));
            Assert.IsFalse(unbound.Start(out CoCoDiagnostic notFrozen));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryNotFrozen, notFrozen.Code);
            Assert.IsTrue(unfrozenRuntime.FreezeBindings(out _));
            Assert.IsTrue(unbound.Start(out _));
            unbound.Stop();

        }

        [Test]
        public void RuntimeDisposeUnbindsCreatedInboxAndStopsRunningInbox()
        {
            EventDomainId domain = DomainId(55UL);
            EventTypeId eventType = CreateEventTypeId(55UL);
            GraphInstanceId createdOwner = GraphId(965UL);
            IntentFrameRuntime createdRuntime = CreateMailboxRuntime(createdOwner);
            var createdInbox = new ActorEventInboxCore(createdOwner, domain, 1, 1, 4);
            Assert.IsTrue(createdInbox.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(createdInbox.TryBindIntentRuntime(createdRuntime, out _));
            createdRuntime.Dispose();
            Assert.AreEqual(ActorEventInboxState.Created, createdInbox.State);
            IntentFrameRuntime replacement = CreateMailboxRuntime(createdOwner);
            Assert.IsTrue(createdInbox.TryBindIntentRuntime(replacement, out _));
            Assert.IsTrue(createdInbox.Start(out _));

            GraphInstanceId runningOwner = GraphId(966UL);
            IntentFrameRuntime runningRuntime = CreateMailboxRuntime(runningOwner);
            var runningInbox = new ActorEventInboxCore(runningOwner, domain, 1, 1, 4);
            Assert.IsTrue(runningInbox.TryRegisterLane(
                eventType,
                1,
                false,
                out ActorEventLaneHandle<TestEvent> lane,
                out _));
            Assert.IsTrue(runningInbox.TryBindIntentRuntime(runningRuntime, out _));
            Assert.IsTrue(runningInbox.Start(out _));
            Assert.AreEqual(InboxEnqueueResult.Accepted, runningInbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(967UL), runningOwner, 1UL, 1UL, 1)));
            runningRuntime.Dispose();
            Assert.AreEqual(ActorEventInboxState.Stopped, runningInbox.State);
            Assert.AreEqual(0, runningInbox.GetSealedCount(lane));
            Assert.AreEqual(InboxEnqueueResult.MailboxUnavailable, runningInbox.TryEnqueue(
                lane,
                Packet(eventType, domain, GraphId(967UL), runningOwner, 1UL, 2UL, 2)));
        }

        [Test]
        public void UserCallbackExceptionsCancelCollection()
        {
            FrameLayoutId sourceLayoutId = CreateFrameLayoutId(56UL);
            var sourceLayout = new IntentFrameLayout(sourceLayoutId, 1);
            Assert.IsTrue(sourceLayout.TryRegister(
                CreateIntentId(56UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> sourceHandle,
                out _));
            Assert.IsTrue(sourceLayout.Freeze(out _));
            GraphInstanceId sourceOwner = GraphId(968UL);
            Assert.IsTrue(sourceLayout.TryCreateRuntime(
                sourceOwner,
                1,
                out IntentFrameRuntime sourceRuntime,
                out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                sourceHandle,
                0,
                out IntentSourceRequirement<TestIntent> sourceRequirement));
            var sourceException = new InvalidOperationException("source failure");
            Assert.IsTrue(sourceRuntime.TryBindSource(
                sourceRequirement,
                new ThrowingIntentSource(sourceException),
                out IntentSourceBinding<TestIntent> sourceBinding,
                out _));
            Assert.IsTrue(sourceRuntime.FreezeBindings(out _));
            StateFlowFrameHeader sourceHeader = IntentHeader(sourceLayoutId, 1UL, 1UL, sourceOwner);
            Assert.IsTrue(sourceRuntime.TryBegin(sourceHeader, out _));
            Assert.AreSame(sourceException, Assert.Throws<InvalidOperationException>(
                () => sourceRuntime.TrySample(sourceBinding, sourceHeader.TickFrame)));
            Assert.IsFalse(sourceRuntime.IsCollecting);
            Assert.IsFalse(sourceRuntime.Frame.IsFrozen);

            FrameLayoutId reducerLayoutId = CreateFrameLayoutId(58UL);
            var reducerLayout = new IntentFrameLayout(reducerLayoutId, 1);
            Assert.IsTrue(reducerLayout.TryRegister(
                CreateIntentId(58UL),
                2,
                new ThrowOnceReducerFactory(),
                out IntentHandle<TestIntent> reducerHandle,
                out _));
            Assert.IsTrue(reducerLayout.Freeze(out _));
            GraphInstanceId reducerOwner = GraphId(971UL);
            Assert.IsTrue(reducerLayout.TryCreateRuntime(
                reducerOwner,
                2,
                out IntentFrameRuntime reducerRuntime,
                out _));
            BindTwoSources(reducerRuntime, reducerHandle,
                out IntentSourceBinding<TestIntent> reducerA,
                out IntentSourceBinding<TestIntent> reducerB);
            StateFlowFrameHeader reducerHeader = IntentHeader(reducerLayoutId, 1UL, 1UL, reducerOwner);
            Assert.IsTrue(reducerRuntime.TryBegin(reducerHeader, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerA, reducerHeader.TickFrame));
            Assert.AreEqual(IntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerB, reducerHeader.TickFrame));
            Assert.AreEqual("reducer failure", Assert.Throws<InvalidOperationException>(
                () => reducerRuntime.TryFreeze(out _)).Message);
            Assert.IsFalse(reducerRuntime.IsCollecting);

            StateFlowFrameHeader reducerRetry = IntentHeader(reducerLayoutId, 2UL, 1UL, reducerOwner);
            Assert.IsTrue(reducerRuntime.TryBegin(reducerRetry, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerA, reducerRetry.TickFrame));
            Assert.AreEqual(IntentSourceSampleResult.Contributed,
                reducerRuntime.TrySample(reducerB, reducerRetry.TickFrame));
            Assert.AreEqual("reducer failure", Assert.Throws<InvalidOperationException>(
                () => reducerRuntime.TryFreeze(out _)).Message);
            Assert.IsFalse(reducerRuntime.IsCollecting);
            Assert.IsFalse(reducerRuntime.Frame.IsFrozen);
        }

        [Test]
        public void CallbackReentryIsRejectedAndDisposeIsDeferredUntilCallbackReturns()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(59UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(59UL),
                2,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            GraphInstanceId owner = GraphId(972UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 2, out IntentFrameRuntime runtime, out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out IntentSourceRequirement<TestIntent> requirement));
            var source = new ReentrantIntentSource();
            Assert.IsTrue(runtime.TryBindSource(
                requirement,
                source,
                out IntentSourceBinding<TestIntent> binding,
                out _));
            Assert.IsTrue(runtime.FreezeBindings(out _));
            source.Runtime = runtime;
            source.ReentrantHeader = IntentHeader(layoutId, 2UL, 1UL, owner);
            source.SourceBinding = binding;
            StateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL, owner);
            Assert.IsTrue(runtime.TryBegin(header, out _));
            Assert.AreEqual(IntentSourceSampleResult.Contributed, runtime.TrySample(binding, header.TickFrame));
            Assert.IsFalse(source.BeginResult);
            Assert.IsFalse(source.FreezeResult);
            Assert.IsFalse(source.CancelResult);
            Assert.AreEqual(IntentSourceSampleResult.InvalidBinding, source.SampleResult);
            Assert.IsTrue(runtime.TryFreeze(out _));

            Assert.IsTrue(layout.TryCreateRuntime(GraphId(973UL), 1, out IntentFrameRuntime disposingRuntime,
                out _));
            var disposingSource = new DisposingIntentSource();
            Assert.IsTrue(disposingRuntime.TryBindSource(
                requirement,
                disposingSource,
                out IntentSourceBinding<TestIntent> disposingBinding,
                out _));
            Assert.IsTrue(disposingRuntime.FreezeBindings(out _));
            disposingSource.Runtime = disposingRuntime;
            StateFlowFrameHeader disposingHeader = IntentHeader(layoutId, 1UL, 1UL, GraphId(973UL));
            Assert.IsTrue(disposingRuntime.TryBegin(disposingHeader, out _));
            Assert.AreEqual(IntentSourceSampleResult.InvalidBinding,
                disposingRuntime.TrySample(disposingBinding, disposingHeader.TickFrame));
            Assert.IsTrue(disposingSource.ReturnedFromDispose);
            Assert.IsTrue(disposingRuntime.IsDisposed);
            Assert.IsFalse(disposingRuntime.IsCollecting);
            Assert.IsFalse(disposingRuntime.Frame.IsFrozen);
        }

        [Test]
        public void InboxBindingAndLifecycleTransitionsCannotLeaveACollectingRuntimeHalfBound()
        {
            GraphInstanceId owner = GraphId(974UL);
            EventDomainId domain = DomainId(60UL);
            EventTypeId eventType = CreateEventTypeId(60UL);
            IntentFrameRuntime runtime = CreateMailboxRuntime(owner);
            var inbox = new ActorEventInboxCore(owner, domain, 1, 1, 4);
            Assert.IsTrue(inbox.TryRegisterLane<TestEvent>(eventType, 1, false, out _, out _));
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 1UL, 1UL, owner), out _));
            Assert.IsFalse(inbox.Start(out _));
            Assert.AreEqual(ActorEventInboxState.Created, inbox.State);
            Assert.IsTrue(runtime.CancelCollection());
            Assert.IsTrue(inbox.Start(out _));

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 2UL, 1UL, owner), out _));
            inbox.Stop();
            Assert.IsFalse(runtime.IsCollecting);
            Assert.IsFalse(runtime.Frame.IsFrozen);
            Assert.AreEqual(ActorEventInboxState.Stopped, inbox.State);

            var replacement = new ActorEventInboxCore(owner, domain, 1, 1, 4);
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
            Assert.AreEqual(ActorEventInboxState.Disposed, replacement.State);
        }

        [Test]
        public void InboxCannotResealSuspendOrResumeWhileIntentRuntimeIsCollecting()
        {
            GraphInstanceId owner = GraphId(981UL);
            GraphInstanceId source = GraphId(982UL);
            EventDomainId domain = DomainId(67UL);
            EventTypeId eventType = CreateEventTypeId(67UL);
            IntentFrameRuntime runtime = CreateMailboxRuntime(owner);
            var inbox = CreateBoundInbox(
                owner,
                domain,
                eventType,
                2,
                runtime,
                out ActorEventLaneHandle<TestEvent> lane);
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                lane,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 1)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                lane,
                out ActorEventSealedBatch<TestEvent> firstBatch));
            Assert.IsTrue(firstBatch.TryRead(0, out EventPacket<TestEvent> firstPacket));
            Assert.AreEqual(1, firstPacket.Payload.Value);

            Assert.IsTrue(runtime.TryBegin(IntentHeader(runtime.LayoutId, 1UL, 1UL, owner), out _));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
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
                out ActorEventSealedBatch<TestEvent> secondBatch));
            Assert.AreEqual(1, secondBatch.Count);
            Assert.IsTrue(secondBatch.TryRead(0, out EventPacket<TestEvent> secondPacket));
            Assert.AreEqual(2, secondPacket.Payload.Value);
        }

        [Test]
        public void ReducerDisposeRequestStopsLaterReducersAndInvalidatesPartialFrame()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(62UL);
            var layout = new IntentFrameLayout(layoutId, 2);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(62UL),
                2,
                new DisposingReducerFactory(),
                out IntentHandle<TestIntent> disposingHandle,
                out _));
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(63UL),
                2,
                new CountingReducerFactory(),
                out IntentHandle<TestIntent> countingHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            GraphInstanceId owner = GraphId(976UL);
            Assert.IsTrue(layout.TryCreateRuntime(owner, 4, out IntentFrameRuntime runtime, out _));
            BindTwoSourcesWithoutFreezing(runtime, disposingHandle,
                out IntentSourceBinding<TestIntent> disposingA,
                out IntentSourceBinding<TestIntent> disposingB);
            BindTwoSourcesWithoutFreezing(runtime, countingHandle,
                out IntentSourceBinding<TestIntent> countingA,
                out IntentSourceBinding<TestIntent> countingB);
            Assert.IsTrue(runtime.FreezeBindings(out _));

            DisposingIntentReducer.Runtime = runtime;
            DisposingIntentReducer.ReduceCount = 0;
            CountingIntentReducer.ReduceCount = 0;
            try
            {
                StateFlowFrameHeader header = IntentHeader(layoutId, 1UL, 1UL, owner);
                Assert.IsTrue(runtime.TryBegin(header, out _));
                Assert.AreEqual(IntentSourceSampleResult.Contributed,
                    runtime.TrySample(disposingA, header.TickFrame));
                Assert.AreEqual(IntentSourceSampleResult.Contributed,
                    runtime.TrySample(disposingB, header.TickFrame));
                Assert.AreEqual(IntentSourceSampleResult.Contributed,
                    runtime.TrySample(countingA, header.TickFrame));
                Assert.AreEqual(IntentSourceSampleResult.Contributed,
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
            FrameLayoutId layoutId = CreateFrameLayoutId(64UL);
            GraphInstanceId owner = GraphId(977UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            var factory = new ReentrantReducerFactory(layout, owner);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(64UL),
                1,
                factory,
                out IntentHandle<TestIntent> handle,
                out _));
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(layout.Freeze(out _));

            Assert.IsTrue(layout.TryCreateRuntime(owner, 0, out IntentFrameRuntime runtime, out _));
            Assert.IsFalse(factory.ReentrantCreateResult);
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, factory.ReentrantDiagnostic.Code);
            runtime.Dispose();
        }

        [Test]
        public void TargetedPacketOnlyEntersMatchingActorAndDomain()
        {
            GraphInstanceId owner = GraphId(100UL);
            GraphInstanceId source = GraphId(101UL);
            EventDomainId domain = DomainId(1UL);
            EventTypeId eventType = CreateEventTypeId(1UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out ActorEventLaneHandle<TestEvent> handle);

            EventPacket<TestEvent> wrongTarget = Packet(
                eventType,
                domain,
                source,
                GraphId(999UL),
                epoch: 1UL,
                sequence: 1UL,
                value: 1);
            Assert.AreEqual(InboxEnqueueResult.EventTargetMismatch, inbox.TryEnqueue(handle, wrongTarget));

            EventPacket<TestEvent> wrongDomain = Packet(
                eventType,
                DomainId(2UL),
                source,
                owner,
                epoch: 1UL,
                sequence: 2UL,
                value: 2);
            Assert.AreEqual(InboxEnqueueResult.EventDomainMismatch, inbox.TryEnqueue(handle, wrongDomain));

            EventPacket<TestEvent> accepted = Packet(
                eventType,
                domain,
                source,
                owner,
                epoch: 1UL,
                sequence: 3UL,
                value: 3);
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, accepted));
            Assert.AreEqual(1UL, inbox.Counters.Accepted);
        }

        [Test]
        public void LaneHandleCannotCrossInboxInstancesWithTheSameIdentity()
        {
            GraphInstanceId owner = GraphId(170UL);
            EventDomainId domain = DomainId(18UL);
            EventTypeId eventType = CreateEventTypeId(18UL);
            var first = CreateInbox(
                owner,
                domain,
                eventType,
                1,
                out ActorEventLaneHandle<TestEvent> firstHandle);
            var second = CreateInbox(
                owner,
                domain,
                eventType,
                1,
                out ActorEventLaneHandle<TestEvent> secondHandle);
            EventPacket<TestEvent> packet = Packet(
                eventType,
                domain,
                GraphId(171UL),
                owner,
                1UL,
                1UL,
                1);

            Assert.AreEqual(
                InboxEnqueueResult.InvalidPacket,
                first.TryEnqueue(secondHandle, packet));
            Assert.AreEqual(
                InboxEnqueueResult.InvalidPacket,
                second.TryEnqueue(firstHandle, packet));
            Assert.IsFalse(firstHandle.Equals(secondHandle));
        }

        [Test]
        public void InboxUsesSealedDoubleBufferAndNextTickVisibility()
        {
            GraphInstanceId owner = GraphId(200UL);
            GraphInstanceId source = GraphId(201UL);
            EventDomainId domain = DomainId(2UL);
            EventTypeId eventType = CreateEventTypeId(2UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                handle,
                out ActorEventSealedBatch<TestEvent> firstBatch));
            Assert.IsTrue(firstBatch.IsValid);
            Assert.AreEqual(1, firstBatch.Count);
            Assert.IsTrue(firstBatch.TryRead(0, out EventPacket<TestEvent> firstFromBatch));
            Assert.AreEqual(10, firstFromBatch.Payload.Value);
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> first));
            Assert.AreEqual(10, first.Payload.Value);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> stillFirst));
            Assert.AreEqual(10, stillFirst.Payload.Value);

            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> second));
            Assert.AreEqual(20, second.Payload.Value);
            Assert.IsFalse(firstBatch.IsValid);
            Assert.AreEqual(0, firstBatch.Count);
            Assert.IsFalse(firstBatch.TryRead(0, out _));
        }

        [Test]
        public void InboxRejectsSecondSealForTheSameTick()
        {
            GraphInstanceId owner = GraphId(210UL);
            GraphInstanceId source = GraphId(211UL);
            EventDomainId domain = DomainId(21UL);
            EventTypeId eventType = CreateEventTypeId(21UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            TickFrame firstTick = MailboxTick(1UL);
            Assert.IsTrue(inbox.SealForTick(firstTick));

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));
            Assert.IsFalse(inbox.SealForTick(firstTick));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(1UL, 1UL, 2UL)));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(0UL, 1UL, 3UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> first));
            Assert.AreEqual(10, first.Payload.Value);

            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> second));
            Assert.AreEqual(20, second.Payload.Value);
        }

        [Test]
        public void SealedBatchNeverRevivesAfterTimelineReset()
        {
            GraphInstanceId owner = GraphId(220UL);
            GraphInstanceId source = GraphId(221UL);
            EventDomainId domain = DomainId(22UL);
            EventTypeId eventType = CreateEventTypeId(22UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out ActorEventSealedBatch<TestEvent> oldBatch));
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out ActorEventSealedBatch<TestEvent> alias));
            Assert.IsTrue(oldBatch.IsValid);
            Assert.IsTrue(alias.IsValid);

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsFalse(oldBatch.IsValid);
            Assert.IsTrue(inbox.ResumeAfterTimelineReset());
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL, 2UL, 2UL)));

            Assert.IsFalse(oldBatch.IsValid);
            Assert.IsFalse(oldBatch.TryRead(0, out _));
            Assert.IsFalse(alias.IsValid);
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out ActorEventSealedBatch<TestEvent> newBatch));
            Assert.IsTrue(newBatch.IsValid);
            Assert.IsTrue(newBatch.TryRead(0, out EventPacket<TestEvent> packet));
            Assert.AreEqual(20, packet.Payload.Value);
        }

        [Test]
        public void DeclaredBroadcastRejectsSourceEchoByDefault()
        {
            GraphInstanceId owner = GraphId(300UL);
            EventDomainId domain = DomainId(3UL);
            EventTypeId eventType = CreateEventTypeId(3UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out ActorEventLaneHandle<TestEvent> handle);

            EventPacket<TestEvent> echo = Packet(
                eventType,
                domain,
                owner,
                default,
                epoch: 1UL,
                sequence: 1UL,
                value: 1,
                mode: EventDeliveryMode.DeclaredBroadcast);
            Assert.AreEqual(InboxEnqueueResult.SourceEchoRejected, inbox.TryEnqueue(handle, echo));
            Assert.AreEqual(1UL, inbox.Counters.Rejected);

            EventPacket<TestEvent> external = Packet(
                eventType,
                domain,
                GraphId(301UL),
                default,
                epoch: 1UL,
                sequence: 1UL,
                value: 2,
                mode: EventDeliveryMode.DeclaredBroadcast);
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, external));
        }

        [Test]
        public void UndeclaredBroadcastNeverEntersAGameplayLane()
        {
            GraphInstanceId owner = GraphId(310UL);
            EventDomainId domain = DomainId(31UL);
            EventTypeId declaredType = CreateEventTypeId(31UL);
            EventTypeId undeclaredType = CreateEventTypeId(32UL);
            var inbox = CreateInbox(
                owner,
                domain,
                declaredType,
                2,
                out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.UndeclaredEventType, inbox.TryEnqueue(
                handle,
                Packet(
                    undeclaredType,
                    domain,
                    GraphId(311UL),
                    default,
                    1UL,
                    1UL,
                    1,
                    EventDeliveryMode.DeclaredBroadcast)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.AreEqual(0UL, inbox.Counters.Accepted);
        }

        [Test]
        public void InboxRejectsDuplicatesSequenceReuseAndStaleEpochs()
        {
            GraphInstanceId owner = GraphId(400UL);
            GraphInstanceId source = GraphId(401UL);
            EventDomainId domain = DomainId(4UL);
            EventTypeId firstType = CreateEventTypeId(4UL);
            EventTypeId secondType = CreateEventTypeId(5UL);
            var inbox = new ActorEventInboxCore(owner, domain, 2, 2, 8);
            Assert.IsTrue(inbox.TryRegisterLane(
                firstType,
                4,
                false,
                out ActorEventLaneHandle<TestEvent> firstHandle,
                out _));
            Assert.IsTrue(inbox.TryRegisterLane(
                secondType,
                4,
                false,
                out ActorEventLaneHandle<OtherTestEvent> secondHandle,
                out _));
            var runtime = CreateMailboxRuntime(owner);
            Assert.IsTrue(inbox.TryBindIntentRuntime(runtime, out _));
            Assert.IsTrue(inbox.Start(out _));

            EventPacket<TestEvent> original = Packet(
                firstType,
                domain,
                source,
                owner,
                epoch: 2UL,
                sequence: 7UL,
                value: 1);
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(firstHandle, original));
            Assert.AreEqual(InboxEnqueueResult.Duplicate, inbox.TryEnqueue(firstHandle, original));

            EventPacket<TestEvent> outOfOrder = Packet(
                firstType,
                domain,
                source,
                owner,
                epoch: 2UL,
                sequence: 6UL,
                value: 2);
            Assert.AreEqual(
                InboxEnqueueResult.EventSequenceConflict,
                inbox.TryEnqueue(firstHandle, outOfOrder));

            EventPacket<OtherTestEvent> reusedSequence = OtherPacket(
                secondType,
                domain,
                source,
                owner,
                epoch: 2UL,
                sequence: 7UL);
            Assert.AreEqual(
                InboxEnqueueResult.EventSequenceConflict,
                inbox.TryEnqueue(secondHandle, reusedSequence));

            EventPacket<TestEvent> stale = Packet(
                firstType,
                domain,
                source,
                owner,
                epoch: 1UL,
                sequence: 8UL,
                value: 3);
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(firstHandle, stale));
            Assert.AreEqual(1UL, inbox.Counters.Duplicate);
        }

        [Test]
        public void SuspendAccumulatesWhileRewindDropsAndClears()
        {
            GraphInstanceId owner = GraphId(500UL);
            GraphInstanceId source = GraphId(501UL);
            EventDomainId domain = DomainId(5UL);
            EventTypeId eventType = CreateEventTypeId(6UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);

            Assert.IsTrue(inbox.Suspend());
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 1)));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.AreEqual(InboxEnqueueResult.RewindOrRestoreDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 2UL, 1UL, 2)));
            Assert.AreEqual(1UL, inbox.Counters.RewindRestoreDropped);
            Assert.IsTrue(inbox.ResumeAfterTimelineReset());
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.IsFalse(inbox.SealForTick(MailboxTick(2UL, 1UL, 2UL)));

            Assert.AreEqual(InboxEnqueueResult.EventSequenceConflict, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 3)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 4)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL, 2UL, 2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> resumed));
            Assert.AreEqual(4, resumed.Payload.Value);
        }

        [Test]
        public void SuspendedInboxRejectsRewindBeginAndPreservesLegalBacklog()
        {
            GraphInstanceId owner = GraphId(510UL);
            GraphInstanceId source = GraphId(511UL);
            EventDomainId domain = DomainId(51UL);
            EventTypeId eventType = CreateEventTypeId(51UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);

            Assert.IsTrue(inbox.Suspend());
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 11)));
            Assert.IsFalse(inbox.BeginRewindOrRestore());
            Assert.AreEqual(ActorEventInboxState.Suspended, inbox.State);

            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> packet));
            Assert.AreEqual(11, packet.Payload.Value);
        }

        [Test]
        public void RewindCancelKeepsEpochAndNeverRevivesClearedBacklog()
        {
            GraphInstanceId owner = GraphId(520UL);
            GraphInstanceId source = GraphId(521UL);
            EventDomainId domain = DomainId(52UL);
            EventTypeId eventType = CreateEventTypeId(52UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(handle, out ActorEventSealedBatch<TestEvent> oldBatch));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsFalse(oldBatch.IsValid);
            Assert.IsTrue(inbox.CanCancelRewindOrRestore);
            Assert.AreEqual(InboxEnqueueResult.RewindOrRestoreDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 20)));

            inbox.CancelRewindOrRestoreNoFail();
            Assert.AreEqual(ActorEventInboxState.Running, inbox.State);
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.AreEqual(1UL, inbox.Counters.RewindRestoreDropped);
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 30)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL, 1UL, 2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> resumed));
            Assert.AreEqual(30, resumed.Payload.Value);
        }

        [Test]
        public void TimelineResetPreflightAndNoFailCompletionRequireNewEpoch()
        {
            GraphInstanceId owner = GraphId(530UL);
            GraphInstanceId source = GraphId(531UL);
            EventDomainId domain = DomainId(53UL);
            EventTypeId eventType = CreateEventTypeId(53UL);
            var inbox = CreateInbox(owner, domain, eventType, 2, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 10)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsTrue(inbox.CanResumeAfterTimelineReset);
            inbox.ResumeAfterTimelineResetNoFail();
            Assert.AreEqual(ActorEventInboxState.Running, inbox.State);
            Assert.IsFalse(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL, 2UL, 2UL)));
        }

        [Test]
        public void TimelineResetOwnerEpochBarrierRejectsSeenAndUnseenOldOwnerPackets()
        {
            GraphInstanceId owner = GraphId(540UL);
            EventDomainId domain = DomainId(54UL);
            EventTypeId eventType = CreateEventTypeId(54UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);
            EventPacket<TestEvent> seen = Packet(
                eventType,
                domain,
                owner,
                owner,
                1UL,
                1UL,
                10);
            EventPacket<TestEvent> unseen = Packet(
                eventType,
                domain,
                owner,
                owner,
                1UL,
                2UL,
                20);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, seen));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new TimelineEpoch(2UL));

            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(handle, seen));
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(handle, unseen));
            Assert.AreEqual(InboxEnqueueResult.InvalidPacket, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 3UL, 30)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 2UL, 3UL, 40)));
        }

        [Test]
        public void TimelineResetOwnerEpochBarrierLeavesRemoteSourceWatermarksUnchanged()
        {
            GraphInstanceId owner = GraphId(550UL);
            GraphInstanceId remote = GraphId(551UL);
            EventDomainId domain = DomainId(55UL);
            EventTypeId eventType = CreateEventTypeId(55UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 7UL, 1UL, 10)));
            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new TimelineEpoch(2UL));

            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 6UL, 2UL, 15)));
            Assert.AreEqual(InboxEnqueueResult.EventSequenceConflict, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 7UL, 1UL, 15)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, remote, owner, 7UL, 2UL, 20)));
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 1UL, 1UL, 30)));
        }

        [Test]
        public void TimelineResetRebasesOwnerWatermarkWithoutChangingRemoteWatermarks()
        {
            GraphInstanceId owner = GraphId(570UL);
            GraphInstanceId firstRemote = GraphId(571UL);
            GraphInstanceId secondRemote = GraphId(572UL);
            EventDomainId domain = DomainId(57UL);
            EventTypeId eventType = CreateEventTypeId(57UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, firstRemote, owner, 7UL, 1UL, 10)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 9UL, 1UL, 20)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, secondRemote, owner, 5UL, 1UL, 30)));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new TimelineEpoch(2UL));

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 2UL, 2UL, 40)));
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 1UL, 3UL, 50)));
            Assert.AreEqual(InboxEnqueueResult.InvalidPacket, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 3UL, 60)));
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, firstRemote, owner, 6UL, 2UL, 65)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, firstRemote, owner, 7UL, 2UL, 70)));
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, secondRemote, owner, 4UL, 2UL, 75)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, secondRemote, owner, 5UL, 2UL, 80)));
        }

        [Test]
        public void OwnerEpochBarrierOnlyChangesOnConfirmedTimelineReset()
        {
            GraphInstanceId owner = GraphId(560UL);
            EventDomainId domain = DomainId(56UL);
            EventTypeId eventType = CreateEventTypeId(56UL);
            var inbox = CreateInbox(owner, domain, eventType, 4, out ActorEventLaneHandle<TestEvent> handle);

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new TimelineEpoch(2UL));
            Assert.IsTrue(inbox.Suspend());
            Assert.IsTrue(inbox.Resume());
            Assert.AreEqual(InboxEnqueueResult.InvalidPacket, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 1UL, 10)));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            Assert.IsTrue(inbox.CancelRewindOrRestore());
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 1UL, 1UL, 20)));

            Assert.IsTrue(inbox.BeginRewindOrRestore());
            inbox.ResumeAfterTimelineResetNoFail(new TimelineEpoch(3UL));
            Assert.AreEqual(InboxEnqueueResult.StaleTimelineEpoch, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 2UL, 1UL, 30)));
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, owner, owner, 3UL, 1UL, 40)));
        }

        [Test]
        public void ReliableAndUnreliableOverflowHaveDifferentOutcomes()
        {
            GraphInstanceId owner = GraphId(600UL);
            GraphInstanceId source = GraphId(601UL);
            EventDomainId domain = DomainId(6UL);
            EventTypeId eventType = CreateEventTypeId(7UL);
            var inbox = CreateInbox(owner, domain, eventType, 1, out ActorEventLaneHandle<TestEvent> handle);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 1UL, 1)));
            Assert.AreEqual(InboxEnqueueResult.UnreliableOverflowDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 2UL, 2)));
            Assert.AreEqual(InboxEnqueueResult.ReliableOverflowFaultRequired, inbox.TryEnqueue(
                handle,
                Packet(
                    eventType,
                    domain,
                    source,
                    owner,
                    1UL,
                    3UL,
                    3,
                    reliability: EventReliability.Reliable)));

            Assert.AreEqual(1UL, inbox.Counters.UnreliableOverflowDropped);
            Assert.AreEqual(1UL, inbox.Counters.ReliableOverflowFaults);
            Assert.IsTrue(inbox.HasReliableOverflowFault);
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.AreEqual(1, inbox.GetSealedCount(handle));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out EventPacket<TestEvent> retained));
            Assert.AreEqual(1, retained.Payload.Value);

            Assert.IsTrue(inbox.Suspend());
            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 4UL, 4)));
            Assert.AreEqual(InboxEnqueueResult.UnreliableOverflowDropped, inbox.TryEnqueue(
                handle,
                Packet(eventType, domain, source, owner, 1UL, 5UL, 5)));
            Assert.AreEqual(InboxEnqueueResult.ReliableOverflowFaultRequired, inbox.TryEnqueue(
                handle,
                Packet(
                    eventType,
                    domain,
                    source,
                    owner,
                    1UL,
                    6UL,
                    6,
                    reliability: EventReliability.Reliable)));
            Assert.IsTrue(inbox.Resume());
            Assert.IsTrue(inbox.SealForTick(MailboxTick(2UL)));
            Assert.IsTrue(inbox.TryReadSealed(handle, 0, out retained));
            Assert.AreEqual(4, retained.Payload.Value);
        }

        [Test]
        public void StopAndDisposeClearBatchesAndRejectFurtherEvents()
        {
            GraphInstanceId owner = GraphId(700UL);
            GraphInstanceId source = GraphId(701UL);
            EventDomainId domain = DomainId(7UL);
            EventTypeId eventType = CreateEventTypeId(8UL);
            var inbox = CreateInbox(owner, domain, eventType, 1, out ActorEventLaneHandle<TestEvent> handle);
            EventPacket<TestEvent> packet = Packet(eventType, domain, source, owner, 1UL, 1UL, 1);

            Assert.AreEqual(InboxEnqueueResult.Accepted, inbox.TryEnqueue(handle, packet));
            Assert.IsTrue(inbox.SealForTick(MailboxTick(1UL)));
            Assert.IsTrue(inbox.TryGetSealedBatch(
                handle,
                out ActorEventSealedBatch<TestEvent> retainedBatch));
            inbox.Stop();
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
            Assert.IsFalse(retainedBatch.IsValid);
            Assert.IsFalse(retainedBatch.TryRead(0, out _));
            Assert.AreEqual(InboxEnqueueResult.MailboxUnavailable, inbox.TryEnqueue(handle, packet));

            inbox.Dispose();
            Assert.AreEqual(ActorEventInboxState.Disposed, inbox.State);
            Assert.AreEqual(0, inbox.GetSealedCount(handle));
        }

        [Test]
        public void IntentAndInboxHotPathsAllocateNoManagedMemoryAfterWarmup()
        {
            FrameLayoutId layoutId = CreateFrameLayoutId(80UL);
            var layout = new IntentFrameLayout(layoutId, 1);
            Assert.IsTrue(layout.TryRegister(
                CreateIntentId(80UL),
                1,
                new OrderedIntentReducerFactory(),
                out IntentHandle<TestIntent> intentHandle,
                out _));
            Assert.IsTrue(layout.Freeze(out _));
            GraphInstanceId intentOwner = GraphId(900UL);
            Assert.IsTrue(layout.TryCreateRuntime(
                intentOwner,
                1,
                out IntentFrameRuntime intentRuntime,
                out _));
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                intentHandle,
                0,
                out IntentSourceRequirement<TestIntent> sourceRequirement));
            Assert.IsTrue(intentRuntime.TryBindSource(
                sourceRequirement,
                new CountingIntentSource(1),
                out IntentSourceBinding<TestIntent> sourceBinding,
                out _));
            Assert.IsTrue(intentRuntime.FreezeBindings(out _));
            var headers = new StateFlowFrameHeader[10100];
            for (int index = 0; index < headers.Length; index++)
            {
                headers[index] = IntentHeader(layoutId, (ulong)index + 1UL, 1UL);
            }

            GraphInstanceId owner = GraphId(800UL);
            GraphInstanceId source = GraphId(801UL);
            EventDomainId domain = DomainId(8UL);
            EventTypeId eventType = CreateEventTypeId(80UL);
            FrameLayoutId eventLayoutId = CreateFrameLayoutId(81UL);
            var eventRuntime = CreateMailboxRuntime(owner);
            var eventHeaders = new StateFlowFrameHeader[10100];
            for (int index = 0; index < eventHeaders.Length; index++)
            {
                eventHeaders[index] = IntentHeader(
                    eventLayoutId,
                    (ulong)index + 1UL,
                    1UL,
                    owner);
            }

            var inbox = new ActorEventInboxCore(owner, domain, 1, 1, 64);
            Assert.IsTrue(inbox.TryRegisterLane(
                eventType,
                1,
                false,
                out ActorEventLaneHandle<TestEvent> eventHandle,
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
                    eventHeaders[index],
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
                    eventHeaders[index + 100],
                    (ulong)index + 101UL);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, allocated);
        }

        private static ActorEventInboxCore CreateInbox(
            GraphInstanceId owner,
            EventDomainId domain,
            EventTypeId eventType,
            int laneCapacity,
            out ActorEventLaneHandle<TestEvent> handle)
        {
            var runtime = CreateMailboxRuntime(owner);
            var inbox = new ActorEventInboxCore(owner, domain, 1, 4, 16);
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

        private static void BindTwoSources(
            IntentFrameRuntime runtime,
            IntentHandle<TestIntent> handle,
            out IntentSourceBinding<TestIntent> first,
            out IntentSourceBinding<TestIntent> second)
        {
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out IntentSourceRequirement<TestIntent> requirement));
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
            IntentFrameRuntime runtime,
            IntentHandle<TestIntent> handle,
            out IntentSourceBinding<TestIntent> first,
            out IntentSourceBinding<TestIntent> second)
        {
            Assert.IsTrue(IntentSourceRequirement<TestIntent>.TryCreate(
                handle,
                0,
                out IntentSourceRequirement<TestIntent> requirement));
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

        private static IntentFrameRuntime CreateMailboxRuntime(
            GraphInstanceId owner, bool freezeBindings = true)
        {
            var layout = new IntentFrameLayout(CreateFrameLayoutId(owner.Value + 2000UL), 0);
            Assert.IsTrue(layout.Freeze(out _));
            Assert.IsTrue(layout.TryCreateRuntime(owner, 0, out var runtime, out _));
            if (freezeBindings) Assert.IsTrue(runtime.FreezeBindings(out _));
            return runtime;
        }

        private static ActorEventInboxCore CreateBoundInbox(
            GraphInstanceId owner,
            EventDomainId domain,
            EventTypeId eventType,
            int laneCapacity,
            IntentFrameRuntime runtime,
            out ActorEventLaneHandle<TestEvent> handle)
        {
            var inbox = new ActorEventInboxCore(owner, domain, 1, 4, 16);
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
            IntentFrameRuntime runtime,
            IntentSourceBinding<TestIntent> binding,
            StateFlowFrameHeader header,
            IntentHandle<TestIntent> handle)
        {
            return runtime.TryBegin(header, out _) &&
                   runtime.TrySample(binding, header.TickFrame) ==
                   IntentSourceSampleResult.Contributed &&
                   runtime.TryFreeze(out _) &&
                   runtime.Frame.TryGet(handle, out TestIntent resolved) &&
                   resolved.Value == 1;
        }

        private static bool RunInboxCycle(
            ActorEventInboxCore inbox,
            ActorEventLaneHandle<TestEvent> handle,
            EventTypeId eventType,
            EventDomainId domain,
            GraphInstanceId source,
            GraphInstanceId owner,
            StateFlowFrameHeader header,
            ulong sequence)
        {
            if (!EventSequence.TryCreate(sequence, out EventSequence eventSequence) ||
                !ActorEventEnvelope.TryCreate(
                    eventType,
                    domain,
                    source,
                    owner,
                    new TimelineEpoch(1UL),
                    new TimelineTick(sequence),
                    eventSequence,
                    EventDeliveryMode.Targeted,
                    EventReliability.Unreliable,
                    default,
                    default,
                    default,
                    out ActorEventEnvelope envelope))
            {
                return false;
            }

            var payload = new TestEvent((int)sequence);
            return EventPacket<TestEvent>.TryCreate(envelope, payload, out EventPacket<TestEvent> packet) &&
                   inbox.TryEnqueue(handle, packet) == InboxEnqueueResult.Accepted &&
                   inbox.SealForTick(header.TickFrame) &&
                   inbox.TryGetSealedBatch(handle, out ActorEventSealedBatch<TestEvent> batch) &&
                   batch.TryRead(0, out var received) && received.Payload.Value == (int)sequence;
        }

        private static StateFlowFrameHeader IntentHeader(
            FrameLayoutId layoutId,
            ulong tick,
            ulong epoch)
        {
            return IntentHeader(layoutId, tick, epoch, GraphId(900UL));
        }

        private static StateFlowFrameHeader IntentHeader(
            FrameLayoutId layoutId,
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

        private static StateFlowFrameHeader IntentHeader(
            FrameLayoutId layoutId,
            ulong tick,
            ulong epoch,
            GraphInstanceId graphInstanceId)
        {
            return IntentHeader(layoutId, tick, epoch, tick, graphInstanceId);
        }

        private static StateFlowFrameHeader IntentHeader(
            FrameLayoutId layoutId,
            ulong tick,
            ulong epoch,
            ulong executionSequence,
            GraphInstanceId graphInstanceId)
        {
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out TimelineId timelineId));
            Assert.IsTrue(TimelinePosition.TryCreate(tick, out TimelinePosition position));
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out ClockDomainId clockDomainId));
            Assert.IsTrue(TickFrame.TryCreate(
                1d / 60d,
                timelineId,
                position,
                new TimelineTick(tick),
                clockDomainId,
                new ExecutionSequence(executionSequence),
                new TimelineEpoch(epoch),
                out TickFrame tickFrame,
                out _));
            Assert.IsTrue(StateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                StateFlowFrameKind.Intent,
                tickFrame,
                out StateFlowFrameHeader header));
            return header;
        }

        private static TickFrame MailboxTick(
            ulong tick,
            ulong epoch = 1UL,
            ulong executionSequence = 0UL)
        {
            ulong sequence = executionSequence == 0UL ? tick : executionSequence;
            if (!TimelineId.TryCreate(1UL, 1UL, out TimelineId timelineId) ||
                !TimelinePosition.TryCreate(tick, out TimelinePosition position) ||
                !ClockDomainId.TryCreate(1UL, out ClockDomainId clockDomainId) ||
                !TickFrame.TryCreate(
                    1d / 60d,
                    timelineId,
                    position,
                    new TimelineTick(tick),
                    clockDomainId,
                    new ExecutionSequence(sequence),
                    new TimelineEpoch(epoch),
                    out TickFrame tickFrame,
                    out _))
            {
                return default;
            }

            return tickFrame;
        }

        private static EventPacket<TestEvent> Packet(
            EventTypeId eventType,
            EventDomainId domain,
            GraphInstanceId source,
            GraphInstanceId target,
            ulong epoch,
            ulong sequence,
            int value,
            EventDeliveryMode mode = EventDeliveryMode.Targeted,
            EventReliability reliability = EventReliability.Unreliable)
        {
            ActorEventEnvelope envelope = Envelope(
                eventType,
                domain,
                source,
                target,
                epoch,
                sequence,
                mode,
                reliability);
            var payload = new TestEvent(value);
            Assert.IsTrue(EventPacket<TestEvent>.TryCreate(envelope, payload, out EventPacket<TestEvent> packet));
            return packet;
        }

        private static EventPacket<OtherTestEvent> OtherPacket(
            EventTypeId eventType,
            EventDomainId domain,
            GraphInstanceId source,
            GraphInstanceId target,
            ulong epoch,
            ulong sequence)
        {
            ActorEventEnvelope envelope = Envelope(
                eventType,
                domain,
                source,
                target,
                epoch,
                sequence,
                EventDeliveryMode.Targeted,
                EventReliability.Unreliable);
            Assert.IsTrue(EventPacket<OtherTestEvent>.TryCreate(
                envelope,
                default,
                out EventPacket<OtherTestEvent> packet));
            return packet;
        }

        private static ActorEventEnvelope Envelope(
            EventTypeId eventType,
            EventDomainId domain,
            GraphInstanceId source,
            GraphInstanceId target,
            ulong epoch,
            ulong sequence,
            EventDeliveryMode mode,
            EventReliability reliability)
        {
            Assert.IsTrue(EventSequence.TryCreate(sequence, out EventSequence eventSequence));
            Assert.IsTrue(ActorEventEnvelope.TryCreate(
                eventType,
                domain,
                source,
                target,
                new TimelineEpoch(epoch),
                new TimelineTick(10UL),
                eventSequence,
                mode,
                reliability,
                default,
                default,
                default,
                out ActorEventEnvelope envelope));
            return envelope;
        }

        private static GraphInstanceId GraphId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static FrameLayoutId CreateFrameLayoutId(ulong low)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(1UL, low, out FrameLayoutId id));
            return id;
        }

        private static IntentId CreateIntentId(ulong low)
        {
            Assert.IsTrue(IntentId.TryCreate(2UL, low, out IntentId id));
            return id;
        }

        private static EventTypeId CreateEventTypeId(ulong low)
        {
            Assert.IsTrue(EventTypeId.TryCreate(3UL, low, out EventTypeId id));
            return id;
        }

        private static EventDomainId DomainId(ulong value)
        {
            Assert.IsTrue(EventDomainId.TryCreate(value, out EventDomainId id));
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

        private readonly struct OtherTestEvent { }

        private readonly struct OrderedIntentReducer : IIntentReducer<TestIntent>
        {
            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                return new TestIntent((current.Value * 10) + candidate.Value);
            }
        }

        private sealed class OrderedIntentReducerFactory :
            IIntentReducerFactory<TestIntent, OrderedIntentReducer>
        {
            public OrderedIntentReducer Create(GraphInstanceId graphInstanceId)
            {
                return new OrderedIntentReducer();
            }
        }

        private struct StatefulIntentReducer : IIntentReducer<TestIntent>
        {
            private int _reductionCount;

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                _reductionCount++;
                return new TestIntent(current.Value + candidate.Value + (_reductionCount * 100));
            }
        }

        private sealed class StatefulReducerFactory :
            IIntentReducerFactory<TestIntent, StatefulIntentReducer>
        {
            public int CreateCount { get; private set; }

            public StatefulIntentReducer Create(GraphInstanceId graphInstanceId)
            {
                CreateCount++;
                return default;
            }
        }

        private struct ThrowOnceIntentReducer : IIntentReducer<TestIntent>
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
            IIntentReducerFactory<TestIntent, ThrowOnceIntentReducer>
        {
            public ThrowOnceIntentReducer Create(GraphInstanceId graphInstanceId)
            {
                return default;
            }
        }

        private struct DisposingIntentReducer : IIntentReducer<TestIntent>
        {
            public static IntentFrameRuntime Runtime { get; set; }
            public static int ReduceCount { get; set; }

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                ReduceCount++;
                Runtime.Dispose();
                return new TestIntent(current.Value + candidate.Value);
            }
        }

        private sealed class DisposingReducerFactory :
            IIntentReducerFactory<TestIntent, DisposingIntentReducer>
        {
            public DisposingIntentReducer Create(GraphInstanceId graphInstanceId)
            {
                return default;
            }
        }

        private struct CountingIntentReducer : IIntentReducer<TestIntent>
        {
            public static int ReduceCount { get; set; }

            public TestIntent Reduce(in TestIntent current, in TestIntent candidate)
            {
                ReduceCount++;
                return new TestIntent(current.Value + candidate.Value);
            }
        }

        private sealed class CountingReducerFactory :
            IIntentReducerFactory<TestIntent, CountingIntentReducer>
        {
            public CountingIntentReducer Create(GraphInstanceId graphInstanceId)
            {
                return default;
            }
        }

        private sealed class ReentrantReducerFactory :
            IIntentReducerFactory<TestIntent, OrderedIntentReducer>
        {
            private readonly IntentFrameLayout _layout;
            private readonly GraphInstanceId _graphInstanceId;

            public ReentrantReducerFactory(
                IntentFrameLayout layout,
                GraphInstanceId graphInstanceId)
            {
                _layout = layout;
                _graphInstanceId = graphInstanceId;
            }

            public bool ReentrantCreateResult { get; private set; }
            public CoCoDiagnostic ReentrantDiagnostic { get; private set; }

            public OrderedIntentReducer Create(GraphInstanceId graphInstanceId)
            {
                ReentrantCreateResult = _layout.TryCreateRuntime(
                    _graphInstanceId,
                    0,
                    out IntentFrameRuntime nested,
                    out CoCoDiagnostic diagnostic);
                ReentrantDiagnostic = diagnostic;
                nested?.Dispose();
                return default;
            }
        }

        private sealed class CountingIntentSource : IIntentFrameSource<TestIntent>
        {
            private readonly int _value;

            public CountingIntentSource(int value)
            {
                _value = value;
            }

            public int SampleCount { get; private set; }

            public bool TrySample(in TickFrame tickFrame, out TestIntent intent)
            {
                SampleCount++;
                intent = new TestIntent(_value);
                return true;
            }
        }

        private sealed class ThrowingIntentSource : IIntentFrameSource<TestIntent>
        {
            private readonly InvalidOperationException _exception;

            public ThrowingIntentSource(InvalidOperationException exception)
            {
                _exception = exception;
            }

            public bool TrySample(in TickFrame tickFrame, out TestIntent intent)
            {
                intent = default;
                throw _exception;
            }
        }

        private sealed class ReentrantIntentSource : IIntentFrameSource<TestIntent>
        {
            public IntentFrameRuntime Runtime { get; set; }
            public StateFlowFrameHeader ReentrantHeader { get; set; }
            public IntentSourceBinding<TestIntent> SourceBinding { get; set; }
            public bool BeginResult { get; private set; }
            public bool FreezeResult { get; private set; }
            public bool CancelResult { get; private set; }
            public IntentSourceSampleResult SampleResult { get; private set; }

            public bool TrySample(in TickFrame tickFrame, out TestIntent intent)
            {
                BeginResult = Runtime.TryBegin(ReentrantHeader, out _);
                FreezeResult = Runtime.TryFreeze(out _);
                CancelResult = Runtime.CancelCollection();
                SampleResult = Runtime.TrySample(SourceBinding, tickFrame);
                intent = new TestIntent(1);
                return true;
            }
        }

        private sealed class DisposingIntentSource : IIntentFrameSource<TestIntent>
        {
            public IntentFrameRuntime Runtime { get; set; }
            public bool ReturnedFromDispose { get; private set; }

            public bool TrySample(in TickFrame tickFrame, out TestIntent intent)
            {
                Runtime.Dispose();
                ReturnedFromDispose = !Runtime.IsDisposed;
                intent = new TestIntent(1);
                return true;
            }
        }

    }
}
