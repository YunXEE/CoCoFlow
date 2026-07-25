using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map.Pooling
{
    [Serializable]
    public sealed class RegionPoolProfileBinding
    {
        [SerializeField] private PoolId poolId;
        [SerializeField] private string profileBindingId = string.Empty;
        [SerializeField, Min(0)] private int prewarmCount;
        [SerializeField, Min(0)] private int maxRetained;

        public PoolId PoolId => poolId;
        public string ProfileBindingId => profileBindingId ?? string.Empty;
        public int PrewarmCount => prewarmCount;
        public int MaxRetained => maxRetained;
    }

    [Serializable]
    public sealed class PoolRegionParticipantConfig :
        RegionParticipantConfig
    {
        [SerializeField] private List<RegionPoolProfileBinding> profiles =
            new List<RegionPoolProfileBinding>();

        public IReadOnlyList<RegionPoolProfileBinding> Profiles =>
            profiles ??
            (IReadOnlyList<RegionPoolProfileBinding>)
            Array.Empty<RegionPoolProfileBinding>();
    }

    public readonly struct RegionPoolProfilePlan :
        IEquatable<RegionPoolProfilePlan>
    {
        internal RegionPoolProfilePlan(
            PoolId poolId,
            string profileBindingId,
            int prewarmCount,
            int maxRetained)
        {
            PoolId = poolId;
            ProfileBindingId = profileBindingId ?? string.Empty;
            PrewarmCount = prewarmCount;
            MaxRetained = maxRetained;
        }

        public PoolId PoolId { get; }
        public string ProfileBindingId { get; }
        public int PrewarmCount { get; }
        public int MaxRetained { get; }
        public bool IsValid =>
            PoolId.IsValid &&
            !string.IsNullOrWhiteSpace(ProfileBindingId) &&
            string.Equals(
                ProfileBindingId,
                ProfileBindingId.Trim(),
                StringComparison.Ordinal) &&
            PrewarmCount >= 0 &&
            MaxRetained >= 0 &&
            PrewarmCount <= MaxRetained;

        public bool Equals(RegionPoolProfilePlan other) =>
            PoolId == other.PoolId &&
            string.Equals(
                ProfileBindingId,
                other.ProfileBindingId,
                StringComparison.Ordinal) &&
            PrewarmCount == other.PrewarmCount &&
            MaxRetained == other.MaxRetained;

        public override bool Equals(object obj) =>
            obj is RegionPoolProfilePlan other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = PoolId.GetHashCode();
                hashCode = hashCode * 397 ^
                           StringComparer.Ordinal.GetHashCode(ProfileBindingId);
                hashCode = hashCode * 397 ^ PrewarmCount;
                hashCode = hashCode * 397 ^ MaxRetained;
                return hashCode;
            }
        }

        public static bool operator ==(
            RegionPoolProfilePlan left,
            RegionPoolProfilePlan right) => left.Equals(right);

        public static bool operator !=(
            RegionPoolProfilePlan left,
            RegionPoolProfilePlan right) => !left.Equals(right);
    }

    public sealed class PoolRegionParticipantPlan : IRegionParticipantPlan
    {
        private readonly RegionImmutableArray<RegionPoolProfilePlan> profiles;

        internal PoolRegionParticipantPlan(
            IList<RegionPoolProfilePlan> profiles,
            string fingerprint)
        {
            this.profiles = profiles == null
                ? RegionImmutableArray<RegionPoolProfilePlan>.Empty
                : new RegionImmutableArray<RegionPoolProfilePlan>(
                    profiles);
            Fingerprint = fingerprint ?? string.Empty;
        }

        public IReadOnlyList<RegionPoolProfilePlan> Profiles => profiles;
        public string Fingerprint { get; }
    }

    public interface IRegionPoolParticipantBinding
    {
        bool TryGetPoolRuntime(
            RegionPlanNodeId nodeId,
            out PoolRuntime runtime,
            out CoCoDiagnostic diagnostic);

        bool TryCreateCandidateScope(
            RegionPlanNodeId nodeId,
            long candidateSequence,
            out PoolScope scope,
            out CoCoDiagnostic diagnostic);

        bool TryResolveProfile(
            in RegionPoolProfilePlan profilePlan,
            out PoolProfile profile,
            out CoCoDiagnostic diagnostic);

        bool TryPublishCommittedScope(
            RegionPlanNodeId nodeId,
            PoolScope scope,
            out CoCoDiagnostic diagnostic);

        bool TryReleaseCommittedScope(
            RegionPlanNodeId nodeId,
            PoolScope expectedScope,
            out CoCoDiagnostic diagnostic);
    }

    public sealed class PoolRegionParticipantConfigFreezer :
        IRegionParticipantConfigFreezer,
        IRegionRequiresOwningContentDependency
    {
        public Type ConfigurationType => typeof(PoolRegionParticipantConfig);
        public Type PlanType => typeof(PoolRegionParticipantPlan);

        public bool TryFreeze(
            in RegionParticipantFreezeContext context,
            RegionParticipantConfig configuration,
            out IRegionParticipantPlan plan,
            out CoCoDiagnostic diagnostic)
        {
            plan = null;
            if (!(configuration is PoolRegionParticipantConfig poolConfig))
            {
                diagnostic = RegionErrors.InvalidProfile(
                    "The Pool participant requires PoolRegionParticipantConfig.");
                return false;
            }

            if (!string.IsNullOrEmpty(context.FragmentId) ||
                context.SceneReference.IsValid)
            {
                diagnostic = RegionErrors.InvalidProfile(
                    "The Pool participant is Region/Chunk scoped but does not bind a Scene fragment or own the Chunk Scene.");
                return false;
            }

            if (poolConfig.Profiles.Count == 0)
            {
                diagnostic = RegionErrors.InvalidProfile(
                    "The Pool participant requires at least one profile binding.");
                return false;
            }

            var profilePlans =
                new List<RegionPoolProfilePlan>(poolConfig.Profiles.Count);
            var poolIds = new HashSet<PoolId>();
            for (int index = 0; index < poolConfig.Profiles.Count; index++)
            {
                RegionPoolProfileBinding profile = poolConfig.Profiles[index];
                if (profile == null)
                {
                    diagnostic = RegionErrors.InvalidProfile(
                        "Pool profile bindings cannot be null.");
                    return false;
                }

                var profilePlan = new RegionPoolProfilePlan(
                    profile.PoolId,
                    profile.ProfileBindingId,
                    profile.PrewarmCount,
                    profile.MaxRetained);
                if (!profilePlan.IsValid)
                {
                    diagnostic = RegionErrors.InvalidProfile(
                        "Pool profile bindings require a PoolId, stable binding id, and 0 <= prewarm <= max retained.");
                    return false;
                }

                if (!poolIds.Add(profilePlan.PoolId))
                {
                    diagnostic = RegionErrors.InvalidProfile(
                        "PoolId '" + profilePlan.PoolId.Value +
                        "' is duplicated in one Region participant.");
                    return false;
                }

                profilePlans.Add(profilePlan);
            }

            profilePlans.Sort(
                (left, right) => string.CompareOrdinal(
                    left.PoolId.Value,
                    right.PoolId.Value));
            plan = new PoolRegionParticipantPlan(
                profilePlans,
                BuildFingerprint(profilePlans));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static string BuildFingerprint(
            IReadOnlyList<RegionPoolProfilePlan> profiles)
        {
            var builder = new DeterministicHash();
            builder.Append("cocoflow.map.pool-plan.v1");
            for (int index = 0; index < profiles.Count; index++)
            {
                builder.Append(profiles[index].PoolId.Value);
                builder.Append(profiles[index].ProfileBindingId);
                builder.Append(profiles[index].PrewarmCount);
                builder.Append(profiles[index].MaxRetained);
            }

            return builder.Complete();
        }

        private struct DeterministicHash
        {
            private const ulong Offset = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;
            private ulong hash;
            private bool initialized;

            internal void Append(string value)
            {
                EnsureInitialized();
                string safe = value ?? string.Empty;
                AddInt32(safe.Length);
                for (int index = 0; index < safe.Length; index++)
                {
                    char character = safe[index];
                    AddByte((byte)character);
                    AddByte((byte)(character >> 8));
                }
            }

            internal void Append(int value) =>
                Append(value.ToString(CultureInfo.InvariantCulture));

            internal string Complete()
            {
                EnsureInitialized();
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }

            private void AddInt32(int value)
            {
                AddByte((byte)value);
                AddByte((byte)(value >> 8));
                AddByte((byte)(value >> 16));
                AddByte((byte)(value >> 24));
            }

            private void AddByte(byte value)
            {
                hash ^= value;
                hash *= Prime;
            }

            private void EnsureInitialized()
            {
                if (initialized) return;
                hash = Offset;
                initialized = true;
            }
        }
    }

    public sealed class PoolRegionParticipantFactory :
        IRegionParticipantFactory
    {
        private readonly IRegionPoolParticipantBinding binding;
        private long nextCandidateSequence;

        public PoolRegionParticipantFactory(
            IRegionPoolParticipantBinding binding)
        {
            this.binding = binding ??
                           throw new ArgumentNullException(nameof(binding));
        }

        public Type CandidateType => typeof(PoolRegionParticipantCandidate);

        public bool TryCreateCandidate(
            in RegionParticipantCreateContext context,
            IRegionParticipantPlan plan,
            out IRegionParticipantCandidate candidate,
            out CoCoDiagnostic diagnostic)
        {
            candidate = null;
            if (!context.NodeId.IsValid ||
                !(plan is PoolRegionParticipantPlan poolPlan))
            {
                diagnostic = RegionErrors.CompilationFailed(
                    "The Pool participant factory requires a valid node and immutable Pool plan.");
                return false;
            }

            long sequence = Interlocked.Increment(ref nextCandidateSequence);
            if (sequence <= 0L)
            {
                diagnostic = RegionErrors.TransitionFailed(
                    "Pool participant candidate sequence exhausted.");
                return false;
            }

            candidate = new PoolRegionParticipantCandidate(
                context.NodeId,
                sequence,
                poolPlan,
                binding);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class PoolRegionParticipantCandidate :
        IRegionParticipantCandidate,
        IRegionParticipantTerminalCleanup
    {
        private readonly RegionPlanNodeId nodeId;
        private readonly long candidateSequence;
        private readonly PoolRegionParticipantPlan plan;
        private readonly IRegionPoolParticipantBinding binding;
        private readonly object cleanupGate = new object();
        private PoolRuntime runtime;
        private PoolScope scope;
        private Task<RegionParticipantCleanupResult> cleanupTask;
        private bool prepareStarted;
        private bool prepared;
        private bool committed;
        private bool published;
        private bool terminalForced;

        internal PoolRegionParticipantCandidate(
            RegionPlanNodeId nodeId,
            long candidateSequence,
            PoolRegionParticipantPlan plan,
            IRegionPoolParticipantBinding binding)
        {
            this.nodeId = nodeId;
            this.candidateSequence = candidateSequence;
            this.plan = plan;
            this.binding = binding;
        }

        public UniTask<RegionParticipantPrepareResult> PrepareAsync(
            in RegionParticipantPrepareContext context,
            CancellationToken cancellationToken)
        {
            if (prepareStarted ||
                context.NodeId != nodeId ||
                terminalForced)
            {
                return UniTask.FromResult(
                    RegionParticipantPrepareResult.Failure(
                        RegionErrors.TransitionFailed(
                            "The Pool participant candidate cannot prepare more than once or for another node.")));
            }

            prepareStarted = true;
            return PrepareCoreAsync(context, cancellationToken);
        }

        private async UniTask<RegionParticipantPrepareResult> PrepareCoreAsync(
            RegionParticipantPrepareContext context,
            CancellationToken cancellationToken)
        {
            if (!binding.TryGetPoolRuntime(
                    nodeId,
                    out runtime,
                    out CoCoDiagnostic diagnostic) ||
                runtime == null ||
                runtime.IsShuttingDown ||
                runtime.IsDisposed)
            {
                return RegionParticipantPrepareResult.Failure(
                    diagnostic.IsNone
                        ? RegionErrors.TransitionFailed(
                            "A live explicitly bound PoolRuntime is required.")
                        : diagnostic);
            }

            if (!binding.TryCreateCandidateScope(
                    nodeId,
                    candidateSequence,
                    out scope,
                    out diagnostic) ||
                scope == null ||
                !runtime.Owns(scope) ||
                scope.State != PoolScopeState.Open)
            {
                return RegionParticipantPrepareResult.Failure(
                    diagnostic.IsNone
                        ? RegionErrors.TransitionFailed(
                            "The Pool binding did not create an open Scope owned by its bound Runtime.")
                        : diagnostic);
            }

            for (int index = 0; index < plan.Profiles.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return RegionParticipantPrepareResult.Failure(
                        RegionErrors.TransitionFailed(
                            "Pool participant preparation was cancelled."));
                }

                RegionPoolProfilePlan profilePlan = plan.Profiles[index];
                if (!binding.TryResolveProfile(
                        profilePlan,
                        out PoolProfile profile,
                        out diagnostic) ||
                    !ProfileMatches(profilePlan, profile))
                {
                    return RegionParticipantPrepareResult.Failure(
                        diagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "Pool binding '" +
                                profilePlan.ProfileBindingId +
                                "' did not resolve the exact compiled PoolProfile.")
                            : diagnostic);
                }

                PoolPrepareResult prepare = await scope.PrepareAsync(
                    profile,
                    cancellationToken);
                if (!prepare.Succeeded)
                {
                    return RegionParticipantPrepareResult.Failure(
                        prepare.Diagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "Pool '" + profile.Id.Value +
                                "' failed to prepare.")
                            : prepare.Diagnostic);
                }

                PoolPrewarmResult prewarm = await scope.PrewarmAsync(
                    profile.Id,
                    cancellationToken);
                if (!prewarm.Succeeded)
                {
                    return RegionParticipantPrepareResult.Failure(
                        prewarm.Diagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "Pool '" + profile.Id.Value +
                                "' failed to prewarm.")
                            : prewarm.Diagnostic);
                }
            }

            prepared = true;
            return RegionParticipantPrepareResult.Success();
        }

        public bool TryCommit(
            in RegionParticipantCommitContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (context.NodeId != nodeId ||
                !prepared ||
                committed ||
                terminalForced ||
                scope == null ||
                scope.State != PoolScopeState.Open)
            {
                diagnostic = RegionErrors.TransitionFailed(
                    "Only one prepared live Pool participant candidate can commit.");
                return false;
            }

            if (!binding.TryPublishCommittedScope(
                    nodeId,
                    scope,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TransitionFailed(
                        "The Pool binding rejected the committed Scope.");
                }

                return false;
            }

            published = true;
            committed = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public UniTask<RegionParticipantCleanupResult> CleanupAsync(
            RegionParticipantCleanupReason reason,
            CancellationToken cancellationToken)
        {
            lock (cleanupGate)
            {
                if (cleanupTask != null)
                {
                    return AwaitCleanupAsync(cleanupTask);
                }

                var completion =
                    new TaskCompletionSource<RegionParticipantCleanupResult>();
                Task<RegionParticipantCleanupResult> currentTask =
                    completion.Task;
                cleanupTask = currentTask;
                CompleteCleanupAsync(completion).Forget();
                return AwaitCleanupAsync(currentTask);
            }
        }

        public void ForceCleanupNoFail()
        {
            try
            {
                terminalForced = true;
                if (published && scope != null)
                {
                    binding.TryReleaseCommittedScope(
                        nodeId,
                        scope,
                        out _);
                    published = false;
                }

                scope?.ForceClose();
            }
            catch
            {
                // Host terminal fallback is deliberately no-fail.
            }
        }

        private async UniTaskVoid CompleteCleanupAsync(
            TaskCompletionSource<RegionParticipantCleanupResult> completion)
        {
            RegionParticipantCleanupResult result;
            try
            {
                if (published && scope != null)
                {
                    if (!binding.TryReleaseCommittedScope(
                            nodeId,
                            scope,
                            out CoCoDiagnostic releaseDiagnostic))
                    {
                        result = RegionParticipantCleanupResult.Failure(
                            releaseDiagnostic.IsNone
                                ? RegionErrors.CleanupBlocked(
                                    "The committed Pool Scope binding could not be released.")
                                : releaseDiagnostic);
                        CompleteCleanupAttempt(completion, result);
                        return;
                    }

                    published = false;
                }

                if (scope != null &&
                    scope.State != PoolScopeState.Closed)
                {
                    CoCoDiagnostic closeDiagnostic =
                        await scope.CloseAsync();
                    if (closeDiagnostic.IsError)
                    {
                        result = RegionParticipantCleanupResult.Failure(
                            closeDiagnostic);
                        CompleteCleanupAttempt(completion, result);
                        return;
                    }
                }

                result = RegionParticipantCleanupResult.Success();
            }
            catch (Exception exception)
            {
                result = RegionParticipantCleanupResult.Failure(
                    RegionErrors.CleanupBlocked(
                        "Pool Scope cleanup threw: " +
                        exception.Message));
            }

            CompleteCleanupAttempt(completion, result);
        }

        private void CompleteCleanupAttempt(
            TaskCompletionSource<RegionParticipantCleanupResult> completion,
            RegionParticipantCleanupResult result)
        {
            if (!result.Succeeded)
            {
                lock (cleanupGate)
                {
                    if (ReferenceEquals(cleanupTask, completion.Task))
                    {
                        cleanupTask = null;
                    }
                }
            }

            completion.TrySetResult(result);
        }

        private static bool ProfileMatches(
            in RegionPoolProfilePlan profilePlan,
            in PoolProfile profile) =>
            profile.IsValid &&
            profile.Id == profilePlan.PoolId &&
            profile.PrewarmCount == profilePlan.PrewarmCount &&
            profile.MaxRetained == profilePlan.MaxRetained;

        private static async UniTask<RegionParticipantCleanupResult>
            AwaitCleanupAsync(
                Task<RegionParticipantCleanupResult> task) =>
            await task;
    }
}
