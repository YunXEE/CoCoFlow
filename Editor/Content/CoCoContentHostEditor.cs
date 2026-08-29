using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Content
{
    [CustomEditor(typeof(CoCoContentHost))]
    internal sealed class CoCoContentHostEditor : UnityEditor.Editor
    {
        private SerializedProperty backendComponents;
        private SerializedProperty diagnosticCapacity;
        private SerializedProperty captureLeaseStacks;
        private SerializedProperty captureLeaseStacksInRelease;

        private void OnEnable()
        {
            backendComponents = serializedObject.FindProperty("backendComponents");
            diagnosticCapacity = serializedObject.FindProperty("diagnosticCapacity");
            captureLeaseStacks = serializedObject.FindProperty("captureLeaseStacks");
            captureLeaseStacksInRelease = serializedObject.FindProperty(
                "captureLeaseStacksInRelease");
        }

        public override void OnInspectorGUI()
        {
            var host = (CoCoContentHost)target;
            serializedObject.Update();
            bool configurationReadOnly = host != null && host.IsInitialized;

            using (new EditorGUI.DisabledScope(configurationReadOnly))
            {
                EditorGUILayout.PropertyField(
                    backendComponents,
                    new GUIContent(
                        "Backend Components",
                        "Optional MonoBehaviours implementing IContentBackend. " +
                        "The Direct backend is always built in."),
                    true);
                EditorGUILayout.PropertyField(
                    diagnosticCapacity,
                    new GUIContent(
                        "Diagnostic Capacity",
                        "Maximum number of lifecycle records retained by the bounded ledger."));
                EditorGUILayout.PropertyField(
                    captureLeaseStacks,
                    new GUIContent(
                        "Capture Lease Stacks",
                        "Capture allocation and release stacks in debug builds."));
                using (new EditorGUI.DisabledScope(
                           captureLeaseStacks != null &&
                           !captureLeaseStacks.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        captureLeaseStacksInRelease,
                        new GUIContent(
                            "Capture in Release",
                            "Also capture lease stacks in non-development builds. " +
                            "This has an allocation and memory cost."));
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (configurationReadOnly)
            {
                EditorGUILayout.HelpBox(
                    "Backend registration, ledger capacity, and stack capture are frozen " +
                    "for this initialized Content Runtime.",
                    MessageType.Info);
            }

            DrawBackendValidation();
            DrawRuntimeStatus(host);

            if (GUILayout.Button("Open Content Monitor"))
            {
                CoCoContentMonitorWindow.Open(host);
            }
        }

        private void DrawBackendValidation()
        {
            if (backendComponents == null || !backendComponents.isArray)
            {
                EditorGUILayout.HelpBox(
                    "The Host backend array could not be resolved.",
                    MessageType.Error);
                return;
            }

            var seen = new Dictionary<ContentBackendId, string>();
            if (ContentBackendId.TryCreate("direct", out ContentBackendId directId))
            {
                seen.Add(directId, "the built-in Direct backend");
            }

            bool reportedAny = false;
            for (int index = 0; index < backendComponents.arraySize; index++)
            {
                SerializedProperty element = backendComponents.GetArrayElementAtIndex(index);
                MonoBehaviour component = element.objectReferenceValue as MonoBehaviour;
                if (component == null)
                {
                    EditorGUILayout.HelpBox(
                        "Backend Components [" + index + "] is empty and will be ignored.",
                        MessageType.Warning);
                    reportedAny = true;
                    continue;
                }

                if (!(component is IContentBackend backend))
                {
                    EditorGUILayout.HelpBox(
                        "Backend Components [" + index + "] ('" + component.name +
                        "') does not implement IContentBackend.",
                        MessageType.Error);
                    reportedAny = true;
                    continue;
                }

                ContentBackendId backendId;
                try
                {
                    backendId = backend.BackendId;
                }
                catch (Exception exception)
                {
                    EditorGUILayout.HelpBox(
                        "Backend Components [" + index + "] threw while reading BackendId: " +
                        exception.Message,
                        MessageType.Error);
                    reportedAny = true;
                    continue;
                }

                if (!backendId.IsValid)
                {
                    EditorGUILayout.HelpBox(
                        "Backend Components [" + index + "] has an invalid BackendId.",
                        MessageType.Error);
                    reportedAny = true;
                    continue;
                }

                if (seen.TryGetValue(backendId, out string prior))
                {
                    EditorGUILayout.HelpBox(
                        "Backend Components [" + index + "] duplicates BackendId '" +
                        backendId.Value + "' already used by " + prior + ".",
                        MessageType.Error);
                    reportedAny = true;
                    continue;
                }

                seen.Add(backendId, "Backend Components [" + index + "]");
            }

            if (!reportedAny)
            {
                EditorGUILayout.HelpBox(
                    backendComponents.arraySize == 0
                        ? "Direct Content is available through the built-in backend."
                        : "All optional backend component registrations are structurally valid.",
                    MessageType.Info);
            }
        }

        private static void DrawRuntimeStatus(CoCoContentHost host)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);
            if (host == null || !host.IsInitialized || host.Runtime == null)
            {
                EditorGUILayout.HelpBox(
                    "The Host has no initialized Content Runtime.",
                    MessageType.Info);
            }
            else
            {
                try
                {
                    ContentRuntimeSnapshot snapshot = host.Runtime.CaptureSnapshot();
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            snapshot.IsShuttingDown ? "Shutting Down" : "Initialized");
                        EditorGUILayout.LabelField(
                            "Entries: " + snapshot.Entries.Count +
                            "    Ledger Records: " + snapshot.Diagnostics.Count);
                    }
                }
                catch (Exception exception)
                {
                    EditorGUILayout.HelpBox(
                        "Runtime snapshot unavailable: " + exception.Message,
                        MessageType.Error);
                }
            }

            DrawDiagnostic(host == null ? CoCoDiagnostic.None : host.LastDiagnostic);
        }

        private static void DrawDiagnostic(CoCoDiagnostic diagnostic)
        {
            if (diagnostic.IsNone)
            {
                return;
            }

            MessageType messageType = diagnostic.IsError
                ? MessageType.Error
                : diagnostic.IsWarning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(
                diagnostic.Domain + "/" + diagnostic.Code + ": " + diagnostic.Message,
                messageType);
        }
    }
}
