using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Modules.Map.Pooling.Tests
{
    public sealed class PoolRegionParticipantTests
    {
        [UnityTest]
        public IEnumerator CandidateOwnsScopeAndClosesItWithoutStoppingRuntime() =>
            UniTask.ToCoroutine(async () =>
            {
                using (PoolHarness harness = PoolHarness.Create())
                {
                    PoolRegionParticipantCandidate candidate =
                        harness.CreateCandidate();
                    RegionParticipantPrepareResult prepared =
                        await candidate.PrepareAsync(
                            harness.PrepareContext,
                            CancellationToken.None);
                    Assert.IsTrue(
                        prepared.Succeeded,
                        prepared.Diagnostic.Message);
                    Assert.IsTrue(candidate.TryCommit(
                        harness.CommitContext,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreSame(
                        harness.Binding.LastCreatedScope,
                        harness.Binding.CommittedScope);

                    RegionParticipantCleanupResult cleanup =
                        await candidate.CleanupAsync(
                            RegionParticipantCleanupReason.Removed,
                            CancellationToken.None);
                    Assert.IsTrue(cleanup.Succeeded, cleanup.Diagnostic.Message);
                    Assert.IsNull(harness.Binding.CommittedScope);
                    Assert.AreEqual(
                        PoolScopeState.Closed,
                        harness.Binding.LastCreatedScope.State);
                    Assert.IsFalse(harness.PoolRuntime.IsDisposed);
                    Assert.IsFalse(harness.PoolRuntime.IsShuttingDown);
                }
            });

        [UnityTest]
        public IEnumerator FailedBindingReleaseCanRetrySameCandidateCleanup() =>
            UniTask.ToCoroutine(async () =>
            {
                using (PoolHarness harness = PoolHarness.Create())
                {
                    PoolRegionParticipantCandidate candidate =
                        harness.CreateCandidate();
                    Assert.IsTrue(
                        (await candidate.PrepareAsync(
                            harness.PrepareContext,
                            CancellationToken.None)).Succeeded);
                    Assert.IsTrue(candidate.TryCommit(
                        harness.CommitContext,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    harness.Binding.FailNextRelease = true;
                    RegionParticipantCleanupResult first =
                        await candidate.CleanupAsync(
                            RegionParticipantCleanupReason.Removed,
                            CancellationToken.None);
                    Assert.IsFalse(first.Succeeded);
                    Assert.AreEqual(
                        PoolScopeState.Open,
                        harness.Binding.LastCreatedScope.State);
                    Assert.AreSame(
                        harness.Binding.LastCreatedScope,
                        harness.Binding.CommittedScope);

                    RegionParticipantCleanupResult retried =
                        await candidate.CleanupAsync(
                            RegionParticipantCleanupReason.Removed,
                            CancellationToken.None);
                    Assert.IsTrue(
                        retried.Succeeded,
                        retried.Diagnostic.Message);
                    Assert.AreEqual(2, harness.Binding.ReleaseAttempts);
                    Assert.AreEqual(
                        PoolScopeState.Closed,
                        harness.Binding.LastCreatedScope.State);
                    Assert.IsNull(harness.Binding.CommittedScope);
                }
            });

        [Test]
        public void PoolCatalogRegistrationIsExplicitAndAotInventoryIsStable()
        {
            using (PoolHarness harness = PoolHarness.Create())
            {
                var catalog = new RegionParticipantCatalog();
                Assert.IsTrue(RegionBuiltInPoolCatalog.TryRegister(
                    catalog,
                    harness.Binding,
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
                catalog.Seal();

                Assert.IsTrue(RegionParticipantTypeId.TryCreate(
                    RegionBuiltInPoolCatalog.ParticipantTypeValue,
                    out RegionParticipantTypeId typeId));
                Assert.IsTrue(RegionParticipantModeId.TryCreate(
                    RegionBuiltInPoolCatalog.ModeValue,
                    out RegionParticipantModeId modeId));
                Assert.IsTrue(catalog.TryGetRegistration(
                    typeId,
                    modeId,
                    out RegionParticipantRegistration registration));
                Assert.AreEqual(
                    typeof(PoolRegionParticipantCandidate),
                    registration.CandidateType);
                Assert.IsTrue(
                    RegionPlanPurityValidator.TryValidate(
                        harness.Plan,
                        out string purityFailure),
                    purityFailure);
                CollectionAssert.Contains(
                    (ICollection<Type>)RegionBuiltInPoolCatalog.AotTypes,
                    typeof(PoolRegionParticipantCandidate));
            }
        }

        private sealed class PoolHarness : IDisposable
        {
            private readonly GameObject owner;
            private readonly GameObject prefab;
            private readonly ContentRuntime contentRuntime;

            private PoolHarness(
                GameObject owner,
                GameObject prefab,
                ContentRuntime contentRuntime,
                PoolRuntime poolRuntime,
                RecordingBinding binding,
                RegionPlanNodeId nodeId,
                PoolRegionParticipantPlan plan)
            {
                this.owner = owner;
                this.prefab = prefab;
                this.contentRuntime = contentRuntime;
                PoolRuntime = poolRuntime;
                Binding = binding;
                NodeId = nodeId;
                Plan = plan;
            }

            internal PoolRuntime PoolRuntime { get; }
            internal RecordingBinding Binding { get; }
            internal RegionPlanNodeId NodeId { get; }
            internal PoolRegionParticipantPlan Plan { get; }
            internal RegionParticipantPrepareContext PrepareContext =>
                new RegionParticipantPrepareContext(
                    NodeId,
                    Capabilities(),
                    1,
                    null);
            internal RegionParticipantCommitContext CommitContext =>
                new RegionParticipantCommitContext(
                    NodeId,
                    Capabilities(),
                    1);

            internal static PoolHarness Create()
            {
                var owner = new GameObject("Pool Region Test Owner");
                var prefab = new GameObject("Pool Region Test Prefab");
                Assert.IsTrue(ContentRuntime.TryCreate(
                    out ContentRuntime contentRuntime,
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
                Assert.IsTrue(PoolRuntime.TryCreate(
                    contentRuntime,
                    owner.transform,
                    out PoolRuntime poolRuntime,
                    out diagnostic),
                    diagnostic.Message);

                Assert.IsTrue(RegionId.TryCreate(
                    "world.wilderness",
                    out RegionId regionId));
                Assert.IsTrue(RegionParticipantSlotId.TryCreate(
                    "pool",
                    out RegionParticipantSlotId slotId));
                Assert.IsTrue(RegionPlanNodeId.TryCreateGlobal(
                    regionId,
                    slotId,
                    out RegionPlanNodeId nodeId));
                Assert.IsTrue(ContentId.TryCreate(
                    "tests.map.pool.prefab",
                    out ContentId contentId));
                Assert.IsTrue(ContentReference.TryCreateDirectPrefabSource(
                    contentId,
                    prefab,
                    out ContentReference prefabReference));
                Assert.IsTrue(PoolId.TryCreate(
                    "tests.map.pool",
                    out PoolId poolId));
                Assert.IsTrue(PoolProfile.TryCreate(
                    poolId,
                    prefabReference,
                    0,
                    0,
                    out PoolProfile profile));

                var profilePlan = new RegionPoolProfilePlan(
                    poolId,
                    "tests.binding",
                    0,
                    0);
                var plan = new PoolRegionParticipantPlan(
                    new[] { profilePlan },
                    "tests.pool-plan");
                var binding = new RecordingBinding(
                    poolRuntime,
                    profile);
                return new PoolHarness(
                    owner,
                    prefab,
                    contentRuntime,
                    poolRuntime,
                    binding,
                    nodeId,
                    plan);
            }

            internal PoolRegionParticipantCandidate CreateCandidate() =>
                new PoolRegionParticipantCandidate(
                    NodeId,
                    Binding.NextCandidateSequence(),
                    Plan,
                    Binding);

            public void Dispose()
            {
                PoolRuntime.ShutdownAsync().Forget();
                contentRuntime.ShutdownAsync().Forget();
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
                if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
            }

            private static RegionCapabilitySet Capabilities()
            {
                RegionCapabilitySet.TryCreate(
                    new[]
                    {
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background
                    },
                    out RegionCapabilitySet capabilities);
                return capabilities;
            }
        }

        private sealed class RecordingBinding :
            IRegionPoolParticipantBinding
        {
            private readonly PoolRuntime runtime;
            private readonly PoolProfile profile;
            private long nextSequence;

            internal RecordingBinding(
                PoolRuntime runtime,
                PoolProfile profile)
            {
                this.runtime = runtime;
                this.profile = profile;
            }

            internal PoolScope LastCreatedScope { get; private set; }
            internal PoolScope CommittedScope { get; private set; }
            internal bool FailNextRelease { get; set; }
            internal int ReleaseAttempts { get; private set; }

            internal long NextCandidateSequence() => ++nextSequence;

            public bool TryGetPoolRuntime(
                RegionPlanNodeId nodeId,
                out PoolRuntime result,
                out CoCoDiagnostic diagnostic)
            {
                result = runtime;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryCreateCandidateScope(
                RegionPlanNodeId nodeId,
                long candidateSequence,
                out PoolScope scope,
                out CoCoDiagnostic diagnostic)
            {
                Assert.IsTrue(ContentOwnerId.TryCreate(
                    "tests.map.pool." + candidateSequence,
                    out ContentOwnerId ownerId));
                bool created = runtime.TryCreateScope(
                    ownerId,
                    out scope,
                    out diagnostic);
                if (created) LastCreatedScope = scope;
                return created;
            }

            public bool TryResolveProfile(
                in RegionPoolProfilePlan profilePlan,
                out PoolProfile result,
                out CoCoDiagnostic diagnostic)
            {
                result = profile;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryPublishCommittedScope(
                RegionPlanNodeId nodeId,
                PoolScope scope,
                out CoCoDiagnostic diagnostic)
            {
                CommittedScope = scope;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryReleaseCommittedScope(
                RegionPlanNodeId nodeId,
                PoolScope expectedScope,
                out CoCoDiagnostic diagnostic)
            {
                ReleaseAttempts++;
                if (FailNextRelease)
                {
                    FailNextRelease = false;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Map,
                        CoCoDiagnosticCode.RegionCleanupBlocked,
                        "Requested binding release failure.");
                    return false;
                }

                if (!ReferenceEquals(CommittedScope, expectedScope))
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Map,
                        CoCoDiagnosticCode.RegionCleanupBlocked,
                        "Committed Scope mismatch.");
                    return false;
                }

                CommittedScope = null;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
