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
        public void RetiredEntityTypesAreAbsentAndLifecycleKeepsItsValues()
        {
            var assembly = typeof(LifecycleContext).Assembly;
            Assert.IsNull(assembly.GetType("CoCoFlow.Runtime.StateGraph.EntityContext"));
            Assert.IsNull(assembly.GetType("CoCoFlow.Runtime.StateGraph.EntityIdentity"));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6 },
                System.Array.ConvertAll((EntityLifecycleState[])System.Enum.GetValues(typeof(EntityLifecycleState)), value => (int)value));
            var context = new LifecycleContext();
            context.TransitionTo(EntityLifecycleState.Active);
            Assert.IsTrue(context.IsAliveLike);
            context.TransitionTo(EntityLifecycleState.Destroyed);
            Assert.IsFalse(context.TryTransitionTo(EntityLifecycleState.Active));
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

            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

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
