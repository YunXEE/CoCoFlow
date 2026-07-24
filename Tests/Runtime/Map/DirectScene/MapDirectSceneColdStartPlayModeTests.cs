using System;
using System.Collections;
using System.Linq;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Map.DirectScene
{
    public sealed class MapDirectSceneColdStartPlayModeTests :
        IPrebuildSetup,
        IPostBuildCleanup
    {
        private const string FixtureScenePath =
            "Packages/com.yunxee.cocoflow/Tests/Runtime/Map/" +
            "Fixtures/WildernessColdStartChunk.unity";
#if UNITY_EDITOR
        private const string OwnershipKey =
            "CoCoFlow.Tests.Map.DirectScene.BuildSettingsOwnership";
        private const int OwnershipNone = 0;
        private const int OwnershipAdded = 1;
        private const int OwnershipEnabled = 2;
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            if (SessionState.GetInt(
                    OwnershipKey,
                    OwnershipNone) != OwnershipNone)
            {
                return;
            }

            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes;
            int index = Array.FindIndex(
                scenes,
                scene => string.Equals(
                    scene.path,
                    FixtureScenePath,
                    StringComparison.Ordinal));
            if (index >= 0)
            {
                if (scenes[index].enabled) return;

                scenes[index].enabled = true;
                EditorBuildSettings.scenes = scenes;
                SessionState.SetInt(
                    OwnershipKey,
                    OwnershipEnabled);
                return;
            }

            EditorBuildSettings.scenes = scenes
                .Concat(
                    new[]
                    {
                        new EditorBuildSettingsScene(
                            FixtureScenePath,
                            true)
                    })
                .ToArray();
            SessionState.SetInt(
                OwnershipKey,
                OwnershipAdded);
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            int ownership = SessionState.GetInt(
                OwnershipKey,
                OwnershipNone);
            if (ownership == OwnershipNone) return;

            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes;
            int index = Array.FindIndex(
                scenes,
                scene => string.Equals(
                    scene.path,
                    FixtureScenePath,
                    StringComparison.Ordinal));
            if (ownership == OwnershipAdded && index >= 0)
            {
                EditorBuildSettings.scenes = scenes
                    .Where((scene, current) => current != index)
                    .ToArray();
            }
            else if (ownership == OwnershipEnabled &&
                     index >= 0)
            {
                scenes[index].enabled = false;
                EditorBuildSettings.scenes = scenes;
            }

            SessionState.EraseInt(OwnershipKey);
#endif
        }

        [UnityTest]
        public IEnumerator DirectLeaseReturnsValidColdStartAnchor() =>
            UniTask.ToCoroutine(async () =>
            {
                Assert.That(
                    ContentRuntime.TryCreate(
                        null,
                        64,
                        false,
                        out ContentRuntime runtime,
                        out CoCoDiagnostic diagnostic),
                    Is.True,
                    diagnostic.Message);
                ContentScope scope = null;
                ContentLease<Scene> lease = null;
                try
                {
                    scope = CreateScope(runtime);
                    ContentReference reference =
                        CreateReference();
                    ContentAcquireResult<Scene> acquire =
                        await scope.AcquireAdditiveSceneAsync(
                            reference);
                    Assert.That(
                        acquire.Succeeded,
                        Is.True,
                        acquire.Diagnostic.Message);
                    lease = acquire.Lease;

                    Scene scene = lease.Value;
                    Assert.That(
                        scene.IsValid() && scene.isLoaded,
                        Is.True);
                    CoCoRegionChunkAnchor anchor =
                        FindUniqueAnchor(scene);
                    Assert.That(
                        anchor.TryValidateColdStart(
                            Region("world.wilderness"),
                            Chunk("wilderness-west"),
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);
                    Assert.That(
                        anchor.ManagedRoots.Count,
                        Is.EqualTo(1));
                    Assert.That(
                        anchor.ManagedRoots[0].activeSelf,
                        Is.False);
                    Assert.That(
                        anchor.TryResolveGameObject(
                            "WildernessTerrain",
                            out GameObject terrain,
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);
                    Assert.That(
                        terrain,
                        Is.SameAs(anchor.ManagedRoots[0]));

                    int sceneHandle = scene.handle;
                    lease.Dispose();
                    lease = null;
                    await WaitUntilSceneUnloadedAsync(
                        sceneHandle);
                    await WaitUntilRuntimeEmptyAsync(runtime);
                }
                finally
                {
                    lease?.Dispose();
                    scope?.Dispose();
                    await runtime.ShutdownAsync();
                }
            });

        private static ContentScope CreateScope(
            ContentRuntime runtime)
        {
            Assert.That(
                ContentOwnerId.TryCreate(
                    "tests.map.direct-scene",
                    out ContentOwnerId ownerId),
                Is.True);
            Assert.That(
                runtime.TryCreateScope(
                    ownerId,
                    out ContentScope scope,
                    out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
            return scope;
        }

        private static ContentReference CreateReference()
        {
            Assert.That(
                ContentId.TryCreate(
                    "tests.map.wilderness.direct",
                    out ContentId contentId),
                Is.True);
            Assert.That(
                ContentReference.TryCreateDirectAdditiveScene(
                    contentId,
                    FixtureScenePath,
                    out ContentReference reference),
                Is.True);
            return reference;
        }

        private static CoCoRegionChunkAnchor FindUniqueAnchor(
            Scene scene)
        {
            CoCoRegionChunkAnchor anchor = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0;
                 rootIndex < roots.Length;
                 rootIndex++)
            {
                CoCoRegionChunkAnchor[] matches =
                    roots[rootIndex]
                        .GetComponentsInChildren<
                            CoCoRegionChunkAnchor>(true);
                for (int index = 0;
                     index < matches.Length;
                     index++)
                {
                    Assert.That(
                        anchor,
                        Is.Null,
                        "The fixture must contain exactly one Anchor.");
                    anchor = matches[index];
                }
            }

            Assert.That(anchor, Is.Not.Null);
            return anchor;
        }

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

        private static async UniTask WaitUntilSceneUnloadedAsync(
            int sceneHandle)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                bool loaded = false;
                for (int index = 0;
                     index < SceneManager.sceneCount;
                     index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    if (scene.handle == sceneHandle)
                    {
                        loaded = scene.isLoaded;
                        break;
                    }
                }

                if (!loaded) return;
                await UniTask.Yield();
            }

            Assert.Fail(
                "The Direct Map fixture did not unload within 300 frames.");
        }

        private static async UniTask WaitUntilRuntimeEmptyAsync(
            ContentRuntime runtime)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                if (runtime.CaptureSnapshot().Entries.Count == 0)
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail(
                "Content retained the Direct Map fixture after release.");
        }
    }
}
