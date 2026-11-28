using System;

// Host 调试器读取上一次提交的活动层和节点状态，界面刷新不会推进执行器。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存上一次成功提交时一个活动节点的身份和进度。</summary>
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

    /// <summary>描述一个已提交图层的活动路径及调试数据。</summary>
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

    /// <summary>集合各层最后一次成功提交的数据，供调试器读取。</summary>
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
