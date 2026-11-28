using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 标准绑定与图编辑器共用字段筛选、配置路径及集合构造规则，确定哪些字段能够写入图节点。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>供标准绑定和节点编辑器识别可配置字段、记忆字段与可冻结的集合。</summary>
    internal static class StateFields
    {
        internal static FieldInfo[] Fields(Type logic, Type attribute) => logic
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.IsDefined(attribute, false)).OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();

        internal static Type StoredType(Type logic)
        {
            Type stored = typeof(byte);
            foreach (var field in Fields(logic, typeof(StateMemoryAttribute)).Reverse())
                stored = typeof(ValueTuple<,>).MakeGenericType(field.FieldType, stored);
            return stored;
        }

        internal static bool IsConfigScalar(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string);

        internal static Type ConfigElementType(Type type) =>
            type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType() :
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0] : null;

        internal static bool IsConfigType(Type type, bool inCollection = false)
        {
            if (IsConfigScalar(type)) return type != typeof(IntPtr) && type != typeof(UIntPtr);
            Type element = ConfigElementType(type);
            if (element != null) return !inCollection && IsConfigType(element, true);
            if (!type.IsValueType) return false;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            return fields.Length != 0 && fields.All(field => !field.IsInitOnly && IsConfigType(field.FieldType, inCollection));
        }

        internal static IList CreateConfigCollection(Type type, int count)
        {
            Type element = ConfigElementType(type);
            var result = type.IsArray ? Array.CreateInstance(element, count) : (IList)Activator.CreateInstance(type);
            if (element == typeof(string) && type.IsArray)
                Array.Fill((string[])result, string.Empty);
            if (!type.IsArray)
                for (int i = 0; i < count; i++) result.Add(element == typeof(string) ? string.Empty : Activator.CreateInstance(element));
            return result;
        }

        internal static IEnumerable<string> ConfigPaths(Type type, string prefix = "")
        {
            var fields = prefix.Length == 0 ? Fields(type, typeof(StateConfigAttribute)) :
                type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                string path = prefix + field.Name;
                if (IsConfigScalar(field.FieldType) || ConfigElementType(field.FieldType) != null || !IsConfigType(field.FieldType))
                    yield return path;
                else
                    foreach (string child in ConfigPaths(field.FieldType, path + "."))
                        yield return child;
            }
        }

        internal static object ReadPath(object owner, string path)
        {
            foreach (string name in path.Split('.'))
                owner = owner.GetType().GetField(name).GetValue(owner);
            return owner;
        }
    }
}
