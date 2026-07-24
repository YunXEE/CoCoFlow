using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Pooling
{
    [CustomEditor(typeof(CoCoPoolHost))]
    internal sealed class CoCoPoolHostEditor : UnityEditor.Editor
    {
        private SerializedProperty contentHost;
        private SerializedProperty diagnosticCapacity;
        private SerializedProperty captureRentalStacks;
        private SerializedProperty captureRentalStacksInRelease;

        private void OnEnable()
        {
            contentHost = serializedObject.FindProperty("contentHost");
            diagnosticCapacity = serializedObject.FindProperty("diagnosticCapacity");
            captureRentalStacks = serializedObject.FindProperty("captureRentalStacks");
            captureRentalStacksInRelease = serializedObject.FindProperty(
                "captureRentalStacksInRelease");
        }

        public override void OnInspectorGUI()
        {
            var host = (CoCoPoolHost)target;
            serializedObject.Update();
            bool configurationReadOnly = host != null && host.IsInitialized;

            using (new EditorGUI.DisabledScope(configurationReadOnly))
            {
                EditorGUILayout.PropertyField(
                    contentHost,
                    new GUIContent(
                        "Content Host",
                        "Explicit source of Content ownership. Pooling never performs an implicit Host lookup."));
                EditorGUILayout.PropertyField(
                    diagnosticCapacity,
                    new GUIContent(
                        "Diagnostic Capacity",
                        "Maximum number of Pool lifecycle records retained by the bounded ledger."));
                EditorGUILayout.PropertyField(
                    captureRentalStacks,
                    new GUIContent(
                        "Capture Rental Stacks",
                        "Capture rental allocation stacks in debug builds."));
                using (new EditorGUI.DisabledScope(
                           captureRentalStacks != null &&
                           !captureRentalStacks.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        captureRentalStacksInRelease,
                        new GUIContent(
                            "Capture in Release",
                            "Also capture rental stacks in non-development builds. " +
                            "This adds allocation and memory cost."));
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (configurationReadOnly)
            {
                EditorGUILayout.HelpBox(
                    "Content composition, ledger capacity, and stack capture are frozen " +
                    "for this initialized Pool Runtime.",
                    MessageType.Info);
            }

            DrawConfigurationValidation();
            DrawRuntimeStatus(host);

            if (GUILayout.Button("Open Pool Monitor"))
            {
                CoCoPoolMonitorWindow.Open(host);
            }
        }

        private void DrawConfigurationValidation()
        {
            if (contentHost == null)
            {
                EditorGUILayout.HelpBox(
                    "The serialized Content Host field could not be resolved.",
                    MessageType.Error);
                return;
            }

            if (contentHost.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign the exact CoCoContentHost whose ContentRuntime owns Pool source leases.",
                    MessageType.Warning);
            }
        }

        private static void DrawRuntimeStatus(CoCoPoolHost host)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);
            if (host == null || !host.IsInitialized || host.Runtime == null)
            {
                EditorGUILayout.HelpBox(
                    "The Host has no initialized Pool Runtime.",
                    MessageType.Info);
            }
            else
            {
                try
                {
                    PoolRuntimeSnapshot snapshot = host.Runtime.CaptureSnapshot();
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            snapshot.IsShuttingDown ? "Shutting Down" : "Initialized");
                        EditorGUILayout.LabelField(
                            "Scopes: " + snapshot.Scopes.Count +
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
