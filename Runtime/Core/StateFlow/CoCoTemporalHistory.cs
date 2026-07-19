using System;

namespace CoCoFlow.Runtime.Core
{
    internal readonly struct CoCoTemporalHistoryEntryInfo : IEquatable<CoCoTemporalHistoryEntryInfo>
    {
        internal CoCoTemporalHistoryEntryInfo(
            CoCoStateFlowFrameHeader header,
            CoCoContextRevision revision,
            CoCoContextFrameOrigin origin)
        {
            Header = header;
            Revision = revision;
            Origin = origin;
        }

        internal CoCoStateFlowFrameHeader Header { get; }
        internal CoCoContextRevision Revision { get; }
        internal CoCoContextFrameOrigin Origin { get; }
        internal bool IsValid =>
            Header.IsValid &&
            Header.HasExactLayoutIdentity &&
            Header.Identity.Kind == CoCoStateFlowFrameKind.Context &&
            Revision.IsValid &&
            Origin.IsValid;

        public bool Equals(CoCoTemporalHistoryEntryInfo other) =>
            Header == other.Header &&
            Revision == other.Revision &&
            Origin.Equals(other.Origin);

        public override bool Equals(object obj) =>
            obj is CoCoTemporalHistoryEntryInfo other && Equals(other);

        public override int GetHashCode() =>
            unchecked((Header.GetHashCode() * 397) ^ Revision.GetHashCode());
    }

    internal readonly struct CoCoTemporalSelection
    {
        private readonly CoCoTemporalHistory _history;

        internal CoCoTemporalSelection(
            CoCoTemporalHistory history,
            ulong historyGeneration,
            ulong readToken,
            int depth,
            int entryIndex)
        {
            _history = history;
            HistoryGeneration = historyGeneration;
            ReadToken = readToken;
            Depth = depth;
            EntryIndex = entryIndex;
        }

        internal ulong HistoryGeneration { get; }
        internal ulong ReadToken { get; }
        internal int Depth { get; }
        internal int EntryIndex { get; }
        internal bool IsValid => _history != null && _history.IsSelectionValid(this);
        internal CoCoTemporalHistoryEntryInfo Info =>
            _history != null && _history.TryGetSelectionInfo(this, out CoCoTemporalHistoryEntryInfo info)
                ? info
                : default;
        internal CoCoContextRestoreReadView RestoreView =>
            IsValid
                ? new CoCoContextRestoreReadView(_history, ReadToken)
                : default;
    }

    internal sealed class CoCoTemporalHistory : IDisposable, ICoCoContextMaterializedReadSource
    {
        private readonly CoCoContextProjectionCodec _codec;
        private readonly CoCoContextFrameLayout _layout;
        private readonly Entry[] _entries;
        private readonly byte[] _previewBuffer;
        private byte[] _stagingPayload;
        private CoCoTemporalHistoryEntryInfo _stagingInfo;
        private PreparedCaptureKind _preparedKind;
        private ulong _preparedGeneration;
        private int _preparedLength;
        private int _preparedBranchDepth;
        private int _preparedBranchSourceIndex;
        private int _headIndex = -1;
        private int _count;
        private ulong _generation = 1UL;
        private ulong _nextReadToken;
        private ulong _activeReadToken;
        private ulong _activeReadGeneration;
        private CoCoTemporalHistoryEntryInfo _activeReadInfo;
        private bool _isDisposed;

        private CoCoTemporalHistory(
            CoCoContextProjectionCodec codec,
            int capacity)
        {
            _codec = codec;
            _layout = codec.Layout;
            _entries = new Entry[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _entries[index] = new Entry(codec.MaxEncodedSize);
            }

            _stagingPayload = new byte[codec.MaxEncodedSize];
            _previewBuffer = _layout.CreateBuffer();
        }

        internal int Capacity => _entries.Length;
        internal int Count => _isDisposed ? 0 : _count;
        internal int MaxEncodedSize => _codec.MaxEncodedSize;
        internal long AllocatedPayloadBytes =>
            ((long)Capacity + 1L) * MaxEncodedSize + _layout.ByteSize;
        internal bool HasPreparedCapture => !_isDisposed && _preparedKind != PreparedCaptureKind.None;
        internal CoCoContextFrameLayout Layout => _layout;

        internal static bool TryCreate(
            CoCoContextFrameLayout layout,
            CoCoContextCodecRegistry codecs,
            int capacity,
            out CoCoTemporalHistory history,
            out CoCoDiagnosticCode diagnosticCode)
        {
            history = null;
            if (layout == null || capacity <= 0 || capacity > 0x7FEFFFFF)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (!CoCoContextProjectionCodec.TryCreate(
                    layout,
                    codecs,
                    CoCoContextProjection.Temporal,
                    out CoCoContextProjectionCodec codec,
                    out diagnosticCode))
            {
                return false;
            }

            long payloadBytes = ((long)capacity + 1L) * codec.MaxEncodedSize + layout.ByteSize;
            if (payloadBytes > int.MaxValue)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            history = new CoCoTemporalHistory(codec, capacity);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool TryGetInfo(
            int depth,
            out CoCoTemporalHistoryEntryInfo info)
        {
            if (!TryResolveDepth(depth, out int index))
            {
                info = default;
                return false;
            }

            info = _entries[index].Info;
            return info.IsValid;
        }

        internal bool TryPrepareCapture(
            in CoCoFinalizedContextCommit candidate,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_isDisposed ||
                _preparedKind != PreparedCaptureKind.None ||
                _generation == ulong.MaxValue ||
                !candidate.TryGetMetadata(
                    out CoCoStateFlowFrameHeader header,
                    out CoCoContextRevision revision,
                    out CoCoContextFrameOrigin origin) ||
                !TryValidateNextAuthority(header, revision, origin))
            {
                diagnosticCode = CoCoDiagnosticCode.CommitPreparationFailed;
                return false;
            }

            if (!candidate.TryEncode(
                    _codec,
                    _stagingPayload,
                    out int bytesWritten,
                    out diagnosticCode))
            {
                return false;
            }

            _stagingInfo = new CoCoTemporalHistoryEntryInfo(header, revision, origin);
            _preparedLength = bytesWritten;
            _preparedKind = PreparedCaptureKind.Forward;
            _preparedGeneration = _generation;
            return true;
        }

        internal bool TryPrepareBranchCapture(
            in CoCoFinalizedContextCommit candidate,
            in CoCoTemporalSelection source,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_isDisposed ||
                _preparedKind != PreparedCaptureKind.None ||
                _generation == ulong.MaxValue ||
                !IsSelectionValid(source) ||
                source.Depth <= 0 ||
                !candidate.TryGetMetadata(
                    out CoCoStateFlowFrameHeader header,
                    out CoCoContextRevision revision,
                    out CoCoContextFrameOrigin origin) ||
                !TryValidateBranchAuthority(header, revision, origin, source))
            {
                diagnosticCode = CoCoDiagnosticCode.CommitPreparationFailed;
                return false;
            }

            if (!candidate.TryEncode(
                    _codec,
                    _stagingPayload,
                    out int bytesWritten,
                    out diagnosticCode))
            {
                return false;
            }

            _stagingInfo = new CoCoTemporalHistoryEntryInfo(header, revision, origin);
            _preparedLength = bytesWritten;
            _preparedKind = PreparedCaptureKind.Branch;
            _preparedGeneration = _generation;
            _preparedBranchDepth = source.Depth;
            _preparedBranchSourceIndex = source.EntryIndex;
            return true;
        }

        internal void PublishCaptureNoFail()
        {
            if (_preparedKind != PreparedCaptureKind.Forward ||
                _preparedGeneration != _generation)
            {
                return;
            }

            int publishIndex = _count == 0
                ? 0
                : (_headIndex + 1) % Capacity;
            PublishStagingTo(publishIndex);
            _headIndex = publishIndex;
            if (_count < Capacity)
            {
                _count++;
            }

            CompletePublishNoFail();
        }

        internal void PublishBranchCaptureNoFail()
        {
            if (_preparedKind != PreparedCaptureKind.Branch ||
                _preparedGeneration != _generation)
            {
                return;
            }

            int publishIndex = (_preparedBranchSourceIndex + 1) % Capacity;
            PublishStagingTo(publishIndex);
            _headIndex = publishIndex;
            _count = _count - _preparedBranchDepth + 1;
            CompletePublishNoFail();
        }

        internal void CancelPreparedCapture()
        {
            if (_isDisposed)
            {
                return;
            }

            ClearPreparedCapture();
        }

        internal bool TrySelect(
            int depth,
            out CoCoTemporalSelection selection,
            out CoCoDiagnosticCode diagnosticCode)
        {
            selection = default;
            if (_isDisposed ||
                _preparedKind != PreparedCaptureKind.None ||
                _nextReadToken == ulong.MaxValue ||
                depth <= 0 ||
                !TryResolveDepth(depth, out int entryIndex))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            InvalidateActiveReadNoFail();
            Entry entry = _entries[entryIndex];
            if (!_codec.TryDecodePayload(
                    new ReadOnlySpan<byte>(entry.Payload, 0, entry.EncodedLength),
                    _previewBuffer,
                    out diagnosticCode))
            {
                return false;
            }

            _nextReadToken++;
            _activeReadToken = _nextReadToken;
            _activeReadGeneration = _generation;
            _activeReadInfo = entry.Info;
            selection = new CoCoTemporalSelection(
                this,
                _generation,
                _activeReadToken,
                depth,
                entryIndex);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool TryPrepareRestore(
            in CoCoTemporalSelection selection,
            CoCoContextFrameArena arena,
            CoCoTickFrame resumedTickFrame,
            out CoCoFinalizedContextCommit finalized,
            out CoCoContextCommitStatus status,
            out CoCoDiagnosticCode diagnosticCode)
        {
            finalized = default;
            status = CoCoContextCommitStatus.RestoreFailed;
            if (!IsSelectionValid(selection) || arena == null || !_layout.IsSameInstance(arena.Layout))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            Entry entry = _entries[selection.EntryIndex];
            if (!_codec.TryDecodeAndPrepareRestore(
                    new ReadOnlySpan<byte>(entry.Payload, 0, entry.EncodedLength),
                    arena,
                    resumedTickFrame,
                    out finalized,
                    out int bytesRead,
                    out status,
                    out diagnosticCode) ||
                bytesRead != entry.EncodedLength)
            {
                return false;
            }

            if (!finalized.TryGetMetadata(
                    out _,
                    out _,
                    out CoCoContextFrameOrigin origin) ||
                !OriginMatches(origin, entry.Info))
            {
                finalized.Cancel();
                finalized = default;
                status = CoCoContextCommitStatus.RestoreFailed;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool IsSelectionValid(in CoCoTemporalSelection selection)
        {
            if (_isDisposed ||
                selection.HistoryGeneration == 0UL ||
                selection.HistoryGeneration != _generation ||
                selection.ReadToken == 0UL ||
                selection.ReadToken != _activeReadToken ||
                _activeReadGeneration != _generation ||
                !TryResolveDepth(selection.Depth, out int entryIndex) ||
                entryIndex != selection.EntryIndex)
            {
                return false;
            }

            return _entries[entryIndex].Info.Equals(_activeReadInfo);
        }

        internal bool TryGetSelectionInfo(
            in CoCoTemporalSelection selection,
            out CoCoTemporalHistoryEntryInfo info)
        {
            if (!IsSelectionValid(selection))
            {
                info = default;
                return false;
            }

            info = _entries[selection.EntryIndex].Info;
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Clear();
            _isDisposed = true;
        }

        internal void Clear()
        {
            if (_isDisposed)
            {
                return;
            }

            for (int index = 0; index < _entries.Length; index++)
            {
                _entries[index].ClearMetadata();
            }

            _headIndex = -1;
            _count = 0;
            ClearPreparedCapture();
            InvalidateActiveReadNoFail();
            AdvanceGenerationNoFail();
        }

        bool ICoCoContextMaterializedReadSource.TryGetMaterializedRead(
            ulong token,
            out CoCoContextFrameLayout layout,
            out byte[] buffer,
            out CoCoStateFlowFrameHeader header,
            out CoCoContextRevision revision,
            out CoCoContextFrameOrigin origin)
        {
            if (_isDisposed ||
                token == 0UL ||
                token != _activeReadToken ||
                _activeReadGeneration != _generation ||
                !_activeReadInfo.IsValid)
            {
                layout = null;
                buffer = null;
                header = default;
                revision = default;
                origin = default;
                return false;
            }

            layout = _layout;
            buffer = _previewBuffer;
            header = _activeReadInfo.Header;
            revision = _activeReadInfo.Revision;
            origin = _activeReadInfo.Origin;
            return true;
        }

        private bool TryValidateNextAuthority(
            CoCoStateFlowFrameHeader header,
            CoCoContextRevision revision,
            CoCoContextFrameOrigin origin)
        {
            var info = new CoCoTemporalHistoryEntryInfo(header, revision, origin);
            if (!info.IsValid ||
                !_layout.HasExactIdentity(
                    header.LayoutId,
                    header.LayoutVersion,
                    header.LayoutSchemaHash))
            {
                return false;
            }

            if (_count == 0)
            {
                return true;
            }

            CoCoTemporalHistoryEntryInfo current = _entries[_headIndex].Info;
            return header.Identity.GraphInstanceId == current.Header.Identity.GraphInstanceId &&
                   CoCoStateFlowTickOrder.IsStrictlyAfter(header.TickFrame, current.Header.TickFrame) &&
                   revision.Value > current.Revision.Value;
        }

        private bool TryValidateBranchAuthority(
            CoCoStateFlowFrameHeader header,
            CoCoContextRevision revision,
            CoCoContextFrameOrigin origin,
            in CoCoTemporalSelection selection)
        {
            if (!TryValidateNextAuthority(header, revision, origin))
            {
                return false;
            }

            CoCoTemporalHistoryEntryInfo source = _entries[selection.EntryIndex].Info;
            CoCoTemporalHistoryEntryInfo current = _entries[_headIndex].Info;
            return OriginMatches(origin, source) &&
                   header.TickFrame.TimelineId == source.Header.TickFrame.TimelineId &&
                   header.TickFrame.ClockDomainId == source.Header.TickFrame.ClockDomainId &&
                   header.TickFrame.TimelineEpoch.Value > source.Header.TickFrame.TimelineEpoch.Value &&
                   header.TickFrame.TimelineEpoch.Value > current.Header.TickFrame.TimelineEpoch.Value &&
                   header.TickFrame.ExecutionSequence.Value > source.Header.TickFrame.ExecutionSequence.Value &&
                   header.TickFrame.ExecutionSequence.Value > current.Header.TickFrame.ExecutionSequence.Value;
        }

        private static bool OriginMatches(
            CoCoContextFrameOrigin origin,
            in CoCoTemporalHistoryEntryInfo source) =>
            origin.IsRestore &&
            origin.SourceGraphInstanceId == source.Header.Identity.GraphInstanceId &&
            origin.SourceTimelineEpoch == source.Header.Identity.TimelineEpoch &&
            origin.SourceTick == source.Header.Identity.Tick &&
            origin.SourceRevision == source.Revision;

        private bool TryResolveDepth(int depth, out int entryIndex)
        {
            if (_isDisposed || depth < 0 || depth >= _count || _headIndex < 0)
            {
                entryIndex = -1;
                return false;
            }

            entryIndex = _headIndex - depth;
            if (entryIndex < 0)
            {
                entryIndex += Capacity;
            }

            return true;
        }

        private void PublishStagingTo(int targetIndex)
        {
            Entry target = _entries[targetIndex];
            byte[] recycled = target.Payload;
            target.Payload = _stagingPayload;
            target.EncodedLength = _preparedLength;
            target.Info = _stagingInfo;
            _stagingPayload = recycled;
        }

        private void CompletePublishNoFail()
        {
            ClearPreparedCapture();
            InvalidateActiveReadNoFail();
            AdvanceGenerationNoFail();
        }

        private void ClearPreparedCapture()
        {
            _stagingInfo = default;
            _preparedKind = PreparedCaptureKind.None;
            _preparedGeneration = 0UL;
            _preparedLength = 0;
            _preparedBranchDepth = 0;
            _preparedBranchSourceIndex = -1;
        }

        private void InvalidateActiveReadNoFail()
        {
            _activeReadToken = 0UL;
            _activeReadGeneration = 0UL;
            _activeReadInfo = default;
        }

        private void AdvanceGenerationNoFail()
        {
            _generation = _generation == ulong.MaxValue ? 1UL : _generation + 1UL;
        }

        private enum PreparedCaptureKind
        {
            None = 0,
            Forward = 1,
            Branch = 2
        }

        private sealed class Entry
        {
            internal Entry(int maxEncodedSize)
            {
                Payload = new byte[maxEncodedSize];
            }

            internal byte[] Payload { get; set; }
            internal int EncodedLength { get; set; }
            internal CoCoTemporalHistoryEntryInfo Info { get; set; }

            internal void ClearMetadata()
            {
                EncodedLength = 0;
                Info = default;
            }
        }
    }
}
