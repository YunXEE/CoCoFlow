using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal sealed class StateGraphManagedReferenceInspection
    {
        internal static readonly StateGraphManagedReferenceInspection Empty =
            new StateGraphManagedReferenceInspection(
                0UL,
                Array.Empty<GraphDiagnostic>());

        private readonly IReadOnlyList<GraphDiagnostic> diagnostics;

        internal StateGraphManagedReferenceInspection(
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

    internal static class StateGraphManagedReferenceInspectionBridge
    {
        private static Func<StateGraphAsset, StateGraphManagedReferenceInspection> inspector;

        internal static void Install(
            Func<StateGraphAsset, StateGraphManagedReferenceInspection> value)
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

        internal static StateGraphManagedReferenceInspection Inspect(StateGraphAsset asset)
        {
            return inspector?.Invoke(asset) ?? StateGraphManagedReferenceInspection.Empty;
        }
    }
}
