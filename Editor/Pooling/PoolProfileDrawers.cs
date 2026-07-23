using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Pooling;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Pooling
{
    [CustomPropertyDrawer(typeof(PoolId))]
    internal sealed class PoolIdDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty value = property.FindPropertyRelative("value");
            bool invalid = value != null &&
                           !value.hasMultipleDifferentValues &&
                           !IsCanonical(value.stringValue);
            return EditorGUIUtility.singleLineHeight +
                   (invalid
                       ? EditorGUIUtility.standardVerticalSpacing +
                         EditorGUIUtility.singleLineHeight * 2f
                       : 0f);
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty value = property.FindPropertyRelative("value");
            if (value == null)
            {
                EditorGUI.HelpBox(
                    position,
                    "PoolId serialized value could not be resolved.",
                    MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(
                new Rect(position.x, position.y, position.width, line),
                value,
                label);
            if (!value.hasMultipleDifferentValues && !IsCanonical(value.stringValue))
            {
                EditorGUI.HelpBox(
                    new Rect(
                        position.x,
                        position.y + line + EditorGUIUtility.standardVerticalSpacing,
                        position.width,
                        line * 2f),
                    string.IsNullOrWhiteSpace(value.stringValue)
                        ? "A non-empty Pool ID is required."
                        : "Pool IDs cannot start or end with whitespace.",
                    MessageType.Warning);
            }

            EditorGUI.EndProperty();
        }

        private static bool IsCanonical(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value == value.Trim();
    }

    [CustomPropertyDrawer(typeof(PoolProfile))]
    internal sealed class PoolProfileDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return line;
            }

            SerializedProperty id = property.FindPropertyRelative("id");
            SerializedProperty prefabSource = property.FindPropertyRelative("prefabSource");
            float height = line + EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(id, true) +
                      EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(prefabSource, true) +
                      EditorGUIUtility.standardVerticalSpacing;
            height += (line + EditorGUIUtility.standardVerticalSpacing) * 2f;
            if (TryGetValidationMessage(property, out _))
            {
                height += line * 2f + EditorGUIUtility.standardVerticalSpacing;
            }

            return height;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty id = property.FindPropertyRelative("id");
            SerializedProperty prefabSource = property.FindPropertyRelative("prefabSource");
            SerializedProperty prewarmCount = property.FindPropertyRelative("prewarmCount");
            SerializedProperty maxRetained = property.FindPropertyRelative("maxRetained");
            if (id == null || prefabSource == null ||
                prewarmCount == null || maxRetained == null)
            {
                EditorGUI.HelpBox(
                    position,
                    "PoolProfile serialized fields could not be resolved.",
                    MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            var foldoutRect = new Rect(position.x, position.y, position.width, line);
            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                new GUIContent(label.text + BuildSummary(id, prewarmCount, maxRetained)),
                true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            float y = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;

            float idHeight = EditorGUI.GetPropertyHeight(id, true);
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, idHeight),
                id,
                new GUIContent("Pool ID"),
                true);
            y += idHeight + EditorGUIUtility.standardVerticalSpacing;

            float sourceHeight = EditorGUI.GetPropertyHeight(prefabSource, true);
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, sourceHeight),
                prefabSource,
                new GUIContent("Prefab Source"),
                true);
            y += sourceHeight + EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, line),
                prewarmCount,
                new GUIContent(
                    "Prewarm Count",
                    "Desired idle count after explicit prepare or prewarm."));
            y += line + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, line),
                maxRetained,
                new GUIContent(
                    "Max Retained",
                    "Maximum idle retention. This is not an active or total cap."));
            y += line + EditorGUIUtility.standardVerticalSpacing;

            if (TryGetValidationMessage(property, out string message))
            {
                EditorGUI.HelpBox(
                    new Rect(position.x, y, position.width, line * 2f),
                    message,
                    MessageType.Warning);
            }

            EditorGUI.indentLevel = previousIndent;
            EditorGUI.EndProperty();
        }

        private static string BuildSummary(
            SerializedProperty id,
            SerializedProperty prewarmCount,
            SerializedProperty maxRetained)
        {
            SerializedProperty value = id.FindPropertyRelative("value");
            string idText = value == null || string.IsNullOrWhiteSpace(value.stringValue)
                ? "unassigned"
                : value.stringValue;
            return "  [" + idText + " · warm " + prewarmCount.intValue +
                   " · retain " + maxRetained.intValue + "]";
        }

        private static bool TryGetValidationMessage(
            SerializedProperty property,
            out string message)
        {
            SerializedProperty prefabSource = property.FindPropertyRelative("prefabSource");
            SerializedProperty prewarmCount = property.FindPropertyRelative("prewarmCount");
            SerializedProperty maxRetained = property.FindPropertyRelative("maxRetained");
            SerializedProperty kind = prefabSource?.FindPropertyRelative("kind");
            if (prefabSource == null || prewarmCount == null ||
                maxRetained == null || kind == null)
            {
                message = "PoolProfile serialization is incomplete.";
                return true;
            }

            if (prewarmCount.intValue < 0 || maxRetained.intValue < 0)
            {
                message = "Prewarm Count and Max Retained must be non-negative.";
                return true;
            }

            if (prewarmCount.intValue > maxRetained.intValue)
            {
                message = "Prewarm Count cannot exceed Max Retained.";
                return true;
            }

            if (kind.intValue != (int)ContentKind.PrefabSource)
            {
                message = "Pooling requires a Prefab Source ContentReference.";
                return true;
            }

            message = string.Empty;
            return false;
        }
    }
}
