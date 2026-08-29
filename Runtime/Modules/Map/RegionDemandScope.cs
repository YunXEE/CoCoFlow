using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Modules.Map
{
    public sealed class RegionDemandScope : IDisposable
    {
        private readonly RegionRuntime runtime;
        private readonly Dictionary<long, RegionDemandLease> leases =
            new Dictionary<long, RegionDemandLease>();
        private int disposed;

        internal RegionDemandScope(
            RegionRuntime runtime,
            RegionDemandOwnerId ownerId,
            long scopeSequence)
        {
            this.runtime = runtime ??
                           throw new ArgumentNullException(nameof(runtime));
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
        }

        public RegionDemandOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public bool TryDemand(
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out RegionDemandLease lease,
            out RegionDemandRevision revision,
            out CoCoDiagnostic diagnostic)
        {
            lease = null;
            revision = default;
            if (IsDisposed)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region Demand Scope is disposed.");
                return false;
            }

            if (!RegionMainThreadGuard.IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            return runtime.TryCreateDemand(
                this,
                regionId,
                capabilities,
                coverage,
                out lease,
                out revision,
                out diagnostic);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;

            RegionDemandLease[] owned;
            lock (leases)
            {
                owned = new RegionDemandLease[leases.Count];
                leases.Values.CopyTo(owned, 0);
                leases.Clear();
            }

            for (int index = 0; index < owned.Length; index++)
            {
                owned[index].DisposeFromScope();
            }

            runtime.ReleaseScope(this);
        }

        internal bool TryAddLease(RegionDemandLease lease)
        {
            if (lease == null || IsDisposed) return false;
            lock (leases)
            {
                if (IsDisposed || leases.ContainsKey(lease.LeaseSequence))
                {
                    return false;
                }

                leases.Add(lease.LeaseSequence, lease);
                return true;
            }
        }

        internal void RemoveLease(RegionDemandLease lease)
        {
            if (lease == null) return;
            lock (leases)
            {
                if (leases.TryGetValue(
                        lease.LeaseSequence,
                        out RegionDemandLease current) &&
                    ReferenceEquals(current, lease))
                {
                    leases.Remove(lease.LeaseSequence);
                }
            }
        }
    }

    public sealed class RegionDemandLease : IDisposable
    {
        private sealed class ReadinessWaiter
        {
            private readonly TaskCompletionSource<RegionReadinessResult> completion =
                new TaskCompletionSource<RegionReadinessResult>();
            private CancellationTokenRegistration cancellationRegistration;
            private int completed;

            internal ReadinessWaiter(
                RegionDemandRevision revision,
                CancellationToken cancellationToken)
            {
                Revision = revision;
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationRegistration = cancellationToken.Register(
                        CompleteCancelled);
                    if (IsCompleted)
                    {
                        cancellationRegistration.Dispose();
                    }
                }
            }

            internal RegionDemandRevision Revision { get; }
            internal bool IsCompleted => Volatile.Read(ref completed) != 0;
            internal Task<RegionReadinessResult> Task => completion.Task;

            internal bool TryComplete(
                RegionReadinessStatus status,
                CoCoDiagnostic diagnostic)
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return false;

                cancellationRegistration.Dispose();
                completion.TrySetResult(
                    new RegionReadinessResult(
                        Revision,
                        status,
                        diagnostic));
                return true;
            }

            private void CompleteCancelled()
            {
                TryComplete(
                    RegionReadinessStatus.Cancelled,
                    CoCoDiagnostic.None);
            }
        }

        private readonly RegionRuntime runtime;
        private readonly RegionDemandScope scope;
        private readonly object stateGate = new object();
        private readonly List<ReadinessWaiter> waiters =
            new List<ReadinessWaiter>();
        private RegionCapabilitySet capabilities;
        private RegionCoverage coverage;
        private RegionDemandRevision revision;
        private RegionReadinessStatus? terminalReadiness;
        private CoCoDiagnostic terminalDiagnostic;
        private int disposed;

        internal RegionDemandLease(
            RegionRuntime runtime,
            RegionDemandScope scope,
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            long leaseSequence,
            RegionDemandRevision revision)
        {
            this.runtime = runtime ??
                           throw new ArgumentNullException(nameof(runtime));
            this.scope = scope ??
                         throw new ArgumentNullException(nameof(scope));
            RegionId = regionId;
            this.capabilities = capabilities ??
                                throw new ArgumentNullException(nameof(capabilities));
            this.coverage = coverage;
            LeaseSequence = leaseSequence;
            this.revision = revision;
        }

        public RegionDemandOwnerId OwnerId => scope.OwnerId;
        public long ScopeSequence => scope.ScopeSequence;
        public long LeaseSequence { get; }
        public RegionId RegionId { get; }
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        internal RegionDemandScope Scope => scope;

        internal bool IsReadyForCurrentRevision
        {
            get
            {
                lock (stateGate)
                {
                    return !IsDisposed &&
                           terminalReadiness == RegionReadinessStatus.Ready;
                }
            }
        }

        internal RegionReadinessStatus? CurrentReadinessStatus
        {
            get
            {
                lock (stateGate)
                {
                    return IsDisposed
                        ? RegionReadinessStatus.Disposed
                        : terminalReadiness;
                }
            }
        }

        internal CoCoDiagnostic CurrentDiagnostic
        {
            get
            {
                lock (stateGate)
                {
                    return terminalDiagnostic;
                }
            }
        }

        public RegionCapabilitySet Capabilities
        {
            get
            {
                lock (stateGate)
                {
                    return capabilities;
                }
            }
        }

        public RegionCoverage Coverage
        {
            get
            {
                lock (stateGate)
                {
                    return coverage;
                }
            }
        }

        public RegionDemandRevision Revision
        {
            get
            {
                lock (stateGate)
                {
                    return revision;
                }
            }
        }

        public bool TryUpdate(
            RegionCapabilitySet updatedCapabilities,
            RegionCoverage updatedCoverage,
            out RegionDemandRevision updatedRevision,
            out CoCoDiagnostic diagnostic)
        {
            updatedRevision = Revision;
            if (IsDisposed)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The Region Demand Lease is disposed.");
                return false;
            }

            if (!RegionMainThreadGuard.IsMainThread)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                return false;
            }

            return runtime.TryUpdateDemand(
                this,
                updatedCapabilities,
                updatedCoverage,
                out updatedRevision,
                out diagnostic);
        }

        public UniTask<RegionReadinessResult> WaitUntilReadyAsync(
            RegionDemandRevision expectedRevision,
            CancellationToken cancellationToken = default)
        {
            ReadinessWaiter waiter;
            lock (stateGate)
            {
                if (IsDisposed)
                {
                    return UniTask.FromResult(
                        new RegionReadinessResult(
                            expectedRevision,
                            RegionReadinessStatus.Disposed,
                            RegionErrors.DemandConflict(
                                "The Region Demand Lease is disposed.")));
                }

                if (!expectedRevision.IsValid ||
                    expectedRevision.Value > revision.Value)
                {
                    return UniTask.FromResult(
                        new RegionReadinessResult(
                            expectedRevision,
                            RegionReadinessStatus.Failed,
                            RegionErrors.DemandConflict(
                                "The requested Region Demand revision is invalid or was never issued by this Lease.")));
                }

                if (expectedRevision != revision)
                {
                    return UniTask.FromResult(
                        new RegionReadinessResult(
                            expectedRevision,
                            RegionReadinessStatus.Superseded,
                            RegionErrors.DemandSuperseded(expectedRevision)));
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return UniTask.FromResult(
                        new RegionReadinessResult(
                            expectedRevision,
                            RegionReadinessStatus.Cancelled,
                            CoCoDiagnostic.None));
                }

                if (terminalReadiness.HasValue)
                {
                    return UniTask.FromResult(
                        new RegionReadinessResult(
                            expectedRevision,
                            terminalReadiness.Value,
                            terminalDiagnostic));
                }

                PruneCompletedWaitersNoLock();
                waiter = new ReadinessWaiter(
                    expectedRevision,
                    cancellationToken);
                waiters.Add(waiter);
            }

            return AwaitWaiterAsync(waiter);
        }

        public void Dispose()
        {
            DisposeCore(false, true);
        }

        internal bool TryApplyUpdate(
            RegionCapabilitySet updatedCapabilities,
            RegionCoverage updatedCoverage,
            RegionDemandRevision updatedRevision)
        {
            lock (stateGate)
            {
                if (IsDisposed || !updatedRevision.IsValid)
                {
                    return false;
                }

                RegionDemandRevision previousRevision = revision;
                capabilities = updatedCapabilities;
                coverage = updatedCoverage;
                revision = updatedRevision;
                terminalReadiness = null;
                terminalDiagnostic = CoCoDiagnostic.None;
                CompleteWaitersNoLock(
                    previousRevision,
                    RegionReadinessStatus.Superseded,
                    RegionErrors.DemandSuperseded(previousRevision));
                return true;
            }
        }

        internal void PublishPending()
        {
            lock (stateGate)
            {
                if (IsDisposed) return;
                terminalReadiness = null;
                terminalDiagnostic = CoCoDiagnostic.None;
            }
        }

        internal void PublishReady(RegionDemandRevision expectedRevision)
        {
            PublishTerminal(
                expectedRevision,
                RegionReadinessStatus.Ready,
                CoCoDiagnostic.None);
        }

        internal void PublishFailed(
            RegionDemandRevision expectedRevision,
            CoCoDiagnostic diagnostic)
        {
            PublishTerminal(
                expectedRevision,
                RegionReadinessStatus.Failed,
                diagnostic.IsNone
                    ? RegionErrors.TransitionFailed(
                        "The Region transition failed without a diagnostic.")
                    : diagnostic);
        }

        internal void DisposeFromScope()
        {
            DisposeCore(true, true);
        }

        internal void DisposeFromRuntimeRollback()
        {
            DisposeCore(true, false);
        }

        private void PublishTerminal(
            RegionDemandRevision expectedRevision,
            RegionReadinessStatus status,
            CoCoDiagnostic diagnostic)
        {
            lock (stateGate)
            {
                if (IsDisposed || revision != expectedRevision) return;

                terminalReadiness = status;
                terminalDiagnostic = diagnostic;
                CompleteWaitersNoLock(
                    expectedRevision,
                    status,
                    diagnostic);
            }
        }

        private void DisposeCore(
            bool scopeAlreadyRemovedLease,
            bool notifyRuntime)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;

            RegionDemandRevision disposedRevision;
            lock (stateGate)
            {
                disposedRevision = revision;
                terminalReadiness = null;
                terminalDiagnostic = CoCoDiagnostic.None;
                CompleteWaitersNoLock(
                    disposedRevision,
                    RegionReadinessStatus.Superseded,
                    RegionErrors.DemandSuperseded(disposedRevision));
            }

            if (!scopeAlreadyRemovedLease)
            {
                scope.RemoveLease(this);
            }

            if (notifyRuntime)
            {
                runtime.ReleaseDemand(this);
            }
        }

        private void CompleteWaitersNoLock(
            RegionDemandRevision expectedRevision,
            RegionReadinessStatus status,
            CoCoDiagnostic diagnostic)
        {
            for (int index = waiters.Count - 1; index >= 0; index--)
            {
                ReadinessWaiter waiter = waiters[index];
                if (waiter.IsCompleted)
                {
                    waiters.RemoveAt(index);
                    continue;
                }

                if (waiter.Revision == expectedRevision)
                {
                    waiter.TryComplete(status, diagnostic);
                    waiters.RemoveAt(index);
                }
            }
        }

        private void PruneCompletedWaitersNoLock()
        {
            for (int index = waiters.Count - 1; index >= 0; index--)
            {
                if (waiters[index].IsCompleted)
                {
                    waiters.RemoveAt(index);
                }
            }
        }

        private static async UniTask<RegionReadinessResult> AwaitWaiterAsync(
            ReadinessWaiter waiter)
        {
            return await waiter.Task;
        }
    }
}
