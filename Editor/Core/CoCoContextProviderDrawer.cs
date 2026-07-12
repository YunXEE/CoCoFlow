using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Core
{
    [CustomPropertyDrawer(typeof(CoCoContextProviderAttribute))]
    public class CoCoContextProviderDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 50f;
        private const float WideButtonWidth = 66f;
        private const float ButtonGap = 4f;

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f +
                   EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var objectRect = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            var buttonRect = new Rect(
                position.x,
                objectRect.yMax + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight);

            DrawObjectField(objectRect, property, label);
            DrawPickerButtons(buttonRect, property);

            EditorGUI.EndProperty();
        }

        private Type RequiredContextType =>
            (attribute as CoCoContextProviderAttribute)?.RequiredContextType;

        private void DrawObjectField(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.LabelField(position, label.text, "Use on object reference fields only.");
                return;
            }

            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.ObjectField(
                position,
                label,
                property.objectReferenceValue,
                typeof(MonoBehaviour),
                true);
            if (!EditorGUI.EndChangeCheck()) return;

            if (selected == null)
            {
                property.objectReferenceValue = null;
                return;
            }

            if (selected is MonoBehaviour behaviour &&
                IsValidContextProvider(behaviour, RequiredContextType, out _))
            {
                property.objectReferenceValue = selected;
                return;
            }

            EditorUtility.DisplayDialog(
                "Context Provider",
                "Selected component does not implement the required ICoCoContextProvider<TContext> contract.",
                "OK");
        }

        private void DrawPickerButtons(
            Rect position,
            SerializedProperty property)
        {
            var indentedRect = EditorGUI.IndentedRect(position);
            var clearRect = new Rect(
                indentedRect.xMax - ButtonWidth,
                indentedRect.y,
                ButtonWidth,
                indentedRect.height);
            var pickRect = new Rect(
                clearRect.x - ButtonGap - ButtonWidth,
                indentedRect.y,
                ButtonWidth,
                indentedRect.height);
            var autoRect = new Rect(
                pickRect.x - ButtonGap - WideButtonWidth,
                indentedRect.y,
                WideButtonWidth,
                indentedRect.height);

            using (new EditorGUI.DisabledScope(!CanSearchFrom(property)))
            {
                if (GUI.Button(autoRect, "自动"))
                {
                    AutoPickContextProvider(property);
                }
            }

            using (new EditorGUI.DisabledScope(!CanShowMenu(property)))
            {
                if (GUI.Button(pickRect, "选择"))
                {
                    ShowContextProviderMenu(property);
                }
            }

            using (new EditorGUI.DisabledScope(!HasContextProviderSelection(property)))
            {
                if (GUI.Button(clearRect, "清空"))
                {
                    SetContextProvider(property, null);
                }
            }
        }

        private static bool CanSearchFrom(SerializedProperty property)
        {
            foreach (var targetObject in property.serializedObject.targetObjects)
            {
                if (targetObject is Component)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanShowMenu(SerializedProperty property)
        {
            return property.serializedObject.targetObjects.Length == 1 &&
                   property.serializedObject.targetObject is Component;
        }

        private static bool HasContextProviderSelection(SerializedProperty property)
        {
            return property.hasMultipleDifferentValues ||
                   property.objectReferenceValue != null;
        }

        private void AutoPickContextProvider(SerializedProperty property)
        {
            var pickedCount = 0;
            var missing = new List<string>();
            foreach (var targetObject in property.serializedObject.targetObjects)
            {
                if (targetObject is not Component component) continue;

                var candidates = CollectContextProviderCandidates(
                    component.transform,
                    RequiredContextType);
                if (candidates.Count == 0)
                {
                    missing.Add(component.name);
                    continue;
                }

                SetContextProvider(targetObject, property.propertyPath, candidates[0].Provider);
                pickedCount++;
            }

            property.serializedObject.Update();
            if (missing.Count == 0) return;

            EditorUtility.DisplayDialog(
                "Context Provider",
                $"已自动选择 {pickedCount} 个 Context Provider。\n未找到：{string.Join(", ", missing)}",
                "OK");
        }

        private void ShowContextProviderMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();
            if (property.serializedObject.targetObject is not Component component)
            {
                menu.AddDisabledItem(new GUIContent("Missing Component"));
                menu.ShowAsContext();
                return;
            }

            var candidates = CollectContextProviderCandidates(
                component.transform,
                RequiredContextType);
            if (candidates.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Context Provider found in this hierarchy"));
            }
            else
            {
                var targetObject = property.serializedObject.targetObject;
                string propertyPath = property.propertyPath;
                var currentValue = property.objectReferenceValue;
                foreach (var candidate in candidates)
                {
                    var provider = candidate.Provider;
                    menu.AddItem(
                        new GUIContent(candidate.Label),
                        provider == currentValue,
                        () => SetContextProvider(targetObject, propertyPath, provider));
                }
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(
                new GUIContent("Clear"),
                property.objectReferenceValue == null,
                () => SetContextProvider(property, null));
            menu.ShowAsContext();
        }

        private static void SetContextProvider(
            SerializedProperty property,
            MonoBehaviour provider)
        {
            foreach (var targetObject in property.serializedObject.targetObjects)
            {
                SetContextProvider(targetObject, property.propertyPath, provider);
            }

            property.serializedObject.Update();
        }

        private static void SetContextProvider(
            UnityEngine.Object targetObject,
            string propertyPath,
            MonoBehaviour provider)
        {
            if (targetObject == null || string.IsNullOrEmpty(propertyPath)) return;

            Undo.RecordObject(targetObject, "Set Context Provider");
            var serializedObject = new SerializedObject(targetObject);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null) return;

            property.objectReferenceValue = provider;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(targetObject);
        }

        private static List<ContextProviderCandidate> CollectContextProviderCandidates(
            Transform sourceTransform,
            Type requiredContextType)
        {
            var candidates = new List<ContextProviderCandidate>();
            if (sourceTransform == null) return candidates;

            var seen = new HashSet<MonoBehaviour>();
            AddProvidersFromTransform(
                candidates,
                seen,
                sourceTransform,
                sourceTransform,
                requiredContextType,
                "Self",
                0);

            var distance = 1;
            for (Transform current = sourceTransform.parent;
                 current != null;
                 current = current.parent)
            {
                AddProvidersFromTransform(
                    candidates,
                    seen,
                    sourceTransform,
                    current,
                    requiredContextType,
                    "Parent",
                    distance);
                distance++;
            }

            AddProvidersFromChildren(
                candidates,
                seen,
                sourceTransform,
                sourceTransform,
                requiredContextType,
                "Child",
                100);

            var root = sourceTransform.root;
            if (root != null && root != sourceTransform)
            {
                AddProvidersFromChildren(
                    candidates,
                    seen,
                    sourceTransform,
                    root,
                    requiredContextType,
                    "Hierarchy",
                    200);
            }

            candidates.Sort(CompareContextProviderCandidates);
            return candidates;
        }

        private static void AddProvidersFromChildren(
            List<ContextProviderCandidate> candidates,
            HashSet<MonoBehaviour> seen,
            Transform sourceTransform,
            Transform root,
            Type requiredContextType,
            string scope,
            int baseOrder)
        {
            if (root == null) return;

            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || seen.Contains(behaviour)) continue;
                if (!IsValidContextProvider(behaviour, requiredContextType, out var contextType)) continue;

                seen.Add(behaviour);
                candidates.Add(new ContextProviderCandidate(
                    behaviour,
                    BuildProviderLabel(scope, sourceTransform, behaviour.transform, behaviour, contextType),
                    baseOrder + GetTransformDistance(sourceTransform, behaviour.transform)));
            }
        }

        private static void AddProvidersFromTransform(
            List<ContextProviderCandidate> candidates,
            HashSet<MonoBehaviour> seen,
            Transform sourceTransform,
            Transform providerRoot,
            Type requiredContextType,
            string scope,
            int order)
        {
            if (providerRoot == null) return;

            var behaviours = providerRoot.GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || seen.Contains(behaviour)) continue;
                if (!IsValidContextProvider(behaviour, requiredContextType, out var contextType)) continue;

                seen.Add(behaviour);
                candidates.Add(new ContextProviderCandidate(
                    behaviour,
                    BuildProviderLabel(scope, sourceTransform, providerRoot, behaviour, contextType),
                    order));
            }
        }

        private static bool IsValidContextProvider(
            MonoBehaviour behaviour,
            Type requiredContextType,
            out Type contextType)
        {
            contextType = null;
            if (behaviour == null) return false;

            var contracts = behaviour.GetType().GetInterfaces();
            foreach (var contract in contracts)
            {
                if (!contract.IsGenericType ||
                    contract.GetGenericTypeDefinition() != typeof(ICoCoContextProvider<>))
                {
                    continue;
                }

                var providerContextType = contract.GetGenericArguments()[0];
                if (requiredContextType != null &&
                    !requiredContextType.IsAssignableFrom(providerContextType))
                {
                    continue;
                }

                contextType = providerContextType;
                return true;
            }

            return false;
        }

        private static string BuildProviderLabel(
            string scope,
            Transform sourceTransform,
            Transform providerTransform,
            MonoBehaviour provider,
            Type contextType)
        {
            string path = BuildRelativePath(sourceTransform, providerTransform);
            string providerName = provider.GetType().Name;
            return contextType == null
                ? $"{scope}/{path}/{providerName}"
                : $"{scope}/{path}/{providerName} ({contextType.Name})";
        }

        private static string BuildRelativePath(
            Transform sourceTransform,
            Transform targetTransform)
        {
            if (targetTransform == null) return string.Empty;
            if (targetTransform == sourceTransform) return targetTransform.name;

            var names = new Stack<string>();
            for (Transform current = targetTransform; current != null; current = current.parent)
            {
                names.Push(current.name);
                if (current == sourceTransform) break;
            }

            return string.Join("/", names);
        }

        private static int GetTransformDistance(
            Transform left,
            Transform right)
        {
            if (left == null || right == null) return int.MaxValue / 2;
            if (left == right) return 0;

            var leftAncestors = new Dictionary<Transform, int>();
            var distance = 0;
            for (Transform current = left; current != null; current = current.parent)
            {
                leftAncestors[current] = distance;
                distance++;
            }

            distance = 0;
            for (Transform current = right; current != null; current = current.parent)
            {
                if (leftAncestors.TryGetValue(current, out var leftDistance))
                {
                    return leftDistance + distance;
                }

                distance++;
            }

            return int.MaxValue / 2;
        }

        private static int CompareContextProviderCandidates(
            ContextProviderCandidate left,
            ContextProviderCandidate right)
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            return orderComparison != 0
                ? orderComparison
                : string.Compare(left.Label, right.Label, StringComparison.Ordinal);
        }

        private readonly struct ContextProviderCandidate
        {
            public ContextProviderCandidate(
                MonoBehaviour provider,
                string label,
                int order)
            {
                Provider = provider;
                Label = label;
                Order = order;
            }

            public MonoBehaviour Provider { get; }
            public string Label { get; }
            public int Order { get; }
        }
    }
}
