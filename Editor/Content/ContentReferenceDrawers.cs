using CoCoFlow.Runtime.Content;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Content
{
    [CustomPropertyDrawer(typeof(ContentId))]
    internal sealed class ContentIdDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            ContentIdentityDrawer.GetHeight(property);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            ContentIdentityDrawer.Draw(position, property, label, "Content ID");
    }

    [CustomPropertyDrawer(typeof(ContentOwnerId))]
    internal sealed class ContentOwnerIdDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            ContentIdentityDrawer.GetHeight(property);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            ContentIdentityDrawer.Draw(position, property, label, "Content Owner ID");
    }

    [CustomPropertyDrawer(typeof(ContentReference))]
    internal sealed class ContentReferenceDrawer : PropertyDrawer
    {
        private const float HelpLines = 2f;

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
            float height = line + EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(id, true) +
                      EditorGUIUtility.standardVerticalSpacing;
            height += (line + EditorGUIUtility.standardVerticalSpacing) * 3f;
            if (TryGetValidationMessage(property, out _))
            {
                height += line * HelpLines +
                          EditorGUIUtility.standardVerticalSpacing;
            }

            return height;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty id = property.FindPropertyRelative("id");
            SerializedProperty kind = property.FindPropertyRelative("kind");
            SerializedProperty sourceKind = property.FindPropertyRelative("sourceKind");
            SerializedProperty directObject = property.FindPropertyRelative("directObject");
            SerializedProperty location = property.FindPropertyRelative("location");
            if (id == null || kind == null || sourceKind == null ||
                directObject == null || location == null)
            {
                EditorGUI.HelpBox(
                    position,
                    "ContentReference serialized fields could not be resolved.",
                    MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            var foldoutRect = new Rect(position.x, position.y, position.width, line);
            string summary = BuildSummary(id, kind, sourceKind);
            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                new GUIContent(label.text + summary, label.tooltip),
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
                new GUIContent("ID"),
                true);
            y += idHeight + EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, line),
                kind,
                new GUIContent("Kind"));
            y += line + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, line),
                sourceKind,
                new GUIContent("Source"));
            y += line + EditorGUIUtility.standardVerticalSpacing;
            if (EditorGUI.EndChangeCheck() &&
                property.serializedObject.targetObjects.Length == 1)
            {
                NormalizeLocator(kind, sourceKind, directObject, location);
            }

            ContentKind selectedKind = (ContentKind)kind.intValue;
            ContentSourceKind selectedSource = (ContentSourceKind)sourceKind.intValue;
            var locatorRect = new Rect(position.x, y, position.width, line);
            if (selectedSource == ContentSourceKind.Addressables ||
                selectedKind == ContentKind.AdditiveScene)
            {
                string locatorLabel = selectedSource == ContentSourceKind.Addressables
                    ? "Address"
                    : "Scene Path or Name";
                EditorGUI.PropertyField(
                    locatorRect,
                    location,
                    new GUIContent(locatorLabel));
            }
            else
            {
                System.Type objectType = selectedKind == ContentKind.PrefabSource
                    ? typeof(GameObject)
                    : typeof(UnityEngine.Object);
                EditorGUI.ObjectField(
                    locatorRect,
                    directObject,
                    objectType,
                    new GUIContent(selectedKind == ContentKind.PrefabSource
                        ? "Prefab Source"
                        : "Asset"));
            }

            y += line + EditorGUIUtility.standardVerticalSpacing;
            if (TryGetValidationMessage(property, out string message))
            {
                EditorGUI.HelpBox(
                    new Rect(position.x, y, position.width, line * HelpLines),
                    message,
                    MessageType.Warning);
            }

            EditorGUI.indentLevel = previousIndent;
            EditorGUI.EndProperty();
        }

        private static void NormalizeLocator(
            SerializedProperty kind,
            SerializedProperty sourceKind,
            SerializedProperty directObject,
            SerializedProperty location)
        {
            ContentKind selectedKind = (ContentKind)kind.intValue;
            ContentSourceKind selectedSource = (ContentSourceKind)sourceKind.intValue;
            if (selectedSource == ContentSourceKind.Addressables)
            {
                directObject.objectReferenceValue = null;
                return;
            }

            if (selectedKind == ContentKind.AdditiveScene)
            {
                directObject.objectReferenceValue = null;
            }
            else
            {
                location.stringValue = string.Empty;
            }
        }

        private static bool TryGetValidationMessage(
            SerializedProperty property,
            out string message)
        {
            SerializedProperty kind = property.FindPropertyRelative("kind");
            SerializedProperty sourceKind = property.FindPropertyRelative("sourceKind");
            SerializedProperty directObject = property.FindPropertyRelative("directObject");
            SerializedProperty location = property.FindPropertyRelative("location");
            if (kind == null || sourceKind == null || directObject == null || location == null)
            {
                message = "ContentReference serialization is incomplete.";
                return true;
            }

            if (kind.intValue < (int)ContentKind.Asset ||
                kind.intValue > (int)ContentKind.AdditiveScene ||
                sourceKind.intValue < (int)ContentSourceKind.Direct ||
                sourceKind.intValue > (int)ContentSourceKind.Addressables)
            {
                message = "Content kind or source is not defined.";
                return true;
            }

            ContentKind selectedKind = (ContentKind)kind.intValue;
            ContentSourceKind selectedSource = (ContentSourceKind)sourceKind.intValue;
            if (selectedSource == ContentSourceKind.Addressables)
            {
                if (directObject.objectReferenceValue != null)
                {
                    message = "Addressables references cannot retain a Direct object.";
                    return true;
                }

                if (string.IsNullOrWhiteSpace(location.stringValue))
                {
                    message = "An Addressables address is required.";
                    return true;
                }
            }
            else if (selectedKind == ContentKind.AdditiveScene)
            {
                if (directObject.objectReferenceValue != null)
                {
                    message = "A Direct additive Scene cannot retain a Direct object.";
                    return true;
                }

                if (string.IsNullOrWhiteSpace(location.stringValue))
                {
                    message = "A Direct additive Scene path or name is required.";
                    return true;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(location.stringValue))
                {
                    message = "A Direct Asset or Prefab Source cannot retain a location.";
                    return true;
                }

                if (directObject.objectReferenceValue == null)
                {
                    message = selectedKind == ContentKind.PrefabSource
                        ? "A Direct Prefab Source is required."
                        : "A Direct Asset is required.";
                    return true;
                }

                if (selectedKind == ContentKind.PrefabSource &&
                    !(directObject.objectReferenceValue is GameObject))
                {
                    message = "A Prefab Source must reference a GameObject.";
                    return true;
                }
            }

            message = string.Empty;
            return false;
        }

        private static string BuildSummary(
            SerializedProperty id,
            SerializedProperty kind,
            SerializedProperty sourceKind)
        {
            SerializedProperty value = id.FindPropertyRelative("value");
            string idText = value == null || string.IsNullOrWhiteSpace(value.stringValue)
                ? "unassigned"
                : value.stringValue;
            return "  [" + idText + " · " +
                   ((ContentKind)kind.intValue) + " · " +
                   ((ContentSourceKind)sourceKind.intValue) + "]";
        }
    }

    internal static class ContentIdentityDrawer
    {
        private const float HelpLines = 2f;

        internal static float GetHeight(SerializedProperty property)
        {
            SerializedProperty value = property.FindPropertyRelative("value");
            bool invalid = value != null &&
                           !value.hasMultipleDifferentValues &&
                           (!IsCanonical(value.stringValue));
            return EditorGUIUtility.singleLineHeight +
                   (invalid
                       ? EditorGUIUtility.standardVerticalSpacing +
                         EditorGUIUtility.singleLineHeight * HelpLines
                       : 0f);
        }

        internal static void Draw(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            string identityName)
        {
            SerializedProperty value = property.FindPropertyRelative("value");
            if (value == null)
            {
                EditorGUI.HelpBox(
                    position,
                    identityName + " serialized value could not be resolved.",
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
                string message = string.IsNullOrWhiteSpace(value.stringValue)
                    ? identityName + " must contain non-whitespace text."
                    : identityName + " cannot begin or end with whitespace.";
                EditorGUI.HelpBox(
                    new Rect(
                        position.x,
                        position.y + line + EditorGUIUtility.standardVerticalSpacing,
                        position.width,
                        line * HelpLines),
                    message,
                    MessageType.Warning);
            }

            EditorGUI.EndProperty();
        }

        private static bool IsCanonical(string value) =>
            !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    }
}
