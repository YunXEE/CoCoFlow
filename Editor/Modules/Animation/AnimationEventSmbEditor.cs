using UnityEngine;
using UnityEditor;
using UnityEditorInternal; // 引入 ReorderableList
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Editor.Modules.Animation
{
    [CustomEditor(typeof(AnimationEventSmb))]
    public class AnimationEventSmbEditor : UnityEditor.Editor
    {
        private SerializedProperty _eventsProp;
        private ReorderableList _reorderableList;

        private void OnEnable()
        {
            _eventsProp = serializedObject.FindProperty("events");

            // 初始化 ReorderableList
            _reorderableList = new ReorderableList(serializedObject, _eventsProp, true, true, true, true)
            {
                // 1. 绘制表头
                drawHeaderCallback = (Rect rect) =>
                {
                    EditorGUI.LabelField(rect, "动画帧事件配置表 (Animation Events)");
                },

                // 2. 绘制列表里的每一个元素
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    var element = _reorderableList.serializedProperty.GetArrayElementAtIndex(index);
                    var eventNameProp = element.FindPropertyRelative("eventName");
                    var triggerTimeProp = element.FindPropertyRelative("triggerTime");

                    rect.y += 2;

                    // 划分每一行的显示区域 (比如 40% 给名字，60% 给时间滑块)
                    float nameWidth = rect.width * 0.4f;
                    float timeWidth = rect.width * 0.6f - 10f; // 减 10 留点空隙

                    // 绘制事件名输入框
                    EditorGUI.PropertyField(
                        new Rect(rect.x, rect.y, nameWidth, EditorGUIUtility.singleLineHeight),
                        eventNameProp, GUIContent.none);

                    // 绘制时间滑块 (0.0 到 1.0)
                    EditorGUI.PropertyField(
                        new Rect(rect.x + nameWidth + 10, rect.y, timeWidth, EditorGUIUtility.singleLineHeight),
                        triggerTimeProp, GUIContent.none);
                },

                // 3. 动态调整元素高度
                elementHeightCallback = (int index) =>
                {
                    return EditorGUIUtility.singleLineHeight + 4;
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("提示：TriggerTime 表示动画播放的百分比 (0=开始, 1=结束)。", MessageType.Info);
            EditorGUILayout.Space(5);

            // 绘制列表
            _reorderableList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
