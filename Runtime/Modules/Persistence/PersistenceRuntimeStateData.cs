using System.Collections.Generic;
using UnityEngine; 

namespace CoCoFlow.Runtime.Modules.Persistence
{

    [System.Serializable]
    public struct SerializableVector3
    {
        public float x, y, z;
        
        public SerializableVector3(Vector3 unityVector)
        {
            x = unityVector.x;
            y = unityVector.y;
            z = unityVector.z;
        }
        
        public static implicit operator Vector3(SerializableVector3 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }
        
        public static implicit operator SerializableVector3(Vector3 v)
        {
            return new SerializableVector3(v);
        }
    }

    [System.Serializable]
    public class PlayerSaveData
    {
        public float currentHealth = 100f;
        public float maxHealth = 100f;
        public SerializableVector3 position; 
        public SerializableVector3 rotation; 
        public string lastSavedScene = "StartVillage";
    }

    [System.Serializable]
    public struct EventSaveData
    {
        public int currentPhase;    
        public bool isCompleted;    
        public bool isFailed;       
    }

    [System.Serializable]
    public struct ItemSlotSaveData
    {
        public int slotIndex;       
        public string itemID;       
        public int count;           
    }

    [System.Serializable]
    public class InventorySaveData
    {
        public List<ItemSlotSaveData> slots = new List<ItemSlotSaveData>();
    }

    [System.Serializable]
    public class RuntimeState
    {
        [Header("Drawer 1: Player Core Data")]
        public PlayerSaveData playerData = new PlayerSaveData();

        [Header("Drawer 2: Inventory System")]
        public InventorySaveData inventoryData = new InventorySaveData();

        [Header("Drawer 3: World Events & Quests")]
        public Dictionary<string, EventSaveData> WorldEvents = new Dictionary<string, EventSaveData>();

        [Header("Drawer 4: Static Entities Flags")]
        public Dictionary<string, bool> StaticBoolFlags = new Dictionary<string, bool>();
        public Dictionary<string, int> StaticIntFlags = new Dictionary<string, int>();
    }
}