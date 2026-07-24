using System;
using System.Threading;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Fixtures.ExternalMapTa
{
    [Serializable]
    public sealed class ExternalWeatherParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField, Min(1)]
        private int simulationBudget = 64;

        public int SimulationBudget =>
            simulationBudget;
    }

    public sealed class ExternalWeatherParticipantPlan :
        IRegionParticipantPlan
    {
        public ExternalWeatherParticipantPlan(
            int simulationBudget)
        {
            SimulationBudget = simulationBudget;
            Fingerprint =
                "external-weather-v1|" +
                simulationBudget;
        }

        public int SimulationBudget { get; }
        public string Fingerprint { get; }
    }

    public sealed class ExternalWeatherConfigFreezer :
        IRegionParticipantConfigFreezer
    {
        public Type ConfigurationType =>
            typeof(ExternalWeatherParticipantConfig);

        public Type PlanType =>
            typeof(ExternalWeatherParticipantPlan);

        public bool TryFreeze(
            in RegionParticipantFreezeContext context,
            RegionParticipantConfig configuration,
            out IRegionParticipantPlan plan,
            out CoCoDiagnostic diagnostic)
        {
            plan = null;
            if (!(configuration is
                    ExternalWeatherParticipantConfig typed) ||
                typed.SimulationBudget <= 0)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Map,
                    CoCoDiagnosticCode.InvalidRegionProfile,
                    "External weather simulation requires a positive budget.");
                return false;
            }

            plan = new ExternalWeatherParticipantPlan(
                typed.SimulationBudget);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class ExternalWeatherParticipantFactory :
        IRegionParticipantFactory
    {
        public Type CandidateType =>
            typeof(ExternalWeatherParticipantCandidate);

        public bool TryCreateCandidate(
            in RegionParticipantCreateContext context,
            IRegionParticipantPlan plan,
            out IRegionParticipantCandidate candidate,
            out CoCoDiagnostic diagnostic)
        {
            candidate = null;
            if (!(plan is
                    ExternalWeatherParticipantPlan typed) ||
                !context.NodeId.IsValid)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Map,
                    CoCoDiagnosticCode.RegionTransitionFailed,
                    "External weather received an invalid plan or node identity.");
                return false;
            }

            candidate =
                new ExternalWeatherParticipantCandidate(
                    context.NodeId,
                    typed);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class ExternalWeatherParticipantCandidate :
        IRegionParticipantCandidate
    {
        private readonly RegionPlanNodeId nodeId;
        private readonly ExternalWeatherParticipantPlan plan;
        private bool prepared;
        private bool cleaned;

        public ExternalWeatherParticipantCandidate(
            RegionPlanNodeId nodeId,
            ExternalWeatherParticipantPlan plan)
        {
            this.nodeId = nodeId;
            this.plan = plan;
        }

        public UniTask<RegionParticipantPrepareResult>
            PrepareAsync(
                in RegionParticipantPrepareContext context,
                CancellationToken cancellationToken)
        {
            if (cleaned ||
                prepared ||
                context.NodeId != nodeId ||
                plan == null ||
                cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Failure(
                        CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Map,
                            CoCoDiagnosticCode.RegionTransitionFailed,
                            "External weather Prepare was invalid or cancelled.")));
            }

            prepared = true;
            return UniTask.FromResult(
                RegionParticipantPrepareResult.Success());
        }

        public bool TryCommit(
            in RegionParticipantCommitContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (!prepared ||
                cleaned ||
                context.NodeId != nodeId)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Map,
                    CoCoDiagnosticCode.RegionCommitFaulted,
                    "External weather lost its prepared candidate before commit.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public UniTask<RegionParticipantCleanupResult>
            CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken)
        {
            cleaned = true;
            prepared = false;
            return UniTask.FromResult(
                RegionParticipantCleanupResult.Success());
        }
    }

    public static class ExternalWeatherParticipant
    {
        public static RegionCapabilityId CapabilityId
        {
            get
            {
                RegionCapabilityId.TryCreate(
                    "ta.world-response.weather",
                    out RegionCapabilityId id);
                return id;
            }
        }

        public static RegionParticipantTypeId TypeId
        {
            get
            {
                RegionParticipantTypeId.TryCreate(
                    "ta.world-response.weather",
                    out RegionParticipantTypeId id);
                return id;
            }
        }

        public static RegionParticipantModeId ModeId
        {
            get
            {
                RegionParticipantModeId.TryCreate(
                    "ta.world-response.default",
                    out RegionParticipantModeId id);
                return id;
            }
        }

        public static bool TryRegister(
            RegionParticipantCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (catalog == null ||
                !catalog.TryRegisterCapability(
                    CapabilityId,
                    out diagnostic) ||
                !RegionCapabilitySet.TryCreate(
                    new[] { CapabilityId },
                    out RegionCapabilitySet capabilities) ||
                !RegionParticipantRegistration.TryCreate(
                    TypeId,
                    ModeId,
                    capabilities,
                    new ExternalWeatherConfigFreezer(),
                    new ExternalWeatherParticipantFactory(),
                    out RegionParticipantRegistration
                        registration,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Map,
                        CoCoDiagnosticCode.RegionCatalogConflict,
                        "External weather registration could not be created.");
                }

                return false;
            }

            return catalog.TryRegisterParticipant(
                registration,
                out diagnostic);
        }
    }
}
