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
            PersistenceHost host = PersistenceHost.ActiveHost;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Configuration Host", host, typeof(PersistenceHost), true);
            using (new EditorGUI.DisabledScope(host != null))
            {
                int maxSlots = Mathf.Max(1, EditorGUILayout.IntField("Max Slots", PersistenceSaveLoadSystem.MaxSaveSlots));
                if (host == null) PersistenceSaveLoadSystem.MaxSaveSlots = maxSlots;
            }
            _slotIndex = EditorGUILayout.IntSlider("Slot", PersistenceSaveLoadSystem.CurrentSlotIndex, 0, PersistenceSaveLoadSystem.MaxSaveSlots - 1);
            PersistenceSaveLoadSystem.CurrentSlotIndex = _slotIndex;
            string directory = Path.GetFullPath(PersistenceFileStore.ResolveSaveDirectory());
            PersistenceHostEditor.DrawPath("Resolved Directory", directory);
            PersistenceHostEditor.DrawPath("Current Slot File", Path.Combine(directory, PersistenceFileStore.GetSaveFileName(_slotIndex)));

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

            PersistenceHostEditor.DrawFolderButtons(directory);

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
