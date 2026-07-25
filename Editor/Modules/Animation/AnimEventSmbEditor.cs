using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Animation;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Animation
{
    [CustomEditor(typeof(AnimEventSmb))]
    internal sealed class AnimEventSmbEditor : UnityEditor.Editor
    {
        private SerializedProperty eventConfigs;
        private ReorderableList eventList;

        private void OnEnable()
        {
            eventConfigs = serializedObject.FindProperty("eventConfigs");
            if (eventConfigs == null)
            {
                return;
            }

            eventList = new ReorderableList(
                serializedObject,
                eventConfigs,
                true,
                true,
                true,
                true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "State Marker Events"),
                elementHeightCallback = _ =>
                    (EditorGUIUtility.singleLineHeight * 3f) + 10f,
                drawElementCallback = DrawElement
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.HelpBox(
                "AnimEventSmb is a StateMachineBehaviour, not an Animator Event callback bridge. " +
                "Enter, Marker, and Exit signals are staged by the owning animation Operator and " +
                "become Event input on a later CoCoTick.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Marker Binding ID is the stable StateFlow mapping. Marker Name is descriptive only; " +
                "each Marker Binding ID must be non-zero and unique in this SMB.",
                MessageType.None);

            if (eventList == null)
            {
                EditorGUILayout.HelpBox(
                    "The serialized eventConfigs array could not be resolved. This SMB cannot be authored safely.",
                    MessageType.Error);
            }
            else
            {
                eventList.DoLayoutList();
                DrawBindingValidation();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = eventList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty bindingId = element.FindPropertyRelative("bindingId");
            SerializedProperty eventName = element.FindPropertyRelative("eventName");
            SerializedProperty triggerTime = element.FindPropertyRelative("triggerTime");
            float line = EditorGUIUtility.singleLineHeight;
            rect.y += 2f;

            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, line),
                bindingId,
                new GUIContent("Marker Binding ID"));
            rect.y += line + 2f;
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, line),
                eventName,
                new GUIContent("Marker Name"));
            rect.y += line + 2f;
            EditorGUI.Slider(
                new Rect(rect.x, rect.y, rect.width, line),
                triggerTime,
                0f,
                1f,
                new GUIContent("Normalized Time"));
        }

        private void DrawBindingValidation()
        {
            var ids = new HashSet<ulong>();
            for (int index = 0; index < eventConfigs.arraySize; index++)
            {
                SerializedProperty bindingId = eventConfigs
                    .GetArrayElementAtIndex(index)
                    .FindPropertyRelative("bindingId");
                if (bindingId == null || bindingId.ulongValue == 0UL)
                {
                    EditorGUILayout.HelpBox(
                        "Marker " + (index + 1) + " requires a non-zero Marker Binding ID.",
                        MessageType.Error);
                    continue;
                }

                if (!ids.Add(bindingId.ulongValue))
                {
                    EditorGUILayout.HelpBox(
                        "Marker " + (index + 1) + " duplicates a Marker Binding ID in this SMB.",
                        MessageType.Error);
                }
            }
        }
    }
}
