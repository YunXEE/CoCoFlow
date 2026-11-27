using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 图资产编译和缓存共用这个结果，保留准确的资产诊断及纯图指纹。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>汇总资产预检、快照适配及图编译产生的诊断和只读图。</summary>
    internal sealed class GraphAssetCompileResult
    {
        private readonly IReadOnlyList<GraphDiagnostic> diagnostics;

        internal GraphAssetCompileResult(
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

        internal CompiledStateGraph Graph { get; }
        internal ulong ContentFingerprint { get; }
        internal IReadOnlyList<GraphDiagnostic> Diagnostics => diagnostics;
        internal bool Succeeded => Graph != null && !HasErrors;

        internal bool HasErrors => ContainsError(diagnostics);

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
}
