using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraphHost
{
    public sealed class StateGraphAssetCompileResult
    {
        private readonly IReadOnlyList<GraphDiagnostic> diagnostics;

        internal StateGraphAssetCompileResult(
            CompiledStateGraph graph,
            ulong contentFingerprint,
            IReadOnlyList<GraphDiagnostic> preflightDiagnostics,
            IReadOnlyList<GraphDiagnostic> adapterDiagnostics,
            bool freezingSkippedGlobally,
            StateGraphCompileResult compilerResult)
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

            var merged = new List<GraphDiagnostic>(
                preflightDiagnostics.Count +
                compilerResult.Diagnostics.Count +
                adapterDiagnostics.Count);
            for (int index = 0; index < preflightDiagnostics.Count; index++)
            {
                merged.Add(preflightDiagnostics[index]);
            }

            for (int index = 0; index < compilerResult.Diagnostics.Count; index++)
            {
                GraphDiagnostic diagnostic = compilerResult.Diagnostics[index];
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

        public CompiledStateGraph Graph { get; }
        public ulong ContentFingerprint { get; }
        public IReadOnlyList<GraphDiagnostic> Diagnostics => diagnostics;
        public bool Succeeded => Graph != null && !HasErrors;

        public bool HasErrors => ContainsError(diagnostics);

        private static bool ContainsError(IReadOnlyList<GraphDiagnostic> source)
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
            GraphDiagnostic diagnostic,
            IReadOnlyList<GraphDiagnostic> preflightDiagnostics)
        {
            for (int index = 0; index < preflightDiagnostics.Count; index++)
            {
                GraphDiagnostic preflight = preflightDiagnostics[index];
                if (preflight.Diagnostic.Code == diagnostic.Diagnostic.Code &&
                    preflight.Location == diagnostic.Location)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGenericConfigMismatchWithPreciseAdapterError(
            GraphDiagnostic diagnostic,
            IReadOnlyList<GraphDiagnostic> preflightDiagnostics,
            IReadOnlyList<GraphDiagnostic> adapterDiagnostics)
        {
            if (diagnostic.Diagnostic.Code != CoCoDiagnosticCode.DescriptorTypeMismatch ||
                diagnostic.Location.Field != GraphField.Config)
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
            GraphDiagnostic diagnostic,
            bool freezingSkippedGlobally) =>
            freezingSkippedGlobally &&
            diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DescriptorTypeMismatch &&
            diagnostic.Location.Field == GraphField.Config;

        private static bool ContainsLocatedError(
            IReadOnlyList<GraphDiagnostic> source,
            GraphDiagnosticLocation location)
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

    public sealed class StateGraphAssetCompiler
    {
        private readonly IStateGraphCompilationCache cache;

        public StateGraphAssetCompiler()
            : this(StateGraphCompilationCache.Shared)
        {
        }

        internal StateGraphAssetCompiler(IStateGraphCompilationCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Freezes Unity-serialized authoring data and compiles it through the pure StateGraph compiler.
        /// The call must originate on Unity's main thread because it reads a ScriptableObject graph.
        /// </summary>
        public StateGraphAssetCompileResult Compile(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog)
        {
            if (ReferenceEquals(asset, null))
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            StateGraphMainThreadGuard.ThrowIfNotMainThread();
            if (asset == null)
            {
                throw new ArgumentException(
                    "The StateGraph Asset has been destroyed.",
                    nameof(asset));
            }

            StateGraphManagedReferenceInspection managedReferenceInspection =
                StateGraphManagedReferenceInspectionBridge.Inspect(asset);
            StateGraphAssetSnapshot snapshot =
                StateGraphAssetSnapshotBuilder.Build(
                    asset,
                    catalog,
                    managedReferenceInspection);
            var key = new StateGraphCompilationCacheKey(
                snapshot.Source.GraphId,
                snapshot.CacheFingerprint,
                catalog.Fingerprint,
                StateGraphCompiler.CurrentSchemaVersion);
            return cache.GetOrAdd(key, () => CompileSnapshot(snapshot, catalog));
        }

        private static StateGraphAssetCompileResult CompileSnapshot(
            StateGraphAssetSnapshot snapshot,
            GraphDescriptorCatalog catalog)
        {
            StateGraphCompileResult compilerResult =
                new StateGraphCompiler().Compile(snapshot.Source, catalog);
            return new StateGraphAssetCompileResult(
                compilerResult.Graph,
                snapshot.ContentFingerprint,
                snapshot.PreflightDiagnostics,
                snapshot.AdapterDiagnostics,
                snapshot.FreezingSkippedGlobally,
                compilerResult);
        }
    }
}
