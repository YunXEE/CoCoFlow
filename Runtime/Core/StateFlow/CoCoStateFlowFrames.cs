using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("CoCoFlow.Tests.Editor.CoreContracts")]

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoStateFlowFrameKind
    {
        None = 0,
        Intent = 1,
        Operation = 2,
        Context = 3
    }

    public readonly struct CoCoStateFlowFrameIdentity : IEquatable<CoCoStateFlowFrameIdentity>
    {
        public CoCoStateFlowFrameIdentity(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoExecutionSequence executionSequence,
            CoCoStateFlowFrameKind kind)
        {
            GraphInstanceId = graphInstanceId;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            ExecutionSequence = executionSequence;
            Kind = kind;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTimelineEpoch TimelineEpoch { get; }
        public CoCoTimelineTick Tick { get; }
        public CoCoExecutionSequence ExecutionSequence { get; }
        public CoCoStateFlowFrameKind Kind { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               Kind >= CoCoStateFlowFrameKind.Intent &&
                               Kind <= CoCoStateFlowFrameKind.Context;

        public bool Equals(CoCoStateFlowFrameIdentity other)
        {
            return GraphInstanceId == other.GraphInstanceId &&
                   TimelineEpoch == other.TimelineEpoch &&
                   Tick == other.Tick &&
                   ExecutionSequence == other.ExecutionSequence &&
                   Kind == other.Kind;
        }

        public override bool Equals(object obj) => obj is CoCoStateFlowFrameIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = GraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineEpoch.GetHashCode();
                hashCode = (hashCode * 397) ^ Tick.GetHashCode();
                hashCode = (hashCode * 397) ^ ExecutionSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Kind;
                return hashCode;
            }
        }

        public static bool operator ==(CoCoStateFlowFrameIdentity left, CoCoStateFlowFrameIdentity right) =>
            left.Equals(right);

        public static bool operator !=(CoCoStateFlowFrameIdentity left, CoCoStateFlowFrameIdentity right) =>
            !left.Equals(right);
    }

    public readonly struct CoCoStateFlowFrameHeader : IEquatable<CoCoStateFlowFrameHeader>
    {
        private CoCoStateFlowFrameHeader(
            CoCoStateFlowFrameIdentity identity,
            CoCoFrameLayoutId layoutId,
            uint layoutVersion,
            ulong layoutSchemaHash,
            CoCoTickFrame tickFrame)
        {
            Identity = identity;
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            LayoutSchemaHash = layoutSchemaHash;
            TickFrame = tickFrame;
        }

        public CoCoStateFlowFrameIdentity Identity { get; }
        public CoCoFrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public CoCoTickFrame TickFrame { get; }
        public bool IsValid => Identity.IsValid && LayoutId.IsValid && TickFrame.IsValid;
        public bool HasExactLayoutIdentity => LayoutVersion > 0U && LayoutSchemaHash != 0UL;

        public static bool TryCreate(
            CoCoGraphInstanceId graphInstanceId,
            CoCoFrameLayoutId layoutId,
            CoCoStateFlowFrameKind kind,
            CoCoTickFrame tickFrame,
            out CoCoStateFlowFrameHeader header)
        {
            var identity = new CoCoStateFlowFrameIdentity(
                graphInstanceId,
                tickFrame.TimelineEpoch,
                tickFrame.Tick,
                tickFrame.ExecutionSequence,
                kind);
            if (!identity.IsValid || !layoutId.IsValid || !tickFrame.IsValid)
            {
                header = default;
                return false;
            }

            header = new CoCoStateFlowFrameHeader(identity, layoutId, 0U, 0UL, tickFrame);
            return true;
        }

        public static bool TryCreate(
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout layout,
            CoCoStateFlowFrameKind kind,
            CoCoTickFrame tickFrame,
            out CoCoStateFlowFrameHeader header)
        {
            if (layout == null ||
                !TryCreate(graphInstanceId, layout.LayoutId, kind, tickFrame, out CoCoStateFlowFrameHeader basic))
            {
                header = default;
                return false;
            }

            header = new CoCoStateFlowFrameHeader(
                basic.Identity,
                layout.LayoutId,
                layout.Version,
                layout.SchemaHash,
                tickFrame);
            return true;
        }

        public bool Equals(CoCoStateFlowFrameHeader other)
        {
            return Identity == other.Identity &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   TickFrame == other.TickFrame;
        }

        public override bool Equals(object obj) => obj is CoCoStateFlowFrameHeader other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Identity.GetHashCode();
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)LayoutVersion;
                hashCode = (hashCode * 397) ^ LayoutSchemaHash.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(CoCoStateFlowFrameHeader left, CoCoStateFlowFrameHeader right) =>
            left.Equals(right);

        public static bool operator !=(CoCoStateFlowFrameHeader left, CoCoStateFlowFrameHeader right) =>
            !left.Equals(right);
    }

    public readonly struct CoCoContextRevision : IEquatable<CoCoContextRevision>
    {
        public CoCoContextRevision(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public bool Equals(CoCoContextRevision other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoContextRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(CoCoContextRevision left, CoCoContextRevision right) => left.Equals(right);
        public static bool operator !=(CoCoContextRevision left, CoCoContextRevision right) => !left.Equals(right);
    }

    public enum CoCoStateBlockOwner
    {
        None = 0,
        Graph = 1,
        Operator = 2,
        Actor = 3
    }

    public readonly struct CoCoStateBlockHandle : IEquatable<CoCoStateBlockHandle>
    {
        private readonly CoCoContextFrameLayout _layout;

        internal CoCoStateBlockHandle(
            CoCoContextFrameLayout layout,
            CoCoStateBlockId blockId,
            int denseIndex,
            CoCoStateBlockOwner owner)
        {
            _layout = layout;
            LayoutId = layout.LayoutId;
            LayoutVersion = layout.Version;
            LayoutSchemaHash = layout.SchemaHash;
            BlockId = blockId;
            DenseIndex = denseIndex;
            Owner = owner;
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public CoCoStateBlockId BlockId { get; }
        public int DenseIndex { get; }
        public CoCoStateBlockOwner Owner { get; }
        public bool IsValid => _layout != null && LayoutId.IsValid && LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL && BlockId.IsValid && DenseIndex >= 0 &&
                               Owner >= CoCoStateBlockOwner.Graph && Owner <= CoCoStateBlockOwner.Actor;

        internal bool IsFor(CoCoContextFrameLayout layout) =>
            ReferenceEquals(_layout, layout) &&
            layout != null &&
            LayoutId == layout.LayoutId &&
            LayoutVersion == layout.Version &&
            LayoutSchemaHash == layout.SchemaHash;

        public bool Equals(CoCoStateBlockHandle other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   BlockId == other.BlockId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) => obj is CoCoStateBlockHandle other && Equals(other);
        public override int GetHashCode() => unchecked((LayoutId.GetHashCode() * 397) ^ DenseIndex);

        public static bool operator ==(CoCoStateBlockHandle left, CoCoStateBlockHandle right) =>
            left.Equals(right);

        public static bool operator !=(CoCoStateBlockHandle left, CoCoStateBlockHandle right) =>
            !left.Equals(right);
    }

    [Flags]
    public enum CoCoContextProjection
    {
        None = 0,
        Temporal = 1 << 0,
        Durable = 1 << 1
    }

    public enum CoCoContextRestorePolicy
    {
        None = 0,
        Stored = 1,
        ResetToDefault = 2,
        Derived = 3
    }

    public enum CoCoContextFrameOriginKind
    {
        None = 0,
        Commit = 1,
        Restore = 2
    }

    public readonly struct CoCoContextFrameOrigin : IEquatable<CoCoContextFrameOrigin>
    {
        private CoCoContextFrameOrigin(
            CoCoContextFrameOriginKind kind,
            CoCoGraphInstanceId sourceGraphInstanceId,
            CoCoTimelineEpoch sourceTimelineEpoch,
            CoCoTimelineTick sourceTick,
            CoCoContextRevision sourceRevision)
        {
            Kind = kind;
            SourceGraphInstanceId = sourceGraphInstanceId;
            SourceTimelineEpoch = sourceTimelineEpoch;
            SourceTick = sourceTick;
            SourceRevision = sourceRevision;
        }

        public CoCoContextFrameOriginKind Kind { get; }
        public CoCoGraphInstanceId SourceGraphInstanceId { get; }
        public CoCoTimelineEpoch SourceTimelineEpoch { get; }
        public CoCoTimelineTick SourceTick { get; }
        public CoCoContextRevision SourceRevision { get; }
        public bool IsRestore => Kind == CoCoContextFrameOriginKind.Restore;
        public bool IsValid => Kind == CoCoContextFrameOriginKind.Commit ||
                               (Kind == CoCoContextFrameOriginKind.Restore &&
                                SourceGraphInstanceId.IsValid &&
                                SourceRevision.IsValid);

        public static CoCoContextFrameOrigin Commit() =>
            new CoCoContextFrameOrigin(CoCoContextFrameOriginKind.Commit, default, default, default, default);

        public static CoCoContextFrameOrigin RestoreFrom(CoCoContextFrame source)
        {
            if (!source.IsAlive)
            {
                return default;
            }

            return new CoCoContextFrameOrigin(
                CoCoContextFrameOriginKind.Restore,
                source.Header.Identity.GraphInstanceId,
                source.Header.Identity.TimelineEpoch,
                source.Header.Identity.Tick,
                source.Revision);
        }

        internal static CoCoContextFrameOrigin RestoreFrom(
            CoCoGraphInstanceId sourceGraphInstanceId,
            CoCoTimelineEpoch sourceTimelineEpoch,
            CoCoTimelineTick sourceTick,
            CoCoContextRevision sourceRevision)
        {
            var origin = new CoCoContextFrameOrigin(
                CoCoContextFrameOriginKind.Restore,
                sourceGraphInstanceId,
                sourceTimelineEpoch,
                sourceTick,
                sourceRevision);
            return origin.IsValid ? origin : default;
        }

        public bool Equals(CoCoContextFrameOrigin other)
        {
            return Kind == other.Kind &&
                   SourceGraphInstanceId == other.SourceGraphInstanceId &&
                   SourceTimelineEpoch == other.SourceTimelineEpoch &&
                   SourceTick == other.SourceTick &&
                   SourceRevision == other.SourceRevision;
        }

        public override bool Equals(object obj) => obj is CoCoContextFrameOrigin other && Equals(other);
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ SourceRevision.GetHashCode());
    }

    public readonly struct CoCoCodecDescriptor : IEquatable<CoCoCodecDescriptor>
    {
        public CoCoCodecDescriptor(CoCoCodecId codecId, uint version)
        {
            CodecId = codecId;
            Version = version;
        }

        public CoCoCodecId CodecId { get; }
        public uint Version { get; }
        public bool UsesCustomCodec => CodecId.IsValid;
        public bool IsValid => UsesCustomCodec ? Version > 0U : Version == 0U;

        public bool Equals(CoCoCodecDescriptor other) => CodecId == other.CodecId && Version == other.Version;
        public override bool Equals(object obj) => obj is CoCoCodecDescriptor other && Equals(other);
        public override int GetHashCode() => unchecked((CodecId.GetHashCode() * 397) ^ (int)Version);
    }

    public interface ICoCoContextValueCodec<TValue>
        where TValue : unmanaged
    {
        CoCoCodecDescriptor Descriptor { get; }
        int MaxEncodedSize { get; }
        bool TryEncode(in TValue value, Span<byte> destination, out int bytesWritten);
        bool TryDecode(ReadOnlySpan<byte> source, out TValue value, out int bytesRead);
    }

    internal interface ICoCoContextValueCodecAdapter
    {
        CoCoCodecDescriptor Descriptor { get; }
        Type ValueType { get; }
        ulong ValueTypeHash { get; }
        int MaxEncodedSize { get; }
        object UntypedCodec { get; }
        bool TryEncode(byte[] source, int sourceOffset, Span<byte> destination, out int bytesWritten);
        bool TryDecode(ReadOnlySpan<byte> source, byte[] destination, int destinationOffset, out int bytesRead);
    }

    internal sealed class CoCoContextValueCodecAdapter<TValue> : ICoCoContextValueCodecAdapter
        where TValue : unmanaged
    {
        private readonly ICoCoContextValueCodec<TValue> _codec;

        public CoCoContextValueCodecAdapter(ICoCoContextValueCodec<TValue> codec)
        {
            _codec = codec;
        }

        public CoCoCodecDescriptor Descriptor => _codec.Descriptor;
        public Type ValueType => typeof(TValue);
        public ulong ValueTypeHash => CoCoStateFlowSchemaHash.HashType(typeof(TValue));
        public int MaxEncodedSize => _codec.MaxEncodedSize;
        public object UntypedCodec => _codec;

        public bool TryEncode(
            byte[] source,
            int sourceOffset,
            Span<byte> destination,
            out int bytesWritten)
        {
            TValue value = CoCoStateFlowTypeRules.Read<TValue>(source, sourceOffset);
            return _codec.TryEncode(in value, destination, out bytesWritten);
        }

        public bool TryDecode(
            ReadOnlySpan<byte> source,
            byte[] destination,
            int destinationOffset,
            out int bytesRead)
        {
            if (!_codec.TryDecode(source, out TValue value, out bytesRead))
            {
                return false;
            }

            CoCoStateFlowTypeRules.Write(destination, destinationOffset, value);
            return true;
        }
    }

    public sealed class CoCoContextCodecRegistry
    {
        private readonly Dictionary<CodecBindingKey, ICoCoContextValueCodecAdapter> _bindings =
            new Dictionary<CodecBindingKey, ICoCoContextValueCodecAdapter>();
        private readonly HashSet<CoCoCodecId> _codecIds = new HashSet<CoCoCodecId>();
        private readonly HashSet<CodecVersionKey> _codecVersions = new HashSet<CodecVersionKey>();
        private bool _frozen;

        public bool IsFrozen => _frozen;
        public int Count => _bindings.Count;

        public bool TryRegister<TValue>(
            ICoCoContextValueCodec<TValue> codec,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            if (_frozen)
            {
                diagnosticCode = CoCoDiagnosticCode.RegistryFrozen;
                return false;
            }

            if (codec == null || !codec.Descriptor.IsValid || !codec.Descriptor.UsesCustomCodec ||
                codec.MaxEncodedSize <= 0 ||
                !CoCoStateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)))
            {
                diagnosticCode = CoCoDiagnosticCode.UnknownCodec;
                return false;
            }

            var key = new CodecBindingKey(codec.Descriptor, typeof(TValue));
            if (_bindings.ContainsKey(key))
            {
                diagnosticCode = CoCoDiagnosticCode.DuplicateIdentifier;
                return false;
            }

            _bindings.Add(key, new CoCoContextValueCodecAdapter<TValue>(codec));
            _codecIds.Add(codec.Descriptor.CodecId);
            _codecVersions.Add(new CodecVersionKey(codec.Descriptor));
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryFreeze(out CoCoDiagnosticCode diagnosticCode)
        {
            if (_frozen)
            {
                diagnosticCode = CoCoDiagnosticCode.RegistryFrozen;
                return false;
            }

            _frozen = true;
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryResolve<TValue>(
            CoCoCodecDescriptor descriptor,
            out ICoCoContextValueCodec<TValue> codec,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            if (!TryResolve(descriptor, typeof(TValue), out ICoCoContextValueCodecAdapter adapter, out diagnosticCode))
            {
                codec = null;
                return false;
            }

            codec = (ICoCoContextValueCodec<TValue>)adapter.UntypedCodec;
            return true;
        }

        internal bool TryResolve(
            CoCoCodecDescriptor descriptor,
            Type valueType,
            out ICoCoContextValueCodecAdapter adapter,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (!_frozen)
            {
                adapter = null;
                diagnosticCode = CoCoDiagnosticCode.RegistryNotFrozen;
                return false;
            }

            if (!descriptor.CodecId.IsValid)
            {
                adapter = null;
                diagnosticCode = CoCoDiagnosticCode.UnknownCodec;
                return false;
            }

            if (!_codecIds.Contains(descriptor.CodecId))
            {
                adapter = null;
                diagnosticCode = CoCoDiagnosticCode.UnknownCodec;
                return false;
            }

            if (descriptor.Version == 0U ||
                !_codecVersions.Contains(new CodecVersionKey(descriptor)))
            {
                adapter = null;
                diagnosticCode = CoCoDiagnosticCode.UnsupportedCodecVersion;
                return false;
            }

            if (!_bindings.TryGetValue(new CodecBindingKey(descriptor, valueType), out adapter))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
                return false;
            }

            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal CoCoDiagnosticCode Classify(
            CoCoCodecDescriptor descriptor,
            Type valueType)
        {
            return TryResolve(descriptor, valueType, out _, out CoCoDiagnosticCode diagnosticCode)
                ? CoCoDiagnosticCode.None
                : diagnosticCode;
        }

        private readonly struct CodecVersionKey : IEquatable<CodecVersionKey>
        {
            public CodecVersionKey(CoCoCodecDescriptor descriptor)
            {
                CodecId = descriptor.CodecId;
                Version = descriptor.Version;
            }

            public CoCoCodecId CodecId { get; }
            public uint Version { get; }

            public bool Equals(CodecVersionKey other) =>
                CodecId == other.CodecId && Version == other.Version;

            public override bool Equals(object obj) => obj is CodecVersionKey other && Equals(other);
            public override int GetHashCode() => unchecked((CodecId.GetHashCode() * 397) ^ (int)Version);
        }

        private readonly struct CodecBindingKey : IEquatable<CodecBindingKey>
        {
            public CodecBindingKey(CoCoCodecDescriptor descriptor, Type valueType)
            {
                Descriptor = descriptor;
                ValueType = valueType;
            }

            public CoCoCodecDescriptor Descriptor { get; }
            public Type ValueType { get; }

            public bool Equals(CodecBindingKey other) =>
                Descriptor.Equals(other.Descriptor) && ValueType == other.ValueType;

            public override bool Equals(object obj) => obj is CodecBindingKey other && Equals(other);

            public override int GetHashCode() =>
                unchecked((Descriptor.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));
        }
    }

    public interface ICoCoDerivedStateRebuilder<TValue>
        where TValue : unmanaged
    {
        bool TryRebuild(in CoCoDerivedStateReadContext context, out TValue value);
    }

    public readonly struct CoCoDerivedStateReadContext
    {
        private readonly CoCoContextFrameLayout _layout;
        private readonly byte[] _buffer;
        private readonly CoCoStateSlotId[] _declaredDependencies;

        internal CoCoDerivedStateReadContext(
            CoCoContextFrameLayout layout,
            byte[] buffer,
            CoCoStateSlotId[] declaredDependencies)
        {
            _layout = layout;
            _buffer = buffer;
            _declaredDependencies = declaredDependencies;
        }

        public bool TryRead<TValue>(CoCoStateSlotId slotId, out TValue value)
            where TValue : unmanaged
        {
            if (_layout == null || _buffer == null || !IsDeclaredDependency(slotId) ||
                !_layout.TryResolveSlot(slotId, out CoCoStateSlot<TValue> slot))
            {
                value = default;
                return false;
            }

            value = CoCoStateFlowTypeRules.Read<TValue>(_buffer, slot.ByteOffset);
            return true;
        }

        private bool IsDeclaredDependency(CoCoStateSlotId slotId)
        {
            if (_declaredDependencies == null)
            {
                return false;
            }

            for (int index = 0; index < _declaredDependencies.Length; index++)
            {
                if (_declaredDependencies[index] == slotId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal interface ICoCoDerivedStateRebuilderAdapter
    {
        Type ValueType { get; }
        bool TryRebuild(
            CoCoContextFrameLayout layout,
            byte[] buffer,
            CoCoStateSlotDescriptor descriptor);
    }

    internal sealed class CoCoDerivedStateRebuilderAdapter<TValue> : ICoCoDerivedStateRebuilderAdapter
        where TValue : unmanaged
    {
        private readonly ICoCoDerivedStateRebuilder<TValue> _rebuilder;

        public CoCoDerivedStateRebuilderAdapter(ICoCoDerivedStateRebuilder<TValue> rebuilder)
        {
            _rebuilder = rebuilder;
        }

        public Type ValueType => typeof(TValue);

        public bool TryRebuild(
            CoCoContextFrameLayout layout,
            byte[] buffer,
            CoCoStateSlotDescriptor descriptor)
        {
            var context = new CoCoDerivedStateReadContext(
                layout,
                buffer,
                descriptor.DerivedDependencyArray);
            if (!_rebuilder.TryRebuild(in context, out TValue value))
            {
                return false;
            }

            CoCoStateFlowTypeRules.Write(buffer, descriptor.ByteOffset, value);
            return true;
        }
    }

    public readonly struct CoCoStateSlot<TValue> : IEquatable<CoCoStateSlot<TValue>>
        where TValue : unmanaged
    {
        private readonly CoCoContextFrameLayout _layout;

        internal CoCoStateSlot(
            CoCoContextFrameLayout layout,
            CoCoStateSlotId slotId,
            int denseIndex,
            int byteOffset,
            int byteSize)
        {
            _layout = layout;
            LayoutId = layout.LayoutId;
            LayoutVersion = layout.Version;
            LayoutSchemaHash = layout.SchemaHash;
            SlotId = slotId;
            DenseIndex = denseIndex;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public CoCoStateSlotId SlotId { get; }
        public int DenseIndex { get; }
        internal int ByteOffset { get; }
        internal int ByteSize { get; }
        public bool IsValid => _layout != null && LayoutId.IsValid && LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL && SlotId.IsValid && DenseIndex >= 0 &&
                               ByteOffset >= 0 && ByteSize > 0;

        internal bool IsFor(CoCoContextFrameLayout layout) =>
            ReferenceEquals(_layout, layout) &&
            layout != null &&
            LayoutId == layout.LayoutId &&
            LayoutVersion == layout.Version &&
            LayoutSchemaHash == layout.SchemaHash;

        public bool Equals(CoCoStateSlot<TValue> other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   SlotId == other.SlotId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) => obj is CoCoStateSlot<TValue> other && Equals(other);
        public override int GetHashCode() => unchecked((LayoutId.GetHashCode() * 397) ^ DenseIndex);
    }

    public sealed class CoCoStateSlotDescriptor
    {
        private readonly CoCoStateSlotId[] _derivedDependencies;

        internal CoCoStateSlotDescriptor(
            CoCoStateSlotId slotId,
            CoCoStateBlockId writerBlockId,
            Type valueType,
            int denseIndex,
            int byteOffset,
            int byteSize,
            CoCoContextProjection projection,
            CoCoContextRestorePolicy restorePolicy,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
            ICoCoDerivedStateRebuilderAdapter derivedRebuilder,
            byte[] defaultBytes)
        {
            SlotId = slotId;
            WriterBlockId = writerBlockId;
            ValueType = valueType;
            DenseIndex = denseIndex;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
            Projection = projection;
            RestorePolicy = restorePolicy;
            Codec = codec;
            _derivedDependencies = (CoCoStateSlotId[])derivedDependencies.Clone();
            DerivedDependencies = Array.AsReadOnly(_derivedDependencies);
            DerivedRebuilder = derivedRebuilder;
            DefaultBytes = defaultBytes;
        }

        public CoCoStateSlotId SlotId { get; }
        public CoCoStateBlockId WriterBlockId { get; }
        public Type ValueType { get; }
        public int DenseIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public CoCoContextProjection Projection { get; }
        public CoCoContextRestorePolicy RestorePolicy { get; }
        public CoCoCodecDescriptor Codec { get; }
        public IReadOnlyList<CoCoStateSlotId> DerivedDependencies { get; }
        internal CoCoStateSlotId[] DerivedDependencyArray => _derivedDependencies;
        internal ICoCoDerivedStateRebuilderAdapter DerivedRebuilder { get; }
        internal byte[] DefaultBytes { get; }
    }

    public sealed class CoCoStateBlockDescriptor
    {
        internal CoCoStateBlockDescriptor(
            CoCoStateBlockId blockId,
            CoCoStateBlockOwner owner,
            int denseIndex,
            CoCoStateSlotDescriptor[] slots)
        {
            BlockId = blockId;
            Owner = owner;
            DenseIndex = denseIndex;
            Slots = Array.AsReadOnly(slots);
        }

        public CoCoStateBlockId BlockId { get; }
        public CoCoStateBlockOwner Owner { get; }
        public int DenseIndex { get; }
        public IReadOnlyList<CoCoStateSlotDescriptor> Slots { get; }
    }

    public sealed class CoCoContextFrameLayoutBuilder
    {
        private readonly List<BlockRegistration> _blocks = new List<BlockRegistration>();
        private readonly Dictionary<CoCoStateBlockId, BlockRegistration> _blocksById =
            new Dictionary<CoCoStateBlockId, BlockRegistration>();
        private readonly Dictionary<CoCoStateSlotId, SlotRegistration> _slotsById =
            new Dictionary<CoCoStateSlotId, SlotRegistration>();
        private bool _frozen;

        public bool TryAddBlock(
            CoCoStateBlockId blockId,
            CoCoStateBlockOwner owner,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_frozen)
            {
                diagnosticCode = CoCoDiagnosticCode.RegistryFrozen;
                return false;
            }

            if (!blockId.IsValid || owner == CoCoStateBlockOwner.None || !Enum.IsDefined(typeof(CoCoStateBlockOwner), owner))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateBlock;
                return false;
            }

            if (_blocksById.ContainsKey(blockId))
            {
                diagnosticCode = CoCoDiagnosticCode.DuplicateIdentifier;
                return false;
            }

            var block = new BlockRegistration(blockId, owner);
            _blocks.Add(block);
            _blocksById.Add(blockId, block);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryAddSlot<TValue>(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoContextProjection projection,
            CoCoContextRestorePolicy restorePolicy,
            TValue defaultValue,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            return TryAddSlotCore(
                blockId,
                slotId,
                projection,
                restorePolicy,
                defaultValue,
                codec,
                derivedDependencies,
                null,
                out diagnosticCode);
        }

        public bool TryAddDerivedSlot<TValue>(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoContextProjection projection,
            TValue defaultValue,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
            ICoCoDerivedStateRebuilder<TValue> rebuilder,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            ICoCoDerivedStateRebuilderAdapter adapter = rebuilder == null
                ? null
                : new CoCoDerivedStateRebuilderAdapter<TValue>(rebuilder);
            return TryAddSlotCore(
                blockId,
                slotId,
                projection,
                CoCoContextRestorePolicy.Derived,
                defaultValue,
                codec,
                derivedDependencies,
                adapter,
                out diagnosticCode);
        }

        private bool TryAddSlotCore<TValue>(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoContextProjection projection,
            CoCoContextRestorePolicy restorePolicy,
            TValue defaultValue,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
            ICoCoDerivedStateRebuilderAdapter derivedRebuilder,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            if (_frozen)
            {
                diagnosticCode = CoCoDiagnosticCode.RegistryFrozen;
                return false;
            }

            if (!_blocksById.TryGetValue(blockId, out BlockRegistration block) || !slotId.IsValid)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
                return false;
            }

            if (_slotsById.ContainsKey(slotId))
            {
                diagnosticCode = CoCoDiagnosticCode.DuplicateIdentifier;
                return false;
            }

            const CoCoContextProjection knownProjection =
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable;
            if (!CoCoStateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)) ||
                !codec.IsValid ||
                (projection & ~knownProjection) != 0 ||
                restorePolicy == CoCoContextRestorePolicy.None ||
                !Enum.IsDefined(typeof(CoCoContextRestorePolicy), restorePolicy))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
                return false;
            }

            CoCoStateSlotId[] dependencies = derivedDependencies == null
                ? Array.Empty<CoCoStateSlotId>()
                : (CoCoStateSlotId[])derivedDependencies.Clone();
            if (restorePolicy == CoCoContextRestorePolicy.Derived &&
                (dependencies.Length == 0 || derivedRebuilder == null || derivedRebuilder.ValueType != typeof(TValue)))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (restorePolicy != CoCoContextRestorePolicy.Derived &&
                (dependencies.Length != 0 || derivedRebuilder != null))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            int byteSize = CoCoStateFlowTypeRules.SizeOf<TValue>();
            var defaultBytes = new byte[byteSize];
            CoCoStateFlowTypeRules.Write(defaultBytes, 0, defaultValue);
            var slot = new SlotRegistration(
                slotId,
                blockId,
                typeof(TValue),
                byteSize,
                projection,
                restorePolicy,
                codec,
                dependencies,
                derivedRebuilder,
                defaultBytes);
            block.Slots.Add(slot);
            _slotsById.Add(slotId, slot);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryFreeze(
            CoCoFrameLayoutId layoutId,
            uint version,
            out CoCoContextFrameLayout layout,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_frozen || !layoutId.IsValid || version == 0U)
            {
                layout = null;
                diagnosticCode = _frozen
                    ? CoCoDiagnosticCode.RegistryFrozen
                    : CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            int nextDenseIndex = 0;
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    block.Slots[slotIndex].DenseIndex = nextDenseIndex++;
                }
            }

            if (!ValidateDerivedDependencies(out int[] derivedOrder, out diagnosticCode))
            {
                layout = null;
                return false;
            }

            if (!ValidateProjectionDependencyClosure(out diagnosticCode))
            {
                layout = null;
                return false;
            }

            var slots = new CoCoStateSlotDescriptor[_slotsById.Count];
            var blocks = new CoCoStateBlockDescriptor[_blocks.Count];
            int offset = 0;
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                var blockSlots = new CoCoStateSlotDescriptor[block.Slots.Count];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    SlotRegistration slot = block.Slots[slotIndex];
                    if (!TryAlign(offset, Math.Min(slot.ByteSize, 8), out int alignedOffset) ||
                        slot.ByteSize > int.MaxValue - alignedOffset)
                    {
                        layout = null;
                        diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                        return false;
                    }

                    offset = alignedOffset;
                    var descriptor = new CoCoStateSlotDescriptor(
                        slot.SlotId,
                        slot.WriterBlockId,
                        slot.ValueType,
                        slot.DenseIndex,
                        offset,
                        slot.ByteSize,
                        slot.Projection,
                        slot.RestorePolicy,
                        slot.Codec,
                        slot.DerivedDependencies,
                        slot.DerivedRebuilder,
                        slot.DefaultBytes);
                    slots[slot.DenseIndex] = descriptor;
                    blockSlots[slotIndex] = descriptor;
                    offset = checked(offset + slot.ByteSize);
                }

                blocks[blockIndex] = new CoCoStateBlockDescriptor(
                    block.BlockId,
                    block.Owner,
                    blockIndex,
                    blockSlots);
            }

            var defaultBuffer = new byte[offset];
            for (int index = 0; index < slots.Length; index++)
            {
                CoCoStateSlotDescriptor slot = slots[index];
                Buffer.BlockCopy(slot.DefaultBytes, 0, defaultBuffer, slot.ByteOffset, slot.ByteSize);
            }

            ulong schemaHash = CoCoStateFlowSchemaHash.Compute(blocks, slots);
            _frozen = true;
            layout = new CoCoContextFrameLayout(
                layoutId,
                version,
                schemaHash,
                blocks,
                slots,
                derivedOrder,
                defaultBuffer);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        private bool ValidateDerivedDependencies(
            out int[] derivedOrder,
            out CoCoDiagnosticCode diagnosticCode)
        {
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    SlotRegistration slot = block.Slots[slotIndex];
                    for (int dependencyIndex = 0;
                         dependencyIndex < slot.DerivedDependencies.Length;
                         dependencyIndex++)
                    {
                        CoCoStateSlotId dependencyId = slot.DerivedDependencies[dependencyIndex];
                        if (!_slotsById.ContainsKey(dependencyId) || dependencyId == slot.SlotId)
                        {
                            derivedOrder = Array.Empty<int>();
                            diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                            return false;
                        }

                        for (int priorIndex = 0; priorIndex < dependencyIndex; priorIndex++)
                        {
                            if (slot.DerivedDependencies[priorIndex] == dependencyId)
                            {
                                derivedOrder = Array.Empty<int>();
                                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                                return false;
                            }
                        }
                    }
                }
            }

            var states = new Dictionary<CoCoStateSlotId, byte>();
            var ordered = new List<int>();
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    SlotRegistration slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy == CoCoContextRestorePolicy.Derived &&
                        !VisitDerived(slot, states, ordered))
                    {
                        derivedOrder = Array.Empty<int>();
                        diagnosticCode = CoCoDiagnosticCode.DerivedDependencyCycle;
                        return false;
                    }
                }
            }

            derivedOrder = ordered.ToArray();
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        private bool ValidateProjectionDependencyClosure(
            out CoCoDiagnosticCode diagnosticCode)
        {
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    SlotRegistration slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy != CoCoContextRestorePolicy.Derived)
                    {
                        continue;
                    }

                    for (int dependencyIndex = 0;
                         dependencyIndex < slot.DerivedDependencies.Length;
                         dependencyIndex++)
                    {
                        SlotRegistration dependency = _slotsById[slot.DerivedDependencies[dependencyIndex]];
                        if (dependency.RestorePolicy == CoCoContextRestorePolicy.ResetToDefault)
                        {
                            continue;
                        }

                        if (((slot.Projection & CoCoContextProjection.Temporal) != 0 &&
                             (dependency.Projection & CoCoContextProjection.Temporal) == 0) ||
                            ((slot.Projection & CoCoContextProjection.Durable) != 0 &&
                             (dependency.Projection & CoCoContextProjection.Durable) == 0))
                        {
                            diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                            return false;
                        }
                    }
                }
            }

            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        private bool VisitDerived(
            SlotRegistration slot,
            Dictionary<CoCoStateSlotId, byte> states,
            List<int> ordered)
        {
            if (states.TryGetValue(slot.SlotId, out byte state))
            {
                return state == 2;
            }

            states[slot.SlotId] = 1;
            for (int index = 0; index < slot.DerivedDependencies.Length; index++)
            {
                SlotRegistration dependency = _slotsById[slot.DerivedDependencies[index]];
                if (dependency.RestorePolicy == CoCoContextRestorePolicy.Derived)
                {
                    if (states.TryGetValue(dependency.SlotId, out byte dependencyState) && dependencyState == 1)
                    {
                        return false;
                    }

                    if (!VisitDerived(dependency, states, ordered))
                    {
                        return false;
                    }
                }
            }

            states[slot.SlotId] = 2;
            ordered.Add(slot.DenseIndex);
            return true;
        }

        private static bool TryAlign(int value, int alignment, out int aligned)
        {
            if (alignment <= 1)
            {
                aligned = value;
                return true;
            }

            int remainder = value % alignment;
            int padding = remainder == 0 ? 0 : alignment - remainder;
            if (padding > int.MaxValue - value)
            {
                aligned = 0;
                return false;
            }

            aligned = value + padding;
            return true;
        }

        private sealed class BlockRegistration
        {
            public BlockRegistration(CoCoStateBlockId blockId, CoCoStateBlockOwner owner)
            {
                BlockId = blockId;
                Owner = owner;
            }

            public CoCoStateBlockId BlockId { get; }
            public CoCoStateBlockOwner Owner { get; }
            public List<SlotRegistration> Slots { get; } = new List<SlotRegistration>();
        }

        private sealed class SlotRegistration
        {
            public SlotRegistration(
                CoCoStateSlotId slotId,
                CoCoStateBlockId writerBlockId,
                Type valueType,
                int byteSize,
                CoCoContextProjection projection,
                CoCoContextRestorePolicy restorePolicy,
                CoCoCodecDescriptor codec,
                CoCoStateSlotId[] derivedDependencies,
                ICoCoDerivedStateRebuilderAdapter derivedRebuilder,
                byte[] defaultBytes)
            {
                SlotId = slotId;
                WriterBlockId = writerBlockId;
                ValueType = valueType;
                ByteSize = byteSize;
                Projection = projection;
                RestorePolicy = restorePolicy;
                Codec = codec;
                DerivedDependencies = derivedDependencies;
                DerivedRebuilder = derivedRebuilder;
                DefaultBytes = defaultBytes;
                DenseIndex = -1;
            }

            public CoCoStateSlotId SlotId { get; }
            public CoCoStateBlockId WriterBlockId { get; }
            public Type ValueType { get; }
            public int ByteSize { get; }
            public CoCoContextProjection Projection { get; }
            public CoCoContextRestorePolicy RestorePolicy { get; }
            public CoCoCodecDescriptor Codec { get; }
            public CoCoStateSlotId[] DerivedDependencies { get; }
            public ICoCoDerivedStateRebuilderAdapter DerivedRebuilder { get; }
            public byte[] DefaultBytes { get; }
            public int DenseIndex { get; set; }
        }
    }

    public sealed class CoCoContextFrameLayout
    {
        private readonly Dictionary<CoCoStateBlockId, CoCoStateBlockDescriptor> _blocksById;
        private readonly Dictionary<CoCoStateSlotId, CoCoStateSlotDescriptor> _slotsById;
        private readonly CoCoStateBlockDescriptor[] _blocks;
        private readonly CoCoStateSlotDescriptor[] _slots;
        private readonly byte[] _defaultBuffer;
        private readonly int[] _derivedOrder;

        internal CoCoContextFrameLayout(
            CoCoFrameLayoutId layoutId,
            uint version,
            ulong schemaHash,
            CoCoStateBlockDescriptor[] blocks,
            CoCoStateSlotDescriptor[] slots,
            int[] derivedOrder,
            byte[] defaultBuffer)
        {
            LayoutId = layoutId;
            Version = version;
            SchemaHash = schemaHash;
            _blocks = blocks;
            _slots = slots;
            Blocks = Array.AsReadOnly(_blocks);
            Slots = Array.AsReadOnly(_slots);
            DerivedOrder = Array.AsReadOnly(derivedOrder);
            _derivedOrder = derivedOrder;
            _defaultBuffer = defaultBuffer;
            _blocksById = new Dictionary<CoCoStateBlockId, CoCoStateBlockDescriptor>(blocks.Length);
            for (int index = 0; index < blocks.Length; index++)
            {
                _blocksById.Add(blocks[index].BlockId, blocks[index]);
            }

            _slotsById = new Dictionary<CoCoStateSlotId, CoCoStateSlotDescriptor>(slots.Length);
            for (int index = 0; index < slots.Length; index++)
            {
                _slotsById.Add(slots[index].SlotId, slots[index]);
            }
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public uint Version { get; }
        public ulong SchemaHash { get; }
        public IReadOnlyList<CoCoStateBlockDescriptor> Blocks { get; }
        public IReadOnlyList<CoCoStateSlotDescriptor> Slots { get; }
        public IReadOnlyList<int> DerivedOrder { get; }
        public int ByteSize => _defaultBuffer.Length;

        public bool TryResolveBlock(CoCoStateBlockId blockId, out CoCoStateBlockHandle block)
        {
            if (_blocksById.TryGetValue(blockId, out CoCoStateBlockDescriptor descriptor))
            {
                block = new CoCoStateBlockHandle(
                    this,
                    descriptor.BlockId,
                    descriptor.DenseIndex,
                    descriptor.Owner);
                return true;
            }

            block = default;
            return false;
        }

        public bool TryResolveSlot<TValue>(CoCoStateSlotId slotId, out CoCoStateSlot<TValue> slot)
            where TValue : unmanaged
        {
            if (_slotsById.TryGetValue(slotId, out CoCoStateSlotDescriptor descriptor) &&
                descriptor.ValueType == typeof(TValue))
            {
                slot = new CoCoStateSlot<TValue>(
                    this,
                    descriptor.SlotId,
                    descriptor.DenseIndex,
                    descriptor.ByteOffset,
                    descriptor.ByteSize);
                return true;
            }

            slot = default;
            return false;
        }

        internal byte[] CreateBuffer()
        {
            var buffer = new byte[_defaultBuffer.Length];
            Buffer.BlockCopy(_defaultBuffer, 0, buffer, 0, _defaultBuffer.Length);
            return buffer;
        }

        internal bool IsWriterFor(CoCoStateBlockHandle block, int slotDenseIndex)
        {
            return block.IsValid && block.IsFor(this) &&
                   block.DenseIndex >= 0 && block.DenseIndex < _blocks.Length &&
                   slotDenseIndex >= 0 && slotDenseIndex < _slots.Length &&
                   _blocks[block.DenseIndex].BlockId == block.BlockId &&
                   _slots[slotDenseIndex].WriterBlockId == block.BlockId &&
                   _slots[slotDenseIndex].RestorePolicy != CoCoContextRestorePolicy.Derived;
        }

        internal void CopyDefaultsTo(byte[] destination)
        {
            System.Buffer.BlockCopy(_defaultBuffer, 0, destination, 0, _defaultBuffer.Length);
        }

        internal bool TryRestore(byte[] source, byte[] destination)
        {
            if (source == null || destination == null ||
                source.Length != _defaultBuffer.Length || destination.Length != _defaultBuffer.Length)
            {
                return false;
            }

            CopyDefaultsTo(destination);
            for (int index = 0; index < _slots.Length; index++)
            {
                CoCoStateSlotDescriptor slot = _slots[index];
                if (slot.RestorePolicy == CoCoContextRestorePolicy.Stored)
                {
                    System.Buffer.BlockCopy(
                        source,
                        slot.ByteOffset,
                        destination,
                        slot.ByteOffset,
                        slot.ByteSize);
                }
            }

            return TryRebuildDerived(destination);
        }

        internal bool TryRebuildDerived(byte[] destination)
        {
            if (destination == null || destination.Length != _defaultBuffer.Length)
            {
                return false;
            }

            for (int index = 0; index < _derivedOrder.Length; index++)
            {
                CoCoStateSlotDescriptor slot = _slots[_derivedOrder[index]];
                if (slot.DerivedRebuilder == null ||
                    !slot.DerivedRebuilder.TryRebuild(this, destination, slot))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool HasProjectionDependencyClosure(CoCoContextProjection projection)
        {
            if (projection != CoCoContextProjection.Temporal &&
                projection != CoCoContextProjection.Durable)
            {
                return false;
            }

            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                CoCoStateSlotDescriptor slot = _slots[slotIndex];
                if (slot.RestorePolicy != CoCoContextRestorePolicy.Derived ||
                    (slot.Projection & projection) == 0)
                {
                    continue;
                }

                CoCoStateSlotId[] dependencies = slot.DerivedDependencyArray;
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
                {
                    if (!_slotsById.TryGetValue(
                            dependencies[dependencyIndex],
                            out CoCoStateSlotDescriptor dependency) ||
                        (dependency.RestorePolicy != CoCoContextRestorePolicy.ResetToDefault &&
                         (dependency.Projection & projection) == 0))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal bool HasExactIdentity(
            CoCoFrameLayoutId layoutId,
            uint version,
            ulong schemaHash)
        {
            return LayoutId == layoutId && Version == version && SchemaHash == schemaHash;
        }

        internal bool IsSameInstance(CoCoContextFrameLayout other) => ReferenceEquals(this, other);
    }

    internal readonly struct CoCoProjectionRestoreSource
    {
        public CoCoProjectionRestoreSource(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineId timelineId,
            CoCoClockDomainId clockDomainId,
            CoCoExecutionSequence executionSequence,
            CoCoTimelineEpoch timelineEpoch,
            CoCoTimelineTick tick,
            CoCoContextRevision revision)
        {
            GraphInstanceId = graphInstanceId;
            TimelineId = timelineId;
            ClockDomainId = clockDomainId;
            ExecutionSequence = executionSequence;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            Revision = revision;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTimelineId TimelineId { get; }
        public CoCoClockDomainId ClockDomainId { get; }
        public CoCoExecutionSequence ExecutionSequence { get; }
        public CoCoTimelineEpoch TimelineEpoch { get; }
        public CoCoTimelineTick Tick { get; }
        public CoCoContextRevision Revision { get; }
        public bool IsValid => GraphInstanceId.IsValid && TimelineId.IsValid &&
                               ClockDomainId.IsValid && Revision.IsValid;
    }

    internal sealed class CoCoContextProjectionCodec
    {
        private const uint Magic = 0x43434650U;
        private const uint FormatVersion = 2U;
        private const int HeaderSize = 108;
        private const int EntryHeaderSize = 48;

        private readonly CoCoContextFrameLayout _layout;
        private readonly CoCoContextCodecRegistry _registry;
        private readonly ProjectionSlotBinding[] _bindings;

        private CoCoContextProjectionCodec(
            CoCoContextFrameLayout layout,
            CoCoContextCodecRegistry registry,
            CoCoContextProjection projection,
            ProjectionSlotBinding[] bindings,
            int maxEncodedSize)
        {
            _layout = layout;
            _registry = registry;
            Projection = projection;
            _bindings = bindings;
            MaxEncodedSize = maxEncodedSize;
        }

        public CoCoContextFrameLayout Layout => _layout;
        public CoCoContextProjection Projection { get; }
        public int MaxEncodedSize { get; }

        public static bool TryCreate(
            CoCoContextFrameLayout layout,
            CoCoContextCodecRegistry registry,
            CoCoContextProjection projection,
            out CoCoContextProjectionCodec codec,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (layout == null ||
                (projection != CoCoContextProjection.Temporal &&
                 projection != CoCoContextProjection.Durable))
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
                CoCoStateSlotDescriptor slot = layout.Slots[index];
                if ((slot.Projection & projection) == 0 ||
                    slot.RestorePolicy != CoCoContextRestorePolicy.Stored)
                {
                    continue;
                }

                ICoCoContextValueCodecAdapter adapter = null;
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

            codec = new CoCoContextProjectionCodec(
                layout,
                registry,
                projection,
                bindings.ToArray(),
                (int)maxEncodedSize);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryEncode(
            CoCoContextFrame frame,
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

            if (destination.Length < MaxEncodedSize)
            {
                diagnosticCode = CoCoDiagnosticCode.CommitPreparationFailed;
                return false;
            }

            int cursor = 0;
            CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, Magic);
            CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, FormatVersion);
            CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, (uint)Projection);
            CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, _layout.LayoutId.High);
            CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, _layout.LayoutId.Low);
            CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, _layout.Version);
            CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, _layout.SchemaHash);
            CoCoStateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                frame.Header.Identity.GraphInstanceId.Value);
            CoCoStateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                frame.Header.TickFrame.TimelineId.High);
            CoCoStateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                frame.Header.TickFrame.TimelineId.Low);
            CoCoStateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                frame.Header.TickFrame.ClockDomainId.Value);
            CoCoStateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                frame.Header.TickFrame.ExecutionSequence.Value);
            CoCoStateFlowBinary.WriteUInt64(
                destination,
                ref cursor,
                frame.Header.Identity.TimelineEpoch.Value);
            CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, frame.Header.Identity.Tick.Value);
            CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, frame.Revision.Value);
            CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, (uint)_bindings.Length);

            for (int index = 0; index < _bindings.Length; index++)
            {
                ProjectionSlotBinding binding = _bindings[index];
                CoCoStateSlotDescriptor slot = binding.Slot;
                CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, slot.SlotId.High);
                CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, slot.SlotId.Low);
                CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, slot.Codec.CodecId.High);
                CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, slot.Codec.CodecId.Low);
                CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, slot.Codec.Version);
                CoCoStateFlowBinary.WriteUInt64(destination, ref cursor, binding.ValueTypeHash);
                int lengthOffset = cursor;
                CoCoStateFlowBinary.WriteUInt32(destination, ref cursor, 0U);

                int payloadLength;
                if (binding.Adapter == null)
                {
                    payloadLength = slot.ByteSize;
                    new ReadOnlySpan<byte>(frame.Buffer, slot.ByteOffset, slot.ByteSize)
                        .CopyTo(destination.Slice(cursor, slot.ByteSize));
                }
                else if (!binding.Adapter.TryEncode(
                             frame.Buffer,
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
                CoCoStateFlowBinary.WriteUInt32(destination, ref patchCursor, (uint)payloadLength);
                cursor += payloadLength;
            }

            bytesWritten = cursor;
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        public bool TryDecodeAndPrepareRestore(
            ReadOnlySpan<byte> source,
            CoCoContextFrameArena arena,
            CoCoTickFrame resumedTickFrame,
            out CoCoFinalizedContextCommit finalized,
            out int bytesRead,
            out CoCoContextCommitStatus commitStatus,
            out CoCoDiagnosticCode diagnosticCode)
        {
            finalized = default;
            bytesRead = 0;
            commitStatus = CoCoContextCommitStatus.RestoreFailed;
            if (arena == null || !_layout.IsSameInstance(arena.Layout))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (!TryValidateSource(
                    source,
                    out CoCoProjectionRestoreSource restoreSource,
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

        internal bool TryDecodePayload(
            ReadOnlySpan<byte> source,
            byte[] destination,
            out CoCoDiagnosticCode diagnosticCode)
        {
            int cursor = HeaderSize;
            for (int index = 0; index < _bindings.Length; index++)
            {
                ProjectionSlotBinding binding = _bindings[index];
                cursor += EntryHeaderSize - sizeof(uint);
                if (!CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint payloadLengthValue))
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

            if (!_layout.TryRebuildDerived(destination))
            {
                diagnosticCode = CoCoDiagnosticCode.CommitPreparationFailed;
                return false;
            }

            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        private bool TryValidateSource(
            ReadOnlySpan<byte> source,
            out CoCoProjectionRestoreSource restoreSource,
            out int encodedLength,
            out CoCoDiagnosticCode diagnosticCode)
        {
            restoreSource = default;
            encodedLength = 0;
            int cursor = 0;
            if (source.Length < HeaderSize ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint magic) ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint formatVersion) ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint projection) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong layoutHigh) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong layoutLow) ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint layoutVersion) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong layoutSchemaHash) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong graphInstanceValue) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong timelineHigh) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong timelineLow) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong clockDomainValue) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong executionSequenceValue) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong timelineEpochValue) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong tickValue) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong revisionValue) ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint slotCount))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (magic != Magic || formatVersion != FormatVersion || projection != (uint)Projection)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (!CoCoFrameLayoutId.TryCreate(layoutHigh, layoutLow, out CoCoFrameLayoutId layoutId) ||
                !_layout.HasExactIdentity(layoutId, layoutVersion, layoutSchemaHash))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (slotCount != (uint)_bindings.Length ||
                !CoCoGraphInstanceId.TryCreate(graphInstanceValue, out CoCoGraphInstanceId graphInstanceId) ||
                !CoCoTimelineId.TryCreate(timelineHigh, timelineLow, out CoCoTimelineId timelineId) ||
                !CoCoClockDomainId.TryCreate(clockDomainValue, out CoCoClockDomainId clockDomainId))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            var revision = new CoCoContextRevision(revisionValue);
            restoreSource = new CoCoProjectionRestoreSource(
                graphInstanceId,
                timelineId,
                clockDomainId,
                new CoCoExecutionSequence(executionSequenceValue),
                new CoCoTimelineEpoch(timelineEpochValue),
                new CoCoTimelineTick(tickValue),
                revision);
            if (!restoreSource.IsValid)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            for (int index = 0; index < _bindings.Length; index++)
            {
                ProjectionSlotBinding binding = _bindings[index];
                if (!CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong slotHigh) ||
                    !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong slotLow) ||
                    !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong codecHigh) ||
                    !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong codecLow) ||
                    !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint codecVersion) ||
                    !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong valueTypeHash) ||
                    !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint payloadLengthValue))
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

            CoCoCodecDescriptor expected = binding.Slot.Codec;
            if (codecHigh == expected.CodecId.High &&
                codecLow == expected.CodecId.Low &&
                codecVersion == expected.Version)
            {
                diagnosticCode = CoCoDiagnosticCode.None;
                return true;
            }

            if (!CoCoCodecId.TryCreate(codecHigh, codecLow, out CoCoCodecId codecId))
            {
                diagnosticCode = expected.UsesCustomCodec
                    ? CoCoDiagnosticCode.UnknownCodec
                    : CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            diagnosticCode = _registry.Classify(
                new CoCoCodecDescriptor(codecId, codecVersion),
                binding.Slot.ValueType);
            if (diagnosticCode == CoCoDiagnosticCode.None)
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
            }

            return false;
        }

        private sealed class ProjectionSlotBinding
        {
            public ProjectionSlotBinding(
                CoCoStateSlotDescriptor slot,
                ICoCoContextValueCodecAdapter adapter,
                int maxValueSize)
            {
                Slot = slot;
                Adapter = adapter;
                MaxValueSize = maxValueSize;
                ValueTypeHash = CoCoStateFlowSchemaHash.HashType(slot.ValueType);
            }

            public CoCoStateSlotDescriptor Slot { get; }
            public ICoCoContextValueCodecAdapter Adapter { get; }
            public int MaxValueSize { get; }
            public ulong ValueTypeHash { get; }
        }
    }

    public interface ICoCoContextFrame
    {
        CoCoStateFlowFrameHeader Header { get; }
        CoCoContextRevision Revision { get; }
        CoCoContextFrameOrigin Origin { get; }
        T Read<T>(CoCoStateSlot<T> slot) where T : unmanaged;
    }

    /// <summary>
    /// Immutable generation-scoped handle to one committed ContextFrame.
    /// Reusing the underlying arena cell never makes an expired handle valid again.
    /// </summary>
    public readonly struct CoCoContextFrame : ICoCoContextFrame, IEquatable<CoCoContextFrame>
    {
        private readonly CoCoContextFrameStorage _storage;
        private readonly ulong _generation;

        internal CoCoContextFrame(CoCoContextFrameStorage storage, ulong generation)
        {
            _storage = storage;
            _generation = generation;
        }

        public CoCoStateFlowFrameHeader Header => IsAlive ? _storage.Header : default;
        public CoCoContextRevision Revision => IsAlive ? _storage.Revision : default;
        public CoCoContextFrameOrigin Origin => IsAlive ? _storage.Origin : default;
        public CoCoContextFrameLayout Layout => IsAlive ? _storage.Layout : null;
        public bool IsAlive => _storage != null && _storage.IsAlive(_generation);

        public T Read<T>(CoCoStateSlot<T> slot)
            where T : unmanaged
        {
            if (!IsAlive || !slot.IsValid || !slot.IsFor(_storage.Layout))
            {
                throw new InvalidOperationException("The ContextFrame or StateSlot handle is not valid for this read.");
            }

            return CoCoStateFlowTypeRules.Read<T>(_storage.Buffer, slot.ByteOffset);
        }

        public bool Retain() => _storage != null && _storage.TryRetain(_generation);
        public bool Release() => _storage != null && _storage.TryRelease(_generation);

        public bool Equals(CoCoContextFrame other) =>
            ReferenceEquals(_storage, other._storage) && _generation == other._generation;

        public override bool Equals(object obj) => obj is CoCoContextFrame other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((_storage?.GetHashCode() ?? 0) * 397) ^ _generation.GetHashCode());

        public static bool operator ==(CoCoContextFrame left, CoCoContextFrame right) => left.Equals(right);
        public static bool operator !=(CoCoContextFrame left, CoCoContextFrame right) => !left.Equals(right);

        internal byte[] Buffer => IsAlive ? _storage.Buffer : null;

        internal bool TryGetStorage(out CoCoContextFrameStorage storage)
        {
            if (IsAlive)
            {
                storage = _storage;
                return true;
            }

            storage = null;
            return false;
        }
    }

    internal sealed class CoCoContextFrameStorage
    {
        private int _externalRetainCount;
        private bool _isArenaOwned;

        public CoCoContextFrameStorage(CoCoContextFrameLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Buffer = layout.CreateBuffer();
        }

        public CoCoStateFlowFrameHeader Header { get; private set; }
        public CoCoContextRevision Revision { get; private set; }
        public CoCoContextFrameOrigin Origin { get; private set; }
        public CoCoContextFrameLayout Layout { get; }
        public byte[] Buffer { get; }
        public ulong Generation { get; private set; }
        public long RetainCount => (_isArenaOwned ? 1L : 0L) + _externalRetainCount;
        public bool CanReuse => RetainCount == 0 && Generation != ulong.MaxValue;

        public bool IsAlive(ulong generation) =>
            generation != 0UL && generation == Generation && RetainCount > 0;

        public bool TryPrepare(
            CoCoStateFlowFrameHeader header,
            CoCoContextRevision revision,
            CoCoContextFrameOrigin origin,
            CoCoContextFrame source,
            out CoCoContextCommitStatus status)
        {
            if (!CanReuse)
            {
                status = CoCoContextCommitStatus.CapacityExhausted;
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
                status = CoCoContextCommitStatus.None;
                return true;
            }

            if (!source.TryGetStorage(out CoCoContextFrameStorage sourceStorage) ||
                !Layout.IsSameInstance(sourceStorage.Layout) ||
                !source.Header.HasExactLayoutIdentity ||
                !Layout.HasExactIdentity(
                    source.Header.LayoutId,
                    source.Header.LayoutVersion,
                    source.Header.LayoutSchemaHash))
            {
                status = CoCoContextCommitStatus.RestoreFailed;
                return false;
            }

            if (origin.IsRestore)
            {
                if (!Layout.TryRestore(sourceStorage.Buffer, Buffer))
                {
                    status = CoCoContextCommitStatus.DerivedRebuildFailed;
                    return false;
                }

                status = CoCoContextCommitStatus.None;
                return true;
            }

            System.Buffer.BlockCopy(sourceStorage.Buffer, 0, Buffer, 0, Buffer.Length);
            status = CoCoContextCommitStatus.None;
            return true;
        }

        public CoCoContextFrame Seal()
        {
            _isArenaOwned = true;
            return new CoCoContextFrame(this, Generation);
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

    public enum CoCoContextCommitStatus
    {
        None = 0,
        Succeeded = 1,
        InvalidTick = 2,
        InvalidOrigin = 3,
        GraphMismatch = 4,
        LayoutMismatch = 5,
        CapacityExhausted = 6,
        PreparationAlreadyActive = 7,
        InvalidPreparation = 8,
        Cancelled = 9,
        RestoreFailed = 10,
        RevisionExhausted = 11,
        DerivedRebuildFailed = 12
    }

    public readonly struct CoCoContextCommitResult
    {
        internal CoCoContextCommitResult(CoCoContextCommitStatus status, CoCoContextFrame frame)
        {
            Status = status;
            Frame = frame;
        }

        public CoCoContextCommitStatus Status { get; }
        public CoCoContextFrame Frame { get; }
        public bool Succeeded => Status == CoCoContextCommitStatus.Succeeded;
    }

    public readonly struct CoCoContextFrameWriter
    {
        private readonly CoCoContextFrameArena _arena;
        private readonly ulong _token;
        private readonly CoCoStateBlockHandle _block;

        internal CoCoContextFrameWriter(
            CoCoContextFrameArena arena,
            ulong token,
            CoCoStateBlockHandle block)
        {
            _arena = arena;
            _token = token;
            _block = block;
        }

        public CoCoStateBlockHandle Block => _block;
        public bool IsValid => _arena != null && _arena.IsPreparationActive(_token) && _block.IsValid;

        public bool Write<TValue>(CoCoStateSlot<TValue> slot, in TValue value)
            where TValue : unmanaged
        {
            return IsValid && _arena.TryWrite(_token, _block, slot, value);
        }
    }

    public readonly struct CoCoPreparedContextCommit
    {
        private readonly CoCoContextFrameArena _arena;
        private readonly ulong _token;

        internal CoCoPreparedContextCommit(CoCoContextFrameArena arena, ulong token)
        {
            _arena = arena;
            _token = token;
        }

        public bool IsValid => _arena != null && _arena.IsPreparationActive(_token);

        public bool TryGetWriter(
            CoCoStateBlockHandle block,
            out CoCoContextFrameWriter writer)
        {
            if (!IsValid || !_arena.CanWrite(_token, block))
            {
                writer = default;
                return false;
            }

            writer = new CoCoContextFrameWriter(_arena, _token, block);
            return true;
        }

        public bool TryFinalize(
            out CoCoFinalizedContextCommit finalized,
            out CoCoContextCommitStatus status)
        {
            if (_arena == null)
            {
                finalized = default;
                status = CoCoContextCommitStatus.InvalidPreparation;
                return false;
            }

            return _arena.TryFinalize(_token, out finalized, out status);
        }

        public CoCoContextCommitStatus Cancel()
        {
            return _arena == null
                ? CoCoContextCommitStatus.InvalidPreparation
                : _arena.CancelPrepared(_token);
        }
    }

    public readonly struct CoCoFinalizedContextCommit
    {
        private readonly CoCoContextFrameArena _arena;
        private readonly ulong _token;

        internal CoCoFinalizedContextCommit(CoCoContextFrameArena arena, ulong token)
        {
            _arena = arena;
            _token = token;
        }

        public bool IsValid => _arena != null && _arena.IsFinalized(_token);

        public CoCoContextCommitResult Commit()
        {
            return _arena == null
                ? new CoCoContextCommitResult(CoCoContextCommitStatus.InvalidPreparation, default)
                : _arena.Commit(_token);
        }

        public CoCoContextCommitStatus Cancel()
        {
            return _arena == null
                ? CoCoContextCommitStatus.InvalidPreparation
                : _arena.CancelFinalized(_token);
        }
    }

    public sealed class CoCoContextFrameArena
    {
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoContextFrameStorage[] _frames;
        private CoCoContextFrame _current;
        private CoCoContextFrameStorage _currentStorage;
        private CoCoContextFrameStorage _reserved;
        private ulong _activeToken;
        private ulong _nextToken;
        private bool _isFinalized;
        private bool _isCallbackActive;

        public CoCoContextFrameArena(
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout layout,
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
            _frames = new CoCoContextFrameStorage[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _frames[index] = new CoCoContextFrameStorage(layout);
            }
        }

        public CoCoContextFrameLayout Layout { get; }
        public CoCoContextFrame Current => _current;
        public bool HasCurrent => _current.IsAlive;
        public int Capacity => _frames.Length;

        public bool TryPrepare(
            CoCoTickFrame tickFrame,
            out CoCoPreparedContextCommit prepared,
            out CoCoContextCommitStatus status)
        {
            if (_isCallbackActive)
            {
                prepared = default;
                status = CoCoContextCommitStatus.PreparationAlreadyActive;
                return false;
            }

            return TryPrepareCore(tickFrame, CoCoContextFrameOrigin.Commit(), _current, out prepared, out status);
        }

        public bool TryPrepareRestore(
            CoCoContextFrame source,
            CoCoTickFrame resumedTickFrame,
            out CoCoFinalizedContextCommit finalized,
            out CoCoContextCommitStatus status)
        {
            if (_isCallbackActive)
            {
                finalized = default;
                status = CoCoContextCommitStatus.PreparationAlreadyActive;
                return false;
            }

            if (!resumedTickFrame.IsValid)
            {
                finalized = default;
                status = CoCoContextCommitStatus.InvalidTick;
                return false;
            }

            if (!source.IsAlive || !Layout.IsSameInstance(source.Layout) ||
                !source.Header.HasExactLayoutIdentity ||
                !Layout.HasExactIdentity(
                    source.Header.LayoutId,
                    source.Header.LayoutVersion,
                    source.Header.LayoutSchemaHash))
            {
                finalized = default;
                status = CoCoContextCommitStatus.LayoutMismatch;
                return false;
            }

            if (source.Header.Identity.GraphInstanceId != _graphInstanceId)
            {
                finalized = default;
                status = CoCoContextCommitStatus.GraphMismatch;
                return false;
            }

            if (resumedTickFrame.TimelineEpoch.Value <=
                    source.Header.Identity.TimelineEpoch.Value ||
                !CoCoStateFlowTickOrder.IsStrictlyAfter(
                    resumedTickFrame,
                    source.Header.TickFrame) ||
                (HasCurrent &&
                 resumedTickFrame.TimelineEpoch.Value <=
                 _current.Header.Identity.TimelineEpoch.Value) ||
                (HasCurrent &&
                 !CoCoStateFlowTickOrder.IsStrictlyAfter(
                     resumedTickFrame,
                     _current.Header.TickFrame)))
            {
                finalized = default;
                status = CoCoContextCommitStatus.InvalidOrigin;
                return false;
            }

            if (!TryPrepareCore(
                    resumedTickFrame,
                    CoCoContextFrameOrigin.RestoreFrom(source),
                    source,
                    out _,
                    out status))
            {
                finalized = default;
                return false;
            }

            _isFinalized = true;
            finalized = new CoCoFinalizedContextCommit(this, _activeToken);
            return true;
        }

        internal bool CanWrite(ulong token, CoCoStateBlockHandle block)
        {
            return IsPreparationActive(token) &&
                   block.IsValid && block.IsFor(Layout);
        }

        internal bool TryWrite<TValue>(
            ulong token,
            CoCoStateBlockHandle block,
            CoCoStateSlot<TValue> slot,
            in TValue value)
            where TValue : unmanaged
        {
            if (!IsPreparationActive(token) ||
                !slot.IsValid || !slot.IsFor(Layout) ||
                !Layout.IsWriterFor(block, slot.DenseIndex))
            {
                return false;
            }

            CoCoStateFlowTypeRules.Write(_reserved.Buffer, slot.ByteOffset, value);
            return true;
        }

        internal bool TryPrepareProjectionRestore(
            CoCoContextProjectionCodec codec,
            CoCoProjectionRestoreSource source,
            ReadOnlySpan<byte> encoded,
            CoCoTickFrame resumedTickFrame,
            out CoCoFinalizedContextCommit finalized,
            out CoCoContextCommitStatus status,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_isCallbackActive)
            {
                finalized = default;
                status = CoCoContextCommitStatus.PreparationAlreadyActive;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (codec == null || !Layout.IsSameInstance(codec.Layout))
            {
                finalized = default;
                status = CoCoContextCommitStatus.LayoutMismatch;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameLayout;
                return false;
            }

            if (!source.IsValid || source.GraphInstanceId != _graphInstanceId)
            {
                finalized = default;
                status = CoCoContextCommitStatus.GraphMismatch;
                diagnosticCode = CoCoDiagnosticCode.InvalidFrameHandle;
                return false;
            }

            if (!resumedTickFrame.IsValid)
            {
                finalized = default;
                status = CoCoContextCommitStatus.InvalidTick;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (resumedTickFrame.TimelineEpoch.Value <= source.TimelineEpoch.Value ||
                resumedTickFrame.TimelineId != source.TimelineId ||
                resumedTickFrame.ClockDomainId != source.ClockDomainId ||
                resumedTickFrame.ExecutionSequence.Value <= source.ExecutionSequence.Value ||
                (HasCurrent &&
                 resumedTickFrame.TimelineEpoch.Value <=
                 _current.Header.Identity.TimelineEpoch.Value) ||
                (HasCurrent &&
                 !CoCoStateFlowTickOrder.IsStrictlyAfter(
                     resumedTickFrame,
                     _current.Header.TickFrame)))
            {
                finalized = default;
                status = CoCoContextCommitStatus.InvalidOrigin;
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            CoCoContextFrameOrigin origin = CoCoContextFrameOrigin.RestoreFrom(
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
                didDecode = codec.TryDecodePayload(encoded, _reserved.Buffer, out diagnosticCode);
            }
            catch
            {
                _isCallbackActive = false;
                CancelActive(token);
                throw;
            }

            _isCallbackActive = false;
            if (!didDecode)
            {
                CancelActive(token);
                finalized = default;
                status = diagnosticCode == CoCoDiagnosticCode.CommitPreparationFailed
                    ? CoCoContextCommitStatus.DerivedRebuildFailed
                    : CoCoContextCommitStatus.RestoreFailed;
                return false;
            }

            _isFinalized = true;
            finalized = new CoCoFinalizedContextCommit(this, token);
            diagnosticCode = CoCoDiagnosticCode.None;
            return true;
        }

        internal bool IsPreparationActive(ulong token) =>
            !_isCallbackActive && _reserved != null && !_isFinalized &&
            token != 0UL && token == _activeToken;

        internal bool IsFinalized(ulong token) =>
            !_isCallbackActive && _reserved != null && _isFinalized &&
            token != 0UL && token == _activeToken;

        internal bool TryFinalize(
            ulong token,
            out CoCoFinalizedContextCommit finalized,
            out CoCoContextCommitStatus status)
        {
            if (_isCallbackActive || !IsPreparationActive(token))
            {
                finalized = default;
                status = CoCoContextCommitStatus.InvalidPreparation;
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
                CancelActive(token);
                throw;
            }

            _isCallbackActive = false;
            if (!didRebuild)
            {
                CancelActive(token);
                finalized = default;
                status = CoCoContextCommitStatus.DerivedRebuildFailed;
                return false;
            }

            _isFinalized = true;
            finalized = new CoCoFinalizedContextCommit(this, token);
            status = CoCoContextCommitStatus.None;
            return true;
        }

        internal CoCoContextCommitResult Commit(ulong token)
        {
            if (_isCallbackActive || !IsFinalized(token))
            {
                return new CoCoContextCommitResult(CoCoContextCommitStatus.InvalidPreparation, default);
            }

            CoCoContextFrameStorage committedStorage = _reserved;
            CoCoContextFrameStorage previousStorage = _currentStorage;
            _reserved = null;
            _activeToken = 0UL;
            _isFinalized = false;
            CoCoContextFrame committed = committedStorage.Seal();
            _currentStorage = committedStorage;
            _current = committed;
            previousStorage?.ReleaseArenaOwnership();
            return new CoCoContextCommitResult(CoCoContextCommitStatus.Succeeded, _current);
        }

        internal CoCoContextCommitStatus CancelPrepared(ulong token)
        {
            return _isCallbackActive || !IsPreparationActive(token)
                ? CoCoContextCommitStatus.InvalidPreparation
                : CancelActive(token);
        }

        internal CoCoContextCommitStatus CancelFinalized(ulong token)
        {
            return _isCallbackActive || !IsFinalized(token)
                ? CoCoContextCommitStatus.InvalidPreparation
                : CancelActive(token);
        }

        private CoCoContextCommitStatus CancelActive(ulong token)
        {
            if (_reserved == null || token == 0UL || token != _activeToken)
            {
                return CoCoContextCommitStatus.InvalidPreparation;
            }

            _reserved.Abandon();
            _reserved = null;
            _activeToken = 0UL;
            _isFinalized = false;
            return CoCoContextCommitStatus.Cancelled;
        }

        private bool TryPrepareCore(
            CoCoTickFrame tickFrame,
            CoCoContextFrameOrigin origin,
            CoCoContextFrame source,
            out CoCoPreparedContextCommit prepared,
            out CoCoContextCommitStatus status)
        {
            if (!tickFrame.IsValid)
            {
                prepared = default;
                status = CoCoContextCommitStatus.InvalidTick;
                return false;
            }

            if (!origin.IsValid)
            {
                prepared = default;
                status = CoCoContextCommitStatus.InvalidOrigin;
                return false;
            }

            if (HasCurrent &&
                !CoCoStateFlowTickOrder.IsStrictlyAfter(
                    tickFrame,
                    _current.Header.TickFrame))
            {
                prepared = default;
                status = CoCoContextCommitStatus.InvalidTick;
                return false;
            }

            if (_reserved != null)
            {
                prepared = default;
                status = CoCoContextCommitStatus.PreparationAlreadyActive;
                return false;
            }

            if (_nextToken == ulong.MaxValue)
            {
                prepared = default;
                status = CoCoContextCommitStatus.RevisionExhausted;
                return false;
            }

            if (HasCurrent && _current.Revision.Value == ulong.MaxValue)
            {
                prepared = default;
                status = CoCoContextCommitStatus.RevisionExhausted;
                return false;
            }

            CoCoContextFrameStorage candidate = FindAvailableFrame();
            if (candidate == null)
            {
                prepared = default;
                status = CoCoContextCommitStatus.CapacityExhausted;
                return false;
            }

            if (!CoCoStateFlowFrameHeader.TryCreate(
                    _graphInstanceId,
                    Layout,
                    CoCoStateFlowFrameKind.Context,
                    tickFrame,
                    out CoCoStateFlowFrameHeader header))
            {
                prepared = default;
                status = CoCoContextCommitStatus.InvalidTick;
                return false;
            }

            ulong revisionValue = !HasCurrent ? 1UL : _current.Revision.Value + 1UL;
            bool didPrepare;
            _isCallbackActive = true;
            try
            {
                didPrepare = candidate.TryPrepare(
                    header,
                    new CoCoContextRevision(revisionValue),
                    origin,
                    source,
                    out status);
            }
            catch
            {
                _isCallbackActive = false;
                candidate.Abandon();
                throw;
            }

            _isCallbackActive = false;

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
            prepared = new CoCoPreparedContextCommit(this, _activeToken);
            status = CoCoContextCommitStatus.None;
            return true;
        }

        private CoCoContextFrameStorage FindAvailableFrame()
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
    }

    internal static class CoCoStateFlowTickOrder
    {
        public static bool IsStrictlyAfter(
            in CoCoTickFrame candidate,
            in CoCoTickFrame previous)
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

    internal static class CoCoStateFlowBinary
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

    internal static class CoCoStateFlowSchemaHash
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(
            CoCoStateBlockDescriptor[] blocks,
            CoCoStateSlotDescriptor[] slots)
        {
            ulong hash = OffsetBasis;
            AddUInt32(ref hash, (uint)blocks.Length);
            for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                CoCoStateBlockDescriptor block = blocks[blockIndex];
                AddUInt64(ref hash, block.BlockId.High);
                AddUInt64(ref hash, block.BlockId.Low);
                AddUInt32(ref hash, (uint)block.Owner);
                AddUInt32(ref hash, (uint)block.DenseIndex);
            }

            AddUInt32(ref hash, (uint)slots.Length);
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                CoCoStateSlotDescriptor slot = slots[slotIndex];
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
                    CoCoStateSlotId dependency = slot.DerivedDependencyArray[dependencyIndex];
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
            string typeName = valueType?.FullName ?? string.Empty;
            string assemblyName = valueType?.Assembly.GetName().Name ?? string.Empty;
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

    internal static class CoCoStateFlowTypeRules
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
