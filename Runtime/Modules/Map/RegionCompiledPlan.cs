using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoCoFlow.Runtime.Content;

namespace CoCoFlow.Runtime.Modules.Map
{
    public readonly struct RegionCompiledSceneReference :
        IEquatable<RegionCompiledSceneReference>
    {
        internal RegionCompiledSceneReference(
            ContentId contentId,
            ContentSourceKind sourceKind,
            string locator,
            string canonicalScenePath)
        {
            ContentId = contentId;
            SourceKind = sourceKind;
            Locator = locator ?? string.Empty;
            CanonicalScenePath = canonicalScenePath ?? string.Empty;
        }

        public ContentId ContentId { get; }
        public ContentSourceKind SourceKind { get; }
        public string Locator { get; }
        public string CanonicalScenePath { get; }
        public bool IsValid =>
            ContentId.IsValid &&
            (SourceKind == ContentSourceKind.Direct ||
             SourceKind == ContentSourceKind.Addressables) &&
            !string.IsNullOrWhiteSpace(Locator) &&
            !string.IsNullOrWhiteSpace(CanonicalScenePath);

        public bool TryCreateContentReference(out ContentReference reference)
        {
            if (!IsValid)
            {
                reference = default;
                return false;
            }

            return SourceKind == ContentSourceKind.Direct
                ? ContentReference.TryCreateDirectAdditiveScene(
                    ContentId,
                    Locator,
                    out reference)
                : ContentReference.TryCreateAddressableAdditiveScene(
                    ContentId,
                    Locator,
                    out reference);
        }

        public bool Equals(RegionCompiledSceneReference other) =>
            ContentId == other.ContentId &&
            SourceKind == other.SourceKind &&
            string.Equals(Locator, other.Locator, StringComparison.Ordinal) &&
            string.Equals(
                CanonicalScenePath,
                other.CanonicalScenePath,
                StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is RegionCompiledSceneReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ContentId.GetHashCode();
                hashCode = hashCode * 397 ^ (int)SourceKind;
                hashCode = hashCode * 397 ^
                           StringComparer.Ordinal.GetHashCode(Locator);
                hashCode = hashCode * 397 ^
                           StringComparer.Ordinal.GetHashCode(
                               CanonicalScenePath);
                return hashCode;
            }
        }

        public static bool operator ==(
            RegionCompiledSceneReference left,
            RegionCompiledSceneReference right) => left.Equals(right);

        public static bool operator !=(
            RegionCompiledSceneReference left,
            RegionCompiledSceneReference right) => !left.Equals(right);
    }

    public readonly struct RegionPlanNodeId : IEquatable<RegionPlanNodeId>
    {
        private RegionPlanNodeId(
            RegionId regionId,
            RegionChunkId chunkId,
            bool hasChunkId,
            RegionParticipantSlotId slotId)
        {
            RegionId = regionId;
            ChunkId = chunkId;
            HasChunkId = hasChunkId;
            SlotId = slotId;
        }

        public RegionId RegionId { get; }
        public RegionChunkId ChunkId { get; }
        public bool HasChunkId { get; }
        public RegionParticipantSlotId SlotId { get; }
        public bool IsValid =>
            RegionId.IsValid &&
            SlotId.IsValid &&
            (!HasChunkId || ChunkId.IsValid);

        public static bool TryCreateGlobal(
            RegionId regionId,
            RegionParticipantSlotId slotId,
            out RegionPlanNodeId nodeId)
        {
            nodeId = new RegionPlanNodeId(regionId, default, false, slotId);
            if (nodeId.IsValid) return true;

            nodeId = default;
            return false;
        }

        public static bool TryCreateChunk(
            RegionId regionId,
            RegionChunkId chunkId,
            RegionParticipantSlotId slotId,
            out RegionPlanNodeId nodeId)
        {
            nodeId = new RegionPlanNodeId(regionId, chunkId, true, slotId);
            if (nodeId.IsValid) return true;

            nodeId = default;
            return false;
        }

        public bool Equals(RegionPlanNodeId other) =>
            RegionId == other.RegionId &&
            HasChunkId == other.HasChunkId &&
            (!HasChunkId || ChunkId == other.ChunkId) &&
            SlotId == other.SlotId;

        public override bool Equals(object obj) =>
            obj is RegionPlanNodeId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = RegionId.GetHashCode();
                hashCode = hashCode * 397 ^ HasChunkId.GetHashCode();
                hashCode = hashCode * 397 ^
                           (HasChunkId ? ChunkId.GetHashCode() : 0);
                hashCode = hashCode * 397 ^ SlotId.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString() =>
            HasChunkId
                ? RegionId.Value + "/" + ChunkId.Value + "/" + SlotId.Value
                : RegionId.Value + "/global/" + SlotId.Value;

        public static bool operator ==(
            RegionPlanNodeId left,
            RegionPlanNodeId right) => left.Equals(right);

        public static bool operator !=(
            RegionPlanNodeId left,
            RegionPlanNodeId right) => !left.Equals(right);
    }

    public sealed class RegionCompiledTier
    {
        internal RegionCompiledTier(
            int index,
            string name,
            RegionCapabilitySet capabilities)
        {
            Index = index;
            Name = name ?? string.Empty;
            Capabilities = capabilities ?? RegionCapabilitySet.Empty;
        }

        public int Index { get; }
        public string Name { get; }
        public RegionCapabilitySet Capabilities { get; }
    }

    public sealed class RegionCompiledChunk
    {
        internal RegionCompiledChunk(
            RegionChunkId chunkId,
            RegionCompiledSceneReference sceneReference,
            RegionParticipantSlotId owningContentSlotId)
        {
            ChunkId = chunkId;
            SceneReference = sceneReference;
            OwningContentSlotId = owningContentSlotId;
        }

        public RegionChunkId ChunkId { get; }
        public RegionCompiledSceneReference SceneReference { get; }
        public string CanonicalScenePath => SceneReference.CanonicalScenePath;
        public RegionParticipantSlotId OwningContentSlotId { get; }
        public bool HasScene => SceneReference.IsValid;
    }

    public sealed class RegionCompiledParticipantNode
    {
        private readonly ReadOnlyCollection<RegionPlanNodeId> dependencies;

        internal RegionCompiledParticipantNode(
            RegionPlanNodeId id,
            RegionParticipantTypeId participantTypeId,
            RegionParticipantModeId modeId,
            RegionParticipantPhase phase,
            int explicitOrder,
            RegionParticipantRequirement requirement,
            RegionCapabilitySet requiredCapabilities,
            IList<RegionPlanNodeId> dependencies,
            IRegionParticipantPlan participantPlan,
            string fragmentId,
            RegionCompiledSceneReference sceneReference,
            string fingerprint)
        {
            Id = id;
            ParticipantTypeId = participantTypeId;
            ModeId = modeId;
            Phase = phase;
            ExplicitOrder = explicitOrder;
            Requirement = requirement;
            RequiredCapabilities =
                requiredCapabilities ?? RegionCapabilitySet.Empty;
            this.dependencies = new ReadOnlyCollection<RegionPlanNodeId>(
                dependencies == null
                    ? new List<RegionPlanNodeId>()
                    : new List<RegionPlanNodeId>(dependencies));
            ParticipantPlan = participantPlan;
            FragmentId = fragmentId ?? string.Empty;
            SceneReference = sceneReference;
            Fingerprint = fingerprint ?? string.Empty;
        }

        public RegionPlanNodeId Id { get; }
        public RegionParticipantTypeId ParticipantTypeId { get; }
        public RegionParticipantModeId ModeId { get; }
        public RegionParticipantPhase Phase { get; }
        public int ExplicitOrder { get; }
        public RegionParticipantRequirement Requirement { get; }
        public RegionCapabilitySet RequiredCapabilities { get; }
        public IReadOnlyList<RegionPlanNodeId> Dependencies => dependencies;
        public IRegionParticipantPlan ParticipantPlan { get; }
        public string FragmentId { get; }
        public RegionCompiledSceneReference SceneReference { get; }
        public string Fingerprint { get; }

        public bool IsActiveFor(RegionCapabilitySet capabilities) =>
            capabilities != null &&
            capabilities.IsSupersetOf(RequiredCapabilities);
    }

    public sealed class RegionCompiledPlan
    {
        private readonly ReadOnlyCollection<RegionCompiledTier> tiers;
        private readonly ReadOnlyCollection<RegionCompiledChunk> chunks;
        private readonly ReadOnlyCollection<RegionCompiledParticipantNode> nodes;
        private readonly Dictionary<RegionChunkId, RegionCompiledChunk> chunkById;
        private readonly Dictionary<RegionPlanNodeId, RegionCompiledParticipantNode>
            nodeById;

        internal RegionCompiledPlan(
            RegionId regionId,
            IList<RegionCompiledTier> tiers,
            IList<RegionCompiledChunk> chunks,
            IList<RegionCompiledParticipantNode> nodes,
            string fingerprint)
        {
            RegionId = regionId;
            this.tiers = new ReadOnlyCollection<RegionCompiledTier>(
                tiers == null
                    ? new List<RegionCompiledTier>()
                    : new List<RegionCompiledTier>(tiers));
            this.chunks = new ReadOnlyCollection<RegionCompiledChunk>(
                chunks == null
                    ? new List<RegionCompiledChunk>()
                    : new List<RegionCompiledChunk>(chunks));
            this.nodes = new ReadOnlyCollection<RegionCompiledParticipantNode>(
                nodes == null
                    ? new List<RegionCompiledParticipantNode>()
                    : new List<RegionCompiledParticipantNode>(nodes));
            Fingerprint = fingerprint ?? string.Empty;

            chunkById = new Dictionary<RegionChunkId, RegionCompiledChunk>();
            for (int index = 0; index < this.chunks.Count; index++)
            {
                chunkById.Add(this.chunks[index].ChunkId, this.chunks[index]);
            }

            nodeById =
                new Dictionary<RegionPlanNodeId, RegionCompiledParticipantNode>();
            for (int index = 0; index < this.nodes.Count; index++)
            {
                nodeById.Add(this.nodes[index].Id, this.nodes[index]);
            }
        }

        public RegionId RegionId { get; }
        public IReadOnlyList<RegionCompiledTier> Tiers => tiers;
        public IReadOnlyList<RegionCompiledChunk> Chunks => chunks;
        public IReadOnlyList<RegionCompiledParticipantNode> Nodes => nodes;
        public string Fingerprint { get; }

        public bool TryGetChunk(
            RegionChunkId chunkId,
            out RegionCompiledChunk chunk) =>
            chunkById.TryGetValue(chunkId, out chunk);

        public bool TryGetNode(
            RegionPlanNodeId nodeId,
            out RegionCompiledParticipantNode node) =>
            nodeById.TryGetValue(nodeId, out node);
    }

    internal sealed class RegionCompiledProfileBlueprint
    {
        internal RegionCompiledProfileBlueprint(
            IList<RegionCompiledTier> tiers,
            IList<RegionCompiledParticipantDefinition> participants)
        {
            Tiers = new ReadOnlyCollection<RegionCompiledTier>(
                new List<RegionCompiledTier>(tiers));
            Participants =
                new ReadOnlyCollection<RegionCompiledParticipantDefinition>(
                    new List<RegionCompiledParticipantDefinition>(participants));
        }

        internal IReadOnlyList<RegionCompiledTier> Tiers { get; }
        internal IReadOnlyList<RegionCompiledParticipantDefinition> Participants
        {
            get;
        }
    }

    internal sealed class RegionCompiledParticipantDefinition
    {
        internal RegionCompiledParticipantDefinition(
            RegionParticipantDefinition source,
            RegionCapabilitySet requiredCapabilities,
            RegionParticipantRegistration registration)
        {
            Source = source;
            RequiredCapabilities = requiredCapabilities;
            Registration = registration;
        }

        internal RegionParticipantDefinition Source { get; }
        internal RegionCapabilitySet RequiredCapabilities { get; }
        internal RegionParticipantRegistration Registration { get; }
    }
}
