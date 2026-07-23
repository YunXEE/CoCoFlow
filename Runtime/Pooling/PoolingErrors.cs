using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Pooling
{
    internal static class PoolingErrors
    {
        internal static CoCoDiagnostic InvalidId() =>
            Error(CoCoDiagnosticCode.InvalidPoolId, "A valid PoolId is required.");

        internal static CoCoDiagnostic InvalidProfile(string reason) =>
            Error(
                CoCoDiagnosticCode.InvalidPoolProfile,
                "The PoolProfile is invalid. " + (reason ?? string.Empty));

        internal static CoCoDiagnostic ProfileConflict(PoolId poolId) =>
            Error(
                CoCoDiagnosticCode.PoolProfileConflict,
                "Pool '" + poolId + "' was prepared with a conflicting profile.");

        internal static CoCoDiagnostic RuntimeDisposed() =>
            Error(
                CoCoDiagnosticCode.PoolRuntimeDisposed,
                "The Pool Runtime is shutting down or disposed.");

        internal static CoCoDiagnostic ScopeClosing(PoolId poolId = default) =>
            CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolScopeClosing,
                poolId.IsValid
                    ? "The Pool Scope is closing; pool '" + poolId + "' rejects this operation."
                    : "The Pool Scope is closing and rejects this operation.");

        internal static CoCoDiagnostic NotReady(PoolId poolId) =>
            Error(
                CoCoDiagnosticCode.PoolNotReady,
                "Pool '" + poolId + "' is not ready.");

        internal static CoCoDiagnostic OperationInProgress(PoolId poolId) =>
            Error(
                CoCoDiagnosticCode.PoolOperationInProgress,
                "Pool '" + poolId + "' already has an incompatible operation in progress.");

        internal static CoCoDiagnostic Cancelled(PoolId poolId) =>
            CoCoDiagnostic.Info(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolOperationCancelled,
                "Pool operation for '" + poolId + "' was cancelled.");

        internal static CoCoDiagnostic CreateFailed(PoolId poolId, string reason) =>
            Error(
                CoCoDiagnosticCode.PoolInstanceCreateFailed,
                "Pool '" + poolId + "' failed to create an instance. " +
                (reason ?? string.Empty));

        internal static CoCoDiagnostic InvalidHandle() =>
            Error(
                CoCoDiagnosticCode.InvalidPooledHandle,
                "The PooledHandle is invalid.");

        internal static CoCoDiagnostic AlreadyReturned(PoolId poolId, long instanceSequence) =>
            CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PooledHandleAlreadyReturned,
                "Instance " + instanceSequence + " from pool '" + poolId +
                "' was already returned.");

        internal static CoCoDiagnostic StaleHandle(PoolId poolId, long instanceSequence) =>
            CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.StalePooledHandle,
                "The handle for instance " + instanceSequence + " from pool '" +
                poolId + "' is stale.");

        internal static CoCoDiagnostic OwnerMismatch(PoolId poolId, long scopeSequence) =>
            Error(
                CoCoDiagnosticCode.PooledHandleOwnerMismatch,
                "The handle for pool '" + poolId + "' does not belong to Pool Scope " +
                scopeSequence + ".");

        internal static CoCoDiagnostic InvalidTransition(
            PoolId poolId,
            long instanceSequence,
            PooledInstanceState state,
            string operation) =>
            Error(
                CoCoDiagnosticCode.InvalidPoolTransition,
                "Instance " + instanceSequence + " from pool '" + poolId +
                "' cannot " + operation + " from state " + state + ".");

        internal static CoCoDiagnostic ActivationFailed(
            PoolId poolId,
            long instanceSequence,
            string reason) =>
            Error(
                CoCoDiagnosticCode.PoolActivationFailed,
                "Instance " + instanceSequence + " from pool '" + poolId +
                "' failed to activate. " + (reason ?? string.Empty));

        internal static CoCoDiagnostic ResetFailed(
            PoolId poolId,
            long instanceSequence,
            string reason) =>
            Error(
                CoCoDiagnosticCode.PoolResetFailed,
                "Instance " + instanceSequence + " from pool '" + poolId +
                "' failed to reset. " + (reason ?? string.Empty));

        internal static CoCoDiagnostic InstanceDestroyed(
            PoolId poolId,
            long instanceSequence,
            bool expected) =>
            expected
                ? CoCoDiagnostic.Info(
                    CoCoDiagnosticDomain.Pooling,
                    CoCoDiagnosticCode.PooledInstanceDestroyed,
                    "Instance " + instanceSequence + " from pool '" + poolId +
                    "' was destroyed.")
                : CoCoDiagnostic.Warning(
                    CoCoDiagnosticDomain.Pooling,
                    CoCoDiagnosticCode.PooledInstanceDestroyed,
                    "Instance " + instanceSequence + " from pool '" + poolId +
                    "' was destroyed outside Pool authority.");

        internal static CoCoDiagnostic MainThreadRequired() =>
            Error(
                CoCoDiagnosticCode.PoolMainThreadRequired,
                "Pooling operations must start on the Unity main thread.");

        internal static CoCoDiagnostic CallbackReentry(PoolId poolId) =>
            Error(
                CoCoDiagnosticCode.PoolCallbackReentry,
                "Pool '" + poolId + "' rejected lifecycle callback re-entry.");

        internal static CoCoDiagnostic HandleLeak(
            PoolId poolId,
            long instanceSequence,
            string allocationStack) =>
            CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolHandleLeak,
                "Instance " + instanceSequence + " from pool '" + poolId +
                "' was still leased during shutdown. " + (allocationStack ?? string.Empty));

        internal static CoCoDiagnostic ForcedShutdown() =>
            CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Pooling,
                CoCoDiagnosticCode.PoolForcedShutdown,
                "The Pool Runtime required synchronous forced shutdown.");

        internal static CoCoDiagnostic TemporalConflict(string reason) =>
            Error(
                CoCoDiagnosticCode.PoolTemporalConflict,
                "Temporal Pool authority conflict. " + (reason ?? string.Empty));

        internal static CoCoDiagnostic TemporalUnavailable(string reason) =>
            Error(
                CoCoDiagnosticCode.PoolTemporalEntityUnavailable,
                "The temporal pooled instance is unavailable. " + (reason ?? string.Empty));

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Pooling, code, message);
    }
}
