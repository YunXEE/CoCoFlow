using System;
using System.IO;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    [CustomEditor(typeof(PersistenceHost))]
    public sealed class PersistenceHostEditor : UnityEditor.Editor
    {
        private static GUIStyle _pathStyle;

        public override void OnInspectorGUI()
        {
            var host = (PersistenceHost)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(host), typeof(MonoScript), false);
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("containerHost"), new GUIContent("Container Host"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSaveSlots"), new GUIContent("Max Save Slots"));
                SerializedProperty slot = serializedObject.FindProperty("initialSlotIndex");
                slot.intValue = EditorGUILayout.IntSlider("Initial Slot", slot.intValue, 0,
                    Mathf.Max(0, serializedObject.FindProperty("maxSaveSlots").intValue - 1));
                SerializedProperty debugMode = serializedObject.FindProperty("debugMode");
                EditorGUILayout.PropertyField(debugMode, new GUIContent("Debug Mode"));
                using (new EditorGUI.DisabledScope(debugMode.boolValue))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("saveDirectory"), new GUIContent("Save Directory"));
                EditorGUILayout.LabelField(debugMode.boolValue
                    ? "Editor only: saves and loads use Assets/DebugSaves. Save Directory is retained for normal mode and builds. Files are created when saving; existing saves are not moved."
                    : "Empty uses the default location. Relative folders go under it; absolute paths use that location directly.", EditorStyles.wordWrappedMiniLabel);
            }
            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle("Active Host", PersistenceHost.ActiveHost == host);
                    EditorGUILayout.IntField("Max Save Slots", PersistenceSaveLoadSystem.MaxSaveSlots);
                    EditorGUILayout.IntField("Current Slot", PersistenceSaveLoadSystem.CurrentSlotIndex);
                    try
                    {
                        EditorGUILayout.ObjectField("Connected Container Host", host.ResolveContainerHost(), typeof(PersistenceContainerHost), true);
                    }
                    catch (InvalidOperationException exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
                }
            }
            EditorGUILayout.Space();
            bool active = Application.isPlaying && PersistenceHost.ActiveHost == host;
            EditorGUILayout.LabelField(active ? "Effective Paths" : "Path Preview", EditorStyles.boldLabel);
            if (!TryGetPaths(host, out string directory, out string file, out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }
            DrawPath("Resolved Directory", directory);
            DrawPath(active ? "Current Slot File" : "Initial Slot File", file);
            DrawFolderButtons(directory);
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        internal static bool TryGetPaths(PersistenceHost host, out string directory, out string file, out string error)
        {
            directory = file = error = null;
            try
            {
                bool active = Application.isPlaying && PersistenceHost.ActiveHost == host;
                directory = Path.GetFullPath(active ? PersistenceFileStore.ResolveSaveDirectory() : host.GetConfiguredSaveDirectory());
                int slot = active ? PersistenceSaveLoadSystem.CurrentSlotIndex : host.ConfiguredInitialSlot;
                file = Path.Combine(directory, PersistenceFileStore.GetSaveFileName(slot));
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        internal static void DrawPath(string label, string value)
        {
            _pathStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            EditorGUILayout.LabelField(label);
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight, _pathStyle.CalcHeight(new GUIContent(value), Mathf.Max(100, EditorGUIUtility.currentViewWidth - 48)));
            EditorGUILayout.SelectableLabel(value, _pathStyle, GUILayout.Height(height));
        }

        internal static void DrawFolderButtons(string directory)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Folder"))
                {
                    Directory.CreateDirectory(directory);
                    EditorUtility.RevealInFinder(directory);
                }
                if (GUILayout.Button("Copy Path")) GUIUtility.systemCopyBuffer = directory;
            }
        }
    }
}
