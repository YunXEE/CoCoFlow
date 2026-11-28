using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 图编辑器保存节点字段覆盖，标准 Host 冻结这些条目并在创建对应 Logic 时应用。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存图节点覆盖的字段路径和值，由标准 freezer 转交给运行时 Logic。</summary>
    [Serializable]
    public sealed class FieldConfig : StateConfig
    {

        #region Public API

        public string[] Values = Array.Empty<string>();

        /// <summary>替换该路径及子路径的节点覆盖，保留其他字段。不支持的值或 null 会抛出异常，已有配置保持原值。</summary>
        public void Set(string path, object value)
        {
            if (value == null || !StateFields.IsConfigType(value.GetType()))
                throw new ArgumentException("Unsupported Config value: " + path);
            var entries = new List<string>();
            Encode(path, value, entries);
            Values = Values.Where(entry => !ContainsPath(entry, path)).Concat(entries).ToArray();
        }

        /// <summary>先解析并校验所有覆盖，再将值写入 Logic 的配置字段。路径、类型或值无效时抛出异常。</summary>
        public void Apply(StateLogic logic)
        {
            var staged = new Dictionary<FieldInfo, object>();
            foreach (string entry in Values.OrderByDescending(IsCollectionCount))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0) throw new FormatException("Invalid Config entry: " + entry);
                string[] path = entry.Substring(0, separator).Replace("[", ".").Replace("]", "").Split('.');
                FieldInfo field = logic.GetType().GetField(path[0], BindingFlags.Public | BindingFlags.Instance);
                if (field == null || !field.IsDefined(typeof(StateConfigAttribute), false) ||
                    field.IsInitOnly || !StateFields.IsConfigType(field.FieldType))
                    throw new InvalidOperationException("Invalid Config field: " + path[0]);
                if (!staged.TryGetValue(field, out object value)) value = field.GetValue(logic);
                staged[field] = SetPath(value, field.FieldType, path, entry.Substring(separator + 1), 1);
            }
            foreach (var pair in staged) pair.Key.SetValue(logic, pair.Value);
        }

        /// <summary>按指定 Logic 类型校验节点覆盖，并把独立的配置条目写入冻结快照。</summary>
        public sealed class Freezer : IConfigFreezer<FieldConfig, FieldConfigSchema>
        {

            #region Public API

            /// <summary>选择用于验证节点覆盖的 Logic 类型，冻结时用该类型的实例检查字段与赋值。</summary>
            public Freezer(Type logicType) => this.logicType = logicType;

            /// <summary>先在指定 Logic 上应用覆盖检查字段，再写入冻结配置数组。无效覆盖会抛出异常，写入失败返回诊断。</summary>
            public bool TryFreeze(FieldConfig source, FrozenConfigWriter<FieldConfigSchema> writer,
                out CoCoDiagnostic diagnostic)
            {
                source.Apply((StateLogic)Activator.CreateInstance(logicType));
                return writer.TryWriteArray(FieldConfigSchema.Values, source.Values, out diagnostic);
            }

            #endregion

            #region Internal Logic

            private readonly Type logicType;

            #endregion
        }

        #endregion

        #region Internal Logic

        internal static bool ContainsPath(string entry, string path) =>
            entry.StartsWith(path + "=", StringComparison.Ordinal) ||
            entry.StartsWith(path + ".", StringComparison.Ordinal) ||
            entry.StartsWith(path + "[", StringComparison.Ordinal);

        private static void Encode(string path, object value, List<string> entries)
        {
            if (value == null) throw new ArgumentException("Unsupported null Config value: " + path);
            Type type = value.GetType();
            if (StateFields.ConfigElementType(type) != null)
            {
                var items = (IList)value;
                entries.Add(path + ".$count=" + items.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < items.Count; i++) Encode(path + "[" + i + "]", items[i], entries);
            }
            else if (StateFields.IsConfigScalar(type))
            {
                string text = value is float f ? f.ToString("R", CultureInfo.InvariantCulture) :
                    value is double d ? d.ToString("R", CultureInfo.InvariantCulture) :
                    Convert.ToString(value, CultureInfo.InvariantCulture);
                entries.Add(path + "=" + text);
            }
            else
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    Encode(path + "." + field.Name, field.GetValue(value), entries);
        }

        private static bool IsCollectionCount(string entry)
        {
            const string suffix = ".$count";
            int separator = entry.IndexOf('=');
            return separator >= suffix.Length &&
                   string.CompareOrdinal(entry, separator - suffix.Length, suffix, 0, suffix.Length) == 0;
        }

        private static object SetPath(object value, Type type, string[] path, string text, int index)
        {
            if (index == path.Length)
            {
                if (!StateFields.IsConfigScalar(type)) throw new FormatException("Expected a Config leaf.");
                return type.IsEnum ? Enum.Parse(type, text) : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            }
            Type elementType = StateFields.ConfigElementType(type);
            if (elementType != null)
            {
                if (path[index] == "$count" && index + 1 == path.Length)
                {
                    int count = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
                    return StateFields.CreateConfigCollection(type, count);
                }
                var items = value as IList;
                int position = int.Parse(path[index], NumberStyles.None, CultureInfo.InvariantCulture);
                if (items == null || position >= items.Count) throw new FormatException("Invalid Config index.");
                var copy = StateFields.CreateConfigCollection(type, items.Count);
                for (int i = 0; i < items.Count; i++) copy[i] = items[i];
                copy[position] = SetPath(items[position], elementType, path, text, index + 1);
                return copy;
            }
            FieldInfo field = type.GetField(path[index], BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.IsInitOnly) throw new FormatException("Invalid Config member: " + path[index]);
            field.SetValue(value, SetPath(field.GetValue(value), field.FieldType, path, text, index + 1));
            return value;
        }

        #endregion
    }

    /// <summary>声明存放字段配置条目数组的冻结布局。</summary>
    public struct FieldConfigSchema : IFrozenConfigSchema
    {

        #region Public API

        public static readonly FrozenConfigArrayField<FieldConfigSchema, string> Values;
        public static readonly FrozenConfigSchema<FieldConfigSchema> Schema;

        #endregion

        #region Internal Logic

        static FieldConfigSchema()
        {
            var builder = new FrozenConfigSchemaBuilder<FieldConfigSchema>();
            FrozenConfigFieldId.TryCreate(0x434F434F4649454CUL, 1, out var id);
            builder.TryAddArrayField(id, out Values, out _);
            builder.TryFreeze(out Schema, out _);
        }

        #endregion
    }
}
