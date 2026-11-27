using System.Reflection;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.Persistence
{
    public sealed class EntityContextTests
    {
        [Test]
        public void EventSequenceIsSeparateFromStableAndRuntimeEntityIds()
        {
            var context = new TestEntityContext();
            context.Identity.StableEntityId = "scene.actor.template";
            context.Identity.RuntimeInstanceId = "runtime.actor.001";

            var first = context.NextEventSequence();
            var second = context.NextEventSequence();
            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
            Assert.AreEqual(second, context.LastEventSequence);
            Assert.AreEqual("runtime.actor.001", context.Identity.RuntimeInstanceId);
            Assert.AreNotEqual(context.Identity.StableEntityId, context.Identity.RuntimeInstanceId);
        }

        [Test]
        public void EntityContextProjectsIdentityLifecycleAndSemanticState()
        {
            var context = new TestEntityContext();
            context.Identity.StableEntityId = "item.persist";
            context.Lifecycle.TransitionTo(EntityLifecycleState.Active);
            context.SemanticStateId = 42;

            EntityContext entityContext = context;

            Assert.AreEqual("item.persist", entityContext.Identity.StableEntityId);
            Assert.AreEqual(EntityLifecycleState.Active, entityContext.Lifecycle.State);
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

        public sealed class TestEntityContext : EntityContext { }

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
