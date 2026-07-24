using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Map
{
    public sealed class RegionSemanticModelPlayModeTests
    {
        [UnityTest]
        public IEnumerator WildernessAndLandmarksRemainIndependentUnderOverlap() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionMainThreadGuard.CaptureCurrentThread();
                Assert.That(
                    ContentRuntime.TryCreate(
                        out ContentRuntime contentRuntime,
                        out CoCoDiagnostic diagnostic),
                    Is.True,
                    diagnostic.Message);
                Assert.That(
                    RegionRuntime.TryCreate(
                        contentRuntime,
                        out RegionRuntime regionRuntime,
                        out diagnostic),
                    Is.True,
                    diagnostic.Message);
                var sink = new SemanticTransitionSink(regionRuntime);
                Assert.That(
                    regionRuntime.TryAttachTransitionSink(
                        sink,
                        out diagnostic),
                    Is.True,
                    diagnostic.Message);

                RegionId wilderness = Region("world.wilderness");
                RegionId castle = Region("world.castle");
                RegionId chapel = Region("world.chapel");
                RegionId mine = Region("world.mine");
                RegionChunkId west = Chunk("wilderness-west");
                RegionChunkId east = Chunk("wilderness-east");
                RegionChunkId keep = Chunk("castle-keep");
                RegionChunkId nave = Chunk("chapel-nave");
                RegionChunkId entrance = Chunk("mine-entrance");
                sink.Configure(wilderness, west, east);
                sink.Configure(castle, keep);
                sink.Configure(chapel, nave);
                sink.Configure(mine, entrance);

                RegionDemandScope player =
                    CreateScope(regionRuntime, "semantic.player");
                RegionDemandScope observer =
                    CreateScope(regionRuntime, "semantic.observer");
                RegionDemandScope landmarks =
                    CreateScope(regionRuntime, "semantic.landmarks");
                try
                {
                    RegionCoverage westOnly = Coverage(west);
                    RegionCoverage eastOnly = Coverage(east);
                    RegionDemandLease playerWilderness = Demand(
                        player,
                        wilderness,
                        FullCapabilities(),
                        westOnly,
                        out RegionDemandRevision playerRevision);
                    RegionDemandLease observerWilderness = Demand(
                        observer,
                        wilderness,
                        RepresentedCapabilities(),
                        eastOnly,
                        out RegionDemandRevision observerRevision);
                    RegionDemandLease castleLease = Demand(
                        landmarks,
                        castle,
                        FullCapabilities(),
                        RegionCoverage.All,
                        out RegionDemandRevision castleRevision);
                    RegionDemandLease chapelLease = Demand(
                        landmarks,
                        chapel,
                        BackgroundCapabilities(),
                        RegionCoverage.All,
                        out RegionDemandRevision chapelRevision);
                    RegionDemandLease mineLease = Demand(
                        landmarks,
                        mine,
                        RepresentedCapabilities(),
                        RegionCoverage.All,
                        out RegionDemandRevision mineRevision);

                    AssertReady(
                        await playerWilderness.WaitUntilReadyAsync(
                            playerRevision));
                    AssertReady(
                        await observerWilderness.WaitUntilReadyAsync(
                            observerRevision));
                    AssertReady(
                        await castleLease.WaitUntilReadyAsync(
                            castleRevision));
                    AssertReady(
                        await chapelLease.WaitUntilReadyAsync(
                            chapelRevision));
                    AssertReady(
                        await mineLease.WaitUntilReadyAsync(
                            mineRevision));

                    RegionDemandResolution wildernessResolution =
                        sink.LastResolution(wilderness);
                    Assert.That(
                        wildernessResolution
                            .GetChunkCapabilities(west)
                            .Contains(RegionCapabilityId.Full),
                        Is.True);
                    Assert.That(
                        wildernessResolution
                            .GetChunkCapabilities(east)
                            .Contains(RegionCapabilityId.Full),
                        Is.False,
                        "A Full west-Chunk demand must not spread into the east Chunk.");
                    Assert.That(
                        wildernessResolution
                            .GetChunkCapabilities(east)
                            .Contains(RegionCapabilityId.Represented),
                        Is.True);

                    Assert.That(
                        sink.LastResolution(castle).RegionCapabilities
                            .Contains(RegionCapabilityId.Full),
                        Is.True);
                    Assert.That(
                        sink.LastResolution(chapel).RegionCapabilities
                            .Contains(RegionCapabilityId.Background),
                        Is.True);
                    Assert.That(
                        sink.LastResolution(chapel).RegionCapabilities
                            .Contains(RegionCapabilityId.Full),
                        Is.False);
                    Assert.That(
                        sink.LastResolution(mine).RegionCapabilities
                            .Contains(RegionCapabilityId.Represented),
                        Is.True);

                    RegionRuntimeSnapshot snapshot =
                        regionRuntime.CaptureSnapshot();
                    CollectionAssert.AreEquivalent(
                        new[] { wilderness, castle, chapel, mine },
                        snapshot.Regions
                            .Select(region => region.RegionId)
                            .ToArray(),
                        "Wilderness and each landmark are separate Region authorities.");
                    Assert.That(snapshot.Demands.Count, Is.EqualTo(5));
                }
                finally
                {
                    player.Dispose();
                    observer.Dispose();
                    landmarks.Dispose();
                    await regionRuntime.ShutdownAsync();
                    await contentRuntime.ShutdownAsync();
                }
            });

        [UnityTest]
        public IEnumerator DisabledHostRemainsTerminalAfterReenable() =>
            UniTask.ToCoroutine(async () =>
            {
                RegionMainThreadGuard.CaptureCurrentThread();
                Assert.That(
                    ContentRuntime.TryCreate(
                        out ContentRuntime contentRuntime,
                        out CoCoDiagnostic diagnostic),
                    Is.True,
                    diagnostic.Message);
                Assert.That(
                    RegionRuntime.TryCreate(
                        contentRuntime,
                        out RegionRuntime regionRuntime,
                        out diagnostic),
                    Is.True,
                    diagnostic.Message);
                var hostObject =
                    new GameObject("Terminal Map Host");
                hostObject.SetActive(false);
                CoCoMapHost host =
                    hostObject.AddComponent<CoCoMapHost>();
                PropertyInfo runtimeProperty =
                    typeof(CoCoMapHost).GetProperty(
                        nameof(CoCoMapHost.Runtime),
                        BindingFlags.Instance |
                        BindingFlags.Public);
                Assert.That(runtimeProperty, Is.Not.Null);
                runtimeProperty.SetValue(host, regionRuntime);

                try
                {
                    hostObject.SetActive(true);
                    Assert.That(host.IsInitialized, Is.True);
                    Assert.That(host.Runtime, Is.SameAs(regionRuntime));

                    hostObject.SetActive(false);
                    await host.ShutdownAsync();
                    Assert.That(regionRuntime.IsDisposed, Is.True);
                    Assert.That(host.IsInitialized, Is.False);
                    Assert.That(host.Runtime, Is.SameAs(regionRuntime));

                    hostObject.SetActive(true);
                    await UniTask.Yield();
                    Assert.That(
                        host.Runtime,
                        Is.SameAs(regionRuntime),
                        "Re-enabling a terminal Host must not create a second runtime.");
                    Assert.That(
                        host.TryInitialize(out diagnostic),
                        Is.False);
                    Assert.That(
                        diagnostic.Code,
                        Is.EqualTo(
                            CoCoDiagnosticCode.RegionRuntimeDisposed));

                    Assert.That(
                        RegionDemandOwnerId.TryCreate(
                            "terminal-host-owner",
                            out RegionDemandOwnerId ownerId),
                        Is.True);
                    Assert.That(
                        host.TryCreateDemandScope(
                            ownerId,
                            out _,
                            out diagnostic),
                        Is.False);
                    Assert.That(
                        diagnostic.Code,
                        Is.EqualTo(
                            CoCoDiagnosticCode.RegionRuntimeDisposed));

                    Assert.That(
                        RegionId.TryCreate(
                            "terminal-host-region",
                            out RegionId regionId),
                        Is.True);
                    Assert.That(
                        host.TryRetryRegion(
                            regionId,
                            out diagnostic),
                        Is.False);
                    Assert.That(
                        diagnostic.Code,
                        Is.EqualTo(
                            CoCoDiagnosticCode.RegionRuntimeDisposed));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(hostObject);
                    await contentRuntime.ShutdownAsync();
                }
            });

        private static RegionDemandLease Demand(
            RegionDemandScope scope,
            RegionId regionId,
            RegionCapabilitySet capabilities,
            RegionCoverage coverage,
            out RegionDemandRevision revision)
        {
            Assert.That(
                scope.TryDemand(
                    regionId,
                    capabilities,
                    coverage,
                    out RegionDemandLease lease,
                    out revision,
                    out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
            return lease;
        }

        private static RegionDemandScope CreateScope(
            RegionRuntime runtime,
            string ownerValue)
        {
            Assert.That(
                RegionDemandOwnerId.TryCreate(
                    ownerValue,
                    out RegionDemandOwnerId ownerId),
                Is.True);
            Assert.That(
                runtime.TryCreateDemandScope(
                    ownerId,
                    out RegionDemandScope scope,
                    out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
            return scope;
        }

        private static void AssertReady(RegionReadinessResult result) =>
            Assert.That(
                result.Status,
                Is.EqualTo(RegionReadinessStatus.Ready),
                result.Diagnostic.Message);

        private static RegionId Region(string value)
        {
            Assert.That(
                RegionId.TryCreate(value, out RegionId regionId),
                Is.True);
            return regionId;
        }

        private static RegionChunkId Chunk(string value)
        {
            Assert.That(
                RegionChunkId.TryCreate(value, out RegionChunkId chunkId),
                Is.True);
            return chunkId;
        }

        private static RegionCoverage Coverage(
            params RegionChunkId[] chunks)
        {
            Assert.That(
                RegionCoverage.TryCreateChunks(
                    chunks,
                    out RegionCoverage coverage),
                Is.True);
            return coverage;
        }

        private static RegionCapabilitySet RepresentedCapabilities() =>
            Capabilities(RegionCapabilityId.Represented);

        private static RegionCapabilitySet BackgroundCapabilities() =>
            Capabilities(
                RegionCapabilityId.Represented,
                RegionCapabilityId.Background);

        private static RegionCapabilitySet FullCapabilities() =>
            Capabilities(
                RegionCapabilityId.Represented,
                RegionCapabilityId.Background,
                RegionCapabilityId.Enterable,
                RegionCapabilityId.Full);

        private static RegionCapabilitySet Capabilities(
            params RegionCapabilityId[] capabilities)
        {
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    capabilities,
                    out RegionCapabilitySet set),
                Is.True);
            return set;
        }

        private sealed class SemanticTransitionSink :
            IRegionDemandTransitionSink
        {
            private readonly RegionRuntime runtime;
            private readonly Dictionary<
                RegionId,
                HashSet<RegionChunkId>> chunksByRegion =
                    new Dictionary<
                        RegionId,
                        HashSet<RegionChunkId>>();
            private readonly Dictionary<
                RegionId,
                RegionDemandResolution> resolutions =
                    new Dictionary<
                        RegionId,
                        RegionDemandResolution>();

            internal SemanticTransitionSink(RegionRuntime runtime)
            {
                this.runtime = runtime;
            }

            public bool IsInvokingParticipantCallback => false;

            internal void Configure(
                RegionId regionId,
                params RegionChunkId[] chunks) =>
                chunksByRegion.Add(
                    regionId,
                    new HashSet<RegionChunkId>(chunks));

            internal RegionDemandResolution LastResolution(
                RegionId regionId) => resolutions[regionId];

            public bool TryValidateDemand(
                RegionId regionId,
                RegionCapabilitySet capabilities,
                RegionCoverage coverage,
                out CoCoDiagnostic diagnostic)
            {
                if (!chunksByRegion.TryGetValue(
                        regionId,
                        out HashSet<RegionChunkId> knownChunks))
                {
                    diagnostic = RegionErrors.DemandConflict(
                        "The semantic fixture does not own this Region.");
                    return false;
                }

                if (!coverage.CoversAll)
                {
                    for (int index = 0;
                         index < coverage.Chunks.Count;
                         index++)
                    {
                        if (!knownChunks.Contains(
                                coverage.Chunks[index]))
                        {
                            diagnostic = RegionErrors.InvalidCoverage(
                                "The semantic fixture does not own this Chunk.");
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
                resolutions[resolution.RegionId] = resolution;
                HashSet<RegionChunkId> chunks =
                    chunksByRegion[resolution.RegionId];
                runtime.PublishTransitionProgress(
                    resolution.RegionId,
                    resolution.DesiredGeneration,
                    chunks,
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
                RegionId regionId,
                RegionDemandResolution resolution,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void StartAcceptedRetry(
                RegionId regionId,
                RegionDemandResolution resolution) =>
                RequestTransition(resolution);

            public UniTask<CoCoDiagnostic> ShutdownAsync() =>
                UniTask.FromResult(CoCoDiagnostic.None);

            public void ForceShutdown()
            {
            }
        }
    }
}
