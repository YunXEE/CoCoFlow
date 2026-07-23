using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;
using UnityAddressables = UnityEngine.AddressableAssets.Addressables;

namespace CoCoFlow.Runtime.Content.Tests.Addressables
{
    public sealed class AddressablesContentBackendContractTests
    {
        [Test]
        public void BackendAcceptsOnlyValidAddressablesReferences()
        {
            Assert.IsTrue(ContentId.TryCreate("content.addressables.test", out ContentId id));
            Assert.IsTrue(ContentReference.TryCreateAddressableAsset(
                id,
                "content/addressables/test",
                out ContentReference addressable));
            var gameObject = new GameObject("Addressables Content Backend Test");
            ScriptableObject directAsset = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                var backend = gameObject.AddComponent<AddressablesContentBackend>();
                Assert.AreEqual("addressables", backend.BackendId.Value);
                Assert.IsTrue(backend.CanHandle(addressable));
                Assert.IsFalse(backend.CanHandle(default));

                Assert.IsTrue(ContentReference.TryCreateDirectAsset(
                    id,
                    directAsset,
                    out ContentReference direct));
                Assert.IsFalse(backend.CanHandle(direct));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(directAsset);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AdapterAssemblyDoesNotRequireUniTaskAddressables()
        {
            string[] references = typeof(AddressablesContentBackend).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            CollectionAssert.DoesNotContain(references, "UniTask.Addressables");
            CollectionAssert.Contains(references, "UniTask");
            CollectionAssert.Contains(references, "Unity.Addressables");
        }

        [Test]
        public void AdapterPublicSurfaceExposesNoResourceManagerHandles()
        {
            Assembly assembly = typeof(AddressablesContentBackend).Assembly;
            foreach (Type type in assembly.GetExportedTypes())
            {
                AssertNoHandle(type.BaseType, type.FullName + " base type");
                foreach (PropertyInfo property in type.GetProperties(
                             BindingFlags.Public |
                             BindingFlags.Instance |
                             BindingFlags.Static |
                             BindingFlags.DeclaredOnly))
                {
                    AssertNoHandle(
                        property.PropertyType,
                        type.FullName + "." + property.Name);
                }

                foreach (MethodInfo method in type.GetMethods(
                             BindingFlags.Public |
                             BindingFlags.Instance |
                             BindingFlags.Static |
                             BindingFlags.DeclaredOnly))
                {
                    AssertNoHandle(
                        method.ReturnType,
                        type.FullName + "." + method.Name + " return");
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        AssertNoHandle(
                            parameter.ParameterType,
                            type.FullName + "." + method.Name +
                            " parameter " + parameter.Name);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator RealAddressablesAssetReleasesOnlyAfterLastLease()
        {
            return UniTask.ToCoroutine(RunRealAddressablesAssetLifecycleAsync);
        }

        private static async UniTask RunRealAddressablesAssetLifecycleAsync()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string tempRoot = "Assets/__CoCoFlowAddressablesIntegration_" + suffix;
            string assetPath = tempRoot + "/Payload.asset";
            string address = "cocoflow/tests/addressables/" + suffix;
            string priorRuntimeDataPath = PlayerPrefs.GetString(
                UnityAddressables.kAddressablesRuntimeDataPath,
                string.Empty);
            bool hadRuntimeDataPath = PlayerPrefs.HasKey(
                UnityAddressables.kAddressablesRuntimeDataPath);

            AddressablesStaticIsolation isolation = null;
            BuildScriptFastMode fastModeBuilder = null;
            GameObject backendObject = null;
            ContentRuntime runtime = null;
            ContentScope scopeA = null;
            ContentScope scopeB = null;
            ContentLease<Texture2D> leaseA = null;
            ContentLease<Texture2D> leaseB = null;
            AsyncOperationHandle<IResourceLocator> initializationHandle = default;
            try
            {
                Assert.IsFalse(AssetDatabase.IsValidFolder(tempRoot));
                string folderGuid = AssetDatabase.CreateFolder(
                    "Assets",
                    "__CoCoFlowAddressablesIntegration_" + suffix);
                Assert.IsFalse(string.IsNullOrEmpty(folderGuid));

                var payload = new Texture2D(2, 2)
                {
                    name = "CoCoFlow Addressables Integration Payload"
                };
                AssetDatabase.CreateAsset(payload, assetPath);

                AddressableAssetSettings settings = AddressableAssetSettings.Create(
                    tempRoot,
                    "AddressableAssetSettings",
                    true,
                    true);
                Assert.IsNotNull(settings);
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                AddressableAssetEntry entry = settings.CreateOrMoveEntry(
                    assetGuid,
                    settings.DefaultGroup,
                    false,
                    false);
                Assert.IsNotNull(entry);
                entry.SetAddress(address, false);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                fastModeBuilder = ScriptableObject.CreateInstance<BuildScriptFastMode>();
                AddressablesPlayModeBuildResult buildResult =
                    fastModeBuilder.BuildData<AddressablesPlayModeBuildResult>(
                        new AddressablesDataBuilderInput(settings));
                Assert.IsTrue(
                    string.IsNullOrEmpty(buildResult.Error),
                    buildResult.Error);

                // The static singleton is replaced only inside this fixture so the
                // temporary Settings/locator/provider graph cannot contaminate the host.
                isolation = new AddressablesStaticIsolation();
                initializationHandle = UnityAddressables.InitializeAsync(false);
                IResourceLocator initializedLocator =
                    initializationHandle.WaitForCompletion();
                Assert.AreEqual(
                    AsyncOperationStatus.Succeeded,
                    initializationHandle.Status,
                    initializationHandle.OperationException == null
                        ? string.Empty
                        : initializationHandle.OperationException.ToString());
                Assert.IsNotNull(initializedLocator);
                UnityAddressables.Release(initializationHandle);
                initializationHandle = default;

                const string assetDatabaseProviderId =
                    "UnityEngine.ResourceManagement.ResourceProviders.AssetDatabaseProvider";
                IList<IResourceProvider> providers =
                    UnityAddressables.ResourceManager.ResourceProviders;
                for (int index = providers.Count - 1; index >= 0; index--)
                {
                    if (providers[index].ProviderId == assetDatabaseProviderId)
                    {
                        providers.RemoveAt(index);
                    }
                }

                var trackingProvider = new TrackingAssetDatabaseProvider();
                Assert.IsTrue(trackingProvider.Initialize(assetDatabaseProviderId, string.Empty));
                trackingProvider.SetLoadDelay(0f);
                providers.Add(trackingProvider);
                Assert.IsTrue(
                    UnityAddressables.ResourceLocators.Any(locator =>
                        locator.Locate(
                            address,
                            typeof(UnityEngine.Object),
                            out IList<IResourceLocation> locations) &&
                        locations != null &&
                        locations.Count > 0),
                    "The isolated FastMode settings did not publish the temporary address.");

                backendObject = new GameObject(
                    "CoCoFlow Addressables Integration Backend");
                var backend = backendObject.AddComponent<AddressablesContentBackend>();
                Assert.IsTrue(ContentRuntime.TryCreate(
                    new IContentBackend[] { backend },
                    64,
                    false,
                    out runtime,
                    out CoCoFlow.Runtime.Core.CoCoDiagnostic runtimeDiagnostic),
                    runtimeDiagnostic.Message);
                Assert.IsTrue(ContentId.TryCreate(
                    "content.addressables.integration." + suffix,
                    out ContentId contentId));
                Assert.IsTrue(ContentReference.TryCreateAddressableAsset(
                    contentId,
                    address,
                    out ContentReference reference));
                Assert.IsTrue(ContentOwnerId.TryCreate(
                    "owner.addressables.integration.a." + suffix,
                    out ContentOwnerId ownerA));
                Assert.IsTrue(ContentOwnerId.TryCreate(
                    "owner.addressables.integration.b." + suffix,
                    out ContentOwnerId ownerB));
                Assert.IsTrue(runtime.TryCreateScope(
                    ownerA,
                    out scopeA,
                    out CoCoFlow.Runtime.Core.CoCoDiagnostic scopeADiagnostic),
                    scopeADiagnostic.Message);
                Assert.IsTrue(runtime.TryCreateScope(
                    ownerB,
                    out scopeB,
                    out CoCoFlow.Runtime.Core.CoCoDiagnostic scopeBDiagnostic),
                    scopeBDiagnostic.Message);

                UniTask<ContentAcquireResult<Texture2D>> acquireA =
                    scopeA.AcquireAssetAsync<Texture2D>(reference);
                UniTask<ContentAcquireResult<Texture2D>> acquireB =
                    scopeB.AcquireAssetAsync<Texture2D>(reference);
                ContentAcquireResult<Texture2D> resultA = await acquireA;
                ContentAcquireResult<Texture2D> resultB = await acquireB;
                Assert.IsTrue(resultA.Succeeded, resultA.Diagnostic.Message);
                Assert.IsTrue(resultB.Succeeded, resultB.Diagnostic.Message);
                leaseA = resultA.Lease;
                leaseB = resultB.Lease;
                Assert.AreSame(leaseA.Value, leaseB.Value);
                Assert.AreEqual(
                    1,
                    trackingProvider.ProvideCount,
                    "Overlapping callers must share one physical Addressables load.");

                ContentRuntimeSnapshot loadedSnapshot = runtime.CaptureSnapshot();
                Assert.AreEqual(1, loadedSnapshot.Entries.Count);
                Assert.AreEqual(2, loadedSnapshot.Entries[0].LeaseCount);

                leaseA.Dispose();
                leaseA = null;
                Assert.AreEqual(
                    0,
                    trackingProvider.ReleaseCount,
                    "Releasing a non-final lease must retain the Addressables handle.");
                ContentRuntimeSnapshot oneLeaseSnapshot = runtime.CaptureSnapshot();
                Assert.AreEqual(1, oneLeaseSnapshot.Entries.Count);
                Assert.AreEqual(1, oneLeaseSnapshot.Entries[0].LeaseCount);

                leaseB.Dispose();
                leaseB = null;
                await WaitUntilAsync(
                    () => trackingProvider.ReleaseCount == 1 &&
                          runtime.CaptureSnapshot().Entries.Count == 0,
                    "The final lease did not release the Addressables handle.");
                Assert.AreEqual(1, trackingProvider.ReleaseCount);
                ContentRuntimeSnapshot releasedSnapshot = runtime.CaptureSnapshot();
                Assert.IsTrue(releasedSnapshot.Diagnostics.Any(record =>
                    record.ContentId == contentId &&
                    record.EventKind == ContentDiagnosticEventKind.ReleaseSucceeded));
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

                if (initializationHandle.IsValid())
                {
                    UnityAddressables.Release(initializationHandle);
                }

                if (backendObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(backendObject);
                }

                isolation?.Dispose();
                if (fastModeBuilder != null)
                {
                    UnityEngine.Object.DestroyImmediate(fastModeBuilder);
                }

                if (hadRuntimeDataPath)
                {
                    PlayerPrefs.SetString(
                        UnityAddressables.kAddressablesRuntimeDataPath,
                        priorRuntimeDataPath);
                }
                else
                {
                    PlayerPrefs.DeleteKey(UnityAddressables.kAddressablesRuntimeDataPath);
                }

                if (AssetDatabase.IsValidFolder(tempRoot))
                {
                    AssetDatabase.DeleteAsset(tempRoot);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static async UniTask WaitUntilAsync(
            Func<bool> condition,
            string failureMessage)
        {
            const int maximumFrames = 180;
            for (int frame = 0; frame < maximumFrames; frame++)
            {
                if (condition())
                {
                    return;
                }

                await UniTask.Yield();
            }

            Assert.Fail(failureMessage);
        }

        private static void AssertNoHandle(Type type, string location)
        {
            if (type == null)
            {
                return;
            }

            string fullName = type.IsGenericType
                ? type.GetGenericTypeDefinition().FullName
                : type.FullName;
            Assert.IsFalse(
                fullName != null &&
                fullName.StartsWith(
                    "UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle",
                    StringComparison.Ordinal),
                location + " exposes " + fullName + ".");

            if (!type.IsGenericType)
            {
                return;
            }

            foreach (Type argument in type.GetGenericArguments())
            {
                AssertNoHandle(argument, location);
            }
        }

        private sealed class TrackingAssetDatabaseProvider : AssetDatabaseProvider
        {
            public int ProvideCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public override void Provide(ProvideHandle provideHandle)
            {
                ProvideCount++;
                base.Provide(provideHandle);
            }

            public override void Release(IResourceLocation location, object asset)
            {
                ReleaseCount++;
                base.Release(location, asset);
            }
        }

        private sealed class AddressablesStaticIsolation : IDisposable
        {
            private readonly FieldInfo instanceField;
            private readonly FieldInfo reinitializeField;
            private readonly object originalInstance;
            private readonly bool originalReinitialize;
            private readonly object isolatedInstance;
            private bool disposed;

            public AddressablesStaticIsolation()
            {
                const BindingFlags staticFlags =
                    BindingFlags.Static | BindingFlags.NonPublic;
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
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(IAllocationStrategy) },
                    null);
                Assert.IsNotNull(constructor);
                isolatedInstance = constructor.Invoke(
                    new object[] { new DefaultAllocationStrategy() });
                instanceField.SetValue(null, isolatedInstance);
                reinitializeField.SetValue(null, false);
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
                    MethodInfo releaseSceneManager = isolatedInstance.GetType().GetMethod(
                        "ReleaseSceneManagerOperation",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    releaseSceneManager?.Invoke(isolatedInstance, null);
                    PropertyInfo resourceManagerProperty = isolatedInstance.GetType()
                        .GetProperty(
                            "ResourceManager",
                            BindingFlags.Instance | BindingFlags.Public);
                    (resourceManagerProperty?.GetValue(isolatedInstance) as ResourceManager)
                        ?.Dispose();
                }
                finally
                {
                    instanceField.SetValue(null, originalInstance);
                    reinitializeField.SetValue(null, originalReinitialize);
                }
            }
        }
    }
}
