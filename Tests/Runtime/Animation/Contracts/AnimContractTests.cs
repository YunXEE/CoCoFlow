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
            Assert.AreEqual(4, AnimContractLimits.PlaybackLayerCount);
            Assert.AreEqual(8, AnimContractLimits.ModulationLaneCount);
            Assert.AreEqual(16, AnimContractLimits.FeedbackCapacity);

            Assert.AreEqual(
                new[]
                {
                    "Control",
                    "Layer00",
                    "Layer01",
                    "Layer02",
                    "Layer03"
                },
                typeof(IAnimPlaybackOperationSection)
                    .GetProperties()
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
        }

        [Test]
        public void StepCommand_RejectsZeroAndNegativeDelta()
        {
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    11UL,
                    out CoCoActivationId activationId));

            Assert.IsFalse(
                AnimPlaybackCommand.TryCreateStep(
                    activationId,
                    0f,
                    out _));
            Assert.IsFalse(
                AnimPlaybackCommand.TryCreateStep(
                    activationId,
                    -0.01f,
                    out _));
            Assert.IsFalse(
                AnimPlaybackCommand.TryCreateStep(
                    activationId,
                    float.NaN,
                    out _));
            Assert.IsFalse(
                AnimPlaybackCommand.TryCreateStep(
                    activationId,
                    float.PositiveInfinity,
                    out _));
            Assert.IsTrue(
                AnimPlaybackCommand.TryCreateStep(
                    activationId,
                    0.01f,
                    out AnimPlaybackCommand command));
            Assert.AreEqual(AnimPlaybackCommandKind.Step, command.Kind);
            Assert.Greater(command.StepDeltaSeconds, 0f);
            Assert.IsFalse(
                AnimOperator.IsPlaybackControlAllowed(
                    AnimPlaybackCommandKind.Step,
                    true,
                    false));
            Assert.IsTrue(
                AnimOperator.IsPlaybackControlAllowed(
                    AnimPlaybackCommandKind.Step,
                    true,
                    true));
            Assert.IsFalse(
                AnimOperator.IsPlaybackControlAllowed(
                    AnimPlaybackCommandKind.Stop,
                    false,
                    true));
        }

        [Test]
        public void OperatorVariants_ShareOneHostExclusiveAnimationIdentity()
        {
            Assert.AreEqual(
                AnimContractIds.OperatorId,
                AnimContractIds.AutoOperatorId);
            Assert.AreEqual(
                AnimOperatorContracts.AdvancedDescriptor.OperatorId,
                AnimOperatorContracts.AutoDescriptor.OperatorId);
        }

        [Test]
        public void PlaybackToken_IdentityIncludesGraphEpochSequenceActivationAndLayer()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    7UL,
                    out CoCoGraphInstanceId firstGraph));
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    8UL,
                    out CoCoGraphInstanceId secondGraph));
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    12UL,
                    out CoCoActivationId activationId));
            Assert.IsTrue(
                CoCoOperationSequence.TryCreate(
                    34UL,
                    out CoCoOperationSequence operationSequence));

            Assert.IsTrue(
                AnimPlaybackToken.TryCreate(
                    firstGraph,
                    activationId,
                    new CoCoTimelineEpoch(5UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer02,
                    out AnimPlaybackToken first));
            Assert.IsTrue(
                AnimPlaybackToken.TryCreate(
                    secondGraph,
                    activationId,
                    new CoCoTimelineEpoch(5UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer02,
                    out AnimPlaybackToken second));

            Assert.IsTrue(first.IsValid);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(firstGraph, first.GraphInstanceId);
            Assert.AreEqual(new CoCoTimelineEpoch(5UL), first.TimelineEpoch);
            Assert.AreEqual(operationSequence, first.OperationSequence);
            Assert.AreEqual(activationId, first.SourceActivationId);
            Assert.AreEqual(AnimPlaybackLayerSlot.Layer02, first.Layer);
        }

        [Test]
        public void PlaybackPublicationFence_RequiresCommittedReplacementAfterRebuild()
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    21UL,
                    out CoCoGraphInstanceId graph));
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    22UL,
                    out CoCoActivationId activation));
            Assert.IsTrue(
                CoCoOperationSequence.TryCreate(
                    23UL,
                    out CoCoOperationSequence operationSequence));
            Assert.IsTrue(
                AnimBindingId.TryCreate(
                    24UL,
                    out AnimBindingId binding));
            Assert.IsTrue(
                AnimPlaybackToken.TryCreate(
                    graph,
                    activation,
                    new CoCoTimelineEpoch(0UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer00,
                    out AnimPlaybackToken token));

            AnimPlaybackContext invalidated = CreatePlaybackContext(
                new AnimPlaybackLayer(
                    AnimPlaybackLayerSlot.Layer00,
                    token,
                    binding,
                    AnimPlaybackStatus.Playing,
                    0.25f),
                false);
            CoCoStateFlowFrameIdentity oldIdentity =
                CreateContextIdentity(graph, 1UL);
            CoCoStateFlowFrameIdentity unrelatedIdentity =
                CreateContextIdentity(graph, 2UL);
            CoCoStateFlowFrameIdentity stagedIdentity =
                CreateContextIdentity(graph, 3UL);
            var fence = new AnimOperator.PlaybackPublicationFence();

            fence.Invalidate(invalidated);
            Assert.IsFalse(
                fence.CanPublish(invalidated, oldIdentity));
            Assert.IsFalse(
                fence.CanPublish(invalidated, unrelatedIdentity),
                "An unrelated commit can copy the invalidated Animation slot.");

            fence.MarkOutcomeStaged(stagedIdentity);
            Assert.IsFalse(
                fence.CanPublish(invalidated, unrelatedIdentity),
                "Staging alone must not restore publication authority.");
            Assert.IsTrue(
                fence.CanPublish(invalidated, stagedIdentity),
                "The exact committed replacement may publish even when its reset value is identical.");
            Assert.IsFalse(fence.IsActive);

            fence.Invalidate(invalidated);
            fence.MarkOutcomeStaged(CreateContextIdentity(graph, 4UL));
            Assert.IsFalse(
                fence.CanPublish(
                    invalidated,
                    CreateContextIdentity(graph, 5UL)),
                "A cancelled candidate must not clear the rebuild fence.");
            AnimPlaybackContext replacement = CreatePlaybackContext(
                new AnimPlaybackLayer(
                    AnimPlaybackLayerSlot.Layer00,
                    token,
                    binding,
                    AnimPlaybackStatus.Playing,
                    0.5f),
                false);
            Assert.IsTrue(
                fence.CanPublish(
                    replacement,
                    CreateContextIdentity(graph, 6UL)),
                "A different committed Animation snapshot replaces the invalidated publication.");

            AnimPlaybackContext held = CreatePlaybackContext(
                new AnimPlaybackLayer(
                    AnimPlaybackLayerSlot.Layer00,
                    default,
                    default,
                    AnimPlaybackStatus.None,
                    0f),
                true);
            fence.Invalidate(held);
            Assert.IsFalse(
                fence.CanPublish(
                    held,
                    CreateContextIdentity(graph, 7UL)),
                "A rebuilt runtime must not expose a copied Held snapshot without a token.");
            Assert.IsTrue(
                fence.CanPublish(
                    CreatePlaybackContext(held.Layer00, false),
                    CreateContextIdentity(graph, 8UL)));
        }

        [Test]
        public void RotationNormalization_AcceptsHugeFiniteAndRejectsInvalidMagnitude()
        {
            var huge = new Vector4(
                float.MaxValue,
                float.MaxValue,
                float.MaxValue,
                float.MaxValue);
            Assert.IsTrue(
                AnimModulationMath.TryNormalizeRotation(
                    huge,
                    out Vector4 normalized));
            Assert.That(normalized.magnitude, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(normalized.x, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(normalized.y, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(normalized.z, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(normalized.w, Is.EqualTo(0.5f).Within(0.00001f));

            Assert.IsFalse(
                AnimModulationMath.TryNormalizeRotation(
                    new Vector4(0.0000001f, 0f, 0f, 0f),
                    out _));
            Assert.IsFalse(
                AnimModulationMath.TryNormalizeRotation(
                    new Vector4(float.NaN, 0f, 0f, 1f),
                    out _));
            Assert.IsFalse(
                AnimModulationMath.TryNormalizeRotation(
                    new Vector4(float.PositiveInfinity, 0f, 0f, 1f),
                    out _));
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
        public void ProductionAssembly_HasExactlyTwoMonoOperators()
        {
            Type[] monoTypes = typeof(AnimOperator).Assembly
                .GetTypes()
                .Where(type =>
                    !type.IsAbstract &&
                    typeof(MonoBehaviour).IsAssignableFrom(type))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    typeof(AnimAutoOperator),
                    typeof(AnimOperator)
                },
                monoTypes);
            Assert.IsTrue(
                typeof(StateMachineBehaviour).IsAssignableFrom(
                    typeof(AnimEventSmb)));
            Assert.IsFalse(
                typeof(MonoBehaviour).IsAssignableFrom(
                    typeof(AnimRootMotionRelay)));
        }

        [Test]
        public void ExactReplaySurface_IsDeferredOnly()
        {
            CollectionAssert.AreEqual(
                new[] { nameof(AnimExactReplayStatus.Deferred) },
                Enum.GetNames(typeof(AnimExactReplayStatus)));
            Assert.AreEqual(
                typeof(AnimExactReplayStatus),
                typeof(AnimOperator)
                    .GetProperty(nameof(AnimOperator.ExactTemporalReplay))
                    ?.PropertyType);
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
            Assert.IsFalse(
                descriptor.Requires.Contains(
                    AnimOperatorContracts.PlaybackRequirement));
            Assert.IsFalse(
                descriptor.Requires.Contains(
                    AnimOperatorContracts.ModulationRequirement));

            Type[] forbiddenFieldTypes =
            {
                typeof(RuntimeAnimatorController),
                typeof(UnityEngine.Playables.PlayableGraph),
                typeof(AnimRootMotionRelay)
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
        public void TickDeltaConversion_RejectsFloatUnderflowAndOverflow()
        {
            Assert.IsFalse(
                AnimOperator.TryConvertTickDeltaSeconds(
                    double.Epsilon,
                    out _));
            Assert.IsFalse(
                AnimOperator.TryConvertTickDeltaSeconds(
                    double.MaxValue,
                    out _));
            Assert.IsTrue(
                AnimOperator.TryConvertTickDeltaSeconds(
                    1d / 60d,
                    out float deltaSeconds));
            Assert.That(deltaSeconds, Is.EqualTo(1f / 60f));
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

        private static AnimPlaybackContext CreatePlaybackContext(
            AnimPlaybackLayer layer00,
            bool isHeld)
        {
            return new AnimPlaybackContext(
                layer00,
                new AnimPlaybackLayer(
                    AnimPlaybackLayerSlot.Layer01,
                    default,
                    default,
                    AnimPlaybackStatus.None,
                    0f),
                new AnimPlaybackLayer(
                    AnimPlaybackLayerSlot.Layer02,
                    default,
                    default,
                    AnimPlaybackStatus.None,
                    0f),
                new AnimPlaybackLayer(
                    AnimPlaybackLayerSlot.Layer03,
                    default,
                    default,
                    AnimPlaybackStatus.None,
                    0f),
                isHeld);
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
