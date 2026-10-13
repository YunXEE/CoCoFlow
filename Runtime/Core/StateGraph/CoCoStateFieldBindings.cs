using System;
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
            string text = value is float f ? f.ToString("R", CultureInfo.InvariantCulture) :
                value is double d ? d.ToString("R", CultureInfo.InvariantCulture) :
                Convert.ToString(value, CultureInfo.InvariantCulture);
            Values = Values.Where(entry => !entry.StartsWith(path + "=", StringComparison.Ordinal))
                .Concat(new[] { path + "=" + text }).ToArray();
        }

        public void Apply(CoCoStateLogic logic)
        {
            foreach (string entry in Values)
            {
                int separator = entry.IndexOf('=');
                SetPath(logic, entry.Substring(0, separator).Split('.'), entry.Substring(separator + 1), 0);
            }
        }

        private static void SetPath(object owner, string[] path, string text, int index)
        {
            FieldInfo field = owner.GetType().GetField(path[index]);
            object value = field.GetValue(owner);
            if (index + 1 < path.Length)
                SetPath(value, path, text, index + 1);
            else
                value = field.FieldType.IsEnum ? Enum.Parse(field.FieldType, text) :
                    Convert.ChangeType(text, field.FieldType, CultureInfo.InvariantCulture);
            field.SetValue(owner, value);
        }

        public sealed class Freezer : ICoCoConfigFreezer<CoCoFieldConfig, CoCoFieldConfigSchema>
        {
            public bool TryFreeze(CoCoFieldConfig source, CoCoFrozenConfigWriter<CoCoFieldConfigSchema> writer,
                out CoCoDiagnostic diagnostic) => writer.TryWriteArray(CoCoFieldConfigSchema.Values, source.Values, out diagnostic);
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

        internal static IEnumerable<string> ConfigPaths(Type type, string prefix = "")
        {
            var fields = prefix.Length == 0 ? Fields(type, typeof(CoCoStateConfigAttribute)) :
                type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                string path = prefix + field.Name;
                if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(decimal))
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

    internal sealed class CoCoFieldMemory<TLogic, TState> : CoCoActivationMemory, ICoCoFieldMemory
        where TLogic : CoCoStateLogic, new()
        where TState : unmanaged
    {
        internal static readonly FieldInfo[] Fields = CoCoStateFields.Fields(typeof(TLogic), typeof(CoCoStateMemoryAttribute));
        internal static readonly TState Initial = (TState)PackValue(new TLogic(), typeof(TState), 0);
        internal TState Value = Initial;

        private static object PackValue(TLogic logic, Type type, int index) =>
            index == Fields.Length ? (object)(byte)0 :
            Activator.CreateInstance(type, Fields[index].GetValue(logic),
                PackValue(logic, type.GetField("Item2").FieldType, index + 1));

        public void Load(CoCoStateLogic logic)
        {
            object value = Value;
            foreach (var field in Fields)
            {
                field.SetValue(logic, value.GetType().GetField("Item1").GetValue(value));
                value = value.GetType().GetField("Item2").GetValue(value);
            }
        }

        public void Store(CoCoStateLogic logic) => Value = (TState)PackValue((TLogic)logic, typeof(TState), 0);

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
