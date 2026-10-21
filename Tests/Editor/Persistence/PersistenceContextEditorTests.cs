using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CoCoFlow.Editor.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Context;
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

        [SetUp]
        public void SetUp()
        {
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
