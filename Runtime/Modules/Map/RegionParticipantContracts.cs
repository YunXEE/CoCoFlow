using System;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    public enum RegionParticipantCleanupReason
    {
        CandidateCancelled = 0,
        CandidateFailed = 1,
        Replaced = 2,
        Removed = 3,
        HostShutdown = 4
    }

    public interface IRegionParticipantPlan
    {
        string Fingerprint { get; }
    }

    /// <summary>
    /// Marks a frozen participant plan whose candidate behavior depends on the
    /// effective capability set supplied by the runtime.
    /// </summary>
    /// <remarks>
    /// Unmarked plans are reused while their structural fingerprint is stable.
    /// Marked plans are replaced when their effective capability set changes.
    /// </remarks>
    public interface IRegionCapabilitySensitivePlan :
        IRegionParticipantPlan
    {
    }

    public interface IRegionParticipantConfigFreezer
    {
        Type ConfigurationType { get; }
        Type PlanType { get; }

        bool TryFreeze(
            in RegionParticipantFreezeContext context,
            RegionParticipantConfig configuration,
            out IRegionParticipantPlan plan,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>
    /// Internal adapter contract for participants whose owned resources must be
    /// released before the Chunk's authoritative Content lease.
    /// </summary>
    internal interface IRegionRequiresOwningContentDependency
    {
    }

    public interface IRegionParticipantFactory
    {
        Type CandidateType { get; }

        bool TryCreateCandidate(
            in RegionParticipantCreateContext context,
            IRegionParticipantPlan plan,
            out IRegionParticipantCandidate candidate,
            out CoCoDiagnostic diagnostic);
    }

    public interface IRegionParticipantCandidate
    {
        UniTask<RegionParticipantPrepareResult> PrepareAsync(
            in RegionParticipantPrepareContext context,
            CancellationToken cancellationToken);

        bool TryCommit(
            in RegionParticipantCommitContext context,
            out CoCoDiagnostic diagnostic);

        UniTask<RegionParticipantCleanupResult> CleanupAsync(
            RegionParticipantCleanupReason reason,
            CancellationToken cancellationToken);
    }

    public interface IRegionParticipantTerminalCleanup
    {
        void ForceCleanupNoFail();
    }

    public interface IRegionFragmentResolver
    {
        bool TryResolveGameObject(
            string fragmentId,
            out GameObject gameObject,
            out CoCoDiagnostic diagnostic);
    }

    public readonly struct RegionParticipantFreezeContext
    {
        internal RegionParticipantFreezeContext(
            RegionPlanNodeId nodeId,
            string fragmentId,
            RegionCompiledSceneReference sceneReference)
        {
            NodeId = nodeId;
            FragmentId = fragmentId ?? string.Empty;
            SceneReference = sceneReference;
        }

        public RegionPlanNodeId NodeId { get; }
        public string FragmentId { get; }
        public RegionCompiledSceneReference SceneReference { get; }
    }

    public readonly struct RegionParticipantCreateContext
    {
        internal RegionParticipantCreateContext(
            RegionPlanNodeId nodeId,
            string fragmentId,
            IRegionFragmentResolver fragmentResolver)
        {
            NodeId = nodeId;
            FragmentId = fragmentId ?? string.Empty;
            FragmentResolver = fragmentResolver;
        }

        public RegionPlanNodeId NodeId { get; }
        public string FragmentId { get; }
        public IRegionFragmentResolver FragmentResolver { get; }
    }

    public readonly struct RegionParticipantPrepareContext
    {
        internal RegionParticipantPrepareContext(
            RegionPlanNodeId nodeId,
            RegionCapabilitySet capabilities,
            long transitionGeneration,
            IRegionFragmentResolver fragmentResolver)
        {
            NodeId = nodeId;
            Capabilities = capabilities ?? RegionCapabilitySet.Empty;
            TransitionGeneration = transitionGeneration;
            FragmentResolver = fragmentResolver;
        }

        public RegionPlanNodeId NodeId { get; }
        public RegionCapabilitySet Capabilities { get; }
        public long TransitionGeneration { get; }
        public IRegionFragmentResolver FragmentResolver { get; }
    }

    public readonly struct RegionParticipantCommitContext
    {
        internal RegionParticipantCommitContext(
            RegionPlanNodeId nodeId,
            RegionCapabilitySet capabilities,
            long transitionGeneration)
        {
            NodeId = nodeId;
            Capabilities = capabilities ?? RegionCapabilitySet.Empty;
            TransitionGeneration = transitionGeneration;
        }

        public RegionPlanNodeId NodeId { get; }
        public RegionCapabilitySet Capabilities { get; }
        public long TransitionGeneration { get; }
    }

    public readonly struct RegionParticipantPrepareResult
    {
        private RegionParticipantPrepareResult(
            bool succeeded,
            CoCoDiagnostic diagnostic)
        {
            Succeeded = succeeded;
            Diagnostic = diagnostic;
        }

        public bool Succeeded { get; }
        public CoCoDiagnostic Diagnostic { get; }

        public static RegionParticipantPrepareResult Success() =>
            new RegionParticipantPrepareResult(true, CoCoDiagnostic.None);

        public static RegionParticipantPrepareResult Failure(
            CoCoDiagnostic diagnostic) =>
            new RegionParticipantPrepareResult(false, diagnostic);
    }

    public readonly struct RegionParticipantCleanupResult
    {
        private RegionParticipantCleanupResult(
            bool succeeded,
            CoCoDiagnostic diagnostic)
        {
            Succeeded = succeeded;
            Diagnostic = diagnostic;
        }

        public bool Succeeded { get; }
        public CoCoDiagnostic Diagnostic { get; }

        public static RegionParticipantCleanupResult Success() =>
            new RegionParticipantCleanupResult(true, CoCoDiagnostic.None);

        public static RegionParticipantCleanupResult Failure(
            CoCoDiagnostic diagnostic) =>
            new RegionParticipantCleanupResult(false, diagnostic);
    }
}
