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
        [SerializeField] private string name = string.Empty;
        [SerializeField] private List<RegionCapabilityId> capabilities =
            new List<RegionCapabilityId>();

        internal RegionTierDefinition()
        {
        }

        internal RegionTierDefinition(
            string name,
            IEnumerable<RegionCapabilityId> capabilities)
        {
            this.name = name ?? string.Empty;
            this.capabilities = capabilities == null
                ? new List<RegionCapabilityId>()
                : new List<RegionCapabilityId>(capabilities);
        }

        public string Name => name ?? string.Empty;
        public IReadOnlyList<RegionCapabilityId> Capabilities =>
            capabilities ?? (IReadOnlyList<RegionCapabilityId>)Array.Empty<RegionCapabilityId>();
    }

    [Serializable]
    public sealed class RegionParticipantDefinition
    {
        [SerializeField] private RegionParticipantSlotId slotId;
        [SerializeField] private RegionParticipantTypeId participantTypeId;
        [SerializeField] private RegionParticipantModeId modeId;
        [SerializeField] private RegionParticipantPhase phase;
        [SerializeField] private int explicitOrder;
        [SerializeField] private RegionParticipantRequirement requirement;
        [SerializeField] private List<RegionCapabilityId> requiredCapabilities =
            new List<RegionCapabilityId>();
        [SerializeField] private List<RegionParticipantSlotId> dependencies =
            new List<RegionParticipantSlotId>();
        [SerializeReference] private RegionParticipantConfig configuration;

        internal RegionParticipantDefinition()
        {
        }

        internal RegionParticipantDefinition(
            RegionParticipantSlotId slotId,
            RegionParticipantTypeId participantTypeId,
            RegionParticipantModeId modeId,
            RegionParticipantPhase phase,
            int explicitOrder,
            RegionParticipantRequirement requirement,
            IEnumerable<RegionCapabilityId> requiredCapabilities,
            IEnumerable<RegionParticipantSlotId> dependencies,
            RegionParticipantConfig configuration)
        {
            this.slotId = slotId;
            this.participantTypeId = participantTypeId;
            this.modeId = modeId;
            this.phase = phase;
            this.explicitOrder = explicitOrder;
            this.requirement = requirement;
            this.requiredCapabilities = requiredCapabilities == null
                ? new List<RegionCapabilityId>()
                : new List<RegionCapabilityId>(requiredCapabilities);
            this.dependencies = dependencies == null
                ? new List<RegionParticipantSlotId>()
                : new List<RegionParticipantSlotId>(dependencies);
            this.configuration = configuration;
        }

        public RegionParticipantSlotId SlotId => slotId;
        public RegionParticipantTypeId ParticipantTypeId => participantTypeId;
        public RegionParticipantModeId ModeId => modeId;
        public RegionParticipantPhase Phase => phase;
        public int ExplicitOrder => explicitOrder;
        public RegionParticipantRequirement Requirement => requirement;
        public IReadOnlyList<RegionCapabilityId> RequiredCapabilities =>
            requiredCapabilities ??
            (IReadOnlyList<RegionCapabilityId>)Array.Empty<RegionCapabilityId>();
        public IReadOnlyList<RegionParticipantSlotId> Dependencies =>
            dependencies ??
            (IReadOnlyList<RegionParticipantSlotId>)Array.Empty<RegionParticipantSlotId>();
        public RegionParticipantConfig Configuration => configuration;
    }

    internal static class RegionDefaultTiers
    {
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
