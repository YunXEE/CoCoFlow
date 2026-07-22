using System;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    public static class CoCoStateGraphEditorCatalogProvider
    {
        private static Func<CoCoGraphDescriptorCatalog> provider;

        public static Func<CoCoGraphDescriptorCatalog> Provider
        {
            get => provider;
            set
            {
                if (ReferenceEquals(provider, value))
                {
                    return;
                }

                provider = value;
                CatalogChanged?.Invoke();
            }
        }

        internal static event Action CatalogChanged;
    }

    [CustomEditor(typeof(CoCoStateGraphAsset))]
    internal sealed class CoCoStateGraphAssetEditor : UnityEditor.Editor
    {
        private int selectedLayerIndex;
        private int selectedStateIndex;
        private CoCoStateGraphAssetCompileResult analysisResult;
        private string analysisFailure = string.Empty;
        private string authoringFailure = string.Empty;
        private string locatedPropertyPath = string.Empty;

        public override void OnInspectorGUI()
        {
            var asset = (CoCoStateGraphAsset)target;
            serializedObject.UpdateIfRequiredOrScript();

            EditorGUILayout.LabelField("Schema", asset.SchemaVersion.ToString());
            EditorGUILayout.LabelField("Graph ID", asset.GraphId.IsValid ? asset.GraphId.ToString() : "Invalid");
            EditorGUILayout.Space();

            SerializedProperty eventAdapterDeclarations =
                serializedObject.FindProperty("eventAdapterDeclarations");
            bool authoringReadOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            EditorGUI.BeginDisabledGroup(authoringReadOnly);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                eventAdapterDeclarations,
                new GUIContent("Event Adapter Declarations"),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                analysisResult = null;
                authoringFailure = string.Empty;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layers", asset.Layers.Count.ToString());
            int stateCount = 0;
            int transitionCount = 0;
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                if (layer != null)
                {
                    stateCount += layer.States.Count;
                    transitionCount += layer.Transitions.Count;
                }
            }

            EditorGUILayout.LabelField("States", stateCount.ToString());
            EditorGUILayout.LabelField("Transitions", transitionCount.ToString());
            if (GUILayout.Button("Open State Graph Editor"))
            {
                CoCoStateGraphEditorWindow.Open(asset);
            }

            EditorGUI.BeginDisabledGroup(authoringReadOnly);
            if (GUILayout.Button("Add Layer"))
            {
                CoCoStateGraphAuthoringOperations.AddLayer(asset);
                serializedObject.UpdateIfRequiredOrScript();
                analysisResult = null;
                authoringFailure = string.Empty;
            }
            EditorGUI.EndDisabledGroup();

            if (authoringReadOnly)
            {
                EditorGUILayout.HelpBox(
                    "StateGraph authoring is read-only while entering or running Play Mode.",
                    MessageType.Info);
            }

            DrawAnalysis(asset);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Topology is edited through the State Graph Editor so stable IDs, EditorLayout, and Undo remain atomic. " +
                "Event Adapter declarations remain non-topology authoring data in this Inspector.",
                MessageType.Info);
        }

        private void DrawSelectedLayerOperations(
            CoCoStateGraphAsset asset,
            SerializedProperty layers)
        {
            if (layers == null || !layers.isArray || layers.arraySize == 0)
            {
                return;
            }

            selectedLayerIndex = Mathf.Clamp(selectedLayerIndex, 0, layers.arraySize - 1);
            string[] layerLabels = new string[layers.arraySize];
            for (int index = 0; index < layers.arraySize; index++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                string displayName = layer.FindPropertyRelative("displayName")?.stringValue;
                layerLabels[index] = string.IsNullOrWhiteSpace(displayName)
                    ? $"Layer {index + 1}"
                    : displayName;
            }

            int previousLayerIndex = selectedLayerIndex;
            selectedLayerIndex = EditorGUILayout.Popup("Authoring Layer", selectedLayerIndex, layerLabels);
            if (selectedLayerIndex != previousLayerIndex)
            {
                authoringFailure = string.Empty;
            }

            SerializedProperty selectedLayer = layers.GetArrayElementAtIndex(selectedLayerIndex);
            if (!TryReadLayerId(selectedLayer, out CoCoLayerId layerId))
            {
                EditorGUILayout.HelpBox("The selected Layer has an invalid stable ID.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Selected Layer ID", layerId.ToString());

            if (GUILayout.Button("Duplicate Layer"))
            {
                if (CoCoStateGraphAuthoringOperations.DuplicateLayer(
                        asset,
                        layerId,
                        out _))
                {
                    selectedLayerIndex++;
                    authoringFailure = string.Empty;
                }
                else
                {
                    authoringFailure =
                        "The selected Layer could not be duplicated because its topology IDs or references are invalid.";
                }

                serializedObject.UpdateIfRequiredOrScript();
                analysisResult = null;
                if (string.IsNullOrEmpty(authoringFailure))
                {
                    return;
                }
            }

            if (!string.IsNullOrEmpty(authoringFailure))
            {
                EditorGUILayout.HelpBox(authoringFailure, MessageType.Warning);
            }

            if (GUILayout.Button("Add Root State"))
            {
                CoCoStateGraphAuthoringOperations.AddState(
                    asset,
                    layerId,
                    default,
                    default);
                serializedObject.UpdateIfRequiredOrScript();
                analysisResult = null;
                authoringFailure = string.Empty;
                return;
            }

            SerializedProperty states = selectedLayer.FindPropertyRelative("states");
            if (states == null || !states.isArray || states.arraySize == 0)
            {
                return;
            }

            selectedStateIndex = Mathf.Clamp(selectedStateIndex, 0, states.arraySize - 1);
            string[] stateLabels = new string[states.arraySize];
            for (int index = 0; index < states.arraySize; index++)
            {
                SerializedProperty state = states.GetArrayElementAtIndex(index);
                string displayName = state.FindPropertyRelative("displayName")?.stringValue;
                stateLabels[index] = string.IsNullOrWhiteSpace(displayName)
                    ? $"State {index + 1}"
                    : displayName;
            }

            selectedStateIndex = EditorGUILayout.Popup("Authoring State", selectedStateIndex, stateLabels);
            SerializedProperty selectedState = states.GetArrayElementAtIndex(selectedStateIndex);
            if (!TryReadStateId(selectedState, out CoCoStateId stateId))
            {
                EditorGUILayout.HelpBox("The selected State has an invalid stable ID.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Selected State ID", stateId.ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Child State"))
            {
                CoCoStateGraphAuthoringOperations.AddState(
                    asset,
                    layerId,
                    stateId,
                    default);
                serializedObject.UpdateIfRequiredOrScript();
                analysisResult = null;
                authoringFailure = string.Empty;
            }

            if (GUILayout.Button("Duplicate Subtree"))
            {
                CoCoStateGraphAuthoringOperations.DuplicateStateSubtree(
                    asset,
                    layerId,
                    stateId,
                    out _);
                serializedObject.UpdateIfRequiredOrScript();
                analysisResult = null;
                authoringFailure = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAnalysis(CoCoStateGraphAsset asset)
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Analyze With Registered Catalog"))
            {
                Analyze(asset);
            }

            if (!string.IsNullOrEmpty(analysisFailure))
            {
                EditorGUILayout.HelpBox(analysisFailure, MessageType.Warning);
            }

            if (analysisResult == null)
            {
                return;
            }

            EditorGUILayout.LabelField(
                analysisResult.Succeeded ? "Compilation succeeded" : "Compilation blocked",
                $"{analysisResult.Diagnostics.Count} diagnostic(s)");
            foreach (CoCoGraphDiagnostic graphDiagnostic in analysisResult.Diagnostics)
            {
                MessageType messageType = graphDiagnostic.Diagnostic.Severity == CoCoDiagnosticSeverity.Error
                    ? MessageType.Error
                    : graphDiagnostic.Diagnostic.Severity == CoCoDiagnosticSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.HelpBox(graphDiagnostic.Diagnostic.Message, messageType);
                if (GUILayout.Button("Locate"))
                {
                    locatedPropertyPath = CoCoStateGraphDiagnosticNavigator.TrySelect(
                        asset,
                        graphDiagnostic.Location,
                        out string propertyPath)
                        ? propertyPath
                        : string.Empty;
                }

                EditorGUILayout.EndVertical();
            }

            if (!string.IsNullOrEmpty(locatedPropertyPath))
            {
                EditorGUILayout.LabelField("Located Property");
                EditorGUILayout.SelectableLabel(
                    locatedPropertyPath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void Analyze(CoCoStateGraphAsset asset)
        {
            analysisResult = null;
            analysisFailure = string.Empty;
            locatedPropertyPath = string.Empty;
            Func<CoCoGraphDescriptorCatalog> provider = CoCoStateGraphEditorCatalogProvider.Provider;
            if (provider == null)
            {
                analysisFailure =
                    "No descriptor catalog provider is registered. Project Editor setup must inject a frozen catalog.";
                return;
            }

            try
            {
                CoCoGraphDescriptorCatalog catalog = provider();
                if (catalog == null)
                {
                    analysisFailure = "The registered descriptor catalog provider returned null.";
                    return;
                }

                CoCoDiagnostic[] closureDiagnostics =
                    CoCoStateGraphAuthoringDependencyClosureValidator.Validate(catalog);
                if (closureDiagnostics.Length > 0)
                {
                    var messages = new string[closureDiagnostics.Length];
                    for (int index = 0; index < closureDiagnostics.Length; index++)
                    {
                        messages[index] = closureDiagnostics[index].Message;
                    }

                    analysisFailure = string.Join(Environment.NewLine, messages);
                    return;
                }

                analysisResult = new CoCoStateGraphAssetCompiler().Compile(asset, catalog);
            }
            catch (Exception exception)
            {
                analysisFailure = $"StateGraph analysis failed before compilation: {exception.Message}";
            }
        }

        private static bool TryReadLayerId(SerializedProperty layer, out CoCoLayerId layerId)
        {
            if (TryReadSerializedId(layer?.FindPropertyRelative("layerId"), out ulong high, out ulong low))
            {
                return CoCoLayerId.TryCreate(high, low, out layerId);
            }

            layerId = default;
            return false;
        }

        private static bool TryReadStateId(SerializedProperty state, out CoCoStateId stateId)
        {
            if (TryReadSerializedId(state?.FindPropertyRelative("stateId"), out ulong high, out ulong low))
            {
                return CoCoStateId.TryCreate(high, low, out stateId);
            }

            stateId = default;
            return false;
        }

        private static bool TryReadSerializedId(
            SerializedProperty serializedId,
            out ulong high,
            out ulong low)
        {
            high = 0UL;
            low = 0UL;
            if (serializedId == null)
            {
                return false;
            }

            SerializedProperty highProperty = serializedId.FindPropertyRelative("high");
            SerializedProperty lowProperty = serializedId.FindPropertyRelative("low");
            if (highProperty == null || lowProperty == null)
            {
                return false;
            }

            high = highProperty.ulongValue;
            low = lowProperty.ulongValue;
            return true;
        }
    }
}
