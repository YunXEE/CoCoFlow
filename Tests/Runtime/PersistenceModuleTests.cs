using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Gameplay.Item;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class PersistenceModuleTests
    {
        [TearDown]
        public void TearDown()
        {
            PersistenceContextRegistry.Clear();
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
        public void CharacterContextAdapterRestoresDurableFacts()
        {
            var source = new CharacterContext();
            source.Identity.StableEntityId = "actor.player";
            source.MarkAlive();
            source.Motion.position = new Vector3(1f, 2f, 3f);
            source.Motion.rotation = Quaternion.Euler(0f, 45f, 0f);
            source.Resources.MaxHealth = 200f;
            source.Resources.CurrentHealth = 75f;

            Assert.IsTrue(PersistenceContextAdapterRegistry.TryCapture(
                source.Identity.StableEntityId,
                source,
                out var record));

            var target = new CharacterContext();
            Assert.IsTrue(PersistenceContextAdapterRegistry.TryApply(record, target));

            Assert.AreEqual("actor.player", target.Identity.StableEntityId);
            Assert.AreEqual(CoCoLifecycleState.Active, target.Lifecycle.State);
            Assert.AreEqual((int)CharacterSemanticState.Alive, target.SemanticStateId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.Motion.position);
            Assert.AreEqual(200f, target.Resources.MaxHealth);
            Assert.AreEqual(75f, target.Resources.CurrentHealth);
        }

        [Test]
        public void ItemContextAdapterRestoresOpenedPayloadFacts()
        {
            var source = new ItemContext();
            source.Identity.StableEntityId = "item.chest";
            source.Payload.itemId = "item.gem.red";
            source.Payload.count = 2;
            source.SetOpened();

            Assert.IsTrue(PersistenceContextAdapterRegistry.TryCapture(
                source.Identity.StableEntityId,
                source,
                out var record));

            var target = new ItemContext();
            Assert.IsTrue(PersistenceContextAdapterRegistry.TryApply(record, target));

            Assert.AreEqual("item.chest", target.Identity.StableEntityId);
            Assert.AreEqual(ItemSemanticState.Opened, target.ItemState);
            Assert.AreEqual(CoCoLifecycleState.Active, target.Lifecycle.State);
            Assert.AreEqual("item.gem.red", target.Payload.itemId);
            Assert.AreEqual(2, target.Payload.count);
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
        public void ContainerBridgeGrantsContainerRewardAndItemContextCanStayOpened()
        {
            var root = new GameObject("Persistence Bridge Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                catalog.rewardDefinitions.Add(new PersistenceRewardDefinition
                {
                    rewardId = "reward.chest.gem",
                    entries = new List<PersistenceContainerEntryTemplate>
                    {
                        new PersistenceContainerEntryTemplate
                        {
                            entryType = PersistenceContainerEntryType.Item,
                            definitionId = "item.gem.red",
                            count = 1
                        }
                    }
                });
                ConfigureContainerCatalog(catalog);

                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);
                store.EnsureContainer(
                    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    PersistenceContainerStore.DefaultItemStorageDefinitionId,
                    PersistenceContainerType.ItemStorage);
                var bridge = root.AddComponent<PersistenceContainerBridge>();
                bridge.SetActorId("actor.chest");
                bridge.SetContainerId(PersistenceContainerStore.DefaultPlayerInventoryContainerId);

                var itemContext = new ItemContext();
                itemContext.SetOpened();

                Assert.IsTrue(bridge.RequestGrantReward("reward.chest.gem"));
                Assert.AreEqual(
                    1,
                    store.GetItemCount(
                        PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                        "item.gem.red"));
                Assert.AreEqual(ItemSemanticState.Opened, itemContext.ItemState);
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

        [Test]
        public void PendingDocumentAppliesWhenPersistenceContextRegisters()
        {
            var root = new GameObject("Persistence Pending Context Test");
            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(persistenceContext, "stableEntityId", "scene.item.pending");

                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.pending",
                    lifecycleState = (int)CoCoLifecycleState.Active,
                    semanticStateId = (int)ItemSemanticState.Opened
                };
                record.stringFacts["item.state"] = "Opened";

                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(record);
                PersistenceSession.SetPendingDocument(PersistenceSaveDocument.Create(
                    0,
                    contextSection,
                    new PersistenceContainerSection()));

                PersistenceContextRegistry.Register(persistenceContext);

                Assert.AreEqual(ItemSemanticState.Opened, provider.Context.ItemState);
                Assert.AreEqual("scene.item.pending", provider.Context.Identity.StableEntityId);
            }
            finally
            {
                PersistenceSession.ClearPendingDocument();
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

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
