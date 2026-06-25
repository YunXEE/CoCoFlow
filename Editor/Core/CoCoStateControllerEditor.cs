using System;
using System.Collections.Generic;
using System.Text;
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

    [CustomEditor(typeof(CoCoStateController))]
    [CanEditMultipleObjects]
    public class CoCoStateControllerEditor : UnityEditor.Editor
    {
        private const string LayerSuffix = "Layer";
        private const string WarningPrefix = "-Warning-";

        #region Public API

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("自动配置"))
                {
                    AutoConfigureTargets();
                }
            }
        }

        #endregion

        #region Internal Logic

        private void AutoConfigureTargets()
        {
            var report = new AutoConfigureReport();
            foreach (var item in targets)
            {
                if (item is CoCoStateController controller)
                {
                    AutoConfigureController(controller, report);
                }
            }

            serializedObject.Update();
            EditorUtility.DisplayDialog("自动配置", report.BuildMessage(), "OK");
        }

        private static void AutoConfigureController(
            CoCoStateController controller,
            AutoConfigureReport report)
        {
            if (controller == null) return;

            var layerRoots = GetDirectChildren(controller.transform);
            if (layerRoots.Count == 0)
            {
                report.AddSkipped($"{controller.name}: 当前物体没有第一层 Layer 子物体。");
                return;
            }

            if (!ValidateLayerNames(controller, layerRoots, report))
            {
                return;
            }

            var layers = new List<CoCoStateLayer>();
            for (int i = 0; i < layerRoots.Count; i++)
            {
                Transform layerRoot = layerRoots[i];
                var rootStates = new List<CoCoStateBase>();
                var childMachines = new List<CoCoStateChildMachine>();

                CollectRootStates(layerRoot, rootStates, childMachines, report);
                var defaultState = rootStates.Count > 0 ? rootStates[0] : null;

                layers.Add(new CoCoStateLayer(
                    BuildLayerName(layerRoot.name),
                    defaultState,
                    rootStates,
                    i,
                    true,
                    true,
                    childMachines));
            }

            Undo.RecordObject(controller, "Auto Configure CoCo State Controller");
            controller.SetStateLayers(layers);
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            report.ConfiguredCount++;
            report.AddInfo($"{controller.name}: 已配置 {layers.Count} 个 State Layer。");
        }

        private static List<Transform> GetDirectChildren(Transform root)
        {
            var children = new List<Transform>();
            if (root == null) return children;

            for (int i = 0; i < root.childCount; i++)
            {
                children.Add(root.GetChild(i));
            }

            return children;
        }

        private static bool ValidateLayerNames(
            CoCoStateController controller,
            List<Transform> layerRoots,
            AutoConfigureReport report)
        {
            var invalidNames = new List<string>();
            foreach (Transform layerRoot in layerRoots)
            {
                if (!HasLayerSuffix(layerRoot.name))
                {
                    invalidNames.Add(layerRoot.name);
                }
            }

            if (invalidNames.Count == 0) return true;

            report.AddSkipped(
                $"{controller.name}: 自动配置已跳过。第一层子物体必须全部以 Layer/layer 结尾：{string.Join(", ", invalidNames)}");
            return false;
        }

        private static void CollectRootStates(
            Transform layerRoot,
            List<CoCoStateBase> rootStates,
            List<CoCoStateChildMachine> childMachines,
            AutoConfigureReport report)
        {
            var rootStateNodes = new List<StateNodeResult>();
            for (int i = 0; i < layerRoot.childCount; i++)
            {
                Transform stateRoot = layerRoot.GetChild(i);
                var stateNode = ResolveStateNode(stateRoot, report);
                if (stateNode.State == null) continue;

                rootStates.Add(stateNode.State);
                rootStateNodes.Add(stateNode);
            }

            MarkDuplicateStateNodes(rootStateNodes, BuildPath(layerRoot), report);
            foreach (var stateNode in rootStateNodes)
            {
                CollectChildMachines(stateNode.StateRoot, stateNode.State, childMachines, report);
            }

            if (rootStates.Count == 0)
            {
                report.AddWarning($"{BuildPath(layerRoot)}: Layer 下没有可用的第一层状态节点。");
            }
        }

        private static void CollectChildMachines(
            Transform parentStateRoot,
            CoCoStateBase parentState,
            List<CoCoStateChildMachine> childMachines,
            AutoConfigureReport report)
        {
            var childStates = new List<CoCoStateBase>();
            var childStateNodes = new List<StateNodeResult>();
            for (int i = 0; i < parentStateRoot.childCount; i++)
            {
                Transform childRoot = parentStateRoot.GetChild(i);
                var childStateNode = ResolveStateNode(childRoot, report);
                if (childStateNode.State == null) continue;

                childStateNodes.Add(childStateNode);
                childStates.Add(childStateNode.State);
            }

            if (childStates.Count == 0) return;

            MarkDuplicateStateNodes(childStateNodes, BuildPath(parentStateRoot), report);
            childMachines.Add(new CoCoStateChildMachine(
                parentState,
                childStates[0],
                childStates));

            for (int i = 0; i < childStateNodes.Count; i++)
            {
                CollectChildMachines(
                    childStateNodes[i].StateRoot,
                    childStateNodes[i].State,
                    childMachines,
                    report);
            }
        }

        private static StateNodeResult ResolveStateNode(
            Transform stateRoot,
            AutoConfigureReport report)
        {
            var stateComponents = stateRoot.GetComponents<CoCoStateBase>();
            if (stateComponents.Length == 0)
            {
                report.AddWarning($"{BuildPath(stateRoot)}: 状态节点没有 CoCoStateBase，已跳过该节点。");
                RenameStateNode(stateRoot, stateRoot.name, true, report);
                return new StateNodeResult(stateRoot, null);
            }

            if (stateComponents.Length > 1)
            {
                report.AddWarning(
                    $"{BuildPath(stateRoot)}: 一个状态节点包含多个 CoCoStateBase，仅使用 {stateComponents[0].GetType().Name}。");
                RenameStateNode(stateRoot, stateComponents[0].GetType().Name, true, report);
                return new StateNodeResult(stateRoot, stateComponents[0]);
            }

            RenameStateNode(stateRoot, stateComponents[0].GetType().Name, false, report);
            return new StateNodeResult(stateRoot, stateComponents[0]);
        }

        private static void MarkDuplicateStateNodes(
            List<StateNodeResult> stateNodes,
            string scopePath,
            AutoConfigureReport report)
        {
            var nodesByType = new Dictionary<Type, List<StateNodeResult>>();
            foreach (var stateNode in stateNodes)
            {
                if (stateNode.State == null) continue;

                Type stateType = stateNode.State.GetType();
                if (!nodesByType.TryGetValue(stateType, out var typedNodes))
                {
                    typedNodes = new List<StateNodeResult>();
                    nodesByType[stateType] = typedNodes;
                }

                typedNodes.Add(stateNode);
            }

            foreach (var pair in nodesByType)
            {
                if (pair.Value.Count <= 1) continue;

                report.AddWarning(
                    $"{scopePath}: 同一个 State Machine 下重复挂载 {pair.Key.Name}，重复节点已加 Warning 前缀。");
                foreach (var duplicateNode in pair.Value)
                {
                    RenameStateNode(duplicateNode.StateRoot, pair.Key.Name, true, report);
                }
            }
        }

        private static void RenameStateNode(
            Transform stateRoot,
            string baseName,
            bool warning,
            AutoConfigureReport report)
        {
            if (stateRoot == null) return;

            string cleanName = StripWarningPrefix(string.IsNullOrWhiteSpace(baseName)
                ? stateRoot.name
                : baseName.Trim());
            string targetName = warning ? WarningPrefix + cleanName : cleanName;
            if (stateRoot.name == targetName) return;

            Undo.RecordObject(stateRoot.gameObject, "Rename CoCo State Node");
            stateRoot.gameObject.name = targetName;
            EditorUtility.SetDirty(stateRoot.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(stateRoot.gameObject);
            report.RenamedCount++;
        }

        private static string StripWarningPrefix(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            string result = value;
            while (result.StartsWith(WarningPrefix, StringComparison.Ordinal))
            {
                result = result.Substring(WarningPrefix.Length);
            }

            return result;
        }

        private static bool HasLayerSuffix(string layerName)
        {
            return !string.IsNullOrWhiteSpace(layerName) &&
                   layerName.EndsWith(LayerSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLayerName(string layerObjectName)
        {
            if (string.IsNullOrWhiteSpace(layerObjectName))
            {
                return "State Layer";
            }

            string trimmedName = layerObjectName.Trim();
            if (!HasLayerSuffix(trimmedName))
            {
                return trimmedName;
            }

            string layerName = trimmedName.Substring(0, trimmedName.Length - LayerSuffix.Length).Trim();
            return string.IsNullOrEmpty(layerName) ? trimmedName : layerName;
        }

        private static string BuildPath(Transform transform)
        {
            if (transform == null) return string.Empty;

            var names = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                names.Push(current.name);
            }

            return string.Join("/", names);
        }

        #endregion

        private sealed class AutoConfigureReport
        {
            private readonly List<string> infos = new List<string>();
            private readonly List<string> warnings = new List<string>();
            private readonly List<string> skipped = new List<string>();

            public int ConfiguredCount { get; set; }
            public int RenamedCount { get; set; }

            public void AddInfo(string message)
            {
                infos.Add(message);
            }

            public void AddWarning(string message)
            {
                warnings.Add(message);
            }

            public void AddSkipped(string message)
            {
                skipped.Add(message);
            }

            public string BuildMessage()
            {
                var builder = new StringBuilder();
                if (ConfiguredCount > 0)
                {
                    builder.AppendLine($"完成：{ConfiguredCount} 个 CoCoStateController 已自动配置。");
                    builder.AppendLine($"重命名：{RenamedCount} 个状态节点。");
                }
                else
                {
                    builder.AppendLine("没有 CoCoStateController 被自动配置。");
                }

                AppendSection(builder, "结果", infos);
                AppendSection(builder, "跳过", skipped);
                AppendSection(builder, "警告", warnings);
                return builder.ToString().TrimEnd();
            }

            private static void AppendSection(
                StringBuilder builder,
                string title,
                List<string> messages)
            {
                if (messages.Count == 0) return;

                builder.AppendLine();
                builder.AppendLine(title + ":");
                int visibleCount = Math.Min(messages.Count, 8);
                for (int i = 0; i < visibleCount; i++)
                {
                    builder.AppendLine("- " + messages[i]);
                }

                if (messages.Count > visibleCount)
                {
                    builder.AppendLine($"- 还有 {messages.Count - visibleCount} 条未显示。");
                }
            }
        }

        private readonly struct StateNodeResult
        {
            public StateNodeResult(
                Transform stateRoot,
                CoCoStateBase state)
            {
                StateRoot = stateRoot;
                State = state;
            }

            public Transform StateRoot { get; }
            public CoCoStateBase State { get; }
        }
    }
}
