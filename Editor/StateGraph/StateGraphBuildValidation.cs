using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.PlayerMetadata;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;
using SystemAssembly = System.Reflection.Assembly;
using UnityAssembly = UnityEditor.Compilation.Assembly;

namespace CoCoFlow.Editor.StateGraph
{
    internal static class StateGraphBuildValidation
    {
        internal static bool HasStateGraphAssets() =>
            AssetDatabase.FindAssets($"t:{nameof(StateGraphAsset)}").Length > 0;

        internal static GraphDescriptorCatalog RequireCatalog()
        {
            Func<GraphDescriptorCatalog> provider = StateGraphEditorCatalogProvider.Provider;
            if (provider == null)
            {
                throw new BuildFailedException(
                    "StateGraph Assets require a registered StateGraphEditorCatalogProvider.");
            }

            GraphDescriptorCatalog catalog;
            try
            {
                catalog = provider();
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"The StateGraph descriptor catalog provider threw: {exception.Message}");
            }

            if (catalog == null || !catalog.IsFrozen)
            {
                throw new BuildFailedException(
                    "The StateGraph descriptor catalog provider must return a frozen, non-null catalog.");
            }

            return catalog;
        }

        internal static void ValidateForBuild(GraphDescriptorCatalog catalog)
        {
            UnityAssembly[] playerAssemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Player);
            CoCoDiagnostic[] closureDiagnostics =
                StateGraphAuthoringDependencyClosureValidator.Validate(
                    catalog,
                    playerAssemblies);
            if (closureDiagnostics.Length == 0)
            {
                return;
            }

            var messages = new List<string>(closureDiagnostics.Length);
            for (int index = 0; index < closureDiagnostics.Length; index++)
            {
                messages.Add(closureDiagnostics[index].Message);
            }

            messages.Sort(StringComparer.Ordinal);
            throw new BuildFailedException(string.Join(Environment.NewLine, messages));
        }

        internal static Type[] CollectAndValidateOperations(
            AssembliesType assembliesType,
            out CoCoDiagnostic[] diagnostics)
        {
            return CollectAndValidateOperations(
                CompilationPipeline.GetAssemblies(assembliesType),
                out diagnostics);
        }

        internal static Type[] CollectAndValidateOperations(
            UnityAssembly[] unityAssemblies,
            out CoCoDiagnostic[] diagnostics)
        {
            var loadedByName = new Dictionary<string, SystemAssembly>(StringComparer.Ordinal);
            SystemAssembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < loadedAssemblies.Length; index++)
            {
                SystemAssembly loadedAssembly = loadedAssemblies[index];
                string loadedName = loadedAssembly.GetName().Name;
                if (!string.IsNullOrEmpty(loadedName))
                {
                    loadedByName[loadedName] = loadedAssembly;
                }
            }

            var sections = new List<Type>();
            var messages = new SortedSet<string>(StringComparer.Ordinal);
            if (unityAssemblies == null)
            {
                unityAssemblies = Array.Empty<UnityAssembly>();
            }

            var orderedUnityAssemblies = (UnityAssembly[])unityAssemblies.Clone();
            Array.Sort(
                orderedUnityAssemblies,
                (left, right) => StringComparer.Ordinal.Compare(left?.name, right?.name));
            for (int assemblyIndex = 0;
                 assemblyIndex < orderedUnityAssemblies.Length;
                 assemblyIndex++)
            {
                UnityAssembly unityAssembly = orderedUnityAssemblies[assemblyIndex];
                if (unityAssembly == null || string.IsNullOrEmpty(unityAssembly.name))
                {
                    continue;
                }

                if (!loadedByName.TryGetValue(
                        unityAssembly.name,
                        out SystemAssembly assembly))
                {
                    messages.Add(
                        $"Operation Section metadata assembly {unityAssembly.name} is not " +
                        "loaded in the current Editor AppDomain.");
                    continue;
                }

                ScanOperations(assembly, sections, messages);
            }

            sections.Sort((left, right) => StringComparer.Ordinal.Compare(
                left.AssemblyQualifiedName,
                right.AssemblyQualifiedName));
            diagnostics = CreateOperationDiagnostics(messages);

            return sections.ToArray();
        }

        internal static PlayerOperationMetadataEntry[] CollectAndValidatePlayerOperationMetadata(
            UnityAssembly[] playerAssemblies,
            string linkerInputDirectory,
            out CoCoDiagnostic[] diagnostics)
        {
            if (string.IsNullOrWhiteSpace(linkerInputDirectory) ||
                !Directory.Exists(linkerInputDirectory))
            {
                var messages = new SortedSet<string>(StringComparer.Ordinal)
                {
                    "UnityLinker Player assembly metadata directory is unavailable."
                };
                diagnostics = CreateOperationDiagnostics(messages);
                return Array.Empty<PlayerOperationMetadataEntry>();
            }

            string[] assemblyPaths = Directory.GetFiles(
                linkerInputDirectory,
                "*.dll",
                SearchOption.TopDirectoryOnly);
            return CollectAndValidatePlayerOperationMetadata(
                playerAssemblies,
                assemblyPaths,
                out diagnostics);
        }

        internal static PlayerOperationMetadataEntry[] CollectAndValidatePlayerOperationMetadata(
            UnityAssembly[] playerAssemblies,
            IReadOnlyList<string> assemblyPaths,
            out CoCoDiagnostic[] diagnostics,
            bool validateSections = true)
        {
            var messages = new SortedSet<string>(StringComparer.Ordinal);
            var playerAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
            var playerAssembliesByName =
                new Dictionary<string, UnityAssembly>(StringComparer.Ordinal);
            if (playerAssemblies != null)
            {
                for (int index = 0; index < playerAssemblies.Length; index++)
                {
                    string name = playerAssemblies[index]?.name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        playerAssemblyNames.Add(name);
                        playerAssembliesByName[name] = playerAssemblies[index];
                    }
                }
            }

            var orderedPaths = new string[assemblyPaths?.Count ?? 0];
            for (int index = 0; index < orderedPaths.Length; index++)
            {
                orderedPaths[index] = assemblyPaths[index];
            }

            Array.Sort(orderedPaths, StringComparer.Ordinal);
            var selectedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < orderedPaths.Length; index++)
            {
                string assemblyPath = orderedPaths[index];
                if (string.IsNullOrWhiteSpace(assemblyPath) ||
                    !string.Equals(
                        Path.GetExtension(assemblyPath),
                        ".dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string expectedName = Path.GetFileNameWithoutExtension(assemblyPath);
                if (!playerAssemblyNames.Contains(expectedName))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(assemblyPath);
                if (!File.Exists(fullPath))
                {
                    messages.Add(
                        $"Player assembly metadata for {expectedName} does not exist.");
                    continue;
                }

                if (selectedPaths.TryGetValue(expectedName, out string existingPath))
                {
                    if (!string.Equals(existingPath, fullPath, StringComparison.Ordinal))
                    {
                        messages.Add(
                            $"UnityLinker input contains duplicate Player assembly metadata " +
                            $"for {expectedName}.");
                    }

                    continue;
                }

                selectedPaths.Add(expectedName, fullPath);
            }

            var orderedPlayerAssemblyNames = new List<string>(selectedPaths.Keys);
            orderedPlayerAssemblyNames.Sort(StringComparer.Ordinal);
            if (orderedPlayerAssemblyNames.Count == 0)
            {
                messages.Add(
                    "No Player script assembly metadata was available for Operation Section " +
                    "validation.");
            }

            var selectedAssemblies = new UnityAssembly[orderedPlayerAssemblyNames.Count];
            for (int index = 0; index < selectedAssemblies.Length; index++)
            {
                selectedAssemblies[index] =
                    playerAssembliesByName[orderedPlayerAssemblyNames[index]];
            }

            var resolverPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in selectedPaths)
            {
                resolverPaths.Add(Path.GetFileName(pair.Value), pair.Value);
            }

            AddCompiledAssemblyResolverPaths(
                selectedAssemblies,
                selectedPaths,
                resolverPaths,
                messages);
            var orderedResolverPaths = new List<string>(resolverPaths.Values);
            orderedResolverPaths.Sort(StringComparer.Ordinal);
            PlayerOperationMetadataEntry[] entries =
                ScanPlayerMetadataInIsolatedDomain(
                    orderedResolverPaths,
                    orderedPlayerAssemblyNames,
                    messages,
                    validateSections);
            diagnostics = CreateOperationDiagnostics(messages);
            return entries;
        }

        private static void AddCompiledAssemblyResolverPaths(
            UnityAssembly[] playerAssemblies,
            IReadOnlyDictionary<string, string> selectedPaths,
            IDictionary<string, string> resolverPaths,
            ISet<string> messages)
        {
            if (playerAssemblies == null)
            {
                return;
            }

            StringComparer pathComparer = Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var systemAssemblyPaths = new HashSet<string>(pathComparer);
            string[] systemAssemblies = CompilationPipeline.GetPrecompiledAssemblyPaths(
                CompilationPipeline.PrecompiledAssemblySources.SystemAssembly);
            for (int index = 0; index < systemAssemblies.Length; index++)
            {
                systemAssemblyPaths.Add(Path.GetFullPath(systemAssemblies[index]));
            }

            var systemAssemblyDirectories = new List<string>();
            ApiCompatibilityLevel[] compatibilityLevels =
            {
                ApiCompatibilityLevel.NET_Standard,
                ApiCompatibilityLevel.NET_Unity_4_8
            };
            for (int levelIndex = 0;
                 levelIndex < compatibilityLevels.Length;
                 levelIndex++)
            {
                string[] directories = CompilationPipeline.GetSystemAssemblyDirectories(
                    compatibilityLevels[levelIndex]);
                for (int directoryIndex = 0;
                     directoryIndex < directories.Length;
                     directoryIndex++)
                {
                    string directory = Path.GetFullPath(directories[directoryIndex])
                        .TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar) +
                        Path.DirectorySeparatorChar;
                    if (!systemAssemblyDirectories.Contains(directory))
                    {
                        systemAssemblyDirectories.Add(directory);
                    }
                }
            }

            systemAssemblyDirectories.Sort(pathComparer);

            for (int assemblyIndex = 0;
                 assemblyIndex < playerAssemblies.Length;
                 assemblyIndex++)
            {
                UnityAssembly assembly = playerAssemblies[assemblyIndex];
                if (assembly == null)
                {
                    continue;
                }

                string[] references = assembly.compiledAssemblyReferences ?? Array.Empty<string>();
                for (int referenceIndex = 0;
                     referenceIndex < references.Length;
                     referenceIndex++)
                {
                    string referencePath = references[referenceIndex];
                    if (string.IsNullOrWhiteSpace(referencePath) ||
                        !string.Equals(
                            Path.GetExtension(referencePath),
                            ".dll",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string fullPath = Path.GetFullPath(referencePath);
                    if (IsSystemAssemblyPath(
                            fullPath,
                            systemAssemblyPaths,
                            systemAssemblyDirectories,
                            pathComparison))
                    {
                        continue;
                    }

                    if (!File.Exists(fullPath))
                    {
                        messages.Add(
                            $"Compiled assembly metadata dependency " +
                            $"{Path.GetFileName(referencePath)} is unavailable.");
                        continue;
                    }

                    string fileName = Path.GetFileName(fullPath);
                    string assemblyName = Path.GetFileNameWithoutExtension(fileName);
                    if (selectedPaths.ContainsKey(assemblyName))
                    {
                        continue;
                    }

                    if (resolverPaths.TryGetValue(fileName, out string existingPath))
                    {
                        if (!string.Equals(existingPath, fullPath, StringComparison.Ordinal))
                        {
                            messages.Add(
                                $"Compiled assembly metadata dependency {fileName} resolves " +
                                "to multiple files.");
                        }

                        continue;
                    }

                    resolverPaths.Add(fileName, fullPath);
                }
            }
        }

        private static bool IsSystemAssemblyPath(
            string path,
            ISet<string> systemAssemblyPaths,
            IReadOnlyList<string> systemAssemblyDirectories,
            StringComparison pathComparison)
        {
            if (systemAssemblyPaths.Contains(path))
            {
                return true;
            }

            for (int index = 0; index < systemAssemblyDirectories.Count; index++)
            {
                if (path.StartsWith(systemAssemblyDirectories[index], pathComparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static PlayerOperationMetadataEntry[] ScanPlayerMetadataInIsolatedDomain(
            IReadOnlyList<string> assemblyPaths,
            ICollection<string> assemblyNames,
            ISet<string> messages,
            bool validateSections)
        {
            if (messages.Count > 0)
            {
                return Array.Empty<PlayerOperationMetadataEntry>();
            }

            string scanDirectory = Path.GetFullPath(Path.Combine(
                "Library",
                "CoCoFlow",
                "StateGraph",
                "PlayerMetadata",
                Guid.NewGuid().ToString("N")));
            AppDomain scanDomain = null;
            try
            {
                Directory.CreateDirectory(scanDirectory);
                for (int index = 0; index < assemblyPaths.Count; index++)
                {
                    string sourcePath = assemblyPaths[index];
                    File.Copy(
                        sourcePath,
                        Path.Combine(scanDirectory, Path.GetFileName(sourcePath)),
                        true);
                }

                string scannerSourcePath =
                    typeof(PlayerOperationMetadataScanner).Assembly.Location;
                string scannerPath = Path.Combine(
                    scanDirectory,
                    Path.GetFileName(scannerSourcePath));
                File.Copy(scannerSourcePath, scannerPath, true);

                var setup = new AppDomainSetup
                {
                    ApplicationBase = scanDirectory,
                    ShadowCopyFiles = "true"
                };
                scanDomain = AppDomain.CreateDomain(
                    "CoCoFlow.StateGraph.PlayerMetadata." + Guid.NewGuid().ToString("N"),
                    null,
                    setup);
                var scanner = (PlayerOperationMetadataScanner)
                    scanDomain.CreateInstanceFromAndUnwrap(
                        scannerPath,
                        typeof(PlayerOperationMetadataScanner).FullName);
                var orderedNames = new string[assemblyNames.Count];
                assemblyNames.CopyTo(orderedNames, 0);
                Array.Sort(orderedNames, StringComparer.Ordinal);
                PlayerOperationMetadataResult result = scanner.Scan(
                    scanDirectory,
                    orderedNames,
                    validateSections);
                if (result == null)
                {
                    messages.Add("Player Operation Section metadata isolation returned no result.");
                    return Array.Empty<PlayerOperationMetadataEntry>();
                }

                for (int index = 0; index < result.Diagnostics.Length; index++)
                {
                    messages.Add(result.Diagnostics[index]);
                }

                return (PlayerOperationMetadataEntry[])result.Entries.Clone();
            }
            catch (Exception)
            {
                messages.Add("Player Operation Section metadata isolation failed.");
                return Array.Empty<PlayerOperationMetadataEntry>();
            }
            finally
            {
                if (scanDomain != null)
                {
                    try
                    {
                        AppDomain.Unload(scanDomain);
                    }
                    catch (Exception)
                    {
                        // The build already has deterministic validation output if unloading fails.
                    }
                }

                try
                {
                    if (Directory.Exists(scanDirectory))
                    {
                        Directory.Delete(scanDirectory, true);
                    }
                }
                catch (Exception)
                {
                    // Temporary Library metadata is non-authoritative and can be reclaimed later.
                }
            }
        }

        private static CoCoDiagnostic[] CreateOperationDiagnostics(
            IEnumerable<string> messages)
        {
            var diagnostics = new List<CoCoDiagnostic>();
            foreach (string message in messages)
            {
                diagnostics.Add(CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operation,
                    CoCoDiagnosticCode.InvalidOperationContract,
                    message));
            }

            return diagnostics.ToArray();
        }

        internal static void ThrowIfBuildDiagnostics(
            CoCoDiagnostic[] closureDiagnostics,
            CoCoDiagnostic[] operationDiagnostics)
        {
            int closureCount = closureDiagnostics?.Length ?? 0;
            int operationCount = operationDiagnostics?.Length ?? 0;
            if (closureCount == 0 && operationCount == 0)
            {
                return;
            }

            var messages = new List<string>(closureCount + operationCount);
            for (int index = 0; index < closureCount; index++)
            {
                messages.Add(closureDiagnostics[index].Message);
            }

            for (int index = 0; index < operationCount; index++)
            {
                messages.Add(operationDiagnostics[index].Message);
            }

            messages.Sort(StringComparer.Ordinal);
            throw new BuildFailedException(string.Join(Environment.NewLine, messages));
        }

        private static void ScanOperations(
            SystemAssembly assembly,
            ICollection<Type> sections,
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
                    $"{assembly.GetName().Name}.");
            }
            catch (Exception)
            {
                messages.Add(
                    $"Operation Section metadata could not be loaded from " +
                    $"{assembly.GetName().Name}.");
                return;
            }

            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                Type type = types[typeIndex];
                if (type == null ||
                    !type.IsInterface ||
                    IsOperationMarker(type) ||
                    !InheritsOperationSectionMarker(type))
                {
                    continue;
                }

                if (!OperationShape.TryCreate(
                        type,
                        out _,
                        out CoCoDiagnostic diagnostic))
                {
                    messages.Add($"{type.FullName}: {diagnostic.Message}");
                    continue;
                }

                sections.Add(type);
            }
        }

        private static bool InheritsOperationSectionMarker(Type type)
        {
            Type[] inherited;
            try
            {
                inherited = type.GetInterfaces();
            }
            catch (Exception)
            {
                return false;
            }

            for (int index = 0; index < inherited.Length; index++)
            {
                if (IsOperationMarker(inherited[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOperationMarker(Type type)
        {
            Type marker = typeof(IOperation);
            return type == marker ||
                   (type != null &&
                    string.Equals(type.FullName, marker.FullName, StringComparison.Ordinal) &&
                    string.Equals(
                        type.Assembly.GetName().Name,
                        marker.Assembly.GetName().Name,
                        StringComparison.Ordinal));
        }

        internal static string WriteOperationLinkXml(Type[] sectionTypes)
        {
            var types = new Dictionary<Type, string>();
            for (int index = 0; index < sectionTypes.Length; index++)
            {
                Type operationType = sectionTypes[index];
                types[operationType] = "all";
                if (!OperationShape.TryCreate(operationType, out OperationShape shape, out _))
                {
                    continue;
                }

                for (int fieldIndex = 0; fieldIndex < shape.FieldCount; fieldIndex++)
                {
                    CollectValueTypes(shape.Fields[fieldIndex].ValueType, types);
                }
            }

            var orderedTypes = new List<Type>(types.Keys);
            orderedTypes.Sort((left, right) => StringComparer.Ordinal.Compare(
                left.AssemblyQualifiedName,
                right.AssemblyQualifiedName));
            string directory = Path.GetFullPath(
                Path.Combine("Library", "CoCoFlow", "StateGraph", "GeneratedLink"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "link.xml");
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("linker");
                string currentAssembly = null;
                for (int index = 0; index < orderedTypes.Count; index++)
                {
                    Type type = orderedTypes[index];
                    string assemblyName = type.Assembly.GetName().Name;
                    if (!string.Equals(currentAssembly, assemblyName, StringComparison.Ordinal))
                    {
                        if (currentAssembly != null)
                        {
                            writer.WriteEndElement();
                        }

                        writer.WriteStartElement("assembly");
                        writer.WriteAttributeString("fullname", assemblyName);
                        currentAssembly = assemblyName;
                    }

                    writer.WriteStartElement("type");
                    writer.WriteAttributeString(
                        "fullname",
                        PlayerOperationMetadataNaming.GetLinkerTypeFullName(type));
                    writer.WriteAttributeString("preserve", types[type]);
                    writer.WriteEndElement();
                }

                if (currentAssembly != null)
                {
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return path;
        }

        internal static string WriteOperationLinkXml(
            PlayerOperationMetadataEntry[] metadataEntries)
        {
            var entries = new Dictionary<string, PlayerOperationMetadataEntry>(
                StringComparer.Ordinal);
            for (int index = 0; index < metadataEntries.Length; index++)
            {
                PlayerOperationMetadataEntry entry = metadataEntries[index];
                if (entry == null ||
                    string.IsNullOrEmpty(entry.AssemblyName) ||
                    string.IsNullOrEmpty(entry.TypeFullName))
                {
                    continue;
                }

                string key = $"{entry.AssemblyName}\0{entry.TypeFullName}";
                if (entries.TryGetValue(
                        key,
                        out PlayerOperationMetadataEntry existing) &&
                    string.Equals(existing.Preserve, "all", StringComparison.Ordinal))
                {
                    continue;
                }

                entries[key] = entry;
            }

            var orderedEntries = new List<PlayerOperationMetadataEntry>(entries.Values);
            orderedEntries.Sort((left, right) =>
            {
                int assemblyOrder = StringComparer.Ordinal.Compare(
                    left.AssemblyName,
                    right.AssemblyName);
                return assemblyOrder != 0
                    ? assemblyOrder
                    : StringComparer.Ordinal.Compare(left.TypeFullName, right.TypeFullName);
            });
            string directory = Path.GetFullPath(
                Path.Combine("Library", "CoCoFlow", "StateGraph", "GeneratedLink"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "link.xml");
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("linker");
                string currentAssembly = null;
                for (int index = 0; index < orderedEntries.Count; index++)
                {
                    PlayerOperationMetadataEntry entry = orderedEntries[index];
                    if (!string.Equals(
                            currentAssembly,
                            entry.AssemblyName,
                            StringComparison.Ordinal))
                    {
                        if (currentAssembly != null)
                        {
                            writer.WriteEndElement();
                        }

                        writer.WriteStartElement("assembly");
                        writer.WriteAttributeString("fullname", entry.AssemblyName);
                        currentAssembly = entry.AssemblyName;
                    }

                    writer.WriteStartElement("type");
                    writer.WriteAttributeString("fullname", entry.TypeFullName);
                    writer.WriteAttributeString("preserve", entry.Preserve);
                    writer.WriteEndElement();
                }

                if (currentAssembly != null)
                {
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return path;
        }

        private static void CollectValueTypes(Type type, IDictionary<Type, string> types)
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
                    CollectValueTypes(arguments[index], types);
                }
            }

            if (type.IsPrimitive ||
                type == typeof(decimal) ||
                type.Assembly == typeof(int).Assembly)
            {
                return;
            }

            Type metadataType = type.IsGenericType
                ? type.GetGenericTypeDefinition()
                : type;
            if (!types.ContainsKey(metadataType))
            {
                types.Add(metadataType, "fields");
            }

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
                CollectValueTypes(fields[index].FieldType, types);
            }
        }
    }

    public sealed class StateGraphBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        /// <summary>清理上次构建的链接状态，并在项目包含状态图资产时校验当前构建输入。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            StateGraphOperationLinkerState.Clear();
            if (!StateGraphBuildValidation.HasStateGraphAssets())
            {
                return;
            }

            GraphDescriptorCatalog catalog = StateGraphBuildValidation.RequireCatalog();
            StateGraphBuildValidation.ValidateForBuild(catalog);
        }
    }

    public sealed class StateGraphPlayerScriptAssemblyProcessor :
        IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -1000;

        /// <summary>检查 Player 脚本程序集的图创作依赖和 Operation 元数据，并准备本次构建的 UnityLinker 保留文件。</summary>
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (report == null)
            {
                throw new BuildFailedException(
                    "Player script assembly metadata report is unavailable.");
            }

            bool hasAssets = StateGraphBuildValidation.HasStateGraphAssets();
            UnityAssembly[] playerAssemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Player);
            CoCoDiagnostic[] closureDiagnostics = hasAssets
                ? StateGraphAuthoringDependencyClosureValidator.Validate(
                    StateGraphBuildValidation.RequireCatalog(), playerAssemblies)
                : Array.Empty<CoCoDiagnostic>();
            var buildFiles = report.GetFiles();
            var assemblyPaths = new string[buildFiles.Length];
            for (int index = 0; index < buildFiles.Length; index++)
            {
                assemblyPaths[index] = buildFiles[index].path;
            }

            PlayerOperationMetadataEntry[] metadataEntries =
                StateGraphBuildValidation.CollectAndValidatePlayerOperationMetadata(
                    playerAssemblies,
                    assemblyPaths,
                    out CoCoDiagnostic[] operationDiagnostics,
                    hasAssets);
            StateGraphBuildValidation.ThrowIfBuildDiagnostics(
                closureDiagnostics,
                operationDiagnostics);
            string linkXmlPath =
                StateGraphBuildValidation.WriteOperationLinkXml(metadataEntries);
            StateGraphOperationLinkerState.Set(report, linkXmlPath);
        }
    }

    public sealed class StateGraphOperationLinkerProcessor : IUnityLinkerProcessor
    {
        public int callbackOrder => -1000;

        /// <summary>返回本次构建已生成的 Operation 保留文件；所需元数据缺失时停止构建。</summary>
        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data)
        {
            if (report == null && !StateGraphBuildValidation.HasStateGraphAssets())
            {
                return null;
            }

            if (StateGraphOperationLinkerState.TryGet(report, out string linkXmlPath))
            {
                return linkXmlPath;
            }

            throw new BuildFailedException(
                "Player Operation Section metadata was not prepared before UnityLinker.");
        }
    }

    internal static class StateGraphOperationLinkerState
    {
        private static readonly object Sync = new object();
        private static string buildKey;
        private static string linkXmlPath;

        internal static void Clear()
        {
            lock (Sync)
            {
                buildKey = null;
                linkXmlPath = null;
            }
        }

        internal static void Set(BuildReport report, string path)
        {
            lock (Sync)
            {
                buildKey = Key(report);
                linkXmlPath = path;
            }
        }

        internal static bool TryGet(BuildReport report, out string path)
        {
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(linkXmlPath) &&
                    File.Exists(linkXmlPath) &&
                    string.Equals(buildKey, Key(report), StringComparison.Ordinal))
                {
                    path = linkXmlPath;
                    return true;
                }
            }

            path = null;
            return false;
        }

        private static string Key(BuildReport report)
        {
            if (report == null)
            {
                return null;
            }

            return $"{report.summary.platform}|{report.summary.outputPath}";
        }
    }
}
