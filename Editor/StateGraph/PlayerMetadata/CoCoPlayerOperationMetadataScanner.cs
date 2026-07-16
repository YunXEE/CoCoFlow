using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Editor.StateGraph.PlayerMetadata
{
    public static class CoCoPlayerOperationMetadataNaming
    {
        public static string GetLinkerTypeFullName(Type type) =>
            type?.FullName?.Replace('+', '/');
    }

    [Serializable]
    public sealed class CoCoPlayerOperationMetadataEntry
    {
        public CoCoPlayerOperationMetadataEntry(
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
    public sealed class CoCoPlayerOperationMetadataResult
    {
        public CoCoPlayerOperationMetadataResult(
            CoCoPlayerOperationMetadataEntry[] entries,
            string[] diagnostics)
        {
            Entries = entries ?? Array.Empty<CoCoPlayerOperationMetadataEntry>();
            Diagnostics = diagnostics ?? Array.Empty<string>();
        }

        public CoCoPlayerOperationMetadataEntry[] Entries { get; }
        public string[] Diagnostics { get; }
    }

    public sealed class CoCoPlayerOperationMetadataScanner : MarshalByRefObject
    {
        public CoCoPlayerOperationMetadataResult Scan(
            string assemblyDirectory,
            string[] playerAssemblyNames)
        {
            var messages = new SortedSet<string>(StringComparer.Ordinal);
            var entries = new Dictionary<string, CoCoPlayerOperationMetadataEntry>(
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

                ScanAssembly(assembly, entries, messages);
            }

            var orderedEntries = new List<CoCoPlayerOperationMetadataEntry>(entries.Values);
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
            return new CoCoPlayerOperationMetadataResult(
                orderedEntries.ToArray(),
                diagnostics);
        }

        private static void ScanAssembly(
            Assembly assembly,
            IDictionary<string, CoCoPlayerOperationMetadataEntry> entries,
            ISet<string> messages)
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

            for (int index = 0; index < types.Length; index++)
            {
                Type type = types[index];
                if (type == null ||
                    !type.IsInterface ||
                    type == typeof(ICoCoOperationSection) ||
                    !typeof(ICoCoOperationSection).IsAssignableFrom(type))
                {
                    continue;
                }

                if (!CoCoOperationSectionShape.TryCreate(
                        type,
                        out CoCoOperationSectionShape shape,
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

        private static void CollectValueTypes(
            Type type,
            IDictionary<string, CoCoPlayerOperationMetadataEntry> entries,
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
            IDictionary<string, CoCoPlayerOperationMetadataEntry> entries)
        {
            string key = Key(type);
            if (entries.TryGetValue(key, out CoCoPlayerOperationMetadataEntry existing) &&
                string.Equals(existing.Preserve, "all", StringComparison.Ordinal))
            {
                return;
            }

            entries[key] = new CoCoPlayerOperationMetadataEntry(
                type.Assembly.GetName().Name,
                CoCoPlayerOperationMetadataNaming.GetLinkerTypeFullName(type),
                preserve);
        }

        private static string Key(Type type) =>
            $"{type.Assembly.GetName().Name}\0" +
            CoCoPlayerOperationMetadataNaming.GetLinkerTypeFullName(type);
    }
}
