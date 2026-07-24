using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionDemandResolverTests
    {
        [UnityTest]
        public IEnumerator OtherOwnerChangesDoNotSupersedeAndChunkFidelityDoesNotSpread() =>
            UniTask.ToCoroutine(async () =>
            {
                CreateRuntime(
                    autoReady: true,
                    out ContentRuntime contentRuntime,
                    out RegionRuntime regionRuntime,
                    out RecordingTransitionSink sink);
                Assert.IsTrue(RegionId.TryCreate("world.wilderness", out RegionId regionId));
                Assert.IsTrue(RegionChunkId.TryCreate("west", out RegionChunkId west));
                Assert.IsTrue(RegionChunkId.TryCreate("east", out RegionChunkId east));
                sink.Configure(regionId, west, east);
                RegionDemandScope player = CreateScope(regionRuntime, "player");
                RegionDemandScope observer = CreateScope(regionRuntime, "observer");
                try
                {
                    Assert.IsTrue(RegionCoverage.TryCreateChunks(
                        new[] { west },
                        out RegionCoverage westOnly));
                    Assert.IsTrue(player.TryDemand(
                        regionId,
                        FullCapabilities(),
                        westOnly,
                        out RegionDemandLease playerLease,
                        out RegionDemandRevision playerRevision,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await playerLease.WaitUntilReadyAsync(playerRevision)).Status);

                    Assert.IsTrue(RegionCoverage.TryCreateChunks(
                        new[] { east },
                        out RegionCoverage eastOnly));
                    Assert.IsTrue(observer.TryDemand(
                        regionId,
                        RepresentedCapabilities(),
                        eastOnly,
                        out RegionDemandLease observerLease,
                        out RegionDemandRevision observerRevision,
                        out diagnostic),
                        diagnostic.Message);

                    Assert.AreEqual(playerRevision, playerLease.Revision);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await playerLease.WaitUntilReadyAsync(playerRevision)).Status);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await observerLease.WaitUntilReadyAsync(observerRevision)).Status);
                    Assert.IsTrue(
                        sink.LastResolution.GetChunkCapabilities(west)
                            .Contains(RegionCapabilityId.Full));
                    Assert.IsFalse(
                        sink.LastResolution.GetChunkCapabilities(east)
                            .Contains(RegionCapabilityId.Full));
                    Assert.IsTrue(
                        sink.LastResolution.GetChunkCapabilities(east)
                            .Contains(RegionCapabilityId.Represented));
                }
                finally
                {
                    player.Dispose();
                    observer.Dispose();
                    await regionRuntime.ShutdownAsync();
                    await contentRuntime.ShutdownAsync();
                }
            });

        [UnityTest]
        public IEnumerator SameLeaseUpdateAndReleaseSupersedeOutstandingRevisions() =>
            UniTask.ToCoroutine(async () =>
            {
                CreateRuntime(
                    autoReady: false,
                    out ContentRuntime contentRuntime,
                    out RegionRuntime regionRuntime,
                    out RecordingTransitionSink sink);
                Assert.IsTrue(RegionId.TryCreate("world.castle", out RegionId regionId));
                Assert.IsTrue(RegionChunkId.TryCreate("keep", out RegionChunkId keep));
                sink.Configure(regionId, keep);
                RegionDemandScope scope = CreateScope(regionRuntime, "player");
                try
                {
                    Assert.IsTrue(RegionCoverage.TryCreateChunks(
                        new[] { keep },
                        out RegionCoverage keepOnly));
                    Assert.IsTrue(scope.TryDemand(
                        regionId,
                        RepresentedCapabilities(),
                        keepOnly,
                        out RegionDemandLease lease,
                        out RegionDemandRevision first,
                        out CoCoDiagnostic diagnostic),
                        diagnostic.Message);
                    UniTask<RegionReadinessResult> firstWait =
                        lease.WaitUntilReadyAsync(first);

                    Assert.IsTrue(lease.TryUpdate(
                        BackgroundCapabilities(),
                        keepOnly,
                        out RegionDemandRevision second,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Superseded,
                        (await firstWait).Status);

                    UniTask<RegionReadinessResult> secondWait =
                        lease.WaitUntilReadyAsync(second);
                    lease.Dispose();
                    Assert.AreEqual(
                        RegionReadinessStatus.Superseded,
                        (await secondWait).Status);
                    Assert.AreEqual(
                        RegionReadinessStatus.Disposed,
                        (await lease.WaitUntilReadyAsync(second)).Status);
                }
                finally
                {
                    scope.Dispose();
                    await regionRuntime.ShutdownAsync();
                    await contentRuntime.ShutdownAsync();
                }
            });

        [UnityTest]
        public IEnumerator UnknownChunkRejectsWholeDemandWithoutIssuingRevision() =>
            UniTask.ToCoroutine(async () =>
            {
                CreateRuntime(
                    autoReady: true,
                    out ContentRuntime contentRuntime,
                    out RegionRuntime regionRuntime,
                    out RecordingTransitionSink sink);
                Assert.IsTrue(RegionId.TryCreate("world.mine", out RegionId regionId));
                Assert.IsTrue(RegionChunkId.TryCreate("entrance", out RegionChunkId entrance));
                Assert.IsTrue(RegionChunkId.TryCreate("external", out RegionChunkId external));
                sink.Configure(regionId, entrance);
                RegionDemandScope scope = CreateScope(regionRuntime, "player");
                try
                {
                    Assert.IsTrue(RegionCoverage.TryCreateChunks(
                        new[] { external },
                        out RegionCoverage invalidCoverage));
                    Assert.IsFalse(scope.TryDemand(
                        regionId,
                        FullCapabilities(),
                        invalidCoverage,
                        out RegionDemandLease lease,
                        out RegionDemandRevision revision,
                        out CoCoDiagnostic diagnostic));
                    Assert.IsNull(lease);
                    Assert.IsFalse(revision.IsValid);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.InvalidRegionCoverage,
                        diagnostic.Code);
                    Assert.AreEqual(0, regionRuntime.CaptureSnapshot().Demands.Count);
                }
                finally
                {
                    scope.Dispose();
                    await regionRuntime.ShutdownAsync();
                    await contentRuntime.ShutdownAsync();
                }
            });

        private static void CreateRuntime(
            bool autoReady,
            out ContentRuntime contentRuntime,
            out RegionRuntime regionRuntime,
            out RecordingTransitionSink sink)
        {
            RegionMainThreadGuard.CaptureCurrentThread();
            Assert.IsTrue(ContentRuntime.TryCreate(
                out contentRuntime,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(RegionRuntime.TryCreate(
                contentRuntime,
                out regionRuntime,
                out diagnostic),
                diagnostic.Message);
            sink = new RecordingTransitionSink(regionRuntime, autoReady);
            Assert.IsTrue(regionRuntime.TryAttachTransitionSink(
                sink,
                out diagnostic),
                diagnostic.Message);
        }

        private static RegionDemandScope CreateScope(
            RegionRuntime runtime,
            string owner)
        {
            Assert.IsTrue(RegionDemandOwnerId.TryCreate(
                owner,
                out RegionDemandOwnerId ownerId));
            Assert.IsTrue(runtime.TryCreateDemandScope(
                ownerId,
                out RegionDemandScope scope,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return scope;
        }

        private static RegionCapabilitySet RepresentedCapabilities()
        {
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[] { RegionCapabilityId.Represented },
                out RegionCapabilitySet capabilities));
            return capabilities;
        }

        private static RegionCapabilitySet BackgroundCapabilities()
        {
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[]
                {
                    RegionCapabilityId.Represented,
                    RegionCapabilityId.Background
                },
                out RegionCapabilitySet capabilities));
            return capabilities;
        }

        private static RegionCapabilitySet FullCapabilities()
        {
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[]
                {
                    RegionCapabilityId.Represented,
                    RegionCapabilityId.Background,
                    RegionCapabilityId.Enterable,
                    RegionCapabilityId.Full
                },
                out RegionCapabilitySet capabilities));
            return capabilities;
        }

        private sealed class RecordingTransitionSink :
            IRegionDemandTransitionSink
        {
            private readonly RegionRuntime runtime;
            private readonly bool autoReady;
            private RegionId regionId;
            private readonly HashSet<RegionChunkId> knownChunks =
                new HashSet<RegionChunkId>();

            internal RecordingTransitionSink(
                RegionRuntime runtime,
                bool autoReady)
            {
                this.runtime = runtime;
                this.autoReady = autoReady;
            }

            internal RegionDemandResolution LastResolution { get; private set; }
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
                    for (int index = 0; index < coverage.Chunks.Count; index++)
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

            public void RequestTransition(RegionDemandResolution resolution)
            {
                LastResolution = resolution;
                if (autoReady)
                {
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
            }

            public bool TryRetryRegion(
                RegionId requestedRegionId,
                RegionDemandResolution resolution,
                out CoCoDiagnostic diagnostic)
            {
                RequestTransition(resolution);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public UniTask<CoCoDiagnostic> ShutdownAsync() =>
                UniTask.FromResult(CoCoDiagnostic.None);

            public void ForceShutdown()
            {
            }
        }
    }
}
