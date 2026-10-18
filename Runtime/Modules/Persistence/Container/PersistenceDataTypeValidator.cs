using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    internal static class PersistenceDataTypeValidator
    {
        private static readonly Dictionary<Type, FieldInfo[]> Fields = new Dictionary<Type, FieldInfo[]>();
        private static readonly HashSet<Type> ValidTypes = new HashSet<Type>();

        internal static void Validate(Type type)
        {
            if (type == null) throw new ArgumentException("A PersistenceData struct type is required.");
            if (ValidTypes.Contains(type)) return;
            ValidateStruct(type, type.FullName, new HashSet<Type>());
            ValidTypes.Add(type);
        }

        internal static bool TryValidate(Type type, out string diagnostic)
        {
            try
            {
                Validate(type);
                diagnostic = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                diagnostic = exception.Message;
                return false;
            }
        }

        internal static FieldInfo[] GetFields(Type type)
        {
            if (!Fields.TryGetValue(type, out FieldInfo[] fields))
            {
                fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(field => !field.IsInitOnly && !field.IsNotSerialized)
                    .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
                Fields.Add(type, fields);
            }
            return fields;
        }

        internal static bool IsLeaf(Type type)
        {
            return type == typeof(string) || type.IsEnum || type == typeof(bool) || type == typeof(char) ||
                type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
                type == typeof(float) || type == typeof(double);
        }

        internal static bool TryGetElementType(Type type, out Type elementType)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return true;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
            elementType = null;
            return false;
        }

        private static void ValidateStruct(Type type, string path, HashSet<Type> visiting)
        {
            if (!type.IsValueType || type.IsPrimitive || type.IsEnum || type.ContainsGenericParameters ||
                !type.IsDefined(typeof(SerializableAttribute), false) ||
                !type.IsDefined(typeof(PersistenceDataAttribute), false))
                throw new ArgumentException($"{path}: {type.FullName} must be a closed struct marked [Serializable, PersistenceData].");
            if (!visiting.Add(type)) return;
            foreach (FieldInfo field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                if (field.IsDefined(typeof(SerializeField), false))
                    throw new ArgumentException($"{path}.{field.Name}: use public data fields, not private SerializeField fields.");
            foreach (FieldInfo field in GetFields(type))
                ValidateField(field.FieldType, path + "." + field.Name, visiting);
            visiting.Remove(type);
        }

        private static void ValidateField(Type type, string path, HashSet<Type> visiting)
        {
            if (IsLeaf(type)) return;
            if (TryGetElementType(type, out Type element))
            {
                if (element.IsArray || TryGetElementType(element, out _))
                    throw new ArgumentException($"{path}: nested collections are not supported.");
                ValidateField(element, path + "[]", visiting);
                return;
            }
            ValidateStruct(type, path, visiting);
        }
    }
}
