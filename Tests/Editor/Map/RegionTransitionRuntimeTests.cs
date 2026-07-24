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
        public IEnumerator FullOnlyDemandResolvesCumulativeFullTierContexts() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "full-context",
                               new StableTestPlan("full-context"))))
                {
                    RegionDemandScope scope =
                        harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Full),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(revision))
                        .Status);

                    Assert.AreEqual(
                        RegionTierId.Full,
                        controller.CreateTier("full-context"));
                    Assert.AreEqual(
                        RegionTierId.Full,
                        controller.PrepareTier("full-context"));
                    Assert.AreEqual(
                        RegionTierId.Full,
                        controller.CommitTier("full-context"));
                    Assert.AreEqual(
                        4,
                        controller.CreateCapabilities(
                            "full-context").Count);
                    Assert.AreEqual(
                        4,
                        controller.PrepareCapabilities(
                            "full-context").Count);
                    RegionCapabilitySet effective =
                        controller.CommitCapabilities(
                            "full-context");
                    Assert.AreEqual(4, effective.Count);
                    Assert.IsTrue(
                        effective.Contains(
                            RegionCapabilityId.Represented));
                    Assert.IsTrue(
                        effective.Contains(
                            RegionCapabilityId.Full));
                    RegionRuntimeRegionSnapshot snapshot =
                        harness.OnlyRegionSnapshot();
                    Assert.AreEqual(
                        RegionTierId.Full,
                        snapshot.DesiredTierId);
                    Assert.AreEqual(
                        RegionTierId.Full,
                        snapshot.CommittedTierId);
                    Assert.AreEqual(
                        4,
                        snapshot.CommittedEffectiveCapabilities
                            .Count);
                }
            });

        [UnityTest]
        public IEnumerator ChunkCoverageResolvesTiersIndependently() =>
            UniTask.ToCoroutine(async () =>
            {
                Assert.IsTrue(
                    RegionId.TryCreate(
                        "world.wilderness",
                        out RegionId regionId));
                RegionChunkId west = ChunkId("west");
                RegionChunkId east = ChunkId("east");
                var plan = new RegionCompiledPlan(
                    regionId,
                    ProfileId("tests.profile.chunks"),
                    CoCoRegionProfile.CurrentSchemaVersion,
                    TestTiers(),
                    new[]
                    {
                        new RegionCompiledChunk(
                            west,
                            default,
                            default),
                        new RegionCompiledChunk(
                            east,
                            default,
                            default)
                    },
                    new[]
                    {
                        ChunkNode(
                            regionId,
                            west,
                            "west",
                            new StableTestPlan("west")),
                        ChunkNode(
                            regionId,
                            east,
                            "east",
                            new StableTestPlan("east"))
                    },
                    Array.Empty<RegionCompiledDependencyRule>(),
                    "tests.chunk-plan");
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           regionId,
                           plan))
                {
                    Assert.IsTrue(
                        RegionCoverage.TryCreateChunks(
                            new[] { west },
                            out RegionCoverage westCoverage));
                    RegionDemandScope westScope =
                        harness.CreateScope("west-player");
                    Assert.IsTrue(westScope.TryDemand(
                        regionId,
                        Capabilities(RegionCapabilityId.Full),
                        westCoverage,
                        out RegionDemandLease westLease,
                        out RegionDemandRevision westRevision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await westLease.WaitUntilReadyAsync(
                            westRevision)).Status);
                    Assert.AreEqual(1, controller.CreateCount("west"));
                    Assert.AreEqual(0, controller.CreateCount("east"));

                    Assert.IsTrue(
                        RegionCoverage.TryCreateChunks(
                            new[] { east },
                            out RegionCoverage eastCoverage));
                    RegionDemandScope eastScope =
                        harness.CreateScope("east-player");
                    Assert.IsTrue(eastScope.TryDemand(
                        regionId,
                        Capabilities(
                            RegionCapabilityId.Background),
                        eastCoverage,
                        out RegionDemandLease eastLease,
                        out RegionDemandRevision eastRevision,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await eastLease.WaitUntilReadyAsync(
                            eastRevision)).Status);

                    Assert.AreEqual(1, controller.CreateCount("west"));
                    Assert.AreEqual(1, controller.CreateCount("east"));
                    Assert.AreEqual(
                        RegionTierId.Full,
                        controller.CommitTier("west"));
                    Assert.AreEqual(
                        RegionTierId.Background,
                        controller.CommitTier("east"));
                    RegionRuntimeRegionSnapshot snapshot =
                        harness.OnlyRegionSnapshot();
                    RegionChunkRuntimeSnapshot westSnapshot =
                        FindChunkSnapshot(snapshot, west);
                    RegionChunkRuntimeSnapshot eastSnapshot =
                        FindChunkSnapshot(snapshot, east);
                    Assert.AreEqual(
                        RegionTierId.Full,
                        westSnapshot.CommittedTierId);
                    Assert.AreEqual(
                        RegionTierId.Background,
                        eastSnapshot.CommittedTierId);
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
        public IEnumerator RejectedRetryDoesNotFailTheActiveRevision() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new CandidateController();
                controller.BlockPrepare("active");
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           Node(
                               "active",
                               new StableTestPlan("active"))))
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
                    UniTask<RegionReadinessResult> readiness =
                        lease.WaitUntilReadyAsync(revision);

                    Assert.IsFalse(harness.Runtime.TryRetryRegion(
                        harness.RegionId,
                        out diagnostic));
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionDemandConflict,
                        diagnostic.Code);

                    controller.CompleteBlockedPrepare();
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await readiness).Status,
                        "A rejected retry must not publish failure into the already-running revision.");
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
                    ProfileId("tests.profile.wilderness"),
                    CoCoRegionProfile.CurrentSchemaVersion,
                    TestTiers(),
                    Array.Empty<RegionCompiledChunk>(),
                    new[]
                    {
                        Node(
                            wildernessId,
                            "shared",
                            new StableTestPlan("shared"))
                    },
                    Array.Empty<RegionCompiledDependencyRule>(),
                    "tests.wilderness");
                var castlePlan = new RegionCompiledPlan(
                    castleId,
                    ProfileId("tests.profile.castle"),
                    CoCoRegionProfile.CurrentSchemaVersion,
                    TestTiers(),
                    Array.Empty<RegionCompiledChunk>(),
                    new[]
                    {
                        Node(
                            castleId,
                            "shared",
                            new StableTestPlan("shared"))
                    },
                    Array.Empty<RegionCompiledDependencyRule>(),
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
        public IEnumerator CrossRegionDependencyWaitsAndReleasesAfterSourceCleanup() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "wilderness",
                        new StableTestPlan("wilderness")));
                var castlePlan = Plan(
                    castleId,
                    "tests.castle",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            wildernessId,
                            RegionCapabilityId.Background)
                    },
                    Node(
                        castleId,
                        "castle",
                        new StableTestPlan("castle")));
                var controller = new CandidateController();
                controller.BlockPrepare("wilderness");
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(5),
                    castleId,
                    wildernessPlan,
                    castlePlan);
                try
                {
                    RegionDemandScope scope =
                        harness.CreateScope("castle-player");
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    await WaitUntilAsync(
                        () => controller.PrepareCount("wilderness") == 1,
                        "The target Region did not begin dependency preparation.");
                    Assert.AreEqual(
                        0,
                        controller.PrepareCount("castle"),
                        "The source must not prepare before its target dependency is Ready.");

                    controller.CompleteBlockedPrepare();
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await castleLease.WaitUntilReadyAsync(revision))
                        .Status);
                    Assert.AreEqual(
                        1,
                        CountDependencyDemands(
                            harness.Runtime.CaptureSnapshot(),
                            wildernessId));

                    controller.BlockFirstCleanup("castle");
                    castleLease.Dispose();
                    await WaitUntilAsync(
                        () => controller.CleanupAsyncInvocationCount(
                                  "castle",
                                  1) == 1,
                        "The source Region did not begin retirement cleanup.");
                    Assert.AreEqual(
                        1,
                        CountDependencyDemands(
                            harness.Runtime.CaptureSnapshot(),
                            wildernessId),
                        "The old target dependency must remain owned while source cleanup is blocked.");

                    controller.CompleteBlockedCleanup();
                    await WaitUntilAsync(
                        () => CountDependencyDemands(
                                  harness.Runtime.CaptureSnapshot(),
                                  wildernessId) == 0,
                        "The target dependency was not released after source cleanup completed.");
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator ReadyDependencyRemainsReadyDuringOtherOwnerUpgrade() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.ready-dependency.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "ready-dependency-target",
                        new SensitiveTestPlan(
                            "ready-dependency-target")));
                RegionCompiledDependencyRule rule = Dependency(
                    RegionCapabilityId.Represented,
                    wildernessId,
                    RegionCapabilityId.Represented);
                var castlePlan = Plan(
                    castleId,
                    "tests.ready-dependency.castle",
                    new[] { rule },
                    Node(
                        castleId,
                        "ready-dependency-source",
                        new SensitiveTestPlan(
                            "ready-dependency-source")));
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(5),
                           castleId,
                           wildernessPlan,
                           castlePlan))
                {
                    RegionDemandScope sourceScope =
                        harness.CreateScope("castle-player");
                    Assert.IsTrue(sourceScope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease sourceLease,
                        out RegionDemandRevision sourceFirst,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await sourceLease.WaitUntilReadyAsync(
                            sourceFirst)).Status);

                    RegionTransitionMonitorRegionSnapshot initialSource =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            castleId);
                    Assert.AreEqual(
                        1,
                        initialSource.Dependencies.Count);
                    RegionDependencyMonitorSnapshot dependency =
                        initialSource.Dependencies[0];
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        dependency.Readiness);
                    long dependencyLeaseSequence =
                        dependency.LeaseSequence;
                    RegionDemandRevision dependencyRevision =
                        dependency.Revision;

                    controller.BlockPrepare(
                        "ready-dependency-target");
                    RegionDemandScope targetScope =
                        harness.CreateScope("wilderness-upgrade");
                    Assert.IsTrue(targetScope.TryDemand(
                        wildernessId,
                        Capabilities(RegionCapabilityId.Full),
                        RegionCoverage.All,
                        out RegionDemandLease targetLease,
                        out RegionDemandRevision targetRevision,
                        out diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => controller.PrepareCount(
                                  "ready-dependency-target") == 2,
                        "The other Owner did not begin the target upgrade.");

                    RegionRuntimeSnapshot upgrading =
                        harness.Runtime.CaptureSnapshot();
                    RegionDemandRuntimeSnapshot retainedDependency =
                        FindDemandByLeaseSequence(
                            upgrading,
                            dependencyLeaseSequence);
                    Assert.AreEqual(
                        dependencyRevision,
                        retainedDependency.Revision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        retainedDependency.Readiness,
                        "Another Owner's target upgrade must not invalidate an already-Ready dependency revision.");
                    Assert.IsFalse(
                        FindDemandByLeaseSequence(
                                upgrading,
                                targetLease.LeaseSequence)
                            .Readiness.HasValue,
                        "The upgrading Owner should remain Pending while its target candidate is blocked.");

                    Assert.IsTrue(sourceLease.TryUpdate(
                        Capabilities(RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision sourceSecond,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await sourceLease.WaitUntilReadyAsync(
                            sourceSecond)).Status,
                        "The source must reuse its still-Ready dependency instead of waiting for an unrelated Owner's upgrade.");
                    Assert.AreEqual(
                        2,
                        controller.PrepareCount(
                            "ready-dependency-source"));
                    Assert.AreEqual(
                        2,
                        controller.PrepareCount(
                            "ready-dependency-target"),
                        "The target upgrade must still be blocked when the source commits its reused dependency.");

                    RegionTransitionMonitorRegionSnapshot reusedSource =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            castleId);
                    Assert.AreEqual(
                        1,
                        reusedSource.Dependencies.Count);
                    Assert.AreEqual(
                        dependencyLeaseSequence,
                        reusedSource.Dependencies[0]
                            .LeaseSequence);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        reusedSource.Dependencies[0].Readiness);

                    controller.CompleteBlockedPrepare();
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await targetLease.WaitUntilReadyAsync(
                            targetRevision)).Status);
                }
            });

        [UnityTest]
        public IEnumerator CrossRegionTargetFailureFailsSourceBeforePrepare() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "wilderness",
                        new StableTestPlan(
                            "wilderness",
                            true)));
                var castlePlan = Plan(
                    castleId,
                    "tests.castle",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            wildernessId,
                            RegionCapabilityId.Represented)
                    },
                    Node(
                        castleId,
                        "castle",
                        new StableTestPlan("castle")));
                var controller = new CandidateController();
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(1),
                           castleId,
                           wildernessPlan,
                           castlePlan))
                {
                    RegionDemandScope scope =
                        harness.CreateScope("castle-player");
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);

                    RegionReadinessResult result =
                        await castleLease.WaitUntilReadyAsync(revision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        result.Status);
                    Assert.AreEqual(
                        0,
                        controller.PrepareCount("castle"),
                        "A failed target must prevent source preparation.");
                    Assert.AreEqual(
                        0,
                        CountDependencyDemands(
                            harness.Runtime.CaptureSnapshot(),
                            wildernessId),
                        "A failed candidate dependency must not leak its Lease.");
                }
            });

        [UnityTest]
        public IEnumerator SupersededSourceReleasesOnlyItsCandidateDependency() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "wilderness",
                        new StableTestPlan("wilderness")));
                var castlePlan = Plan(
                    castleId,
                    "tests.castle",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Full,
                            wildernessId,
                            RegionCapabilityId.Background)
                    },
                    Node(
                        castleId,
                        "castle",
                        new StableTestPlan("castle")));
                var controller = new CandidateController();
                controller.BlockPrepare("wilderness");
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(5),
                    castleId,
                    wildernessPlan,
                    castlePlan);
                try
                {
                    RegionDemandScope scope =
                        harness.CreateScope("castle-player");
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Full),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => controller.PrepareCount("wilderness") == 1,
                        "The candidate dependency did not begin preparation.");

                    Assert.IsTrue(castleLease.TryUpdate(
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Superseded,
                        (await castleLease.WaitUntilReadyAsync(first))
                        .Status);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await castleLease.WaitUntilReadyAsync(second))
                        .Status);
                    await WaitUntilAsync(
                        () => CountDependencyDemands(
                                  harness.Runtime.CaptureSnapshot(),
                                  wildernessId) == 0,
                        "The superseded source generation leaked its dependency Lease.");

                    controller.CompleteBlockedPrepare();
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator SharedAndTransitiveDependenciesKeepIndependentLeasesAndShutdownSourceFirst() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                RegionId chapelId = Region("world.chapel");
                RegionId mineId = Region("world.mine");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "wilderness",
                        new StableTestPlan("wilderness")));
                var castlePlan = Plan(
                    castleId,
                    "tests.castle",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            wildernessId,
                            RegionCapabilityId.Represented)
                    },
                    Node(
                        castleId,
                        "castle",
                        new StableTestPlan("castle")));
                var chapelPlan = Plan(
                    chapelId,
                    "tests.chapel",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            wildernessId,
                            RegionCapabilityId.Represented)
                    },
                    Node(
                        chapelId,
                        "chapel",
                        new StableTestPlan("chapel")));
                var minePlan = Plan(
                    mineId,
                    "tests.mine",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            castleId,
                            RegionCapabilityId.Represented)
                    },
                    Node(
                        mineId,
                        "mine",
                        new StableTestPlan("mine")));
                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(1),
                    mineId,
                    wildernessPlan,
                    castlePlan,
                    chapelPlan,
                    minePlan);
                try
                {
                    RegionDemandScope scope =
                        harness.CreateScope("world-player");
                    Assert.IsTrue(scope.TryDemand(
                        mineId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease mineLease,
                        out RegionDemandRevision mineRevision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.IsTrue(scope.TryDemand(
                        chapelId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease chapelLease,
                        out RegionDemandRevision chapelRevision,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await mineLease.WaitUntilReadyAsync(
                            mineRevision)).Status);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await chapelLease.WaitUntilReadyAsync(
                            chapelRevision)).Status);

                    RegionRuntimeSnapshot snapshot =
                        harness.Runtime.CaptureSnapshot();
                    Assert.AreEqual(
                        2,
                        CountDependencyDemands(
                            snapshot,
                            wildernessId),
                        "Castle and Chapel must retain independent target Leases.");
                    Assert.AreEqual(
                        1,
                        CountDependencyDemands(
                            snapshot,
                            castleId),
                        "Mine must retain its own transitive Castle Lease.");

                    await harness.ShutdownAsync();
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "chapel|1",
                            "mine|1",
                            "castle|1",
                            "wilderness|1"
                        },
                        controller.HostShutdownOrder);
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator ShutdownWaitsForActiveSourceBeforeCleaningTarget() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "wilderness",
                        new StableTestPlan("wilderness")));
                var castlePlan = Plan(
                    castleId,
                    "tests.castle",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            wildernessId,
                            RegionCapabilityId.Represented)
                    },
                    Node(
                        castleId,
                        "castle",
                        new SensitiveTestPlan("castle")));
                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(5),
                    castleId,
                    wildernessPlan,
                    castlePlan);
                try
                {
                    RegionDemandScope scope =
                        harness.CreateScope("castle-player");
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await castleLease.WaitUntilReadyAsync(first))
                        .Status);

                    controller.BlockPrepare("castle");
                    Assert.IsTrue(castleLease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out _,
                        out diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => controller.PrepareCount("castle") == 2,
                        "The replacement source candidate did not enter Prepare.");

                    UniTask shutdown =
                        harness.ShutdownAsync().Preserve();
                    await UniTask.Yield();
                    Assert.AreEqual(
                        0,
                        controller.HostShutdownOrder.Count,
                        "A target cannot be terminal-cleaned while its active source runner is still retained.");

                    controller.CompleteBlockedPrepare();
                    await shutdown;
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "castle|1",
                            "wilderness|1"
                        },
                        controller.HostShutdownOrder);
                }
                finally
                {
                    harness.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator ForceFallbackKeepsLateSourceAheadOfTargetCleanup() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "wilderness",
                        new StableTestPlan("wilderness")));
                var castlePlan = Plan(
                    castleId,
                    "tests.castle",
                    new[]
                    {
                        Dependency(
                            RegionCapabilityId.Represented,
                            wildernessId,
                            RegionCapabilityId.Represented)
                    },
                    Node(
                        castleId,
                        "castle",
                        new SensitiveTestPlan("castle")));
                var controller = new CandidateController();
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(5),
                    castleId,
                    wildernessPlan,
                    castlePlan);
                try
                {
                    RegionDemandScope scope =
                        harness.CreateScope("castle-player");
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await castleLease.WaitUntilReadyAsync(first))
                        .Status);

                    controller.BlockPrepare("castle");
                    Assert.IsTrue(castleLease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out _,
                        out diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => controller.PrepareCount("castle") == 2,
                        "The replacement source candidate did not enter Prepare.");

                    harness.Runtime.ForceShutdown();
                    await UniTask.Yield();
                    Assert.AreEqual(
                        0,
                        controller.HostShutdownOrder.Count,
                        "Force fallback cannot overtake an active source runner to clean its target.");

                    controller.CompleteBlockedPrepare();
                    await WaitUntilAsync(
                        () => controller.HostShutdownOrder.Count == 2,
                        "Ordered terminal fallback did not finish source and target cleanup.");
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "castle|1",
                            "wilderness|1"
                        },
                        controller.HostShutdownOrder);
                    await harness.ShutdownAsync();
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
                            RegionTierId.Represented,
                            Capabilities(
                                RegionCapabilityId.Represented),
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
                            RegionTierId.Represented,
                            Capabilities(
                                RegionCapabilityId.Represented),
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
                            RegionTierId.Represented,
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
                            RegionTierId.Represented,
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
        public IEnumerator RemovedRegionCanRetryBlockedCleanupWithoutDemand() =>
            UniTask.ToCoroutine(async () =>
            {
                const string key = "removed-blocked";
                var controller = new CandidateController();
                controller.BlockFirstCleanup(key);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromMilliseconds(20),
                           Node(
                               key,
                               new StableTestPlan(key))))
                {
                    RegionDemandScope scope =
                        harness.CreateScope("player");
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
                        (await lease.WaitUntilReadyAsync(revision))
                        .Status);

                    lease.Dispose();
                    await WaitUntilAsync(
                        () =>
                        {
                            RegionRuntimeSnapshot snapshot =
                                harness.Runtime.CaptureSnapshot();
                            return snapshot.Regions.Count == 1 &&
                                   snapshot.Regions[0]
                                       .BlockedCleanup;
                        },
                        "The transition to Off did not enter BlockedCleanup.");
                    RegionRuntimeSnapshot blocked =
                        harness.Runtime.CaptureSnapshot();
                    Assert.AreEqual(0, blocked.Demands.Count);
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            key,
                            1));

                    controller.CompleteBlockedCleanup();
                    Assert.IsTrue(harness.Runtime.TryRetryRegion(
                        harness.RegionId,
                        out diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => harness.Runtime.CaptureSnapshot()
                                  .Regions.Count == 0,
                        "The no-Demand Region did not finish its retried Off cleanup.");
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            key,
                            1),
                        "Retry must observe the late cleanup completion without invoking Cleanup twice.");
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            key,
                            RegionParticipantCleanupReason.Removed));
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
        public IEnumerator ForceShutdownInterruptsTerminalAwareCleanup() =>
            UniTask.ToCoroutine(async () =>
            {
                const string key = "terminal-interrupt";
                var controller = new CandidateController();
                controller.BlockFirstCleanup(key);
                controller.SetTerminalCleanupInterrupt(key, true);
                TransitionHarness harness = CreateHarness(
                    controller,
                    TimeSpan.FromSeconds(5),
                    Node(
                        key,
                        new StableTestPlan(key)));
                try
                {
                    RegionDemandScope scope =
                        harness.CreateScope("player");
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
                        (await lease.WaitUntilReadyAsync(revision))
                        .Status);

                    lease.Dispose();
                    await WaitUntilAsync(
                        () => controller
                                  .CleanupAsyncInvocationCount(
                                      key,
                                      1) == 1,
                        "The terminal-aware candidate did not begin cleanup.");
                    Assert.IsFalse(
                        harness.Runtime.CaptureSnapshot()
                            .Regions[0].BlockedCleanup,
                        "The regression must force shutdown before the regular cleanup timeout.");
                    Assert.AreEqual(
                        0,
                        controller
                            .TerminalCleanupInterruptCount(key));
                    Assert.AreEqual(
                        0,
                        controller.TerminalCleanupInvocationCount(
                            key,
                            1));

                    harness.Runtime.ForceShutdown();
                    await WaitUntilAsync(
                        () => controller
                                  .TerminalCleanupInvocationCount(
                                      key,
                                      1) == 1,
                        "Force shutdown remained blocked on an interruptible late cleanup.");
                    Assert.AreEqual(
                        1,
                        controller
                            .TerminalCleanupInterruptCount(key));
                    Assert.AreEqual(
                        1,
                        controller.CleanupAsyncInvocationCount(
                            key,
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

        [UnityTest]
        public IEnumerator AsyncPrepareCanBeSupersededByExternalDemandUpdate() =>
            UniTask.ToCoroutine(async () =>
            {
                const string key = "async-supersede";
                var controller = new CandidateController();
                controller.BlockPrepare(key);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(5),
                           Node(
                               key,
                               new SensitiveTestPlan(key))))
                {
                    RegionDemandScope scope =
                        harness.CreateScope("player");
                    Assert.IsTrue(scope.TryDemand(
                        harness.RegionId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => controller.PrepareCount(key) == 1,
                        "The first candidate did not enter asynchronous Prepare.");

                    Assert.IsTrue(lease.TryUpdate(
                        Capabilities(
                            RegionCapabilityId.Represented,
                            RegionCapabilityId.Background),
                        RegionCoverage.All,
                        out RegionDemandRevision second,
                        out diagnostic),
                        "An external Demand update must remain legal while Prepare is awaiting. " +
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Superseded,
                        (await lease.WaitUntilReadyAsync(first)).Status);

                    controller.CompleteBlockedPrepare();
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(second)).Status);
                    Assert.AreEqual(
                        2,
                        controller.PrepareCount(key));
                    Assert.AreEqual(
                        1,
                        controller.CleanupCount(
                            key,
                            RegionParticipantCleanupReason
                                .CandidateCancelled));
                }
            });

        [UnityTest]
        public IEnumerator DependencyMonitorSnapshotMapsRuleLeaseAndBlocker() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionId wildernessId = Region("world.wilderness");
                RegionId castleId = Region("world.castle");
                var wildernessPlan = Plan(
                    wildernessId,
                    "tests.monitor.wilderness",
                    Array.Empty<RegionCompiledDependencyRule>(),
                    Node(
                        wildernessId,
                        "monitor-target",
                        new StableTestPlan("monitor-target")));
                RegionCompiledDependencyRule rule = Dependency(
                    RegionCapabilityId.Represented,
                    wildernessId,
                    RegionCapabilityId.Background);
                var castlePlan = Plan(
                    castleId,
                    "tests.monitor.castle",
                    new[] { rule },
                    Node(
                        castleId,
                        "monitor-source",
                        new StableTestPlan("monitor-source")));
                var controller = new CandidateController();
                controller.BlockPrepare("monitor-target");
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromSeconds(5),
                           castleId,
                           wildernessPlan,
                           castlePlan))
                {
                    RegionDemandScope scope =
                        harness.CreateScope("monitor-player");
                    Assert.IsTrue(scope.TryDemand(
                        castleId,
                        Capabilities(RegionCapabilityId.Represented),
                        RegionCoverage.All,
                        out RegionDemandLease castleLease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    await WaitUntilAsync(
                        () => controller.PrepareCount(
                                  "monitor-target") == 1,
                        "The target Region did not begin dependency preparation.");
                    Assert.AreEqual(
                        0,
                        controller.PrepareCount("monitor-source"));

                    RegionTransitionMonitorRegionSnapshot waitingRegion =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            castleId);
                    Assert.AreEqual(1, waitingRegion.Dependencies.Count);
                    RegionDependencyMonitorSnapshot waiting =
                        waitingRegion.Dependencies[0];
                    Assert.AreEqual(castleId, waiting.SourceRegionId);
                    Assert.AreEqual(
                        rule.Fingerprint,
                        waiting.RuleFingerprint);
                    Assert.AreEqual(
                        RegionCapabilityId.Represented,
                        waiting.SourceCapability);
                    Assert.AreEqual(
                        wildernessId,
                        waiting.TargetRegionId);
                    Assert.IsTrue(
                        waiting.TargetCapabilities.Contains(
                            RegionCapabilityId.Background));
                    Assert.AreEqual(
                        RegionCoverage.All,
                        waiting.TargetCoverage);
                    Assert.Greater(waiting.LeaseSequence, 0L);
                    Assert.IsTrue(waiting.Revision.IsValid);
                    Assert.IsFalse(waiting.Readiness.HasValue);
                    Assert.IsTrue(waiting.Diagnostic.IsNone);
                    Assert.AreEqual(
                        RegionMonitorDependencyRole.CandidateWaiting,
                        waiting.Role);
                    Assert.IsTrue(waiting.IsBlocker);

                    RegionDemandRuntimeSnapshot dependencyDemand =
                        FindDemandByLeaseSequence(
                            harness.Runtime.CaptureSnapshot(),
                            waiting.LeaseSequence);
                    Assert.AreEqual(
                        "cocoflow.map.dependencies",
                        dependencyDemand.OwnerId.Value);
                    Assert.AreEqual(
                        wildernessId,
                        dependencyDemand.RegionId);
                    Assert.AreEqual(
                        waiting.Revision,
                        dependencyDemand.Revision);

                    controller.CompleteBlockedPrepare();
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await castleLease.WaitUntilReadyAsync(
                            revision)).Status);

                    RegionTransitionMonitorRegionSnapshot readyRegion =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            castleId);
                    Assert.AreEqual(1, readyRegion.Dependencies.Count);
                    RegionDependencyMonitorSnapshot committed =
                        readyRegion.Dependencies[0];
                    Assert.AreEqual(
                        waiting.LeaseSequence,
                        committed.LeaseSequence);
                    Assert.AreEqual(
                        rule.Fingerprint,
                        committed.RuleFingerprint);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        committed.Readiness);
                    Assert.AreEqual(
                        RegionMonitorDependencyRole.Committed,
                        committed.Role);
                    Assert.IsFalse(committed.IsBlocker);

                    Assert.AreEqual(
                        RegionMonitorDependencyRole.CandidateWaiting,
                        waiting.Role,
                        "A captured monitor snapshot must not be mutated when the dependency becomes Ready.");
                    Assert.IsFalse(waiting.Readiness.HasValue);
                    Assert.IsTrue(waiting.IsBlocker);
                }
            });

        [UnityTest]
        public IEnumerator ParticipantMonitorShowsRetiringBlockedCleanupAndPeak() =>
            UniTask.ToCoroutine(async () =>
            {
                const string key = "monitor-sensitive";
                var controller = new CandidateController();
                controller.BlockFirstCleanup(key);
                using (TransitionHarness harness = CreateHarness(
                           controller,
                           TimeSpan.FromMilliseconds(200),
                           Node(
                               key,
                               new SensitiveTestPlan(key))))
                {
                    RegionDemandScope scope =
                        harness.CreateScope("monitor-player");
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
                    await WaitUntilAsync(
                        () => controller.CleanupAsyncInvocationCount(
                                  key,
                                  1) == 1,
                        "The replaced participant did not begin retiring cleanup.");

                    RegionTransitionMonitorRegionSnapshot retiringRegion =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            harness.RegionId);
                    Assert.AreEqual(
                        harness.OnlyRegionSnapshot().DesiredGeneration,
                        retiringRegion.PeakGeneration);
                    Assert.AreEqual(
                        1,
                        retiringRegion.OldNodeCountAtAttemptStart);
                    Assert.AreEqual(
                        2,
                        retiringRegion.OldPlusCandidatePeak);
                    Assert.AreEqual(2, retiringRegion.Participants.Count);
                    RegionParticipantMonitorSnapshot retiring =
                        FindParticipantByRole(
                            retiringRegion,
                            RegionMonitorParticipantRole.Retiring);
                    RegionParticipantMonitorSnapshot committed =
                        FindParticipantByRole(
                            retiringRegion,
                            RegionMonitorParticipantRole.Committed);
                    Assert.AreEqual(
                        RegionParticipantCleanupReason.Replaced,
                        retiring.CleanupReason);
                    Assert.AreNotEqual(
                        retiring.OwnershipSequence,
                        committed.OwnershipSequence);

                    RegionReadinessResult blocked =
                        await lease.WaitUntilReadyAsync(second);
                    Assert.AreEqual(
                        RegionReadinessStatus.Failed,
                        blocked.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCleanupBlocked,
                        blocked.Diagnostic.Code);
                    RegionTransitionMonitorRegionSnapshot blockedRegion =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            harness.RegionId);
                    Assert.AreEqual(
                        2,
                        blockedRegion.OldPlusCandidatePeak);
                    Assert.AreEqual(2, blockedRegion.Participants.Count);
                    RegionParticipantMonitorSnapshot blockedParticipant =
                        FindParticipantByRole(
                            blockedRegion,
                            RegionMonitorParticipantRole.BlockedCleanup);
                    Assert.AreEqual(
                        retiring.OwnershipSequence,
                        blockedParticipant.OwnershipSequence);
                    Assert.AreEqual(
                        RegionParticipantCleanupReason.Replaced,
                        blockedParticipant.CleanupReason);

                    Assert.AreEqual(
                        RegionMonitorParticipantRole.Retiring,
                        retiring.Role,
                        "The earlier monitor snapshot must remain immutable after cleanup becomes blocked.");
                    Assert.AreEqual(
                        RegionParticipantCleanupReason.Replaced,
                        retiring.CleanupReason);

                    controller.CompleteBlockedCleanup();
                    Assert.IsTrue(harness.Runtime.TryRetryRegion(
                        harness.RegionId,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await lease.WaitUntilReadyAsync(second)).Status);
                    RegionTransitionMonitorRegionSnapshot recoveredRegion =
                        FindMonitorRegion(
                            harness.TransitionRuntime
                                .CaptureMonitorRegions(),
                            harness.RegionId);
                    Assert.AreEqual(1, recoveredRegion.Participants.Count);
                    Assert.AreEqual(
                        RegionMonitorParticipantRole.Committed,
                        recoveredRegion.Participants[0].Role);
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
                ProfileId("tests.profile.default"),
                CoCoRegionProfile.CurrentSchemaVersion,
                TestTiers(),
                Array.Empty<RegionCompiledChunk>(),
                nodes,
                Array.Empty<RegionCompiledDependencyRule>(),
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
            return Node(
                nodeId,
                plan,
                requirement,
                phase);
        }

        private static RegionCompiledParticipantNode ChunkNode(
            RegionId regionId,
            RegionChunkId chunkId,
            string slotValue,
            TestPlanBase plan)
        {
            Assert.IsTrue(
                RegionParticipantSlotId.TryCreate(
                    slotValue,
                    out RegionParticipantSlotId slotId));
            Assert.IsTrue(
                RegionPlanNodeId.TryCreateChunk(
                    regionId,
                    chunkId,
                    slotId,
                    out RegionPlanNodeId nodeId));
            return Node(
                nodeId,
                plan,
                RegionParticipantRequirement.Required,
                RegionParticipantPhase.Simulation);
        }

        private static RegionCompiledParticipantNode Node(
            RegionPlanNodeId nodeId,
            TestPlanBase plan,
            RegionParticipantRequirement requirement,
            RegionParticipantPhase phase)
        {
            IList<RegionCompiledTier> tiers =
                TestTiers();
            var variants =
                new List<RegionCompiledParticipantVariant>(
                    tiers.Count - 1);
            for (int tierIndex = 1;
                 tierIndex < tiers.Count;
                 tierIndex++)
            {
                RegionCompiledTier tier = tiers[tierIndex];
                string variantFingerprint = plan.Fingerprint;
                if (plan is IRegionCapabilitySensitivePlan)
                {
                    for (int capabilityIndex = 0;
                         capabilityIndex < tier.Capabilities.Count;
                         capabilityIndex++)
                    {
                        variantFingerprint += "|" +
                            tier.Capabilities.Capabilities[
                                capabilityIndex].Value;
                    }
                }
                variants.Add(
                    new RegionCompiledParticipantVariant(
                        tier.TierId,
                        TestIds.ModeId,
                        tier.Capabilities,
                        plan,
                        variantFingerprint));
            }

            return new RegionCompiledParticipantNode(
                nodeId,
                TestIds.TypeId,
                phase,
                0,
                requirement,
                Array.Empty<RegionPlanNodeId>(),
                variants,
                string.Empty,
                default,
                "node|" + plan.Fingerprint);
        }

        private static RegionChunkId ChunkId(string value)
        {
            Assert.IsTrue(
                RegionChunkId.TryCreate(
                    value,
                    out RegionChunkId chunkId));
            return chunkId;
        }

        private static RegionId Region(string value)
        {
            Assert.IsTrue(
                RegionId.TryCreate(
                    value,
                    out RegionId regionId));
            return regionId;
        }

        private static RegionCompiledPlan Plan(
            RegionId regionId,
            string fingerprint,
            IList<RegionCompiledDependencyRule> dependencyRules,
            params RegionCompiledParticipantNode[] nodes) =>
            new RegionCompiledPlan(
                regionId,
                ProfileId("profile." + fingerprint),
                CoCoRegionProfile.CurrentSchemaVersion,
                TestTiers(),
                Array.Empty<RegionCompiledChunk>(),
                nodes,
                dependencyRules,
                fingerprint);

        private static RegionCompiledDependencyRule Dependency(
            RegionCapabilityId sourceCapability,
            RegionId targetRegionId,
            params RegionCapabilityId[] targetCapabilities)
        {
            RegionCapabilitySet capabilities =
                Capabilities(targetCapabilities);
            string fingerprint =
                RegionDependencyCompiler.BuildFingerprint(
                    sourceCapability,
                    targetRegionId,
                    capabilities,
                    RegionCoverage.All);
            return new RegionCompiledDependencyRule(
                sourceCapability,
                targetRegionId,
                capabilities,
                RegionCoverage.All,
                fingerprint);
        }

        private static int CountDependencyDemands(
            RegionRuntimeSnapshot snapshot,
            RegionId targetRegionId)
        {
            int count = 0;
            for (int index = 0;
                 index < snapshot.Demands.Count;
                 index++)
            {
                RegionDemandRuntimeSnapshot demand =
                    snapshot.Demands[index];
                if (demand.RegionId == targetRegionId &&
                    string.Equals(
                        demand.OwnerId.Value,
                        "cocoflow.map.dependencies",
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static RegionChunkRuntimeSnapshot FindChunkSnapshot(
            RegionRuntimeRegionSnapshot region,
            RegionChunkId chunkId)
        {
            for (int index = 0;
                 index < region.Chunks.Count;
                 index++)
            {
                if (region.Chunks[index].ChunkId == chunkId)
                {
                    return region.Chunks[index];
                }
            }

            Assert.Fail(
                "Missing Chunk snapshot for '" +
                chunkId.Value + "'.");
            return default;
        }

        private static RegionTransitionMonitorRegionSnapshot
            FindMonitorRegion(
                IReadOnlyList<RegionTransitionMonitorRegionSnapshot>
                    regions,
                RegionId regionId)
        {
            for (int index = 0; index < regions.Count; index++)
            {
                if (regions[index].RegionId == regionId)
                {
                    return regions[index];
                }
            }

            Assert.Fail(
                "Missing transition monitor snapshot for Region '" +
                regionId.Value + "'.");
            return null;
        }

        private static RegionParticipantMonitorSnapshot
            FindParticipantByRole(
                RegionTransitionMonitorRegionSnapshot region,
                RegionMonitorParticipantRole role)
        {
            for (int index = 0;
                 index < region.Participants.Count;
                 index++)
            {
                if (region.Participants[index].Role == role)
                {
                    return region.Participants[index];
                }
            }

            Assert.Fail(
                "Missing participant monitor role '" +
                role + "' for Region '" +
                region.RegionId.Value + "'.");
            return default;
        }

        private static RegionDemandRuntimeSnapshot
            FindDemandByLeaseSequence(
                RegionRuntimeSnapshot snapshot,
                long leaseSequence)
        {
            for (int index = 0;
                 index < snapshot.Demands.Count;
                 index++)
            {
                if (snapshot.Demands[index].LeaseSequence ==
                    leaseSequence)
                {
                    return snapshot.Demands[index];
                }
            }

            Assert.Fail(
                "Missing Region Demand snapshot for Lease sequence '" +
                leaseSequence + "'.");
            return default;
        }

        private static RegionProfileId ProfileId(string value)
        {
            Assert.IsTrue(
                RegionProfileId.TryCreate(
                    value,
                    out RegionProfileId profileId));
            return profileId;
        }

        private static IList<RegionCompiledTier>
            TestTiers()
        {
            return new[]
            {
                new RegionCompiledTier(
                    0,
                    RegionTierId.Off,
                    "Off",
                    RegionCapabilitySet.Empty),
                new RegionCompiledTier(
                    1,
                    RegionTierId.Represented,
                    "Represented",
                    Capabilities(
                        RegionCapabilityId.Represented)),
                new RegionCompiledTier(
                    2,
                    RegionTierId.Background,
                    "Background",
                    Capabilities(
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background)),
                new RegionCompiledTier(
                    3,
                    RegionTierId.Enterable,
                    "Enterable",
                    Capabilities(
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable)),
                new RegionCompiledTier(
                    4,
                    RegionTierId.Full,
                    "Full",
                    StandardCapabilities())
            };
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
                controller.RecordCreateContext(
                    typed.Key,
                    context.TierId,
                    context.Capabilities);
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
            IRegionParticipantTerminalCleanup,
            IRegionParticipantTerminalCleanupInterrupt
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
                controller.RecordPrepare(
                    plan.Key,
                    context.TierId,
                    context.Capabilities);
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
                controller.RecordCommit(
                    plan.Key,
                    context.TierId,
                    context.Capabilities);
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

            void IRegionParticipantTerminalCleanupInterrupt.
                InterruptPendingCleanupForTerminalFallback() =>
                controller.InterruptPendingCleanup(plan.Key);
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
            private readonly Dictionary<string, RegionTierId> createTiers =
                new Dictionary<string, RegionTierId>();
            private readonly Dictionary<string, RegionTierId> prepareTiers =
                new Dictionary<string, RegionTierId>();
            private readonly Dictionary<string, RegionTierId> commitTiers =
                new Dictionary<string, RegionTierId>();
            private readonly Dictionary<string, RegionCapabilitySet>
                createCapabilities =
                    new Dictionary<string, RegionCapabilitySet>();
            private readonly Dictionary<string, RegionCapabilitySet>
                prepareCapabilities =
                    new Dictionary<string, RegionCapabilitySet>();
            private readonly Dictionary<string, RegionCapabilitySet>
                commitCapabilities =
                    new Dictionary<string, RegionCapabilitySet>();
            private readonly Dictionary<string, int> cleanups =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> cleanupAsyncInvocations =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int> terminalCleanupInvocations =
                new Dictionary<string, int>();
            private readonly Dictionary<string, int>
                terminalCleanupInterrupts =
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
            private readonly HashSet<string>
                terminalCleanupInterruptible =
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

            internal void RecordCreateContext(
                string key,
                RegionTierId tierId,
                RegionCapabilitySet capabilities)
            {
                createTiers[key] = tierId;
                createCapabilities[key] = capabilities;
            }

            internal void RecordPrepare(
                string key,
                RegionTierId tierId,
                RegionCapabilitySet capabilities)
            {
                Increment(prepares, key);
                prepareTiers[key] = tierId;
                prepareCapabilities[key] = capabilities;
            }

            internal void RecordCommit(
                string key,
                RegionTierId tierId,
                RegionCapabilitySet capabilities)
            {
                Increment(commits, key);
                commitTiers[key] = tierId;
                commitCapabilities[key] = capabilities;
            }

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

            internal void SetTerminalCleanupInterrupt(
                string key,
                bool enabled) =>
                SetFlag(
                    terminalCleanupInterruptible,
                    key,
                    enabled);

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

            internal void InterruptPendingCleanup(string key)
            {
                if (!terminalCleanupInterruptible.Contains(key))
                {
                    return;
                }

                Increment(terminalCleanupInterrupts, key);
                if (string.Equals(
                        key,
                        blockedKey,
                        StringComparison.Ordinal))
                {
                    blockedCleanup.TrySetResult(
                        RegionParticipantCleanupResult.Failure(
                            RegionErrors.CleanupBlocked(
                                "Terminal fallback interrupted pending cleanup.")));
                }
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

            internal RegionTierId CreateTier(string key) =>
                createTiers[key];

            internal RegionTierId PrepareTier(string key) =>
                prepareTiers[key];

            internal RegionTierId CommitTier(string key) =>
                commitTiers[key];

            internal RegionCapabilitySet CreateCapabilities(
                string key) =>
                createCapabilities[key];

            internal RegionCapabilitySet PrepareCapabilities(
                string key) =>
                prepareCapabilities[key];

            internal RegionCapabilitySet CommitCapabilities(
                string key) =>
                commitCapabilities[key];

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

            internal int TerminalCleanupInterruptCount(
                string key) =>
                Get(terminalCleanupInterrupts, key);

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
