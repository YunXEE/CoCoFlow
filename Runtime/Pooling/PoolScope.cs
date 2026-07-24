using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    public sealed class PoolScope : IDisposable
    {
        private readonly PoolRuntime runtime;
        private readonly ContentScope contentScope;
        private readonly Dictionary<PoolId, PoolEntry> entries =
            new Dictionary<PoolId, PoolEntry>();
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private Task<CoCoDiagnostic> closeTask;
        private TaskCompletionSource<CoCoDiagnostic> closeCompletion;
        private bool closeStarted;
        private bool forceCloseRequested;

        internal PoolScope(
            PoolRuntime runtime,
            ContentScope contentScope,
            ContentOwnerId ownerId,
            long scopeSequence)
        {
            this.runtime = runtime;
            this.contentScope = contentScope;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            State = PoolScopeState.Open;
        }

        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public PoolScopeState State { get; private set; }
        public bool IsClosing => State != PoolScopeState.Open;

        internal PoolRuntime Runtime => runtime;
        internal Transform RetentionRoot => runtime.RetentionRoot;
        internal PoolDiagnosticLedger Ledger => runtime.Ledger;
        internal bool CaptureRentalStacks => runtime.CaptureRentalStacks;

        public async UniTask<PoolPrepareResult> PrepareAsync(
            PoolProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                return PoolPrepareResult.Failure(
                    profile.Id,
                    PoolingErrors.MainThreadRequired());
            }

            if (State != PoolScopeState.Open)
            {
                return PoolPrepareResult.Failure(
                    profile.Id,
                    PoolingErrors.ScopeClosing(profile.Id));
            }

            if (!profile.IsValid)
            {
                return PoolPrepareResult.Failure(
                    profile.Id,
                    PoolingErrors.InvalidProfile(
                        "PoolId, PrefabSource, and capacity values must be valid."));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PoolPrepareResult.Cancellation(
                    profile.Id,
                    PoolingErrors.Cancelled(profile.Id));
            }

            if (entries.TryGetValue(profile.Id, out PoolEntry existing))
            {
                if (!existing.Profile.Equals(profile))
                {
                    return PoolPrepareResult.Failure(
                        profile.Id,
                        PoolingErrors.ProfileConflict(profile.Id));
                }

                if (existing.State == PoolEntryState.Ready ||
                    existing.State == PoolEntryState.Prewarming)
                {
                    return PoolPrepareResult.Success(profile.Id, 0);
                }

                if (existing.State == PoolEntryState.Preparing)
                {
                    return await existing.AwaitPrepareAsync(cancellationToken);
                }

                return PoolPrepareResult.Failure(
                    profile.Id,
                    existing.State == PoolEntryState.Closing ||
                    existing.State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(profile.Id)
                        : PoolingErrors.NotReady(profile.Id));
            }

            var entry = new PoolEntry(
                this,
                profile,
                lifetimeCancellation.Token);
            entries.Add(profile.Id, entry);
            entry.StartPrepare(contentScope);
            return await entry.AwaitPrepareAsync(cancellationToken);
        }

        public UniTask<PoolPrewarmResult> PrewarmAsync(
            PoolId poolId,
            CancellationToken cancellationToken = default)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                return UniTask.FromResult(PoolPrewarmResult.Failure(
                    poolId,
                    0,
                    0,
                    PoolingErrors.MainThreadRequired()));
            }

            if (State != PoolScopeState.Open)
            {
                return UniTask.FromResult(PoolPrewarmResult.Failure(
                    poolId,
                    0,
                    0,
                    PoolingErrors.ScopeClosing(poolId)));
            }

            if (!poolId.IsValid)
            {
                return UniTask.FromResult(PoolPrewarmResult.Failure(
                    poolId,
                    0,
                    0,
                    PoolingErrors.InvalidId()));
            }

            bool hasEntry = entries.TryGetValue(poolId, out PoolEntry entry);
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromResult(PoolPrewarmResult.Cancellation(
                    poolId,
                    0,
                    hasEntry ? entry.InactiveCount : 0,
                    PoolingErrors.Cancelled(poolId)));
            }

            if (!hasEntry)
            {
                return UniTask.FromResult(PoolPrewarmResult.Failure(
                    poolId,
                    0,
                    0,
                    PoolingErrors.NotReady(poolId)));
            }

            return entry.AwaitPrewarmAsync(cancellationToken);
        }

        public bool TryRent(
            PoolId poolId,
            out PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            handle = default;
            if (!TryGetOperationalEntry(poolId, out PoolEntry entry, out diagnostic))
            {
                return false;
            }

            return entry.TryRent(out handle, out diagnostic);
        }

        public bool TryReturn(
            in PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (!ReferenceEquals(handle.Scope, this) ||
                handle.ScopeSequence != ScopeSequence)
            {
                diagnostic = PoolingErrors.OwnerMismatch(
                    handle.PoolId,
                    ScopeSequence);
                return false;
            }

            if (!entries.TryGetValue(handle.PoolId, out PoolEntry entry))
            {
                diagnostic = PoolingErrors.StaleHandle(
                    handle.PoolId,
                    handle.InstanceSequence);
                return false;
            }

            return entry.TryReturn(handle, out diagnostic);
        }

        public bool TryClearInactive(
            PoolId poolId,
            out CoCoDiagnostic diagnostic)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (!poolId.IsValid)
            {
                diagnostic = PoolingErrors.InvalidId();
                return false;
            }

            if (!entries.TryGetValue(poolId, out PoolEntry entry))
            {
                diagnostic = PoolingErrors.NotReady(poolId);
                return false;
            }

            return entry.TryClearInactive(out diagnostic);
        }

        public PoolScopeSnapshot CaptureSnapshot()
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                throw new InvalidOperationException(
                    "Pool Scope snapshots must be captured on the Unity main thread.");
            }

            var snapshots = new List<PoolEntrySnapshot>(entries.Count);
            foreach (PoolEntry entry in entries.Values)
            {
                snapshots.Add(entry.CaptureSnapshot());
            }

            snapshots.Sort((left, right) =>
                string.CompareOrdinal(left.PoolId.Value, right.PoolId.Value));
            return new PoolScopeSnapshot(
                OwnerId,
                ScopeSequence,
                State,
                snapshots.ToArray());
        }

        public UniTask<CoCoDiagnostic> CloseAsync()
        {
            if (closeStarted) return AwaitSharedTaskAsync(closeTask);
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                return UniTask.FromResult(PoolingErrors.MainThreadRequired());
            }

            closeCompletion = new TaskCompletionSource<CoCoDiagnostic>();
            closeTask = closeCompletion.Task;
            closeStarted = true;
            State = PoolScopeState.Closing;
            lifetimeCancellation.Cancel();

            PoolEntry[] liveEntries = CaptureEntries();
            foreach (PoolEntry entry in liveEntries)
            {
                entry.BeginClosing();
            }

            TryCompleteClose();
            return AwaitSharedTaskAsync(closeTask);
        }

        public void Dispose()
        {
            CloseAsync().Forget();
        }

        internal bool TryGetInstance(
            in PooledHandle handle,
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                instance = null;
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (!ReferenceEquals(handle.Scope, this) ||
                !entries.TryGetValue(handle.PoolId, out PoolEntry entry))
            {
                instance = null;
                diagnostic = PoolingErrors.OwnerMismatch(
                    handle.PoolId,
                    ScopeSequence);
                return false;
            }

            return entry.TryGetInstance(handle, out instance, out diagnostic);
        }

        internal bool TryActivate(
            in PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (!ReferenceEquals(handle.Scope, this) ||
                !entries.TryGetValue(handle.PoolId, out PoolEntry entry))
            {
                diagnostic = PoolingErrors.OwnerMismatch(
                    handle.PoolId,
                    ScopeSequence);
                return false;
            }

            return entry.TryActivate(handle, out diagnostic);
        }

        internal long AllocateInstanceSequence() =>
            runtime.AllocateInstanceSequence();

        internal void OnEntryPrepareFailed(PoolEntry entry)
        {
            if (entry == null) return;
            if (entries.TryGetValue(entry.Id, out PoolEntry current) &&
                ReferenceEquals(current, entry))
            {
                entries.Remove(entry.Id);
            }
        }

        internal void OnEntryClosed(PoolEntry entry)
        {
            if (!closeStarted || closeTask == null) return;
            TryCompleteClose();
        }

        internal void ForceClose()
        {
            if (State == PoolScopeState.Closed) return;

            if (closeCompletion == null)
            {
                closeCompletion = new TaskCompletionSource<CoCoDiagnostic>();
                closeTask = closeCompletion.Task;
            }

            closeStarted = true;
            forceCloseRequested = true;
            State = PoolScopeState.Closing;
            lifetimeCancellation.Cancel();
            PoolEntry[] live = CaptureEntries();
            foreach (PoolEntry entry in live)
            {
                entry.ForceClose();
            }

            TryCompleteClose();
        }

        internal bool TryAdoptTemporal(
            in PooledHandle handle,
            out PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            token = default;
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (State != PoolScopeState.Open ||
                !ReferenceEquals(handle.Scope, this) ||
                !entries.TryGetValue(handle.PoolId, out PoolEntry entry))
            {
                diagnostic = PoolingErrors.TemporalConflict(
                    "The handle does not belong to an open Pool Scope.");
                return false;
            }

            return entry.TryAdoptTemporal(handle, out token, out diagnostic);
        }

        internal bool TryGetTemporalEntry(
            in PoolTemporalToken token,
            out PoolEntry entry,
            out CoCoDiagnostic diagnostic)
        {
            entry = null;
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (!ReferenceEquals(token.Scope, this) ||
                !entries.TryGetValue(token.PoolId, out entry))
            {
                diagnostic = PoolingErrors.TemporalUnavailable(
                    "The token does not belong to this Pool Scope.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryGetOperationalEntry(
            PoolId poolId,
            out PoolEntry entry,
            out CoCoDiagnostic diagnostic)
        {
            entry = null;
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (State != PoolScopeState.Open)
            {
                diagnostic = PoolingErrors.ScopeClosing(poolId);
                return false;
            }

            if (!poolId.IsValid)
            {
                diagnostic = PoolingErrors.InvalidId();
                return false;
            }

            if (!entries.TryGetValue(poolId, out entry))
            {
                diagnostic = PoolingErrors.NotReady(poolId);
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void TryCompleteClose()
        {
            if (State != PoolScopeState.Closing) return;
            foreach (PoolEntry entry in entries.Values)
            {
                if (!entry.IsTerminal) return;
            }

            contentScope.Dispose();
            entries.Clear();
            lifetimeCancellation.Dispose();
            State = PoolScopeState.Closed;
            runtime.OnScopeClosed(this);

            closeCompletion?.TrySetResult(
                forceCloseRequested
                    ? PoolingErrors.ForcedShutdown()
                    : CoCoDiagnostic.None);
        }

        private PoolEntry[] CaptureEntries()
        {
            var snapshot = new PoolEntry[entries.Count];
            entries.Values.CopyTo(snapshot, 0);
            return snapshot;
        }

        private static async UniTask<T> AwaitSharedTaskAsync<T>(Task<T> task)
        {
            return await task;
        }
    }
}
