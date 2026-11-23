using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal readonly struct StateGraphHostDebugActiveState
    {
        internal StateGraphHostDebugActiveState(
            StateId stateId,
            double localSeconds,
            double actionProgress)
        {
            StateId = stateId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
        }

        internal StateId StateId { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
    }

    internal sealed class StateGraphHostDebugLayer
    {
        private readonly StateGraphHostDebugActiveState[] _activeStates;

        internal StateGraphHostDebugLayer(
            LayerId layerId,
            TransitionId winningTransitionId,
            StateGraphHostDebugActiveState[] activeStates)
        {
            LayerId = layerId;
            WinningTransitionId = winningTransitionId;
            _activeStates = activeStates ?? Array.Empty<StateGraphHostDebugActiveState>();
        }

        internal LayerId LayerId { get; }
        internal TransitionId WinningTransitionId { get; }
        internal int ActiveStateCount => _activeStates.Length;

        internal StateGraphHostDebugActiveState GetActiveState(int index) =>
            _activeStates[index];
    }

    internal sealed class StateGraphHostDebugSnapshot
    {
        private readonly StateGraphHostDebugLayer[] _layers;

        private StateGraphHostDebugSnapshot(
            StateGraphCommittedDebugState graph,
            RuntimeLifecycleState lifecycle,
            RuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            StateFlowFrameHeader contextHeader,
            ContextRevision contextRevision,
            ContextFrameOrigin contextOrigin,
            StateGraphHostDebugLayer[] layers)
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
        internal GraphId GraphId { get; }
        internal ulong CatalogFingerprint { get; }
        internal GraphInstanceId GraphInstanceId { get; }
        internal TimelineId TimelineId { get; }
        internal ClockDomainId ClockDomainId { get; }
        internal TimelineEpoch TimelineEpoch { get; }
        internal TimelineTick Tick { get; }
        internal ExecutionSequence ExecutionSequence { get; }
        internal double Seconds { get; }
        internal RuntimeLifecycleState Lifecycle { get; }
        internal RuntimeFault Fault { get; }
        internal bool RequiresWorldCorrection { get; }
        internal CoCoDiagnostic LastDiagnostic { get; }
        internal StateFlowFrameHeader ContextHeader { get; }
        internal ContextRevision ContextRevision { get; }
        internal ContextFrameOrigin ContextOrigin { get; }
        internal int LayerCount => _layers.Length;

        internal StateGraphHostDebugLayer GetLayer(int index) => _layers[index];

        internal static StateGraphHostDebugSnapshot CopyFrom(
            StateGraphCommittedDebugState graph,
            RuntimeLifecycleState lifecycle,
            RuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            ContextFrame context)
        {
            var layers = new StateGraphHostDebugLayer[graph.LayerCount];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                StateGraphCommittedDebugLayer sourceLayer = graph.GetLayer(layerIndex);
                var states = new StateGraphHostDebugActiveState[
                    sourceLayer.ActiveStateCount];
                for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                {
                    StateGraphCommittedDebugActiveState source =
                        sourceLayer.GetActiveState(stateIndex);
                    states[stateIndex] = new StateGraphHostDebugActiveState(
                        source.StateId,
                        source.LocalSeconds,
                        source.ActionProgress);
                }

                layers[layerIndex] = new StateGraphHostDebugLayer(
                    sourceLayer.LayerId,
                    sourceLayer.WinningTransitionId,
                    states);
            }

            return new StateGraphHostDebugSnapshot(
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
    internal sealed class StateGraphHostTemporalDebugSnapshot
    {
        private readonly StateGraphHostDebugSnapshot _current;
        private readonly TemporalFrameInfo[] _frames;

        internal StateGraphHostTemporalDebugSnapshot(
            StateGraphHostDebugSnapshot current,
            int capacity,
            TemporalFrameInfo[] frames)
        {
            _current = current;
            Capacity = capacity < 0 ? 0 : capacity;
            _frames = frames == null || frames.Length == 0
                ? Array.Empty<TemporalFrameInfo>()
                : (TemporalFrameInfo[])frames.Clone();
        }

        internal int Capacity { get; }
        internal int Count => _frames.Length;
        internal RuntimeFault Fault =>
            _current == null ? default : _current.Fault;
        internal StateFlowFrameHeader ContextHeader =>
            _current == null ? default : _current.ContextHeader;
        internal ContextRevision ContextRevision =>
            _current == null ? default : _current.ContextRevision;
        internal ContextFrameOrigin ContextOrigin =>
            _current == null ? default : _current.ContextOrigin;
        internal int LayerCount => _current?.LayerCount ?? 0;

        internal TemporalFrameInfo GetFrame(int depth) => _frames[depth];

        internal StateGraphHostDebugLayer GetLayer(int index) =>
            _current.GetLayer(index);
    }
#endif
}
