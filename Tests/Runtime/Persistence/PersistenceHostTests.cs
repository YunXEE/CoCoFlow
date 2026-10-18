using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.Persistence
{
    public sealed class PersistenceHostTests : PersistenceContainerTestBase
    {
        private string _directory;

        [SetUp]
        public void SetUpDirectory() => _directory = Path.Combine(Application.temporaryCachePath, "p01-host-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void InspectorConfigurationAppliesToComponentAndStaticSaveEntries()
        {
            PersistenceHost entry = CreateEntry();
            Assert.AreSame(entry, PersistenceHost.ActiveHost);
            Assert.AreSame(Host, entry.ResolveContainerHost());
            Assert.AreEqual(5, PersistenceSaveLoadSystem.MaxSaveSlots);
            Assert.AreEqual(4, PersistenceSaveLoadSystem.CurrentSlotIndex);
            Assert.AreEqual(Path.GetFullPath(_directory), PersistenceFileStore.ResolveSaveDirectory());
            Assert.IsFalse(Directory.Exists(_directory), "Configuration must not create folders before an explicit IO action.");
            var data = Host.GetOrCreate("saved", new ContainerTestChest { Coins = 7 });
            entry.SaveGame();
            Assert.IsTrue(File.Exists(Path.Combine(_directory, "savegame_slot_4.json")));
            data.Value.Coins = 11;
            Assert.IsTrue(PersistenceSaveLoadSystem.LoadGame());
            Assert.AreEqual(7, data.Value.Coins);
            PersistenceSaveLoadSystem.CurrentSlotIndex = 2;
            PersistenceSaveLoadSystem.SaveGame();
            data.Value.Coins = 99;
            Assert.IsTrue(entry.LoadGame());
            Assert.AreEqual(7, data.Value.Coins);
            Assert.IsTrue(File.Exists(Path.Combine(_directory, "savegame_slot_2.json")));
        }

        [Test]
        public void ConfiguredSlotCountIsEnforcedByBothEntries()
        {
            PersistenceHost entry = CreateEntry();
            LogAssert.Expect(LogType.Warning, new Regex("槽位 5 超出上限 5"));
            entry.SaveGame(5);
            LogAssert.Expect(LogType.Warning, new Regex("槽位 5 超出上限 5"));
            Assert.IsFalse(PersistenceSaveLoadSystem.LoadGame(5));
            Assert.IsFalse(Directory.Exists(_directory));
        }

#if UNITY_EDITOR
        [Test]
        public void DebugModeIsolatesSavesAndLoadsAcrossComponentAndStaticEntries()
        {
            string debugDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "DebugSaves"));
            bool directoryExisted = Directory.Exists(debugDirectory);
            bool metaExisted = File.Exists(debugDirectory + ".meta");
            int slot = 100000 + (Guid.NewGuid().GetHashCode() & 0xffff);
            string debugFile = Path.Combine(debugDirectory, "savegame_slot_" + slot + ".json");
            while (File.Exists(debugFile) || File.Exists(debugFile + ".meta"))
                debugFile = Path.Combine(debugDirectory, "savegame_slot_" + ++slot + ".json");
            string assetPath = "Assets/DebugSaves/" + Path.GetFileName(debugFile);
            PersistenceHost entry = CreateEntry();
            try
            {
                var data = Host.GetOrCreate("debug-saved", new ContainerTestChest { Coins = 7 });
                entry.SaveGame();
                string normalFile = Path.Combine(_directory, "savegame_slot_4.json");
                string normalJson = File.ReadAllText(normalFile);
                entry.enabled = false;
                SetField(entry, "debugMode", true);
                SetField(entry, "maxSaveSlots", slot + 1);
                SetField(entry, "initialSlotIndex", slot);
                entry.enabled = true;
                Assert.AreEqual(debugDirectory, PersistenceFileStore.ResolveSaveDirectory());
                Assert.AreEqual(directoryExisted, Directory.Exists(debugDirectory), "Enabling Debug Mode must not create its directory.");
                Assert.IsFalse(File.Exists(debugFile), "Enabling Debug Mode must not automatically save.");
                data.Value.Coins = 11;
                entry.SaveGame();
                Assert.IsTrue(File.Exists(debugFile));
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath), "Debug JSON must appear in the Project window after saving.");
                Assert.AreEqual(normalJson, File.ReadAllText(normalFile), "Debug saving must leave the normal save untouched.");
                data.Value.Coins = 99;
                Assert.IsTrue(PersistenceSaveLoadSystem.LoadGame());
                Assert.AreEqual(11, data.Value.Coins);
                data.Value.Coins = 13;
                PersistenceSaveLoadSystem.SaveGame();
                data.Value.Coins = 99;
                Assert.IsTrue(entry.LoadGame());
                Assert.AreEqual(13, data.Value.Coins);
                entry.enabled = false;
                SetField(entry, "debugMode", false);
                SetField(entry, "initialSlotIndex", 4);
                entry.enabled = true;
                Assert.AreEqual(Path.GetFullPath(_directory), PersistenceFileStore.ResolveSaveDirectory());
                Assert.IsTrue(entry.LoadGame());
                Assert.AreEqual(7, data.Value.Coins);
                Assert.IsTrue(File.Exists(debugFile), "Switching back must retain the debug save.");
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                if (File.Exists(debugFile)) File.Delete(debugFile);
                if (!directoryExisted && Directory.Exists(debugDirectory) && !Directory.EnumerateFileSystemEntries(debugDirectory).Any())
                {
                    Directory.Delete(debugDirectory);
                    if (!metaExisted && File.Exists(debugDirectory + ".meta")) File.Delete(debugDirectory + ".meta");
                }
                UnityEditor.AssetDatabase.Refresh();
            }
        }
#endif

        [Test]
        public void DisablingTheHostRestoresPreviousLegacyConfiguration()
        {
            int maxSlots = PersistenceSaveLoadSystem.MaxSaveSlots;
            int slot = PersistenceSaveLoadSystem.CurrentSlotIndex;
            string directory = PersistenceFileStore.SaveDirectoryOverride;
            PersistenceHost entry = CreateEntry();
            Object.DestroyImmediate(entry.gameObject);
            Assert.IsNull(PersistenceHost.ActiveHost);
            Assert.AreEqual(maxSlots, PersistenceSaveLoadSystem.MaxSaveSlots);
            Assert.AreEqual(slot, PersistenceSaveLoadSystem.CurrentSlotIndex);
            Assert.AreEqual(directory, PersistenceFileStore.SaveDirectoryOverride);
        }

        [Test]
        public void ADisabledComponentCannotSaveThroughRestoredLegacyConfiguration()
        {
            string previous = PersistenceFileStore.SaveDirectoryOverride;
            PersistenceFileStore.SaveDirectoryOverride = Path.Combine(_directory, "legacy");
            try
            {
                PersistenceHost entry = CreateEntry();
                entry.enabled = false;
                LogAssert.Expect(LogType.Error, new Regex("PersistenceHost must be active"));
                entry.SaveGame();
                LogAssert.Expect(LogType.Error, new Regex("PersistenceHost must be active"));
                Assert.IsFalse(entry.LoadGame());
                Assert.IsFalse(Directory.Exists(_directory));
            }
            finally { PersistenceFileStore.SaveDirectoryOverride = previous; }
        }

        [Test]
        public void AnExplicitContainerHostReferenceIsUsedAndInvalidWiringIsRejected()
        {
            PersistenceHost entry = CreateEntry();
            Assert.AreSame(Host, entry.ResolveContainerHost());
            var inactive = NewObject("Unready ContainerHost", false).AddComponent<PersistenceContainerHost>();
            SetField(entry, "containerHost", inactive);
            Assert.Throws<InvalidOperationException>(() => PersistenceSession.Capture(0));
            LogAssert.Expect(LogType.Error, new Regex("保存失败:.*assigned ContainerHost"));
            entry.SaveGame(0);
            Assert.IsFalse(Directory.Exists(_directory));
        }

        [Test]
        public void RuntimeDiagnosticsCountTablesRowsAndActiveBindingsWithoutDeletingDisabledData()
        {
            Assert.AreEqual(0, Host.TableCount);
            Host.GetOrCreate("shared", new ContainerTestChest { Coins = 3 });
            PersistenceContainer binding = NewContainer("bound", new ContainerTestChest { Coins = 5 });
            Host.GetOrCreate("shared", new ContainerTestQuest { Progress = 1 });
            Assert.AreEqual(2, Host.TableCount);
            Assert.AreEqual(1, Host.BoundContainerCount);
            Assert.AreEqual(3, Host.GetTableCounts().Sum(pair => pair.Value));
            binding.gameObject.SetActive(false);
            Assert.AreEqual(0, Host.BoundContainerCount);
            Assert.AreEqual(3, Host.GetTableCounts().Sum(pair => pair.Value));
        }

        private PersistenceHost CreateEntry()
        {
            GameObject root = NewObject("PersistenceHost", false);
            var entry = root.AddComponent<PersistenceHost>();
            SetField(entry, "containerHost", Host);
            SetField(entry, "maxSaveSlots", 5);
            SetField(entry, "initialSlotIndex", 4);
            SetField(entry, "saveDirectory", _directory);
            root.SetActive(true);
            return entry;
        }
    }
}
