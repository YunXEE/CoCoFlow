using System;
using System.Linq;
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
        public void PlaybackToken_IdentityIncludesEpochSequenceActivationAndLayer()
        {
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
                    activationId,
                    new CoCoTimelineEpoch(5UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer02,
                    out AnimPlaybackToken first));
            Assert.IsTrue(
                AnimPlaybackToken.TryCreate(
                    activationId,
                    new CoCoTimelineEpoch(6UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer02,
                    out AnimPlaybackToken second));

            Assert.IsTrue(first.IsValid);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(new CoCoTimelineEpoch(5UL), first.TimelineEpoch);
            Assert.AreEqual(operationSequence, first.OperationSequence);
            Assert.AreEqual(activationId, first.SourceActivationId);
            Assert.AreEqual(AnimPlaybackLayerSlot.Layer02, first.Layer);
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
    }
}
