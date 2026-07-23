using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Content
{
    public sealed class ContentRuntime
    {
        private readonly struct RequestKey : IEquatable<RequestKey>
        {
            internal RequestKey(
                ContentId contentId,
                ContentKind kind,
                Type expectedType,
                ContentBackendId backendId,
                int backendGeneration)
            {
                ContentId = contentId;
                Kind = kind;
                ExpectedType = expectedType;
                BackendId = backendId;
                BackendGeneration = backendGeneration;
            }

            internal ContentId ContentId { get; }
            internal ContentKind Kind { get; }
            internal Type ExpectedType { get; }
            internal ContentBackendId BackendId { get; }
            internal int BackendGeneration { get; }

            public bool Equals(RequestKey other) =>
                ContentId.Equals(other.ContentId) &&
                Kind == other.Kind &&
                ExpectedType == other.ExpectedType &&
                BackendId.Equals(other.BackendId) &&
                BackendGeneration == other.BackendGeneration;

            public override bool Equals(object obj) => obj is RequestKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = ContentId.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)Kind;
                    hashCode = (hashCode * 397) ^ ExpectedType.GetHashCode();
                    hashCode = (hashCode * 397) ^ BackendId.GetHashCode();
                    hashCode = (hashCode * 397) ^ BackendGeneration;
                    return hashCode;
                }
            }
        }

        private sealed class RegisteredBackend
        {
            internal RegisteredBackend(IContentBackend backend, int generation)
            {
                Backend = backend;
                Generation = generation;
            }

            internal IContentBackend Backend { get; }
            internal int Generation { get; }
        }

        private sealed class Entry
        {
            internal Entry(
                RequestKey key,
                ContentReference reference,
                RegisteredBackend backend,
                long resourceGeneration,
                CancellationToken runtimeCancellation)
            {
                Key = key;
                Reference = reference;
                Backend = backend;
                ResourceGeneration = resourceGeneration;
                State = ContentEntryState.Loading;
                LoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    runtimeCancellation);
            }

            internal RequestKey Key { get; }
            internal ContentReference Reference { get; }
            internal RegisteredBackend Backend { get; }
            internal long ResourceGeneration { get; }
            internal CancellationTokenSource LoadCancellation { get; }
            internal ContentEntryState State { get; set; }
            internal ContentBackendResource Resource { get; set; }
            internal CoCoDiagnostic LastDiagnostic { get; set; }
            internal Task<ContentBackendLoadResult> LoadTask { get; set; }
            internal Task<CoCoDiagnostic> ReleaseTask { get; set; }
            internal int WaiterCount { get; set; }
            internal int LeaseCount { get; set; }
            internal bool Abandoned { get; set; }
            internal bool Removed { get; set; }
        }

        private readonly List<RegisteredBackend> backends;
        private readonly Dictionary<RequestKey, Entry> entries =
            new Dictionary<RequestKey, Entry>();
        private readonly HashSet<ContentScope> scopes = new HashSet<ContentScope>();
        private readonly object scopeGate = new object();
        private readonly CancellationTokenSource runtimeCancellation =
            new CancellationTokenSource();
        private readonly ContentDiagnosticLedger ledger;
        private readonly int mainThreadId;
        private readonly bool captureLeaseStacks;
        private long nextScopeSequence = 1;
        private long nextRequestSequence = 1;
        private long nextLeaseSequence = 1;
        private long nextResourceGeneration = 1;
        private bool shutdownStarted;
        private bool isShuttingDown;
        private bool isDisposed;
        private Task<CoCoDiagnostic> shutdownTask;

        private ContentRuntime(
            List<RegisteredBackend> backends,
            int diagnosticCapacity,
            bool captureLeaseStacks)
        {
            this.backends = backends;
            this.captureLeaseStacks = captureLeaseStacks;
            ledger = new ContentDiagnosticLedger(diagnosticCapacity);
            mainThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsShuttingDown => isShuttingDown;
        public bool IsDisposed => isDisposed;
        public bool CaptureLeaseStacks => captureLeaseStacks;

        public static bool TryCreate(
            IEnumerable<IContentBackend> additionalBackends,
            int diagnosticCapacity,
            bool captureLeaseStacks,
            out ContentRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (diagnosticCapacity <= 0)
            {
                diagnostic = ContentErrors.InvalidReference(
                    "Content diagnostic capacity must be greater than zero.");
                return false;
            }

            var registrations = new List<RegisteredBackend>();
            var backendIds = new HashSet<ContentBackendId>();
            if (!TryRegisterBackend(
                    new DirectContentBackend(),
                    registrations,
                    backendIds,
                    out diagnostic))
            {
                return false;
            }

            if (additionalBackends != null)
            {
                try
                {
                    foreach (IContentBackend backend in additionalBackends)
                    {
                        if (!TryRegisterBackend(
                                backend,
                                registrations,
                                backendIds,
                                out diagnostic))
                        {
                            return false;
                        }
                    }
                }
                catch (Exception exception)
                {
                    diagnostic = ContentErrors.LoadFailed(
                        default,
                        "Backend registration enumeration failed. " + exception.Message);
                    return false;
                }
            }

            runtime = new ContentRuntime(
                registrations,
                diagnosticCapacity,
                captureLeaseStacks);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public static bool TryCreate(
            out ContentRuntime runtime,
            out CoCoDiagnostic diagnostic) =>
            TryCreate(null, 256, false, out runtime, out diagnostic);

        public bool TryCreateScope(
            ContentOwnerId ownerId,
            out ContentScope scope,
            out CoCoDiagnostic diagnostic)
        {
            scope = null;
            if (!IsMainThread)
            {
                diagnostic = ContentErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown || isDisposed)
            {
                diagnostic = ContentErrors.RuntimeDisposed();
                return false;
            }

            if (!ownerId.IsValid)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Content,
                    CoCoDiagnosticCode.InvalidContentId,
                    "A Content Scope requires one valid ContentOwnerId.");
                return false;
            }

            scope = new ContentScope(this, ownerId, nextScopeSequence++);
            lock (scopeGate)
            {
                scopes.Add(scope);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public ContentRuntimeSnapshot CaptureSnapshot()
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException(
                    "Content Runtime snapshots must be captured on the Unity main thread.");
            }

            var entrySnapshots = new List<ContentEntrySnapshot>(entries.Count);
            foreach (Entry entry in entries.Values)
            {
                if (entry.Removed) continue;

                entrySnapshots.Add(new ContentEntrySnapshot(
                    entry.Key.ContentId,
                    entry.Key.Kind,
                    entry.Key.ExpectedType,
                    entry.Key.BackendId,
                    entry.Key.BackendGeneration,
                    entry.ResourceGeneration,
                    entry.State,
                    entry.WaiterCount,
                    entry.LeaseCount,
                    entry.LastDiagnostic));
            }

            entrySnapshots.Sort((left, right) =>
            {
                int idComparison = string.CompareOrdinal(
                    left.ContentId.Value,
                    right.ContentId.Value);
                if (idComparison != 0) return idComparison;

                int kindComparison = left.Kind.CompareTo(right.Kind);
                if (kindComparison != 0) return kindComparison;

                return string.CompareOrdinal(
                    left.ExpectedType.FullName,
                    right.ExpectedType.FullName);
            });

            return new ContentRuntimeSnapshot(
                isShuttingDown,
                entrySnapshots.ToArray(),
                ledger.Capture());
        }

        public UniTask<CoCoDiagnostic> ShutdownAsync()
        {
            if (shutdownStarted) return AwaitSharedTaskAsync(shutdownTask);
            if (!IsMainThread)
            {
                return UniTask.FromResult(ContentErrors.MainThreadRequired());
            }

            var completion = new TaskCompletionSource<CoCoDiagnostic>();
            shutdownTask = completion.Task;
            shutdownStarted = true;
            CompleteShutdownTaskAsync(completion).Forget();
            return AwaitSharedTaskAsync(shutdownTask);
        }

        internal async UniTask<ContentAcquireResult<T>> AcquireAsync<T>(
            ContentScope scope,
            ContentReference reference,
            ContentKind expectedKind,
            CancellationToken cancellationToken)
        {
            if (!IsMainThread)
            {
                return ContentAcquireResult<T>.Failure(ContentErrors.MainThreadRequired());
            }

            if (isShuttingDown || isDisposed)
            {
                return ContentAcquireResult<T>.Failure(ContentErrors.RuntimeDisposed());
            }

            if (!reference.IsValid || reference.Kind != expectedKind)
            {
                return ContentAcquireResult<T>.Failure(ContentErrors.InvalidReference(
                    "The ContentReference is invalid or has the wrong Content kind."));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ContentAcquireResult<T>.Cancellation(ContentErrors.Cancelled(reference.Id));
            }

            if (!TrySelectBackend(reference, out RegisteredBackend backend, out CoCoDiagnostic diagnostic))
            {
                return ContentAcquireResult<T>.Failure(diagnostic);
            }

            var key = new RequestKey(
                reference.Id,
                reference.Kind,
                typeof(T),
                backend.Backend.BackendId,
                backend.Generation);
            long requestSequence = nextRequestSequence++;
            ledger.Record(
                ContentDiagnosticEventKind.RequestStarted,
                reference.Id,
                scope.OwnerId,
                key.BackendId,
                key.BackendGeneration,
                0,
                requestSequence,
                0,
                CoCoDiagnostic.None,
                scopeSequence: scope.ScopeSequence);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    CoCoDiagnostic cancelled = ContentErrors.Cancelled(reference.Id);
                    ledger.Record(
                        ContentDiagnosticEventKind.RequestCancelled,
                        reference.Id,
                        scope.OwnerId,
                        key.BackendId,
                        key.BackendGeneration,
                        0,
                        requestSequence,
                        0,
                        cancelled,
                        scopeSequence: scope.ScopeSequence);
                    return ContentAcquireResult<T>.Cancellation(cancelled);
                }

                if (isShuttingDown || isDisposed)
                {
                    return ContentAcquireResult<T>.Failure(ContentErrors.RuntimeDisposed());
                }

                if (!entries.TryGetValue(key, out Entry entry))
                {
                    entry = new Entry(
                        key,
                        reference,
                        backend,
                        nextResourceGeneration++,
                        runtimeCancellation.Token)
                    {
                        WaiterCount = 1
                    };
                    entries.Add(key, entry);
                    StartLoad(entry);
                    return await AwaitLoadingEntryAsync<T>(
                        entry,
                        scope,
                        requestSequence,
                        cancellationToken);
                }

                if (!entry.Reference.Equals(reference))
                {
                    return ContentAcquireResult<T>.Failure(
                        ContentErrors.ReferenceConflict(reference.Id));
                }

                switch (entry.State)
                {
                    case ContentEntryState.Loaded:
                        return CreateLease<T>(entry, scope, requestSequence);
                    case ContentEntryState.Loading:
                        if (entry.Abandoned)
                        {
                            try
                            {
                                await AwaitSharedTaskAsync(entry.LoadTask)
                                    .AttachExternalCancellation(cancellationToken);
                                await UniTask.SwitchToMainThread();
                                if (!entry.Removed && entry.State == ContentEntryState.Releasing)
                                {
                                    await AwaitSharedTaskAsync(entry.ReleaseTask)
                                        .AttachExternalCancellation(cancellationToken);
                                    await UniTask.SwitchToMainThread();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                await UniTask.SwitchToMainThread();
                                CoCoDiagnostic cancelled = ContentErrors.Cancelled(reference.Id);
                                RecordCancellation(
                                    entry,
                                    scope.OwnerId,
                                    scope.ScopeSequence,
                                    requestSequence,
                                    cancelled);
                                return ContentAcquireResult<T>.Cancellation(cancelled);
                            }

                            if (entry.State == ContentEntryState.ReleaseFailed)
                            {
                                return ContentAcquireResult<T>.Failure(entry.LastDiagnostic);
                            }

                            continue;
                        }

                        entry.WaiterCount++;
                        return await AwaitLoadingEntryAsync<T>(
                            entry,
                            scope,
                            requestSequence,
                            cancellationToken);
                    case ContentEntryState.Releasing:
                        try
                        {
                            await AwaitSharedTaskAsync(entry.ReleaseTask)
                                .AttachExternalCancellation(cancellationToken);
                            await UniTask.SwitchToMainThread();
                        }
                        catch (OperationCanceledException)
                        {
                            await UniTask.SwitchToMainThread();
                            CoCoDiagnostic cancelled = ContentErrors.Cancelled(reference.Id);
                            RecordCancellation(
                                entry,
                                scope.OwnerId,
                                scope.ScopeSequence,
                                requestSequence,
                                cancelled);
                            return ContentAcquireResult<T>.Cancellation(cancelled);
                        }

                        if (entry.State == ContentEntryState.ReleaseFailed)
                        {
                            return ContentAcquireResult<T>.Failure(entry.LastDiagnostic);
                        }

                        continue;
                    case ContentEntryState.ReleaseFailed:
                        return ContentAcquireResult<T>.Failure(entry.LastDiagnostic);
                    default:
                        return ContentAcquireResult<T>.Failure(ContentErrors.LoadFailed(
                            reference.Id,
                            "The registry entry is in an unknown state."));
                }
            }
        }

        internal void OnScopeDisposed(ContentScope scope)
        {
            lock (scopeGate)
            {
                scopes.Remove(scope);
            }
        }

        private async UniTask<ContentAcquireResult<T>> AwaitLoadingEntryAsync<T>(
            Entry entry,
            ContentScope scope,
            long requestSequence,
            CancellationToken cancellationToken)
        {
            ContentBackendLoadResult loadResult;
            try
            {
                loadResult = await AwaitSharedTaskAsync(entry.LoadTask)
                    .AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                CoCoDiagnostic cancelled = ContentErrors.Cancelled(entry.Key.ContentId);
                RecordCancellation(
                    entry,
                    scope.OwnerId,
                    scope.ScopeSequence,
                    requestSequence,
                    cancelled);
                FinishWaiter(entry);
                return ContentAcquireResult<T>.Cancellation(cancelled);
            }

            await UniTask.SwitchToMainThread();
            try
            {
                if (!loadResult.Succeeded)
                {
                    return ContentAcquireResult<T>.Failure(loadResult.Diagnostic);
                }

                if (entry.State == ContentEntryState.ReleaseFailed)
                {
                    return ContentAcquireResult<T>.Failure(entry.LastDiagnostic);
                }

                if (entry.State != ContentEntryState.Loaded)
                {
                    return ContentAcquireResult<T>.Failure(ContentErrors.LoadFailed(
                        entry.Key.ContentId,
                        "The loaded resource was reclaimed before a lease was published."));
                }

                return CreateLease<T>(entry, scope, requestSequence);
            }
            finally
            {
                FinishWaiter(entry);
            }
        }

        private void StartLoad(Entry entry)
        {
            var completion = new TaskCompletionSource<ContentBackendLoadResult>();
            entry.LoadTask = completion.Task;
            CompleteLoadTaskAsync(entry, completion).Forget();
        }

        private async UniTask CompleteLoadTaskAsync(
            Entry entry,
            TaskCompletionSource<ContentBackendLoadResult> completion)
        {
            ContentBackendLoadResult result;
            try
            {
                result = await LoadEntryAsync(entry);
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                CoCoDiagnostic diagnostic = ContentErrors.LoadFailed(
                    entry.Key.ContentId,
                    "Content registry load finalization failed. " + exception.Message);
                if (!entry.Removed)
                {
                    entry.LastDiagnostic = diagnostic;
                    ledger.Record(
                        ContentDiagnosticEventKind.LoadFailed,
                        entry.Key.ContentId,
                        default,
                        entry.Key.BackendId,
                        entry.Key.BackendGeneration,
                        entry.ResourceGeneration,
                        0,
                        0,
                        diagnostic);
                    RemoveEntry(entry);
                }

                result = ContentBackendLoadResult.Failure(diagnostic);
            }

            completion.TrySetResult(result);
        }

        private async UniTask<ContentBackendLoadResult> LoadEntryAsync(Entry entry)
        {
            ContentBackendLoadResult result;
            try
            {
                result = await entry.Backend.Backend.LoadAsync(
                    new ContentBackendRequest(entry.Reference, entry.Key.ExpectedType),
                    entry.LoadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                result = ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                    entry.Key.ContentId,
                    "The backend cancelled its physical load."));
            }
            catch (Exception exception)
            {
                result = ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                    entry.Key.ContentId,
                    exception.Message));
            }

            await UniTask.SwitchToMainThread();

            if (!result.Succeeded || result.Resource == null)
            {
                CoCoDiagnostic failure = result.Diagnostic.IsError
                    ? result.Diagnostic
                    : ContentErrors.LoadFailed(
                        entry.Key.ContentId,
                        "The backend returned no resource.");
                entry.LastDiagnostic = failure;
                ledger.Record(
                    ContentDiagnosticEventKind.LoadFailed,
                    entry.Key.ContentId,
                    default,
                    entry.Key.BackendId,
                    entry.Key.BackendGeneration,
                    entry.ResourceGeneration,
                    0,
                    0,
                    failure);
                RemoveEntry(entry);
                return ContentBackendLoadResult.Failure(failure);
            }

            if (!entry.Key.ExpectedType.IsAssignableFrom(result.Resource.ValueType) ||
                result.Resource.Value == null)
            {
                CoCoDiagnostic mismatch = ContentErrors.TypeMismatch(
                    entry.Key.ContentId,
                    entry.Key.ExpectedType);
                CoCoDiagnostic releaseDiagnostic = await ReleaseUnpublishedResourceAsync(
                    entry,
                    result.Resource);
                await UniTask.SwitchToMainThread();
                if (!releaseDiagnostic.IsNone)
                {
                    entry.Resource = result.Resource;
                    entry.State = ContentEntryState.ReleaseFailed;
                    entry.LastDiagnostic = releaseDiagnostic;
                }
                else
                {
                    RemoveEntry(entry);
                }

                ledger.Record(
                    ContentDiagnosticEventKind.LoadFailed,
                    entry.Key.ContentId,
                    default,
                    entry.Key.BackendId,
                    entry.Key.BackendGeneration,
                    entry.ResourceGeneration,
                    0,
                    0,
                    mismatch);
                return ContentBackendLoadResult.Failure(mismatch);
            }

            entry.Resource = result.Resource;
            entry.State = ContentEntryState.Loaded;
            entry.LastDiagnostic = CoCoDiagnostic.None;
            ledger.Record(
                ContentDiagnosticEventKind.LoadSucceeded,
                entry.Key.ContentId,
                default,
                entry.Key.BackendId,
                entry.Key.BackendGeneration,
                entry.ResourceGeneration,
                0,
                0,
                CoCoDiagnostic.None);

            if (entry.Abandoned || isShuttingDown)
            {
                StartRelease(entry);
            }

            return result;
        }

        private ContentAcquireResult<T> CreateLease<T>(
            Entry entry,
            ContentScope scope,
            long requestSequence)
        {
            if (!(entry.Resource.Value is T value))
            {
                return ContentAcquireResult<T>.Failure(
                    ContentErrors.TypeMismatch(entry.Key.ContentId, typeof(T)));
            }

            long leaseSequence = nextLeaseSequence++;
            string allocationStack = captureLeaseStacks
                ? Environment.StackTrace
                : string.Empty;
            entry.LeaseCount++;
            var lease = new ContentLease<T>(
                entry.Key.ContentId,
                scope.OwnerId,
                scope.ScopeSequence,
                leaseSequence,
                entry.ResourceGeneration,
                value,
                allocationStack,
                captureLeaseStacks,
                (releasedLease, releaseStack) =>
                {
                    scope.OnLeaseReleased(releasedLease);
                    ReleaseLease(entry, releasedLease, requestSequence, releaseStack);
                });
            ledger.Record(
                ContentDiagnosticEventKind.LeaseCreated,
                entry.Key.ContentId,
                scope.OwnerId,
                entry.Key.BackendId,
                entry.Key.BackendGeneration,
                entry.ResourceGeneration,
                requestSequence,
                leaseSequence,
                CoCoDiagnostic.None,
                allocationStack,
                scopeSequence: scope.ScopeSequence);
            return ContentAcquireResult<T>.Success(lease);
        }

        private void FinishWaiter(Entry entry)
        {
            if (entry.WaiterCount > 0) entry.WaiterCount--;
            if (entry.Removed || entry.WaiterCount != 0 || entry.LeaseCount != 0) return;

            if (entry.State == ContentEntryState.Loading)
            {
                entry.Abandoned = true;
                entry.LoadCancellation.Cancel();
            }
            else if (entry.State == ContentEntryState.Loaded)
            {
                StartRelease(entry);
            }
        }

        private void ReleaseLease(
            Entry entry,
            ContentLease lease,
            long requestSequence,
            string releaseStack)
        {
            if (!IsMainThread)
            {
                ReleaseLeaseOnMainThreadAsync(
                    entry,
                    lease,
                    requestSequence,
                    releaseStack).Forget();
                return;
            }

            if (entry.Removed || entry.LeaseCount <= 0) return;

            entry.LeaseCount--;
            ledger.Record(
                ContentDiagnosticEventKind.LeaseReleased,
                entry.Key.ContentId,
                lease.OwnerId,
                entry.Key.BackendId,
                entry.Key.BackendGeneration,
                entry.ResourceGeneration,
                requestSequence,
                lease.LeaseSequence,
                CoCoDiagnostic.None,
                lease.AllocationStack,
                releaseStack,
                lease.ScopeSequence);
            if (entry.LeaseCount == 0 && entry.WaiterCount == 0 &&
                entry.State == ContentEntryState.Loaded)
            {
                StartRelease(entry);
            }
        }

        private async UniTask ReleaseLeaseOnMainThreadAsync(
            Entry entry,
            ContentLease lease,
            long requestSequence,
            string releaseStack)
        {
            await UniTask.SwitchToMainThread();
            ReleaseLease(entry, lease, requestSequence, releaseStack);
        }

        private void StartRelease(Entry entry)
        {
            if (entry.Removed || entry.State != ContentEntryState.Loaded) return;

            entry.State = ContentEntryState.Releasing;
            ledger.Record(
                ContentDiagnosticEventKind.ReleaseStarted,
                entry.Key.ContentId,
                default,
                entry.Key.BackendId,
                entry.Key.BackendGeneration,
                entry.ResourceGeneration,
                0,
                0,
                CoCoDiagnostic.None);
            var completion = new TaskCompletionSource<CoCoDiagnostic>();
            entry.ReleaseTask = completion.Task;
            CompleteReleaseTaskAsync(entry, completion).Forget();
        }

        private async UniTask CompleteReleaseTaskAsync(
            Entry entry,
            TaskCompletionSource<CoCoDiagnostic> completion)
        {
            CoCoDiagnostic diagnostic;
            try
            {
                diagnostic = await ReleaseEntryAsync(entry);
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                diagnostic = ContentErrors.ReleaseFailed(
                    entry.Key.ContentId,
                    "Content registry release finalization failed. " + exception.Message);
                if (!entry.Removed)
                {
                    entry.State = ContentEntryState.ReleaseFailed;
                    entry.LastDiagnostic = diagnostic;
                    ledger.Record(
                        ContentDiagnosticEventKind.ReleaseFailed,
                        entry.Key.ContentId,
                        default,
                        entry.Key.BackendId,
                        entry.Key.BackendGeneration,
                        entry.ResourceGeneration,
                        0,
                        0,
                        diagnostic);
                }
            }

            completion.TrySetResult(diagnostic);
        }

        private async UniTask<CoCoDiagnostic> ReleaseEntryAsync(Entry entry)
        {
            CoCoDiagnostic diagnostic;
            try
            {
                diagnostic = await entry.Resource.ReleaseAsync();
            }
            catch (Exception exception)
            {
                diagnostic = ContentErrors.ReleaseFailed(
                    entry.Key.ContentId,
                    exception.Message);
            }

            await UniTask.SwitchToMainThread();
            if (diagnostic.IsNone)
            {
                ledger.Record(
                    ContentDiagnosticEventKind.ReleaseSucceeded,
                    entry.Key.ContentId,
                    default,
                    entry.Key.BackendId,
                    entry.Key.BackendGeneration,
                    entry.ResourceGeneration,
                    0,
                    0,
                    CoCoDiagnostic.None);
                entry.Resource = null;
                RemoveEntry(entry);
                return CoCoDiagnostic.None;
            }

            if (!diagnostic.IsError)
            {
                diagnostic = ContentErrors.ReleaseFailed(
                    entry.Key.ContentId,
                    diagnostic.Message);
            }

            entry.State = ContentEntryState.ReleaseFailed;
            entry.LastDiagnostic = diagnostic;
            ledger.Record(
                ContentDiagnosticEventKind.ReleaseFailed,
                entry.Key.ContentId,
                default,
                entry.Key.BackendId,
                entry.Key.BackendGeneration,
                entry.ResourceGeneration,
                0,
                0,
                diagnostic);
            return diagnostic;
        }

        private async UniTask<CoCoDiagnostic> ReleaseUnpublishedResourceAsync(
            Entry entry,
            ContentBackendResource resource)
        {
            try
            {
                CoCoDiagnostic diagnostic = await resource.ReleaseAsync();
                return diagnostic.IsNone || diagnostic.IsError
                    ? diagnostic
                    : ContentErrors.ReleaseFailed(entry.Key.ContentId, diagnostic.Message);
            }
            catch (Exception exception)
            {
                return ContentErrors.ReleaseFailed(entry.Key.ContentId, exception.Message);
            }
        }

        private async UniTask<CoCoDiagnostic> ShutdownCoreAsync()
        {
            isShuttingDown = true;
            ContentScope[] liveScopes;
            lock (scopeGate)
            {
                liveScopes = new ContentScope[scopes.Count];
                scopes.CopyTo(liveScopes);
            }

            foreach (ContentScope scope in liveScopes)
            {
                scope.Dispose();
            }

            runtimeCancellation.Cancel();
            Entry[] loadingEntries = CaptureEntries();
            foreach (Entry entry in loadingEntries)
            {
                entry.Abandoned = true;
                entry.LoadCancellation.Cancel();
                if (entry.State == ContentEntryState.Loaded)
                {
                    entry.LeaseCount = 0;
                    StartRelease(entry);
                }
            }

            foreach (Entry entry in loadingEntries)
            {
                if (entry.State != ContentEntryState.Loading) continue;
                await entry.LoadTask;
                await UniTask.SwitchToMainThread();
                if (entry.State == ContentEntryState.Loaded)
                {
                    entry.LeaseCount = 0;
                    StartRelease(entry);
                }
            }

            Entry[] releasingEntries = CaptureEntries();
            foreach (Entry entry in releasingEntries)
            {
                if (entry.State == ContentEntryState.Releasing)
                {
                    await entry.ReleaseTask;
                    await UniTask.SwitchToMainThread();
                }
            }

            CoCoDiagnostic shutdownDiagnostic = CoCoDiagnostic.None;
            foreach (Entry entry in entries.Values)
            {
                if (entry.State != ContentEntryState.ReleaseFailed) continue;
                shutdownDiagnostic = entry.LastDiagnostic;
                break;
            }

            isDisposed = true;
            return shutdownDiagnostic;
        }

        private async UniTask CompleteShutdownTaskAsync(
            TaskCompletionSource<CoCoDiagnostic> completion)
        {
            CoCoDiagnostic diagnostic;
            try
            {
                diagnostic = await ShutdownCoreAsync();
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                diagnostic = ContentErrors.ReleaseFailed(
                    default,
                    "Content Runtime shutdown finalization failed. " + exception.Message);
                isShuttingDown = true;
                isDisposed = true;
            }

            completion.TrySetResult(diagnostic);
        }

        private static async UniTask<T> AwaitSharedTaskAsync<T>(Task<T> task)
        {
            return await task;
        }

        private Entry[] CaptureEntries()
        {
            var snapshot = new Entry[entries.Count];
            entries.Values.CopyTo(snapshot, 0);
            return snapshot;
        }

        private bool TrySelectBackend(
            ContentReference reference,
            out RegisteredBackend selected,
            out CoCoDiagnostic diagnostic)
        {
            selected = null;
            foreach (RegisteredBackend backend in backends)
            {
                bool canHandle;
                try
                {
                    canHandle = backend.Backend.CanHandle(reference);
                }
                catch (Exception exception)
                {
                    diagnostic = ContentErrors.LoadFailed(
                        reference.Id,
                        "Backend selection failed. " + exception.Message);
                    return false;
                }

                if (!canHandle) continue;
                if (selected != null)
                {
                    diagnostic = ContentErrors.BackendConflict(reference);
                    return false;
                }

                selected = backend;
            }

            if (selected == null)
            {
                diagnostic = ContentErrors.MissingBackend(reference);
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryRegisterBackend(
            IContentBackend backend,
            List<RegisteredBackend> registrations,
            HashSet<ContentBackendId> backendIds,
            out CoCoDiagnostic diagnostic)
        {
            if (backend == null || !backend.BackendId.IsValid)
            {
                diagnostic = ContentErrors.InvalidReference(
                    "Every Content backend requires one valid ContentBackendId.");
                return false;
            }

            if (!backendIds.Add(backend.BackendId))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Content,
                    CoCoDiagnosticCode.ContentBackendConflict,
                    "Content backend ID '" + backend.BackendId + "' is registered more than once.");
                return false;
            }

            registrations.Add(new RegisteredBackend(backend, registrations.Count + 1));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void RemoveEntry(Entry entry)
        {
            if (entry.Removed) return;
            if (entries.TryGetValue(entry.Key, out Entry current) && ReferenceEquals(current, entry))
            {
                entries.Remove(entry.Key);
            }

            entry.Removed = true;
            entry.LoadCancellation.Dispose();
        }

        private void RecordCancellation(
            Entry entry,
            ContentOwnerId ownerId,
            long scopeSequence,
            long requestSequence,
            CoCoDiagnostic diagnostic)
        {
            ledger.Record(
                ContentDiagnosticEventKind.RequestCancelled,
                entry.Key.ContentId,
                ownerId,
                entry.Key.BackendId,
                entry.Key.BackendGeneration,
                entry.ResourceGeneration,
                requestSequence,
                0,
                diagnostic,
                scopeSequence: scopeSequence);
        }

        private bool IsMainThread => Environment.CurrentManagedThreadId == mainThreadId;
    }
}
