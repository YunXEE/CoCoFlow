using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Content;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionParticipantSlotBinding
    {
        [SerializeField] private RegionParticipantSlotId slotId;
        [SerializeField] private string fragmentId = string.Empty;

        public RegionParticipantSlotId SlotId => slotId;
        public string FragmentId => fragmentId ?? string.Empty;
    }

    [Serializable]
    public sealed class RegionChunkBinding
    {
        [SerializeField] private RegionChunkId chunkId;
        [SerializeField] private ContentReference sceneSource;
        [SerializeField] private RegionParticipantSlotId owningContentSlotId;
        [SerializeField] private List<RegionParticipantSlotBinding> participants =
            new List<RegionParticipantSlotBinding>();

        public RegionChunkId ChunkId => chunkId;
        public ContentReference SceneSource => sceneSource;
        public RegionParticipantSlotId OwningContentSlotId =>
            owningContentSlotId;
        public IReadOnlyList<RegionParticipantSlotBinding> Participants =>
            participants ??
            (IReadOnlyList<RegionParticipantSlotBinding>)
            Array.Empty<RegionParticipantSlotBinding>();
    }

    [CreateAssetMenu(
        fileName = "CoCoRegionBinding",
        menuName = "CoCoFlow/Map/Region Binding")]
    public sealed class CoCoRegionBinding : ScriptableObject
    {
        [SerializeField] private RegionId regionId;
        [SerializeField] private CoCoRegionProfile profile;
        [SerializeField] private List<RegionParticipantSlotBinding>
            regionParticipants = new List<RegionParticipantSlotBinding>();
        [SerializeField] private List<RegionChunkBinding> chunks =
            new List<RegionChunkBinding>();

        public RegionId RegionId => regionId;
        public CoCoRegionProfile Profile => profile;
        public IReadOnlyList<RegionParticipantSlotBinding> RegionParticipants =>
            regionParticipants ??
            (IReadOnlyList<RegionParticipantSlotBinding>)
            Array.Empty<RegionParticipantSlotBinding>();
        public IReadOnlyList<RegionChunkBinding> Chunks =>
            chunks ??
            (IReadOnlyList<RegionChunkBinding>)Array.Empty<RegionChunkBinding>();
    }
}
