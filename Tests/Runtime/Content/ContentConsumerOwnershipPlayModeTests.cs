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
    }
}
