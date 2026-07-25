using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    [CustomEditor(typeof(CoCoMapHost))]
    internal sealed class CoCoMapHostEditor :
        UnityEditor.Editor
    {
        private SerializedProperty contentHost;
        private SerializedProperty catalogProviderComponent;
        private SerializedProperty addressableSceneResolverComponent;
        private SerializedProperty bootstrapBindings;
        private SerializedProperty cleanupTimeoutSeconds;
        private string validationMessage = string.Empty;
        private MessageType validationMessageType =
            MessageType.None;

        private void OnEnable()
        {
            contentHost =
                serializedObject.FindProperty("contentHost");
            catalogProviderComponent =
                serializedObject.FindProperty(
                    "catalogProviderComponent");
            addressableSceneResolverComponent =
                serializedObject.FindProperty(
                    "addressableSceneResolverComponent");
            bootstrapBindings =
                serializedObject.FindProperty(
                    "bootstrapBindings");
            cleanupTimeoutSeconds =
                serializedObject.FindProperty(
                    "cleanupTimeoutSeconds");
        }

        public override void OnInspectorGUI()
        {
            var host = (CoCoMapHost)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(
                       host != null &&
                       host.IsInitialized))
            {
                EditorGUILayout.PropertyField(
                    contentHost);
                EditorGUILayout.PropertyField(
                    catalogProviderComponent);
                EditorGUILayout.PropertyField(
                    addressableSceneResolverComponent);
                EditorGUILayout.PropertyField(
                    bootstrapBindings,
                    true);
                EditorGUILayout.PropertyField(
                    cleanupTimeoutSeconds);
            }

            serializedObject.ApplyModifiedProperties();
            if (host != null && host.IsInitialized)
            {
                EditorGUILayout.HelpBox(
                    "Host references, bootstrap Bindings, and cleanup timeout " +
                    "are frozen for this initialized Map Runtime.",
                    MessageType.Info);
            }

            DrawCompilationDiagnostics(host);
            DrawDiagnostic(
                host == null
                    ? CoCoDiagnostic.None
                    : host.LastDiagnostic);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "Open Region Monitor"))
                {
                    CoCoMapMonitorWindow.Open(host);
                }

                using (new EditorGUI.DisabledScope(
                           EditorApplication.isCompiling))
                {
                    if (GUILayout.Button(
                            "Validate Map Build Contracts"))
                    {
                        ValidateBuildContracts();
                    }
                }
            }

            if (!string.IsNullOrEmpty(validationMessage))
            {
                EditorGUILayout.HelpBox(
                    validationMessage,
                    validationMessageType);
            }
        }

        private void ValidateBuildContracts()
        {
            try
            {
                Type[] aotTypes =
                    CoCoMapBuildValidation.ValidateForBuild();
                validationMessage =
                    "Map build contracts are valid. " +
                    aotTypes.Length +
                    " deterministic AOT participant types will be preserved.";
                validationMessageType =
                    MessageType.Info;
            }
            catch (BuildFailedException exception)
            {
                validationMessage = exception.Message;
                validationMessageType =
                    MessageType.Error;
            }
            catch (Exception exception)
            {
                validationMessage =
                    "Map validation threw: " +
                    exception.Message;
                validationMessageType =
                    MessageType.Error;
            }
        }

        private static void DrawCompilationDiagnostics(
            CoCoMapHost host)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Bootstrap Compilation",
                EditorStyles.boldLabel);
            if (host == null ||
                host.CompilationDiagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    host != null && host.IsInitialized
                        ? "Bootstrap Bindings compiled without diagnostics."
                        : "Runtime compilation diagnostics become available after initialization.",
                    MessageType.Info);
                return;
            }

            for (int index = 0;
                 index < host.CompilationDiagnostics.Count;
                 index++)
            {
                RegionCompileDiagnostic item =
                    host.CompilationDiagnostics[index];
                EditorGUILayout.HelpBox(
                    item.Path + ": " +
                    item.Diagnostic.Message,
                    item.Diagnostic.IsError
                        ? MessageType.Error
                        : item.Diagnostic.IsWarning
                            ? MessageType.Warning
                            : MessageType.Info);
            }
        }

        private static void DrawDiagnostic(
            CoCoDiagnostic diagnostic)
        {
            if (diagnostic.IsNone) return;
            EditorGUILayout.HelpBox(
                diagnostic.Domain + "/" +
                diagnostic.Code + ": " +
                diagnostic.Message,
                diagnostic.IsError
                    ? MessageType.Error
                    : diagnostic.IsWarning
                        ? MessageType.Warning
                        : MessageType.Info);
        }
    }
}
