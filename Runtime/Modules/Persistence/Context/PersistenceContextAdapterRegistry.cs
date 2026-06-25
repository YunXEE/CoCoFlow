using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    public static class PersistenceContextAdapterRegistry
    {
        private static readonly List<IPersistenceContextAdapter> Adapters = new List<IPersistenceContextAdapter>
        {
            new PersistenceCharacterContextAdapter(),
            new PersistenceItemContextAdapter()
        };

        #region Public API

        public static void Register(IPersistenceContextAdapter adapter)
        {
            if (adapter == null || Adapters.Contains(adapter)) return;
            Adapters.Add(adapter);
        }

        public static bool TryCapture(
            string stableEntityId,
            ICoCoContext context,
            out PersistenceContextRecord record)
        {
            record = null;
            if (context == null || string.IsNullOrEmpty(stableEntityId)) return false;

            for (int i = 0; i < Adapters.Count; i++)
            {
                var adapter = Adapters[i];
                if (adapter.CanCapture(context))
                {
                    record = adapter.Capture(stableEntityId, context);
                    return record != null;
                }
            }

            return false;
        }

        public static bool TryApply(PersistenceContextRecord record, ICoCoContext context)
        {
            if (record == null || context == null) return false;

            for (int i = 0; i < Adapters.Count; i++)
            {
                var adapter = Adapters[i];
                if (adapter.CanApply(record, context))
                {
                    adapter.Apply(record, context);
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
