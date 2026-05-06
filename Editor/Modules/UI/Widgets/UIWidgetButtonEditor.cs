using UnityEditor;

namespace CoCoFlow.Editor.Modules.UI.Widgets
{
    [CustomEditor(typeof(Runtime.Modules.UI.Widgets.UIWidgetButton))]
    public class UIWidgetButtonEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var actionTypeProp = serializedObject.FindProperty("_actionType");
            EditorGUILayout.PropertyField(actionTypeProp);

            int currentType = actionTypeProp.enumValueIndex;

            // 根据不同的动作类型切换显示具体的配置项
            if (currentType == (int)Runtime.Modules.UI.Widgets.UIButtonActionType.OpenPanel)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_targetPanelAddress"));
            }
            else if (currentType == (int)Runtime.Modules.UI.Widgets.UIButtonActionType.CustomGameLogic)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_onCustomClick"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
