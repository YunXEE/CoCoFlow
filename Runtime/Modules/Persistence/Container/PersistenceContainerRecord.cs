using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    [Serializable]
    public sealed class PersistenceContainerEntryRecord
    {
        public string entryId = string.Empty;
        public PersistenceContainerEntryType entryType = PersistenceContainerEntryType.Item;
        public string definitionId = string.Empty;
        public int count = 1;
        public int slotIndex = -1;
        public string state = string.Empty;
        public int revision;
        public PersistenceQuestProgressRecord questProgress;
        public Dictionary<string, string> stringValues = new Dictionary<string, string>();
        public Dictionary<string, int> intValues = new Dictionary<string, int>();
        public Dictionary<string, float> floatValues = new Dictionary<string, float>();
        public Dictionary<string, bool> boolValues = new Dictionary<string, bool>();
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class PersistenceContainerRecord
    {
        public string containerId = string.Empty;
        public string definitionId = string.Empty;
        public string templateId = string.Empty;
        public PersistenceContainerType containerType = PersistenceContainerType.ItemStorage;
        public string ownerId = string.Empty;
        public string authorityId = string.Empty;
        public string scope = string.Empty;
        public int schemaVersion = 1;
        public int revision;
        public bool materialized = true;
        public List<string> tags = new List<string>();
        public List<PersistenceContainerEntryRecord> entries = new List<PersistenceContainerEntryRecord>();
    }
}
