using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>
    /// Marks the stable field schema used by one family of frozen configuration snapshots.
    /// Schema marker types are value types so they cannot carry mutable runtime state.
    /// </summary>
    public interface IFrozenConfigSchema
    {
    }

    /// <summary>
    /// Stable 128-bit identity for a field in a frozen configuration schema.
    /// </summary>
    public readonly struct FrozenConfigFieldId : IEquatable<FrozenConfigFieldId>
    {
        private FrozenConfigFieldId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

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

        public static bool TryParse(string value, out FrozenConfigFieldId id)
        {
            if (!GraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        public bool Equals(FrozenConfigFieldId other) => High == other.High && Low == other.Low;
        public override bool Equals(object obj) => obj is FrozenConfigFieldId other && Equals(other);
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(FrozenConfigFieldId left, FrozenConfigFieldId right) =>
            left.Equals(right);

        public static bool operator !=(FrozenConfigFieldId left, FrozenConfigFieldId right) =>
            !left.Equals(right);
    }

    public readonly struct FrozenConfigField<TSchema, TValue> :
        IEquatable<FrozenConfigField<TSchema, TValue>>
        where TSchema : struct, IFrozenConfigSchema
    {
        internal FrozenConfigField(FrozenConfigFieldId fieldId)
        {
            FieldId = fieldId;
        }

        public FrozenConfigFieldId FieldId { get; }
        public bool IsValid => FieldId.IsValid;

        public bool Equals(FrozenConfigField<TSchema, TValue> other) => FieldId == other.FieldId;
        public override bool Equals(object obj) =>
            obj is FrozenConfigField<TSchema, TValue> other && Equals(other);
        public override int GetHashCode() => FieldId.GetHashCode();

        public static bool operator ==(
            FrozenConfigField<TSchema, TValue> left,
            FrozenConfigField<TSchema, TValue> right) => left.Equals(right);

        public static bool operator !=(
            FrozenConfigField<TSchema, TValue> left,
            FrozenConfigField<TSchema, TValue> right) => !left.Equals(right);
    }

    public readonly struct FrozenConfigArrayField<TSchema, TElement> :
        IEquatable<FrozenConfigArrayField<TSchema, TElement>>
        where TSchema : struct, IFrozenConfigSchema
    {
        internal FrozenConfigArrayField(FrozenConfigFieldId fieldId)
        {
            FieldId = fieldId;
        }

        public FrozenConfigFieldId FieldId { get; }
        public bool IsValid => FieldId.IsValid;

        public bool Equals(FrozenConfigArrayField<TSchema, TElement> other) =>
            FieldId == other.FieldId;
        public override bool Equals(object obj) =>
            obj is FrozenConfigArrayField<TSchema, TElement> other && Equals(other);
        public override int GetHashCode() => FieldId.GetHashCode();

        public static bool operator ==(
            FrozenConfigArrayField<TSchema, TElement> left,
            FrozenConfigArrayField<TSchema, TElement> right) => left.Equals(right);

        public static bool operator !=(
            FrozenConfigArrayField<TSchema, TElement> left,
            FrozenConfigArrayField<TSchema, TElement> right) => !left.Equals(right);
    }

    /// <summary>
    /// Builds a canonical, immutable field schema. Registration order does not affect its identity.
    /// </summary>
    public sealed class FrozenConfigSchemaBuilder<TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        private readonly List<FrozenConfigFieldDefinition> _definitions =
            new List<FrozenConfigFieldDefinition>();
        private readonly HashSet<FrozenConfigFieldId> _fieldIds =
            new HashSet<FrozenConfigFieldId>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

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
    }

    /// <summary>
    /// Canonical immutable definition of one frozen configuration shape.
    /// </summary>
    public sealed class FrozenConfigSchema<TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        private readonly FrozenConfigFieldDefinition[] _definitions;
        private readonly Dictionary<FrozenConfigFieldId, int> _indices;

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

        public Type SchemaType => typeof(TSchema);
        public int FieldCount => _definitions.Length;
        public ulong Fingerprint { get; }
        public bool IsValid => Fingerprint != 0UL;

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

    }

    /// <summary>
    /// A one-shot writer owned by the framework for one invocation of an author supplied freezer.
    /// Any invalid write permanently fails the writer, and sealing makes retained references inert.
    /// </summary>
    public sealed class FrozenConfigWriter<TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        private readonly FrozenConfigSchema<TSchema> _schema;
        private readonly object[] _values;
        private readonly bool[] _written;
        private CoCoDiagnostic _failure;
        private bool _isSealed;

        internal FrozenConfigWriter(FrozenConfigSchema<TSchema> schema)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _values = new object[schema.FieldCount];
            _written = new bool[schema.FieldCount];
        }

        public bool IsSealed => _isSealed;
        public bool HasFailed => !_failure.IsNone;

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
    }

    /// <summary>
    /// Owns a defensive copy of a fixed configuration sequence without exposing its backing array.
    /// </summary>
    public sealed class FrozenArray<T> : IReadOnlyList<T>, IFrozenArrayValue
    {
        private readonly T[] _items;

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

        public int Count => _items.Length;
        public T this[int index] => _items[index];

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        void IFrozenArrayValue.AddToHash(ref ulong hash)
        {
            FrozenConfigHash.Add(ref hash, unchecked((ulong)_items.Length));
            for (int index = 0; index < _items.Length; index++)
            {
                FrozenConfigHash.AddScalar(ref hash, typeof(T), _items[index]);
            }
        }
    }

    public interface IConfigFreezer<TAuthoringConfig, TSchema>
        where TSchema : struct, IFrozenConfigSchema
    {
        bool TryFreeze(
            TAuthoringConfig source,
            FrozenConfigWriter<TSchema> writer,
            out CoCoDiagnostic diagnostic);
    }

    public sealed class FrozenConfigSnapshot
    {
        private readonly Type _schemaType;
        private readonly ulong _schemaFingerprint;
        private readonly FrozenConfigFieldDefinition[] _definitions;
        private readonly Dictionary<FrozenConfigFieldId, int> _indices;
        private readonly object[] _values;

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

        public Type SchemaType => _schemaType;
        public ulong SchemaFingerprint => _schemaFingerprint;
        public ulong Fingerprint { get; }
        public bool IsValid => SchemaType != null &&
                               SchemaFingerprint != 0UL &&
                               Fingerprint != 0UL;

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
    }

    public abstract class StateRuntimeRegistration
    {
        protected StateRuntimeRegistration(
            Type logicType,
            Type configSchemaType,
            ulong configSchemaFingerprint,
            Type activationMemoryType,
            bool providesActionProgress)
        {
            LogicType = logicType;
            ConfigSchemaType = configSchemaType;
            ConfigSchemaFingerprint = configSchemaFingerprint;
            ActivationMemoryType = activationMemoryType;
            ProvidesActionProgress = providesActionProgress;
        }

        public Type LogicType { get; }
        public Type ConfigSchemaType { get; }
        public ulong ConfigSchemaFingerprint { get; }
        public Type ActivationMemoryType { get; }
        public bool ProvidesActionProgress { get; }
    }

    public sealed class StateRuntimeRegistration<TLogic, TSchema, TMemory> :
        StateRuntimeRegistration
        where TLogic : StateLogic
        where TSchema : struct, IFrozenConfigSchema
        where TMemory : ActivationMemory
    {
        public StateRuntimeRegistration(
            FrozenConfigSchema<TSchema> configSchema,
            bool providesActionProgress = false)
            : base(
                typeof(TLogic),
                typeof(TSchema),
                RequireSchema(configSchema).Fingerprint,
                typeof(TMemory),
                providesActionProgress)
        {
            ConfigSchema = configSchema;
        }

        public FrozenConfigSchema<TSchema> ConfigSchema { get; }

        private static FrozenConfigSchema<TSchema> RequireSchema(
            FrozenConfigSchema<TSchema> schema) =>
            schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public abstract class ConditionRuntimeRegistration
    {
        protected ConditionRuntimeRegistration(
            Type conditionType,
            Type configSchemaType,
            ulong configSchemaFingerprint)
        {
            ConditionType = conditionType;
            ConfigSchemaType = configSchemaType;
            ConfigSchemaFingerprint = configSchemaFingerprint;
        }

        public Type ConditionType { get; }
        public Type ConfigSchemaType { get; }
        public ulong ConfigSchemaFingerprint { get; }
    }

    public sealed class ConditionRuntimeRegistration<TCondition, TSchema> :
        ConditionRuntimeRegistration
        where TCondition : StateCondition
        where TSchema : struct, IFrozenConfigSchema
    {
        public ConditionRuntimeRegistration(FrozenConfigSchema<TSchema> configSchema)
            : base(
                typeof(TCondition),
                typeof(TSchema),
                RequireSchema(configSchema).Fingerprint)
        {
            ConfigSchema = configSchema;
        }

        public FrozenConfigSchema<TSchema> ConfigSchema { get; }

        private static FrozenConfigSchema<TSchema> RequireSchema(
            FrozenConfigSchema<TSchema> schema) =>
            schema ?? throw new ArgumentNullException(nameof(schema));
    }

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

    internal interface IFrozenArrayValue
    {
        void AddToHash(ref ulong hash);
    }

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

        [StructLayout(LayoutKind.Explicit)]
        private struct SingleBits
        {
            [FieldOffset(0)] public float Value;
            [FieldOffset(0)] public uint Bits;
        }
    }
}
