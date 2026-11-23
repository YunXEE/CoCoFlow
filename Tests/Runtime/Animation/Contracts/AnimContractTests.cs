using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.StateGraph;
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
        public void RootMotionFeedback_PreservesTargetAndPayload()
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
                GraphInstanceId.TryCreate(
                    801UL,
                    out GraphInstanceId source));
            Assert.IsTrue(
                GraphInstanceId.TryCreate(
                    802UL,
                    out GraphInstanceId target));
            Assert.IsTrue(
                EventSequence.TryCreate(
                    1UL,
                    out EventSequence sequence));
            Assert.IsTrue(
                ActorEventEnvelope.TryCreate(
                    AnimContractIds.FeedbackEventTypeId,
                    AnimContractIds.FeedbackEventDomainId,
                    source,
                    target,
                    new TimelineEpoch(0UL),
                    new TimelineTick(1UL),
                    sequence,
                    EventDeliveryMode.Targeted,
                    EventReliability.Reliable,
                    default,
                    default,
                    default,
                    out ActorEventEnvelope envelope));
            Assert.IsTrue(
                EventPacket<AnimFeedbackEvent>.TryCreate(
                    envelope,
                    feedbackEvent,
                    out EventPacket<AnimFeedbackEvent> packet));
            Assert.That(packet.Envelope.TargetGraphInstanceId, Is.EqualTo(target));
            Assert.That(packet.Payload.Record.Kind, Is.EqualTo(AnimFeedbackKind.RootMotion));
            Assert.That(packet.Payload.Record.PositionX, Is.EqualTo(1f));
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
            OperatorDescriptor descriptor =
                AnimOperatorContracts.Definition.Descriptor;
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
                GraphInstanceId.TryCreate(
                    401UL,
                    out GraphInstanceId graphInstanceId));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graphInstanceId,
                    new TimelineEpoch(0UL),
                    new TimelineTick(1UL),
                    new ExecutionSequence(1UL),
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
                new TimelineEpoch(0UL));
            Assert.IsTrue(buffer.Overflowed);
            buffer.PrepareForTimeline(
                graphInstanceId,
                new TimelineEpoch(1UL));
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
                GraphInstanceId.TryCreate(
                    501UL,
                    out GraphInstanceId oldGraph));
            Assert.IsTrue(
                GraphInstanceId.TryCreate(
                    502UL,
                    out GraphInstanceId newGraph));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    oldGraph,
                    new TimelineEpoch(0UL),
                    new TimelineTick(1UL),
                    new ExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp oldSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    newGraph,
                    new TimelineEpoch(1UL),
                    new TimelineTick(1UL),
                    new ExecutionSequence(1UL),
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
                GraphInstanceId.TryCreate(
                    601UL,
                    out GraphInstanceId graph));
            var epoch = new TimelineEpoch(0UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    graph,
                    epoch,
                    new TimelineTick(1UL),
                    new ExecutionSequence(1UL),
                    new ContextRevision(1UL),
                    out AnimFeedbackSourceStamp staleSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    graph,
                    epoch,
                    new TimelineTick(8UL),
                    new ExecutionSequence(8UL),
                    new ContextRevision(8UL),
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
                GraphInstanceId.TryCreate(
                    701UL,
                    out GraphInstanceId graph));
            var epoch = new TimelineEpoch(0UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    epoch,
                    new TimelineTick(1UL),
                    new ExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp staleSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    epoch,
                    new TimelineTick(8UL),
                    new ExecutionSequence(8UL),
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
                GraphInstanceId.TryCreate(
                    751UL,
                    out GraphInstanceId graph));
            var epoch = new TimelineEpoch(0UL);
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCommitted(
                    graph,
                    epoch,
                    new TimelineTick(7UL),
                    new ExecutionSequence(7UL),
                    new ContextRevision(7UL),
                    out AnimFeedbackSourceStamp directSource));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    epoch,
                    new TimelineTick(8UL),
                    new ExecutionSequence(8UL),
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
                GraphInstanceId.TryCreate(
                    701UL,
                    out GraphInstanceId firstGraph));
            Assert.IsTrue(
                GraphInstanceId.TryCreate(
                    702UL,
                    out GraphInstanceId secondGraph));
            var epoch = new TimelineEpoch(0UL);
            var tick = new TimelineTick(8UL);
            var sequence = new ExecutionSequence(9UL);
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
                    new TimelineEpoch(1UL),
                    tick,
                    sequence));
            Assert.IsFalse(
                candidate.MatchesCandidate(
                    firstGraph,
                    epoch,
                    new TimelineTick(9UL),
                    sequence));

            var revision = new ContextRevision(4UL);
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
                    new ContextRevision(5UL)));
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

        private static StateFlowFrameIdentity CreateContextIdentity(
            GraphInstanceId graph,
            ulong tick)
        {
            return new StateFlowFrameIdentity(
                graph,
                new TimelineEpoch(0UL),
                new TimelineTick(tick),
                new ExecutionSequence(tick),
                StateFlowFrameKind.Context);
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
