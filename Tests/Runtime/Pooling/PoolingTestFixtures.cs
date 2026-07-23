using System;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    internal sealed class PoolingTestFixture
    {
        private PoolingTestFixture(
            GameObject ownerRoot,
            GameObject prefab,
            ContentReference prefabSource,
            ContentRuntime contentRuntime,
            PoolRuntime poolRuntime,
            PoolScope scope,
            PoolProfile profile,
            PoolingDelayedPrefabBackend delayedBackend = null)
        {
            OwnerRoot = ownerRoot;
            Prefab = prefab;
            PrefabSource = prefabSource;
            ContentRuntime = contentRuntime;
            PoolRuntime = poolRuntime;
            Scope = scope;
            Profile = profile;
            DelayedBackend = delayedBackend;
        }

        internal GameObject OwnerRoot { get; }
        internal GameObject Prefab { get; }
        internal ContentReference PrefabSource { get; }
        internal ContentRuntime ContentRuntime { get; }
        internal PoolRuntime PoolRuntime { get; }
        internal PoolScope Scope { get; }
        internal PoolProfile Profile { get; }
        internal PoolingDelayedPrefabBackend DelayedBackend { get; }

        internal static PoolingTestFixture Create(
            int prewarmCount,
            int maxRetained,
            Action<GameObject> configurePrefab = null)
        {
            PoolingMainThreadGuard.CaptureCurrentThread();
            var ownerRoot = new GameObject("Pre9 Pooling Test Root");
            var prefab = new GameObject("Pre9 Pooling Test Prefab");
            prefab.SetActive(false);
            configurePrefab?.Invoke(prefab);

            string suffix = Guid.NewGuid().ToString("N");
            Assert.That(
                ContentId.TryCreate(
                    "tests.pooling.prefab." + suffix,
                    out ContentId contentId),
                Is.True);
            Assert.That(
                ContentReference.TryCreateDirectPrefabSource(
                    contentId,
                    prefab,
                    out ContentReference prefabSource),
                Is.True);
            Assert.That(
                PoolId.TryCreate("tests.pooling." + suffix, out PoolId poolId),
                Is.True);
            Assert.That(
                PoolProfile.TryCreate(
                    poolId,
                    prefabSource,
                    prewarmCount,
                    maxRetained,
                    out PoolProfile profile),
                Is.True);

            Assert.That(
                ContentRuntime.TryCreate(
                    out ContentRuntime contentRuntime,
                    out CoCoDiagnostic contentDiagnostic),
                Is.True,
                contentDiagnostic.Message);
            Assert.That(
                PoolRuntime.TryCreate(
                    contentRuntime,
                    ownerRoot.transform,
                    256,
                    false,
                    out PoolRuntime poolRuntime,
                    out CoCoDiagnostic poolDiagnostic),
                Is.True,
                poolDiagnostic.Message);
            Assert.That(
                ContentOwnerId.TryCreate(
                    "tests.pooling.owner." + suffix,
                    out ContentOwnerId ownerId),
                Is.True);
            Assert.That(
                poolRuntime.TryCreateScope(
                    ownerId,
                    out PoolScope scope,
                    out CoCoDiagnostic scopeDiagnostic),
                Is.True,
                scopeDiagnostic.Message);

            return new PoolingTestFixture(
                ownerRoot,
                prefab,
                prefabSource,
                contentRuntime,
                poolRuntime,
                scope,
                profile);
        }

        internal static PoolingTestFixture CreateDelayed(
            int prewarmCount,
            int maxRetained,
            Action<GameObject> configurePrefab = null)
        {
            PoolingMainThreadGuard.CaptureCurrentThread();
            var ownerRoot = new GameObject("Pre9 Delayed Pooling Test Root");
            PoolingDelayedPrefabBackend delayedBackend =
                ownerRoot.AddComponent<PoolingDelayedPrefabBackend>();
            var prefab = new GameObject("Pre9 Delayed Pooling Test Prefab");
            prefab.SetActive(false);
            configurePrefab?.Invoke(prefab);

            string suffix = Guid.NewGuid().ToString("N");
            Assert.That(
                ContentId.TryCreate(
                    "tests.pooling.delayed.prefab." + suffix,
                    out ContentId contentId),
                Is.True);
            Assert.That(
                ContentReference.TryCreateAddressablePrefabSource(
                    contentId,
                    "tests/pooling/delayed/" + suffix,
                    out ContentReference prefabSource),
                Is.True);
            Assert.That(
                PoolId.TryCreate(
                    "tests.pooling.delayed." + suffix,
                    out PoolId poolId),
                Is.True);
            Assert.That(
                PoolProfile.TryCreate(
                    poolId,
                    prefabSource,
                    prewarmCount,
                    maxRetained,
                    out PoolProfile profile),
                Is.True);

            Assert.That(
                ContentRuntime.TryCreate(
                    new IContentBackend[] { delayedBackend },
                    256,
                    false,
                    out ContentRuntime contentRuntime,
                    out CoCoDiagnostic contentDiagnostic),
                Is.True,
                contentDiagnostic.Message);
            Assert.That(
                PoolRuntime.TryCreate(
                    contentRuntime,
                    ownerRoot.transform,
                    256,
                    false,
                    out PoolRuntime poolRuntime,
                    out CoCoDiagnostic poolDiagnostic),
                Is.True,
                poolDiagnostic.Message);
            Assert.That(
                ContentOwnerId.TryCreate(
                    "tests.pooling.delayed.owner." + suffix,
                    out ContentOwnerId ownerId),
                Is.True);
            Assert.That(
                poolRuntime.TryCreateScope(
                    ownerId,
                    out PoolScope scope,
                    out CoCoDiagnostic scopeDiagnostic),
                Is.True,
                scopeDiagnostic.Message);

            return new PoolingTestFixture(
                ownerRoot,
                prefab,
                prefabSource,
                contentRuntime,
                poolRuntime,
                scope,
                profile,
                delayedBackend);
        }

        internal PoolProfile CreateSiblingProfile(
            string suffix,
            int prewarmCount,
            int maxRetained)
        {
            Assert.That(
                PoolId.TryCreate(
                    Profile.Id.Value + "." + suffix,
                    out PoolId poolId),
                Is.True);
            Assert.That(
                PoolProfile.TryCreate(
                    poolId,
                    PrefabSource,
                    prewarmCount,
                    maxRetained,
                    out PoolProfile profile),
                Is.True);
            return profile;
        }

        internal async UniTask CleanupAsync()
        {
            if (PoolRuntime != null && !PoolRuntime.IsDisposed)
            {
                PoolRuntime.ForceShutdown();
                await UniTask.NextFrame();
            }

            if (ContentRuntime != null && !ContentRuntime.IsDisposed)
            {
                await ContentRuntime.ShutdownAsync();
            }

            if (OwnerRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(OwnerRoot);
            }

            if (Prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(Prefab);
            }
        }
    }

    internal sealed class PoolingDelayedPrefabBackend :
        MonoBehaviour,
        IContentBackend
    {
        private static readonly ContentBackendId Id = CreateBackendId();
        private readonly Queue<UniTaskCompletionSource<ContentBackendLoadResult>>
            _pendingLoads =
                new Queue<UniTaskCompletionSource<ContentBackendLoadResult>>();

        internal int LoadCount { get; private set; }
        internal int ReleaseCount { get; private set; }
        internal int PendingCount => _pendingLoads.Count;

        public ContentBackendId BackendId => Id;

        public bool CanHandle(ContentReference reference) =>
            reference.IsValid &&
            reference.SourceKind == ContentSourceKind.Addressables &&
            reference.Kind == ContentKind.PrefabSource;

        public async UniTask<ContentBackendLoadResult> LoadAsync(
            ContentBackendRequest request,
            CancellationToken lifetimeCancellationToken)
        {
            _ = request;
            LoadCount++;
            var completion =
                new UniTaskCompletionSource<ContentBackendLoadResult>();
            _pendingLoads.Enqueue(completion);
            using (lifetimeCancellationToken.Register(
                       () => completion.TrySetCanceled(lifetimeCancellationToken)))
            {
                return await completion.Task;
            }
        }

        internal void CompleteNextSuccess(GameObject prefab)
        {
            while (_pendingLoads.Count > 0)
            {
                UniTaskCompletionSource<ContentBackendLoadResult> completion =
                    _pendingLoads.Dequeue();
                if (completion.TrySetResult(
                        ContentBackendLoadResult.Success(
                            prefab,
                            ReleaseAsync)))
                {
                    return;
                }
            }

            Assert.Fail("No pending delayed Prefab Source load was available.");
        }

        internal void CompleteNextFailure()
        {
            CoCoDiagnostic diagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Content,
                CoCoDiagnosticCode.ContentLoadFailed,
                "Delayed Pooling test backend rejected the load.");
            while (_pendingLoads.Count > 0)
            {
                UniTaskCompletionSource<ContentBackendLoadResult> completion =
                    _pendingLoads.Dequeue();
                if (completion.TrySetResult(
                        ContentBackendLoadResult.Failure(diagnostic)))
                {
                    return;
                }
            }

            Assert.Fail("No pending delayed Prefab Source load was available.");
        }

        private UniTask<CoCoDiagnostic> ReleaseAsync()
        {
            ReleaseCount++;
            return UniTask.FromResult(CoCoDiagnostic.None);
        }

        private static ContentBackendId CreateBackendId()
        {
            Assert.That(
                ContentBackendId.TryCreate(
                    "tests.pooling-delayed-prefab",
                    out ContentBackendId backendId),
                Is.True);
            return backendId;
        }
    }

    internal sealed class PoolLifecycleProbe :
        MonoBehaviour,
        IPoolable
    {
        private static readonly List<string> EventBuffer = new List<string>();

        [SerializeField] private string participantId;

        internal static IReadOnlyList<string> Events => EventBuffer;
        internal string BoundValue { get; set; }
        internal bool FailRent { get; set; }
        internal bool FailReturn { get; set; }
        internal PoolRentContext LastRentContext { get; private set; }
        internal PoolReturnContext LastReturnContext { get; private set; }

        internal static void ResetEvents() => EventBuffer.Clear();

        internal void Configure(string value)
        {
            participantId = value;
        }

        public bool TryOnRent(
            in PoolRentContext context,
            out CoCoDiagnostic diagnostic)
        {
            LastRentContext = context;
            EventBuffer.Add(
                "rent:" + participantId + ":" + BoundValue + ":" +
                gameObject.activeInHierarchy);
            if (FailRent)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Pooling,
                    CoCoDiagnosticCode.PoolActivationFailed,
                    "Test probe rejected rent.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryOnReturn(
            in PoolReturnContext context,
            out CoCoDiagnostic diagnostic)
        {
            LastReturnContext = context;
            EventBuffer.Add(
                "return:" + participantId + ":" + context.Reason + ":" +
                gameObject.activeInHierarchy);
            if (FailReturn)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Pooling,
                    CoCoDiagnosticCode.PoolResetFailed,
                    "Test probe rejected return.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void OnEnable()
        {
            EventBuffer.Add("enable:" + participantId);
        }

        private void OnDisable()
        {
            EventBuffer.Add("disable:" + participantId);
        }
    }
}
