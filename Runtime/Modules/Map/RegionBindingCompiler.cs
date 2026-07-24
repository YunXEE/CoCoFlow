using System;
using System.Collections.Generic;
using System.Globalization;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    public interface IRegionAddressableSceneResolver
    {
        bool TryResolveUniqueScene(
            string address,
            out string sceneAssetPath,
            out CoCoDiagnostic diagnostic);
    }

    public sealed class RegionBindingCompiler
    {
        private readonly RegionProfileCompiler profileCompiler =
            new RegionProfileCompiler();

        public RegionCompileResult Compile(
            CoCoRegionBinding binding,
            RegionParticipantCatalog catalog,
            IRegionAddressableSceneResolver addressableSceneResolver = null)
        {
            var diagnostics = new List<RegionCompileDiagnostic>();
            if (binding == null)
            {
                AddError(
                    diagnostics,
                    "binding",
                    RegionErrors.CompilationFailed(
                        "A Region Binding is required."));
                return new RegionCompileResult(null, diagnostics);
            }

            if (!binding.RegionId.IsValid)
            {
                AddError(
                    diagnostics,
                    "binding.regionId",
                    RegionErrors.InvalidIdentifier(
                        "A Region Binding requires one stable RegionId."));
            }

            if (!profileCompiler.TryCompile(
                    binding.Profile,
                    catalog,
                    diagnostics,
                    out RegionCompiledProfileBlueprint profile))
            {
                return new RegionCompileResult(null, diagnostics);
            }

            var definitions =
                new Dictionary<
                    RegionParticipantSlotId,
                    RegionCompiledParticipantDefinition>();
            for (int index = 0; index < profile.Participants.Count; index++)
            {
                definitions.Add(
                    profile.Participants[index].Source.SlotId,
                    profile.Participants[index]);
            }

            List<RegionCompiledChunk> chunks = CompileChunks(
                binding,
                addressableSceneResolver,
                diagnostics,
                out Dictionary<RegionChunkId, RegionCompiledSceneReference>
                    sceneByChunk);

            var globalSeeds =
                new Dictionary<RegionParticipantSlotId, NodeSeed>();
            var chunkSeeds =
                new Dictionary<
                    RegionChunkId,
                    Dictionary<RegionParticipantSlotId, NodeSeed>>();
            var allSeeds = new List<NodeSeed>();
            var bindingCountBySlot =
                new Dictionary<RegionParticipantSlotId, int>();

            CompileBindings(
                binding.RegionId,
                null,
                binding.RegionParticipants,
                default,
                default,
                definitions,
                globalSeeds,
                allSeeds,
                bindingCountBySlot,
                diagnostics,
                "binding.regionParticipants");

            for (int chunkIndex = 0;
                 chunkIndex < binding.Chunks.Count;
                 chunkIndex++)
            {
                RegionChunkBinding chunkBinding = binding.Chunks[chunkIndex];
                if (chunkBinding == null || !chunkBinding.ChunkId.IsValid)
                {
                    continue;
                }

                var scopeSeeds =
                    new Dictionary<RegionParticipantSlotId, NodeSeed>();
                if (!chunkSeeds.TryAdd(chunkBinding.ChunkId, scopeSeeds))
                {
                    continue;
                }

                sceneByChunk.TryGetValue(
                    chunkBinding.ChunkId,
                    out RegionCompiledSceneReference sceneReference);
                CompileBindings(
                    binding.RegionId,
                    chunkBinding.ChunkId,
                    chunkBinding.Participants,
                    sceneReference,
                    chunkBinding.OwningContentSlotId,
                    definitions,
                    scopeSeeds,
                    allSeeds,
                    bindingCountBySlot,
                    diagnostics,
                    "binding.chunks[" + chunkIndex + "].participants");

                if (!scopeSeeds.TryGetValue(
                        chunkBinding.OwningContentSlotId,
                        out NodeSeed owningContentSeed))
                {
                    AddError(
                        diagnostics,
                        "binding.chunks[" + chunkIndex +
                        "].owningContentSlotId",
                        RegionErrors.InvalidProfile(
                            "The owning Content slot must be bound in the same Chunk."));
                }
                else if (!owningContentSeed.Definition.Registration
                             .CanOwnChunkScene)
                {
                    AddError(
                        diagnostics,
                        "binding.chunks[" + chunkIndex +
                        "].owningContentSlotId",
                        RegionErrors.SceneContract(
                            "The owning Content slot must use a Map-authorized Chunk Scene owner registration."));
                }
            }

            ValidateRequiredBindings(
                profile.Participants,
                bindingCountBySlot,
                diagnostics);
            ResolveDependencies(
                allSeeds,
                globalSeeds,
                chunkSeeds,
                diagnostics);
            ValidateOwningContentDependencies(
                allSeeds,
                chunkSeeds,
                diagnostics);

            if (HasErrors(diagnostics))
            {
                return new RegionCompileResult(null, diagnostics);
            }

            var nodes =
                new List<RegionCompiledParticipantNode>(allSeeds.Count);
            for (int index = 0; index < allSeeds.Count; index++)
            {
                NodeSeed seed = allSeeds[index];
                RegionCompiledParticipantNode node = FreezeNode(
                    seed,
                    diagnostics);
                if (node != null) nodes.Add(node);
            }

            if (HasErrors(diagnostics))
            {
                return new RegionCompileResult(null, diagnostics);
            }

            chunks.Sort(CompareChunks);
            nodes.Sort(CompareNodes);
            string fingerprint = BuildPlanFingerprint(
                binding.RegionId,
                profile.Tiers,
                chunks,
                nodes);
            var plan = new RegionCompiledPlan(
                binding.RegionId,
                new List<RegionCompiledTier>(profile.Tiers),
                chunks,
                nodes,
                fingerprint);
            return new RegionCompileResult(plan, diagnostics);
        }

        public IReadOnlyList<RegionCompileResult> CompileAll(
            IEnumerable<CoCoRegionBinding> bindings,
            RegionParticipantCatalog catalog,
            IRegionAddressableSceneResolver addressableSceneResolver = null)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));

            var results = new List<RegionCompileResult>();
            foreach (CoCoRegionBinding binding in bindings)
            {
                results.Add(Compile(binding, catalog, addressableSceneResolver));
            }

            var ownersByPath =
                new Dictionary<string, List<SceneOwner>>(
                    StringComparer.Ordinal);
            var ownersByContentId =
                new Dictionary<ContentId, List<SceneOwner>>();
            var ownersByRegionId =
                new Dictionary<RegionId, List<int>>();
            for (int resultIndex = 0;
                 resultIndex < results.Count;
                 resultIndex++)
            {
                RegionCompileResult result = results[resultIndex];
                if (!result.Succeeded) continue;

                if (!ownersByRegionId.TryGetValue(
                        result.Plan.RegionId,
                        out List<int> regionOwners))
                {
                    regionOwners = new List<int>();
                    ownersByRegionId.Add(
                        result.Plan.RegionId,
                        regionOwners);
                }

                regionOwners.Add(resultIndex);
                for (int chunkIndex = 0;
                     chunkIndex < result.Plan.Chunks.Count;
                     chunkIndex++)
                {
                    RegionCompiledChunk chunk = result.Plan.Chunks[chunkIndex];
                    if (!ownersByPath.TryGetValue(
                            chunk.CanonicalScenePath,
                            out List<SceneOwner> owners))
                    {
                        owners = new List<SceneOwner>();
                        ownersByPath.Add(chunk.CanonicalScenePath, owners);
                    }

                    var owner = new SceneOwner(
                        resultIndex,
                        result.Plan.RegionId,
                        chunk.ChunkId,
                        chunk.OwningContentSlotId);
                    owners.Add(owner);
                    if (!ownersByContentId.TryGetValue(
                            chunk.SceneReference.ContentId,
                            out List<SceneOwner> contentOwners))
                    {
                        contentOwners = new List<SceneOwner>();
                        ownersByContentId.Add(
                            chunk.SceneReference.ContentId,
                            contentOwners);
                    }

                    contentOwners.Add(owner);
                }
            }

            var duplicateDiagnostics =
                new Dictionary<int, List<RegionCompileDiagnostic>>();
            foreach (KeyValuePair<RegionId, List<int>> pair in ownersByRegionId)
            {
                if (pair.Value.Count < 2) continue;

                for (int index = 0; index < pair.Value.Count; index++)
                {
                    AddDuplicateDiagnostic(
                        duplicateDiagnostics,
                        pair.Value[index],
                        "binding.regionId",
                        RegionErrors.InvalidIdentifier(
                            "RegionId '" + pair.Key.Value +
                            "' is owned by more than one bootstrap Binding."));
                }
            }

            foreach (
                KeyValuePair<ContentId, List<SceneOwner>> pair
                in ownersByContentId)
            {
                if (pair.Value.Count < 2) continue;

                string owners = FormatOwners(pair.Value);
                for (int index = 0; index < pair.Value.Count; index++)
                {
                    AddDuplicateDiagnostic(
                        duplicateDiagnostics,
                        pair.Value[index].ResultIndex,
                        "binding.chunks.sceneSource",
                        RegionErrors.SceneContract(
                            "ContentId '" + pair.Key.Value +
                            "' has multiple owning Region/Chunk/Slot tuples: " +
                            owners + "."));
                }
            }

            foreach (KeyValuePair<string, List<SceneOwner>> pair in ownersByPath)
            {
                if (pair.Value.Count < 2) continue;

                string owners = FormatOwners(pair.Value);
                for (int index = 0; index < pair.Value.Count; index++)
                {
                    SceneOwner owner = pair.Value[index];
                    AddDuplicateDiagnostic(
                        duplicateDiagnostics,
                        owner.ResultIndex,
                        "binding.chunks.sceneSource",
                        RegionErrors.SceneContract(
                            "Scene '" + pair.Key +
                            "' has multiple owning Region/Chunk/Slot tuples: " +
                            owners + "."));
                }
            }

            foreach (
                KeyValuePair<int, List<RegionCompileDiagnostic>> pair
                in duplicateDiagnostics)
            {
                var diagnostics = new List<RegionCompileDiagnostic>(
                    results[pair.Key].Diagnostics);
                diagnostics.AddRange(pair.Value);
                results[pair.Key] =
                    new RegionCompileResult(null, diagnostics);
            }

            return results.AsReadOnly();
        }

        private static void AddDuplicateDiagnostic(
            IDictionary<int, List<RegionCompileDiagnostic>> diagnosticsByResult,
            int resultIndex,
            string path,
            CoCoDiagnostic diagnostic)
        {
            if (!diagnosticsByResult.TryGetValue(
                    resultIndex,
                    out List<RegionCompileDiagnostic> diagnostics))
            {
                diagnostics = new List<RegionCompileDiagnostic>();
                diagnosticsByResult.Add(resultIndex, diagnostics);
            }

            diagnostics.Add(
                new RegionCompileDiagnostic(
                    path,
                    diagnostic));
        }

        private static List<RegionCompiledChunk> CompileChunks(
            CoCoRegionBinding binding,
            IRegionAddressableSceneResolver addressableSceneResolver,
            IList<RegionCompileDiagnostic> diagnostics,
            out Dictionary<RegionChunkId, RegionCompiledSceneReference>
                sceneByChunk)
        {
            var compiled =
                new List<RegionCompiledChunk>(binding.Chunks.Count);
            sceneByChunk =
                new Dictionary<RegionChunkId, RegionCompiledSceneReference>();
            var contentIds = new HashSet<ContentId>();
            var scenePaths = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < binding.Chunks.Count; index++)
            {
                RegionChunkBinding chunk = binding.Chunks[index];
                string path = "binding.chunks[" + index + "]";
                if (chunk == null)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Chunk bindings cannot be null."));
                    continue;
                }

                if (!chunk.ChunkId.IsValid ||
                    sceneByChunk.ContainsKey(chunk.ChunkId))
                {
                    AddError(
                        diagnostics,
                        path + ".chunkId",
                        RegionErrors.InvalidIdentifier(
                            "ChunkId values must be valid and unique within a Region."));
                    continue;
                }

                if (!chunk.OwningContentSlotId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path + ".owningContentSlotId",
                        RegionErrors.InvalidIdentifier(
                            "A Chunk requires one owning Content participant SlotId."));
                    continue;
                }

                if (!TryCompileScene(
                        chunk.SceneSource,
                        addressableSceneResolver,
                        out RegionCompiledSceneReference sceneReference,
                        out CoCoDiagnostic sceneDiagnostic))
                {
                    AddError(
                        diagnostics,
                        path + ".sceneSource",
                        sceneDiagnostic);
                    continue;
                }

                if (!contentIds.Add(sceneReference.ContentId))
                {
                    AddError(
                        diagnostics,
                        path + ".sceneSource",
                        RegionErrors.SceneContract(
                            "ContentId '" +
                            sceneReference.ContentId.Value +
                            "' is already owned by another Chunk in this Region."));
                }

                if (!scenePaths.Add(sceneReference.CanonicalScenePath))
                {
                    AddError(
                        diagnostics,
                        path + ".sceneSource",
                        RegionErrors.SceneContract(
                            "Scene '" +
                            sceneReference.CanonicalScenePath +
                            "' is already owned by another Chunk in this Region."));
                }

                sceneByChunk.Add(chunk.ChunkId, sceneReference);
                compiled.Add(
                    new RegionCompiledChunk(
                        chunk.ChunkId,
                        sceneReference,
                        chunk.OwningContentSlotId));
            }

            return compiled;
        }

        private static bool TryCompileScene(
            ContentReference source,
            IRegionAddressableSceneResolver addressableSceneResolver,
            out RegionCompiledSceneReference sceneReference,
            out CoCoDiagnostic diagnostic)
        {
            sceneReference = default;
            if (!source.IsValid ||
                source.Kind != ContentKind.AdditiveScene)
            {
                diagnostic = RegionErrors.SceneContract(
                    "Chunk scene sources must be valid Additive Scene ContentReferences.");
                return false;
            }

            if (source.SourceKind == ContentSourceKind.Direct)
            {
                if (!IsCanonicalScenePath(source.Location))
                {
                    diagnostic = RegionErrors.SceneContract(
                        "Direct Scene locators must be canonical full asset paths in the form Assets/.../*.unity.");
                    return false;
                }

                sceneReference = new RegionCompiledSceneReference(
                    source.Id,
                    source.SourceKind,
                    source.Location,
                    source.Location);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (source.SourceKind != ContentSourceKind.Addressables ||
                addressableSceneResolver == null)
            {
                diagnostic = RegionErrors.SceneContract(
                    "Addressable Scene validation requires an explicit unique-scene resolver.");
                return false;
            }

            string sceneAssetPath;
            try
            {
                if (!addressableSceneResolver.TryResolveUniqueScene(
                        source.Location,
                        out sceneAssetPath,
                        out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = RegionErrors.SceneContract(
                            "Address '" + source.Location +
                            "' did not resolve to exactly one Scene asset.");
                    }

                    return false;
                }
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.SceneContract(
                    "Addressable Scene resolution failed: " +
                    exception.Message);
                return false;
            }

            if (!IsCanonicalScenePath(sceneAssetPath))
            {
                diagnostic = RegionErrors.SceneContract(
                    "Address '" + source.Location +
                    "' must resolve to one canonical Assets/.../*.unity path.");
                return false;
            }

            sceneReference = new RegionCompiledSceneReference(
                source.Id,
                source.SourceKind,
                source.Location,
                sceneAssetPath);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool IsCanonicalScenePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
                !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".unity", StringComparison.Ordinal) ||
                path.IndexOf('\\') >= 0 ||
                path.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            string[] segments = path.Split('/');
            if (segments.Length < 2) return false;
            for (int index = 0; index < segments.Length; index++)
            {
                if (string.IsNullOrEmpty(segments[index]) ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CompileBindings(
            RegionId regionId,
            RegionChunkId? chunkId,
            IReadOnlyList<RegionParticipantSlotBinding> bindings,
            RegionCompiledSceneReference sceneReference,
            RegionParticipantSlotId owningContentSlotId,
            IReadOnlyDictionary<
                RegionParticipantSlotId,
                RegionCompiledParticipantDefinition> definitions,
            IDictionary<RegionParticipantSlotId, NodeSeed> scopeSeeds,
            ICollection<NodeSeed> allSeeds,
            IDictionary<RegionParticipantSlotId, int> bindingCountBySlot,
            IList<RegionCompileDiagnostic> diagnostics,
            string path)
        {
            for (int index = 0; index < bindings.Count; index++)
            {
                RegionParticipantSlotBinding binding = bindings[index];
                string bindingPath = path + "[" + index + "]";
                if (binding == null || !binding.SlotId.IsValid)
                {
                    AddError(
                        diagnostics,
                        bindingPath,
                        RegionErrors.InvalidIdentifier(
                            "Participant bindings require a valid SlotId."));
                    continue;
                }

                if (!definitions.TryGetValue(
                        binding.SlotId,
                        out RegionCompiledParticipantDefinition definition))
                {
                    AddError(
                        diagnostics,
                        bindingPath + ".slotId",
                        RegionErrors.InvalidProfile(
                            "Bound slot '" + binding.SlotId.Value +
                            "' is not defined by the selected Profile."));
                    continue;
                }

                if (!IsValidFragmentId(binding.FragmentId))
                {
                    AddError(
                        diagnostics,
                        bindingPath + ".fragmentId",
                        RegionErrors.InvalidProfile(
                            "Fragment ids must be empty or canonical '/' separated relative paths."));
                    continue;
                }

                RegionPlanNodeId nodeId;
                bool created = chunkId.HasValue
                    ? RegionPlanNodeId.TryCreateChunk(
                        regionId,
                        chunkId.Value,
                        binding.SlotId,
                        out nodeId)
                    : RegionPlanNodeId.TryCreateGlobal(
                        regionId,
                        binding.SlotId,
                        out nodeId);
                if (!created || scopeSeeds.ContainsKey(binding.SlotId))
                {
                    AddError(
                        diagnostics,
                        bindingPath + ".slotId",
                        RegionErrors.InvalidProfile(
                            "Participant SlotId values must be unique within a Region-global or Chunk scope."));
                    continue;
                }

                RegionCompiledSceneReference participantScene =
                    chunkId.HasValue &&
                    binding.SlotId == owningContentSlotId
                        ? sceneReference
                        : default;
                var seed = new NodeSeed(
                    nodeId,
                    binding,
                    definition,
                    participantScene,
                    owningContentSlotId);
                scopeSeeds.Add(binding.SlotId, seed);
                allSeeds.Add(seed);

                bindingCountBySlot.TryGetValue(
                    binding.SlotId,
                    out int bindingCount);
                bindingCountBySlot[binding.SlotId] = bindingCount + 1;
            }
        }

        private static bool IsValidFragmentId(string fragmentId)
        {
            if (string.IsNullOrEmpty(fragmentId)) return true;
            if (string.IsNullOrWhiteSpace(fragmentId) ||
                !string.Equals(
                    fragmentId,
                    fragmentId.Trim(),
                    StringComparison.Ordinal) ||
                fragmentId.StartsWith("/", StringComparison.Ordinal) ||
                fragmentId.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            string[] segments = fragmentId.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (string.IsNullOrEmpty(segments[index]) ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateRequiredBindings(
            IReadOnlyList<RegionCompiledParticipantDefinition> definitions,
            IReadOnlyDictionary<RegionParticipantSlotId, int> bindingCountBySlot,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                RegionParticipantDefinition definition =
                    definitions[index].Source;
                if (definition.Requirement !=
                    RegionParticipantRequirement.Required)
                {
                    continue;
                }

                if (!bindingCountBySlot.TryGetValue(
                        definition.SlotId,
                        out int count) ||
                    count == 0)
                {
                    AddError(
                        diagnostics,
                        "binding.participants." +
                        definition.SlotId.Value,
                        RegionErrors.InvalidProfile(
                            "Required participant slot '" +
                            definition.SlotId.Value +
                            "' has no Binding."));
                }
            }
        }

        private static void ResolveDependencies(
            IReadOnlyList<NodeSeed> seeds,
            IReadOnlyDictionary<RegionParticipantSlotId, NodeSeed> globalSeeds,
            IReadOnlyDictionary<
                RegionChunkId,
                Dictionary<RegionParticipantSlotId, NodeSeed>> chunkSeeds,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            for (int seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
            {
                NodeSeed seed = seeds[seedIndex];
                IReadOnlyList<RegionParticipantSlotId> dependencies =
                    seed.Definition.Source.Dependencies;
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.Count;
                     dependencyIndex++)
                {
                    RegionParticipantSlotId dependencySlot =
                        dependencies[dependencyIndex];
                    NodeSeed dependency = null;
                    if (!seed.Id.HasChunkId)
                    {
                        globalSeeds.TryGetValue(
                            dependencySlot,
                            out dependency);
                    }
                    else
                    {
                        NodeSeed local = null;
                        bool hasLocal = false;
                        if (chunkSeeds.TryGetValue(
                                seed.Id.ChunkId,
                                out Dictionary<
                                    RegionParticipantSlotId,
                                    NodeSeed> localSeeds))
                        {
                            hasLocal = localSeeds.TryGetValue(
                                dependencySlot,
                                out local);
                        }
                        bool hasGlobal = globalSeeds.TryGetValue(
                            dependencySlot,
                            out NodeSeed global);
                        if (hasLocal && hasGlobal)
                        {
                            AddError(
                                diagnostics,
                                "binding.nodes." + seed.Id +
                                ".dependencies",
                                RegionErrors.InvalidProfile(
                                    "Dependency slot '" +
                                    dependencySlot.Value +
                                    "' is ambiguous between Region-global and the same Chunk."));
                            continue;
                        }

                        dependency = hasLocal ? local : global;
                    }

                    if (dependency == null)
                    {
                        AddError(
                            diagnostics,
                            "binding.nodes." + seed.Id +
                            ".dependencies",
                            RegionErrors.InvalidProfile(
                                "Dependency slot '" +
                                dependencySlot.Value +
                                "' must be bound Region-global or in the same Chunk; cross-Chunk edges are forbidden."));
                        continue;
                    }

                    if (CompareExecutionOrder(
                            dependency.Definition.Source,
                            seed.Definition.Source) >= 0)
                    {
                        AddError(
                            diagnostics,
                            "binding.nodes." + seed.Id +
                            ".dependencies",
                            RegionErrors.InvalidProfile(
                                "Dependencies must precede dependants under Phase, explicit order, and SlotId ordering."));
                        continue;
                    }

                    seed.Dependencies.Add(dependency.Id);
                }

                seed.Dependencies.Sort(CompareNodeIds);
            }
        }

        private static void ValidateOwningContentDependencies(
            IReadOnlyList<NodeSeed> seeds,
            IReadOnlyDictionary<
                RegionChunkId,
                Dictionary<RegionParticipantSlotId, NodeSeed>> chunkSeeds,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            for (int index = 0; index < seeds.Count; index++)
            {
                NodeSeed seed = seeds[index];
                if (!seed.Id.HasChunkId ||
                    !(seed.Definition.Registration.ConfigFreezer is
                        IRegionRequiresOwningContentDependency))
                {
                    continue;
                }

                if (!seed.OwningContentSlotId.IsValid ||
                    !chunkSeeds.TryGetValue(
                        seed.Id.ChunkId,
                        out Dictionary<
                            RegionParticipantSlotId,
                            NodeSeed> localSeeds) ||
                    !localSeeds.TryGetValue(
                        seed.OwningContentSlotId,
                        out NodeSeed owningContentSeed) ||
                    !owningContentSeed.Definition.Registration
                        .CanOwnChunkScene ||
                    !seed.Dependencies.Contains(owningContentSeed.Id))
                {
                    AddError(
                        diagnostics,
                        "binding.nodes." + seed.Id + ".dependencies",
                        RegionErrors.InvalidProfile(
                            "A Chunk-scoped participant that owns dependent resources must directly depend on the same Chunk's authoritative Content slot."));
                }
            }
        }

        private static RegionCompiledParticipantNode FreezeNode(
            NodeSeed seed,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            IRegionParticipantPlan participantPlan;
            CoCoDiagnostic diagnostic;
            try
            {
                var context = new RegionParticipantFreezeContext(
                    seed.Id,
                    seed.Binding.FragmentId,
                    seed.SceneReference);
                if (!seed.Definition.Registration.ConfigFreezer.TryFreeze(
                        context,
                        seed.Definition.Source.Configuration,
                        out participantPlan,
                        out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = RegionErrors.CompilationFailed(
                            "Participant config freezing failed without a diagnostic.");
                    }

                    AddError(
                        diagnostics,
                        "binding.nodes." + seed.Id + ".configuration",
                        diagnostic);
                    return null;
                }
            }
            catch (Exception exception)
            {
                AddError(
                    diagnostics,
                    "binding.nodes." + seed.Id + ".configuration",
                    RegionErrors.CompilationFailed(
                        "Participant config freezing threw: " +
                        exception.Message));
                return null;
            }

            Type expectedPlanType =
                seed.Definition.Registration.PlanType;
            if (participantPlan == null ||
                participantPlan.GetType() != expectedPlanType)
            {
                AddError(
                    diagnostics,
                    "binding.nodes." + seed.Id + ".configuration",
                    RegionErrors.CompilationFailed(
                        "The config freezer must return exactly its registered immutable plan type."));
                return null;
            }

            string participantFingerprint;
            try
            {
                participantFingerprint = participantPlan.Fingerprint;
            }
            catch (Exception exception)
            {
                AddError(
                    diagnostics,
                    "binding.nodes." + seed.Id + ".configuration",
                    RegionErrors.CompilationFailed(
                        "The frozen participant plan fingerprint threw: " +
                        exception.Message));
                return null;
            }

            if (string.IsNullOrWhiteSpace(participantFingerprint))
            {
                AddError(
                    diagnostics,
                    "binding.nodes." + seed.Id + ".configuration",
                    RegionErrors.CompilationFailed(
                        "The frozen participant plan requires a deterministic non-empty fingerprint."));
                return null;
            }

            if (!RegionPlanPurityValidator.TryValidate(
                    participantPlan,
                    out string purityFailure))
            {
                AddError(
                    diagnostics,
                    "binding.nodes." + seed.Id + ".configuration",
                    RegionErrors.CompilationFailed(
                        "The frozen participant plan is not immutable pure data: " +
                        purityFailure));
                return null;
            }

            string fingerprint = BuildNodeFingerprint(
                seed,
                participantPlan,
                participantFingerprint);
            return new RegionCompiledParticipantNode(
                seed.Id,
                seed.Definition.Source.ParticipantTypeId,
                seed.Definition.Source.ModeId,
                seed.Definition.Source.Phase,
                seed.Definition.Source.ExplicitOrder,
                seed.Definition.Source.Requirement,
                seed.Definition.RequiredCapabilities,
                seed.Dependencies,
                participantPlan,
                seed.Binding.FragmentId,
                seed.SceneReference,
                fingerprint);
        }

        private static string BuildNodeFingerprint(
            NodeSeed seed,
            IRegionParticipantPlan participantPlan,
            string participantFingerprint)
        {
            var builder = new FingerprintBuilder();
            builder.Append(seed.Id.ToString());
            builder.Append(seed.Definition.Source.ParticipantTypeId.Value);
            builder.Append(seed.Definition.Source.ModeId.Value);
            builder.Append((int)seed.Definition.Source.Phase);
            builder.Append(seed.Definition.Source.ExplicitOrder);
            builder.Append((int)seed.Definition.Source.Requirement);
            for (int index = 0;
                 index < seed.Definition.RequiredCapabilities.Count;
                 index++)
            {
                builder.Append(
                    seed.Definition.RequiredCapabilities
                        .Capabilities[index].Value);
            }

            for (int index = 0; index < seed.Dependencies.Count; index++)
            {
                builder.Append(seed.Dependencies[index].ToString());
            }

            builder.Append(seed.Binding.FragmentId);
            AppendScene(ref builder, seed.SceneReference);
            builder.Append(
                participantPlan.GetType().AssemblyQualifiedName);
            builder.Append(participantFingerprint);
            return builder.Complete();
        }

        private static string BuildPlanFingerprint(
            RegionId regionId,
            IReadOnlyList<RegionCompiledTier> tiers,
            IReadOnlyList<RegionCompiledChunk> chunks,
            IReadOnlyList<RegionCompiledParticipantNode> nodes)
        {
            var builder = new FingerprintBuilder();
            builder.Append(regionId.Value);
            for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
            {
                RegionCompiledTier tier = tiers[tierIndex];
                builder.Append(tier.Index);
                builder.Append(tier.Name);
                for (int capabilityIndex = 0;
                     capabilityIndex < tier.Capabilities.Count;
                     capabilityIndex++)
                {
                    builder.Append(
                        tier.Capabilities.Capabilities[capabilityIndex].Value);
                }
            }

            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                RegionCompiledChunk chunk = chunks[chunkIndex];
                builder.Append(chunk.ChunkId.Value);
                builder.Append(chunk.OwningContentSlotId.Value);
                AppendScene(ref builder, chunk.SceneReference);
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                builder.Append(nodes[nodeIndex].Id.ToString());
                builder.Append(nodes[nodeIndex].Fingerprint);
            }

            return builder.Complete();
        }

        private static void AppendScene(
            ref FingerprintBuilder builder,
            RegionCompiledSceneReference scene)
        {
            if (!scene.IsValid)
            {
                builder.Append(string.Empty);
                return;
            }

            builder.Append(scene.ContentId.Value);
            builder.Append((int)scene.SourceKind);
            builder.Append(scene.Locator);
            builder.Append(scene.CanonicalScenePath);
        }

        private static int CompareChunks(
            RegionCompiledChunk left,
            RegionCompiledChunk right) =>
            string.CompareOrdinal(
                left.ChunkId.Value,
                right.ChunkId.Value);

        private static int CompareNodes(
            RegionCompiledParticipantNode left,
            RegionCompiledParticipantNode right)
        {
            int phase = left.Phase.CompareTo(right.Phase);
            if (phase != 0) return phase;

            int order = left.ExplicitOrder.CompareTo(right.ExplicitOrder);
            if (order != 0) return order;

            int slot = string.CompareOrdinal(
                left.Id.SlotId.Value,
                right.Id.SlotId.Value);
            return slot != 0
                ? slot
                : CompareNodeIds(left.Id, right.Id);
        }

        private static int CompareExecutionOrder(
            RegionParticipantDefinition left,
            RegionParticipantDefinition right)
        {
            int phase = left.Phase.CompareTo(right.Phase);
            if (phase != 0) return phase;

            int order = left.ExplicitOrder.CompareTo(right.ExplicitOrder);
            return order != 0
                ? order
                : string.CompareOrdinal(
                    left.SlotId.Value,
                    right.SlotId.Value);
        }

        private static int CompareNodeIds(
            RegionPlanNodeId left,
            RegionPlanNodeId right) =>
            string.CompareOrdinal(left.ToString(), right.ToString());

        private static string FormatOwners(IReadOnlyList<SceneOwner> owners)
        {
            var descriptions = new string[owners.Count];
            for (int index = 0; index < owners.Count; index++)
            {
                descriptions[index] =
                    owners[index].RegionId.Value + "/" +
                    owners[index].ChunkId.Value + "/" +
                    owners[index].SlotId.Value;
            }

            Array.Sort(descriptions, StringComparer.Ordinal);
            return string.Join(", ", descriptions);
        }

        private static bool HasErrors(
            IReadOnlyList<RegionCompileDiagnostic> diagnostics)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Diagnostic.IsError) return true;
            }

            return false;
        }

        private static void AddError(
            IList<RegionCompileDiagnostic> diagnostics,
            string path,
            CoCoDiagnostic diagnostic) =>
            diagnostics.Add(new RegionCompileDiagnostic(path, diagnostic));

        private sealed class NodeSeed
        {
            internal NodeSeed(
                RegionPlanNodeId id,
                RegionParticipantSlotBinding binding,
                RegionCompiledParticipantDefinition definition,
                RegionCompiledSceneReference sceneReference,
                RegionParticipantSlotId owningContentSlotId)
            {
                Id = id;
                Binding = binding;
                Definition = definition;
                SceneReference = sceneReference;
                OwningContentSlotId = owningContentSlotId;
            }

            internal RegionPlanNodeId Id { get; }
            internal RegionParticipantSlotBinding Binding { get; }
            internal RegionCompiledParticipantDefinition Definition { get; }
            internal RegionCompiledSceneReference SceneReference { get; }
            internal RegionParticipantSlotId OwningContentSlotId { get; }
            internal List<RegionPlanNodeId> Dependencies { get; } =
                new List<RegionPlanNodeId>();
        }

        private readonly struct SceneOwner
        {
            internal SceneOwner(
                int resultIndex,
                RegionId regionId,
                RegionChunkId chunkId,
                RegionParticipantSlotId slotId)
            {
                ResultIndex = resultIndex;
                RegionId = regionId;
                ChunkId = chunkId;
                SlotId = slotId;
            }

            internal int ResultIndex { get; }
            internal RegionId RegionId { get; }
            internal RegionChunkId ChunkId { get; }
            internal RegionParticipantSlotId SlotId { get; }
        }

        private struct FingerprintBuilder
        {
            private const ulong Offset = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;
            private ulong hash;
            private bool initialized;

            internal void Append(string value)
            {
                EnsureInitialized();
                string safe = value ?? string.Empty;
                AddInt32(safe.Length);
                for (int index = 0; index < safe.Length; index++)
                {
                    char character = safe[index];
                    AddByte((byte)character);
                    AddByte((byte)(character >> 8));
                }
            }

            internal void Append(int value) =>
                Append(value.ToString(CultureInfo.InvariantCulture));

            internal string Complete()
            {
                EnsureInitialized();
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }

            private void AddInt32(int value)
            {
                AddByte((byte)value);
                AddByte((byte)(value >> 8));
                AddByte((byte)(value >> 16));
                AddByte((byte)(value >> 24));
            }

            private void AddByte(byte value)
            {
                hash ^= value;
                hash *= Prime;
            }

            private void EnsureInitialized()
            {
                if (initialized) return;
                hash = Offset;
                initialized = true;
            }
        }
    }
}
