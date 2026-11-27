using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 目录与 Host 装配 Context 块、槽、默认值及恢复策略，实例按冻结布局读写帧。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>区分图、Operator 与 Host 在 Context 布局中各自拥有的数据块。</summary>
    public enum StateBlockOwner
    {
        None = 0,
        Graph = 1,
        Operator = 2,
        Host = 3
    }

    /// <summary>把某个数据块的稳定 ID 和所有者解析到具体的布局。</summary>
    public readonly struct StateBlockHandle : IEquatable<StateBlockHandle>
    {
        private readonly ContextFrameLayout _layout;

        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public StateBlockId BlockId { get; }
        public int DenseIndex { get; }
        public StateBlockOwner Owner { get; }
        public bool IsValid => _layout != null && LayoutId.IsValid && LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL && BlockId.IsValid && DenseIndex >= 0 &&
                               Owner >= StateBlockOwner.Graph && Owner <= StateBlockOwner.Host;

        #region Public API

        /// <summary>按<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>BlockId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateBlockHandle other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   BlockId == other.BlockId &&
                   DenseIndex == other.DenseIndex;
        }

        /// <summary>仅当对象属于同类型并且<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>BlockId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateBlockHandle other && Equals(other);
        /// <summary>为哈希集合计算与<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>BlockId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((LayoutId.GetHashCode() * 397) ^ DenseIndex);

        /// <summary>比较两端的<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>BlockId</c>，相等时返回 true。</summary>
        public static bool operator ==(StateBlockHandle left, StateBlockHandle right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>BlockId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(StateBlockHandle left, StateBlockHandle right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

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

        internal bool IsFor(ContextFrameLayout layout) =>
            ReferenceEquals(_layout, layout) &&
            layout != null &&
            LayoutId == layout.LayoutId &&
            LayoutVersion == layout.Version &&
            LayoutSchemaHash == layout.SchemaHash;

        #endregion
    }

    /// <summary>标记槽值会被哪个持久化或时间恢复投影捕获。</summary>
    [Flags]
    public enum ContextProjection
    {
        None = 0,
        Temporal = 1 << 0,
        Durable = 1 << 1
    }

    /// <summary>决定恢复时读取存储值、使用默认值还是运行派生重建器。</summary>
    public enum ContextRestorePolicy
    {
        None = 0,
        Stored = 1,
        ResetToDefault = 2,
        Derived = 3
    }

    /// <summary>标识槽值编解码合同及其格式版本。</summary>
    public readonly struct CodecDescriptor : IEquatable<CodecDescriptor>
    {
        /// <summary>记录编码器的稳定标识和版本，以便保存与恢复校验编码合同。</summary>
        public CodecDescriptor(CodecId codecId, uint version)
        {
            CodecId = codecId;
            Version = version;
        }

        public CodecId CodecId { get; }
        public uint Version { get; }
        public bool UsesCustomCodec => CodecId.IsValid;
        public bool IsValid => UsesCustomCodec ? Version > 0U : Version == 0U;

        /// <summary>按<c>CodecId</c>、<c>Version</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(CodecDescriptor other) => CodecId == other.CodecId && Version == other.Version;
        /// <summary>仅当对象属于同类型并且<c>CodecId</c>、<c>Version</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is CodecDescriptor other && Equals(other);
        /// <summary>为哈希集合计算与<c>CodecId</c>、<c>Version</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((CodecId.GetHashCode() * 397) ^ (int)Version);
    }

    /// <summary>在模块值与可存储的字节之间执行有界编解码。</summary>
    public interface IContextValueCodec<TValue>
        where TValue : unmanaged
    {
        CodecDescriptor Descriptor { get; }
        int MaxEncodedSize { get; }
        /// <summary>将槽值编码到提供的字节缓冲区，并给出实际长度。无法表示或缓冲区不足时返回 false。</summary>
        bool TryEncode(in TValue value, Span<byte> destination, out int bytesWritten);
        /// <summary>从给定的编码数据还原类型化槽值。无效数据返回 false，恢复方可拒绝整份候选。</summary>
        bool TryDecode(ReadOnlySpan<byte> source, out TValue value, out int bytesRead);
    }

    /// <summary>供框架按运行时类型调用已注册的槽值编解码器。</summary>
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

    /// <summary>将一个具体值类型的编解码器适配到框架注册表。</summary>
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

    /// <summary>按值类型、稳定标识和版本登记与冻结 Context 编解码器。</summary>
    public sealed class ContextCodecRegistry
    {
        private readonly Dictionary<CodecBindingKey, IContextValueCodecAdapter> _bindings =
            new Dictionary<CodecBindingKey, IContextValueCodecAdapter>();
        private readonly HashSet<CodecId> _codecIds = new HashSet<CodecId>();
        private readonly HashSet<CodecVersionKey> _codecVersions = new HashSet<CodecVersionKey>();
        private bool _frozen;

        public bool IsFrozen => _frozen;
        public int Count => _bindings.Count;

        #region Public API

        /// <summary>在冻结前登记与值类型对应的编码器，重复或无效注册时返回 false 并说明原因。</summary>
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

        /// <summary>封存编码器目录，供后续布局及恢复按相同的标识解析值类型。</summary>
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

        /// <summary>在注册表中查找与标识及值类型匹配的编码器。不存在匹配项时返回 false。</summary>
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

        #endregion

        #region Internal Logic

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

        /// <summary>将编解码器 ID 与编码版本组成注册键。</summary>
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

        /// <summary>按值类型与编解码描述查找具体绑定。</summary>
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

        #endregion
    }

    /// <summary>从已恢复的依赖值重建声明为派生的槽。</summary>
    public interface IDerivedStateRebuilder<TValue>
        where TValue : unmanaged
    {
        /// <summary>从已声明依赖重新计算派生槽。失败返回 false，调用方应放弃该恢复候选。</summary>
        bool TryRebuild(in DerivedStateReadContext context, out TValue value);
    }

    /// <summary>将指定恢复步骤中可读的依赖槽提供给重建器。</summary>
    public readonly struct DerivedStateReadContext
    {

        #region Public API

        /// <summary>从已声明的依赖槽读取重建所需的值。未声明依赖或类型不匹配时返回 false。</summary>
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

        #endregion

        #region Internal Logic

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

        #endregion
    }

    /// <summary>将无类型的恢复调度接到已登记的派生值类型。</summary>
    internal interface IDerivedStateRebuilderAdapter
    {
        Type ValueType { get; }
        bool TryRebuild(
            ContextFrameLayout layout,
            byte[] buffer,
            StateSlotDescriptor descriptor);
    }

    /// <summary>调用泛型重建器并把结果写回对应的派生槽。</summary>
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

    /// <summary>将某个类型的 Context 值定位到冻结布局中的槽位。</summary>
    public readonly struct StateSlot<TValue> : IEquatable<StateSlot<TValue>>
        where TValue : unmanaged
    {
        private readonly ContextFrameLayout _layout;

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

        #region Public API

        /// <summary>按<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>SlotId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateSlot<TValue> other)
        {
            return ReferenceEquals(_layout, other._layout) &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   SlotId == other.SlotId &&
                   DenseIndex == other.DenseIndex;
        }

        /// <summary>仅当对象属于同类型并且<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>SlotId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateSlot<TValue> other && Equals(other);
        /// <summary>为哈希集合计算与<c>layout</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>SlotId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((LayoutId.GetHashCode() * 397) ^ DenseIndex);

        #endregion

        #region Internal Logic

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

        internal bool IsFor(ContextFrameLayout layout) =>
            ReferenceEquals(_layout, layout) &&
            layout != null &&
            LayoutId == layout.LayoutId &&
            LayoutVersion == layout.Version &&
            LayoutSchemaHash == layout.SchemaHash;

        #endregion
    }

    /// <summary>记录槽位的所有者、值类型、默认值、投影及恢复合同。</summary>
    public sealed class StateSlotDescriptor
    {
        private readonly StateSlotId[] _derivedDependencies;

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

        #region Internal Logic

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

        #endregion
    }

    /// <summary>描述一个拥有者可写的数据块及其中的槽位。</summary>
    public sealed class StateBlockDescriptor
    {

        #region Public API

        public StateBlockId BlockId { get; }
        public StateBlockOwner Owner { get; }
        public int DenseIndex { get; }
        public IReadOnlyList<StateSlotDescriptor> Slots { get; }

        #endregion

        #region Internal Logic

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

        #endregion
    }

    /// <summary>收集块与槽的声明，校验所有权、身份和布局后生成只读布局。</summary>
    public sealed class ContextFrameLayoutBuilder
    {

        #region Public API

        /// <summary>在冻结前把有稳定标识和所有者的数据块加入布局。重复或无效声明时返回 false。</summary>
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

        /// <summary>在已登记的块中加入类型化槽及默认值。块、标识或恢复合同不合法时返回 false。</summary>
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

        /// <summary>声明由其他槽重建的派生槽。依赖或类型不符合布局要求时返回 false。</summary>
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

        /// <summary>验证所有块、槽和恢复依赖，以指定布局身份和版本生成只读布局，失败时返回诊断代码。</summary>
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

        #endregion

        #region Internal Logic

        private readonly List<BlockRegistration> _blocks = new List<BlockRegistration>();
        private readonly Dictionary<StateBlockId, BlockRegistration> _blocksById =
            new Dictionary<StateBlockId, BlockRegistration>();
        private readonly Dictionary<StateSlotId, SlotRegistration> _slotsById =
            new Dictionary<StateSlotId, SlotRegistration>();
        private bool _frozen;

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

        /// <summary>在布局冻结前保存一块数据的所有权和待关联的槽。</summary>
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

        /// <summary>在布局冻结前保存槽类型、默认值和恢复所需的元数据。</summary>
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

        #endregion
    }

    /// <summary>为实例分配固定的帧布局，按稳定 ID 解析类型化槽和块。</summary>
    public sealed class ContextFrameLayout
    {
        private readonly Dictionary<StateBlockId, StateBlockDescriptor> _blocksById;
        private readonly Dictionary<StateSlotId, StateSlotDescriptor> _slotsById;
        private readonly StateBlockDescriptor[] _blocks;
        private readonly StateSlotDescriptor[] _slots;
        private readonly byte[] _defaultBuffer;
        private readonly int[] _derivedOrder;

        public FrameLayoutId LayoutId { get; }
        public uint Version { get; }
        public ulong SchemaHash { get; }
        public IReadOnlyList<StateBlockDescriptor> Blocks { get; }
        public IReadOnlyList<StateSlotDescriptor> Slots { get; }
        public IReadOnlyList<int> DerivedOrder { get; }
        public int ByteSize => _defaultBuffer.Length;

        #region Public API

        /// <summary>根据稳定块 ID 在冻结布局中查找块，找不到时返回 false。</summary>
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

        /// <summary>按稳定槽 ID 在冻结布局中查找句柄或描述符。不存在匹配槽时返回 false。</summary>
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

        #endregion

        #region Internal Logic

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

        #endregion
    }
}
