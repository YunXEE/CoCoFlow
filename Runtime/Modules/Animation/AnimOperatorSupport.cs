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

    /// <summary>
    /// Private provenance for staged feedback. Feedback records deliberately stay
    /// transport-only; their source frame is an operator-local delivery guard.
    /// </summary>
    internal readonly struct AnimFeedbackSourceStamp
    {
        private enum SourceKind : byte
        {
            None = 0,
            CommittedContext = 1,
            CandidateTick = 2
        }

        private readonly SourceKind _kind;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoTimelineEpoch _timelineEpoch;
        private readonly CoCoTimelineTick _tick;
        private readonly CoCoExecutionSequence _executionSequence;
        private readonly CoCoContextRevision _revision;

        internal bool IsValid => _kind != SourceKind.None;

        private AnimFeedbackSourceStamp(
            SourceKind kind,
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence,
            CoCoContextRevision revision)
        {
            _kind = kind;
            _graphInstanceId = graphInstanceId;
            _timelineEpoch = timelineEpoch;
            _tick = tick;
            _executionSequence = executionSequence;
            _revision = revision;
        }

        internal static bool TryCaptureCommitted(
            CoCoStateGraphHost host,
            out AnimFeedbackSourceStamp stamp)
        {
            CoCoContextFrame frame = host != null ? host.CurrentContext : default;
            if (host == null ||
                !host.GraphInstanceId.IsValid ||
                !frame.IsAlive ||
                !frame.Header.IsValid ||
                frame.Header.Identity.Kind != CoCoStateFlowFrameKind.Context ||
                !frame.Revision.IsValid ||
                frame.Header.Identity.GraphInstanceId != host.GraphInstanceId)
            {
                stamp = default;
                return false;
            }

            CoCoStateFlowFrameIdentity identity = frame.Header.Identity;
            return TryCreateCommitted(
                identity.GraphInstanceId,
                identity.TimelineEpoch,
                identity.Tick,
                identity.ExecutionSequence,
                frame.Revision,
                out stamp);
        }

        internal static bool TryCreateCommitted(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence,
            CoCoContextRevision revision,
            out AnimFeedbackSourceStamp stamp)
        {
            if (!graphInstanceId.IsValid || !revision.IsValid)
            {
                stamp = default;
                return false;
            }

            stamp = new AnimFeedbackSourceStamp(
                SourceKind.CommittedContext,
                graphInstanceId,
                timelineEpoch,
                tick,
                executionSequence,
                revision);
            return true;
        }

        internal static bool TryCaptureCandidate(
            in CoCoOperatorExecutionContext context,
            CoCoStateGraphHost host,
            out AnimFeedbackSourceStamp stamp)
        {
            if (!context.IsValid ||
                host == null ||
                !host.GraphInstanceId.IsValid)
            {
                stamp = default;
                return false;
            }

            return TryCreateCandidate(
                host.GraphInstanceId,
                context.TickFrame.TimelineEpoch,
                context.TickFrame.Tick,
                context.TickFrame.ExecutionSequence,
                out stamp);
        }

        internal static bool TryCreateCandidate(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence,
            out AnimFeedbackSourceStamp stamp)
        {
            if (!graphInstanceId.IsValid)
            {
                stamp = default;
                return false;
            }

            stamp = new AnimFeedbackSourceStamp(
                SourceKind.CandidateTick,
                graphInstanceId,
                timelineEpoch,
                tick,
                executionSequence,
                default);
            return true;
        }

        internal bool IsSameTimeline(
            CoCoStateGraphHost host,
            CoCoTimelineEpoch timelineEpoch)
        {
            return host != null &&
                   IsSameTimeline(
                       host.GraphInstanceId,
                       timelineEpoch);
        }

        internal bool IsSameTimeline(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch)
        {
            return _kind != SourceKind.None &&
                   graphInstanceId.IsValid &&
                   graphInstanceId == _graphInstanceId &&
                   timelineEpoch == _timelineEpoch;
        }

        internal bool IsSameTimeline(in AnimFeedbackSourceStamp other)
        {
            return _kind != SourceKind.None &&
                   other._kind != SourceKind.None &&
                   _graphInstanceId == other._graphInstanceId &&
                   _timelineEpoch == other._timelineEpoch;
        }

        internal bool IsSameSource(in AnimFeedbackSourceStamp other)
        {
            return _kind == other._kind &&
                   _kind != SourceKind.None &&
                   _graphInstanceId == other._graphInstanceId &&
                   _timelineEpoch == other._timelineEpoch &&
                   _tick == other._tick &&
                   _executionSequence == other._executionSequence &&
                   _revision == other._revision;
        }

        internal bool IsCommittedContext =>
            _kind == SourceKind.CommittedContext;

        internal bool MatchesCandidate(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence)
        {
            return _kind == SourceKind.CandidateTick &&
                   graphInstanceId == _graphInstanceId &&
                   timelineEpoch == _timelineEpoch &&
                   tick == _tick &&
                   executionSequence == _executionSequence;
        }

        internal bool MatchesCommitted(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence,
            CoCoContextRevision revision)
        {
            return _kind == SourceKind.CommittedContext &&
                   graphInstanceId == _graphInstanceId &&
                   timelineEpoch == _timelineEpoch &&
                   tick == _tick &&
                   executionSequence == _executionSequence &&
                   revision == _revision;
        }

        internal bool Matches(
            in CoCoOperatorExecutionContext context,
            CoCoStateGraphHost host)
        {
            if (!context.IsValid ||
                host == null ||
                !host.GraphInstanceId.IsValid ||
                host.GraphInstanceId != _graphInstanceId)
            {
                return false;
            }

            switch (_kind)
            {
                case SourceKind.CommittedContext:
                    CoCoContextFrameReadView previous = context.PreviousContext;
                    CoCoStateFlowFrameIdentity identity = previous.Header.Identity;
                    return previous.HasCommittedFrame &&
                           identity.Kind == CoCoStateFlowFrameKind.Context &&
                           MatchesCommitted(
                               identity.GraphInstanceId,
                               identity.TimelineEpoch,
                               identity.Tick,
                               identity.ExecutionSequence,
                               previous.Revision);
                case SourceKind.CandidateTick:
                    return MatchesCandidate(
                        host.GraphInstanceId,
                        context.TickFrame.TimelineEpoch,
                        context.TickFrame.Tick,
                        context.TickFrame.ExecutionSequence);
                default:
                    return false;
            }
        }
    }

    internal sealed class AnimFeedbackBuffer
    {
        private readonly AnimFeedbackEvent[] _events =
            new AnimFeedbackEvent[AnimContractLimits.FeedbackCapacity];
        private readonly AnimFeedbackSourceStamp[] _sources =
            new AnimFeedbackSourceStamp[AnimContractLimits.FeedbackCapacity];
        private int _count;
        private AnimFeedbackSourceStamp _overflowSource;

        internal int Count => _count;
        internal bool Overflowed { get; private set; }

        internal void PrepareForExecution(
            in CoCoOperatorExecutionContext context,
            CoCoStateGraphHost host)
        {
            if (!context.IsValid ||
                host == null ||
                !host.GraphInstanceId.IsValid)
            {
                return;
            }

            if (Overflowed)
            {
                PrepareForTimeline(
                    host.GraphInstanceId,
                    context.TickFrame.TimelineEpoch);
                if (Overflowed)
                {
                    return;
                }
            }

            int originalCount = _count;
            int retainedCount = 0;
            for (int index = 0; index < originalCount; index++)
            {
                if (!_sources[index].Matches(context, host))
                {
                    continue;
                }

                if (retainedCount != index)
                {
                    _events[retainedCount] = _events[index];
                    _sources[retainedCount] = _sources[index];
                }

                retainedCount++;
            }

            if (retainedCount < originalCount)
            {
                Array.Clear(
                    _events,
                    retainedCount,
                    originalCount - retainedCount);
                Array.Clear(
                    _sources,
                    retainedCount,
                    originalCount - retainedCount);
                _count = retainedCount;
            }
        }

        internal void PrepareForTimeline(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch)
        {
            if (Overflowed &&
                !_overflowSource.IsSameTimeline(
                    graphInstanceId,
                    timelineEpoch))
            {
                Clear();
            }
        }

        internal bool TryAppend(
            in AnimFeedbackRecord record,
            in AnimFeedbackSourceStamp source)
        {
            if (!source.IsValid ||
                !AnimFeedbackEvent.TryCreate(
                    record,
                    out AnimFeedbackEvent feedbackEvent))
            {
                return false;
            }

            if (Overflowed &&
                !_overflowSource.IsSameTimeline(source))
            {
                // A new Graph/Epoch owns a new transaction even when its first
                // callback arrives before the next Operator execution.
                Clear();
            }
            else if (!Overflowed && _count > 0)
            {
                AnimFeedbackSourceStamp batchSource = _sources[0];
                bool sameSource = batchSource.IsSameSource(source);
                if (!batchSource.IsSameTimeline(source) ||
                    (source.IsCommittedContext && !sameSource) ||
                    (!source.IsCommittedContext &&
                     !batchSource.IsCommittedContext &&
                     !sameSource))
                {
                    // A newer Direct frame or candidate execution supersedes
                    // stale same-timeline records before they consume the next
                    // transaction's reliable capacity. The one valid mixed
                    // batch is previous committed Direct + current candidate.
                    Clear();
                }
            }

            if (_count >= _events.Length)
            {
                Overflowed = true;
                _overflowSource = source;
                return false;
            }

            _events[_count] = feedbackEvent;
            _sources[_count] = source;
            _count++;
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
                // A Direct SMB callback belongs only to the immediately following
                // execution. Candidate feedback belongs only to its own Tick.
                // Stale callbacks are intentionally discarded, never re-enveloped.
                if (!_sources[index].Matches(context, host))
                {
                    continue;
                }

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
            Array.Clear(_sources, 0, _count);
            _count = 0;
            Overflowed = false;
            _overflowSource = default;
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
