using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    internal static class PersistenceDataTypeRegistry
    {
        private static Dictionary<string, Type> _types;

        internal static string GetTypeId(Type type)
        {
            PersistenceDataTypeValidator.Validate(type);
            return type.Assembly.GetName().Name + ":" + type.FullName;
        }

        internal static Type Resolve(string typeId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(typeId) || !_types.TryGetValue(typeId, out Type type))
                throw new ArgumentException($"Unknown PersistenceData type '{typeId}'.");
            PersistenceDataTypeValidator.Validate(type);
            return type;
        }

        internal static Type[] GetValidTypes()
        {
            EnsureInitialized();
            return _types.Values.Where(type => PersistenceDataTypeValidator.TryValidate(type, out _))
                .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        }

        private static void EnsureInitialized()
        {
            if (_types != null) return;
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach (Type type in GetLoadableTypes(assembly))
            {
                if (type == null || !type.IsDefined(typeof(PersistenceDataAttribute), false)) continue;
                string id = type.Assembly.GetName().Name + ":" + type.FullName;
                if (types.TryGetValue(id, out Type existing) && existing != type)
                    throw new ArgumentException($"Duplicate PersistenceData type ID '{id}'.");
                types[id] = type;
            }
            _types = types;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { return exception.Types; }
        }
    }
}
