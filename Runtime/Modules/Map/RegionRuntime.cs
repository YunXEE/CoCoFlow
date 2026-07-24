using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Modules.Map
{
    public readonly struct RegionDemandRuntimeSnapshot
    {
        internal RegionDemandRuntimeSnapshot(
            RegionDemandOwnerId ownerId,
            long scopeSequence,
            long leaseSequence,
            RegionId regionId,
            RegionDemandRevision revision,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            RegionReadinessStatus? readiness,
            CoCoDiagnostic diagnostic)
        {
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            LeaseSequence = leaseSequence;
            RegionId = regionId;
            Revision = revision;
            Capabilities = capabilities;
            Coverage = coverage;
            Readiness = readiness;
            Diagnostic = diagnostic;
        }

        public RegionDemandOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public long LeaseSequence { get; }
        public RegionId RegionId { get; }
        public RegionDemandRevision Revision { get; }
        public RegionCapabilitySet Capabilities { get; }
        public RegionCoverage Coverage { get; }
        public RegionReadinessStatus? Readiness { get; }
        public CoCoDiagnostic Diagnostic { get; }
    }

    public readonly struct RegionChunkRuntimeSnapshot
    {
        internal RegionChunkRuntimeSnapshot(
            RegionChunkId chunkId,
            RegionCapabilitySet desiredCapabilities,
            RegionCapabilitySet committedCapabilities,
            RegionTierId desiredTierId,
            RegionTierId committedTierId,
            RegionCapabilitySet desiredEffectiveCapabilities,
            RegionCapabilitySet committedEffectiveCapabilities)
        {
            ChunkId = chunkId;
            DesiredCapabilities =
                desiredCapabilities ?? RegionCapabilitySet.Empty;
            CommittedCapabilities =
                committedCapabilities ?? RegionCapabilitySet.Empty;
            DesiredTierId = desiredTierId;
            CommittedTierId = committedTierId;
            DesiredEffectiveCapabilities =
                desiredEffectiveCapabilities ??
                RegionCapabilitySet.Empty;
            CommittedEffectiveCapabilities =
                committedEffectiveCapabilities ??
                RegionCapabilitySet.Empty;
        }

        public RegionChunkId ChunkId { get; }
        public RegionCapabilitySet DesiredCapabilities { get; }
        public RegionCapabilitySet CommittedCapabilities { get; }
        public RegionTierId DesiredTierId { get; }
        public RegionTierId CommittedTierId { get; }
        public RegionCapabilitySet DesiredEffectiveCapabilities { get; }
        public RegionCapabilitySet CommittedEffectiveCapabilities { get; }
    }

    public sealed class RegionRuntimeRegionSnapshot
    {
        private readonly ReadOnlyCollection<RegionChunkRuntimeSnapshot> chunks;

        internal RegionRuntimeRegionSnapshot(
            RegionId regionId,
            long desiredGeneration,
            long committedGeneration,
            RegionCapabilitySet desiredCapabilities,
            RegionCapabilitySet committedCapabilities,
            RegionCoverage desiredCoverage,
            RegionCoverage committedCoverage,
            RegionTierId desiredTierId,
            RegionTierId committedTierId,
            RegionCapabilitySet desiredEffectiveCapabilities,
            RegionCapabilitySet committedEffectiveCapabilities,
            int reusedNodeCount,
            int candidateNodeCount,
            bool optionalDegraded,
            bool faulted,
            bool blockedCleanup,
            CoCoDiagnostic diagnostic,
            IList<RegionChunkRuntimeSnapshot> chunks)
        {
            RegionId = regionId;
            DesiredGeneration = desiredGeneration;
            CommittedGeneration = committedGeneration;
            DesiredCapabilities =
                desiredCapabilities ?? RegionCapabilitySet.Empty;
            CommittedCapabilities =
                committedCapabilities ?? RegionCapabilitySet.Empty;
            DesiredCoverage = desiredCoverage;
            CommittedCoverage = committedCoverage;
            DesiredTierId = desiredTierId;
            CommittedTierId = committedTierId;
            DesiredEffectiveCapabilities =
                desiredEffectiveCapabilities ??
                RegionCapabilitySet.Empty;
            CommittedEffectiveCapabilities =
                committedEffectiveCapabilities ??
                RegionCapabilitySet.Empty;
            ReusedNodeCount = reusedNodeCount;
            CandidateNodeCount = candidateNodeCount;
            OptionalDegraded = optionalDegraded;
            Faulted = faulted;
            BlockedCleanup = blockedCleanup;
            Diagnostic = diagnostic;
            this.chunks = new ReadOnlyCollection<RegionChunkRuntimeSnapshot>(
                chunks == null
                    ? new List<RegionChunkRuntimeSnapshot>()
                    : new List<RegionChunkRuntimeSnapshot>(chunks));
        }

        public RegionId RegionId { get; }
        public long DesiredGeneration { get; }
        public long CommittedGeneration { get; }
        public RegionCapabilitySet DesiredCapabilities { get; }
        public RegionCapabilitySet CommittedCapabilities { get; }
        public RegionCoverage DesiredCoverage { get; }
        public RegionCoverage CommittedCoverage { get; }
        public RegionTierId DesiredTierId { get; }
        public RegionTierId CommittedTierId { get; }
        public RegionCapabilitySet DesiredEffectiveCapabilities { get; }
        public RegionCapabilitySet CommittedEffectiveCapabilities { get; }
        public int ReusedNodeCount { get; }
        public int CandidateNodeCount { get; }
        public bool OptionalDegraded { get; }
        public bool Faulted { get; }
        public bool BlockedCleanup { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public IReadOnlyList<RegionChunkRuntimeSnapshot> Chunks => chunks;
        public bool HasInFlightTransition =>
            DesiredGeneration != CommittedGeneration ||
            CandidateNodeCount > 0;
    }

    public sealed class RegionRuntimeSnapshot
    {
        private readonly ReadOnlyCollection<RegionDemandRuntimeSnapshot> demands;
        private readonly ReadOnlyCollection<RegionRuntimeRegionSnapshot> regions;

        internal RegionRuntimeSnapshot(
            bool isShuttingDown,
            bool isDisposed,
            IList<RegionDemandRuntimeSnapshot> demands,
            IList<RegionRuntimeRegionSnapshot> regions,
            CoCoDiagnostic lastDiagnostic)
        {
            IsShuttingDown = isShuttingDown;
            IsDisposed = isDisposed;
            LastDiagnostic = lastDiagnostic;
            this.demands =
                new ReadOnlyCollection<RegionDemandRuntimeSnapshot>(
                    demands == null
                        ? new List<RegionDemandRuntimeSnapshot>()
                        : new List<RegionDemandRuntimeSnapshot>(demands));
            this.regions =
                new ReadOnlyCollection<RegionRuntimeRegionSnapshot>(
                    regions == null
                        ? new List<RegionRuntimeRegionSnapshot>()
                        : new List<RegionRuntimeRegionSnapshot>(regions));
        }

        public bool IsShuttingDown { get; }
        public bool IsDisposed { get; }
        public IReadOnlyList<RegionDemandRuntimeSnapshot> Demands => demands;
        public IReadOnlyList<RegionRuntimeRegionSnapshot> Regions => regions;
        public CoCoDiagnostic LastDiagnostic { get; }
    }

    internal readonly struct RegionDemandLeaseSnapshot
    {
        internal RegionDemandLeaseSnapshot(
            long leaseSequence,
            RegionDemandRevision revision,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage)
        {
            LeaseSequence = leaseSequence;
            Revision = revision;
            Capabilities = capabilities;
            Coverage = coverage;
        }

        internal long LeaseSequence { get; }
        internal RegionDemandRevision Revision { get; }
        internal RegionCapabilitySet Capabilities { get; }
        internal RegionCoverage Coverage { get; }
    }

    internal sealed class RegionDemandResolution
    {
        private readonly ReadOnlyCollection<RegionDemandLeaseSnapshot> leases;
        private readonly ReadOnlyDictionary<RegionChunkId, RegionCapabilitySet>
            explicitChunkCapabilities;

        internal RegionDemandResolution(
            RegionId regionId,
            long desiredGeneration,
            RegionCapabilitySet regionCapabilities,
            RegionCoverage mergedCoverage,
            RegionCapabilitySet allChunkCapabilities,
            IDictionary<RegionChunkId, RegionCapabilitySet>
                explicitChunkCapabilities,
            IList<RegionDemandLeaseSnapshot> leases)
        {
            RegionId = regionId;
            DesiredGeneration = desiredGeneration;
            RegionCapabilities = regionCapabilities ?? RegionCapabilitySet.Empty;
            MergedCoverage = mergedCoverage;
            AllChunkCapabilities =
                allChunkCapabilities ?? RegionCapabilitySet.Empty;
            this.explicitChunkCapabilities =
                new ReadOnlyDictionary<RegionChunkId, RegionCapabilitySet>(
                    explicitChunkCapabilities == null
                        ? new Dictionary<RegionChunkId, RegionCapabilitySet>()
                        : new Dictionary<RegionChunkId, RegionCapabilitySet>(
                            explicitChunkCapabilities));
            this.leases = new ReadOnlyCollection<RegionDemandLeaseSnapshot>(
                leases == null
                    ? new List<RegionDemandLeaseSnapshot>()
                    : new List<RegionDemandLeaseSnapshot>(leases));
        }

        internal RegionId RegionId { get; }
        internal long DesiredGeneration { get; }
        internal RegionCapabilitySet RegionCapabilities { get; }
        internal RegionCoverage MergedCoverage { get; }
        internal RegionCapabilitySet AllChunkCapabilities { get; }
        internal IReadOnlyDictionary<RegionChunkId, RegionCapabilitySet>
            ExplicitChunkCapabilities => explicitChunkCapabilities;
        internal IReadOnlyList<RegionDemandLeaseSnapshot> Leases => leases;
        internal bool HasDemand => leases.Count > 0;

        internal RegionCapabilitySet GetChunkCapabilities(
            RegionChunkId chunkId)
        {
            if (!chunkId.IsValid)
            {
                return RegionCapabilitySet.Empty;
            }

            return explicitChunkCapabilities.TryGetValue(
                chunkId,
                out RegionCapabilitySet explicitCapabilities)
                ? AllChunkCapabilities.Union(explicitCapabilities)
                : AllChunkCapabilities;
        }
    }

    internal readonly struct RegionResolvedChunkFidelity
    {
        internal RegionResolvedChunkFidelity(
            RegionTierId tierId,
            RegionCapabilitySet effectiveCapabilities)
        {
            TierId = tierId;
            EffectiveCapabilities =
                effectiveCapabilities ?? RegionCapabilitySet.Empty;
        }

        internal RegionTierId TierId { get; }
        internal RegionCapabilitySet EffectiveCapabilities { get; }
    }

    internal interface IRegionDemandTransitionSink
    {
        bool IsInvokingParticipantCallback { get; }

        bool TryValidateDemand(
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out CoCoDiagnostic diagnostic);

        void RequestTransition(RegionDemandResolution resolution);

        bool TryAcceptRetry(
            RegionId regionId,
            RegionDemandResolution resolution,
            out CoCoDiagnostic diagnostic);

        void StartAcceptedRetry(
            RegionId regionId,
            RegionDemandResolution resolution);

        UniTask<CoCoDiagnostic> ShutdownAsync();

        void ForceShutdown();
    }

    public sealed class RegionRuntime
    {
        private sealed class ContentShutdownParticipant :
            IContentRuntimeShutdownParticipant
        {
            private readonly RegionRuntime runtime;

            internal ContentShutdownParticipant(RegionRuntime runtime)
            {
                this.runtime = runtime;
            }

            public UniTask<CoCoDiagnostic>
                DrainBeforeContentShutdownAsync()
            {
                return runtime.ShutdownAsync();
            }
        }

        private sealed class RegionDemandState
        {
            internal RegionDemandState(RegionId regionId)
            {
                RegionId = regionId;
            }

            internal RegionId RegionId { get; }
            internal Dictionary<long, RegionDemandLease> Leases { get; } =
                new Dictionary<long, RegionDemandLease>();
            internal HashSet<RegionChunkId> KnownChunks { get; } =
                new HashSet<RegionChunkId>();
            internal Dictionary<RegionChunkId, RegionCapabilitySet>
                CommittedExplicitChunkCapabilities { get; } =
                    new Dictionary<RegionChunkId, RegionCapabilitySet>();
            internal long DesiredGeneration { get; set; }
            internal long CommittedGeneration { get; set; }
            internal RegionDemandResolution Resolution { get; set; }
            internal RegionCapabilitySet CommittedRegionCapabilities { get; set; } =
                RegionCapabilitySet.Empty;
            internal RegionCoverage CommittedCoverage { get; set; }
            internal RegionCapabilitySet CommittedAllChunkCapabilities { get; set; } =
                RegionCapabilitySet.Empty;
            internal RegionTierId DesiredTierId { get; set; }
            internal RegionTierId CommittedTierId { get; set; }
            internal RegionCapabilitySet DesiredEffectiveCapabilities
            {
                get;
                set;
            } = RegionCapabilitySet.Empty;
            internal RegionCapabilitySet CommittedEffectiveCapabilities
            {
                get;
                set;
            } = RegionCapabilitySet.Empty;
            internal Dictionary<
                RegionChunkId,
                RegionResolvedChunkFidelity> DesiredChunkFidelity
            {
                get;
            } =
                new Dictionary<
                    RegionChunkId,
                    RegionResolvedChunkFidelity>();
            internal Dictionary<
                RegionChunkId,
                RegionResolvedChunkFidelity> CommittedChunkFidelity
            {
                get;
            } =
                new Dictionary<
                    RegionChunkId,
                    RegionResolvedChunkFidelity>();
            internal int ReusedNodeCount { get; set; }
            internal int CandidateNodeCount { get; set; }
            internal bool OptionalDegraded { get; set; }
            internal bool Faulted { get; set; }
            internal bool BlockedCleanup { get; set; }
            internal CoCoDiagnostic Diagnostic { get; set; }
        }

        private readonly ContentRuntime contentRuntime;
        private readonly ContentShutdownParticipant contentShutdownParticipant;
        private readonly Dictionary<RegionDemandOwnerId, RegionDemandScope> scopes =
            new Dictionary<RegionDemandOwnerId, RegionDemandScope>();
        private readonly Dictionary<RegionId, RegionDemandState> regions =
            new Dictionary<RegionId, RegionDemandState>();
        private readonly int mainThreadId;
        private IRegionDemandTransitionSink transitionSink;
        private long nextScopeSequence = 1L;
        private long nextLeaseSequence = 1L;
        private long nextDemandRevision = 1L;
        private readonly HashSet<RegionId> deferredTransitionRegions =
            new HashSet<RegionId>();
        private long temporalBarrierToken;
        private long nextTemporalBarrierToken = 1L;
        private bool temporalFlushScheduled;
        private bool isShuttingDown;
        private bool isDisposed;
        private Task<CoCoDiagnostic> shutdownTask;
        private TaskCompletionSource<CoCoDiagnostic> shutdownCompletion;

        private RegionRuntime(ContentRuntime contentRuntime)
        {
            this.contentRuntime = contentRuntime;
            contentShutdownParticipant =
                new ContentShutdownParticipant(this);
            mainThreadId = RegionMainThreadGuard.MainThreadId;
        }

        public bool IsShuttingDown => isShuttingDown;
        public bool IsDisposed => isDisposed;
        public CoCoDiagnostic LastDiagnostic { get; private set; }

        internal ContentRuntime ContentRuntime => contentRuntime;
        internal bool IsTemporalDispatchDeferred =>
            temporalBarrierToken != 0L || temporalFlushScheduled;
        internal int DeferredTransitionCount =>
            deferredTransitionRegions.Count;

        public RegionRuntimeSnapshot CaptureSnapshot()
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException(
                    "Map Region snapshots must be captured on the Unity main thread.");
            }

            var orderedStates = new List<RegionDemandState>(regions.Values);
            orderedStates.Sort(
                (left, right) => string.CompareOrdinal(
                    left.RegionId.Value,
                    right.RegionId.Value));
            var demandSnapshots = new List<RegionDemandRuntimeSnapshot>();
            var regionSnapshots =
                new List<RegionRuntimeRegionSnapshot>(orderedStates.Count);

            for (int stateIndex = 0;
                 stateIndex < orderedStates.Count;
                 stateIndex++)
            {
                RegionDemandState state = orderedStates[stateIndex];
                var orderedLeases =
                    new List<RegionDemandLease>(state.Leases.Values);
                orderedLeases.Sort(
                    (left, right) =>
                        left.LeaseSequence.CompareTo(right.LeaseSequence));
                for (int leaseIndex = 0;
                     leaseIndex < orderedLeases.Count;
                     leaseIndex++)
                {
                    RegionDemandLease lease = orderedLeases[leaseIndex];
                    demandSnapshots.Add(
                        new RegionDemandRuntimeSnapshot(
                            lease.OwnerId,
                            lease.ScopeSequence,
                            lease.LeaseSequence,
                            lease.RegionId,
                            lease.Revision,
                            lease.Capabilities,
                            lease.Coverage,
                            lease.CurrentReadinessStatus,
                            lease.CurrentDiagnostic));
                }

                RegionDemandResolution resolution = state.Resolution;
                var chunkIds = new HashSet<RegionChunkId>(state.KnownChunks);
                foreach (RegionChunkId chunkId in
                         state.CommittedExplicitChunkCapabilities.Keys)
                {
                    chunkIds.Add(chunkId);
                }

                if (resolution != null)
                {
                    foreach (RegionChunkId chunkId in
                             resolution.ExplicitChunkCapabilities.Keys)
                    {
                        chunkIds.Add(chunkId);
                    }
                }

                var orderedChunkIds = new List<RegionChunkId>(chunkIds);
                orderedChunkIds.Sort(
                    (left, right) =>
                        string.CompareOrdinal(left.Value, right.Value));
                var chunkSnapshots =
                    new List<RegionChunkRuntimeSnapshot>(
                        orderedChunkIds.Count);
                for (int chunkIndex = 0;
                     chunkIndex < orderedChunkIds.Count;
                     chunkIndex++)
                {
                    RegionChunkId chunkId = orderedChunkIds[chunkIndex];
                    state.CommittedExplicitChunkCapabilities.TryGetValue(
                        chunkId,
                        out RegionCapabilitySet explicitCommitted);
                    RegionCapabilitySet committed =
                        state.CommittedAllChunkCapabilities.Union(
                            explicitCommitted ?? RegionCapabilitySet.Empty);
                    state.DesiredChunkFidelity.TryGetValue(
                        chunkId,
                        out RegionResolvedChunkFidelity desiredFidelity);
                    state.CommittedChunkFidelity.TryGetValue(
                        chunkId,
                        out RegionResolvedChunkFidelity committedFidelity);
                    chunkSnapshots.Add(
                        new RegionChunkRuntimeSnapshot(
                            chunkId,
                            resolution == null
                                ? RegionCapabilitySet.Empty
                                : resolution.GetChunkCapabilities(chunkId),
                            committed,
                            desiredFidelity.TierId,
                            committedFidelity.TierId,
                            desiredFidelity.EffectiveCapabilities,
                            committedFidelity.EffectiveCapabilities));
                }

                regionSnapshots.Add(
                    new RegionRuntimeRegionSnapshot(
                        state.RegionId,
                        state.DesiredGeneration,
                        state.CommittedGeneration,
                        resolution == null
                            ? RegionCapabilitySet.Empty
                            : resolution.RegionCapabilities,
                        state.CommittedRegionCapabilities,
                        resolution == null
                            ? default
                            : resolution.MergedCoverage,
                        state.CommittedCoverage,
                        state.DesiredTierId,
                        state.CommittedTierId,
                        state.DesiredEffectiveCapabilities,
                        state.CommittedEffectiveCapabilities,
                        state.ReusedNodeCount,
                        state.CandidateNodeCount,
                        state.OptionalDegraded,
                        state.Faulted,
                        state.BlockedCleanup,
                        state.Diagnostic,
                        chunkSnapshots));
            }

            return new RegionRuntimeSnapshot(
                isShuttingDown,
                isDisposed,
                demandSnapshots,
                regionSnapshots,
                LastDiagnostic);
        }

        internal static bool TryCreate(
            ContentRuntime contentRuntime,
            out RegionRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (!RegionMainThreadGuard.IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (contentRuntime == null ||
                contentRuntime.IsShuttingDown ||
                contentRuntime.IsDisposed)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "A live Content Runtime is required by the Map Region runtime.");
                return false;
            }

            var candidate = new RegionRuntime(contentRuntime);
            if (!contentRuntime.TryRegisterShutdownParticipant(
                    candidate.contentShutdownParticipant))
            {
                diagnostic = RegionErrors.DemandConflict(
                    "Content Runtime rejected Map Region shutdown ownership.");
                return false;
            }

            runtime = candidate;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryAttachTransitionSink(
            IRegionDemandTransitionSink sink,
            out CoCoDiagnostic diagnostic)
        {
            if (!IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown ||
                isDisposed ||
                sink == null ||
                transitionSink != null)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region transition authority must attach exactly once before Demand begins.");
                return false;
            }

            transitionSink = sink;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCreateDemandScope(
            RegionDemandOwnerId ownerId,
            out RegionDemandScope scope,
            out CoCoDiagnostic diagnostic)
        {
            scope = null;
            if (!IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown || isDisposed)
            {
                diagnostic = RegionErrors.RuntimeDisposed();
                return false;
            }

            if (!ownerId.IsValid)
            {
                diagnostic = RegionErrors.InvalidIdentifier(
                    "A Region Demand Scope requires a valid owner id.");
                return false;
            }

            if (transitionSink == null)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region transition authority is not attached.");
                return false;
            }

            if (transitionSink.IsInvokingParticipantCallback)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "Region Demand ownership cannot be mutated reentrantly from a Participant callback.");
                return false;
            }

            if (scopes.ContainsKey(ownerId))
            {
                diagnostic = RegionErrors.DemandConflict(
                    "A live Region Demand Scope already owns id '" +
                    ownerId.Value + "'.");
                return false;
            }

            scope = new RegionDemandScope(
                this,
                ownerId,
                NextScopeSequence());
            scopes.Add(ownerId, scope);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryRetryRegion(
            RegionId regionId,
            out CoCoDiagnostic diagnostic)
        {
            if (!IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown || isDisposed)
            {
                diagnostic = RegionErrors.RuntimeDisposed();
                return false;
            }

            if (transitionSink == null ||
                !regions.TryGetValue(
                    regionId,
                    out RegionDemandState state) ||
                state.Resolution == null ||
                !state.Resolution.HasDemand)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "A live Region Demand is required before retry.");
                return false;
            }

            if (transitionSink.IsInvokingParticipantCallback)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "A Region cannot be retried reentrantly from a Participant callback.");
                return false;
            }

            if (IsTemporalDispatchDeferred)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "A Region cannot be retried while Temporal projection defers transition dispatch.");
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!transitionSink.TryAcceptRetry(
                    regionId,
                    state.Resolution,
                    out diagnostic))
            {
                LastDiagnostic = diagnostic;
                return false;
            }

            foreach (RegionDemandLease lease in state.Leases.Values)
            {
                if (!lease.IsReadyForCurrentRevision)
                {
                    lease.PublishPending();
                }
            }

            transitionSink.StartAcceptedRetry(
                regionId,
                state.Resolution);
            diagnostic = CoCoDiagnostic.None;
            LastDiagnostic = diagnostic;
            return true;
        }

        public UniTask<CoCoDiagnostic> ShutdownAsync()
        {
            if (shutdownTask != null)
            {
                return AwaitSharedTaskAsync(shutdownTask);
            }

            if (!IsMainThread)
            {
                return UniTask.FromResult(RegionErrors.MainThreadRequired());
            }

            EnsureSharedShutdownTask();
            CompleteShutdownAsync(shutdownCompletion).Forget();
            return AwaitSharedTaskAsync(shutdownTask);
        }

        internal bool TryEnterTemporalBarrier(
            out RegionTemporalBarrier barrier,
            out CoCoDiagnostic diagnostic)
        {
            barrier = null;
            if (!IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown || isDisposed)
            {
                diagnostic = RegionErrors.RuntimeDisposed();
                return false;
            }

            if (transitionSink == null ||
                transitionSink.IsInvokingParticipantCallback ||
                temporalBarrierToken != 0L ||
                temporalFlushScheduled ||
                deferredTransitionRegions.Count != 0)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal requires one idle transition boundary with no deferred flush.");
                return false;
            }

            foreach (RegionDemandState state in regions.Values)
            {
                if (state.Faulted ||
                    state.BlockedCleanup ||
                    state.CandidateNodeCount != 0 ||
                    state.DesiredGeneration != state.CommittedGeneration)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Region '" + state.RegionId.Value +
                        "' is not at one stable committed transition boundary.");
                    return false;
                }
            }

            long token = NextTemporalBarrierToken();
            temporalBarrierToken = token;
            barrier = new RegionTemporalBarrier(this, token);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void FlushDeferredTransitionsNoThrow()
        {
            if (!IsMainThread ||
                isShuttingDown ||
                isDisposed ||
                temporalBarrierToken != 0L ||
                !temporalFlushScheduled)
            {
                return;
            }

            while (!isShuttingDown &&
                   !isDisposed &&
                   temporalBarrierToken == 0L &&
                   deferredTransitionRegions.Count > 0)
            {
                var orderedRegionIds =
                    new List<RegionId>(deferredTransitionRegions);
                orderedRegionIds.Sort(
                    (left, right) => string.CompareOrdinal(
                        left.Value,
                        right.Value));
                for (int index = 0;
                     index < orderedRegionIds.Count;
                     index++)
                {
                    RegionId regionId = orderedRegionIds[index];
                    deferredTransitionRegions.Remove(regionId);
                    if (regions.TryGetValue(
                            regionId,
                            out RegionDemandState state) &&
                        state.Resolution != null)
                    {
                        DispatchResolution(state);
                    }
                }
            }

            if (!isShuttingDown &&
                !isDisposed &&
                temporalBarrierToken == 0L &&
                deferredTransitionRegions.Count == 0)
            {
                temporalFlushScheduled = false;
            }
        }

        internal bool TryCreateDemand(
            RegionDemandScope scope,
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out RegionDemandLease lease,
            out RegionDemandRevision revision,
            out CoCoDiagnostic diagnostic)
        {
            lease = null;
            revision = default;
            if (!TryValidateMutation(
                    scope,
                    regionId,
                    capabilities,
                    coverage,
                    out diagnostic))
            {
                return false;
            }

            long leaseSequence = NextLeaseSequence();
            revision = NextRevision();
            lease = new RegionDemandLease(
                this,
                scope,
                regionId,
                capabilities,
                coverage,
                leaseSequence,
                revision);
            if (!scope.TryAddLease(lease))
            {
                lease = null;
                revision = default;
                diagnostic = RegionErrors.DemandConflict(
                    "The Region Demand Scope stopped accepting ownership.");
                return false;
            }

            if (!regions.TryGetValue(regionId, out RegionDemandState state))
            {
                state = new RegionDemandState(regionId);
                regions.Add(regionId, state);
            }

            state.Leases.Add(leaseSequence, lease);
            RecomputeAndDispatch(state);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryUpdateDemand(
            RegionDemandLease lease,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out RegionDemandRevision revision,
            out CoCoDiagnostic diagnostic)
        {
            revision = lease == null ? default : lease.Revision;
            diagnostic = CoCoDiagnostic.None;
            if (lease == null ||
                !TryValidateMutation(
                    lease.Scope,
                    lease.RegionId,
                    capabilities,
                    coverage,
                    out diagnostic) ||
                !regions.TryGetValue(
                    lease.RegionId,
                    out RegionDemandState state) ||
                !state.Leases.TryGetValue(
                    lease.LeaseSequence,
                    out RegionDemandLease current) ||
                !ReferenceEquals(current, lease))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.DemandConflict(
                        "The Region Demand Lease is not owned by this runtime.");
                }

                return false;
            }

            if (lease.Capabilities.Equals(capabilities) &&
                lease.Coverage.Equals(coverage))
            {
                revision = lease.Revision;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            revision = NextRevision();
            if (!lease.TryApplyUpdate(capabilities, coverage, revision))
            {
                revision = lease.Revision;
                diagnostic = RegionErrors.DemandConflict(
                    "The Region Demand Lease stopped accepting updates.");
                return false;
            }

            RecomputeAndDispatch(state);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void ReleaseDemand(RegionDemandLease lease)
        {
            if (lease == null) return;
            if (!IsMainThread ||
                transitionSink != null &&
                transitionSink.IsInvokingParticipantCallback)
            {
                ReleaseDemandOnMainThreadAsync(lease).Forget();
                return;
            }

            ReleaseDemandOnMainThread(lease);
        }

        internal void ReleaseScope(RegionDemandScope scope)
        {
            if (scope == null) return;
            if (!IsMainThread ||
                transitionSink != null &&
                transitionSink.IsInvokingParticipantCallback)
            {
                ReleaseScopeOnMainThreadAsync(scope).Forget();
                return;
            }

            RemoveScope(scope);
        }

        internal void PublishTransitionReady(
            RegionId regionId,
            long desiredGeneration)
        {
            if (!IsMainThread ||
                !regions.TryGetValue(
                    regionId,
                    out RegionDemandState state) ||
                state.DesiredGeneration != desiredGeneration)
            {
                return;
            }

            foreach (RegionDemandLease lease in state.Leases.Values)
            {
                lease.PublishReady(lease.Revision);
            }

            RegionDemandResolution resolution = state.Resolution;
            state.CommittedGeneration = desiredGeneration;
            state.CommittedRegionCapabilities =
                resolution.RegionCapabilities;
            state.CommittedCoverage = resolution.MergedCoverage;
            state.CommittedTierId = state.DesiredTierId;
            state.CommittedEffectiveCapabilities =
                state.DesiredEffectiveCapabilities;
            state.CommittedAllChunkCapabilities =
                resolution.AllChunkCapabilities;
            state.CommittedExplicitChunkCapabilities.Clear();
            foreach (KeyValuePair<RegionChunkId, RegionCapabilitySet> pair in
                     resolution.ExplicitChunkCapabilities)
            {
                state.KnownChunks.Add(pair.Key);
                state.CommittedExplicitChunkCapabilities.Add(
                    pair.Key,
                    pair.Value);
            }

            state.CommittedChunkFidelity.Clear();
            foreach (
                KeyValuePair<
                    RegionChunkId,
                    RegionResolvedChunkFidelity> pair
                in state.DesiredChunkFidelity)
            {
                state.CommittedChunkFidelity.Add(
                    pair.Key,
                    pair.Value);
            }

            state.CandidateNodeCount = 0;
            state.Faulted = false;
            state.Diagnostic = state.OptionalDegraded
                ? RegionErrors.OptionalDegraded(
                    "The Region committed with one or more absent optional Participants.")
                : CoCoDiagnostic.None;
            LastDiagnostic = CoCoDiagnostic.None;
        }

        internal void PublishResolvedFidelity(
            RegionId regionId,
            long desiredGeneration,
            RegionTierId regionTierId,
            RegionCapabilitySet regionEffectiveCapabilities,
            IReadOnlyDictionary<
                RegionChunkId,
                RegionResolvedChunkFidelity> chunkFidelity)
        {
            if (!IsMainThread ||
                !regions.TryGetValue(
                    regionId,
                    out RegionDemandState state) ||
                state.DesiredGeneration != desiredGeneration)
            {
                return;
            }

            state.DesiredTierId = regionTierId;
            state.DesiredEffectiveCapabilities =
                regionEffectiveCapabilities ??
                RegionCapabilitySet.Empty;
            state.DesiredChunkFidelity.Clear();
            if (chunkFidelity == null) return;

            foreach (
                KeyValuePair<
                    RegionChunkId,
                    RegionResolvedChunkFidelity> pair
                in chunkFidelity)
            {
                state.KnownChunks.Add(pair.Key);
                state.DesiredChunkFidelity.Add(
                    pair.Key,
                    pair.Value);
            }
        }

        internal void PublishTransitionFailed(
            RegionId regionId,
            long desiredGeneration,
            CoCoDiagnostic diagnostic)
        {
            if (!IsMainThread ||
                !regions.TryGetValue(
                    regionId,
                    out RegionDemandState state) ||
                state.DesiredGeneration != desiredGeneration)
            {
                return;
            }

            CoCoDiagnostic failure = diagnostic.IsNone
                ? RegionErrors.TransitionFailed(
                    "The Region transition failed without a diagnostic.")
                : diagnostic;
            foreach (RegionDemandLease lease in state.Leases.Values)
            {
                if (!lease.IsReadyForCurrentRevision)
                {
                    lease.PublishFailed(lease.Revision, failure);
                }
            }

            state.CandidateNodeCount = 0;
            state.Faulted =
                failure.Code == CoCoDiagnosticCode.RegionCommitFaulted;
            state.BlockedCleanup =
                failure.Code == CoCoDiagnosticCode.RegionCleanupBlocked;
            state.Diagnostic = failure;
            LastDiagnostic = failure;
        }

        internal void PublishTransitionProgress(
            RegionId regionId,
            long desiredGeneration,
            IEnumerable<RegionChunkId> knownChunks,
            int reusedNodeCount,
            int candidateNodeCount,
            bool optionalDegraded,
            bool faulted,
            bool blockedCleanup,
            CoCoDiagnostic diagnostic)
        {
            if (!IsMainThread ||
                reusedNodeCount < 0 ||
                candidateNodeCount < 0 ||
                !regions.TryGetValue(
                    regionId,
                    out RegionDemandState state) ||
                state.DesiredGeneration != desiredGeneration)
            {
                return;
            }

            if (knownChunks != null)
            {
                foreach (RegionChunkId chunkId in knownChunks)
                {
                    if (chunkId.IsValid)
                    {
                        state.KnownChunks.Add(chunkId);
                    }
                }
            }

            state.ReusedNodeCount = reusedNodeCount;
            state.CandidateNodeCount = candidateNodeCount;
            state.OptionalDegraded = optionalDegraded;
            state.Faulted = faulted;
            state.BlockedCleanup = blockedCleanup;
            state.Diagnostic = diagnostic;
            if (!diagnostic.IsNone)
            {
                LastDiagnostic = diagnostic;
            }

            if (!state.Resolution.HasDemand &&
                state.CommittedGeneration == state.DesiredGeneration &&
                candidateNodeCount == 0 &&
                !faulted &&
                !blockedCleanup)
            {
                regions.Remove(regionId);
            }
        }

        private bool IsMainThread =>
            mainThreadId != 0 &&
            RegionMainThreadGuard.IsMainThread &&
            RegionMainThreadGuard.MainThreadId == mainThreadId;

        private bool TryValidateMutation(
            RegionDemandScope scope,
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out CoCoDiagnostic diagnostic)
        {
            if (!IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown || isDisposed)
            {
                diagnostic = RegionErrors.RuntimeDisposed();
                return false;
            }

            if (scope == null ||
                scope.IsDisposed ||
                !scopes.TryGetValue(
                    scope.OwnerId,
                    out RegionDemandScope currentScope) ||
                !ReferenceEquals(scope, currentScope))
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region Demand Scope is not owned by this runtime.");
                return false;
            }

            if (!regionId.IsValid)
            {
                diagnostic = RegionErrors.InvalidIdentifier(
                    "A Region Demand requires a valid Region id.");
                return false;
            }

            if (capabilities == null || capabilities.Count == 0)
            {
                diagnostic = RegionErrors.InvalidCapability(
                    "A Region Demand requires at least one capability; release the Lease to demand Off.");
                return false;
            }

            if (!coverage.IsValid)
            {
                diagnostic = RegionErrors.InvalidCoverage(
                    "A Region Demand requires All or a non-empty explicit Chunk set.");
                return false;
            }

            if (transitionSink == null)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region transition authority is not attached.");
                return false;
            }

            if (transitionSink.IsInvokingParticipantCallback)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "Region Demand cannot be mutated reentrantly from a Participant callback.");
                return false;
            }

            return transitionSink.TryValidateDemand(
                regionId,
                capabilities,
                coverage,
                out diagnostic);
        }

        private void RecomputeAndDispatch(RegionDemandState state)
        {
            foreach (RegionDemandLease lease in state.Leases.Values)
            {
                if (!lease.IsReadyForCurrentRevision)
                {
                    lease.PublishPending();
                }
            }

            state.DesiredGeneration = NextDesiredGeneration(
                state.DesiredGeneration);
            state.Resolution = BuildResolution(state);
            LastDiagnostic = CoCoDiagnostic.None;
            if (IsTemporalDispatchDeferred)
            {
                deferredTransitionRegions.Add(state.RegionId);
                return;
            }

            DispatchResolution(state);
        }

        private void DispatchResolution(RegionDemandState state)
        {
            try
            {
                transitionSink.RequestTransition(state.Resolution);
            }
            catch (Exception exception)
            {
                LastDiagnostic = RegionErrors.TransitionFailed(
                    "The Region transition authority rejected Demand resolution. " +
                    exception.Message);
                PublishTransitionFailed(
                    state.RegionId,
                    state.DesiredGeneration,
                    LastDiagnostic);
            }
        }

        private static RegionDemandResolution BuildResolution(
            RegionDemandState state)
        {
            RegionCapabilitySet regionCapabilities =
                RegionCapabilitySet.Empty;
            RegionCapabilitySet allChunkCapabilities =
                RegionCapabilitySet.Empty;
            var explicitCapabilities =
                new Dictionary<RegionChunkId, RegionCapabilitySet>();
            var coverageChunks = new HashSet<RegionChunkId>();
            var snapshots = new List<RegionDemandLeaseSnapshot>(
                state.Leases.Count);
            bool coversAll = false;

            foreach (RegionDemandLease lease in state.Leases.Values)
            {
                RegionCapabilitySet capabilities = lease.Capabilities;
                RegionCoverage coverage = lease.Coverage;
                regionCapabilities = regionCapabilities.Union(capabilities);
                snapshots.Add(
                    new RegionDemandLeaseSnapshot(
                        lease.LeaseSequence,
                        lease.Revision,
                        capabilities,
                        coverage));

                if (coverage.CoversAll)
                {
                    coversAll = true;
                    allChunkCapabilities =
                        allChunkCapabilities.Union(capabilities);
                    continue;
                }

                for (int index = 0; index < coverage.Chunks.Count; index++)
                {
                    RegionChunkId chunkId = coverage.Chunks[index];
                    coverageChunks.Add(chunkId);
                    explicitCapabilities.TryGetValue(
                        chunkId,
                        out RegionCapabilitySet existing);
                    explicitCapabilities[chunkId] =
                        (existing ?? RegionCapabilitySet.Empty).Union(
                            capabilities);
                }
            }

            snapshots.Sort(
                (left, right) =>
                    left.LeaseSequence.CompareTo(right.LeaseSequence));
            RegionCoverage mergedCoverage = default;
            if (snapshots.Count > 0)
            {
                if (coversAll)
                {
                    mergedCoverage = RegionCoverage.All;
                }
                else
                {
                    RegionCoverage.TryCreateChunks(
                        coverageChunks,
                        out mergedCoverage);
                }
            }

            return new RegionDemandResolution(
                state.RegionId,
                state.DesiredGeneration,
                regionCapabilities,
                mergedCoverage,
                allChunkCapabilities,
                explicitCapabilities,
                snapshots);
        }

        private void ReleaseDemandOnMainThread(RegionDemandLease lease)
        {
            if (!regions.TryGetValue(
                    lease.RegionId,
                    out RegionDemandState state) ||
                !state.Leases.TryGetValue(
                    lease.LeaseSequence,
                    out RegionDemandLease current) ||
                !ReferenceEquals(current, lease))
            {
                return;
            }

            state.Leases.Remove(lease.LeaseSequence);
            if (isShuttingDown || isDisposed)
            {
                if (state.Leases.Count == 0)
                {
                    regions.Remove(state.RegionId);
                }

                return;
            }

            RecomputeAndDispatch(state);
        }

        private async UniTaskVoid ReleaseDemandOnMainThreadAsync(
            RegionDemandLease lease)
        {
            if (IsMainThread &&
                transitionSink != null &&
                transitionSink.IsInvokingParticipantCallback)
            {
                await UniTask.Yield();
            }

            await UniTask.SwitchToMainThread();
            ReleaseDemandOnMainThread(lease);
        }

        private void RemoveScope(RegionDemandScope scope)
        {
            if (scopes.TryGetValue(
                    scope.OwnerId,
                    out RegionDemandScope current) &&
                ReferenceEquals(current, scope))
            {
                scopes.Remove(scope.OwnerId);
            }
        }

        private async UniTaskVoid ReleaseScopeOnMainThreadAsync(
            RegionDemandScope scope)
        {
            if (IsMainThread &&
                transitionSink != null &&
                transitionSink.IsInvokingParticipantCallback)
            {
                await UniTask.Yield();
            }

            await UniTask.SwitchToMainThread();
            RemoveScope(scope);
        }

        private async UniTask CompleteShutdownAsync(
            TaskCompletionSource<CoCoDiagnostic> completion)
        {
            CoCoDiagnostic diagnostic = CoCoDiagnostic.None;
            try
            {
                isShuttingDown = true;
                InvalidateTemporalBarrierNoDispatch();
                DisposeLiveScopes();
                if (transitionSink != null)
                {
                    diagnostic = await transitionSink.ShutdownAsync();
                    await UniTask.SwitchToMainThread();
                }

                if (!isDisposed)
                {
                    FinalizeDisposedState();
                }
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                diagnostic = RegionErrors.CleanupBlocked(
                    "Map Region shutdown required a terminal fallback. " +
                    exception.Message);
                ForceShutdown(diagnostic);
            }

            if (!completion.Task.IsCompleted)
            {
                LastDiagnostic = diagnostic;
                completion.TrySetResult(diagnostic);
            }
        }

        internal void ForceShutdown()
        {
            ForceShutdown(CoCoDiagnostic.None);
        }

        internal void ForceShutdown(CoCoDiagnostic terminalDiagnostic)
        {
            EnsureSharedShutdownTask();
            if (isDisposed)
            {
                shutdownCompletion.TrySetResult(
                    !LastDiagnostic.IsNone
                        ? LastDiagnostic
                        : terminalDiagnostic);
                return;
            }

            isShuttingDown = true;
            InvalidateTemporalBarrierNoDispatch();
            DisposeLiveScopes();
            transitionSink?.ForceShutdown();
            if (!terminalDiagnostic.IsNone)
            {
                LastDiagnostic = terminalDiagnostic;
            }

            FinalizeDisposedState();
            shutdownCompletion.TrySetResult(
                !LastDiagnostic.IsNone
                    ? LastDiagnostic
                    : CoCoDiagnostic.None);
        }

        private void ReleaseTemporalBarrier(long token)
        {
            if (!IsMainThread ||
                token == 0L ||
                temporalBarrierToken != token)
            {
                return;
            }

            temporalBarrierToken = 0L;
            if (!isShuttingDown && !isDisposed)
            {
                temporalFlushScheduled = true;
            }
        }

        private void InvalidateTemporalBarrierNoDispatch()
        {
            temporalBarrierToken = 0L;
            temporalFlushScheduled = false;
            deferredTransitionRegions.Clear();
        }

        private void DisposeLiveScopes()
        {
            RegionDemandScope[] liveScopes =
                new RegionDemandScope[scopes.Count];
            scopes.Values.CopyTo(liveScopes, 0);
            for (int index = 0; index < liveScopes.Length; index++)
            {
                liveScopes[index].Dispose();
            }

            scopes.Clear();
        }

        private void FinalizeDisposedState()
        {
            scopes.Clear();
            regions.Clear();
            isDisposed = true;
            contentRuntime.UnregisterShutdownParticipant(
                contentShutdownParticipant);
        }

        private void EnsureSharedShutdownTask()
        {
            if (shutdownTask != null) return;

            shutdownCompletion =
                new TaskCompletionSource<CoCoDiagnostic>();
            shutdownTask = shutdownCompletion.Task;
        }

        private long NextScopeSequence()
        {
            long sequence = nextScopeSequence++;
            if (nextScopeSequence <= 0L) nextScopeSequence = 1L;
            return sequence;
        }

        private long NextLeaseSequence()
        {
            long sequence = nextLeaseSequence++;
            if (nextLeaseSequence <= 0L) nextLeaseSequence = 1L;
            return sequence;
        }

        private RegionDemandRevision NextRevision()
        {
            long revision = nextDemandRevision++;
            if (nextDemandRevision <= 0L) nextDemandRevision = 1L;
            return new RegionDemandRevision(revision);
        }

        private long NextTemporalBarrierToken()
        {
            long token = nextTemporalBarrierToken++;
            if (nextTemporalBarrierToken <= 0L)
            {
                nextTemporalBarrierToken = 1L;
            }

            return token;
        }

        private static long NextDesiredGeneration(long current)
        {
            current++;
            return current <= 0L ? 1L : current;
        }

        private static async UniTask<T> AwaitSharedTaskAsync<T>(Task<T> task)
        {
            return await task;
        }

        internal sealed class RegionTemporalBarrier : IDisposable
        {
            private RegionRuntime runtime;
            private readonly long token;

            internal RegionTemporalBarrier(
                RegionRuntime runtime,
                long token)
            {
                this.runtime = runtime;
                this.token = token;
            }

            public void Dispose()
            {
                RegionRuntime owner =
                    Interlocked.Exchange(ref runtime, null);
                owner?.ReleaseTemporalBarrier(token);
            }
        }
    }
}
