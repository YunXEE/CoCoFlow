using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Editor.PlayerMetadata
{
    public static class PlayerOperationMetadataNaming
    {
        public static string GetLinkerTypeFullName(Type type) =>
            type?.FullName?.Replace('+', '/');
    }

    [Serializable]
    public sealed class PlayerOperationMetadataEntry
    {
        public PlayerOperationMetadataEntry(
            string assemblyName,
            string typeFullName,
            string preserve)
        {
            AssemblyName = assemblyName;
            TypeFullName = typeFullName;
            Preserve = preserve;
        }

        public string AssemblyName { get; }
        public string TypeFullName { get; }
        public string Preserve { get; }
    }

    [Serializable]
    public sealed class PlayerOperationMetadataResult
    {
        public PlayerOperationMetadataResult(
            PlayerOperationMetadataEntry[] entries,
            string[] diagnostics)
        {
            Entries = entries ?? Array.Empty<PlayerOperationMetadataEntry>();
            Diagnostics = diagnostics ?? Array.Empty<string>();
        }

        public PlayerOperationMetadataEntry[] Entries { get; }
        public string[] Diagnostics { get; }
    }

    public sealed class PlayerOperationMetadataScanner : MarshalByRefObject
    {
        public PlayerOperationMetadataResult Scan(
            string assemblyDirectory,
            string[] playerAssemblyNames)
        {
            return Scan(assemblyDirectory, playerAssemblyNames, true);
        }

        public PlayerOperationMetadataResult Scan(
            string assemblyDirectory,
            string[] playerAssemblyNames,
            bool validateSections)
        {
            var messages = new SortedSet<string>(StringComparer.Ordinal);
            var entries = new Dictionary<string, PlayerOperationMetadataEntry>(
                StringComparer.Ordinal);
            string[] assemblyNames = playerAssemblyNames == null
                ? Array.Empty<string>()
                : (string[])playerAssemblyNames.Clone();
            Array.Sort(assemblyNames, StringComparer.Ordinal);
            for (int index = 0; index < assemblyNames.Length; index++)
            {
                string assemblyName = assemblyNames[index];
                string assemblyPath = Path.Combine(assemblyDirectory, assemblyName + ".dll");
                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFrom(assemblyPath);
                }
                catch (Exception)
                {
                    messages.Add(
                        $"Player assembly metadata for {assemblyName} could not be loaded in " +
                        "the isolated resolver.");
                    continue;
                }

                if (!string.Equals(
                        assembly.GetName().Name,
                        assemblyName,
                        StringComparison.Ordinal))
                {
                    messages.Add(
                        $"Player assembly metadata identity does not match {assemblyName} in " +
                        "the isolated resolver.");
                    continue;
                }

                ScanAssembly(assembly, entries, messages, validateSections);
            }

            var orderedEntries = new List<PlayerOperationMetadataEntry>(entries.Values);
            orderedEntries.Sort((left, right) =>
            {
                int assemblyOrder = string.CompareOrdinal(
                    left.AssemblyName,
                    right.AssemblyName);
                return assemblyOrder != 0
                    ? assemblyOrder
                    : string.CompareOrdinal(left.TypeFullName, right.TypeFullName);
            });
            var diagnostics = new string[messages.Count];
            messages.CopyTo(diagnostics);
            return new PlayerOperationMetadataResult(
                orderedEntries.ToArray(),
                diagnostics);
        }

        private static void ScanAssembly(
            Assembly assembly,
            IDictionary<string, PlayerOperationMetadataEntry> entries,
            ISet<string> messages,
            bool validateSections)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
                messages.Add(
                    $"Operation Section metadata could not be fully loaded from " +
                    $"{assembly.GetName().Name} in the isolated resolver.");
            }
            catch (Exception)
            {
                messages.Add(
                    $"Operation Section metadata could not be loaded from " +
                    $"{assembly.GetName().Name} in the isolated resolver.");
                return;
            }

            foreach (OperationViewFactoryAttribute generated in
                     assembly.GetCustomAttributes(typeof(OperationViewFactoryAttribute), false))
                AddEntry(generated.Factory, "all", entries);

            for (int index = 0; index < types.Length; index++)
            {
                Type type = types[index];
                CollectStandardBindingMetadata(type, entries);
                if (type == null ||
                    !type.IsInterface ||
                    type == typeof(IOperationSection) ||
                    !typeof(IOperationSection).IsAssignableFrom(type))
                {
                    continue;
                }

                // 运行时创建的图也需要完整声明；无资产构建保留无效声明，
                // 由运行时注册路径报告原有诊断，不在构建时提前拒绝。
                if (!validateSections)
                {
                    AddEntry(type, "all", entries);
                    var propertyTypes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (PropertyInfo property in type.GetProperties())
                    {
                        CollectValueTypes(property.PropertyType, entries, propertyTypes);
                    }

                    continue;
                }

                if (!OperationSectionShape.TryCreate(
                        type,
                        out OperationSectionShape shape,
                        out CoCoDiagnostic diagnostic))
                {
                    messages.Add($"{type.FullName}: {diagnostic.Message}");
                    continue;
                }

                AddEntry(type, "all", entries);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (int fieldIndex = 0; fieldIndex < shape.FieldCount; fieldIndex++)
                {
                    CollectValueTypes(shape.Fields[fieldIndex].ValueType, entries, visited);
                }
            }
        }

        private static void CollectStandardBindingMetadata(
            Type type,
            IDictionary<string, PlayerOperationMetadataEntry> entries)
        {
            if (type == null || !type.IsClass) return;
            if (typeof(IOperator).IsAssignableFrom(type))
            {
                foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public |
                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (property.IsDefined(typeof(OperatorDefinitionAttribute), false)) AddEntry(type, "all", entries);
            }
            if (!type.IsDefined(typeof(StateAttribute), false) && !type.IsDefined(typeof(ConditionAttribute), false))
            {
                return;
            }

            // 标准绑定按声明发现类型，再通过泛型反射创建实例、绑定属性和配置字段。
            // 保留声明类型本身，避免测试里偶然存在的 new 调用成为构造函数的唯一根。
            AddEntry(type, "all", entries);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsDefined(typeof(StateConfigAttribute), false) ||
                    field.IsDefined(typeof(StateMemoryAttribute), false))
                {
                    CollectValueTypes(field.FieldType, entries, visited);
                }
            }
        }

        private static void CollectValueTypes(
            Type type,
            IDictionary<string, PlayerOperationMetadataEntry> entries,
            ISet<string> visited)
        {
            if (type == null)
            {
                return;
            }

            if (type.IsGenericType)
            {
                Type[] arguments = type.GetGenericArguments();
                for (int index = 0; index < arguments.Length; index++)
                {
                    CollectValueTypes(arguments[index], entries, visited);
                }
            }

            if (type.IsPrimitive ||
                type.IsPointer ||
                type == typeof(decimal) ||
                type.Assembly == typeof(int).Assembly)
            {
                return;
            }

            Type metadataType = type.IsGenericType
                ? type.GetGenericTypeDefinition()
                : type;
            string key = Key(metadataType);
            if (!visited.Add(key))
            {
                return;
            }

            AddEntry(metadataType, "fields", entries);
            if (!type.IsValueType || type.IsEnum)
            {
                return;
            }

            FieldInfo[] fields = type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            for (int index = 0; index < fields.Length; index++)
            {
                CollectValueTypes(fields[index].FieldType, entries, visited);
            }
        }

        private static void AddEntry(
            Type type,
            string preserve,
            IDictionary<string, PlayerOperationMetadataEntry> entries)
        {
            string key = Key(type);
            if (entries.TryGetValue(key, out PlayerOperationMetadataEntry existing) &&
                string.Equals(existing.Preserve, "all", StringComparison.Ordinal))
            {
                return;
            }

            entries[key] = new PlayerOperationMetadataEntry(
                type.Assembly.GetName().Name,
                PlayerOperationMetadataNaming.GetLinkerTypeFullName(type),
                preserve);
        }

        private static string Key(Type type) =>
            $"{type.Assembly.GetName().Name}\0" +
            PlayerOperationMetadataNaming.GetLinkerTypeFullName(type);
    }
}
