using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoCoFlow.Runtime.Modules.Persistence
{
    public static class PersistenceSaveLoadSystem
    {
        // 存档槽位配置
        public static int MaxSaveSlots = 3; 
        public static int CurrentSlotIndex = 0; 

        /// <summary>
        /// 判断存档目录
        /// </summary>
        private static string GetSaveDirectory()
        {
#if UNITY_EDITOR
            // 测试文件夹
            string path = Path.Combine(Application.dataPath, "CoCoFlow/Test/Saves");
#else
            // 正式运行：放在系统标准持久化目录
            string path = Application.persistentDataPath;
#endif
            // 确保文件夹存在，没有就建一个
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        /// <summary>
        /// 组合出完整的文件名，显式使用 .json 后缀方便 IDE 解析
        /// </summary>
        private static string GetSaveFilePath(int slotIndex)
        {
            return Path.Combine(GetSaveDirectory(), $"savegame_slot_{slotIndex}.json");
        }

        /// <summary>
        /// 执行存盘操作
        /// </summary>
        public static void SaveGame(int slotIndex = -1)
        {
            // 如果不传参，就保存到当前槽位
            int targetSlot = slotIndex >= 0 ? slotIndex : CurrentSlotIndex;
            
            if (targetSlot >= MaxSaveSlots)
            {
                Debug.LogWarning($"[SaveLoadSystem] 槽位 {targetSlot} 超出上限 {MaxSaveSlots}！");
                return;
            }

            if (PersistenceRuntimeStateManager.CurrentData == null)
            {
                Debug.LogWarning("[SaveLoadSystem] 当前没有可保存的数据！");
                return;
            }

            try
            {
                string jsonString = JsonConvert.SerializeObject(PersistenceRuntimeStateManager.CurrentData, Formatting.Indented);
                string path = GetSaveFilePath(targetSlot);
                
                File.WriteAllText(path, jsonString);
                Debug.Log($"[SaveLoadSystem] 游戏已保存至槽位 {targetSlot}: {path}");
                
#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif
            }
            catch (Exception ex)
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

#if UNITY_EDITOR
            if (slotIndex < 0)
            {
                targetSlot = EditorPrefs.GetInt("CoCo_DebugSaveSlot", 0);
                CurrentSlotIndex = targetSlot; 
            }
#endif

            string path = GetSaveFilePath(targetSlot);
            
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveLoadSystem] 找不到槽位 {targetSlot} 的存档文件！");
                return false;
            }

            try
            {
                string jsonString = File.ReadAllText(path);
                var loadedData = JsonConvert.DeserializeObject<RuntimeState>(jsonString);
                PersistenceRuntimeStateManager.InjectData(loadedData);
                
                Debug.Log($"[SaveLoadSystem] 槽位 {targetSlot} 加载成功！");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveLoadSystem] 加载失败: {ex.Message}");
                return false;
            }
        }
    }
}