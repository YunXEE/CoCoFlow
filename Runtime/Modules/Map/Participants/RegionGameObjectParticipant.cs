using System;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionGameObjectParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private bool active = true;

        public bool Active => active;
    }

    public static class RegionGameObjectParticipant
    {
        private sealed class GameObjectPlan : IRegionParticipantPlan
        {
            internal GameObjectPlan(bool active)
            {
                Active = active;
                Fingerprint = "game-object-v1|" + (active ? "1" : "0");
            }

            internal bool Active { get; }
            public string Fingerprint { get; }
        }

        private sealed class GameObjectFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionGameObjectParticipantConfig);

            public Type PlanType => typeof(GameObjectPlan);

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
                            "GameObject",
                            out diagnostic))
                {
                    return false;
                }

                var typed =
                    (RegionGameObjectParticipantConfig)configuration;
                plan = new GameObjectPlan(typed.Active);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class GameObjectFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType => typeof(GameObjectCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = null;
                if (!(plan is GameObjectPlan typed) ||
                    context.FragmentResolver == null)
                {
                    diagnostic = RegionErrors.TransitionFailed(
                        "The GameObject participant received an invalid plan or fragment resolver.");
                    return false;
                }

                candidate = new GameObjectCandidate(
                    context.NodeId,
                    context.FragmentId,
                    context.FragmentResolver,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class GameObjectCandidate :
            IRegionParticipantCandidate,
            IRegionParticipantTerminalCleanup
        {
            private readonly RegionPlanNodeId nodeId;
            private readonly string fragmentId;
            private readonly IRegionFragmentResolver resolver;
            private readonly GameObjectPlan plan;
            private GameObject target;
            private bool originalActive;
            private bool prepared;
            private bool committed;
            private bool cleaned;

            internal GameObjectCandidate(
                RegionPlanNodeId nodeId,
                string fragmentId,
                IRegionFragmentResolver resolver,
                GameObjectPlan plan)
            {
                this.nodeId = nodeId;
                this.fragmentId = fragmentId;
                this.resolver = resolver;
                this.plan = plan;
            }

            public UniTask<RegionParticipantPrepareResult> PrepareAsync(
                in RegionParticipantPrepareContext context,
                CancellationToken cancellationToken)
            {
                if (cleaned ||
                    prepared ||
                    context.NodeId != nodeId ||
                    cancellationToken.IsCancellationRequested)
                {
                    return UniTask.FromResult(
                        RegionParticipantPrepareResult.Failure(
                            RegionErrors.TransitionFailed(
                                "The GameObject participant Prepare request is invalid or cancelled.")));
                }

                if (!RegionBuiltInParticipantUtilities.TryResolveGameObject(
                        resolver,
                        fragmentId,
                        out target,
                        out CoCoDiagnostic diagnostic))
                {
                    return UniTask.FromResult(
                        RegionParticipantPrepareResult.Failure(diagnostic));
                }

                originalActive = target.activeSelf;
                prepared = true;
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Success());
            }

            public bool TryCommit(
                in RegionParticipantCommitContext context,
                out CoCoDiagnostic diagnostic)
            {
                if (!prepared || cleaned || target == null)
                {
                    diagnostic = RegionErrors.CommitFaulted(
                        "The GameObject participant lost its prepared target before commit.");
                    return false;
                }

                try
                {
                    target.SetActive(plan.Active);
                    committed = true;
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.CommitFaulted(
                        "The GameObject participant commit threw: " +
                        exception.Message);
                    return false;
                }
            }

            public UniTask<RegionParticipantCleanupResult> CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(
                    CleanupNoThrow(
                        reason ==
                        RegionParticipantCleanupReason.Replaced));

            public void ForceCleanupNoFail()
            {
                CleanupNoThrow(false);
            }

            private RegionParticipantCleanupResult CleanupNoThrow(
                bool preserveCommittedState)
            {
                if (cleaned)
                {
                    return RegionParticipantCleanupResult.Success();
                }

                try
                {
                    if (committed &&
                        !preserveCommittedState &&
                        target != null)
                    {
                        target.SetActive(originalActive);
                    }

                    target = null;
                    prepared = false;
                    committed = false;
                    cleaned = true;
                    return RegionParticipantCleanupResult.Success();
                }
                catch (Exception exception)
                {
                    return RegionParticipantCleanupResult.Failure(
                        RegionErrors.CleanupBlocked(
                            "The GameObject participant could not restore its target: " +
                            exception.Message));
                }
            }
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.GameObjectTypeId;

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
                    new GameObjectFreezer(),
                    new GameObjectFactory(),
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

    internal sealed class RegionEnabledComponentPlan :
        IRegionParticipantPlan
    {
        internal RegionEnabledComponentPlan(
            string kind,
            bool enabled,
            bool includeChildren)
        {
            Enabled = enabled;
            IncludeChildren = includeChildren;
            Fingerprint =
                (kind ?? string.Empty) + "-v1|" +
                (enabled ? "1" : "0") + "|" +
                (includeChildren ? "1" : "0");
        }

        internal bool Enabled { get; }
        internal bool IncludeChildren { get; }
        public string Fingerprint { get; }
    }

    internal abstract class RegionEnabledComponentCandidate<T> :
        IRegionParticipantCandidate,
        IRegionParticipantTerminalCleanup
        where T : Component
    {
        private readonly RegionPlanNodeId nodeId;
        private readonly string fragmentId;
        private readonly IRegionFragmentResolver resolver;
        private readonly RegionEnabledComponentPlan plan;
        private T[] components = Array.Empty<T>();
        private bool[] originalValues = Array.Empty<bool>();
        private bool prepared;
        private bool committed;
        private bool cleaned;

        protected RegionEnabledComponentCandidate(
            RegionPlanNodeId nodeId,
            string fragmentId,
            IRegionFragmentResolver resolver,
            RegionEnabledComponentPlan plan)
        {
            this.nodeId = nodeId;
            this.fragmentId = fragmentId;
            this.resolver = resolver;
            this.plan = plan;
        }

        protected abstract bool ReadEnabled(T component);
        protected abstract void WriteEnabled(T component, bool enabled);
        protected abstract string ParticipantName { get; }

        public UniTask<RegionParticipantPrepareResult> PrepareAsync(
            in RegionParticipantPrepareContext context,
            CancellationToken cancellationToken)
        {
            if (cleaned ||
                prepared ||
                context.NodeId != nodeId ||
                cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Failure(
                        RegionErrors.TransitionFailed(
                            "The " + ParticipantName +
                            " participant Prepare request is invalid or cancelled.")));
            }

            if (!RegionBuiltInParticipantUtilities.TryResolveComponents(
                    resolver,
                    fragmentId,
                    plan.IncludeChildren,
                    out components,
                    out CoCoDiagnostic diagnostic))
            {
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Failure(diagnostic));
            }

            try
            {
                originalValues = new bool[components.Length];
                for (int index = 0; index < components.Length; index++)
                {
                    if (components[index] == null)
                    {
                        return UniTask.FromResult(
                            RegionParticipantPrepareResult.Failure(
                                RegionErrors.SceneContract(
                                    "The " + ParticipantName +
                                    " participant resolved a missing component.")));
                    }

                    originalValues[index] =
                        ReadEnabled(components[index]);
                }

                prepared = true;
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Success());
            }
            catch (Exception exception)
            {
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Failure(
                        RegionErrors.TransitionFailed(
                            "The " + ParticipantName +
                            " participant could not capture its target state: " +
                            exception.Message)));
            }
        }

        public bool TryCommit(
            in RegionParticipantCommitContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (!prepared || cleaned)
            {
                diagnostic = RegionErrors.CommitFaulted(
                    "The " + ParticipantName +
                    " participant was not prepared before commit.");
                return false;
            }

            try
            {
                for (int index = 0; index < components.Length; index++)
                {
                    if (components[index] == null)
                    {
                        diagnostic = RegionErrors.CommitFaulted(
                            "The " + ParticipantName +
                            " participant lost a target before commit.");
                        return false;
                    }

                    WriteEnabled(components[index], plan.Enabled);
                }

                committed = true;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.CommitFaulted(
                    "The " + ParticipantName +
                    " participant commit threw: " +
                    exception.Message);
                return false;
            }
        }

        public UniTask<RegionParticipantCleanupResult> CleanupAsync(
            RegionParticipantCleanupReason reason,
            CancellationToken cancellationToken) =>
            UniTask.FromResult(
                CleanupNoThrow(
                    reason ==
                    RegionParticipantCleanupReason.Replaced));

        public void ForceCleanupNoFail()
        {
            CleanupNoThrow(false);
        }

        private RegionParticipantCleanupResult CleanupNoThrow(
            bool preserveCommittedState)
        {
            if (cleaned)
            {
                return RegionParticipantCleanupResult.Success();
            }

            try
            {
                if (committed && !preserveCommittedState)
                {
                    int count = Math.Min(
                        components.Length,
                        originalValues.Length);
                    for (int index = 0; index < count; index++)
                    {
                        if (components[index] != null)
                        {
                            WriteEnabled(
                                components[index],
                                originalValues[index]);
                        }
                    }
                }

                components = Array.Empty<T>();
                originalValues = Array.Empty<bool>();
                prepared = false;
                committed = false;
                cleaned = true;
                return RegionParticipantCleanupResult.Success();
            }
            catch (Exception exception)
            {
                return RegionParticipantCleanupResult.Failure(
                    RegionErrors.CleanupBlocked(
                        "The " + ParticipantName +
                        " participant could not restore its targets: " +
                        exception.Message));
            }
        }
    }
}
