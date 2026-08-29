using System;
using CoCoFlow.Runtime.Modules.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Animation
{
    internal sealed class AnimEventSmbInjector : EditorWindow
    {
        private AnimatorController targetController;
        private bool replaceExisting;
        private int stateCount;
        private int existingSmbCount;

        [MenuItem("CoCoFlow/Animation/Inject Anim Event SMB")]
        private static void ShowWindow()
        {
            var window = GetWindow<AnimEventSmbInjector>("Anim Event SMB Injector");
            window.minSize = new Vector2(420f, 260f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Anim Event SMB Injector", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Adds one AnimEventSmb to each state in the selected Animator Controller, including " +
                "nested state machines. The Controller remains the animation authoring authority; " +
                "this utility does not create StateFlow mappings or marker IDs.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            targetController = (AnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller",
                targetController,
                typeof(AnimatorController),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshPreview();
            }

            using (new EditorGUI.DisabledScope(targetController == null))
            {
                replaceExisting = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Replace existing AnimEventSmb instances",
                        "Existing marker configurations on this Controller will be deleted before a fresh SMB is added."),
                    replaceExisting);
            }

            DrawPreview();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(targetController == null || EditorApplication.isCompiling))
            {
                if (GUILayout.Button(
                        replaceExisting
                            ? "Replace Anim Event SMBs"
                            : "Add Missing Anim Event SMBs",
                        GUILayout.Height(32f)))
                {
                    ExecuteInjection();
                }
            }
        }

        private void DrawPreview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (targetController == null)
                {
                    EditorGUILayout.LabelField("Select an Animator Controller to preview the affected states.");
                    return;
                }

                EditorGUILayout.LabelField("Target", targetController.name);
                EditorGUILayout.LabelField("States", stateCount.ToString());
                EditorGUILayout.LabelField("Existing AnimEventSmb", existingSmbCount.ToString());
                if (replaceExisting && existingSmbCount > 0)
                {
                    EditorGUILayout.HelpBox(
                        "Replace deletes existing SMB marker configurations on this Animator Controller. " +
                        "Use it only when the Controller's SMB authoring should be reset.",
                        MessageType.Warning);
                }
            }
        }

        private void RefreshPreview()
        {
            stateCount = 0;
            existingSmbCount = 0;
            if (targetController == null)
            {
                return;
            }

            foreach (AnimatorControllerLayer layer in targetController.layers)
            {
                CountStateGraph(layer.stateMachine, ref stateCount, ref existingSmbCount);
            }
        }

        private void ExecuteInjection()
        {
            if (targetController == null)
            {
                return;
            }

            if (replaceExisting && existingSmbCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "Replace Anim Event SMBs",
                    "This deletes " + existingSmbCount + " existing AnimEventSmb instances and their marker configurations from '" +
                    targetController.name + "'. Continue?",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            int injectedCount = 0;
            int clearedCount = 0;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(
                replaceExisting
                    ? "Replace Anim Event SMBs"
                    : "Add Anim Event SMBs");
            try
            {
                Undo.RegisterCompleteObjectUndo(
                    targetController,
                    replaceExisting
                        ? "Replace Anim Event SMBs"
                        : "Add Anim Event SMBs");
                foreach (AnimatorControllerLayer layer in targetController.layers)
                {
                    ProcessAnimatorStateGraph(layer.stateMachine, ref injectedCount, ref clearedCount);
                }

                EditorUtility.SetDirty(targetController);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                RefreshPreview();
                EditorUtility.DisplayDialog(
                    "Anim Event SMB Injection Complete",
                    "Controller: " + targetController.name + "\nAdded: " + injectedCount + "\nRemoved: " + clearedCount,
                    "OK");
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Anim Event SMB Injection Failed",
                    exception.Message,
                    "OK");
            }
        }

        private static void CountStateGraph(
            AnimatorStateMachine animatorGraph,
            ref int states,
            ref int existing)
        {
            if (animatorGraph == null)
            {
                return;
            }

            foreach (ChildAnimatorState childState in animatorGraph.states)
            {
                if (childState.state == null)
                {
                    continue;
                }

                states++;
                foreach (StateMachineBehaviour behaviour in childState.state.behaviours)
                {
                    if (behaviour is AnimEventSmb)
                    {
                        existing++;
                    }
                }
            }

            foreach (ChildAnimatorStateMachine childStateMachine in animatorGraph.stateMachines)
            {
                CountStateGraph(childStateMachine.stateMachine, ref states, ref existing);
            }
        }

        private void ProcessAnimatorStateGraph(
            AnimatorStateMachine animatorGraph,
            ref int injectedCount,
            ref int clearedCount)
        {
            foreach (ChildAnimatorState childState in animatorGraph.states)
            {
                ProcessState(childState.state, ref injectedCount, ref clearedCount);
            }

            foreach (ChildAnimatorStateMachine childStateMachine in animatorGraph.stateMachines)
            {
                ProcessAnimatorStateGraph(
                    childStateMachine.stateMachine,
                    ref injectedCount,
                    ref clearedCount);
            }
        }

        private void ProcessState(
            AnimatorState state,
            ref int injectedCount,
            ref int clearedCount)
        {
            if (state == null)
            {
                return;
            }

            if (replaceExisting)
            {
                StateMachineBehaviour[] behaviours = state.behaviours;
                for (int index = behaviours.Length - 1; index >= 0; index--)
                {
                    if (behaviours[index] is AnimEventSmb)
                    {
                        Undo.DestroyObjectImmediate(behaviours[index]);
                        clearedCount++;
                    }
                }
            }

            if (!replaceExisting)
            {
                foreach (StateMachineBehaviour behaviour in state.behaviours)
                {
                    if (behaviour is AnimEventSmb)
                    {
                        return;
                    }
                }
            }

            AnimEventSmb added = state.AddStateMachineBehaviour<AnimEventSmb>();
            Undo.RegisterCreatedObjectUndo(added, "Add Anim Event SMB");
            injectedCount++;
        }
    }
}
