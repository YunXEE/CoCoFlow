using CoCoFlow.Runtime.Modules.Persistence.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    public static class PersistenceSaveLoadSystem
    {
        #region Public API

        public static int MaxSaveSlots = 3;
        public static int CurrentSlotIndex = 0;

        /// <summary>
        /// 执行存盘操作
        /// </summary>
        public static void SaveGame(int slotIndex = -1)
        {
            int targetSlot = slotIndex >= 0 ? slotIndex : CurrentSlotIndex;

            if (!IsValidSlot(targetSlot))
            {
                Debug.LogWarning($"[SaveLoadSystem] 槽位 {targetSlot} 超出上限 {MaxSaveSlots}！");
                return;
            }

            try
            {
                var document = PersistenceSession.Capture(targetSlot);
                PersistenceFileStore.WriteDocument(targetSlot, document);
                Debug.Log($"[SaveLoadSystem] 游戏已保存至槽位 {targetSlot}: {PersistenceFileStore.GetSaveFilePath(targetSlot)}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SaveLoadSystem] 保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行读档操作
        /// </summary>
        public static bool LoadGame(int slotIndex = -1)
        {
            int targetSlot = slotIndex >= 0 ? slotIndex : CurrentSlotIndex;

            if (!IsValidSlot(targetSlot))
            {
                Debug.LogWarning($"[SaveLoadSystem] 槽位 {targetSlot} 超出上限 {MaxSaveSlots}！");
                return false;
            }

            try
            {
                if (!PersistenceFileStore.TryReadDocument(targetSlot, out var document))
                {
                    Debug.LogWarning($"[SaveLoadSystem] 找不到槽位 {targetSlot} 的存档文件！");
                    return false;
                }

                PersistenceSession.SetPendingDocument(document);
                PersistenceSession.ApplyPendingDocument();
                Debug.Log($"[SaveLoadSystem] 槽位 {targetSlot} 加载成功！");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SaveLoadSystem] 加载失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Internal Logic

        private static bool IsValidSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < MaxSaveSlots;
        }

        #endregion
    }
}
