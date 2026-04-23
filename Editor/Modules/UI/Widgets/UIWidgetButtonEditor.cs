using UnityEditor;

namespace CoCoFlow.Editor.Modules.UI.Widgets
{
    [CustomEditor(typeof(Runtime.Modules.UI.Widgets.UIWidgetButton))]
    public class UIButtonWidgetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var actionTypeProp = serializedObject.FindProperty("actionType");
            EditorGUILayout.PropertyField(actionTypeProp);

            int currentType = actionTypeProp.enumValueIndex;

            if (currentType == (int)Runtime.Modules.UI.Widgets.UIButtonActionType.OpenPanel)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetPanelAddress"));
            }
            else if (currentType == (int)Runtime.Modules.UI.Widgets.UIButtonActionType.CustomGameLogic)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onCustomClick"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
