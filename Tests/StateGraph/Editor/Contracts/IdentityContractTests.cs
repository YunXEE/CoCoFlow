using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class IdentityContractTests
    {
        [Test]
        public void TopologyAndTimelineIdsUseTwoUlongParts()
        {
            Assert.IsTrue(GraphId.TryCreate(1UL, 2UL, out var graphId));
            Assert.IsTrue(LayerId.TryCreate(3UL, 4UL, out var layerId));
            Assert.IsTrue(StateId.TryCreate(5UL, 6UL, out var stateId));
            Assert.IsTrue(TransitionId.TryCreate(7UL, 8UL, out var transitionId));
            Assert.IsTrue(TimelineId.TryCreate(9UL, 10UL, out var timelineId));

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

            Assert.IsTrue(GraphId.TryParse(graphId.ToString(), out var parsedGraphId));
            Assert.IsTrue(LayerId.TryParse(layerId.ToString(), out var parsedLayerId));
            Assert.IsTrue(StateId.TryParse(stateId.ToString(), out var parsedStateId));
            Assert.IsTrue(TransitionId.TryParse(transitionId.ToString(), out var parsedTransitionId));
            Assert.IsTrue(TimelineId.TryParse(timelineId.ToString(), out var parsedTimelineId));

            Assert.AreEqual(graphId, parsedGraphId);
            Assert.AreEqual(layerId, parsedLayerId);
            Assert.AreEqual(stateId, parsedStateId);
            Assert.AreEqual(transitionId, parsedTransitionId);
            Assert.AreEqual(timelineId, parsedTimelineId);
        }

        [Test]
        public void StableIdsRejectAllZeroValues()
        {
            Assert.IsFalse(GraphId.TryCreate(0UL, 0UL, out var graphId));
            Assert.IsFalse(LayerId.TryCreate(0UL, 0UL, out var layerId));
            Assert.IsFalse(StateId.TryCreate(0UL, 0UL, out var stateId));
            Assert.IsFalse(TransitionId.TryCreate(0UL, 0UL, out var transitionId));
            Assert.IsFalse(TimelineId.TryCreate(0UL, 0UL, out var timelineId));
            Assert.IsFalse(FrameLayoutId.TryCreate(0UL, 0UL, out var frameLayoutId));
            Assert.IsFalse(OperationId.TryCreate(0UL, 0UL, out var operationSectionId));
            Assert.IsFalse(OperatorId.TryCreate(0UL, 0UL, out var operatorId));
            Assert.IsFalse(OperatorClaimId.TryCreate(0UL, 0UL, out var operatorClaimId));
            Assert.IsFalse(IntentId.TryCreate(0UL, 0UL, out var intentId));
            Assert.IsFalse(StateBlockId.TryCreate(0UL, 0UL, out var stateBlockId));
            Assert.IsFalse(StateSlotId.TryCreate(0UL, 0UL, out var stateSlotId));
            Assert.IsFalse(EventTypeId.TryCreate(0UL, 0UL, out var eventTypeId));
            Assert.IsFalse(StableEntityId.TryCreate(0UL, 0UL, out var stableEntityId));
            Assert.IsFalse(TemporalEntityId.TryCreate(0UL, 0UL, out var temporalEntityId));
            Assert.IsFalse(CorrelationId.TryCreate(0UL, 0UL, out var correlationId));
            Assert.IsFalse(CodecId.TryCreate(0UL, 0UL, out var codecId));

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
            Assert.IsTrue(FrameLayoutId.TryCreate(1UL, 2UL, out var frameLayoutId));
            Assert.IsTrue(OperationId.TryCreate(3UL, 4UL, out var operationSectionId));
            Assert.IsTrue(OperatorId.TryCreate(5UL, 6UL, out var operatorId));
            Assert.IsTrue(OperatorClaimId.TryCreate(7UL, 8UL, out var operatorClaimId));
            Assert.IsTrue(IntentId.TryCreate(9UL, 10UL, out var intentId));
            Assert.IsTrue(StateBlockId.TryCreate(11UL, 12UL, out var stateBlockId));
            Assert.IsTrue(StateSlotId.TryCreate(13UL, 14UL, out var stateSlotId));
            Assert.IsTrue(EventTypeId.TryCreate(15UL, 16UL, out var eventTypeId));
            Assert.IsTrue(StableEntityId.TryCreate(17UL, 18UL, out var stableEntityId));
            Assert.IsTrue(TemporalEntityId.TryCreate(19UL, 20UL, out var temporalEntityId));
            Assert.IsTrue(CorrelationId.TryCreate(21UL, 22UL, out var correlationId));
            Assert.IsTrue(CodecId.TryCreate(23UL, 24UL, out var codecId));

            AssertStableIdRoundTrip(frameLayoutId, 1UL, 2UL, FrameLayoutId.TryParse);
            AssertStableIdRoundTrip(operationSectionId, 3UL, 4UL, OperationId.TryParse);
            AssertStableIdRoundTrip(operatorId, 5UL, 6UL, OperatorId.TryParse);
            AssertStableIdRoundTrip(operatorClaimId, 7UL, 8UL, OperatorClaimId.TryParse);
            AssertStableIdRoundTrip(intentId, 9UL, 10UL, IntentId.TryParse);
            AssertStableIdRoundTrip(stateBlockId, 11UL, 12UL, StateBlockId.TryParse);
            AssertStableIdRoundTrip(stateSlotId, 13UL, 14UL, StateSlotId.TryParse);
            AssertStableIdRoundTrip(eventTypeId, 15UL, 16UL, EventTypeId.TryParse);
            AssertStableIdRoundTrip(stableEntityId, 17UL, 18UL, StableEntityId.TryParse);
            AssertStableIdRoundTrip(temporalEntityId, 19UL, 20UL, TemporalEntityId.TryParse);
            AssertStableIdRoundTrip(correlationId, 21UL, 22UL, CorrelationId.TryParse);
            AssertStableIdRoundTrip(codecId, 23UL, 24UL, CodecId.TryParse);
        }

        [Test]
        public void RuntimeAndClockIdsRequireNonZeroValues()
        {
            Assert.IsFalse(GraphInstanceId.TryCreate(0UL, out var invalidInstanceId));
            Assert.IsFalse(ActivationId.TryCreate(0UL, out var invalidActivationId));
            Assert.IsFalse(ClockDomainId.TryCreate(0UL, out var invalidClockDomainId));
            Assert.IsFalse(EventDomainId.TryCreate(0UL, out var invalidEventDomainId));
            Assert.IsFalse(OperationSequence.TryCreate(0UL, out var invalidOperationSequence));
            Assert.IsFalse(EventSequence.TryCreate(0UL, out var invalidEventSequence));

            Assert.IsTrue(GraphInstanceId.TryCreate(1UL, out var instanceId));
            Assert.IsTrue(ActivationId.TryCreate(2UL, out var activationId));
            Assert.IsTrue(ClockDomainId.TryCreate(3UL, out var clockDomainId));
            Assert.IsTrue(EventDomainId.TryCreate(4UL, out var eventDomainId));
            Assert.IsTrue(OperationSequence.TryCreate(5UL, out var operationSequence));
            Assert.IsTrue(EventSequence.TryCreate(6UL, out var eventSequence));

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
            Assert.AreEqual(EventSequence.Zero, invalidEventSequence);
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
                Assert.IsFalse(GraphId.TryParse(invalidValue, out _));
                Assert.IsFalse(TimelineId.TryParse(invalidValue, out _));
            }
        }

        [Test]
        public void IdentityTypesAreReadonlyValuesWithGetterOnlyComponents()
        {
            AssertImmutableId(typeof(GraphId), "High", "Low");
            AssertImmutableId(typeof(LayerId), "High", "Low");
            AssertImmutableId(typeof(StateId), "High", "Low");
            AssertImmutableId(typeof(TransitionId), "High", "Low");
            AssertImmutableId(typeof(TimelineId), "High", "Low");
            AssertImmutableId(typeof(GraphInstanceId), "Value");
            AssertImmutableId(typeof(ActivationId), "Value");
            AssertImmutableId(typeof(ClockDomainId), "Value");
            AssertImmutableId(typeof(FrameLayoutId), "High", "Low");
            AssertImmutableId(typeof(OperationId), "High", "Low");
            AssertImmutableId(typeof(OperatorId), "High", "Low");
            AssertImmutableId(typeof(OperatorClaimId), "High", "Low");
            AssertImmutableId(typeof(IntentId), "High", "Low");
            AssertImmutableId(typeof(StateBlockId), "High", "Low");
            AssertImmutableId(typeof(StateSlotId), "High", "Low");
            AssertImmutableId(typeof(EventTypeId), "High", "Low");
            AssertImmutableId(typeof(StableEntityId), "High", "Low");
            AssertImmutableId(typeof(TemporalEntityId), "High", "Low");
            AssertImmutableId(typeof(CorrelationId), "High", "Low");
            AssertImmutableId(typeof(CodecId), "High", "Low");
            AssertImmutableId(typeof(EventDomainId), "Value");
            AssertImmutableId(typeof(OperationSequence), "Value");
            AssertImmutableId(typeof(EventSequence), "Value");
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
            Assert.IsTrue(GraphId.TryCreate(1UL, 2UL, out var leftGraph));
            Assert.IsTrue(GraphId.TryCreate(1UL, 2UL, out var rightGraph));
            Assert.IsTrue(LayerId.TryCreate(3UL, 4UL, out var leftLayer));
            Assert.IsTrue(LayerId.TryCreate(3UL, 4UL, out var rightLayer));
            Assert.IsTrue(StateId.TryCreate(5UL, 6UL, out var leftState));
            Assert.IsTrue(StateId.TryCreate(5UL, 6UL, out var rightState));
            Assert.IsTrue(TransitionId.TryCreate(7UL, 8UL, out var leftTransition));
            Assert.IsTrue(TransitionId.TryCreate(7UL, 8UL, out var rightTransition));
            Assert.IsTrue(TimelineId.TryCreate(9UL, 10UL, out var leftTimeline));
            Assert.IsTrue(TimelineId.TryCreate(9UL, 10UL, out var rightTimeline));
            Assert.IsTrue(GraphInstanceId.TryCreate(11UL, out var leftInstance));
            Assert.IsTrue(GraphInstanceId.TryCreate(11UL, out var rightInstance));
            Assert.IsTrue(ActivationId.TryCreate(12UL, out var leftActivation));
            Assert.IsTrue(ActivationId.TryCreate(12UL, out var rightActivation));
            Assert.IsTrue(ClockDomainId.TryCreate(13UL, out var leftClockDomain));
            Assert.IsTrue(ClockDomainId.TryCreate(13UL, out var rightClockDomain));
            Assert.IsTrue(FrameLayoutId.TryCreate(14UL, 15UL, out var leftFrameLayout));
            Assert.IsTrue(FrameLayoutId.TryCreate(14UL, 15UL, out var rightFrameLayout));
            Assert.IsTrue(OperationId.TryCreate(16UL, 17UL, out var leftOperationSection));
            Assert.IsTrue(OperationId.TryCreate(16UL, 17UL, out var rightOperationSection));
            Assert.IsTrue(OperatorId.TryCreate(18UL, 19UL, out var leftOperator));
            Assert.IsTrue(OperatorId.TryCreate(18UL, 19UL, out var rightOperator));
            Assert.IsTrue(OperatorClaimId.TryCreate(20UL, 21UL, out var leftOperatorClaim));
            Assert.IsTrue(OperatorClaimId.TryCreate(20UL, 21UL, out var rightOperatorClaim));
            Assert.IsTrue(IntentId.TryCreate(22UL, 23UL, out var leftIntent));
            Assert.IsTrue(IntentId.TryCreate(22UL, 23UL, out var rightIntent));
            Assert.IsTrue(StateBlockId.TryCreate(24UL, 25UL, out var leftStateBlock));
            Assert.IsTrue(StateBlockId.TryCreate(24UL, 25UL, out var rightStateBlock));
            Assert.IsTrue(StateSlotId.TryCreate(26UL, 27UL, out var leftStateSlot));
            Assert.IsTrue(StateSlotId.TryCreate(26UL, 27UL, out var rightStateSlot));
            Assert.IsTrue(EventTypeId.TryCreate(28UL, 29UL, out var leftEventType));
            Assert.IsTrue(EventTypeId.TryCreate(28UL, 29UL, out var rightEventType));
            Assert.IsTrue(StableEntityId.TryCreate(30UL, 31UL, out var leftStableEntity));
            Assert.IsTrue(StableEntityId.TryCreate(30UL, 31UL, out var rightStableEntity));
            Assert.IsTrue(TemporalEntityId.TryCreate(32UL, 33UL, out var leftTemporalEntity));
            Assert.IsTrue(TemporalEntityId.TryCreate(32UL, 33UL, out var rightTemporalEntity));
            Assert.IsTrue(CorrelationId.TryCreate(34UL, 35UL, out var leftCorrelation));
            Assert.IsTrue(CorrelationId.TryCreate(34UL, 35UL, out var rightCorrelation));
            Assert.IsTrue(CodecId.TryCreate(36UL, 37UL, out var leftCodec));
            Assert.IsTrue(CodecId.TryCreate(36UL, 37UL, out var rightCodec));
            Assert.IsTrue(EventDomainId.TryCreate(32UL, out var leftEventDomain));
            Assert.IsTrue(EventDomainId.TryCreate(32UL, out var rightEventDomain));
            Assert.IsTrue(OperationSequence.TryCreate(33UL, out var leftOperationSequence));
            Assert.IsTrue(OperationSequence.TryCreate(33UL, out var rightOperationSequence));
            Assert.IsTrue(EventSequence.TryCreate(34UL, out var leftEventSequence));
            Assert.IsTrue(EventSequence.TryCreate(34UL, out var rightEventSequence));

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
                typeof(GraphId),
                typeof(LayerId),
                typeof(StateId),
                typeof(TransitionId),
                typeof(TimelineId),
                typeof(GraphInstanceId),
                typeof(ActivationId),
                typeof(ClockDomainId),
                typeof(FrameLayoutId),
                typeof(OperationId),
                typeof(OperatorId),
                typeof(OperatorClaimId),
                typeof(IntentId),
                typeof(StateBlockId),
                typeof(StateSlotId),
                typeof(EventTypeId),
                typeof(StableEntityId),
                typeof(TemporalEntityId),
                typeof(CorrelationId),
                typeof(CodecId),
                typeof(EventDomainId),
                typeof(OperationSequence),
                typeof(EventSequence)
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
