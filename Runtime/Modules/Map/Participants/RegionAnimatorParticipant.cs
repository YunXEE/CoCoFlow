using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionAnimatorParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool includeChildren = true;

        public bool Enabled => enabled;
        public bool IncludeChildren => includeChildren;
    }

    public static class RegionAnimatorParticipant
    {
        private sealed class AnimatorFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionAnimatorParticipantConfig);

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
                            "Animator",
                            out diagnostic))
                {
                    return false;
                }

                var typed =
                    (RegionAnimatorParticipantConfig)configuration;
                plan = new RegionEnabledComponentPlan(
                    "animator",
                    typed.Enabled,
                    typed.IncludeChildren);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class AnimatorFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType => typeof(AnimatorCandidate);

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
                        "The Animator participant received an invalid plan or fragment resolver.");
                    return false;
                }

                candidate = new AnimatorCandidate(
                    context.NodeId,
                    context.FragmentId,
                    context.FragmentResolver,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class AnimatorCandidate :
            RegionEnabledComponentCandidate<Animator>
        {
            internal AnimatorCandidate(
                RegionPlanNodeId nodeId,
                string fragmentId,
                IRegionFragmentResolver resolver,
                RegionEnabledComponentPlan plan)
                : base(nodeId, fragmentId, resolver, plan)
            {
            }

            protected override bool ReadEnabled(Animator component) =>
                component.enabled;

            protected override void WriteEnabled(
                Animator component,
                bool enabled) =>
                component.enabled = enabled;

            protected override string ParticipantName => "Animator";
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.AnimatorTypeId;

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
                    new AnimatorFreezer(),
                    new AnimatorFactory(),
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
