using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    public sealed class PersistenceItemContextAdapter : IPersistenceContextAdapter
    {
        private const string ItemContextType = "CoCoFlow.Runtime.Gameplay.Item.ItemContext";

        public bool CanCapture(ICoCoContext context)
        {
            return context is CoCoEntityContext entityContext &&
                   PersistenceContextReflection.IsOrDerivesFrom(entityContext.GetType(), ItemContextType);
        }

        public bool CanApply(PersistenceContextRecord record, ICoCoContext context)
        {
            return CanCapture(context);
        }

        public PersistenceContextRecord Capture(string stableEntityId, ICoCoContext context)
        {
            var entityContext = (CoCoEntityContext)context;
            var record = new PersistenceContextRecord { stableEntityId = stableEntityId };
            PersistenceContextReflection.CaptureEntityBase(record, entityContext);

            object itemState = PersistenceContextReflection.GetPropertyValue(entityContext, "ItemState");
            if (itemState != null)
            {
                record.stringFacts["item.state"] = itemState.ToString();
            }

            object payload = PersistenceContextReflection.GetPropertyValue(entityContext, "Payload");
            if (payload != null)
            {
                record.stringFacts["item.payload.itemId"] =
                    (string)PersistenceContextReflection.GetFieldValue(payload, "itemId") ?? string.Empty;
                record.intFacts["item.payload.count"] =
                    (int)PersistenceContextReflection.GetFieldValue(payload, "count");
            }

            return record;
        }

        public void Apply(PersistenceContextRecord record, ICoCoContext context)
        {
            var entityContext = (CoCoEntityContext)context;
            PersistenceContextReflection.ApplyEntityBase(record, entityContext);

            if (record.stringFacts.TryGetValue("item.state", out string state))
            {
                switch (state)
                {
                    case "Inactive":
                        PersistenceContextReflection.Invoke(entityContext, "SetInactive");
                        break;
                    case "Available":
                        PersistenceContextReflection.Invoke(entityContext, "SetAvailable");
                        break;
                    case "Locked":
                        PersistenceContextReflection.Invoke(entityContext, "SetLocked");
                        break;
                    case "Opening":
                        PersistenceContextReflection.Invoke(entityContext, "SetOpening");
                        break;
                    case "Opened":
                        PersistenceContextReflection.Invoke(entityContext, "SetOpened");
                        break;
                    case "Consumed":
                        PersistenceContextReflection.Invoke(entityContext, "SetConsumed");
                        break;
                }
            }

            object payload = PersistenceContextReflection.GetPropertyValue(entityContext, "Payload");
            if (payload != null)
            {
                if (record.stringFacts.TryGetValue("item.payload.itemId", out string itemId))
                {
                    PersistenceContextReflection.SetFieldValue(payload, "itemId", itemId);
                }

                if (record.intFacts.TryGetValue("item.payload.count", out int count))
                {
                    PersistenceContextReflection.SetFieldValue(payload, "count", count);
                }
            }
        }
    }
}
