using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoIdentityContractTests
    {
        [Test]
        public void TopologyAndTimelineIdsUseTwoUlongParts()
        {
            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var graphId));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var layerId));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var stateId));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var transitionId));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var timelineId));

            Assert.AreEqual(1UL, graphId.High);
            Assert.AreEqual(2UL, graphId.Low);
            Assert.AreEqual(3UL, layerId.High);
            Assert.AreEqual(4UL, layerId.Low);
            Assert.AreEqual(5UL, stateId.High);
            Assert.AreEqual(6UL, stateId.Low);
            Assert.AreEqual(7UL, transitionId.High);
            Assert.AreEqual(8UL, transitionId.Low);
            Assert.AreEqual(9UL, timelineId.High);
            Assert.AreEqual(10UL, timelineId.Low);
            Assert.AreEqual(32, graphId.ToString().Length);
            Assert.AreEqual(32, timelineId.ToString().Length);

            Assert.IsTrue(CoCoGraphId.TryParse(graphId.ToString(), out var parsedGraphId));
            Assert.IsTrue(CoCoLayerId.TryParse(layerId.ToString(), out var parsedLayerId));
            Assert.IsTrue(CoCoStateId.TryParse(stateId.ToString(), out var parsedStateId));
            Assert.IsTrue(CoCoTransitionId.TryParse(transitionId.ToString(), out var parsedTransitionId));
            Assert.IsTrue(CoCoTimelineId.TryParse(timelineId.ToString(), out var parsedTimelineId));

            Assert.AreEqual(graphId, parsedGraphId);
            Assert.AreEqual(layerId, parsedLayerId);
            Assert.AreEqual(stateId, parsedStateId);
            Assert.AreEqual(transitionId, parsedTransitionId);
            Assert.AreEqual(timelineId, parsedTimelineId);
        }

        [Test]
        public void StableIdsRejectAllZeroValues()
        {
            Assert.IsFalse(CoCoGraphId.TryCreate(0UL, 0UL, out var graphId));
            Assert.IsFalse(CoCoLayerId.TryCreate(0UL, 0UL, out var layerId));
            Assert.IsFalse(CoCoStateId.TryCreate(0UL, 0UL, out var stateId));
            Assert.IsFalse(CoCoTransitionId.TryCreate(0UL, 0UL, out var transitionId));
            Assert.IsFalse(CoCoTimelineId.TryCreate(0UL, 0UL, out var timelineId));

            Assert.IsFalse(graphId.IsValid);
            Assert.IsFalse(layerId.IsValid);
            Assert.IsFalse(stateId.IsValid);
            Assert.IsFalse(transitionId.IsValid);
            Assert.IsFalse(timelineId.IsValid);
        }

        [Test]
        public void RuntimeAndClockIdsRequireNonZeroValues()
        {
            Assert.IsFalse(CoCoGraphInstanceId.TryCreate(0UL, out var invalidInstanceId));
            Assert.IsFalse(CoCoActivationId.TryCreate(0UL, out var invalidActivationId));
            Assert.IsFalse(CoCoClockDomainId.TryCreate(0UL, out var invalidClockDomainId));

            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(1UL, out var instanceId));
            Assert.IsTrue(CoCoActivationId.TryCreate(2UL, out var activationId));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(3UL, out var clockDomainId));

            Assert.IsFalse(invalidInstanceId.IsValid);
            Assert.IsFalse(invalidActivationId.IsValid);
            Assert.IsFalse(invalidClockDomainId.IsValid);
            Assert.AreEqual(1UL, instanceId.Value);
            Assert.AreEqual(2UL, activationId.Value);
            Assert.AreEqual(3UL, clockDomainId.Value);
        }

        [Test]
        public void StableIdParsingRequiresExactlyThirtyTwoHexCharacters()
        {
            string[] invalidValues =
            {
                null,
                string.Empty,
                "1",
                new string('0', 31),
                new string('0', 33),
                new string('g', 32),
                new string('0', 32)
            };

            foreach (string invalidValue in invalidValues)
            {
                Assert.IsFalse(CoCoGraphId.TryParse(invalidValue, out _));
                Assert.IsFalse(CoCoTimelineId.TryParse(invalidValue, out _));
            }
        }

        [Test]
        public void IdentityTypesAreReadonlyValuesWithGetterOnlyComponents()
        {
            AssertImmutableId(typeof(CoCoGraphId), "High", "Low");
            AssertImmutableId(typeof(CoCoLayerId), "High", "Low");
            AssertImmutableId(typeof(CoCoStateId), "High", "Low");
            AssertImmutableId(typeof(CoCoTransitionId), "High", "Low");
            AssertImmutableId(typeof(CoCoTimelineId), "High", "Low");
            AssertImmutableId(typeof(CoCoGraphInstanceId), "Value");
            AssertImmutableId(typeof(CoCoActivationId), "Value");
            AssertImmutableId(typeof(CoCoClockDomainId), "Value");
        }

        [Test]
        public void IdentityTypesDoNotDeclareSerializableAttribute()
        {
            foreach (Type idType in GetIdentityTypes())
            {
                Assert.IsFalse(
                    idType.IsDefined(typeof(SerializableAttribute), false),
                    idType.FullName);
            }
        }

        [Test]
        public void EqualIdsHaveDeterministicEqualityAndHashCodes()
        {
            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var leftGraph));
            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var rightGraph));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var leftLayer));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var rightLayer));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var leftState));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var rightState));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var leftTransition));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var rightTransition));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var leftTimeline));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var rightTimeline));
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(11UL, out var leftInstance));
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(11UL, out var rightInstance));
            Assert.IsTrue(CoCoActivationId.TryCreate(12UL, out var leftActivation));
            Assert.IsTrue(CoCoActivationId.TryCreate(12UL, out var rightActivation));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(13UL, out var leftClockDomain));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(13UL, out var rightClockDomain));

            AssertEqualAndHash(leftGraph, rightGraph);
            AssertEqualAndHash(leftLayer, rightLayer);
            AssertEqualAndHash(leftState, rightState);
            AssertEqualAndHash(leftTransition, rightTransition);
            AssertEqualAndHash(leftTimeline, rightTimeline);
            AssertEqualAndHash(leftInstance, rightInstance);
            AssertEqualAndHash(leftActivation, rightActivation);
            AssertEqualAndHash(leftClockDomain, rightClockDomain);
        }

        private static void AssertImmutableId(Type idType, params string[] componentNames)
        {
            Assert.IsTrue(idType.IsValueType, idType.FullName);
            Assert.IsTrue(idType.IsDefined(typeof(IsReadOnlyAttribute), false), idType.FullName);
            Assert.IsEmpty(
                idType.GetFields(BindingFlags.Public | BindingFlags.Instance),
                idType.FullName);

            foreach (string componentName in componentNames)
            {
                PropertyInfo component = idType.GetProperty(
                    componentName,
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(component, $"{idType.FullName}.{componentName}");
                Assert.AreEqual(typeof(ulong), component.PropertyType);
                Assert.IsNotNull(component.GetGetMethod(false));
                Assert.IsNull(component.GetSetMethod(true));
            }
        }

        private static Type[] GetIdentityTypes()
        {
            return new[]
            {
                typeof(CoCoGraphId),
                typeof(CoCoLayerId),
                typeof(CoCoStateId),
                typeof(CoCoTransitionId),
                typeof(CoCoTimelineId),
                typeof(CoCoGraphInstanceId),
                typeof(CoCoActivationId),
                typeof(CoCoClockDomainId)
            };
        }

        private static void AssertEqualAndHash<T>(T left, T right)
        {
            Assert.AreEqual(left, right);
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }
    }
}
