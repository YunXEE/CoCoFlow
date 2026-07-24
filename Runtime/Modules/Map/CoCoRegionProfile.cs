using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [CreateAssetMenu(
        fileName = "CoCoRegionProfile",
        menuName = "CoCoFlow/Map/Region Profile")]
    public sealed class CoCoRegionProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const int DefaultTierCount = 5;

        [SerializeField, HideInInspector] private int schemaVersion =
            CurrentSchemaVersion;
        [SerializeField, HideInInspector] private RegionProfileId profileId;
        [SerializeField] private List<RegionTierDefinition> tiers =
            CreateDefaultTiers();
        [SerializeField] private List<RegionParticipantDefinition> participants =
            new List<RegionParticipantDefinition>();

        public int SchemaVersion => schemaVersion;
        public RegionProfileId ProfileId => profileId;

        public IReadOnlyList<RegionTierDefinition> Tiers =>
            tiers ??
            (IReadOnlyList<RegionTierDefinition>)Array.Empty<RegionTierDefinition>();

        public IReadOnlyList<RegionParticipantDefinition> Participants =>
            participants ??
            (IReadOnlyList<RegionParticipantDefinition>)
            Array.Empty<RegionParticipantDefinition>();

        internal bool SetEditorIdentity(RegionProfileId identity)
        {
            if (!identity.IsValid || profileId == identity)
            {
                return false;
            }

            profileId = identity;
            return true;
        }

        internal void SynchronizeParticipantTierSettings()
        {
            if (participants == null) return;

            for (int index = 0; index < participants.Count; index++)
            {
                participants[index]?.SynchronizeTierSettings(Tiers);
            }
        }

        private static List<RegionTierDefinition> CreateDefaultTiers()
        {
            var defaults = new List<RegionTierDefinition>(DefaultTierCount);
            string[] names =
            {
                "0 - Unloaded",
                "1 - Represented",
                "2 - Background",
                "3 - Enterable",
                "4 - Full"
            };

            for (int index = 0; index < RegionDefaultTiers.Capabilities.Length; index++)
            {
                defaults.Add(new RegionTierDefinition(
                    RegionDefaultTiers.Ids[index],
                    names[index],
                    RegionDefaultTiers.Capabilities[index]));
            }

            return defaults;
        }
    }
}
