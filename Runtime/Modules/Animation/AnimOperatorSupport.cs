using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraphHost.StateGraphHost;

    internal static class AnimOperatorContracts
    {
        internal static readonly OperationSectionRequirement ParameterRequirement;
        internal static readonly OperationSectionRequirement TriggerRequirement;
        internal static readonly EventOutboxRequirement FeedbackRequirement;
        internal static readonly StandardOperatorDefinition Definition;

        static AnimOperatorContracts()
        {
            var builder = new StandardOperatorBuilder();
            ParameterRequirement = builder.Require<IAnimParameterOperationSection>(AnimContractIds.ParameterSectionId,
                AnimContractIds.ParameterSectionSemanticFingerprint);
            TriggerRequirement = builder.Require<IAnimTriggerOperationSection>(AnimContractIds.TriggerSectionId,
                AnimContractIds.TriggerSectionSemanticFingerprint);
            builder.OwnOutcome(AnimContractIds.SnapshotBlockId, AnimContractIds.SnapshotSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored, default(AnimSnapshotState), 1);
            FeedbackRequirement = builder.Emit<AnimFeedbackEvent>(AnimContractIds.FeedbackEventTypeId,
                AnimContractIds.FeedbackEventDomainId, AnimContractLimits.FeedbackCapacity);
            Definition = builder.Freeze<AnimAutoOperator>(AnimContractIds.AutoOperatorId);
        }

        internal static CoCoDiagnostic Error(string message) => CoCoDiagnostic.Error(
            CoCoDiagnosticDomain.Operator, CoCoDiagnosticCode.OperatorExecutionFailed, message);
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
        private readonly GraphInstanceId _graphInstanceId;
        private readonly TimelineEpoch _timelineEpoch;
        private readonly TimelineTick _tick;
        private readonly ExecutionSequence _executionSequence;
        private readonly ContextRevision _revision;

        internal bool IsValid => _kind != SourceKind.None;

        private AnimFeedbackSourceStamp(
            SourceKind kind,
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
            ContextRevision revision)
        {
            _kind = kind;
            _graphInstanceId = graphInstanceId;
            _timelineEpoch = timelineEpoch;
            _tick = tick;
            _executionSequence = executionSequence;
            _revision = revision;
        }

        internal static bool TryCaptureCommitted(
            StateGraphHost host,
            out AnimFeedbackSourceStamp stamp)
        {
            ContextFrame frame = host != null ? host.CurrentContext : default;
            if (host == null ||
                !host.GraphInstanceId.IsValid ||
                !frame.IsAlive ||
                !frame.Header.IsValid ||
                frame.Header.Identity.Kind != StateFlowFrameKind.Context ||
                !frame.Revision.IsValid ||
                frame.Header.Identity.GraphInstanceId != host.GraphInstanceId)
            {
                stamp = default;
                return false;
            }

            StateFlowFrameIdentity identity = frame.Header.Identity;
            return TryCreateCommitted(
                identity.GraphInstanceId,
                identity.TimelineEpoch,
                identity.Tick,
                identity.ExecutionSequence,
                frame.Revision,
                out stamp);
        }

        internal static bool TryCreateCommitted(
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
            ContextRevision revision,
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
            in OperatorExecutionContext context,
            StateGraphHost host,
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
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
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
            StateGraphHost host,
            TimelineEpoch timelineEpoch)
        {
            return host != null &&
                   IsSameTimeline(
                       host.GraphInstanceId,
                       timelineEpoch);
        }

        internal bool IsSameTimeline(
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch)
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
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence)
        {
            return _kind == SourceKind.CandidateTick &&
                   graphInstanceId == _graphInstanceId &&
                   timelineEpoch == _timelineEpoch &&
                   tick == _tick &&
                   executionSequence == _executionSequence;
        }

        internal bool MatchesCommitted(
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
            ContextRevision revision)
        {
            return _kind == SourceKind.CommittedContext &&
                   graphInstanceId == _graphInstanceId &&
                   timelineEpoch == _timelineEpoch &&
                   tick == _tick &&
                   executionSequence == _executionSequence &&
                   revision == _revision;
        }

        internal bool Matches(
            in OperatorExecutionContext context,
            StateGraphHost host)
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
                    ContextFrameReadView previous = context.PreviousContext;
                    StateFlowFrameIdentity identity = previous.Header.Identity;
                    return previous.HasCommittedFrame &&
                           identity.Kind == StateFlowFrameKind.Context &&
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
            in OperatorExecutionContext context,
            StateGraphHost host)
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
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch)
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
            in OperatorExecutionContext context,
            StateGraphHost host)
        {
            if (Overflowed || host == null || !host.GraphInstanceId.IsValid ||
                !EventOutboxTarget.TryTargeted(
                    host.GraphInstanceId,
                    EventReliability.Reliable,
                    default,
                    default,
                    default,
                    out EventOutboxTarget target))
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
                        _events[index]) != EventOutboxWriteResult.Accepted)
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
    }
}
