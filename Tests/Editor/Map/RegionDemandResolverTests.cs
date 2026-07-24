using System;
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

        [UnityTest]
        public IEnumerator ForceShutdownDisposesLiveScopesAndTerminatesWaiters() =>
            UniTask.ToCoroutine(async () =>
            {
                CreateRuntime(
                    autoReady: false,
                    out ContentRuntime contentRuntime,
                    out RegionRuntime regionRuntime,
                    out RecordingTransitionSink sink);
                Assert.IsTrue(RegionId.TryCreate(
                    "world.shutdown",
                    out RegionId regionId));
                Assert.IsTrue(RegionChunkId.TryCreate(
                    "center",
                    out RegionChunkId chunkId));
                sink.Configure(regionId, chunkId);
                RegionDemandScope scope =
                    CreateScope(regionRuntime, "shutdown-owner");
                Assert.IsTrue(RegionCoverage.TryCreateChunks(
                    new[] { chunkId },
                    out RegionCoverage coverage));
                Assert.IsTrue(scope.TryDemand(
                    regionId,
                    RepresentedCapabilities(),
                    coverage,
                    out RegionDemandLease lease,
                    out RegionDemandRevision revision,
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
                UniTask<RegionReadinessResult> pending =
                    lease.WaitUntilReadyAsync(revision);

                regionRuntime.ForceShutdown();

                Assert.That(scope.IsDisposed, Is.True);
                Assert.That(lease.IsDisposed, Is.True);
                Assert.That(
                    (await pending).Status,
                    Is.EqualTo(RegionReadinessStatus.Superseded));
                Assert.That(
                    (await lease.WaitUntilReadyAsync(revision)).Status,
                    Is.EqualTo(RegionReadinessStatus.Disposed));
                Assert.That(
                    (await regionRuntime.ShutdownAsync()).IsNone,
                    Is.True,
                    "Force and graceful shutdown callers must converge on one completed result.");

                await contentRuntime.ShutdownAsync();
            });

        [UnityTest]
        public IEnumerator TemporalFlushQueuesCascadingRecomputeAndConvergesOnce() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionMainThreadGuard.CaptureCurrentThread();
                Assert.IsTrue(ContentRuntime.TryCreate(
                    out ContentRuntime contentRuntime,
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
                Assert.IsTrue(RegionRuntime.TryCreate(
                    contentRuntime,
                    out RegionRuntime regionRuntime,
                    out diagnostic),
                    diagnostic.Message);
                var sink = new CascadingTransitionSink(regionRuntime);
                Assert.IsTrue(regionRuntime.TryAttachTransitionSink(
                    sink,
                    out diagnostic),
                    diagnostic.Message);

                Assert.IsTrue(RegionId.TryCreate(
                    "a.source",
                    out RegionId sourceRegionId));
                Assert.IsTrue(RegionId.TryCreate(
                    "z.target",
                    out RegionId targetRegionId));
                Assert.IsTrue(RegionChunkId.TryCreate(
                    "source-chunk",
                    out RegionChunkId sourceChunkId));
                Assert.IsTrue(RegionChunkId.TryCreate(
                    "target-chunk",
                    out RegionChunkId targetChunkId));
                sink.Configure(sourceRegionId, sourceChunkId);
                sink.Configure(targetRegionId, targetChunkId);
                Assert.IsTrue(RegionCoverage.TryCreateChunks(
                    new[] { sourceChunkId },
                    out RegionCoverage sourceCoverage));
                Assert.IsTrue(RegionCoverage.TryCreateChunks(
                    new[] { targetChunkId },
                    out RegionCoverage targetCoverage));

                RegionDemandScope sourceScope =
                    CreateScope(regionRuntime, "flush-source");
                RegionDemandScope targetScope =
                    CreateScope(regionRuntime, "flush-target");
                RegionDemandScope dependencyScope =
                    CreateScope(regionRuntime, "flush-dependency");
                try
                {
                    Assert.IsTrue(sourceScope.TryDemand(
                        sourceRegionId,
                        RepresentedCapabilities(),
                        sourceCoverage,
                        out RegionDemandLease sourceLease,
                        out RegionDemandRevision sourceInitial,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await sourceLease.WaitUntilReadyAsync(
                            sourceInitial)).Status);
                    Assert.IsTrue(targetScope.TryDemand(
                        targetRegionId,
                        RepresentedCapabilities(),
                        targetCoverage,
                        out RegionDemandLease targetLease,
                        out RegionDemandRevision targetInitial,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await targetLease.WaitUntilReadyAsync(
                            targetInitial)).Status);

                    Assert.IsTrue(regionRuntime.TryEnterTemporalBarrier(
                        out RegionRuntime.RegionTemporalBarrier barrier,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.IsTrue(sourceLease.TryUpdate(
                        BackgroundCapabilities(),
                        sourceCoverage,
                        out RegionDemandRevision sourceUpdated,
                        out diagnostic),
                        diagnostic.Message);
                    Assert.IsTrue(targetLease.TryUpdate(
                        BackgroundCapabilities(),
                        targetCoverage,
                        out RegionDemandRevision targetUpdated,
                        out diagnostic),
                        diagnostic.Message);

                    RegionDemandLease dependencyLease = null;
                    RegionDemandRevision dependencyRevision = default;
                    sink.ResetObservations();
                    sink.BeforePublish = resolution =>
                    {
                        if (resolution.RegionId != sourceRegionId ||
                            dependencyLease != null)
                        {
                            return;
                        }

                        Assert.IsTrue(dependencyScope.TryDemand(
                            targetRegionId,
                            FullCapabilities(),
                            targetCoverage,
                            out dependencyLease,
                            out dependencyRevision,
                            out CoCoDiagnostic dependencyDiagnostic),
                            dependencyDiagnostic.Message);
                    };

                    barrier.Dispose();
                    regionRuntime.FlushDeferredTransitionsNoThrow();

                    CollectionAssert.AreEqual(
                        new[] { sourceRegionId, targetRegionId },
                        sink.RequestOrder,
                        "A target already dirty in the flush round must consume the cascading recompute without nested or duplicate dispatch.");
                    Assert.AreEqual(
                        1,
                        sink.MaxRequestDepth,
                        "Cross-Region recompute during flush must remain queued instead of dispatching reentrantly.");
                    Assert.NotNull(dependencyLease);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await sourceLease.WaitUntilReadyAsync(
                            sourceUpdated)).Status);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await targetLease.WaitUntilReadyAsync(
                            targetUpdated)).Status);
                    Assert.AreEqual(
                        RegionReadinessStatus.Ready,
                        (await dependencyLease.WaitUntilReadyAsync(
                            dependencyRevision)).Status);
                    Assert.IsFalse(
                        regionRuntime.IsTemporalDispatchDeferred);
                }
                finally
                {
                    sourceScope.Dispose();
                    targetScope.Dispose();
                    dependencyScope.Dispose();
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
        }

        private sealed class CascadingTransitionSink :
            IRegionDemandTransitionSink
        {
            private readonly RegionRuntime runtime;
            private readonly Dictionary<
                RegionId,
                HashSet<RegionChunkId>> chunksByRegion =
                    new Dictionary<
                        RegionId,
                        HashSet<RegionChunkId>>();
            private int requestDepth;

            internal CascadingTransitionSink(RegionRuntime runtime)
            {
                this.runtime = runtime;
            }

            internal Action<RegionDemandResolution> BeforePublish
            {
                get;
                set;
            }

            internal List<RegionId> RequestOrder { get; } =
                new List<RegionId>();
            internal int MaxRequestDepth { get; private set; }
            public bool IsInvokingParticipantCallback => false;

            internal void Configure(
                RegionId regionId,
                params RegionChunkId[] chunks)
            {
                chunksByRegion[regionId] =
                    new HashSet<RegionChunkId>(chunks);
            }

            internal void ResetObservations()
            {
                RequestOrder.Clear();
                requestDepth = 0;
                MaxRequestDepth = 0;
            }

            public bool TryValidateDemand(
                RegionId regionId,
                RegionCapabilitySet capabilities,
                RegionCoverage coverage,
                out CoCoDiagnostic diagnostic)
            {
                if (!chunksByRegion.TryGetValue(
                        regionId,
                        out HashSet<RegionChunkId> chunks))
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
                        if (!chunks.Contains(coverage.Chunks[index]))
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
                requestDepth++;
                MaxRequestDepth = Math.Max(
                    MaxRequestDepth,
                    requestDepth);
                RequestOrder.Add(resolution.RegionId);
                try
                {
                    BeforePublish?.Invoke(resolution);
                    runtime.PublishTransitionProgress(
                        resolution.RegionId,
                        resolution.DesiredGeneration,
                        chunksByRegion[resolution.RegionId],
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
                finally
                {
                    requestDepth--;
                }
            }

            public bool TryAcceptRetry(
                RegionId regionId,
                RegionDemandResolution resolution,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "The cascading flush test sink has no retryable state.");
                return false;
            }

            public void StartAcceptedRetry(
                RegionId regionId,
                RegionDemandResolution resolution)
            {
                throw new InvalidOperationException(
                    "The cascading flush test sink cannot start Retry.");
            }

            public UniTask<CoCoDiagnostic> ShutdownAsync() =>
                UniTask.FromResult(CoCoDiagnostic.None);

            public void ForceShutdown()
            {
            }
        }
    }
}
