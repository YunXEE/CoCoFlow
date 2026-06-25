using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Persistence;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    public static class PersistenceContainerValidator
    {
        [MenuItem("CoCoFlow/Persistence/Validate Selected Catalog")]
        public static void ValidateSelected()
        {
            Validate(Selection.activeObject as PersistenceContainerCatalog);
        }

        public static bool Validate(PersistenceContainerCatalog catalog)
        {
            if (catalog == null)
            {
                Debug.LogWarning("[Persistence] No PersistenceContainerCatalog selected.");
                return false;
            }

            bool valid = true;
            var ids = new HashSet<string>();
            valid &= ValidateUniqueIds(catalog.itemDefinitions, ids, item => item.itemId, "item");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.rewardDefinitions, ids, reward => reward.rewardId, "reward");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.containerDefinitions, ids, definition => definition.definitionId, "container definition");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.containerTemplates, ids, template => template.templateId, "container template");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.lootTableDefinitions, ids, table => table.lootTableId, "loot table");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.eventDefinitions, ids, evt => evt.eventId, "event");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.factDefinitions, ids, fact => fact.factId, "fact");
            ids.Clear();
            valid &= ValidateUniqueIds(catalog.sequentialQuestDefinitions, ids, quest => quest.questId, "quest");

            valid &= ValidateTemplateReferences(catalog);
            valid &= ValidateRewardEntries(catalog);
            valid &= ValidateQuestSteps(catalog);

            Debug.Log(valid
                ? "[Persistence] Catalog validation passed."
                : "[Persistence] Catalog validation found issues.");
            return valid;
        }

        private static bool ValidateTemplateReferences(PersistenceContainerCatalog catalog)
        {
            bool valid = true;
            for (int i = 0; i < catalog.containerTemplates.Count; i++)
            {
                var template = catalog.containerTemplates[i];
                if (template == null) continue;

                if (catalog.FindContainerDefinition(template.definitionId) == null)
                {
                    Debug.LogWarning(
                        $"[Persistence] Template {template.templateId} references missing container definition {template.definitionId}.",
                        catalog);
                    valid = false;
                }

                for (int tableIndex = 0; tableIndex < template.lootTableIds.Count; tableIndex++)
                {
                    string lootTableId = template.lootTableIds[tableIndex];
                    if (catalog.FindLootTable(lootTableId) == null)
                    {
                        Debug.LogWarning(
                            $"[Persistence] Template {template.templateId} references missing loot table {lootTableId}.",
                            catalog);
                        valid = false;
                    }
                }

                valid &= ValidateEntryReferences(catalog, template.entries, $"template {template.templateId}");
            }

            return valid;
        }

        private static bool ValidateRewardEntries(PersistenceContainerCatalog catalog)
        {
            bool valid = true;
            for (int i = 0; i < catalog.rewardDefinitions.Count; i++)
            {
                var reward = catalog.rewardDefinitions[i];
                if (reward == null) continue;
                valid &= ValidateEntryReferences(catalog, reward.entries, $"reward {reward.rewardId}");
            }

            for (int i = 0; i < catalog.lootTableDefinitions.Count; i++)
            {
                var lootTable = catalog.lootTableDefinitions[i];
                if (lootTable == null) continue;

                for (int entryIndex = 0; entryIndex < lootTable.weightedEntries.Count; entryIndex++)
                {
                    var weightedEntry = lootTable.weightedEntries[entryIndex];
                    if (weightedEntry == null) continue;
                    valid &= ValidateEntryReference(catalog, weightedEntry.entry, $"loot table {lootTable.lootTableId}");
                }
            }

            return valid;
        }

        private static bool ValidateQuestSteps(PersistenceContainerCatalog catalog)
        {
            bool valid = true;
            for (int i = 0; i < catalog.sequentialQuestDefinitions.Count; i++)
            {
                var quest = catalog.sequentialQuestDefinitions[i];
                if (quest == null) continue;
                if (quest.steps == null || quest.steps.Count == 0)
                {
                    Debug.LogWarning($"[Persistence] Quest {quest.questId} has no steps.", catalog);
                    valid = false;
                }
            }

            return valid;
        }

        private static bool ValidateEntryReferences(
            PersistenceContainerCatalog catalog,
            IReadOnlyList<PersistenceContainerEntryTemplate> entries,
            string owner)
        {
            bool valid = true;
            if (entries == null) return true;

            for (int i = 0; i < entries.Count; i++)
            {
                valid &= ValidateEntryReference(catalog, entries[i], owner);
            }

            return valid;
        }

        private static bool ValidateEntryReference(
            PersistenceContainerCatalog catalog,
            PersistenceContainerEntryTemplate entry,
            string owner)
        {
            if (entry == null) return true;

            bool valid = true;
            switch (entry.entryType)
            {
                case PersistenceContainerEntryType.Item:
                    valid = catalog.FindItem(entry.definitionId) != null;
                    break;
                case PersistenceContainerEntryType.QuestProgress:
                    valid = catalog.FindQuest(entry.definitionId) != null;
                    break;
                case PersistenceContainerEntryType.EventState:
                    valid = catalog.FindEvent(entry.definitionId) != null;
                    break;
                case PersistenceContainerEntryType.Fact:
                    valid = catalog.FindFact(entry.definitionId) != null;
                    break;
            }

            if (!valid)
            {
                Debug.LogWarning(
                    $"[Persistence] {owner} references missing {entry.entryType} definition {entry.definitionId}.",
                    catalog);
            }

            return valid;
        }

        private static bool ValidateUniqueIds<T>(
            IReadOnlyList<T> items,
            HashSet<string> ids,
            System.Func<T, string> getId,
            string label)
            where T : class
        {
            bool valid = true;
            if (items == null) return true;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;

                string id = getId(item);
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[Persistence] Empty {label} id at index {i}.");
                    valid = false;
                    continue;
                }

                if (!ids.Add(id))
                {
                    Debug.LogWarning($"[Persistence] Duplicate {label} id: {id}.");
                    valid = false;
                }
            }

            return valid;
        }
    }
}
