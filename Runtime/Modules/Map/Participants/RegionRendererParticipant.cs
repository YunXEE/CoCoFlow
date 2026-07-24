using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionRendererParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool includeChildren = true;

        public bool Enabled => enabled;
        public bool IncludeChildren => includeChildren;
    }

    public static class RegionRendererParticipant
    {
        private sealed class RendererFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionRendererParticipantConfig);

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
                            "Renderer",
                            out diagnostic))
                {
                    return false;
                }

                var typed =
                    (RegionRendererParticipantConfig)configuration;
                plan = new RegionEnabledComponentPlan(
                    "renderer",
                    typed.Enabled,
                    typed.IncludeChildren);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class RendererFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType => typeof(RendererCandidate);

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
                        "The Renderer participant received an invalid plan or fragment resolver.");
                    return false;
                }

                candidate = new RendererCandidate(
                    context.NodeId,
                    context.FragmentId,
                    context.FragmentResolver,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class RendererCandidate :
            RegionEnabledComponentCandidate<Renderer>
        {
            internal RendererCandidate(
                RegionPlanNodeId nodeId,
                string fragmentId,
                IRegionFragmentResolver resolver,
                RegionEnabledComponentPlan plan)
                : base(nodeId, fragmentId, resolver, plan)
            {
            }

            protected override bool ReadEnabled(Renderer component) =>
                component.enabled;

            protected override void WriteEnabled(
                Renderer component,
                bool enabled) =>
                component.enabled = enabled;

            protected override string ParticipantName => "Renderer";
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.RendererTypeId;

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
                    new RendererFreezer(),
                    new RendererFactory(),
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
