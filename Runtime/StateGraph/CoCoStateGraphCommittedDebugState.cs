using System;

namespace CoCoFlow.Runtime.Core
{
    internal readonly struct CoCoStateGraphCommittedDebugActiveState
    {
        internal CoCoStateGraphCommittedDebugActiveState(
            CoCoStateId stateId,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress)
        {
            StateId = stateId;
            ActivationId = activationId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
        }

        internal CoCoStateId StateId { get; }
        internal CoCoActivationId ActivationId { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
    }

    internal sealed class CoCoStateGraphCommittedDebugLayer
    {
        private readonly CoCoStateGraphCommittedDebugActiveState[] _activeStates;

        internal CoCoStateGraphCommittedDebugLayer(
            CoCoLayerId layerId,
            CoCoTransitionId winningTransitionId,
            CoCoStateGraphCommittedDebugActiveState[] activeStates)
        {
            LayerId = layerId;
            WinningTransitionId = winningTransitionId;
            _activeStates = activeStates ??
                            Array.Empty<CoCoStateGraphCommittedDebugActiveState>();
        }

        internal CoCoLayerId LayerId { get; }
        internal CoCoTransitionId WinningTransitionId { get; }
        internal int ActiveStateCount => _activeStates.Length;

        internal CoCoStateGraphCommittedDebugActiveState GetActiveState(int index) =>
            _activeStates[index];
    }

    internal sealed class CoCoStateGraphCommittedDebugState
    {
        private readonly CoCoStateGraphCommittedDebugLayer[] _layers;

        internal CoCoStateGraphCommittedDebugState(
            uint schemaVersion,
            ulong contentFingerprint,
            CoCoGraphId graphId,
            ulong catalogFingerprint,
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineId timelineId,
            CoCoClockDomainId clockDomainId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence,
            double seconds,
            CoCoStateGraphCommittedDebugLayer[] layers)
        {
            SchemaVersion = schemaVersion;
            ContentFingerprint = contentFingerprint;
            GraphId = graphId;
            CatalogFingerprint = catalogFingerprint;
            GraphInstanceId = graphInstanceId;
            TimelineId = timelineId;
            ClockDomainId = clockDomainId;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            ExecutionSequence = executionSequence;
            Seconds = seconds;
            _layers = layers ?? Array.Empty<CoCoStateGraphCommittedDebugLayer>();
        }

        internal uint SchemaVersion { get; }
        internal ulong ContentFingerprint { get; }
        internal CoCoGraphId GraphId { get; }
        internal ulong CatalogFingerprint { get; }
        internal CoCoGraphInstanceId GraphInstanceId { get; }
        internal CoCoTimelineId TimelineId { get; }
        internal CoCoClockDomainId ClockDomainId { get; }
        internal CoCoTimelineEpoch TimelineEpoch { get; }
        internal CoCoTimelineTick Tick { get; }
        internal CoCoExecutionSequence ExecutionSequence { get; }
        internal double Seconds { get; }
        internal int LayerCount => _layers.Length;

        internal CoCoStateGraphCommittedDebugLayer GetLayer(int index) => _layers[index];
    }
}
