using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存调试快照中一个活动节点的身份。</summary>
    internal readonly struct HostDebugActiveState
    {
        internal HostDebugActiveState(
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

    /// <summary>保存同一层的活动路径供调试器显示。</summary>
    internal sealed class HostDebugLayer
    {
        private readonly HostDebugActiveState[] _activeStates;

        internal HostDebugLayer(
            LayerId layerId,
            TransitionId winningTransitionId,
            HostDebugActiveState[] activeStates)
        {
            LayerId = layerId;
            WinningTransitionId = winningTransitionId;
            _activeStates = activeStates ?? Array.Empty<HostDebugActiveState>();
        }

        internal LayerId LayerId { get; }
        internal TransitionId WinningTransitionId { get; }
        internal int ActiveStateCount => _activeStates.Length;

        internal HostDebugActiveState GetActiveState(int index) =>
            _activeStates[index];
    }

    /// <summary>汇总同一提交边界上的实例、活动路径、Context 和故障。</summary>
    internal sealed class HostDebugSnapshot
    {
        private readonly HostDebugLayer[] _layers;

        private HostDebugSnapshot(
            StateGraphCommittedDebugState graph,
            RuntimeLifecycleState lifecycle,
            RuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            StateFlowFrameHeader contextHeader,
            ContextRevision contextRevision,
            ContextFrameOrigin contextOrigin,
            HostDebugLayer[] layers)
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

        internal HostDebugLayer GetLayer(int index) => _layers[index];

        internal static HostDebugSnapshot CopyFrom(
            StateGraphCommittedDebugState graph,
            RuntimeLifecycleState lifecycle,
            RuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            ContextFrame context)
        {
            var layers = new HostDebugLayer[graph.LayerCount];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                StateGraphCommittedDebugLayer sourceLayer = graph.GetLayer(layerIndex);
                var states = new HostDebugActiveState[
                    sourceLayer.ActiveStateCount];
                for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                {
                    StateGraphCommittedDebugActiveState source =
                        sourceLayer.GetActiveState(stateIndex);
                    states[stateIndex] = new HostDebugActiveState(
                        source.StateId,
                        source.LocalSeconds,
                        source.ActionProgress);
                }

                layers[layerIndex] = new HostDebugLayer(
                    sourceLayer.LayerId,
                    sourceLayer.WinningTransitionId,
                    states);
            }

            return new HostDebugSnapshot(
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
    /// <summary>保存当前调试快照及历史环中可用帧的元数据。</summary>
    internal sealed class HostHistoryDebugSnapshot
    {
        private readonly HostDebugSnapshot _current;
        private readonly HostHistoryFrame[] _frames;

        internal HostHistoryDebugSnapshot(
            HostDebugSnapshot current,
            int capacity,
            HostHistoryFrame[] frames)
        {
            _current = current;
            Capacity = capacity < 0 ? 0 : capacity;
            _frames = frames == null || frames.Length == 0
                ? Array.Empty<HostHistoryFrame>()
                : (HostHistoryFrame[])frames.Clone();
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

        internal HostHistoryFrame GetFrame(int depth) => _frames[depth];

        internal HostDebugLayer GetLayer(int index) =>
            _current.GetLayer(index);
    }
#endif
}
