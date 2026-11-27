using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 实例在预分配的存储中按层和深度合成输出，提交时才推进离散命令的序号。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>以层与激活深度确定各次状态写入的优先顺序。</summary>
    public readonly struct OperationWriteRank : IEquatable<OperationWriteRank>
    {
        private readonly bool _isValid;

        public int LayerIndex { get; }
        public int PathDepth { get; }
        public bool IsValid => _isValid && LayerIndex >= 0 && PathDepth >= 0;

        internal static OperationWriteRank Base => new OperationWriteRank(0, 0);

        #region Public API

        /// <summary>把非负的层索引与路径深度组成写入排序键，任一输入无效时返回 false。</summary>
        public static bool TryCreate(
            int layerIndex,
            int pathDepth,
            out OperationWriteRank rank)
        {
            if (layerIndex < 0 || pathDepth < 0)
            {
                rank = default;
                return false;
            }

            rank = new OperationWriteRank(layerIndex, pathDepth);
            return true;
        }

        /// <summary>按<c>isValid</c>、<c>LayerIndex</c>、<c>PathDepth</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationWriteRank other) =>
            _isValid == other._isValid &&
            LayerIndex == other.LayerIndex &&
            PathDepth == other.PathDepth;

        /// <summary>仅当对象属于同类型并且<c>isValid</c>、<c>LayerIndex</c>、<c>PathDepth</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationWriteRank other && Equals(other);

        /// <summary>为哈希集合计算与<c>isValid</c>、<c>LayerIndex</c>、<c>PathDepth</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _isValid.GetHashCode();
                hashCode = (hashCode * 397) ^ LayerIndex;
                hashCode = (hashCode * 397) ^ PathDepth;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>isValid</c>、<c>LayerIndex</c>、<c>PathDepth</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperationWriteRank left,
            OperationWriteRank right) => left.Equals(right);

        /// <summary>比较两端的<c>isValid</c>、<c>LayerIndex</c>、<c>PathDepth</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperationWriteRank left,
            OperationWriteRank right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperationWriteRank(int layerIndex, int pathDepth)
        {
            _isValid = true;
            LayerIndex = layerIndex;
            PathDepth = pathDepth;
        }

        internal bool IsLowerThan(in OperationWriteRank other)
        {
            return LayerIndex < other.LayerIndex ||
                   (LayerIndex == other.LayerIndex && PathDepth < other.PathDepth);
        }

        #endregion
    }

    /// <summary>保存实例本步的操作值与离散启用状态，候选取消时丢弃写入。</summary>
    internal sealed class OperationFrame : IStagedOperationReader
    {
        private readonly OperationRegistry _registry;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly byte[] _storage;
        private readonly OperationEntryHeader[] _entryHeaders;
        private readonly OperationWriteRank[] _fieldRanks;
        private readonly bool[] _fieldHasContribution;
        private readonly OperationWriteRank[] _discreteRanks;
        private readonly bool[] _discreteHasContribution;
        private readonly ActivationId[] _discreteActivationIds;
        private readonly ulong[] _committedSequences;
        private readonly ulong[] _pendingSequences;
        private readonly object[] _views;
        private TimelineEpoch _committedSequenceEpoch;
        private TimelineEpoch _pendingSequenceEpoch;
        private TickFrame _lastCommittedTickFrame;
        private StateFlowFrameHeader _header;
        private ulong _writeToken;
        private object _runtimeOwner;
        private bool _hasCommittedSequenceEpoch;
        private bool _hasCommittedTickFrame;
        private bool _isWriting;
        private bool _isFinalized;

        private OperationFrame(
            OperationRegistry registry,
            GraphInstanceId graphInstanceId)
        {
            _registry = registry;
            _graphInstanceId = graphInstanceId;
            _storage = new byte[registry.ByteSize];
            _entryHeaders = new OperationEntryHeader[registry.Count];
            _fieldRanks = new OperationWriteRank[registry.ByteSize];
            _fieldHasContribution = new bool[registry.ByteSize];
            _discreteRanks = new OperationWriteRank[registry.Count];
            _discreteHasContribution = new bool[registry.Count];
            _discreteActivationIds = new ActivationId[registry.Count];
            _committedSequences = new ulong[registry.Count];
            _pendingSequences = new ulong[registry.Count];
            _views = registry.CreateViews(this);
        }

        public OperationRegistry Registry => _registry;
        public GraphInstanceId GraphInstanceId => _graphInstanceId;

        public static bool TryCreate(
            OperationRegistry registry,
            GraphInstanceId graphInstanceId,
            IReadOnlyList<OperationRequirement> graphProvides,
            out OperationFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (registry == null || !registry.IsFrozen)
            {
                frame = null;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "OperationFrame requires a frozen Section Registry.");
                return false;
            }

            if (!graphInstanceId.IsValid)
            {
                frame = null;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "OperationFrame GraphInstanceId must be valid.");
                return false;
            }

            if (!registry.TryValidateProvides(graphProvides, out diagnostic))
            {
                frame = null;
                return false;
            }

            try
            {
                frame = new OperationFrame(registry, graphInstanceId);
            }
            catch (Exception)
            {
                frame = null;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section view creation failed during GraphInstance setup.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryBegin(TickFrame tickFrame, out OperationFrameWriter writer) =>
            TryBegin(null, tickFrame, out writer);

        internal bool TryBegin(
            object runtimeOwner,
            TickFrame tickFrame,
            out OperationFrameWriter writer)
        {
            if ((_runtimeOwner != null && !ReferenceEquals(_runtimeOwner, runtimeOwner)) ||
                _isWriting ||
                _isFinalized ||
                (_hasCommittedTickFrame &&
                 !StateFlowTickOrder.IsStrictlyAfter(
                     tickFrame,
                     _lastCommittedTickFrame)) ||
                !StateFlowFrameHeader.TryCreate(
                    _graphInstanceId,
                    _registry.LayoutId,
                    StateFlowFrameKind.Operation,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                writer = default;
                return false;
            }

            if (!TryPrepareSequenceEpoch(tickFrame.TimelineEpoch))
            {
                writer = default;
                return false;
            }

            Array.Clear(_storage, 0, _storage.Length);
            Array.Clear(_fieldHasContribution, 0, _fieldHasContribution.Length);
            Array.Clear(_discreteHasContribution, 0, _discreteHasContribution.Length);
            Array.Clear(_discreteActivationIds, 0, _discreteActivationIds.Length);
            for (int index = 0; index < _entryHeaders.Length; index++)
            {
                _entryHeaders[index] = _registry.GetMode(index) == Mode.Continuous
                    ? OperationEntryHeader.Continuous()
                    : default;
            }

            _header = header;
            _isWriting = true;
            _isFinalized = false;
            _writeToken = _writeToken == ulong.MaxValue ? 1UL : _writeToken + 1UL;
            writer = new OperationFrameWriter(this, _writeToken);
            return true;
        }

        internal bool TryClaimRuntimeOwner(object owner, GraphInstanceId graphInstanceId)
        {
            lock (this)
            {
                if (owner == null ||
                    !graphInstanceId.IsValid ||
                    graphInstanceId != _graphInstanceId ||
                    _runtimeOwner != null ||
                    _isWriting ||
                    _isFinalized ||
                    _hasCommittedTickFrame ||
                    _hasCommittedSequenceEpoch)
                {
                    return false;
                }

                _runtimeOwner = owner;
                return true;
            }
        }

        internal void ReleaseRuntimeOwner(object owner)
        {
            lock (this)
            {
                if (ReferenceEquals(_runtimeOwner, owner))
                {
                    _runtimeOwner = null;
                }
            }
        }

        public bool TryGet<TOperation>(
            ulong token,
            OperationHandle<TOperation> handle,
            out OperationEntry<TOperation> entry)
            where TOperation : class, IOperation
        {
            if (!IsFinalized(token) || !_registry.ValidateHandle(handle))
            {
                entry = default;
                return false;
            }

            entry = new OperationEntry<TOperation>(
                _entryHeaders[handle.DenseIndex],
                (TOperation)_views[handle.DenseIndex]);
            return true;
        }

        internal bool TryGetHeader(
            ulong token,
            int denseIndex,
            out OperationEntryHeader header)
        {
            if (!IsFinalized(token) ||
                denseIndex < 0 ||
                denseIndex >= _entryHeaders.Length)
            {
                header = default;
                return false;
            }

            header = _entryHeaders[denseIndex];
            return true;
        }

        internal bool TryRead<TValue>(
            OperationField<TValue> field,
            out TValue value)
            where TValue : unmanaged
        {
            if (!_isFinalized || !_registry.ValidateField(field))
            {
                value = default;
                return false;
            }

            value = OperationBinary.Read<TValue>(
                _storage,
                field.ByteOffset,
                field.ByteSize);
            return true;
        }

        internal void ReadField(OperationDescriptor operation, int index, Span<byte> destination)
        {
            if (!_isFinalized) return;
            OperationFieldDescriptor field = operation.Fields[index];
            new ReadOnlySpan<byte>(_storage, operation.ByteOffset + field.ByteOffset, field.ByteSize).CopyTo(destination);
        }

        internal bool TryWriteField(ulong token, OperationWriteRank rank, OperationDescriptor operation,
            int index, ReadOnlySpan<byte> bytes)
        {
            if (!CanWrite(token) || !rank.IsValid || !ReferenceEquals(_registry.Operations[operation.DenseIndex], operation))
                return false;
            var field = operation.Fields[index];
            return field.ByteSize == bytes.Length && WriteBytes(rank, operation.ByteOffset + field.ByteOffset, bytes);
        }

        internal bool TryWrite<TValue>(
            ulong token,
            OperationWriteRank rank,
            OperationField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            if (!CanWrite(token) ||
                !rank.IsValid ||
                !_registry.ValidateField(field))
            {
                return false;
            }

            TValue copy = value;
            return WriteBytes(rank, field.ByteOffset, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1)));
        }

        private bool WriteBytes(OperationWriteRank rank, int fieldAddress, ReadOnlySpan<byte> bytes)
        {
            if (_fieldHasContribution[fieldAddress] &&
                rank.IsLowerThan(_fieldRanks[fieldAddress]))
            {
                return true;
            }

            bytes.CopyTo(_storage.AsSpan(fieldAddress, bytes.Length));
            _fieldRanks[fieldAddress] = rank;
            _fieldHasContribution[fieldAddress] = true;
            return true;
        }

        internal bool TryEnableDiscrete<TOperation>(
            ulong token,
            OperationWriteRank rank,
            OperationHandle<TOperation> handle,
            ActivationId activationId)
            where TOperation : class, IOperation
        {
            if (!CanWrite(token) ||
                !rank.IsValid ||
                !activationId.IsValid ||
                !_registry.ValidateHandle(handle) ||
                _registry.GetMode(handle.DenseIndex) != Mode.Discrete)
            {
                return false;
            }

            int operationIndex = handle.DenseIndex;
            if (_discreteHasContribution[operationIndex] &&
                rank.IsLowerThan(_discreteRanks[operationIndex]))
            {
                return true;
            }

            _discreteRanks[operationIndex] = rank;
            _discreteHasContribution[operationIndex] = true;
            _discreteActivationIds[operationIndex] = activationId;
            return true;
        }

        internal bool TryFinalize(
            ulong token,
            out FinalizedOperationFrame finalized)
        {
            if (!CanWrite(token))
            {
                finalized = default;
                return false;
            }

            for (int index = 0; index < _discreteHasContribution.Length; index++)
            {
                if (_discreteHasContribution[index] &&
                    _pendingSequences[index] == ulong.MaxValue)
                {
                    finalized = default;
                    return false;
                }
            }

            for (int index = 0; index < _discreteHasContribution.Length; index++)
            {
                if (!_discreteHasContribution[index])
                {
                    continue;
                }

                ulong next = _pendingSequences[index] + 1UL;
                if (!OperationSequence.TryCreate(next, out OperationSequence sequence))
                {
                    finalized = default;
                    return false;
                }

                _pendingSequences[index] = next;
                _entryHeaders[index] = OperationEntryHeader.Discrete(
                    _discreteActivationIds[index],
                    sequence);
            }

            _isWriting = false;
            _isFinalized = true;
            finalized = new FinalizedOperationFrame(this, token);
            return true;
        }

        internal bool IsCommitReady(ulong token) => IsFinalized(token);

        internal void CommitPreparedNoFail()
        {
            CommitPendingSequenceEpoch();
            _lastCommittedTickFrame = _header.TickFrame;
            _hasCommittedTickFrame = true;
            ClearActiveTransaction();
        }

        internal void CommitPrepared(ulong token)
        {
            if (!IsCommitReady(token))
            {
                throw new InvalidOperationException(
                    "The finalized OperationFrame token is no longer ready.");
            }

            CommitPreparedNoFail();
        }

        internal bool Commit(ulong token)
        {
            if (!IsCommitReady(token))
            {
                return false;
            }

            CommitPrepared(token);
            return true;
        }

        internal bool CancelPrepared(ulong token)
        {
            if (!CanWrite(token))
            {
                return false;
            }

            ClearActiveTransaction();
            return true;
        }

        internal bool CancelFinalized(ulong token)
        {
            if (!IsFinalized(token))
            {
                return false;
            }

            ClearActiveTransaction();
            return true;
        }

        internal bool IsWriting(ulong token) => CanWrite(token);

        public bool IsFinalized(ulong token) =>
            _isFinalized && token != 0UL && token == _writeToken;

        public StateFlowFrameHeader GetFinalizedHeader(ulong token) =>
            IsFinalized(token) ? _header : default;

        private bool TryPrepareSequenceEpoch(TimelineEpoch epoch)
        {
            if (_hasCommittedSequenceEpoch && epoch == _committedSequenceEpoch)
            {
                Array.Copy(_committedSequences, _pendingSequences, _committedSequences.Length);
                _pendingSequenceEpoch = epoch;
                return true;
            }

            if (_hasCommittedSequenceEpoch && epoch.Value < _committedSequenceEpoch.Value)
            {
                return false;
            }

            Array.Clear(_pendingSequences, 0, _pendingSequences.Length);
            _pendingSequenceEpoch = epoch;
            return true;
        }

        private void CommitPendingSequenceEpoch()
        {
            _committedSequenceEpoch = _pendingSequenceEpoch;
            _hasCommittedSequenceEpoch = true;
            Array.Copy(_pendingSequences, _committedSequences, _pendingSequences.Length);
            _pendingSequenceEpoch = default;
        }

        private void ClearActiveTransaction()
        {
            _isWriting = false;
            _isFinalized = false;
            _header = default;
            _pendingSequenceEpoch = default;
        }

        private bool CanWrite(ulong token) => _isWriting && token != 0UL && token == _writeToken;
    }

    /// <summary>向下游提供已准备的输出，保留提交或取消该候选的凭据。</summary>
    internal readonly struct FinalizedOperationFrame : IOperationFrame
    {
        private readonly OperationFrame _frame;
        private readonly ulong _token;

        internal FinalizedOperationFrame(OperationFrame frame, ulong token)
        {
            _frame = frame;
            _token = token;
        }

        internal StagedOperationFrame ReadView => new StagedOperationFrame(_frame, _token);

        public StateFlowFrameHeader Header =>
            _frame != null ? _frame.GetFinalizedHeader(_token) : default;

        public OperationRegistry Registry => _frame?.Registry;
        public bool IsValid => _frame != null && _frame.IsFinalized(_token);

        public bool TryGet<TOperation>(
            OperationHandle<TOperation> handle,
            out OperationEntry<TOperation> entry)
            where TOperation : class, IOperation
        {
            if (_frame == null)
            {
                entry = default;
                return false;
            }

            return _frame.TryGet(_token, handle, out entry);
        }

        internal bool TryGetHeader(
            OperationRequirement requirement,
            out OperationEntryHeader header)
        {
            if (_frame == null ||
                !_frame.Registry.TryResolveDenseIndex(requirement, out int denseIndex))
            {
                header = default;
                return false;
            }

            return _frame.TryGetHeader(_token, denseIndex, out header);
        }

        internal bool TryGetHeader(
            int denseIndex,
            out OperationEntryHeader header)
        {
            if (_frame == null)
            {
                header = default;
                return false;
            }

            return _frame.TryGetHeader(_token, denseIndex, out header);
        }

        internal bool Commit() => _frame != null && _frame.Commit(_token);
        internal bool Cancel() => _frame != null && _frame.CancelFinalized(_token);

        internal bool IsCommitReady => _frame != null && _frame.IsCommitReady(_token);

        internal void CommitPrepared()
        {
            _frame.CommitPrepared(_token);
        }

        internal void CommitPreparedNoFail()
        {
            _frame.CommitPreparedNoFail();
        }
    }

    /// <summary>在有效的写入租约内更新该候选已登记的操作字段。</summary>
    internal readonly struct OperationFrameWriter
    {
        private readonly OperationFrame _frame;
        private readonly ulong _token;

        internal OperationFrameWriter(OperationFrame frame, ulong token)
        {
            _frame = frame;
            _token = token;
        }

        public bool IsValid => _frame != null && _frame.IsWriting(_token);

        internal bool TryResolveTypedHandle<TOperation>(
            out OperationHandle<TOperation> handle)
            where TOperation : class, IOperation
        {
            handle = default;
            return _frame != null &&
                   _frame.Registry.TryResolveTypedHandle(out handle);
        }

        internal OperationField<TValue> ResolveField<TOperation, TValue>(
            int fieldIndex)
            where TOperation : class, IOperation
            where TValue : unmanaged
        {
            return _frame != null &&
                   _frame.Registry.TryResolveTypedField<TOperation, TValue>(
                       fieldIndex,
                       out OperationField<TValue> field)
                ? field
                : default;
        }

        internal bool WriteField(OperationWriteRank rank, OperationDescriptor operation, int fieldIndex,
            ReadOnlySpan<byte> value) => _frame != null && _frame.TryWriteField(_token, rank, operation, fieldIndex, value);

        public bool Write<TValue>(
            OperationField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return Write(OperationWriteRank.Base, field, value);
        }

        public bool Write<TValue>(
            OperationWriteRank rank,
            OperationField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return _frame != null && _frame.TryWrite(_token, rank, field, value);
        }

        public bool EnableDiscrete<TOperation>(
            OperationHandle<TOperation> handle,
            ActivationId activationId)
            where TOperation : class, IOperation
        {
            return EnableDiscrete(OperationWriteRank.Base, handle, activationId);
        }

        public bool EnableDiscrete<TOperation>(
            OperationWriteRank rank,
            OperationHandle<TOperation> handle,
            ActivationId activationId)
            where TOperation : class, IOperation
        {
            return _frame != null &&
                   _frame.TryEnableDiscrete(_token, rank, handle, activationId);
        }

        internal bool TryFinalize(out FinalizedOperationFrame finalized)
        {
            if (_frame == null)
            {
                finalized = default;
                return false;
            }

            return _frame.TryFinalize(_token, out finalized);
        }

        internal bool Cancel() => _frame != null && _frame.CancelPrepared(_token);
    }
}
