using System;
using System.Collections.Generic;
using System.Text;
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
                RegionCapabilitySet capabilities,
                string effectiveFingerprint)
            {
                Definition = definition;
                Capabilities =
                    capabilities ?? RegionCapabilitySet.Empty;
                EffectiveFingerprint =
                    effectiveFingerprint ?? string.Empty;
            }

            internal RegionCompiledParticipantNode Definition { get; }
            internal RegionCapabilitySet Capabilities { get; }
            internal string EffectiveFingerprint { get; }
        }

        private sealed class OwnedNode
        {
            internal OwnedNode(
                RegionCompiledParticipantNode definition,
                RegionCapabilitySet capabilities,
                string effectiveFingerprint,
                IRegionParticipantCandidate candidate)
            {
                Definition = definition;
                Capabilities =
                    capabilities ?? RegionCapabilitySet.Empty;
                EffectiveFingerprint =
                    effectiveFingerprint ?? string.Empty;
                Candidate = candidate;
            }

            internal RegionCompiledParticipantNode Definition { get; }
            internal RegionCapabilitySet Capabilities { get; }
            internal string EffectiveFingerprint { get; }
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

        private sealed class BlockedState
        {
            internal BlockedState(
                CleanupBatch batch,
                RegionDemandResolution resolution,
                BlockedContinuation continuation,
                bool optionalDegraded,
                CoCoDiagnostic diagnostic)
            {
                Batch = batch;
                Resolution = resolution;
                Continuation = continuation;
                OptionalDegraded = optionalDegraded;
                Diagnostic = diagnostic;
            }

            internal CleanupBatch Batch { get; }
            internal RegionDemandResolution Resolution { get; }
            internal BlockedContinuation Continuation { get; }
            internal bool OptionalDegraded { get; }
            internal CoCoDiagnostic Diagnostic { get; }
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

        public bool TryRetryRegion(
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
                if (resolution.DesiredGeneration !=
                    state.Blocked.Resolution.DesiredGeneration)
                {
                    state.PendingResolution = resolution;
                }

                StartBlockedRetry(state);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!state.RequiresRetry)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region has no retryable Prepare failure.");
                return false;
            }

            state.RequiresRetry = false;
            state.PendingResolution = resolution;
            EnsureRunner(state);
            diagnostic = CoCoDiagnostic.None;
            return true;
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

            if (lateCleanups != null)
            {
                foreach (KeyValuePair<
                             IRegionParticipantCandidate,
                             Task<RegionParticipantCleanupResult>> pair in
                         lateCleanups)
                {
                    if (pair.Key == null || pair.Value == null) continue;
                    exclusionSnapshot.Add(pair.Key);
                    ObserveLateCleanupNoThrowAsync(
                        pair.Key,
                        pair.Value).Forget();
                }
            }

            foreach (RegionState state in states.Values)
            {
                state.ActiveCancellation?.Cancel();
                if (state.RunnerTask != null &&
                    !state.RunnerTask.IsCompleted)
                {
                    ObserveLateRunnerAndForceStateNoThrowAsync(
                        state,
                        state.RunnerTask,
                        exclusionSnapshot.Count == 0
                            ? null
                            : exclusionSnapshot).Forget();
                    continue;
                }

                ForceCleanupState(
                    state,
                    exclusionSnapshot.Count == 0
                        ? null
                        : exclusionSnapshot);
            }

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
                    if (committed.Candidate is
                            IRegionChunkAnchorSource &&
                        !attempt.TrySeedAnchor(
                            committed,
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

                    nextCommitted.Add(definition.Id, committed);
                    available.Add(definition.Id);
                    reusedCount++;
                    continue;
                }

                if (!catalog.TryGetRegistration(
                        definition.ParticipantTypeId,
                        definition.ModeId,
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
                    definition.FragmentId,
                    attempt.CreateResolver(definition.Id));
                EnterParticipantCallback();
                try
                {
                    created = registration.Factory.TryCreateCandidate(
                        createContext,
                        definition.ParticipantPlan,
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
                        definition,
                        desiredNode.Capabilities,
                        desiredNode.EffectiveFingerprint,
                        candidate);
                    candidates.Add(owned);
                    state.ActiveOwned.Add(owned);
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
                     !ReferenceEquals(oldNode, nextNode)))
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
            state.Blocked = new BlockedState(
                batch,
                resolution,
                continuation,
                optionalDegraded,
                blocked);
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
            var runners = new List<Task>();
            foreach (RegionState state in states.Values)
            {
                state.ActiveCancellation?.Cancel();
                if (state.RunnerTask != null &&
                    !state.RunnerTask.IsCompleted)
                {
                    runners.Add(state.RunnerTask);
                }
            }

            if (runners.Count > 0)
            {
                Task allRunners = Task.WhenAll(runners);
                Task completed = await Task.WhenAny(
                    allRunners,
                    Task.Delay(cleanupTimeout));
                await UniTask.SwitchToMainThread();
                if (!ReferenceEquals(completed, allRunners))
                {
                    CoCoDiagnostic timeout =
                        RegionErrors.CleanupBlocked(
                            "Map shutdown timed out while cancelling active Region transitions.");
                    ForceShutdown();
                    return timeout;
                }
            }

            CoCoDiagnostic firstFailure = CoCoDiagnostic.None;
            var terminalCleanupExclusions =
                new HashSet<IRegionParticipantCandidate>(
                    ReferenceCandidateComparer.Instance);
            var lateShutdownCleanups =
                new Dictionary<
                    IRegionParticipantCandidate,
                    Task<RegionParticipantCleanupResult>>(
                    ReferenceCandidateComparer.Instance);
            foreach (RegionState state in states.Values)
            {
                HashSet<IRegionParticipantCandidate>
                    blockedCleanupCandidates = null;
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
                        blockedInFlight.Add(work.Node.Candidate))
                    {
                        ObserveLateCleanupNoThrowAsync(
                            work.Node.Candidate,
                            work.InFlight).Forget();
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
            state.Blocked = null;
        }

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
                if (!node.IsActiveFor(effective)) continue;

                string fingerprint = BuildEffectiveFingerprint(
                    node,
                    effective);
                desired.Add(
                    node.Id,
                    new DesiredNode(
                        node,
                        effective,
                        fingerprint));
            }

            return desired;
        }

        private static string BuildEffectiveFingerprint(
            RegionCompiledParticipantNode node,
            RegionCapabilitySet capabilities)
        {
            var builder = new StringBuilder();
            AppendFingerprintPart(builder, node.Fingerprint);
            if (node.ParticipantPlan is IRegionCapabilitySensitivePlan)
            {
                builder.Append('|').Append(capabilities.Count);
                for (int index = 0;
                     index < capabilities.Count;
                     index++)
                {
                    AppendFingerprintPart(
                        builder,
                        capabilities.Capabilities[index].Value);
                }
            }

            return builder.ToString();
        }

        private static void AppendFingerprintPart(
            StringBuilder builder,
            string value)
        {
            string safe = value ?? string.Empty;
            builder.Append('|')
                .Append(safe.Length)
                .Append(':')
                .Append(safe);
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

        private async UniTaskVoid ObserveLateCleanupNoThrowAsync(
            IRegionParticipantCandidate candidate,
            Task<RegionParticipantCleanupResult> cleanup)
        {
            bool requiresTerminalCleanup;
            try
            {
                RegionParticipantCleanupResult result =
                    await cleanup;
                requiresTerminalCleanup = !result.Succeeded;
            }
            catch
            {
                requiresTerminalCleanup = true;
            }

            try
            {
                await UniTask.SwitchToMainThread();
                if (!requiresTerminalCleanup)
                {
                    ownedCandidates.Remove(candidate);
                    return;
                }

                ForceCleanupCandidate(
                    candidate,
                    new HashSet<IRegionParticipantCandidate>(
                        ReferenceCandidateComparer.Instance));
            }
            catch
            {
                // Terminal shutdown has no remaining recovery surface.
            }
        }

        private async UniTaskVoid ObserveLateRunnerAndForceStateNoThrowAsync(
            RegionState state,
            Task runner,
            ISet<IRegionParticipantCandidate> excluded)
        {
            try
            {
                try
                {
                    await runner;
                }
                catch
                {
                    // Runner failure still releases terminal ownership below.
                }

                await UniTask.SwitchToMainThread();
                ForceCleanupState(state, excluded);
            }
            catch
            {
                // Terminal shutdown has no remaining recovery surface.
            }
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
