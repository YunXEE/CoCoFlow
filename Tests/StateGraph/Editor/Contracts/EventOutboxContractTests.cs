using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class EventOutboxContractTests
    {
        [Test]
        public void RequirementCreatesTypedFixedCapacityLaneWithoutReflection()
        {
            EventTypeId eventTypeId = CreateEventTypeId(1UL);
            EventDomainId domainId = CreateEventDomainId(2UL);
            Assert.IsTrue(EventOutboxRequirement.TryCreate<TestEvent>(
                eventTypeId,
                domainId,
                2,
                out EventOutboxRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);

            IEventOutboxLane lane = requirement.CreateLane();
            Assert.IsNotNull(lane);
            Assert.IsInstanceOf<IEventOutboxLane<TestEvent>>(lane);
            var typed = (IEventOutboxLane<TestEvent>)lane;
            Assert.IsTrue(EventOutboxTarget.TryTargeted(
                CreateGraphInstanceId(3UL),
                EventReliability.Reliable,
                default,
                default,
                default,
                out EventOutboxTarget target));

            Assert.IsTrue(typed.TryAppend(target, new TestEvent(10), out int first));
            Assert.IsTrue(typed.TryAppend(target, new TestEvent(20), out int second));
            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
            Assert.IsFalse(typed.TryAppend(target, new TestEvent(30), out _));
            Assert.AreEqual(2, lane.Count);

            var publisher = new TestPublisher();
            Assert.IsTrue(lane.TryPublish(
                second,
                new CommittedEventSource(
                    CreateGraphInstanceId(4UL),
                    new TimelineEpoch(5UL),
                    new TimelineTick(6UL),
                    CreateEventSequence(7UL)),
                publisher));
            Assert.AreEqual(1, publisher.Count);
            Assert.AreEqual(typeof(TestEvent), publisher.PayloadType);
            Assert.AreEqual(eventTypeId, publisher.Envelope.EventTypeId);
            Assert.AreEqual(domainId, publisher.Envelope.EventDomainId);
            Assert.AreEqual(target.TargetGraphInstanceId, publisher.Envelope.TargetGraphInstanceId);
            Assert.AreEqual(7UL, publisher.Envelope.SourceEventSequence.Value);

            lane.Reset();
            Assert.AreEqual(0, lane.Count);
        }

        [Test]
        public void TargetsEnforceTargetedAndDeclaredBroadcastShape()
        {
            Assert.IsFalse(EventOutboxTarget.TryTargeted(
                default,
                EventReliability.Reliable,
                default,
                default,
                default,
                out _));
            Assert.IsFalse(EventOutboxTarget.TryDeclaredBroadcast(
                EventReliability.None,
                default,
                default,
                default,
                out _));
            Assert.IsTrue(EventOutboxTarget.TryDeclaredBroadcast(
                EventReliability.Unreliable,
                default,
                default,
                default,
                out EventOutboxTarget broadcast));
            Assert.AreEqual(EventDeliveryMode.DeclaredBroadcast, broadcast.DeliveryMode);
            Assert.IsFalse(broadcast.TargetGraphInstanceId.IsValid);
        }

        [Test]
        public void RequirementRejectsInvalidIdentityAndCapacity()
        {
            Assert.IsFalse(EventOutboxRequirement.TryCreate<TestEvent>(
                default,
                CreateEventDomainId(8UL),
                1,
                out _,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticDomain.EventOutbox, diagnostic.Domain);
            Assert.IsFalse(EventOutboxRequirement.TryCreate<TestEvent>(
                CreateEventTypeId(9UL),
                CreateEventDomainId(10UL),
                0,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidEventPacket, diagnostic.Code);
        }

        [Test]
        public void WriterAcceptsOnlyDeclaredLaneAndExpiresWithTransactionToken()
        {
            var descriptorBuilder = new OperatorDescriptorBuilder();
            Assert.IsTrue(descriptorBuilder.TryRequire<ITestSection>(
                CreateSectionId(19UL),
                Mode.Continuous,
                out _,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(descriptorBuilder.TryEmit<TestEvent>(
                CreateEventTypeId(20UL),
                CreateEventDomainId(21UL),
                1,
                out EventOutboxRequirement declared,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(OperatorId.TryCreate(0UL, 22UL, out OperatorId operatorId));
            Assert.IsTrue(descriptorBuilder.TryFreeze<TestOperator>(
                operatorId,
                out OperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(EventOutboxRequirement.TryCreate<OtherEvent>(
                CreateEventTypeId(23UL),
                CreateEventDomainId(21UL),
                1,
                out EventOutboxRequirement undeclared,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(EventOutboxTarget.TryDeclaredBroadcast(
                EventReliability.Unreliable,
                default,
                default,
                default,
                out EventOutboxTarget target));
            var sink = new OutboxSink(declared, operatorId, 50UL);
            var writer = new EventOutboxWriter(descriptor, sink, 50UL);

            Assert.IsTrue(writer.IsValid);
            Assert.AreEqual(
                EventOutboxWriteResult.UndeclaredEventType,
                writer.TryWrite(undeclared, target, new OtherEvent(1)));
            Assert.AreEqual(
                EventOutboxWriteResult.PayloadTypeMismatch,
                writer.TryWrite(declared, target, new OtherEvent(2)));
            Assert.AreEqual(
                EventOutboxWriteResult.InvalidTarget,
                writer.TryWrite(declared, default, new TestEvent(2)));
            Assert.AreEqual(
                EventOutboxWriteResult.Accepted,
                writer.TryWrite(declared, target, new TestEvent(3)));
            Assert.AreEqual(
                EventOutboxWriteResult.CapacityExceeded,
                writer.TryWrite(declared, target, new TestEvent(4)));
            Assert.AreEqual(4, sink.RejectedWriteCount);
            sink.Deactivate();
            Assert.IsFalse(writer.IsValid);
            Assert.AreEqual(
                EventOutboxWriteResult.InvalidWriter,
                writer.TryWrite(declared, target, new TestEvent(5)));
            Assert.AreEqual(4, sink.RejectedWriteCount);
        }

        [Test]
        public void TypedLaneAppendAndResetAllocateNoManagedMemoryAfterWarmup()
        {
            Assert.IsTrue(EventOutboxRequirement.TryCreate<TestEvent>(
                CreateEventTypeId(30UL),
                CreateEventDomainId(31UL),
                1,
                out EventOutboxRequirement requirement,
                out _));
            var lane = (IEventOutboxLane<TestEvent>)requirement.CreateLane();
            Assert.IsTrue(EventOutboxTarget.TryDeclaredBroadcast(
                EventReliability.Unreliable,
                default,
                default,
                default,
                out EventOutboxTarget target));
            var payload = new TestEvent(1);
            bool succeeded = true;
            for (int index = 0; index < 100; index++)
            {
                succeeded &= lane.TryAppend(target, payload, out _);
                lane.Reset();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= lane.TryAppend(target, payload, out _);
                lane.Reset();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, allocated);
        }

        private static EventTypeId CreateEventTypeId(ulong low)
        {
            Assert.IsTrue(EventTypeId.TryCreate(0UL, low, out EventTypeId id));
            return id;
        }

        private static OperationId CreateSectionId(ulong low)
        {
            Assert.IsTrue(OperationId.TryCreate(0UL, low, out OperationId id));
            return id;
        }

        private static EventDomainId CreateEventDomainId(ulong value)
        {
            Assert.IsTrue(EventDomainId.TryCreate(value, out EventDomainId id));
            return id;
        }

        private static GraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static EventSequence CreateEventSequence(ulong value)
        {
            Assert.IsTrue(EventSequence.TryCreate(value, out EventSequence sequence));
            return sequence;
        }

        private readonly struct TestEvent
        {
            public TestEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private readonly struct OtherEvent
        {
            public OtherEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private interface ITestSection : IOperation
        {
            int Value { get; }
        }

        private sealed class TestOperator : IOperator
        {
            public OperatorDescriptor Descriptor { get; set; }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                outcome = OperatorOutcome.NoOp;
                return true;
            }
        }

        private sealed class OutboxSink : IEventOutboxSink
        {
            private readonly IEventOutboxLane _lane;
            private readonly OperatorId _operatorId;
            private readonly ulong _token;
            private bool _active = true;

            public int RejectedWriteCount { get; private set; }

            public OutboxSink(
                EventOutboxRequirement requirement,
                OperatorId operatorId,
                ulong token)
            {
                _lane = requirement.CreateLane();
                _operatorId = operatorId;
                _token = token;
            }

            public bool IsActive(ulong token, OperatorId operatorId) =>
                _active && token == _token && operatorId == _operatorId;

            public void RejectWrite(ulong token, OperatorId operatorId)
            {
                if (_active)
                {
                    RejectedWriteCount++;
                }
            }

            public EventOutboxWriteResult TryWrite<TEvent>(
                ulong token,
                OperatorId operatorId,
                EventOutboxRequirement requirement,
                EventOutboxTarget target,
                in TEvent payload)
                where TEvent : unmanaged
            {
                if (!IsActive(token, operatorId))
                {
                    return EventOutboxWriteResult.InvalidWriter;
                }

                if (!(_lane is IEventOutboxLane<TEvent> typed) ||
                    typed.Requirement != requirement)
                {
                    return EventOutboxWriteResult.PayloadTypeMismatch;
                }

                return typed.TryAppend(target, payload, out _)
                    ? EventOutboxWriteResult.Accepted
                    : EventOutboxWriteResult.CapacityExceeded;
            }

            public void Deactivate()
            {
                _active = false;
            }
        }

        private sealed class TestPublisher : ICommittedEventPublisher
        {
            public int Count { get; private set; }
            public Type PayloadType { get; private set; }
            public GraphEventEnvelope Envelope { get; private set; }

            public bool TryPublish<TEvent>(in EventPacket<TEvent> packet)
                where TEvent : unmanaged
            {
                Count++;
                PayloadType = typeof(TEvent);
                Envelope = packet.Envelope;
                return true;
            }
        }
    }
}
