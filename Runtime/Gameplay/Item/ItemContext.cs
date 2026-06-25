using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.Gameplay.Item
{
    public enum ItemSemanticState
    {
        Inactive,
        Available,
        Locked,
        Opening,
        Opened,
        Consumed
    }

    [Serializable]
    public class ItemIntent : ICoCoIntent
    {
        public bool openRequested;
        public bool unlockRequested;
        public bool useRequested;
        public string actorId;

        public void CopyFrom(ItemIntent source)
        {
            if (source == null)
            {
                Clear();
                return;
            }

            openRequested = source.openRequested;
            unlockRequested = source.unlockRequested;
            useRequested = source.useRequested;
            actorId = source.actorId;
        }

        public void Clear()
        {
            openRequested = false;
            unlockRequested = false;
            useRequested = false;
            actorId = string.Empty;
        }
    }

    [Serializable]
    public class ItemInventoryPayload
    {
        [FormerlySerializedAs("ItemId")] public string itemId;
        [FormerlySerializedAs("Count")] public int count = 1;

        public bool HasPayload => !string.IsNullOrEmpty(itemId) && count > 0;
    }

    [Serializable]
    public class ItemContext : CoCoEntityContext
    {
        [SerializeField] private ItemIntent intent = new ItemIntent();
        [SerializeField] private ItemSemanticState itemState = ItemSemanticState.Inactive;
        [SerializeField] private ItemInventoryPayload payload = new ItemInventoryPayload();

        public ItemIntent Intent => intent;
        public ItemSemanticState ItemState => itemState;
        public ItemInventoryPayload Payload => payload;

        public void SetInactive()
        {
            itemState = ItemSemanticState.Inactive;
            SemanticStateId = (int)itemState;
            Lifecycle.TryTransitionTo(CoCoLifecycleState.Disabled);
        }

        public void SetAvailable()
        {
            itemState = ItemSemanticState.Available;
            SemanticStateId = (int)itemState;
            Lifecycle.TransitionTo(CoCoLifecycleState.Active);
        }

        public void SetLocked()
        {
            itemState = ItemSemanticState.Locked;
            SemanticStateId = (int)itemState;
            Lifecycle.TransitionTo(CoCoLifecycleState.Active);
        }

        public void SetOpening()
        {
            itemState = ItemSemanticState.Opening;
            SemanticStateId = (int)itemState;
            ActionStateId = (int)itemState;
            Lifecycle.TransitionTo(CoCoLifecycleState.Active);
        }

        public void SetOpened()
        {
            itemState = ItemSemanticState.Opened;
            SemanticStateId = (int)itemState;
            ActionStateId = 0;
            Lifecycle.TransitionTo(CoCoLifecycleState.Active);
        }

        public void SetConsumed()
        {
            itemState = ItemSemanticState.Consumed;
            SemanticStateId = (int)itemState;
            ActionStateId = 0;
            Lifecycle.TransitionTo(CoCoLifecycleState.Consumed);
        }
    }

    public struct ItemOpenedEvent
    {
        public ItemContext Context;
        public string ItemId;
        public int EventSequence;
    }

    public struct ItemConsumedEvent
    {
        public ItemContext Context;
        public string ItemId;
        public int EventSequence;
    }
}
