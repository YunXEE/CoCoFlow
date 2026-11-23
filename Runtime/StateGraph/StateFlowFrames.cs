using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public enum StateFlowFrameKind
    {
        None = 0,
        Intent = 1,
        Operation = 2,
        Context = 3
    }

    public readonly struct StateFlowFrameIdentity : IEquatable<StateFlowFrameIdentity>
    {
        public StateFlowFrameIdentity(
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
            StateFlowFrameKind kind)
        {
            GraphInstanceId = graphInstanceId;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            ExecutionSequence = executionSequence;
            Kind = kind;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TimelineEpoch TimelineEpoch { get; }
        public TimelineTick Tick { get; }
        public ExecutionSequence ExecutionSequence { get; }
        public StateFlowFrameKind Kind { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               Kind >= StateFlowFrameKind.Intent &&
                               Kind <= StateFlowFrameKind.Context;

        public bool Equals(StateFlowFrameIdentity other)
        {
            return GraphInstanceId == other.GraphInstanceId &&
                   TimelineEpoch == other.TimelineEpoch &&
                   Tick == other.Tick &&
                   ExecutionSequence == other.ExecutionSequence &&
                   Kind == other.Kind;
        }

        public override bool Equals(object obj) => obj is StateFlowFrameIdentity other && Equals(other);

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

        public static bool operator ==(StateFlowFrameIdentity left, StateFlowFrameIdentity right) =>
            left.Equals(right);

        public static bool operator !=(StateFlowFrameIdentity left, StateFlowFrameIdentity right) =>
            !left.Equals(right);
    }

    public readonly struct StateFlowFrameHeader : IEquatable<StateFlowFrameHeader>
    {
        private StateFlowFrameHeader(
            StateFlowFrameIdentity identity,
            FrameLayoutId layoutId,
            uint layoutVersion,
            ulong layoutSchemaHash,
            TickFrame tickFrame)
        {
            Identity = identity;
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            LayoutSchemaHash = layoutSchemaHash;
            TickFrame = tickFrame;
        }

        public StateFlowFrameIdentity Identity { get; }
        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public TickFrame TickFrame { get; }
        public bool IsValid => Identity.IsValid && LayoutId.IsValid && TickFrame.IsValid;
        public bool HasExactLayoutIdentity => LayoutVersion > 0U && LayoutSchemaHash != 0UL;

        public static bool TryCreate(
            GraphInstanceId graphInstanceId,
            FrameLayoutId layoutId,
            StateFlowFrameKind kind,
            TickFrame tickFrame,
            out StateFlowFrameHeader header)
        {
            var identity = new StateFlowFrameIdentity(
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

            header = new StateFlowFrameHeader(identity, layoutId, 0U, 0UL, tickFrame);
            return true;
        }

        public static bool TryCreate(
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            StateFlowFrameKind kind,
            TickFrame tickFrame,
            out StateFlowFrameHeader header)
        {
            if (layout == null ||
                !TryCreate(graphInstanceId, layout.LayoutId, kind, tickFrame, out StateFlowFrameHeader basic))
            {
                header = default;
                return false;
            }

            header = new StateFlowFrameHeader(
                basic.Identity,
                layout.LayoutId,
                layout.Version,
                layout.SchemaHash,
                tickFrame);
            return true;
        }

        public bool Equals(StateFlowFrameHeader other)
        {
            return Identity == other.Identity &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   TickFrame == other.TickFrame;
        }

        public override bool Equals(object obj) => obj is StateFlowFrameHeader other && Equals(other);
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

        public static bool operator ==(StateFlowFrameHeader left, StateFlowFrameHeader right) =>
            left.Equals(right);

        public static bool operator !=(StateFlowFrameHeader left, StateFlowFrameHeader right) =>
            !left.Equals(right);
    }

    public readonly struct ContextRevision : IEquatable<ContextRevision>
    {
        public ContextRevision(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public bool Equals(ContextRevision other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ContextRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(ContextRevision left, ContextRevision right) => left.Equals(right);
        public static bool operator !=(ContextRevision left, ContextRevision right) => !left.Equals(right);
    }

    public enum StateBlockOwner
    {
        None = 0,
        Graph = 1,
        Operator = 2,
        Actor = 3
    }

    public readonly struct StateBlockHandle : IEquatable<StateBlockHandle>
    {
        private readonly ContextFrameLayout _layout;

        internal StateBlockHandle(
            ContextFrameLayout layout,
            StateBlockId blockId,
            int denseIndex,
            StateBlockOwner owner)
        {
            _layout = layout;
            LayoutId = layout.LayoutId;
            LayoutVersion = layout.Version;
            LayoutSchemaHash = layout.SchemaHash;
            BlockId = blockId;
            DenseIndex = denseIndex;
            Owner = owner;
        }

        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public StateBlockId BlockId { get; }
        public int DenseIndex { get; }
        public StateBlockOwner Owner { get; }
        public bool IsValid => _layout != null && LayoutId.IsValid && LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL && BlockId.IsValid && DenseIndex >= 0 &&
                               Owner >= StateBlockOwner.Graph && Owner <= StateBlockOwner.Actor;

        internal bool IsFor(ContextFrameLayout layout) =>
            ReferenceEquals(_layout, layout) &&
            layout != null &&
            LayoutId == layout.LayoutId &&
            LayoutVersion == layout.Version &&
            LayoutSchemaHash == layout.SchemaHash;

        public bool Equals(StateBlockHandle other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   BlockId == other.BlockId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) => obj is StateBlockHandle other && Equals(other);
        public override int GetHashCode() => unchecked((LayoutId.GetHashCode() * 397) ^ DenseIndex);

        public static bool operator ==(StateBlockHandle left, StateBlockHandle right) =>
            left.Equals(right);

        public static bool operator !=(StateBlockHandle left, StateBlockHandle right) =>
            !left.Equals(right);
    }

    [Flags]
    public enum ContextProjection
    {
        None = 0,
        Temporal = 1 << 0,
        Durable = 1 << 1
    }

    public enum ContextRestorePolicy
    {
        None = 0,
        Stored = 1,
        ResetToDefault = 2,
        Derived = 3
    }

    public enum ContextFrameOriginKind
    {
        None = 0,
        Commit = 1,
        Restore = 2
    }

    public readonly struct ContextFrameOrigin : IEquatable<ContextFrameOrigin>
    {
        private ContextFrameOrigin(
            ContextFrameOriginKind kind,
            GraphInstanceId sourceGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            ContextRevision sourceRevision)
        {
            Kind = kind;
            SourceGraphInstanceId = sourceGraphInstanceId;
            SourceTimelineEpoch = sourceTimelineEpoch;
            SourceTick = sourceTick;
            SourceRevision = sourceRevision;
        }

        public ContextFrameOriginKind Kind { get; }
        public GraphInstanceId SourceGraphInstanceId { get; }
        public TimelineEpoch SourceTimelineEpoch { get; }
        public TimelineTick SourceTick { get; }
        public ContextRevision SourceRevision { get; }
        public bool IsRestore => Kind == ContextFrameOriginKind.Restore;
        public bool IsValid => Kind == ContextFrameOriginKind.Commit ||
                               (Kind == ContextFrameOriginKind.Restore &&
                                SourceGraphInstanceId.IsValid &&
                                SourceRevision.IsValid);

        public static ContextFrameOrigin Commit() =>
            new ContextFrameOrigin(ContextFrameOriginKind.Commit, default, default, default, default);

        public static ContextFrameOrigin RestoreFrom(ContextFrame source)
        {
            if (!source.IsAlive)
            {
                return default;
            }

            return new ContextFrameOrigin(
                ContextFrameOriginKind.Restore,
                source.Header.Identity.GraphInstanceId,
                source.Header.Identity.TimelineEpoch,
                source.Header.Identity.Tick,
                source.Revision);
        }

        internal static ContextFrameOrigin RestoreFrom(
            GraphInstanceId sourceGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            ContextRevision sourceRevision)
        {
            var origin = new ContextFrameOrigin(
                ContextFrameOriginKind.Restore,
                sourceGraphInstanceId,
                sourceTimelineEpoch,
                sourceTick,
                sourceRevision);
            return origin.IsValid ? origin : default;
        }

        public bool Equals(ContextFrameOrigin other)
        {
            return Kind == other.Kind &&
                   SourceGraphInstanceId == other.SourceGraphInstanceId &&
                   SourceTimelineEpoch == other.SourceTimelineEpoch &&
                   SourceTick == other.SourceTick &&
                   SourceRevision == other.SourceRevision;
        }

        public override bool Equals(object obj) => obj is ContextFrameOrigin other && Equals(other);
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ SourceRevision.GetHashCode());
    }

    public readonly struct CodecDescriptor : IEquatable<CodecDescriptor>
    {
        public CodecDescriptor(CodecId codecId, uint version)
        {
            CodecId = codecId;
            Version = version;
        }

        public CodecId CodecId { get; }
        public uint Version { get; }
        public bool UsesCustomCodec => CodecId.IsValid;
        public bool IsValid => UsesCustomCodec ? Version > 0U : Version == 0U;

        public bool Equals(CodecDescriptor other) => CodecId == other.CodecId && Version == other.Version;
        public override bool Equals(object obj) => obj is CodecDescriptor other && Equals(other);
        public override int GetHashCode() => unchecked((CodecId.GetHashCode() * 397) ^ (int)Version);
    }

    public interface IContextValueCodec<TValue>
        where TValue : unmanaged
    {
        CodecDescriptor Descriptor { get; }
        int MaxEncodedSize { get; }
        bool TryEncode(in TValue value, Span<byte> destination, out int bytesWritten);
        bool TryDecode(ReadOnlySpan<byte> source, out TValue value, out int bytesRead);
    }

    internal interface IContextValueCodecAdapter
    {
        CodecDescriptor Descriptor { get; }
        Type ValueType { get; }
        ulong ValueTypeHash { get; }
        int MaxEncodedSize { get; }
        object UntypedCodec { get; }
        bool TryEncode(byte[] source, int sourceOffset, Span<byte> destination, out int bytesWritten);
        bool TryDecode(ReadOnlySpan<byte> source, byte[] destination, int destinationOffset, out int bytesRead);
    }

    internal sealed class ContextValueCodecAdapter<TValue> : IContextValueCodecAdapter
        where TValue : unmanaged
    {
        private readonly IContextValueCodec<TValue> _codec;

        public ContextValueCodecAdapter(IContextValueCodec<TValue> codec)
        {
            _codec = codec;
        }

        public CodecDescriptor Descriptor => _codec.Descriptor;
        public Type ValueType => typeof(TValue);
        public ulong ValueTypeHash => StateFlowSchemaHash.HashType(typeof(TValue));
        public int MaxEncodedSize => _codec.MaxEncodedSize;
        public object UntypedCodec => _codec;

        public bool TryEncode(
            byte[] source,
            int sourceOffset,
            Span<byte> destination,
            out int bytesWritten)
        {
            TValue value = StateFlowTypeRules.Read<TValue>(source, sourceOffset);
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

            StateFlowTypeRules.Write(destination, destinationOffset, value);
            return true;
        }
    }

    public sealed class ContextCodecRegistry
    {
        private readonly Dictionary<CodecBindingKey, IContextValueCodecAdapter> _bindings =
            new Dictionary<CodecBindingKey, IContextValueCodecAdapter>();
        private readonly HashSet<CodecId> _codecIds = new HashSet<CodecId>();
        private readonly HashSet<CodecVersionKey> _codecVersions = new HashSet<CodecVersionKey>();
        private bool _frozen;

        public bool IsFrozen => _frozen;
        public int Count => _bindings.Count;

        public bool TryRegister<TValue>(
            IContextValueCodec<TValue> codec,
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
                !StateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)))
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

            _bindings.Add(key, new ContextValueCodecAdapter<TValue>(codec));
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
            CodecDescriptor descriptor,
            out IContextValueCodec<TValue> codec,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            if (!TryResolve(descriptor, typeof(TValue), out IContextValueCodecAdapter adapter, out diagnosticCode))
            {
                codec = null;
                return false;
            }

            codec = (IContextValueCodec<TValue>)adapter.UntypedCodec;
            return true;
        }

        internal bool TryResolve(
            CodecDescriptor descriptor,
            Type valueType,
            out IContextValueCodecAdapter adapter,
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
            CodecDescriptor descriptor,
            Type valueType)
        {
            return TryResolve(descriptor, valueType, out _, out CoCoDiagnosticCode diagnosticCode)
                ? CoCoDiagnosticCode.None
                : diagnosticCode;
        }

        private readonly struct CodecVersionKey : IEquatable<CodecVersionKey>
        {
            public CodecVersionKey(CodecDescriptor descriptor)
            {
                CodecId = descriptor.CodecId;
                Version = descriptor.Version;
            }

            public CodecId CodecId { get; }
            public uint Version { get; }

            public bool Equals(CodecVersionKey other) =>
                CodecId == other.CodecId && Version == other.Version;

            public override bool Equals(object obj) => obj is CodecVersionKey other && Equals(other);
            public override int GetHashCode() => unchecked((CodecId.GetHashCode() * 397) ^ (int)Version);
        }

        private readonly struct CodecBindingKey : IEquatable<CodecBindingKey>
        {
            public CodecBindingKey(CodecDescriptor descriptor, Type valueType)
            {
                Descriptor = descriptor;
                ValueType = valueType;
            }

            public CodecDescriptor Descriptor { get; }
            public Type ValueType { get; }

            public bool Equals(CodecBindingKey other) =>
                Descriptor.Equals(other.Descriptor) && ValueType == other.ValueType;

            public override bool Equals(object obj) => obj is CodecBindingKey other && Equals(other);

            public override int GetHashCode() =>
                unchecked((Descriptor.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));
        }
    }

    public interface IDerivedStateRebuilder<TValue>
        where TValue : unmanaged
    {
        bool TryRebuild(in DerivedStateReadContext context, out TValue value);
    }

    public readonly struct DerivedStateReadContext
    {
        private readonly ContextFrameLayout _layout;
        private readonly byte[] _buffer;
        private readonly StateSlotId[] _declaredDependencies;

        internal DerivedStateReadContext(
            ContextFrameLayout layout,
            byte[] buffer,
            StateSlotId[] declaredDependencies)
        {
            _layout = layout;
            _buffer = buffer;
            _declaredDependencies = declaredDependencies;
        }

        public bool TryRead<TValue>(StateSlotId slotId, out TValue value)
            where TValue : unmanaged
        {
            if (_layout == null || _buffer == null || !IsDeclaredDependency(slotId) ||
                !_layout.TryResolveSlot(slotId, out StateSlot<TValue> slot))
            {
                value = default;
                return false;
            }

            value = StateFlowTypeRules.Read<TValue>(_buffer, slot.ByteOffset);
            return true;
        }

        private bool IsDeclaredDependency(StateSlotId slotId)
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

    internal interface IDerivedStateRebuilderAdapter
    {
        Type ValueType { get; }
        bool TryRebuild(
            ContextFrameLayout layout,
            byte[] buffer,
            StateSlotDescriptor descriptor);
    }

    internal sealed class DerivedStateRebuilderAdapter<TValue> : IDerivedStateRebuilderAdapter
        where TValue : unmanaged
    {
        private readonly IDerivedStateRebuilder<TValue> _rebuilder;

        public DerivedStateRebuilderAdapter(IDerivedStateRebuilder<TValue> rebuilder)
        {
            _rebuilder = rebuilder;
        }

        public Type ValueType => typeof(TValue);

        public bool TryRebuild(
            ContextFrameLayout layout,
            byte[] buffer,
            StateSlotDescriptor descriptor)
        {
            var context = new DerivedStateReadContext(
                layout,
                buffer,
                descriptor.DerivedDependencyArray);
            if (!_rebuilder.TryRebuild(in context, out TValue value))
            {
                return false;
            }

            StateFlowTypeRules.Write(buffer, descriptor.ByteOffset, value);
            return true;
        }
    }

    public readonly struct StateSlot<TValue> : IEquatable<StateSlot<TValue>>
        where TValue : unmanaged
    {
        private readonly ContextFrameLayout _layout;

        internal StateSlot(
            ContextFrameLayout layout,
            StateSlotId slotId,
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

        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public StateSlotId SlotId { get; }
        public int DenseIndex { get; }
        internal int ByteOffset { get; }
        internal int ByteSize { get; }
        public bool IsValid => _layout != null && LayoutId.IsValid && LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL && SlotId.IsValid && DenseIndex >= 0 &&
                               ByteOffset >= 0 && ByteSize > 0;

        internal bool IsFor(ContextFrameLayout layout) =>
            ReferenceEquals(_layout, layout) &&
            layout != null &&
            LayoutId == layout.LayoutId &&
            LayoutVersion == layout.Version &&
            LayoutSchemaHash == layout.SchemaHash;

        public bool Equals(StateSlot<TValue> other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   SlotId == other.SlotId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) => obj is StateSlot<TValue> other && Equals(other);
        public override int GetHashCode() => unchecked((LayoutId.GetHashCode() * 397) ^ DenseIndex);
    }

    public sealed class StateSlotDescriptor
    {
        private readonly StateSlotId[] _derivedDependencies;

        internal StateSlotDescriptor(
            StateSlotId slotId,
            StateBlockId writerBlockId,
            Type valueType,
            int denseIndex,
            int byteOffset,
            int byteSize,
            ContextProjection projection,
            ContextRestorePolicy restorePolicy,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
            IDerivedStateRebuilderAdapter derivedRebuilder,
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
            _derivedDependencies = (StateSlotId[])derivedDependencies.Clone();
            DerivedDependencies = Array.AsReadOnly(_derivedDependencies);
            DerivedRebuilder = derivedRebuilder;
            DefaultBytes = defaultBytes;
        }

        public StateSlotId SlotId { get; }
        public StateBlockId WriterBlockId { get; }
        public Type ValueType { get; }
        public int DenseIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public ContextProjection Projection { get; }
        public ContextRestorePolicy RestorePolicy { get; }
        public CodecDescriptor Codec { get; }
        public IReadOnlyList<StateSlotId> DerivedDependencies { get; }
        internal StateSlotId[] DerivedDependencyArray => _derivedDependencies;
        internal IDerivedStateRebuilderAdapter DerivedRebuilder { get; }
        internal byte[] DefaultBytes { get; }
    }

    public sealed class StateBlockDescriptor
    {
        internal StateBlockDescriptor(
            StateBlockId blockId,
            StateBlockOwner owner,
            int denseIndex,
            StateSlotDescriptor[] slots)
        {
            BlockId = blockId;
            Owner = owner;
            DenseIndex = denseIndex;
            Slots = Array.AsReadOnly(slots);
        }

        public StateBlockId BlockId { get; }
        public StateBlockOwner Owner { get; }
        public int DenseIndex { get; }
        public IReadOnlyList<StateSlotDescriptor> Slots { get; }
    }

    public sealed class ContextFrameLayoutBuilder
    {
        private readonly List<BlockRegistration> _blocks = new List<BlockRegistration>();
        private readonly Dictionary<StateBlockId, BlockRegistration> _blocksById =
            new Dictionary<StateBlockId, BlockRegistration>();
        private readonly Dictionary<StateSlotId, SlotRegistration> _slotsById =
            new Dictionary<StateSlotId, SlotRegistration>();
        private bool _frozen;

        public bool TryAddBlock(
            StateBlockId blockId,
            StateBlockOwner owner,
            out CoCoDiagnosticCode diagnosticCode)
        {
            if (_frozen)
            {
                diagnosticCode = CoCoDiagnosticCode.RegistryFrozen;
                return false;
            }

            if (!blockId.IsValid || owner == StateBlockOwner.None || !Enum.IsDefined(typeof(StateBlockOwner), owner))
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
            StateBlockId blockId,
            StateSlotId slotId,
            ContextProjection projection,
            ContextRestorePolicy restorePolicy,
            TValue defaultValue,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
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
            StateBlockId blockId,
            StateSlotId slotId,
            ContextProjection projection,
            TValue defaultValue,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
            IDerivedStateRebuilder<TValue> rebuilder,
            out CoCoDiagnosticCode diagnosticCode)
            where TValue : unmanaged
        {
            IDerivedStateRebuilderAdapter adapter = rebuilder == null
                ? null
                : new DerivedStateRebuilderAdapter<TValue>(rebuilder);
            return TryAddSlotCore(
                blockId,
                slotId,
                projection,
                ContextRestorePolicy.Derived,
                defaultValue,
                codec,
                derivedDependencies,
                adapter,
                out diagnosticCode);
        }

        private bool TryAddSlotCore<TValue>(
            StateBlockId blockId,
            StateSlotId slotId,
            ContextProjection projection,
            ContextRestorePolicy restorePolicy,
            TValue defaultValue,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
            IDerivedStateRebuilderAdapter derivedRebuilder,
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

            const ContextProjection knownProjection =
                ContextProjection.Temporal | ContextProjection.Durable;
            if (!StateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)) ||
                !codec.IsValid ||
                (projection & ~knownProjection) != 0 ||
                restorePolicy == ContextRestorePolicy.None ||
                !Enum.IsDefined(typeof(ContextRestorePolicy), restorePolicy))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidStateSlot;
                return false;
            }

            StateSlotId[] dependencies = derivedDependencies == null
                ? Array.Empty<StateSlotId>()
                : (StateSlotId[])derivedDependencies.Clone();
            if (restorePolicy == ContextRestorePolicy.Derived &&
                (dependencies.Length == 0 || derivedRebuilder == null || derivedRebuilder.ValueType != typeof(TValue)))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            if (restorePolicy != ContextRestorePolicy.Derived &&
                (dependencies.Length != 0 || derivedRebuilder != null))
            {
                diagnosticCode = CoCoDiagnosticCode.InvalidRestoreMetadata;
                return false;
            }

            int byteSize = StateFlowTypeRules.SizeOf<TValue>();
            var defaultBytes = new byte[byteSize];
            StateFlowTypeRules.Write(defaultBytes, 0, defaultValue);
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
            FrameLayoutId layoutId,
            uint version,
            out ContextFrameLayout layout,
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

            var slots = new StateSlotDescriptor[_slotsById.Count];
            var blocks = new StateBlockDescriptor[_blocks.Count];
            int offset = 0;
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                var blockSlots = new StateSlotDescriptor[block.Slots.Count];
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
                    var descriptor = new StateSlotDescriptor(
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

                blocks[blockIndex] = new StateBlockDescriptor(
                    block.BlockId,
                    block.Owner,
                    blockIndex,
                    blockSlots);
            }

            var defaultBuffer = new byte[offset];
            for (int index = 0; index < slots.Length; index++)
            {
                StateSlotDescriptor slot = slots[index];
                Buffer.BlockCopy(slot.DefaultBytes, 0, defaultBuffer, slot.ByteOffset, slot.ByteSize);
            }

            ulong schemaHash = StateFlowSchemaHash.Compute(blocks, slots);
            _frozen = true;
            layout = new ContextFrameLayout(
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
                        StateSlotId dependencyId = slot.DerivedDependencies[dependencyIndex];
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

            var states = new Dictionary<StateSlotId, byte>();
            var ordered = new List<int>();
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                BlockRegistration block = _blocks[blockIndex];
                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    SlotRegistration slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy == ContextRestorePolicy.Derived &&
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
                    if (slot.RestorePolicy != ContextRestorePolicy.Derived)
                    {
                        continue;
                    }

                    for (int dependencyIndex = 0;
                         dependencyIndex < slot.DerivedDependencies.Length;
                         dependencyIndex++)
                    {
                        SlotRegistration dependency = _slotsById[slot.DerivedDependencies[dependencyIndex]];
                        if (dependency.RestorePolicy == ContextRestorePolicy.ResetToDefault)
                        {
                            continue;
                        }

                        if (((slot.Projection & ContextProjection.Temporal) != 0 &&
                             (dependency.Projection & ContextProjection.Temporal) == 0) ||
                            ((slot.Projection & ContextProjection.Durable) != 0 &&
                             (dependency.Projection & ContextProjection.Durable) == 0))
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
            Dictionary<StateSlotId, byte> states,
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
                if (dependency.RestorePolicy == ContextRestorePolicy.Derived)
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
            public BlockRegistration(StateBlockId blockId, StateBlockOwner owner)
            {
                BlockId = blockId;
                Owner = owner;
            }

            public StateBlockId BlockId { get; }
            public StateBlockOwner Owner { get; }
            public List<SlotRegistration> Slots { get; } = new List<SlotRegistration>();
        }

        private sealed class SlotRegistration
        {
            public SlotRegistration(
                StateSlotId slotId,
                StateBlockId writerBlockId,
                Type valueType,
                int byteSize,
                ContextProjection projection,
                ContextRestorePolicy restorePolicy,
                CodecDescriptor codec,
                StateSlotId[] derivedDependencies,
                IDerivedStateRebuilderAdapter derivedRebuilder,
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

            public StateSlotId SlotId { get; }
            public StateBlockId WriterBlockId { get; }
            public Type ValueType { get; }
            public int ByteSize { get; }
            public ContextProjection Projection { get; }
            public ContextRestorePolicy RestorePolicy { get; }
            public CodecDescriptor Codec { get; }
            public StateSlotId[] DerivedDependencies { get; }
            public IDerivedStateRebuilderAdapter DerivedRebuilder { get; }
            public byte[] DefaultBytes { get; }
            public int DenseIndex { get; set; }
        }
    }

    public sealed class ContextFrameLayout
    {
        private readonly Dictionary<StateBlockId, StateBlockDescriptor> _blocksById;
        private readonly Dictionary<StateSlotId, StateSlotDescriptor> _slotsById;
        private readonly StateBlockDescriptor[] _blocks;
        private readonly StateSlotDescriptor[] _slots;
        private readonly byte[] _defaultBuffer;
        private readonly int[] _derivedOrder;

        internal ContextFrameLayout(
            FrameLayoutId layoutId,
            uint version,
            ulong schemaHash,
            StateBlockDescriptor[] blocks,
            StateSlotDescriptor[] slots,
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
            _blocksById = new Dictionary<StateBlockId, StateBlockDescriptor>(blocks.Length);
            for (int index = 0; index < blocks.Length; index++)
            {
                _blocksById.Add(blocks[index].BlockId, blocks[index]);
            }

            _slotsById = new Dictionary<StateSlotId, StateSlotDescriptor>(slots.Length);
            for (int index = 0; index < slots.Length; index++)
            {
                _slotsById.Add(slots[index].SlotId, slots[index]);
            }
        }

        public FrameLayoutId LayoutId { get; }
        public uint Version { get; }
        public ulong SchemaHash { get; }
        public IReadOnlyList<StateBlockDescriptor> Blocks { get; }
        public IReadOnlyList<StateSlotDescriptor> Slots { get; }
        public IReadOnlyList<int> DerivedOrder { get; }
        public int ByteSize => _defaultBuffer.Length;

        public bool TryResolveBlock(StateBlockId blockId, out StateBlockHandle block)
        {
            if (_blocksById.TryGetValue(blockId, out StateBlockDescriptor descriptor))
            {
                block = new StateBlockHandle(
                    this,
                    descriptor.BlockId,
                    descriptor.DenseIndex,
                    descriptor.Owner);
                return true;
            }

            block = default;
            return false;
        }

        public bool TryResolveSlot<TValue>(StateSlotId slotId, out StateSlot<TValue> slot)
            where TValue : unmanaged
        {
            if (_slotsById.TryGetValue(slotId, out StateSlotDescriptor descriptor) &&
                descriptor.ValueType == typeof(TValue))
            {
                slot = new StateSlot<TValue>(
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

        internal bool IsWriterFor(StateBlockHandle block, int slotDenseIndex)
        {
            return block.IsValid && block.IsFor(this) &&
                   block.DenseIndex >= 0 && block.DenseIndex < _blocks.Length &&
                   slotDenseIndex >= 0 && slotDenseIndex < _slots.Length &&
                   _blocks[block.DenseIndex].BlockId == block.BlockId &&
                   _slots[slotDenseIndex].WriterBlockId == block.BlockId &&
                   _slots[slotDenseIndex].RestorePolicy != ContextRestorePolicy.Derived;
        }

        internal void CopyDefaultsTo(byte[] destination)
        {
            System.Buffer.BlockCopy(_defaultBuffer, 0, destination, 0, _defaultBuffer.Length);
        }

        internal void CopyRetainedSlots(IReadOnlyList<StateSlotDescriptor> slots,
            byte[] authority, byte[] destination)
        {
            if (slots == null) return;
            foreach (StateSlotDescriptor slot in slots)
                System.Buffer.BlockCopy(authority ?? _defaultBuffer, slot.ByteOffset,
                    destination, slot.ByteOffset, slot.ByteSize);
        }

        internal TValue ReadDefault<TValue>(StateSlot<TValue> slot)
            where TValue : unmanaged
        {
            if (!slot.IsValid || !slot.IsFor(this))
            {
                throw new InvalidOperationException("The StateSlot handle is not valid for this ContextFrame layout.");
            }

            return StateFlowTypeRules.Read<TValue>(_defaultBuffer, slot.ByteOffset);
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
                StateSlotDescriptor slot = _slots[index];
                if (slot.RestorePolicy == ContextRestorePolicy.Stored)
                {
                    Buffer.BlockCopy(
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
                StateSlotDescriptor slot = _slots[_derivedOrder[index]];
                if (slot.DerivedRebuilder == null ||
                    !slot.DerivedRebuilder.TryRebuild(this, destination, slot))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool HasProjectionDependencyClosure(ContextProjection projection)
        {
            if (projection != ContextProjection.Temporal &&
                projection != ContextProjection.Durable)
            {
                return false;
            }

            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                StateSlotDescriptor slot = _slots[slotIndex];
                if (slot.RestorePolicy != ContextRestorePolicy.Derived ||
                    (slot.Projection & projection) == 0)
                {
                    continue;
                }

                StateSlotId[] dependencies = slot.DerivedDependencyArray;
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
                {
                    if (!_slotsById.TryGetValue(
                            dependencies[dependencyIndex],
                            out StateSlotDescriptor dependency) ||
                        (dependency.RestorePolicy != ContextRestorePolicy.ResetToDefault &&
                         (dependency.Projection & projection) == 0))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal bool HasExactIdentity(
            FrameLayoutId layoutId,
            uint version,
            ulong schemaHash)
        {
            return LayoutId == layoutId && Version == version && SchemaHash == schemaHash;
        }

        internal bool IsSameInstance(ContextFrameLayout other) => ReferenceEquals(this, other);
    }

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

    public interface IContextFrame
    {
        StateFlowFrameHeader Header { get; }
        ContextRevision Revision { get; }
        ContextFrameOrigin Origin { get; }
        T Read<T>(StateSlot<T> slot) where T : unmanaged;
    }

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

    /// <summary>
    /// Read-only view over either a committed ContextFrame or the immutable defaults of a layout.
    /// The default-backed form deliberately has no fabricated Tick or Revision identity.
    /// </summary>
    public readonly struct ContextFrameReadView : IContextFrame, IEquatable<ContextFrameReadView>
    {
        private readonly ContextFrameLayout _layout;
        private readonly ContextFrame _frame;
        private readonly bool _hasCommittedFrame;

        internal ContextFrameReadView(ContextFrameLayout layout)
        {
            _layout = layout;
            _frame = default;
            _hasCommittedFrame = false;
        }

        internal ContextFrameReadView(ContextFrame frame)
        {
            _layout = frame.Layout;
            _frame = frame;
            _hasCommittedFrame = frame.IsAlive;
        }

        public StateFlowFrameHeader Header => HasCommittedFrame ? _frame.Header : default;
        public ContextRevision Revision => HasCommittedFrame ? _frame.Revision : default;
        public ContextFrameOrigin Origin => HasCommittedFrame ? _frame.Origin : default;
        public ContextFrameLayout Layout => IsValid ? _layout : null;
        public bool HasCommittedFrame => _hasCommittedFrame && _frame.IsAlive;
        public bool IsValid => _layout != null && (!_hasCommittedFrame || _frame.IsAlive);

        public static bool TryCreate(ContextFrame frame, out ContextFrameReadView view)
        {
            if (!frame.IsAlive)
            {
                view = default;
                return false;
            }

            view = new ContextFrameReadView(frame);
            return true;
        }

        public TValue Read<TValue>(StateSlot<TValue> slot)
            where TValue : unmanaged
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("The ContextFrame read view is no longer valid.");
            }

            return HasCommittedFrame ? _frame.Read(slot) : _layout.ReadDefault(slot);
        }

        public bool Equals(ContextFrameReadView other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   _frame == other._frame &&
                   _hasCommittedFrame == other._hasCommittedFrame;
        }

        public override bool Equals(object obj) => obj is ContextFrameReadView other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((_layout?.GetHashCode() ?? 0) * 397) ^ _frame.GetHashCode());

        public static bool operator ==(ContextFrameReadView left, ContextFrameReadView right) =>
            left.Equals(right);

        public static bool operator !=(ContextFrameReadView left, ContextFrameReadView right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Immutable generation-scoped handle to one committed ContextFrame.
    /// Reusing the underlying arena cell never makes an expired handle valid again.
    /// </summary>
    public readonly struct ContextFrame : IContextFrame, IEquatable<ContextFrame>
    {
        private readonly ContextFrameStorage _storage;
        private readonly ulong _generation;

        internal ContextFrame(ContextFrameStorage storage, ulong generation)
        {
            _storage = storage;
            _generation = generation;
        }

        public StateFlowFrameHeader Header => IsAlive ? _storage.Header : default;
        public ContextRevision Revision => IsAlive ? _storage.Revision : default;
        public ContextFrameOrigin Origin => IsAlive ? _storage.Origin : default;
        public ContextFrameLayout Layout => IsAlive ? _storage.Layout : null;
        public bool IsAlive => _storage != null && _storage.IsAlive(_generation);

        public T Read<T>(StateSlot<T> slot)
            where T : unmanaged
        {
            if (!IsAlive || !slot.IsValid || !slot.IsFor(_storage.Layout))
            {
                throw new InvalidOperationException("The ContextFrame or StateSlot handle is not valid for this read.");
            }

            return StateFlowTypeRules.Read<T>(_storage.Buffer, slot.ByteOffset);
        }

        public bool Retain() => _storage != null && _storage.TryRetain(_generation);
        public bool Release() => _storage != null && _storage.TryRelease(_generation);

        public bool Equals(ContextFrame other) =>
            ReferenceEquals(_storage, other._storage) && _generation == other._generation;

        public override bool Equals(object obj) => obj is ContextFrame other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((_storage?.GetHashCode() ?? 0) * 397) ^ _generation.GetHashCode());

        public static bool operator ==(ContextFrame left, ContextFrame right) => left.Equals(right);
        public static bool operator !=(ContextFrame left, ContextFrame right) => !left.Equals(right);

        internal byte[] Buffer => IsAlive ? _storage.Buffer : null;

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
    }

    internal sealed class ContextFrameStorage
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

    public enum ContextCommitStatus
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

    public readonly struct ContextCommitResult
    {
        internal ContextCommitResult(ContextCommitStatus status, ContextFrame frame)
        {
            Status = status;
            Frame = frame;
        }

        public ContextCommitStatus Status { get; }
        public ContextFrame Frame { get; }
        public bool Succeeded => Status == ContextCommitStatus.Succeeded;
    }

    public readonly struct ContextFrameWriter
    {
        private readonly ContextFrameArena _arena;
        private readonly ulong _token;
        private readonly StateBlockHandle _block;

        internal ContextFrameWriter(
            ContextFrameArena arena,
            ulong token,
            StateBlockHandle block)
        {
            _arena = arena;
            _token = token;
            _block = block;
        }

        public StateBlockHandle Block => _block;
        public bool IsValid => _arena != null && _arena.IsPreparationActive(_token) && _block.IsValid;

        public bool Write<TValue>(StateSlot<TValue> slot, in TValue value)
            where TValue : unmanaged
        {
            return IsValid && _arena.TryWrite(_token, _block, slot, value);
        }
    }

    public readonly struct PreparedContextCommit
    {
        private readonly ContextFrameArena _arena;
        private readonly ulong _token;

        internal PreparedContextCommit(ContextFrameArena arena, ulong token)
        {
            _arena = arena;
            _token = token;
        }

        public bool IsValid => _arena != null && _arena.IsPreparationActive(_token);

        public bool TryGetWriter(
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

        public bool TryFinalize(
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

        public ContextCommitStatus Cancel()
        {
            return _arena == null
                ? ContextCommitStatus.InvalidPreparation
                : _arena.CancelPrepared(_token);
        }
    }

    public readonly struct FinalizedContextCommit
    {
        private readonly ContextFrameArena _arena;
        private readonly ulong _token;

        internal FinalizedContextCommit(ContextFrameArena arena, ulong token)
        {
            _arena = arena;
            _token = token;
        }

        public bool IsValid => _arena != null && _arena.IsFinalized(_token);
        internal bool IsCommitReady => _arena != null && _arena.IsCommitReady(_token);

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

        public ContextCommitResult Commit()
        {
            return _arena == null
                ? new ContextCommitResult(ContextCommitStatus.InvalidPreparation, default)
                : _arena.Commit(_token);
        }

        public ContextCommitStatus Cancel()
        {
            return _arena == null
                ? ContextCommitStatus.InvalidPreparation
                : _arena.CancelFinalized(_token);
        }
    }

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

        public ContextFrameArena(
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

            // Keep a non-owning sentinel cell until the first authority swap so
            // the proof-only commit core never needs a first-commit null branch.
            _currentStorage = _frames[0];
        }

        public ContextFrameLayout Layout { get; }
        internal IReadOnlyList<StateSlotDescriptor> RetainedRestoreSlots { get; set; }
        public ContextFrame Current => _isDisposed ? default : _current;
        public ContextFrameReadView Previous => _isDisposed
            ? default
            : HasCurrent
                ? new ContextFrameReadView(_current)
                : new ContextFrameReadView(Layout);
        public bool HasCurrent => !_isDisposed && _current.IsAlive;
        internal bool HasAvailableCapacity => !_isDisposed && _reserved == null && FindAvailableFrame() != null;
        internal bool IsDisposed => _isDisposed;
        public int Capacity => _frames.Length;

        public bool TryPrepare(
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

        public bool TryPrepareRestore(
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

        public bool TryValidateRestore(
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
    }

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
