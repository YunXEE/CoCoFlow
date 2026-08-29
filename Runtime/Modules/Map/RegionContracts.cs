using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public struct RegionProfileId : IEquatable<RegionProfileId>
    {
        [SerializeField] private string value;

        private RegionProfileId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsStableId(Value);

        public static bool TryCreate(string value, out RegionProfileId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsStableId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionProfileId(normalized);
            return true;
        }

        public bool Equals(RegionProfileId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionProfileId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;

        public static bool operator ==(
            RegionProfileId left,
            RegionProfileId right) => left.Equals(right);

        public static bool operator !=(
            RegionProfileId left,
            RegionProfileId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionTierId : IEquatable<RegionTierId>
    {
        [SerializeField] private string value;

        private RegionTierId(string value) => this.value = value;

        public static RegionTierId Off => CreateBuiltIn("off");
        public static RegionTierId Represented => CreateBuiltIn("represented");
        public static RegionTierId Background => CreateBuiltIn("background");
        public static RegionTierId Enterable => CreateBuiltIn("enterable");
        public static RegionTierId Full => CreateBuiltIn("full");

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsStableId(Value);

        public static bool TryCreate(string value, out RegionTierId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsStableId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionTierId(normalized);
            return true;
        }

        public bool Equals(RegionTierId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionTierId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;

        public static bool operator ==(
            RegionTierId left,
            RegionTierId right) => left.Equals(right);

        public static bool operator !=(
            RegionTierId left,
            RegionTierId right) => !left.Equals(right);

        private static RegionTierId CreateBuiltIn(string value) =>
            new RegionTierId(value);
    }

    [Serializable]
    public struct RegionId : IEquatable<RegionId>
    {
        [SerializeField] private string value;

        private RegionId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsStableId(Value);

        public static bool TryCreate(string value, out RegionId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsStableId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionId(normalized);
            return true;
        }

        public bool Equals(RegionId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is RegionId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(RegionId left, RegionId right) => left.Equals(right);
        public static bool operator !=(RegionId left, RegionId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionChunkId : IEquatable<RegionChunkId>
    {
        [SerializeField] private string value;

        private RegionChunkId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsStableId(Value);

        public static bool TryCreate(string value, out RegionChunkId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsStableId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionChunkId(normalized);
            return true;
        }

        public bool Equals(RegionChunkId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is RegionChunkId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(RegionChunkId left, RegionChunkId right) => left.Equals(right);
        public static bool operator !=(RegionChunkId left, RegionChunkId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionParticipantSlotId : IEquatable<RegionParticipantSlotId>
    {
        [SerializeField] private string value;

        private RegionParticipantSlotId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsStableId(Value);

        public static bool TryCreate(string value, out RegionParticipantSlotId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsStableId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionParticipantSlotId(normalized);
            return true;
        }

        public bool Equals(RegionParticipantSlotId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionParticipantSlotId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(
            RegionParticipantSlotId left,
            RegionParticipantSlotId right) => left.Equals(right);
        public static bool operator !=(
            RegionParticipantSlotId left,
            RegionParticipantSlotId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionParticipantModeId : IEquatable<RegionParticipantModeId>
    {
        [SerializeField] private string value;

        private RegionParticipantModeId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsNamespacedId(Value);

        public static bool TryCreate(string value, out RegionParticipantModeId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsNamespacedId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionParticipantModeId(normalized);
            return true;
        }

        public bool Equals(RegionParticipantModeId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionParticipantModeId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(
            RegionParticipantModeId left,
            RegionParticipantModeId right) => left.Equals(right);
        public static bool operator !=(
            RegionParticipantModeId left,
            RegionParticipantModeId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionParticipantTypeId : IEquatable<RegionParticipantTypeId>
    {
        [SerializeField] private string value;

        private RegionParticipantTypeId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsNamespacedId(Value);

        public static bool TryCreate(string value, out RegionParticipantTypeId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsNamespacedId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionParticipantTypeId(normalized);
            return true;
        }

        public bool Equals(RegionParticipantTypeId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionParticipantTypeId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(
            RegionParticipantTypeId left,
            RegionParticipantTypeId right) => left.Equals(right);
        public static bool operator !=(
            RegionParticipantTypeId left,
            RegionParticipantTypeId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionDemandOwnerId : IEquatable<RegionDemandOwnerId>
    {
        [SerializeField] private string value;

        private RegionDemandOwnerId(string value) => this.value = value;

        public string Value => value ?? string.Empty;
        public bool IsValid => RegionIdentifierRules.IsStableId(Value);

        public static bool TryCreate(string value, out RegionDemandOwnerId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsStableId(normalized))
            {
                id = default;
                return false;
            }

            id = new RegionDemandOwnerId(normalized);
            return true;
        }

        public bool Equals(RegionDemandOwnerId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionDemandOwnerId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(
            RegionDemandOwnerId left,
            RegionDemandOwnerId right) => left.Equals(right);
        public static bool operator !=(
            RegionDemandOwnerId left,
            RegionDemandOwnerId right) => !left.Equals(right);
    }

    [Serializable]
    public struct RegionCapabilityId : IEquatable<RegionCapabilityId>
    {
        public const string ReservedNamespace = "cocoflow.";

        [SerializeField] private string value;

        private RegionCapabilityId(string value) => this.value = value;

        public static RegionCapabilityId Represented =>
            new RegionCapabilityId("cocoflow.represented");
        public static RegionCapabilityId Background =>
            new RegionCapabilityId("cocoflow.background");
        public static RegionCapabilityId Enterable =>
            new RegionCapabilityId("cocoflow.enterable");
        public static RegionCapabilityId Full =>
            new RegionCapabilityId("cocoflow.full");

        public string Value => value ?? string.Empty;
        public bool IsValid =>
            RegionIdentifierRules.IsNamespacedId(Value) &&
            (!Value.StartsWith(ReservedNamespace, StringComparison.Ordinal) ||
             IsStandardValue(Value));
        public bool IsStandard => IsStandardValue(Value);

        public static bool TryCreate(string value, out RegionCapabilityId id)
        {
            string normalized = RegionIdentifierRules.Normalize(value);
            if (!RegionIdentifierRules.IsNamespacedId(normalized) ||
                (normalized.StartsWith(ReservedNamespace, StringComparison.Ordinal) &&
                 !IsStandardValue(normalized)))
            {
                id = default;
                return false;
            }

            id = new RegionCapabilityId(normalized);
            return true;
        }

        public static int StandardOrder(RegionCapabilityId id)
        {
            if (id == Represented) return 0;
            if (id == Background) return 1;
            if (id == Enterable) return 2;
            if (id == Full) return 3;
            return -1;
        }

        public bool Equals(RegionCapabilityId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionCapabilityId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(
            RegionCapabilityId left,
            RegionCapabilityId right) => left.Equals(right);
        public static bool operator !=(
            RegionCapabilityId left,
            RegionCapabilityId right) => !left.Equals(right);

        private static bool IsStandardValue(string candidate) =>
            string.Equals(candidate, Represented.Value, StringComparison.Ordinal) ||
            string.Equals(candidate, Background.Value, StringComparison.Ordinal) ||
            string.Equals(candidate, Enterable.Value, StringComparison.Ordinal) ||
            string.Equals(candidate, Full.Value, StringComparison.Ordinal);
    }

    public sealed class RegionCapabilitySet : IEquatable<RegionCapabilitySet>
    {
        private static readonly RegionCapabilitySet EmptyInstance =
            new RegionCapabilitySet(Array.Empty<RegionCapabilityId>());

        private readonly RegionCapabilityId[] capabilities;
        private readonly ReadOnlyCollection<RegionCapabilityId> readOnlyCapabilities;

        private RegionCapabilitySet(RegionCapabilityId[] capabilities)
        {
            this.capabilities = capabilities;
            readOnlyCapabilities = Array.AsReadOnly(capabilities);
        }

        public static RegionCapabilitySet Empty => EmptyInstance;
        public IReadOnlyList<RegionCapabilityId> Capabilities => readOnlyCapabilities;
        public int Count => capabilities.Length;

        public static bool TryCreate(
            IEnumerable<RegionCapabilityId> source,
            out RegionCapabilitySet set)
        {
            if (source == null)
            {
                set = null;
                return false;
            }

            var unique = new HashSet<RegionCapabilityId>();
            foreach (RegionCapabilityId capability in source)
            {
                if (!capability.IsValid)
                {
                    set = null;
                    return false;
                }

                unique.Add(capability);
            }

            RegionCapabilityId[] ordered = new RegionCapabilityId[unique.Count];
            unique.CopyTo(ordered);
            Array.Sort(ordered, CompareCapabilities);
            set = ordered.Length == 0
                ? Empty
                : new RegionCapabilitySet(ordered);
            return true;
        }

        public bool Contains(RegionCapabilityId capability) =>
            capability.IsValid &&
            Array.BinarySearch(capabilities, capability, CapabilityComparer.Instance) >= 0;

        public bool IsSupersetOf(RegionCapabilitySet other)
        {
            if (other == null) return false;
            for (int index = 0; index < other.capabilities.Length; index++)
            {
                if (!Contains(other.capabilities[index])) return false;
            }

            return true;
        }

        public bool IsStrictSupersetOf(RegionCapabilitySet other) =>
            other != null &&
            Count > other.Count &&
            IsSupersetOf(other);

        public RegionCapabilitySet Union(RegionCapabilitySet other)
        {
            if (other == null || other.Count == 0) return this;
            if (Count == 0) return other;

            var combined = new RegionCapabilityId[Count + other.Count];
            Array.Copy(capabilities, combined, Count);
            Array.Copy(other.capabilities, 0, combined, Count, other.Count);
            return TryCreate(combined, out RegionCapabilitySet result)
                ? result
                : throw new InvalidOperationException(
                    "Valid immutable capability sets must have a valid union.");
        }

        public bool Equals(RegionCapabilitySet other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || Count != other.Count) return false;
            for (int index = 0; index < Count; index++)
            {
                if (capabilities[index] != other.capabilities[index]) return false;
            }

            return true;
        }

        public override bool Equals(object obj) =>
            obj is RegionCapabilitySet other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 17;
                for (int index = 0; index < capabilities.Length; index++)
                {
                    hashCode = hashCode * 31 + capabilities[index].GetHashCode();
                }

                return hashCode;
            }
        }

        private static int CompareCapabilities(
            RegionCapabilityId left,
            RegionCapabilityId right) =>
            string.CompareOrdinal(left.Value, right.Value);

        private sealed class CapabilityComparer : IComparer<RegionCapabilityId>
        {
            internal static readonly CapabilityComparer Instance =
                new CapabilityComparer();

            public int Compare(RegionCapabilityId left, RegionCapabilityId right) =>
                CompareCapabilities(left, right);
        }
    }

    public enum RegionCoverageKind
    {
        None = 0,
        All = 1,
        Chunks = 2
    }

    public readonly struct RegionCoverage : IEquatable<RegionCoverage>
    {
        private readonly RegionCoverageKind kind;
        private readonly IReadOnlyList<RegionChunkId> chunks;

        private RegionCoverage(
            RegionCoverageKind kind,
            IReadOnlyList<RegionChunkId> chunks)
        {
            this.kind = kind;
            this.chunks = chunks;
        }

        public static RegionCoverage All =>
            new RegionCoverage(
                RegionCoverageKind.All,
                Array.Empty<RegionChunkId>());

        public RegionCoverageKind Kind => kind;
        public bool IsValid =>
            kind == RegionCoverageKind.All ||
            kind == RegionCoverageKind.Chunks &&
            chunks != null &&
            chunks.Count > 0;
        public bool CoversAll => kind == RegionCoverageKind.All;
        public IReadOnlyList<RegionChunkId> Chunks =>
            chunks ?? Array.Empty<RegionChunkId>();

        public static bool TryCreateChunks(
            IEnumerable<RegionChunkId> source,
            out RegionCoverage coverage)
        {
            if (source == null)
            {
                coverage = default;
                return false;
            }

            var unique = new HashSet<RegionChunkId>();
            foreach (RegionChunkId chunkId in source)
            {
                if (!chunkId.IsValid || !unique.Add(chunkId))
                {
                    coverage = default;
                    return false;
                }
            }

            if (unique.Count == 0)
            {
                coverage = default;
                return false;
            }

            var ordered = new RegionChunkId[unique.Count];
            unique.CopyTo(ordered);
            Array.Sort(
                ordered,
                (left, right) => string.CompareOrdinal(left.Value, right.Value));
            coverage = new RegionCoverage(
                RegionCoverageKind.Chunks,
                Array.AsReadOnly(ordered));
            return true;
        }

        public bool Contains(RegionChunkId chunkId)
        {
            if (!chunkId.IsValid || !IsValid) return false;
            if (CoversAll) return true;
            for (int index = 0; index < Chunks.Count; index++)
            {
                if (Chunks[index] == chunkId) return true;
            }

            return false;
        }

        public bool Equals(RegionCoverage other)
        {
            if (kind != other.kind || Chunks.Count != other.Chunks.Count) return false;
            for (int index = 0; index < Chunks.Count; index++)
            {
                if (Chunks[index] != other.Chunks[index]) return false;
            }

            return true;
        }

        public override bool Equals(object obj) =>
            obj is RegionCoverage other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)kind;
                for (int index = 0; index < Chunks.Count; index++)
                {
                    hashCode = hashCode * 31 + Chunks[index].GetHashCode();
                }

                return hashCode;
            }
        }

        public static bool operator ==(RegionCoverage left, RegionCoverage right) =>
            left.Equals(right);
        public static bool operator !=(RegionCoverage left, RegionCoverage right) =>
            !left.Equals(right);
    }

    public readonly struct RegionDemandRevision : IEquatable<RegionDemandRevision>
    {
        internal RegionDemandRevision(long value) => Value = value;

        public long Value { get; }
        public bool IsValid => Value > 0L;
        public bool Equals(RegionDemandRevision other) => Value == other.Value;
        public override bool Equals(object obj) =>
            obj is RegionDemandRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(
            RegionDemandRevision left,
            RegionDemandRevision right) => left.Equals(right);
        public static bool operator !=(
            RegionDemandRevision left,
            RegionDemandRevision right) => !left.Equals(right);
    }

    public enum RegionReadinessStatus
    {
        Ready = 1,
        Cancelled = 2,
        Superseded = 3,
        Failed = 4,
        Disposed = 5
    }

    public readonly struct RegionReadinessResult
    {
        internal RegionReadinessResult(
            RegionDemandRevision revision,
            RegionReadinessStatus status,
            CoCoFlow.Runtime.Core.CoCoDiagnostic diagnostic)
        {
            Revision = revision;
            Status = status;
            Diagnostic = diagnostic;
        }

        public RegionDemandRevision Revision { get; }
        public RegionReadinessStatus Status { get; }
        public CoCoFlow.Runtime.Core.CoCoDiagnostic Diagnostic { get; }
        public bool IsReady => Status == RegionReadinessStatus.Ready;
    }

    internal static class RegionIdentifierRules
    {
        internal static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();

        internal static bool IsStableId(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value.Length > 128)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid =
                    character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' ||
                    character == '-' ||
                    character == '_' ||
                    character == '/';
                if (!valid) return false;
            }

            return true;
        }

        internal static bool IsNamespacedId(string value)
        {
            if (!IsStableId(value)) return false;
            int separator = value.IndexOf('.');
            return separator > 0 &&
                   separator < value.Length - 1 &&
                   value.IndexOf("..", StringComparison.Ordinal) < 0;
        }
    }
}
