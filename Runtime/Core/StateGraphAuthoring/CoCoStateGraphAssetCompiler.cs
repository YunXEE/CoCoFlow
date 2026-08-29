using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public sealed class CoCoStateGraphAssetCompileResult
    {
        private readonly IReadOnlyList<CoCoGraphDiagnostic> diagnostics;

        internal CoCoStateGraphAssetCompileResult(
            CoCoCompiledStateGraph graph,
            ulong contentFingerprint,
            IReadOnlyList<CoCoGraphDiagnostic> preflightDiagnostics,
            IReadOnlyList<CoCoGraphDiagnostic> adapterDiagnostics,
            bool freezingSkippedGlobally,
            CoCoStateGraphCompileResult compilerResult)
        {
            if (preflightDiagnostics == null)
            {
                throw new ArgumentNullException(nameof(preflightDiagnostics));
            }

            if (adapterDiagnostics == null)
            {
                throw new ArgumentNullException(nameof(adapterDiagnostics));
            }

            if (compilerResult == null)
            {
                throw new ArgumentNullException(nameof(compilerResult));
            }

            var merged = new List<CoCoGraphDiagnostic>(
                preflightDiagnostics.Count +
                compilerResult.Diagnostics.Count +
                adapterDiagnostics.Count);
            for (int index = 0; index < preflightDiagnostics.Count; index++)
            {
                merged.Add(preflightDiagnostics[index]);
            }

            for (int index = 0; index < compilerResult.Diagnostics.Count; index++)
            {
                CoCoGraphDiagnostic diagnostic = compilerResult.Diagnostics[index];
                if (!IsDuplicateOfPreflight(diagnostic, preflightDiagnostics) &&
                    !IsGlobalFreezeSkipConfigNoise(diagnostic, freezingSkippedGlobally) &&
                    !IsGenericConfigMismatchWithPreciseAdapterError(
                        diagnostic,
                        preflightDiagnostics,
                        adapterDiagnostics))
                {
                    merged.Add(diagnostic);
                }
            }

            for (int index = 0; index < adapterDiagnostics.Count; index++)
            {
                merged.Add(adapterDiagnostics[index]);
            }

            diagnostics = merged.AsReadOnly();
            ContentFingerprint = contentFingerprint;
            Graph = ContainsError(preflightDiagnostics) || ContainsError(adapterDiagnostics)
                ? null
                : graph;
        }

        public CoCoCompiledStateGraph Graph { get; }
        public ulong ContentFingerprint { get; }
        public IReadOnlyList<CoCoGraphDiagnostic> Diagnostics => diagnostics;
        public bool Succeeded => Graph != null && !HasErrors;

        public bool HasErrors => ContainsError(diagnostics);

        private static bool ContainsError(IReadOnlyList<CoCoGraphDiagnostic> source)
        {
            for (int index = 0; index < source.Count; index++)
            {
                if (source[index].IsError)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDuplicateOfPreflight(
            CoCoGraphDiagnostic diagnostic,
            IReadOnlyList<CoCoGraphDiagnostic> preflightDiagnostics)
        {
            for (int index = 0; index < preflightDiagnostics.Count; index++)
            {
                CoCoGraphDiagnostic preflight = preflightDiagnostics[index];
                if (preflight.Diagnostic.Code == diagnostic.Diagnostic.Code &&
                    preflight.Location == diagnostic.Location)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGenericConfigMismatchWithPreciseAdapterError(
            CoCoGraphDiagnostic diagnostic,
            IReadOnlyList<CoCoGraphDiagnostic> preflightDiagnostics,
            IReadOnlyList<CoCoGraphDiagnostic> adapterDiagnostics)
        {
            if (diagnostic.Diagnostic.Code != CoCoDiagnosticCode.DescriptorTypeMismatch ||
                diagnostic.Location.Field != CoCoGraphField.Config)
            {
                return false;
            }

            if (ContainsLocatedError(preflightDiagnostics, diagnostic.Location))
            {
                return true;
            }

            return ContainsLocatedError(adapterDiagnostics, diagnostic.Location);
        }

        private static bool IsGlobalFreezeSkipConfigNoise(
            CoCoGraphDiagnostic diagnostic,
            bool freezingSkippedGlobally) =>
            freezingSkippedGlobally &&
            diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DescriptorTypeMismatch &&
            diagnostic.Location.Field == CoCoGraphField.Config;

        private static bool ContainsLocatedError(
            IReadOnlyList<CoCoGraphDiagnostic> source,
            CoCoGraphDiagnosticLocation location)
        {
            for (int index = 0; index < source.Count; index++)
            {
                if (source[index].IsError && source[index].Location == location)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class CoCoStateGraphAssetCompiler
    {
        private readonly ICoCoStateGraphCompilationCache cache;

        public CoCoStateGraphAssetCompiler()
            : this(CoCoStateGraphCompilationCache.Shared)
        {
        }

        public CoCoStateGraphAssetCompiler(ICoCoStateGraphCompilationCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Freezes Unity-serialized authoring data and compiles it through the pure StateGraph compiler.
        /// The call must originate on Unity's main thread because it reads a ScriptableObject graph.
        /// </summary>
        public CoCoStateGraphAssetCompileResult Compile(
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog)
        {
            if (ReferenceEquals(asset, null))
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            CoCoStateGraphMainThreadGuard.ThrowIfNotMainThread();
            if (asset == null)
            {
                throw new ArgumentException(
                    "The StateGraph Asset has been destroyed.",
                    nameof(asset));
            }

            CoCoStateGraphManagedReferenceInspection managedReferenceInspection =
                CoCoStateGraphManagedReferenceInspectionBridge.Inspect(asset);
            CoCoStateGraphAssetSnapshot snapshot =
                CoCoStateGraphAssetSnapshotBuilder.Build(
                    asset,
                    catalog,
                    managedReferenceInspection);
            var key = new CoCoStateGraphCompilationCacheKey(
                snapshot.Source.GraphId,
                snapshot.CacheFingerprint,
                catalog.Fingerprint,
                CoCoStateGraphCompiler.CurrentSchemaVersion);
            return cache.GetOrAdd(key, () => CompileSnapshot(snapshot, catalog));
        }

        private static CoCoStateGraphAssetCompileResult CompileSnapshot(
            CoCoStateGraphAssetSnapshot snapshot,
            CoCoGraphDescriptorCatalog catalog)
        {
            CoCoStateGraphCompileResult compilerResult =
                new CoCoStateGraphCompiler().Compile(snapshot.Source, catalog);
            return new CoCoStateGraphAssetCompileResult(
                compilerResult.Graph,
                snapshot.ContentFingerprint,
                snapshot.PreflightDiagnostics,
                snapshot.AdapterDiagnostics,
                snapshot.FreezingSkippedGlobally,
                compilerResult);
        }
    }
}
