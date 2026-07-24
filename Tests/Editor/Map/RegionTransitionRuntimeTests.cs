using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionTransitionRuntimeTests
    {
        [UnityTest]
        public IEnumerator StablePlansReuseWhileCapabilitySensitivePlansReplace() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node("stable", new StableTestPlan("stable")),
                           Node(
                               "sensitive",
                               new SensitiveTestPlan("sensitive"))))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(first)).Status);

                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(second)).Status);

                    Assert.AreEqual(1, controller.CreateCount("stable"));
                    Assert.AreEqual(2, controller.CreateCount("sensitive"));
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "sensitive",
                            RegionParticipantCleanupReason.Replaced));
                    RegionRuntimeRegionSnapshot snapshot =
                        harness.OnlyRegionSnapshot();
                    Assert.AreEqual(1, snapshot.ReusedNodeCount);
                    Assert.AreEqual(0, snapshot.CandidateNodeCount);
                }
            });

        [UnityTest]
        public IEnumerator OptionalPrepareFailureCommitsAbsentAndReportsDegraded() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node("required", new StableTestPlan("required")),
                           Node(
                               "optional",
                               new StableTestPlan(
                                   "optional",
                                   prepareFails: true),
                               RegionParticipantRequirement.Optional)))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult result =
                        await lease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(RegionReadinessStatus.Ready, result.Status);
                    Assert.IsTrue(harness.OnlyRegionSnapshot().OptionalDegraded);
                    Assert.AreEqual(1, controller.CommitCount("required"));
                    Assert.AreEqual(0, controller.CommitCount("optional"));
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "optional",
                            RegionParticipantCleanupReason.CandidateFailed));
                }
            });

        [UnityTest]
        public IEnumerator RequiredPrepareFailureCanRetrySameLeaseRevision() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.SetPrepareFailure("required", true);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node("required", new StableTestPlan("required"))))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult failed =
                        await lease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(RegionReadinessStatus.Failed, failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionTransitionFailed,
                        failed.Diagnostic.Code);

                    controller.SetPrepareFailure("required", false);
                    Assert.IsTrue(harness.Runtime.TryRetryRegion(
                        harness.RegionId,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(revision)).Status);
                    Assert.AreEqual(2, controller.CreateCount("required"));
                }
            });

        [UnityTest]
        public IEnumerator CommitFaultIsTerminalUntilHostShutdown() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.SetCommitFailure("fault", true);
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(1),
                    Node("fault", new StableTestPlan("fault")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult failed =
                        await lease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(RegionReadinessStatus.Failed, failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCommitFaulted,
                        failed.Diagnostic.Code);
                    Assert.IsTrue(harness.OnlyRegionSnapshot().Faulted);
                    Assert.IsFalse(harness.Runtime.TryRetryRegion(
                        harness.RegionId,
                        out diagnostic));
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCommitFaulted,
                        diagnostic.Code);

                    Assert.AreEqual(0, controller.TotalCleanupCount("fault"));
                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "fault",
                            RegionParticipantCleanupReason.HostShutdown));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator FailedFactoryCandidateIsOwnedAndCleanedExactlyOnce() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.SetFactoryFailureWithCandidate("factory-fail", true);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "factory-fail",
                               new StableTestPlan("factory-fail"))))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult failed =
                        await lease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        failed.Status);
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "factory-fail",
                            1));
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "factory-fail",
                            RegionParticipantCleanupReason
                                .CandidateFailed));
                }
            });

        [UnityTest]
        public IEnumerator ThrowingFactoryCandidateIsOwnedAndCleanedExactlyOnce() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.SetFactoryThrowAfterAllocation(
                    "factory-throw",
                    true);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "factory-throw",
                               new StableTestPlan("factory-throw"))))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult failed =
                        await lease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionTransitionFailed,
                        failed.Diagnostic.Code);
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "factory-throw",
                            1));
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "factory-throw",
                            RegionParticipantCleanupReason
                                .CandidateFailed));
                }
            });

        [UnityTest]
        public IEnumerator WrongExactCandidateTypeIsSafelyCleaned() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.SetWrongCandidateType("wrong-type", true);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "wrong-type",
                               new StableTestPlan("wrong-type"))))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult failed =
                        await lease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCatalogConflict,
                        failed.Diagnostic.Code);
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "wrong-type",
                            1));
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "wrong-type",
                            RegionParticipantCleanupReason
                                .CandidateFailed));
                }
            });

        [UnityTest]
        public IEnumerator ExistingCommittedCandidateAliasFailsWithoutCleaningOwner() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(1),
                    Node(
                        "alias",
                        new SensitiveTestPlan("alias")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(first)).Status);

                    controller.SetAliasExistingCandidate("alias", true);
                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    RegionReadinessResult failed =
                        await lease.WaitUntilReadyAsync(second);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCatalogConflict,
                        failed.Diagnostic.Code);
                    Assert.AreEqual(
                        0,
                        controller.CleanupAsyncInvocationCount(
                            "alias",
                            1));

                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "alias",
                            RegionParticipantCleanupReason
                                .HostShutdown));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator OptionalCandidateAliasIsAbsentWithoutCleaningOwner() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.SetAliasExistingCandidate(
                    "optional-alias",
                    true);
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(1),
                    Node(
                        "a-owner",
                        new StableTestPlan("optional-alias")),
                    Node(
                        "b-optional",
                        new StableTestPlan("optional-alias"),
                        RegionParticipantRequirement.Optional));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(revision)).Status);
                    Assert.IsTrue(
                        harness.OnlyRegionSnapshot().OptionalDegraded);
                    Assert.AreEqual(
                        1,
                        controller.CommitCount("optional-alias"));
                    Assert.AreEqual(
                        0,
                        controller.CleanupAsyncInvocationCount(
                            "optional-alias",
                            1));

                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "optional-alias",
                            RegionParticipantCleanupReason
                                .HostShutdown));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator CandidateAliasDuringAnotherRegionCleanupIsRejected() =>
            UniTask.ToCoroutine(async () =>
            {
                Assert.IsTrue(RegionId.TryCreate(
                    "world.wilderness",
                    out RegionId wildernessId));
                Assert.IsTrue(RegionId.TryCreate(
                    "world.castle",
                    out RegionId castleId));
                var wildernessPlan = new RegionCompiledPlan(
                    wildernessId,
                    Array.Empty<RegionCompiledTier>(),
                    Array.Empty<RegionCompiledChunk>(),
                    new[]
                    {
                        Node(
                            wildernessId,
                            "shared",
                            new StableTestPlan("shared"))
                    },
                    "tests.wilderness");
                var castlePlan = new RegionCompiledPlan(
                    castleId,
                    Array.Empty<RegionCompiledTier>(),
                    Array.Empty<RegionCompiledChunk>(),
                    new[]
                    {
                        Node(
                            castleId,
                            "shared",
                            new StableTestPlan("shared"))
                    },
                    "tests.castle");

                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(5),
                    wildernessId,
                    wildernessPlan,
                    castlePlan);
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        wildernessId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease wildernessLease,
                        out RegionDemandRevision wildernessRevision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await wildernessLease.WaitUntilReadyAsync(
                            wildernessRevision)).Status);

                    controller.BlockFirstCleanup("shared");
                    wildernessLease.Dispose();
                    await WaitUntilAsync(
                        () => controller.CleanupAsyncInvocationCount(
                                  "shared",
                                  1) == 1,
                        "The first Region did not enter Cleanup.");

                    controller.SetAliasExistingCandidate("shared", true);
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision castleRevision,
                        out diagnostic),
                        diagnostic.Message);
                    RegionReadinessResult failed =
                        await castleLease.WaitUntilReadyAsync(castleRevision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCatalogConflict,
                        failed.Diagnostic.Code);
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "shared",
                            1));

                    controller.CompleteBlockedCleanup();
                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        1,
                        controller.TotalCleanupCount("shared"));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator FailedBehaviourCommitRestoresAlreadyChangedComponents() =>
            UniTask.ToCoroutine(async () =>
            {
                var target = new GameObject("Region Commit Target");
                try
                {
                    RegionCommitDestroyerProbe destroyer =
                        target.AddComponent<RegionCommitDestroyerProbe>();
                    destroyer.enabled = false;
                    RegionCommitVictimProbe victim =
                        target.AddComponent<RegionCommitVictimProbe>();
                    victim.enabled = false;
                    destroyer.Victim = victim;

                    Assert.IsTrue(RegionId.TryCreate(
                        "world.wilderness",
                        out RegionId regionId));
                    Assert.IsTrue(RegionChunkId.TryCreate(
                        "surface",
                        out RegionChunkId chunkId));
                    Assert.IsTrue(RegionParticipantSlotId.TryCreate(
                        "behaviour",
                        out RegionParticipantSlotId slotId));
                    Assert.IsTrue(RegionPlanNodeId.TryCreateChunk(
                        regionId,
                        chunkId,
                        slotId,
                        out RegionPlanNodeId nodeId));

                    var catalog = new RegionParticipantCatalog();
                    Assert.IsTrue(RegionBehaviourParticipant.TryRegister(
                        catalog,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.IsTrue(catalog.TryGetRegistration(
                        RegionBehaviourParticipant.TypeId,
                        RegionBehaviourParticipant.ModeId,
                        out RegionParticipantRegistration registration));
                    var freezeContext =
                        new RegionParticipantFreezeContext(
                            nodeId,
                            "target",
                            default);
                    Assert.IsTrue(
                        registration.ConfigFreezer.TryFreeze(
                            freezeContext,
                            new RegionBehaviourParticipantConfig(),
                            out IRegionParticipantPlan plan,
                            out diagnostic),
                        diagnostic.Message);
                    var resolver =
                        new FixedFragmentResolver(target);
                    var createContext =
                        new RegionParticipantCreateContext(
                            nodeId,
                            "target",
                            resolver);
                    Assert.IsTrue(
                        registration.Factory.TryCreateCandidate(
                            createContext,
                            plan,
                            out IRegionParticipantCandidate candidate,
                            out diagnostic),
                        diagnostic.Message);

                    RegionCapabilitySet capabilities =
                        Capabilities(RegionCapabilityId.Represented);
                    var prepareContext =
                        new RegionParticipantPrepareContext(
                            nodeId,
                            capabilities,
                            1,
                            resolver);
                    Assert.IsTrue(
                        (await candidate.PrepareAsync(
                            prepareContext,
                            CancellationToken.None)).Succeeded);
                    var commitContext =
                        new RegionParticipantCommitContext(
                            nodeId,
                            capabilities,
                            1);
                    Assert.IsFalse(candidate.TryCommit(
                        commitContext,
                        out diagnostic));
                    Assert.IsTrue(destroyer.enabled);
                    Assert.IsTrue(
                        (await candidate.CleanupAsync(
                            RegionParticipantCleanupReason.HostShutdown,
                            CancellationToken.None)).Succeeded);
                    Assert.IsFalse(destroyer.enabled);
                }
                finally
                {
                    if (target != null)
                    {
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
            });

        [UnityTest]
        public IEnumerator CommitFaultShutdownCleansCandidateBeforeOldCommittedNode() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(1),
                    Node(
                        "sensitive",
                        new SensitiveTestPlan("sensitive")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(first)).Status);

                    controller.SetCommitFailure("sensitive", true);
                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        (await lease.WaitUntilReadyAsync(second)).Status);

                    await harness.ShutdownAsync();
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "sensitive#2",
                            "sensitive#1"
                        },
                        controller.HostShutdownOrder);
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator ShutdownWaitsForIgnoredCancellationPrepareBeforeCleanup() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.BlockPrepare("slow-prepare");
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromMilliseconds(20),
                    Node(
                        "slow-prepare",
                        new StableTestPlan("slow-prepare")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out _,
                        out _,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        1,
                        controller.PrepareCount("slow-prepare"));

                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        0,
                        controller.CleanupAsyncInvocationCount(
                            "slow-prepare",
                            1));
                    Assert.AreEqual(
                        0,
                        controller.TerminalCleanupInvocationCount(
                            "slow-prepare",
                            1));

                    controller.CompleteBlockedPrepare();
                    await WaitUntilAsync(
                        () => controller.CleanupAsyncInvocationCount(
                                  "slow-prepare",
                                  1) == 1,
                        "Cancelled Prepare runner did not clean its candidate.");
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "slow-prepare",
                            RegionParticipantCleanupReason
                                .CandidateCancelled));
                    Assert.AreEqual(
                        0,
                        controller.TerminalCleanupInvocationCount(
                            "slow-prepare",
                            1));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator BlockedCleanupObservesLateCompletionBeforeRetry() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.BlockFirstCleanup("sensitive");
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromMilliseconds(20),
                           Node(
                               "sensitive",
                               new SensitiveTestPlan("sensitive"))))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(first)).Status);

                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    RegionReadinessResult blocked =
                        await lease.WaitUntilReadyAsync(second);
                    Assert.AreEqual(RegionReadinessStatus.Failed, blocked.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCleanupBlocked,
                        blocked.Diagnostic.Code);
                    Assert.IsTrue(harness.OnlyRegionSnapshot().BlockedCleanup);
                    Assert.AreEqual(1, controller.TotalCleanupCount("sensitive"));

                    controller.CompleteBlockedCleanup();
                    Assert.IsTrue(harness.Runtime.TryRetryRegion(
                        harness.RegionId,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(second)).Status);
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            "sensitive",
                            RegionParticipantCleanupReason.Replaced));
                    Assert.IsFalse(harness.OnlyRegionSnapshot().BlockedCleanup);
                }
            });

        [UnityTest]
        public IEnumerator ShutdownDoesNotInvokeBlockedCleanupTwice() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.BlockFirstCleanup("sensitive");
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromMilliseconds(20),
                    Node(
                        "sensitive",
                        new SensitiveTestPlan("sensitive")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(first)).Status);
                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        (await lease.WaitUntilReadyAsync(second)).Status);

                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "sensitive",
                            1));
                    Assert.AreEqual(
                        0,
                        controller.TerminalCleanupInvocationCount(
                            "sensitive",
                            1));
                    controller.CompleteBlockedCleanup();
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator LateBlockedCleanupFailureUsesTerminalFallback() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.BlockFirstCleanup("sensitive");
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromMilliseconds(20),
                    Node(
                        "sensitive",
                        new SensitiveTestPlan("sensitive")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(first)).Status);
                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        (await lease.WaitUntilReadyAsync(second)).Status);

                    await harness.ShutdownAsync();
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "sensitive",
                            1));
                    Assert.AreEqual(
                        0,
                        controller.TerminalCleanupInvocationCount(
                            "sensitive",
                            1));

                    controller.FailBlockedCleanup();
                    await WaitUntilAsync(
                        () => controller.TerminalCleanupInvocationCount(
                                  "sensitive",
                                  1) == 1,
                        "Late failed cleanup did not invoke terminal fallback.");
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "sensitive",
                            1));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator TerminalCleanupFailureAndThrowAreNotInvokedAgain() =>
            UniTask.ToCoroutine(async () =>
            {
                await VerifyTerminalCleanupIsNotInvokedAgainAsync(
                    "cleanup-failure",
                    false);
                await VerifyTerminalCleanupIsNotInvokedAgainAsync(
                    "cleanup-throw",
                    true);
            });

        [UnityTest]
        public IEnumerator ShutdownBatchTimeoutWaitsForLateTerminalFallback() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromMilliseconds(20),
                    Node(
                        "shutdown-timeout",
                        new StableTestPlan("shutdown-timeout")));
                try
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(revision)).Status);

                    controller.BlockFirstCleanup("shutdown-timeout");
                    CoCoDiagnostic shutdownDiagnostic =
                        await harness.TransitionRuntime.ShutdownAsync();
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCleanupBlocked,
                        shutdownDiagnostic.Code);
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "shutdown-timeout",
                            1));
                    Assert.AreEqual(
                        0,
                        controller.TerminalCleanupInvocationCount(
                            "shutdown-timeout",
                            1));

                    controller.FailBlockedCleanup();
                    await WaitUntilAsync(
                        () => controller.TerminalCleanupInvocationCount(
                                  "shutdown-timeout",
                                  1) == 1,
                        "Timed-out shutdown Cleanup did not use terminal fallback.");
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            "shutdown-timeout",
                            1));
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator CleanupRunsInExactReversePlanOrder() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "residency",
                               new StableTestPlan("residency"),
                               phase: RegionParticipantPhase.Residency),
                           Node(
                               "services",
                               new StableTestPlan("services"),
                               phase: RegionParticipantPhase.Services),
                           Node(
                               "simulation",
                               new StableTestPlan("simulation"),
                               phase: RegionParticipantPhase.Simulation),
                           Node(
                               "presentation",
                               new StableTestPlan("presentation"),
                               phase: RegionParticipantPhase.Presentation)))
                {
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(revision)).Status);

                    lease.Dispose();
                    await WaitUntilAsync(
                        () => controller.RemovedOrder.Count == 4,
                        "Removed cleanup did not complete.");
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "presentation",
                            "simulation",
                            "services",
                            "residency"
                        },
                        controller.RemovedOrder);
                }
            });

        [UnityTest]
        public IEnumerator ParticipantCallbackCannotReenterDemandMutation() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "reentrant",
                               new StableTestPlan("reentrant"))))
                {
                    controller.ReentrantRuntime = harness.Runtime;
                    controller.ReenterOnCommit = true;
                    RegionDemandScope scope = harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(revision)).Status);
                    Assert.IsFalse(controller.ReentrantMutationSucceeded);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionDemandConflict,
                        controller.ReentrantDiagnostic.Code);
                }
            });

        private static async UniTask
            VerifyTerminalCleanupIsNotInvokedAgainAsync(
                string key,
                bool throws)
        {
            var controller = new CandidateController();
            if (throws)
            {
                controller.SetCleanupThrow(key, true);
            }
            else
            {
                controller.SetCleanupFailure(key, true);
            }

            TransitionHarness harness = CreateHarness(
                controller,
                TimeSpan.FromSeconds(1),
                Node(
                    key,
                    new SensitiveTestPlan(key)));
            try
            {
                RegionDemandScope scope = harness.CreateScope("player");
                Assert.IsTrue(scope.TryDemand(
                    harness.RegionId,
                    Capabilities(RegionCapabilityId.Represented),
                    RegionCoverage.All,
                    out RegionDemandLease lease,
                    out RegionDemandRevision first,
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
                Assert.AreEqual(
                    RegionReadinessStatus.Ready,
                    (await lease.WaitUntilReadyAsync(first)).Status);

                Assert.IsTrue(lease.TryUpdate(
                    Capabilities(
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background),
                    RegionCoverage.All,
                    out RegionDemandRevision second,
                    out diagnostic),
                    diagnostic.Message);
                RegionReadinessResult blocked =
                    await lease.WaitUntilReadyAsync(second);
                Assert.AreEqual(
                    RegionReadinessStatus.Failed,
                    blocked.Status);
                Assert.AreEqual(
                    CoCoDiagnosticCode.RegionCleanupBlocked,
                    blocked.Diagnostic.Code);
                Assert.AreEqual(
                    1,
                    controller.CleanupAsyncInvocationCount(
                        key,
                        1));

                Assert.IsTrue(harness.Runtime.TryRetryRegion(
                    harness.RegionId,
                    out diagnostic),
                    diagnostic.Message);
                await UniTask.Yield();
                Assert.AreEqual(
                    1,
                    controller.CleanupAsyncInvocationCount(
                        key,
                        1));

                await harness.ShutdownAsync();
                Assert.AreEqual(
                    1,
                    controller.CleanupAsyncInvocationCount(
                        key,
                        1));
                await WaitUntilAsync(
                    () => controller.TerminalCleanupInvocationCount(
                              key,
                              1) == 1,
                    "Failed Cleanup did not use terminal fallback.");
            }
            finally
            {
                harness.Dispose();
            }
        }

        private static TransitionHarness CreateHarness(
            CandidateController controller,
            TimeSpan cleanupTimeout,
            params RegionCompiledParticipantNode[] nodes)
        {
            Assert.IsTrue(RegionId.TryCreate(
                "world.wilderness",
                out RegionId regionId));
            var plan = new RegionCompiledPlan(
                regionId,
                Array.Empty<RegionCompiledTier>(),
                Array.Empty<RegionCompiledChunk>(),
                nodes,
                "tests.plan");
            return CreateHarness(
                controller,
                cleanupTimeout,
                regionId,
                plan);
        }

        private static TransitionHarness CreateHarness(
            CandidateController controller,
            TimeSpan cleanupTimeout,
            RegionId primaryRegionId,
            params RegionCompiledPlan[] plans)
        {
            RegionMainThreadGuard.CaptureCurrentThread();
            Assert.IsTrue(ContentRuntime.TryCreate(
                out ContentRuntime contentRuntime,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(RegionRuntime.TryCreate(
                contentRuntime,
                out RegionRuntime runtime,
                out diagnostic),
                diagnostic.Message);

            var catalog = new RegionParticipantCatalog();
            CandidateController.Active = controller;
            Assert.IsTrue(RegionParticipantRegistration.TryCreate(
                TestIds.TypeId,
                TestIds.ModeId,
                StandardCapabilities(),
                new TestFreezer(),
                new TestFactory(),
                out RegionParticipantRegistration registration,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(catalog.TryRegisterParticipant(
                registration,
                out diagnostic),
                diagnostic.Message);
            catalog.Seal();

            Assert.IsTrue(RegionTransitionRuntime.TryCreate(
                runtime,
                catalog,
                plans,
                cleanupTimeout,
                out RegionTransitionRuntime transitionRuntime,
                out diagnostic),
                diagnostic.Message);
            return new TransitionHarness(
                primaryRegionId,
                contentRuntime,
                runtime,
                transitionRuntime);
        }

        private static RegionCompiledParticipantNode Node(
            string slotValue,
            TestPlanBase plan,
            RegionParticipantRequirement requirement =
                RegionParticipantRequirement.Required,
            RegionParticipantPhase phase =
                RegionParticipantPhase.Simulation)
        {
            Assert.IsTrue(RegionId.TryCreate(
                "world.wilderness",
                out RegionId regionId));
            return Node(
                regionId,
                slotValue,
                plan,
                requirement,
                phase);
        }

        private static RegionCompiledParticipantNode Node(
            RegionId regionId,
            string slotValue,
            TestPlanBase plan,
            RegionParticipantRequirement requirement =
                RegionParticipantRequirement.Required,
            RegionParticipantPhase phase =
                RegionParticipantPhase.Simulation)
        {
            Assert.IsTrue(RegionParticipantSlotId.TryCreate(
                slotValue,
                out RegionParticipantSlotId slotId));
            Assert.IsTrue(RegionPlanNodeId.TryCreateGlobal(
                regionId,
                slotId,
                out RegionPlanNodeId nodeId));
            return new RegionCompiledParticipantNode(
                nodeId,
                TestIds.TypeId,
                TestIds.ModeId,
                phase,
                0,
                requirement,
                Capabilities(RegionCapabilityId.Represented),
                Array.Empty<RegionPlanNodeId>(),
                plan,
                string.Empty,
                default,
                plan.Fingerprint);
        }

        private static RegionCapabilitySet StandardCapabilities() =>
            Capabilities(
                RegionCapabilityId.Represented,
                RegionCapabilityId.Background,
                RegionCapabilityId.Enterable,
                RegionCapabilityId.Full);

        private static RegionCapabilitySet Capabilities(
            params RegionCapabilityId[] ids)
        {
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                ids,
                out RegionCapabilitySet result));
            return result;
        }

        private static async UniTask WaitUntilAsync(
            Func<bool> predicate,
            string failure)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                if (predicate()) return;
                await UniTask.Yield();
            }

            Assert.Fail(failure);
        }

        private sealed class TransitionHarness : IDisposable
        {
            private bool shutdown;

            internal TransitionHarness(
                RegionId regionId,
                ContentRuntime contentRuntime,
                RegionRuntime runtime,
                RegionTransitionRuntime transitionRuntime)
            {
                RegionId = regionId;
                ContentRuntime = contentRuntime;
                Runtime = runtime;
                TransitionRuntime = transitionRuntime;
            }

            internal RegionId RegionId { get; }
            internal ContentRuntime ContentRuntime { get; }
            internal RegionRuntime Runtime { get; }
            internal RegionTransitionRuntime TransitionRuntime { get; }

            internal RegionDemandScope CreateScope(string owner)
            {
                Assert.IsTrue(RegionDemandOwnerId.TryCreate(
                    owner,
                    out RegionDemandOwnerId ownerId));
                Assert.IsTrue(Runtime.TryCreateDemandScope(
                    ownerId,
                    out RegionDemandScope scope,
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
                return scope;
            }

            internal RegionRuntimeRegionSnapshot OnlyRegionSnapshot()
            {
                RegionRuntimeSnapshot snapshot = Runtime.CaptureSnapshot();
                Assert.AreEqual(1, snapshot.Regions.Count);
                return snapshot.Regions[0];
            }

            internal async UniTask ShutdownAsync()
            {
                if (shutdown) return;
                shutdown = true;
                await Runtime.ShutdownAsync();
                await ContentRuntime.ShutdownAsync();
            }

            public void Dispose()
            {
                if (!shutdown)
                {
                    Runtime.ForceShutdown();
                    ContentRuntime.ShutdownAsync().Forget();
                    shutdown = true;
                }

                CandidateController.Active = null;
            }
        }

        [Serializable]
        private sealed class TestConfig : RegionParticipantConfig
        {
        }

        private abstract class TestPlanBase : IRegionParticipantPlan
        {
            protected TestPlanBase(
                string key,
                bool prepareFails = false)
            {
                Key = key;
                PrepareFails = prepareFails;
                Fingerprint = "test|" + key;
            }

            internal string Key { get; }
            internal bool PrepareFails { get; }
            public string Fingerprint { get; }
        }

        private sealed class StableTestPlan : TestPlanBase
        {
            internal StableTestPlan(
                string key,
                bool prepareFails = false)
                : base(key, prepareFails)
            {
            }
        }

        private sealed class SensitiveTestPlan :
            TestPlanBase,
            IRegionCapabilitySensitivePlan
        {
            internal SensitiveTestPlan(string key)
                : base(key)
            {
            }
        }

        private sealed class TestFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType => typeof(TestConfig);
            public Type PlanType => typeof(StableTestPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = null;
                diagnostic = RegionErrors.InvalidProfile(
                    "The test freezer is not used.");
                return false;
            }
        }

        private sealed class TestFactory : IRegionParticipantFactory
        {
            public Type CandidateType => typeof(RecordingCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                if (!(plan is TestPlanBase typed) ||
                    CandidateController.Active == null)
                {
                    candidate = null;
                    diagnostic = RegionErrors.TransitionFailed(
                        "The test candidate controller is missing.");
                    return false;
                }

                CandidateController controller =
                    CandidateController.Active;
                candidate = controller.Create(
                    context.NodeId,
                    typed);
                if (controller.ShouldFactoryThrowAfterAllocation(typed.Key))
                {
                    throw new InvalidOperationException(
                        "Requested factory throw after allocating a candidate.");
                }

                if (controller.ShouldFactoryFailWithCandidate(typed.Key))
                {
                    diagnostic = RegionErrors.TransitionFailed(
                        "Requested factory failure with an allocated candidate.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class FixedFragmentResolver :
            IRegionFragmentResolver
        {
            private readonly GameObject target;

            internal FixedFragmentResolver(GameObject target)
            {
                this.target = target;
            }

            public bool TryResolveGameObject(
                string fragmentId,
                out GameObject gameObject,
                out CoCoDiagnostic diagnostic)
            {
                gameObject = target;
                diagnostic = target == null
                    ? RegionErrors.SceneContract(
                        "The fixed test fragment target is missing.")
                    : CoCoDiagnostic.None;
                return target != null;
            }
        }

        private class RecordingCandidate :
            IRegionParticipantCandidate,
            IRegionParticipantTerminalCleanup
        {
            private readonly CandidateController controller;
            private readonly RegionPlanNodeId nodeId;
            private readonly TestPlanBase plan;
            private readonly int creation;
            private bool cleanupInvoked;

            internal RecordingCandidate(
                CandidateController controller,
                RegionPlanNodeId nodeId,
                TestPlanBase plan,
                int creation)
            {
                this.controller = controller;
                this.nodeId = nodeId;
                this.plan = plan;
                this.creation = creation;
            }

            public UniTask<RegionParticipantPrepareResult> PrepareAsync(
                in RegionParticipantPrepareContext context,
                CancellationToken cancellationToken)
            {
                controller.RecordPrepare(plan.Key);
                if (controller.ShouldBlockPrepare(
                        plan.Key,
                        out UniTask<RegionParticipantPrepareResult> pending))
                {
                    return pending;
                }

                bool fails =
                    plan.PrepareFails ||
                    controller.ShouldPrepareFail(plan.Key);
                return UniTask.FromResult(
                    fails
                        ? RegionParticipantPrepareResult.Failure(
                            RegionErrors.TransitionFailed(
                                "Requested test Prepare failure."))
                        : RegionParticipantPrepareResult.Success());
            }

            public bool TryCommit(
                in RegionParticipantCommitContext context,
                out CoCoDiagnostic diagnostic)
            {
                controller.RecordCommit(plan.Key);
                controller.TryReenterMutation();
                if (controller.ShouldCommitFail(plan.Key))
                {
                    diagnostic = RegionErrors.CommitFaulted(
                        "Requested test Commit failure.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public UniTask<RegionParticipantCleanupResult> CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken)
            {
                controller.RecordCleanupAsyncInvocation(
                    plan.Key,
                    creation);
                if (cleanupInvoked)
                {
                    return UniTask.FromResult(
                        RegionParticipantCleanupResult.Failure(
                            RegionErrors.CleanupBlocked(
                                "Cleanup was invoked more than once.")));
                }

                cleanupInvoked = true;
                controller.RecordCleanup(
                    plan.Key,
                    creation,
                    reason);
                if (controller.ShouldThrowCleanup(
                        plan.Key,
                        creation))
                {
                    throw new InvalidOperationException(
                        "Requested test Cleanup throw.");
                }

                if (controller.ShouldFailCleanup(
                        plan.Key,
                        creation))
                {
                    return UniTask.FromResult(
                        RegionParticipantCleanupResult.Failure(
                            RegionErrors.CleanupBlocked(
                                "Requested test Cleanup failure.")));
                }

                if (controller.ShouldBlockCleanup(
                        plan.Key,
                        creation,
                        out UniTask<RegionParticipantCleanupResult> pending))
                {
                    return pending;
                }

                return UniTask.FromResult(
                    RegionParticipantCleanupResult.Success());
            }

            public void ForceCleanupNoFail()
            {
                controller.RecordTerminalCleanupInvocation(
                    plan.Key,
                    creation);
                if (cleanupInvoked) return;
                cleanupInvoked = true;
                controller.RecordCleanup(
                    plan.Key,
                    creation,
                    RegionParticipantCleanupReason.HostShutdown);
            }
        }

        private sealed class WrongRecordingCandidate :
            RecordingCandidate
        {
            internal WrongRecordingCandidate(
                CandidateController controller,
                RegionPlanNodeId nodeId,
                TestPlanBase plan,
                int creation)
                : base(controller, nodeId, plan, creation)
            {
            }
        }

        private sealed class CandidateController
        {
            private readonly Dictionary<string, int> creates =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> prepares =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> commits =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> cleanups =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> cleanupAsyncInvocations =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> terminalCleanupInvocations =
                new Dictionary<string, int>();
            private readonly HashSet<string> prepareFailures =
                new HashSet<string>();
            private readonly HashSet<string> commitFailures =
                new HashSet<string>();
            private readonly HashSet<string> cleanupFailures =
                new HashSet<string>();
            private readonly HashSet<string> cleanupThrows =
                new HashSet<string>();
            private readonly HashSet<string> factoryFailuresWithCandidate =
                new HashSet<string>();
            private readonly HashSet<string> factoryThrowsAfterAllocation =
                new HashSet<string>();
            private readonly HashSet<string> wrongCandidateTypes =
                new HashSet<string>();
            private readonly HashSet<string> aliasExistingCandidates =
                new HashSet<string>();
            private readonly Dictionary<string, RecordingCandidate>
                existingCandidates =
                    new Dictionary<string, RecordingCandidate>();
            private string blockedKey;
            private UniTaskCompletionSource<RegionParticipantCleanupResult>
                blockedCleanup;
            private string blockedPrepareKey;
            private UniTaskCompletionSource<RegionParticipantPrepareResult>
                blockedPrepare;

            internal static CandidateController Active { get; set; }
            internal List<string> RemovedOrder { get; } =
                new List<string>();
            internal List<string> HostShutdownOrder { get; } =
                new List<string>();
            internal RegionRuntime ReentrantRuntime { get; set; }
            internal bool ReenterOnCommit { get; set; }
            internal bool ReentrantMutationSucceeded { get; private set; }
            internal CoCoDiagnostic ReentrantDiagnostic { get; private set; }

            internal RecordingCandidate Create(
                RegionPlanNodeId nodeId,
                TestPlanBase plan)
            {
                int creation = Increment(creates, plan.Key);
                if (aliasExistingCandidates.Contains(plan.Key) &&
                    existingCandidates.TryGetValue(
                        plan.Key,
                        out RecordingCandidate existing))
                {
                    return existing;
                }

                RecordingCandidate candidate =
                    wrongCandidateTypes.Contains(plan.Key)
                    ? new WrongRecordingCandidate(
                        this,
                        nodeId,
                        plan,
                        creation)
                    : new RecordingCandidate(
                        this,
                        nodeId,
                        plan,
                        creation);
                if (!existingCandidates.ContainsKey(plan.Key))
                {
                    existingCandidates.Add(plan.Key, candidate);
                }

                return candidate;
            }

            internal void RecordPrepare(string key) =>
                Increment(prepares, key);

            internal void RecordCommit(string key) =>
                Increment(commits, key);

            internal void RecordCleanupAsyncInvocation(
                string key,
                int creation) =>
                Increment(
                    cleanupAsyncInvocations,
                    CreationKey(key, creation));

            internal void RecordTerminalCleanupInvocation(
                string key,
                int creation) =>
                Increment(
                    terminalCleanupInvocations,
                    CreationKey(key, creation));

            internal void RecordCleanup(
                string key,
                int creation,
                RegionParticipantCleanupReason reason)
            {
                Increment(cleanups, CleanupKey(key, reason));
                if (reason == RegionParticipantCleanupReason.Removed)
                {
                    RemovedOrder.Add(key);
                }

                if (reason ==
                    RegionParticipantCleanupReason.HostShutdown)
                {
                    HostShutdownOrder.Add(
                        CreationKey(key, creation));
                }
            }

            internal void SetPrepareFailure(string key, bool enabled) =>
                SetFlag(prepareFailures, key, enabled);

            internal void SetCommitFailure(string key, bool enabled) =>
                SetFlag(commitFailures, key, enabled);

            internal void SetCleanupFailure(string key, bool enabled) =>
                SetFlag(cleanupFailures, key, enabled);

            internal void SetCleanupThrow(string key, bool enabled) =>
                SetFlag(cleanupThrows, key, enabled);

            internal void SetFactoryFailureWithCandidate(
                string key,
                bool enabled) =>
                SetFlag(
                    factoryFailuresWithCandidate,
                    key,
                    enabled);

            internal bool ShouldFactoryFailWithCandidate(string key) =>
                factoryFailuresWithCandidate.Contains(key);

            internal void SetFactoryThrowAfterAllocation(
                string key,
                bool enabled) =>
                SetFlag(
                    factoryThrowsAfterAllocation,
                    key,
                    enabled);

            internal bool ShouldFactoryThrowAfterAllocation(string key) =>
                factoryThrowsAfterAllocation.Contains(key);

            internal void SetAliasExistingCandidate(
                string key,
                bool enabled) =>
                SetFlag(
                    aliasExistingCandidates,
                    key,
                    enabled);

            internal void SetWrongCandidateType(
                string key,
                bool enabled) =>
                SetFlag(wrongCandidateTypes, key, enabled);

            internal bool ShouldPrepareFail(string key) =>
                prepareFailures.Contains(key);

            internal bool ShouldCommitFail(string key) =>
                commitFailures.Contains(key);

            internal bool ShouldFailCleanup(
                string key,
                int creation) =>
                creation == 1 &&
                cleanupFailures.Contains(key);

            internal bool ShouldThrowCleanup(
                string key,
                int creation) =>
                creation == 1 &&
                cleanupThrows.Contains(key);

            internal void BlockPrepare(string key)
            {
                blockedPrepareKey = key;
                blockedPrepare =
                    new UniTaskCompletionSource<
                        RegionParticipantPrepareResult>();
            }

            internal bool ShouldBlockPrepare(
                string key,
                out UniTask<RegionParticipantPrepareResult> pending)
            {
                if (string.Equals(
                        key,
                        blockedPrepareKey,
                        StringComparison.Ordinal) &&
                    blockedPrepare != null)
                {
                    pending = blockedPrepare.Task;
                    return true;
                }

                pending = default;
                return false;
            }

            internal void CompleteBlockedPrepare()
            {
                blockedPrepare.TrySetResult(
                    RegionParticipantPrepareResult.Success());
            }

            internal void BlockFirstCleanup(string key)
            {
                blockedKey = key;
                blockedCleanup =
                    new UniTaskCompletionSource<
                        RegionParticipantCleanupResult>();
            }

            internal bool ShouldBlockCleanup(
                string key,
                int creation,
                out UniTask<RegionParticipantCleanupResult> pending)
            {
                if (creation == 1 &&
                    string.Equals(
                        key,
                        blockedKey,
                        StringComparison.Ordinal) &&
                    blockedCleanup != null)
                {
                    pending = blockedCleanup.Task;
                    return true;
                }

                pending = default;
                return false;
            }

            internal void CompleteBlockedCleanup()
            {
                blockedCleanup.TrySetResult(
                    RegionParticipantCleanupResult.Success());
            }

            internal void FailBlockedCleanup()
            {
                blockedCleanup.TrySetResult(
                    RegionParticipantCleanupResult.Failure(
                        RegionErrors.CleanupBlocked(
                            "Requested late cleanup failure.")));
            }

            internal void TryReenterMutation()
            {
                if (!ReenterOnCommit || ReentrantRuntime == null) return;
                Assert.IsTrue(RegionDemandOwnerId.TryCreate(
                    "participant.reentrant",
                    out RegionDemandOwnerId ownerId));
                ReentrantMutationSucceeded =
                    ReentrantRuntime.TryCreateDemandScope(
                        ownerId,
                        out _,
                        out CoCoDiagnostic diagnostic);
                ReentrantDiagnostic = diagnostic;
            }

            internal int CreateCount(string key) =>
                Get(creates, key);

            internal int PrepareCount(string key) =>
                Get(prepares, key);

            internal int CommitCount(string key) =>
                Get(commits, key);

            internal int CleanupAsyncInvocationCount(
                string key,
                int creation) =>
                Get(
                    cleanupAsyncInvocations,
                    CreationKey(key, creation));

            internal int TerminalCleanupInvocationCount(
                string key,
                int creation) =>
                Get(
                    terminalCleanupInvocations,
                    CreationKey(key, creation));

            internal int CleanupCount(
                string key,
                RegionParticipantCleanupReason reason) =>
                Get(cleanups, CleanupKey(key, reason));

            internal int TotalCleanupCount(string key)
            {
                int total = 0;
                foreach (
                    RegionParticipantCleanupReason reason
                    in Enum.GetValues(
                        typeof(RegionParticipantCleanupReason)))
                {
                    total += CleanupCount(key, reason);
                }

                return total;
            }

            private static int Increment(
                IDictionary<string, int> counts,
                string key)
            {
                counts.TryGetValue(key, out int current);
                current++;
                counts[key] = current;
                return current;
            }

            private static int Get(
                IReadOnlyDictionary<string, int> counts,
                string key) =>
                counts.TryGetValue(key, out int value) ? value : 0;

            private static string CleanupKey(
                string key,
                RegionParticipantCleanupReason reason) =>
                key + "|" + (int)reason;

            private static string CreationKey(
                string key,
                int creation) =>
                key + "#" + creation;

            private static void SetFlag(
                ISet<string> values,
                string key,
                bool enabled)
            {
                if (enabled)
                {
                    values.Add(key);
                }
                else
                {
                    values.Remove(key);
                }
            }
        }

        private static class TestIds
        {
            internal static readonly RegionParticipantTypeId TypeId =
                CreateTypeId();
            internal static readonly RegionParticipantModeId ModeId =
                CreateModeId();

            private static RegionParticipantTypeId CreateTypeId()
            {
                RegionParticipantTypeId.TryCreate(
                    "tests.participant",
                    out RegionParticipantTypeId id);
                return id;
            }

            private static RegionParticipantModeId CreateModeId()
            {
                RegionParticipantModeId.TryCreate(
                    "tests.default",
                    out RegionParticipantModeId id);
                return id;
            }
        }
    }

    public sealed class RegionCommitDestroyerProbe : MonoBehaviour
    {
        public Behaviour Victim { get; set; }

        private void OnEnable()
        {
            if (Victim != null)
            {
                UnityEngine.Object.DestroyImmediate(Victim);
            }
        }
    }

    public sealed class RegionCommitVictimProbe : MonoBehaviour
    {
    }
}
