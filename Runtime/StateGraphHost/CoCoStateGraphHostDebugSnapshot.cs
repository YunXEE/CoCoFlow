using System;

namespace CoCoFlow.Runtime.Core
{
    internal readonly struct CoCoStateGraphHostDebugActiveState
    {
        internal CoCoStateGraphHostDebugActiveState(
            CoCoStateId stateId,
            double localSeconds,
            double actionProgress)
        {
            StateId = stateId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
        }

        internal CoCoStateId StateId { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
    }

    internal sealed class CoCoStateGraphHostDebugLayer
    {
        private readonly CoCoStateGraphHostDebugActiveState[] _activeStates;

        internal CoCoStateGraphHostDebugLayer(
            CoCoLayerId layerId,
            CoCoTransitionId winningTransitionId,
            CoCoStateGraphHostDebugActiveState[] activeStates)
        {
            LayerId = layerId;
            WinningTransitionId = winningTransitionId;
            _activeStates = activeStates ?? Array.Empty<CoCoStateGraphHostDebugActiveState>();
        }

        internal CoCoLayerId LayerId { get; }
        internal CoCoTransitionId WinningTransitionId { get; }
        internal int ActiveStateCount => _activeStates.Length;

        internal CoCoStateGraphHostDebugActiveState GetActiveState(int index) =>
            _activeStates[index];
    }

    internal sealed class CoCoStateGraphHostDebugSnapshot
    {
        private readonly CoCoStateGraphHostDebugLayer[] _layers;

        private CoCoStateGraphHostDebugSnapshot(
            CoCoStateGraphCommittedDebugState graph,
            CoCoRuntimeLifecycleState lifecycle,
            CoCoRuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            CoCoStateFlowFrameHeader contextHeader,
            CoCoContextRevision contextRevision,
            CoCoContextFrameOrigin contextOrigin,
            CoCoStateGraphHostDebugLayer[] layers)
        {
            SchemaVersion = graph.SchemaVersion;
            ContentFingerprint = graph.ContentFingerprint;
            GraphId = graph.GraphId;
            CatalogFingerprint = graph.CatalogFingerprint;
            GraphInstanceId = graph.GraphInstanceId;
            TimelineId = graph.TimelineId;
            ClockDomainId = graph.ClockDomainId;
            TimelineEpoch = graph.TimelineEpoch;
            Tick = graph.Tick;
            ExecutionSequence = graph.ExecutionSequence;
            Seconds = graph.Seconds;
            Lifecycle = lifecycle;
            Fault = fault;
            RequiresWorldCorrection = requiresWorldCorrection;
            LastDiagnostic = lastDiagnostic;
            ContextHeader = contextHeader;
            ContextRevision = contextRevision;
            ContextOrigin = contextOrigin;
            _layers = layers;
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
        internal CoCoRuntimeLifecycleState Lifecycle { get; }
        internal CoCoRuntimeFault Fault { get; }
        internal bool RequiresWorldCorrection { get; }
        internal CoCoDiagnostic LastDiagnostic { get; }
        internal CoCoStateFlowFrameHeader ContextHeader { get; }
        internal CoCoContextRevision ContextRevision { get; }
        internal CoCoContextFrameOrigin ContextOrigin { get; }
        internal int LayerCount => _layers.Length;

        internal CoCoStateGraphHostDebugLayer GetLayer(int index) => _layers[index];

        internal static CoCoStateGraphHostDebugSnapshot CopyFrom(
            CoCoStateGraphCommittedDebugState graph,
            CoCoRuntimeLifecycleState lifecycle,
            CoCoRuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            CoCoContextFrame context)
        {
            var layers = new CoCoStateGraphHostDebugLayer[graph.LayerCount];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                CoCoStateGraphCommittedDebugLayer sourceLayer = graph.GetLayer(layerIndex);
                var states = new CoCoStateGraphHostDebugActiveState[
                    sourceLayer.ActiveStateCount];
                for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                {
                    CoCoStateGraphCommittedDebugActiveState source =
                        sourceLayer.GetActiveState(stateIndex);
                    states[stateIndex] = new CoCoStateGraphHostDebugActiveState(
                        source.StateId,
                        source.LocalSeconds,
                        source.ActionProgress);
                }

                layers[layerIndex] = new CoCoStateGraphHostDebugLayer(
                    sourceLayer.LayerId,
                    sourceLayer.WinningTransitionId,
                    states);
            }

            return new CoCoStateGraphHostDebugSnapshot(
                graph,
                lifecycle,
                fault,
                requiresWorldCorrection,
                lastDiagnostic,
                context.Header,
                context.Revision,
                context.Origin,
                layers);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only immutable copy of one committed Host boundary and its
    /// Temporal ring metadata. Frames are ordered by logical history depth:
    /// depth zero is the newest retained authority.
    /// </summary>
    internal sealed class CoCoStateGraphHostTemporalDebugSnapshot
    {
        private readonly CoCoStateGraphHostDebugSnapshot _current;
        private readonly CoCoTemporalFrameInfo[] _frames;

        internal CoCoStateGraphHostTemporalDebugSnapshot(
            CoCoStateGraphHostDebugSnapshot current,
            int capacity,
            CoCoTemporalFrameInfo[] frames)
        {
            _current = current;
            Capacity = capacity < 0 ? 0 : capacity;
            _frames = frames == null || frames.Length == 0
                ? Array.Empty<CoCoTemporalFrameInfo>()
                : (CoCoTemporalFrameInfo[])frames.Clone();
        }

        internal int Capacity { get; }
        internal int Count => _frames.Length;
        internal CoCoRuntimeFault Fault =>
            _current == null ? default : _current.Fault;
        internal CoCoStateFlowFrameHeader ContextHeader =>
            _current == null ? default : _current.ContextHeader;
        internal CoCoContextRevision ContextRevision =>
            _current == null ? default : _current.ContextRevision;
        internal CoCoContextFrameOrigin ContextOrigin =>
            _current == null ? default : _current.ContextOrigin;
        internal int LayerCount => _current?.LayerCount ?? 0;

        internal CoCoTemporalFrameInfo GetFrame(int depth) => _frames[depth];

        internal CoCoStateGraphHostDebugLayer GetLayer(int index) =>
            _current.GetLayer(index);
    }
#endif
}
