using System.Collections;
using System.Linq;
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

        private static async UniTask RunSceneOwnershipContractAsync()
        {
            ContentRuntime runtime = null;
            ContentScope scopeA = null;
            ContentScope scopeB = null;
            ContentLease<Scene> leaseA = null;
            ContentLease<Scene> leaseB = null;
            try
            {
                Assert.IsTrue(ContentRuntime.TryCreate(
                    null,
                    64,
                    false,
                    out runtime,
                    out CoCoDiagnostic runtimeDiagnostic),
                    runtimeDiagnostic.Message);
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

        private static ContentReference CreateReference(string contentValue)
        {
            Assert.IsTrue(ContentId.TryCreate(contentValue, out ContentId contentId));
            Assert.IsTrue(ContentReference.TryCreateDirectAdditiveScene(
                contentId,
                FixtureScenePath,
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
