using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Core;
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

namespace CoCoFlow.Runtime.Content.Tests.Addressables
{
    public sealed class AddressablesSceneContentBackendPlayModeTests :
        IPrebuildSetup,
        IPostBuildCleanup
    {
        private const string FixtureScenePath =
            "Packages/com.yunxee.cocoflow/Tests/Runtime/Content/Addressables/" +
            "Fixtures/AddressablesSceneContentFixture.unity";
        private const string FixtureAddress =
            "cocoflow/tests/addressables/additive-scene";
#if UNITY_EDITOR
        private const string BuildSettingsOwnershipModeStateKey =
            "CoCoFlow.Tests.Content.Addressables.BuildSettingsOwnership.Mode";
        private const string BuildSettingsOwnershipOrdinalStateKey =
            "CoCoFlow.Tests.Content.Addressables.BuildSettingsOwnership.Ordinal";
        private const int BuildSettingsOwnershipNone = 0;
        private const int BuildSettingsOwnershipAdded = 1;
        private const int BuildSettingsOwnershipEnabled = 2;
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            if (SessionState.GetInt(
                    BuildSettingsOwnershipModeStateKey,
                    BuildSettingsOwnershipNone) !=
                BuildSettingsOwnershipNone)
            {
                return;
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int firstMatchIndex = FindFixtureSceneIndex(scenes, 0);
            if (firstMatchIndex >= 0)
            {
                if (scenes.Any(scene =>
                        scene.enabled &&
                        string.Equals(
                            scene.path,
                            FixtureScenePath,
                            StringComparison.Ordinal)))
                {
                    return;
                }

                RecordBuildSettingsOwnership(BuildSettingsOwnershipEnabled, 0);
                scenes[firstMatchIndex].enabled = true;
                EditorBuildSettings.scenes = scenes;
                return;
            }

            RecordBuildSettingsOwnership(BuildSettingsOwnershipAdded, 0);
            EditorBuildSettings.scenes = scenes
                .Concat(new[] { new EditorBuildSettingsScene(FixtureScenePath, true) })
                .ToArray();
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            int ownershipMode = SessionState.GetInt(
                BuildSettingsOwnershipModeStateKey,
                BuildSettingsOwnershipNone);
            if (ownershipMode == BuildSettingsOwnershipNone)
            {
                return;
            }

            int ownedOrdinal = SessionState.GetInt(
                BuildSettingsOwnershipOrdinalStateKey,
                -1);
            if (ownedOrdinal < 0 ||
                (ownershipMode != BuildSettingsOwnershipAdded &&
                 ownershipMode != BuildSettingsOwnershipEnabled))
            {
                throw new InvalidOperationException(
                    "Addressables Scene Build Settings ownership state is invalid.");
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int ownedIndex = FindFixtureSceneIndex(scenes, ownedOrdinal);
            if (ownershipMode == BuildSettingsOwnershipAdded)
            {
                if (ownedIndex >= 0)
                {
                    EditorBuildSettings.scenes = scenes
                        .Where((scene, index) => index != ownedIndex)
                        .ToArray();
                }
            }
            else if (ownedIndex >= 0 && scenes[ownedIndex].enabled)
            {
                scenes[ownedIndex].enabled = false;
                EditorBuildSettings.scenes = scenes;
            }

            ClearBuildSettingsOwnership();
#endif
        }

#if UNITY_EDITOR
        private static int FindFixtureSceneIndex(
            EditorBuildSettingsScene[] scenes,
            int fixtureOrdinal)
        {
            int currentOrdinal = 0;
            for (int index = 0; index < scenes.Length; index++)
            {
                if (!string.Equals(
                        scenes[index].path,
                        FixtureScenePath,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (currentOrdinal == fixtureOrdinal)
                {
                    return index;
                }

                currentOrdinal++;
            }

            return -1;
        }

        private static void RecordBuildSettingsOwnership(
            int ownershipMode,
            int ownedOrdinal)
        {
            SessionState.SetInt(
                BuildSettingsOwnershipOrdinalStateKey,
                ownedOrdinal);
            SessionState.SetInt(
                BuildSettingsOwnershipModeStateKey,
                ownershipMode);
        }

        private static void ClearBuildSettingsOwnership()
        {
            SessionState.EraseInt(BuildSettingsOwnershipModeStateKey);
            SessionState.EraseInt(BuildSettingsOwnershipOrdinalStateKey);
        }
#endif

        [UnityTest]
        public IEnumerator SharedAddressablesSceneLeaseUnloadsAfterFinalRelease() =>
            UniTask.ToCoroutine(RunSharedSceneLifecycleAsync);

        private static async UniTask RunSharedSceneLifecycleAsync()
        {
            AddressablesStaticIsolation isolation = null;
            GameObject backendObject = null;
            ContentRuntime runtime = null;
            ContentScope scopeA = null;
            ContentScope scopeB = null;
            ContentLease<Scene> leaseA = null;
            ContentLease<Scene> leaseB = null;
            try
            {
                isolation = new AddressablesStaticIsolation();
                var locator = new ResourceLocationMap(
                    "CoCoFlow Addressables Scene Fixture");
                locator.Add(
                    FixtureAddress,
                    new ResourceLocationBase(
                        FixtureAddress,
                        FixtureScenePath,
                        typeof(SceneProvider).FullName,
                        typeof(SceneInstance)));
                UnityAddressables.AddResourceLocator(locator);

                backendObject = new GameObject(
                    "CoCoFlow Addressables Scene Backend");
                var backend = backendObject.AddComponent<AddressablesContentBackend>();
                Assert.IsTrue(ContentRuntime.TryCreate(
                    new IContentBackend[] { backend },
                    64,
                    false,
                    out runtime,
                    out CoCoDiagnostic runtimeDiagnostic),
                    runtimeDiagnostic.Message);
                scopeA = CreateScope(runtime, "owner.addressables-scene.a");
                scopeB = CreateScope(runtime, "owner.addressables-scene.b");
                Assert.IsTrue(ContentId.TryCreate(
                    "content.addressables-scene.shared",
                    out ContentId contentId));
                Assert.IsTrue(ContentReference.TryCreateAddressableAdditiveScene(
                    contentId,
                    FixtureAddress,
                    out ContentReference reference));

                UniTask<ContentAcquireResult<Scene>> requestA =
                    scopeA.AcquireAdditiveSceneAsync(reference).Preserve();
                UniTask<ContentAcquireResult<Scene>> requestB =
                    scopeB.AcquireAdditiveSceneAsync(reference).Preserve();
                await WaitUntilRequestsCompleteAsync(isolation, requestA, requestB);
                ContentAcquireResult<Scene> resultA = await requestA;
                ContentAcquireResult<Scene> resultB = await requestB;

                Assert.IsTrue(resultA.Succeeded, resultA.Diagnostic.Message);
                Assert.IsTrue(resultB.Succeeded, resultB.Diagnostic.Message);
                leaseA = resultA.Lease;
                leaseB = resultB.Lease;
                Scene sharedScene = leaseA.Value;
                Assert.IsTrue(sharedScene.IsValid() && sharedScene.isLoaded);
                Assert.AreEqual(sharedScene.handle, leaseB.Value.handle);
                Assert.AreEqual(
                    1,
                    CountLoadedFixtureScenes(),
                    "Overlapping leases must share one physical Addressables Scene load.");

                ContentRuntimeSnapshot loaded = runtime.CaptureSnapshot();
                Assert.AreEqual(1, loaded.Entries.Count);
                Assert.AreEqual(2, loaded.Entries[0].LeaseCount);

                int sceneHandle = sharedScene.handle;
                leaseA.Dispose();
                leaseA = null;
                Assert.IsTrue(
                    IsSceneLoaded(sceneHandle),
                    "Releasing a non-final lease must retain the Addressables Scene.");
                ContentRuntimeSnapshot oneLease = runtime.CaptureSnapshot();
                Assert.AreEqual(1, oneLease.Entries.Count);
                Assert.AreEqual(1, oneLease.Entries[0].LeaseCount);

                leaseB.Dispose();
                leaseB = null;
                await WaitUntilSceneUnloadedAsync(sceneHandle, isolation);
                await WaitUntilRuntimeEmptyAsync(runtime, isolation);
                Assert.AreEqual(0, CountLoadedFixtureScenes());
                Assert.AreEqual(
                    1,
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

                if (backendObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(backendObject);
                }

                isolation?.Dispose();
            }
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

        private static async UniTask WaitUntilRequestsCompleteAsync(
            AddressablesStaticIsolation isolation,
            UniTask<ContentAcquireResult<Scene>> requestA,
            UniTask<ContentAcquireResult<Scene>> requestB)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                isolation.Pump();
                if (requestA.Status != UniTaskStatus.Pending &&
                    requestB.Status != UniTaskStatus.Pending)
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail(
                "The isolated Addressables Scene requests did not complete within 300 frames.");
        }

        private static async UniTask WaitUntilSceneUnloadedAsync(
            int sceneHandle,
            AddressablesStaticIsolation isolation)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                isolation.Pump();
                if (!IsSceneLoaded(sceneHandle))
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail(
                "The owned Addressables Scene did not unload within 300 frames.");
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
                "The Content Runtime retained an Addressables Scene entry after unload.");
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

        private sealed class AddressablesStaticIsolation : IDisposable
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
                    BindingFlags.Static | BindingFlags.NonPublic;
                const BindingFlags instanceFlags =
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic;
                instanceField = typeof(UnityAddressables).GetField(
                    "m_AddressablesInstance",
                    staticFlags);
                reinitializeField = typeof(UnityAddressables).GetField(
                    "reinitializeAddressables",
                    staticFlags);
                Assert.IsNotNull(instanceField);
                Assert.IsNotNull(reinitializeField);

                originalInstance = instanceField.GetValue(null);
                originalReinitialize = (bool)reinitializeField.GetValue(null);
                Type implementationType = instanceField.FieldType;
                ConstructorInfo constructor = implementationType.GetConstructor(
                    instanceFlags,
                    null,
                    new[] { typeof(IAllocationStrategy) },
                    null);
                Assert.IsNotNull(constructor);
                isolatedInstance = constructor.Invoke(
                    new object[] { new DefaultAllocationStrategy() });

                FieldInfo initializedField = implementationType.GetField(
                    "hasStartedInitialization",
                    instanceFlags);
                FieldInfo sceneProviderField = implementationType.GetField(
                    "SceneProvider",
                    instanceFlags);
                Assert.IsNotNull(initializedField);
                Assert.IsNotNull(sceneProviderField);
                initializedField.SetValue(isolatedInstance, true);
                sceneProviderField.SetValue(isolatedInstance, new SceneProvider());
                PropertyInfo resourceManagerProperty = implementationType.GetProperty(
                    "ResourceManager",
                    instanceFlags);
                Assert.IsNotNull(resourceManagerProperty);
                resourceManager =
                    resourceManagerProperty.GetValue(isolatedInstance) as ResourceManager;
                Assert.IsNotNull(resourceManager);
                resourceManagerUpdate = typeof(ResourceManager).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(resourceManagerUpdate);
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

                instanceField.SetValue(null, isolatedInstance);
                reinitializeField.SetValue(null, false);
            }

            internal void Pump()
            {
                resourceManagerUpdate.Invoke(
                    resourceManager,
                    new object[] { Time.unscaledDeltaTime });
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                try
                {
                    MethodInfo releaseSceneManager =
                        isolatedInstance.GetType().GetMethod(
                            "ReleaseSceneManagerOperation",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    releaseSceneManager?.Invoke(isolatedInstance, null);
                    resourceManager.Dispose();
                }
                finally
                {
                    instanceField.SetValue(null, originalInstance);
                    reinitializeField.SetValue(null, originalReinitialize);
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
                FieldInfo field = instance.GetType().GetField(
                    fieldName,
                    instanceFlags);
                MethodInfo method = instance.GetType().GetMethod(
                    methodName,
                    instanceFlags);
                Assert.IsNotNull(field);
                Assert.IsNotNull(method);
                field.SetValue(
                    instance,
                    Delegate.CreateDelegate(field.FieldType, instance, method));
            }
        }
    }
}
