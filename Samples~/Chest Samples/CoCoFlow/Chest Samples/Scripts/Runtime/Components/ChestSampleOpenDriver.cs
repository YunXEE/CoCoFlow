using CoCoFlow.Runtime.Gameplay.Item;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.ChestSamples
{
    public sealed class ChestSampleOpenDriver : MonoBehaviour
    {
        [SerializeField] private ItemInputDriver itemInputDriver;
        [SerializeField] private string actorId = "actor.player";

        #region Public API

        [ContextMenu("Open Chest")]
        public void OpenChest()
        {
            ResolveInputDriver()?.RequestOpen(actorId);
        }

        [ContextMenu("Clear Intent")]
        public void ClearIntent()
        {
            ResolveInputDriver()?.ClearIntent();
        }

        #endregion

        #region Internal Logic

        private ItemInputDriver ResolveInputDriver()
        {
            if (itemInputDriver != null) return itemInputDriver;
            itemInputDriver = GetComponent<ItemInputDriver>();
            return itemInputDriver;
        }

        #endregion
    }
}
