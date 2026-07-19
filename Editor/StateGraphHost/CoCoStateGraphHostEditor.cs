using System;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    [CustomEditor(typeof(CoCoStateGraphHost))]
    internal sealed class CoCoStateGraphHostEditor : UnityEditor.Editor
    {
        private SerializedProperty _asset;
        private SerializedProperty _driver;
        private SerializedProperty _autoStart;
        private SerializedProperty _timeScale;
        private SerializedProperty _operators;
        private SerializedProperty _actorContextBinding;
        private SerializedProperty _contextRestoreBinding;
        private SerializedProperty _temporalHistoryCapacity;
        private SerializedProperty _contextFrameCapacity;
        private SerializedProperty _eventOutboxCapacity;
        private SerializedProperty _traceCapacity;
        private SerializedProperty _eventLaneCapacity;
        private SerializedProperty _eventSourceCapacity;
        private SerializedProperty _eventDedupCapacity;

        private void OnEnable()
        {
            _asset = serializedObject.FindProperty("stateGraphAsset");
            _driver = serializedObject.FindProperty("driver");
            _autoStart = serializedObject.FindProperty("autoStart");
            _timeScale = serializedObject.FindProperty("timeScale");
            _operators = serializedObject.FindProperty("operators");
            _actorContextBinding = serializedObject.FindProperty("actorContextBinding");
            _contextRestoreBinding = serializedObject.FindProperty("contextRestoreBinding");
            _temporalHistoryCapacity = serializedObject.FindProperty("temporalHistoryCapacity");
            _contextFrameCapacity = serializedObject.FindProperty("contextFrameCapacity");
            _eventOutboxCapacity = serializedObject.FindProperty("eventOutboxCapacity");
            _traceCapacity = serializedObject.FindProperty("traceCapacity");
            _eventLaneCapacity = serializedObject.FindProperty("eventLaneCapacity");
            _eventSourceCapacity = serializedObject.FindProperty("eventSourceCapacity");
            _eventDedupCapacity = serializedObject.FindProperty("eventDedupCapacity");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_asset);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_driver);
            EditorGUILayout.PropertyField(_autoStart);
            EditorGUILayout.PropertyField(_timeScale);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Operator Transaction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_operators, true);
            EditorGUILayout.PropertyField(_actorContextBinding);
            EditorGUILayout.PropertyField(_contextFrameCapacity);
            EditorGUILayout.PropertyField(_eventOutboxCapacity);
            EditorGUILayout.PropertyField(_traceCapacity);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Temporal Rewind", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _temporalHistoryCapacity,
                new GUIContent("History Entries"));
            EditorGUILayout.PropertyField(
                _contextRestoreBinding,
                new GUIContent("Restore Binding"));

            using (new EditorGUILayout.FadeGroupScope(1f))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Inbox Capacity", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_eventLaneCapacity, new GUIContent("Per Event Lane"));
                EditorGUILayout.PropertyField(_eventSourceCapacity, new GUIContent("Tracked Sources"));
                EditorGUILayout.PropertyField(_eventDedupCapacity, new GUIContent("Dedup Window"));
            }

            serializedObject.ApplyModifiedProperties();
            DrawValidation();
        }

        private void DrawValidation()
        {
            var host = (CoCoStateGraphHost)target;
            if (host.StateGraphAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "StateGraph Asset is the Host's only required reference.",
                    MessageType.Error);
                return;
            }

            if (!Enum.IsDefined(typeof(CoCoStateGraphDriver), host.Driver) ||
                !IsPositiveFinite(host.TimeScale) ||
                host.TemporalHistoryCapacity < 0 ||
                host.ContextFrameCapacity < 2 ||
                host.EventOutboxCapacity < 0 ||
                host.TraceCapacity < 0)
            {
                EditorGUILayout.HelpBox(
                    "Driver, TimeScale, Temporal capacity, Context capacity, Outbox capacity, and Trace capacity must be valid.",
                    MessageType.Error);
                return;
            }

            ICoCoStateGraphProjectBindingProvider provider =
                CoCoStateGraphProjectBindings.Provider;
            if (provider == null)
            {
                EditorGUILayout.HelpBox(
                    "No project StateGraph binding provider is installed. The Host will remain Created and execute no callbacks.",
                    MessageType.Warning);
                return;
            }

            CoCoStateGraphAssetCompileResult result;
            try
            {
                result = new CoCoStateGraphAssetCompiler().Compile(
                    host.StateGraphAsset,
                    provider.Catalog);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    "StateGraph validation failed: " + exception.Message,
                    MessageType.Error);
                return;
            }

            if (!result.Succeeded)
            {
                for (int index = 0; index < result.Diagnostics.Count; index++)
                {
                    if (result.Diagnostics[index].IsError)
                    {
                        EditorGUILayout.HelpBox(
                            result.Diagnostics[index].Diagnostic.Message,
                            MessageType.Error);
                    }
                }

                return;
            }

            if (!CoCoStateGraphHostBindingValidation.TryValidate(
                    result.Graph,
                    provider,
                    host,
                    host.ContextFrameCapacity,
                    host.EventOutboxCapacity,
                    host.TraceCapacity,
                    host.EventLaneCapacity,
                    host.EventSourceCapacity,
                    host.EventDedupCapacity,
                    out CoCoDiagnostic bindingDiagnostic))
            {
                EditorGUILayout.HelpBox(
                    bindingDiagnostic.Message,
                    MessageType.Error);
                return;
            }

            int eventCount = result.Graph.IntentRequirements.AdapterCount;
            string eventSummary = eventCount == 0
                ? "No Event declarations: this Host will create neither Inbox nor Router."
                : $"{eventCount} Event Adapter declaration(s), Domain {result.Graph.IntentRequirements.EventAdapterDeclarations[0].EventDomainId}.";
            EditorGUILayout.HelpBox(
                $"Compiled Graph and explicit Operator transaction are valid. Driver: {host.Driver}. {eventSummary}",
                MessageType.Info);

            if (Application.isPlaying)
            {
                CoCoTemporalState temporal = host.TemporalState;
                EditorGUILayout.HelpBox(
                    $"Temporal: {temporal.Mode}; History: {temporal.Count}/{temporal.Capacity}; Preview depth: {temporal.PreviewDepth}; Dropped input: {temporal.RewindRestoreDropped}.",
                    MessageType.Info);
            }
        }

        private static bool IsPositiveFinite(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
