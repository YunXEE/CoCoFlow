using System;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    [Serializable]
    public struct PoolId : IEquatable<PoolId>
    {
        [SerializeField] private string value;

        private PoolId(string value)
        {
            this.value = value;
        }

        public string Value => value ?? string.Empty;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(value) &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);

        public static bool TryCreate(string value, out PoolId id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                id = default;
                return false;
            }

            id = new PoolId(value.Trim());
            return true;
        }

        public bool Equals(PoolId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PoolId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;

        public static bool operator ==(PoolId left, PoolId right) => left.Equals(right);
        public static bool operator !=(PoolId left, PoolId right) => !left.Equals(right);
    }

    [Serializable]
    public struct PoolProfile : IEquatable<PoolProfile>
    {
        [SerializeField] private PoolId id;
        [SerializeField] private ContentReference prefabSource;
        [SerializeField, Min(0)] private int prewarmCount;
        [SerializeField, Min(0)] private int maxRetained;

        private PoolProfile(
            PoolId id,
            ContentReference prefabSource,
            int prewarmCount,
            int maxRetained)
        {
            this.id = id;
            this.prefabSource = prefabSource;
            this.prewarmCount = prewarmCount;
            this.maxRetained = maxRetained;
        }

        public PoolId Id => id;
        public ContentReference PrefabSource => prefabSource;
        public int PrewarmCount => prewarmCount;
        public int MaxRetained => maxRetained;

        public bool IsValid =>
            id.IsValid &&
            prefabSource.IsValid &&
            prefabSource.Kind == ContentKind.PrefabSource &&
            prewarmCount >= 0 &&
            maxRetained >= 0 &&
            prewarmCount <= maxRetained;

        public static bool TryCreate(
            PoolId id,
            ContentReference prefabSource,
            int prewarmCount,
            int maxRetained,
            out PoolProfile profile)
        {
            profile = new PoolProfile(id, prefabSource, prewarmCount, maxRetained);
            if (profile.IsValid) return true;

            profile = default;
            return false;
        }

        public bool Equals(PoolProfile other) =>
            id.Equals(other.id) &&
            prefabSource.Equals(other.prefabSource) &&
            prewarmCount == other.prewarmCount &&
            maxRetained == other.maxRetained;

        public override bool Equals(object obj) =>
            obj is PoolProfile other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = id.GetHashCode();
                hashCode = (hashCode * 397) ^ prefabSource.GetHashCode();
                hashCode = (hashCode * 397) ^ prewarmCount;
                hashCode = (hashCode * 397) ^ maxRetained;
                return hashCode;
            }
        }

        public static bool operator ==(PoolProfile left, PoolProfile right) => left.Equals(right);
        public static bool operator !=(PoolProfile left, PoolProfile right) => !left.Equals(right);
    }

    public enum PoolScopeState
    {
        Open = 0,
        Closing = 1,
        Closed = 2
    }

    public enum PoolEntryState
    {
        Preparing = 0,
        Ready = 1,
        Prewarming = 2,
        Closing = 3,
        Closed = 4,
        Failed = 5
    }

    public enum PooledInstanceState
    {
        Internal = 0,
        Inactive = 1,
        LeasedInactive = 2,
        Active = 3,
        Returning = 4,
        TemporalInactive = 5,
        TemporalActive = 6,
        TemporalQuarantined = 7,
        DestroyPending = 8,
        Destroyed = 9
    }

    public enum PoolReturnReason
    {
        ConsumerReturn = 0,
        ActivationFailure = 1,
        ScopeClosing = 2,
        TemporalDespawn = 3,
        TemporalRelease = 4,
        ForcedShutdown = 5
    }

    public readonly struct PoolRentContext
    {
        internal PoolRentContext(
            PoolId poolId,
            ContentOwnerId ownerId,
            long scopeSequence,
            long instanceSequence,
            uint generation,
            bool temporal)
        {
            PoolId = poolId;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            InstanceSequence = instanceSequence;
            Generation = generation;
            IsTemporal = temporal;
        }

        public PoolId PoolId { get; }
        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public long InstanceSequence { get; }
        public uint Generation { get; }
        public bool IsTemporal { get; }
    }

    public readonly struct PoolReturnContext
    {
        internal PoolReturnContext(
            PoolId poolId,
            ContentOwnerId ownerId,
            long scopeSequence,
            long instanceSequence,
            uint generation,
            PoolReturnReason reason,
            bool temporal)
        {
            PoolId = poolId;
            OwnerId = ownerId;
            ScopeSequence = scopeSequence;
            InstanceSequence = instanceSequence;
            Generation = generation;
            Reason = reason;
            IsTemporal = temporal;
        }

        public PoolId PoolId { get; }
        public ContentOwnerId OwnerId { get; }
        public long ScopeSequence { get; }
        public long InstanceSequence { get; }
        public uint Generation { get; }
        public PoolReturnReason Reason { get; }
        public bool IsTemporal { get; }
    }

    public interface IPoolable
    {
        bool TryOnRent(in PoolRentContext context, out CoCoDiagnostic diagnostic);
        bool TryOnReturn(in PoolReturnContext context, out CoCoDiagnostic diagnostic);
    }
}
