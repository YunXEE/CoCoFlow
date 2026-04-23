using CoCoFlow.Runtime.Modules.UI;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.UI
{
    [CustomEditor(typeof(UIWidgetContainer))]
    public class UIWidgetContainerEditor : UnityEditor.Editor
    {
        private SerializedProperty modeProp;
        private SerializedProperty placeholderCountProp;
        private SerializedProperty managedItemsProp;
        private SerializedProperty layoutTypeProp;
        private SerializedProperty anchorProp;
        private SerializedProperty spacingProp;
        private SerializedProperty cellSizeProp;
        private SerializedProperty gridColumnsProp;
        private SerializedProperty showPreviewProp;

        private void OnEnable()
        {
            modeProp = serializedObject.FindProperty("mode");
            placeholderCountProp = serializedObject.FindProperty("placeholderCount");
            managedItemsProp = serializedObject.FindProperty("managedItems");
            layoutTypeProp = serializedObject.FindProperty("layoutType");
            anchorProp = serializedObject.FindProperty("anchor");
            spacingProp = serializedObject.FindProperty("spacing");
            cellSizeProp = serializedObject.FindProperty("cellSize");
            gridColumnsProp = serializedObject.FindProperty("gridColumns");
            showPreviewProp = serializedObject.FindProperty("showPreview");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            UIWidgetContainer container = (UIWidgetContainer)target;

            EditorGUILayout.Space();
            GUILayout.Label("数据模式 (Data Mode)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(modeProp);

            // 核心分流：根据模式显示不同的数据源配置
            if (modeProp.enumValueIndex == (int)WidgetContainerMode.Static)
            {
                EditorGUILayout.HelpBox("静态模式：请将需要参与排版的 Widget 手动拖入下方列表。未在列表中的子物体（如背景、特效）将被忽略。", MessageType.Info);
                EditorGUILayout.PropertyField(managedItemsProp, new GUIContent("Managed Widgets (受控元素列表)"), true);
            }
            else
            {
                EditorGUILayout.HelpBox("动态模式：通过代码在运行时实例化 Widget。下方数字仅用于编辑器预览排版坑位。", MessageType.Info);
                EditorGUILayout.PropertyField(placeholderCountProp, new GUIContent("Placeholder Count (预设坑位数量)"));
            }

            EditorGUILayout.Space();
            GUILayout.Label("排版规则 (Layout Rules)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(layoutTypeProp);
            EditorGUILayout.PropertyField(anchorProp, new GUIContent("Alignment (阵列锚点)"));

            WidgetContainerLayout currentLayout = (WidgetContainerLayout)layoutTypeProp.enumValueIndex;

            if (currentLayout == WidgetContainerLayout.Grid)
            {
                EditorGUILayout.PropertyField(gridColumnsProp, new GUIContent("Columns (列数)"));
                EditorGUILayout.PropertyField(spacingProp, new GUIContent("Spacing (X/Y 间距)"));
            }
            else if (currentLayout == WidgetContainerLayout.Column)
            {
                EditorGUILayout.PropertyField(spacingProp, new GUIContent("Vertical Spacing (垂直间距)"));
            }
            else if (currentLayout == WidgetContainerLayout.Row)
            {
                EditorGUILayout.PropertyField(spacingProp, new GUIContent("Horizontal Spacing (水平间距)"));
            }

            EditorGUILayout.Space();
            GUILayout.Label("坑位定义 (Slot Definition)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(cellSizeProp, new GUIContent("Slot Size (坑位宽高)"));

            EditorGUILayout.Space();
            GUILayout.Label("编辑器预览 (Editor Preview)", EditorStyles.boldLabel);

            GUI.color = showPreviewProp.boolValue ? Color.green : Color.gray;
            if (GUILayout.Button(showPreviewProp.boolValue ? "Preview: ON" : "Preview: OFF", GUILayout.Height(30)))
            {
                showPreviewProp.boolValue = !showPreviewProp.boolValue;
                SceneView.RepaintAll();
            }
            GUI.color = Color.white;

            if (GUILayout.Button("Force Apply Layout Now (手动吸附)", GUILayout.Height(30)))
            {
                container.ApplyLayout();
                SceneView.RepaintAll();
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                // 如果在 Inspector 中增删了数组元素或修改了参数，立即重新排版
                if (!Application.isPlaying && container.mode == WidgetContainerMode.Static)
                {
                    container.ApplyLayout();
                }
            }
        }
    }
}
