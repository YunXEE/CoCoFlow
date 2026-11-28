using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 项目 freezer 按 schema 写节点配置，编译器封存为只读快照，供多个实例安全共享。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>以类型区分节点配置的冻结布局，防止跨 schema 读写。</summary>
    public interface IFrozenConfigSchema
    {
    }

    /// <summary>在冻结配置 schema 中定位一个可序列化字段。</summary>
    public readonly struct FrozenConfigFieldId : IEquatable<FrozenConfigFieldId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out FrozenConfigFieldId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new FrozenConfigFieldId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out FrozenConfigFieldId id)
        {
            if (!GraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(FrozenConfigFieldId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is FrozenConfigFieldId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(FrozenConfigFieldId left, FrozenConfigFieldId right) =>
            left.Equals(right);

        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(FrozenConfigFieldId left, FrozenConfigFieldId right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        private FrozenConfigFieldId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>将一个标量配置字段绑定到所属 schema 和固定索引。</summary>
    public readonly struct FrozenConfigField<TSchema, TValue> :
        IEquatable<FrozenConfigField<TSchema, TValue>>
        where TSchema : struct, IFrozenConfigSchema
    {

        #region Public API

        public FrozenConfigFieldId FieldId { get; }
        public bool IsValid => FieldId.IsValid;

        /// <summary>按<c>FieldId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(FrozenConfigField<TSchema, TValue> other) => FieldId == other.FieldId;
        /// <summary>仅当对象属于同类型并且<c>FieldId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is FrozenConfigField<TSchema, TValue> other && Equals(other);
        /// <summary>为哈希集合计算与<c>FieldId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => FieldId.GetHashCode();

        /// <summary>比较两端的<c>FieldId</c>，相等时返回 true。</summary>
        public static bool operator ==(
            FrozenConfigField<TSchema, TValue> left,
            FrozenConfigField<TSchema, TValue> right) => left.Equals(right);

        /// <summary>比较两端的<c>FieldId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            FrozenConfigField<TSchema, TValue> left,
            FrozenConfigField<TSchema, TValue> right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal FrozenConfigField(FrozenConfigFieldId fieldId)
        {
            FieldId = fieldId;
        }

        #endregion
    }

    /// <summary>将一个配置数组字段绑定到所属 schema 和元素类型。</summary>
    public readonly struct FrozenConfigArrayField<TSchema, TElement> :
        IEquatable<FrozenConfigArrayField<TSchema, TElement>>
        where TSchema : struct, IFrozenConfigSchema
    {

        #region Public API

        public FrozenConfigFieldId FieldId { get; }
        public bool IsValid => FieldId.IsValid;

        /// <summary>按<c>FieldId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(FrozenConfigArrayField<TSchema, TElement> other) =>
            FieldId == other.FieldId;
        /// <summary>仅当对象属于同类型并且<c>FieldId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is FrozenConfigArrayField<TSchema, TElement> other && Equals(other);
        /// <summary>为哈希集合计算与<c>FieldId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => FieldId.GetHashCode();

        /// <summary>比较两端的<c>FieldId</c>，相等时返回 true。</summary>
        public static bool operator ==(
            FrozenConfigArrayField<TSchema, TElement> left,
            FrozenConfigArrayField<TSchema, TElement> right) => left.Equals(right);

        /// <summary>比较两端的<c>FieldId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            FrozenConfigArrayField<TSchema, TElement> left,
            FrozenConfigArrayField<TSchema, TElement> right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal FrozenConfigArrayField(FrozenConfigFieldId fieldId)
        {
            FieldId = fieldId;
        }

        #endregion
    }

    /// <summary>收集标量与数组字段并分配稳定的配置槽位。</summary>
    public sealed class FrozenConfigSchemaBuilder<TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        private readonly List<FrozenConfigFieldDefinition> _definitions =
            new List<FrozenConfigFieldDefinition>();
        private readonly HashSet<FrozenConfigFieldId> _fieldIds =
            new HashSet<FrozenConfigFieldId>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        #region Public API

        /// <summary>在 schema 冻结前登记标量字段并返回类型化句柄，标识重复或类型不支持时返回 false。</summary>
        public bool TryAddField<TValue>(
            FrozenConfigFieldId fieldId,
            out FrozenConfigField<TSchema, TValue> field,
            out CoCoDiagnostic diagnostic)
        {
            field = default;
            if (!TryAdd(fieldId, typeof(TValue), false, out diagnostic))
            {
                return false;
            }

            field = new FrozenConfigField<TSchema, TValue>(fieldId);
            return true;
        }

        /// <summary>在 schema 冻结前登记一维数组字段并返回元素类型匹配的句柄，声明无效时返回 false。</summary>
        public bool TryAddArrayField<TElement>(
            FrozenConfigFieldId fieldId,
            out FrozenConfigArrayField<TSchema, TElement> field,
            out CoCoDiagnostic diagnostic)
        {
            field = default;
            if (!TryAdd(fieldId, typeof(TElement), true, out diagnostic))
            {
                return false;
            }

            field = new FrozenConfigArrayField<TSchema, TElement>(fieldId);
            return true;
        }

        /// <summary>固化字段编号、类型和布局指纹。配置 schema 不能冻结时返回 false 并提供诊断。</summary>
        public bool TryFreeze(
            out FrozenConfigSchema<TSchema> schema,
            out CoCoDiagnostic diagnostic)
        {
            schema = null;
            if (_isFrozen)
            {
                diagnostic = Error("A frozen configuration schema builder can only be frozen once.");
                return false;
            }

            _isFrozen = true;
            var definitions = _definitions.ToArray();
            Array.Sort(definitions, FrozenConfigFieldDefinition.Compare);
            schema = new FrozenConfigSchema<TSchema>(definitions);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        private bool TryAdd(
            FrozenConfigFieldId fieldId,
            Type valueType,
            bool isArray,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                diagnostic = Error("A frozen configuration schema cannot be changed after it is frozen.");
                return false;
            }

            if (!fieldId.IsValid)
            {
                diagnostic = Error("Frozen configuration fields require a valid stable field id.");
                return false;
            }

            if (!FrozenConfigValueContract.IsAllowedScalar(valueType))
            {
                diagnostic = Error(
                    "Frozen configuration fields support only primitive, enum, decimal, or string values.");
                return false;
            }

            if (!_fieldIds.Add(fieldId))
            {
                diagnostic = Error("Frozen configuration field ids must be unique within a schema.");
                return false;
            }

            _definitions.Add(new FrozenConfigFieldDefinition(fieldId, valueType, isArray));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidFrozenConfig,
                message);

        #endregion
    }

    /// <summary>保存已封存字段的声明，用来创建与该布局相符的写入器。</summary>
    public sealed class FrozenConfigSchema<TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        private readonly FrozenConfigFieldDefinition[] _definitions;
        private readonly Dictionary<FrozenConfigFieldId, int> _indices;

        public Type SchemaType => typeof(TSchema);
        public int FieldCount => _definitions.Length;
        public ulong Fingerprint { get; }
        public bool IsValid => Fingerprint != 0UL;

        #region Internal Logic

        internal FrozenConfigSchema(FrozenConfigFieldDefinition[] definitions)
        {
            _definitions = (FrozenConfigFieldDefinition[])definitions.Clone();
            _indices = new Dictionary<FrozenConfigFieldId, int>(_definitions.Length);
            ulong hash = FrozenConfigHash.OffsetBasis;
            FrozenConfigHash.AddType(ref hash, typeof(TSchema));
            FrozenConfigHash.Add(ref hash, unchecked((ulong)_definitions.Length));
            for (int index = 0; index < _definitions.Length; index++)
            {
                FrozenConfigFieldDefinition definition = _definitions[index];
                _indices.Add(definition.FieldId, index);
                FrozenConfigHash.Add(ref hash, definition.FieldId.High);
                FrozenConfigHash.Add(ref hash, definition.FieldId.Low);
                FrozenConfigHash.Add(ref hash, definition.IsArray ? 1UL : 0UL);
                FrozenConfigHash.AddType(ref hash, definition.ValueType);
            }

            Fingerprint = FrozenConfigHash.Complete(hash);
        }

        internal FrozenConfigFieldDefinition GetDefinition(int index) => _definitions[index];

        internal bool TryGetDefinition(
            FrozenConfigFieldId fieldId,
            out int index,
            out FrozenConfigFieldDefinition definition)
        {
            if (_indices.TryGetValue(fieldId, out index))
            {
                definition = _definitions[index];
                return true;
            }

            index = -1;
            definition = null;
            return false;
        }

        internal FrozenConfigWriter<TSchema> CreateWriter() =>
            new FrozenConfigWriter<TSchema>(this);

        #endregion

    }

    /// <summary>在封存前接收合法字段值，并将可变输入复制到只读配置快照。</summary>
    public sealed class FrozenConfigWriter<TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        private readonly FrozenConfigSchema<TSchema> _schema;
        private readonly object[] _values;
        private readonly bool[] _written;
        private CoCoDiagnostic _failure;
        private bool _isSealed;

        public bool IsSealed => _isSealed;
        public bool HasFailed => !_failure.IsNone;

        #region Public API

        /// <summary>将单值写入匹配的冻结字段，字段无效、类型不符或重复写入时返回 false 并提供诊断。</summary>
        public bool TryWrite<TValue>(
            FrozenConfigField<TSchema, TValue> field,
            TValue value,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryBeginWrite(
                    field.FieldId,
                    typeof(TValue),
                    false,
                    out int index,
                    out diagnostic))
            {
                return false;
            }

            if (ReferenceEquals(value, null))
            {
                return Fail("Frozen configuration values cannot be null.", out diagnostic);
            }

            _values[index] = value;
            _written[index] = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>将数组值写入已登记的数组字段，类型或字段不匹配时返回 false 并提供诊断。</summary>
        public bool TryWriteArray<TElement>(
            FrozenConfigArrayField<TSchema, TElement> field,
            IReadOnlyList<TElement> values,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryBeginWrite(
                    field.FieldId,
                    typeof(TElement),
                    true,
                    out int index,
                    out diagnostic))
            {
                return false;
            }

            if (values == null)
            {
                return Fail("Frozen configuration arrays cannot be null.", out diagnostic);
            }

            try
            {
                _values[index] = new FrozenArray<TElement>(values);
            }
            catch (Exception)
            {
                return Fail(
                    "Frozen configuration arrays must contain only non-null supported scalar values.",
                    out diagnostic);
            }

            _written[index] = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        internal FrozenConfigWriter(FrozenConfigSchema<TSchema> schema)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _values = new object[schema.FieldCount];
            _written = new bool[schema.FieldCount];
        }

        internal bool TrySeal(
            out FrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (_isSealed)
            {
                diagnostic = Error("A frozen configuration writer can only be sealed once.");
                return false;
            }

            _isSealed = true;
            if (!_failure.IsNone)
            {
                diagnostic = _failure;
                return false;
            }

            for (int index = 0; index < _written.Length; index++)
            {
                if (_written[index])
                {
                    continue;
                }

                _failure = Error(
                    $"Frozen configuration field {_schema.GetDefinition(index).FieldId} was not written.");
                diagnostic = _failure;
                return false;
            }

            ulong fingerprint = FrozenConfigHash.ComputeSnapshot(_schema, _values);
            snapshot = FrozenConfigSnapshot.Create(_schema, _values, fingerprint);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void SealWithoutSnapshot()
        {
            _isSealed = true;
        }

        private bool TryBeginWrite(
            FrozenConfigFieldId fieldId,
            Type valueType,
            bool isArray,
            out int index,
            out CoCoDiagnostic diagnostic)
        {
            index = -1;
            if (_isSealed)
            {
                diagnostic = Error("A sealed frozen configuration writer cannot be changed.");
                return false;
            }

            if (!_failure.IsNone)
            {
                diagnostic = _failure;
                return false;
            }

            if (!fieldId.IsValid ||
                !_schema.TryGetDefinition(fieldId, out index, out FrozenConfigFieldDefinition definition))
            {
                return Fail("The frozen configuration field is not part of this schema.", out diagnostic);
            }

            if (definition.IsArray != isArray || definition.ValueType != valueType)
            {
                return Fail(
                    "The frozen configuration field kind or value type does not match its schema.",
                    out diagnostic);
            }

            if (_written[index])
            {
                return Fail("Each frozen configuration field must be written exactly once.", out diagnostic);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool Fail(string message, out CoCoDiagnostic diagnostic)
        {
            if (_failure.IsNone)
            {
                _failure = Error(message);
            }

            diagnostic = _failure;
            return false;
        }

        private static CoCoDiagnostic Error(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidFrozenConfig,
                message);

        #endregion
    }

    /// <summary>向 Logic 暴露冻结配置的数组元素，读者无法修改快照中的原数组。</summary>
    public sealed class FrozenArray<T> : IReadOnlyList<T>, IFrozenArrayValue
    {
        private readonly T[] _items;

        public int Count => _items.Length;

        #region Public API

        public T this[int index] => _items[index];

        /// <summary>按冻结时保留的顺序遍历元素，不向调用方暴露原可变数组。</summary>
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        /// <summary>按冻结时保留的顺序遍历元素，不向调用方暴露原可变数组。</summary>
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        void IFrozenArrayValue.AddToHash(ref ulong hash)
        {
            FrozenConfigHash.Add(ref hash, unchecked((ulong)_items.Length));
            for (int index = 0; index < _items.Length; index++)
            {
                FrozenConfigHash.AddScalar(ref hash, typeof(T), _items[index]);
            }
        }

        #endregion

        #region Internal Logic

        internal FrozenArray(IReadOnlyList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            _items = new T[items.Count];
            for (int index = 0; index < items.Count; index++)
            {
                T item = items[index];
                if (ReferenceEquals(item, null))
                {
                    throw new ArgumentException("Frozen configuration array elements cannot be null.", nameof(items));
                }

                _items[index] = item;
            }
        }

        #endregion
    }

    /// <summary>由项目或标准绑定实现，将可编辑的节点配置转换为指定 schema 的值。</summary>
    public interface IConfigFreezer<TAuthoringConfig, TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        /// <summary>将节点的可编辑配置转换为独立的类型化快照。失败时返回 false 和诊断，编译器拒绝该节点。</summary>
        bool TryFreeze(
            TAuthoringConfig source,
            FrozenConfigWriter<TSchema> writer,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>通过带类型的字段句柄读取已封存的节点配置。</summary>
    public sealed class FrozenConfigSnapshot
    {
        private readonly Type _schemaType;
        private readonly ulong _schemaFingerprint;
        private readonly FrozenConfigFieldDefinition[] _definitions;
        private readonly Dictionary<FrozenConfigFieldId, int> _indices;
        private readonly object[] _values;

        public Type SchemaType => _schemaType;
        public ulong SchemaFingerprint => _schemaFingerprint;
        public ulong Fingerprint { get; }
        public bool IsValid => SchemaType != null &&
                               SchemaFingerprint != 0UL &&
                               Fingerprint != 0UL;

        #region Public API

        /// <summary>按同一 schema 的标量字段取值，快照无效或字段不匹配时返回 false。</summary>
        public bool TryRead<TSchema, TValue>(
            FrozenConfigField<TSchema, TValue> field,
            out TValue value)
            where TSchema : struct, IFrozenConfigSchema
        {
            value = default;
            if (!IsValid || SchemaType != typeof(TSchema) || !field.IsValid ||
                !TryGetDefinition(field.FieldId, out int index, out FrozenConfigFieldDefinition definition) ||
                definition.IsArray ||
                definition.ValueType != typeof(TValue))
            {
                return false;
            }

            value = (TValue)_values[index];
            return true;
        }

        /// <summary>按同一 schema 的数组字段读取只读快照，字段种类或类型不符时返回 false。</summary>
        public bool TryReadArray<TSchema, TElement>(
            FrozenConfigArrayField<TSchema, TElement> field,
            out FrozenArray<TElement> values)
            where TSchema : struct, IFrozenConfigSchema
        {
            values = null;
            if (!IsValid || SchemaType != typeof(TSchema) || !field.IsValid ||
                !TryGetDefinition(field.FieldId, out int index, out FrozenConfigFieldDefinition definition) ||
                !definition.IsArray ||
                definition.ValueType != typeof(TElement))
            {
                return false;
            }

            values = (FrozenArray<TElement>)_values[index];
            return true;
        }

        #endregion

        #region Internal Logic

        private FrozenConfigSnapshot(
            Type schemaType,
            ulong schemaFingerprint,
            FrozenConfigFieldDefinition[] definitions,
            object[] values,
            ulong fingerprint)
        {
            _schemaType = schemaType;
            _schemaFingerprint = schemaFingerprint;
            _definitions = definitions;
            _indices = new Dictionary<FrozenConfigFieldId, int>(definitions.Length);
            for (int index = 0; index < definitions.Length; index++)
            {
                _indices.Add(definitions[index].FieldId, index);
            }

            _values = (object[])values.Clone();
            Fingerprint = fingerprint;
        }

        internal static FrozenConfigSnapshot Create<TSchema>(
            FrozenConfigSchema<TSchema> schema,
            object[] values,
            ulong fingerprint)
            where TSchema : struct, IFrozenConfigSchema
        {
            var definitions = new FrozenConfigFieldDefinition[schema.FieldCount];
            for (int index = 0; index < definitions.Length; index++)
            {
                FrozenConfigFieldDefinition source = schema.GetDefinition(index);
                definitions[index] = new FrozenConfigFieldDefinition(
                    source.FieldId,
                    source.ValueType,
                    source.IsArray);
            }

            return new FrozenConfigSnapshot(
                typeof(TSchema),
                schema.Fingerprint,
                definitions,
                values,
                fingerprint);
        }

        internal bool MatchesSchema<TSchema>(FrozenConfigSchema<TSchema> schema)
            where TSchema : struct, IFrozenConfigSchema
        {
            if (!IsValid ||
                schema == null ||
                SchemaType != typeof(TSchema) ||
                SchemaFingerprint != schema.Fingerprint ||
                _definitions.Length != schema.FieldCount)
            {
                return false;
            }

            for (int index = 0; index < _definitions.Length; index++)
            {
                FrozenConfigFieldDefinition left = _definitions[index];
                FrozenConfigFieldDefinition right = schema.GetDefinition(index);
                if (left.FieldId != right.FieldId ||
                    left.ValueType != right.ValueType ||
                    left.IsArray != right.IsArray)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetDefinition(
            FrozenConfigFieldId fieldId,
            out int index,
            out FrozenConfigFieldDefinition definition)
        {
            if (_indices.TryGetValue(fieldId, out index))
            {
                definition = _definitions[index];
                return true;
            }

            index = -1;
            definition = null;
            return false;
        }

        #endregion
    }

    /// <summary>记录一个冻结字段的稳定 ID、类型、位置和标量或数组种类。</summary>
    internal sealed class FrozenConfigFieldDefinition
    {
        public FrozenConfigFieldDefinition(
            FrozenConfigFieldId fieldId,
            Type valueType,
            bool isArray)
        {
            FieldId = fieldId;
            ValueType = valueType;
            IsArray = isArray;
        }

        public FrozenConfigFieldId FieldId { get; }
        public Type ValueType { get; }
        public bool IsArray { get; }

        public static int Compare(
            FrozenConfigFieldDefinition left,
            FrozenConfigFieldDefinition right)
        {
            int high = left.FieldId.High.CompareTo(right.FieldId.High);
            return high != 0 ? high : left.FieldId.Low.CompareTo(right.FieldId.Low);
        }
    }

    /// <summary>把不同元素类型的冻结数组接入公共的指纹和元素复制路径。</summary>
    internal interface IFrozenArrayValue
    {
        void AddToHash(ref ulong hash);
    }

    /// <summary>约束配置值可复制、可比较和可稳定散列的范围。</summary>
    internal static class FrozenConfigValueContract
    {
        public static bool IsAllowedScalar(Type type) =>
            type == typeof(bool) ||
            type == typeof(byte) ||
            type == typeof(sbyte) ||
            type == typeof(short) ||
            type == typeof(ushort) ||
            type == typeof(int) ||
            type == typeof(uint) ||
            type == typeof(long) ||
            type == typeof(ulong) ||
            type == typeof(char) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal) ||
            type == typeof(string) ||
            type.IsEnum;
    }

    /// <summary>根据字段类型和内容计算冻结配置的稳定指纹。</summary>
    internal static class FrozenConfigHash
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Complete(ulong hash) => hash == 0UL ? OffsetBasis : hash;

        public static ulong ComputeSnapshot<TSchema>(
            FrozenConfigSchema<TSchema> schema,
            object[] values)
            where TSchema : struct, IFrozenConfigSchema
        {
            ulong hash = OffsetBasis;
            Add(ref hash, schema.Fingerprint);
            Add(ref hash, unchecked((ulong)values.Length));
            for (int index = 0; index < schema.FieldCount; index++)
            {
                FrozenConfigFieldDefinition definition = schema.GetDefinition(index);
                Add(ref hash, definition.FieldId.High);
                Add(ref hash, definition.FieldId.Low);
                if (definition.IsArray)
                {
                    ((IFrozenArrayValue)values[index]).AddToHash(ref hash);
                }
                else
                {
                    AddScalar(ref hash, definition.ValueType, values[index]);
                }
            }

            return Complete(hash);
        }

        public static void AddScalar(ref ulong hash, Type type, object value)
        {
            if (type.IsEnum)
            {
                Type underlying = Enum.GetUnderlyingType(type);
                object normalized = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                AddIntegral(ref hash, underlying, normalized);
                return;
            }

            if (type == typeof(string))
            {
                AddString(ref hash, (string)value);
                return;
            }

            if (type == typeof(decimal))
            {
                int[] bits = decimal.GetBits((decimal)value);
                for (int index = 0; index < bits.Length; index++)
                {
                    Add(ref hash, unchecked((ulong)(uint)bits[index]));
                }

                return;
            }

            if (type == typeof(float))
            {
                var bits = new SingleBits { Value = (float)value };
                Add(ref hash, bits.Bits);
                return;
            }

            if (type == typeof(double))
            {
                Add(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)));
                return;
            }

            if (type == typeof(bool))
            {
                Add(ref hash, (bool)value ? 1UL : 0UL);
                return;
            }

            if (type == typeof(char))
            {
                Add(ref hash, (char)value);
                return;
            }

            AddIntegral(ref hash, type, value);
        }

        public static void AddType(ref ulong hash, Type type)
        {
            AddString(ref hash, GraphTypeIdentity.AssemblyName(type));
            AddString(ref hash, GraphTypeIdentity.FullName(type));
        }

        public static void Add(ref ulong hash, ulong value)
        {
            for (int index = 0; index < 8; index++)
            {
                hash ^= (byte)(value >> (index * 8));
                hash *= Prime;
            }
        }

        private static void AddIntegral(ref ulong hash, Type type, object value)
        {
            if (type == typeof(byte))
            {
                Add(ref hash, (byte)value);
            }
            else if (type == typeof(sbyte))
            {
                Add(ref hash, unchecked((ulong)(sbyte)value));
            }
            else if (type == typeof(short))
            {
                Add(ref hash, unchecked((ulong)(short)value));
            }
            else if (type == typeof(ushort))
            {
                Add(ref hash, (ushort)value);
            }
            else if (type == typeof(int))
            {
                Add(ref hash, unchecked((ulong)(int)value));
            }
            else if (type == typeof(uint))
            {
                Add(ref hash, (uint)value);
            }
            else if (type == typeof(long))
            {
                Add(ref hash, unchecked((ulong)(long)value));
            }
            else
            {
                Add(ref hash, (ulong)value);
            }
        }

        private static void AddString(ref ulong hash, string value)
        {
            Add(ref hash, unchecked((ulong)value.Length));
            for (int index = 0; index < value.Length; index++)
            {
                Add(ref hash, value[index]);
            }
        }

        /// <summary>将单精度值的位模式用于精确的内容散列。</summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct SingleBits
        {
            [FieldOffset(0)] public float Value;
            [FieldOffset(0)] public uint Bits;
        }
    }
}
