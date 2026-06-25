using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    [Serializable]
    public sealed class PersistenceGameplayTag
    {
        public string tag = string.Empty;
        public string description = string.Empty;
    }

    [Serializable]
    public sealed class PersistenceTagQuery
    {
        public List<string> allTags = new List<string>();
        public List<string> anyTags = new List<string>();
        public List<string> noneTags = new List<string>();

        public bool Matches(IReadOnlyList<string> candidateTags)
        {
            if (candidateTags == null) return allTags.Count == 0 && anyTags.Count == 0;

            for (int i = 0; i < allTags.Count; i++)
            {
                if (!Contains(candidateTags, allTags[i])) return false;
            }

            if (anyTags.Count > 0)
            {
                bool anyMatch = false;
                for (int i = 0; i < anyTags.Count; i++)
                {
                    if (Contains(candidateTags, anyTags[i]))
                    {
                        anyMatch = true;
                        break;
                    }
                }

                if (!anyMatch) return false;
            }

            for (int i = 0; i < noneTags.Count; i++)
            {
                if (Contains(candidateTags, noneTags[i])) return false;
            }

            return true;
        }

        private static bool Contains(IReadOnlyList<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == tag) return true;
            }

            return false;
        }
    }

    [Serializable]
    public enum PersistenceContainerType
    {
        ItemStorage,
        QuestBook,
        EventLog,
        FactSet
    }

    [Serializable]
    public enum PersistenceContainerEntryType
    {
        Item,
        QuestProgress,
        EventState,
        Fact
    }

    [Serializable]
    public enum PersistenceQuestStatus
    {
        Inactive,
        Active,
        Completed,
        Failed
    }

    [Serializable]
    public enum PersistenceQuestObjectiveType
    {
        EntityKilled,
        ItemDelivered
    }

    public static class PersistenceContainerEventIds
    {
        public const string CommandRequested = "Persistence.Container.CommandRequested";
        public const string CommandApplied = "Persistence.Container.CommandApplied";
        public const string CommandRejected = "Persistence.Container.CommandRejected";
    }

    [Serializable]
    public enum PersistenceContainerCommandType
    {
        MaterializeContainer,
        AddEntry,
        AddItem,
        TransferItem,
        GrantReward,
        ApplyQuestEvent,
        SetEventState,
        SetFactBool
    }

    [Serializable]
    public sealed class PersistenceContainerCommandRequested
    {
        public string commandId = string.Empty;
        public PersistenceContainerCommandType commandType;
        public string actorId = string.Empty;
        public string sourceContainerId = string.Empty;
        public string targetContainerId = string.Empty;
        public string templateId = string.Empty;
        public string rewardId = string.Empty;
        public string definitionId = string.Empty;
        public PersistenceContainerEntryTemplate entryTemplate;
        public PersistenceQuestObjectiveType questObjectiveType;
        public List<string> tags = new List<string>();
        public int count;
        public int slotIndex = -1;
        public int sequence;
        public int tick;
        public bool reliable = true;
        public string state = string.Empty;
        public string stringValue = string.Empty;
        public bool boolValue;
    }

    [Serializable]
    public sealed class PersistenceContainerCommandApplied
    {
        public string commandId = string.Empty;
        public PersistenceContainerCommandType commandType;
        public string actorId = string.Empty;
        public string sourceContainerId = string.Empty;
        public string targetContainerId = string.Empty;
        public int sequence;
        public int tick;
        public List<string> changedContainerIds = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceContainerCommandRejected
    {
        public string commandId = string.Empty;
        public PersistenceContainerCommandType commandType;
        public string actorId = string.Empty;
        public string sourceContainerId = string.Empty;
        public string targetContainerId = string.Empty;
        public int sequence;
        public int tick;
        public string reason = string.Empty;
    }

    [Serializable]
    public sealed class PersistenceItemDefinition
    {
        public string itemId = string.Empty;
        public string displayName = string.Empty;
        public bool stackable = true;
        public int maxStack = 99;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceQuestStepDefinition
    {
        public string stepId = string.Empty;
        public PersistenceQuestObjectiveType objectiveType;
        public string eventTag = string.Empty;
        public string targetTag = string.Empty;
        public string itemId = string.Empty;
        public int requiredCount = 1;
    }

    [Serializable]
    public sealed class PersistenceSequentialQuestDefinition
    {
        public string questId = string.Empty;
        public string displayName = string.Empty;
        public List<string> tags = new List<string>();
        public List<PersistenceQuestStepDefinition> steps = new List<PersistenceQuestStepDefinition>();
    }

    [Serializable]
    public sealed class PersistenceQuestProgressRecord
    {
        public string questId = string.Empty;
        public PersistenceQuestStatus status = PersistenceQuestStatus.Inactive;
        public int currentStepIndex;
        public int currentCount;

        public bool IsActive => status == PersistenceQuestStatus.Active;
    }

    [Serializable]
    public sealed class PersistenceEventDefinition
    {
        public string eventId = string.Empty;
        public string displayName = string.Empty;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceFactDefinition
    {
        public string factId = string.Empty;
        public string displayName = string.Empty;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceContainerDefinition
    {
        public string definitionId = string.Empty;
        public PersistenceContainerType containerType = PersistenceContainerType.ItemStorage;
        public string displayName = string.Empty;
        public int capacity = -1;
        public bool sameTypeTransfersOnly = true;
        public List<PersistenceContainerEntryType> acceptedEntryTypes = new List<PersistenceContainerEntryType>();
        public List<PersistenceContainerType> transferableContainerTypes = new List<PersistenceContainerType>();
        public PersistenceTagQuery acceptedItemTags = new PersistenceTagQuery();
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceContainerEntryTemplate
    {
        public PersistenceContainerEntryType entryType = PersistenceContainerEntryType.Item;
        public string definitionId = string.Empty;
        public int count = 1;
        public int slotIndex = -1;
        public string state = string.Empty;
        public string stringValue = string.Empty;
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceWeightedContainerEntry
    {
        public PersistenceContainerEntryTemplate entry = new PersistenceContainerEntryTemplate();
        public int weight = 1;
    }

    [Serializable]
    public sealed class PersistenceLootTableDefinition
    {
        public string lootTableId = string.Empty;
        public int rolls = 1;
        public List<PersistenceWeightedContainerEntry> weightedEntries = new List<PersistenceWeightedContainerEntry>();
    }

    [Serializable]
    public sealed class PersistenceContainerTemplate
    {
        public string templateId = string.Empty;
        public string definitionId = string.Empty;
        public string defaultContainerId = string.Empty;
        public string ownerId = string.Empty;
        public string authorityId = string.Empty;
        public string scope = string.Empty;
        public bool materializeOnNewGame;
        public int seed;
        public List<PersistenceContainerEntryTemplate> entries = new List<PersistenceContainerEntryTemplate>();
        public List<string> lootTableIds = new List<string>();
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceRewardDefinition
    {
        public string rewardId = string.Empty;
        public List<PersistenceContainerEntryTemplate> entries = new List<PersistenceContainerEntryTemplate>();
    }
}
