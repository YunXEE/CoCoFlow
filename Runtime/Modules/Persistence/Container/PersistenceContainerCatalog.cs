using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    [CreateAssetMenu(fileName = "PersistenceContainerCatalog", menuName = "CoCoFlow/Persistence/Container Catalog")]
    public sealed class PersistenceContainerCatalog : ScriptableObject
    {
        public List<PersistenceGameplayTag> gameplayTags = new List<PersistenceGameplayTag>();
        public List<PersistenceItemDefinition> itemDefinitions = new List<PersistenceItemDefinition>();
        public List<PersistenceRewardDefinition> rewardDefinitions = new List<PersistenceRewardDefinition>();
        public List<PersistenceContainerDefinition> containerDefinitions = new List<PersistenceContainerDefinition>();
        public List<PersistenceContainerTemplate> containerTemplates = new List<PersistenceContainerTemplate>();
        public List<PersistenceLootTableDefinition> lootTableDefinitions = new List<PersistenceLootTableDefinition>();
        public List<PersistenceEventDefinition> eventDefinitions = new List<PersistenceEventDefinition>();
        public List<PersistenceFactDefinition> factDefinitions = new List<PersistenceFactDefinition>();
        public List<PersistenceSequentialQuestDefinition> sequentialQuestDefinitions =
            new List<PersistenceSequentialQuestDefinition>();

        #region Public API

        public PersistenceRewardDefinition FindReward(string rewardId)
        {
            return rewardDefinitions.Find(reward => reward != null && reward.rewardId == rewardId);
        }

        public PersistenceContainerDefinition FindContainerDefinition(string definitionId)
        {
            return containerDefinitions.Find(container => container != null && container.definitionId == definitionId);
        }

        public PersistenceContainerDefinition FindContainer(string definitionId)
        {
            return FindContainerDefinition(definitionId);
        }

        public PersistenceContainerTemplate FindTemplate(string templateId)
        {
            return containerTemplates.Find(template => template != null && template.templateId == templateId);
        }

        public PersistenceLootTableDefinition FindLootTable(string lootTableId)
        {
            return lootTableDefinitions.Find(table => table != null && table.lootTableId == lootTableId);
        }

        public PersistenceEventDefinition FindEvent(string eventId)
        {
            return eventDefinitions.Find(evt => evt != null && evt.eventId == eventId);
        }

        public PersistenceFactDefinition FindFact(string factId)
        {
            return factDefinitions.Find(fact => fact != null && fact.factId == factId);
        }

        public PersistenceSequentialQuestDefinition FindQuest(string questId)
        {
            return sequentialQuestDefinitions.Find(quest => quest != null && quest.questId == questId);
        }

        public PersistenceItemDefinition FindItem(string itemId)
        {
            return itemDefinitions.Find(item => item != null && item.itemId == itemId);
        }

        #endregion
    }
}
