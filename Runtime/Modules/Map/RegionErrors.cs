using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    internal static class RegionErrors
    {
        internal static CoCoDiagnostic InvalidIdentifier(string message) =>
            Error(CoCoDiagnosticCode.InvalidRegionIdentifier, message);

        internal static CoCoDiagnostic InvalidCapability(string message) =>
            Error(CoCoDiagnosticCode.InvalidRegionCapability, message);

        internal static CoCoDiagnostic UnsupportedCapability(
            RegionCapabilityId capability) =>
            Error(
                CoCoDiagnosticCode.UnsupportedRegionCapability,
                "Region capability '" + capability.Value +
                "' is not registered by the active catalog.");

        internal static CoCoDiagnostic InvalidCoverage(string message) =>
            Error(CoCoDiagnosticCode.InvalidRegionCoverage, message);

        internal static CoCoDiagnostic InvalidProfile(string message) =>
            Error(CoCoDiagnosticCode.InvalidRegionProfile, message);

        internal static CoCoDiagnostic CompilationFailed(string message) =>
            Error(CoCoDiagnosticCode.RegionCompilationFailed, message);

        internal static CoCoDiagnostic CatalogConflict(string message) =>
            Error(CoCoDiagnosticCode.RegionCatalogConflict, message);

        internal static CoCoDiagnostic RuntimeDisposed() =>
            Error(
                CoCoDiagnosticCode.RegionRuntimeDisposed,
                "The Map Region runtime is disposed.");

        internal static CoCoDiagnostic MainThreadRequired() =>
            Error(
                CoCoDiagnosticCode.RegionMainThreadRequired,
                "The Map Region operation must run on the Unity main thread.");

        internal static CoCoDiagnostic DemandConflict(string message) =>
            Error(CoCoDiagnosticCode.RegionDemandConflict, message);

        internal static CoCoDiagnostic DemandSuperseded(
            RegionDemandRevision revision) =>
            Error(
                CoCoDiagnosticCode.RegionDemandSuperseded,
                "Region demand revision " + revision.Value + " was superseded.");

        internal static CoCoDiagnostic TransitionFailed(string message) =>
            Error(CoCoDiagnosticCode.RegionTransitionFailed, message);

        internal static CoCoDiagnostic CommitFaulted(string message) =>
            Error(CoCoDiagnosticCode.RegionCommitFaulted, message);

        internal static CoCoDiagnostic CleanupBlocked(string message) =>
            Error(CoCoDiagnosticCode.RegionCleanupBlocked, message);

        internal static CoCoDiagnostic OptionalDegraded(string message) =>
            CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Map,
                CoCoDiagnosticCode.RegionOptionalDegraded,
                message);

        internal static CoCoDiagnostic SceneContract(string message) =>
            Error(CoCoDiagnosticCode.RegionSceneContractViolation, message);

        internal static CoCoDiagnostic TemporalConflict(string message) =>
            Error(CoCoDiagnosticCode.RegionTemporalConflict, message);

        internal static CoCoDiagnostic TemporalProjection(string message) =>
            Error(CoCoDiagnosticCode.RegionTemporalProjectionFailed, message);

        internal static CoCoDiagnostic TemporalCleanup(string message) =>
            Error(CoCoDiagnosticCode.RegionTemporalCleanupFailed, message);

        private static CoCoDiagnostic Error(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Map,
                code,
                message);
    }
}
