using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    [Serializable]
    public sealed class PersistenceContextSection
    {
        public List<PersistenceContextRecord> records = new List<PersistenceContextRecord>();

        public bool TryGetRecord(string stableEntityId, out PersistenceContextRecord record)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var current = records[i];
                if (current != null && current.stableEntityId == stableEntityId)
                {
                    record = current;
                    return true;
                }
            }

            record = null;
            return false;
        }

        public void AddOrReplace(PersistenceContextRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.stableEntityId)) return;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && records[i].stableEntityId == record.stableEntityId)
                {
                    records[i] = record;
                    return;
                }
            }

            records.Add(record);
        }
    }
}
