using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Animation;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.Animation
{
    public sealed class AnimContractTests
    {
        [Test]
        public void FixedCapacities_RemainFrozen()
        {
            Assert.AreEqual(16, AnimContractLimits.ParameterLaneCount);
            Assert.AreEqual(8, AnimContractLimits.TriggerLaneCount);
            Assert.AreEqual(16, AnimContractLimits.FeedbackCapacity);
        }

        [Test]
        public void AutoOperator_UsesOneHostExclusiveAnimationIdentity()
        {
            Assert.AreEqual(
                AnimContractIds.OperatorId,
                AnimContractIds.AutoOperatorId);
        }

        [Test]
        public void FeedbackIntent_PreservesOrderAndReportsOverflow()
        {
            AnimFeedbackIntent intent = default;
            for (int index = 0;
                 index < AnimContractLimits.FeedbackCapacity + 1;
                 index++)
            {
                Assert.IsTrue(
                    AnimFeedbackRecord.TryCreateRootMotion(
                        index,
                        0f,
                        0f,
                        0f,
                        0f,
                        0f,
                        1f,
                        out AnimFeedbackRecord record));
                intent = intent.Append(record);
            }

            Assert.AreEqual(AnimContractLimits.FeedbackCapacity, intent.Count);
            Assert.IsTrue(intent.Overflowed);
            for (int index = 0; index < intent.Count; index++)
            {
                Assert.IsTrue(intent.TryGetRecord(index, out AnimFeedbackRecord record));
                Assert.AreEqual(index, record.PositionX);
            }
        }

        [Test]
        public void RootMotionFeedback_ProjectsThroughTypedEventAdapter()
        {
            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    1f,
                    2f,
                    3f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord record));
            Assert.IsTrue(
                AnimFeedbackEvent.TryCreate(
                    record,
                    out AnimFeedbackEvent feedbackEvent));
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    801UL,
                    out CoCoGraphInstanceId source));
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    802UL,
                    out CoCoGraphInstanceId target));
            Assert.IsTrue(
                CoCoEventSequence.TryCreate(
                    1UL,
                    out CoCoEventSequence sequence));
            Assert.IsTrue(
                CoCoActorEventEnvelope.TryCreate(
                    AnimContractIds.FeedbackEventTypeId,
                    AnimContractIds.FeedbackEventDomainId,
                    source,
                    target,
                    new CoCoTimelineEpoch(0UL),
                    new CoCoTimelineTick(1UL),
                    sequence,
                    CoCoEventDeliveryMode.Targeted,
                    CoCoEventReliability.Reliable,
                    default,
                    default,
                    default,
                    out CoCoActorEventEnvelope envelope));
            Assert.IsTrue(
                CoCoEventPacket<AnimFeedbackEvent>.TryCreate(
                    envelope,
                    feedbackEvent,
                    out CoCoEventPacket<AnimFeedbackEvent> packet));
            var adapter = new AnimFeedbackEventToIntentAdapter();

            Assert.IsTrue(
                adapter.TryProject(
                    packet,
                    out AnimFeedbackIntent intent));
            Assert.That(intent.Count, Is.EqualTo(1));
            Assert.IsTrue(intent.TryGetRecord(0, out AnimFeedbackRecord projected));
            Assert.That(projected.Kind, Is.EqualTo(AnimFeedbackKind.RootMotion));
            Assert.That(projected.PositionX, Is.EqualTo(1f));
        }

        [Test]
        public void FeedbackRecord_RejectsInvalidStateAndRotation()
        {
            Assert.IsFalse(
                AnimFeedbackRecord.TryCreateState(
                    AnimFeedbackKind.StateEnter,
                    default,
                    0,
                    0,
                    0,
                    0f,
                    out _));
            Assert.IsFalse(
                AnimFeedbackRecord.TryCreateRootMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    out _));
        }

        [Test]
        public void ProductionAssembly_HasExactlyOneMonoOperator()
        {
            Type[] monoTypes = typeof(AnimAutoOperator).Assembly
                .GetTypes()
                .Where(type =>
                    !type.IsAbstract &&
                    typeof(MonoBehaviour).IsAssignableFrom(type))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    typeof(AnimAutoOperator)
                },
                monoTypes);
            Assert.IsTrue(
                typeof(StateMachineBehaviour).IsAssignableFrom(
                    typeof(AnimEventSmb)));
        }

        [Test]
        public void DirectOperator_RequiresOnlyParametersAndTriggers()
        {
            CoCoOperatorDescriptor descriptor =
                AnimOperatorContracts.AutoDescriptor;
            Assert.IsTrue(
                descriptor.Requires.Contains(
                    AnimOperatorContracts.ParameterRequirement));
            Assert.IsTrue(
                descriptor.Requires.Contains(
                    AnimOperatorContracts.TriggerRequirement));


            Type[] forbiddenFieldTypes =
            {
                typeof(RuntimeAnimatorController),
                typeof(UnityEngine.Playables.PlayableGraph)
            };
            Type[] fieldTypes = typeof(AnimAutoOperator)
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .Select(field => field.FieldType)
                .ToArray();
            foreach (Type forbidden in forbiddenFieldTypes)
            {
                CollectionAssert.DoesNotContain(fieldTypes, forbidden);
            }
        }

        [Test]
        public void MarkerScan_CrossesLoopBoundaryInStableAbsoluteOrder()
        {
            AnimEventConfig[] configs =
            {
                CreateEventConfig(101UL, 0f),
                CreateEventConfig(102UL, 1f),
                CreateEventConfig(103UL, 0f),
                CreateEventConfig(104UL, 0.8f),
                CreateEventConfig(105UL, 0.8f)
            };
            var receiver = new RecordingReceiver();

            Assert.IsTrue(
                AnimEventSmb.TryEmitCrossedMarkers(
                    configs,
                    true,
                    77,
                    2,
                    0.7f,
                    1.1f,
                    receiver));

            Assert.That(
                receiver.Signals.Select(signal => signal.BindingId.Value),
                Is.EqualTo(new[] { 104UL, 105UL, 102UL, 101UL, 103UL }));
            Assert.That(
                receiver.Signals.Select(signal => signal.LoopCount),
                Is.EqualTo(new[] { 0, 0, 0, 1, 1 }));
            Assert.That(
                receiver.Signals.Select(signal => signal.NormalizedTime),
                Is.EqualTo(new[] { 0.8f, 0.8f, 1f, 0f, 0f }));
        }

        [Test]
        public void MarkerEntry_EmitsOnlyTheExactOffsetInStableBoundaryOrder()
        {
            AnimEventConfig[] configs =
            {
                CreateEventConfig(111UL, 0f),
                CreateEventConfig(112UL, 0.2f),
                CreateEventConfig(113UL, 1f),
                CreateEventConfig(114UL, 0f)
            };
            var initial = new RecordingReceiver();

            Assert.IsTrue(
                AnimEventSmb.TryEmitEntryMarkers(
                    configs,
                    true,
                    77,
                    2,
                    0f,
                    initial));
            Assert.That(
                initial.Signals.Select(signal => signal.BindingId.Value),
                Is.EqualTo(new[] { 111UL, 114UL }));
            Assert.That(
                initial.Signals.Select(signal => signal.LoopCount),
                Is.EqualTo(new[] { 0, 0 }));

            var offset = new RecordingReceiver();
            Assert.IsTrue(
                AnimEventSmb.TryEmitEntryMarkers(
                    configs,
                    false,
                    77,
                    2,
                    0.2f,
                    offset));
            Assert.That(
                offset.Signals.Select(signal => signal.BindingId.Value),
                Is.EqualTo(new[] { 112UL }));

            var boundary = new RecordingReceiver();
            Assert.IsTrue(
                AnimEventSmb.TryEmitEntryMarkers(
                    configs,
                    true,
                    77,
                    2,
                    1f,
                    boundary));
            Assert.That(
                boundary.Signals.Select(signal => signal.BindingId.Value),
                Is.EqualTo(new[] { 113UL, 111UL, 114UL }));
            Assert.That(
                boundary.Signals.Select(signal => signal.LoopCount),
                Is.EqualTo(new[] { 0, 1, 1 }));
        }

        [Test]
        public void MarkerScan_HandlesMultipleLoopsExitTailAndBackwardsReset()
        {
            AnimEventConfig[] configs =
            {
                CreateEventConfig(201UL, 0.2f),
                CreateEventConfig(202UL, 0.8f),
                CreateEventConfig(203UL, 1f)
            };
            var looping = new RecordingReceiver();

            Assert.IsTrue(
                AnimEventSmb.TryEmitCrossedMarkers(
                    configs,
                    true,
                    88,
                    0,
                    0.9f,
                    3.25f,
                    looping));
            Assert.That(
                looping.Signals.Select(signal => signal.LoopCount),
                Is.EqualTo(new[] { 0, 1, 1, 1, 2, 2, 2, 3 }));
            Assert.That(
                looping.Signals.Select(signal => signal.BindingId.Value),
                Is.EqualTo(
                    new[]
                    {
                        203UL,
                        201UL,
                        202UL,
                        203UL,
                        201UL,
                        202UL,
                        203UL,
                        201UL
                    }));

            var oneShot = new RecordingReceiver();
            Assert.IsTrue(
                AnimEventSmb.TryEmitCrossedMarkers(
                    configs,
                    false,
                    88,
                    0,
                    0.7f,
                    1.2f,
                    oneShot));
            Assert.That(
                oneShot.Signals.Select(signal => signal.BindingId.Value),
                Is.EqualTo(new[] { 202UL, 203UL }));

            var backwards = new RecordingReceiver();
            Assert.IsTrue(
                AnimEventSmb.TryEmitCrossedMarkers(
                    configs,
                    true,
                    88,
                    0,
                    1.1f,
                    0.4f,
                    backwards));
            Assert.That(backwards.Signals, Is.Empty);
        }

        [Test]
        public void MarkerScan_StopsImmediatelyWhenReceiverRejectsSeventeenth()
        {
            var configs = new AnimEventConfig[20];
            for (int index = 0; index < configs.Length; index++)
            {
                configs[index] = CreateEventConfig(
                    (ulong)(300 + index),
                    0.5f);
            }

            var receiver = new RecordingReceiver(
                AnimContractLimits.FeedbackCapacity);
            Assert.IsFalse(
                AnimEventSmb.TryEmitCrossedMarkers(
                    configs,
                    false,
                    99,
                    0,
                    0f,
                    1f,
                    receiver));
            Assert.That(
                receiver.Signals.Count,
                Is.EqualTo(AnimContractLimits.FeedbackCapacity));
            Assert.That(
                receiver.Attempts,
                Is.EqualTo(AnimContractLimits.FeedbackCapacity + 1));
        }

        [Test]
        public void FeedbackBuffer_SeventeenthRecordPoisonsWholeBatchAndBoundariesRecover()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    401UL,
                    out CoCoGraphInstanceId graphInstanceId));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graphInstanceId,
                    new CoCoTimelineEpoch(0UL),
                    new CoCoTimelineTick(1UL),
                    new CoCoExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp source));
            var buffer = new AnimFeedbackBuffer();

            for (int index = 0;
                 index < AnimContractLimits.FeedbackCapacity;
                 index++)
            {
                Assert.IsTrue(
                    AnimFeedbackRecord.TryCreateRootMotion(
                        index,
                        0f,
                        0f,
                        0f,
                        0f,
                        0f,
                        1f,
                        out AnimFeedbackRecord record));
                Assert.IsTrue(buffer.TryAppend(record, source));
            }

            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    17f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord overflow));
            Assert.IsFalse(buffer.TryAppend(overflow, source));
            Assert.That(
                buffer.Count,
                Is.EqualTo(AnimContractLimits.FeedbackCapacity));
            Assert.IsTrue(buffer.Overflowed);

            buffer.PrepareForTimeline(
                graphInstanceId,
                new CoCoTimelineEpoch(0UL));
            Assert.IsTrue(buffer.Overflowed);
            buffer.PrepareForTimeline(
                graphInstanceId,
                new CoCoTimelineEpoch(1UL));
            Assert.That(buffer.Count, Is.Zero);
            Assert.IsFalse(buffer.Overflowed);
            Assert.IsTrue(buffer.TryAppend(overflow, source));

            buffer.Clear();
            Assert.IsFalse(buffer.Overflowed);
        }

        [Test]
        public void FeedbackBuffer_NewTimelineCallbackBeforeExecutionStartsFreshBatch()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    501UL,
                    out CoCoGraphInstanceId oldGraph));
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    502UL,
                    out CoCoGraphInstanceId newGraph));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    oldGraph,
                    new CoCoTimelineEpoch(0UL),
                    new CoCoTimelineTick(1UL),
                    new CoCoExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp oldSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    newGraph,
                    new CoCoTimelineEpoch(1UL),
                    new CoCoTimelineTick(1UL),
                    new CoCoExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp newSource));
            var buffer = new AnimFeedbackBuffer();
            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    1f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord record));

            for (int index = 0;
                 index < AnimContractLimits.FeedbackCapacity;
                 index++)
            {
                Assert.IsTrue(buffer.TryAppend(record, oldSource));
            }

            Assert.IsFalse(buffer.TryAppend(record, oldSource));
            Assert.IsTrue(buffer.Overflowed);
            Assert.IsFalse(oldSource.IsSameTimeline(newSource));

            Assert.IsTrue(buffer.TryAppend(record, newSource));
            Assert.IsFalse(buffer.Overflowed);
            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        [Test]
        public void FeedbackBuffer_NewerCommittedFrameDropsStaleDirectCapacity()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    601UL,
                    out CoCoGraphInstanceId graph));
            var epoch = new CoCoTimelineEpoch(0UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    graph,
                    epoch,
                    new CoCoTimelineTick(1UL),
                    new CoCoExecutionSequence(1UL),
                    new CoCoContextRevision(1UL),
                    out AnimFeedbackSourceStamp staleSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    graph,
                    epoch,
                    new CoCoTimelineTick(8UL),
                    new CoCoExecutionSequence(8UL),
                    new CoCoContextRevision(8UL),
                    out AnimFeedbackSourceStamp currentSource));
            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    1f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord record));
            var buffer = new AnimFeedbackBuffer();
            for (int index = 0;
                 index < AnimContractLimits.FeedbackCapacity;
                 index++)
            {
                Assert.IsTrue(buffer.TryAppend(record, staleSource));
            }

            Assert.IsTrue(buffer.TryAppend(record, currentSource));
            Assert.IsFalse(buffer.Overflowed);
            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        [Test]
        public void FeedbackBuffer_NewerCandidateDropsStaleCandidateCapacity()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    701UL,
                    out CoCoGraphInstanceId graph));
            var epoch = new CoCoTimelineEpoch(0UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    epoch,
                    new CoCoTimelineTick(1UL),
                    new CoCoExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp staleSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    epoch,
                    new CoCoTimelineTick(8UL),
                    new CoCoExecutionSequence(8UL),
                    out AnimFeedbackSourceStamp currentSource));
            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    1f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord record));
            var buffer = new AnimFeedbackBuffer();
            for (int index = 0;
                 index < AnimContractLimits.FeedbackCapacity;
                 index++)
            {
                Assert.IsTrue(buffer.TryAppend(record, staleSource));
            }

            Assert.IsTrue(buffer.TryAppend(record, currentSource));
            Assert.IsFalse(buffer.Overflowed);
            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        [Test]
        public void FeedbackBuffer_DirectAndCandidateShareOneAtomicCapacity()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    751UL,
                    out CoCoGraphInstanceId graph));
            var epoch = new CoCoTimelineEpoch(0UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    graph,
                    epoch,
                    new CoCoTimelineTick(7UL),
                    new CoCoExecutionSequence(7UL),
                    new CoCoContextRevision(7UL),
                    out AnimFeedbackSourceStamp directSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    epoch,
                    new CoCoTimelineTick(8UL),
                    new CoCoExecutionSequence(8UL),
                    out AnimFeedbackSourceStamp candidateSource));
            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    1f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord record));
            var buffer = new AnimFeedbackBuffer();
            for (int index = 0; index < 8; index++)
            {
                Assert.IsTrue(
                    buffer.TryAppend(record, directSource));
            }

            for (int index = 0; index < 8; index++)
            {
                Assert.IsTrue(
                    buffer.TryAppend(record, candidateSource));
            }

            Assert.That(
                buffer.Count,
                Is.EqualTo(AnimContractLimits.FeedbackCapacity));
            Assert.IsFalse(buffer.Overflowed);
            Assert.IsFalse(
                buffer.TryAppend(record, candidateSource));
            Assert.That(
                buffer.Count,
                Is.EqualTo(AnimContractLimits.FeedbackCapacity));
            Assert.IsTrue(buffer.Overflowed);
        }

        [Test]
        public void FeedbackSourceStamp_RejectsOtherGraphEpochTickAndRevision()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    701UL,
                    out CoCoGraphInstanceId firstGraph));
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    702UL,
                    out CoCoGraphInstanceId secondGraph));
            var epoch = new CoCoTimelineEpoch(0UL);
            var tick = new CoCoTimelineTick(8UL);
            var sequence = new CoCoExecutionSequence(9UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    firstGraph,
                    epoch,
                    tick,
                    sequence,
                    out AnimFeedbackSourceStamp candidate));
            Assert.IsTrue(
                candidate.MatchesCandidate(
                    firstGraph,
                    epoch,
                    tick,
                    sequence));
            Assert.IsFalse(
                candidate.MatchesCandidate(
                    secondGraph,
                    epoch,
                    tick,
                    sequence));
            Assert.IsFalse(
                candidate.MatchesCandidate(
                    firstGraph,
                    new CoCoTimelineEpoch(1UL),
                    tick,
                    sequence));
            Assert.IsFalse(
                candidate.MatchesCandidate(
                    firstGraph,
                    epoch,
                    new CoCoTimelineTick(9UL),
                    sequence));

            var revision = new CoCoContextRevision(4UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    firstGraph,
                    epoch,
                    tick,
                    sequence,
                    revision,
                    out AnimFeedbackSourceStamp committed));
            Assert.IsTrue(
                committed.MatchesCommitted(
                    firstGraph,
                    epoch,
                    tick,
                    sequence,
                    revision));
            Assert.IsFalse(
                committed.MatchesCommitted(
                    firstGraph,
                    epoch,
                    tick,
                    sequence,
                    new CoCoContextRevision(5UL)));
            Assert.IsFalse(
                committed.MatchesCommitted(
                    secondGraph,
                    epoch,
                    tick,
                    sequence,
                    revision));
        }

        private static AnimEventConfig CreateEventConfig(
            ulong bindingId,
            float triggerTime)
        {
            var config = new AnimEventConfig();
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AnimEventConfig)
                .GetField("bindingId", flags)
                ?.SetValue(config, bindingId);
            typeof(AnimEventConfig)
                .GetField("eventName", flags)
                ?.SetValue(config, "Test");
            typeof(AnimEventConfig)
                .GetField("triggerTime", flags)
                ?.SetValue(config, triggerTime);
            return config;
        }

        private static CoCoStateFlowFrameIdentity CreateContextIdentity(
            CoCoGraphInstanceId graph,
            ulong tick)
        {
            return new CoCoStateFlowFrameIdentity(
                graph,
                new CoCoTimelineEpoch(0UL),
                new CoCoTimelineTick(tick),
                new CoCoExecutionSequence(tick),
                CoCoStateFlowFrameKind.Context);
        }

        private sealed class RecordingReceiver : IAnimEventReceiver
        {
            private readonly int _acceptedCapacity;

            internal RecordingReceiver(int acceptedCapacity = int.MaxValue)
            {
                _acceptedCapacity = acceptedCapacity;
            }

            internal List<AnimSmbSignal> Signals { get; } =
                new List<AnimSmbSignal>();
            internal int Attempts { get; private set; }

            public bool TryReceiveSmbSignal(in AnimSmbSignal signal)
            {
                Attempts++;
                if (Signals.Count >= _acceptedCapacity)
                {
                    return false;
                }

                Signals.Add(signal);
                return true;
            }
        }
    }
}
