using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Animation
{
    [CustomEditor(typeof(AnimAutoOperator))]
    internal sealed class AnimAutoOperatorEditor : AnimOperatorEditorBase
    {
        private SerializedProperty animator;
        private SerializedProperty stateGraphHost;
        private SerializedProperty parameterBindings;
        private SerializedProperty triggerBindings;

        private void OnEnable()
        {
            animator = serializedObject.FindProperty("animator");
            stateGraphHost = serializedObject.FindProperty("stateGraphHost");
            parameterBindings = serializedObject.FindProperty("parameterBindings");
            triggerBindings = serializedObject.FindProperty("triggerBindings");
        }

        public override void OnInspectorGUI()
        {
            var animationOperator = (AnimAutoOperator)target;
            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.HelpBox(
                "Anim Auto Operator only delivers Animator parameters and triggers. SMB signals are staged " +
                "to Event input; it owns neither playback nor root motion.",
                MessageType.Info);
            DrawReferences(animator, stateGraphHost);
            DrawAnimatorSurface(animator.objectReferenceValue as Animator, null);
            DrawMapping(parameterBindings, "Parameter Mappings", 16);
            DrawMapping(triggerBindings, "Trigger Mappings", 8);
            serializedObject.ApplyModifiedProperties();

            DrawValidation(
                animator,
                stateGraphHost,
                null,
                parameterBindings,
                triggerBindings,
                null,
                null,
                null,
                false);
            DrawRebuild(animationOperator);
            DrawDiagnostic(animationOperator.LastDiagnostic);
        }
    }

    internal abstract class AnimOperatorEditorBase : UnityEditor.Editor
    {
        protected static void DrawReferences(
            SerializedProperty animator,
            SerializedProperty stateGraphHost)
        {
            EditorGUILayout.LabelField("Required References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(animator);
            EditorGUILayout.PropertyField(stateGraphHost);
        }

        protected static void DrawAnimatorSurface(
            Animator animator,
            RuntimeAnimatorController expectedController)
        {
            EditorGUILayout.LabelField("Animator Controller Authority", EditorStyles.boldLabel);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an Animator with its RuntimeAnimatorController before authoring mappings.",
                    MessageType.Warning);
                return;
            }

            if (expectedController != null && animator.runtimeAnimatorController != expectedController)
            {
                EditorGUILayout.HelpBox(
                    "Controller must match Animator.runtimeAnimatorController; live rebuild will reject this setup.",
                    MessageType.Error);
            }

            AnimatorController authoringController = ResolveAuthoringController(
                expectedController ?? animator.runtimeAnimatorController);
            if (authoringController == null)
            {
                EditorGUILayout.HelpBox(
                    "Mapping fields remain editable, but a base AnimatorController asset is needed to inspect its layers and state paths.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                "Controller",
                authoringController.name);
            EditorGUILayout.LabelField(
                "Layers",
                DescribeLayers(authoringController));
            EditorGUILayout.LabelField(
                "Parameters",
                DescribeParameters(animator.parameters),
                EditorStyles.wordWrappedMiniLabel);
        }

        protected static void DrawMapping(
            SerializedProperty mapping,
            string label,
            int capacity)
        {
            if (mapping == null)
            {
                EditorGUILayout.HelpBox(label + " array could not be resolved.", MessageType.Error);
                return;
            }

            string capacityLabel = capacity > 0 ? " (fixed capacity: " + capacity + ")" : string.Empty;
            EditorGUILayout.LabelField(label + capacityLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(mapping, new GUIContent("Entries: " + mapping.arraySize), true);
        }

        protected static void DrawValidation(
            SerializedProperty animator,
            SerializedProperty stateGraphHost,
            SerializedProperty controller,
            SerializedProperty parameterBindings,
            SerializedProperty triggerBindings,
            SerializedProperty playbackLayers,
            SerializedProperty stateBindings,
            SerializedProperty modulationBindings,
            bool advanced)
        {
            var errors = new List<string>();
            if (animator == null || animator.objectReferenceValue == null)
            {
                errors.Add("Animator is required.");
            }

            if (stateGraphHost == null || stateGraphHost.objectReferenceValue == null)
            {
                errors.Add("State Graph Host is required.");
            }

            ValidateCapacity(parameterBindings, 16, "Parameter mappings", errors);
            ValidateCapacity(triggerBindings, 8, "Trigger mappings", errors);
            ValidateBindingIds(parameterBindings, "Parameter mapping", errors);
            ValidateBindingIds(triggerBindings, "Trigger mapping", errors);
            if (advanced)
            {
                if (controller == null || controller.objectReferenceValue == null)
                {
                    errors.Add("Controller is required.");
                }

                if (playbackLayers == null || playbackLayers.arraySize == 0)
                {
                    errors.Add("At least one Playable layer mapping is required.");
                }

                ValidateCapacity(playbackLayers, 4, "Playable layer mappings", errors);
                ValidateCapacity(modulationBindings, 8, "Modulation mappings", errors);
                ValidateBindingIds(stateBindings, "State mapping", errors);
                ValidateBindingIds(modulationBindings, "Modulation mapping", errors);
            }

            EditorGUILayout.LabelField("Setup Validation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                errors.Count == 0
                    ? "Capacity and binding-ID checks pass. Live rebuild additionally validates Animator parameter types, Controller state paths, duplicate targets, and Host boundary."
                    : string.Join("\n", errors),
                errors.Count == 0 ? MessageType.Info : MessageType.Error);
        }

        protected static void DrawRebuild(AnimAutoOperator animationOperator)
        {
            DrawRebuildButton(
                "Rebuild Anim Auto Operator Binding",
                "Enter Play Mode to validate and rebuild the live Animator binding.",
                () => animationOperator.TryRebuildBindings(out CoCoDiagnostic diagnostic),
                animationOperator);
        }


        protected static void DrawDiagnostic(CoCoDiagnostic diagnostic)
        {
            if (!diagnostic.IsNone)
            {
                EditorGUILayout.HelpBox(
                    diagnostic.Domain + "/" + diagnostic.Code + ": " + diagnostic.Message,
                    diagnostic.IsError ? MessageType.Error : MessageType.Warning);
            }
        }

        private static void DrawRebuildButton(
            string undoName,
            string editModeMessage,
            System.Func<bool> rebuild,
            Object targetObject)
        {
            EditorGUILayout.LabelField("Runtime Binding", EditorStyles.boldLabel);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(editModeMessage, MessageType.Info);
                return;
            }

            if (GUILayout.Button("Validate and Rebuild Runtime Binding"))
            {
                Undo.RecordObject(targetObject, undoName);
                rebuild();
            }
        }

        private static AnimatorController ResolveAuthoringController(
            RuntimeAnimatorController controller)
        {
            while (controller is AnimatorOverrideController overrideController)
            {
                controller = overrideController.runtimeAnimatorController;
            }

            return controller as AnimatorController;
        }

        private static string DescribeLayers(AnimatorController controller)
        {
            var layers = new List<string>();
            for (int index = 0; index < controller.layers.Length; index++)
            {
                layers.Add(index + ": " + controller.layers[index].name);
            }

            return layers.Count == 0 ? "<none>" : string.Join(" | ", layers);
        }

        private static string DescribeParameters(AnimatorControllerParameter[] parameters)
        {
            var names = new List<string>();
            for (int index = 0; index < parameters.Length; index++)
            {
                names.Add(parameters[index].name + " (" + parameters[index].type + ")");
            }

            return names.Count == 0 ? "<none>" : string.Join(", ", names);
        }

        private static void ValidateCapacity(
            SerializedProperty mapping,
            int capacity,
            string label,
            List<string> errors)
        {
            if (mapping != null && mapping.arraySize > capacity)
            {
                errors.Add(label + " exceed fixed capacity " + capacity + ".");
            }
        }

        private static void ValidateBindingIds(
            SerializedProperty mapping,
            string label,
            List<string> errors)
        {
            if (mapping == null)
            {
                errors.Add(label + " array could not be resolved.");
                return;
            }

            var ids = new HashSet<ulong>();
            for (int index = 0; index < mapping.arraySize; index++)
            {
                SerializedProperty id = mapping.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("bindingId");
                if (id == null || id.ulongValue == 0UL)
                {
                    errors.Add(label + " " + (index + 1) + " requires a non-zero Binding ID.");
                }
                else if (!ids.Add(id.ulongValue))
                {
                    errors.Add(label + " " + (index + 1) + " duplicates a Binding ID.");
                }
            }
        }
    }
}
