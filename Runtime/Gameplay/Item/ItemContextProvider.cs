using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Item
{
    public class ItemContextProvider :
        MonoBehaviour,
        ICoCoContextProvider<ItemContext>,
        ICoCoIntentSource<ItemIntent>
    {
        [SerializeField] private ItemContext context = new ItemContext();

        public ItemContext Context => context;
        public ItemIntent Intent => context.Intent;

        public void RequestOpen(string actorId = "")
        {
            context.Intent.openRequested = true;
            context.Intent.actorId = actorId;
        }

        public void RequestUnlock(string actorId = "")
        {
            context.Intent.unlockRequested = true;
            context.Intent.actorId = actorId;
        }

        public void RequestUse(string actorId = "")
        {
            context.Intent.useRequested = true;
            context.Intent.actorId = actorId;
        }

        public void ClearIntent()
        {
            context.Intent.Clear();
        }
    }
}
