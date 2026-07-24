using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
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

        private static TransitionHarness CreateHarness(
            CandidateController controller,
            TimeSpan cleanupTimeout,
            params RegionCompiledParticipantNode[] nodes)
        {
            RegionMainThreadGuard.CaptureCurrentThread();
            Assert.IsTrue(RegionId.TryCreate(
                "world.wilderness",
                out RegionId regionId));
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

            var plan = new RegionCompiledPlan(
                regionId,
                Array.Empty<RegionCompiledTier>(),
                Array.Empty<RegionCompiledChunk>(),
                nodes,
                "tests.plan");
            Assert.IsTrue(RegionTransitionRuntime.TryCreate(
                runtime,
                catalog,
                new[] { plan },
                cleanupTimeout,
                out _,
                out diagnostic),
                diagnostic.Message);
            return new TransitionHarness(
                regionId,
                contentRuntime,
                runtime);
        }

        private static RegionCompiledParticipantNode Node(
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
            Assert.IsTrue(RegionId.TryCreate(
                "world.wilderness",
                out RegionId regionId));
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
                RegionRuntime runtime)
            {
                RegionId = regionId;
                ContentRuntime = contentRuntime;
                Runtime = runtime;
            }

            internal RegionId RegionId { get; }
            internal ContentRuntime ContentRuntime { get; }
            internal RegionRuntime Runtime { get; }

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
            public Type PlanType => typeof(TestPlanBase);

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

                candidate = CandidateController.Active.Create(
                    context.NodeId,
                    typed);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class RecordingCandidate :
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
                if (cleanupInvoked)
                {
                    return UniTask.FromResult(
                        RegionParticipantCleanupResult.Failure(
                            RegionErrors.CleanupBlocked(
                                "Cleanup was invoked more than once.")));
                }

                cleanupInvoked = true;
                controller.RecordCleanup(plan.Key, reason);
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
                if (cleanupInvoked) return;
                cleanupInvoked = true;
                controller.RecordCleanup(
                    plan.Key,
                    RegionParticipantCleanupReason.HostShutdown);
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
            private readonly HashSet<string> prepareFailures =
                new HashSet<string>();
            private readonly HashSet<string> commitFailures =
                new HashSet<string>();
            private string blockedKey;
            private UniTaskCompletionSource<RegionParticipantCleanupResult>
                blockedCleanup;

            internal static CandidateController Active { get; set; }
            internal List<string> RemovedOrder { get; } =
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
                return new RecordingCandidate(
                    this,
                    nodeId,
                    plan,
                    creation);
            }

            internal void RecordPrepare(string key) =>
                Increment(prepares, key);

            internal void RecordCommit(string key) =>
                Increment(commits, key);

            internal void RecordCleanup(
                string key,
                RegionParticipantCleanupReason reason)
            {
                Increment(cleanups, CleanupKey(key, reason));
                if (reason == RegionParticipantCleanupReason.Removed)
                {
                    RemovedOrder.Add(key);
                }
            }

            internal void SetPrepareFailure(string key, bool enabled) =>
                SetFlag(prepareFailures, key, enabled);

            internal void SetCommitFailure(string key, bool enabled) =>
                SetFlag(commitFailures, key, enabled);

            internal bool ShouldPrepareFail(string key) =>
                prepareFailures.Contains(key);

            internal bool ShouldCommitFail(string key) =>
                commitFailures.Contains(key);

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

            internal int CommitCount(string key) =>
                Get(commits, key);

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
}
