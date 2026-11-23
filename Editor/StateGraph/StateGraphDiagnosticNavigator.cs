using System;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEditor;

namespace CoCoFlow.Editor.StateGraph
{
    public static class StateGraphDiagnosticNavigator
    {
        public static bool TryFindProperty(
            SerializedObject serializedAsset,
            GraphDiagnosticLocation location,
            out SerializedProperty property)
        {
            if (serializedAsset == null)
            {
                throw new ArgumentNullException(nameof(serializedAsset));
            }

            serializedAsset.UpdateIfRequiredOrScript();
            property = null;
            if (location.GraphId.IsValid &&
                !Matches(
                    serializedAsset.FindProperty("graphId"),
                    location.GraphId.High,
                    location.GraphId.Low))
            {
                return false;
            }

            if (location.ElementKind == GraphElementKind.Graph)
            {
                property = FindGraphProperty(serializedAsset, location.Field);
                return property != null;
            }

            if (location.ElementKind == GraphElementKind.Manifest)
            {
                property = serializedAsset.FindProperty("layers");
                return property != null;
            }

            if (location.ElementKind == GraphElementKind.EventAdapterDeclaration)
            {
                SerializedProperty declarations =
                    serializedAsset.FindProperty("eventAdapterDeclarations");
                if (declarations == null ||
                    !declarations.isArray ||
                    location.EventAdapterDeclarationIndex < 0 ||
                    location.EventAdapterDeclarationIndex >= declarations.arraySize)
                {
                    return false;
                }

                SerializedProperty declaration = declarations.GetArrayElementAtIndex(
                    location.EventAdapterDeclarationIndex);
                switch (location.Field)
                {
                    case GraphField.Identifier:
                    case GraphField.EventAdapterDeclarations:
                        property = declaration;
                        break;
                    default:
                        property = declaration;
                        break;
                }

                return property != null;
            }

            SerializedProperty layers = serializedAsset.FindProperty("layers");
            SerializedProperty layer = FindLayer(layers, location);
            if (layer == null)
            {
                return false;
            }

            if (location.ElementKind == GraphElementKind.Layer)
            {
                property = FindLayerProperty(layer, location.Field);
                return property != null;
            }

            if (location.ElementKind == GraphElementKind.State)
            {
                SerializedProperty state = FindState(layer.FindPropertyRelative("states"), location);
                if (location.StateIndex == -1)
                {
                    SerializedProperty anyState = layer.FindPropertyRelative("anyState");
                    if (anyState != null && anyState.managedReferenceValue != null &&
                        Matches(anyState.FindPropertyRelative("stateId"),
                            location.StateId.High, location.StateId.Low))
                    {
                        state = anyState;
                    }
                }
                if (state == null)
                {
                    return false;
                }

                property = FindStateProperty(state, location.Field);
                return property != null;
            }

            SerializedProperty transition = FindTransition(
                layer.FindPropertyRelative("transitions"),
                location);
            if (transition == null)
            {
                return false;
            }

            if (location.ElementKind == GraphElementKind.Transition)
            {
                property = FindTransitionProperty(transition, location.Field);
                return property != null;
            }

            if (location.ElementKind == GraphElementKind.Condition)
            {
                SerializedProperty conditions = transition.FindPropertyRelative("conditions");
                if (conditions == null ||
                    location.ConditionIndex < 0 ||
                    location.ConditionIndex >= conditions.arraySize)
                {
                    return false;
                }

                SerializedProperty condition = conditions.GetArrayElementAtIndex(location.ConditionIndex);
                property = location.Field == GraphField.Config
                    ? condition.FindPropertyRelative("config")
                    : condition.FindPropertyRelative("conditionDescriptorId");
                return property != null;
            }

            return false;
        }

        public static bool TrySelect(
            StateGraphAsset asset,
            GraphDiagnosticLocation location,
            out string propertyPath)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var serializedAsset = new SerializedObject(asset);
            if (!TryFindProperty(serializedAsset, location, out SerializedProperty property))
            {
                propertyPath = string.Empty;
                return false;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            propertyPath = property.propertyPath;
            return true;
        }

        private static SerializedProperty FindGraphProperty(
            SerializedObject serializedAsset,
            GraphField field)
        {
            switch (field)
            {
                case GraphField.SchemaVersion:
                    return serializedAsset.FindProperty("schemaVersion");
                case GraphField.ContentFingerprint:
                    return serializedAsset.FindProperty("layers");
                case GraphField.Identifier:
                    return serializedAsset.FindProperty("graphId");
                case GraphField.AssetGuidStamp:
                    return serializedAsset.FindProperty("assetGuidStamp");
                default:
                    return serializedAsset.FindProperty("layers");
            }
        }

        private static SerializedProperty FindLayerProperty(SerializedProperty layer, GraphField field)
        {
            switch (field)
            {
                case GraphField.Identifier:
                    return layer.FindPropertyRelative("layerId");
                case GraphField.InitialState:
                    return layer.FindPropertyRelative("initialStateId");
                default:
                    return layer;
            }
        }

        private static SerializedProperty FindStateProperty(SerializedProperty state, GraphField field)
        {
            switch (field)
            {
                case GraphField.Identifier:
                    return state.FindPropertyRelative("stateId");
                case GraphField.ParentState:
                    return state.FindPropertyRelative("parentStateId");
                case GraphField.InitialChildState:
                    return state.FindPropertyRelative("initialChildStateId");
                case GraphField.Descriptor:
                    return state.FindPropertyRelative("stateDescriptorId");
                case GraphField.Config:
                    return state.FindPropertyRelative("config");
                default:
                    return state;
            }
        }

        private static SerializedProperty FindTransitionProperty(
            SerializedProperty transition,
            GraphField field)
        {
            switch (field)
            {
                case GraphField.Identifier:
                    return transition.FindPropertyRelative("transitionId");
                case GraphField.SourceState:
                    return transition.FindPropertyRelative("sourceStateId");
                case GraphField.TargetState:
                    return transition.FindPropertyRelative("targetStateId");
                case GraphField.Priority:
                    return transition.FindPropertyRelative("priority");
                case GraphField.Window:
                    return transition.FindPropertyRelative("windowMode");
                case GraphField.Conditions:
                case GraphField.Config:
                case GraphField.Descriptor:
                    return transition.FindPropertyRelative("conditions");
                default:
                    return transition;
            }
        }

        private static SerializedProperty FindLayer(
            SerializedProperty layers,
            GraphDiagnosticLocation location)
        {
            if (layers == null || !layers.isArray)
            {
                return null;
            }

            return FindByStableIdOrIndex(
                layers,
                "layerId",
                location.LayerId.IsValid,
                location.LayerId.High,
                location.LayerId.Low,
                location.LayerIndex);
        }

        private static SerializedProperty FindState(
            SerializedProperty states,
            GraphDiagnosticLocation location)
        {
            if (states == null || !states.isArray)
            {
                return null;
            }

            return FindByStableIdOrIndex(
                states,
                "stateId",
                location.StateId.IsValid,
                location.StateId.High,
                location.StateId.Low,
                location.StateIndex);
        }

        private static SerializedProperty FindTransition(
            SerializedProperty transitions,
            GraphDiagnosticLocation location)
        {
            if (transitions == null || !transitions.isArray)
            {
                return null;
            }

            return FindByStableIdOrIndex(
                transitions,
                "transitionId",
                location.TransitionId.IsValid,
                location.TransitionId.High,
                location.TransitionId.Low,
                location.TransitionIndex);
        }

        private static SerializedProperty FindByStableIdOrIndex(
            SerializedProperty elements,
            string idPropertyName,
            bool hasValidId,
            ulong high,
            ulong low,
            int fallbackIndex)
        {
            if (!hasValidId)
            {
                return IsValidIndex(elements, fallbackIndex)
                    ? elements.GetArrayElementAtIndex(fallbackIndex)
                    : null;
            }

            SerializedProperty uniqueMatch = null;
            int matchCount = 0;
            for (int index = 0; index < elements.arraySize; index++)
            {
                SerializedProperty candidate = elements.GetArrayElementAtIndex(index);
                if (!Matches(candidate.FindPropertyRelative(idPropertyName), high, low))
                {
                    continue;
                }

                matchCount++;
                if (matchCount == 1)
                {
                    uniqueMatch = candidate.Copy();
                }
            }

            if (matchCount == 1)
            {
                return uniqueMatch;
            }

            if (matchCount > 1 && IsValidIndex(elements, fallbackIndex))
            {
                SerializedProperty indexed = elements.GetArrayElementAtIndex(fallbackIndex);
                if (Matches(indexed.FindPropertyRelative(idPropertyName), high, low))
                {
                    return indexed;
                }
            }

            return null;
        }

        private static bool IsValidIndex(SerializedProperty elements, int index)
        {
            return index >= 0 && index < elements.arraySize;
        }

        private static bool Matches(SerializedProperty serializedId, ulong high, ulong low)
        {
            if (serializedId == null)
            {
                return false;
            }

            SerializedProperty serializedHigh = serializedId.FindPropertyRelative("high");
            SerializedProperty serializedLow = serializedId.FindPropertyRelative("low");
            return serializedHigh != null &&
                   serializedLow != null &&
                   serializedHigh.ulongValue == high &&
                   serializedLow.ulongValue == low;
        }
    }
}
