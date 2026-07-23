using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Content
{
    public enum ContentEntryState
    {
        Loading = 0,
        Loaded = 1,
        Releasing = 2,
        ReleaseFailed = 3
    }

    public enum ContentDiagnosticEventKind
    {
        RequestStarted = 0,
        RequestCancelled = 1,
        LoadSucceeded = 2,
        LoadFailed = 3,
        LeaseCreated = 4,
        LeaseReleased = 5,
        ReleaseStarted = 6,
        ReleaseSucceeded = 7,
        ReleaseFailed = 8
    }

    public readonly struct ContentEntrySnapshot
    {
        internal ContentEntrySnapshot(
            ContentId contentId,
            ContentKind kind,
            Type expectedType,
            ContentBackendId backendId,
            int backendGeneration,
            long resourceGeneration,
            ContentEntryState state,
            int waiterCount,
            int leaseCount,
            CoCoDiagnostic diagnostic)
        {
            ContentId = contentId;
            Kind = kind;
            ExpectedType = expectedType;
            BackendId = backendId;
            BackendGeneration = backendGeneration;
            ResourceGeneration = resourceGeneration;
            State = state;
            WaiterCount = waiterCount;
            LeaseCount = leaseCount;
            Diagnostic = diagnostic;
        }

        public ContentId ContentId { get; }
        public ContentKind Kind { get; }
        public Type ExpectedType { get; }
        public ContentBackendId BackendId { get; }
        public int BackendGeneration { get; }
        public long ResourceGeneration { get; }
        public ContentEntryState State { get; }
        public int WaiterCount { get; }
        public int LeaseCount { get; }
        public CoCoDiagnostic Diagnostic { get; }
    }

    public readonly struct ContentDiagnosticRecord
    {
        internal ContentDiagnosticRecord(
            long sequence,
            DateTime timestampUtc,
            ContentDiagnosticEventKind eventKind,
            ContentId contentId,
            ContentOwnerId ownerId,
            long scopeSequence,
            ContentBackendId backendId,
            int backendGeneration,
            long resourceGeneration,
            long requestSequence,
            long leaseSequence,
            CoCoDiagnostic diagnostic,
            string allocationStack,
            string releaseStack)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            EventKind = eventKind;
            ContentId = contentId;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            BackendId = backendId;
            BackendGeneration = backendGeneration;
            ResourceGeneration = resourceGeneration;
            RequestSequence = requestSequence;
            LeaseSequence = leaseSequence;
            Diagnostic = diagnostic;
            AllocationStack = allocationStack ?? string.Empty;
            ReleaseStack = releaseStack ?? string.Empty;
        }

        public long Sequence { get; }
        public DateTime TimestampUtc { get; }
        public ContentDiagnosticEventKind EventKind { get; }
        public ContentId ContentId { get; }
        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public ContentBackendId BackendId { get; }
        public int BackendGeneration { get; }
        public long ResourceGeneration { get; }
        public long RequestSequence { get; }
        public long LeaseSequence { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public string AllocationStack { get; }
        public string ReleaseStack { get; }
    }

    public sealed class ContentRuntimeSnapshot
    {
        internal ContentRuntimeSnapshot(
            bool isShuttingDown,
            ContentEntrySnapshot[] entries,
            ContentDiagnosticRecord[] diagnostics)
        {
            IsShuttingDown = isShuttingDown;
            Entries = Array.AsReadOnly(entries ?? Array.Empty<ContentEntrySnapshot>());
            Diagnostics = Array.AsReadOnly(diagnostics ?? Array.Empty<ContentDiagnosticRecord>());
        }

        public bool IsShuttingDown { get; }
        public IReadOnlyList<ContentEntrySnapshot> Entries { get; }
        public IReadOnlyList<ContentDiagnosticRecord> Diagnostics { get; }
    }

    internal sealed class ContentDiagnosticLedger
    {
        private readonly ContentDiagnosticRecord[] records;
        private int start;
        private int count;
        private long nextSequence = 1;

        internal ContentDiagnosticLedger(int capacity)
        {
            records = new ContentDiagnosticRecord[capacity];
        }

        internal void Record(
            ContentDiagnosticEventKind eventKind,
            ContentId contentId,
            ContentOwnerId ownerId,
            ContentBackendId backendId,
            int backendGeneration,
            long resourceGeneration,
            long requestSequence,
            long leaseSequence,
            CoCoDiagnostic diagnostic,
            string allocationStack = null,
            string releaseStack = null,
            long scopeSequence = 0)
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

            records[index] = new ContentDiagnosticRecord(
                nextSequence++,
                DateTime.UtcNow,
                eventKind,
                contentId,
                ownerId,
                scopeSequence,
                backendId,
                backendGeneration,
                resourceGeneration,
                requestSequence,
                leaseSequence,
                diagnostic,
                allocationStack,
                releaseStack);
        }

        internal ContentDiagnosticRecord[] Capture()
        {
            var snapshot = new ContentDiagnosticRecord[count];
            for (int index = 0; index < count; index++)
            {
                snapshot[index] = records[(start + index) % records.Length];
            }

            return snapshot;
        }
    }
}
