using System;
using System.Collections.Generic;
using System.Globalization;

namespace CoCoFlow.Runtime.Modules.Map
{
    internal static class RegionDependencyCompiler
    {
        internal static bool TryCompile(
            CoCoRegionBinding binding,
            IReadOnlyList<RegionCompiledTier> sourceTiers,
            IList<RegionCompileDiagnostic> diagnostics,
            out List<RegionCompiledDependencyRule> compiled)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (sourceTiers == null)
            {
                throw new ArgumentNullException(nameof(sourceTiers));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            compiled = new List<RegionCompiledDependencyRule>(
                binding.DependencyRules.Count);
            int initialErrorCount = CountErrors(diagnostics);
            RegionCapabilitySet sourceSupported =
                sourceTiers.Count == 0
                    ? RegionCapabilitySet.Empty
                    : sourceTiers[sourceTiers.Count - 1].Capabilities;
            var fingerprints = new HashSet<string>(
                StringComparer.Ordinal);

            for (int index = 0;
                 index < binding.DependencyRules.Count;
                 index++)
            {
                RegionDependencyRule rule =
                    binding.DependencyRules[index];
                string path =
                    "binding.dependencyRules[" + index + "]";
                if (rule == null)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Cross-Region dependency rules cannot be null."));
                    continue;
                }

                bool valid = true;
                if (!rule.SourceCapability.IsValid)
                {
                    AddError(
                        diagnostics,
                        path + ".sourceCapability",
                        RegionErrors.InvalidCapability(
                            "A dependency rule requires one valid source capability."));
                    valid = false;
                }
                else if (!sourceSupported.Contains(
                             rule.SourceCapability))
                {
                    AddError(
                        diagnostics,
                        path + ".sourceCapability",
                        RegionErrors.InvalidProfile(
                            "The source Profile cannot resolve dependency trigger capability '" +
                            rule.SourceCapability.Value + "'."));
                    valid = false;
                }

                if (!rule.TargetRegionId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path + ".targetRegionId",
                        RegionErrors.InvalidIdentifier(
                            "A dependency rule requires one valid target RegionId."));
                    valid = false;
                }
                else if (rule.TargetRegionId == binding.RegionId)
                {
                    AddError(
                        diagnostics,
                        path + ".targetRegionId",
                        RegionErrors.InvalidProfile(
                            "A Region cannot declare a dependency on itself."));
                    valid = false;
                }

                if (HasDuplicateOrInvalidCapabilities(
                        rule.TargetCapabilities) ||
                    !RegionCapabilitySet.TryCreate(
                        rule.TargetCapabilities,
                        out RegionCapabilitySet targetCapabilities) ||
                    targetCapabilities.Count == 0)
                {
                    AddError(
                        diagnostics,
                        path + ".targetCapabilities",
                        RegionErrors.InvalidCapability(
                            "Target capabilities must be a non-empty set of valid unique identifiers."));
                    valid = false;
                    targetCapabilities = RegionCapabilitySet.Empty;
                }

                if (!rule.TryGetTargetCoverage(
                        out RegionCoverage targetCoverage))
                {
                    AddError(
                        diagnostics,
                        path + ".targetCoverage",
                        RegionErrors.InvalidCoverage(
                            "Target Coverage must be All or a non-empty set of unique ChunkIds."));
                    valid = false;
                    targetCoverage = default;
                }

                if (!valid) continue;

                string fingerprint = BuildFingerprint(
                    rule.SourceCapability,
                    rule.TargetRegionId,
                    targetCapabilities,
                    targetCoverage);
                if (!fingerprints.Add(fingerprint))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "This normalized cross-Region dependency rule is duplicated."));
                    continue;
                }

                compiled.Add(
                    new RegionCompiledDependencyRule(
                        rule.SourceCapability,
                        rule.TargetRegionId,
                        targetCapabilities,
                        targetCoverage,
                        fingerprint));
            }

            compiled.Sort(
                (left, right) => string.CompareOrdinal(
                    left.Fingerprint,
                    right.Fingerprint));
            return CountErrors(diagnostics) == initialErrorCount;
        }

        internal static string BuildFingerprint(
            RegionCapabilityId sourceCapability,
            RegionId targetRegionId,
            RegionCapabilitySet targetCapabilities,
            RegionCoverage targetCoverage)
        {
            var builder = new FingerprintBuilder();
            builder.Append("region-dependency-v1");
            builder.Append(sourceCapability.Value);
            builder.Append(targetRegionId.Value);
            builder.Append(
                targetCapabilities == null
                    ? -1
                    : targetCapabilities.Count);
            if (targetCapabilities != null)
            {
                for (int index = 0;
                     index < targetCapabilities.Count;
                     index++)
                {
                    builder.Append(
                        targetCapabilities.Capabilities[index].Value);
                }
            }

            builder.Append((int)targetCoverage.Kind);
            builder.Append(targetCoverage.Chunks.Count);
            for (int index = 0;
                 index < targetCoverage.Chunks.Count;
                 index++)
            {
                builder.Append(
                    targetCoverage.Chunks[index].Value);
            }

            return builder.Complete();
        }

        internal static void ValidateGlobal(
            IReadOnlyList<RegionCompileResult> results,
            IDictionary<int, List<RegionCompileDiagnostic>>
                diagnosticsByResult)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (diagnosticsByResult == null)
            {
                throw new ArgumentNullException(
                    nameof(diagnosticsByResult));
            }

            var ownersByRegion =
                new Dictionary<RegionId, List<int>>();
            for (int index = 0; index < results.Count; index++)
            {
                RegionCompileResult result = results[index];
                if (!result.Succeeded) continue;

                if (!ownersByRegion.TryGetValue(
                        result.Plan.RegionId,
                        out List<int> owners))
                {
                    owners = new List<int>();
                    ownersByRegion.Add(
                        result.Plan.RegionId,
                        owners);
                }

                owners.Add(index);
            }

            var validTargets =
                new Dictionary<RegionId, RegionCompiledPlan>();
            var resultIndexByRegion =
                new Dictionary<RegionId, int>();
            foreach (
                KeyValuePair<RegionId, List<int>> pair
                in ownersByRegion)
            {
                if (pair.Value.Count != 1) continue;

                int resultIndex = pair.Value[0];
                validTargets.Add(
                    pair.Key,
                    results[resultIndex].Plan);
                resultIndexByRegion.Add(
                    pair.Key,
                    resultIndex);
            }

            var graph =
                new Dictionary<RegionId, List<RegionId>>();
            foreach (RegionId regionId in validTargets.Keys)
            {
                graph.Add(regionId, new List<RegionId>());
            }

            for (int resultIndex = 0;
                 resultIndex < results.Count;
                 resultIndex++)
            {
                RegionCompileResult result = results[resultIndex];
                if (!result.Succeeded) continue;

                RegionCompiledPlan source = result.Plan;
                for (int ruleIndex = 0;
                     ruleIndex < source.DependencyRules.Count;
                     ruleIndex++)
                {
                    RegionCompiledDependencyRule rule =
                        source.DependencyRules[ruleIndex];
                    string path =
                        "binding.dependencyRules." +
                        rule.Fingerprint;
                    if (!validTargets.TryGetValue(
                            rule.TargetRegionId,
                            out RegionCompiledPlan target))
                    {
                        AddGlobalError(
                            diagnosticsByResult,
                            resultIndex,
                            path + ".targetRegionId",
                            RegionErrors.InvalidProfile(
                                "Target Region '" +
                                rule.TargetRegionId.Value +
                                "' does not resolve to exactly one successfully compiled bootstrap Binding."));
                        continue;
                    }

                    bool edgeValid = true;
                    if (!target.TryResolveTier(
                            rule.TargetCapabilities,
                            out _))
                    {
                        AddGlobalError(
                            diagnosticsByResult,
                            resultIndex,
                            path + ".targetCapabilities",
                            RegionErrors.InvalidCapability(
                                "Target Region '" +
                                rule.TargetRegionId.Value +
                                "' cannot resolve the requested dependency capabilities."));
                        edgeValid = false;
                    }

                    if (!rule.TargetCoverage.CoversAll)
                    {
                        for (int chunkIndex = 0;
                             chunkIndex <
                             rule.TargetCoverage.Chunks.Count;
                             chunkIndex++)
                        {
                            RegionChunkId chunkId =
                                rule.TargetCoverage.Chunks[chunkIndex];
                            if (target.TryGetChunk(
                                    chunkId,
                                    out _))
                            {
                                continue;
                            }

                            AddGlobalError(
                                diagnosticsByResult,
                                resultIndex,
                                path + ".targetCoverage",
                                RegionErrors.InvalidCoverage(
                                    "Target Chunk '" +
                                    chunkId.Value +
                                    "' is not owned by target Region '" +
                                    rule.TargetRegionId.Value +
                                    "'."));
                            edgeValid = false;
                        }
                    }

                    if (edgeValid &&
                        graph.TryGetValue(
                            source.RegionId,
                            out List<RegionId> edges))
                    {
                        edges.Add(rule.TargetRegionId);
                    }
                }
            }

            foreach (List<RegionId> edges in graph.Values)
            {
                edges.Sort(
                    (left, right) => string.CompareOrdinal(
                        left.Value,
                        right.Value));
            }

            var visitState = new Dictionary<RegionId, byte>();
            var stack = new List<RegionId>();
            var cycleRegions = new HashSet<RegionId>();
            var orderedRegions = new List<RegionId>(graph.Keys);
            orderedRegions.Sort(
                (left, right) => string.CompareOrdinal(
                    left.Value,
                    right.Value));
            for (int index = 0;
                 index < orderedRegions.Count;
                 index++)
            {
                CollectCycles(
                    orderedRegions[index],
                    graph,
                    visitState,
                    stack,
                    cycleRegions);
            }

            foreach (RegionId regionId in cycleRegions)
            {
                if (!resultIndexByRegion.TryGetValue(
                        regionId,
                        out int resultIndex))
                {
                    continue;
                }

                AddGlobalError(
                    diagnosticsByResult,
                    resultIndex,
                    "binding.dependencyRules",
                    RegionErrors.InvalidProfile(
                        "Cross-Region dependency rules must form one global directed acyclic graph; Region '" +
                        regionId.Value +
                        "' participates in a cycle."));
            }

            PropagateInvalidTargets(
                results,
                resultIndexByRegion,
                diagnosticsByResult);
        }

        private static void PropagateInvalidTargets(
            IReadOnlyList<RegionCompileResult> results,
            IReadOnlyDictionary<RegionId, int> resultIndexByRegion,
            IDictionary<int, List<RegionCompileDiagnostic>>
                diagnosticsByResult)
        {
            var invalid = new HashSet<int>(
                diagnosticsByResult.Keys);
            bool changed;
            do
            {
                changed = false;
                for (int resultIndex = 0;
                     resultIndex < results.Count;
                     resultIndex++)
                {
                    RegionCompileResult result = results[resultIndex];
                    if (!result.Succeeded ||
                        invalid.Contains(resultIndex))
                    {
                        continue;
                    }

                    for (int ruleIndex = 0;
                         ruleIndex <
                         result.Plan.DependencyRules.Count;
                         ruleIndex++)
                    {
                        RegionCompiledDependencyRule rule =
                            result.Plan.DependencyRules[ruleIndex];
                        if (!resultIndexByRegion.TryGetValue(
                                rule.TargetRegionId,
                                out int targetIndex) ||
                            !invalid.Contains(targetIndex))
                        {
                            continue;
                        }

                        AddGlobalError(
                            diagnosticsByResult,
                            resultIndex,
                            "binding.dependencyRules." +
                            rule.Fingerprint +
                            ".targetRegionId",
                            RegionErrors.InvalidProfile(
                                "Target Region '" +
                                rule.TargetRegionId.Value +
                                "' failed global dependency validation, so this source cannot remain independently compiled."));
                        invalid.Add(resultIndex);
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);
        }

        private static bool HasDuplicateOrInvalidCapabilities(
            IReadOnlyList<RegionCapabilityId> capabilities)
        {
            if (capabilities == null) return true;

            var unique = new HashSet<RegionCapabilityId>();
            for (int index = 0; index < capabilities.Count; index++)
            {
                RegionCapabilityId capability = capabilities[index];
                if (!capability.IsValid ||
                    !unique.Add(capability))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectCycles(
            RegionId regionId,
            IReadOnlyDictionary<RegionId, List<RegionId>> graph,
            IDictionary<RegionId, byte> visitState,
            IList<RegionId> stack,
            ISet<RegionId> cycleRegions)
        {
            if (visitState.TryGetValue(
                    regionId,
                    out byte state))
            {
                if (state != 1) return;

                int start = -1;
                for (int index = 0; index < stack.Count; index++)
                {
                    if (stack[index] == regionId)
                    {
                        start = index;
                        break;
                    }
                }

                for (int index = Math.Max(0, start);
                     index < stack.Count;
                     index++)
                {
                    cycleRegions.Add(stack[index]);
                }

                cycleRegions.Add(regionId);
                return;
            }

            visitState[regionId] = 1;
            stack.Add(regionId);
            if (graph.TryGetValue(
                    regionId,
                    out List<RegionId> targets))
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    CollectCycles(
                        targets[index],
                        graph,
                        visitState,
                        stack,
                        cycleRegions);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visitState[regionId] = 2;
        }

        private static void AddGlobalError(
            IDictionary<int, List<RegionCompileDiagnostic>>
                diagnosticsByResult,
            int resultIndex,
            string path,
            CoCoFlow.Runtime.Core.CoCoDiagnostic diagnostic)
        {
            if (!diagnosticsByResult.TryGetValue(
                    resultIndex,
                    out List<RegionCompileDiagnostic> diagnostics))
            {
                diagnostics =
                    new List<RegionCompileDiagnostic>();
                diagnosticsByResult.Add(
                    resultIndex,
                    diagnostics);
            }

            diagnostics.Add(
                new RegionCompileDiagnostic(
                    path,
                    diagnostic));
        }

        private static int CountErrors(
            IList<RegionCompileDiagnostic> diagnostics)
        {
            int count = 0;
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Diagnostic.IsError) count++;
            }

            return count;
        }

        private static void AddError(
            IList<RegionCompileDiagnostic> diagnostics,
            string path,
            CoCoFlow.Runtime.Core.CoCoDiagnostic diagnostic) =>
            diagnostics.Add(
                new RegionCompileDiagnostic(
                    path,
                    diagnostic));

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
                Append(
                    value.ToString(
                        CultureInfo.InvariantCulture));

            internal string Complete()
            {
                EnsureInitialized();
                return hash.ToString(
                    "x16",
                    CultureInfo.InvariantCulture);
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
