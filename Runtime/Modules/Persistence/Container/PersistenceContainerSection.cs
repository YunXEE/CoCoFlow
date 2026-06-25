using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    [Serializable]
    public sealed class PersistenceContainerSection
    {
        public List<PersistenceContainerRecord> records = new List<PersistenceContainerRecord>();

        public PersistenceContainerRecord GetOrAddRecord(
            string containerId,
            string definitionId,
            PersistenceContainerType containerType)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record != null && record.containerId == containerId)
                {
                    if (string.IsNullOrEmpty(record.definitionId))
                    {
                        record.definitionId = definitionId;
                    }

                    record.containerType = containerType;
                    return record;
                }
            }

            var newRecord = new PersistenceContainerRecord
            {
                containerId = containerId,
                definitionId = definitionId,
                containerType = containerType,
                materialized = true
            };
            records.Add(newRecord);
            return newRecord;
        }

        public bool TryGetRecord(string containerId, out PersistenceContainerRecord record)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var current = records[i];
                if (current != null && current.containerId == containerId)
                {
                    record = current;
                    return true;
                }
            }

            record = null;
            return false;
        }

        public void AddOrReplace(PersistenceContainerRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.containerId)) return;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && records[i].containerId == record.containerId)
                {
                    records[i] = record;
                    return;
                }
            }

            records.Add(record);
        }
    }
}
