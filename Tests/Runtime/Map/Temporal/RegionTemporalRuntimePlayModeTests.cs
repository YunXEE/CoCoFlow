using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Runtime.Modules.Map.Temporal.Tests
{
    public sealed class RegionTemporalRuntimePlayModeTests
    {
        [UnityTest]
        public IEnumerator HistoricalAvailabilityIsRetainedWithoutMapReplay() =>
            UniTask.ToCoroutine(RunHistoricalAvailabilityAsync);

        [UnityTest]
        public IEnumerator PreviewDoesNotMutateMapDemandOrDispatchTransition() =>
            UniTask.ToCoroutine(RunPreviewPurityAsync);

        [UnityTest]
        public IEnumerator BranchTruncateDefersRetentionDecreaseUntilDrain() =>
            UniTask.ToCoroutine(RunDeferredBranchCleanupAsync);

        [UnityTest]
        public IEnumerator PreviewDefersLogicalDemandMutationsUntilOneFlush() =>
            UniTask.ToCoroutine(RunDeferredDemandMutationAsync);

        [UnityTest]
        public IEnumerator StandaloneCorrectionHoldsBarrierUntilFinish() =>
            UniTask.ToCoroutine(RunStandaloneCorrectionBarrierAsync);

        [UnityTest]
        public IEnumerator TemporalRetainsResolvedEffectiveCapabilities() =>
            UniTask.ToCoroutine(RunEffectiveCapabilityRetentionAsync);

        [UnityTest]
        public IEnumerator AuthorityResetSeedsEmptyImportedBaselineAndDefersTemporalRelease() =>
            UniTask.ToCoroutine(RunAuthorityResetAsync);

        private static async UniTask RunAuthorityResetAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(FullCapabilities());
            try
            {
                CaptureForward(
                    fixture.Temporal,
                    CreateFrame(60UL));
                Assert.That(
                    fixture.GameplayLease.TryUpdate(
                        RepresentedCapabilities(),
                        fixture.Coverage,
                        out _,
                        out CoCoDiagnostic update),
                    Is.True,
                    update.Message);
                CaptureForward(
                    fixture.Temporal,
                    CreateFrame(61UL));
                Assert.That(fixture.Temporal.HistoryCount, Is.EqualTo(2));
                Assert.That(
                    FindTemporalDemand(
                            fixture.Region.CaptureSnapshot())
                        .Capabilities.Contains(RegionCapabilityId.Full),
                    Is.True);

                CoCoTemporalFrameInfo imported = CreateFrame(70UL);
                Assert.That(
                    fixture.Temporal.TryPrepareAuthorityReset(
                        imported,
                        out CoCoDiagnostic prepareCancel),
                    Is.True,
                    prepareCancel.Message);
                Assert.That(
                    fixture.Temporal.HistoryCount,
                    Is.EqualTo(2),
                    "Preparing an imported baseline must not publish or release retention.");
                fixture.Temporal.CancelPreparedAuthorityResetNoFail();
                Assert.That(fixture.Temporal.HistoryCount, Is.EqualTo(2));
                Assert.That(
                    CountTemporalDemands(
                        fixture.Region.CaptureSnapshot()),
                    Is.EqualTo(1));

                Assert.That(
                    fixture.Temporal.TryPrepareAuthorityReset(
                        imported,
                        out CoCoDiagnostic prepare),
                    Is.True,
                    prepare.Message);
                int transitionsBeforeCommit = fixture.Sink.RequestCount;
                fixture.Temporal.CommitPreparedAuthorityResetNoFail();

                Assert.That(fixture.Temporal.HistoryCount, Is.EqualTo(1));
                AssertImportedEmptyHead(fixture.Temporal, imported);
                Assert.That(
                    ReadPendingTargetCount(fixture.Temporal),
                    Is.Zero,
                    "Authority reset must publish an empty future Temporal retention target.");
                Assert.That(
                    CountTemporalDemands(
                        fixture.Region.CaptureSnapshot()),
                    Is.EqualTo(1),
                    "Publishing the baseline must not release a temporal Lease inside the authority callback.");
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBeforeCommit));

                fixture.Temporal.DrainPublishedCleanupNoFail();
                RegionRuntimeSnapshot afterDrain =
                    fixture.Region.CaptureSnapshot();
                Assert.That(CountTemporalDemands(afterDrain), Is.Zero);
                Assert.That(
                    afterDrain.Demands.Count,
                    Is.EqualTo(1),
                    "Gameplay ownership must survive removal of the independent Temporal retention Scope.");
                Assert.That(
                    afterDrain.Regions[0].CommittedCapabilities
                        .Contains(RegionCapabilityId.Represented),
                    Is.True);
                Assert.That(
                    afterDrain.Regions[0].CommittedCapabilities
                        .Contains(RegionCapabilityId.Full),
                    Is.False,
                    "The empty imported baseline must not reseed old effective Temporal capabilities.");
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.GreaterThanOrEqualTo(transitionsBeforeCommit),
                    "Temporal cleanup may publish the independent gameplay resolution, but must not recreate old retention.");

                fixture.Region.FlushDeferredTransitionsNoThrow();
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.GreaterThanOrEqualTo(transitionsBeforeCommit));

                CaptureForward(
                    fixture.Temporal,
                    CreateFrame(71UL));
                Assert.That(
                    fixture.Temporal.HistoryCount,
                    Is.EqualTo(2),
                    "The first post-import Tick must extend the new timeline instead of reviving old history.");
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static async UniTask RunEffectiveCapabilityRetentionAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(FullRequirementOnly());
            try
            {
                RegionRuntimeRegionState committed =
                    fixture.Region.CaptureSnapshot().Regions[0];
                Assert.That(
                    committed.CommittedCapabilities.Count,
                    Is.EqualTo(1),
                    "The public snapshot must preserve the raw Demand requirement.");
                Assert.That(
                    committed.CommittedEffectiveCapabilities.Count,
                    Is.EqualTo(4),
                    "The transition authority must publish the cumulative resolved tier.");

                CaptureForward(
                    fixture.Temporal,
                    CreateFrame(50UL));
                fixture.GameplayLease.Dispose();

                RegionDemandRuntimeSnapshot retained =
                    FindTemporalDemand(
                        fixture.Region.CaptureSnapshot());
                Assert.That(
                    retained.Capabilities.Count,
                    Is.EqualTo(4),
                    "Temporal retention must record effective capabilities, not the raw {Full} requirement.");
                Assert.That(
                    retained.Capabilities.Contains(
                        RegionCapabilityId.Represented),
                    Is.True);
                Assert.That(
                    retained.Capabilities.Contains(
                        RegionCapabilityId.Background),
                    Is.True);
                Assert.That(
                    retained.Capabilities.Contains(
                        RegionCapabilityId.Enterable),
                    Is.True);
                Assert.That(
                    retained.Capabilities.Contains(
                        RegionCapabilityId.Full),
                    Is.True);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static async UniTask RunHistoricalAvailabilityAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(FullCapabilities());
            try
            {
                CoCoTemporalFrameInfo captured = CreateFrame(1UL);
                CaptureForward(fixture.Temporal, captured);

                fixture.GameplayLease.Dispose();

                RegionRuntimeSnapshot retained =
                    fixture.Region.CaptureSnapshot();
                Assert.That(retained.Demands.Count, Is.EqualTo(1));
                Assert.That(
                    retained.Demands[0].OwnerId.Value,
                    Does.StartWith("cocoflow.map.temporal."));
                Assert.That(
                    retained.Regions[0].CommittedCapabilities
                        .Contains(RegionCapabilityId.Full),
                    Is.True,
                    "Temporal retention must preserve availability after the gameplay Lease releases.");

                Assert.That(
                    fixture.Temporal.TryBeginPreview(
                        fixture.Temporal.HistoryCount,
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.Temporal.TryPrepareProjection(
                        CoCoContextRestoreApplyKind.Preview,
                        0,
                        captured,
                        captured.TickFrame,
                        out CoCoDiagnostic projection),
                    Is.True,
                    projection.Message);
                Assert.That(
                    fixture.Temporal.TryApplyPreparedAvailabilityBarrier(
                        CoCoContextRestoreApplyKind.Preview,
                        out CoCoDiagnostic barrier),
                    Is.True,
                    barrier.Message);

                RegionRuntimeSnapshot afterBarrier =
                    fixture.Region.CaptureSnapshot();
                Assert.That(
                    DemandSignature(afterBarrier),
                    Is.EqualTo(DemandSignature(retained)),
                    "The availability barrier validates committed Map state; it must not replay or rewrite it.");

                fixture.Temporal.FinishProjectionNoFail(true);
                fixture.Temporal.CompletePreviewNoFail(
                    CoCoContextRestoreApplyKind.Cancel);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static async UniTask RunPreviewPurityAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(FullCapabilities());
            try
            {
                CoCoTemporalFrameInfo captured = CreateFrame(10UL);
                CaptureForward(fixture.Temporal, captured);

                RegionRuntimeSnapshot before =
                    fixture.Region.CaptureSnapshot();
                string demandBefore = DemandSignature(before);
                int transitionsBefore = fixture.Sink.RequestCount;

                Assert.That(
                    fixture.Temporal.TryBeginPreview(
                        fixture.Temporal.HistoryCount,
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.Temporal.TryPrepareProjection(
                        CoCoContextRestoreApplyKind.Preview,
                        0,
                        captured,
                        captured.TickFrame,
                        out CoCoDiagnostic projection),
                    Is.True,
                    projection.Message);
                Assert.That(
                    fixture.Temporal.TryApplyPreparedAvailabilityBarrier(
                        CoCoContextRestoreApplyKind.Preview,
                        out CoCoDiagnostic barrier),
                    Is.True,
                    barrier.Message);
                fixture.Temporal.FinishProjectionNoFail(true);
                fixture.Temporal.CompletePreviewNoFail(
                    CoCoContextRestoreApplyKind.Cancel);

                RegionRuntimeSnapshot after =
                    fixture.Region.CaptureSnapshot();
                Assert.That(
                    DemandSignature(after),
                    Is.EqualTo(demandBefore),
                    "Preview must not create, update, or release a Map Demand.");
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore),
                    "Preview must not dispatch a Region transition or tier commit.");
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static async UniTask RunDeferredBranchCleanupAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(RepresentedCapabilities());
            try
            {
                CoCoTemporalFrameInfo representedFrame = CreateFrame(20UL);
                CaptureForward(fixture.Temporal, representedFrame);

                Assert.That(
                    fixture.GameplayLease.TryUpdate(
                        FullCapabilities(),
                        fixture.Coverage,
                        out _,
                        out CoCoDiagnostic fullUpdate),
                    Is.True,
                    fullUpdate.Message);
                CoCoTemporalFrameInfo fullFrame = CreateFrame(21UL);
                CaptureForward(fixture.Temporal, fullFrame);

                Assert.That(
                    fixture.GameplayLease.TryUpdate(
                        RepresentedCapabilities(),
                        fixture.Coverage,
                        out _,
                        out CoCoDiagnostic representedUpdate),
                    Is.True,
                    representedUpdate.Message);

                RegionDemandRuntimeSnapshot temporalBefore =
                    FindTemporalDemand(fixture.Region.CaptureSnapshot());
                Assert.That(
                    temporalBefore.Capabilities
                        .Contains(RegionCapabilityId.Full),
                    Is.True);
                int transitionsBeforeBranch = fixture.Sink.RequestCount;

                Assert.That(
                    fixture.Temporal.TryBeginPreview(
                        fixture.Temporal.HistoryCount,
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.Temporal.TryPrepareBranchCapture(
                        1,
                        CreateFrame(22UL),
                        out CoCoDiagnostic branch),
                    Is.True,
                    branch.Message);
                fixture.Temporal.PublishBranchCaptureNoFail();
                fixture.Temporal.CompletePreviewNoFail(
                    CoCoContextRestoreApplyKind.Confirm);

                RegionDemandRuntimeSnapshot beforeDrain =
                    FindTemporalDemand(fixture.Region.CaptureSnapshot());
                Assert.That(
                    beforeDrain.Revision,
                    Is.EqualTo(temporalBefore.Revision));
                Assert.That(
                    beforeDrain.Capabilities
                        .Contains(RegionCapabilityId.Full),
                    Is.True,
                    "Publishing the truncated branch must not lower retention inside the callback.");
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBeforeBranch));

                fixture.Temporal.DrainPublishedCleanupNoFail();

                RegionDemandRuntimeSnapshot afterDrain =
                    FindTemporalDemand(fixture.Region.CaptureSnapshot());
                Assert.That(afterDrain.Revision, Is.Not.EqualTo(beforeDrain.Revision));
                Assert.That(
                    afterDrain.Capabilities
                        .Contains(RegionCapabilityId.Full),
                    Is.False);
                Assert.That(
                    afterDrain.Capabilities
                        .Contains(RegionCapabilityId.Represented),
                    Is.True);
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBeforeBranch),
                    "The cleanup callback may update logical retention, but dispatch must wait until the callback stack has exited.");

                fixture.Region.FlushDeferredTransitionsNoThrow();
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.GreaterThan(transitionsBeforeBranch),
                    "LateUpdate-equivalent flush must publish the final retention resolution.");
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static async UniTask RunDeferredDemandMutationAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(FullCapabilities());
            RegionDemandScope transientScope = null;
            try
            {
                CaptureForward(fixture.Temporal, CreateFrame(30UL));
                int transitionsBefore = fixture.Sink.RequestCount;
                Assert.That(
                    fixture.Temporal.TryBeginPreview(
                        fixture.Temporal.HistoryCount,
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);

                Assert.That(
                    fixture.GameplayLease.TryUpdate(
                        RepresentedCapabilities(),
                        fixture.Coverage,
                        out RegionDemandRevision updatedRevision,
                        out CoCoDiagnostic update),
                    Is.True,
                    update.Message);
                UniTask<RegionReadinessResult> updatedReadiness =
                    fixture.GameplayLease.WaitUntilReadyAsync(
                        updatedRevision);

                Assert.That(
                    RegionDemandOwnerId.TryCreate(
                        "tests.map.temporal.transient." +
                        Guid.NewGuid().ToString("N"),
                        out RegionDemandOwnerId transientOwner),
                    Is.True);
                Assert.That(
                    fixture.Region.TryCreateDemandScope(
                        transientOwner,
                        out transientScope,
                        out CoCoDiagnostic scopeDiagnostic),
                    Is.True,
                    scopeDiagnostic.Message);
                Assert.That(
                    transientScope.TryDemand(
                        fixture.RegionId,
                        FullCapabilities(),
                        fixture.Coverage,
                        out RegionDemandLease transientLease,
                        out RegionDemandRevision transientRevision,
                        out CoCoDiagnostic transientDiagnostic),
                    Is.True,
                    transientDiagnostic.Message);
                UniTask<RegionReadinessResult> transientReadiness =
                    transientLease.WaitUntilReadyAsync(
                        transientRevision);
                transientLease.Dispose();
                Assert.That(
                    (await transientReadiness).Status,
                    Is.EqualTo(RegionReadinessStatus.Superseded));

                Assert.That(
                    fixture.Region.TryRetryRegion(
                        fixture.RegionId,
                        out CoCoDiagnostic retryDiagnostic),
                    Is.False);
                Assert.That(
                    retryDiagnostic.Code,
                    Is.EqualTo(
                        CoCoDiagnosticCode.RegionTemporalConflict));
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore),
                    "Preview mutations and rejected Retry must have no transition side effects.");

                fixture.Temporal.CompletePreviewNoFail(
                    CoCoContextRestoreApplyKind.Cancel);
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore),
                    "Preview completion must not dispatch while still on the Temporal callback stack.");

                fixture.Region.FlushDeferredTransitionsNoThrow();
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore + 1),
                    "One flush must coalesce all Preview mutations to the final Region resolution.");
                Assert.That(
                    (await updatedReadiness).Status,
                    Is.EqualTo(RegionReadinessStatus.Ready));
            }
            finally
            {
                transientScope?.Dispose();
                await CleanupFixtureAsync(fixture);
            }
        }

        private static async UniTask RunStandaloneCorrectionBarrierAsync()
        {
            TemporalRuntimeFixture fixture =
                CreateFixture(RepresentedCapabilities());
            try
            {
                CoCoTemporalFrameInfo frame = CreateFrame(40UL);
                Assert.That(
                    fixture.Temporal.TryPrepareProjection(
                        CoCoContextRestoreApplyKind.Correction,
                        0,
                        frame,
                        frame.TickFrame,
                        out CoCoDiagnostic correction),
                    Is.True,
                    correction.Message);
                int transitionsBefore = fixture.Sink.RequestCount;

                Assert.That(
                    fixture.GameplayLease.TryUpdate(
                        FullCapabilities(),
                        fixture.Coverage,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic update),
                    Is.True,
                    update.Message);
                UniTask<RegionReadinessResult> readiness =
                    fixture.GameplayLease.WaitUntilReadyAsync(revision);
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore));

                fixture.Temporal.FinishProjectionNoFail(true);
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore),
                    "Correction completion must leave dispatch queued until the callback stack exits.");

                fixture.Region.FlushDeferredTransitionsNoThrow();
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore + 1));
                Assert.That(
                    (await readiness).Status,
                    Is.EqualTo(RegionReadinessStatus.Ready));
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static TemporalRuntimeFixture CreateFixture(
            RegionCapabilitySet initialCapabilities)
        {
            RegionMainThreadGuard.CaptureCurrentThread();
            Assert.That(
                ContentRuntime.TryCreate(
                    out ContentRuntime content,
                    out CoCoDiagnostic contentDiagnostic),
                Is.True,
                contentDiagnostic.Message);
            Assert.That(
                RegionRuntime.TryCreate(
                    content,
                    out RegionRuntime region,
                    out CoCoDiagnostic regionDiagnostic),
                Is.True,
                regionDiagnostic.Message);

            Assert.That(
                RegionId.TryCreate(
                    "tests.map.temporal.wilderness",
                    out RegionId regionId),
                Is.True);
            Assert.That(
                RegionChunkId.TryCreate(
                    "west",
                    out RegionChunkId chunkId),
                Is.True);
            Assert.That(
                RegionCoverage.TryCreateChunks(
                    new[] { chunkId },
                    out RegionCoverage coverage),
                Is.True);

            var sink = new ImmediateTransitionSink(region);
            sink.Configure(regionId, chunkId);
            Assert.That(
                region.TryAttachTransitionSink(
                    sink,
                    out CoCoDiagnostic sinkDiagnostic),
                Is.True,
                sinkDiagnostic.Message);

            Assert.That(
                RegionDemandOwnerId.TryCreate(
                    "tests.map.temporal.gameplay." +
                    Guid.NewGuid().ToString("N"),
                    out RegionDemandOwnerId ownerId),
                Is.True);
            Assert.That(
                region.TryCreateDemandScope(
                    ownerId,
                    out RegionDemandScope gameplayScope,
                    out CoCoDiagnostic scopeDiagnostic),
                Is.True,
                scopeDiagnostic.Message);
            Assert.That(
                gameplayScope.TryDemand(
                    regionId,
                    initialCapabilities,
                    coverage,
                    out RegionDemandLease gameplayLease,
                    out _,
                    out CoCoDiagnostic demandDiagnostic),
                Is.True,
                demandDiagnostic.Message);

            var hostObject =
                new GameObject("Pre10 Map Temporal Runtime Test Host");
            hostObject.SetActive(false);
            CoCoStateGraphHost host =
                hostObject.AddComponent<CoCoStateGraphHost>();
            Assert.That(
                RegionTemporalRuntime.TryCreate(
                    host,
                    region,
                    3,
                    out RegionTemporalRuntime temporal,
                    out CoCoDiagnostic temporalDiagnostic),
                Is.True,
                temporalDiagnostic.Message);

            return new TemporalRuntimeFixture(
                hostObject,
                content,
                region,
                sink,
                gameplayScope,
                gameplayLease,
                temporal,
                regionId,
                chunkId,
                coverage);
        }

        private static async UniTask CleanupFixtureAsync(
            TemporalRuntimeFixture fixture)
        {
            if (fixture == null) return;

            fixture.Temporal.Dispose();
            fixture.GameplayScope.Dispose();
            await fixture.Region.ShutdownAsync();
            await fixture.Content.ShutdownAsync();
            if (fixture.HostObject != null)
            {
                Object.DestroyImmediate(fixture.HostObject);
            }
        }

        private static void CaptureForward(
            RegionTemporalRuntime temporal,
            in CoCoTemporalFrameInfo frame)
        {
            Assert.That(
                temporal.TryPrepareForwardCapture(
                    frame,
                    out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
            temporal.PublishForwardCaptureNoFail();
            temporal.DrainPublishedCleanupNoFail();
        }

        private static CoCoTemporalFrameInfo CreateFrame(ulong sequence)
        {
            Assert.That(
                CoCoGraphInstanceId.TryCreate(
                    0xC0C010UL,
                    out CoCoGraphInstanceId graphId),
                Is.True);
            Assert.That(
                CoCoTimelineId.TryCreate(
                    0xC0C010UL,
                    1UL,
                    out CoCoTimelineId timelineId),
                Is.True);
            Assert.That(
                CoCoTimelinePosition.TryCreate(
                    sequence * 0.1d,
                    out CoCoTimelinePosition position),
                Is.True);
            Assert.That(
                CoCoClockDomainId.TryCreate(
                    1UL,
                    out CoCoClockDomainId clockDomainId),
                Is.True);
            Assert.That(
                CoCoTickFrame.TryCreate(
                    0.1d,
                    timelineId,
                    position,
                    new CoCoTimelineTick(sequence),
                    clockDomainId,
                    new CoCoExecutionSequence(sequence),
                    new CoCoTimelineEpoch(1UL),
                    out CoCoTickFrame tickFrame,
                    out CoCoDiagnostic tickDiagnostic),
                Is.True,
                tickDiagnostic.Message);

            ConstructorInfo[] constructors =
                typeof(CoCoTemporalFrameInfo).GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(constructors.Length, Is.EqualTo(1));
            return (CoCoTemporalFrameInfo)constructors[0].Invoke(
                new object[]
                {
                    graphId,
                    tickFrame,
                    new CoCoContextRevision(sequence + 1UL),
                    CoCoContextFrameOrigin.Commit()
                });
        }

        private static RegionCapabilitySet RepresentedCapabilities()
        {
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[] { RegionCapabilityId.Represented },
                    out RegionCapabilitySet capabilities),
                Is.True);
            return capabilities;
        }

        private static RegionCapabilitySet FullCapabilities()
        {
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[]
                    {
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable,
                        RegionCapabilityId.Full
                    },
                    out RegionCapabilitySet capabilities),
                Is.True);
            return capabilities;
        }

        private static RegionCapabilitySet FullRequirementOnly()
        {
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[] { RegionCapabilityId.Full },
                    out RegionCapabilitySet capabilities),
                Is.True);
            return capabilities;
        }

        private static RegionDemandRuntimeSnapshot FindTemporalDemand(
            RegionRuntimeSnapshot snapshot)
        {
            RegionDemandRuntimeSnapshot result = default;
            int matches = 0;
            for (int index = 0; index < snapshot.Demands.Count; index++)
            {
                RegionDemandRuntimeSnapshot demand =
                    snapshot.Demands[index];
                if (!demand.OwnerId.Value.StartsWith(
                        "cocoflow.map.temporal.",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                matches++;
                result = demand;
            }

            Assert.That(matches, Is.EqualTo(1));
            return result;
        }

        private static int CountTemporalDemands(
            RegionRuntimeSnapshot snapshot)
        {
            int count = 0;
            for (int index = 0; index < snapshot.Demands.Count; index++)
            {
                if (snapshot.Demands[index].OwnerId.Value.StartsWith(
                        "cocoflow.map.temporal.",
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int ReadPendingTargetCount(
            RegionTemporalRuntime runtime)
        {
            FieldInfo field = typeof(RegionTemporalRuntime).GetField(
                "pendingTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var pending = field.GetValue(runtime) as ICollection;
            Assert.That(pending, Is.Not.Null);
            return pending.Count;
        }

        private static void AssertImportedEmptyHead(
            RegionTemporalRuntime runtime,
            in CoCoTemporalFrameInfo expected)
        {
            FieldInfo framesField =
                typeof(RegionTemporalRuntime).GetField(
                    "frames",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo headField =
                typeof(RegionTemporalRuntime).GetField(
                    "headIndex",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(framesField, Is.Not.Null);
            Assert.That(headField, Is.Not.Null);
            var frames = (Array)framesField.GetValue(runtime);
            int headIndex = (int)headField.GetValue(runtime);
            object head = frames.GetValue(headIndex);
            Assert.That(head, Is.Not.Null);

            PropertyInfo infoProperty = head.GetType().GetProperty(
                "Info",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo regionsProperty = head.GetType().GetProperty(
                "Regions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(infoProperty, Is.Not.Null);
            Assert.That(regionsProperty, Is.Not.Null);
            var actual =
                (CoCoTemporalFrameInfo)infoProperty.GetValue(head);
            var regions =
                regionsProperty.GetValue(head) as ICollection;
            Assert.That(
                actual.TickFrame,
                Is.EqualTo(expected.TickFrame));
            Assert.That(actual.Revision, Is.EqualTo(expected.Revision));
            Assert.That(regions, Is.Not.Null);
            Assert.That(regions.Count, Is.Zero);
        }

        private static string DemandSignature(
            RegionRuntimeSnapshot snapshot)
        {
            var builder = new StringBuilder();
            for (int demandIndex = 0;
                 demandIndex < snapshot.Demands.Count;
                 demandIndex++)
            {
                RegionDemandRuntimeSnapshot demand =
                    snapshot.Demands[demandIndex];
                builder.Append(demand.OwnerId.Value)
                    .Append(':')
                    .Append(demand.RegionId.Value)
                    .Append(':')
                    .Append(demand.Revision.Value)
                    .Append(':')
                    .Append((int)demand.Coverage.Kind)
                    .Append(':');
                for (int capabilityIndex = 0;
                     capabilityIndex < demand.Capabilities.Count;
                     capabilityIndex++)
                {
                    builder.Append(
                            demand.Capabilities
                                .Capabilities[capabilityIndex].Value)
                        .Append(',');
                }

                builder.Append('|');
            }

            return builder.ToString();
        }

        private sealed class TemporalRuntimeFixture
        {
            internal TemporalRuntimeFixture(
                GameObject hostObject,
                ContentRuntime content,
                RegionRuntime region,
                ImmediateTransitionSink sink,
                RegionDemandScope gameplayScope,
                RegionDemandLease gameplayLease,
                RegionTemporalRuntime temporal,
                RegionId regionId,
                RegionChunkId chunkId,
                RegionCoverage coverage)
            {
                HostObject = hostObject;
                Content = content;
                Region = region;
                Sink = sink;
                GameplayScope = gameplayScope;
                GameplayLease = gameplayLease;
                Temporal = temporal;
                RegionId = regionId;
                ChunkId = chunkId;
                Coverage = coverage;
            }

            internal GameObject HostObject { get; }
            internal ContentRuntime Content { get; }
            internal RegionRuntime Region { get; }
            internal ImmediateTransitionSink Sink { get; }
            internal RegionDemandScope GameplayScope { get; }
            internal RegionDemandLease GameplayLease { get; }
            internal RegionTemporalRuntime Temporal { get; }
            internal RegionId RegionId { get; }
            internal RegionChunkId ChunkId { get; }
            internal RegionCoverage Coverage { get; }
        }

        private sealed class ImmediateTransitionSink :
            IRegionDemandTransitionSink
        {
            private readonly RegionRuntime runtime;
            private RegionId regionId;
            private readonly HashSet<RegionChunkId> knownChunks =
                new HashSet<RegionChunkId>();

            internal ImmediateTransitionSink(RegionRuntime runtime)
            {
                this.runtime = runtime;
            }

            internal int RequestCount { get; private set; }
            public bool IsInvokingParticipantCallback => false;

            internal void Configure(
                RegionId configuredRegionId,
                params RegionChunkId[] chunks)
            {
                regionId = configuredRegionId;
                knownChunks.Clear();
                for (int index = 0; index < chunks.Length; index++)
                {
                    knownChunks.Add(chunks[index]);
                }
            }

            public bool TryValidateDemand(
                RegionId requestedRegionId,
                RegionCapabilitySet capabilities,
                RegionCoverage coverage,
                out CoCoDiagnostic diagnostic)
            {
                if (requestedRegionId != regionId)
                {
                    diagnostic = RegionErrors.DemandConflict(
                        "Unknown Region.");
                    return false;
                }

                if (!coverage.CoversAll)
                {
                    for (int index = 0;
                         index < coverage.Chunks.Count;
                         index++)
                    {
                        if (!knownChunks.Contains(coverage.Chunks[index]))
                        {
                            diagnostic = RegionErrors.InvalidCoverage(
                                "Unknown Chunk.");
                            return false;
                        }
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void RequestTransition(
                RegionDemandResolution resolution)
            {
                RequestCount++;
                ResolveDefaultTier(
                    resolution.RegionCapabilities,
                    out RegionTierId regionTierId,
                    out RegionCapabilitySet regionEffective);
                var chunkFidelity =
                    new Dictionary<
                        RegionChunkId,
                        RegionResolvedChunkFidelity>();
                foreach (RegionChunkId chunkId in knownChunks)
                {
                    ResolveDefaultTier(
                        resolution.GetChunkCapabilities(chunkId),
                        out RegionTierId chunkTierId,
                        out RegionCapabilitySet chunkEffective);
                    chunkFidelity.Add(
                        chunkId,
                        new RegionResolvedChunkFidelity(
                            chunkTierId,
                            chunkEffective));
                }

                runtime.PublishResolvedFidelity(
                    resolution.RegionId,
                    resolution.DesiredGeneration,
                    regionTierId,
                    regionEffective,
                    chunkFidelity);
                runtime.PublishTransitionProgress(
                    resolution.RegionId,
                    resolution.DesiredGeneration,
                    knownChunks,
                    0,
                    0,
                    false,
                    false,
                    false,
                    CoCoDiagnostic.None);
                runtime.PublishTransitionReady(
                    resolution.RegionId,
                    resolution.DesiredGeneration);
            }

            public bool TryAcceptRetry(
                RegionId requestedRegionId,
                RegionDemandResolution resolution,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void StartAcceptedRetry(
                RegionId requestedRegionId,
                RegionDemandResolution resolution)
            {
                RequestTransition(resolution);
            }

            public UniTask<CoCoDiagnostic> ShutdownAsync() =>
                UniTask.FromResult(CoCoDiagnostic.None);

            public void ForceShutdown()
            {
            }

            private static void ResolveDefaultTier(
                RegionCapabilitySet requirement,
                out RegionTierId tierId,
                out RegionCapabilitySet effective)
            {
                var capabilities = new List<RegionCapabilityId>();
                if (requirement != null)
                {
                    for (int index = 0;
                         index < requirement.Count;
                         index++)
                    {
                        RegionCapabilityId capability =
                            requirement.Capabilities[index];
                        if (capability != RegionCapabilityId.Represented &&
                            capability != RegionCapabilityId.Background &&
                            capability != RegionCapabilityId.Enterable &&
                            capability != RegionCapabilityId.Full)
                        {
                            capabilities.Add(capability);
                        }
                    }
                }

                int standardDepth =
                    requirement != null &&
                    requirement.Contains(RegionCapabilityId.Full)
                        ? 4
                        : requirement != null &&
                          requirement.Contains(
                              RegionCapabilityId.Enterable)
                            ? 3
                            : requirement != null &&
                              requirement.Contains(
                                  RegionCapabilityId.Background)
                                ? 2
                                : requirement != null &&
                                  requirement.Contains(
                                      RegionCapabilityId.Represented)
                                    ? 1
                                    : 0;
                if (standardDepth >= 1)
                {
                    capabilities.Add(
                        RegionCapabilityId.Represented);
                }

                if (standardDepth >= 2)
                {
                    capabilities.Add(
                        RegionCapabilityId.Background);
                }

                if (standardDepth >= 3)
                {
                    capabilities.Add(
                        RegionCapabilityId.Enterable);
                }

                if (standardDepth >= 4)
                {
                    capabilities.Add(
                        RegionCapabilityId.Full);
                }

                Assert.That(
                    RegionCapabilitySet.TryCreate(
                        capabilities,
                        out effective),
                    Is.True);
                tierId = standardDepth switch
                {
                    4 => RegionTierId.Full,
                    3 => RegionTierId.Enterable,
                    2 => RegionTierId.Background,
                    1 => RegionTierId.Represented,
                    _ => RegionTierId.Off
                };
            }
        }
    }
}
