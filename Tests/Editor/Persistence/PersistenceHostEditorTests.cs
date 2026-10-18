using System;
using System.IO;
using CoCoFlow.Editor.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.Persistence
{
    public sealed class PersistenceHostEditorTests
    {
        [Test]
        public void BothHostsHaveTheirOwnInspector()
        {
            var root = new GameObject("Host inspectors");
            root.SetActive(false);
            var host = root.AddComponent<PersistenceHost>();
            var containers = root.AddComponent<PersistenceContainerHost>();
            UnityEditor.Editor first = null, second = null;
            try
            {
                first = UnityEditor.Editor.CreateEditor(host);
                second = UnityEditor.Editor.CreateEditor(containers);
                Assert.IsInstanceOf<PersistenceHostEditor>(first);
                Assert.IsInstanceOf<PersistenceContainerHostEditor>(second);
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PathPreviewResolvesDefaultRelativeAndAbsolutePathsWithoutCreatingFolders(int mode)
        {
            var root = new GameObject("Path preview");
            root.SetActive(false);
            var host = root.AddComponent<PersistenceHost>();
            string name = "p01-preview-" + Guid.NewGuid().ToString("N");
            string configured = mode == 0 ? string.Empty : mode == 1 ? name : Path.Combine(Application.temporaryCachePath, name);
            string expected = mode == 0 ? Application.persistentDataPath : mode == 1 ? Path.Combine(Application.persistentDataPath, name) : configured;
            try
            {
                var data = new SerializedObject(host);
                data.FindProperty("saveDirectory").stringValue = configured;
                data.FindProperty("initialSlotIndex").intValue = 2;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(PersistenceHostEditor.TryGetPaths(host, out string directory, out string file, out string error), error);
                Assert.AreEqual(Path.GetFullPath(expected), directory);
                Assert.AreEqual(Path.Combine(Path.GetFullPath(expected), "savegame_slot_2.json"), file);
                if (mode != 0) Assert.IsFalse(Directory.Exists(directory));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DebugPreviewUsesProjectAssetsAndRetainsNormalDirectoryWithoutWriting()
        {
            var root = new GameObject("Debug path preview");
            root.SetActive(false);
            var host = root.AddComponent<PersistenceHost>();
            string debugDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "DebugSaves"));
            bool existed = Directory.Exists(debugDirectory);
            try
            {
                var data = new SerializedObject(host);
                data.FindProperty("saveDirectory").stringValue = "NormalSaves";
                data.FindProperty("debugMode").boolValue = true;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(PersistenceHostEditor.TryGetPaths(host, out string directory, out string file, out string error), error);
                Assert.AreEqual(debugDirectory, directory);
                Assert.AreEqual(Path.Combine(debugDirectory, "savegame_slot_0.json"), file);
                Assert.AreEqual(existed, Directory.Exists(debugDirectory), "Preview must not create the debug directory.");
                data.FindProperty("debugMode").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(PersistenceHostEditor.TryGetPaths(host, out directory, out file, out error), error);
                Assert.AreEqual(Path.GetFullPath(Path.Combine(Application.persistentDataPath, "NormalSaves")), directory);
                Assert.AreEqual("NormalSaves", data.FindProperty("saveDirectory").stringValue);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ConfigurationAndDraggedReferenceSurvivePrefabSaveAndOverrides()
        {
            const string folder = "Assets/AgentTests/044-p01-persistence/EditorFixtures";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            string path = folder + "/PersistenceHosts.prefab";
            var root = new GameObject("Configured hosts");
            root.SetActive(false);
            var containers = root.AddComponent<PersistenceContainerHost>();
            var host = root.AddComponent<PersistenceHost>();
            GameObject instance = null;
            try
            {
                var data = new SerializedObject(host);
                data.FindProperty("containerHost").objectReferenceValue = containers;
                data.FindProperty("maxSaveSlots").intValue = 6;
                data.FindProperty("initialSlotIndex").intValue = 4;
                data.FindProperty("saveDirectory").stringValue = "MyGame/Saves";
                data.FindProperty("debugMode").boolValue = true;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var authored = new SerializedObject(prefab.GetComponent<PersistenceHost>());
                Assert.AreEqual(6, authored.FindProperty("maxSaveSlots").intValue);
                Assert.AreEqual(4, authored.FindProperty("initialSlotIndex").intValue);
                Assert.AreEqual("MyGame/Saves", authored.FindProperty("saveDirectory").stringValue);
                Assert.IsTrue(authored.FindProperty("debugMode").boolValue);
                Assert.AreSame(prefab.GetComponent<PersistenceContainerHost>(), authored.FindProperty("containerHost").objectReferenceValue);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var changed = new SerializedObject(instance.GetComponent<PersistenceHost>());
                Undo.IncrementCurrentGroup();
                changed.FindProperty("maxSaveSlots").intValue = 8;
                changed.FindProperty("debugMode").boolValue = false;
                changed.ApplyModifiedProperties();
                Assert.IsTrue(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                changed.Update();
                Assert.AreEqual(6, changed.FindProperty("maxSaveSlots").intValue);
                Assert.IsTrue(changed.FindProperty("debugMode").boolValue);
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
