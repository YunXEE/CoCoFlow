using System;
using CoCoFlow.Runtime.Core;
using UnityEditor;

namespace CoCoFlow.Editor.StateGraph
{
    public static class CoCoStateGraphDiagnosticNavigator
    {
        public static bool TryFindProperty(
            SerializedObject serializedAsset,
            CoCoGraphDiagnosticLocation location,
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

            if (location.ElementKind == CoCoGraphElementKind.Graph)
            {
                property = FindGraphProperty(serializedAsset, location.Field);
                return property != null;
            }

            if (location.ElementKind == CoCoGraphElementKind.Manifest)
            {
                property = serializedAsset.FindProperty("layers");
                return property != null;
            }

            if (location.ElementKind == CoCoGraphElementKind.EventAdapterDeclaration)
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
                    case CoCoGraphField.Identifier:
                    case CoCoGraphField.EventAdapterDeclarations:
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

            if (location.ElementKind == CoCoGraphElementKind.Layer)
            {
                property = FindLayerProperty(layer, location.Field);
                return property != null;
            }

            if (location.ElementKind == CoCoGraphElementKind.State)
            {
                SerializedProperty state = FindState(layer.FindPropertyRelative("states"), location);
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

            if (location.ElementKind == CoCoGraphElementKind.Transition)
            {
                property = FindTransitionProperty(transition, location.Field);
                return property != null;
            }

            if (location.ElementKind == CoCoGraphElementKind.Condition)
            {
                SerializedProperty conditions = transition.FindPropertyRelative("conditions");
                if (conditions == null ||
                    location.ConditionIndex < 0 ||
                    location.ConditionIndex >= conditions.arraySize)
                {
                    return false;
                }

                SerializedProperty condition = conditions.GetArrayElementAtIndex(location.ConditionIndex);
                property = location.Field == CoCoGraphField.Config
                    ? condition.FindPropertyRelative("config")
                    : condition.FindPropertyRelative("conditionDescriptorId");
                return property != null;
            }

            return false;
        }

        public static bool TrySelect(
            CoCoStateGraphAsset asset,
            CoCoGraphDiagnosticLocation location,
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
            CoCoGraphField field)
        {
            switch (field)
            {
                case CoCoGraphField.SchemaVersion:
                    return serializedAsset.FindProperty("schemaVersion");
                case CoCoGraphField.ContentFingerprint:
                    return serializedAsset.FindProperty("layers");
                case CoCoGraphField.Identifier:
                    return serializedAsset.FindProperty("graphId");
                case CoCoGraphField.AssetGuidStamp:
                    return serializedAsset.FindProperty("assetGuidStamp");
                default:
                    return serializedAsset.FindProperty("layers");
            }
        }

        private static SerializedProperty FindLayerProperty(SerializedProperty layer, CoCoGraphField field)
        {
            switch (field)
            {
                case CoCoGraphField.Identifier:
                    return layer.FindPropertyRelative("layerId");
                case CoCoGraphField.InitialState:
                    return layer.FindPropertyRelative("initialStateId");
                default:
                    return layer;
            }
        }

        private static SerializedProperty FindStateProperty(SerializedProperty state, CoCoGraphField field)
        {
            switch (field)
            {
                case CoCoGraphField.Identifier:
                    return state.FindPropertyRelative("stateId");
                case CoCoGraphField.ParentState:
                    return state.FindPropertyRelative("parentStateId");
                case CoCoGraphField.InitialChildState:
                    return state.FindPropertyRelative("initialChildStateId");
                case CoCoGraphField.Descriptor:
                    return state.FindPropertyRelative("stateDescriptorId");
                case CoCoGraphField.Config:
                    return state.FindPropertyRelative("config");
                default:
                    return state;
            }
        }

        private static SerializedProperty FindTransitionProperty(
            SerializedProperty transition,
            CoCoGraphField field)
        {
            switch (field)
            {
                case CoCoGraphField.Identifier:
                    return transition.FindPropertyRelative("transitionId");
                case CoCoGraphField.SourceState:
                    return transition.FindPropertyRelative("sourceStateId");
                case CoCoGraphField.TargetState:
                    return transition.FindPropertyRelative("targetStateId");
                case CoCoGraphField.Priority:
                    return transition.FindPropertyRelative("priority");
                case CoCoGraphField.Window:
                    return transition.FindPropertyRelative("windowMode");
                case CoCoGraphField.Conditions:
                case CoCoGraphField.Config:
                case CoCoGraphField.Descriptor:
                    return transition.FindPropertyRelative("conditions");
                default:
                    return transition;
            }
        }

        private static SerializedProperty FindLayer(
            SerializedProperty layers,
            CoCoGraphDiagnosticLocation location)
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
            CoCoGraphDiagnosticLocation location)
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
            CoCoGraphDiagnosticLocation location)
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
