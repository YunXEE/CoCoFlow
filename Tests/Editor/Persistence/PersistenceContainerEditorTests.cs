using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CoCoFlow.Editor.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Tests.Runtime.Persistence;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.Persistence
{
    [Serializable, PersistenceData]
    public struct ContainerEditorOnlyData
    {
        // Public JSON data-field naming, not a Unity component field.
        // ReSharper disable once InconsistentNaming
        public int Value;
    }

    public sealed class PersistenceContainerEditorTests
    {
        private const string DirectoryPath = "Assets/AgentTests/044-p01-persistence/EditorFixtures";

        [Test]
        public void TypePickerContainsLegalRootsAndExcludesInvalidShapes()
        {
            Type[] types = PersistenceDataTypeRegistry.GetValidTypes();
            CollectionAssert.Contains(types, typeof(ContainerTestChest));
            CollectionAssert.DoesNotContain(types, typeof(ContainerBadNested));
            CollectionAssert.DoesNotContain(types, typeof(ContainerMissingSerializable));
            CollectionAssert.DoesNotContain(types, typeof(ContainerBadObject));
            Assert.AreEqual(typeof(ContainerTestChest), PersistenceDataTypeRegistry.Resolve(PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest))));
        }

        [Test]
        public void TypePickerDisplaysShortNamesAndOnlyReportsActuallyMissingTypes()
        {
            var entries = PersistenceContainerTypePicker.CreateEntries(new[] { typeof(ContainerTestChest), typeof(ContainerTestQuest) });
            string id = PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest));
            GUIContent content = PersistenceContainerTypePicker.GetButtonContent(entries, id);
            Assert.AreEqual("ContainerTestChest", content.text);
            StringAssert.Contains(typeof(ContainerTestChest).FullName, content.tooltip);
            Assert.IsFalse(entries.Any(entry => entry.Label.Contains("Missing")));
            Assert.AreEqual("Select a data struct", PersistenceContainerTypePicker.GetButtonContent(entries, string.Empty).text);
            Assert.AreEqual("Missing data struct", PersistenceContainerTypePicker.GetButtonContent(entries, "Removed:Game.OldData").text);
        }

        [Test]
        public void TypePickerSearchesTypeNamespaceAndAssemblyIgnoringCase()
        {
            var entries = PersistenceContainerTypePicker.CreateEntries(new[] { typeof(ContainerTestChest), typeof(ContainerEditorOnlyData) });
            string chestId = PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest));
            Assert.AreEqual(chestId, PersistenceContainerTypePicker.Filter(entries, "TESTCHEST").Single().Id);
            Assert.AreEqual(chestId, PersistenceContainerTypePicker.Filter(entries, "runtime.persistence").Single().Id);
            Assert.AreEqual(chestId, PersistenceContainerTypePicker.Filter(entries, typeof(ContainerTestChest).Assembly.GetName().Name).Single().Id);
            Assert.AreEqual(chestId, PersistenceContainerTypePicker.Filter(entries, "runtime CHEST").Single().Id);
            Assert.IsEmpty(PersistenceContainerTypePicker.Filter(entries, "no-matching-business-type"));
            Assert.AreEqual(entries.Length, PersistenceContainerTypePicker.Filter(entries, "   ").Length);
        }

        [Test]
        public void DuplicateTypeNamesUseOnlyTheNamespaceSuffixNeededToDistinguishThem()
        {
            var entries = PersistenceContainerTypePicker.CreateEntries(new[] { typeof(Picker.Left.SharedData), typeof(Picker.Right.SharedData) });
            CollectionAssert.AreEquivalent(new[] { "None", "SharedData (Left)", "SharedData (Right)" }, entries.Select(entry => entry.Label));
            Assert.AreEqual(1, PersistenceContainerTypePicker.Filter(entries, "picker.left").Length);
        }

        [Test]
        public void SelectingTheSameTypePreservesInitialDataAndChangingTypeCanBeUndone()
        {
            var root = new GameObject("Type selection undo");
            root.SetActive(false);
            var container = root.AddComponent<PersistenceContainer>();
            try
            {
                var data = new SerializedObject(container);
                string originalId = PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest));
                data.FindProperty("dataTypeId").stringValue = originalId;
                data.FindProperty("initialDataJson").stringValue = "{\"Coins\":23}";
                data.ApplyModifiedPropertiesWithoutUndo();
                PersistenceContainerEditor.ApplyTypeSelection(data, originalId);
                Assert.AreEqual("{\"Coins\":23}", data.FindProperty("initialDataJson").stringValue);
                Undo.IncrementCurrentGroup();
                PersistenceContainerEditor.ApplyTypeSelection(data, PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestQuest)));
                Assert.AreEqual(typeof(ContainerTestQuest), container.DataType);
                Assert.AreEqual("{}", data.FindProperty("initialDataJson").stringValue);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                data.Update();
                Assert.AreEqual(originalId, data.FindProperty("dataTypeId").stringValue);
                Assert.AreEqual("{\"Coins\":23}", data.FindProperty("initialDataJson").stringValue);
                PersistenceContainerEditor.ApplyTypeSelection(data, string.Empty);
                Assert.IsNull(container.DataType);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ConfigurationUsesSerializedPropertiesAndSupportsUndo()
        {
            var root = new GameObject("Inspector undo");
            root.SetActive(false);
            var container = root.AddComponent<PersistenceContainer>();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(container);
                Assert.IsInstanceOf<PersistenceContainerEditor>(inspector);
                Undo.IncrementCurrentGroup();
                var serialized = new SerializedObject(container);
                serialized.FindProperty("id").stringValue = "changed";
                serialized.ApplyModifiedProperties();
                Assert.AreEqual("changed", container.Id);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(string.Empty, container.Id);
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TypedInitialJsonSurvivesPrefabReloadAndInstanceOverrides()
        {
            Directory.CreateDirectory(DirectoryPath);
            AssetDatabase.Refresh();
            string path = DirectoryPath + "/Container.prefab";
            var root = new GameObject("Authored container");
            root.SetActive(false);
            var container = root.AddComponent<PersistenceContainer>();
            GameObject instance = null;
            try
            {
                var serialized = new SerializedObject(container);
                serialized.FindProperty("id").stringValue = "shared";
                serialized.FindProperty("dataTypeId").stringValue = PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest));
                serialized.FindProperty("initialDataJson").stringValue = "{\"Coins\":7,\"Items\":[{\"Key\":\"item\",\"Count\":2}]}";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var authored = prefab.GetComponent<PersistenceContainer>();
                Assert.AreEqual(typeof(ContainerTestChest), authored.DataType);
                var saved = new SerializedObject(authored);
                StringAssert.Contains("\"Count\":2", saved.FindProperty("initialDataJson").stringValue);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var overridden = new SerializedObject(instance.GetComponent<PersistenceContainer>());
                overridden.FindProperty("id").stringValue = "instance";
                overridden.ApplyModifiedProperties();
                Assert.IsTrue(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
                Assert.AreEqual("shared", authored.Id);
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void PlayerMetadataIncludesNestedStructsAndListsAndExcludesEditorAssemblies()
        {
            var assemblies = new HashSet<string> { typeof(ContainerTestChest).Assembly.GetName().Name };
            Type[] types = PersistenceContainerPlayerMetadata.CollectTypes(
                new[] { typeof(ContainerTestChest), typeof(ContainerEditorOnlyData) }, assemblies);
            CollectionAssert.Contains(types, typeof(ContainerTestChest));
            CollectionAssert.Contains(types, typeof(ContainerTestItem));
            CollectionAssert.Contains(types, typeof(List<ContainerTestItem>));
            CollectionAssert.DoesNotContain(types, typeof(ContainerEditorOnlyData));
            XDocument xml = XDocument.Parse(PersistenceContainerPlayerMetadata.CreateLinkXml(types));
            XElement root = xml.Descendants("type").Single(type => (string)type.Attribute("fullname") == typeof(ContainerTestChest).FullName);
            Assert.AreEqual("fields", (string)root.Attribute("preserve"));
            Assert.IsTrue(xml.Descendants("type").Any(type => (string)type.Attribute("fullname") == "System.Collections.Generic.List`1"));
        }
    }
}

namespace CoCoFlow.Tests.Editor.Persistence.Picker.Left
{
    [Serializable, PersistenceData]
    public struct SharedData { }
}

namespace CoCoFlow.Tests.Editor.Persistence.Picker.Right
{
    [Serializable, PersistenceData]
    public struct SharedData { }
}
