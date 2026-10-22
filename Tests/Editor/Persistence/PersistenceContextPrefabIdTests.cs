using System;
using System.IO;
using System.Linq;
using CoCoFlow.Editor.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.Persistence
{
    public sealed class PersistenceContextPrefabIdTests
    {
        private string _folder;
        private string[] Folders => new[] { _folder };

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/AgentTests")) AssetDatabase.CreateFolder("Assets", "AgentTests");
            _folder = "Assets/AgentTests/BUG-124-automated-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/AgentTests", Path.GetFileName(_folder));
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(_folder);

        [Test]
        public void ScanCountsAndReportsConflictsWithoutChangingAssets()
        {
            var a = Create("A", "duplicate", true);
            Create("B", "duplicate");
            Create("C", "duplicate");
            Create("Empty", "");
            Create("Valid", "unique");
            var files = Directory.GetFiles(_folder, "*.prefab").ToDictionary(path => path, File.ReadAllBytes);
            var report = PersistenceContextPrefabIds.Scan(Folders);
            Assert.AreEqual(5, report.PrefabCount);
            Assert.AreEqual(1, report.LockedCount);
            Assert.AreEqual(4, report.Entries.Count - report.LockedCount);
            Assert.AreEqual(1, report.EmptyIds.Count());
            Assert.AreEqual(1, report.Duplicates.Count());
            StringAssert.Contains("B.prefab", report.Conflicts(a));
            StringAssert.Contains("C.prefab", report.Conflicts(a));
            Assert.IsFalse(report.IsValid);
            foreach (var entry in report.Entries) Assert.IsFalse(EditorUtility.IsDirty(entry.Context));
            foreach (var file in files) CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key));
        }

        [Test]
        public void LockedAssetRejectsBothRegenerationAndExplicitFill()
        {
            var context = Create("Locked", "", true);
            Assert.Throws<InvalidOperationException>(() => PersistenceContextPrefabIds.Regenerate(context, Folders));
            context.EnsureStableEntityId();
            Assert.AreEqual("", context.StableEntityId);
            Assert.AreEqual("fixture.key", context.PrefabKey);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrefabModeInspectorLocksIdentityButKeepsPrefabKeyEditable(bool locked)
        {
            var asset = Create("Inspector", "inspector.id", locked);
            var stage = PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(asset));
            var editor = UnityEditor.Editor.CreateEditor(stage.prefabContentsRoot.GetComponent<PersistenceContext>());
            try
            {
                var view = editor.CreateInspectorGUI();
                var fields = view.Query<PropertyField>().ToList();
                Assert.AreEqual(!locked, fields.Single(field => field.bindingPath == "stableEntityId").enabledSelf);
                Assert.IsTrue(fields.Single(field => field.bindingPath == "prefabKey").enabledSelf);
                Assert.AreEqual(!locked, view.Query<Button>().ToList().Single(button => button.text == "Regenerate ID").enabledSelf);
            }
            finally { Object.DestroyImmediate(editor); StageUtility.GoToMainStage(); }
        }

        [Test]
        public void RegenerationChangesOnlySelectedIdentityAndPersistsIt()
        {
            var a = Create("A", "duplicate");
            var b = Create("B", "duplicate", true);
            Create("C", "another");
            string id = PersistenceContextPrefabIds.Regenerate(a, Folders);
            Assert.AreNotEqual("duplicate", id);
            Assert.AreNotEqual("another", id);
            Assert.AreEqual("duplicate", b.StableEntityId);
            Assert.AreEqual("fixture.key", a.PrefabKey);
            AssetDatabase.ImportAsset(_folder + "/A.prefab", ImportAssetOptions.ForceUpdate);
            Assert.AreEqual(id, AssetDatabase.LoadAssetAtPath<GameObject>(_folder + "/A.prefab")
                .GetComponent<PersistenceContext>().StableEntityId);
            Assert.IsTrue(PersistenceContextPrefabIds.Scan(Folders).IsValid);
        }

        [Test]
        public void VariantStoresItsOwnIdAfterSaveAndBaseChanges()
        {
            var original = Create("Base", "base.id");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(original.gameObject);
            var variant = PrefabUtility.SaveAsPrefabAsset(instance, _folder + "/Variant.prefab");
            Object.DestroyImmediate(instance);
            var context = variant.GetComponent<PersistenceContext>();
            Assert.IsTrue(PrefabUtility.IsPartOfVariantPrefab(context));
            Set(context, "base.id", true);
            Assert.Throws<InvalidOperationException>(() => PersistenceContextPrefabIds.Regenerate(original, Folders));
            Assert.AreEqual("base.id", original.StableEntityId);
            Assert.AreEqual("base.id", context.StableEntityId);
            Set(context, "base.id");
            string id = PersistenceContextPrefabIds.Regenerate(context, Folders);
            Assert.AreEqual("base.id", original.StableEntityId);
            Assert.IsTrue(new SerializedObject(context).FindProperty("stableEntityId").prefabOverride);
            Set(original, "changed.base");
            PrefabUtility.SavePrefabAsset(original.gameObject);
            AssetDatabase.ImportAsset(_folder + "/Variant.prefab", ImportAssetOptions.ForceUpdate);
            Assert.AreEqual(id, AssetDatabase.LoadAssetAtPath<GameObject>(_folder + "/Variant.prefab")
                .GetComponent<PersistenceContext>().StableEntityId);
        }

        [Test]
        public void NestedReferencesDoNotCreateAdditionalAssetIdentities()
        {
            var child = Create("Child", "child.id");
            var wrapper = new GameObject("Wrapper");
            var nested = (GameObject)PrefabUtility.InstantiatePrefab(child.gameObject);
            nested.transform.SetParent(wrapper.transform);
            PrefabUtility.SaveAsPrefabAsset(wrapper, _folder + "/Wrapper.prefab");
            Object.DestroyImmediate(wrapper);
            var report = PersistenceContextPrefabIds.Scan(Folders);
            Assert.AreEqual(1, report.PrefabCount);
            Assert.AreEqual(1, report.Entries.Count);
            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void NestedPrefabSerializationAndEnableNeverAssignIds()
        {
            var child = Create("Visual", "visual.id");
            var parent = Create("Player", "");
            var contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(parent));
            PrefabUtility.InstantiatePrefab(child.gameObject, contents.transform);
            PrefabUtility.SaveAsPrefabAsset(contents, _folder + "/Player.prefab");
            PrefabUtility.UnloadPrefabContents(contents);
            for (int i = 0; i < 20; i++)
            {
                contents = PrefabUtility.LoadPrefabContents(_folder + "/Player.prefab");
                try
                {
                    var context = contents.GetComponent<PersistenceContext>();
                    context.enabled = false;
                    context.enabled = true;
                    context.OnBeforeSerialize();
                    Assert.AreEqual("", context.StableEntityId);
                    Assert.AreEqual("visual.id", contents.GetComponentsInChildren<PersistenceContext>(true)[1].StableEntityId);
                    PrefabUtility.SaveAsPrefabAsset(contents, _folder + "/Player.prefab");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                AssetDatabase.ImportAsset(_folder + "/Player.prefab", ImportAssetOptions.ForceUpdate);
            }
        }

        private PersistenceContext Create(string name, string id, bool locked = false)
        {
            var root = new GameObject(name);
            var context = root.AddComponent<PersistenceContext>();
            Assert.AreEqual("", context.StableEntityId);
            Set(context, id, locked);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, _folder + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<PersistenceContext>();
        }

        private static void Set(PersistenceContext context, string id, bool locked = false)
        {
            var data = new SerializedObject(context);
            data.FindProperty("stableEntityId").stringValue = id;
            data.FindProperty("idLocked").boolValue = locked;
            data.FindProperty("prefabKey").stringValue = "fixture.key";
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
