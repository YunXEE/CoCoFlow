using System;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    public readonly struct PooledHandle :
        IDisposable,
        IEquatable<PooledHandle>
    {
        private readonly PoolScope scope;
        private readonly PoolId poolId;
        private readonly long scopeSequence;
        private readonly long instanceSequence;
        private readonly uint generation;

        internal PooledHandle(
            PoolScope scope,
            PoolId poolId,
            long scopeSequence,
            long instanceSequence,
            uint generation)
        {
            this.scope = scope;
            this.poolId = poolId;
            this.scopeSequence = scopeSequence;
            this.instanceSequence = instanceSequence;
            this.generation = generation;
        }

        public PoolId PoolId => poolId;
        public long ScopeSequence => scopeSequence;
        public long InstanceSequence => instanceSequence;
        public uint Generation => generation;

        public bool IsValid =>
            scope != null &&
            poolId.IsValid &&
            scopeSequence > 0 &&
            instanceSequence > 0 &&
            generation != 0;

        public bool TryGetInstance(
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            if (scope == null)
            {
                instance = null;
                diagnostic = PoolingErrors.InvalidHandle();
                return false;
            }

            return scope.TryGetInstance(this, out instance, out diagnostic);
        }

        public bool TryActivate(out CoCoDiagnostic diagnostic)
        {
            if (scope == null)
            {
                diagnostic = PoolingErrors.InvalidHandle();
                return false;
            }

            return scope.TryActivate(this, out diagnostic);
        }

        public bool TryReturn(out CoCoDiagnostic diagnostic)
        {
            if (scope == null)
            {
                diagnostic = PoolingErrors.InvalidHandle();
                return false;
            }

            return scope.TryReturn(this, out diagnostic);
        }

        public void Dispose()
        {
            TryReturn(out _);
        }

        public bool Equals(PooledHandle other) =>
            ReferenceEquals(scope, other.scope) &&
            poolId.Equals(other.poolId) &&
            scopeSequence == other.scopeSequence &&
            instanceSequence == other.instanceSequence &&
            generation == other.generation;

        public override bool Equals(object obj) =>
            obj is PooledHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = scope == null ? 0 : RuntimeHelpers.GetHashCode(scope);
                hashCode = (hashCode * 397) ^ poolId.GetHashCode();
                hashCode = (hashCode * 397) ^ scopeSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ instanceSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ generation.GetHashCode();
                return hashCode;
            }
        }

        internal PoolScope Scope => scope;

        public static bool operator ==(PooledHandle left, PooledHandle right) =>
            left.Equals(right);

        public static bool operator !=(PooledHandle left, PooledHandle right) =>
            !left.Equals(right);
    }
}
