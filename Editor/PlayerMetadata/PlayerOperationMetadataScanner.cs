using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// Player 构建前扫描玩法类型和已生成的 Operation 视图，提供链接器所需的保留条目。
namespace CoCoFlow.Editor.PlayerMetadata
{
    /// <summary>将反射取得的类型名转换成 Player 链接器可识别的拼写。</summary>
    public static class PlayerOperationMetadataNaming
    {
        /// <summary>返回链接器使用的嵌套类型全名，将 CLR 的加号转换为斜线，无法取得名称时返回 null。</summary>
        public static string GetLinkerTypeFullName(Type type) =>
            type?.FullName?.Replace('+', '/');
    }

    /// <summary>将一个需要保留的操作合同和生成绑定关联到来源程序集。</summary>
    [Serializable]
    public sealed class PlayerOperationMetadataEntry
    {
        /// <summary>保存链接器所需的程序集名、类型全名与保留范围。</summary>
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

    /// <summary>汇总扫描得到的 Player 保留条目与诊断。</summary>
    [Serializable]
    public sealed class PlayerOperationMetadataResult
    {
        /// <summary>打包扫描出的保留条目和诊断，让构建方在写入链接器配置前检查。</summary>
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

    /// <summary>从指定的项目程序集和操作声明收集 Player 构建所需的元数据。</summary>
    public sealed class PlayerOperationMetadataScanner : MarshalByRefObject
    {

        #region Public API

        /// <summary>扫描提供的程序集并按程序集和类型名输出已去重的保留项，同时返回加载或声明错误。</summary>
        public PlayerOperationMetadataResult Scan(
            string assemblyDirectory,
            string[] playerAssemblyNames)
        {
            return Scan(assemblyDirectory, playerAssemblyNames, true);
        }

        /// <summary>扫描提供的程序集并按程序集和类型名输出已去重的保留项，同时返回加载或声明错误。</summary>
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

        #endregion

        #region Internal Logic

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
                    type == typeof(IOperation) ||
                    !typeof(IOperation).IsAssignableFrom(type))
                {
                    continue;
                }

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

                if (!OperationShape.TryCreate(
                        type,
                        out OperationShape shape,
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
            if (!type.IsDefined(typeof(StateAttribute), false))
            {
                return;
            }

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

        #endregion
    }
}
