using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    public readonly struct RegionCompileDiagnostic
    {
        public RegionCompileDiagnostic(
            string path,
            CoCoDiagnostic diagnostic)
        {
            Path = path ?? string.Empty;
            Diagnostic = diagnostic;
        }

        public string Path { get; }
        public CoCoDiagnostic Diagnostic { get; }
    }

    public sealed class RegionCompileResult
    {
        internal RegionCompileResult(
            RegionCompiledPlan plan,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            Plan = plan;
            Diagnostics = new ReadOnlyCollection<RegionCompileDiagnostic>(
                diagnostics == null
                    ? Array.Empty<RegionCompileDiagnostic>()
                    : new List<RegionCompileDiagnostic>(diagnostics));
        }

        public RegionCompiledPlan Plan { get; }
        public IReadOnlyList<RegionCompileDiagnostic> Diagnostics { get; }
        public bool Succeeded => Plan != null && !HasErrors(Diagnostics);

        private static bool HasErrors(
            IReadOnlyList<RegionCompileDiagnostic> diagnostics)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Diagnostic.IsError) return true;
            }

            return false;
        }
    }
}
