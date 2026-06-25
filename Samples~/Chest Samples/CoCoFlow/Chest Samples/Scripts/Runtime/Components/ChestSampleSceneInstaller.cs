using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.ChestSamples
{
    public sealed class ChestSampleSceneInstaller : MonoBehaviour
    {
        public const string PlayerInventoryContainerId = "container.player.inventory";
        public const string WorldFactContainerId = "container.world.facts";
        public const string EventLogContainerId = "container.world.events";
        public const string ChestRewardId = "reward.chest_sample.gem_cache";
        public const string ChestOpenedFactId = "fact.chest_sample.opened";
        public const string ChestOpenedEventId = "event.chest_sample.opened";
        public const string GemItemId = "item.chest_sample.gem";
        public const string CoinItemId = "item.chest_sample.coin";

        [SerializeField] private PersistenceContainerStore containerStore;
        [SerializeField] private bool configureOnAwake = true;
        [SerializeField] private bool materializeStartupContainers = true;

        private PersistenceContainerCatalog runtimeCatalog;

        #region Public API

        public PersistenceContainerCatalog RuntimeCatalog => runtimeCatalog;

        public void Configure()
        {
            var store = ResolveStore();
            if (store == null) return;

            runtimeCatalog = CreateRuntimeCatalog();
            store.SetCatalog(runtimeCatalog);

            if (materializeStartupContainers)
            {
                store.MaterializeStartupContainers();
            }
        }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            if (configureOnAwake)
            {
                Configure();
            }
        }

        private void OnDestroy()
        {
            if (runtimeCatalog == null) return;

            if (Application.isPlaying)
            {
                Destroy(runtimeCatalog);
            }
            else
            {
                DestroyImmediate(runtimeCatalog);
            }

            runtimeCatalog = null;
        }

        private PersistenceContainerStore ResolveStore()
        {
            if (containerStore != null) return containerStore;

            containerStore = GetComponent<PersistenceContainerStore>();
            if (containerStore == null)
            {
                containerStore = gameObject.AddComponent<PersistenceContainerStore>();
            }

            return containerStore;
        }

        private static PersistenceContainerCatalog CreateRuntimeCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            catalog.name = "ChestSampleRuntimeCatalog";
            catalog.hideFlags = HideFlags.DontSave;

            AddDefinitions(catalog);
            AddTemplates(catalog);
            AddReward(catalog);
            return catalog;
        }

        private static void AddDefinitions(PersistenceContainerCatalog catalog)
        {
            catalog.itemDefinitions.Add(new PersistenceItemDefinition
            {
                itemId = GemItemId,
                displayName = "Chest Sample Gem",
                stackable = true,
                maxStack = 99,
                tags = new List<string> { "Item.Currency.Gem" }
            });
            catalog.itemDefinitions.Add(new PersistenceItemDefinition
            {
                itemId = CoinItemId,
                displayName = "Chest Sample Coin",
                stackable = true,
                maxStack = 999,
                tags = new List<string> { "Item.Currency.Coin" }
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
                definitionId = PersistenceContainerStore.DefaultFactSetDefinitionId,
                containerType = PersistenceContainerType.FactSet,
                acceptedEntryTypes = new List<PersistenceContainerEntryType>
                {
                    PersistenceContainerEntryType.Fact
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
            catalog.factDefinitions.Add(new PersistenceFactDefinition
            {
                factId = ChestOpenedFactId,
                displayName = "Chest Sample Opened"
            });
            catalog.eventDefinitions.Add(new PersistenceEventDefinition
            {
                eventId = ChestOpenedEventId,
                displayName = "Chest Sample Opened"
            });
        }

        private static void AddTemplates(PersistenceContainerCatalog catalog)
        {
            catalog.containerTemplates.Add(new PersistenceContainerTemplate
            {
                templateId = "template.chest_sample.player_inventory",
                definitionId = PersistenceContainerStore.DefaultItemStorageDefinitionId,
                defaultContainerId = PlayerInventoryContainerId,
                materializeOnNewGame = true
            });
            catalog.containerTemplates.Add(new PersistenceContainerTemplate
            {
                templateId = "template.chest_sample.world_facts",
                definitionId = PersistenceContainerStore.DefaultFactSetDefinitionId,
                defaultContainerId = WorldFactContainerId,
                materializeOnNewGame = true
            });
            catalog.containerTemplates.Add(new PersistenceContainerTemplate
            {
                templateId = "template.chest_sample.event_log",
                definitionId = PersistenceContainerStore.DefaultEventLogDefinitionId,
                defaultContainerId = EventLogContainerId,
                materializeOnNewGame = true
            });
        }

        private static void AddReward(PersistenceContainerCatalog catalog)
        {
            catalog.rewardDefinitions.Add(new PersistenceRewardDefinition
            {
                rewardId = ChestRewardId,
                entries = new List<PersistenceContainerEntryTemplate>
                {
                    new PersistenceContainerEntryTemplate
                    {
                        entryType = PersistenceContainerEntryType.Item,
                        definitionId = GemItemId,
                        count = 1
                    },
                    new PersistenceContainerEntryTemplate
                    {
                        entryType = PersistenceContainerEntryType.Item,
                        definitionId = CoinItemId,
                        count = 25
                    }
                }
            });
        }

        #endregion
    }
}
