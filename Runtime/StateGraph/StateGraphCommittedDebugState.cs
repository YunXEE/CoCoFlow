using System;

namespace CoCoFlow.Runtime.StateGraph
{
    internal readonly struct StateGraphCommittedDebugActiveState
    {
        internal StateGraphCommittedDebugActiveState(
            StateId stateId,
            ActivationId activationId,
            double localSeconds,
            double actionProgress)
        {
            StateId = stateId;
            ActivationId = activationId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
        }

        internal StateId StateId { get; }
        internal ActivationId ActivationId { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
    }

    internal sealed class StateGraphCommittedDebugLayer
    {
        private readonly StateGraphCommittedDebugActiveState[] _activeStates;

        internal StateGraphCommittedDebugLayer(
            LayerId layerId,
            TransitionId winningTransitionId,
            StateGraphCommittedDebugActiveState[] activeStates)
        {
            LayerId = layerId;
            WinningTransitionId = winningTransitionId;
            _activeStates = activeStates ??
                            Array.Empty<StateGraphCommittedDebugActiveState>();
        }

        internal LayerId LayerId { get; }
        internal TransitionId WinningTransitionId { get; }
        internal int ActiveStateCount => _activeStates.Length;

        internal StateGraphCommittedDebugActiveState GetActiveState(int index) =>
            _activeStates[index];
    }

    internal sealed class StateGraphCommittedDebugState
    {
        private readonly StateGraphCommittedDebugLayer[] _layers;

        internal StateGraphCommittedDebugState(
            uint schemaVersion,
            ulong contentFingerprint,
            GraphId graphId,
            ulong catalogFingerprint,
            GraphInstanceId graphInstanceId,
            TimelineId timelineId,
            ClockDomainId clockDomainId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
            double seconds,
            StateGraphCommittedDebugLayer[] layers)
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
            _layers = layers ?? Array.Empty<StateGraphCommittedDebugLayer>();
        }

        internal uint SchemaVersion { get; }
        internal ulong ContentFingerprint { get; }
        internal GraphId GraphId { get; }
        internal ulong CatalogFingerprint { get; }
        internal GraphInstanceId GraphInstanceId { get; }
        internal TimelineId TimelineId { get; }
        internal ClockDomainId ClockDomainId { get; }
        internal TimelineEpoch TimelineEpoch { get; }
        internal TimelineTick Tick { get; }
        internal ExecutionSequence ExecutionSequence { get; }
        internal double Seconds { get; }
        internal int LayerCount => _layers.Length;

        internal StateGraphCommittedDebugLayer GetLayer(int index) => _layers[index];
    }
}
