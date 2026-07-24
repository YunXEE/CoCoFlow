using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    internal static class RegionPlanPurityValidator
    {
        internal static bool TryValidate(
            IRegionParticipantPlan plan,
            out string failure)
        {
            if (plan == null)
            {
                failure = "The frozen participant plan is null.";
                return false;
            }

            var visited = new HashSet<object>(
                ReferenceIdentityComparer.Instance);
            return TryValidateValue(
                plan,
                plan.GetType(),
                plan.GetType().FullName ?? plan.GetType().Name,
                visited,
                out failure);
        }

        private static bool TryValidateValue(
            object value,
            Type declaredType,
            string path,
            ISet<object> visited,
            out string failure)
        {
            Type nullableType = Nullable.GetUnderlyingType(declaredType);
            if (nullableType != null)
            {
                declaredType = nullableType;
            }

            if (TryRejectType(declaredType, path, out failure))
            {
                return false;
            }

            if (value == null)
            {
                failure = string.Empty;
                return true;
            }

            Type runtimeType = value.GetType();
            if (TryRejectType(runtimeType, path, out failure))
            {
                return false;
            }

            if (IsTerminalValue(runtimeType))
            {
                failure = string.Empty;
                return true;
            }

            if (!runtimeType.IsValueType && !visited.Add(value))
            {
                failure = string.Empty;
                return true;
            }

            if (IsApprovedReadOnlyCollection(runtimeType))
            {
                int index = 0;
                foreach (object element in (IEnumerable)value)
                {
                    Type elementType =
                        element == null ? typeof(object) : element.GetType();
                    if (!TryValidateValue(
                            element,
                            elementType,
                            path + "[" + index + "]",
                            visited,
                            out failure))
                    {
                        return false;
                    }

                    index++;
                }

                failure = string.Empty;
                return true;
            }

            if (typeof(IEnumerable).IsAssignableFrom(runtimeType))
            {
                failure =
                    path + " uses mutable or unapproved collection type '" +
                    runtimeType.FullName + "'.";
                return false;
            }

            for (Type current = runtimeType;
                 current != null && current != typeof(object);
                 current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                Array.Sort(
                    fields,
                    (left, right) => string.CompareOrdinal(
                        left.Name,
                        right.Name));
                for (int index = 0; index < fields.Length; index++)
                {
                    FieldInfo field = fields[index];
                    string fieldPath = path + "." + field.Name;
                    if (!runtimeType.IsValueType && !field.IsInitOnly)
                    {
                        failure =
                            fieldPath +
                            " is mutable; participant plan reference fields must be readonly.";
                        return false;
                    }

                    if (TryRejectType(
                            field.FieldType,
                            fieldPath,
                            out failure))
                    {
                        return false;
                    }

                    object fieldValue;
                    try
                    {
                        fieldValue = field.GetValue(value);
                    }
                    catch (Exception exception)
                    {
                        failure =
                            fieldPath +
                            " could not be inspected fail-closed: " +
                            exception.Message;
                        return false;
                    }

                    if (!TryValidateValue(
                            fieldValue,
                            field.FieldType,
                            fieldPath,
                            visited,
                            out failure))
                    {
                        return false;
                    }
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryRejectType(
            Type type,
            string path,
            out string failure)
        {
            if (type == null)
            {
                failure = path + " has no stable runtime type.";
                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                failure =
                    path + " references UnityEngine.Object type '" +
                    type.FullName + "'.";
                return true;
            }

            if (type == typeof(UnityEngine.SceneManagement.Scene) ||
                type == typeof(PhysicsScene) ||
                type == typeof(PhysicsScene2D))
            {
                failure =
                    path + " references Unity runtime authority type '" +
                    type.FullName + "'.";
                return true;
            }

            if (type == typeof(IntPtr) ||
                type == typeof(UIntPtr) ||
                type.IsPointer ||
                type.IsByRef ||
                type.IsByRefLike)
            {
                failure =
                    path + " references native/backend handle type '" +
                    type.FullName + "'.";
                return true;
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                failure =
                    path + " references delegate type '" +
                    type.FullName + "'.";
                return true;
            }

            if (typeof(Task).IsAssignableFrom(type) ||
                IsTaskLikeValueType(type))
            {
                failure =
                    path + " references task type '" +
                    type.FullName + "'.";
                return true;
            }

            if (typeof(IDisposable).IsAssignableFrom(type))
            {
                failure =
                    path + " references disposable authority type '" +
                    type.FullName + "'.";
                return true;
            }

            if (type.IsArray || IsMutableCollectionType(type))
            {
                failure =
                    path + " references mutable collection type '" +
                    type.FullName + "'.";
                return true;
            }

            failure = string.Empty;
            return false;
        }

        private static bool IsTerminalValue(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(TimeSpan) ||
                   type == typeof(Guid);
        }

        private static bool IsApprovedReadOnlyCollection(Type type)
        {
            if (!type.IsGenericType) return false;

            Type definition = type.GetGenericTypeDefinition();
            return definition == typeof(RegionImmutableArray<>);
        }

        private static bool IsMutableCollectionType(Type type)
        {
            if (typeof(IList).IsAssignableFrom(type) ||
                typeof(IDictionary).IsAssignableFrom(type))
            {
                return !IsApprovedReadOnlyCollection(type);
            }

            Type[] interfaces = type.IsInterface
                ? new[] { type }
                : type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type candidate = interfaces[index];
                if (!candidate.IsGenericType) continue;

                Type definition = candidate.GetGenericTypeDefinition();
                if (definition == typeof(ICollection<>) ||
                    definition == typeof(IDictionary<,>) ||
                    definition == typeof(ISet<>))
                {
                    return !IsApprovedReadOnlyCollection(type);
                }
            }

            return false;
        }

        private static bool IsTaskLikeValueType(Type type)
        {
            string fullName = type.FullName ?? string.Empty;
            return string.Equals(
                       fullName,
                       "System.Threading.Tasks.ValueTask",
                       StringComparison.Ordinal) ||
                   fullName.StartsWith(
                       "System.Threading.Tasks.ValueTask`",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       fullName,
                       "Cysharp.Threading.Tasks.UniTask",
                       StringComparison.Ordinal) ||
                   fullName.StartsWith(
                       "Cysharp.Threading.Tasks.UniTask`",
                       StringComparison.Ordinal);
        }

        private sealed class ReferenceIdentityComparer :
            IEqualityComparer<object>
        {
            internal static readonly ReferenceIdentityComparer Instance =
                new ReferenceIdentityComparer();

            public new bool Equals(object left, object right) =>
                ReferenceEquals(left, right);

            public int GetHashCode(object value) =>
                RuntimeHelpers.GetHashCode(value);
        }
    }
}
