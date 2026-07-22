using System;

namespace CoCoFlow.Runtime.Core
{
    internal readonly struct CoCoStateGraphHostDebugActiveState
    {
        internal CoCoStateGraphHostDebugActiveState(
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
        private readonly CoCoOperatorClaimState[] _claims;

        private CoCoStateGraphHostDebugSnapshot(
            CoCoStateGraphCommittedDebugState graph,
            CoCoRuntimeLifecycleState lifecycle,
            CoCoRuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            CoCoStateFlowFrameHeader contextHeader,
            CoCoContextRevision contextRevision,
            CoCoContextFrameOrigin contextOrigin,
            CoCoStateGraphHostDebugLayer[] layers,
            CoCoOperatorClaimState[] claims)
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
            _claims = claims;
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
        internal int ClaimCount => _claims.Length;

        internal CoCoStateGraphHostDebugLayer GetLayer(int index) => _layers[index];
        internal CoCoOperatorClaimState GetClaim(int index) => _claims[index];

        internal static CoCoStateGraphHostDebugSnapshot CopyFrom(
            CoCoStateGraphCommittedDebugState graph,
            CoCoRuntimeLifecycleState lifecycle,
            CoCoRuntimeFault fault,
            bool requiresWorldCorrection,
            CoCoDiagnostic lastDiagnostic,
            CoCoContextFrame context,
            CoCoOperatorClaimState[] claims)
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
                        source.ActivationId,
                        source.LocalSeconds,
                        source.ActionProgress);
                }

                layers[layerIndex] = new CoCoStateGraphHostDebugLayer(
                    sourceLayer.LayerId,
                    sourceLayer.WinningTransitionId,
                    states);
            }

            CoCoOperatorClaimState[] claimCopy = claims == null || claims.Length == 0
                ? Array.Empty<CoCoOperatorClaimState>()
                : (CoCoOperatorClaimState[])claims.Clone();
            return new CoCoStateGraphHostDebugSnapshot(
                graph,
                lifecycle,
                fault,
                requiresWorldCorrection,
                lastDiagnostic,
                context.Header,
                context.Revision,
                context.Origin,
                layers,
                claimCopy);
        }
    }
}
