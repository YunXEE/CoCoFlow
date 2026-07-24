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
                    Is.GreaterThan(transitionsBeforeBranch),
                    "Only the published cleanup drain may dispatch the retention decrease.");
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
