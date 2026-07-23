using System;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Content
{
    public enum ContentAcquireStatus
    {
        None = 0,
        Succeeded = 1,
        Cancelled = 2,
        Failed = 3
    }

    public readonly struct ContentAcquireResult<T>
    {
        private ContentAcquireResult(
            ContentAcquireStatus status,
            ContentLease<T> lease,
            CoCoDiagnostic diagnostic)
        {
            Status = status;
            Lease = lease;
            Diagnostic = diagnostic;
        }

        public ContentAcquireStatus Status { get; }
        public ContentLease<T> Lease { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public bool Succeeded => Status == ContentAcquireStatus.Succeeded && Lease != null;
        public bool Cancelled => Status == ContentAcquireStatus.Cancelled;

        internal static ContentAcquireResult<T> Success(ContentLease<T> lease) =>
            new ContentAcquireResult<T>(ContentAcquireStatus.Succeeded, lease, CoCoDiagnostic.None);

        internal static ContentAcquireResult<T> Failure(CoCoDiagnostic diagnostic) =>
            new ContentAcquireResult<T>(ContentAcquireStatus.Failed, null, diagnostic);

        internal static ContentAcquireResult<T> Cancellation(CoCoDiagnostic diagnostic) =>
            new ContentAcquireResult<T>(ContentAcquireStatus.Cancelled, null, diagnostic);
    }

    public abstract class ContentLease : IDisposable
    {
        private Action<ContentLease, string> release;
        private readonly bool captureReleaseStack;
        private int released;

        internal ContentLease(
            ContentId id,
            ContentOwnerId ownerId,
            long scopeSequence,
            long leaseSequence,
            long resourceGeneration,
            string allocationStack,
            bool captureReleaseStack,
            Action<ContentLease, string> release)
        {
            Id = id;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            LeaseSequence = leaseSequence;
            ResourceGeneration = resourceGeneration;
            AllocationStack = allocationStack ?? string.Empty;
            this.captureReleaseStack = captureReleaseStack;
            this.release = release;
        }

        public ContentId Id { get; }
        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public long LeaseSequence { get; }
        public long ResourceGeneration { get; }
        public string AllocationStack { get; }
        public bool IsReleased => Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;

            ClearValue();
            Action<ContentLease, string> callback = Interlocked.Exchange(ref release, null);
            callback?.Invoke(this, captureReleaseStack ? Environment.StackTrace : string.Empty);
        }

        protected abstract void ClearValue();
    }

    public sealed class ContentLease<T> : ContentLease
    {
        private T value;

        internal ContentLease(
            ContentId id,
            ContentOwnerId ownerId,
            long scopeSequence,
            long leaseSequence,
            long resourceGeneration,
            T value,
            string allocationStack,
            bool captureReleaseStack,
            Action<ContentLease, string> release)
            : base(
                id,
                ownerId,
                scopeSequence,
                leaseSequence,
                resourceGeneration,
                allocationStack,
                captureReleaseStack,
                release)
        {
            this.value = value;
        }

        /// <summary>
        /// Gets the acquired value while this lease is live. Disposing the lease
        /// clears this reference and subsequent reads return the default value.
        /// </summary>
        public T Value => value;

        protected override void ClearValue()
        {
            value = default;
        }
    }

    public sealed class ContentScope : IDisposable
    {
        private readonly ContentRuntime runtime;
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private readonly object leaseGate = new object();
        private readonly List<ContentLease> leases = new List<ContentLease>();
        private int disposed;
        private int activeRequests;
        private bool lifetimeCancellationCompleted;
        private bool lifetimeCancellationDisposed;

        internal ContentScope(
            ContentRuntime runtime,
            ContentOwnerId ownerId,
            long scopeSequence)
        {
            this.runtime = runtime;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
        }

        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public UniTask<ContentAcquireResult<T>> AcquireAssetAsync<T>(
            ContentReference reference,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object =>
            AcquireAsync<T>(reference, ContentKind.Asset, cancellationToken);

        public UniTask<ContentAcquireResult<GameObject>> AcquirePrefabSourceAsync(
            ContentReference reference,
            CancellationToken cancellationToken = default) =>
            AcquireAsync<GameObject>(reference, ContentKind.PrefabSource, cancellationToken);

        public UniTask<ContentAcquireResult<Scene>> AcquireAdditiveSceneAsync(
            ContentReference reference,
            CancellationToken cancellationToken = default) =>
            AcquireAsync<Scene>(reference, ContentKind.AdditiveScene, cancellationToken);

        private async UniTask<ContentAcquireResult<T>> AcquireAsync<T>(
            ContentReference reference,
            ContentKind expectedKind,
            CancellationToken cancellationToken)
        {
            if (!TryBeginRequest())
            {
                return ContentAcquireResult<T>.Cancellation(ContentErrors.ScopeDisposed(OwnerId));
            }

            try
            {
                using (CancellationTokenSource requestCancellation =
                       CancellationTokenSource.CreateLinkedTokenSource(
                           lifetimeCancellation.Token,
                           cancellationToken))
                {
                    ContentAcquireResult<T> result = await runtime.AcquireAsync<T>(
                        this,
                        reference,
                        expectedKind,
                        requestCancellation.Token);
                    if (!result.Succeeded) return result;

                    bool accepted;
                    lock (leaseGate)
                    {
                        accepted = !IsDisposed;
                        if (accepted) leases.Add(result.Lease);
                    }

                    if (accepted) return result;

                    result.Lease.Dispose();
                    return ContentAcquireResult<T>.Cancellation(
                        ContentErrors.ScopeDisposed(OwnerId));
                }
            }
            finally
            {
                EndRequest();
            }
        }

        public void Dispose()
        {
            Dispose(null);
        }

        internal void Dispose(Action beforeLifetimeCancellation)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;

            ContentLease[] ownedLeases;
            lock (leaseGate)
            {
                ownedLeases = leases.ToArray();
                leases.Clear();
            }

            bool disposeCancellation = false;
            try
            {
                beforeLifetimeCancellation?.Invoke();
                lifetimeCancellation.Cancel();
            }
            finally
            {
                lock (leaseGate)
                {
                    lifetimeCancellationCompleted = true;
                    disposeCancellation = activeRequests == 0 &&
                                          !lifetimeCancellationDisposed;
                    if (disposeCancellation) lifetimeCancellationDisposed = true;
                }

                try
                {
                    foreach (ContentLease lease in ownedLeases)
                    {
                        lease.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        if (disposeCancellation) lifetimeCancellation.Dispose();
                    }
                    finally
                    {
                        runtime.OnScopeDisposed(this);
                    }
                }
            }
        }

        internal void OnLeaseReleased(ContentLease lease)
        {
            lock (leaseGate)
            {
                leases.Remove(lease);
            }
        }

        private bool TryBeginRequest()
        {
            lock (leaseGate)
            {
                if (IsDisposed) return false;

                activeRequests++;
                return true;
            }
        }

        private void EndRequest()
        {
            bool disposeCancellation = false;
            lock (leaseGate)
            {
                activeRequests--;
                if (activeRequests == 0 &&
                    IsDisposed &&
                    lifetimeCancellationCompleted &&
                    !lifetimeCancellationDisposed)
                {
                    lifetimeCancellationDisposed = true;
                    disposeCancellation = true;
                }
            }

            if (disposeCancellation) lifetimeCancellation.Dispose();
        }
    }
}
