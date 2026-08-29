using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    internal enum RegionMonitorParticipantRole
    {
        Committed = 0,
        Reused = 1,
        Candidate = 2,
        Retiring = 3,
        BlockedCleanup = 4,
        FaultRetained = 5
    }

    internal enum RegionMonitorDependencyRole
    {
        Committed = 0,
        Reused = 1,
        CandidateWaiting = 2,
        CandidateReady = 3,
        BlockedRetained = 4,
        FaultRetained = 5
    }

    internal readonly struct RegionParticipantMonitorSnapshot
    {
        internal RegionParticipantMonitorSnapshot(
            long ownershipSequence,
            RegionPlanNodeId nodeId,
            RegionParticipantTypeId participantTypeId,
            RegionParticipantPhase phase,
            int explicitOrder,
            RegionParticipantRequirement requirement,
            RegionTierId tierId,
            RegionParticipantModeId modeId,
            RegionCapabilitySet effectiveCapabilities,
            RegionMonitorParticipantRole role,
            RegionParticipantCleanupReason? cleanupReason,
            ContentId contentId,
            long contentScopeSequence,
            long contentLeaseSequence)
        {
            OwnershipSequence = ownershipSequence;
            NodeId = nodeId;
            ParticipantTypeId = participantTypeId;
            Phase = phase;
            ExplicitOrder = explicitOrder;
            Requirement = requirement;
            TierId = tierId;
            ModeId = modeId;
            EffectiveCapabilities =
                effectiveCapabilities ?? RegionCapabilitySet.Empty;
            Role = role;
            CleanupReason = cleanupReason;
            ContentId = contentId;
            ContentScopeSequence = contentScopeSequence;
            ContentLeaseSequence = contentLeaseSequence;
        }

        internal long OwnershipSequence { get; }
        internal RegionPlanNodeId NodeId { get; }
        internal RegionParticipantTypeId ParticipantTypeId { get; }
        internal RegionParticipantPhase Phase { get; }
        internal int ExplicitOrder { get; }
        internal RegionParticipantRequirement Requirement { get; }
        internal RegionTierId TierId { get; }
        internal RegionParticipantModeId ModeId { get; }
        internal RegionCapabilitySet EffectiveCapabilities { get; }
        internal RegionMonitorParticipantRole Role { get; }
        internal RegionParticipantCleanupReason? CleanupReason { get; }
        internal ContentId ContentId { get; }
        internal long ContentScopeSequence { get; }
        internal long ContentLeaseSequence { get; }
    }

    internal readonly struct RegionDependencyMonitorSnapshot
    {
        internal RegionDependencyMonitorSnapshot(
            RegionId sourceRegionId,
            string ruleFingerprint,
            RegionCapabilityId sourceCapability,
            RegionId targetRegionId,
            RegionCapabilitySet targetCapabilities,
            RegionCoverage targetCoverage,
            long leaseSequence,
            RegionDemandRevision revision,
            RegionReadinessStatus? readiness,
            CoCoDiagnostic diagnostic,
            RegionMonitorDependencyRole role,
            bool isBlocker)
        {
            SourceRegionId = sourceRegionId;
            RuleFingerprint = ruleFingerprint ?? string.Empty;
            SourceCapability = sourceCapability;
            TargetRegionId = targetRegionId;
            TargetCapabilities =
                targetCapabilities ?? RegionCapabilitySet.Empty;
            TargetCoverage = targetCoverage;
            LeaseSequence = leaseSequence;
            Revision = revision;
            Readiness = readiness;
            Diagnostic = diagnostic;
            Role = role;
            IsBlocker = isBlocker;
        }

        internal RegionId SourceRegionId { get; }
        internal string RuleFingerprint { get; }
        internal RegionCapabilityId SourceCapability { get; }
        internal RegionId TargetRegionId { get; }
        internal RegionCapabilitySet TargetCapabilities { get; }
        internal RegionCoverage TargetCoverage { get; }
        internal long LeaseSequence { get; }
        internal RegionDemandRevision Revision { get; }
        internal RegionReadinessStatus? Readiness { get; }
        internal CoCoDiagnostic Diagnostic { get; }
        internal RegionMonitorDependencyRole Role { get; }
        internal bool IsBlocker { get; }
    }

    internal sealed class RegionTransitionMonitorRegionSnapshot
    {
        private readonly ReadOnlyCollection<
            RegionParticipantMonitorSnapshot> participants;
        private readonly ReadOnlyCollection<
            RegionDependencyMonitorSnapshot> dependencies;

        internal RegionTransitionMonitorRegionSnapshot(
            RegionId regionId,
            long peakGeneration,
            int oldNodeCountAtAttemptStart,
            int oldPlusCandidatePeak,
            IList<RegionParticipantMonitorSnapshot> participants,
            IList<RegionDependencyMonitorSnapshot> dependencies)
        {
            RegionId = regionId;
            PeakGeneration = peakGeneration;
            OldNodeCountAtAttemptStart =
                oldNodeCountAtAttemptStart;
            OldPlusCandidatePeak = oldPlusCandidatePeak;
            this.participants =
                new ReadOnlyCollection<
                    RegionParticipantMonitorSnapshot>(
                    participants == null
                        ? new List<
                            RegionParticipantMonitorSnapshot>()
                        : new List<
                            RegionParticipantMonitorSnapshot>(
                            participants));
            this.dependencies =
                new ReadOnlyCollection<
                    RegionDependencyMonitorSnapshot>(
                    dependencies == null
                        ? new List<
                            RegionDependencyMonitorSnapshot>()
                        : new List<
                            RegionDependencyMonitorSnapshot>(
                            dependencies));
        }

        internal RegionId RegionId { get; }
        internal long PeakGeneration { get; }
        internal int OldNodeCountAtAttemptStart { get; }
        internal int OldPlusCandidatePeak { get; }
        internal IReadOnlyList<RegionParticipantMonitorSnapshot>
            Participants => participants;
        internal IReadOnlyList<RegionDependencyMonitorSnapshot>
            Dependencies => dependencies;
    }

    internal sealed class RegionMapMonitorSnapshot
    {
        private readonly ReadOnlyCollection<
            RegionTransitionMonitorRegionSnapshot> transitionRegions;
        private readonly ReadOnlyCollection<
            RegionDemandRuntimeSnapshot> temporalRetentionDemands;

        internal RegionMapMonitorSnapshot(
            RegionRuntimeSnapshot runtime,
            bool temporalDispatchDeferred,
            int deferredTransitionCount,
            IList<RegionTransitionMonitorRegionSnapshot>
                transitionRegions,
            IList<RegionDemandRuntimeSnapshot>
                temporalRetentionDemands)
        {
            Runtime = runtime;
            TemporalDispatchDeferred = temporalDispatchDeferred;
            DeferredTransitionCount = deferredTransitionCount;
            this.transitionRegions =
                new ReadOnlyCollection<
                    RegionTransitionMonitorRegionSnapshot>(
                    transitionRegions == null
                        ? new List<
                            RegionTransitionMonitorRegionSnapshot>()
                        : new List<
                            RegionTransitionMonitorRegionSnapshot>(
                            transitionRegions));
            this.temporalRetentionDemands =
                new ReadOnlyCollection<
                    RegionDemandRuntimeSnapshot>(
                    temporalRetentionDemands == null
                        ? new List<
                            RegionDemandRuntimeSnapshot>()
                        : new List<
                            RegionDemandRuntimeSnapshot>(
                            temporalRetentionDemands));
        }

        internal RegionRuntimeSnapshot Runtime { get; }
        internal bool TemporalDispatchDeferred { get; }
        internal int DeferredTransitionCount { get; }
        internal IReadOnlyList<RegionTransitionMonitorRegionSnapshot>
            TransitionRegions => transitionRegions;
        internal IReadOnlyList<RegionDemandRuntimeSnapshot>
            TemporalRetentionDemands => temporalRetentionDemands;
    }
}
