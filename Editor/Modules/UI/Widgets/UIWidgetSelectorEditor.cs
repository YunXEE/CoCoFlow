using UnityEditor;
using CoCoFlow.Runtime.Modules.UI.Widgets;

namespace CoCoFlow.Editor.Modules.UI.Widgets
{
    [CustomEditor(typeof(UIWidgetSelector))]
    public class UIWidgetSelectorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Navigation Controls", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_leftArrowBtn"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_rightArrowBtn"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_optionText"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
