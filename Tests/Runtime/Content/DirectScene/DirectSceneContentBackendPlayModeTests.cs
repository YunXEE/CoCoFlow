using System;
using System.Collections;
using System.Linq;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Content.Tests.DirectScene
{
    public sealed class DirectSceneContentBackendPlayModeTests :
        IPrebuildSetup,
        IPostBuildCleanup
    {
        private const string FixtureScenePath =
            "Packages/com.yunxee.cocoflow/Tests/Runtime/Content/DirectScene/" +
            "Fixtures/DirectSceneContentFixture.unity";
        private const string FixtureSceneName = "DirectSceneContentFixture";
        private const string FixtureRelativePath =
            "Tests/Runtime/Content/DirectScene/Fixtures/DirectSceneContentFixture";

        public void Setup()
        {
#if UNITY_EDITOR
            if (EditorBuildSettings.scenes.Any(scene => scene.path == FixtureScenePath))
            {
                return;
            }

            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Concat(new[] { new EditorBuildSettingsScene(FixtureScenePath, true) })
                .ToArray();
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Where(scene => scene.path != FixtureScenePath)
                .ToArray();
#endif
        }

        [UnityTest]
        public IEnumerator ConcurrentSameLocatorLoadsOwnAndReleaseDistinctSceneInstances() =>
            UniTask.ToCoroutine(RunSceneOwnershipContractAsync);

        [UnityTest]
        public IEnumerator SupportedLocatorVariantsOwnAndReleaseWithZeroResidualScenes() =>
            UniTask.ToCoroutine(async () =>
            {
                string[] locations =
                {
                    FixtureScenePath,
                    FixtureScenePath.Substring(
                        0,
                        FixtureScenePath.Length - ".unity".Length),
                    FixtureRelativePath,
                    FixtureRelativePath + ".UNITY",
                    FixtureSceneName,
                    (FixtureSceneName + ".unity").ToUpperInvariant(),
                    FixtureScenePath.ToUpperInvariant(),
                    FixtureRelativePath.Replace('/', '\\')
                };
                ContentRuntime runtime = null;
                ContentScope scope = null;
                ContentLease<Scene> lease = null;
                DirectSceneLifecycleProbe.ResetCounts();
                try
                {
                    runtime = CreateRuntime();
                    for (int index = 0; index < locations.Length; index++)
                    {
                        scope = CreateScope(runtime, "owner.direct-scene.locator." + index);
                        ContentReference reference = CreateReference(
                            "content.direct-scene.locator." + index,
                            locations[index]);
                        ContentAcquireResult<Scene> result =
                            await scope.AcquireAdditiveSceneAsync(reference);

                        Assert.IsTrue(
                            result.Succeeded,
                            locations[index] + ": " + result.Diagnostic.Message);
                        lease = result.Lease;
                        Scene scene = lease.Value;
                        Assert.IsTrue(scene.IsValid() && scene.isLoaded);
                        Assert.IsTrue(string.Equals(
                            FixtureScenePath,
                            scene.path,
                            StringComparison.OrdinalIgnoreCase));

                        int handle = scene.handle;
                        lease.Dispose();
                        lease = null;
                        await WaitUntilSceneUnloadedAsync(handle);
                        scope.Dispose();
                        scope = null;
                        await WaitUntilRuntimeEmptyAsync(runtime);
                        Assert.AreEqual(0, CountLoadedFixtureScenes());
                    }

                    Assert.AreEqual(locations.Length, DirectSceneLifecycleProbe.AwakeCount);
                    Assert.AreEqual(locations.Length, DirectSceneLifecycleProbe.EnableCount);
                }
                finally
                {
                    lease?.Dispose();
                    scope?.Dispose();
                    if (runtime != null)
                    {
                        await runtime.ShutdownAsync();
                    }
                }
            });

        [UnityTest]
        public IEnumerator CancelledQueuedLoadIsSkippedAndNextWaiterAcquiresGate() =>
            UniTask.ToCoroutine(async () =>
            {
                ContentRuntime runtime = null;
                ContentScope scopeA = null;
                ContentScope scopeB = null;
                ContentScope scopeC = null;
                ContentLease<Scene> leaseA = null;
                ContentLease<Scene> leaseC = null;
                var cancellation = new CancellationTokenSource();
                DirectSceneLifecycleProbe.ResetCounts();
                try
                {
                    runtime = CreateRuntime();
                    scopeA = CreateScope(runtime, "owner.direct-scene.gate.a");
                    scopeB = CreateScope(runtime, "owner.direct-scene.gate.b");
                    scopeC = CreateScope(runtime, "owner.direct-scene.gate.c");
                    ContentReference referenceA =
                        CreateReference("content.direct-scene.gate.a");
                    ContentReference referenceB =
                        CreateReference("content.direct-scene.gate.b");
                    ContentReference referenceC =
                        CreateReference("content.direct-scene.gate.c");

                    UniTask<ContentAcquireResult<Scene>> requestA =
                        scopeA.AcquireAdditiveSceneAsync(referenceA);
                    UniTask<ContentAcquireResult<Scene>> requestB =
                        scopeB.AcquireAdditiveSceneAsync(referenceB, cancellation.Token);
                    UniTask<ContentAcquireResult<Scene>> requestC =
                        scopeC.AcquireAdditiveSceneAsync(referenceC);
                    cancellation.Cancel();

                    ContentAcquireResult<Scene> resultB = await requestB;
                    ContentAcquireResult<Scene> resultA = await requestA;
                    ContentAcquireResult<Scene> resultC = await requestC;

                    Assert.IsTrue(resultB.Cancelled, resultB.Diagnostic.Message);
                    Assert.IsTrue(resultA.Succeeded, resultA.Diagnostic.Message);
                    Assert.IsTrue(resultC.Succeeded, resultC.Diagnostic.Message);
                    leaseA = resultA.Lease;
                    leaseC = resultC.Lease;
                    Assert.AreNotEqual(leaseA.Value.handle, leaseC.Value.handle);
                    Assert.AreEqual(
                        2,
                        DirectSceneLifecycleProbe.AwakeCount,
                        "The cancelled gate waiter must not start a physical Scene load.");
                    Assert.AreEqual(2, DirectSceneLifecycleProbe.EnableCount);
                    Assert.AreEqual(2, CountLoadedFixtureScenes());

                    int handleA = leaseA.Value.handle;
                    int handleC = leaseC.Value.handle;
                    leaseA.Dispose();
                    leaseA = null;
                    leaseC.Dispose();
                    leaseC = null;
                    await WaitUntilSceneUnloadedAsync(handleA);
                    await WaitUntilSceneUnloadedAsync(handleC);
                    await WaitUntilRuntimeEmptyAsync(runtime);
                    Assert.AreEqual(0, CountLoadedFixtureScenes());
                }
                finally
                {
                    cancellation.Dispose();
                    leaseA?.Dispose();
                    leaseC?.Dispose();
                    scopeA?.Dispose();
                    scopeB?.Dispose();
                    scopeC?.Dispose();
                    if (runtime != null)
                    {
                        await runtime.ShutdownAsync();
                    }
                }
            });

        [UnityTest]
        public IEnumerator CancellationAfterPhysicalLoadStartsReclaimsLateScene() =>
            UniTask.ToCoroutine(async () =>
            {
                ContentRuntime runtime = null;
                ContentScope scope = null;
                var cancellation = new CancellationTokenSource();
                DirectSceneLifecycleProbe.ResetCounts();
                try
                {
                    runtime = CreateRuntime();
                    scope = CreateScope(runtime, "owner.direct-scene.late-reclaim");
                    ContentReference reference =
                        CreateReference("content.direct-scene.late-reclaim");

                    UniTask<ContentAcquireResult<Scene>> request =
                        scope.AcquireAdditiveSceneAsync(reference, cancellation.Token);
                    cancellation.Cancel();
                    ContentAcquireResult<Scene> result = await request;

                    Assert.IsTrue(result.Cancelled, result.Diagnostic.Message);
                    await WaitUntilRuntimeEmptyAsync(runtime);
                    Assert.AreEqual(0, CountLoadedFixtureScenes());
                    Assert.AreEqual(
                        1,
                        DirectSceneLifecycleProbe.AwakeCount,
                        "A physical load already started before cancellation and must finish.");
                    Assert.AreEqual(1, DirectSceneLifecycleProbe.EnableCount);
                    Assert.AreEqual(
                        1,
                        runtime.CaptureSnapshot().Diagnostics.Count(record =>
                            record.EventKind ==
                            ContentDiagnosticEventKind.ReleaseSucceeded));
                }
                finally
                {
                    cancellation.Dispose();
                    scope?.Dispose();
                    if (runtime != null)
                    {
                        await runtime.ShutdownAsync();
                    }
                }
            });

        private static async UniTask RunSceneOwnershipContractAsync()
        {
            ContentRuntime runtime = null;
            ContentScope scopeA = null;
            ContentScope scopeB = null;
            ContentLease<Scene> leaseA = null;
            ContentLease<Scene> leaseB = null;
            try
            {
                runtime = CreateRuntime();
                scopeA = CreateScope(runtime, "owner.direct-scene.a");
                scopeB = CreateScope(runtime, "owner.direct-scene.b");
                ContentReference referenceA = CreateReference("content.direct-scene.a");
                ContentReference referenceB = CreateReference("content.direct-scene.b");

                UniTask<ContentAcquireResult<Scene>> requestA =
                    scopeA.AcquireAdditiveSceneAsync(referenceA);
                UniTask<ContentAcquireResult<Scene>> requestB =
                    scopeB.AcquireAdditiveSceneAsync(referenceB);
                ContentAcquireResult<Scene> resultA = await requestA;
                ContentAcquireResult<Scene> resultB = await requestB;

                Assert.IsTrue(resultA.Succeeded, resultA.Diagnostic.Message);
                Assert.IsTrue(resultB.Succeeded, resultB.Diagnostic.Message);
                leaseA = resultA.Lease;
                leaseB = resultB.Lease;
                Scene sceneA = leaseA.Value;
                Scene sceneB = leaseB.Value;
                Assert.IsTrue(sceneA.IsValid() && sceneA.isLoaded);
                Assert.IsTrue(sceneB.IsValid() && sceneB.isLoaded);
                Assert.AreNotEqual(
                    sceneA.handle,
                    sceneB.handle,
                    "Each Direct load must own its exact additive Scene instance.");
                Assert.AreEqual(2, CountLoadedFixtureScenes());

                int handleA = sceneA.handle;
                int handleB = sceneB.handle;
                leaseA.Dispose();
                leaseA = null;
                await WaitUntilSceneUnloadedAsync(handleA);
                Assert.IsTrue(
                    IsSceneLoaded(handleB),
                    "Releasing the first lease must not unload the second instance.");

                leaseB.Dispose();
                leaseB = null;
                await WaitUntilSceneUnloadedAsync(handleB);
                await WaitUntilRuntimeEmptyAsync(runtime);
                Assert.AreEqual(0, CountLoadedFixtureScenes());
                Assert.AreEqual(
                    2,
                    runtime.CaptureSnapshot().Diagnostics.Count(record =>
                        record.EventKind ==
                        ContentDiagnosticEventKind.ReleaseSucceeded));
            }
            finally
            {
                leaseA?.Dispose();
                leaseB?.Dispose();
                scopeA?.Dispose();
                scopeB?.Dispose();
                if (runtime != null)
                {
                    await runtime.ShutdownAsync();
                }
            }
        }

        private static ContentRuntime CreateRuntime()
        {
            Assert.IsTrue(ContentRuntime.TryCreate(
                null,
                64,
                false,
                out ContentRuntime runtime,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return runtime;
        }

        private static ContentScope CreateScope(ContentRuntime runtime, string ownerValue)
        {
            Assert.IsTrue(ContentOwnerId.TryCreate(ownerValue, out ContentOwnerId ownerId));
            Assert.IsTrue(runtime.TryCreateScope(
                ownerId,
                out ContentScope scope,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return scope;
        }

        private static ContentReference CreateReference(
            string contentValue,
            string location = FixtureScenePath)
        {
            Assert.IsTrue(ContentId.TryCreate(contentValue, out ContentId contentId));
            Assert.IsTrue(ContentReference.TryCreateDirectAdditiveScene(
                contentId,
                location,
                out ContentReference reference));
            return reference;
        }

        private static async UniTask WaitUntilSceneUnloadedAsync(int sceneHandle)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                if (!IsSceneLoaded(sceneHandle))
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail("The owned Direct Scene instance did not unload within 300 frames.");
        }

        private static bool IsSceneLoaded(int sceneHandle)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.handle == sceneHandle)
                {
                    return scene.isLoaded;
                }
            }

            return false;
        }

        private static async UniTask WaitUntilRuntimeEmptyAsync(ContentRuntime runtime)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                if (runtime.CaptureSnapshot().Entries.Count == 0)
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail("The Content Runtime retained a Direct Scene entry after unload.");
        }

        private static int CountLoadedFixtureScenes()
        {
            int count = 0;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && scene.path == FixtureScenePath)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
