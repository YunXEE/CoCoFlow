using System;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    [CustomEditor(typeof(PersistenceContainer))]
    public sealed class PersistenceContainerEditor : UnityEditor.Editor
    {
        private PersistenceContainerTypePicker.Entry[] _typeEntries;

        private void OnEnable()
        {
            _typeEntries = PersistenceContainerTypePicker.CreateEntries(PersistenceContainerPlayerMetadata.GetPlayerDataTypes());
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty id = serializedObject.FindProperty("id");
            SerializedProperty typeId = serializedObject.FindProperty("dataTypeId");
            SerializedProperty initial = serializedObject.FindProperty("initialDataJson");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(id, new GUIContent("ID"));
                DrawTypePicker(typeId);
                try
                {
                    Type type = PersistenceDataTypeRegistry.Resolve(typeId.stringValue);
                    object data = PersistenceContainerCodec.ParseInitialData(type, initial.stringValue);
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Initial Data", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    data = PersistenceContainerDataDrawer.Draw(type, data, type.Name);
                    if (EditorGUI.EndChangeCheck())
                        initial.stringValue = PersistenceContainerCodec.FormatInitialData(type, data);
                }
                catch (Exception exception)
                {
                    EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                }
                if (string.IsNullOrWhiteSpace(id.stringValue))
                    EditorGUILayout.HelpBox("A nonempty ID is required. Matching type and ID intentionally share one row.", MessageType.Warning);
            }
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying)
                EditorGUILayout.LabelField("Binding", ((PersistenceContainer)target).IsBound ? "Bound" : "Unbound");
        }

        private void DrawTypePicker(SerializedProperty typeId)
        {
            Rect position = EditorGUILayout.GetControlRect();
            var label = new GUIContent("Data Type");
            EditorGUI.BeginProperty(position, label, typeId);
            position = EditorGUI.PrefixLabel(position, label);
            GUIContent content = PersistenceContainerTypePicker.GetButtonContent(_typeEntries, typeId.stringValue);
            if (EditorGUI.DropdownButton(position, content, FocusType.Keyboard))
                PopupWindow.Show(position, new PersistenceContainerTypePicker(_typeEntries, typeId.stringValue, OnTypeSelected, position.width));
            EditorGUI.EndProperty();
        }

        private void OnTypeSelected(string typeId)
        {
            if (this == null || target == null || Application.isPlaying) return;
            ApplyTypeSelection(serializedObject, typeId);
            Repaint();
        }

        internal static void ApplyTypeSelection(SerializedObject data, string typeId)
        {
            data.Update();
            SerializedProperty selected = data.FindProperty("dataTypeId");
            if (selected.stringValue == typeId) return;
            selected.stringValue = typeId;
            data.FindProperty("initialDataJson").stringValue = "{}";
            data.ApplyModifiedProperties();
        }
    }
}
