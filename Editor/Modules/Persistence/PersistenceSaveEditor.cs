using System.IO;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    public sealed class PersistenceSaveEditor : EditorWindow
    {
        private int _slotIndex;

        [MenuItem("CoCoFlow/Persistence/Save Editor")]
        public static void ShowWindow()
        {
            GetWindow<PersistenceSaveEditor>("Persistence Save");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Persistence Save", EditorStyles.boldLabel);
            PersistenceSaveLoadSystem.MaxSaveSlots =
                Mathf.Max(1, EditorGUILayout.IntField("Max Slots", PersistenceSaveLoadSystem.MaxSaveSlots));
            _slotIndex = EditorGUILayout.IntSlider("Slot", _slotIndex, 0, PersistenceSaveLoadSystem.MaxSaveSlots - 1);
            PersistenceSaveLoadSystem.CurrentSlotIndex = _slotIndex;

            EditorGUILayout.Space();
            if (GUILayout.Button("Save Slot"))
            {
                PersistenceSaveLoadSystem.SaveGame(_slotIndex);
                AssetDatabase.Refresh();
            }

            if (GUILayout.Button("Load Slot"))
            {
                PersistenceSaveLoadSystem.LoadGame(_slotIndex);
            }

            if (GUILayout.Button("Open Save Folder"))
            {
                string directory = PersistenceFileStore.GetSaveDirectory();
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                EditorUtility.RevealInFinder(directory);
            }

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Clear Save Files"))
            {
                int deleted = PersistenceFileStore.DeleteSaveFiles();
                Debug.Log($"[Persistence] Deleted {deleted} save files.");
                AssetDatabase.Refresh();
            }

            GUI.backgroundColor = Color.white;
        }
    }
}
