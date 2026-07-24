using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionBehaviourParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool includeChildren = true;

        public bool Enabled => enabled;
        public bool IncludeChildren => includeChildren;
    }

    public static class RegionBehaviourParticipant
    {
        private sealed class BehaviourFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionBehaviourParticipantConfig);

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
                            "Behaviour",
                            out diagnostic))
                {
                    return false;
                }

                var typed =
                    (RegionBehaviourParticipantConfig)configuration;
                plan = new RegionEnabledComponentPlan(
                    "behaviour",
                    typed.Enabled,
                    typed.IncludeChildren);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class BehaviourFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType => typeof(BehaviourCandidate);

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
                        "The Behaviour participant received an invalid plan or fragment resolver.");
                    return false;
                }

                candidate = new BehaviourCandidate(
                    context.NodeId,
                    context.FragmentId,
                    context.FragmentResolver,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class BehaviourCandidate :
            RegionEnabledComponentCandidate<Behaviour>
        {
            internal BehaviourCandidate(
                RegionPlanNodeId nodeId,
                string fragmentId,
                IRegionFragmentResolver resolver,
                RegionEnabledComponentPlan plan)
                : base(nodeId, fragmentId, resolver, plan)
            {
            }

            protected override bool ReadEnabled(Behaviour component) =>
                component.enabled;

            protected override void WriteEnabled(
                Behaviour component,
                bool enabled) =>
                component.enabled = enabled;

            protected override string ParticipantName => "Behaviour";
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.BehaviourTypeId;

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
                    new BehaviourFreezer(),
                    new BehaviourFactory(),
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
