using System;
using System.Collections.Generic;
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
            PoolProfile profile)
        {
            OwnerRoot = ownerRoot;
            Prefab = prefab;
            PrefabSource = prefabSource;
            ContentRuntime = contentRuntime;
            PoolRuntime = poolRuntime;
            Scope = scope;
            Profile = profile;
        }

        internal GameObject OwnerRoot { get; }
        internal GameObject Prefab { get; }
        internal ContentReference PrefabSource { get; }
        internal ContentRuntime ContentRuntime { get; }
        internal PoolRuntime PoolRuntime { get; }
        internal PoolScope Scope { get; }
        internal PoolProfile Profile { get; }

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
