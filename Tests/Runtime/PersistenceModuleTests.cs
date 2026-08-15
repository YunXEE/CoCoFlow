using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class PersistenceModuleTests
    {
        [TearDown]
        public void TearDown()
        {
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
        }

        [Test]
        public void PersistenceContextGeneratesDistinctStableEntityIds()
        {
            var first = new GameObject("Persistence Context A");
            var second = new GameObject("Persistence Context B");
            try
            {
                var firstContext = first.AddComponent<PersistenceContext>();
                var secondContext = second.AddComponent<PersistenceContext>();

                firstContext.EnsureStableEntityId();
                secondContext.EnsureStableEntityId();

                Assert.IsInstanceOf<ICoCoStableEntityIdProvider>(firstContext);
                Assert.IsFalse(string.IsNullOrEmpty(firstContext.StableEntityId));
                Assert.IsFalse(string.IsNullOrEmpty(secondContext.StableEntityId));
                Assert.AreNotEqual(firstContext.StableEntityId, secondContext.StableEntityId);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void PersistenceSaveDocumentContainsContextAndContainerSections()
        {
            var document = PersistenceSaveDocument.Create(
                0,
                new PersistenceContextSection(),
                new PersistenceContainerSection());

            Assert.AreEqual(PersistenceSaveDocument.CurrentSchemaVersion, document.schemaVersion);
            Assert.IsNotNull(document.contextSection);
            Assert.IsNotNull(document.containerSection);
            Assert.IsNotNull(document.metadata);
        }

        [Test]
        public void VersionOneSaveDocumentMigratesToSchemaTwo()
        {
            var document = new PersistenceSaveDocument
            {
                schemaVersion = 1,
                metadata = null,
                contextSection = null,
                containerSection = null
            };

            PersistenceSaveDocument migrated =
                PersistenceSaveDocument.MigrateToCurrentSchema(document);

            Assert.AreSame(document, migrated);
            Assert.AreEqual(2, migrated.schemaVersion);
            Assert.IsNotNull(migrated.metadata);
            Assert.IsNotNull(migrated.contextSection);
            Assert.IsNotNull(migrated.containerSection);
        }

        [Test]
        public void SaveDocumentRejectsSchemaNewerThanCurrent()
        {
            var document = new PersistenceSaveDocument
            {
                schemaVersion = PersistenceSaveDocument.CurrentSchemaVersion + 1
            };

            Assert.Throws<System.NotSupportedException>(
                () => PersistenceSaveDocument.MigrateToCurrentSchema(document));
        }

        [Test]
        public void SelectingPendingDocumentCreatesFreshRuntimeApplyToken()
        {
            var document = PersistenceSaveDocument.Create(
                0,
                new PersistenceContextSection(),
                new PersistenceContainerSection());

            PersistenceSession.SetPendingDocument(document);
            object firstToken = GetPrivateStaticField<object>(
                typeof(PersistenceSession),
                "_pendingDocumentApplyToken");
            Assert.IsNotNull(firstToken);

            PersistenceSession.SetPendingDocument(document);
            object secondToken = GetPrivateStaticField<object>(
                typeof(PersistenceSession),
                "_pendingDocumentApplyToken");
            Assert.IsNotNull(secondToken);
            Assert.AreNotSame(firstToken, secondToken);

            PersistenceSession.ClearPendingDocument();
            Assert.IsNull(
                GetPrivateStaticField<object>(
                    typeof(PersistenceSession),
                    "_pendingDocumentApplyToken"));
        }

        [Test]
        public void StateGraphContextPayloadRoundTripsAsOpaqueBase64()
        {
            string previousOverride = PersistenceFileStore.SaveDirectoryOverride;
            string directory = CreateTempSaveDirectory();
            try
            {
                PersistenceFileStore.SaveDirectoryOverride = directory;
                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.state-graph.actor",
                    contextType = "CoCoFlow.StateGraph.ContextFrame",
                    prefabKey = "actor.state-graph"
                };
                byte[] payload = { 1, 2, 3, 4, 5 };
                SetPrivateField(record, "stateGraphContextPayload", payload);

                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(record);
                PersistenceFileStore.WriteDocument(
                    0,
                    PersistenceSaveDocument.Create(
                        0,
                        contextSection,
                        new PersistenceContainerSection()));

                string json = File.ReadAllText(PersistenceFileStore.GetSaveFilePath(0));
                StringAssert.Contains("AQIDBAU=", json);
                Assert.IsTrue(PersistenceFileStore.TryReadDocument(0, out var loaded));
                Assert.AreEqual(2, loaded.schemaVersion);
                Assert.IsTrue(
                    loaded.contextSection.TryGetRecord(
                        "scene.state-graph.actor",
                        out var loadedRecord));
                CollectionAssert.AreEqual(
                    payload,
                    GetPrivateField<byte[]>(loadedRecord, "stateGraphContextPayload"));
            }
            finally
            {
                PersistenceFileStore.SaveDirectoryOverride = previousOverride;
                DeleteTempSaveDirectory(directory);
            }
        }

        [UnityTest]
        public IEnumerator DeferredCreatedAndStoppedHostsKeepLatestRecordAndCancelOnDisable()
        {
            DeferredStateGraphHostFixture created = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.created",
                false);
            DeferredStateGraphHostFixture stopped = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.stopped",
                true);
            try
            {
                PersistenceContextRecord createdFirst = CreateStateGraphRecord(
                    created.StableEntityId,
                    1);
                PersistenceContextRecord createdLatest = CreateStateGraphRecord(
                    created.StableEntityId,
                    2);
                PersistenceContextRecord stoppedFirst = CreateStateGraphRecord(
                    stopped.StableEntityId,
                    3);
                PersistenceContextRecord stoppedLatest = CreateStateGraphRecord(
                    stopped.StableEntityId,
                    4);

                Assert.IsTrue(created.Persistence.TryApply(createdFirst));
                Assert.IsTrue(created.Persistence.TryApply(createdLatest));
                Assert.AreSame(
                    createdLatest,
                    GetPrivateField<PersistenceContextRecord>(
                        created.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNotNull(
                    GetPrivateField<Coroutine>(
                        created.Persistence,
                        "_deferredApplyCoroutine"));

                Assert.IsTrue(stopped.Persistence.TryApply(stoppedFirst));
                Assert.IsTrue(stopped.Persistence.TryApply(stoppedLatest));
                Assert.AreSame(
                    stoppedLatest,
                    GetPrivateField<PersistenceContextRecord>(
                        stopped.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNotNull(
                    GetPrivateField<Coroutine>(
                        stopped.Persistence,
                        "_deferredApplyCoroutine"));

                created.Root.SetActive(false);
                stopped.Root.SetActive(false);
                Assert.IsNull(
                    GetPrivateField<PersistenceContextRecord>(
                        created.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNull(
                    GetPrivateField<Coroutine>(
                        created.Persistence,
                        "_deferredApplyCoroutine"));
                Assert.IsNull(
                    GetPrivateField<PersistenceContextRecord>(
                        stopped.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNull(
                    GetPrivateField<Coroutine>(
                        stopped.Persistence,
                        "_deferredApplyCoroutine"));

                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(created.Root);
                Object.DestroyImmediate(stopped.Root);
            }
        }

        [UnityTest]
        public IEnumerator DeferredStateGraphApplyIsCancelledWhenOwnerIsDestroyed()
        {
            DeferredStateGraphHostFixture fixture = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.destroyed-owner",
                false);
            Assert.IsTrue(
                fixture.Persistence.TryApply(
                    CreateStateGraphRecord(fixture.StableEntityId, 5)));

            Object.DestroyImmediate(fixture.Root);
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DeferredStateGraphApplyLogsFailureWhenHostDisappears()
        {
            DeferredStateGraphHostFixture fixture = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.missing-host",
                false);
            try
            {
                Assert.IsTrue(
                    fixture.Persistence.TryApply(
                        CreateStateGraphRecord(fixture.StableEntityId, 6)));
                LogAssert.Expect(
                    LogType.Error,
                    "[PersistenceContext] Deferred StateGraph apply failed for " +
                    "'scene.state-graph.missing-host': StateGraph ContextFrame record " +
                    "requires a CoCoStateGraphHost on the same GameObject.");

                Object.DestroyImmediate(fixture.Host);
                yield return null;

                Assert.IsNull(
                    GetPrivateField<PersistenceContextRecord>(
                        fixture.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNull(
                    GetPrivateField<Coroutine>(
                        fixture.Persistence,
                        "_deferredApplyCoroutine"));
            }
            finally
            {
                Object.DestroyImmediate(fixture.Root);
            }
        }

        [Test]
        public void FileStoreOverwritesExistingSlotWithoutLeavingTemporaryFiles()
        {
            string previousOverride = PersistenceFileStore.SaveDirectoryOverride;
            string directory = CreateTempSaveDirectory();
            try
            {
                PersistenceFileStore.SaveDirectoryOverride = directory;
                var firstSection = new PersistenceContextSection();
                firstSection.AddOrReplace(new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.first"
                });
                var secondSection = new PersistenceContextSection();
                secondSection.AddOrReplace(new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.second"
                });

                PersistenceFileStore.WriteDocument(
                    1,
                    PersistenceSaveDocument.Create(
                        1,
                        firstSection,
                        new PersistenceContainerSection()));
                PersistenceFileStore.WriteDocument(
                    1,
                    PersistenceSaveDocument.Create(
                        1,
                        secondSection,
                        new PersistenceContainerSection()));

                Assert.IsTrue(PersistenceFileStore.TryReadDocument(1, out var loaded));
                Assert.IsFalse(loaded.contextSection.TryGetRecord("scene.item.first", out _));
                Assert.IsTrue(loaded.contextSection.TryGetRecord("scene.item.second", out _));

                string savePath = PersistenceFileStore.GetSaveFilePath(1);
                Assert.IsFalse(File.Exists(savePath + ".tmp"));
                Assert.IsFalse(File.Exists(savePath + ".bak"));
            }
            finally
            {
                PersistenceFileStore.SaveDirectoryOverride = previousOverride;
                DeleteTempSaveDirectory(directory);
            }
        }

        [Test]
        public void FileStoreReadsBackupWhenTargetFileIsMissing()
        {
            string previousOverride = PersistenceFileStore.SaveDirectoryOverride;
            string directory = CreateTempSaveDirectory();
            try
            {
                PersistenceFileStore.SaveDirectoryOverride = directory;
                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.backup"
                });

                PersistenceFileStore.WriteDocument(
                    2,
                    PersistenceSaveDocument.Create(
                        2,
                        contextSection,
                        new PersistenceContainerSection()));

                string savePath = PersistenceFileStore.GetSaveFilePath(2);
                File.Move(savePath, savePath + ".bak");

                Assert.IsTrue(PersistenceFileStore.TryReadDocument(2, out var loaded));
                Assert.IsTrue(loaded.contextSection.TryGetRecord("scene.item.backup", out _));
            }
            finally
            {
                PersistenceFileStore.SaveDirectoryOverride = previousOverride;
                DeleteTempSaveDirectory(directory);
            }
        }

        [Test]
        public void ContainerStoreMaterializesTypedItemContainersAndTransfersItems()
        {
            var root = new GameObject("Persistence Container Store Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                ConfigureContainerCatalog(catalog);
                catalog.containerTemplates.Add(new PersistenceContainerTemplate
                {
                    templateId = "template.player.inventory",
                    definitionId = PersistenceContainerStore.DefaultItemStorageDefinitionId,
                    defaultContainerId = PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    materializeOnNewGame = true,
                    entries = new List<PersistenceContainerEntryTemplate>
                    {
                        new PersistenceContainerEntryTemplate
                        {
                            entryType = PersistenceContainerEntryType.Item,
                            definitionId = "item.medkit.basic",
                            count = 2
                        }
                    }
                });
                catalog.containerTemplates.Add(new PersistenceContainerTemplate
                {
                    templateId = "template.player.stash",
                    definitionId = PersistenceContainerStore.DefaultItemStorageDefinitionId,
                    defaultContainerId = "container.player.stash",
                    materializeOnNewGame = true
                });

                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);

                store.MaterializeStartupContainers();

                Assert.AreEqual(
                    2,
                    store.GetItemCount(
                        PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                        "item.medkit.basic"));
                var bridge = root.AddComponent<PersistenceContainerBridge>();
                bridge.SetActorId("actor.player");
                bridge.SetContainerId(PersistenceContainerStore.DefaultPlayerInventoryContainerId);

                Assert.IsTrue(bridge.RequestTransferItemTo(
                    "container.player.stash",
                    "item.medkit.basic",
                    1));
                Assert.AreEqual(
                    1,
                    store.GetItemCount(
                        PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                        "item.medkit.basic"));
                Assert.AreEqual(1, store.GetItemCount("container.player.stash", "item.medkit.basic"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ContainerStoreMaintainsQuestBookEventLogAndFactSet()
        {
            var root = new GameObject("Persistence Container Data Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                ConfigureContainerCatalog(catalog);
                catalog.sequentialQuestDefinitions.Add(CreateGemQuestDefinition());

                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);
                store.EnsureContainer(
                    PersistenceContainerStore.DefaultQuestBookContainerId,
                    PersistenceContainerStore.DefaultQuestBookDefinitionId,
                    PersistenceContainerType.QuestBook);
                Assert.IsTrue(store.ActivateQuest(
                    PersistenceContainerStore.DefaultQuestBookContainerId,
                    "quest.village.gem"));

                var questBridge = root.AddComponent<PersistenceContainerBridge>();
                questBridge.SetActorId("actor.quest-source");
                Assert.IsTrue(questBridge.RequestEntityKilled(
                    new List<string> { "Entity.Monster.GemGuardian" },
                    PersistenceContainerStore.DefaultQuestBookContainerId));

                var progress = store.GetOrAddQuestProgress(
                    PersistenceContainerStore.DefaultQuestBookContainerId,
                    "quest.village.gem");
                Assert.AreEqual(PersistenceQuestStatus.Active, progress.status);
                Assert.AreEqual(1, progress.currentStepIndex);

                Assert.IsTrue(questBridge.RequestItemDelivered(
                    "item.gem.red",
                    new List<string> { "Entity.Npc.VillageElder" },
                    PersistenceContainerStore.DefaultQuestBookContainerId));
                Assert.AreEqual(PersistenceQuestStatus.Completed, progress.status);

                var eventBridge = root.AddComponent<PersistenceContainerBridge>();
                eventBridge.SetActorId("actor.world-event");
                eventBridge.SetContainerId(PersistenceContainerStore.DefaultEventLogContainerId);
                Assert.IsTrue(eventBridge.RequestSetEventState("event.raid.extracted", "Triggered"));

                eventBridge.SetContainerId(PersistenceContainerStore.DefaultWorldFactContainerId);
                Assert.IsTrue(eventBridge.RequestSetFactBool("fact.village.door_open", true));
                Assert.IsTrue(store.GetFactBool(
                    PersistenceContainerStore.DefaultWorldFactContainerId,
                    "fact.village.door_open"));
                Assert.IsTrue(store.TryGetContainer(
                    PersistenceContainerStore.DefaultEventLogContainerId,
                    out var eventLog));
                Assert.AreEqual(PersistenceContainerType.EventLog, eventLog.containerType);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ContainerStoreRejectsCrossTypeTransfersWhenPolicyRequiresSameType()
        {
            var root = new GameObject("Persistence Container Policy Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                ConfigureContainerCatalog(catalog);
                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);
                store.EnsureContainer(
                    "container.source.stash",
                    PersistenceContainerStore.DefaultItemStorageDefinitionId,
                    PersistenceContainerType.ItemStorage);
                store.EnsureContainer(
                    "container.target.questbook",
                    PersistenceContainerStore.DefaultQuestBookDefinitionId,
                    PersistenceContainerType.QuestBook);

                Assert.IsTrue(store.AddItemToContainer("container.source.stash", "item.gem.red", 1));
                Assert.IsFalse(store.TransferItem(
                    "container.source.stash",
                    "container.target.questbook",
                    "item.gem.red",
                    1));
                Assert.AreEqual(1, store.GetItemCount("container.source.stash", "item.gem.red"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ContainerStoreKeepsExistingContainerTypeWhenCommandIsRejected()
        {
            var root = new GameObject("Persistence Container Type Guard Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                ConfigureContainerCatalog(catalog);
                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);
                store.EnsureContainer(
                    PersistenceContainerStore.DefaultQuestBookContainerId,
                    PersistenceContainerStore.DefaultQuestBookDefinitionId,
                    PersistenceContainerType.QuestBook);

                Assert.IsFalse(store.AddItemToContainer(
                    PersistenceContainerStore.DefaultQuestBookContainerId,
                    "item.gem.red",
                    1));
                Assert.IsTrue(store.TryGetContainer(
                    PersistenceContainerStore.DefaultQuestBookContainerId,
                    out var record));
                Assert.AreEqual(PersistenceContainerType.QuestBook, record.containerType);
                Assert.AreEqual(PersistenceContainerStore.DefaultQuestBookDefinitionId, record.definitionId);
                Assert.AreEqual(0, record.entries.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ContainerStoreRejectsNewItemStackWhenCapacityIsFull()
        {
            var root = new GameObject("Persistence Container Capacity Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                ConfigureContainerCatalog(catalog);
                catalog.FindContainerDefinition(PersistenceContainerStore.DefaultItemStorageDefinitionId).capacity = 1;

                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);
                store.EnsureContainer(
                    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    PersistenceContainerStore.DefaultItemStorageDefinitionId,
                    PersistenceContainerType.ItemStorage);

                Assert.IsTrue(store.AddItemToContainer(
                    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    "item.medkit.basic",
                    10));
                Assert.IsFalse(store.AddItemToContainer(
                    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    "item.medkit.basic",
                    1));
                Assert.IsTrue(store.TryGetContainer(
                    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    out var record));
                Assert.AreEqual(1, record.entries.Count);
                Assert.AreEqual(
                    10,
                    store.GetItemCount(
                        PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                        "item.medkit.basic"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static PersistenceSequentialQuestDefinition CreateGemQuestDefinition()
        {
            return new PersistenceSequentialQuestDefinition
            {
                questId = "quest.village.gem",
                displayName = "Gem Quest",
                steps = new List<PersistenceQuestStepDefinition>
                {
                    new PersistenceQuestStepDefinition
                    {
                        stepId = "kill_guardian",
                        objectiveType = PersistenceQuestObjectiveType.EntityKilled,
                        targetTag = "Entity.Monster.GemGuardian",
                        requiredCount = 1
                    },
                    new PersistenceQuestStepDefinition
                    {
                        stepId = "deliver_gem",
                        objectiveType = PersistenceQuestObjectiveType.ItemDelivered,
                        targetTag = "Entity.Npc.VillageElder",
                        itemId = "item.gem.red",
                        requiredCount = 1
                    }
                }
            };
        }

        private static void ConfigureContainerCatalog(PersistenceContainerCatalog catalog)
        {
            catalog.itemDefinitions.Add(new PersistenceItemDefinition
            {
                itemId = "item.gem.red",
                displayName = "Red Gem",
                stackable = true,
                maxStack = 99
            });
            catalog.itemDefinitions.Add(new PersistenceItemDefinition
            {
                itemId = "item.medkit.basic",
                displayName = "Basic Medkit",
                stackable = true,
                maxStack = 10
            });
            catalog.containerDefinitions.Add(new PersistenceContainerDefinition
            {
                definitionId = PersistenceContainerStore.DefaultItemStorageDefinitionId,
                containerType = PersistenceContainerType.ItemStorage,
                sameTypeTransfersOnly = true,
                acceptedEntryTypes = new List<PersistenceContainerEntryType>
                {
                    PersistenceContainerEntryType.Item
                }
            });
            catalog.containerDefinitions.Add(new PersistenceContainerDefinition
            {
                definitionId = PersistenceContainerStore.DefaultQuestBookDefinitionId,
                containerType = PersistenceContainerType.QuestBook,
                sameTypeTransfersOnly = true,
                acceptedEntryTypes = new List<PersistenceContainerEntryType>
                {
                    PersistenceContainerEntryType.QuestProgress
                }
            });
            catalog.containerDefinitions.Add(new PersistenceContainerDefinition
            {
                definitionId = PersistenceContainerStore.DefaultEventLogDefinitionId,
                containerType = PersistenceContainerType.EventLog,
                acceptedEntryTypes = new List<PersistenceContainerEntryType>
                {
                    PersistenceContainerEntryType.EventState
                }
            });
            catalog.containerDefinitions.Add(new PersistenceContainerDefinition
            {
                definitionId = PersistenceContainerStore.DefaultFactSetDefinitionId,
                containerType = PersistenceContainerType.FactSet,
                acceptedEntryTypes = new List<PersistenceContainerEntryType>
                {
                    PersistenceContainerEntryType.Fact
                }
            });
            catalog.eventDefinitions.Add(new PersistenceEventDefinition
            {
                eventId = "event.raid.extracted",
                displayName = "Raid Extracted"
            });
            catalog.factDefinitions.Add(new PersistenceFactDefinition
            {
                factId = "fact.village.door_open",
                displayName = "Village Door Open"
            });
        }

        private static string CreateTempSaveDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "CoCoFlowPersistenceTests",
                System.Guid.NewGuid().ToString("N"));
        }

        private static void DeleteTempSaveDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {target.GetType().Name}.");
            return (T)field.GetValue(target);
        }

        private static T GetPrivateStaticField<T>(System.Type type, string fieldName)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {type.Name}.");
            return (T)field.GetValue(null);
        }

        private static DeferredStateGraphHostFixture CreateDeferredStateGraphHostFixture(
            string stableEntityId,
            bool stopped)
        {
            var root = new GameObject(
                stopped
                    ? "Persistence Deferred Stopped Host Test"
                    : "Persistence Deferred Created Host Test");
            root.SetActive(false);
            Component host = root.AddComponent(ResolveStateGraphHostType());
            SetPrivateField(host, "autoStart", false);
            SetPrivateField(host, "_hasStoppedInstance", stopped);
            var persistence = root.AddComponent<PersistenceContext>();
            SetPrivateField(persistence, "stableEntityId", stableEntityId);
            root.SetActive(true);
            return new DeferredStateGraphHostFixture(
                stableEntityId,
                root,
                host,
                persistence);
        }

        private static PersistenceContextRecord CreateStateGraphRecord(
            string stableEntityId,
            byte marker)
        {
            var record = new PersistenceContextRecord
            {
                stableEntityId = stableEntityId,
                contextType = "CoCoFlow.StateGraph.ContextFrame",
                prefabKey = "pre13.state-graph"
            };
            SetPrivateField(
                record,
                "stateGraphContextPayload",
                new[] { marker });
            return record;
        }

        private static System.Type ResolveStateGraphHostType()
        {
            System.Type type = System.Type.GetType(
                "CoCoFlow.Runtime.Core.CoCoStateGraphHost, CoCoFlow.Runtime.StateGraphHost");
            Assert.IsNotNull(type, "CoCoStateGraphHost runtime type could not be resolved.");
            return type;
        }

        private static void SetHostLifecycleToRunning(Component host)
        {
            System.Type runtimeType = System.Type.GetType(
                "CoCoFlow.Runtime.Core.CoCoStateGraphRuntime, " +
                "CoCoFlow.Runtime.Core.StateGraph");
            Assert.IsNotNull(runtimeType, "CoCoStateGraphRuntime type could not be resolved.");
            object runtime =
                System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                    runtimeType);
            FieldInfo lifecycleField = runtimeType.GetField(
                "_lifecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(lifecycleField);
            lifecycleField.SetValue(
                runtime,
                System.Enum.Parse(lifecycleField.FieldType, "Running"));
            SetPrivateField(host, "_runtime", runtime);
        }

        private sealed class DeferredStateGraphHostFixture
        {
            internal DeferredStateGraphHostFixture(
                string stableEntityId,
                GameObject root,
                Component host,
                PersistenceContext persistence)
            {
                StableEntityId = stableEntityId;
                Root = root;
                Host = host;
                Persistence = persistence;
            }

            internal string StableEntityId { get; }
            internal GameObject Root { get; }
            internal Component Host { get; }
            internal PersistenceContext Persistence { get; }
        }
    }
}
