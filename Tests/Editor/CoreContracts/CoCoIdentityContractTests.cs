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
            Assert.IsFalse(CoCoFrameLayoutId.TryCreate(0UL, 0UL, out var frameLayoutId));
            Assert.IsFalse(CoCoOperationSectionId.TryCreate(0UL, 0UL, out var operationSectionId));
            Assert.IsFalse(CoCoOperatorId.TryCreate(0UL, 0UL, out var operatorId));
            Assert.IsFalse(CoCoOperatorClaimId.TryCreate(0UL, 0UL, out var operatorClaimId));
            Assert.IsFalse(CoCoIntentId.TryCreate(0UL, 0UL, out var intentId));
            Assert.IsFalse(CoCoStateBlockId.TryCreate(0UL, 0UL, out var stateBlockId));
            Assert.IsFalse(CoCoStateSlotId.TryCreate(0UL, 0UL, out var stateSlotId));
            Assert.IsFalse(CoCoEventTypeId.TryCreate(0UL, 0UL, out var eventTypeId));
            Assert.IsFalse(CoCoStableEntityId.TryCreate(0UL, 0UL, out var stableEntityId));
            Assert.IsFalse(CoCoTemporalEntityId.TryCreate(0UL, 0UL, out var temporalEntityId));
            Assert.IsFalse(CoCoCorrelationId.TryCreate(0UL, 0UL, out var correlationId));
            Assert.IsFalse(CoCoCodecId.TryCreate(0UL, 0UL, out var codecId));

            Assert.IsFalse(graphId.IsValid);
            Assert.IsFalse(layerId.IsValid);
            Assert.IsFalse(stateId.IsValid);
            Assert.IsFalse(transitionId.IsValid);
            Assert.IsFalse(timelineId.IsValid);
            Assert.IsFalse(frameLayoutId.IsValid);
            Assert.IsFalse(operationSectionId.IsValid);
            Assert.IsFalse(operatorId.IsValid);
            Assert.IsFalse(operatorClaimId.IsValid);
            Assert.IsFalse(intentId.IsValid);
            Assert.IsFalse(stateBlockId.IsValid);
            Assert.IsFalse(stateSlotId.IsValid);
            Assert.IsFalse(eventTypeId.IsValid);
            Assert.IsFalse(stableEntityId.IsValid);
            Assert.IsFalse(temporalEntityId.IsValid);
            Assert.IsFalse(correlationId.IsValid);
            Assert.IsFalse(codecId.IsValid);
        }

        [Test]
        public void StateFlowStableIdsUseTwoUlongPartsAndRoundTrip()
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(1UL, 2UL, out var frameLayoutId));
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(3UL, 4UL, out var operationSectionId));
            Assert.IsTrue(CoCoOperatorId.TryCreate(5UL, 6UL, out var operatorId));
            Assert.IsTrue(CoCoOperatorClaimId.TryCreate(7UL, 8UL, out var operatorClaimId));
            Assert.IsTrue(CoCoIntentId.TryCreate(9UL, 10UL, out var intentId));
            Assert.IsTrue(CoCoStateBlockId.TryCreate(11UL, 12UL, out var stateBlockId));
            Assert.IsTrue(CoCoStateSlotId.TryCreate(13UL, 14UL, out var stateSlotId));
            Assert.IsTrue(CoCoEventTypeId.TryCreate(15UL, 16UL, out var eventTypeId));
            Assert.IsTrue(CoCoStableEntityId.TryCreate(17UL, 18UL, out var stableEntityId));
            Assert.IsTrue(CoCoTemporalEntityId.TryCreate(19UL, 20UL, out var temporalEntityId));
            Assert.IsTrue(CoCoCorrelationId.TryCreate(21UL, 22UL, out var correlationId));
            Assert.IsTrue(CoCoCodecId.TryCreate(23UL, 24UL, out var codecId));

            AssertStableIdRoundTrip(frameLayoutId, 1UL, 2UL, CoCoFrameLayoutId.TryParse);
            AssertStableIdRoundTrip(operationSectionId, 3UL, 4UL, CoCoOperationSectionId.TryParse);
            AssertStableIdRoundTrip(operatorId, 5UL, 6UL, CoCoOperatorId.TryParse);
            AssertStableIdRoundTrip(operatorClaimId, 7UL, 8UL, CoCoOperatorClaimId.TryParse);
            AssertStableIdRoundTrip(intentId, 9UL, 10UL, CoCoIntentId.TryParse);
            AssertStableIdRoundTrip(stateBlockId, 11UL, 12UL, CoCoStateBlockId.TryParse);
            AssertStableIdRoundTrip(stateSlotId, 13UL, 14UL, CoCoStateSlotId.TryParse);
            AssertStableIdRoundTrip(eventTypeId, 15UL, 16UL, CoCoEventTypeId.TryParse);
            AssertStableIdRoundTrip(stableEntityId, 17UL, 18UL, CoCoStableEntityId.TryParse);
            AssertStableIdRoundTrip(temporalEntityId, 19UL, 20UL, CoCoTemporalEntityId.TryParse);
            AssertStableIdRoundTrip(correlationId, 21UL, 22UL, CoCoCorrelationId.TryParse);
            AssertStableIdRoundTrip(codecId, 23UL, 24UL, CoCoCodecId.TryParse);
        }

        [Test]
        public void RuntimeAndClockIdsRequireNonZeroValues()
        {
            Assert.IsFalse(CoCoGraphInstanceId.TryCreate(0UL, out var invalidInstanceId));
            Assert.IsFalse(CoCoActivationId.TryCreate(0UL, out var invalidActivationId));
            Assert.IsFalse(CoCoClockDomainId.TryCreate(0UL, out var invalidClockDomainId));
            Assert.IsFalse(CoCoEventDomainId.TryCreate(0UL, out var invalidEventDomainId));
            Assert.IsFalse(CoCoOperationSequence.TryCreate(0UL, out var invalidOperationSequence));
            Assert.IsFalse(CoCoEventSequence.TryCreate(0UL, out var invalidEventSequence));

            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(1UL, out var instanceId));
            Assert.IsTrue(CoCoActivationId.TryCreate(2UL, out var activationId));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(3UL, out var clockDomainId));
            Assert.IsTrue(CoCoEventDomainId.TryCreate(4UL, out var eventDomainId));
            Assert.IsTrue(CoCoOperationSequence.TryCreate(5UL, out var operationSequence));
            Assert.IsTrue(CoCoEventSequence.TryCreate(6UL, out var eventSequence));

            Assert.IsFalse(invalidInstanceId.IsValid);
            Assert.IsFalse(invalidActivationId.IsValid);
            Assert.IsFalse(invalidClockDomainId.IsValid);
            Assert.IsFalse(invalidEventDomainId.IsValid);
            Assert.IsFalse(invalidOperationSequence.IsValid);
            Assert.IsFalse(invalidEventSequence.IsValid);
            Assert.AreEqual(1UL, instanceId.Value);
            Assert.AreEqual(2UL, activationId.Value);
            Assert.AreEqual(3UL, clockDomainId.Value);
            Assert.AreEqual(4UL, eventDomainId.Value);
            Assert.AreEqual(5UL, operationSequence.Value);
            Assert.AreEqual(6UL, eventSequence.Value);
            Assert.AreEqual(CoCoEventSequence.Zero, invalidEventSequence);
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
            AssertImmutableId(typeof(CoCoFrameLayoutId), "High", "Low");
            AssertImmutableId(typeof(CoCoOperationSectionId), "High", "Low");
            AssertImmutableId(typeof(CoCoOperatorId), "High", "Low");
            AssertImmutableId(typeof(CoCoOperatorClaimId), "High", "Low");
            AssertImmutableId(typeof(CoCoIntentId), "High", "Low");
            AssertImmutableId(typeof(CoCoStateBlockId), "High", "Low");
            AssertImmutableId(typeof(CoCoStateSlotId), "High", "Low");
            AssertImmutableId(typeof(CoCoEventTypeId), "High", "Low");
            AssertImmutableId(typeof(CoCoStableEntityId), "High", "Low");
            AssertImmutableId(typeof(CoCoTemporalEntityId), "High", "Low");
            AssertImmutableId(typeof(CoCoCorrelationId), "High", "Low");
            AssertImmutableId(typeof(CoCoCodecId), "High", "Low");
            AssertImmutableId(typeof(CoCoEventDomainId), "Value");
            AssertImmutableId(typeof(CoCoOperationSequence), "Value");
            AssertImmutableId(typeof(CoCoEventSequence), "Value");
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
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(14UL, 15UL, out var leftFrameLayout));
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(14UL, 15UL, out var rightFrameLayout));
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(16UL, 17UL, out var leftOperationSection));
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(16UL, 17UL, out var rightOperationSection));
            Assert.IsTrue(CoCoOperatorId.TryCreate(18UL, 19UL, out var leftOperator));
            Assert.IsTrue(CoCoOperatorId.TryCreate(18UL, 19UL, out var rightOperator));
            Assert.IsTrue(CoCoOperatorClaimId.TryCreate(20UL, 21UL, out var leftOperatorClaim));
            Assert.IsTrue(CoCoOperatorClaimId.TryCreate(20UL, 21UL, out var rightOperatorClaim));
            Assert.IsTrue(CoCoIntentId.TryCreate(22UL, 23UL, out var leftIntent));
            Assert.IsTrue(CoCoIntentId.TryCreate(22UL, 23UL, out var rightIntent));
            Assert.IsTrue(CoCoStateBlockId.TryCreate(24UL, 25UL, out var leftStateBlock));
            Assert.IsTrue(CoCoStateBlockId.TryCreate(24UL, 25UL, out var rightStateBlock));
            Assert.IsTrue(CoCoStateSlotId.TryCreate(26UL, 27UL, out var leftStateSlot));
            Assert.IsTrue(CoCoStateSlotId.TryCreate(26UL, 27UL, out var rightStateSlot));
            Assert.IsTrue(CoCoEventTypeId.TryCreate(28UL, 29UL, out var leftEventType));
            Assert.IsTrue(CoCoEventTypeId.TryCreate(28UL, 29UL, out var rightEventType));
            Assert.IsTrue(CoCoStableEntityId.TryCreate(30UL, 31UL, out var leftStableEntity));
            Assert.IsTrue(CoCoStableEntityId.TryCreate(30UL, 31UL, out var rightStableEntity));
            Assert.IsTrue(CoCoTemporalEntityId.TryCreate(32UL, 33UL, out var leftTemporalEntity));
            Assert.IsTrue(CoCoTemporalEntityId.TryCreate(32UL, 33UL, out var rightTemporalEntity));
            Assert.IsTrue(CoCoCorrelationId.TryCreate(34UL, 35UL, out var leftCorrelation));
            Assert.IsTrue(CoCoCorrelationId.TryCreate(34UL, 35UL, out var rightCorrelation));
            Assert.IsTrue(CoCoCodecId.TryCreate(36UL, 37UL, out var leftCodec));
            Assert.IsTrue(CoCoCodecId.TryCreate(36UL, 37UL, out var rightCodec));
            Assert.IsTrue(CoCoEventDomainId.TryCreate(32UL, out var leftEventDomain));
            Assert.IsTrue(CoCoEventDomainId.TryCreate(32UL, out var rightEventDomain));
            Assert.IsTrue(CoCoOperationSequence.TryCreate(33UL, out var leftOperationSequence));
            Assert.IsTrue(CoCoOperationSequence.TryCreate(33UL, out var rightOperationSequence));
            Assert.IsTrue(CoCoEventSequence.TryCreate(34UL, out var leftEventSequence));
            Assert.IsTrue(CoCoEventSequence.TryCreate(34UL, out var rightEventSequence));

            AssertEqualAndHash(leftGraph, rightGraph);
            AssertEqualAndHash(leftLayer, rightLayer);
            AssertEqualAndHash(leftState, rightState);
            AssertEqualAndHash(leftTransition, rightTransition);
            AssertEqualAndHash(leftTimeline, rightTimeline);
            AssertEqualAndHash(leftInstance, rightInstance);
            AssertEqualAndHash(leftActivation, rightActivation);
            AssertEqualAndHash(leftClockDomain, rightClockDomain);
            AssertEqualAndHash(leftFrameLayout, rightFrameLayout);
            AssertEqualAndHash(leftOperationSection, rightOperationSection);
            AssertEqualAndHash(leftOperator, rightOperator);
            AssertEqualAndHash(leftOperatorClaim, rightOperatorClaim);
            AssertEqualAndHash(leftIntent, rightIntent);
            AssertEqualAndHash(leftStateBlock, rightStateBlock);
            AssertEqualAndHash(leftStateSlot, rightStateSlot);
            AssertEqualAndHash(leftEventType, rightEventType);
            AssertEqualAndHash(leftStableEntity, rightStableEntity);
            AssertEqualAndHash(leftTemporalEntity, rightTemporalEntity);
            AssertEqualAndHash(leftCorrelation, rightCorrelation);
            AssertEqualAndHash(leftCodec, rightCodec);
            AssertEqualAndHash(leftEventDomain, rightEventDomain);
            AssertEqualAndHash(leftOperationSequence, rightOperationSequence);
            AssertEqualAndHash(leftEventSequence, rightEventSequence);
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
                typeof(CoCoClockDomainId),
                typeof(CoCoFrameLayoutId),
                typeof(CoCoOperationSectionId),
                typeof(CoCoOperatorId),
                typeof(CoCoOperatorClaimId),
                typeof(CoCoIntentId),
                typeof(CoCoStateBlockId),
                typeof(CoCoStateSlotId),
                typeof(CoCoEventTypeId),
                typeof(CoCoStableEntityId),
                typeof(CoCoTemporalEntityId),
                typeof(CoCoCorrelationId),
                typeof(CoCoCodecId),
                typeof(CoCoEventDomainId),
                typeof(CoCoOperationSequence),
                typeof(CoCoEventSequence)
            };
        }

        private delegate bool TryParseStableId<T>(string value, out T id);

        private static void AssertStableIdRoundTrip<T>(
            T id,
            ulong expectedHigh,
            ulong expectedLow,
            TryParseStableId<T> tryParse)
        {
            Type idType = typeof(T);
            Assert.AreEqual(expectedHigh, idType.GetProperty("High")?.GetValue(id));
            Assert.AreEqual(expectedLow, idType.GetProperty("Low")?.GetValue(id));

            string serialized = id.ToString();
            Assert.AreEqual(32, serialized.Length);
            Assert.IsTrue(tryParse(serialized, out T parsed));
            Assert.AreEqual(id, parsed);
        }

        private static void AssertEqualAndHash<T>(T left, T right)
        {
            Assert.AreEqual(left, right);
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }
    }
}
