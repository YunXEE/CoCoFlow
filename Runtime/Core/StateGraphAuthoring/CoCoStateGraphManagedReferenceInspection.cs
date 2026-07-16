using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    internal sealed class CoCoStateGraphManagedReferenceInspection
    {
        internal static readonly CoCoStateGraphManagedReferenceInspection Empty =
            new CoCoStateGraphManagedReferenceInspection(
                0UL,
                Array.Empty<CoCoGraphDiagnostic>());

        private readonly IReadOnlyList<CoCoGraphDiagnostic> diagnostics;

        internal CoCoStateGraphManagedReferenceInspection(
            ulong fingerprint,
            IReadOnlyList<CoCoGraphDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var copy = new CoCoGraphDiagnostic[diagnostics.Count];
            for (int index = 0; index < diagnostics.Count; index++)
            {
                copy[index] = diagnostics[index];
            }

            Fingerprint = fingerprint;
            this.diagnostics = Array.AsReadOnly(copy);
        }

        internal ulong Fingerprint { get; }
        internal IReadOnlyList<CoCoGraphDiagnostic> Diagnostics => diagnostics;

        internal bool ContainsLocation(CoCoGraphDiagnosticLocation location)
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

    internal static class CoCoStateGraphManagedReferenceInspectionBridge
    {
        private static Func<CoCoStateGraphAsset, CoCoStateGraphManagedReferenceInspection> inspector;

        internal static void Install(
            Func<CoCoStateGraphAsset, CoCoStateGraphManagedReferenceInspection> value)
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

        internal static CoCoStateGraphManagedReferenceInspection Inspect(CoCoStateGraphAsset asset)
        {
            return inspector?.Invoke(asset) ?? CoCoStateGraphManagedReferenceInspection.Empty;
        }
    }
}
