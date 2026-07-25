using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map.Pooling
{
    public static class RegionBuiltInPoolCatalog
    {
        public const string ParticipantTypeValue = "cocoflow.pool-scope";
        public const string ModeValue = "cocoflow.pool.default";

        private static readonly ReadOnlyCollection<Type> PreservedTypes =
            CreatePreservedTypes();

        public static IReadOnlyList<Type> AotTypes => PreservedTypes;

        public static bool TryRegister(
            RegionParticipantCatalog catalog,
            IRegionPoolParticipantBinding binding,
            out CoCoDiagnostic diagnostic)
        {
            if (catalog == null || binding == null)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Pool participant registration requires an explicit catalog and runtime binding.");
                return false;
            }

            if (!RegionParticipantTypeId.TryCreate(
                    ParticipantTypeValue,
                    out RegionParticipantTypeId participantTypeId) ||
                !RegionParticipantModeId.TryCreate(
                    ModeValue,
                    out RegionParticipantModeId modeId) ||
                !RegionCapabilitySet.TryCreate(
                    new[]
                    {
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable,
                        RegionCapabilityId.Full
                    },
                    out RegionCapabilitySet capabilities))
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "Built-in Pool participant identifiers or capabilities are invalid.");
                return false;
            }

            var freezer = new PoolRegionParticipantConfigFreezer();
            var factory = new PoolRegionParticipantFactory(binding);
            if (!RegionParticipantRegistration.TryCreate(
                    participantTypeId,
                    modeId,
                    capabilities,
                    freezer,
                    factory,
                    out RegionParticipantRegistration registration,
                    out diagnostic))
            {
                return false;
            }

            return catalog.TryRegisterParticipant(
                registration,
                out diagnostic);
        }

        private static ReadOnlyCollection<Type> CreatePreservedTypes()
        {
            var types = new List<Type>
            {
                typeof(RegionPoolProfileBinding),
                typeof(PoolRegionParticipantConfig),
                typeof(RegionPoolProfilePlan),
                typeof(PoolRegionParticipantPlan),
                typeof(PoolRegionParticipantConfigFreezer),
                typeof(PoolRegionParticipantFactory),
                typeof(PoolRegionParticipantCandidate)
            };
            types.Sort(
                (left, right) => string.CompareOrdinal(
                    left.AssemblyQualifiedName,
                    right.AssemblyQualifiedName));
            return new ReadOnlyCollection<Type>(types);
        }
    }
}
