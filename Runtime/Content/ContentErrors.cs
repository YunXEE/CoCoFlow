using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Content
{
    internal static class ContentErrors
    {
        internal static CoCoDiagnostic InvalidReference(string message) =>
            Error(CoCoDiagnosticCode.InvalidContentReference, message);

        internal static CoCoDiagnostic MissingBackend(ContentReference reference) =>
            Error(
                CoCoDiagnosticCode.MissingContentBackend,
                "No registered Content backend can handle '" + reference.Id + "'.");

        internal static CoCoDiagnostic BackendConflict(ContentReference reference) =>
            Error(
                CoCoDiagnosticCode.ContentBackendConflict,
                "More than one registered Content backend can handle '" + reference.Id + "'.");

        internal static CoCoDiagnostic TypeMismatch(ContentId id, System.Type expectedType) =>
            Error(
                CoCoDiagnosticCode.ContentTypeMismatch,
                "Content '" + id + "' is not assignable to '" + expectedType.FullName + "'.");

        internal static CoCoDiagnostic LoadFailed(ContentId id, string reason) =>
            Error(
                CoCoDiagnosticCode.ContentLoadFailed,
                "Content '" + id + "' failed to load. " + (reason ?? string.Empty));

        internal static CoCoDiagnostic Cancelled(ContentId id) =>
            CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.Content,
                CoCoDiagnosticCode.ContentRequestCancelled,
                "Content request for '" + id + "' was cancelled.");

        internal static CoCoDiagnostic ScopeDisposed(ContentOwnerId ownerId) =>
            CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.Content,
                CoCoDiagnosticCode.ContentScopeDisposed,
                "Content Scope for owner '" + ownerId + "' is disposed.");

        internal static CoCoDiagnostic RuntimeDisposed() =>
            Error(
                CoCoDiagnosticCode.ContentRuntimeDisposed,
                "The Content Runtime is shutting down or disposed.");

        internal static CoCoDiagnostic ReferenceConflict(ContentId id) =>
            Error(
                CoCoDiagnosticCode.ContentReferenceConflict,
                "Content ID '" + id + "' was requested with a conflicting reference.");

        internal static CoCoDiagnostic MainThreadRequired() =>
            Error(
                CoCoDiagnosticCode.ContentMainThreadRequired,
                "Content registry operations must start on the Unity main thread.");

        internal static CoCoDiagnostic ReleaseFailed(ContentId id, string reason) =>
            Error(
                CoCoDiagnosticCode.ContentReleaseFailed,
                "Content '" + id + "' failed to release. " + (reason ?? string.Empty));

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Content, code, message);
    }
}
