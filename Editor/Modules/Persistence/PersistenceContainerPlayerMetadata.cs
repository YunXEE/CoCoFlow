using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;

[assembly: InternalsVisibleTo("CoCoFlow.Tests.Editor.Persistence")]

namespace CoCoFlow.Editor.Modules.Persistence
{
    /// <summary>Preserves only the marked Player data types and their field dependencies.</summary>
    internal static class PersistenceContainerPlayerMetadata
    {
        internal static Type[] CollectTypes(IEnumerable<Type> roots, ISet<string> playerAssemblies)
        {
            var types = new HashSet<Type>();
            foreach (Type type in roots)
            {
                if (!playerAssemblies.Contains(type.Assembly.GetName().Name)) continue;
                PersistenceDataTypeValidator.Validate(type);
                Collect(type, types);
            }
            return types.OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();
        }

        internal static string CreateLinkXml(IEnumerable<Type> types)
        {
            var linker = new XElement("linker");
            foreach (IGrouping<string, Type> assembly in types.GroupBy(type => type.Assembly.GetName().Name))
            {
                var element = new XElement("assembly", new XAttribute("fullname", assembly.Key));
                foreach (Type type in assembly)
                {
                    Type metadataType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                    string name = metadataType.FullName!.Replace('+', '/');
                    if (element.Elements("type").Any(item => (string)item.Attribute("fullname") == name)) continue;
                    element.Add(new XElement("type", new XAttribute("fullname", name),
                        new XAttribute("preserve", type.IsGenericType ? "all" : "fields")));
                }
                linker.Add(element);
            }
            return new XDocument(linker).ToString();
        }

        private static void Collect(Type type, ISet<Type> types)
        {
            if (type.IsArray)
            {
                Collect(type.GetElementType(), types);
                return;
            }
            if (type.IsPrimitive || type == typeof(string) || !types.Add(type)) return;
            if (PersistenceDataTypeValidator.TryGetElementType(type, out Type element))
            {
                Collect(element, types);
                return;
            }
            if (type.IsEnum) return;
            foreach (FieldInfo field in PersistenceDataTypeValidator.GetFields(type)) Collect(field.FieldType, types);
        }
    }

    public sealed class PersistenceContainerLinkerProcessor : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var playerAssemblies = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(assembly => assembly.name), StringComparer.Ordinal);
            Type[] types = PersistenceContainerPlayerMetadata.CollectTypes(
                PersistenceDataTypeRegistry.GetValidTypes(), playerAssemblies);
            if (types.Length == 0) return null;
            string directory = Path.Combine("Library", "CoCoFlow", "Persistence");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "link.xml");
            File.WriteAllText(path, PersistenceContainerPlayerMetadata.CreateLinkXml(types));
            return path;
        }
    }
}
