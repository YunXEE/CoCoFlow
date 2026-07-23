using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Pooling.Temporal
{
    // Mirrors only committed Temporal presence. Frames contain entity identifiers and
    // metadata; physical Pool tokens stay exclusively in the live runtime record table.
    internal sealed class PoolTemporalSidecar : IDisposable
    {
        private readonly Frame[] _frames;
        private CoCoTemporalEntityId[] _staging = Array.Empty<CoCoTemporalEntityId>();
        private CoCoTemporalFrameInfo _stagingInfo;
        private PreparedCaptureKind _preparedKind;
        private int _preparedCount;
        private int _preparedBranchDepth;
        private int _preparedBranchSourceIndex = -1;
        private int _headIndex = -1;
        private int _count;
        private bool _isDisposed;

        internal PoolTemporalSidecar(int capacity)
        {
            if (capacity < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "Pool Temporal history capacity must be at least two.");
            }

            _frames = new Frame[capacity];
            for (int index = 0; index < _frames.Length; index++)
            {
                _frames[index] = new Frame();
            }
        }

        internal int Capacity => _frames.Length;
        internal int Count => _isDisposed ? 0 : _count;
        internal bool HasPreparedCapture =>
            !_isDisposed && _preparedKind != PreparedCaptureKind.None;

        internal bool TryPrepareForwardCapture(
            PoolTemporalRecord[] records,
            int recordCount,
            in CoCoTemporalFrameInfo candidate)
        {
            if (_isDisposed ||
                _preparedKind != PreparedCaptureKind.None ||
                !candidate.IsValid ||
                records == null ||
                recordCount < 0 ||
                recordCount > records.Length)
            {
                return false;
            }

            int presentCount = 0;
            for (int index = 0; index < recordCount; index++)
            {
                PoolTemporalRecord record = records[index];
                if (record != null &&
                    record.IsRetained &&
                    record.AuthorityPresent)
                {
                    presentCount++;
                }
            }

            EnsureStagingCapacity(presentCount);
            int writeIndex = 0;
            for (int index = 0; index < recordCount; index++)
            {
                PoolTemporalRecord record = records[index];
                if (record == null ||
                    !record.IsRetained ||
                    !record.AuthorityPresent)
                {
                    continue;
                }

                _staging[writeIndex++] = record.EntityId;
            }

            _preparedCount = writeIndex;
            _stagingInfo = candidate;
            _preparedKind = PreparedCaptureKind.Forward;
            return true;
        }

        internal void PublishForwardCaptureNoFail()
        {
            if (_isDisposed || _preparedKind != PreparedCaptureKind.Forward)
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

            ClearPreparedCapture();
        }

        internal bool TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead)
        {
            if (_isDisposed ||
                _preparedKind != PreparedCaptureKind.None ||
                historyDepth <= 0 ||
                !branchHead.IsValid ||
                !TryResolveDepth(historyDepth, out int sourceIndex))
            {
                return false;
            }

            Frame source = _frames[sourceIndex];
            EnsureStagingCapacity(source.Count);
            if (source.Count > 0)
            {
                Array.Copy(source.Values, 0, _staging, 0, source.Count);
            }

            _preparedCount = source.Count;
            _stagingInfo = branchHead;
            _preparedBranchDepth = historyDepth;
            _preparedBranchSourceIndex = sourceIndex;
            _preparedKind = PreparedCaptureKind.Branch;
            return true;
        }

        internal void PublishBranchCaptureNoFail()
        {
            if (_isDisposed ||
                _preparedKind != PreparedCaptureKind.Branch ||
                _preparedBranchSourceIndex < 0)
            {
                return;
            }

            int publishIndex = (_preparedBranchSourceIndex + 1) % Capacity;
            PublishStagingTo(publishIndex);
            _headIndex = publishIndex;
            _count = _count - _preparedBranchDepth + 1;
            ClearPreparedCapture();
        }

        internal void CancelPreparedCaptureNoFail()
        {
            if (!_isDisposed)
            {
                ClearPreparedCapture();
            }
        }

        internal bool ContainsAtDepth(
            int historyDepth,
            CoCoTemporalEntityId entityId)
        {
            if (!entityId.IsValid ||
                !TryResolveDepth(historyDepth, out int frameIndex))
            {
                return false;
            }

            return Contains(_frames[frameIndex], entityId);
        }

        internal int GetEntityCountAtDepth(int historyDepth) =>
            TryResolveDepth(historyDepth, out int frameIndex)
                ? _frames[frameIndex].Count
                : 0;

        internal bool TryGetEntityAtDepth(
            int historyDepth,
            int entityIndex,
            out CoCoTemporalEntityId entityId)
        {
            if (!TryResolveDepth(historyDepth, out int frameIndex))
            {
                entityId = default;
                return false;
            }

            Frame frame = _frames[frameIndex];
            if (entityIndex < 0 || entityIndex >= frame.Count)
            {
                entityId = default;
                return false;
            }

            entityId = frame.Values[entityIndex];
            return entityId.IsValid;
        }

        internal bool IsReachable(CoCoTemporalEntityId entityId)
        {
            if (_isDisposed || !entityId.IsValid)
            {
                return false;
            }

            for (int depth = 0; depth < _count; depth++)
            {
                if (TryResolveDepth(depth, out int frameIndex) &&
                    Contains(_frames[frameIndex], entityId))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool IsAlignedWith(int historyCount) =>
            !_isDisposed &&
            historyCount >= 0 &&
            historyCount == _count;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            for (int index = 0; index < _frames.Length; index++)
            {
                _frames[index].Clear();
            }

            _staging = Array.Empty<CoCoTemporalEntityId>();
            _headIndex = -1;
            _count = 0;
            ClearPreparedCapture();
        }

        private bool TryResolveDepth(int historyDepth, out int frameIndex)
        {
            if (_isDisposed ||
                historyDepth < 0 ||
                historyDepth >= _count ||
                _headIndex < 0)
            {
                frameIndex = -1;
                return false;
            }

            frameIndex = _headIndex - historyDepth;
            if (frameIndex < 0)
            {
                frameIndex += Capacity;
            }

            return true;
        }

        private void PublishStagingTo(int targetIndex)
        {
            Frame target = _frames[targetIndex];
            CoCoTemporalEntityId[] recycled = target.Values;
            target.Values = _staging;
            target.Count = _preparedCount;
            target.Info = _stagingInfo;
            _staging = recycled;
        }

        private void EnsureStagingCapacity(int required)
        {
            if (_staging.Length >= required)
            {
                return;
            }

            int capacity = _staging.Length == 0 ? 4 : _staging.Length;
            while (capacity < required)
            {
                int next = capacity <= int.MaxValue / 2
                    ? capacity * 2
                    : int.MaxValue;
                if (next == capacity)
                {
                    throw new InvalidOperationException(
                        "Temporal entity high-water capacity is exhausted.");
                }

                capacity = next;
            }

            var expanded = new CoCoTemporalEntityId[capacity];
            if (_preparedCount > 0)
            {
                Array.Copy(_staging, 0, expanded, 0, _preparedCount);
            }

            _staging = expanded;
        }

        private void ClearPreparedCapture()
        {
            int clearCount = _preparedCount < _staging.Length
                ? _preparedCount
                : _staging.Length;
            for (int index = 0; index < clearCount; index++)
            {
                _staging[index] = default;
            }

            _stagingInfo = default;
            _preparedKind = PreparedCaptureKind.None;
            _preparedCount = 0;
            _preparedBranchDepth = 0;
            _preparedBranchSourceIndex = -1;
        }

        private static bool Contains(
            Frame frame,
            CoCoTemporalEntityId entityId)
        {
            for (int index = 0; index < frame.Count; index++)
            {
                if (frame.Values[index] == entityId)
                {
                    return true;
                }
            }

            return false;
        }

        private enum PreparedCaptureKind
        {
            None = 0,
            Forward = 1,
            Branch = 2
        }

        private sealed class Frame
        {
            internal CoCoTemporalEntityId[] Values { get; set; } =
                Array.Empty<CoCoTemporalEntityId>();

            internal int Count { get; set; }
            internal CoCoTemporalFrameInfo Info { get; set; }

            internal void Clear()
            {
                for (int index = 0; index < Count; index++)
                {
                    Values[index] = default;
                }

                Count = 0;
                Info = default;
            }
        }
    }
}
