using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityAddressables = UnityEngine.AddressableAssets.Addressables;

namespace CoCoFlow.Tests.Runtime.Map.Addressables
{
    public sealed class MapAddressablesColdStartPlayModeTests :
        IPrebuildSetup,
        IPostBuildCleanup
    {
        private const string FixtureScenePath =
            "Packages/com.yunxee.cocoflow/Tests/Runtime/Map/" +
            "Fixtures/WildernessColdStartChunk.unity";
        private const string FixtureAddress =
            "cocoflow/tests/map/wilderness-cold-start";
#if UNITY_EDITOR
        private const string OwnershipKey =
            "CoCoFlow.Tests.Map.Addressables.BuildSettingsOwnership";
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
        public IEnumerator AddressablesLeaseReturnsValidColdStartAnchor() =>
            UniTask.ToCoroutine(RunColdStartAsync);

        private static async UniTask RunColdStartAsync()
        {
            AddressablesStaticIsolation isolation = null;
            GameObject backendObject = null;
            ContentRuntime runtime = null;
            ContentScope scope = null;
            ContentLease<Scene> lease = null;
            try
            {
                isolation = new AddressablesStaticIsolation();
                var locator = new ResourceLocationMap(
                    "CoCoFlow Map Addressables Fixture");
                locator.Add(
                    FixtureAddress,
                    new ResourceLocationBase(
                        FixtureAddress,
                        FixtureScenePath,
                        typeof(SceneProvider).FullName,
                        typeof(SceneInstance)));
                UnityAddressables.AddResourceLocator(locator);

                backendObject = new GameObject(
                    "CoCoFlow Map Addressables Backend");
                var backend =
                    backendObject
                        .AddComponent<AddressablesContentBackend>();
                Assert.That(
                    ContentRuntime.TryCreate(
                        new IContentBackend[] { backend },
                        64,
                        false,
                        out runtime,
                        out CoCoDiagnostic diagnostic),
                    Is.True,
                    diagnostic.Message);
                scope = CreateScope(runtime);
                ContentReference reference =
                    CreateReference();
                UniTask<ContentAcquireResult<Scene>> request =
                    scope.AcquireAdditiveSceneAsync(reference)
                        .Preserve();
                await WaitUntilCompleteAsync(
                    request,
                    isolation);
                ContentAcquireResult<Scene> acquire =
                    await request;
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

                int sceneHandle = scene.handle;
                lease.Dispose();
                lease = null;
                await WaitUntilSceneUnloadedAsync(
                    sceneHandle,
                    isolation);
                await WaitUntilRuntimeEmptyAsync(
                    runtime,
                    isolation);
            }
            finally
            {
                lease?.Dispose();
                scope?.Dispose();
                if (runtime != null)
                {
                    await runtime.ShutdownAsync();
                }

                if (backendObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        backendObject);
                }

                isolation?.Dispose();
            }
        }

        private static ContentScope CreateScope(
            ContentRuntime runtime)
        {
            Assert.That(
                ContentOwnerId.TryCreate(
                    "tests.map.addressables",
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
                    "tests.map.wilderness.addressables",
                    out ContentId contentId),
                Is.True);
            Assert.That(
                ContentReference
                    .TryCreateAddressableAdditiveScene(
                        contentId,
                        FixtureAddress,
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

        private static async UniTask WaitUntilCompleteAsync(
            UniTask<ContentAcquireResult<Scene>> request,
            AddressablesStaticIsolation isolation)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                isolation.Pump();
                if (request.Status != UniTaskStatus.Pending)
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail(
                "The Addressables Map request did not complete within 300 frames.");
        }

        private static async UniTask WaitUntilSceneUnloadedAsync(
            int sceneHandle,
            AddressablesStaticIsolation isolation)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                isolation.Pump();
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
                "The Addressables Map fixture did not unload within 300 frames.");
        }

        private static async UniTask WaitUntilRuntimeEmptyAsync(
            ContentRuntime runtime,
            AddressablesStaticIsolation isolation)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                isolation.Pump();
                if (runtime.CaptureSnapshot().Entries.Count == 0)
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail(
                "Content retained the Addressables Map fixture after release.");
        }

        private sealed class AddressablesStaticIsolation :
            IDisposable
        {
            private readonly FieldInfo instanceField;
            private readonly FieldInfo reinitializeField;
            private readonly object originalInstance;
            private readonly bool originalReinitialize;
            private readonly object isolatedInstance;
            private readonly ResourceManager resourceManager;
            private readonly MethodInfo resourceManagerUpdate;
            private bool disposed;

            internal AddressablesStaticIsolation()
            {
                const BindingFlags staticFlags =
                    BindingFlags.Static |
                    BindingFlags.NonPublic;
                const BindingFlags instanceFlags =
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic;
                instanceField =
                    typeof(UnityAddressables).GetField(
                        "m_AddressablesInstance",
                        staticFlags);
                reinitializeField =
                    typeof(UnityAddressables).GetField(
                        "reinitializeAddressables",
                        staticFlags);
                Assert.That(instanceField, Is.Not.Null);
                Assert.That(reinitializeField, Is.Not.Null);

                originalInstance =
                    instanceField.GetValue(null);
                originalReinitialize =
                    (bool)reinitializeField.GetValue(null);
                Type implementationType =
                    instanceField.FieldType;
                ConstructorInfo constructor =
                    implementationType.GetConstructor(
                        instanceFlags,
                        null,
                        new[]
                        {
                            typeof(IAllocationStrategy)
                        },
                        null);
                Assert.That(constructor, Is.Not.Null);
                isolatedInstance = constructor.Invoke(
                    new object[]
                    {
                        new DefaultAllocationStrategy()
                    });

                FieldInfo initializedField =
                    implementationType.GetField(
                        "hasStartedInitialization",
                        instanceFlags);
                FieldInfo sceneProviderField =
                    implementationType.GetField(
                        "SceneProvider",
                        instanceFlags);
                Assert.That(initializedField, Is.Not.Null);
                Assert.That(sceneProviderField, Is.Not.Null);
                initializedField.SetValue(
                    isolatedInstance,
                    true);
                sceneProviderField.SetValue(
                    isolatedInstance,
                    new SceneProvider());
                PropertyInfo resourceManagerProperty =
                    implementationType.GetProperty(
                        "ResourceManager",
                        instanceFlags);
                Assert.That(
                    resourceManagerProperty,
                    Is.Not.Null);
                resourceManager =
                    resourceManagerProperty.GetValue(
                        isolatedInstance) as ResourceManager;
                Assert.That(resourceManager, Is.Not.Null);
                resourceManagerUpdate =
                    typeof(ResourceManager).GetMethod(
                        "Update",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(
                    resourceManagerUpdate,
                    Is.Not.Null);
                SetCallback(
                    isolatedInstance,
                    "m_OnHandleCompleteAction",
                    "OnHandleCompleted");
                SetCallback(
                    isolatedInstance,
                    "m_OnSceneHandleCompleteAction",
                    "OnSceneHandleCompleted");
                SetCallback(
                    isolatedInstance,
                    "m_OnHandleDestroyedAction",
                    "OnHandleDestroyed");

                instanceField.SetValue(
                    null,
                    isolatedInstance);
                reinitializeField.SetValue(null, false);
            }

            internal void Pump()
            {
                resourceManagerUpdate.Invoke(
                    resourceManager,
                    new object[]
                    {
                        Time.unscaledDeltaTime
                    });
            }

            public void Dispose()
            {
                if (disposed) return;

                disposed = true;
                try
                {
                    MethodInfo releaseSceneManager =
                        isolatedInstance
                            .GetType()
                            .GetMethod(
                                "ReleaseSceneManagerOperation",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic);
                    releaseSceneManager?.Invoke(
                        isolatedInstance,
                        null);
                    resourceManager.Dispose();
                }
                finally
                {
                    instanceField.SetValue(
                        null,
                        originalInstance);
                    reinitializeField.SetValue(
                        null,
                        originalReinitialize);
                }
            }

            private static void SetCallback(
                object instance,
                string fieldName,
                string methodName)
            {
                const BindingFlags instanceFlags =
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic;
                FieldInfo field =
                    instance.GetType().GetField(
                        fieldName,
                        instanceFlags);
                MethodInfo method =
                    instance.GetType().GetMethod(
                        methodName,
                        instanceFlags);
                Assert.That(field, Is.Not.Null);
                Assert.That(method, Is.Not.Null);
                field.SetValue(
                    instance,
                    Delegate.CreateDelegate(
                        field.FieldType,
                        instance,
                        method));
            }
        }
    }
}
