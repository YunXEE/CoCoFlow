using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    internal sealed class RegionTransitionRuntime :
        IRegionDemandTransitionSink
    {
        private enum AttemptOutcome
        {
            Ready = 0,
            Failed = 1,
            Cancelled = 2,
            BlockedCleanup = 3,
            FaultedCommit = 4
        }

        private enum BlockedContinuation
        {
            Rerun = 0,
            PublishReady = 1
        }

        private sealed class DesiredNode
        {
            internal DesiredNode(
                RegionCompiledParticipantNode definition,
                RegionCompiledParticipantVariant variant)
            {
                Definition = definition;
                Variant = variant;
            }

            internal RegionCompiledParticipantNode Definition { get; }
            internal RegionCompiledParticipantVariant Variant { get; }
            internal RegionTierId TierId => Variant.TierId;
            internal RegionCapabilitySet Capabilities =>
                Variant.EffectiveCapabilities;
            internal RegionParticipantModeId ModeId => Variant.ModeId;
            internal IRegionParticipantPlan ParticipantPlan =>
                Variant.ParticipantPlan;
            internal string EffectiveFingerprint => Variant.Fingerprint;
        }

        private sealed class OwnedNode
        {
            internal OwnedNode(
                long ownershipSequence,
                RegionCompiledParticipantNode definition,
                RegionCompiledParticipantVariant variant,
                IRegionParticipantCandidate candidate)
            {
                OwnershipSequence = ownershipSequence;
                Definition = definition;
                Variant = variant;
                Candidate = candidate;
            }

            internal long OwnershipSequence { get; }
            internal RegionCompiledParticipantNode Definition { get; }
            internal RegionCompiledParticipantVariant Variant { get; }
            internal RegionTierId TierId => Variant.TierId;
            internal RegionCapabilitySet Capabilities =>
                Variant.EffectiveCapabilities;
            internal RegionParticipantModeId ModeId => Variant.ModeId;
            internal IRegionParticipantPlan ParticipantPlan =>
                Variant.ParticipantPlan;
            internal string EffectiveFingerprint => Variant.Fingerprint;
            internal IRegionParticipantCandidate Candidate { get; }
        }

        private sealed class CleanupWork
        {
            internal CleanupWork(
                OwnedNode node,
                RegionParticipantCleanupReason reason)
            {
                Node = node;
                Reason = reason;
            }

            internal OwnedNode Node { get; }
            internal RegionParticipantCleanupReason Reason { get; }
            internal bool InvocationAttempted { get; set; }
            internal Task<RegionParticipantCleanupResult> InFlight { get; set; }
            internal CoCoDiagnostic FailureDiagnostic { get; set; }
            internal bool Completed { get; set; }
        }

        private sealed class CleanupBatch
        {
            internal CleanupBatch(IList<CleanupWork> works)
            {
                Works = works == null
                    ? new List<CleanupWork>()
                    : new List<CleanupWork>(works);
            }

            internal List<CleanupWork> Works { get; }
            internal int Index { get; set; }
            internal int RemainingCount => Works.Count - Index;
            internal bool IsComplete => Index >= Works.Count;
        }

        private readonly struct CleanupBatchResult
        {
            internal CleanupBatchResult(
                bool succeeded,
                CoCoDiagnostic diagnostic)
            {
                Succeeded = succeeded;
                Diagnostic = diagnostic;
            }

            internal bool Succeeded { get; }
            internal CoCoDiagnostic Diagnostic { get; }
        }

        private readonly struct DependencyPrepareResult
        {
            internal DependencyPrepareResult(
                bool succeeded,
                CoCoDiagnostic diagnostic)
            {
                Succeeded = succeeded;
                Diagnostic = diagnostic;
            }

            internal bool Succeeded { get; }
            internal CoCoDiagnostic Diagnostic { get; }
        }

        private sealed class DependencyLeaseEntry
        {
            internal DependencyLeaseEntry(
                RegionCompiledDependencyRule rule,
                RegionDemandLease lease,
                RegionDemandRevision revision)
            {
                Rule = rule;
                Lease = lease;
                Revision = revision;
            }

            internal RegionCompiledDependencyRule Rule { get; }
            internal RegionDemandLease Lease { get; }
            internal RegionDemandRevision Revision { get; }
            internal RegionReadinessStatus? Readiness { get; set; }
            internal CoCoDiagnostic Diagnostic { get; set; }
        }

        private sealed class DependencyAttempt
        {
            internal Dictionary<string, DependencyLeaseEntry> Next { get; } =
                new Dictionary<string, DependencyLeaseEntry>(
                    StringComparer.Ordinal);
            internal List<DependencyLeaseEntry> Created { get; } =
                new List<DependencyLeaseEntry>();
        }

        private sealed class BlockedState
        {
            internal BlockedState(
                CleanupBatch batch,
                RegionDemandResolution resolution,
                BlockedContinuation continuation,
                bool optionalDegraded,
                CoCoDiagnostic diagnostic,
                DependencyAttempt dependencies)
            {
                Batch = batch;
                Resolution = resolution;
                Continuation = continuation;
                OptionalDegraded = optionalDegraded;
                Diagnostic = diagnostic;
                Dependencies = dependencies;
            }

            internal CleanupBatch Batch { get; }
            internal RegionDemandResolution Resolution { get; }
            internal BlockedContinuation Continuation { get; }
            internal bool OptionalDegraded { get; }
            internal CoCoDiagnostic Diagnostic { get; }
            internal DependencyAttempt Dependencies { get; }
        }

        private sealed class RegionState
        {
            internal RegionState(RegionCompiledPlan plan)
            {
                Plan = plan;
                for (int index = 0; index < plan.Chunks.Count; index++)
                {
                    KnownChunks.Add(plan.Chunks[index].ChunkId);
                }
            }

            internal RegionCompiledPlan Plan { get; }
            internal Dictionary<RegionPlanNodeId, OwnedNode> Committed { get; } =
                new Dictionary<RegionPlanNodeId, OwnedNode>();
            internal List<OwnedNode> ActiveOwned { get; } =
                new List<OwnedNode>();
            internal List<OwnedNode> FaultOwned { get; } =
                new List<OwnedNode>();
            internal List<RegionChunkId> KnownChunks { get; } =
                new List<RegionChunkId>();
            internal Dictionary<string, DependencyLeaseEntry>
                CommittedDependencies { get; } =
                    new Dictionary<string, DependencyLeaseEntry>(
                        StringComparer.Ordinal);
            internal List<DependencyLeaseEntry> FaultDependencies { get; } =
                new List<DependencyLeaseEntry>();
            internal DependencyAttempt ActiveDependencyAttempt { get; set; }
            internal CleanupBatch ActiveCleanupBatch { get; set; }
            internal RegionDemandResolution PendingResolution { get; set; }
            internal CancellationTokenSource ActiveCancellation { get; set; }
            internal Task RunnerTask { get; set; }
            internal BlockedState Blocked { get; set; }
            internal bool RunnerActive { get; set; }
            internal bool RequiresRetry { get; set; }
            internal bool FaultedCommit { get; set; }
            internal int ReusedNodeCount { get; set; }
            internal bool OptionalDegraded { get; set; }
            internal CoCoDiagnostic LastDiagnostic { get; set; }
            internal HashSet<RegionPlanNodeId> ReusedNodeIds { get; } =
                new HashSet<RegionPlanNodeId>();
            internal long PeakGeneration { get; set; }
            internal int OldNodeCountAtAttemptStart { get; set; }
            internal int OldPlusCandidatePeak { get; set; }
        }

        private sealed class TransitionAttempt
        {
            private readonly RegionTransitionRuntime owner;
            private readonly RegionState state;
            private readonly RegionDemandResolution resolution;
            private readonly Dictionary<RegionChunkId, CoCoRegionChunkAnchor>
                anchors =
                    new Dictionary<RegionChunkId, CoCoRegionChunkAnchor>();

            internal TransitionAttempt(
                RegionTransitionRuntime owner,
                RegionState state,
                RegionDemandResolution resolution)
            {
                this.owner = owner;
                this.state = state;
                this.resolution = resolution;
            }

            internal IRegionFragmentResolver CreateResolver(
                RegionPlanNodeId nodeId) =>
                new NodeResolver(this, nodeId);

            internal bool TrySeedAnchor(
                OwnedNode node,
                out CoCoDiagnostic diagnostic)
            {
                if (!(node.Candidate is IRegionChunkAnchorSource source) ||
                    !source.TryGetAnchor(
                        out CoCoRegionChunkAnchor anchor) ||
                    anchor == null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "A reused Content node no longer exposes its leased Scene Anchor.");
                    return false;
                }

                return TryRegisterChunkAnchor(
                    node.Definition.Id,
                    resolution.DesiredGeneration,
                    anchor,
                    out diagnostic);
            }

            private bool TryResolveGameObject(
                RegionPlanNodeId nodeId,
                string fragmentId,
                out GameObject gameObject,
                out CoCoDiagnostic diagnostic)
            {
                gameObject = null;
                if (!nodeId.HasChunkId ||
                    !anchors.TryGetValue(
                        nodeId.ChunkId,
                        out CoCoRegionChunkAnchor anchor) ||
                    anchor == null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The node's exact leased Chunk Scene Anchor is not prepared.");
                    return false;
                }

                return anchor.TryResolveGameObject(
                    fragmentId,
                    out gameObject,
                    out diagnostic);
            }

            private bool TryCreateContentScope(
                RegionPlanNodeId nodeId,
                long transitionGeneration,
                out ContentScope scope,
                out CoCoDiagnostic diagnostic)
            {
                scope = null;
                if (nodeId.RegionId != state.Plan.RegionId ||
                    !nodeId.HasChunkId ||
                    transitionGeneration !=
                    resolution.DesiredGeneration)
                {
                    diagnostic = RegionErrors.DemandConflict(
                        "The Content participant requested ownership outside its active transition.");
                    return false;
                }

                string ownerValue =
                    "map/" + nodeId + "/g" +
                    transitionGeneration;
                if (!ContentOwnerId.TryCreate(
                        ownerValue,
                        out ContentOwnerId ownerId))
                {
                    diagnostic = RegionErrors.InvalidIdentifier(
                        "The Map runtime could not create a Content owner id.");
                    return false;
                }

                return owner.runtime.ContentRuntime.TryCreateScope(
                    ownerId,
                    out scope,
                    out diagnostic);
            }

            private bool TryRegisterChunkAnchor(
                RegionPlanNodeId nodeId,
                long transitionGeneration,
                CoCoRegionChunkAnchor anchor,
                out CoCoDiagnostic diagnostic)
            {
                if (nodeId.RegionId != state.Plan.RegionId ||
                    !nodeId.HasChunkId ||
                    transitionGeneration !=
                    resolution.DesiredGeneration ||
                    anchor == null ||
                    anchor.RegionId != nodeId.RegionId ||
                    anchor.ChunkId != nodeId.ChunkId)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The prepared Anchor does not match its active Region transition node.");
                    return false;
                }

                if (anchors.TryGetValue(
                        nodeId.ChunkId,
                        out CoCoRegionChunkAnchor current))
                {
                    if (current == anchor)
                    {
                        diagnostic = CoCoDiagnostic.None;
                        return true;
                    }

                    diagnostic = RegionErrors.SceneContract(
                        "More than one leased Scene Anchor attempted to own the same Chunk.");
                    return false;
                }

                anchors.Add(nodeId.ChunkId, anchor);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            private void UnregisterChunkAnchor(
                RegionPlanNodeId nodeId,
                CoCoRegionChunkAnchor anchor)
            {
                if (nodeId.HasChunkId &&
                    anchors.TryGetValue(
                        nodeId.ChunkId,
                        out CoCoRegionChunkAnchor current) &&
                    current == anchor)
                {
                    anchors.Remove(nodeId.ChunkId);
                }
            }

            private sealed class NodeResolver :
                IRegionFragmentResolver,
                IRegionContentParticipantRuntime
            {
                private readonly TransitionAttempt attempt;
                private readonly RegionPlanNodeId nodeId;

                internal NodeResolver(
                    TransitionAttempt attempt,
                    RegionPlanNodeId nodeId)
                {
                    this.attempt = attempt;
                    this.nodeId = nodeId;
                }

                public bool TryResolveGameObject(
                    string fragmentId,
                    out GameObject gameObject,
                    out CoCoDiagnostic diagnostic) =>
                    attempt.TryResolveGameObject(
                        nodeId,
                        fragmentId,
                        out gameObject,
                        out diagnostic);

                public bool TryCreateContentScope(
                    RegionPlanNodeId requestedNodeId,
                    long transitionGeneration,
                    out ContentScope scope,
                    out CoCoDiagnostic diagnostic)
                {
                    if (requestedNodeId != nodeId)
                    {
                        scope = null;
                        diagnostic = RegionErrors.DemandConflict(
                            "The Content participant cannot acquire for another node.");
                        return false;
                    }

                    return attempt.TryCreateContentScope(
                        nodeId,
                        transitionGeneration,
                        out scope,
                        out diagnostic);
                }

                public bool TryRegisterChunkAnchor(
                    RegionPlanNodeId requestedNodeId,
                    long transitionGeneration,
                    CoCoRegionChunkAnchor anchor,
                    out CoCoDiagnostic diagnostic)
                {
                    if (requestedNodeId != nodeId)
                    {
                        diagnostic = RegionErrors.SceneContract(
                            "The Content participant cannot register another node's Anchor.");
                        return false;
                    }

                    return attempt.TryRegisterChunkAnchor(
                        nodeId,
                        transitionGeneration,
                        anchor,
                        out diagnostic);
                }

                public void UnregisterChunkAnchor(
                    RegionPlanNodeId requestedNodeId,
                    CoCoRegionChunkAnchor anchor)
                {
                    if (requestedNodeId == nodeId)
                    {
                        attempt.UnregisterChunkAnchor(nodeId, anchor);
                    }
                }
            }
        }

        private readonly RegionRuntime runtime;
        private readonly RegionParticipantCatalog catalog;
        private readonly Dictionary<RegionId, RegionState> states =
            new Dictionary<RegionId, RegionState>();
        private readonly HashSet<IRegionParticipantCandidate>
            ownedCandidates =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
        private readonly TimeSpan cleanupTimeout;
        private RegionDemandScope dependencyScope;
        private long nextOwnershipSequence = 1L;
        private int callbackDepth;
        private bool shuttingDown;
        private bool disposed;
        private Task<CoCoDiagnostic> shutdownTask;

        private RegionTransitionRuntime(
            RegionRuntime runtime,
            RegionParticipantCatalog catalog,
            IEnumerable<RegionCompiledPlan> plans,
            TimeSpan cleanupTimeout)
        {
            this.runtime = runtime;
            this.catalog = catalog;
            this.cleanupTimeout = cleanupTimeout;
            foreach (RegionCompiledPlan plan in plans)
            {
                states.Add(plan.RegionId, new RegionState(plan));
            }
        }

        public bool IsInvokingParticipantCallback =>
            Volatile.Read(ref callbackDepth) != 0;

        internal IReadOnlyList<RegionTransitionMonitorRegionSnapshot>
            CaptureMonitorRegions()
        {
            if (!RegionMainThreadGuard.IsMainThread)
            {
                throw new InvalidOperationException(
                    "Map transition monitoring must be captured on the Unity main thread.");
            }

            var orderedStates = new List<RegionState>(states.Values);
            orderedStates.Sort(
                (left, right) => CompareRegionIds(
                    left.Plan.RegionId,
                    right.Plan.RegionId));
            var snapshots =
                new List<RegionTransitionMonitorRegionSnapshot>(
                    orderedStates.Count);
            for (int index = 0;
                 index < orderedStates.Count;
                 index++)
            {
                RegionState state = orderedStates[index];
                List<RegionParticipantMonitorSnapshot> participants =
                    CaptureParticipantSnapshots(state);
                List<RegionDependencyMonitorSnapshot> dependencies =
                    CaptureDependencySnapshots(state);
                snapshots.Add(
                    new RegionTransitionMonitorRegionSnapshot(
                        state.Plan.RegionId,
                        state.PeakGeneration,
                        state.OldNodeCountAtAttemptStart,
                        state.OldPlusCandidatePeak,
                        participants,
                        dependencies));
            }

            return snapshots.AsReadOnly();
        }

        internal static bool TryCreate(
            RegionRuntime runtime,
            RegionParticipantCatalog catalog,
            IEnumerable<RegionCompiledPlan> plans,
            TimeSpan cleanupTimeout,
            out RegionTransitionRuntime transition,
            out CoCoDiagnostic diagnostic)
        {
            transition = null;
            if (!RegionMainThreadGuard.IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (runtime == null ||
                runtime.IsDisposed ||
                runtime.IsShuttingDown ||
                catalog == null ||
                !catalog.IsSealed ||
                plans == null ||
                cleanupTimeout <= TimeSpan.Zero)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "A live Region runtime, sealed catalog, immutable plans, and positive cleanup timeout are required.");
                return false;
            }

            var unique = new HashSet<RegionId>();
            var materialized = new List<RegionCompiledPlan>();
            foreach (RegionCompiledPlan plan in plans)
            {
                if (plan == null ||
                    !plan.RegionId.IsValid ||
                    !unique.Add(plan.RegionId))
                {
                    diagnostic = RegionErrors.CompilationFailed(
                        "Compiled bootstrap plans must be non-null and have unique Region ids.");
                    return false;
                }

                materialized.Add(plan);
            }

            if (materialized.Count == 0)
            {
                diagnostic = RegionErrors.CompilationFailed(
                    "At least one compiled Region plan is required.");
                return false;
            }

            var candidate = new RegionTransitionRuntime(
                runtime,
                catalog,
                materialized,
                cleanupTimeout);
            if (!runtime.TryAttachTransitionSink(
                    candidate,
                    out diagnostic))
            {
                return false;
            }

            if (!RegionDemandOwnerId.TryCreate(
                    "cocoflow.map.dependencies",
                    out RegionDemandOwnerId dependencyOwnerId) ||
                !runtime.TryCreateDemandScope(
                    dependencyOwnerId,
                    out candidate.dependencyScope,
                    out diagnostic))
            {
                candidate.ForceShutdown();
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.DemandConflict(
                        "The Map runtime could not reserve its cross-Region dependency Demand Scope.");
                }

                return false;
            }

            transition = candidate;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryValidateDemand(
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out CoCoDiagnostic diagnostic)
        {
            if (shuttingDown || disposed)
            {
                diagnostic = RegionErrors.RuntimeDisposed();
                return false;
            }

            if (!states.TryGetValue(
                    regionId,
                    out RegionState state))
            {
                diagnostic = RegionErrors.InvalidIdentifier(
                    "No compiled bootstrap Binding owns Region '" +
                    regionId.Value + "'.");
                return false;
            }

            for (int index = 0;
                 index < capabilities.Count;
                 index++)
            {
                RegionCapabilityId capability =
                    capabilities.Capabilities[index];
                if (!catalog.SupportsCapability(capability))
                {
                    diagnostic =
                        RegionErrors.UnsupportedCapability(capability);
                    return false;
                }
            }

            if (!state.Plan.TryResolveTier(
                    capabilities,
                    out RegionCompiledTier _))
            {
                RegionCapabilitySet finalCapabilities =
                    state.Plan.Tiers.Count == 0
                        ? RegionCapabilitySet.Empty
                        : state.Plan.Tiers[
                            state.Plan.Tiers.Count - 1]
                            .Capabilities;
                for (int index = 0;
                     index < capabilities.Count;
                     index++)
                {
                    RegionCapabilityId capability =
                        capabilities.Capabilities[index];
                    if (!finalCapabilities.Contains(capability))
                    {
                        diagnostic =
                            RegionErrors.UnsupportedCapability(
                                capability);
                        return false;
                    }
                }

                diagnostic = RegionErrors.InvalidProfile(
                    "The compiled Region Profile cannot resolve the requested capability set to a fidelity tier.");
                return false;
            }

            if (!coverage.CoversAll)
            {
                for (int index = 0;
                     index < coverage.Chunks.Count;
                     index++)
                {
                    if (!state.Plan.TryGetChunk(
                            coverage.Chunks[index],
                            out _))
                    {
                        diagnostic = RegionErrors.InvalidCoverage(
                            "Chunk '" +
                            coverage.Chunks[index].Value +
                            "' is not owned by Region '" +
                            regionId.Value + "'.");
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void RequestTransition(RegionDemandResolution resolution)
        {
            if (resolution == null ||
                shuttingDown ||
                disposed ||
                !states.TryGetValue(
                    resolution.RegionId,
                    out RegionState state))
            {
                throw new InvalidOperationException(
                    "The Region transition request is not owned by this runtime.");
            }

            PublishResolvedFidelity(
                state,
                resolution);
            state.PendingResolution = resolution;
            if (state.FaultedCommit)
            {
                CoCoDiagnostic fault = state.LastDiagnostic.IsNone
                    ? RegionErrors.CommitFaulted(
                        "The Region is terminally faulted after commit.")
                    : state.LastDiagnostic;
                PublishFailureAndProgress(
                    state,
                    resolution.DesiredGeneration,
                    fault,
                    true,
                    false,
                    state.ActiveOwned.Count +
                    state.FaultOwned.Count);
                return;
            }

            if (state.Blocked != null)
            {
                PublishFailureAndProgress(
                    state,
                    resolution.DesiredGeneration,
                    state.Blocked.Diagnostic,
                    false,
                    true,
                    state.Blocked.Batch.RemainingCount);
                return;
            }

            state.RequiresRetry = false;
            state.ActiveCancellation?.Cancel();
            EnsureRunner(state);
        }

        public bool TryAcceptRetry(
            RegionId regionId,
            RegionDemandResolution resolution,
            out CoCoDiagnostic diagnostic)
        {
            if (shuttingDown ||
                disposed ||
                resolution == null ||
                !states.TryGetValue(
                    regionId,
                    out RegionState state))
            {
                diagnostic = RegionErrors.RuntimeDisposed();
                return false;
            }

            if (state.FaultedCommit)
            {
                diagnostic = state.LastDiagnostic.IsNone
                    ? RegionErrors.CommitFaulted(
                        "Commit faults are terminal until Host shutdown.")
                    : state.LastDiagnostic;
                return false;
            }

            if (state.RunnerActive)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region already has an active transition.");
                return false;
            }

            if (state.Blocked != null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!state.RequiresRetry)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region has no retryable Prepare failure.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void StartAcceptedRetry(
            RegionId regionId,
            RegionDemandResolution resolution)
        {
            if (shuttingDown ||
                disposed ||
                resolution == null ||
                !states.TryGetValue(
                    regionId,
                    out RegionState state) ||
                state.FaultedCommit ||
                state.RunnerActive)
            {
                throw new InvalidOperationException(
                    "The accepted Region retry could not be started synchronously.");
            }

            if (state.Blocked != null)
            {
                if (resolution.DesiredGeneration !=
                    state.Blocked.Resolution.DesiredGeneration)
                {
                    state.PendingResolution = resolution;
                }

                StartBlockedRetry(state);
                return;
            }

            if (!state.RequiresRetry)
            {
                throw new InvalidOperationException(
                    "The accepted Region retry lost its retryable Prepare failure.");
            }

            state.RequiresRetry = false;
            state.PendingResolution = resolution;
            EnsureRunner(state);
        }

        public UniTask<CoCoDiagnostic> ShutdownAsync()
        {
            if (shutdownTask != null)
            {
                return AwaitTaskAsync(shutdownTask);
            }

            shutdownTask = ShutdownCoreAsync().AsTask();
            return AwaitTaskAsync(shutdownTask);
        }

        public void ForceShutdown()
        {
            ForceShutdown(null, null);
        }

        private void ForceShutdown(
            ISet<IRegionParticipantCandidate> excluded,
            IReadOnlyDictionary<
                IRegionParticipantCandidate,
                Task<RegionParticipantCleanupResult>> lateCleanups)
        {
            if (disposed) return;

            shuttingDown = true;
            var exclusionSnapshot =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            if (excluded != null)
            {
                foreach (IRegionParticipantCandidate candidate in excluded)
                {
                    if (candidate != null)
                    {
                        exclusionSnapshot.Add(candidate);
                    }
                }
            }

            var lateCleanupSnapshot =
                new Dictionary<
                    IRegionParticipantCandidate,
                    Task<RegionParticipantCleanupResult>>(
                    ReferenceCandidateComparer.Instance);
            if (lateCleanups != null)
            {
                foreach (KeyValuePair<
                             IRegionParticipantCandidate,
                             Task<RegionParticipantCleanupResult>> pair in
                         lateCleanups)
                {
                    if (pair.Key == null || pair.Value == null) continue;
                    lateCleanupSnapshot[pair.Key] = pair.Value;
                }
            }

            IReadOnlyList<RegionState> shutdownOrder =
                BuildSourceFirstShutdownOrder();
            ForceCleanupStatesInOrderNoThrowAsync(
                shutdownOrder,
                exclusionSnapshot,
                lateCleanupSnapshot).Forget();

            states.Clear();
            callbackDepth = 0;
            disposed = true;
        }

        private void EnsureRunner(RegionState state)
        {
            if (state.RunnerActive ||
                state.Blocked != null ||
                state.FaultedCommit ||
                shuttingDown ||
                disposed ||
                state.PendingResolution == null)
            {
                return;
            }

            state.RunnerActive = true;
            state.RunnerTask = RunStateLoopAsync(state).AsTask();
        }

        private async UniTask RunStateLoopAsync(RegionState state)
        {
            try
            {
                while (!shuttingDown &&
                       !disposed &&
                       state.Blocked == null &&
                       !state.FaultedCommit &&
                       state.PendingResolution != null)
                {
                    RegionDemandResolution resolution =
                        state.PendingResolution;
                    state.PendingResolution = null;
                    state.ActiveCancellation?.Dispose();
                    state.ActiveCancellation =
                        new CancellationTokenSource();

                    AttemptOutcome outcome;
                    try
                    {
                        outcome = await ExecuteTransitionAsync(
                            state,
                            resolution,
                            state.ActiveCancellation.Token);
                        await UniTask.SwitchToMainThread();
                    }
                    catch (Exception exception)
                    {
                        await UniTask.SwitchToMainThread();
                        CoCoDiagnostic failure =
                            RegionErrors.TransitionFailed(
                                "The Region transition runner threw: " +
                                exception.Message);
                        ReleaseActiveDependencies(state);
                        state.LastDiagnostic = failure;
                        if (state.PendingResolution == null)
                        {
                            state.RequiresRetry = true;
                            PublishFailureAndProgress(
                                state,
                                resolution.DesiredGeneration,
                                failure,
                                false,
                                false,
                                state.ActiveOwned.Count);
                        }

                        outcome = AttemptOutcome.Failed;
                    }

                    state.ActiveCancellation.Dispose();
                    state.ActiveCancellation = null;

                    if (outcome == AttemptOutcome.FaultedCommit ||
                        outcome == AttemptOutcome.BlockedCleanup)
                    {
                        break;
                    }

                    if (outcome == AttemptOutcome.Failed &&
                        state.PendingResolution == null)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                state.ActiveCancellation?.Dispose();
                state.ActiveCancellation = null;
                state.RunnerActive = false;
                if (state.PendingResolution != null &&
                    state.Blocked == null &&
                    !state.FaultedCommit &&
                    !shuttingDown &&
                    !disposed)
                {
                    EnsureRunner(state);
                }
            }
        }

        private async UniTask<AttemptOutcome> ExecuteTransitionAsync(
            RegionState state,
            RegionDemandResolution resolution,
            CancellationToken cancellationToken)
        {
            state.PeakGeneration = resolution.DesiredGeneration;
            state.OldNodeCountAtAttemptStart =
                CountUniqueOwnedNodes(state.Committed.Values);
            state.OldPlusCandidatePeak =
                state.OldNodeCountAtAttemptStart;
            state.ReusedNodeIds.Clear();

            var dependencyAttempt = new DependencyAttempt();
            state.ActiveDependencyAttempt = dependencyAttempt;
            DependencyPrepareResult dependencyResult =
                await PrepareDependenciesAsync(
                    state,
                    resolution,
                    dependencyAttempt,
                    cancellationToken);
            await UniTask.SwitchToMainThread();
            if (!dependencyResult.Succeeded)
            {
                if (IsSuperseded(
                        state,
                        resolution,
                        cancellationToken))
                {
                    return await CleanupCancelledAttemptAsync(
                        state,
                        resolution,
                        Array.Empty<OwnedNode>(),
                        false);
                }

                return await CleanupAttemptAfterFailureAsync(
                    state,
                    resolution,
                    Array.Empty<OwnedNode>(),
                    dependencyResult.Diagnostic,
                    false,
                    cancellationToken);
            }

            Dictionary<RegionPlanNodeId, DesiredNode> desired =
                BuildDesiredNodes(state.Plan, resolution);
            var nextCommitted =
                new Dictionary<RegionPlanNodeId, OwnedNode>();
            var available = new HashSet<RegionPlanNodeId>();
            var candidates = new List<OwnedNode>();
            var attempt = new TransitionAttempt(this, state, resolution);
            bool optionalDegraded = false;
            int reusedCount = 0;

            for (int nodeIndex = 0;
                 nodeIndex < state.Plan.Nodes.Count;
                 nodeIndex++)
            {
                RegionCompiledParticipantNode definition =
                    state.Plan.Nodes[nodeIndex];
                if (!desired.TryGetValue(
                        definition.Id,
                        out DesiredNode desiredNode))
                {
                    continue;
                }

                if (!DependenciesAvailable(definition, available))
                {
                    CoCoDiagnostic dependencyFailure =
                        RegionErrors.TransitionFailed(
                            "Participant '" + definition.Id +
                            "' has an unavailable dependency.");
                    if (definition.Requirement ==
                        RegionParticipantRequirement.Optional)
                    {
                        optionalDegraded = true;
                        continue;
                    }

                    return await CleanupAttemptAfterFailureAsync(
                        state,
                        resolution,
                        candidates,
                        dependencyFailure,
                        optionalDegraded,
                        cancellationToken);
                }

                if (state.Committed.TryGetValue(
                        definition.Id,
                        out OwnedNode committed) &&
                    string.Equals(
                        committed.EffectiveFingerprint,
                        desiredNode.EffectiveFingerprint,
                        StringComparison.Ordinal))
                {
                    var reused = new OwnedNode(
                        committed.OwnershipSequence,
                        definition,
                        desiredNode.Variant,
                        committed.Candidate);
                    if (reused.Candidate is
                            IRegionChunkAnchorSource &&
                        !attempt.TrySeedAnchor(
                            reused,
                            out CoCoDiagnostic anchorDiagnostic))
                    {
                        if (definition.Requirement ==
                            RegionParticipantRequirement.Optional)
                        {
                            optionalDegraded = true;
                            continue;
                        }

                        return await CleanupAttemptAfterFailureAsync(
                            state,
                            resolution,
                            candidates,
                            anchorDiagnostic,
                            optionalDegraded,
                            cancellationToken);
                    }

                    nextCommitted.Add(definition.Id, reused);
                    available.Add(definition.Id);
                    state.ReusedNodeIds.Add(definition.Id);
                    reusedCount++;
                    continue;
                }

                if (!catalog.TryGetRegistration(
                        definition.ParticipantTypeId,
                        desiredNode.ModeId,
                        out RegionParticipantRegistration registration))
                {
                    CoCoDiagnostic registrationFailure =
                        RegionErrors.CatalogConflict(
                            "The compiled participant registration is no longer available.");
                    if (definition.Requirement ==
                        RegionParticipantRequirement.Optional)
                    {
                        optionalDegraded = true;
                        continue;
                    }

                    return await CleanupAttemptAfterFailureAsync(
                        state,
                        resolution,
                        candidates,
                        registrationFailure,
                        optionalDegraded,
                        cancellationToken);
                }

                IRegionParticipantCandidate candidate = null;
                CoCoDiagnostic createDiagnostic = CoCoDiagnostic.None;
                bool created;
                var createContext = new RegionParticipantCreateContext(
                    definition.Id,
                    desiredNode.TierId,
                    desiredNode.Capabilities,
                    definition.FragmentId,
                    attempt.CreateResolver(definition.Id));
                EnterParticipantCallback();
                try
                {
                    created = registration.Factory.TryCreateCandidate(
                        createContext,
                        desiredNode.ParticipantPlan,
                        out candidate,
                        out createDiagnostic);
                }
                catch (Exception exception)
                {
                    created = false;
                    createDiagnostic = RegionErrors.TransitionFailed(
                        "Participant candidate creation threw: " +
                        exception.Message);
                }
                finally
                {
                    ExitParticipantCallback();
                }

                if (candidate != null &&
                    !ownedCandidates.Add(candidate))
                {
                    if (definition.Requirement ==
                        RegionParticipantRequirement.Optional)
                    {
                        optionalDegraded = true;
                        continue;
                    }

                    CoCoDiagnostic aliasFailure =
                        RegionErrors.CatalogConflict(
                            "Participant factory returned a candidate instance already owned by the Map runtime. Candidate instances must be unique per node generation.");
                    return await CleanupAttemptAfterFailureAsync(
                        state,
                        resolution,
                        candidates,
                        aliasFailure,
                        optionalDegraded,
                        cancellationToken);
                }

                OwnedNode owned = null;
                if (candidate != null)
                {
                    owned = new OwnedNode(
                        NextOwnershipSequence(),
                        definition,
                        desiredNode.Variant,
                        candidate);
                    candidates.Add(owned);
                    state.ActiveOwned.Add(owned);
                    state.OldPlusCandidatePeak = Math.Max(
                        state.OldPlusCandidatePeak,
                        CountUniqueOwnedNodes(
                            state.Committed.Values,
                            state.ActiveOwned,
                            state.FaultOwned));
                    PublishProgress(
                        state,
                        resolution.DesiredGeneration,
                        reusedCount,
                        state.ActiveOwned.Count,
                        optionalDegraded,
                        false,
                        false,
                        CoCoDiagnostic.None);
                }

                bool exactCandidateType =
                    candidate != null &&
                    candidate.GetType() ==
                    registration.CandidateType;
                if (!created ||
                    candidate == null ||
                    !exactCandidateType)
                {
                    CoCoDiagnostic failure = candidate != null &&
                                             !exactCandidateType
                        ? RegionErrors.CatalogConflict(
                            "Participant factory returned candidate type '" +
                            candidate.GetType().AssemblyQualifiedName +
                            "' instead of its exact registered type '" +
                            registration.CandidateType
                                .AssemblyQualifiedName + "'.")
                        : createDiagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "Participant candidate creation failed without a diagnostic.")
                            : createDiagnostic;
                    if (definition.Requirement ==
                        RegionParticipantRequirement.Optional)
                    {
                        optionalDegraded = true;
                        AttemptOutcome? optionalOutcome =
                            await CleanupOptionalCandidateAfterFailureAsync(
                                state,
                                resolution,
                                candidates,
                                owned,
                                optionalDegraded);
                        if (optionalOutcome.HasValue)
                        {
                            return optionalOutcome.Value;
                        }

                        continue;
                    }

                    return await CleanupAttemptAfterFailureAsync(
                        state,
                        resolution,
                        candidates,
                        failure,
                        optionalDegraded,
                        cancellationToken);
                }

                RegionParticipantPrepareResult prepareResult;
                try
                {
                    var prepareContext =
                        new RegionParticipantPrepareContext(
                            definition.Id,
                            desiredNode.TierId,
                            desiredNode.Capabilities,
                            resolution.DesiredGeneration,
                            attempt.CreateResolver(definition.Id));
                    UniTask<RegionParticipantPrepareResult> prepareTask;
                    EnterParticipantCallback();
                    try
                    {
                        prepareTask = candidate.PrepareAsync(
                            prepareContext,
                            cancellationToken);
                    }
                    finally
                    {
                        ExitParticipantCallback();
                    }

                    prepareResult = await prepareTask;
                    await UniTask.SwitchToMainThread();
                }
                catch (Exception exception)
                {
                    await UniTask.SwitchToMainThread();
                    prepareResult =
                        RegionParticipantPrepareResult.Failure(
                            RegionErrors.TransitionFailed(
                                "Participant Prepare threw: " +
                                exception.Message));
                }

                if (IsSuperseded(
                        state,
                        resolution,
                        cancellationToken))
                {
                    return await CleanupCancelledAttemptAsync(
                        state,
                        resolution,
                        candidates,
                        optionalDegraded);
                }

                if (!prepareResult.Succeeded)
                {
                    CoCoDiagnostic failure =
                        prepareResult.Diagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "Participant Prepare failed without a diagnostic.")
                            : prepareResult.Diagnostic;
                    if (definition.Requirement ==
                        RegionParticipantRequirement.Optional)
                    {
                        optionalDegraded = true;
                        AttemptOutcome? optionalOutcome =
                            await CleanupOptionalCandidateAfterFailureAsync(
                                state,
                                resolution,
                                candidates,
                                owned,
                                optionalDegraded);
                        if (optionalOutcome.HasValue)
                        {
                            return optionalOutcome.Value;
                        }

                        continue;
                    }

                    return await CleanupAttemptAfterFailureAsync(
                        state,
                        resolution,
                        candidates,
                        failure,
                        optionalDegraded,
                        cancellationToken);
                }

                nextCommitted.Add(definition.Id, owned);
                available.Add(definition.Id);
            }

            if (IsSuperseded(
                    state,
                    resolution,
                    cancellationToken))
            {
                return await CleanupCancelledAttemptAsync(
                    state,
                    resolution,
                    candidates,
                    optionalDegraded);
            }

            for (int index = 0; index < candidates.Count; index++)
            {
                OwnedNode owned = candidates[index];
                bool committed;
                CoCoDiagnostic commitDiagnostic;
                var commitContext =
                    new RegionParticipantCommitContext(
                        owned.Definition.Id,
                        owned.TierId,
                        owned.Capabilities,
                        resolution.DesiredGeneration);
                EnterParticipantCallback();
                try
                {
                    committed = owned.Candidate.TryCommit(
                        commitContext,
                        out commitDiagnostic);
                }
                catch (Exception exception)
                {
                    committed = false;
                    commitDiagnostic = RegionErrors.CommitFaulted(
                        "Participant CommitNoFail threw: " +
                        exception.Message);
                }
                finally
                {
                    ExitParticipantCallback();
                }

                if (!committed)
                {
                    CoCoDiagnostic fault = commitDiagnostic.IsNone
                        ? RegionErrors.CommitFaulted(
                            "Participant CommitNoFail failed without a diagnostic.")
                        : commitDiagnostic.Code ==
                          CoCoDiagnosticCode.RegionCommitFaulted
                            ? commitDiagnostic
                            : RegionErrors.CommitFaulted(
                                commitDiagnostic.Message);
                    EnterCommitFault(
                        state,
                        resolution,
                        candidates,
                        reusedCount,
                        optionalDegraded,
                        fault);
                    return AttemptOutcome.FaultedCommit;
                }
            }

            var retired = new List<OwnedNode>();
            for (int nodeIndex = state.Plan.Nodes.Count - 1;
                 nodeIndex >= 0;
                 nodeIndex--)
            {
                RegionPlanNodeId nodeId =
                    state.Plan.Nodes[nodeIndex].Id;
                if (state.Committed.TryGetValue(
                        nodeId,
                        out OwnedNode oldNode) &&
                    (!nextCommitted.TryGetValue(
                         nodeId,
                         out OwnedNode nextNode) ||
                     !ReferenceEquals(
                         oldNode.Candidate,
                         nextNode.Candidate)))
                {
                    retired.Add(oldNode);
                }
            }

            state.Committed.Clear();
            foreach (KeyValuePair<RegionPlanNodeId, OwnedNode> pair in
                     nextCommitted)
            {
                state.Committed.Add(pair.Key, pair.Value);
            }

            for (int index = 0; index < candidates.Count; index++)
            {
                state.ActiveOwned.Remove(candidates[index]);
            }

            state.ReusedNodeCount = reusedCount;
            state.OptionalDegraded = optionalDegraded;
            state.RequiresRetry = false;
            state.LastDiagnostic = CoCoDiagnostic.None;

            CleanupBatch retiredBatch =
                CreateRetiredCleanupBatch(retired, nextCommitted);
            CleanupBatchResult retiredResult =
                retiredBatch.IsComplete
                    ? new CleanupBatchResult(
                        true,
                        CoCoDiagnostic.None)
                    : await ProcessCleanupBatchAsync(
                        state,
                        retiredBatch);
            if (!retiredResult.Succeeded)
            {
                await UniTask.SwitchToMainThread();
                BlockCleanup(
                    state,
                    resolution,
                    retiredBatch,
                    BlockedContinuation.PublishReady,
                    optionalDegraded,
                    retiredResult.Diagnostic);
                return AttemptOutcome.BlockedCleanup;
            }

            await UniTask.SwitchToMainThread();
            CommitDependencies(
                state,
                state.ActiveDependencyAttempt);
            PublishReady(
                state,
                resolution,
                reusedCount,
                optionalDegraded);
            return AttemptOutcome.Ready;
        }

        private async UniTask<AttemptOutcome>
            CleanupAttemptAfterFailureAsync(
                RegionState state,
                RegionDemandResolution resolution,
                IList<OwnedNode> candidates,
                CoCoDiagnostic failure,
                bool optionalDegraded,
                CancellationToken cancellationToken)
        {
            CleanupBatch batch = CreateCandidateCleanupBatch(
                candidates,
                RegionParticipantCleanupReason.CandidateFailed);
            CleanupBatchResult cleanupResult =
                batch.IsComplete
                    ? new CleanupBatchResult(
                        true,
                        CoCoDiagnostic.None)
                    : await ProcessCleanupBatchAsync(state, batch);
            if (!cleanupResult.Succeeded)
            {
                await UniTask.SwitchToMainThread();
                BlockCleanup(
                    state,
                    resolution,
                    batch,
                    BlockedContinuation.Rerun,
                    optionalDegraded,
                    cleanupResult.Diagnostic);
                return AttemptOutcome.BlockedCleanup;
            }

            await UniTask.SwitchToMainThread();
            ReleaseActiveDependencies(state);
            if (IsSuperseded(
                    state,
                    resolution,
                    cancellationToken))
            {
                return AttemptOutcome.Cancelled;
            }

            state.RequiresRetry = true;
            state.LastDiagnostic = failure;
            PublishFailureAndProgress(
                state,
                resolution.DesiredGeneration,
                failure,
                false,
                false,
                0);
            return AttemptOutcome.Failed;
        }

        private async UniTask<AttemptOutcome>
            CleanupCancelledAttemptAsync(
                RegionState state,
                RegionDemandResolution resolution,
                IList<OwnedNode> candidates,
                bool optionalDegraded)
        {
            CleanupBatch batch = CreateCandidateCleanupBatch(
                candidates,
                RegionParticipantCleanupReason.CandidateCancelled);
            CleanupBatchResult cleanupResult =
                batch.IsComplete
                    ? new CleanupBatchResult(
                        true,
                        CoCoDiagnostic.None)
                    : await ProcessCleanupBatchAsync(state, batch);
            if (!cleanupResult.Succeeded)
            {
                await UniTask.SwitchToMainThread();
                BlockCleanup(
                    state,
                    resolution,
                    batch,
                    BlockedContinuation.Rerun,
                    optionalDegraded,
                    cleanupResult.Diagnostic);
                return AttemptOutcome.BlockedCleanup;
            }

            await UniTask.SwitchToMainThread();
            ReleaseActiveDependencies(state);
            return AttemptOutcome.Cancelled;
        }

        private async UniTask<AttemptOutcome?>
            CleanupOptionalCandidateAfterFailureAsync(
                RegionState state,
                RegionDemandResolution resolution,
                IList<OwnedNode> candidates,
                OwnedNode owned,
                bool optionalDegraded)
        {
            if (owned == null) return null;

            CleanupBatch optionalBatch = new CleanupBatch(
                new[]
                {
                    new CleanupWork(
                        owned,
                        RegionParticipantCleanupReason.CandidateFailed)
                });
            CleanupBatchResult cleanupResult =
                await ProcessCleanupBatchAsync(state, optionalBatch);
            if (!cleanupResult.Succeeded)
            {
                await UniTask.SwitchToMainThread();
                CleanupBatch allCandidates =
                    CreateCandidateCleanupBatch(
                        candidates,
                        RegionParticipantCleanupReason.CandidateFailed);
                CleanupWork exactWork =
                    optionalBatch.Works[optionalBatch.Index];
                for (int index = allCandidates.Index;
                     index < allCandidates.Works.Count;
                     index++)
                {
                    if (!ReferenceEquals(
                            allCandidates.Works[index].Node,
                            owned))
                    {
                        continue;
                    }

                    // Continue observing the exact cleanup invocation that
                    // timed out instead of invoking Cleanup twice.
                    allCandidates.Works[index] = exactWork;
                    break;
                }

                BlockCleanup(
                    state,
                    resolution,
                    allCandidates,
                    BlockedContinuation.Rerun,
                    optionalDegraded,
                    cleanupResult.Diagnostic);
                return AttemptOutcome.BlockedCleanup;
            }

            await UniTask.SwitchToMainThread();
            candidates.Remove(owned);
            return null;
        }

        private void EnterCommitFault(
            RegionState state,
            RegionDemandResolution resolution,
            IList<OwnedNode> candidates,
            int reusedCount,
            bool optionalDegraded,
            CoCoDiagnostic fault)
        {
            RetainFaultDependencies(state);
            for (int index = 0; index < candidates.Count; index++)
            {
                OwnedNode candidate = candidates[index];
                state.ActiveOwned.Remove(candidate);
                if (!state.FaultOwned.Contains(candidate))
                {
                    state.FaultOwned.Add(candidate);
                }
            }

            state.FaultedCommit = true;
            state.RequiresRetry = false;
            state.ReusedNodeCount = reusedCount;
            state.OptionalDegraded = optionalDegraded;
            state.LastDiagnostic = fault;
            PublishFailureAndProgress(
                state,
                resolution.DesiredGeneration,
                fault,
                true,
                false,
                state.FaultOwned.Count);
        }

        private async UniTask<CleanupBatchResult> ProcessCleanupBatchAsync(
            RegionState state,
            CleanupBatch batch)
        {
            CleanupBatch previous = state.ActiveCleanupBatch;
            state.ActiveCleanupBatch = batch;
            try
            {
                while (!batch.IsComplete)
                {
                    CleanupWork work = batch.Works[batch.Index];
                    if (work.Completed)
                    {
                        batch.Index++;
                        continue;
                    }

                    if (!work.FailureDiagnostic.IsNone)
                    {
                        return new CleanupBatchResult(
                            false,
                            work.FailureDiagnostic);
                    }

                    if (!work.InvocationAttempted)
                    {
                        work.InvocationAttempted = true;
                        try
                        {
                            UniTask<RegionParticipantCleanupResult> cleanup;
                            EnterParticipantCallback();
                            try
                            {
                                cleanup = work.Node.Candidate.CleanupAsync(
                                    work.Reason,
                                    CancellationToken.None);
                            }
                            finally
                            {
                                ExitParticipantCallback();
                            }

                            work.InFlight = cleanup.AsTask();
                        }
                        catch (Exception exception)
                        {
                            work.FailureDiagnostic =
                                RegionErrors.CleanupBlocked(
                                    "Participant cleanup invocation threw: " +
                                    exception.Message);
                            return new CleanupBatchResult(
                                false,
                                work.FailureDiagnostic);
                        }
                    }

                    if (work.InFlight == null)
                    {
                        work.FailureDiagnostic =
                            RegionErrors.CleanupBlocked(
                                "Participant cleanup did not return an observable operation.");
                        return new CleanupBatchResult(
                            false,
                            work.FailureDiagnostic);
                    }

                    Task timeoutTask = Task.Delay(cleanupTimeout);
                    Task completed = await Task.WhenAny(
                        work.InFlight,
                        timeoutTask);
                    await UniTask.SwitchToMainThread();
                    if (!ReferenceEquals(completed, work.InFlight))
                    {
                        return new CleanupBatchResult(
                            false,
                            RegionErrors.CleanupBlocked(
                                "Participant cleanup exceeded the " +
                                cleanupTimeout.TotalSeconds +
                                "-second unscaled timeout."));
                    }

                    RegionParticipantCleanupResult result;
                    try
                    {
                        result = await work.InFlight;
                        await UniTask.SwitchToMainThread();
                    }
                    catch (Exception exception)
                    {
                        await UniTask.SwitchToMainThread();
                        work.FailureDiagnostic =
                            RegionErrors.CleanupBlocked(
                                "Participant cleanup threw: " +
                                exception.Message);
                        return new CleanupBatchResult(
                            false,
                            work.FailureDiagnostic);
                    }

                    if (!result.Succeeded)
                    {
                        work.FailureDiagnostic =
                            result.Diagnostic.IsNone
                                ? RegionErrors.CleanupBlocked(
                                    "Participant cleanup failed without a diagnostic.")
                                : result.Diagnostic.Code ==
                                  CoCoDiagnosticCode.RegionCleanupBlocked
                                    ? result.Diagnostic
                                    : RegionErrors.CleanupBlocked(
                                        result.Diagnostic.Message);
                        return new CleanupBatchResult(
                            false,
                            work.FailureDiagnostic);
                    }

                    work.Completed = true;
                    ownedCandidates.Remove(work.Node.Candidate);
                    state.ActiveOwned.Remove(work.Node);
                    state.FaultOwned.Remove(work.Node);
                    if (state.Committed.TryGetValue(
                            work.Node.Definition.Id,
                            out OwnedNode committed) &&
                        ReferenceEquals(committed, work.Node))
                    {
                        state.Committed.Remove(work.Node.Definition.Id);
                    }

                    batch.Index++;
                }

                return new CleanupBatchResult(
                    true,
                    CoCoDiagnostic.None);
            }
            finally
            {
                if (ReferenceEquals(
                        state.ActiveCleanupBatch,
                        batch))
                {
                    state.ActiveCleanupBatch = previous;
                }
            }
        }

        private void BlockCleanup(
            RegionState state,
            RegionDemandResolution resolution,
            CleanupBatch batch,
            BlockedContinuation continuation,
            bool optionalDegraded,
            CoCoDiagnostic diagnostic)
        {
            CoCoDiagnostic blocked = diagnostic.IsNone
                ? RegionErrors.CleanupBlocked(
                    "Region cleanup is blocked.")
                : diagnostic.Code ==
                  CoCoDiagnosticCode.RegionCleanupBlocked
                    ? diagnostic
                    : RegionErrors.CleanupBlocked(
                        diagnostic.Message);
            DependencyAttempt dependencies =
                state.ActiveDependencyAttempt;
            state.ActiveDependencyAttempt = null;
            state.Blocked = new BlockedState(
                batch,
                resolution,
                continuation,
                optionalDegraded,
                blocked,
                dependencies);
            state.RequiresRetry = false;
            state.LastDiagnostic = blocked;
            long generation = state.PendingResolution == null
                ? resolution.DesiredGeneration
                : state.PendingResolution.DesiredGeneration;
            PublishFailureAndProgress(
                state,
                generation,
                blocked,
                false,
                true,
                batch.RemainingCount);
        }

        private void StartBlockedRetry(RegionState state)
        {
            if (state.RunnerActive || state.Blocked == null) return;

            state.RunnerActive = true;
            state.RunnerTask = RetryBlockedCleanupAsync(state).AsTask();
        }

        private async UniTask RetryBlockedCleanupAsync(
            RegionState state)
        {
            BlockedState blocked = state.Blocked;
            try
            {
                CleanupBatchResult cleanupResult =
                    await ProcessCleanupBatchAsync(
                        state,
                        blocked.Batch);
                if (!cleanupResult.Succeeded)
                {
                    await UniTask.SwitchToMainThread();
                    state.LastDiagnostic =
                        cleanupResult.Diagnostic;
                    long generation =
                        state.PendingResolution == null
                            ? blocked.Resolution.DesiredGeneration
                            : state.PendingResolution
                                .DesiredGeneration;
                    PublishFailureAndProgress(
                        state,
                        generation,
                        cleanupResult.Diagnostic,
                        false,
                        true,
                        blocked.Batch.RemainingCount);
                    return;
                }

                await UniTask.SwitchToMainThread();
                state.Blocked = null;
                state.LastDiagnostic = CoCoDiagnostic.None;

                if (blocked.Continuation ==
                    BlockedContinuation.PublishReady)
                {
                    CommitDependencies(
                        state,
                        blocked.Dependencies);
                }
                else
                {
                    ReleaseDependencyAttempt(
                        blocked.Dependencies);
                }

                if (state.PendingResolution != null)
                {
                    return;
                }

                if (blocked.Continuation ==
                    BlockedContinuation.PublishReady)
                {
                    PublishReady(
                        state,
                        blocked.Resolution,
                        state.ReusedNodeCount,
                        blocked.OptionalDegraded);
                }
                else
                {
                    state.PendingResolution = blocked.Resolution;
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                state.RunnerActive = false;
                if (state.PendingResolution != null &&
                    state.Blocked == null &&
                    !state.FaultedCommit &&
                    !shuttingDown &&
                    !disposed)
                {
                    EnsureRunner(state);
                }
            }
        }

        private async UniTask<CoCoDiagnostic> ShutdownCoreAsync()
        {
            if (disposed) return CoCoDiagnostic.None;
            if (!RegionMainThreadGuard.IsMainThread)
            {
                return RegionErrors.MainThreadRequired();
            }

            shuttingDown = true;
            CoCoDiagnostic firstFailure = CoCoDiagnostic.None;
            var terminalCleanupExclusions =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            var lateShutdownCleanups =
                new Dictionary<
                    IRegionParticipantCandidate,
                    Task<RegionParticipantCleanupResult>>(
                    ReferenceCandidateComparer.Instance);
            IReadOnlyList<RegionState> shutdownOrder =
                BuildSourceFirstShutdownOrder();
            for (int stateIndex = 0;
                 stateIndex < shutdownOrder.Count;
                 stateIndex++)
            {
                RegionState state = shutdownOrder[stateIndex];
                state.ActiveCancellation?.Cancel();
                if (state.RunnerTask != null &&
                    !state.RunnerTask.IsCompleted)
                {
                    Task runner = state.RunnerTask;
                    Task completed = await Task.WhenAny(
                        runner,
                        Task.Delay(cleanupTimeout));
                    await UniTask.SwitchToMainThread();
                    if (!ReferenceEquals(completed, runner))
                    {
                        CoCoDiagnostic timeout =
                            RegionErrors.CleanupBlocked(
                                "Map shutdown timed out while cancelling source Region '" +
                                state.Plan.RegionId.Value +
                                "' before its dependency targets.");
                        ForceShutdown(
                            terminalCleanupExclusions,
                            lateShutdownCleanups);
                        return timeout;
                    }

                    try
                    {
                        await runner;
                        await UniTask.SwitchToMainThread();
                    }
                    catch (Exception exception)
                    {
                        await UniTask.SwitchToMainThread();
                        if (firstFailure.IsNone)
                        {
                            firstFailure =
                                RegionErrors.CleanupBlocked(
                                    "A Region transition runner faulted during shutdown: " +
                                    exception.Message);
                        }
                    }
                }

                HashSet<IRegionParticipantCandidate>
                    blockedCleanupCandidates = null;
                bool blockedCleanupSucceeded = true;
                if (state.Blocked != null)
                {
                    CleanupBatchResult blockedResult =
                        await ProcessCleanupBatchAsync(
                            state,
                            state.Blocked.Batch);
                    AddCompletedCleanupCandidates(
                        state.Blocked.Batch,
                        terminalCleanupExclusions);
                    if (!blockedResult.Succeeded &&
                        firstFailure.IsNone)
                    {
                        firstFailure =
                            blockedResult.Diagnostic;
                    }

                    await UniTask.SwitchToMainThread();
                    if (!blockedResult.Succeeded)
                    {
                        blockedCleanupSucceeded = false;
                        blockedCleanupCandidates =
                            CollectRemainingCleanupCandidates(
                                state.Blocked.Batch);
                    }
                }

                CleanupBatch shutdownBatch =
                    CreateShutdownCleanupBatch(
                        state,
                        blockedCleanupCandidates);
                CleanupBatchResult shutdownResult =
                    shutdownBatch.IsComplete
                        ? new CleanupBatchResult(
                            true,
                            CoCoDiagnostic.None)
                    : await ProcessCleanupBatchAsync(
                        state,
                        shutdownBatch);
                AddCompletedCleanupCandidates(
                    shutdownBatch,
                    terminalCleanupExclusions);
                CollectObservableCleanupTasks(
                    shutdownBatch,
                    lateShutdownCleanups);
                if (!shutdownResult.Succeeded &&
                    firstFailure.IsNone)
                {
                    firstFailure =
                        shutdownResult.Diagnostic;
                }

                await UniTask.SwitchToMainThread();
                if (blockedCleanupSucceeded &&
                    shutdownResult.Succeeded)
                {
                    DisposeStateDependencies(state);
                    state.Blocked = null;
                }
            }

            if (!firstFailure.IsNone)
            {
                CoCoDiagnostic blocked =
                    firstFailure.Code ==
                    CoCoDiagnosticCode.RegionCleanupBlocked
                        ? firstFailure
                        : RegionErrors.CleanupBlocked(
                            firstFailure.Message);
                ForceShutdown(
                    terminalCleanupExclusions,
                    lateShutdownCleanups);
                return blocked;
            }

            states.Clear();
            disposed = true;
            return CoCoDiagnostic.None;
        }

        private void ForceCleanupState(
            RegionState state,
            ISet<IRegionParticipantCandidate> excluded)
        {
            var unique =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            var blockedInFlight =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            if (excluded != null)
            {
                foreach (IRegionParticipantCandidate candidate in excluded)
                {
                    if (candidate != null)
                    {
                        blockedInFlight.Add(candidate);
                    }
                }
            }

            if (state.Blocked != null)
            {
                for (int index = state.Blocked.Batch.Index;
                     index < state.Blocked.Batch.Works.Count;
                     index++)
                {
                    CleanupWork work =
                        state.Blocked.Batch.Works[index];
                    if (work.InFlight != null &&
                        !work.InFlight.IsCompleted)
                    {
                        blockedInFlight.Add(work.Node.Candidate);
                    }
                }
            }

            for (int index = state.FaultOwned.Count - 1;
                 index >= 0;
                 index--)
            {
                ForceCleanupCandidate(
                    state.FaultOwned[index].Candidate,
                    unique,
                    blockedInFlight);
            }

            for (int index = state.ActiveOwned.Count - 1;
                 index >= 0;
                 index--)
            {
                ForceCleanupCandidate(
                    state.ActiveOwned[index].Candidate,
                    unique,
                    blockedInFlight);
            }

            for (int nodeIndex = state.Plan.Nodes.Count - 1;
                 nodeIndex >= 0;
                 nodeIndex--)
            {
                RegionPlanNodeId nodeId =
                    state.Plan.Nodes[nodeIndex].Id;
                if (state.Committed.TryGetValue(
                        nodeId,
                        out OwnedNode committed))
                {
                    ForceCleanupCandidate(
                        committed.Candidate,
                        unique,
                        blockedInFlight);
                }
            }

            if (state.Blocked != null)
            {
                for (int index = state.Blocked.Batch.Index;
                     index < state.Blocked.Batch.Works.Count;
                     index++)
                {
                    ForceCleanupCandidate(
                        state.Blocked.Batch.Works[index]
                            .Node.Candidate,
                        unique,
                        blockedInFlight);
                }
            }

            state.Committed.Clear();
            state.ActiveOwned.Clear();
            state.FaultOwned.Clear();
            DisposeStateDependencies(state);
            state.ActiveCleanupBatch = null;
            state.ReusedNodeIds.Clear();
            state.Blocked = null;
        }

        private static void DisposeStateDependencies(
            RegionState state)
        {
            ReleaseActiveDependencies(state);
            if (state.Blocked != null)
            {
                ReleaseDependencyAttempt(
                    state.Blocked.Dependencies);
            }

            foreach (
                DependencyLeaseEntry entry
                in state.CommittedDependencies.Values)
            {
                DisposeDependencyNoThrow(entry);
            }

            for (int index =
                     state.FaultDependencies.Count - 1;
                 index >= 0;
                 index--)
            {
                DisposeDependencyNoThrow(
                    state.FaultDependencies[index]);
            }

            state.CommittedDependencies.Clear();
            state.FaultDependencies.Clear();
        }

        private IReadOnlyList<RegionState>
            BuildSourceFirstShutdownOrder()
        {
            var adjacency =
                new Dictionary<RegionId, HashSet<RegionId>>();
            var indegree =
                new Dictionary<RegionId, int>();
            foreach (
                KeyValuePair<RegionId, RegionState> pair
                in states)
            {
                adjacency.Add(
                    pair.Key,
                    new HashSet<RegionId>());
                indegree.Add(pair.Key, 0);
            }

            foreach (
                KeyValuePair<RegionId, RegionState> pair
                in states)
            {
                for (int ruleIndex = 0;
                     ruleIndex <
                     pair.Value.Plan.DependencyRules.Count;
                     ruleIndex++)
                {
                    RegionId target =
                        pair.Value.Plan.DependencyRules[ruleIndex]
                            .TargetRegionId;
                    if (!indegree.ContainsKey(target) ||
                        !adjacency[pair.Key].Add(target))
                    {
                        continue;
                    }

                    indegree[target] = indegree[target] + 1;
                }
            }

            var ready = new List<RegionId>();
            foreach (
                KeyValuePair<RegionId, int> pair
                in indegree)
            {
                if (pair.Value == 0)
                {
                    ready.Add(pair.Key);
                }
            }

            ready.Sort(CompareRegionIds);
            var ordered = new List<RegionState>(states.Count);
            while (ready.Count > 0)
            {
                RegionId source = ready[0];
                ready.RemoveAt(0);
                ordered.Add(states[source]);

                var targets =
                    new List<RegionId>(adjacency[source]);
                targets.Sort(CompareRegionIds);
                for (int index = 0;
                     index < targets.Count;
                     index++)
                {
                    RegionId target = targets[index];
                    int next = indegree[target] - 1;
                    indegree[target] = next;
                    if (next == 0)
                    {
                        ready.Add(target);
                        ready.Sort(CompareRegionIds);
                    }
                }
            }

            if (ordered.Count != states.Count)
            {
                var remaining = new List<RegionState>();
                foreach (RegionState state in states.Values)
                {
                    if (!ordered.Contains(state))
                    {
                        remaining.Add(state);
                    }
                }

                remaining.Sort(
                    (left, right) => CompareRegionIds(
                        left.Plan.RegionId,
                        right.Plan.RegionId));
                ordered.AddRange(remaining);
            }

            return ordered;
        }

        private static int CompareRegionIds(
            RegionId left,
            RegionId right) =>
            string.CompareOrdinal(
                left.Value,
                right.Value);

        private void ForceCleanupCandidate(
            IRegionParticipantCandidate candidate,
            ISet<IRegionParticipantCandidate> unique,
            ISet<IRegionParticipantCandidate> excluded = null)
        {
            if (candidate == null ||
                excluded != null &&
                excluded.Contains(candidate) ||
                !unique.Add(candidate))
            {
                return;
            }
            try
            {
                if (candidate is IRegionParticipantTerminalCleanup terminal)
                {
                    EnterParticipantCallback();
                    try
                    {
                        terminal.ForceCleanupNoFail();
                    }
                    finally
                    {
                        ExitParticipantCallback();
                    }
                }
            }
            catch
            {
                // Terminal Host shutdown cannot safely recover further.
            }
            finally
            {
                ownedCandidates.Remove(candidate);
            }
        }

        private async UniTask<DependencyPrepareResult>
            PrepareDependenciesAsync(
                RegionState state,
                RegionDemandResolution resolution,
                DependencyAttempt attempt,
                CancellationToken cancellationToken)
        {
            if (dependencyScope == null ||
                dependencyScope.IsDisposed)
            {
                return new DependencyPrepareResult(
                    false,
                    RegionErrors.RuntimeDisposed());
            }

            RegionCapabilitySet sourceEffective =
                RegionCapabilitySet.Empty;
            if (state.Plan.TryResolveTier(
                    resolution.RegionCapabilities,
                    out RegionCompiledTier sourceTier))
            {
                sourceEffective = sourceTier.Capabilities;
            }

            for (int index = 0;
                 index < state.Plan.DependencyRules.Count;
                 index++)
            {
                RegionCompiledDependencyRule rule =
                    state.Plan.DependencyRules[index];
                if (!rule.IsActiveFor(sourceEffective))
                {
                    continue;
                }

                if (state.CommittedDependencies.TryGetValue(
                        rule.Fingerprint,
                        out DependencyLeaseEntry committed) &&
                    committed.Lease != null &&
                    !committed.Lease.IsDisposed)
                {
                    attempt.Next.Add(
                        rule.Fingerprint,
                        committed);
                    continue;
                }

                if (!dependencyScope.TryDemand(
                        rule.TargetRegionId,
                        rule.TargetCapabilities,
                        rule.TargetCoverage,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic))
                {
                    return new DependencyPrepareResult(
                        false,
                        diagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "A cross-Region dependency Demand could not be created.")
                            : diagnostic);
                }

                var entry = new DependencyLeaseEntry(
                    rule,
                    lease,
                    revision);
                attempt.Created.Add(entry);
                attempt.Next.Add(
                    rule.Fingerprint,
                    entry);
            }

            for (int index = 0;
                 index < attempt.Created.Count;
                 index++)
            {
                DependencyLeaseEntry entry =
                    attempt.Created[index];
                RegionReadinessResult readiness;
                try
                {
                    readiness =
                        await entry.Lease.WaitUntilReadyAsync(
                            entry.Revision,
                            cancellationToken);
                    await UniTask.SwitchToMainThread();
                }
                catch (Exception exception)
                {
                    await UniTask.SwitchToMainThread();
                    CoCoDiagnostic waitFailure =
                        RegionErrors.TransitionFailed(
                            "Cross-Region dependency readiness threw: " +
                            exception.Message);
                    entry.Diagnostic = waitFailure;
                    return new DependencyPrepareResult(
                        false,
                        waitFailure);
                }

                entry.Readiness = readiness.Status;
                entry.Diagnostic = readiness.Diagnostic;
                if (readiness.Status ==
                    RegionReadinessStatus.Ready)
                {
                    continue;
                }

                string targetDetail =
                    readiness.Diagnostic.IsNone
                        ? readiness.Status.ToString()
                        : readiness.Diagnostic.Domain + "/" +
                          readiness.Diagnostic.Code + ": " +
                          readiness.Diagnostic.Message;
                CoCoDiagnostic failure =
                    RegionErrors.TransitionFailed(
                        "Target Region '" +
                        entry.Rule.TargetRegionId.Value +
                        "' did not become Ready for source Region '" +
                        state.Plan.RegionId.Value +
                        "' through dependency rule '" +
                        entry.Rule.Fingerprint +
                        "' (" + targetDetail + ").");
                return new DependencyPrepareResult(
                    false,
                    failure);
            }

            return new DependencyPrepareResult(
                true,
                CoCoDiagnostic.None);
        }

        private static void CommitDependencies(
            RegionState state,
            DependencyAttempt attempt)
        {
            if (attempt == null)
            {
                state.ActiveDependencyAttempt = null;
                return;
            }

            foreach (
                KeyValuePair<string, DependencyLeaseEntry> pair
                in state.CommittedDependencies)
            {
                if (attempt.Next.TryGetValue(
                        pair.Key,
                        out DependencyLeaseEntry next) &&
                    ReferenceEquals(
                        pair.Value,
                        next))
                {
                    continue;
                }

                DisposeDependencyNoThrow(pair.Value);
            }

            state.CommittedDependencies.Clear();
            foreach (
                KeyValuePair<string, DependencyLeaseEntry> pair
                in attempt.Next)
            {
                state.CommittedDependencies.Add(
                    pair.Key,
                    pair.Value);
            }

            attempt.Created.Clear();
            state.ActiveDependencyAttempt = null;
        }

        private static void ReleaseActiveDependencies(
            RegionState state)
        {
            DependencyAttempt attempt =
                state.ActiveDependencyAttempt;
            state.ActiveDependencyAttempt = null;
            ReleaseDependencyAttempt(attempt);
        }

        private static void ReleaseDependencyAttempt(
            DependencyAttempt attempt)
        {
            if (attempt == null) return;

            for (int index = attempt.Created.Count - 1;
                 index >= 0;
                 index--)
            {
                DisposeDependencyNoThrow(
                    attempt.Created[index]);
            }

            attempt.Created.Clear();
            attempt.Next.Clear();
        }

        private static void RetainFaultDependencies(
            RegionState state)
        {
            DependencyAttempt attempt =
                state.ActiveDependencyAttempt;
            state.ActiveDependencyAttempt = null;
            if (attempt == null) return;

            for (int index = 0;
                 index < attempt.Created.Count;
                 index++)
            {
                state.FaultDependencies.Add(
                    attempt.Created[index]);
            }

            attempt.Created.Clear();
            attempt.Next.Clear();
        }

        private static void DisposeDependencyNoThrow(
            DependencyLeaseEntry entry)
        {
            if (entry == null ||
                entry.Lease == null ||
                entry.Lease.IsDisposed)
            {
                return;
            }

            try
            {
                entry.Lease.Dispose();
            }
            catch
            {
                // Dependency ownership is best-effort only after the source
                // attempt has already reached a terminal path.
            }
        }

        private static List<RegionParticipantMonitorSnapshot>
            CaptureParticipantSnapshots(RegionState state)
        {
            var byOwnership =
                new Dictionary<
                    long,
                    RegionParticipantMonitorSnapshot>();
            foreach (OwnedNode node in state.Committed.Values)
            {
                RegionMonitorParticipantRole role =
                    state.ReusedNodeIds.Contains(
                        node.Definition.Id)
                        ? RegionMonitorParticipantRole.Reused
                        : RegionMonitorParticipantRole.Committed;
                byOwnership[node.OwnershipSequence] =
                    CreateParticipantMonitorSnapshot(
                        node,
                        role,
                        null);
            }

            for (int index = 0;
                 index < state.ActiveOwned.Count;
                 index++)
            {
                OwnedNode node = state.ActiveOwned[index];
                byOwnership[node.OwnershipSequence] =
                    CreateParticipantMonitorSnapshot(
                        node,
                        RegionMonitorParticipantRole.Candidate,
                        null);
            }

            for (int index = 0;
                 index < state.FaultOwned.Count;
                 index++)
            {
                OwnedNode node = state.FaultOwned[index];
                byOwnership[node.OwnershipSequence] =
                    CreateParticipantMonitorSnapshot(
                        node,
                        RegionMonitorParticipantRole.FaultRetained,
                        null);
            }

            ApplyCleanupMonitorRoles(
                state.ActiveCleanupBatch,
                RegionMonitorParticipantRole.Retiring,
                byOwnership);
            if (state.Blocked != null)
            {
                ApplyCleanupMonitorRoles(
                    state.Blocked.Batch,
                    RegionMonitorParticipantRole.BlockedCleanup,
                    byOwnership);
            }

            var snapshots =
                new List<RegionParticipantMonitorSnapshot>(
                    byOwnership.Values);
            snapshots.Sort(CompareParticipantMonitorSnapshots);
            return snapshots;
        }

        private static void ApplyCleanupMonitorRoles(
            CleanupBatch batch,
            RegionMonitorParticipantRole role,
            IDictionary<long, RegionParticipantMonitorSnapshot>
                snapshots)
        {
            if (batch == null) return;

            for (int index = batch.Index;
                 index < batch.Works.Count;
                 index++)
            {
                CleanupWork work = batch.Works[index];
                if (work == null ||
                    work.Completed ||
                    work.Node == null)
                {
                    continue;
                }

                snapshots[work.Node.OwnershipSequence] =
                    CreateParticipantMonitorSnapshot(
                        work.Node,
                        role,
                        work.Reason);
            }
        }

        private static RegionParticipantMonitorSnapshot
            CreateParticipantMonitorSnapshot(
                OwnedNode node,
                RegionMonitorParticipantRole role,
                RegionParticipantCleanupReason? cleanupReason)
        {
            ContentId contentId =
                node.Definition.SceneReference.ContentId;
            long contentScopeSequence = 0L;
            long contentLeaseSequence = 0L;
            if (node.Candidate is
                IRegionContentMonitorSource content)
            {
                contentId = content.ContentId;
                contentScopeSequence =
                    content.ContentScopeSequence;
                contentLeaseSequence =
                    content.ContentLeaseSequence;
            }

            return new RegionParticipantMonitorSnapshot(
                node.OwnershipSequence,
                node.Definition.Id,
                node.Definition.ParticipantTypeId,
                node.Definition.Phase,
                node.Definition.ExplicitOrder,
                node.Definition.Requirement,
                node.TierId,
                node.ModeId,
                node.Capabilities,
                role,
                cleanupReason,
                contentId,
                contentScopeSequence,
                contentLeaseSequence);
        }

        private static int CompareParticipantMonitorSnapshots(
            RegionParticipantMonitorSnapshot left,
            RegionParticipantMonitorSnapshot right)
        {
            int phase = left.Phase.CompareTo(right.Phase);
            if (phase != 0) return phase;

            int order =
                left.ExplicitOrder.CompareTo(
                    right.ExplicitOrder);
            if (order != 0) return order;

            int node = RegionBindingCompiler.CompareNodeIds(
                left.NodeId,
                right.NodeId);
            if (node != 0) return node;

            int role = left.Role.CompareTo(right.Role);
            return role != 0
                ? role
                : left.OwnershipSequence.CompareTo(
                    right.OwnershipSequence);
        }

        private static List<RegionDependencyMonitorSnapshot>
            CaptureDependencySnapshots(RegionState state)
        {
            var snapshots =
                new Dictionary<long, RegionDependencyMonitorSnapshot>();
            foreach (
                DependencyLeaseEntry entry
                in state.CommittedDependencies.Values)
            {
                AddDependencyMonitorSnapshot(
                    state,
                    entry,
                    RegionMonitorDependencyRole.Committed,
                    false,
                    snapshots);
            }

            AddDependencyAttemptMonitorSnapshots(
                state,
                state.ActiveDependencyAttempt,
                false,
                snapshots);
            if (state.Blocked != null)
            {
                AddDependencyAttemptMonitorSnapshots(
                    state,
                    state.Blocked.Dependencies,
                    true,
                    snapshots);
            }

            for (int index = 0;
                 index < state.FaultDependencies.Count;
                 index++)
            {
                AddDependencyMonitorSnapshot(
                    state,
                    state.FaultDependencies[index],
                    RegionMonitorDependencyRole.FaultRetained,
                    false,
                    snapshots);
            }

            var ordered =
                new List<RegionDependencyMonitorSnapshot>(
                    snapshots.Values);
            ordered.Sort(
                (left, right) =>
                {
                    int fingerprint = string.CompareOrdinal(
                        left.RuleFingerprint,
                        right.RuleFingerprint);
                    return fingerprint != 0
                        ? fingerprint
                        : left.LeaseSequence.CompareTo(
                            right.LeaseSequence);
                });
            return ordered;
        }

        private static void AddDependencyAttemptMonitorSnapshots(
            RegionState state,
            DependencyAttempt attempt,
            bool blocked,
            IDictionary<long, RegionDependencyMonitorSnapshot>
                snapshots)
        {
            if (attempt == null) return;

            foreach (
                DependencyLeaseEntry entry
                in attempt.Next.Values)
            {
                bool created =
                    ContainsDependencyEntry(
                        attempt.Created,
                        entry);
                RegionMonitorDependencyRole role;
                bool blocker = false;
                if (!created)
                {
                    role = RegionMonitorDependencyRole.Reused;
                }
                else if (blocked)
                {
                    role =
                        RegionMonitorDependencyRole.BlockedRetained;
                }
                else if (entry.Readiness ==
                         RegionReadinessStatus.Ready)
                {
                    role =
                        RegionMonitorDependencyRole.CandidateReady;
                }
                else
                {
                    role =
                        RegionMonitorDependencyRole.CandidateWaiting;
                    blocker = true;
                }

                AddDependencyMonitorSnapshot(
                    state,
                    entry,
                    role,
                    blocker,
                    snapshots);
            }
        }

        private static void AddDependencyMonitorSnapshot(
            RegionState state,
            DependencyLeaseEntry entry,
            RegionMonitorDependencyRole role,
            bool blocker,
            IDictionary<long, RegionDependencyMonitorSnapshot>
                snapshots)
        {
            if (entry == null ||
                entry.Rule == null ||
                entry.Lease == null)
            {
                return;
            }

            snapshots[entry.Lease.LeaseSequence] =
                new RegionDependencyMonitorSnapshot(
                    state.Plan.RegionId,
                    entry.Rule.Fingerprint,
                    entry.Rule.SourceCapability,
                    entry.Rule.TargetRegionId,
                    entry.Rule.TargetCapabilities,
                    entry.Rule.TargetCoverage,
                    entry.Lease.LeaseSequence,
                    entry.Revision,
                    entry.Readiness,
                    entry.Diagnostic,
                    role,
                    blocker);
        }

        private static bool ContainsDependencyEntry(
            IList<DependencyLeaseEntry> entries,
            DependencyLeaseEntry expected)
        {
            for (int index = 0;
                 index < entries.Count;
                 index++)
            {
                if (ReferenceEquals(entries[index], expected))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<RegionPlanNodeId, DesiredNode>
            BuildDesiredNodes(
                RegionCompiledPlan plan,
                RegionDemandResolution resolution)
        {
            var desired =
                new Dictionary<RegionPlanNodeId, DesiredNode>();
            if (!resolution.HasDemand) return desired;

            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                RegionCompiledParticipantNode node =
                    plan.Nodes[index];
                RegionCapabilitySet effective =
                    node.Id.HasChunkId
                        ? resolution.GetChunkCapabilities(
                            node.Id.ChunkId)
                        : resolution.RegionCapabilities;
                if (!plan.TryResolveTier(
                        effective,
                        out RegionCompiledTier tier) ||
                    !node.TryGetVariant(
                        tier.TierId,
                        out RegionCompiledParticipantVariant variant))
                {
                    continue;
                }

                desired.Add(
                    node.Id,
                    new DesiredNode(
                        node,
                        variant));
            }

            return desired;
        }

        private void PublishResolvedFidelity(
            RegionState state,
            RegionDemandResolution resolution)
        {
            RegionTierId regionTierId = default;
            RegionCapabilitySet regionEffective =
                RegionCapabilitySet.Empty;
            if (state.Plan.TryResolveTier(
                    resolution.RegionCapabilities,
                    out RegionCompiledTier regionTier))
            {
                regionTierId = regionTier.TierId;
                regionEffective = regionTier.Capabilities;
            }

            var chunks =
                new Dictionary<
                    RegionChunkId,
                    RegionResolvedChunkFidelity>();
            for (int index = 0;
                 index < state.Plan.Chunks.Count;
                 index++)
            {
                RegionChunkId chunkId =
                    state.Plan.Chunks[index].ChunkId;
                RegionCapabilitySet required =
                    resolution.GetChunkCapabilities(chunkId);
                if (!state.Plan.TryResolveTier(
                        required,
                        out RegionCompiledTier tier))
                {
                    continue;
                }

                chunks.Add(
                    chunkId,
                    new RegionResolvedChunkFidelity(
                        tier.TierId,
                        tier.Capabilities));
            }

            runtime.PublishResolvedFidelity(
                state.Plan.RegionId,
                resolution.DesiredGeneration,
                regionTierId,
                regionEffective,
                chunks);
        }

        private static bool DependenciesAvailable(
            RegionCompiledParticipantNode node,
            ISet<RegionPlanNodeId> available)
        {
            for (int index = 0;
                 index < node.Dependencies.Count;
                 index++)
            {
                if (!available.Contains(node.Dependencies[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSuperseded(
            RegionState state,
            RegionDemandResolution resolution,
            CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ||
            state.PendingResolution != null &&
            state.PendingResolution.DesiredGeneration !=
            resolution.DesiredGeneration;

        private static CleanupBatch CreateCandidateCleanupBatch(
            IList<OwnedNode> candidates,
            RegionParticipantCleanupReason reason)
        {
            var works = new List<CleanupWork>(candidates.Count);
            for (int index = candidates.Count - 1;
                 index >= 0;
                 index--)
            {
                works.Add(new CleanupWork(candidates[index], reason));
            }

            return new CleanupBatch(works);
        }

        private static CleanupBatch CreateRetiredCleanupBatch(
            IList<OwnedNode> retired,
            IReadOnlyDictionary<RegionPlanNodeId, OwnedNode>
                nextCommitted)
        {
            var works = new List<CleanupWork>(retired.Count);
            for (int index = 0; index < retired.Count; index++)
            {
                OwnedNode oldNode = retired[index];
                RegionParticipantCleanupReason reason =
                    nextCommitted.ContainsKey(oldNode.Definition.Id)
                        ? RegionParticipantCleanupReason.Replaced
                        : RegionParticipantCleanupReason.Removed;
                works.Add(new CleanupWork(oldNode, reason));
            }

            return new CleanupBatch(works);
        }

        private static HashSet<IRegionParticipantCandidate>
            CollectRemainingCleanupCandidates(CleanupBatch batch)
        {
            var candidates =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            if (batch == null) return candidates;

            for (int index = batch.Index;
                 index < batch.Works.Count;
                 index++)
            {
                IRegionParticipantCandidate candidate =
                    batch.Works[index].Node.Candidate;
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private static void AddCompletedCleanupCandidates(
            CleanupBatch batch,
            ISet<IRegionParticipantCandidate> candidates)
        {
            if (batch == null || candidates == null) return;

            for (int index = 0; index < batch.Works.Count; index++)
            {
                CleanupWork work = batch.Works[index];
                if (work.Completed && work.Node.Candidate != null)
                {
                    candidates.Add(work.Node.Candidate);
                }
            }
        }

        private static void CollectObservableCleanupTasks(
            CleanupBatch batch,
            IDictionary<
                IRegionParticipantCandidate,
                Task<RegionParticipantCleanupResult>> cleanups)
        {
            if (batch == null || cleanups == null) return;

            for (int index = batch.Index;
                 index < batch.Works.Count;
                 index++)
            {
                CleanupWork work = batch.Works[index];
                if (!work.Completed &&
                    work.InvocationAttempted &&
                    work.InFlight != null &&
                    work.Node.Candidate != null)
                {
                    cleanups[work.Node.Candidate] = work.InFlight;
                }
            }
        }

        private static CleanupBatch CreateShutdownCleanupBatch(
            RegionState state,
            ISet<IRegionParticipantCandidate> excluded = null)
        {
            var unique =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            if (excluded != null)
            {
                foreach (IRegionParticipantCandidate candidate in excluded)
                {
                    if (candidate != null)
                    {
                        unique.Add(candidate);
                    }
                }
            }

            var works = new List<CleanupWork>();
            for (int index = state.FaultOwned.Count - 1;
                 index >= 0;
                 index--)
            {
                OwnedNode node = state.FaultOwned[index];
                if (unique.Add(node.Candidate))
                {
                    works.Add(
                        new CleanupWork(
                            node,
                            RegionParticipantCleanupReason
                                .HostShutdown));
                }
            }

            for (int index = state.ActiveOwned.Count - 1;
                 index >= 0;
                 index--)
            {
                OwnedNode node = state.ActiveOwned[index];
                if (unique.Add(node.Candidate))
                {
                    works.Add(
                        new CleanupWork(
                            node,
                            RegionParticipantCleanupReason
                                .HostShutdown));
                }
            }

            for (int nodeIndex = state.Plan.Nodes.Count - 1;
                 nodeIndex >= 0;
                 nodeIndex--)
            {
                RegionPlanNodeId nodeId =
                    state.Plan.Nodes[nodeIndex].Id;
                if (state.Committed.TryGetValue(
                        nodeId,
                        out OwnedNode committed) &&
                    unique.Add(committed.Candidate))
                {
                    works.Add(
                        new CleanupWork(
                            committed,
                            RegionParticipantCleanupReason
                                .HostShutdown));
                }
            }

            return new CleanupBatch(works);
        }

        private void PublishReady(
            RegionState state,
            RegionDemandResolution resolution,
            int reusedCount,
            bool optionalDegraded)
        {
            state.ReusedNodeCount = reusedCount;
            state.OptionalDegraded = optionalDegraded;
            state.LastDiagnostic = optionalDegraded
                ? RegionErrors.OptionalDegraded(
                    "One or more optional Region participants are absent.")
                : CoCoDiagnostic.None;
            PublishProgress(
                state,
                resolution.DesiredGeneration,
                reusedCount,
                0,
                optionalDegraded,
                false,
                false,
                state.LastDiagnostic);
            runtime.PublishTransitionReady(
                state.Plan.RegionId,
                resolution.DesiredGeneration);
            PublishProgress(
                state,
                resolution.DesiredGeneration,
                reusedCount,
                0,
                optionalDegraded,
                false,
                false,
                state.LastDiagnostic);
        }

        private void PublishFailureAndProgress(
            RegionState state,
            long generation,
            CoCoDiagnostic diagnostic,
            bool faulted,
            bool blockedCleanup,
            int candidateCount)
        {
            runtime.PublishTransitionFailed(
                state.Plan.RegionId,
                generation,
                diagnostic);
            PublishProgress(
                state,
                generation,
                state.ReusedNodeCount,
                Math.Max(0, candidateCount),
                state.OptionalDegraded,
                faulted,
                blockedCleanup,
                diagnostic);
        }

        private void PublishProgress(
            RegionState state,
            long generation,
            int reusedCount,
            int candidateCount,
            bool optionalDegraded,
            bool faulted,
            bool blockedCleanup,
            CoCoDiagnostic diagnostic)
        {
            runtime.PublishTransitionProgress(
                state.Plan.RegionId,
                generation,
                state.KnownChunks,
                Math.Max(0, reusedCount),
                Math.Max(0, candidateCount),
                optionalDegraded,
                faulted,
                blockedCleanup,
                diagnostic);
        }

        private static int CountUniqueOwnedNodes(
            params IEnumerable<OwnedNode>[] sources)
        {
            var unique =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            if (sources == null) return 0;

            for (int sourceIndex = 0;
                 sourceIndex < sources.Length;
                 sourceIndex++)
            {
                IEnumerable<OwnedNode> source =
                    sources[sourceIndex];
                if (source == null) continue;

                foreach (OwnedNode node in source)
                {
                    if (node?.Candidate != null)
                    {
                        unique.Add(node.Candidate);
                    }
                }
            }

            return unique.Count;
        }

        private long NextOwnershipSequence()
        {
            long sequence = nextOwnershipSequence++;
            if (nextOwnershipSequence <= 0L)
            {
                nextOwnershipSequence = 1L;
            }

            return sequence;
        }

        private void EnterParticipantCallback()
        {
            Interlocked.Increment(ref callbackDepth);
        }

        private void ExitParticipantCallback()
        {
            Interlocked.Decrement(ref callbackDepth);
        }

        private static async UniTask<T> AwaitTaskAsync<T>(
            Task<T> task) =>
            await task;

        private async UniTaskVoid ForceCleanupStatesInOrderNoThrowAsync(
            IReadOnlyList<RegionState> shutdownOrder,
            HashSet<IRegionParticipantCandidate> excluded,
            IReadOnlyDictionary<
                IRegionParticipantCandidate,
                Task<RegionParticipantCleanupResult>> lateCleanups)
        {
            try
            {
                var observed =
                    new HashSet<IRegionParticipantCandidate>(
                        ReferenceCandidateComparer.Instance);
                for (int stateIndex = 0;
                     stateIndex < shutdownOrder.Count;
                     stateIndex++)
                {
                    RegionState state = shutdownOrder[stateIndex];
                    state.ActiveCancellation?.Cancel();
                    Task runner = state.RunnerTask;
                    if (runner != null && !runner.IsCompleted)
                    {
                        try
                        {
                            await runner;
                        }
                        catch
                        {
                            // Terminal cleanup below remains authoritative.
                        }

                        await UniTask.SwitchToMainThread();
                    }

                    var stateCleanups =
                        new List<KeyValuePair<
                            IRegionParticipantCandidate,
                            Task<RegionParticipantCleanupResult>>>();
                    foreach (KeyValuePair<
                                 IRegionParticipantCandidate,
                                 Task<RegionParticipantCleanupResult>> pair in
                             lateCleanups)
                    {
                        if (pair.Key != null &&
                            pair.Value != null &&
                            !observed.Contains(pair.Key) &&
                            StateOwnsCandidate(state, pair.Key))
                        {
                            stateCleanups.Add(pair);
                        }
                    }

                    if (state.Blocked != null)
                    {
                        for (int workIndex = state.Blocked.Batch.Index;
                             workIndex <
                             state.Blocked.Batch.Works.Count;
                             workIndex++)
                        {
                            CleanupWork work =
                                state.Blocked.Batch.Works[workIndex];
                            if (work.Node.Candidate != null &&
                                work.InFlight != null &&
                                !observed.Contains(
                                    work.Node.Candidate))
                            {
                                stateCleanups.Add(
                                    new KeyValuePair<
                                        IRegionParticipantCandidate,
                                        Task<RegionParticipantCleanupResult>>(
                                        work.Node.Candidate,
                                        work.InFlight));
                            }
                        }
                    }

                    for (int cleanupIndex = 0;
                         cleanupIndex < stateCleanups.Count;
                         cleanupIndex++)
                    {
                        KeyValuePair<
                            IRegionParticipantCandidate,
                            Task<RegionParticipantCleanupResult>> cleanup =
                                stateCleanups[cleanupIndex];
                        if (!observed.Add(cleanup.Key))
                        {
                            continue;
                        }

                        bool succeeded = false;
                        try
                        {
                            RegionParticipantCleanupResult result =
                                await cleanup.Value;
                            succeeded = result.Succeeded;
                        }
                        catch
                        {
                            // Terminal force cleanup handles this candidate.
                        }

                        await UniTask.SwitchToMainThread();
                        if (succeeded)
                        {
                            excluded.Add(cleanup.Key);
                            ownedCandidates.Remove(cleanup.Key);
                        }
                        else
                        {
                            excluded.Remove(cleanup.Key);
                        }
                    }

                    ForceCleanupState(
                        state,
                        excluded.Count == 0
                            ? null
                            : excluded);
                }

                foreach (KeyValuePair<
                             IRegionParticipantCandidate,
                             Task<RegionParticipantCleanupResult>> pair in
                         lateCleanups)
                {
                    if (pair.Key == null ||
                        pair.Value == null ||
                        !observed.Add(pair.Key))
                    {
                        continue;
                    }

                    bool succeeded = false;
                    try
                    {
                        RegionParticipantCleanupResult result =
                            await pair.Value;
                        succeeded = result.Succeeded;
                    }
                    catch
                    {
                        // Terminal force cleanup handles this candidate.
                    }

                    await UniTask.SwitchToMainThread();
                    if (succeeded)
                    {
                        ownedCandidates.Remove(pair.Key);
                    }
                    else
                    {
                        ForceCleanupCandidate(
                            pair.Key,
                            new HashSet<IRegionParticipantCandidate>(
                                ReferenceCandidateComparer.Instance));
                    }
                }
            }
            catch
            {
                // Terminal shutdown has no remaining recovery surface.
            }
        }

        private static bool StateOwnsCandidate(
            RegionState state,
            IRegionParticipantCandidate candidate)
        {
            for (int index = 0;
                 index < state.ActiveOwned.Count;
                 index++)
            {
                if (ReferenceEquals(
                        state.ActiveOwned[index].Candidate,
                        candidate))
                {
                    return true;
                }
            }

            for (int index = 0;
                 index < state.FaultOwned.Count;
                 index++)
            {
                if (ReferenceEquals(
                        state.FaultOwned[index].Candidate,
                        candidate))
                {
                    return true;
                }
            }

            foreach (OwnedNode node in state.Committed.Values)
            {
                if (ReferenceEquals(node.Candidate, candidate))
                {
                    return true;
                }
            }

            if (state.Blocked != null)
            {
                for (int index = state.Blocked.Batch.Index;
                     index < state.Blocked.Batch.Works.Count;
                     index++)
                {
                    if (ReferenceEquals(
                            state.Blocked.Batch.Works[index]
                                .Node.Candidate,
                            candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private sealed class ReferenceCandidateComparer :
            IEqualityComparer<IRegionParticipantCandidate>
        {
            internal static readonly ReferenceCandidateComparer Instance =
                new ReferenceCandidateComparer();

            public bool Equals(
                IRegionParticipantCandidate left,
                IRegionParticipantCandidate right) =>
                ReferenceEquals(left, right);

            public int GetHashCode(
                IRegionParticipantCandidate candidate) =>
                candidate == null
                    ? 0
                    : System.Runtime.CompilerServices
                        .RuntimeHelpers.GetHashCode(candidate);
        }
    }
}
