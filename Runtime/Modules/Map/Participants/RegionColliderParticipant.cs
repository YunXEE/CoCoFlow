using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionColliderParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool includeChildren = true;

        public bool Enabled => enabled;
        public bool IncludeChildren => includeChildren;
    }

    public static class RegionColliderParticipant
    {
        private sealed class ColliderFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionColliderParticipantConfig);

            public Type PlanType => typeof(RegionEnabledComponentPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = null;
                if (!RegionBuiltInParticipantUtilities
                        .TryValidateFragmentConfiguration(
                            context,
                            configuration,
                            ConfigurationType,
                            "Collider",
                            out diagnostic))
                {
                    return false;
                }

                var typed =
                    (RegionColliderParticipantConfig)configuration;
                plan = new RegionEnabledComponentPlan(
                    "collider",
                    typed.Enabled,
                    typed.IncludeChildren);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ColliderFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType => typeof(ColliderCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = null;
                if (!(plan is RegionEnabledComponentPlan typed) ||
                    context.FragmentResolver == null)
                {
                    diagnostic = RegionErrors.TransitionFailed(
                        "The Collider participant received an invalid plan or fragment resolver.");
                    return false;
                }

                candidate = new ColliderCandidate(
                    context.NodeId,
                    context.FragmentId,
                    context.FragmentResolver,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ColliderCandidate :
            RegionEnabledComponentCandidate<Collider>
        {
            internal ColliderCandidate(
                RegionPlanNodeId nodeId,
                string fragmentId,
                IRegionFragmentResolver resolver,
                RegionEnabledComponentPlan plan)
                : base(nodeId, fragmentId, resolver, plan)
            {
            }

            protected override bool ReadEnabled(Collider component) =>
                component.enabled;

            protected override void WriteEnabled(
                Collider component,
                bool enabled) =>
                component.enabled = enabled;

            protected override string ParticipantName => "Collider";
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.ColliderTypeId;

        public static RegionParticipantModeId ModeId =>
            RegionBuiltInParticipantUtilities.DefaultModeId;

        public static bool TryRegister(
            RegionParticipantCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            if (catalog == null)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "A participant catalog is required.");
                return false;
            }

            if (!RegionParticipantRegistration.TryCreate(
                    TypeId,
                    ModeId,
                    RegionBuiltInParticipantUtilities.StandardCapabilities,
                    new ColliderFreezer(),
                    new ColliderFactory(),
                    out RegionParticipantRegistration registration,
                    out diagnostic))
            {
                return false;
            }

            return catalog.TryRegisterParticipant(
                registration,
                out diagnostic);
        }
    }
}
