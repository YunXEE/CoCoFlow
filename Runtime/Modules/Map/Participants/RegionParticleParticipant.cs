using System;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    public enum RegionParticleAction
    {
        Play = 0,
        Pause = 1,
        Stop = 2
    }

    [Serializable]
    public sealed class RegionParticleParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private RegionParticleAction action =
            RegionParticleAction.Play;
        [SerializeField] private bool includeChildren = true;
        [SerializeField] private bool clearOnStop;

        public RegionParticleAction Action => action;
        public bool IncludeChildren => includeChildren;
        public bool ClearOnStop => clearOnStop;
    }

    public static class RegionParticleParticipant
    {
        private sealed class ParticlePlan : IRegionParticipantPlan
        {
            internal ParticlePlan(
                RegionParticleAction action,
                bool includeChildren,
                bool clearOnStop)
            {
                Action = action;
                IncludeChildren = includeChildren;
                ClearOnStop = clearOnStop;
                Fingerprint =
                    "particle-v1|" + (int)action + "|" +
                    (includeChildren ? "1" : "0") + "|" +
                    (clearOnStop ? "1" : "0");
            }

            internal RegionParticleAction Action { get; }
            internal bool IncludeChildren { get; }
            internal bool ClearOnStop { get; }
            public string Fingerprint { get; }
        }

        private sealed class ParticleFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionParticleParticipantConfig);

            public Type PlanType => typeof(ParticlePlan);

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
                            "Particle",
                            out diagnostic))
                {
                    return false;
                }

                var typed =
                    (RegionParticleParticipantConfig)configuration;
                if (!Enum.IsDefined(
                        typeof(RegionParticleAction),
                        typed.Action))
                {
                    diagnostic = RegionErrors.InvalidProfile(
                        "The Particle participant action is invalid.");
                    return false;
                }

                plan = new ParticlePlan(
                    typed.Action,
                    typed.IncludeChildren,
                    typed.ClearOnStop);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ParticleFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType => typeof(ParticleCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = null;
                if (!(plan is ParticlePlan typed) ||
                    context.FragmentResolver == null)
                {
                    diagnostic = RegionErrors.TransitionFailed(
                        "The Particle participant received an invalid plan or fragment resolver.");
                    return false;
                }

                candidate = new ParticleCandidate(
                    context.NodeId,
                    context.FragmentId,
                    context.FragmentResolver,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ParticleCandidate :
            IRegionParticipantCandidate,
            IRegionParticipantTerminalCleanup
        {
            private enum OriginalState
            {
                Stopped = 0,
                Playing = 1,
                Paused = 2
            }

            private readonly RegionPlanNodeId nodeId;
            private readonly string fragmentId;
            private readonly IRegionFragmentResolver resolver;
            private readonly ParticlePlan plan;
            private ParticleSystem[] systems = Array.Empty<ParticleSystem>();
            private OriginalState[] originalStates =
                Array.Empty<OriginalState>();
            private bool prepared;
            private bool restoreRequired;
            private bool cleaned;

            internal ParticleCandidate(
                RegionPlanNodeId nodeId,
                string fragmentId,
                IRegionFragmentResolver resolver,
                ParticlePlan plan)
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
                                "The Particle participant Prepare request is invalid or cancelled.")));
                }

                if (!RegionBuiltInParticipantUtilities.TryResolveComponents(
                        resolver,
                        fragmentId,
                        plan.IncludeChildren,
                        out systems,
                        out CoCoDiagnostic diagnostic))
                {
                    return UniTask.FromResult(
                        RegionParticipantPrepareResult.Failure(diagnostic));
                }

                try
                {
                    originalStates = new OriginalState[systems.Length];
                    for (int index = 0; index < systems.Length; index++)
                    {
                        ParticleSystem system = systems[index];
                        if (system == null)
                        {
                            return UniTask.FromResult(
                                RegionParticipantPrepareResult.Failure(
                                    RegionErrors.SceneContract(
                                        "The Particle participant resolved a missing ParticleSystem.")));
                        }

                        originalStates[index] = system.isPaused
                            ? OriginalState.Paused
                            : system.isPlaying
                                ? OriginalState.Playing
                                : OriginalState.Stopped;
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
                                "The Particle participant could not capture its target state: " +
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
                        "The Particle participant was not prepared before commit.");
                    return false;
                }

                try
                {
                    for (int index = 0; index < systems.Length; index++)
                    {
                        if (systems[index] == null)
                        {
                            diagnostic = RegionErrors.CommitFaulted(
                                "The Particle participant lost a target before commit.");
                            return false;
                        }
                    }

                    restoreRequired = true;
                    for (int index = 0; index < systems.Length; index++)
                    {
                        ApplyAction(
                            systems[index],
                            plan.Action,
                            plan.ClearOnStop);
                    }

                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.CommitFaulted(
                        "The Particle participant commit threw: " +
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
                    if (restoreRequired && !preserveCommittedState)
                    {
                        int count = Math.Min(
                            systems.Length,
                            originalStates.Length);
                        for (int index = 0; index < count; index++)
                        {
                            ParticleSystem system = systems[index];
                            if (system == null) continue;

                            switch (originalStates[index])
                            {
                                case OriginalState.Playing:
                                    system.Play(false);
                                    break;
                                case OriginalState.Paused:
                                    system.Pause(false);
                                    break;
                                default:
                                    system.Stop(
                                        false,
                                        ParticleSystemStopBehavior
                                            .StopEmitting);
                                    break;
                            }
                        }
                    }

                    cleaned = true;
                    systems = Array.Empty<ParticleSystem>();
                    originalStates = Array.Empty<OriginalState>();
                    prepared = false;
                    restoreRequired = false;
                    return RegionParticipantCleanupResult.Success();
                }
                catch (Exception exception)
                {
                    return RegionParticipantCleanupResult.Failure(
                        RegionErrors.CleanupBlocked(
                            "The Particle participant could not restore its targets: " +
                            exception.Message));
                }
            }

            private static void ApplyAction(
                ParticleSystem system,
                RegionParticleAction action,
                bool clearOnStop)
            {
                switch (action)
                {
                    case RegionParticleAction.Play:
                        system.Play(false);
                        break;
                    case RegionParticleAction.Pause:
                        system.Pause(false);
                        break;
                    case RegionParticleAction.Stop:
                        system.Stop(
                            false,
                            clearOnStop
                                ? ParticleSystemStopBehavior
                                    .StopEmittingAndClear
                                : ParticleSystemStopBehavior
                                    .StopEmitting);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(action),
                            action,
                            "Unsupported Region particle action.");
                }
            }
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.ParticleTypeId;

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
                    new ParticleFreezer(),
                    new ParticleFactory(),
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
