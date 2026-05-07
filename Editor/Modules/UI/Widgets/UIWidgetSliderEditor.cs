using UnityEditor;
using CoCoFlow.Runtime.Modules.UI.Widgets;

namespace CoCoFlow.Editor.Modules.UI.Widgets
{
    [CustomEditor(typeof(UIWidgetSlider))]
    public class UIWidgetSliderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_slider"));

            var valueTextProp = serializedObject.FindProperty("_valueText");
            EditorGUILayout.PropertyField(valueTextProp);

            // 仅在分配了文本组件时显示格式化配置
            if (valueTextProp.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_textFormat"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_displayAsPercentage"));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
