using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal sealed class CoCoStateGraphConfigFreezer
    {
        private readonly CoCoGraphDescriptorCatalog catalog;
        private readonly HashSet<object> activeReferences =
            new HashSet<object>(ReferenceComparer.Instance);
        private readonly Dictionary<object, bool> referenceSerializationModes =
            new Dictionary<object, bool>(ReferenceComparer.Instance);

        internal CoCoStateGraphConfigFreezer(CoCoGraphDescriptorCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>
        /// Computes a deterministic cache fingerprint from the explicitly supported authoring-data
        /// envelope. This never converts authoring data into runtime config and stops at forbidden
        /// values instead of treating reflection as a freezer fallback.
        /// </summary>
        internal static ulong ComputeAuthoringFingerprint(object source)
        {
            var builder = new AuthoringFingerprintBuilder();
            builder.AddValue(source?.GetType(), source);
            return builder.Fingerprint;
        }

        internal bool TryFreezeState(
            CoCoStateDescriptorId descriptorId,
            CoCoStateConfig source,
            CoCoGraphDiagnosticLocation location,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoGraphDiagnostic diagnostic)
        {
            if (!TryValidateAuthoringConfig(source, out CoCoDiagnostic validationDiagnostic))
            {
                snapshot = null;
                diagnostic = new CoCoGraphDiagnostic(validationDiagnostic, location);
                return false;
            }

            if (!catalog.TryFreezeStateConfig(
                    descriptorId,
                    source,
                    out snapshot,
                    out CoCoDiagnostic freezeDiagnostic))
            {
                diagnostic = new CoCoGraphDiagnostic(freezeDiagnostic, location);
                return false;
            }

            if (snapshot == null || !snapshot.IsValid)
            {
                snapshot = null;
                diagnostic = Error(location, "State config freezer returned an invalid frozen snapshot.");
                return false;
            }

            diagnostic = default;
            return true;
        }

        internal bool TryFreezeCondition(
            CoCoConditionDescriptorId descriptorId,
            CoCoConditionConfig source,
            CoCoGraphDiagnosticLocation location,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoGraphDiagnostic diagnostic)
        {
            if (!TryValidateAuthoringConfig(source, out CoCoDiagnostic validationDiagnostic))
            {
                snapshot = null;
                diagnostic = new CoCoGraphDiagnostic(validationDiagnostic, location);
                return false;
            }

            if (!catalog.TryFreezeConditionConfig(
                    descriptorId,
                    source,
                    out snapshot,
                    out CoCoDiagnostic freezeDiagnostic))
            {
                diagnostic = new CoCoGraphDiagnostic(freezeDiagnostic, location);
                return false;
            }

            if (snapshot == null || !snapshot.IsValid)
            {
                snapshot = null;
                diagnostic = Error(location, "Condition config freezer returned an invalid frozen snapshot.");
                return false;
            }

            diagnostic = default;
            return true;
        }

        private bool TryValidateAuthoringConfig(object source, out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            activeReferences.Clear();
            referenceSerializationModes.Clear();
            try
            {
                // Each top-level Config is itself stored through SerializeReference on the Asset.
                return TryValidateValue(source.GetType(), source, true, 0, out diagnostic);
            }
            finally
            {
                activeReferences.Clear();
                referenceSerializationModes.Clear();
            }
        }

        private bool TryValidateValue(
            Type declaredType,
            object value,
            bool preservesReferenceIdentity,
            int collectionDepth,
            out CoCoDiagnostic diagnostic)
        {
            Type valueType = value?.GetType() ?? declaredType;
            if (!TryValidateDeclaredShape(
                    valueType,
                    collectionDepth,
                    new HashSet<Type>(),
                    out diagnostic))
            {
                return false;
            }

            bool isCollection = valueType.IsArray || IsList(valueType);
            if (value == null)
            {
                bool nullRoundTrips = preservesReferenceIdentity &&
                                      !isCollection &&
                                      valueType != typeof(string) &&
                                      !valueType.IsValueType;
                if (!nullRoundTrips)
                {
                    diagnostic = AuthoringError(
                        "Null Config values require SerializeReference and cannot be collection containers or strings.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (IsAllowedScalar(valueType))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!preservesReferenceIdentity && declaredType != valueType)
            {
                diagnostic = AuthoringError(
                    "Polymorphic Config values require an explicit SerializeReference field.");
                return false;
            }

            if (isCollection && collectionDepth > 0)
            {
                diagnostic = AuthoringError(
                    "Config collections must be one-dimensional and may not contain nested collections.");
                return false;
            }

            // SerializeReference on an array/List field applies to its elements, not to the
            // collection object. The container itself always has inline value semantics.
            bool valuePreservesReferenceIdentity = preservesReferenceIdentity && !isCollection;
            bool tracksReference = !valueType.IsValueType;
            if (tracksReference && activeReferences.Contains(value))
            {
                diagnostic = AuthoringError("Config data may not contain object cycles.");
                return false;
            }

            if (tracksReference &&
                referenceSerializationModes.TryGetValue(value, out bool previousPreservesIdentity) &&
                (!valuePreservesReferenceIdentity || !previousPreservesIdentity))
            {
                diagnostic = AuthoringError(
                    "Shared Config references require SerializeReference at every use site.");
                return false;
            }

            if (tracksReference)
            {
                referenceSerializationModes[value] = valuePreservesReferenceIdentity;
                activeReferences.Add(value);
            }

            try
            {
                if (valueType.IsArray)
                {
                    var array = (Array)value;
                    if (array.Rank != 1)
                    {
                        diagnostic = AuthoringError("Config arrays must be one-dimensional.");
                        return false;
                    }

                    Type elementType = valueType.GetElementType();
                    foreach (object item in array)
                    {
                        if (!TryValidateValue(
                                elementType,
                                item,
                                preservesReferenceIdentity,
                                collectionDepth + 1,
                                out diagnostic))
                        {
                            return false;
                        }
                    }

                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                if (IsList(valueType))
                {
                    Type elementType = valueType.GetGenericArguments()[0];
                    foreach (object item in (IEnumerable)value)
                    {
                        if (!TryValidateValue(
                                elementType,
                                item,
                                preservesReferenceIdentity,
                                collectionDepth + 1,
                                out diagnostic))
                        {
                            return false;
                        }
                    }

                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                if (valueType.GetProperties(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic).Length > 0)
                {
                    diagnostic = AuthoringError(
                        $"Config type '{valueType.FullName}' must use serialized data fields, not properties.");
                    return false;
                }

                foreach (FieldInfo field in GetSerializableFields(valueType))
                {
                    if (!TryValidateFieldMetadata(field, out diagnostic))
                    {
                        return false;
                    }

                    object fieldValue = field.GetValue(value);
                    bool fieldPreservesReferenceIdentity =
                        field.IsDefined(typeof(SerializeReference), true);
                    if (!TryValidateValue(
                            field.FieldType,
                            fieldValue,
                            fieldPreservesReferenceIdentity,
                            0,
                            out diagnostic))
                    {
                        return false;
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            finally
            {
                if (tracksReference)
                {
                    activeReferences.Remove(value);
                }
            }
        }

        private static bool TryValidateDeclaredShape(
            Type type,
            int collectionDepth,
            ISet<Type> activeTypes,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateType(type, out diagnostic))
            {
                return false;
            }

            if (IsAllowedScalar(type))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    diagnostic = AuthoringError("Config arrays must be one-dimensional.");
                    return false;
                }

                if (collectionDepth > 0)
                {
                    diagnostic = AuthoringError(
                        "Config collections must be one-dimensional and may not contain nested collections.");
                    return false;
                }

                return TryValidateDeclaredShape(
                    type.GetElementType(),
                    collectionDepth + 1,
                    activeTypes,
                    out diagnostic);
            }

            if (IsList(type))
            {
                if (collectionDepth > 0)
                {
                    diagnostic = AuthoringError(
                        "Config collections must be one-dimensional and may not contain nested collections.");
                    return false;
                }

                return TryValidateDeclaredShape(
                    type.GetGenericArguments()[0],
                    collectionDepth + 1,
                    activeTypes,
                    out diagnostic);
            }

            if (!activeTypes.Add(type))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                if (type.GetProperties(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic).Length > 0)
                {
                    diagnostic = AuthoringError(
                        $"Config type '{type.FullName}' must use serialized data fields, not properties.");
                    return false;
                }

                foreach (FieldInfo field in GetSerializableFields(type))
                {
                    if (!TryValidateFieldMetadata(field, out diagnostic) ||
                        !TryValidateDeclaredShape(
                            field.FieldType,
                            0,
                            activeTypes,
                            out diagnostic))
                    {
                        return false;
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            finally
            {
                activeTypes.Remove(type);
            }
        }

        private static bool TryValidateFieldMetadata(
            FieldInfo field,
            out CoCoDiagnostic diagnostic)
        {
            if (!IsSerializedDataField(field))
            {
                diagnostic = AuthoringError(
                    $"Config type '{field.DeclaringType?.FullName}' contains non-serialized instance state in field '{field.Name}'.");
                return false;
            }

            if (field.IsDefined(typeof(SerializeReference), true) &&
                !SupportsSerializeReference(field.FieldType))
            {
                diagnostic = AuthoringError(
                    $"Config field '{field.Name}' uses SerializeReference with an unsupported value type.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool SupportsSerializeReference(Type fieldType)
        {
            Type referencedType = fieldType;
            if (fieldType.IsArray)
            {
                if (fieldType.GetArrayRank() != 1)
                {
                    return false;
                }

                referencedType = fieldType.GetElementType();
            }
            else if (IsList(fieldType))
            {
                referencedType = fieldType.GetGenericArguments()[0];
            }

            return referencedType != null &&
                   !referencedType.IsValueType &&
                   referencedType != typeof(string) &&
                   !referencedType.IsArray &&
                   !IsList(referencedType) &&
                   !typeof(UnityEngine.Object).IsAssignableFrom(referencedType);
        }

        private static bool TryValidateType(Type type, out CoCoDiagnostic diagnostic)
        {
            if (type == null ||
                typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                IsUnityType(type) ||
                typeof(Delegate).IsAssignableFrom(type) ||
                typeof(IDictionary).IsAssignableFrom(type) ||
                IsTopologyId(type))
            {
                diagnostic = AuthoringError(
                    $"Config type '{type?.FullName ?? "<null>"}' is outside the pure authoring envelope.");
                return false;
            }

            if (IsAllowedScalar(type) || type.IsArray || IsList(type))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (type.IsEnum)
            {
                diagnostic = AuthoringError(
                    $"Config enum '{type.FullName}' must use a 32-bit-or-smaller underlying type.");
                return false;
            }

            if ((type.Namespace != null &&
                 type.Namespace.StartsWith("System", StringComparison.Ordinal)) ||
                !type.IsSerializable)
            {
                diagnostic = AuthoringError(
                    $"Config type '{type.FullName}' must be a serializable pure data type.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static IEnumerable<FieldInfo> GetSerializableFields(Type type)
        {
            var hierarchy = new Stack<Type>();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                hierarchy.Push(current);
            }

            while (hierarchy.Count > 0)
            {
                Type current = hierarchy.Pop();
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                Array.Sort(fields, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic)
                    {
                        continue;
                    }

                    yield return field;
                }
            }
        }

        private static bool IsSerializedDataField(FieldInfo field)
        {
            return !field.IsNotSerialized &&
                   !field.IsInitOnly &&
                   (field.IsPublic ||
                    field.IsDefined(typeof(SerializeField), true) ||
                    field.IsDefined(typeof(SerializeReference), true));
        }

        private static bool IsAllowedScalar(Type type)
        {
            return IsSupportedEnum(type) ||
                   type == typeof(bool) ||
                   type == typeof(byte) ||
                   type == typeof(sbyte) ||
                   type == typeof(short) ||
                   type == typeof(ushort) ||
                   type == typeof(int) ||
                   type == typeof(uint) ||
                   type == typeof(long) ||
                   type == typeof(ulong) ||
                   type == typeof(float) ||
                   type == typeof(double) ||
                   type == typeof(char) ||
                   type == typeof(string);
        }

        private static bool IsSupportedEnum(Type type)
        {
            if (!type.IsEnum)
            {
                return false;
            }

            Type underlyingType = Enum.GetUnderlyingType(type);
            return underlyingType != typeof(long) && underlyingType != typeof(ulong);
        }

        private static bool IsList(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

        private static bool IsUnityType(Type type)
        {
            string assemblyName = type.Assembly.GetName().Name;
            return assemblyName.StartsWith("Unity.", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal);
        }

        private static bool IsTopologyId(Type type) =>
            type == typeof(CoCoGraphId) ||
            type == typeof(CoCoLayerId) ||
            type == typeof(CoCoStateId) ||
            type == typeof(CoCoTransitionId);

        private static CoCoGraphDiagnostic Error(
            CoCoGraphDiagnosticLocation location,
            string message) =>
            new CoCoGraphDiagnostic(AuthoringError(message), location);

        private static CoCoDiagnostic AuthoringError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidAuthoringDependency,
                message);

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object left, object right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }

        private sealed class AuthoringFingerprintBuilder
        {
            private const ulong Offset = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;

            private readonly Dictionary<object, int> referenceOrdinals =
                new Dictionary<object, int>(ReferenceComparer.Instance);
            private ulong hash = Offset;

            internal ulong Fingerprint => hash == 0UL ? Offset : hash;

            internal void AddValue(Type declaredType, object value)
            {
                Add(0xA0UL);
                AddType(declaredType);
                if (value == null)
                {
                    Add(0xA1UL);
                    return;
                }

                Type valueType = value.GetType();
                AddType(valueType);
                if (IsAllowedScalar(valueType))
                {
                    Add(0xA2UL);
                    AddScalar(valueType, value);
                    return;
                }

                if (!valueType.IsValueType)
                {
                    if (referenceOrdinals.TryGetValue(value, out int ordinal))
                    {
                        Add(0xA3UL);
                        Add(ordinal);
                        return;
                    }

                    referenceOrdinals.Add(value, referenceOrdinals.Count);
                }

                if (valueType.IsArray)
                {
                    AddArray(valueType, (Array)value);
                    return;
                }

                if (IsList(valueType))
                {
                    AddList(valueType, (IList)value);
                    return;
                }

                if (IsForbiddenValueType(valueType))
                {
                    // The type and this marker are sufficient to distinguish the deterministic
                    // failure class. Forbidden values are deliberately never inspected.
                    Add(0xAFUL);
                    return;
                }

                Add(0xA4UL);
                FieldInfo[] fields;
                try
                {
                    fields = ToArray(GetSerializableFields(valueType));
                }
                catch (Exception exception)
                {
                    Add(0xAEUL);
                    AddType(exception.GetType());
                    return;
                }

                Add(fields.Length);
                foreach (FieldInfo field in fields)
                {
                    AddType(field.DeclaringType);
                    Add(field.Name);
                    AddType(field.FieldType);
                    try
                    {
                        AddValue(field.FieldType, field.GetValue(value));
                    }
                    catch (Exception exception)
                    {
                        Add(0xADUL);
                        AddType(exception.GetType());
                    }
                }
            }

            private void AddArray(Type arrayType, Array array)
            {
                Add(0xA5UL);
                Add(array.Rank);
                for (int dimension = 0; dimension < array.Rank; dimension++)
                {
                    Add(array.GetLength(dimension));
                }

                Type elementType = arrayType.GetElementType();
                foreach (object item in array)
                {
                    AddValue(elementType, item);
                }
            }

            private void AddList(Type listType, IList list)
            {
                Add(0xA6UL);
                Add(list.Count);
                Type elementType = listType.GetGenericArguments()[0];
                foreach (object item in list)
                {
                    AddValue(elementType, item);
                }
            }

            private void AddScalar(Type type, object value)
            {
                if (type.IsEnum)
                {
                    Type underlyingType = Enum.GetUnderlyingType(type);
                    AddType(underlyingType);
                    switch (Type.GetTypeCode(underlyingType))
                    {
                        case TypeCode.SByte:
                        case TypeCode.Int16:
                        case TypeCode.Int32:
                        case TypeCode.Int64:
                            Add(unchecked((ulong)Convert.ToInt64(value)));
                            return;
                        default:
                            Add(Convert.ToUInt64(value));
                            return;
                    }
                }

                switch (value)
                {
                    case bool boolValue:
                        Add(boolValue ? 1UL : 0UL);
                        break;
                    case byte byteValue:
                        Add(byteValue);
                        break;
                    case sbyte sbyteValue:
                        Add(unchecked((ulong)sbyteValue));
                        break;
                    case short shortValue:
                        Add(unchecked((ulong)shortValue));
                        break;
                    case ushort ushortValue:
                        Add(ushortValue);
                        break;
                    case int intValue:
                        Add(unchecked((ulong)intValue));
                        break;
                    case uint uintValue:
                        Add(uintValue);
                        break;
                    case long longValue:
                        Add(unchecked((ulong)longValue));
                        break;
                    case ulong ulongValue:
                        Add(ulongValue);
                        break;
                    case float floatValue:
                        Add(unchecked((ulong)BitConverter.ToInt32(
                            BitConverter.GetBytes(floatValue),
                            0)));
                        break;
                    case double doubleValue:
                        Add(unchecked((ulong)BitConverter.DoubleToInt64Bits(doubleValue)));
                        break;
                    case char charValue:
                        Add(charValue);
                        break;
                    case string stringValue:
                        Add(stringValue);
                        break;
                }
            }

            private static bool IsForbiddenValueType(Type type)
            {
                return typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                       IsUnityType(type) ||
                       typeof(Delegate).IsAssignableFrom(type) ||
                       typeof(IDictionary).IsAssignableFrom(type) ||
                       IsTopologyId(type) ||
                       (type.Namespace != null &&
                        type.Namespace.StartsWith("System", StringComparison.Ordinal)) ||
                       !type.IsSerializable;
            }

            private static FieldInfo[] ToArray(IEnumerable<FieldInfo> fields)
            {
                var result = new List<FieldInfo>();
                foreach (FieldInfo field in fields)
                {
                    result.Add(field);
                }

                return result.ToArray();
            }

            private void AddType(Type type)
            {
                if (type == null)
                {
                    Add(0UL);
                    return;
                }

                Add(type.Assembly.GetName().Name);
                Add(type.FullName ?? type.Name);
            }

            private void Add(string value)
            {
                if (value == null)
                {
                    Add(-1);
                    return;
                }

                Add(value.Length);
                foreach (char character in value)
                {
                    Add(character);
                }
            }

            private void Add(int value) => Add(unchecked((ulong)value));

            private void Add(ulong value)
            {
                for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
                {
                    hash ^= (byte)(value >> (byteIndex * 8));
                    hash *= Prime;
                }
            }
        }
    }
}
