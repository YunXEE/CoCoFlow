using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Pooling
{
    public enum PoolDiagnosticEventKind
    {
        ScopeCreated = 0,
        PrepareStarted = 1,
        PrepareSucceeded = 2,
        PrepareCancelled = 3,
        PrepareFailed = 4,
        PrewarmStarted = 5,
        PrewarmCompleted = 6,
        RentSucceeded = 7,
        ActivateSucceeded = 8,
        ReturnSucceeded = 9,
        InstanceCreated = 10,
        InstanceDestroyed = 11,
        ExternalDestroy = 12,
        HandleRejected = 13,
        LifecycleFailed = 14,
        InactiveCleared = 15,
        ScopeClosing = 16,
        ScopeClosed = 17,
        ForcedShutdown = 18,
        TemporalAdopted = 19,
        TemporalStateChanged = 20,
        TemporalReleased = 21
    }

    public readonly struct PoolEntrySnapshot
    {
        internal PoolEntrySnapshot(
            PoolId poolId,
            ContentId contentId,
            PoolEntryState state,
            int prewarmCount,
            int maxRetained,
            int activeCount,
            int inactiveCount,
            int temporalRetainedCount,
            int quarantineCount,
            int pendingDestroyCount,
            long createdCount,
            long destroyedCount,
            long rentCount,
            long idleHitCount,
            long createMissCount,
            long resetFailureCount,
            long externalDestroyCount,
            bool holdsSourceLease,
            CoCoDiagnostic diagnostic)
        {
            PoolId = poolId;
            ContentId = contentId;
            State = state;
            PrewarmCount = prewarmCount;
            MaxRetained = maxRetained;
            ActiveCount = activeCount;
            InactiveCount = inactiveCount;
            TemporalRetainedCount = temporalRetainedCount;
            QuarantineCount = quarantineCount;
            PendingDestroyCount = pendingDestroyCount;
            CreatedCount = createdCount;
            DestroyedCount = destroyedCount;
            RentCount = rentCount;
            IdleHitCount = idleHitCount;
            CreateMissCount = createMissCount;
            ResetFailureCount = resetFailureCount;
            ExternalDestroyCount = externalDestroyCount;
            HoldsSourceLease = holdsSourceLease;
            Diagnostic = diagnostic;
        }

        public PoolId PoolId { get; }
        public ContentId ContentId { get; }
        public PoolEntryState State { get; }
        public int PrewarmCount { get; }
        public int MaxRetained { get; }
        public int ActiveCount { get; }
        public int InactiveCount { get; }
        public int TemporalRetainedCount { get; }
        public int QuarantineCount { get; }
        public int PendingDestroyCount { get; }
        public long CreatedCount { get; }
        public long DestroyedCount { get; }
        public long RentCount { get; }
        public long IdleHitCount { get; }
        public long CreateMissCount { get; }
        public long ResetFailureCount { get; }
        public long ExternalDestroyCount { get; }
        public bool HoldsSourceLease { get; }
        public CoCoDiagnostic Diagnostic { get; }

        public float HitRate =>
            RentCount == 0 ? 0f : (float)IdleHitCount / RentCount;
    }

    public sealed class PoolScopeSnapshot
    {
        internal PoolScopeSnapshot(
            ContentOwnerId ownerId,
            long scopeSequence,
            PoolScopeState state,
            PoolEntrySnapshot[] entries)
        {
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            State = state;
            Entries = Array.AsReadOnly(entries ?? Array.Empty<PoolEntrySnapshot>());
        }

        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public PoolScopeState State { get; }
        public IReadOnlyList<PoolEntrySnapshot> Entries { get; }
    }

    public readonly struct PoolDiagnosticRecord
    {
        internal PoolDiagnosticRecord(
            long sequence,
            DateTime timestampUtc,
            PoolDiagnosticEventKind eventKind,
            ContentOwnerId ownerId,
            long scopeSequence,
            PoolId poolId,
            long instanceSequence,
            uint generation,
            PooledInstanceState instanceState,
            int activeCount,
            int inactiveCount,
            int quarantineCount,
            CoCoDiagnostic diagnostic,
            string allocationStack,
            string releaseStack)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            EventKind = eventKind;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            PoolId = poolId;
            InstanceSequence = instanceSequence;
            Generation = generation;
            InstanceState = instanceState;
            ActiveCount = activeCount;
            InactiveCount = inactiveCount;
            QuarantineCount = quarantineCount;
            Diagnostic = diagnostic;
            AllocationStack = allocationStack ?? string.Empty;
            ReleaseStack = releaseStack ?? string.Empty;
        }

        public long Sequence { get; }
        public DateTime TimestampUtc { get; }
        public PoolDiagnosticEventKind EventKind { get; }
        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public PoolId PoolId { get; }
        public long InstanceSequence { get; }
        public uint Generation { get; }
        public PooledInstanceState InstanceState { get; }
        public int ActiveCount { get; }
        public int InactiveCount { get; }
        public int QuarantineCount { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public string AllocationStack { get; }
        public string ReleaseStack { get; }
    }

    public sealed class PoolRuntimeSnapshot
    {
        internal PoolRuntimeSnapshot(
            bool isShuttingDown,
            bool isDisposed,
            PoolScopeSnapshot[] scopes,
            PoolDiagnosticRecord[] diagnostics)
        {
            IsShuttingDown = isShuttingDown;
            IsDisposed = isDisposed;
            Scopes = Array.AsReadOnly(scopes ?? Array.Empty<PoolScopeSnapshot>());
            Diagnostics = Array.AsReadOnly(
                diagnostics ?? Array.Empty<PoolDiagnosticRecord>());
        }

        public bool IsShuttingDown { get; }
        public bool IsDisposed { get; }
        public IReadOnlyList<PoolScopeSnapshot> Scopes { get; }
        public IReadOnlyList<PoolDiagnosticRecord> Diagnostics { get; }
    }

    internal sealed class PoolDiagnosticLedger
    {
        private readonly PoolDiagnosticRecord[] records;
        private int start;
        private int count;
        private long nextSequence = 1;

        internal PoolDiagnosticLedger(int capacity)
        {
            records = new PoolDiagnosticRecord[capacity];
        }

        internal void Record(
            PoolDiagnosticEventKind eventKind,
            ContentOwnerId ownerId,
            long scopeSequence,
            PoolId poolId,
            long instanceSequence,
            uint generation,
            PooledInstanceState instanceState,
            int activeCount,
            int inactiveCount,
            int quarantineCount,
            CoCoDiagnostic diagnostic,
            string allocationStack = null,
            string releaseStack = null)
        {
            int index = (start + count) % records.Length;
            if (count == records.Length)
            {
                index = start;
                start = (start + 1) % records.Length;
            }
            else
            {
                count++;
            }

            records[index] = new PoolDiagnosticRecord(
                nextSequence++,
                DateTime.UtcNow,
                eventKind,
                ownerId,
                scopeSequence,
                poolId,
                instanceSequence,
                generation,
                instanceState,
                activeCount,
                inactiveCount,
                quarantineCount,
                diagnostic,
                allocationStack,
                releaseStack);
        }

        internal PoolDiagnosticRecord[] Capture()
        {
            var snapshot = new PoolDiagnosticRecord[count];
            for (int index = 0; index < count; index++)
            {
                snapshot[index] = records[(start + index) % records.Length];
            }

            return snapshot;
        }
    }
}
