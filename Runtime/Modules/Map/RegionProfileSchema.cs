using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    public enum RegionParticipantRequirement
    {
        Required = 0,
        Optional = 1
    }

    public enum RegionParticipantPhase
    {
        Residency = 0,
        Services = 100,
        Simulation = 200,
        Presentation = 300
    }

    [Serializable]
    public abstract class RegionParticipantConfig
    {
    }

    [Serializable]
    public sealed class RegionTierDefinition
    {
        [SerializeField] private RegionTierId tierId;
        [SerializeField] private string name = string.Empty;
        [SerializeField] private List<RegionCapabilityId> capabilities =
            new List<RegionCapabilityId>();

        internal RegionTierDefinition()
        {
        }

        internal RegionTierDefinition(
            RegionTierId tierId,
            string name,
            IEnumerable<RegionCapabilityId> capabilities)
        {
            this.tierId = tierId;
            this.name = name ?? string.Empty;
            this.capabilities = capabilities == null
                ? new List<RegionCapabilityId>()
                : new List<RegionCapabilityId>(capabilities);
        }

        public RegionTierId TierId => tierId;
        public string Name => name ?? string.Empty;
        public IReadOnlyList<RegionCapabilityId> Capabilities =>
            capabilities ?? (IReadOnlyList<RegionCapabilityId>)Array.Empty<RegionCapabilityId>();
    }

    [Serializable]
    public sealed class RegionParticipantTierSetting
    {
        [SerializeField] private RegionTierId tierId;
        [SerializeField] private bool enabled;
        [SerializeField] private RegionParticipantModeId modeId;
        [SerializeReference] private RegionParticipantConfig configuration;

        internal RegionParticipantTierSetting()
        {
        }

        internal RegionParticipantTierSetting(RegionTierId tierId)
        {
            this.tierId = tierId;
        }

        internal RegionParticipantTierSetting(
            RegionTierId tierId,
            RegionParticipantModeId modeId,
            RegionParticipantConfig configuration)
        {
            this.tierId = tierId;
            enabled = true;
            this.modeId = modeId;
            this.configuration = configuration;
        }

        public RegionTierId TierId => tierId;
        public bool Enabled => enabled;
        public RegionParticipantModeId ModeId => modeId;
        public RegionParticipantConfig Configuration => configuration;
    }

    [Serializable]
    public sealed class RegionParticipantDefinition
    {
        [SerializeField] private RegionParticipantSlotId slotId;
        [SerializeField] private RegionParticipantTypeId participantTypeId;
        [SerializeField] private RegionParticipantPhase phase;
        [SerializeField] private int explicitOrder;
        [SerializeField] private RegionParticipantRequirement requirement;
        [SerializeField] private List<RegionParticipantSlotId> dependencies =
            new List<RegionParticipantSlotId>();
        [SerializeField] private List<RegionParticipantTierSetting> tierSettings =
            new List<RegionParticipantTierSetting>();

        internal RegionParticipantDefinition()
        {
        }

        internal RegionParticipantDefinition(
            RegionParticipantSlotId slotId,
            RegionParticipantTypeId participantTypeId,
            RegionParticipantPhase phase,
            int explicitOrder,
            RegionParticipantRequirement requirement,
            IEnumerable<RegionParticipantSlotId> dependencies,
            IEnumerable<RegionParticipantTierSetting> tierSettings)
        {
            this.slotId = slotId;
            this.participantTypeId = participantTypeId;
            this.phase = phase;
            this.explicitOrder = explicitOrder;
            this.requirement = requirement;
            this.dependencies = dependencies == null
                ? new List<RegionParticipantSlotId>()
                : new List<RegionParticipantSlotId>(dependencies);
            this.tierSettings = tierSettings == null
                ? new List<RegionParticipantTierSetting>()
                : new List<RegionParticipantTierSetting>(tierSettings);
        }

        public RegionParticipantSlotId SlotId => slotId;
        public RegionParticipantTypeId ParticipantTypeId => participantTypeId;
        public RegionParticipantPhase Phase => phase;
        public int ExplicitOrder => explicitOrder;
        public RegionParticipantRequirement Requirement => requirement;
        public IReadOnlyList<RegionParticipantSlotId> Dependencies =>
            dependencies ??
            (IReadOnlyList<RegionParticipantSlotId>)Array.Empty<RegionParticipantSlotId>();
        public IReadOnlyList<RegionParticipantTierSetting> TierSettings =>
            tierSettings ??
            (IReadOnlyList<RegionParticipantTierSetting>)
            Array.Empty<RegionParticipantTierSetting>();

        internal void SynchronizeTierSettings(
            IReadOnlyList<RegionTierDefinition> tiers)
        {
            var existing =
                new Dictionary<RegionTierId, RegionParticipantTierSetting>();
            if (tierSettings != null)
            {
                for (int index = 0; index < tierSettings.Count; index++)
                {
                    RegionParticipantTierSetting setting = tierSettings[index];
                    if (setting != null &&
                        setting.TierId.IsValid &&
                        !existing.ContainsKey(setting.TierId))
                    {
                        existing.Add(setting.TierId, setting);
                    }
                }
            }

            var synchronized = new List<RegionParticipantTierSetting>(
                tiers == null ? 0 : tiers.Count);
            if (tiers != null)
            {
                for (int index = 0; index < tiers.Count; index++)
                {
                    RegionTierDefinition tier = tiers[index];
                    if (tier != null &&
                        tier.TierId.IsValid &&
                        existing.TryGetValue(
                            tier.TierId,
                            out RegionParticipantTierSetting setting))
                    {
                        synchronized.Add(setting);
                    }
                    else
                    {
                        synchronized.Add(
                            new RegionParticipantTierSetting(
                                tier == null
                                    ? default
                                    : tier.TierId));
                    }
                }
            }

            tierSettings = synchronized;
        }
    }

    internal static class RegionDefaultTiers
    {
        internal static readonly RegionTierId[] Ids =
        {
            RegionTierId.Off,
            RegionTierId.Represented,
            RegionTierId.Background,
            RegionTierId.Enterable,
            RegionTierId.Full
        };

        internal static readonly RegionCapabilityId[][] Capabilities =
        {
            Array.Empty<RegionCapabilityId>(),
            new[] { RegionCapabilityId.Represented },
            new[]
            {
                RegionCapabilityId.Represented,
                RegionCapabilityId.Background
            },
            new[]
            {
                RegionCapabilityId.Represented,
                RegionCapabilityId.Background,
                RegionCapabilityId.Enterable
            },
            new[]
            {
                RegionCapabilityId.Represented,
                RegionCapabilityId.Background,
                RegionCapabilityId.Enterable,
                RegionCapabilityId.Full
            }
        };
    }
}
