using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CoCoFlow.Runtime.Core
{
    [Serializable]
    public sealed class CoCoFieldConfig : CoCoStateConfig
    {
        public string[] Values = Array.Empty<string>();

        public void Set(string path, object value)
        {
            if (value == null || !CoCoStateFields.IsConfigType(value.GetType()))
                throw new ArgumentException("Unsupported Config value: " + path);
            var entries = new List<string>();
            Encode(path, value, entries);
            Values = Values.Where(entry => !ContainsPath(entry, path)).Concat(entries).ToArray();
        }

        internal static bool ContainsPath(string entry, string path) =>
            entry.StartsWith(path + "=", StringComparison.Ordinal) ||
            entry.StartsWith(path + ".", StringComparison.Ordinal) ||
            entry.StartsWith(path + "[", StringComparison.Ordinal);

        private static void Encode(string path, object value, List<string> entries)
        {
            Type type = value.GetType();
            if (CoCoStateFields.ConfigElementType(type) != null)
            {
                var items = (IList)value;
                entries.Add(path + ".$count=" + items.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < items.Count; i++) Encode(path + "[" + i + "]", items[i], entries);
            }
            else if (CoCoStateFields.IsConfigScalar(type))
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

        public void Apply(CoCoStateLogic logic)
        {
            // Stage whole roots before publishing, including copies of modified collections.
            var staged = new Dictionary<FieldInfo, object>();
            foreach (string entry in Values.OrderBy(entry => entry.Contains(".$count=") ? 0 : 1))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0) throw new FormatException("Invalid Config entry: " + entry);
                string[] path = entry.Substring(0, separator).Replace("[", ".").Replace("]", "").Split('.');
                FieldInfo field = logic.GetType().GetField(path[0], BindingFlags.Public | BindingFlags.Instance);
                if (field == null || !field.IsDefined(typeof(CoCoStateConfigAttribute), false) ||
                    field.IsInitOnly || !CoCoStateFields.IsConfigType(field.FieldType))
                    throw new InvalidOperationException("Invalid Config field: " + path[0]);
                if (!staged.TryGetValue(field, out object value)) value = field.GetValue(logic);
                staged[field] = SetPath(value, field.FieldType, path, entry.Substring(separator + 1), 1);
            }
            foreach (var pair in staged) pair.Key.SetValue(logic, pair.Value);
        }

        private static object SetPath(object value, Type type, string[] path, string text, int index)
        {
            if (index == path.Length)
            {
                if (!CoCoStateFields.IsConfigScalar(type)) throw new FormatException("Expected a Config leaf.");
                return type.IsEnum ? Enum.Parse(type, text) : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            }
            Type elementType = CoCoStateFields.ConfigElementType(type);
            if (elementType != null)
            {
                if (path[index] == "$count" && index + 1 == path.Length)
                {
                    int count = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
                    return CoCoStateFields.CreateConfigCollection(type, count);
                }
                var items = value as IList;
                int position = int.Parse(path[index], NumberStyles.None, CultureInfo.InvariantCulture);
                if (items == null || position >= items.Count) throw new FormatException("Invalid Config index.");
                var copy = CoCoStateFields.CreateConfigCollection(type, items.Count);
                for (int i = 0; i < items.Count; i++) copy[i] = items[i];
                copy[position] = SetPath(items[position], elementType, path, text, index + 1);
                return copy;
            }
            FieldInfo field = type.GetField(path[index], BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.IsInitOnly) throw new FormatException("Invalid Config member: " + path[index]);
            field.SetValue(value, SetPath(field.GetValue(value), field.FieldType, path, text, index + 1));
            return value;
        }

        public sealed class Freezer : ICoCoConfigFreezer<CoCoFieldConfig, CoCoFieldConfigSchema>
        {
            private readonly Type logicType;
            public Freezer(Type logicType) => this.logicType = logicType;

            public bool TryFreeze(CoCoFieldConfig source, CoCoFrozenConfigWriter<CoCoFieldConfigSchema> writer,
                out CoCoDiagnostic diagnostic)
            {
                source.Apply((CoCoStateLogic)Activator.CreateInstance(logicType));
                return writer.TryWriteArray(CoCoFieldConfigSchema.Values, source.Values, out diagnostic);
            }
        }
    }

    public struct CoCoFieldConfigSchema : ICoCoFrozenConfigSchema
    {
        public static readonly CoCoFrozenConfigArrayField<CoCoFieldConfigSchema, string> Values;
        public static readonly CoCoFrozenConfigSchema<CoCoFieldConfigSchema> Schema;

        static CoCoFieldConfigSchema()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<CoCoFieldConfigSchema>();
            CoCoFrozenConfigFieldId.TryCreate(0x434F434F4649454CUL, 1, out var id);
            builder.TryAddArrayField(id, out Values, out _);
            builder.TryFreeze(out Schema, out _);
        }
    }

    internal static class CoCoStateFields
    {
        internal static FieldInfo[] Fields(Type logic, Type attribute) => logic
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.IsDefined(attribute, false)).OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();

        internal static Type StoredType(Type logic)
        {
            Type stored = typeof(byte);
            foreach (var field in Fields(logic, typeof(CoCoStateMemoryAttribute)).Reverse())
                stored = typeof(ValueTuple<,>).MakeGenericType(field.FieldType, stored);
            return stored;
        }

        internal static bool IsConfigScalar(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(decimal);

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
            if (!type.IsArray)
                for (int i = 0; i < count; i++) result.Add(Activator.CreateInstance(element));
            return result;
        }

        internal static IEnumerable<string> ConfigPaths(Type type, string prefix = "")
        {
            var fields = prefix.Length == 0 ? Fields(type, typeof(CoCoStateConfigAttribute)) :
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

    internal interface ICoCoFieldMemory
    {
        void Load(CoCoStateLogic logic);
        void Store(CoCoStateLogic logic);
    }

    internal delegate void CoCoFieldMemoryTransfer<TState>(CoCoStateLogic logic, ref TState state, bool load);

    internal sealed class CoCoFieldMemory<TLogic, TState> : CoCoActivationMemory, ICoCoFieldMemory
        where TLogic : CoCoStateLogic, new()
        where TState : unmanaged
    {
        internal static readonly FieldInfo[] Fields = CoCoStateFields.Fields(typeof(TLogic), typeof(CoCoStateMemoryAttribute));
        internal static readonly TState Initial = (TState)PackValue(new TLogic(), typeof(TState), 0);
        internal static CoCoFieldMemoryTransfer<TState> Transfer;
        internal TState Value = Initial;

        private static object PackValue(TLogic logic, Type type, int index) =>
            index == Fields.Length ? (object)(byte)0 :
            Activator.CreateInstance(type, Fields[index].GetValue(logic),
                PackValue(logic, type.GetField("Item2").FieldType, index + 1));

        public void Load(CoCoStateLogic logic) => Transfer(logic, ref Value, true);
        public void Store(CoCoStateLogic logic) => Transfer(logic, ref Value, false);

        internal static ulong Fingerprint(CoCoFieldMemory<TLogic, TState> memory)
        {
            ulong hash = CoCoFrozenConfigHash.OffsetBasis;
            foreach (var field in Fields)
                CoCoFrozenConfigHash.AddScalar(ref hash, typeof(string), field.Name);
            foreach (byte value in MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref memory.Value, 1)))
                CoCoFrozenConfigHash.Add(ref hash, value);
            return CoCoFrozenConfigHash.Complete(hash);
        }

        internal sealed class Binding : ICoCoActivationMemoryStateBinding<CoCoFieldMemory<TLogic, TState>, TState>
        {
            public ulong SemanticFingerprint => Fingerprint(new CoCoFieldMemory<TLogic, TState>());
            public bool TryCapture(CoCoFieldMemory<TLogic, TState> memory, out TState state, out CoCoDiagnostic diagnostic)
            {
                state = memory.Value;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryPrepareRestore(in TState state, CoCoFieldMemory<TLogic, TState> memory, out CoCoDiagnostic diagnostic)
            {
                memory.Value = state;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
