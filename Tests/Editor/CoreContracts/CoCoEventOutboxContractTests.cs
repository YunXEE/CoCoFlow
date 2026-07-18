using System;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoEventOutboxContractTests
    {
        [Test]
        public void RequirementCreatesTypedFixedCapacityLaneWithoutReflection()
        {
            CoCoEventTypeId eventTypeId = CreateEventTypeId(1UL);
            CoCoEventDomainId domainId = CreateEventDomainId(2UL);
            Assert.IsTrue(CoCoEventOutboxRequirement.TryCreate<TestEvent>(
                eventTypeId,
                domainId,
                2,
                out CoCoEventOutboxRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);

            ICoCoEventOutboxLane lane = requirement.CreateLane();
            Assert.IsNotNull(lane);
            Assert.IsInstanceOf<ICoCoEventOutboxLane<TestEvent>>(lane);
            var typed = (ICoCoEventOutboxLane<TestEvent>)lane;
            Assert.IsTrue(CoCoEventOutboxTarget.TryTargeted(
                CreateGraphInstanceId(3UL),
                CoCoEventReliability.Reliable,
                default,
                default,
                default,
                out CoCoEventOutboxTarget target));

            Assert.IsTrue(typed.TryAppend(target, new TestEvent(10), out int first));
            Assert.IsTrue(typed.TryAppend(target, new TestEvent(20), out int second));
            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
            Assert.IsFalse(typed.TryAppend(target, new TestEvent(30), out _));
            Assert.AreEqual(2, lane.Count);

            var publisher = new TestPublisher();
            Assert.IsTrue(lane.TryPublish(
                second,
                new CoCoCommittedEventSource(
                    CreateGraphInstanceId(4UL),
                    new CoCoTimelineEpoch(5UL),
                    new CoCoTimelineTick(6UL),
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
            Assert.IsFalse(CoCoEventOutboxTarget.TryTargeted(
                default,
                CoCoEventReliability.Reliable,
                default,
                default,
                default,
                out _));
            Assert.IsFalse(CoCoEventOutboxTarget.TryDeclaredBroadcast(
                CoCoEventReliability.None,
                default,
                default,
                default,
                out _));
            Assert.IsTrue(CoCoEventOutboxTarget.TryDeclaredBroadcast(
                CoCoEventReliability.Unreliable,
                default,
                default,
                default,
                out CoCoEventOutboxTarget broadcast));
            Assert.AreEqual(CoCoEventDeliveryMode.DeclaredBroadcast, broadcast.DeliveryMode);
            Assert.IsFalse(broadcast.TargetGraphInstanceId.IsValid);
        }

        [Test]
        public void RequirementRejectsInvalidIdentityAndCapacity()
        {
            Assert.IsFalse(CoCoEventOutboxRequirement.TryCreate<TestEvent>(
                default,
                CreateEventDomainId(8UL),
                1,
                out _,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticDomain.EventOutbox, diagnostic.Domain);
            Assert.IsFalse(CoCoEventOutboxRequirement.TryCreate<TestEvent>(
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
            var descriptorBuilder = new CoCoOperatorDescriptorBuilder();
            Assert.IsTrue(descriptorBuilder.TryRequire<ITestSection>(
                CreateSectionId(19UL),
                CoCoOperationSectionMode.Continuous,
                out _,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(descriptorBuilder.TryEmit<TestEvent>(
                CreateEventTypeId(20UL),
                CreateEventDomainId(21UL),
                1,
                out CoCoEventOutboxRequirement declared,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoOperatorId.TryCreate(0UL, 22UL, out CoCoOperatorId operatorId));
            Assert.IsTrue(descriptorBuilder.TryFreeze<TestOperator>(
                operatorId,
                out CoCoOperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoEventOutboxRequirement.TryCreate<OtherEvent>(
                CreateEventTypeId(23UL),
                CreateEventDomainId(21UL),
                1,
                out CoCoEventOutboxRequirement undeclared,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoEventOutboxTarget.TryDeclaredBroadcast(
                CoCoEventReliability.Unreliable,
                default,
                default,
                default,
                out CoCoEventOutboxTarget target));
            var sink = new OutboxSink(declared, operatorId, 50UL);
            var writer = new CoCoEventOutboxWriter(descriptor, sink, 50UL);

            Assert.IsTrue(writer.IsValid);
            Assert.AreEqual(
                CoCoEventOutboxWriteResult.UndeclaredEventType,
                writer.TryWrite(undeclared, target, new OtherEvent(1)));
            Assert.AreEqual(
                CoCoEventOutboxWriteResult.PayloadTypeMismatch,
                writer.TryWrite(declared, target, new OtherEvent(2)));
            Assert.AreEqual(
                CoCoEventOutboxWriteResult.InvalidTarget,
                writer.TryWrite(declared, default, new TestEvent(2)));
            Assert.AreEqual(
                CoCoEventOutboxWriteResult.Accepted,
                writer.TryWrite(declared, target, new TestEvent(3)));
            Assert.AreEqual(
                CoCoEventOutboxWriteResult.CapacityExceeded,
                writer.TryWrite(declared, target, new TestEvent(4)));
            Assert.AreEqual(4, sink.RejectedWriteCount);
            sink.Deactivate();
            Assert.IsFalse(writer.IsValid);
            Assert.AreEqual(
                CoCoEventOutboxWriteResult.InvalidWriter,
                writer.TryWrite(declared, target, new TestEvent(5)));
            Assert.AreEqual(4, sink.RejectedWriteCount);
        }

        [Test]
        public void TypedLaneAppendAndResetAllocateNoManagedMemoryAfterWarmup()
        {
            Assert.IsTrue(CoCoEventOutboxRequirement.TryCreate<TestEvent>(
                CreateEventTypeId(30UL),
                CreateEventDomainId(31UL),
                1,
                out CoCoEventOutboxRequirement requirement,
                out _));
            var lane = (ICoCoEventOutboxLane<TestEvent>)requirement.CreateLane();
            Assert.IsTrue(CoCoEventOutboxTarget.TryDeclaredBroadcast(
                CoCoEventReliability.Unreliable,
                default,
                default,
                default,
                out CoCoEventOutboxTarget target));
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

        private static CoCoEventTypeId CreateEventTypeId(ulong low)
        {
            Assert.IsTrue(CoCoEventTypeId.TryCreate(0UL, low, out CoCoEventTypeId id));
            return id;
        }

        private static CoCoOperationSectionId CreateSectionId(ulong low)
        {
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(0UL, low, out CoCoOperationSectionId id));
            return id;
        }

        private static CoCoEventDomainId CreateEventDomainId(ulong value)
        {
            Assert.IsTrue(CoCoEventDomainId.TryCreate(value, out CoCoEventDomainId id));
            return id;
        }

        private static CoCoGraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoEventSequence CreateEventSequence(ulong value)
        {
            Assert.IsTrue(CoCoEventSequence.TryCreate(value, out CoCoEventSequence sequence));
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

        private interface ITestSection : ICoCoOperationSection
        {
            int Value { get; }
        }

        private sealed class TestOperator : ICoCoOperator
        {
            public CoCoOperatorDescriptor Descriptor { get; set; }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                outcome = CoCoOperatorOutcome.NoOp;
                return true;
            }
        }

        private sealed class OutboxSink : ICoCoEventOutboxSink
        {
            private readonly ICoCoEventOutboxLane _lane;
            private readonly CoCoOperatorId _operatorId;
            private readonly ulong _token;
            private bool _active = true;

            public int RejectedWriteCount { get; private set; }

            public OutboxSink(
                CoCoEventOutboxRequirement requirement,
                CoCoOperatorId operatorId,
                ulong token)
            {
                _lane = requirement.CreateLane();
                _operatorId = operatorId;
                _token = token;
            }

            public bool IsActive(ulong token, CoCoOperatorId operatorId) =>
                _active && token == _token && operatorId == _operatorId;

            public void RejectWrite(ulong token, CoCoOperatorId operatorId)
            {
                if (_active)
                {
                    RejectedWriteCount++;
                }
            }

            public CoCoEventOutboxWriteResult TryWrite<TEvent>(
                ulong token,
                CoCoOperatorId operatorId,
                CoCoEventOutboxRequirement requirement,
                CoCoEventOutboxTarget target,
                in TEvent payload)
                where TEvent : unmanaged
            {
                if (!IsActive(token, operatorId))
                {
                    return CoCoEventOutboxWriteResult.InvalidWriter;
                }

                if (!(_lane is ICoCoEventOutboxLane<TEvent> typed) ||
                    typed.Requirement != requirement)
                {
                    return CoCoEventOutboxWriteResult.PayloadTypeMismatch;
                }

                return typed.TryAppend(target, payload, out _)
                    ? CoCoEventOutboxWriteResult.Accepted
                    : CoCoEventOutboxWriteResult.CapacityExceeded;
            }

            public void Deactivate()
            {
                _active = false;
            }
        }

        private sealed class TestPublisher : ICoCoCommittedEventPublisher
        {
            public int Count { get; private set; }
            public Type PayloadType { get; private set; }
            public CoCoActorEventEnvelope Envelope { get; private set; }

            public bool TryPublish<TEvent>(in CoCoEventPacket<TEvent> packet)
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
