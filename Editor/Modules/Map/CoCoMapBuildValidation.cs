using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEditor.UnityLinker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Editor.Modules.Map
{
    internal static class CoCoMapBuildValidation
    {
        private const string LinkerDirectory =
            "Library/CoCoFlow/Map";

        internal static bool HasRegionBindings()
        {
            return AssetDatabase.FindAssets(
                       "t:" + nameof(CoCoRegionBinding))
                   .Length > 0;
        }

        internal static Type[] ValidateForBuild()
        {
            CoCoRegionBinding[] bindings =
                LoadBindings(out string[] bindingPaths);
            if (bindings.Length == 0)
            {
                return Array.Empty<Type>();
            }

            var messages =
                new SortedSet<string>(StringComparer.Ordinal);
            ValidateManagedReferences(
                bindings,
                bindingPaths,
                messages);

            if (!CoCoMapAuthoringContext.TryResolveGlobal(
                    out RegionParticipantCatalog catalog,
                    out IRegionAddressableSceneResolver resolver,
                    out string providerFailure))
            {
                messages.Add(providerFailure);
                ThrowIfErrors(messages);
                return Array.Empty<Type>();
            }

            IReadOnlyList<RegionCompiledPlan> plans =
                CompilePlans(
                    bindings,
                    bindingPaths,
                    catalog,
                    resolver,
                    messages);

            ValidatePlayerAssemblyClosure(
                catalog,
                messages);
            if (messages.Count == 0)
            {
                ValidateColdStartScenes(
                    plans,
                    messages);
            }

            ThrowIfErrors(messages);
            return CollectAotTypes(catalog);
        }

        internal static void ValidatePlayerScenesForBuild(
            IReadOnlyCollection<string> playerScenePaths)
        {
            CoCoRegionBinding[] bindings =
                LoadBindings(out string[] bindingPaths);
            if (bindings.Length == 0)
            {
                return;
            }

            var messages =
                new SortedSet<string>(StringComparer.Ordinal);
            if (!CoCoMapAuthoringContext.TryResolveGlobal(
                    out RegionParticipantCatalog catalog,
                    out IRegionAddressableSceneResolver resolver,
                    out string providerFailure))
            {
                messages.Add(providerFailure);
                ThrowIfErrors(messages);
                return;
            }

            IReadOnlyList<RegionCompiledPlan> plans =
                CompilePlans(
                    bindings,
                    bindingPaths,
                    catalog,
                    resolver,
                    messages);
            if (messages.Count == 0)
            {
                var directScenePaths =
                    new SortedSet<string>(StringComparer.Ordinal);
                for (int planIndex = 0;
                     planIndex < plans.Count;
                     planIndex++)
                {
                    IReadOnlyList<RegionCompiledChunk> chunks =
                        plans[planIndex].Chunks;
                    for (int chunkIndex = 0;
                         chunkIndex < chunks.Count;
                         chunkIndex++)
                    {
                        RegionCompiledSceneReference scene =
                            chunks[chunkIndex].SceneReference;
                        if (scene.SourceKind == ContentSourceKind.Direct)
                        {
                            directScenePaths.Add(
                                scene.CanonicalScenePath);
                        }
                    }
                }

                ValidateDirectScenePathsForBuild(
                    directScenePaths,
                    playerScenePaths);
            }

            ThrowIfErrors(messages);
        }

        internal static void ValidateDirectScenePathsForBuild(
            IReadOnlyCollection<string> directScenePaths,
            IReadOnlyCollection<string> playerScenePaths)
        {
            var playerScenes =
                new HashSet<string>(StringComparer.Ordinal);
            if (playerScenePaths != null)
            {
                foreach (string path in playerScenePaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        playerScenes.Add(path);
                    }
                }
            }

            var messages =
                new SortedSet<string>(StringComparer.Ordinal);
            if (directScenePaths != null)
            {
                foreach (string path in directScenePaths)
                {
                    if (!string.IsNullOrEmpty(path) &&
                        !playerScenes.Contains(path))
                    {
                        messages.Add(
                            "Direct Map Scene '" + path +
                            "' is not included in this Player build.");
                    }
                }
            }

            ThrowIfErrors(messages);
        }

        internal static string WriteLinkXml(
            IReadOnlyList<Type> source)
        {
            var typesByAssembly =
                new SortedDictionary<
                    string,
                    SortedSet<string>>(
                    StringComparer.Ordinal);
            if (source != null)
            {
                for (int index = 0;
                     index < source.Count;
                     index++)
                {
                    Type type = source[index];
                    if (type == null ||
                        string.IsNullOrEmpty(type.FullName))
                    {
                        continue;
                    }

                    string assemblyName =
                        type.Assembly.GetName().Name;
                    if (!typesByAssembly.TryGetValue(
                            assemblyName,
                            out SortedSet<string> names))
                    {
                        names = new SortedSet<string>(
                            StringComparer.Ordinal);
                        typesByAssembly.Add(
                            assemblyName,
                            names);
                    }

                    names.Add(
                        type.FullName.Replace('+', '/'));
                }
            }

            string directory =
                Path.GetFullPath(LinkerDirectory);
            Directory.CreateDirectory(directory);
            string path =
                Path.Combine(directory, "link.xml");
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace
            };
            using (XmlWriter writer =
                   XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("linker");
                foreach (
                    KeyValuePair<
                        string,
                        SortedSet<string>> assembly
                    in typesByAssembly)
                {
                    writer.WriteStartElement("assembly");
                    writer.WriteAttributeString(
                        "fullname",
                        assembly.Key);
                    foreach (string typeName in assembly.Value)
                    {
                        writer.WriteStartElement("type");
                        writer.WriteAttributeString(
                            "fullname",
                            typeName);
                        writer.WriteAttributeString(
                            "preserve",
                            "all");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return path;
        }

        private static CoCoRegionBinding[] LoadBindings(
            out string[] paths)
        {
            string[] guids =
                AssetDatabase.FindAssets(
                    "t:" + nameof(CoCoRegionBinding));
            var orderedPaths =
                new List<string>(guids.Length);
            for (int index = 0;
                 index < guids.Length;
                 index++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[index]);
                if (!string.IsNullOrEmpty(path))
                {
                    orderedPaths.Add(path);
                }
            }

            orderedPaths.Sort(StringComparer.Ordinal);
            var bindings =
                new List<CoCoRegionBinding>(
                    orderedPaths.Count);
            var retainedPaths =
                new List<string>(orderedPaths.Count);
            for (int index = 0;
                 index < orderedPaths.Count;
                 index++)
            {
                CoCoRegionBinding binding =
                    AssetDatabase.LoadAssetAtPath<CoCoRegionBinding>(
                        orderedPaths[index]);
                if (binding == null) continue;
                bindings.Add(binding);
                retainedPaths.Add(orderedPaths[index]);
            }

            paths = retainedPaths.ToArray();
            return bindings.ToArray();
        }

        private static IReadOnlyList<RegionCompiledPlan> CompilePlans(
            IReadOnlyList<CoCoRegionBinding> bindings,
            IReadOnlyList<string> bindingPaths,
            RegionParticipantCatalog catalog,
            IRegionAddressableSceneResolver resolver,
            ISet<string> messages)
        {
            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    bindings,
                    catalog,
                    resolver);
            var plans = new List<RegionCompiledPlan>(results.Count);
            for (int resultIndex = 0;
                 resultIndex < results.Count;
                 resultIndex++)
            {
                RegionCompileResult result = results[resultIndex];
                for (int diagnosticIndex = 0;
                     diagnosticIndex < result.Diagnostics.Count;
                     diagnosticIndex++)
                {
                    RegionCompileDiagnostic diagnostic =
                        result.Diagnostics[diagnosticIndex];
                    if (!diagnostic.Diagnostic.IsError)
                    {
                        continue;
                    }

                    messages.Add(
                        bindingPaths[resultIndex] + " · " +
                        diagnostic.Path + ": " +
                        diagnostic.Diagnostic.Message);
                }

                if (result.Succeeded)
                {
                    plans.Add(result.Plan);
                }
            }

            return plans;
        }

        private static void ValidateManagedReferences(
            IReadOnlyList<CoCoRegionBinding> bindings,
            IReadOnlyList<string> bindingPaths,
            ISet<string> messages)
        {
            var checkedProfiles =
                new HashSet<CoCoRegionProfile>();
            for (int index = 0;
                 index < bindings.Count;
                 index++)
            {
                CoCoRegionBinding binding = bindings[index];
                string path = bindingPaths[index];
                if (CoCoMapAuthoringContext
                    .HasMissingManagedReferences(binding))
                {
                    messages.Add(
                        path +
                        " contains missing managed-reference data.");
                }

                CoCoRegionProfile profile =
                    binding.Profile;
                if (profile == null)
                {
                    messages.Add(
                        path +
                        " has no Region Profile.");
                    continue;
                }

                if (!checkedProfiles.Add(profile))
                {
                    continue;
                }

                if (CoCoMapAuthoringContext
                    .HasMissingManagedReferences(profile))
                {
                    messages.Add(
                        AssetDatabase.GetAssetPath(profile) +
                        " contains a missing SerializeReference " +
                        "participant configuration.");
                }
            }
        }

        private static void ValidatePlayerAssemblyClosure(
            RegionParticipantCatalog catalog,
            ISet<string> messages)
        {
            UnityEditor.Compilation.Assembly[] assemblies =
                CompilationPipeline.GetAssemblies(
                    AssembliesType.Player);
            var playerAssemblyNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < assemblies.Length;
                 index++)
            {
                string name = assemblies[index]?.name;
                if (!string.IsNullOrEmpty(name))
                {
                    playerAssemblyNames.Add(name);
                }
            }

            IReadOnlyList<Type> types =
                catalog.RegisteredTypes;
            for (int index = 0;
                 index < types.Count;
                 index++)
            {
                Type type = types[index];
                if (type == null ||
                    string.IsNullOrEmpty(type.FullName))
                {
                    messages.Add(
                        "The Region participant catalog contains an " +
                        "AOT type without stable metadata.");
                    continue;
                }

                if (type.ContainsGenericParameters ||
                    type.IsPointer ||
                    type.IsByRef ||
                    type.IsInterface ||
                    type.IsAbstract)
                {
                    messages.Add(
                        "Region participant AOT type '" +
                        type +
                        "' must be a closed concrete metadata type.");
                }

                string assemblyName =
                    type.Assembly.GetName().Name;
                if (!playerAssemblyNames.Contains(
                        assemblyName))
                {
                    messages.Add(
                        "Region participant AOT type '" +
                        type.FullName +
                        "' belongs to assembly '" +
                        assemblyName +
                        "', which is outside the Player assembly closure.");
                }

                if (typeof(RegionParticipantConfig)
                        .IsAssignableFrom(type) &&
                    !type.IsSerializable)
                {
                    messages.Add(
                        "Region participant config type '" +
                        type.FullName +
                        "' must be [Serializable] for SerializeReference.");
                }
            }
        }

        private static Type[] CollectAotTypes(
            RegionParticipantCatalog catalog)
        {
            var unique = new HashSet<Type>();
            IReadOnlyList<Type> registered =
                catalog.RegisteredTypes;
            for (int index = 0;
                 index < registered.Count;
                 index++)
            {
                if (registered[index] != null)
                {
                    unique.Add(registered[index]);
                }
            }

            var ordered = new List<Type>(unique);
            ordered.Sort(
                (left, right) =>
                {
                    int assemblyOrder =
                        string.CompareOrdinal(
                            left.Assembly.GetName().Name,
                            right.Assembly.GetName().Name);
                    return assemblyOrder != 0
                        ? assemblyOrder
                        : string.CompareOrdinal(
                            left.FullName,
                            right.FullName);
                });
            return ordered.ToArray();
        }

        private static void ValidateColdStartScenes(
            IReadOnlyList<RegionCompiledPlan> plans,
            ISet<string> messages)
        {
            for (int planIndex = 0;
                 planIndex < plans.Count;
                 planIndex++)
            {
                RegionCompiledPlan plan = plans[planIndex];
                for (int chunkIndex = 0;
                     chunkIndex < plan.Chunks.Count;
                     chunkIndex++)
                {
                    RegionCompiledChunk chunk =
                        plan.Chunks[chunkIndex];
                    ValidateColdStartScene(
                        plan.RegionId,
                        chunk,
                        messages);
                }
            }
        }

        private static void ValidateColdStartScene(
            RegionId regionId,
            RegionCompiledChunk chunk,
            ISet<string> messages)
        {
            string path = chunk.CanonicalScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                messages.Add(
                    "Map-managed Scene '" + path +
                    "' does not resolve to a unique Scene asset.");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedForValidation =
                !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedForValidation)
                {
                    scene = EditorSceneManager.OpenScene(
                        path,
                        OpenSceneMode.Additive);
                }

                var anchors =
                    new List<CoCoRegionChunkAnchor>();
                GameObject[] roots =
                    scene.GetRootGameObjects();
                for (int rootIndex = 0;
                     rootIndex < roots.Length;
                     rootIndex++)
                {
                    anchors.AddRange(
                        roots[rootIndex]
                            .GetComponentsInChildren<
                                CoCoRegionChunkAnchor>(true));
                }

                if (anchors.Count != 1)
                {
                    messages.Add(
                        "Map-managed Scene '" + path +
                        "' must contain exactly one " +
                        "CoCoRegionChunkAnchor; found " +
                        anchors.Count + ".");
                    return;
                }

                if (!anchors[0].TryValidateColdStart(
                        regionId,
                        chunk.ChunkId,
                        out CoCoFlow.Runtime.Core.CoCoDiagnostic
                            diagnostic))
                {
                    messages.Add(
                        "Map-managed Scene '" + path +
                        "' violates cold-start ownership: " +
                        diagnostic.Message);
                }
            }
            catch (Exception exception)
            {
                messages.Add(
                    "Map-managed Scene '" + path +
                    "' could not be validated: " +
                    exception.Message);
            }
            finally
            {
                if (openedForValidation &&
                    scene.IsValid() &&
                    scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(
                        scene,
                        true);
                }
            }
        }

        private static void ThrowIfErrors(
            ISet<string> messages)
        {
            if (messages.Count == 0) return;
            var ordered = new List<string>(messages);
            ordered.Sort(StringComparer.Ordinal);
            throw new BuildFailedException(
                string.Join(
                    Environment.NewLine,
                    ordered));
        }
    }

    internal sealed class CoCoMapPlayerSceneBuildProcessor :
        BuildPlayerProcessor
    {
        public override void PrepareForBuild(
            BuildPlayerContext buildPlayerContext)
        {
            if (!CoCoMapBuildValidation.HasRegionBindings())
            {
                return;
            }

            if (buildPlayerContext == null)
            {
                throw new BuildFailedException(
                    "Map Player Scene validation requires the active build context.");
            }

            CoCoMapBuildValidation.ValidatePlayerScenesForBuild(
                buildPlayerContext.BuildPlayerOptions.scenes);
        }
    }

    public sealed class CoCoMapBuildPreprocessor :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => -900;

        public void OnPreprocessBuild(BuildReport report)
        {
            CoCoMapLinkerState.Clear();
            if (!CoCoMapBuildValidation
                    .HasRegionBindings())
            {
                return;
            }

            Type[] aotTypes =
                CoCoMapBuildValidation.ValidateForBuild();
            string linkXmlPath =
                CoCoMapBuildValidation.WriteLinkXml(
                    aotTypes);
            CoCoMapLinkerState.Set(
                report,
                linkXmlPath);
        }
    }

    public sealed class CoCoMapLinkerProcessor :
        IUnityLinkerProcessor
    {
        public int callbackOrder => -900;

        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data)
        {
            if (!CoCoMapBuildValidation
                    .HasRegionBindings())
            {
                return null;
            }

            if (CoCoMapLinkerState.TryGet(
                    report,
                    out string path))
            {
                return path;
            }

            throw new BuildFailedException(
                "Region participant AOT metadata was not validated " +
                "before UnityLinker.");
        }
    }

    internal static class CoCoMapLinkerState
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

        internal static void Set(
            BuildReport report,
            string path)
        {
            lock (Sync)
            {
                buildKey = Key(report);
                linkXmlPath = path;
            }
        }

        internal static bool TryGet(
            BuildReport report,
            out string path)
        {
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(linkXmlPath) &&
                    File.Exists(linkXmlPath) &&
                    string.Equals(
                        buildKey,
                        Key(report),
                        StringComparison.Ordinal))
                {
                    path = linkXmlPath;
                    return true;
                }
            }

            path = null;
            return false;
        }

        private static string Key(
            BuildReport report)
        {
            if (report == null) return null;
            return report.summary.platform + "|" +
                   report.summary.outputPath;
        }
    }
}
