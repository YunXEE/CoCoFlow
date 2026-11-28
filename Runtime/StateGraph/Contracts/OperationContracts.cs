using System.Buffers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// Logic 声明要写的 Operation，目录登记接口和布局，Operator 通过类型化句柄读取本步条目。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>标记 State 可通过属性写入、Operator 可按同一接口读取的命令合同。</summary>
    public interface IOperation
    {
    }

    /// <summary>在接口上声明连续或离散模式，在 Logic 属性上标记要注入的写入视图。</summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Property, Inherited = false)]
    public sealed class OperationAttribute : Attribute
    {

        internal bool HasMode { get; }
        public Mode Mode { get; }

        #region Public API

        /// <summary>把 Logic 属性标记为待注入的操作视图，接口本身另行声明输出模式。</summary>
        public OperationAttribute() { }

        /// <summary>将接口登记为指定的连续或离散输出模式。标准绑定和生成器检查该模式是否合法。</summary>
        public OperationAttribute(Mode mode)
        {
            Mode = mode;
            HasMode = true;
        }

        #endregion
    }

    /// <summary>将 Operation 接口关联到随包生成器为该接口产生的视图类型。</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class OperationViewFactoryAttribute : Attribute
    {
        /// <summary>在合同程序集记录操作接口与生成视图的对应关系，标准绑定用这项元数据构造视图。</summary>
        public OperationViewFactoryAttribute(Type section, Type factory)
        {
            OperationType = section;
            Factory = factory;
        }

        public Type OperationType { get; }
        public Type Factory { get; }
    }

    /// <summary>在装配时创建普通项目定义的 Operation 属性视图，并接入当前实例的字节存储。</summary>
    internal sealed class GeneratedOperationViewFactory<TOperation> : IOperationViewFactory<TOperation>
        where TOperation : class, IOperation
    {
        private readonly Type _viewType;

        internal GeneratedOperationViewFactory()
        {
            Type operationType = typeof(TOperation);
            foreach (OperationViewFactoryAttribute entry in operationType.Assembly.GetCustomAttributes(
                         typeof(OperationViewFactoryAttribute), false))
            {
                if (entry.OperationType != operationType) continue;
                _viewType = entry.Factory;
                return;
            }
            throw new InvalidOperationException($"No generated view for Operation {operationType.FullName}.");
        }

        public TOperation Create(in OperationViewContext<TOperation> context)
        {
            var reader = context.Reader;
            var operation = context.Descriptor;
            SpanAction<byte, int> read = (bytes, index) => reader.Read(operation, index, bytes);
            SpanAction<byte, int> write = (bytes, index) => reader.Write(operation, index, bytes);
            return (TOperation)Activator.CreateInstance(_viewType, read, write);
        }

    }

    /// <summary>区分每步自动可读的连续输出和本步写入后才启用的离散输出。</summary>
    public enum Mode
    {
        None = 0,
        Continuous = 1,
        Discrete = 2
    }

    /// <summary>描述一个接口属性的稳定索引、类型及在命令中的字节范围。</summary>
    public sealed class OperationFieldShape :
        IEquatable<OperationFieldShape>
    {

        #region Public API

        public int DenseIndex { get; }
        public string Name { get; }
        public Type ValueType { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }

        /// <summary>按<c>DenseIndex</c>、<c>Name</c>、<c>ValueType</c>、<c>ByteOffset</c>、<c>ByteSize</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationFieldShape other)
        {
            return other != null &&
                   DenseIndex == other.DenseIndex &&
                   string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                   ValueType == other.ValueType &&
                   ByteOffset == other.ByteOffset &&
                   ByteSize == other.ByteSize;
        }

        /// <summary>仅当对象属于同类型并且<c>DenseIndex</c>、<c>Name</c>、<c>ValueType</c>、<c>ByteOffset</c>、<c>ByteSize</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationFieldShape other && Equals(other);

        /// <summary>为哈希集合计算与<c>DenseIndex</c>、<c>Name</c>、<c>ValueType</c>、<c>ByteOffset</c>、<c>ByteSize</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DenseIndex;
                hashCode = (hashCode * 397) ^ (Name?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (ValueType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ ByteOffset;
                hashCode = (hashCode * 397) ^ ByteSize;
                return hashCode;
            }
        }

        #endregion

        #region Internal Logic

        internal OperationFieldShape(
            int denseIndex,
            string name,
            Type valueType,
            int byteOffset,
            int byteSize)
        {
            DenseIndex = denseIndex;
            Name = name;
            ValueType = valueType;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        #endregion
    }

    /// <summary>保存一个 Operation 接口经过校验的完整字段排列和字节大小。</summary>
    public sealed class OperationShape : IEquatable<OperationShape>
    {
        private readonly OperationFieldShape[] _fields;
        private readonly IReadOnlyList<OperationFieldShape> _readOnlyFields;

        public int ByteSize { get; }
        public int FieldCount => _fields.Length;
        public IReadOnlyList<OperationFieldShape> Fields => _readOnlyFields;
        public ulong ShapeFingerprint { get; }
        public bool IsValid => ByteSize > 0 &&
                               _fields.Length > 0 &&
                               ShapeFingerprint != 0UL;

        #region Public API

        /// <summary>检查给定的操作接口和字段布局，成功输出可注册的形状，拒绝时返回诊断。</summary>
        public static bool TryCreate(
            Type operationType,
            out OperationShape shape,
            out CoCoDiagnostic diagnostic)
        {
            return OperationContract.TryCreateShape(
                operationType,
                out shape,
                out diagnostic);
        }

        /// <summary>按<c>ShapeFingerprint</c>、<c>ByteSize</c>、<c>fields</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationShape other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null ||
                ShapeFingerprint != other.ShapeFingerprint ||
                ByteSize != other.ByteSize ||
                _fields.Length != other._fields.Length)
            {
                return false;
            }

            for (int index = 0; index < _fields.Length; index++)
            {
                if (!_fields[index].Equals(other._fields[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>仅当对象属于同类型并且<c>ShapeFingerprint</c>、<c>ByteSize</c>、<c>fields</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationShape other && Equals(other);

        /// <summary>为哈希集合计算与<c>ShapeFingerprint</c>、<c>ByteSize</c>、<c>fields</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => ShapeFingerprint.GetHashCode();

        #endregion

        #region Internal Logic

        internal OperationShape(
            int byteSize,
            OperationFieldShape[] fields)
        {
            ByteSize = byteSize;
            _fields = (OperationFieldShape[])fields.Clone();
            _readOnlyFields = Array.AsReadOnly(_fields);
            ShapeFingerprint = OperationShapeFingerprint.Compute(byteSize, _fields);
        }

        #endregion
    }

    /// <summary>记录本步条目是否启用，离散输出还携带激活身份与序号。</summary>
    public readonly struct OperationEntryHeader : IEquatable<OperationEntryHeader>
    {

        #region Public API

        public bool Enabled { get; }
        public ActivationId ActivationId { get; }
        public OperationSequence OperationSequence { get; }

        /// <summary>按<c>Enabled</c>、<c>ActivationId</c>、<c>OperationSequence</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationEntryHeader other)
        {
            return Enabled == other.Enabled &&
                   ActivationId == other.ActivationId &&
                   OperationSequence == other.OperationSequence;
        }

        /// <summary>仅当对象属于同类型并且<c>Enabled</c>、<c>ActivationId</c>、<c>OperationSequence</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationEntryHeader other && Equals(other);

        /// <summary>为哈希集合计算与<c>Enabled</c>、<c>ActivationId</c>、<c>OperationSequence</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Enabled.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationSequence.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>Enabled</c>、<c>ActivationId</c>、<c>OperationSequence</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperationEntryHeader left,
            OperationEntryHeader right) => left.Equals(right);

        /// <summary>比较两端的<c>Enabled</c>、<c>ActivationId</c>、<c>OperationSequence</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperationEntryHeader left,
            OperationEntryHeader right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperationEntryHeader(
            bool enabled,
            ActivationId activationId,
            OperationSequence operationSequence)
        {
            Enabled = enabled;
            ActivationId = activationId;
            OperationSequence = operationSequence;
        }

        internal static OperationEntryHeader Continuous() =>
            new OperationEntryHeader(true, default, default);

        internal static OperationEntryHeader Discrete(
            ActivationId activationId,
            OperationSequence operationSequence) =>
            new OperationEntryHeader(true, activationId, operationSequence);

        #endregion
    }

    /// <summary>让图和 Operator 使用同一标识、模式及字段布局来匹配输出。</summary>
    public readonly struct OperationRequirement : IEquatable<OperationRequirement>
    {
        private readonly Type _operationType;
        private readonly OperationShape _shape;

        public OperationId OperationId { get; }
        public Mode Mode { get; }
        public Type OperationType => _operationType;
        public OperationShape Shape => _shape;
        public bool IsValid => OperationId.IsValid &&
                               OperationContract.IsDefinedMode(Mode) &&
                               _operationType != null &&
                               _shape != null &&
                               _shape.IsValid;

        #region Public API

        /// <summary>将操作标识和模式绑定为类型化要件，无效标识或模式时返回 false 和诊断。</summary>
        public static bool TryCreate<TOperation>(
            OperationId operationId,
            Mode mode,
            out OperationRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TOperation : class, IOperation
        {
            if (!operationId.IsValid)
            {
                requirement = default;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "Operation OperationId must be valid.");
                return false;
            }

            if (!OperationContract.IsDefinedMode(mode))
            {
                requirement = default;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section mode must be Continuous or Discrete.");
                return false;
            }

            if (!OperationShape.TryCreate(
                    typeof(TOperation),
                    out OperationShape shape,
                    out diagnostic))
            {
                requirement = default;
                return false;
            }

            requirement = new OperationRequirement(
                operationId,
                mode,
                typeof(TOperation),
                shape);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>按<c>OperationId</c>、<c>Mode</c>、<c>operationType</c>、<c>shape</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationRequirement other)
        {
            return OperationId == other.OperationId &&
                   Mode == other.Mode &&
                   _operationType == other._operationType &&
                   Equals(_shape, other._shape);
        }

        /// <summary>仅当对象属于同类型并且<c>OperationId</c>、<c>Mode</c>、<c>operationType</c>、<c>shape</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationRequirement other && Equals(other);

        /// <summary>为哈希集合计算与<c>OperationId</c>、<c>Mode</c>、<c>operationType</c>、<c>shape</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = OperationId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Mode;
                hashCode = (hashCode * 397) ^ (_operationType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (_shape?.GetHashCode() ?? 0);
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>OperationId</c>、<c>Mode</c>、<c>operationType</c>、<c>shape</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperationRequirement left,
            OperationRequirement right) => left.Equals(right);

        /// <summary>比较两端的<c>OperationId</c>、<c>Mode</c>、<c>operationType</c>、<c>shape</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperationRequirement left,
            OperationRequirement right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperationRequirement(
            OperationId operationId,
            Mode mode,
            Type operationType,
            OperationShape shape)
        {
            OperationId = operationId;
            Mode = mode;
            _operationType = operationType;
            _shape = shape;
        }

        #endregion
    }

    /// <summary>将一种已注册 Operation 定位到对应注册表的条目。</summary>
    public readonly struct OperationHandle<TOperation> :
        IEquatable<OperationHandle<TOperation>>
        where TOperation : class, IOperation
    {
        private readonly OperationRegistry _owner;

        public FrameLayoutId LayoutId { get; }
        public OperationId OperationId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               OperationId.IsValid &&
                               DenseIndex >= 0;

        #region Public API

        /// <summary>按<c>owner</c>、<c>LayoutId</c>、<c>OperationId</c>、<c>DenseIndex</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationHandle<TOperation> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   OperationId == other.OperationId &&
                   DenseIndex == other.DenseIndex;
        }

        /// <summary>仅当对象属于同类型并且<c>owner</c>、<c>LayoutId</c>、<c>OperationId</c>、<c>DenseIndex</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationHandle<TOperation> other && Equals(other);

        /// <summary>为哈希集合计算与<c>owner</c>、<c>LayoutId</c>、<c>OperationId</c>、<c>DenseIndex</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationId.GetHashCode();
                hashCode = (hashCode * 397) ^ DenseIndex;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>owner</c>、<c>LayoutId</c>、<c>OperationId</c>、<c>DenseIndex</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperationHandle<TOperation> left,
            OperationHandle<TOperation> right) => left.Equals(right);

        /// <summary>比较两端的<c>owner</c>、<c>LayoutId</c>、<c>OperationId</c>、<c>DenseIndex</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperationHandle<TOperation> left,
            OperationHandle<TOperation> right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal OperationHandle(
            OperationRegistry owner,
            OperationId operationId,
            int denseIndex)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            OperationId = operationId;
            DenseIndex = denseIndex;
        }

        internal bool IsOwnedBy(OperationRegistry owner) => ReferenceEquals(_owner, owner);

        #endregion
    }

    /// <summary>将指定类型的字段绑定到已解析的 Operation 及存储位置。</summary>
    public readonly struct OperationField<TValue> :
        IEquatable<OperationField<TValue>>
        where TValue : unmanaged
    {
        private readonly OperationRegistry _owner;

        public FrameLayoutId LayoutId { get; }
        public int OperationIndex { get; }
        public int FieldIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               OperationIndex >= 0 &&
                               FieldIndex >= 0 &&
                               ByteOffset >= 0 &&
                               ByteSize > 0;

        #region Public API

        /// <summary>按<c>owner</c>、<c>LayoutId</c>、<c>OperationIndex</c>、<c>FieldIndex</c>、<c>ByteOffset</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationField<TValue> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   OperationIndex == other.OperationIndex &&
                   FieldIndex == other.FieldIndex &&
                   ByteOffset == other.ByteOffset &&
                   ByteSize == other.ByteSize;
        }

        /// <summary>仅当对象属于同类型并且<c>owner</c>、<c>LayoutId</c>、<c>OperationIndex</c>、<c>FieldIndex</c>、<c>ByteOffset</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperationField<TValue> other && Equals(other);

        /// <summary>为哈希集合计算与<c>owner</c>、<c>LayoutId</c>、<c>OperationIndex</c>、<c>FieldIndex</c>、<c>ByteOffset</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationIndex;
                hashCode = (hashCode * 397) ^ FieldIndex;
                hashCode = (hashCode * 397) ^ ByteOffset;
                hashCode = (hashCode * 397) ^ ByteSize;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>owner</c>、<c>LayoutId</c>、<c>OperationIndex</c>、<c>FieldIndex</c>、<c>ByteOffset</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperationField<TValue> left,
            OperationField<TValue> right) => left.Equals(right);

        /// <summary>比较两端的<c>owner</c>、<c>LayoutId</c>、<c>OperationIndex</c>、<c>FieldIndex</c>、<c>ByteOffset</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperationField<TValue> left,
            OperationField<TValue> right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal OperationField(
            OperationRegistry owner,
            int operationIndex,
            int fieldIndex,
            int byteOffset,
            int byteSize)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            OperationIndex = operationIndex;
            FieldIndex = fieldIndex;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        internal bool IsOwnedBy(OperationRegistry owner) => ReferenceEquals(_owner, owner);

        #endregion
    }

    /// <summary>记录注册表中一个命令字段的类型、偏移与密集索引。</summary>
    public sealed class OperationFieldDescriptor
    {

        #region Public API

        public int DenseIndex { get; }
        public string Name { get; }
        public Type ValueType { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }

        #endregion

        #region Internal Logic

        internal OperationFieldDescriptor(
            int denseIndex,
            string name,
            Type valueType,
            int byteOffset,
            int byteSize)
        {
            DenseIndex = denseIndex;
            Name = name;
            ValueType = valueType;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        #endregion
    }

    /// <summary>描述已注册的 Operation 和其各个属性在本实例输出帧中的布局。</summary>
    public sealed class OperationDescriptor
    {
        private readonly IReadOnlyList<OperationFieldDescriptor> _readOnlyFields;

        public OperationId OperationId { get; }
        public Mode Mode { get; }
        public Type OperationType { get; }
        public OperationShape Shape { get; }
        public int DenseIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public IReadOnlyList<OperationFieldDescriptor> Fields => _readOnlyFields;

        #region Internal Logic

        internal OperationDescriptor(
            OperationId operationId,
            Mode mode,
            Type operationType,
            OperationShape shape,
            int denseIndex,
            int byteOffset,
            int byteSize,
            OperationFieldDescriptor[] fields)
        {
            OperationId = operationId;
            Mode = mode;
            OperationType = operationType;
            Shape = shape;
            DenseIndex = denseIndex;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
            _readOnlyFields = Array.AsReadOnly(fields);
        }

        #endregion
    }

    /// <summary>根据框架给出的布局与读写能力创建类型化命令视图。</summary>
    internal interface IOperationViewFactory<TOperation>
        where TOperation : class, IOperation
    {
        TOperation Create(in OperationViewContext<TOperation> context);
    }

    /// <summary>仅在框架构造视图时传入字段描述与访问器。</summary>
    internal readonly struct OperationViewContext<TOperation>
        where TOperation : class, IOperation
    {
        private readonly OperationRegistry _registry;

        internal OperationViewContext(
            OperationRegistry registry,
            OperationReader reader,
            OperationHandle<TOperation> handle)
        {
            _registry = registry;
            Reader = reader;
            Handle = handle;
        }

        internal OperationDescriptor Descriptor => _registry.Operations[Handle.DenseIndex];
        public OperationReader Reader { get; }
        public OperationHandle<TOperation> Handle { get; }
        public bool IsValid => _registry != null && Reader != null && Handle.IsValid;

        public bool TryGetField<TValue>(
            int denseFieldIndex,
            out OperationField<TValue> field)
            where TValue : unmanaged
        {
            if (_registry == null)
            {
                field = default;
                return false;
            }

            return _registry.TryResolveField(Handle, denseFieldIndex, out field);
        }
    }

    /// <summary>接受当前回调已获授权的字段写入和离散条目启用请求。</summary>
    internal interface IOperationWriteTarget
    {
        void Write<TValue>(OperationField<TValue> field, in TValue value)
            where TValue : unmanaged;
        void Write(OperationDescriptor operation, int fieldIndex, ReadOnlySpan<byte> bytes);
        void Enable<TOperation>() where TOperation : class, IOperation;
    }

    /// <summary>把接口视图的属性访问转发到当前读帧或有租约的写目标。</summary>
    internal sealed class OperationReader
    {
        private readonly OperationFrame _frame;
        private readonly IOperationWriteTarget _writeTarget;
        private readonly Action _enable;

        internal OperationReader(OperationFrame frame)
        {
            _frame = frame;
        }

        internal OperationReader(IOperationWriteTarget writeTarget, Action enable)
        {
            _writeTarget = writeTarget;
            _enable = enable;
        }

        internal void Read(OperationDescriptor operation, int index, Span<byte> value)
        {
            value.Clear();
            _frame?.ReadField(operation, index, value);
        }

        internal void Write(OperationDescriptor operation, int index, ReadOnlySpan<byte> value)
        {
            if (_writeTarget == null) throw new InvalidOperationException("An Operator cannot write a finalized frame.");
            _enable?.Invoke();
            _writeTarget.Write(operation, index, value);
        }

        public void Write<TValue>(OperationField<TValue> field, in TValue value)
            where TValue : unmanaged
        {
            _enable?.Invoke();
            _writeTarget.Write(field, value);
        }

        public bool TryRead<TValue>(
            OperationField<TValue> field,
            out TValue value)
            where TValue : unmanaged
        {
            if (_frame == null)
            {
                value = default;
                return false;
            }

            return _frame.TryRead(field, out value);
        }

        public TValue Read<TValue>(OperationField<TValue> field)
            where TValue : unmanaged
        {
            return _frame != null && _frame.TryRead(field, out TValue value)
                ? value
                : default;
        }
    }

    /// <summary>收集操作要求和视图工厂，并把字段位置冻结成实例可复用的注册表。</summary>
    public sealed class OperationRegistryBuilder
    {
        private readonly Dictionary<Type, Registration> _byType = new Dictionary<Type, Registration>();
        private readonly Dictionary<OperationId, Registration> _byId =
            new Dictionary<OperationId, Registration>();
        private readonly List<Registration> _registrations = new List<Registration>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;
        public int Count => _registrations.Count;

        #region Public API

        /// <summary>按指定布局冻结所有操作注册，声明有误或目录不能再冻结时返回 false 并提供诊断。</summary>
        public bool TryFreeze(
            FrameLayoutId layoutId,
            out OperationRegistry registry,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                registry = null;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Operation Section Registry may only be frozen once.");
                return false;
            }

            if (!layoutId.IsValid)
            {
                registry = null;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "OperationFrame LayoutId must be valid.");
                return false;
            }

            Registration[] registrations = _registrations.ToArray();
            Array.Sort(registrations, RegistrationComparer.Instance);

            var descriptors = new OperationDescriptor[registrations.Length];
            var factories = new IViewFactoryRegistration[registrations.Length];
            int layoutSize = 0;
            for (int operationIndex = 0; operationIndex < registrations.Length; operationIndex++)
            {
                Registration registration = registrations[operationIndex];
                OperationShape shape = registration.Requirement.Shape;
                var fields = new OperationFieldDescriptor[shape.FieldCount];
                for (int fieldIndex = 0; fieldIndex < shape.FieldCount; fieldIndex++)
                {
                    OperationFieldShape field = shape.Fields[fieldIndex];
                    fields[fieldIndex] = new OperationFieldDescriptor(
                        field.DenseIndex,
                        field.Name,
                        field.ValueType,
                        field.ByteOffset,
                        field.ByteSize);
                }

                if (layoutSize > int.MaxValue - shape.ByteSize)
                {
                    registry = null;
                    diagnostic = OperationContract.Error(
                        CoCoDiagnosticCode.InvalidFrameLayout,
                        "OperationFrame storage exceeds the supported arena size.");
                    return false;
                }

                descriptors[operationIndex] = new OperationDescriptor(
                    registration.Requirement.OperationId,
                    registration.Requirement.Mode,
                    registration.Requirement.OperationType,
                    shape,
                    operationIndex,
                    layoutSize,
                    shape.ByteSize,
                    fields);
                factories[operationIndex] = registration.Factory;
                layoutSize += shape.ByteSize;
            }

            registry = new OperationRegistry(layoutId, layoutSize, descriptors, factories);
            if (!registry.TryPrewarmViews(out diagnostic))
            {
                registry = null;
                return false;
            }

            _isFrozen = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        internal bool TryRegister<TOperation>(
            OperationId operationId,
            Mode mode,
            IOperationViewFactory<TOperation> viewFactory,
            out OperationRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TOperation : class, IOperation
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Operation Section Registry is already frozen.");
                return false;
            }

            if (viewFactory == null)
            {
                requirement = default;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section read-view factory is required.");
                return false;
            }

            if (!OperationRequirement.TryCreate<TOperation>(
                    operationId,
                    mode,
                    out requirement,
                    out diagnostic))
            {
                return false;
            }

            Type operationType = typeof(TOperation);
            if (_byType.TryGetValue(operationType, out Registration sameType))
            {
                if (sameType.Requirement == requirement &&
                    ReferenceEquals(sameType.Factory.FactoryInstance, viewFactory))
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                requirement = default;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "The same Operation Section interface cannot be registered with another identity, mode, or view factory instance.");
                return false;
            }

            if (_byId.ContainsKey(operationId))
            {
                requirement = default;
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "Operation OperationId is already assigned to another interface.");
                return false;
            }

            var registration = new Registration(
                requirement,
                new ViewFactoryRegistration<TOperation>(viewFactory));
            _registrations.Add(registration);
            _byType.Add(operationType, registration);
            _byId.Add(operationId, registration);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>保存一个待冻结的操作声明与创建视图的工厂。</summary>
        private sealed class Registration
        {
            public Registration(
                OperationRequirement requirement,
                IViewFactoryRegistration factory)
            {
                Requirement = requirement;
                Factory = factory;
            }

            public OperationRequirement Requirement { get; }
            public IViewFactoryRegistration Factory { get; }
        }

        /// <summary>按稳定 Operation ID 排列注册记录，以便相同声明生成相同布局。</summary>
        private sealed class RegistrationComparer : IComparer<Registration>
        {
            public static readonly RegistrationComparer Instance = new RegistrationComparer();

            public int Compare(Registration left, Registration right)
            {
                int high = left.Requirement.OperationId.High.CompareTo(right.Requirement.OperationId.High);
                return high != 0
                    ? high
                    : left.Requirement.OperationId.Low.CompareTo(right.Requirement.OperationId.Low);
            }
        }

        #endregion
    }

    /// <summary>按已冻结的命令描述解析类型化句柄、字段及读写视图。</summary>
    public sealed class OperationRegistry
    {
        private readonly OperationDescriptor[] _descriptors;
        private readonly IReadOnlyList<OperationDescriptor> _readOnlyDescriptors;
        private readonly IViewFactoryRegistration[] _factories;

        public FrameLayoutId LayoutId { get; }
        public int ByteSize { get; }
        public int Count => _descriptors.Length;
        public IReadOnlyList<OperationDescriptor> Operations => _readOnlyDescriptors;
        public bool IsFrozen => LayoutId.IsValid;

        #region Public API

        /// <summary>在冻结目录中按接口类型取得操作句柄。没有匹配布局或类型时返回 false。</summary>
        public bool TryResolveTypedHandle<TOperation>(
            out OperationHandle<TOperation> handle)
            where TOperation : class, IOperation
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                if (_descriptors[index].OperationType != typeof(TOperation))
                {
                    continue;
                }

                handle = new OperationHandle<TOperation>(
                    this,
                    _descriptors[index].OperationId,
                    index);
                return true;
            }

            handle = default;
            return false;
        }

        /// <summary>按操作类型和字段索引解析类型化字段。索引越界或值类型不符时返回 false。</summary>
        public bool TryResolveTypedField<TOperation, TValue>(
            int fieldIndex,
            out OperationField<TValue> field)
            where TOperation : class, IOperation
            where TValue : unmanaged
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                if (_descriptors[index].OperationType != typeof(TOperation))
                {
                    continue;
                }

                var handle = new OperationHandle<TOperation>(
                    this,
                    _descriptors[index].OperationId,
                    index);
                return TryResolveField(handle, fieldIndex, out field);
            }

            field = default;
            return false;
        }

        /// <summary>按稳定操作 ID 解析匹配的描述符或句柄。目录中找不到声明时返回 false。</summary>
        public bool TryResolve<TOperation>(
            OperationRequirement requirement,
            out OperationHandle<TOperation> handle)
            where TOperation : class, IOperation
        {
            if (!requirement.IsValid ||
                requirement.OperationType != typeof(TOperation) ||
                !TryFind(requirement, out int operationIndex))
            {
                handle = default;
                return false;
            }

            OperationDescriptor descriptor = _descriptors[operationIndex];
            handle = new OperationHandle<TOperation>(
                this,
                descriptor.OperationId,
                operationIndex);
            return true;
        }

        /// <summary>将操作中的字段索引解析为类型化句柄，操作、索引或值类型不匹配时返回 false。</summary>
        public bool TryResolveField<TOperation, TValue>(
            OperationHandle<TOperation> handle,
            int denseFieldIndex,
            out OperationField<TValue> field)
            where TOperation : class, IOperation
            where TValue : unmanaged
        {
            if (!ValidateHandle(handle) ||
                denseFieldIndex < 0 ||
                denseFieldIndex >= _descriptors[handle.DenseIndex].Fields.Count)
            {
                field = default;
                return false;
            }

            OperationDescriptor section = _descriptors[handle.DenseIndex];
            OperationFieldDescriptor descriptor = section.Fields[denseFieldIndex];
            if (descriptor.ValueType != typeof(TValue))
            {
                field = default;
                return false;
            }

            field = new OperationField<TValue>(
                this,
                handle.DenseIndex,
                denseFieldIndex,
                section.ByteOffset + descriptor.ByteOffset,
                descriptor.ByteSize);
            return true;
        }

        /// <summary>核对状态声明的操作要件与冻结目录，缺失或模式不匹配时返回 false 并提供诊断。</summary>
        public bool TryValidateProvides(
            IReadOnlyList<OperationRequirement> graphProvides,
            out CoCoDiagnostic diagnostic)
        {
            if (graphProvides == null)
            {
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.MissingOperationContract,
                    "Graph Operation Provides must be declared.");
                return false;
            }

            if (graphProvides.Count != _descriptors.Length)
            {
                diagnostic = OperationContract.Error(
                    CoCoDiagnosticCode.MissingOperationContract,
                    "Graph Operation Provides must cover every Section in the OperationFrame layout.");
                return false;
            }

            for (int index = 0; index < graphProvides.Count; index++)
            {
                OperationRequirement current = graphProvides[index];
                if (!current.IsValid || !TryFind(current, out _))
                {
                    diagnostic = OperationContract.Error(
                        CoCoDiagnosticCode.MissingOperationContract,
                        "Graph Operation Provides contains an unregistered Section.");
                    return false;
                }

                for (int previous = 0; previous < index; previous++)
                {
                    if (graphProvides[previous] == current)
                    {
                        diagnostic = OperationContract.Error(
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "Graph Operation Provides contains the same Section more than once.");
                        return false;
                    }
                }
            }

            for (int operationIndex = 0; operationIndex < _descriptors.Length; operationIndex++)
            {
                OperationDescriptor descriptor = _descriptors[operationIndex];
                bool covered = false;
                for (int provideIndex = 0; provideIndex < graphProvides.Count; provideIndex++)
                {
                    OperationRequirement provided = graphProvides[provideIndex];
                    if (provided.OperationId == descriptor.OperationId &&
                        provided.Mode == descriptor.Mode &&
                        provided.OperationType == descriptor.OperationType &&
                        HasExactShape(provided.Shape, descriptor.Shape))
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    diagnostic = OperationContract.Error(
                        CoCoDiagnosticCode.MissingOperationContract,
                        "Graph Operation Provides does not cover the complete OperationFrame layout.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        internal OperationRegistry(
            FrameLayoutId layoutId,
            int byteSize,
            OperationDescriptor[] descriptors,
            IViewFactoryRegistration[] factories)
        {
            LayoutId = layoutId;
            ByteSize = byteSize;
            _descriptors = descriptors;
            _readOnlyDescriptors = Array.AsReadOnly(descriptors);
            _factories = factories;
        }

        internal bool TryResolveDenseIndex(
            OperationRequirement requirement,
            out int denseIndex)
        {
            if (!requirement.IsValid || !TryFind(requirement, out denseIndex))
            {
                denseIndex = -1;
                return false;
            }

            return true;
        }

        internal bool ValidateHandle<TOperation>(OperationHandle<TOperation> handle)
            where TOperation : class, IOperation
        {
            return handle.IsValid &&
                   handle.IsOwnedBy(this) &&
                   handle.LayoutId == LayoutId &&
                   handle.DenseIndex < _descriptors.Length &&
                   _descriptors[handle.DenseIndex].OperationId == handle.OperationId &&
                   _descriptors[handle.DenseIndex].OperationType == typeof(TOperation);
        }

        internal bool ValidateField<TValue>(OperationField<TValue> field)
            where TValue : unmanaged
        {
            if (!field.IsValid ||
                !field.IsOwnedBy(this) ||
                field.LayoutId != LayoutId ||
                field.OperationIndex >= _descriptors.Length)
            {
                return false;
            }

            OperationDescriptor section = _descriptors[field.OperationIndex];
            if (field.FieldIndex >= section.Fields.Count)
            {
                return false;
            }

            OperationFieldDescriptor descriptor = section.Fields[field.FieldIndex];
            return field.ByteOffset == section.ByteOffset + descriptor.ByteOffset &&
                   field.ByteSize == descriptor.ByteSize &&
                   descriptor.ValueType == typeof(TValue);
        }

        internal object CreateWriteView(Type operationType, IOperationWriteTarget target)
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                if (_descriptors[index].OperationType == operationType)
                    return _factories[index].CreateWriteView(this, target, _descriptors[index]);
            }
            throw new InvalidOperationException("No registered Operation Section: " + operationType);
        }

        internal object[] CreateViews(OperationFrame frame)
        {
            var views = new object[_descriptors.Length];
            var reader = new OperationReader(frame);
            for (int index = 0; index < _descriptors.Length; index++)
            {
                object view = _factories[index].Create(this, reader, _descriptors[index]);
                if (view == null || !view.GetType().IsSealed)
                {
                    throw new InvalidOperationException(
                        "Operation Section factory returned an invalid read view.");
                }

                views[index] = view;
            }

            return views;
        }

        internal Mode GetMode(int operationIndex) => _descriptors[operationIndex].Mode;

        private bool TryFind(OperationRequirement requirement, out int operationIndex)
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                OperationDescriptor descriptor = _descriptors[index];
                if (descriptor.OperationId == requirement.OperationId &&
                    descriptor.Mode == requirement.Mode &&
                    descriptor.OperationType == requirement.OperationType &&
                    HasExactShape(requirement.Shape, descriptor.Shape))
                {
                    operationIndex = index;
                    return true;
                }
            }

            operationIndex = -1;
            return false;
        }

        private static bool HasExactShape(
            OperationShape provided,
            OperationShape registered)
        {
            return provided != null &&
                   registered != null &&
                   provided.ShapeFingerprint == registered.ShapeFingerprint &&
                   provided.Equals(registered);
        }

        internal bool TryPrewarmViews(out CoCoDiagnostic diagnostic)
        {
            var reader = new OperationReader(null);
            for (int index = 0; index < _descriptors.Length; index++)
            {
                object view;
                try
                {
                    view = _factories[index].Create(this, reader, _descriptors[index]);
                }
                catch (Exception)
                {
                    diagnostic = OperationContract.Error(
                        CoCoDiagnosticCode.InvalidOperationContract,
                        "Operation Section read-view factory failed during prewarm.");
                    return false;
                }

                if (view == null || !view.GetType().IsSealed)
                {
                    diagnostic = OperationContract.Error(
                        CoCoDiagnosticCode.InvalidOperationContract,
                        "Operation Section read-view factory must return a sealed view.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion
    }

    /// <summary>将本步输出的启用信息与类型化只读视图一同交给 Operator。</summary>
    public readonly struct OperationEntry<TOperation>
        where TOperation : class, IOperation
    {

        #region Public API

        public OperationEntryHeader Header { get; }
        public TOperation View { get; }

        #endregion

        #region Internal Logic

        internal OperationEntry(
            OperationEntryHeader header,
            TOperation view)
        {
            Header = header;
            View = view;
        }

        #endregion
    }

    /// <summary>向消费者公开本步输出的帧头、注册表与类型化条目查询。</summary>
    public interface IOperationFrame
    {
        StateFlowFrameHeader Header { get; }
        OperationRegistry Registry { get; }

        /// <summary>从该帧取得给定句柄的类型化操作条目。句柄或帧无效时返回 false。</summary>
        bool TryGet<TOperation>(
            OperationHandle<TOperation> handle,
            out OperationEntry<TOperation> entry)
            where TOperation : class, IOperation;
    }

    /// <summary>用提交令牌校验某个输出候选的存在性，并提供只读条目。</summary>
    internal interface IStagedOperationReader
    {
        OperationRegistry Registry { get; }
        bool IsFinalized(ulong token);
        StateFlowFrameHeader GetFinalizedHeader(ulong token);
        bool TryGet<TOperation>(ulong token, OperationHandle<TOperation> handle,
            out OperationEntry<TOperation> entry) where TOperation : class, IOperation;
    }

    /// <summary>在 Host 决定提交之前供 Operator 读取类型化命令，事务结束后令牌失效。</summary>
    public readonly struct StagedOperationFrame : IOperationFrame
    {
        private readonly IStagedOperationReader _source;
        private readonly ulong _token;

        public StateFlowFrameHeader Header => _source?.GetFinalizedHeader(_token) ?? default;
        public OperationRegistry Registry => _source?.Registry;
        public bool IsValid => _source != null && _source.IsFinalized(_token);

        #region Public API

        /// <summary>从这一候选取得给定操作的条目。候选失效或句柄不匹配时返回 false。</summary>
        public bool TryGet<TOperation>(OperationHandle<TOperation> handle,
            out OperationEntry<TOperation> entry) where TOperation : class, IOperation
        {
            entry = default;
            return _source != null && _source.TryGet(_token, handle, out entry);
        }

        #endregion

        #region Internal Logic

        internal StagedOperationFrame(IStagedOperationReader source, ulong token)
        {
            _source = source;
            _token = token;
        }

        #endregion
    }

    /// <summary>保存一种操作的实际工厂并按框架授予的能力创建读视图或写视图。</summary>
    internal interface IViewFactoryRegistration
    {
        object FactoryInstance { get; }

        object CreateWriteView(OperationRegistry registry,
            IOperationWriteTarget target, OperationDescriptor descriptor);

        object Create(
            OperationRegistry registry,
            OperationReader reader,
            OperationDescriptor descriptor);
    }

    /// <summary>把泛型工厂接入框架的注册表以装配某一个 Operation 接口。</summary>
    internal sealed class ViewFactoryRegistration<TOperation> : IViewFactoryRegistration
        where TOperation : class, IOperation
    {
        private readonly IOperationViewFactory<TOperation> _factory;

        public ViewFactoryRegistration(IOperationViewFactory<TOperation> factory)
        {
            _factory = factory;
        }

        public object FactoryInstance => _factory;

        public object CreateWriteView(OperationRegistry registry,
            IOperationWriteTarget target, OperationDescriptor descriptor)
        {
            Action enable = descriptor.Mode == Mode.Discrete
                ? (Action)target.Enable<TOperation> : null;
            return Create(registry, new OperationReader(target, enable), descriptor);
        }

        public object Create(
            OperationRegistry registry,
            OperationReader reader,
            OperationDescriptor descriptor)
        {
            var handle = new OperationHandle<TOperation>(
                registry,
                descriptor.OperationId,
                descriptor.DenseIndex);
            var context = new OperationViewContext<TOperation>(registry, reader, handle);
            return _factory.Create(context);
        }
    }

    /// <summary>验证操作接口的继承、属性和数据方法能否形成固定布局。</summary>
    internal static class OperationContract
    {
        public static bool IsDefinedMode(Mode mode) =>
            mode == Mode.Continuous || mode == Mode.Discrete;

        public static bool TryCreateShape(
            Type operationType,
            out OperationShape shape,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateIdentity(operationType, out diagnostic))
            {
                shape = null;
                return false;
            }

            if (!TryValidateMembers(operationType, out PropertyInfo[] properties, out diagnostic))
            {
                shape = null;
                return false;
            }

            var fields = new OperationFieldShape[properties.Length];
            int sectionSize = 0;
            for (int fieldIndex = 0; fieldIndex < properties.Length; fieldIndex++)
            {
                PropertyInfo property = properties[fieldIndex];
                if (!TryGetManagedSize(property.PropertyType, out int fieldSize))
                {
                    shape = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationContract,
                        "Operation Section field layout could not be determined.");
                    return false;
                }

                if (fieldSize <= 0 || sectionSize > int.MaxValue - fieldSize)
                {
                    shape = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidFrameLayout,
                        "Operation Section field storage exceeds the supported arena size.");
                    return false;
                }

                fields[fieldIndex] = new OperationFieldShape(
                    fieldIndex,
                    property.Name,
                    property.PropertyType,
                    sectionSize,
                    fieldSize);
                sectionSize += fieldSize;
            }

            shape = new OperationShape(sectionSize, fields);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public static bool TryValidateIdentity(
            Type operationType,
            out CoCoDiagnostic diagnostic)
        {
            if (operationType == null ||
                !operationType.IsInterface ||
                operationType == typeof(IOperation))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section must be a dedicated interface.");
                return false;
            }

            Type[] inherited = operationType.GetInterfaces();
            if (inherited.Length != 1 || !IsOperationMarker(inherited[0]))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section must inherit only IOperation directly.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool IsOperationMarker(Type type)
        {
            Type marker = typeof(IOperation);
            return type == marker ||
                   (type != null &&
                    string.Equals(type.FullName, marker.FullName, StringComparison.Ordinal) &&
                    string.Equals(
                        type.Assembly.GetName().Name,
                        marker.Assembly.GetName().Name,
                        StringComparison.Ordinal));
        }

        public static bool TryValidateMembers(
            Type operationType,
            out PropertyInfo[] properties,
            out CoCoDiagnostic diagnostic)
        {
            if (operationType == null)
            {
                properties = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section type is required.");
                return false;
            }

            const BindingFlags declaredMembers = BindingFlags.Instance |
                                                 BindingFlags.Static |
                                                 BindingFlags.Public |
                                                 BindingFlags.NonPublic |
                                                 BindingFlags.DeclaredOnly;
            if (operationType.GetEvents(declaredMembers).Length != 0 ||
                operationType.GetFields(declaredMembers).Length != 0)
            {
                properties = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section cannot declare events or fields.");
                return false;
            }

            properties = operationType.GetProperties(declaredMembers);
            if (properties.Length == 0)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "Operation Section must declare at least one getter property.");
                return false;
            }

            Array.Sort(properties, PropertyComparer.Instance);
            var accessors = new HashSet<MethodInfo>();
            for (int index = 0; index < properties.Length; index++)
            {
                PropertyInfo property = properties[index];
                MethodInfo getter = property.GetGetMethod(true);
                if (getter == null ||
                    !getter.IsPublic ||
                    getter.IsStatic ||
                    !getter.IsAbstract ||
                    property.GetIndexParameters().Length != 0 ||
                    property.PropertyType.IsByRef ||
                    Nullable.GetUnderlyingType(property.PropertyType) != null ||
                    !StateFlowTypeRules.IsReferenceFreeValueType(property.PropertyType))
                {
                    properties = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationContract,
                        "Operation Section properties must be public abstract unmanaged properties.");
                    return false;
                }

                accessors.Add(getter);
                if (property.GetSetMethod(true) is MethodInfo setter) accessors.Add(setter);
            }

            MethodInfo[] methods = operationType.GetMethods(declaredMembers);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                bool dataMethod = method.IsPublic && method.IsAbstract && !method.IsStatic &&
                    method.ReturnType == typeof(void) &&
                    Array.TrueForAll(method.GetParameters(), parameter =>
                        StateFlowTypeRules.IsReferenceFreeValueType(parameter.ParameterType));
                if (!accessors.Contains(method) && !dataMethod)
                {
                    properties = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationContract,
                        "Operation Section methods must be void data operations.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public static bool TryGetManagedSize(Type valueType, out int size)
        {
            GCHandle pinned = default;
            try
            {
                if (valueType == null ||
                    !StateFlowTypeRules.IsReferenceFreeValueType(valueType))
                {
                    size = 0;
                    return false;
                }

                Array values = Array.CreateInstance(valueType, 2);
                pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
                long first = Marshal.UnsafeAddrOfPinnedArrayElement(values, 0).ToInt64();
                long second = Marshal.UnsafeAddrOfPinnedArrayElement(values, 1).ToInt64();
                long stride = second - first;
                if (stride <= 0L || stride > int.MaxValue)
                {
                    size = 0;
                    return false;
                }

                size = (int)stride;
                return true;
            }
            catch (Exception)
            {
                size = 0;
                return false;
            }
            finally
            {
                if (pinned.IsAllocated)
                {
                    pinned.Free();
                }
            }
        }

        public static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operation, code, message);

        /// <summary>按序号规则比较属性名，使字段排列与反射枚举顺序无关。</summary>
        private sealed class PropertyComparer : IComparer<PropertyInfo>
        {
            public static readonly PropertyComparer Instance = new PropertyComparer();

            public int Compare(PropertyInfo left, PropertyInfo right) =>
                string.CompareOrdinal(left.Name, right.Name);
        }
    }

    /// <summary>从操作接口及其字段布局计算可复核的形状标识。</summary>
    internal static class OperationShapeFingerprint
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(
            int byteSize,
            IReadOnlyList<OperationFieldShape> fields)
        {
            ulong hash = OffsetBasis;
            Add(ref hash, 1U);
            Add(ref hash, byteSize);
            Add(ref hash, fields.Count);
            for (int index = 0; index < fields.Count; index++)
            {
                OperationFieldShape field = fields[index];
                Add(ref hash, field.DenseIndex);
                Add(ref hash, field.Name);
                Add(ref hash, field.ValueType);
                Add(ref hash, field.ByteOffset);
                Add(ref hash, field.ByteSize);
            }

            return hash == 0UL ? OffsetBasis : hash;
        }

        private static void Add(ref ulong hash, Type valueType)
        {
            if (valueType.IsGenericType)
            {
                Type definition = valueType.GetGenericTypeDefinition();
                Add(ref hash, GraphTypeIdentity.AssemblyName(definition));
                Add(ref hash, GraphTypeIdentity.FullName(definition));
                Type[] arguments = valueType.GetGenericArguments();
                Add(ref hash, arguments.Length);
                for (int index = 0; index < arguments.Length; index++)
                {
                    Add(ref hash, arguments[index]);
                }

                return;
            }

            Add(ref hash, GraphTypeIdentity.AssemblyName(valueType));
            Add(ref hash, GraphTypeIdentity.FullName(valueType));
        }

        private static void Add(ref ulong hash, string value)
        {
            if (value == null)
            {
                Add(ref hash, -1);
                return;
            }

            Add(ref hash, value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                AddByte(ref hash, (byte)character);
                AddByte(ref hash, (byte)(character >> 8));
            }
        }

        private static void Add(ref ulong hash, int value) =>
            Add(ref hash, unchecked((uint)value));

        private static void Add(ref ulong hash, uint value)
        {
            AddByte(ref hash, (byte)value);
            AddByte(ref hash, (byte)(value >> 8));
            AddByte(ref hash, (byte)(value >> 16));
            AddByte(ref hash, (byte)(value >> 24));
        }

        private static void AddByte(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= Prime;
        }
    }

    /// <summary>在预分配的命令存储与无托管引用的字段值之间复制字节。</summary>
    internal static class OperationBinary
    {
        public static TValue Read<TValue>(byte[] storage, int offset, int size)
            where TValue : unmanaged
        {
            TValue value = default;
            Span<byte> destination = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateSpan(ref value, 1));
            new ReadOnlySpan<byte>(storage, offset, size).CopyTo(destination);
            return value;
        }

        public static void Write<TValue>(byte[] storage, int offset, int size, in TValue value)
            where TValue : unmanaged
        {
            TValue copy = value;
            ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(ref copy, 1));
            source.CopyTo(new Span<byte>(storage, offset, size));
        }
    }
}
