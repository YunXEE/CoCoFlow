using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    internal static class AnimOperatorContracts
    {
        internal static readonly CoCoOperationSectionRequirement ParameterRequirement;
        internal static readonly CoCoOperationSectionRequirement TriggerRequirement;
        internal static readonly CoCoOperationSectionRequirement PlaybackRequirement;
        internal static readonly CoCoOperationSectionRequirement ModulationRequirement;
        internal static readonly CoCoEventOutboxRequirement FeedbackRequirement;
        internal static readonly CoCoOperatorDescriptor AutoDescriptor;
        internal static readonly CoCoOperatorDescriptor AdvancedDescriptor;

        static AnimOperatorContracts()
        {
            Require(
                AnimOperationSchema.TryCreateParameterRequirement(
                    out ParameterRequirement,
                    out CoCoDiagnostic parameterDiagnostic),
                parameterDiagnostic);
            Require(
                AnimOperationSchema.TryCreateTriggerRequirement(
                    out TriggerRequirement,
                    out CoCoDiagnostic triggerDiagnostic),
                triggerDiagnostic);
            Require(
                AnimOperationSchema.TryCreatePlaybackRequirement(
                    out PlaybackRequirement,
                    out CoCoDiagnostic playbackDiagnostic),
                playbackDiagnostic);
            Require(
                AnimOperationSchema.TryCreateModulationRequirement(
                    out ModulationRequirement,
                    out CoCoDiagnostic modulationDiagnostic),
                modulationDiagnostic);
            Require(
                CoCoEventOutboxRequirement.TryCreate<AnimFeedbackEvent>(
                    AnimContractIds.FeedbackEventTypeId,
                    AnimContractIds.FeedbackEventDomainId,
                    AnimContractLimits.FeedbackCapacity,
                    out FeedbackRequirement,
                    out CoCoDiagnostic feedbackDiagnostic),
                feedbackDiagnostic);

            AutoDescriptor = BuildAutoDescriptor();
            AdvancedDescriptor = BuildAdvancedDescriptor();
        }

        internal static CoCoDiagnostic Error(string message)
        {
            return CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.OperatorExecutionFailed,
                message);
        }

        internal static CoCoDiagnostic RestoreUnavailable()
        {
            return CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.InvalidGraphRestore,
                "Pre11 exact AnimatorControllerPlayable replay is deferred: " +
                "the bounded-anchor Replay Gate did not pass.");
        }

        private static CoCoOperatorDescriptor BuildAutoDescriptor()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            Require(
                builder.TryRequire<IAnimParameterOperationSection>(
                    AnimContractIds.ParameterSectionId,
                    CoCoOperationSectionMode.Continuous,
                    out _,
                    out CoCoDiagnostic parameters),
                parameters);
            Require(
                builder.TryRequire<IAnimTriggerOperationSection>(
                    AnimContractIds.TriggerSectionId,
                    CoCoOperationSectionMode.Discrete,
                    out _,
                    out CoCoDiagnostic triggers),
                triggers);
            Require(
                builder.TryEmit<AnimFeedbackEvent>(
                    AnimContractIds.FeedbackEventTypeId,
                    AnimContractIds.FeedbackEventDomainId,
                    AnimContractLimits.FeedbackCapacity,
                    out _,
                    out CoCoDiagnostic feedback),
                feedback);
            Require(
                builder.TryFreeze<AnimAutoOperator>(
                    AnimContractIds.AutoOperatorId,
                    out CoCoOperatorDescriptor descriptor,
                    out CoCoDiagnostic freeze),
                freeze);
            return descriptor;
        }

        private static CoCoOperatorDescriptor BuildAdvancedDescriptor()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            Require(
                builder.TryRequire<IAnimParameterOperationSection>(
                    AnimContractIds.ParameterSectionId,
                    CoCoOperationSectionMode.Continuous,
                    out _,
                    out CoCoDiagnostic parameters),
                parameters);
            Require(
                builder.TryRequire<IAnimTriggerOperationSection>(
                    AnimContractIds.TriggerSectionId,
                    CoCoOperationSectionMode.Discrete,
                    out _,
                    out CoCoDiagnostic triggers),
                triggers);
            Require(
                builder.TryRequire<IAnimPlaybackOperationSection>(
                    AnimContractIds.PlaybackSectionId,
                    CoCoOperationSectionMode.Discrete,
                    out _,
                    out CoCoDiagnostic playback),
                playback);
            Require(
                builder.TryRequire<IAnimModulationOperationSection>(
                    AnimContractIds.ModulationSectionId,
                    CoCoOperationSectionMode.Continuous,
                    out _,
                    out CoCoDiagnostic modulation),
                modulation);
            Require(
                builder.TryOwnOutcome<AnimPlaybackContext>(
                    AnimContractIds.PlaybackContextSlotId,
                    out CoCoDiagnostic outcome),
                outcome);
            Require(
                builder.TryEmit<AnimFeedbackEvent>(
                    AnimContractIds.FeedbackEventTypeId,
                    AnimContractIds.FeedbackEventDomainId,
                    AnimContractLimits.FeedbackCapacity,
                    out _,
                    out CoCoDiagnostic feedback),
                feedback);
            Require(
                builder.TryFreeze<AnimOperator>(
                    AnimContractIds.OperatorId,
                    out CoCoOperatorDescriptor descriptor,
                    out CoCoDiagnostic freeze),
                freeze);
            return descriptor;
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded || diagnostic.IsError)
            {
                throw new InvalidOperationException(
                    diagnostic.IsError
                        ? diagnostic.Message
                        : "Animation contract initialization failed.");
            }
        }
    }

    internal sealed class AnimFeedbackBuffer
    {
        private readonly AnimFeedbackEvent[] _events =
            new AnimFeedbackEvent[AnimContractLimits.FeedbackCapacity];
        private int _count;

        internal int Count => _count;
        internal bool Overflowed { get; private set; }

        internal bool TryAppend(in AnimFeedbackRecord record)
        {
            if (!AnimFeedbackEvent.TryCreate(record, out AnimFeedbackEvent feedbackEvent))
            {
                return false;
            }

            if (_count >= _events.Length)
            {
                Overflowed = true;
                return false;
            }

            _events[_count++] = feedbackEvent;
            return true;
        }

        internal bool TryWrite(
            in CoCoOperatorExecutionContext context,
            CoCoStateGraphHost host)
        {
            if (Overflowed || host == null || !host.GraphInstanceId.IsValid ||
                !CoCoEventOutboxTarget.TryTargeted(
                    host.GraphInstanceId,
                    CoCoEventReliability.Reliable,
                    default,
                    default,
                    default,
                    out CoCoEventOutboxTarget target))
            {
                return false;
            }

            for (int index = 0; index < _count; index++)
            {
                if (context.EventOutbox.TryWrite(
                        AnimOperatorContracts.FeedbackRequirement,
                        target,
                        _events[index]) != CoCoEventOutboxWriteResult.Accepted)
                {
                    return false;
                }
            }

            Clear();
            return true;
        }

        internal void Clear()
        {
            Array.Clear(_events, 0, _count);
            _count = 0;
            Overflowed = false;
        }
    }

    /// <summary>
    /// Plain runtime helper owned by AnimOperator. It captures Playable evaluation
    /// deltas and never writes a Transform, CharacterController or Rigidbody.
    /// </summary>
    internal sealed class AnimRootMotionRelay
    {
        private bool _enabled;
        private bool _relayPosition;
        private bool _relayRotation;
        private bool _captured;
        private Vector3 _positionDelta;
        private Quaternion _rotationDelta = Quaternion.identity;

        internal bool Enabled => _enabled;

        internal void Configure(
            bool enabled,
            bool relayPosition,
            bool relayRotation)
        {
            _enabled = enabled && (relayPosition || relayRotation);
            _relayPosition = relayPosition;
            _relayRotation = relayRotation;
            ResetEvaluation();
        }

        internal void ResetEvaluation()
        {
            _captured = false;
            _positionDelta = Vector3.zero;
            _rotationDelta = Quaternion.identity;
        }

        internal void Capture(Vector3 positionDelta, Quaternion rotationDelta)
        {
            if (!_enabled)
            {
                return;
            }

            if (_relayPosition)
            {
                _positionDelta += positionDelta;
            }

            if (_relayRotation)
            {
                _rotationDelta = rotationDelta * _rotationDelta;
            }

            _captured = true;
        }

        internal bool TryComplete(
            Animator animator,
            out AnimFeedbackRecord record)
        {
            if (!_enabled)
            {
                record = default;
                return false;
            }

            if (!_captured && animator != null)
            {
                Capture(animator.deltaPosition, animator.deltaRotation);
            }

            Vector3 position = _relayPosition ? _positionDelta : Vector3.zero;
            Quaternion rotation = _relayRotation ? _rotationDelta : Quaternion.identity;
            bool created = AnimFeedbackRecord.TryCreateRootMotion(
                position.x,
                position.y,
                position.z,
                rotation.x,
                rotation.y,
                rotation.z,
                rotation.w,
                out record);
            ResetEvaluation();
            return created;
        }
    }

    internal static class AnimOperationLaneReader
    {
        internal static AnimParameterCommand Read(
            IAnimParameterOperationSection section,
            int lane)
        {
            switch (lane)
            {
                case 0: return section.Slot00;
                case 1: return section.Slot01;
                case 2: return section.Slot02;
                case 3: return section.Slot03;
                case 4: return section.Slot04;
                case 5: return section.Slot05;
                case 6: return section.Slot06;
                case 7: return section.Slot07;
                case 8: return section.Slot08;
                case 9: return section.Slot09;
                case 10: return section.Slot10;
                case 11: return section.Slot11;
                case 12: return section.Slot12;
                case 13: return section.Slot13;
                case 14: return section.Slot14;
                case 15: return section.Slot15;
                default: return default;
            }
        }

        internal static AnimTriggerCommand Read(
            IAnimTriggerOperationSection section,
            int lane)
        {
            switch (lane)
            {
                case 0: return section.Slot00;
                case 1: return section.Slot01;
                case 2: return section.Slot02;
                case 3: return section.Slot03;
                case 4: return section.Slot04;
                case 5: return section.Slot05;
                case 6: return section.Slot06;
                case 7: return section.Slot07;
                default: return default;
            }
        }

        internal static AnimPlaybackCommand Read(
            IAnimPlaybackOperationSection section,
            int lane)
        {
            switch (lane)
            {
                case 0: return section.Layer00;
                case 1: return section.Layer01;
                case 2: return section.Layer02;
                case 3: return section.Layer03;
                default: return default;
            }
        }

        internal static AnimModulationCommand Read(
            IAnimModulationOperationSection section,
            int lane)
        {
            switch (lane)
            {
                case 0: return section.Slot00;
                case 1: return section.Slot01;
                case 2: return section.Slot02;
                case 3: return section.Slot03;
                case 4: return section.Slot04;
                case 5: return section.Slot05;
                case 6: return section.Slot06;
                case 7: return section.Slot07;
                default: return default;
            }
        }
    }
}
