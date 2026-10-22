using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CoCoFlow.Editor.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.Persistence
{
    public sealed class PersistenceContextEditorTests
    {
        private Scene _scene;
        private SceneSetup[] _previousSetup;
        private string _directory;
        private IPersistenceContextAdapter[] _previousAdapters;

        [SetUp]
        public void SetUp()
        {
            _previousAdapters = GetAdapters().ToArray();
            PersistenceSession.ClearPendingDocument();
            _previousSetup = EditorSceneManager.GetSceneManagerSetup();
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(_scene);
            _directory = "Assets/CoCoFlowBug124Tests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_directory));
            PersistenceContextRegistry.Clear();
            PersistenceContextStableIdEditor.Synchronize();
        }

        [TearDown]
        public void TearDown()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (_previousSetup != null && _previousSetup.Any(setup => setup.isLoaded && setup.isActive) &&
                _previousSetup.All(setup => !string.IsNullOrEmpty(setup.path)))
                EditorSceneManager.RestoreSceneManagerSetup(_previousSetup);
            if (!string.IsNullOrEmpty(_directory)) AssetDatabase.DeleteAsset(_directory);
            PersistenceContextStableIdEditor.Synchronize();
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
            GetAdapters().Clear();
            GetAdapters().AddRange(_previousAdapters);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SerializationAndValidationCallbacksAreSafeOnWorkerThread(bool destroyed)
        {
            PersistenceContext context = CreateContext("callback");
            if (destroyed) Object.DestroyImmediate(context.gameObject);
            MethodInfo validate = typeof(PersistenceContext).GetMethod("OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(validate);
            Task work = Task.Run(() =>
            {
                context.OnBeforeSerialize();
                context.OnAfterDeserialize();
                validate.Invoke(context, null);
            });
            Assert.IsTrue(work.Wait(TimeSpan.FromSeconds(5)), "Callback must not wait for the Editor thread.");
            Assert.IsNull(work.Exception);
        }

        [Test]
        public void RepeatedMaintenanceAndSerializationPreserveExistingIdAndCleanScene()
        {
            PersistenceContext context = CreateContext("existing");
            SetId(context, "scene.existing");
            SaveScene();
            Assert.IsFalse(_scene.isDirty);
            for (int index = 0; index < 20; index++)
            {
                context.OnBeforeSerialize();
                PersistenceContextStableIdEditor.Synchronize();
            }
            Assert.AreEqual("scene.existing", context.StableEntityId);
            Assert.IsFalse(_scene.isDirty, "An unchanged ID must not dirty the scene.");
            ReopenScene();
            Assert.AreEqual("scene.existing", FindContext("existing").StableEntityId);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FirstSaveWritesIdWithoutWaitingForEditorUpdate(bool inactive)
        {
            PersistenceContext context = CreateContext("first-save", inactive);
            Assert.IsEmpty(_scene.path);
            SaveScene();
            string id = context.StableEntityId;
            Assert.IsNotEmpty(id);
            ReopenScene();
            Assert.AreEqual(id, FindContext("first-save").StableEntityId);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopySavedImmediatelyGetsUniqueIdAndKeepsSourceId(bool inactive)
        {
            PersistenceContext source = CreateContext("source", inactive);
            SaveScene();
            string originalId = source.StableEntityId;
            GameObject copy = Object.Instantiate(source.gameObject);
            copy.name = "copy";
            copy.transform.SetAsFirstSibling();
            SaveScene();
            string copyId = copy.GetComponent<PersistenceContext>().StableEntityId;
            Assert.AreEqual(originalId, source.StableEntityId, "Keep the known owner even if the copy is visited first.");
            Assert.AreNotEqual(originalId, copyId);
            ReopenScene();
            Assert.AreEqual(originalId, FindContext("source").StableEntityId);
            Assert.AreEqual(copyId, FindContext("copy").StableEntityId);
        }

        [TestCase("component")]
        [TestCase("object")]
        [TestCase("clear")]
        public void MaintenanceRestoresActualRegistrationAfterRemoval(string removal)
        {
            PersistenceContext context = CreateContext("registration");
            SaveScene();
            string id = context.StableEntityId;
            var registry = (System.Collections.IDictionary)typeof(PersistenceContextRegistry)
                .GetField("Contexts", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.AreSame(context, registry[id]);
            if (removal == "component")
            {
                context.enabled = false;
                context.enabled = true;
            }
            else if (removal == "object")
            {
                context.gameObject.SetActive(false);
                context.gameObject.SetActive(true);
            }
            else PersistenceContextRegistry.Clear();
            Assert.IsFalse(registry.Contains(id));
            SaveScene();
            Assert.AreSame(context, registry[id], "Saving must restore the actual Registry entry.");
            Assert.AreEqual(id, context.StableEntityId);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImmediateCaptureAfterReenableIncludesActualRecord(bool toggleObject)
        {
            PersistenceContext context = CreateContext("capture");
            SaveScene();
            string id = context.StableEntityId;
            SetCaptureData(context.gameObject, 42);
            if (toggleObject)
            {
                context.gameObject.SetActive(false);
                context.gameObject.SetActive(true);
            }
            else
            {
                context.enabled = false;
                context.enabled = true;
            }
            // No scene save, manual registration/sync, or Editor update after enable.
            PersistenceSaveDocument document = PersistenceSession.Capture(0);
            Assert.IsTrue(document.contextSection.TryGetRecord(id, out var record));
            Assert.AreEqual(42, record.semanticStateId);
            Assert.AreEqual(id, context.StableEntityId);
        }

        [Test]
        public void ImmediateCaptureOfNewContextIncludesGeneratedIdAndData()
        {
            PersistenceContext context = CreateContext("new-capture");
            SetCaptureData(context.gameObject, 42);
            PersistenceSaveDocument document = PersistenceSession.Capture(0);
            Assert.IsNotEmpty(context.StableEntityId);
            Assert.IsTrue(document.contextSection.TryGetRecord(context.StableEntityId, out var record));
            Assert.AreEqual(42, record.semanticStateId);
        }

        [Test]
        public void ImmediateCaptureOfCopyKeepsBothEntitiesAndTheirData()
        {
            PersistenceContext source = CreateContext("capture-source");
            SaveScene();
            string originalId = source.StableEntityId;
            SetCaptureData(source.gameObject, 42);
            GameObject copy = Object.Instantiate(source.gameObject);
            copy.transform.SetAsFirstSibling();
            SetCaptureData(copy, 99);
            PersistenceSaveDocument document = PersistenceSession.Capture(0);
            string copyId = copy.GetComponent<PersistenceContext>().StableEntityId;
            Assert.AreEqual(originalId, source.StableEntityId);
            Assert.AreNotEqual(originalId, copyId);
            Assert.AreEqual(2, document.contextSection.records.Count);
            Assert.IsTrue(document.contextSection.TryGetRecord(originalId, out var original));
            Assert.IsTrue(document.contextSection.TryGetRecord(copyId, out var duplicate));
            Assert.AreEqual(42, original.semanticStateId);
            Assert.AreEqual(99, duplicate.semanticStateId);
        }

        [Test]
        public void CaptureMaintenanceDoesNotReplayPendingDataForRegisteredContext()
        {
            PersistenceContext context = CreateContext("pending-capture");
            SaveScene();
            SetCaptureData(context.gameObject, 42);
            PersistenceSession.SetPendingDocument(PersistenceSession.Capture(0));
            context.enabled = false;
            context.enabled = true;
            Assert.IsTrue(PersistenceSession.Capture(0).contextSection
                .TryGetRecord(context.StableEntityId, out _));
            SetCaptureData(context.gameObject, 99);
            for (int index = 0; index < 2; index++)
            {
                Assert.IsTrue(PersistenceSession.Capture(0).contextSection
                    .TryGetRecord(context.StableEntityId, out var record));
                Assert.AreEqual(99, record.semanticStateId, "Capture must not reapply the pending value 42.");
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ColdOwnershipCopySavedFirstKeepsDeserializedSourceId(bool inactive, bool prefab)
        {
            if (prefab)
            {
                string path = CreatePrefab("cold-template", "cold.template");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(path), _scene);
                instance.name = "source";
                instance.SetActive(!inactive);
            }
            else CreateContext("source", inactive);
            SaveScene();
            ReopenScene();
            // Domain reload loses Editor static ownership. Load the source through
            // Unity deserialization, then copy/save in the same tick, without update.
            ((System.Collections.IDictionary)typeof(PersistenceContextStableIdEditor)
                .GetField("_owners", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
            PersistenceContext source = FindContext("source");
            string originalId = source.StableEntityId;
            GameObject copy = Object.Instantiate(source.gameObject);
            copy.name = "copy";
            copy.transform.SetAsFirstSibling();
            SaveScene();
            string copyId = copy.GetComponent<PersistenceContext>().StableEntityId;
            Assert.AreEqual(originalId, source.StableEntityId, "A cold ownership table must preserve the source ID.");
            Assert.AreNotEqual(originalId, copyId);
            ReopenScene();
            Assert.AreEqual(originalId, FindContext("source").StableEntityId);
            Assert.AreEqual(copyId, FindContext("copy").StableEntityId);
        }

        [Test]
        public void PrefabInstancesKeepUniqueOverridesAfterReloadWithoutChangingAsset()
        {
            string prefabPath = CreatePrefab("template", "prefab.template");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var first = (GameObject)PrefabUtility.InstantiatePrefab(asset, _scene);
            first.name = "first-instance";
            PersistenceContextStableIdEditor.Synchronize();
            string firstId = first.GetComponent<PersistenceContext>().StableEntityId;
            var second = (GameObject)PrefabUtility.InstantiatePrefab(asset, _scene);
            second.name = "second-instance";
            SaveScene();
            string secondId = second.GetComponent<PersistenceContext>().StableEntityId;
            Assert.AreNotEqual(firstId, secondId);
            Assert.IsTrue(PrefabUtility.GetPropertyModifications(second).Any(modification =>
                modification.propertyPath == "stableEntityId" && modification.value == secondId));
            Assert.AreEqual("prefab.template", asset.GetComponent<PersistenceContext>().StableEntityId);
            ReopenScene();
            Assert.AreEqual(firstId, FindContext("first-instance").StableEntityId);
            Assert.AreEqual(secondId, FindContext("second-instance").StableEntityId);
            Assert.AreEqual("prefab.template", asset.GetComponent<PersistenceContext>().StableEntityId);
        }

        [Test]
        public void PrefabAssetAndLoadedPreviewDoNotReceiveSceneIds()
        {
            string path = CreatePrefab("preview", string.Empty);
            GameObject preview = PrefabUtility.LoadPrefabContents(path);
            try
            {
                PersistenceContextStableIdEditor.Synchronize();
                Assert.IsEmpty(preview.GetComponent<PersistenceContext>().StableEntityId);
                Assert.IsEmpty(AssetDatabase.LoadAssetAtPath<GameObject>(path)
                    .GetComponent<PersistenceContext>().StableEntityId);
                Assert.IsFalse(preview.scene.isDirty);
            }
            finally { PrefabUtility.UnloadPrefabContents(preview); }
        }

        [Test]
        public void PrefabStageIsExcludedWhileOrdinarySceneStillReceivesIds()
        {
            string path = CreatePrefab("stage", string.Empty);
            PersistenceContext ordinary = CreateContext("ordinary");
            PrefabStage stage = PrefabStageUtility.OpenPrefab(path);
            try
            {
                PersistenceContextStableIdEditor.Synchronize();
                Assert.IsEmpty(stage.prefabContentsRoot.GetComponent<PersistenceContext>().StableEntityId);
                Assert.IsNotEmpty(ordinary.StableEntityId);
                Assert.IsFalse(stage.scene.isDirty);
            }
            finally { StageUtility.GoToMainStage(); }
        }

        [Test]
        public void NestedPrefabSaveUnloadAndImportTwentyRoundsPreserveIdsWithoutErrors()
        {
            string innerPath = CreatePrefab("visual", "visual.template");
            PersistenceContext outer = CreateContext("player");
            SetId(outer, "player.template");
            var nested = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(innerPath), _scene);
            nested.transform.SetParent(outer.transform, false);
            string path = _directory + "/Player.prefab";
            PrefabUtility.SaveAsPrefabAsset(outer.gameObject, path);
            Object.DestroyImmediate(outer.gameObject);
            for (int round = 0; round < 20; round++)
            {
                GameObject preview = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    preview.transform.GetChild(0).localPosition = Vector3.right * round;
                    PersistenceContextStableIdEditor.Synchronize();
                    Assert.AreEqual("player.template", preview.GetComponent<PersistenceContext>().StableEntityId);
                    Assert.AreEqual("visual.template", preview.transform.GetChild(0)
                        .GetComponent<PersistenceContext>().StableEntityId);
                    Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(preview, path));
                }
                finally { PrefabUtility.UnloadPrefabContents(preview); }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.AreEqual("player.template", saved.GetComponent<PersistenceContext>().StableEntityId);
                Assert.AreEqual("visual.template", saved.transform.GetChild(0)
                    .GetComponent<PersistenceContext>().StableEntityId);
                LogAssert.NoUnexpectedReceived();
            }
            TestContext.WriteLine("Nested Prefab save/unload/import: 20 completed rounds.");
        }

        private PersistenceContext CreateContext(string name, bool inactive = false)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, _scene);
            root.SetActive(!inactive);
            return root.AddComponent<PersistenceContext>();
        }

        private static List<IPersistenceContextAdapter> GetAdapters()
        {
            return (List<IPersistenceContextAdapter>)typeof(PersistenceContextAdapterRegistry)
                .GetField("Adapters", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        }

        private static void SetCaptureData(GameObject root, int value)
        {
            // Reuse the legacy provider/adapter fixture without changing assembly
            // dependencies or introducing a second test implementation of that contract.
            Type fixture = Type.GetType("CoCoFlow.Tests.Runtime.ContextLifecycle.PersistenceModuleTests, " +
                                        "CoCoFlow.Tests.Runtime.ContextLifecycle", true);
            Type providerType = fixture.GetNestedType("TestContextProviderBehaviour", BindingFlags.NonPublic);
            Component provider = root.GetComponent(providerType) ?? root.AddComponent(providerType);
            object context = providerType.GetProperty("Context").GetValue(provider);
            context.GetType().GetProperty("SemanticStateId").SetValue(context, value);
            Type adapterType = fixture.GetNestedType("TestContextAdapter", BindingFlags.NonPublic);
            PersistenceContextAdapterRegistry.Register((IPersistenceContextAdapter)
                adapterType.GetField("Instance").GetValue(null));
        }

        private static void SetId(PersistenceContext context, string id)
        {
            var serialized = new SerializedObject(context);
            serialized.FindProperty("stableEntityId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private string CreatePrefab(string name, string id)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                SetId(root.AddComponent<PersistenceContext>(), id);
                string path = _directory + "/" + name + ".prefab";
                Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(root, path));
                return path;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private void SaveScene()
        {
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, _directory + "/Identity.unity"));
        }

        private void ReopenScene()
        {
            string path = _scene.path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SceneManager.SetActiveScene(_scene);
        }

        private PersistenceContext FindContext(string name)
        {
            return _scene.GetRootGameObjects().Single(root => root.name == name)
                .GetComponent<PersistenceContext>();
        }
    }
}
