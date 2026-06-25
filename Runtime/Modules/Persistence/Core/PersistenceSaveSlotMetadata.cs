using System;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    [Serializable]
    public sealed class PersistenceSaveSlotMetadata
    {
        public int slotIndex;
        public string displayName = string.Empty;
        public string createdUtc = string.Empty;
        public string updatedUtc = string.Empty;
        public string unityVersion = string.Empty;

        public static PersistenceSaveSlotMetadata Create(int slotIndex)
        {
            string now = DateTime.UtcNow.ToString("O");
            return new PersistenceSaveSlotMetadata
            {
                slotIndex = slotIndex,
                displayName = $"Slot {slotIndex}",
                createdUtc = now,
                updatedUtc = now,
                unityVersion = UnityEngine.Application.unityVersion
            };
        }

        public void Touch()
        {
            if (string.IsNullOrEmpty(createdUtc))
            {
                createdUtc = DateTime.UtcNow.ToString("O");
            }

            updatedUtc = DateTime.UtcNow.ToString("O");
        }
    }
}
