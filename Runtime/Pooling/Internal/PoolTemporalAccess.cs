using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    internal struct PoolTemporalToken
    {
        internal PoolTemporalToken(
            PoolScope scope,
            PoolId poolId,
            long instanceSequence,
            uint generation)
        {
            Scope = scope;
            PoolId = poolId;
            InstanceSequence = instanceSequence;
            Generation = generation;
        }

        internal PoolScope Scope { get; }
        internal PoolId PoolId { get; }
        internal long InstanceSequence { get; }
        internal uint Generation { get; }

        internal bool IsValid =>
            Scope != null &&
            PoolId.IsValid &&
            InstanceSequence > 0 &&
            Generation != 0;
    }

    internal static class PoolTemporalAccess
    {
        internal static bool TryAdopt(
            PoolRuntime expectedRuntime,
            ref PooledHandle handle,
            out PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            token = default;
            if (expectedRuntime == null ||
                !handle.IsValid ||
                !expectedRuntime.Owns(handle.Scope))
            {
                diagnostic = PoolingErrors.TemporalConflict(
                    "The handle is not owned by the expected Pool Runtime.");
                return false;
            }

            if (!handle.Scope.TryAdoptTemporal(handle, out token, out diagnostic))
            {
                return false;
            }

            handle = default;
            return true;
        }

        internal static bool TryGetInstance(
            in PoolTemporalToken token,
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryGetEntry(token, out PoolEntry entry, out diagnostic))
            {
                instance = null;
                return false;
            }

            return entry.TryGetTemporalInstance(token, out instance, out diagnostic);
        }

        internal static bool TryActivate(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryGetEntry(token, out PoolEntry entry, out diagnostic))
            {
                return false;
            }

            return entry.TryActivateTemporal(ref token, out diagnostic);
        }

        internal static bool TryDespawn(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryGetEntry(token, out PoolEntry entry, out diagnostic))
            {
                return false;
            }

            return entry.TryDespawnTemporal(ref token, out diagnostic);
        }

        internal static bool TryPreparePresence(
            ref PoolTemporalToken token,
            bool desiredPresent,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryGetEntry(token, out PoolEntry entry, out diagnostic))
            {
                return false;
            }

            return entry.TryPrepareTemporalPresence(
                ref token,
                desiredPresent,
                out diagnostic);
        }

        internal static bool TryRelease(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryGetEntry(token, out PoolEntry entry, out diagnostic))
            {
                return false;
            }

            return entry.TryReleaseTemporal(ref token, out diagnostic);
        }

        internal static bool ForceDestroy(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryGetEntry(token, out PoolEntry entry, out diagnostic))
            {
                return false;
            }

            return entry.ForceDestroyTemporal(ref token, out diagnostic);
        }

        private static bool TryGetEntry(
            in PoolTemporalToken token,
            out PoolEntry entry,
            out CoCoDiagnostic diagnostic)
        {
            entry = null;
            if (!token.IsValid || token.Scope == null)
            {
                diagnostic = PoolingErrors.TemporalUnavailable(
                    "The internal authority token is invalid.");
                return false;
            }

            return token.Scope.TryGetTemporalEntry(token, out entry, out diagnostic);
        }
    }
}
