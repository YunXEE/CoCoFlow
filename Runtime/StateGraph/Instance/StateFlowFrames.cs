using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 实例管理 Context 帧存储、写入租约以及提交和恢复凭据，Host 在整步屏障应用准备好的结果。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>提供某一投影恢复 Context 时所需的帧身份和槽值。</summary>
    internal readonly struct ProjectionRestoreSource
    {
        public ProjectionRestoreSource(
            GraphInstanceId graphInstanceId,
            TimelineId timelineId,
            ClockDomainId clockDomainId,
            ExecutionSequence executionSequence,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ContextRevision revision)
        {
            GraphInstanceId = graphInstanceId;
            TimelineId = timelineId;
            ClockDomainId = clockDomainId;
            ExecutionSequence = executionSequence;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            Revision = revision;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TimelineId TimelineId { get; }
        public ClockDomainId ClockDomainId { get; }
        public ExecutionSequence ExecutionSequence { get; }
        public TimelineEpoch TimelineEpoch { get; }
        public TimelineTick Tick { get; }
        public ContextRevision Revision { get; }
        public bool IsValid => GraphInstanceId.IsValid && TimelineId.IsValid &&
                               ClockDomainId.IsValid && Revision.IsValid;
    }

    /// <summary>按布局和 codec 编解码指定投影的数据，并核验恢复身份。</summary>
    internal sealed class ContextProjectionCodec
    {
        private const uint Magic = 0x43434650U;
        private const uint FormatVersion = 2U;
        private const int HeaderSize = 108;
        private const int EntryHeaderSize = 48;

        private readonly ContextFrameLayout _layout;
        private readonly ContextCodecRegistry _registry;
        private readonly ProjectionSlotBinding[] _bindings;

        private ContextProjectionCodec(
            ContextFrameLayout layout,
            ContextCodecRegistry registry,
            ContextProjection projection,
            ProjectionSlotBinding[] bindings,
            int maxEncodedSize)
        {
            _layout = layout;
            _registry = registry;
            Projection = projection;
            _bindings = bindings;
            MaxEncodedSize = maxEncodedSize;
        }

        public ContextFrameLayout Layout => _layout;
        public ContextProjection Projection { get; }
        public int MaxEncodedSize { get; }

        public static bool TryCreate(
            ContextFrameLayout layout,
            ContextCodecRegistry registry,
            ContextProjection projection,
            out ContextProjectionCodec codec,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (layout == null ||
                (projection != ContextProjection.Temporal &&
                 projection != ContextProjection.Durable))
            {
                codec = null;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (registry == null || !registry.IsFrozen)
            {
                codec = null;
                diagnosticCode = CoCoDiagnosticCode.RegistryNotFrozen;
                return false;
            }

            if (!layout.HasProjectionDependencyClosure(projection))
            {
                codec = null;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            var bindings = new List<ProjectionSlotBinding>();
            long maxEncodedSize = HeaderSize;
            for (int index = 0; index < layout.Slots.Count; index++)
            {
                StateSlotDescriptor slot = layout.Slots[index];
                if ((slot.Projection & projection) == 0 ||
                    slot.RestorePolicy != ContextRestorePolicy.Stored)
                {
                    continue;
                }

                IContextValueCodecAdapter adapter = null;
                int maxValueSize = slot.ByteSize;
                if (slot.Codec.UsesCustomCodec)
                {
                    if (!registry.TryResolve(
                            slot.Codec,
                            slot.ValueType,
                            out adapter,
                            out diagnosticCode))
                    {
                        codec = null;
                        return false;
                    }

                    maxValueSize = adapter.MaxEncodedSize;
                }

                maxEncodedSize += EntryHeaderSize + (long)maxValueSize;
                if (maxEncodedSize > int.MaxValue)
                {
                    codec = null;
                    diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                    return false;
                }

                bindings.Add(new ProjectionSlotBinding(slot, adapter, maxValueSize));
            }

            codec = new ContextProjectionCodec(
                layout,
                registry,
                projection,
                bindings.ToArray(),
                (int)maxEncodedSize);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryEncode(
            ContextFrame frame,
            Span<byte> destination,
            out int bytesWritten,
            out CoCoDiagnosticCode diagnosticCode)
        {
            bytesWritten = 0;
            if (!frame.IsAlive || !_layout.IsSameInstance(frame.Layout) ||
                !frame.Header.HasExactLayoutIdentity ||
                !_layout.HasExactIdentity(
                    frame.Header.LayoutId,
                    frame.Header.LayoutVersion,
                    frame.Header.LayoutSchemaHash))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            return TryEncodeCore(
                frame.Header,
                frame.Revision,
                frame.Buffer,
                destination,
                out bytesWritten,
                out diagnosticCode);
        }

        internal bool TryEncodeFinalized(
            StateFlowFrameHeader header,
            ContextRevision revision,
            byte[] buffer,
            Span<byte> destination,
            out int bytesWritten,
            out CoCoDiagnosticCode diagnosticCode)
        {
            bytesWritten = 0;
            if (buffer == null ||
                buffer.Length != _layout.ByteSize ||
                !header.IsValid ||
                header.Identity.Kind != StateFlowFrameKind.Context ||
                !header.HasExactLayoutIdentity ||
                !_layout.HasExactIdentity(
                    header.LayoutId,
                    header.LayoutVersion,
                    header.LayoutSchemaHash) ||
                !revision.IsValid)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            return TryEncodeCore(
                header,
                revision,
                buffer,
                destination,
                out bytesWritten,
                out diagnosticCode);
        }

        private bool TryEncodeCore(
            StateFlowFrameHeader header,
            ContextRevision revision,
            byte[] buffer,
            Span<byte> destination,
            out int bytesWritten,
            out CoCoDiagnosticCode diagnosticCode)
        {
            bytesWritten = 0;
            if (destination.Length < MaxEncodedSize)
            {
                diagnosticCode = CoCoDiagnosticCode.CommitPreparationFailed;
                return false;
            }

            int cursor = 0;
            StateFlowBinary.WriteUInt32(destination, ref cursor, Magic);
            StateFlowBinary.WriteUInt32(destination, ref cursor, FormatVersion);
            StateFlowBinary.WriteUInt32(destination, ref cursor, (uint)Projection);
            StateFlowBinary.WriteUInt64(destination, ref cursor, _layout.LayoutId.High);
            StateFlowBinary.WriteUInt64(destination, ref cursor, _layout.LayoutId.Low);
            StateFlowBinary.WriteUInt32(destination, ref cursor, _layout.Version);
            StateFlowBinary.WriteUInt64(destination, ref cursor, _layout.SchemaHash);
            StateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                header.Identity.GraphInstanceId.Value);
            StateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                header.TickFrame.TimelineId.High);
            StateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                header.TickFrame.TimelineId.Low);
            StateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                header.TickFrame.ClockDomainId.Value);
            StateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                header.TickFrame.ExecutionSequence.Value);
            StateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                header.Identity.TimelineEpoch.Value);
            StateFlowBinary.WriteUInt64(destination, ref cursor, header.Identity.Tick.Value);
            StateFlowBinary.WriteUInt64(destination, ref cursor, revision.Value);
            StateFlowBinary.WriteUInt32(destination, ref cursor, (uint)_bindings.Length);

            for (int index = 0; index < _bindings.Length; index++)
            {
                ProjectionSlotBinding binding = _bindings[index];
                StateSlotDescriptor slot = binding.Slot;
                StateFlowBinary.WriteUInt64(destination, ref cursor, slot.SlotId.High);
                StateFlowBinary.WriteUInt64(destination, ref cursor, slot.SlotId.Low);
                StateFlowBinary.WriteUInt64(destination, ref cursor, slot.Codec.CodecId.High);
                StateFlowBinary.WriteUInt64(destination, ref cursor, slot.Codec.CodecId.Low);
                StateFlowBinary.WriteUInt32(destination, ref cursor, slot.Codec.Version);
                StateFlowBinary.WriteUInt64(destination, ref cursor, binding.ValueTypeHash);
                int lengthOffset = cursor;
                StateFlowBinary.WriteUInt32(destination, ref cursor, 0U);

                int payloadLength;
                if (binding.Adapter == null)
                {
                    payloadLength = slot.ByteSize;
                    new ReadOnlySpan<byte>(buffer, slot.ByteOffset, slot.ByteSize)
                        .CopyTo(destination.Slice(cursor, slot.ByteSize));
                }
                else if (!binding.Adapter.TryEncode(
                             buffer,
                             slot.ByteOffset,
                             destination.Slice(cursor, binding.MaxValueSize),
                             out payloadLength) ||
                         payloadLength <= 0 ||
                         payloadLength > binding.MaxValueSize)
                {
                    diagnosticCode = CoCoDiagnosticCode.UnknownCodec;
                    return false;
                }

                int patchCursor = lengthOffset;
                StateFlowBinary.WriteUInt32(destination, ref patchCursor, (uint)payloadLength);
                cursor += payloadLength;
            }

            bytesWritten = cursor;
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryDecodeAndPrepareRestore(
            ReadOnlySpan<byte> source,
            ContextFrameArena arena,
            TickFrame resumedTickFrame,
            out FinalizedContextCommit finalized,
            out int bytesRead,
            out ContextCommitStatus commitStatus,
            out CoCoDiagnosticCode diagnosticCode)
        {
            finalized = default;
            bytesRead = 0;
            commitStatus = ContextCommitStatus.RestoreFailed;
            if (arena == null || !_layout.IsSameInstance(arena.Layout))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (!TryValidateSource(
                    source,
                    out ProjectionRestoreSource restoreSource,
                    out int encodedLength,
                    out diagnosticCode))
            {
                return false;
            }

            if (!arena.TryPrepareProjectionRestore(
                    this,
                    restoreSource,
                    source.Slice(0, encodedLength),
                    resumedTickFrame,
                    out finalized,
                    out commitStatus,
                    out CoCoDiagnosticCode restoreDiagnostic))
            {
                diagnosticCode = restoreDiagnostic;
                return false;
            }

            bytesRead = encodedLength;
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool TryDecodeAndPrepareImport(
            ReadOnlySpan<byte> source,
            ContextFrameArena arena,
            TickFrame importedTickFrame,
            out FinalizedContextCommit finalized,
            out ProjectionRestoreSource restoreSource,
            out int bytesRead,
            out ContextCommitStatus commitStatus,
            out CoCoDiagnosticCode diagnosticCode)
        {
            finalized = default;
            restoreSource = default;
            bytesRead = 0;
            commitStatus = ContextCommitStatus.RestoreFailed;
            if (arena == null || !_layout.IsSameInstance(arena.Layout))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (!TryValidateSource(
                    source,
                    out restoreSource,
                    out int encodedLength,
                    out diagnosticCode))
            {
                return false;
            }

            if (!arena.TryPrepareProjectionImport(
                    this,
                    restoreSource,
                    source.Slice(0, encodedLength),
                    importedTickFrame,
                    out finalized,
                    out commitStatus,
                    out CoCoDiagnosticCode restoreDiagnostic))
            {
                diagnosticCode = restoreDiagnostic;
                return false;
            }

            bytesRead = encodedLength;
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool TryDecodePayload(
            ReadOnlySpan<byte> source,
            byte[] destination,
            out CoCoDiagnosticCode diagnosticCode,
            IReadOnlyList<StateSlotDescriptor> retainedSlots = null,
            byte[] authority = null)
        {
            if (destination == null || destination.Length != _layout.ByteSize)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            _layout.CopyDefaultsTo(destination);
            int cursor = HeaderSize;
            for (int index = 0; index < _bindings.Length; index++)
            {
                ProjectionSlotBinding binding = _bindings[index];
                cursor += EntryHeaderSize - sizeof(uint);
                if (!StateFlowBinary.TryReadUInt32(source, ref cursor, out uint payloadLengthValue))
                {
                    diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                    return false;
                }

                int payloadLength = (int)payloadLengthValue;
                ReadOnlySpan<byte> payload = source.Slice(cursor, payloadLength);
                if (binding.Adapter == null)
                {
                    payload.CopyTo(new Span<byte>(
                        destination,
                        binding.Slot.ByteOffset,
                        binding.Slot.ByteSize));
                }
                else if (!binding.Adapter.TryDecode(
                             payload,
                             destination,
                             binding.Slot.ByteOffset,
                             out int bytesRead) ||
                         bytesRead != payloadLength)
                {
                    diagnosticCode = CoCoDiagnosticCode.UnknownCodec;
                    return false;
                }

                cursor += payloadLength;
            }

            _layout.CopyRetainedSlots(retainedSlots, authority, destination);
            if (!_layout.TryRebuildDerived(destination))
            {
                diagnosticCode = CoCoDiagnosticCode.CommitPreparationFailed;
                return false;
            }

            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool TryValidateSource(
            ReadOnlySpan<byte> source,
            out ProjectionRestoreSource restoreSource,
            out int encodedLength,
            out CoCoDiagnosticCode diagnosticCode)
        {
            restoreSource = default;
            encodedLength = 0;
            int cursor = 0;
            if (source.Length < HeaderSize ||
                !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint magic) ||
                !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint formatVersion) ||
                !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint projection) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong layoutHigh) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong layoutLow) ||
                !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint layoutVersion) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong layoutSchemaHash) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong graphInstanceValue) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong timelineHigh) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong timelineLow) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong clockDomainValue) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong executionSequenceValue) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong timelineEpochValue) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong tickValue) ||
                !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong revisionValue) ||
                !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint slotCount))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (magic != Magic || formatVersion != FormatVersion || projection != (uint)Projection)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (!FrameLayoutId.TryCreate(layoutHigh, layoutLow, out FrameLayoutId layoutId) ||
                !_layout.HasExactIdentity(layoutId, layoutVersion, layoutSchemaHash))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (slotCount != (uint)_bindings.Length ||
                !GraphInstanceId.TryCreate(graphInstanceValue, out GraphInstanceId graphInstanceId) ||
                !TimelineId.TryCreate(timelineHigh, timelineLow, out TimelineId timelineId) ||
                !ClockDomainId.TryCreate(clockDomainValue, out ClockDomainId clockDomainId))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            var revision = new ContextRevision(revisionValue);
            restoreSource = new ProjectionRestoreSource(
                graphInstanceId,
                timelineId,
                clockDomainId,
                new ExecutionSequence(executionSequenceValue),
                new TimelineEpoch(timelineEpochValue),
                new TimelineTick(tickValue),
                revision);
            if (!restoreSource.IsValid)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            for (int index = 0; index < _bindings.Length; index++)
            {
                ProjectionSlotBinding binding = _bindings[index];
                if (!StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong slotHigh) ||
                    !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong slotLow) ||
                    !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong codecHigh) ||
                    !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong codecLow) ||
                    !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint codecVersion) ||
                    !StateFlowBinary.TryReadUInt64(source, ref cursor, out ulong valueTypeHash) ||
                    !StateFlowBinary.TryReadUInt32(source, ref cursor, out uint payloadLengthValue))
                {
                    diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                    return false;
                }

                if (slotHigh != binding.Slot.SlotId.High || slotLow != binding.Slot.SlotId.Low)
                {
                    diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
                    return false;
                }

                if (!TryValidateEncodedCodec(
                        binding,
                        codecHigh,
                        codecLow,
                        codecVersion,
                        valueTypeHash,
                        out diagnosticCode))
                {
                    return false;
                }

                if (payloadLengthValue > int.MaxValue)
                {
                    diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                    return false;
                }

                int payloadLength = (int)payloadLengthValue;
                if (payloadLength <= 0 || payloadLength > binding.MaxValueSize ||
                    (binding.Adapter == null && payloadLength != binding.Slot.ByteSize) ||
                    payloadLength > source.Length - cursor)
                {
                    diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                    return false;
                }

                cursor += payloadLength;
            }

            encodedLength = cursor;
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        private bool TryValidateEncodedCodec(
            ProjectionSlotBinding binding,
            ulong codecHigh,
            ulong codecLow,
            uint codecVersion,
            ulong valueTypeHash,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (valueTypeHash != binding.ValueTypeHash)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
                return false;
            }

            CodecDescriptor expected = binding.Slot.Codec;
            if (codecHigh == expected.CodecId.High &&
                codecLow == expected.CodecId.Low &&
                codecVersion == expected.Version)
            {
                diagnosticCode = CoCoDiagnosticCode.None;
                return true;
            }

            if (!CodecId.TryCreate(codecHigh, codecLow, out CodecId codecId))
            {
                diagnosticCode = expected.UsesCustomCodec
                    ? CoCoDiagnosticCode.UnknownCodec
                    : CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            diagnosticCode = _registry.Classify(
                new CodecDescriptor(codecId, codecVersion),
                binding.Slot.ValueType);
            if (diagnosticCode == CoCoDiagnosticCode.None)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
            }

            return false;
        }

        /// <summary>将投影中的槽绑定到对应值类型及持久化编码器。</summary>
        private sealed class ProjectionSlotBinding
        {
            public ProjectionSlotBinding(
                StateSlotDescriptor slot,
                IContextValueCodecAdapter adapter,
                int maxValueSize)
            {
                Slot = slot;
                Adapter = adapter;
                MaxValueSize = maxValueSize;
                ValueTypeHash = StateFlowSchemaHash.HashType(slot.ValueType);
            }

            public StateSlotDescriptor Slot { get; }
            public IContextValueCodecAdapter Adapter { get; }
            public int MaxValueSize { get; }
            public ulong ValueTypeHash { get; }
        }
    }

    /// <summary>按存储索引与代次读取已物化并仍有效的 Context 帧。</summary>
    internal interface IContextMaterializedReadSource
    {
        bool TryGetMaterializedRead(
            ulong token,
            out ContextFrameLayout layout,
            out byte[] buffer,
            out StateFlowFrameHeader header,
            out ContextRevision revision,
            out ContextFrameOrigin origin);
    }

    /// <summary>在恢复准备期间按已校验的布局读取目标帧。</summary>
    internal readonly struct ContextRestoreReadView
    {
        private readonly ContextFrame _source;
        private readonly ContextFrameLayout _layout;
        private readonly IContextMaterializedReadSource _materializedSource;
        private readonly ulong _materializedToken;

        internal ContextRestoreReadView(
            ContextFrame source,
            ContextFrameLayout layout)
        {
            _source = source;
            _layout = source.IsAlive &&
                      layout != null &&
                      layout.IsSameInstance(source.Layout)
                ? layout
                : null;
            _materializedSource = null;
            _materializedToken = 0UL;
        }

        internal ContextRestoreReadView(
            IContextMaterializedReadSource materializedSource,
            ulong token)
        {
            _source = default;
            _layout = null;
            _materializedSource = materializedSource;
            _materializedToken = token;
        }

        internal bool IsValid =>
            TryResolve(
                out _,
                out _,
                out _,
                out _,
                out _);

        internal StateFlowFrameHeader Header =>
            TryResolve(out _, out _, out StateFlowFrameHeader header, out _, out _)
                ? header
                : default;

        internal ContextRevision Revision =>
            TryResolve(out _, out _, out _, out ContextRevision revision, out _)
                ? revision
                : default;

        internal ContextFrameOrigin Origin =>
            TryResolve(out _, out _, out _, out _, out ContextFrameOrigin origin)
                ? origin
                : default;

        internal ContextFrameLayout Layout =>
            TryResolve(out ContextFrameLayout layout, out _, out _, out _, out _)
                ? layout
                : null;

        internal bool TryRead<TValue>(
            StateSlot<TValue> slot,
            out TValue value)
            where TValue : unmanaged
        {
            value = default;
            if (!TryResolve(
                    out ContextFrameLayout layout,
                    out byte[] buffer,
                    out _,
                    out _,
                    out _) ||
                !slot.IsValid ||
                !slot.IsFor(layout) ||
                slot.DenseIndex < 0 ||
                slot.DenseIndex >= layout.Slots.Count)
            {
                return false;
            }

            StateSlotDescriptor descriptor = layout.Slots[slot.DenseIndex];
            if (descriptor.SlotId != slot.SlotId ||
                descriptor.DenseIndex != slot.DenseIndex ||
                descriptor.ValueType != typeof(TValue) ||
                descriptor.ByteOffset != slot.ByteOffset ||
                descriptor.ByteSize != slot.ByteSize ||
                descriptor.ByteSize != StateFlowTypeRules.SizeOf<TValue>())
            {
                return false;
            }

            if (_materializedSource != null)
            {
                value = StateFlowTypeRules.Read<TValue>(buffer, slot.ByteOffset);
                return true;
            }

            if (descriptor.RestorePolicy == ContextRestorePolicy.Stored)
            {
                value = _source.Read(slot);
                return true;
            }

            if (descriptor.RestorePolicy == ContextRestorePolicy.ResetToDefault)
            {
                value = layout.ReadDefault(slot);
                return true;
            }

            return false;
        }

        internal bool TryRead<TValue>(
            StateSlotId slotId,
            out TValue value)
            where TValue : unmanaged
        {
            value = default;
            ContextFrameLayout layout = Layout;
            return layout != null &&
                   layout.TryResolveSlot(slotId, out StateSlot<TValue> slot) &&
                   TryRead(slot, out value);
        }

        private bool TryResolve(
            out ContextFrameLayout layout,
            out byte[] buffer,
            out StateFlowFrameHeader header,
            out ContextRevision revision,
            out ContextFrameOrigin origin)
        {
            if (_materializedSource != null)
            {
                return _materializedSource.TryGetMaterializedRead(
                    _materializedToken,
                    out layout,
                    out buffer,
                    out header,
                    out revision,
                    out origin);
            }

            if (_layout != null &&
                _source.IsAlive &&
                _layout.IsSameInstance(_source.Layout))
            {
                layout = _layout;
                buffer = _source.Buffer;
                header = _source.Header;
                revision = _source.Revision;
                origin = _source.Origin;
                return true;
            }

            layout = null;
            buffer = null;
            header = default;
            revision = default;
            origin = default;
            return false;
        }
    }

    /// <summary>以存储区与代次标识一次已提交 Context，读者可延长其有效期。</summary>
    public readonly struct ContextFrame : IContextFrame, IEquatable<ContextFrame>
    {
        private readonly ContextFrameStorage _storage;
        private readonly ulong _generation;

        /// <summary>此 Context 帧的身份和时钟。</summary>
        public StateFlowFrameHeader Header => IsAlive ? _storage.Header : default;
        /// <summary>此帧提交时的修订号。</summary>
        public ContextRevision Revision => IsAlive ? _storage.Revision : default;
        /// <summary>此帧的来源，例如正常提交或恢复。</summary>
        public ContextFrameOrigin Origin => IsAlive ? _storage.Origin : default;
        /// <summary>解释此帧中各槽值的冻结布局。</summary>
        public ContextFrameLayout Layout => IsAlive ? _storage.Layout : null;
        /// <summary>当前存储代次仍与此帧匹配时为真。</summary>
        public bool IsAlive => _storage != null && _storage.IsAlive(_generation);

        internal byte[] Buffer => IsAlive ? _storage.Buffer : null;
        internal ContextFrameReadView ReadView => new ContextFrameReadView(Layout, _storage, _generation);

        #region Public API

        /// <summary>从仍存活的帧读取所属布局的类型化槽，帧已释放或句柄不匹配时抛出异常。</summary>
        /// <param name="slot">目标布局中的类型化槽或槽标识。</param>
        /// <typeparam name="T">要读取的非托管槽值类型。</typeparam>
        /// <returns>当前帧中指定槽的值。使用失效帧或不匹配的槽会抛出异常。</returns>
        public T Read<T>(StateSlot<T> slot)
            where T : unmanaged
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("The ContextFrame or StateSlot handle is not valid for this read.");
            }

            return _storage.Read(slot);
        }

        /// <summary>增加当前帧代次的持有计数，帧已回收时返回 false。</summary>
        /// <returns>存储代次有效且取得了一份持有时为真。</returns>
        public bool Retain() => _storage != null && _storage.TryRetain(_generation);
        /// <summary>释放当前帧的一份持有，帧已失效时返回 false。最后一个读者释放后存储可复用。</summary>
        /// <returns>成功释放一份持有时为真，失效或未持有时为假。</returns>
        public bool Release() => _storage != null && _storage.TryRelease(_generation);

        /// <summary>按<c>storage</c>、<c>generation</c>比较两个值，全部相等时返回 true。</summary>
        /// <param name="other">待比较的同类值。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public bool Equals(ContextFrame other) =>
            ReferenceEquals(_storage, other._storage) && _generation == other._generation;

        /// <summary>仅当对象属于同类型并且<c>storage</c>、<c>generation</c>相等时返回 true。</summary>
        /// <param name="obj">待比较的对象。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public override bool Equals(object obj) => obj is ContextFrame other && Equals(other);
        /// <summary>为哈希集合计算与<c>storage</c>、<c>generation</c>的相等判定一致的散列值。</summary>
        /// <returns>与相等判定一致的哈希值。</returns>
        public override int GetHashCode() =>
            unchecked(((_storage?.GetHashCode() ?? 0) * 397) ^ _generation.GetHashCode());

        /// <summary>比较两端的<c>storage</c>、<c>generation</c>，相等时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator ==(ContextFrame left, ContextFrame right) => left.Equals(right);
        /// <summary>比较两端的<c>storage</c>、<c>generation</c>，存在差异时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator !=(ContextFrame left, ContextFrame right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal ContextFrame(ContextFrameStorage storage, ulong generation)
        {
            _storage = storage;
            _generation = generation;
        }

        internal bool TryGetStorage(out ContextFrameStorage storage)
        {
            if (IsAlive)
            {
                storage = _storage;
                return true;
            }

            storage = null;
            return false;
        }

        #endregion
    }

    /// <summary>为帧池中的一项保存槽值、帧头与引用计数。</summary>
    internal sealed class ContextFrameStorage : ICommittedContextReadSource
    {
        private int _externalRetainCount;
        private bool _isArenaOwned;

        public ContextFrameStorage(ContextFrameLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Buffer = layout.CreateBuffer();
        }

        public StateFlowFrameHeader Header { get; private set; }
        public ContextRevision Revision { get; private set; }
        public ContextFrameOrigin Origin { get; private set; }
        public ContextFrameLayout Layout { get; }
        public byte[] Buffer { get; }
        public ulong Generation { get; private set; }
        public long RetainCount => (_isArenaOwned ? 1L : 0L) + _externalRetainCount;
        public bool CanReuse => RetainCount == 0 && Generation != ulong.MaxValue;

        public TValue Read<TValue>(StateSlot<TValue> slot) where TValue : unmanaged
        {
            if (!slot.IsValid || !slot.IsFor(Layout))
                throw new InvalidOperationException("The ContextFrame or StateSlot handle is not valid for this read.");
            return StateFlowTypeRules.Read<TValue>(Buffer, slot.ByteOffset);
        }

        public bool IsAlive(ulong generation) =>
            generation != 0UL && generation == Generation && RetainCount > 0;

        public bool TryPrepare(
            StateFlowFrameHeader header,
            ContextRevision revision,
            ContextFrameOrigin origin,
            ContextFrame source,
            out ContextCommitStatus status)
        {
            if (!CanReuse)
            {
                status = ContextCommitStatus.CapacityExhausted;
                return false;
            }

            Generation++;
            Header = header;
            Revision = revision;
            Origin = origin;
            _isArenaOwned = false;
            _externalRetainCount = 0;

            if (!source.IsAlive)
            {
                Layout.CopyDefaultsTo(Buffer);
                status = ContextCommitStatus.None;
                return true;
            }

            if (!source.TryGetStorage(out ContextFrameStorage sourceStorage) ||
                !Layout.IsSameInstance(sourceStorage.Layout) ||
                !source.Header.HasExactLayoutIdentity ||
                !Layout.HasExactIdentity(
                    source.Header.LayoutId,
                    source.Header.LayoutVersion,
                    source.Header.LayoutSchemaHash))
            {
                status = ContextCommitStatus.RestoreFailed;
                return false;
            }

            if (origin.IsRestore)
            {
                if (!Layout.TryRestore(sourceStorage.Buffer, Buffer))
                {
                    status = ContextCommitStatus.DerivedRebuildFailed;
                    return false;
                }

                status = ContextCommitStatus.None;
                return true;
            }

            System.Buffer.BlockCopy(sourceStorage.Buffer, 0, Buffer, 0, Buffer.Length);
            status = ContextCommitStatus.None;
            return true;
        }

        public ContextFrame Seal()
        {
            _isArenaOwned = true;
            return new ContextFrame(this, Generation);
        }

        public void ReleaseArenaOwnership()
        {
            _isArenaOwned = false;
        }

        public bool TryRetain(ulong generation)
        {
            if (!IsAlive(generation) || _externalRetainCount == int.MaxValue)
            {
                return false;
            }

            _externalRetainCount++;
            return true;
        }

        public bool TryRelease(ulong generation)
        {
            if (generation == 0UL || generation != Generation || _externalRetainCount <= 0)
            {
                return false;
            }

            _externalRetainCount--;
            return true;
        }

        public void Abandon()
        {
            Header = default;
            Revision = default;
            Origin = default;
            _isArenaOwned = false;
            _externalRetainCount = 0;
        }
    }

    /// <summary>区分一次 Context 候选提交成功及各类拒绝结果。</summary>
    public enum ContextCommitStatus
    {
        /// <summary>尚无指定值。</summary>
        None = 0,
        /// <summary>操作已成功提交。</summary>
        Succeeded = 1,
        /// <summary>时钟与本次提交要求不符。</summary>
        InvalidTick = 2,
        /// <summary>提交来源无效。</summary>
        InvalidOrigin = 3,
        /// <summary>来源帧与目标图不匹配。</summary>
        GraphMismatch = 4,
        /// <summary>来源帧与目标布局不匹配。</summary>
        LayoutMismatch = 5,
        /// <summary>可用于本次准备的容量已耗尽。</summary>
        CapacityExhausted = 6,
        /// <summary>已有尚未结束的准备。</summary>
        PreparationAlreadyActive = 7,
        /// <summary>准备凭据已失效或与事务不匹配。</summary>
        InvalidPreparation = 8,
        /// <summary>候选已取消。</summary>
        Cancelled = 9,
        /// <summary>恢复准备未成功。</summary>
        RestoreFailed = 10,
        /// <summary>无可用的后续修订号。</summary>
        RevisionExhausted = 11,
        /// <summary>恢复后重建派生槽失败。</summary>
        DerivedRebuildFailed = 12
    }

    /// <summary>向持有者返回 Context 提交状态、诊断及成功生成的帧。</summary>
    public readonly struct ContextCommitResult
    {

        #region Public API

        /// <summary>本次 Context 提交的结果状态。</summary>
        public ContextCommitStatus Status { get; }
        /// <summary>与此结果关联的 Context 帧。</summary>
        public ContextFrame Frame { get; }
        /// <summary>本次提交结果为成功时为真。</summary>
        public bool Succeeded => Status == ContextCommitStatus.Succeeded;

        #endregion

        #region Internal Logic

        internal ContextCommitResult(ContextCommitStatus status, ContextFrame frame)
        {
            Status = status;
            Frame = frame;
        }

        #endregion
    }

    /// <summary>在所属候选有效时将类型匹配的值写入允许的槽。</summary>
    public readonly struct ContextFrameWriter
    {
        private readonly ContextFrameArena _arena;
        private readonly ulong _token;
        private readonly StateBlockHandle _block;

        /// <summary>本次写者有权写入的状态块。</summary>
        public StateBlockHandle Block => _block;
        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => _arena != null && _arena.IsPreparationActive(_token) && _block.IsValid;

        #region Public API

        /// <summary>在租约有效时将值写入该候选布局的槽，过期或槽不匹配时返回 false。</summary>
        /// <param name="slot">目标布局中的类型化槽或槽标识。</param>
        /// <param name="value">写入目标槽的值。</param>
        /// <typeparam name="TValue">状态槽的非托管值类型。</typeparam>
        /// <returns>写者及槽属于本次准备且写入成功时为真。</returns>
        public bool Write<TValue>(StateSlot<TValue> slot, in TValue value)
            where TValue : unmanaged
        {
            return IsValid && _arena.TryWrite(_token, _block, slot, value);
        }

        #endregion

        #region Internal Logic

        internal ContextFrameWriter(
            ContextFrameArena arena,
            ulong token,
            StateBlockHandle block)
        {
            _arena = arena;
            _token = token;
            _block = block;
        }

        #endregion
    }

    /// <summary>标记通过验证的 Context 候选，供事务屏障复核其有效性。</summary>
    public readonly struct PreparedContextCommit
    {
        private readonly ContextFrameArena _arena;
        private readonly ulong _token;

        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => _arena != null && _arena.IsPreparationActive(_token);

        #region Internal Logic

        internal PreparedContextCommit(ContextFrameArena arena, ulong token)
        {
            _arena = arena;
            _token = token;
        }

        internal bool TryGetWriter(
            StateBlockHandle block,
            out ContextFrameWriter writer)
        {
            if (!IsValid || !_arena.CanWrite(_token, block))
            {
                writer = default;
                return false;
            }

            writer = new ContextFrameWriter(_arena, _token, block);
            return true;
        }

        internal bool TryFinalize(
            out FinalizedContextCommit finalized,
            out ContextCommitStatus status)
        {
            if (_arena == null)
            {
                finalized = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            return _arena.TryFinalize(_token, out finalized, out status);
        }

        internal ContextCommitStatus Cancel()
        {
            return _arena == null
                ? ContextCommitStatus.InvalidPreparation
                : _arena.CancelPrepared(_token);
        }

        #endregion
    }

    /// <summary>标记可在整步屏障应用或取消的 Context 提交。</summary>
    public readonly struct FinalizedContextCommit
    {
        private readonly ContextFrameArena _arena;
        private readonly ulong _token;

        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => _arena != null && _arena.IsFinalized(_token);
        internal bool IsCommitReady => _arena != null && _arena.IsCommitReady(_token);

        #region Internal Logic

        internal FinalizedContextCommit(ContextFrameArena arena, ulong token)
        {
            _arena = arena;
            _token = token;
        }

        internal bool TryCreateRestoreReadView(out ContextRestoreReadView view)
        {
            if (_arena == null || !_arena.IsFinalized(_token))
            {
                view = default;
                return false;
            }

            view = new ContextRestoreReadView(_arena, _token);
            return view.IsValid;
        }

        internal bool TryGetMetadata(
            out StateFlowFrameHeader header,
            out ContextRevision revision,
            out ContextFrameOrigin origin)
        {
            if (_arena != null)
            {
                return _arena.TryGetFinalizedMetadata(
                    _token,
                    out header,
                    out revision,
                    out origin);
            }

            header = default;
            revision = default;
            origin = default;
            return false;
        }

        internal bool TryEncode(
            ContextProjectionCodec codec,
            Span<byte> destination,
            out int bytesWritten,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_arena != null)
            {
                return _arena.TryEncodeFinalized(
                    _token,
                    codec,
                    destination,
                    out bytesWritten,
                    out diagnosticCode);
            }

            bytesWritten = 0;
            diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
            return false;
        }

        internal ContextFrame CommitNoFailUnchecked() =>
            _arena.CommitNoFailUnchecked();

        internal ContextCommitResult Commit()
        {
            return _arena == null
                ? new ContextCommitResult(ContextCommitStatus.InvalidPreparation, default)
                : _arena.Commit(_token);
        }

        internal ContextCommitStatus Cancel()
        {
            return _arena == null
                ? ContextCommitStatus.InvalidPreparation
                : _arena.CancelFinalized(_token);
        }

        #endregion
    }

    /// <summary>按布局复用 Context 帧存储，并管理候选写入、引用和恢复。</summary>
    public sealed class ContextFrameArena : IDisposable, IContextMaterializedReadSource
    {
        private readonly GraphInstanceId _graphInstanceId;
        private readonly ContextFrameStorage[] _frames;
        private ContextFrame _current;
        private ContextFrameStorage _currentStorage;
        private ContextFrameStorage _reserved;
        private ulong _activeToken;
        private ulong _nextToken;
        private bool _isFinalized;
        private bool _isCallbackActive;
        private bool _disposeRequested;
        private bool _isDisposed;

        /// <summary>解释此帧中各槽值的冻结布局。</summary>
        public ContextFrameLayout Layout { get; }
        internal IReadOnlyList<StateSlotDescriptor> RetainedRestoreSlots { get; set; }
        /// <summary>当前已提交的帧。</summary>
        public ContextFrame Current => _isDisposed ? default : _current;
        /// <summary>当前帧之前保留的提交帧。</summary>
        public ContextFrameReadView Previous => _isDisposed
            ? default
            : HasCurrent
                ? _current.ReadView
                : new ContextFrameReadView(Layout);
        /// <summary>已经产生可读取的当前帧时为真。</summary>
        public bool HasCurrent => !_isDisposed && _current.IsAlive;
        internal bool HasAvailableCapacity => !_isDisposed && _reserved == null && FindAvailableFrame() != null;
        internal bool IsDisposed => _isDisposed;
        /// <summary>此存储可接纳的最大条目数。</summary>
        public int Capacity => _frames.Length;

        #region Public API

        bool IContextMaterializedReadSource.TryGetMaterializedRead(
            ulong token,
            out ContextFrameLayout layout,
            out byte[] buffer,
            out StateFlowFrameHeader header,
            out ContextRevision revision,
            out ContextFrameOrigin origin)
        {
            if (!IsFinalized(token))
            {
                layout = null;
                buffer = null;
                header = default;
                revision = default;
                origin = default;
                return false;
            }

            layout = Layout;
            buffer = _reserved.Buffer;
            header = _reserved.Header;
            revision = _reserved.Revision;
            origin = _reserved.Origin;
            return true;
        }

        /// <summary>结束帧池的使用并释放其持有的状态，随后不能再从该池准备提交。</summary>
        public void Dispose()
        {
            if (_isDisposed || _disposeRequested)
            {
                return;
            }

            if (_isCallbackActive)
            {
                _disposeRequested = true;
                return;
            }

            DisposeCore();
        }

        #endregion

        #region Internal Logic

        internal ContextFrameArena(
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            int capacity)
        {
            if (!graphInstanceId.IsValid)
            {
                throw new ArgumentException("GraphInstanceId must be valid.", nameof(graphInstanceId));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (capacity < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "ContextFrame arena capacity must be at least two.");
            }

            if (capacity > 0x7FEFFFFF || (long)layout.ByteSize * capacity > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "ContextFrame arena capacity exceeds the checked managed arena size.");
            }

            _graphInstanceId = graphInstanceId;
            Layout = layout;
            _frames = new ContextFrameStorage[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _frames[index] = new ContextFrameStorage(layout);
            }

            _currentStorage = _frames[0];
        }

        internal bool TryPrepare(
            TickFrame tickFrame,
            out PreparedContextCommit prepared,
            out ContextCommitStatus status)
        {
            if (_isDisposed)
            {
                prepared = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (_isCallbackActive)
            {
                prepared = default;
                status = ContextCommitStatus.PreparationAlreadyActive;
                return false;
            }

            return TryPrepareCore(tickFrame, ContextFrameOrigin.Commit(), _current, out prepared, out status);
        }

        internal bool TryPrepareRestore(
            ContextFrame source,
            TickFrame resumedTickFrame,
            out FinalizedContextCommit finalized,
            out ContextCommitStatus status)
        {
            if (_isDisposed)
            {
                finalized = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (_isCallbackActive)
            {
                finalized = default;
                status = ContextCommitStatus.PreparationAlreadyActive;
                return false;
            }

            if (!TryValidateRestore(source, resumedTickFrame, out status))
            {
                finalized = default;
                return false;
            }

            if (!TryPrepareCore(
                    resumedTickFrame,
                    ContextFrameOrigin.RestoreFrom(source),
                    source,
                    out _,
                    out status))
            {
                finalized = default;
                return false;
            }

            _isFinalized = true;
            finalized = new FinalizedContextCommit(this, _activeToken);
            return true;
        }

        internal bool TryValidateRestore(
            ContextFrame source,
            TickFrame resumedTickFrame,
            out ContextCommitStatus status)
        {
            if (_isDisposed)
            {
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (!resumedTickFrame.IsValid)
            {
                status = ContextCommitStatus.InvalidTick;
                return false;
            }

            if (!source.IsAlive || !Layout.IsSameInstance(source.Layout) ||
                !source.Header.HasExactLayoutIdentity ||
                !Layout.HasExactIdentity(
                    source.Header.LayoutId,
                    source.Header.LayoutVersion,
                    source.Header.LayoutSchemaHash))
            {
                status = ContextCommitStatus.LayoutMismatch;
                return false;
            }

            if (source.Header.Identity.GraphInstanceId != _graphInstanceId)
            {
                status = ContextCommitStatus.GraphMismatch;
                return false;
            }

            if (resumedTickFrame.TimelineEpoch.Value <=
                    source.Header.Identity.TimelineEpoch.Value ||
                !StateFlowTickOrder.IsStrictlyAfter(
                    resumedTickFrame,
                    source.Header.TickFrame) ||
                (HasCurrent &&
                 resumedTickFrame.TimelineEpoch.Value <=
                 _current.Header.Identity.TimelineEpoch.Value) ||
                (HasCurrent &&
                 !StateFlowTickOrder.IsStrictlyAfter(
                     resumedTickFrame,
                     _current.Header.TickFrame)))
            {
                status = ContextCommitStatus.InvalidOrigin;
                return false;
            }

            status = ContextCommitStatus.None;
            return true;
        }

        internal bool CanWrite(ulong token, StateBlockHandle block)
        {
            return IsPreparationActive(token) &&
                   block.IsValid && block.IsFor(Layout);
        }

        internal bool TryWrite<TValue>(
            ulong token,
            StateBlockHandle block,
            StateSlot<TValue> slot,
            in TValue value)
            where TValue : unmanaged
        {
            if (!IsPreparationActive(token) ||
                !slot.IsValid || !slot.IsFor(Layout) ||
                !Layout.IsWriterFor(block, slot.DenseIndex))
            {
                return false;
            }

            StateFlowTypeRules.Write(_reserved.Buffer, slot.ByteOffset, value);
            return true;
        }

        internal bool TryPrepareProjectionRestore(
            ContextProjectionCodec codec,
            ProjectionRestoreSource source,
            ReadOnlySpan<byte> encoded,
            TickFrame resumedTickFrame,
            out FinalizedContextCommit finalized,
            out ContextCommitStatus status,
            out CoCoDiagnosticCode diagnosticCode) =>
            TryPrepareProjectionRestoreCore(
                codec,
                source,
                encoded,
                resumedTickFrame,
                requireSameRuntimeIdentity: true,
                out finalized,
                out status,
                out diagnosticCode);

        internal bool TryPrepareProjectionImport(
            ContextProjectionCodec codec,
            ProjectionRestoreSource source,
            ReadOnlySpan<byte> encoded,
            TickFrame importedTickFrame,
            out FinalizedContextCommit finalized,
            out ContextCommitStatus status,
            out CoCoDiagnosticCode diagnosticCode) =>
            TryPrepareProjectionRestoreCore(
                codec,
                source,
                encoded,
                importedTickFrame,
                requireSameRuntimeIdentity: false,
                out finalized,
                out status,
                out diagnosticCode);

        private bool TryPrepareProjectionRestoreCore(
            ContextProjectionCodec codec,
            ProjectionRestoreSource source,
            ReadOnlySpan<byte> encoded,
            TickFrame resumedTickFrame,
            bool requireSameRuntimeIdentity,
            out FinalizedContextCommit finalized,
            out ContextCommitStatus status,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_isDisposed)
            {
                finalized = default;
                status = ContextCommitStatus.InvalidPreparation;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            if (_isCallbackActive)
            {
                finalized = default;
                status = ContextCommitStatus.PreparationAlreadyActive;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (codec == null || !Layout.IsSameInstance(codec.Layout))
            {
                finalized = default;
                status = ContextCommitStatus.LayoutMismatch;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (!source.IsValid ||
                (requireSameRuntimeIdentity &&
                 source.GraphInstanceId != _graphInstanceId))
            {
                finalized = default;
                status = ContextCommitStatus.GraphMismatch;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            if (!resumedTickFrame.IsValid)
            {
                finalized = default;
                status = ContextCommitStatus.InvalidTick;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (resumedTickFrame.TimelineEpoch.Value <= source.TimelineEpoch.Value ||
                (requireSameRuntimeIdentity &&
                 resumedTickFrame.TimelineId != source.TimelineId) ||
                (requireSameRuntimeIdentity &&
                 resumedTickFrame.ClockDomainId != source.ClockDomainId) ||
                resumedTickFrame.ExecutionSequence.Value <= source.ExecutionSequence.Value ||
                (HasCurrent &&
                 resumedTickFrame.TimelineEpoch.Value <=
                 _current.Header.Identity.TimelineEpoch.Value) ||
                (HasCurrent &&
                 !StateFlowTickOrder.IsStrictlyAfter(
                     resumedTickFrame,
                     _current.Header.TickFrame)))
            {
                finalized = default;
                status = ContextCommitStatus.InvalidOrigin;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            ContextFrameOrigin origin = ContextFrameOrigin.RestoreFrom(
                source.GraphInstanceId,
                source.TimelineEpoch,
                source.Tick,
                source.Revision);
            if (!TryPrepareCore(
                    resumedTickFrame,
                    origin,
                    default,
                    out _,
                    out status))
            {
                finalized = default;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            ulong token = _activeToken;
            bool didDecode;
            _isCallbackActive = true;
            try
            {
                didDecode = codec.TryDecodePayload(encoded, _reserved.Buffer, out diagnosticCode,
                    RetainedRestoreSlots, _current.Buffer);
            }
            catch
            {
                _isCallbackActive = false;
                if (_disposeRequested)
                {
                    DisposeCore();
                }
                else
                {
                    CancelActive(token);
                }

                throw;
            }

            _isCallbackActive = false;
            if (_disposeRequested)
            {
                DisposeCore();
                finalized = default;
                status = ContextCommitStatus.InvalidPreparation;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            if (!didDecode)
            {
                CancelActive(token);
                finalized = default;
                status = diagnosticCode == CoCoDiagnosticCode.CommitPreparationFailed
                    ? ContextCommitStatus.DerivedRebuildFailed
                    : ContextCommitStatus.RestoreFailed;
                return false;
            }

            _isFinalized = true;
            finalized = new FinalizedContextCommit(this, token);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool IsPreparationActive(ulong token) =>
            !_isDisposed && !_isCallbackActive && _reserved != null && !_isFinalized &&
            token != 0UL && token == _activeToken;

        internal bool IsFinalized(ulong token) =>
            !_isDisposed && !_isCallbackActive && _reserved != null && _isFinalized &&
            token != 0UL && token == _activeToken;

        internal bool TryGetFinalizedMetadata(
            ulong token,
            out StateFlowFrameHeader header,
            out ContextRevision revision,
            out ContextFrameOrigin origin)
        {
            if (!IsFinalized(token))
            {
                header = default;
                revision = default;
                origin = default;
                return false;
            }

            header = _reserved.Header;
            revision = _reserved.Revision;
            origin = _reserved.Origin;
            return true;
        }

        internal bool TryEncodeFinalized(
            ulong token,
            ContextProjectionCodec codec,
            Span<byte> destination,
            out int bytesWritten,
            out CoCoDiagnosticCode diagnosticCode)
        {
            bytesWritten = 0;
            if (!IsFinalized(token) ||
                codec == null ||
                !Layout.IsSameInstance(codec.Layout))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            bool didEncode;
            _isCallbackActive = true;
            try
            {
                didEncode = codec.TryEncodeFinalized(
                    _reserved.Header,
                    _reserved.Revision,
                    _reserved.Buffer,
                    destination,
                    out bytesWritten,
                    out diagnosticCode);
            }
            catch
            {
                _isCallbackActive = false;
                if (_disposeRequested)
                {
                    DisposeCore();
                }
                else
                {
                    CancelActive(token);
                }

                throw;
            }

            _isCallbackActive = false;
            if (_disposeRequested)
            {
                DisposeCore();
                bytesWritten = 0;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            return didEncode;
        }

        internal bool TryFinalize(
            ulong token,
            out FinalizedContextCommit finalized,
            out ContextCommitStatus status)
        {
            if (_isCallbackActive || !IsPreparationActive(token))
            {
                finalized = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            bool didRebuild;
            _isCallbackActive = true;
            try
            {
                didRebuild = Layout.TryRebuildDerived(_reserved.Buffer);
            }
            catch
            {
                _isCallbackActive = false;
                if (_disposeRequested)
                {
                    DisposeCore();
                }
                else
                {
                    CancelActive(token);
                }

                throw;
            }

            _isCallbackActive = false;
            if (_disposeRequested)
            {
                DisposeCore();
                finalized = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (!didRebuild)
            {
                CancelActive(token);
                finalized = default;
                status = ContextCommitStatus.DerivedRebuildFailed;
                return false;
            }

            _isFinalized = true;
            finalized = new FinalizedContextCommit(this, token);
            status = ContextCommitStatus.None;
            return true;
        }

        internal ContextCommitResult Commit(ulong token)
        {
            if (!IsCommitReady(token))
            {
                return new ContextCommitResult(ContextCommitStatus.InvalidPreparation, default);
            }

            return new ContextCommitResult(
                ContextCommitStatus.Succeeded,
                CommitNoFailUnchecked());
        }

        internal bool IsCommitReady(ulong token) =>
            !_isCallbackActive && IsFinalized(token);

        internal ContextFrame CommitNoFailUnchecked()
        {
            ContextFrameStorage committedStorage = _reserved;
            ContextFrameStorage previousStorage = _currentStorage;
            _reserved = null;
            _activeToken = 0UL;
            _isFinalized = false;
            ContextFrame committed = committedStorage.Seal();
            _currentStorage = committedStorage;
            _current = committed;
            previousStorage.ReleaseArenaOwnership();
            return _current;
        }

        private void DisposeCore()
        {
            if (_reserved != null)
            {
                _reserved.Abandon();
                _reserved = null;
            }

            _currentStorage?.ReleaseArenaOwnership();
            _currentStorage = null;
            _current = default;
            _activeToken = 0UL;
            _isFinalized = false;
            _disposeRequested = false;
            _isDisposed = true;
        }

        internal ContextCommitStatus CancelPrepared(ulong token)
        {
            return _isCallbackActive || !IsPreparationActive(token)
                ? ContextCommitStatus.InvalidPreparation
                : CancelActive(token);
        }

        internal ContextCommitStatus CancelFinalized(ulong token)
        {
            return _isCallbackActive || !IsFinalized(token)
                ? ContextCommitStatus.InvalidPreparation
                : CancelActive(token);
        }

        private ContextCommitStatus CancelActive(ulong token)
        {
            if (_reserved == null || token == 0UL || token != _activeToken)
            {
                return ContextCommitStatus.InvalidPreparation;
            }

            _reserved.Abandon();
            _reserved = null;
            _activeToken = 0UL;
            _isFinalized = false;
            return ContextCommitStatus.Cancelled;
        }

        private bool TryPrepareCore(
            TickFrame tickFrame,
            ContextFrameOrigin origin,
            ContextFrame source,
            out PreparedContextCommit prepared,
            out ContextCommitStatus status)
        {
            if (_isDisposed)
            {
                prepared = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (!tickFrame.IsValid)
            {
                prepared = default;
                status = ContextCommitStatus.InvalidTick;
                return false;
            }

            if (!origin.IsValid)
            {
                prepared = default;
                status = ContextCommitStatus.InvalidOrigin;
                return false;
            }

            if (HasCurrent &&
                !StateFlowTickOrder.IsStrictlyAfter(
                    tickFrame,
                    _current.Header.TickFrame))
            {
                prepared = default;
                status = ContextCommitStatus.InvalidTick;
                return false;
            }

            if (_reserved != null)
            {
                prepared = default;
                status = ContextCommitStatus.PreparationAlreadyActive;
                return false;
            }

            if (_nextToken == ulong.MaxValue)
            {
                prepared = default;
                status = ContextCommitStatus.RevisionExhausted;
                return false;
            }

            if (HasCurrent && _current.Revision.Value == ulong.MaxValue)
            {
                prepared = default;
                status = ContextCommitStatus.RevisionExhausted;
                return false;
            }

            ContextFrameStorage candidate = FindAvailableFrame();
            if (candidate == null)
            {
                prepared = default;
                status = ContextCommitStatus.CapacityExhausted;
                return false;
            }

            if (!StateFlowFrameHeader.TryCreate(
                    _graphInstanceId,
                    Layout,
                    StateFlowFrameKind.Context,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                prepared = default;
                status = ContextCommitStatus.InvalidTick;
                return false;
            }

            ulong revisionValue = !HasCurrent ? 1UL : _current.Revision.Value + 1UL;
            bool didPrepare;
            _isCallbackActive = true;
            try
            {
                didPrepare = candidate.TryPrepare(
                    header,
                    new ContextRevision(revisionValue),
                    origin,
                    source,
                    out status);
            }
            catch
            {
                _isCallbackActive = false;
                candidate.Abandon();
                if (_disposeRequested)
                {
                    DisposeCore();
                }

                throw;
            }

            _isCallbackActive = false;

            if (_disposeRequested)
            {
                candidate.Abandon();
                DisposeCore();
                prepared = default;
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            if (!didPrepare)
            {
                candidate.Abandon();
                prepared = default;
                return false;
            }

            _reserved = candidate;
            _isFinalized = false;
            _nextToken++;
            _activeToken = _nextToken;
            prepared = new PreparedContextCommit(this, _activeToken);
            status = ContextCommitStatus.None;
            return true;
        }

        private ContextFrameStorage FindAvailableFrame()
        {
            for (int index = 0; index < _frames.Length; index++)
            {
                if (!ReferenceEquals(_frames[index], _currentStorage) && _frames[index].CanReuse)
                {
                    return _frames[index];
                }
            }

            return null;
        }

        #endregion
    }

    /// <summary>对比两帧的实例、时间线和执行顺序，供提交时验证。</summary>
    internal static class StateFlowTickOrder
    {
        public static bool IsStrictlyAfter(
            in TickFrame candidate,
            in TickFrame previous)
        {
            if (!candidate.IsValid || !previous.IsValid ||
                candidate.TimelineId != previous.TimelineId ||
                candidate.ClockDomainId != previous.ClockDomainId ||
                candidate.ExecutionSequence.Value <= previous.ExecutionSequence.Value)
            {
                return false;
            }

            if (candidate.TimelineEpoch.Value != previous.TimelineEpoch.Value)
            {
                return candidate.TimelineEpoch.Value > previous.TimelineEpoch.Value;
            }

            return candidate.Tick.Value > previous.Tick.Value &&
                   candidate.TimelinePosition.Seconds > previous.TimelinePosition.Seconds;
        }
    }

    /// <summary>读写投影格式中的定宽整数，供保存与恢复共用。</summary>
    internal static class StateFlowBinary
    {
        public static void WriteUInt32(Span<byte> destination, ref int offset, uint value)
        {
            destination[offset++] = (byte)value;
            destination[offset++] = (byte)(value >> 8);
            destination[offset++] = (byte)(value >> 16);
            destination[offset++] = (byte)(value >> 24);
        }

        public static void WriteUInt64(Span<byte> destination, ref int offset, ulong value)
        {
            WriteUInt32(destination, ref offset, (uint)value);
            WriteUInt32(destination, ref offset, (uint)(value >> 32));
        }

        public static bool TryReadUInt32(ReadOnlySpan<byte> source, ref int offset, out uint value)
        {
            if (offset < 0 || source.Length - offset < sizeof(uint))
            {
                value = 0U;
                return false;
            }

            value = source[offset] |
                    ((uint)source[offset + 1] << 8) |
                    ((uint)source[offset + 2] << 16) |
                    ((uint)source[offset + 3] << 24);
            offset += sizeof(uint);
            return true;
        }

        public static bool TryReadUInt64(ReadOnlySpan<byte> source, ref int offset, out ulong value)
        {
            if (!TryReadUInt32(source, ref offset, out uint low) ||
                !TryReadUInt32(source, ref offset, out uint high))
            {
                value = 0UL;
                return false;
            }

            value = low | ((ulong)high << 32);
            return true;
        }
    }

    /// <summary>计算稳定的布局与类型指纹，使保存和恢复按相同合同核验。</summary>
    internal static class StateFlowSchemaHash
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(
            StateBlockDescriptor[] blocks,
            StateSlotDescriptor[] slots)
        {
            ulong hash = OffsetBasis;
            AddUInt32(ref hash, (uint)blocks.Length);
            for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                StateBlockDescriptor block = blocks[blockIndex];
                AddUInt64(ref hash, block.BlockId.High);
                AddUInt64(ref hash, block.BlockId.Low);
                AddUInt32(ref hash, (uint)block.Owner);
                AddUInt32(ref hash, (uint)block.DenseIndex);
            }

            AddUInt32(ref hash, (uint)slots.Length);
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                StateSlotDescriptor slot = slots[slotIndex];
                AddUInt64(ref hash, slot.SlotId.High);
                AddUInt64(ref hash, slot.SlotId.Low);
                AddUInt64(ref hash, slot.WriterBlockId.High);
                AddUInt64(ref hash, slot.WriterBlockId.Low);
                AddUInt64(ref hash, HashType(slot.ValueType));
                AddUInt32(ref hash, (uint)slot.DenseIndex);
                AddUInt32(ref hash, (uint)slot.ByteOffset);
                AddUInt32(ref hash, (uint)slot.ByteSize);
                AddUInt32(ref hash, (uint)slot.Projection);
                AddUInt32(ref hash, (uint)slot.RestorePolicy);
                AddUInt64(ref hash, slot.Codec.CodecId.High);
                AddUInt64(ref hash, slot.Codec.CodecId.Low);
                AddUInt32(ref hash, slot.Codec.Version);
                AddUInt32(ref hash, (uint)slot.DerivedDependencyArray.Length);
                for (int dependencyIndex = 0;
                     dependencyIndex < slot.DerivedDependencyArray.Length;
                     dependencyIndex++)
                {
                    StateSlotId dependency = slot.DerivedDependencyArray[dependencyIndex];
                    AddUInt64(ref hash, dependency.High);
                    AddUInt64(ref hash, dependency.Low);
                }

                AddUInt32(ref hash, (uint)slot.DefaultBytes.Length);
                for (int byteIndex = 0; byteIndex < slot.DefaultBytes.Length; byteIndex++)
                {
                    AddByte(ref hash, slot.DefaultBytes[byteIndex]);
                }
            }

            return hash == 0UL ? OffsetBasis : hash;
        }

        public static ulong HashType(Type valueType)
        {
            ulong hash = OffsetBasis;
            string typeName = valueType == null ? string.Empty : GraphTypeIdentity.FullName(valueType);
            string assemblyName = valueType == null ? string.Empty : GraphTypeIdentity.AssemblyName(valueType);
            AddString(ref hash, assemblyName);
            AddByte(ref hash, 0xFF);
            AddString(ref hash, typeName);
            if (valueType == null)
            {
                return hash == 0UL ? OffsetBasis : hash;
            }

            AddUInt32(ref hash, (uint)valueType.Attributes);
            StructLayoutAttribute layout = valueType.StructLayoutAttribute;
            if (layout != null)
            {
                AddUInt32(ref hash, (uint)layout.Value);
                AddUInt32(ref hash, (uint)layout.Pack);
                AddUInt32(ref hash, (uint)layout.Size);
                AddUInt32(ref hash, (uint)layout.CharSet);
            }

            if (valueType.IsEnum)
            {
                AddUInt64(ref hash, HashType(Enum.GetUnderlyingType(valueType)));
            }
            else if (!valueType.IsPrimitive)
            {
                FieldInfo[] fields = valueType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Array.Sort(fields, FieldMetadataOrder.Instance);
                AddUInt32(ref hash, (uint)fields.Length);
                for (int index = 0; index < fields.Length; index++)
                {
                    FieldInfo field = fields[index];
                    AddString(ref hash, field.Name);
                    AddUInt64(ref hash, HashType(field.FieldType));
                    FieldOffsetAttribute explicitOffset =
                        field.GetCustomAttribute<FieldOffsetAttribute>();
                    AddUInt32(
                        ref hash,
                        explicitOffset == null ? uint.MaxValue : (uint)explicitOffset.Value);
                }
            }

            return hash == 0UL ? OffsetBasis : hash;
        }

        /// <summary>按字段元数据次序遍历布局，供类型指纹采用一致顺序。</summary>
        private sealed class FieldMetadataOrder : IComparer<FieldInfo>
        {
            public static readonly FieldMetadataOrder Instance = new FieldMetadataOrder();

            public int Compare(FieldInfo left, FieldInfo right) =>
                left.MetadataToken.CompareTo(right.MetadataToken);
        }

        private static void AddString(ref ulong hash, string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                AddByte(ref hash, (byte)character);
                AddByte(ref hash, (byte)(character >> 8));
            }
        }

        private static void AddUInt32(ref ulong hash, uint value)
        {
            AddByte(ref hash, (byte)value);
            AddByte(ref hash, (byte)(value >> 8));
            AddByte(ref hash, (byte)(value >> 16));
            AddByte(ref hash, (byte)(value >> 24));
        }

        private static void AddUInt64(ref ulong hash, ulong value)
        {
            AddUInt32(ref hash, (uint)value);
            AddUInt32(ref hash, (uint)(value >> 32));
        }

        private static void AddByte(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= Prime;
        }
    }

    /// <summary>判断帧值类型是否符合非托管或布局要求。</summary>
    internal static class StateFlowTypeRules
    {
        public static bool IsReferenceFreeValueType(Type valueType)
        {
            if (valueType == null ||
                valueType == typeof(string) ||
                valueType.IsByRef ||
                valueType.IsPointer ||
                valueType.IsByRefLike ||
                valueType == typeof(IntPtr) ||
                valueType == typeof(UIntPtr))
            {
                return false;
            }

            if (valueType.IsPrimitive || valueType.IsEnum)
            {
                return true;
            }

            if (!valueType.IsValueType)
            {
                return false;
            }

            FieldInfo[] fields = valueType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                if (!IsReferenceFreeValueType(fields[index].FieldType))
                {
                    return false;
                }
            }

            return true;
        }

        public static T Read<T>(byte[] buffer, int offset)
            where T : unmanaged
        {
            T value = default;
            Span<byte> destination = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateSpan(ref value, 1));
            new ReadOnlySpan<byte>(buffer, offset, destination.Length).CopyTo(destination);
            return value;
        }

        public static void Write<T>(byte[] buffer, int offset, in T value)
            where T : unmanaged
        {
            T copy = value;
            ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(ref copy, 1));
            source.CopyTo(new Span<byte>(buffer, offset, source.Length));
        }

        public static int SizeOf<T>()
            where T : unmanaged
        {
            T value = default;
            return MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).Length;
        }
    }
}
