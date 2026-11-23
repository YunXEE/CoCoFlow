using CoCoFlow.Runtime.Core;
using NUnit.Framework;

namespace CoCoFlow.Tests.Runtime.Core
{
    /// <summary>
    /// Core event-envelope contracts migrated from the retired Gameplay sample.
    /// Fixtures are test-owned types only; no sample dependency remains.
    /// </summary>
    public class CoCoEventEnvelopeTests
    {
        [Test]
        public void EventEnvelopeRepresentsTargetedAndBareSequences()
        {
            var opened = CoCoEventEnvelope.Create(
                eventTypeId: "Test.Opened",
                sourceEntityId: "actor.test.a",
                sequence: 7,
                tick: 320,
                reliable: true,
                targetEntityId: "item.test.container.01",
                payloadTypeId: "TestOpenedEvent",
                payload: "test.currency");

            Assert.IsTrue(opened.IsValid);
            Assert.IsTrue(opened.HasTarget);
            Assert.IsTrue(opened.HasPayload);
            Assert.AreEqual("Test.Opened", opened.eventTypeId);
            Assert.AreEqual("actor.test.a", opened.sourceEntityId);
            Assert.AreEqual("item.test.container.01", opened.targetEntityId);
            Assert.AreEqual(7, opened.sequence);
            Assert.AreEqual(320, opened.tick);
            Assert.IsTrue(opened.reliable);

            var bare = CoCoEventEnvelope.Create(
                eventTypeId: "Test.Fire",
                sourceEntityId: "actor.test.a",
                sequence: 128,
                tick: 512,
                reliable: false);

            Assert.IsTrue(bare.IsValid);
            Assert.AreEqual("Test.Fire", bare.eventTypeId);
            Assert.AreEqual(128, bare.sequence);
            Assert.IsFalse(bare.HasTarget);
            Assert.IsFalse(bare.HasPayload);
        }

        [Test]
        public void EventBusPublishesTypedEventAndEnvelopeForNetworkBridge()
        {
            var agent = new CoCoEventAgent();
            var typedObserved = false;
            var envelopeObserved = false;

            try
            {
                agent.Subscribe<TestOpenedEvent>((ref TestOpenedEvent evt) =>
                {
                    typedObserved = evt.ItemId == "test.currency" && evt.EventSequence == 1;
                });
                agent.Subscribe<CoCoEventEnvelope>((ref CoCoEventEnvelope envelope) =>
                {
                    envelopeObserved = envelope.eventTypeId == "Test.Opened" &&
                                       envelope.sourceEntityId == "actor.test.a" &&
                                       envelope.targetEntityId == "item.test.container.01" &&
                                       envelope.sequence == 7;
                });

                var openedEvent = new TestOpenedEvent
                {
                    ItemId = "test.currency",
                    EventSequence = 1
                };
                var envelope = CoCoEventEnvelope.Create(
                    eventTypeId: "Test.Opened",
                    sourceEntityId: "actor.test.a",
                    sequence: 7,
                    tick: 320,
                    reliable: true,
                    targetEntityId: "item.test.container.01",
                    payloadTypeId: nameof(TestOpenedEvent),
                    payload: openedEvent.ItemId);

                CoCoEventBus.PublishWithEnvelope(ref openedEvent, ref envelope);

                Assert.IsTrue(typedObserved);
                Assert.IsTrue(envelopeObserved);
            }
            finally
            {
                agent.UnsubscribeAll();
            }
        }

        public struct TestOpenedEvent
        {
            public string ItemId;
            public int EventSequence;
        }

    }
}
