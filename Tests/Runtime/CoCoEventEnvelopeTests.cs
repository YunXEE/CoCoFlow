using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
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

        [Test]
        public void EventSequenceIsSeparateFromStableAndRuntimeEntityIds()
        {
            var context = new TestEntityContext();
            context.Identity.StableEntityId = "scene.actor.template";
            context.Identity.RuntimeInstanceId = "runtime.actor.001";

            var first = context.NextEventSequence();
            var second = context.NextEventSequence();
            var envelope = CoCoEventEnvelope.Create(
                eventTypeId: "Test.Fire",
                sourceEntityId: context.Identity.RuntimeInstanceId,
                sequence: second,
                tick: 900,
                reliable: false);

            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
            Assert.AreEqual(2, envelope.sequence);
            Assert.AreEqual("runtime.actor.001", envelope.sourceEntityId);
            Assert.AreNotEqual(context.Identity.StableEntityId, envelope.sourceEntityId);
        }

        [Test]
        public void EntityContextProjectsIdentityLifecycleAndSemanticState()
        {
            var context = new TestEntityContext();
            context.Identity.StableEntityId = "item.persist";
            context.Lifecycle.TransitionTo(CoCoLifecycleState.Active);
            context.SemanticStateId = 42;

            CoCoEntityContext entityContext = context;

            Assert.AreEqual("item.persist", entityContext.Identity.StableEntityId);
            Assert.AreEqual(CoCoLifecycleState.Active, entityContext.Lifecycle.State);
            Assert.AreEqual(42, entityContext.SemanticStateId);
        }

        [Test]
        public void PersistenceUniqueIdMapsToStableEntityIdWithoutOwningRuntimeId()
        {
            var root = new GameObject("Persistence Identity Test");
            try
            {
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(persistenceContext, "stableEntityId", "scene.item.001");

                Assert.IsInstanceOf<ICoCoStableEntityIdProvider>(persistenceContext);
                Assert.AreEqual("scene.item.001", persistenceContext.StableEntityId);

                var context = new TestEntityContext();
                context.Identity.StableEntityId = persistenceContext.StableEntityId;
                context.Identity.RuntimeInstanceId = "runtime.host.42";

                Assert.IsTrue(context.Identity.HasStableEntityId);
                Assert.IsTrue(context.Identity.HasRuntimeInstanceId);
                Assert.AreEqual("scene.item.001", context.Identity.StableEntityId);
                Assert.AreEqual("runtime.host.42", context.Identity.RuntimeInstanceId);
                Assert.AreNotEqual(context.Identity.StableEntityId, context.Identity.RuntimeInstanceId);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public struct TestOpenedEvent
        {
            public string ItemId;
            public int EventSequence;
        }

        public sealed class TestEntityContext : CoCoEntityContext { }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
