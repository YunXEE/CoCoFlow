using System;
using System.Collections.Generic;
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
        private SerializedProperty _intentSources;
        private SerializedProperty _eventAdapters;
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
        private readonly List<MonoBehaviour> _candidateBuffer = new List<MonoBehaviour>();
        private readonly List<MonoBehaviour> _assignedBuffer = new List<MonoBehaviour>();
        private readonly CoCoStateGraphHostDebuggerView _debugger =
            new CoCoStateGraphHostDebuggerView();

        private void OnEnable()
        {
            _asset = serializedObject.FindProperty("stateGraphAsset");
            _driver = serializedObject.FindProperty("driver");
            _autoStart = serializedObject.FindProperty("autoStart");
            _timeScale = serializedObject.FindProperty("timeScale");
            _intentSources = serializedObject.FindProperty("intentSources");
            _eventAdapters = serializedObject.FindProperty("eventAdapters");
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
            var host = (CoCoStateGraphHost)target;
            serializedObject.Update();
            bool configurationReadOnly = Application.isPlaying && host.HasLiveRuntime;
            using (new EditorGUI.DisabledScope(configurationReadOnly))
            {
                EditorGUILayout.PropertyField(_asset);
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(_driver);
                EditorGUILayout.PropertyField(_autoStart);
                EditorGUILayout.PropertyField(_timeScale);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Intent Input Bindings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_intentSources, true);
                EditorGUILayout.PropertyField(_eventAdapters, true);

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
            }

            serializedObject.ApplyModifiedProperties();
            if (configurationReadOnly)
            {
                EditorGUILayout.HelpBox(
                    "Runtime bindings and capacities are frozen for this live Host. Stop it before editing configuration.",
                    MessageType.Info);
            }
            else
            {
                DrawBindingSuggestions(host);
            }

            DrawValidation();
            if (GUILayout.Button("Open StateGraph Debugger Window"))
            {
                CoCoStateGraphDebuggerWindow.Open(host);
            }

            _debugger.Draw(host);
        }

        private void DrawBindingSuggestions(CoCoStateGraphHost host)
        {
            ICoCoStateGraphProjectBindingProvider provider =
                CoCoStateGraphProjectBindings.Provider;
            if (host == null || host.StateGraphAsset == null || provider == null)
            {
                return;
            }

            CoCoStateGraphAssetCompileResult result;
            try
            {
                result = new CoCoStateGraphAssetCompiler().Compile(
                    host.StateGraphAsset,
                    provider.Catalog);
            }
            catch (Exception)
            {
                return;
            }

            if (!result.Succeeded)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Binding Candidates", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Intent Source candidates match an ICoCoIntentFrameSource<> component only. Validate below to confirm the Provider's exact generic slot contract.",
                MessageType.Info);
            DrawIntentSourceSuggestions(host);
            DrawEventAdapterSuggestions(host, result.Graph);
        }

        private void DrawIntentSourceSuggestions(CoCoStateGraphHost host)
        {
            CopyAssigned(_intentSources, _assignedBuffer);
            CoCoStateGraphHostBindingCandidates.FindIntentSources(
                host,
                _assignedBuffer,
                _candidateBuffer);
            for (int index = 0; index < _intentSources.arraySize; index++)
            {
                MonoBehaviour current = _intentSources.GetArrayElementAtIndex(index)
                    .objectReferenceValue as MonoBehaviour;
                if (CoCoStateGraphHostBindingCandidates.IsIntentSource(current))
                {
                    continue;
                }

                DrawCandidateButtons(
                    _intentSources,
                    index,
                    _candidateBuffer,
                    $"Use for Source [{index}]");
            }

            DrawCandidateButtons(
                _intentSources,
                _intentSources.arraySize,
                _candidateBuffer,
                "Add Intent Source");
        }

        private void DrawEventAdapterSuggestions(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph)
        {
            IReadOnlyList<CoCoCompiledEventToIntentDeclaration> declarations =
                graph.IntentRequirements.EventAdapterDeclarations;
            for (int index = 0; index < declarations.Count; index++)
            {
                CoCoCompiledEventToIntentDeclaration declaration = declarations[index];
                MonoBehaviour current = index < _eventAdapters.arraySize
                    ? _eventAdapters.GetArrayElementAtIndex(index).objectReferenceValue as MonoBehaviour
                    : null;
                if (CoCoStateGraphHostBindingCandidates.IsEventAdapter(
                        current,
                        declaration.EventPayloadType,
                        declaration.ProvidedIntentType))
                {
                    continue;
                }

                CopyAssigned(_eventAdapters, _assignedBuffer);
                CoCoStateGraphHostBindingCandidates.FindEventAdapters(
                    host,
                    declaration.EventPayloadType,
                    declaration.ProvidedIntentType,
                    _assignedBuffer,
                    _candidateBuffer);
                DrawCandidateButtons(
                    _eventAdapters,
                    index,
                    _candidateBuffer,
                    $"Use for Adapter [{index}]");
            }
        }

        private void DrawCandidateButtons(
            SerializedProperty array,
            int targetIndex,
            IReadOnlyList<MonoBehaviour> candidates,
            string action)
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                MonoBehaviour candidate = candidates[index];
                if (!GUILayout.Button($"{action}: {candidate.name} ({candidate.GetType().Name})"))
                {
                    continue;
                }

                Undo.RecordObject(target, action);
                serializedObject.Update();
                if (targetIndex >= array.arraySize)
                {
                    array.arraySize = targetIndex + 1;
                }

                array.GetArrayElementAtIndex(targetIndex).objectReferenceValue = candidate;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                GUIUtility.ExitGUI();
            }
        }

        private static void CopyAssigned(
            SerializedProperty array,
            List<MonoBehaviour> destination)
        {
            destination.Clear();
            for (int index = 0; index < array.arraySize; index++)
            {
                destination.Add(
                    array.GetArrayElementAtIndex(index).objectReferenceValue as MonoBehaviour);
            }
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

            if (host.TraceCapacity == 0)
            {
                EditorGUILayout.HelpBox(
                    "Trace is disabled. Committed snapshots remain available in Play Mode; set a positive Trace Capacity before Start to record bounded history.",
                    MessageType.Info);
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
