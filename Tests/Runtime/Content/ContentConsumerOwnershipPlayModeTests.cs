using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using CoCoFlow.Runtime.Modules.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.ContentConsumers
{
    public sealed class ContentConsumerOwnershipPlayModeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator UiKeepsPrefabSourceLeaseUntilPanelInstanceIsDestroyed()
        {
            GameObject managerObject = Track(new GameObject("UI Content Consumer Test"));
            managerObject.SetActive(false);
            CoCoContentHost host = managerObject.AddComponent<CoCoContentHost>();
            UIManager manager = managerObject.AddComponent<UIManager>();
            Transform panelRoot = CreateChild(managerObject.transform, "Panel Root");
            SetField(manager, "contentHost", host);
            SetField(manager, "hudRoot", panelRoot);
            SetField(manager, "panelRoot", panelRoot);
            SetField(manager, "popupRoot", panelRoot);

            GameObject prefab = Track(new GameObject(
                "Panel Prefab Source",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(ContentConsumerPanel)));
            ContentConsumerPanel sourcePanel = prefab.GetComponent<ContentConsumerPanel>();
            SetField(sourcePanel, "config", UIPanelConfig.None);
            prefab.SetActive(false);

            ContentId contentId = CreateContentId("tests.ui.panel");
            Assert.That(
                ContentReference.TryCreateDirectPrefabSource(
                    contentId,
                    prefab,
                    out ContentReference panelSource),
                Is.True);

            managerObject.SetActive(true);
            Assert.That(host.IsInitialized, Is.True, host.LastDiagnostic.Message);

            manager.OpenPanel(panelSource);
            yield return null;

            ContentConsumerPanel instance = FindPanelInstance(prefab);
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.SourceContentId, Is.EqualTo(contentId));
            AssertSingleEntry(host, contentId, expectedLeaseCount: 1);

            manager.CloseCurrentPanel();
            yield return null;

            Assert.That(instance == null, Is.True, "UI must destroy the panel instance.");
            Assert.That(prefab != null, Is.True, "Direct release must not destroy the prefab source.");
            Assert.That(host.Runtime.CaptureSnapshot().Entries.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MapDuplicateDemandIsIdempotentAndRequesterReleaseIsIsolated()
        {
            CreateMapFixture(
                out CoCoContentHost host,
                out TestSceneBackend backend,
                out MapResourceManager manager);
            ContentId sceneId = CreateContentId("tests.map.shared-scene");
            ContentReference sceneSource = CreateSceneReference(sceneId);
            ContentOwnerId requesterA = CreateOwnerId("tests.map.requester-a");
            ContentOwnerId requesterB = CreateOwnerId("tests.map.requester-b");

            manager.DemandScene(requesterA, sceneSource);
            manager.DemandScene(requesterA, sceneSource);
            manager.DemandScene(requesterB, sceneSource);
            yield return null;

            Assert.That(backend.LoadCount, Is.EqualTo(1));
            AssertSingleEntry(host, sceneId, expectedLeaseCount: 2);

            manager.ReleaseScene(requesterA, sceneId);
            yield return null;

            Assert.That(backend.ReleaseCount, Is.Zero);
            AssertSingleEntry(host, sceneId, expectedLeaseCount: 1);

            manager.ReleaseScene(requesterB, sceneId);
            yield return null;

            Assert.That(backend.ReleaseCount, Is.EqualTo(1));
            Assert.That(host.Runtime.CaptureSnapshot().Entries.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyingOneMapManagerDoesNotReleaseAnotherManagersLease()
        {
            CreateContentHost(out CoCoContentHost host, out TestSceneBackend backend);
            MapResourceManager managerA = CreateMapManager("Map Manager A", host);
            MapResourceManager managerB = CreateMapManager("Map Manager B", host);
            ContentId sceneId = CreateContentId("tests.map.world-routed-scene");
            ContentReference sceneSource = CreateSceneReference(sceneId);

            managerA.DemandScene(CreateOwnerId("tests.map.world-a"), sceneSource);
            managerB.DemandScene(CreateOwnerId("tests.map.world-b"), sceneSource);
            yield return null;
            AssertSingleEntry(host, sceneId, expectedLeaseCount: 2);

            UnityEngine.Object.DestroyImmediate(managerA.gameObject);
            yield return null;

            Assert.That(backend.ReleaseCount, Is.Zero);
            AssertSingleEntry(host, sceneId, expectedLeaseCount: 1);

            UnityEngine.Object.DestroyImmediate(managerB.gameObject);
            yield return null;

            Assert.That(backend.ReleaseCount, Is.EqualTo(1));
            Assert.That(host.Runtime.CaptureSnapshot().Entries.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyingUiManagerDuringPendingAcquirePublishesNoPanelAndReclaimsLateSource() =>
            UniTask.ToCoroutine(RunDestroyingUiManagerDuringPendingAcquireAsync);

        [UnityTest]
        public IEnumerator MapImmediateRedemandIgnoresStaleCompletionAndKeepsNewLease() =>
            UniTask.ToCoroutine(RunMapImmediateRedemandAsync);

        private async UniTask RunDestroyingUiManagerDuringPendingAcquireAsync()
        {
            CreateDelayedContentHost(
                out CoCoContentHost host,
                out DelayedContentBackend backend);

            GameObject managerObject = Track(new GameObject("Pending UI Manager"));
            managerObject.SetActive(false);
            UIManager manager = managerObject.AddComponent<UIManager>();
            Transform panelRoot = CreateChild(managerObject.transform, "Panel Root");
            SetField(manager, "contentHost", host);
            SetField(manager, "hudRoot", panelRoot);
            SetField(manager, "panelRoot", panelRoot);
            SetField(manager, "popupRoot", panelRoot);

            GameObject prefab = Track(new GameObject(
                "Delayed Panel Prefab Source",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(ContentConsumerPanel)));
            ContentConsumerPanel sourcePanel = prefab.GetComponent<ContentConsumerPanel>();
            SetField(sourcePanel, "config", UIPanelConfig.None);
            prefab.SetActive(false);

            ContentId contentId = CreateContentId("tests.ui.pending-panel");
            Assert.That(
                ContentReference.TryCreateAddressablePrefabSource(
                    contentId,
                    "tests/ui/pending-panel",
                    out ContentReference panelSource),
                Is.True);

            managerObject.SetActive(true);
            manager.OpenPanel(panelSource);
            await WaitUntilAsync(
                () => backend.PendingCount == 1,
                "UI did not start the delayed Prefab Source acquire.");

            UnityEngine.Object.DestroyImmediate(managerObject);
            backend.CompleteNextPrefab(prefab);

            await WaitUntilRuntimeEmptyAsync(host.Runtime);
            Assert.That(FindPanelInstance(prefab), Is.Null);
            Assert.That(backend.LoadCount, Is.EqualTo(1));
            Assert.That(backend.ReleaseCount, Is.EqualTo(1));
            Assert.That(GetTrackedScopeCount(host.Runtime), Is.Zero);
        }

        private async UniTask RunMapImmediateRedemandAsync()
        {
            CreateDelayedContentHost(
                out CoCoContentHost host,
                out DelayedContentBackend backend);
            MapResourceManager manager = CreateMapManager("Redemand Map Manager", host);
            ContentId sceneId = CreateContentId("tests.map.immediate-redemand");
            ContentReference sceneSource = CreateSceneReference(sceneId);
            ContentOwnerId requester = CreateOwnerId("tests.map.redemand-requester");
            int loadedEventCount = 0;
            var eventAgent = new EventAgent();
            eventAgent.Subscribe<MapChunkLoadedEvent>(
                (ref MapChunkLoadedEvent loadedEvent) =>
                {
                    if (loadedEvent.RequesterId == requester &&
                        loadedEvent.SceneId == sceneId)
                    {
                        loadedEventCount++;
                    }
                });

            try
            {
                manager.DemandScene(requester, sceneSource);
                await WaitUntilAsync(
                    () => backend.PendingCount == 1,
                    "Map did not start the first delayed Scene acquire.");

                manager.ReleaseScene(requester, sceneId);
                manager.DemandScene(requester, sceneSource);
                backend.CompleteNextScene();

                await WaitUntilAsync(
                    () => backend.LoadCount == 2 && backend.PendingCount == 1,
                    "The replacement demand did not start after stale cleanup.");
                Assert.That(loadedEventCount, Is.Zero);
                Assert.That(backend.ReleaseCount, Is.EqualTo(1));

                backend.CompleteNextScene();
                await WaitUntilAsync(
                    () => loadedEventCount == 1,
                    "The current Map demand did not publish its loaded event.");
                AssertSingleEntry(host, sceneId, expectedLeaseCount: 1);
                Assert.That(backend.ReleaseCount, Is.EqualTo(1));

                manager.ReleaseScene(requester, sceneId);
                await WaitUntilRuntimeEmptyAsync(host.Runtime);
                Assert.That(backend.ReleaseCount, Is.EqualTo(2));
                Assert.That(loadedEventCount, Is.EqualTo(1));
                Assert.That(GetTrackedScopeCount(host.Runtime), Is.Zero);
            }
            finally
            {
                eventAgent.UnsubscribeAll();
            }
        }

        private void CreateMapFixture(
            out CoCoContentHost host,
            out TestSceneBackend backend,
            out MapResourceManager manager)
        {
            CreateContentHost(out host, out backend);
            manager = CreateMapManager("Map Manager", host);
        }

        private void CreateContentHost(
            out CoCoContentHost host,
            out TestSceneBackend backend)
        {
            GameObject hostObject = Track(new GameObject("Content Host"));
            hostObject.SetActive(false);
            backend = hostObject.AddComponent<TestSceneBackend>();
            host = hostObject.AddComponent<CoCoContentHost>();
            SetField(host, "backendComponents", new MonoBehaviour[] { backend });
            hostObject.SetActive(true);
            Assert.That(host.IsInitialized, Is.True, host.LastDiagnostic.Message);
        }

        private void CreateDelayedContentHost(
            out CoCoContentHost host,
            out DelayedContentBackend backend)
        {
            GameObject hostObject = Track(new GameObject("Delayed Content Host"));
            hostObject.SetActive(false);
            backend = hostObject.AddComponent<DelayedContentBackend>();
            host = hostObject.AddComponent<CoCoContentHost>();
            SetField(host, "backendComponents", new MonoBehaviour[] { backend });
            hostObject.SetActive(true);
            Assert.That(host.IsInitialized, Is.True, host.LastDiagnostic.Message);
        }

        private MapResourceManager CreateMapManager(string name, CoCoContentHost host)
        {
            GameObject managerObject = Track(new GameObject(name));
            MapResourceManager manager = managerObject.AddComponent<MapResourceManager>();
            SetField(manager, "contentHost", host);
            return manager;
        }

        private GameObject Track(GameObject gameObject)
        {
            _objects.Add(gameObject);
            return gameObject;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static ContentConsumerPanel FindPanelInstance(GameObject prefab)
        {
            ContentConsumerPanel[] panels =
                Resources.FindObjectsOfTypeAll<ContentConsumerPanel>();
            foreach (ContentConsumerPanel panel in panels)
            {
                if (panel != null && panel.gameObject != prefab)
                {
                    return panel;
                }
            }

            return null;
        }

        private static ContentReference CreateSceneReference(ContentId sceneId)
        {
            Assert.That(
                ContentReference.TryCreateAddressableAdditiveScene(
                    sceneId,
                    "tests/fake-additive-scene",
                    out ContentReference reference),
                Is.True);
            return reference;
        }

        private static ContentId CreateContentId(string value)
        {
            Assert.That(ContentId.TryCreate(value, out ContentId id), Is.True);
            return id;
        }

        private static ContentOwnerId CreateOwnerId(string value)
        {
            Assert.That(ContentOwnerId.TryCreate(value, out ContentOwnerId id), Is.True);
            return id;
        }

        private static void AssertSingleEntry(
            CoCoContentHost host,
            ContentId contentId,
            int expectedLeaseCount)
        {
            ContentRuntimeSnapshot snapshot = host.Runtime.CaptureSnapshot();
            Assert.That(snapshot.Entries.Count, Is.EqualTo(1));
            Assert.That(snapshot.Entries[0].ContentId, Is.EqualTo(contentId));
            Assert.That(snapshot.Entries[0].LeaseCount, Is.EqualTo(expectedLeaseCount));
        }

        private static async UniTask WaitUntilRuntimeEmptyAsync(ContentRuntime runtime)
        {
            await WaitUntilAsync(
                () => runtime.CaptureSnapshot().Entries.Count == 0,
                "The Content Runtime retained an entry after consumer cleanup.");
        }

        private static int GetTrackedScopeCount(ContentRuntime runtime)
        {
            FieldInfo scopesField = typeof(ContentRuntime).GetField(
                "scopes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(scopesField, Is.Not.Null);
            object scopes = scopesField.GetValue(runtime);
            Assert.That(scopes, Is.Not.Null);
            PropertyInfo countProperty = scopes.GetType().GetProperty("Count");
            Assert.That(countProperty, Is.Not.Null);
            return (int)countProperty.GetValue(scopes);
        }

        private static async UniTask WaitUntilAsync(
            Func<bool> predicate,
            string failureMessage)
        {
            for (int frame = 0; frame < 300; frame++)
            {
                if (predicate()) return;
                await UniTask.Yield();
            }

            Assert.Fail(failureMessage);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            Type currentType = target.GetType();
            FieldInfo field = null;
            while (currentType != null && field == null)
            {
                field = currentType.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                currentType = currentType.BaseType;
            }

            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private sealed class ContentConsumerPanel : UIPanelBase
        {
            public override UniTask ShowAsync()
            {
                gameObject.SetActive(true);
                return UniTask.CompletedTask;
            }

            public override UniTask HideAsync()
            {
                gameObject.SetActive(false);
                return UniTask.CompletedTask;
            }

            protected override void OnDestroy()
            {
                // Intentionally do not call base: source ownership is an independent
                // instance component and must still release deterministically.
            }
        }

        private sealed class TestSceneBackend : MonoBehaviour, IContentBackend
        {
            private static readonly ContentBackendId Id = CreateBackendId();

            public int LoadCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.AdditiveScene;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                _ = request;
                _ = lifetimeCancellationToken;
                LoadCount++;
                return UniTask.FromResult(ContentBackendLoadResult.Success(
                    default(Scene),
                    ReleaseAsync));
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                return UniTask.FromResult(CoCoDiagnostic.None);
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.content-consumer-scene",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class DelayedContentBackend : MonoBehaviour, IContentBackend
        {
            private static readonly ContentBackendId Id = CreateBackendId();
            private readonly Queue<PendingLoad> _pendingLoads =
                new Queue<PendingLoad>();

            public int LoadCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public int PendingCount => _pendingLoads.Count;
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                (reference.Kind == ContentKind.PrefabSource ||
                 reference.Kind == ContentKind.AdditiveScene);

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                _ = lifetimeCancellationToken;
                LoadCount++;
                var completion =
                    new UniTaskCompletionSource<ContentBackendLoadResult>();
                _pendingLoads.Enqueue(new PendingLoad(
                    request.Reference.Kind,
                    completion));
                return completion.Task;
            }

            public void CompleteNextPrefab(GameObject prefab)
            {
                PendingLoad pending = Dequeue(ContentKind.PrefabSource);
                pending.Completion.TrySetResult(
                    ContentBackendLoadResult.Success(prefab, ReleaseAsync));
            }

            public void CompleteNextScene()
            {
                PendingLoad pending = Dequeue(ContentKind.AdditiveScene);
                pending.Completion.TrySetResult(
                    ContentBackendLoadResult.Success(default(Scene), ReleaseAsync));
            }

            private PendingLoad Dequeue(ContentKind expectedKind)
            {
                Assert.That(_pendingLoads.Count, Is.GreaterThan(0));
                PendingLoad pending = _pendingLoads.Dequeue();
                Assert.That(pending.Kind, Is.EqualTo(expectedKind));
                return pending;
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                return UniTask.FromResult(CoCoDiagnostic.None);
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.content-consumer-delayed",
                    out ContentBackendId backendId);
                return backendId;
            }

            private readonly struct PendingLoad
            {
                public PendingLoad(
                    ContentKind kind,
                    UniTaskCompletionSource<ContentBackendLoadResult> completion)
                {
                    Kind = kind;
                    Completion = completion;
                }

                public ContentKind Kind { get; }
                public UniTaskCompletionSource<ContentBackendLoadResult> Completion { get; }
            }
        }
    }
}
