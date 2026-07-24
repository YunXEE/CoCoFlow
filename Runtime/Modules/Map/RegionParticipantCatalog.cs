using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    public interface IRegionParticipantCatalogProvider
    {
        bool TryGetCatalog(
            out RegionParticipantCatalog catalog,
            out CoCoDiagnostic diagnostic);
    }

    public sealed class RegionParticipantRegistration
    {
        private RegionParticipantRegistration(
            RegionParticipantTypeId participantTypeId,
            RegionParticipantModeId modeId,
            RegionCapabilitySet supportedCapabilities,
            IRegionParticipantConfigFreezer configFreezer,
            IRegionParticipantFactory factory)
        {
            ParticipantTypeId = participantTypeId;
            ModeId = modeId;
            SupportedCapabilities = supportedCapabilities;
            ConfigFreezer = configFreezer;
            Factory = factory;
        }

        public RegionParticipantTypeId ParticipantTypeId { get; }
        public RegionParticipantModeId ModeId { get; }
        public RegionCapabilitySet SupportedCapabilities { get; }
        public IRegionParticipantConfigFreezer ConfigFreezer { get; }
        public IRegionParticipantFactory Factory { get; }

        public static bool TryCreate(
            RegionParticipantTypeId participantTypeId,
            RegionParticipantModeId modeId,
            RegionCapabilitySet supportedCapabilities,
            IRegionParticipantConfigFreezer configFreezer,
            IRegionParticipantFactory factory,
            out RegionParticipantRegistration registration,
            out CoCoDiagnostic diagnostic)
        {
            registration = null;
            if (!participantTypeId.IsValid || !modeId.IsValid)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Participant registrations require valid type and mode identifiers.");
                return false;
            }

            if (supportedCapabilities == null ||
                supportedCapabilities.Count == 0)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Participant registrations require at least one supported capability.");
                return false;
            }

            if (configFreezer == null ||
                configFreezer.ConfigurationType == null ||
                !typeof(RegionParticipantConfig).IsAssignableFrom(
                    configFreezer.ConfigurationType) ||
                configFreezer.PlanType == null ||
                !typeof(IRegionParticipantPlan).IsAssignableFrom(
                    configFreezer.PlanType))
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Participant registrations require explicit config and immutable plan types.");
                return false;
            }

            if (factory == null ||
                factory.CandidateType == null ||
                !typeof(IRegionParticipantCandidate).IsAssignableFrom(
                    factory.CandidateType))
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Participant registrations require an explicit candidate type and factory.");
                return false;
            }

            registration = new RegionParticipantRegistration(
                participantTypeId,
                modeId,
                supportedCapabilities,
                configFreezer,
                factory);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class RegionParticipantCatalog
    {
        private readonly Dictionary<RegistrationKey, RegionParticipantRegistration>
            registrations =
                new Dictionary<RegistrationKey, RegionParticipantRegistration>();
        private readonly HashSet<RegionCapabilityId> capabilities =
            new HashSet<RegionCapabilityId>();
        private RegionCapabilitySet frozenCapabilities;
        private ReadOnlyCollection<Type> registeredTypes;

        public RegionParticipantCatalog()
        {
            capabilities.Add(RegionCapabilityId.Represented);
            capabilities.Add(RegionCapabilityId.Background);
            capabilities.Add(RegionCapabilityId.Enterable);
            capabilities.Add(RegionCapabilityId.Full);
        }

        public bool IsSealed { get; private set; }

        public RegionCapabilitySet SupportedCapabilities
        {
            get
            {
                EnsureCapabilitySnapshot();
                return frozenCapabilities;
            }
        }

        public IReadOnlyList<Type> RegisteredTypes
        {
            get
            {
                EnsureTypeSnapshot();
                return registeredTypes;
            }
        }

        public bool TryRegisterCapability(
            RegionCapabilityId capability,
            out CoCoDiagnostic diagnostic)
        {
            if (IsSealed)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "The Region participant catalog is sealed.");
                return false;
            }

            if (!capability.IsValid)
            {
                diagnostic = RegionErrors.InvalidCapability(
                    "Catalog capabilities must use valid stable identifiers.");
                return false;
            }

            capabilities.Add(capability);
            frozenCapabilities = null;
            registeredTypes = null;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryRegisterParticipant(
            RegionParticipantRegistration registration,
            out CoCoDiagnostic diagnostic)
        {
            if (IsSealed)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "The Region participant catalog is sealed.");
                return false;
            }

            if (registration == null)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Cannot register a null Region participant.");
                return false;
            }

            var key = new RegistrationKey(
                registration.ParticipantTypeId,
                registration.ModeId);
            if (registrations.ContainsKey(key))
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Participant type '" +
                    registration.ParticipantTypeId.Value +
                    "' and mode '" +
                    registration.ModeId.Value +
                    "' are already registered.");
                return false;
            }

            registrations.Add(key, registration);
            for (int index = 0;
                 index < registration.SupportedCapabilities.Count;
                 index++)
            {
                capabilities.Add(
                    registration.SupportedCapabilities.Capabilities[index]);
            }

            frozenCapabilities = null;
            registeredTypes = null;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryGetRegistration(
            RegionParticipantTypeId participantTypeId,
            RegionParticipantModeId modeId,
            out RegionParticipantRegistration registration)
        {
            if (!participantTypeId.IsValid || !modeId.IsValid)
            {
                registration = null;
                return false;
            }

            return registrations.TryGetValue(
                new RegistrationKey(participantTypeId, modeId),
                out registration);
        }

        public bool SupportsCapability(RegionCapabilityId capability) =>
            capability.IsValid && capabilities.Contains(capability);

        public void Seal()
        {
            EnsureCapabilitySnapshot();
            EnsureTypeSnapshot();
            IsSealed = true;
        }

        private void EnsureCapabilitySnapshot()
        {
            if (frozenCapabilities != null) return;
            if (!RegionCapabilitySet.TryCreate(
                    capabilities,
                    out frozenCapabilities))
            {
                throw new InvalidOperationException(
                    "A catalog containing only valid capabilities must be freezeable.");
            }
        }

        private void EnsureTypeSnapshot()
        {
            if (registeredTypes != null) return;

            var unique = new HashSet<Type>();
            foreach (RegionParticipantRegistration registration
                     in registrations.Values)
            {
                unique.Add(registration.ConfigFreezer.GetType());
                unique.Add(registration.ConfigFreezer.ConfigurationType);
                unique.Add(registration.ConfigFreezer.PlanType);
                unique.Add(registration.Factory.GetType());
                unique.Add(registration.Factory.CandidateType);
            }

            var ordered = new List<Type>(unique);
            ordered.Sort(
                (left, right) => string.CompareOrdinal(
                    left.AssemblyQualifiedName,
                    right.AssemblyQualifiedName));
            registeredTypes = new ReadOnlyCollection<Type>(ordered);
        }

        private readonly struct RegistrationKey : IEquatable<RegistrationKey>
        {
            internal RegistrationKey(
                RegionParticipantTypeId participantTypeId,
                RegionParticipantModeId modeId)
            {
                ParticipantTypeId = participantTypeId;
                ModeId = modeId;
            }

            private RegionParticipantTypeId ParticipantTypeId { get; }
            private RegionParticipantModeId ModeId { get; }

            public bool Equals(RegistrationKey other) =>
                ParticipantTypeId == other.ParticipantTypeId &&
                ModeId == other.ModeId;

            public override bool Equals(object obj) =>
                obj is RegistrationKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ParticipantTypeId.GetHashCode() * 397 ^
                           ModeId.GetHashCode();
                }
            }
        }
    }
}
