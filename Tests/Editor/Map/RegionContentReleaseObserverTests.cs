using System.Collections;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionContentReleaseObserverTests
    {
        private const string FixtureScenePath =
            "Packages/com.yunxee.cocoflow/Tests/Runtime/Map/" +
            "Fixtures/WildernessColdStartChunk.unity";

        [UnityTest]
        public IEnumerator ExactGenerationWaitsDespiteSameIdRequest() =>
            UniTask.ToCoroutine(async () =>
            {
                TestAsset asset =
                    ScriptableObject.CreateInstance<TestAsset>();
                var backend = new ControlledReleaseBackend(asset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope firstScope = null;
                ContentScope secondScope = null;
                try
                {
                    const string contentValue =
                        "tests.map.release.exact-generation";
                    firstScope = CreateScope(
                        runtime,
                        "tests.map.release.exact.first");
                    secondScope = CreateScope(
                        runtime,
                        "tests.map.release.exact.second");
                    ContentAcquireResult<ScriptableObject>
                        firstResult =
                            await firstScope
                                .AcquireAssetAsync<ScriptableObject>(
                                    CreateReference(contentValue),
                                    CancellationToken.None);
                    Assert.IsTrue(
                        firstResult.Succeeded,
                        firstResult.Diagnostic.Message);
                    ContentLease<ScriptableObject> first =
                        firstResult.Lease;
                    ContentLease<TestAsset> second =
                        await AcquireAsync(
                            secondScope,
                            contentValue);
                    Assert.AreNotEqual(
                        first.ResourceGeneration,
                        second.ResourceGeneration,
                        "Different Content request identities must receive different resource generations.");

                    ContentId contentId = first.Id;
                    long resourceGeneration =
                        first.ResourceGeneration;

                    firstScope.Dispose();
                    firstScope = null;
                    UniTask<CoCoDiagnostic> observation =
                        RegionContentReleaseObserver.ObserveAsync(
                                runtime,
                                contentId,
                                resourceGeneration)
                            .Preserve();
                    await UniTask.Yield();
                    Assert.AreEqual(
                        UniTaskStatus.Pending,
                        observation.Status,
                        "A live Lease for a different request with the same ContentId must not complete exact-generation cleanup.");

                    backend.CompleteRelease(CoCoDiagnostic.None);
                    CoCoDiagnostic diagnostic =
                        await observation;
                    Assert.IsTrue(
                        diagnostic.IsNone,
                        diagnostic.Message);
                    Assert.AreEqual(
                        1,
                        runtime.CaptureSnapshot().Entries.Count,
                        "Only the exact released generation should be removed while the different request remains leased.");
                }
                finally
                {
                    firstScope?.Dispose();
                    secondScope?.Dispose();
                    backend.CompleteRelease(CoCoDiagnostic.None);
                    await runtime.ShutdownAsync();
                    Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator SharedOwnerReleaseDoesNotWaitForPhysicalUnload() =>
            UniTask.ToCoroutine(async () =>
            {
                TestAsset asset =
                    ScriptableObject.CreateInstance<TestAsset>();
                var backend = new ControlledReleaseBackend(asset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope firstScope = null;
                ContentScope secondScope = null;
                try
                {
                    firstScope = CreateScope(
                        runtime,
                        "tests.map.release.shared.first");
                    secondScope = CreateScope(
                        runtime,
                        "tests.map.release.shared.second");
                    ContentLease<TestAsset> first =
                        await AcquireAsync(
                            firstScope,
                            "tests.map.release.shared");
                    ContentLease<TestAsset> second =
                        await AcquireAsync(
                            secondScope,
                            "tests.map.release.shared");
                    Assert.AreEqual(
                        first.ResourceGeneration,
                        second.ResourceGeneration);

                    firstScope.Dispose();
                    firstScope = null;
                    CoCoDiagnostic diagnostic =
                        await RegionContentReleaseObserver.ObserveAsync(
                            runtime,
                            first.Id,
                            first.ResourceGeneration);
                    Assert.IsTrue(
                        diagnostic.IsNone,
                        diagnostic.Message);
                    Assert.AreEqual(
                        0,
                        backend.ReleaseCount,
                        "Map must release only its own Lease while another Content owner remains live.");
                    Assert.AreEqual(
                        1,
                        runtime.CaptureSnapshot().Entries[0]
                            .LeaseCount);

                    ContentId contentId = second.Id;
                    long resourceGeneration =
                        second.ResourceGeneration;
                    secondScope.Dispose();
                    secondScope = null;
                    UniTask<CoCoDiagnostic> finalObservation =
                        RegionContentReleaseObserver.ObserveAsync(
                                runtime,
                                contentId,
                                resourceGeneration)
                            .Preserve();
                    backend.CompleteRelease(CoCoDiagnostic.None);
                    Assert.IsTrue(
                        (await finalObservation).IsNone);
                }
                finally
                {
                    firstScope?.Dispose();
                    secondScope?.Dispose();
                    backend.CompleteRelease(CoCoDiagnostic.None);
                    await runtime.ShutdownAsync();
                    Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator ReleaseFailureRemainsVisibleToMapCleanup() =>
            UniTask.ToCoroutine(async () =>
            {
                TestAsset asset =
                    ScriptableObject.CreateInstance<TestAsset>();
                var backend = new ControlledReleaseBackend(asset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = null;
                try
                {
                    scope = CreateScope(
                        runtime,
                        "tests.map.release.failure");
                    ContentLease<TestAsset> lease =
                        await AcquireAsync(
                            scope,
                            "tests.map.release.failure");
                    ContentId contentId = lease.Id;
                    long resourceGeneration =
                        lease.ResourceGeneration;

                    scope.Dispose();
                    scope = null;
                    UniTask<CoCoDiagnostic> observation =
                        RegionContentReleaseObserver.ObserveAsync(
                                runtime,
                                contentId,
                                resourceGeneration)
                            .Preserve();
                    backend.CompleteRelease(
                        CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Content,
                            CoCoDiagnosticCode.ContentReleaseFailed,
                            "controlled release failure"));
                    CoCoDiagnostic diagnostic =
                        await observation;
                    Assert.AreEqual(
                        CoCoDiagnosticCode.ContentReleaseFailed,
                        diagnostic.Code);
                    Assert.AreEqual(
                        ContentEntryState.ReleaseFailed,
                        runtime.CaptureSnapshot().Entries[0]
                            .State);
                }
                finally
                {
                    scope?.Dispose();
                    backend.CompleteRelease(CoCoDiagnostic.None);
                    await runtime.ShutdownAsync();
                    Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator TerminalCancellationStopsReleaseObservation() =>
            UniTask.ToCoroutine(async () =>
            {
                TestAsset asset =
                    ScriptableObject.CreateInstance<TestAsset>();
                var backend = new ControlledReleaseBackend(asset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = null;
                var terminalCancellation =
                    new CancellationTokenSource();
                try
                {
                    scope = CreateScope(
                        runtime,
                        "tests.map.release.terminal");
                    ContentLease<TestAsset> lease =
                        await AcquireAsync(
                            scope,
                            "tests.map.release.terminal");
                    ContentId contentId = lease.Id;
                    long resourceGeneration =
                        lease.ResourceGeneration;

                    scope.Dispose();
                    scope = null;
                    UniTask<CoCoDiagnostic> observation =
                        RegionContentReleaseObserver.ObserveAsync(
                                runtime,
                                contentId,
                                resourceGeneration,
                                terminalCancellation.Token)
                            .Preserve();
                    await UniTask.Yield();
                    Assert.AreEqual(
                        UniTaskStatus.Pending,
                        observation.Status);

                    terminalCancellation.Cancel();
                    CoCoDiagnostic diagnostic =
                        await observation;
                    Assert.AreEqual(
                        CoCoDiagnosticCode.RegionCleanupBlocked,
                        diagnostic.Code);
                    Assert.AreEqual(
                        1,
                        backend.ReleaseCount,
                        "Interrupting the Map observer must not issue a second Content release.");
                }
                finally
                {
                    scope?.Dispose();
                    terminalCancellation.Cancel();
                    backend.CompleteRelease(CoCoDiagnostic.None);
                    await runtime.ShutdownAsync();
                    terminalCancellation.Dispose();
                    Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator ContentParticipantCleanupWaitsAndMapsReleaseFailure() =>
            UniTask.ToCoroutine(async () =>
            {
                Scene scene = default;
                ControlledSceneReleaseBackend backend = null;
                ContentRuntime runtime = null;
                ContentParticipantRuntimeBridge bridge = null;
                IRegionParticipantCandidate candidate = null;
                try
                {
                    scene = EditorSceneManager.OpenScene(
                        FixtureScenePath,
                        OpenSceneMode.Additive);
                    backend =
                        new ControlledSceneReleaseBackend(scene);
                    runtime = CreateRuntime(backend);
                    bridge =
                        new ContentParticipantRuntimeBridge(runtime);

                    RegionId regionId =
                        CreateRegionId("world.wilderness");
                    RegionChunkId chunkId =
                        CreateChunkId("wilderness-west");
                    RegionParticipantSlotId slotId =
                        CreateSlotId("content");
                    Assert.That(
                        RegionPlanNodeId.TryCreateChunk(
                            regionId,
                            chunkId,
                            slotId,
                            out RegionPlanNodeId nodeId),
                        Is.True);
                    RegionTierId tierId =
                        CreateTierId("full");
                    RegionCapabilitySet capabilities =
                        CreateFullCapabilities();
                    ContentId contentId =
                        CreateContentId(
                            "tests.map.release.participant");
                    var sceneReference =
                        new RegionCompiledSceneReference(
                            contentId,
                            ContentSourceKind.Addressables,
                            "tests/map/wilderness",
                            FixtureScenePath);

                    var catalog = new RegionParticipantCatalog();
                    Assert.That(
                        RegionContentParticipant.TryRegister(
                            catalog,
                            out CoCoDiagnostic diagnostic),
                        Is.True,
                        diagnostic.Message);
                    Assert.That(
                        catalog.TryGetRegistration(
                            RegionContentParticipant.TypeId,
                            RegionContentParticipant.ModeId,
                            out RegionParticipantRegistration
                                registration),
                        Is.True);

                    var freezeContext =
                        new RegionParticipantFreezeContext(
                            nodeId,
                            tierId,
                            capabilities,
                            string.Empty,
                            sceneReference);
                    Assert.That(
                        registration.ConfigFreezer.TryFreeze(
                            freezeContext,
                            new RegionContentParticipantConfig(),
                            out IRegionParticipantPlan plan,
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);

                    var createContext =
                        new RegionParticipantCreateContext(
                            nodeId,
                            tierId,
                            capabilities,
                            string.Empty,
                            bridge);
                    Assert.That(
                        registration.Factory.TryCreateCandidate(
                            createContext,
                            plan,
                            out candidate,
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);

                    var prepareContext =
                        new RegionParticipantPrepareContext(
                            nodeId,
                            tierId,
                            capabilities,
                            1L,
                            bridge);
                    RegionParticipantPrepareResult prepare =
                        await candidate.PrepareAsync(
                            prepareContext,
                            CancellationToken.None);
                    Assert.That(
                        prepare.Succeeded,
                        Is.True,
                        prepare.Diagnostic.Message);
                    Assert.That(
                        candidate.TryCommit(
                            new RegionParticipantCommitContext(
                                nodeId,
                                tierId,
                                capabilities,
                                1L),
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);

                    UniTask<RegionParticipantCleanupResult> cleanup =
                        candidate.CleanupAsync(
                                RegionParticipantCleanupReason.Removed,
                                CancellationToken.None)
                            .Preserve();
                    await UniTask.Yield();
                    Assert.That(
                        cleanup.Status,
                        Is.EqualTo(UniTaskStatus.Pending),
                        "Participant cleanup must wait for the exact Content backend release.");
                    Assert.That(
                        backend.ReleaseCount,
                        Is.EqualTo(1));

                    backend.CompleteRelease(
                        CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Content,
                            CoCoDiagnosticCode.ContentReleaseFailed,
                            "controlled participant release failure"));
                    RegionParticipantCleanupResult result =
                        await cleanup;
                    Assert.That(result.Succeeded, Is.False);
                    Assert.That(
                        result.Diagnostic.Code,
                        Is.EqualTo(
                            CoCoDiagnosticCode.RegionCleanupBlocked));
                    Assert.That(
                        bridge.UnregisterCount,
                        Is.EqualTo(1));
                }
                finally
                {
                    if (candidate is
                        IRegionParticipantTerminalCleanup terminal)
                    {
                        terminal.ForceCleanupNoFail();
                    }

                    bridge?.DisposeScope();
                    backend?.CompleteRelease(CoCoDiagnostic.None);
                    if (runtime != null)
                    {
                        await runtime.ShutdownAsync();
                    }

                    if (scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            });

        private static ContentRuntime CreateRuntime(
            IContentBackend backend)
        {
            Assert.IsTrue(ContentRuntime.TryCreate(
                new[] { backend },
                64,
                false,
                out ContentRuntime runtime,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return runtime;
        }

        private static ContentScope CreateScope(
            ContentRuntime runtime,
            string ownerValue)
        {
            Assert.IsTrue(ContentOwnerId.TryCreate(
                ownerValue,
                out ContentOwnerId ownerId));
            Assert.IsTrue(runtime.TryCreateScope(
                ownerId,
                out ContentScope scope,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return scope;
        }

        private static async UniTask<ContentLease<TestAsset>>
            AcquireAsync(
                ContentScope scope,
                string contentValue)
        {
            ContentReference reference =
                CreateReference(contentValue);
            ContentAcquireResult<TestAsset> result =
                await scope.AcquireAssetAsync<TestAsset>(
                    reference,
                    CancellationToken.None);
            Assert.IsTrue(
                result.Succeeded,
                result.Diagnostic.Message);
            return result.Lease;
        }

        private static ContentReference CreateReference(
            string contentValue)
        {
            Assert.IsTrue(ContentId.TryCreate(
                contentValue,
                out ContentId contentId));
            Assert.IsTrue(ContentReference.TryCreateAddressableAsset(
                contentId,
                "tests/" + contentValue,
                out ContentReference reference));
            return reference;
        }

        private static RegionId CreateRegionId(string value)
        {
            Assert.That(
                RegionId.TryCreate(value, out RegionId regionId),
                Is.True);
            return regionId;
        }

        private static RegionChunkId CreateChunkId(string value)
        {
            Assert.That(
                RegionChunkId.TryCreate(
                    value,
                    out RegionChunkId chunkId),
                Is.True);
            return chunkId;
        }

        private static RegionParticipantSlotId CreateSlotId(
            string value)
        {
            Assert.That(
                RegionParticipantSlotId.TryCreate(
                    value,
                    out RegionParticipantSlotId slotId),
                Is.True);
            return slotId;
        }

        private static RegionTierId CreateTierId(string value)
        {
            Assert.That(
                RegionTierId.TryCreate(
                    value,
                    out RegionTierId tierId),
                Is.True);
            return tierId;
        }

        private static ContentId CreateContentId(string value)
        {
            Assert.That(
                ContentId.TryCreate(
                    value,
                    out ContentId contentId),
                Is.True);
            return contentId;
        }

        private static RegionCapabilitySet CreateFullCapabilities()
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

        private sealed class TestAsset : ScriptableObject
        {
        }

        private sealed class ContentParticipantRuntimeBridge :
            IRegionFragmentResolver,
            IRegionContentParticipantRuntime
        {
            private readonly ContentRuntime runtime;
            private ContentScope scope;
            private RegionPlanNodeId registeredNodeId;
            private CoCoRegionChunkAnchor registeredAnchor;

            internal ContentParticipantRuntimeBridge(
                ContentRuntime runtime)
            {
                this.runtime = runtime;
            }

            internal int UnregisterCount { get; private set; }

            public bool TryResolveGameObject(
                string fragmentId,
                out GameObject gameObject,
                out CoCoDiagnostic diagnostic)
            {
                gameObject = null;
                diagnostic = RegionErrors.SceneContract(
                    "The Content participant test does not resolve fragments.");
                return false;
            }

            public bool TryCreateContentScope(
                RegionPlanNodeId nodeId,
                long transitionGeneration,
                out ContentScope createdScope,
                out CoCoDiagnostic diagnostic)
            {
                _ = nodeId;
                if (transitionGeneration <= 0L ||
                    scope != null)
                {
                    createdScope = null;
                    diagnostic = RegionErrors.DemandConflict(
                        "The Content participant test received an invalid repeated scope request.");
                    return false;
                }

                Assert.That(
                    ContentOwnerId.TryCreate(
                        "tests.map.release.participant",
                        out ContentOwnerId ownerId),
                    Is.True);
                if (!runtime.TryCreateScope(
                        ownerId,
                        out createdScope,
                        out diagnostic))
                {
                    return false;
                }

                scope = createdScope;
                return true;
            }

            public bool TryRegisterChunkAnchor(
                RegionPlanNodeId nodeId,
                long transitionGeneration,
                CoCoRegionChunkAnchor anchor,
                out CoCoDiagnostic diagnostic)
            {
                if (!nodeId.HasChunkId ||
                    transitionGeneration <= 0L ||
                    anchor == null ||
                    registeredAnchor != null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The Content participant test received an invalid Anchor registration.");
                    return false;
                }

                registeredNodeId = nodeId;
                registeredAnchor = anchor;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void UnregisterChunkAnchor(
                RegionPlanNodeId nodeId,
                CoCoRegionChunkAnchor anchor)
            {
                if (nodeId != registeredNodeId ||
                    anchor != registeredAnchor)
                {
                    return;
                }

                registeredNodeId = default;
                registeredAnchor = null;
                UnregisterCount++;
            }

            public UniTask<CoCoDiagnostic>
                ObserveContentReleaseAsync(
                    ContentId contentId,
                    long resourceGeneration,
                    CancellationToken terminalCancellationToken) =>
                RegionContentReleaseObserver.ObserveAsync(
                    runtime,
                    contentId,
                    resourceGeneration,
                    terminalCancellationToken);

            internal void DisposeScope()
            {
                scope?.Dispose();
                scope = null;
            }
        }

        private sealed class ControlledSceneReleaseBackend :
            IContentBackend
        {
            private static readonly ContentBackendId Id =
                CreateBackendId();
            private readonly Scene scene;
            private readonly UniTaskCompletionSource<CoCoDiagnostic>
                releaseCompletion =
                    new UniTaskCompletionSource<CoCoDiagnostic>();
            private bool releaseCompleted;

            internal ControlledSceneReleaseBackend(Scene scene)
            {
                this.scene = scene;
            }

            public ContentBackendId BackendId => Id;
            internal int ReleaseCount { get; private set; }

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind ==
                ContentSourceKind.Addressables &&
                reference.Kind ==
                ContentKind.AdditiveScene;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                _ = request;
                _ = lifetimeCancellationToken;
                return UniTask.FromResult(
                    ContentBackendLoadResult.Success(
                        scene,
                        ReleaseAsync));
            }

            internal void CompleteRelease(
                CoCoDiagnostic diagnostic)
            {
                if (releaseCompleted)
                {
                    return;
                }

                releaseCompleted = true;
                releaseCompletion.TrySetResult(diagnostic);
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                return releaseCompletion.Task;
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.map.release-observer.scene",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class ControlledReleaseBackend :
            IContentBackend
        {
            private static readonly ContentBackendId Id =
                CreateBackendId();
            private readonly TestAsset asset;
            private readonly UniTaskCompletionSource<CoCoDiagnostic>
                releaseCompletion =
                    new UniTaskCompletionSource<CoCoDiagnostic>();
            private bool releaseCompleted;

            internal ControlledReleaseBackend(TestAsset asset)
            {
                this.asset = asset;
            }

            public ContentBackendId BackendId => Id;
            internal int ReleaseCount { get; private set; }

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind ==
                ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.Asset;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                _ = request;
                _ = lifetimeCancellationToken;
                return UniTask.FromResult(
                    ContentBackendLoadResult.Success(
                        asset,
                        ReleaseAsync));
            }

            internal void CompleteRelease(
                CoCoDiagnostic diagnostic)
            {
                if (releaseCompleted)
                {
                    return;
                }

                releaseCompleted = true;
                releaseCompletion.TrySetResult(diagnostic);
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                return releaseCompletion.Task;
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.map.release-observer",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

    }
}
