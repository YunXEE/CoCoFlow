
namespace CoCoFlow.Runtime.Modules.Persistence
{
    public static class PersistenceRuntimeStateManager
    {
        public static RuntimeState CurrentData { get; private set; }

        public static void InitializeNewGame()
        {
            CurrentData = new RuntimeState();
        }

        public static void InjectData(RuntimeState loadedData)
        {
            if (loadedData != null)
            {
                CurrentData = loadedData;
            }
        }

        #region Drawer 4: 静态场景互动物 API (原 Bool/Int Flags)

        public static void SetStaticBool(string key, bool value)
        {
            if (CurrentData == null) return;
            CurrentData.StaticBoolFlags[key] = value;
        }

        public static bool GetStaticBool(string key, bool defaultValue = false)
        {
            if (CurrentData == null) return defaultValue;
            if (CurrentData.StaticBoolFlags.TryGetValue(key, out bool value))
            {
                return value;
            }
            return defaultValue;
        }

        public static void SetStaticInt(string key, int value)
        {
            if (CurrentData == null) return;
            CurrentData.StaticIntFlags[key] = value;
        }

        public static int GetStaticInt(string key, int defaultValue = 0)
        {
            if (CurrentData == null) return defaultValue;
            if (CurrentData.StaticIntFlags.TryGetValue(key, out int value))
            {
                return value;
            }
            return defaultValue;
        }

        #endregion

        #region Drawer 3: 世界事件与任务进度 API

        /// <summary>
        /// 获取事件状态，如果字典里没有，会自动返回一个全部为默认值（阶段0，未完成，未失败）的新结构体
        /// </summary>
        public static EventSaveData GetEvent(string eventID)
        {
            if (CurrentData != null && CurrentData.WorldEvents.TryGetValue(eventID, out var evtData))
            {
                return evtData;
            }
            return new EventSaveData { currentPhase = 0, isCompleted = false, isFailed = false };
        }

        /// <summary>
        /// 推进事件阶段
        /// </summary>
        public static void SetEventPhase(string eventID, int phase)
        {
            if (CurrentData == null) return;
            var evtData = GetEvent(eventID);
            evtData.currentPhase = phase;
            CurrentData.WorldEvents[eventID] = evtData; // 【硬核细节】：因为是 Struct，必须重新赋值回字典
        }

        /// <summary>
        /// 标记事件完成
        /// </summary>
        public static void CompleteEvent(string eventID)
        {
            if (CurrentData == null) return;
            var evtData = GetEvent(eventID);
            evtData.isCompleted = true;
            CurrentData.WorldEvents[eventID] = evtData;
        }

        /// <summary>
        /// 标记事件失败
        /// </summary>
        public static void FailEvent(string eventID)
        {
            if (CurrentData == null) return;
            var evtData = GetEvent(eventID);
            evtData.isFailed = true;
            CurrentData.WorldEvents[eventID] = evtData;
        }

        #endregion

        #region Drawer 1 & 2: 玩家与物品栏的快捷通道

        // 让外部系统（如玩家血量脚本）直接拿到引用，进行 CurrentHealth = 80 这样的直接修改
        public static PlayerSaveData PlayerData => CurrentData?.playerData;
        
        // 让 UI 物品栏系统直接拿到格子列表进行增删改查
        public static InventorySaveData InventoryData => CurrentData?.inventoryData;

        #endregion
    }
}