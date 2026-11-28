using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存一次托管引用检查的指纹、位置与诊断。</summary>
    internal sealed class GraphAssetReferenceInspection
    {
        internal static readonly GraphAssetReferenceInspection Empty =
            new GraphAssetReferenceInspection(
                0UL,
                Array.Empty<GraphDiagnostic>());

        private readonly IReadOnlyList<GraphDiagnostic> diagnostics;

        internal GraphAssetReferenceInspection(
            ulong fingerprint,
            IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var copy = new GraphDiagnostic[diagnostics.Count];
            for (int index = 0; index < diagnostics.Count; index++)
            {
                copy[index] = diagnostics[index];
            }

            Fingerprint = fingerprint;
            this.diagnostics = Array.AsReadOnly(copy);
        }

        internal ulong Fingerprint { get; }
        internal IReadOnlyList<GraphDiagnostic> Diagnostics => diagnostics;

        internal bool ContainsLocation(GraphDiagnosticLocation location)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Location == location)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>向资产编译器提供编辑器安装的托管引用检查。</summary>
    internal static class GraphAssetReferenceInspectionBridge
    {
        private static Func<StateGraphAsset, GraphAssetReferenceInspection> inspector;

        internal static void Install(
            Func<StateGraphAsset, GraphAssetReferenceInspection> value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (inspector != null && inspector != value)
            {
                throw new InvalidOperationException(
                    "The StateGraph managed-reference inspector is already installed for this domain.");
            }

            inspector = value;
        }

        internal static GraphAssetReferenceInspection Inspect(StateGraphAsset asset)
        {
            return inspector?.Invoke(asset) ?? GraphAssetReferenceInspection.Empty;
        }
    }
}
