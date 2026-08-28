using System;
using System.Collections.Generic;
using System.Text;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    /// <summary>
    /// Host inspector enhancement for the two weakly-typed hook arrays.
    /// The runtime disciplines these at startup; this editor surfaces the
    /// same discipline while assembling:
    ///   - intent sources: reflection over every closed
    ///     ICoCoIntentFrameSource&lt;T&gt; implementation in the scene —
    ///     any intent type, any object, listed per COMPONENT (one object
    ///     may carry several sources);
    ///   - operators: ICoCoOperator implementations, plus a Host-boundary
    ///     warning (the runtime rejects components outside the Host's
    ///     transform subtree at startup);
    ///   - an "Add from scene" menu lists every matching component —
    ///     the input reader commonly lives on a global rig, not on the
    ///     actor, so nothing has to be dragged at all.
    /// </summary>
    [CustomEditor(typeof(CoCoStateGraphHost))]
    internal sealed class CoCoStateGraphHostEditor : UnityEditor.Editor
    {
        private SerializedProperty intentSources;
        private SerializedProperty operators;

        private void OnEnable()
        {
            intentSources = serializedObject.FindProperty("intentSources");
            operators = serializedObject.FindProperty("operators");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHookList(
                intentSources,
                "Intent Sources",
                IsValidIntentSource,
                DescribeIntentSource,
                SceneIntentSources,
                null);
            DrawHookList(
                operators,
                "Operators",
                IsValidOperator,
                component => component.GetType().Name,
                SceneOperators,
                DescribeOperatorBoundary);

            DrawRestoreBindingSection();

            DrawPropertiesExcluding(
                serializedObject,
                "intentSources",
                "operators",
                "contextRestoreBinding",
                "m_Script");

            serializedObject.ApplyModifiedProperties();
        }

        // ----- intent sources: any closed ICoCoIntentFrameSource<T> -----

        private static bool IsValidIntentSource(MonoBehaviour component)
        {
            return FindIntentSourceInterfaces(component.GetType()).Count > 0;
        }

        private static string DescribeIntentSource(MonoBehaviour component)
        {
            List<Type> intentTypes = FindIntentSourceInterfaces(component.GetType());
            if (intentTypes.Count == 0)
            {
                return component.GetType().Name + " implements no " +
                    "ICoCoIntentFrameSource<T> interface";
            }

            var label = new StringBuilder(component.GetType().Name);
            label.Append(" (");
            for (int index = 0; index < intentTypes.Count; index++)
            {
                if (index > 0)
                {
                    label.Append(", ");
                }

                label.Append(intentTypes[index].Name);
            }

            label.Append(')');
            return label.ToString();
        }

        private static List<Type> FindIntentSourceInterfaces(Type type)
        {
            var intentTypes = new List<Type>();
            Type[] interfaces = type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type iface = interfaces[index];
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() ==
                        typeof(ICoCoIntentFrameSource<>))
                {
                    intentTypes.Add(iface.GetGenericArguments()[0]);
                }
            }

            return intentTypes;
        }

        // ----- operators -----

        private static bool IsValidOperator(MonoBehaviour component)
        {
            return component is ICoCoOperator;
        }

        private string DescribeOperatorBoundary(MonoBehaviour component)
        {
            var host = (CoCoStateGraphHost)target;
            if (CoCoStateGraphHostBoundary.Contains(host, component))
            {
                return null;
            }

            return component.name +
                " is outside the Host boundary — move it onto the Host " +
                "object or one of its children, the runtime rejects it at " +
                "startup.";
        }

        // ----- scene scans (per component, not per object) -----

        private static IEnumerable<MonoBehaviour> SceneIntentSources()
        {
            return FindSceneComponents(IsValidIntentSource);
        }

        private static IEnumerable<MonoBehaviour> SceneOperators()
        {
            return FindSceneComponents(IsValidOperator);
        }

        private static IEnumerable<MonoBehaviour> FindSceneComponents(
            Func<MonoBehaviour, bool> filter)
        {
            var found = new List<MonoBehaviour>();
            foreach (MonoBehaviour component in
                     FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (component != null && filter(component))
                {
                    found.Add(component);
                }
            }

            return found;
        }

        // ----- restore binding chain -----

        private void DrawRestoreBindingSection()
        {
            EditorGUILayout.LabelField("Context Restore Binding", EditorStyles.boldLabel);

            var host = (CoCoStateGraphHost)target;
            SerializedProperty rootProperty =
                serializedObject.FindProperty("contextRestoreBinding");
            MonoBehaviour root = rootProperty.objectReferenceValue as MonoBehaviour;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                UnityEngine.Object picked = EditorGUILayout.ObjectField(
                    root,
                    typeof(MonoBehaviour),
                    true);
                if (EditorGUI.EndChangeCheck())
                {
                    rootProperty.objectReferenceValue = picked;
                }

                if (GUILayout.Button("Auto-wire chain", EditorStyles.miniButton))
                {
                    AutoWireRestoreChain(rootProperty);
                }
            }

            if (root != null && !(root is ICoCoContextRestoreBinding))
            {
                EditorGUILayout.HelpBox(
                    root.name + " does not implement ICoCoContextRestoreBinding — " +
                    "restore projection will be silently skipped.",
                    MessageType.Error);
            }
            else if (root != null && !CoCoStateGraphHostBoundary.Contains(host, root))
            {
                EditorGUILayout.HelpBox(
                    root.name + " is outside the Host boundary — the Temporal " +
                    "controller drops it silently at startup. Move it into the " +
                    "Host subtree or pick another component.",
                    MessageType.Warning);
            }
            else if (root != null)
            {
                DrawRestoreChainPreview(root);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "no root wired — save/load and temporal restore will not " +
                    "project the world back onto the ledger",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);
        }

        private static void DrawRestoreChainPreview(MonoBehaviour root)
        {
            var seen = new HashSet<UnityEngine.Object>();
            MonoBehaviour current = root;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                bool valid = current is ICoCoContextRestoreBinding;
                EditorGUILayout.LabelField(
                    (current == root ? "root → " : "      → ") +
                    current.GetType().Name + " @ " + current.name,
                    valid ? EditorStyles.miniLabel : EditorStyles.boldLabel);
                if (!valid || !seen.Add(current))
                {
                    if (!valid)
                    {
                        EditorGUILayout.HelpBox(
                            current.name + " breaks the chain — it implements no " +
                            "ICoCoContextRestoreBinding.",
                            MessageType.Error);
                    }

                    return;
                }

                current = (current as ICoCoTemporalDecoratorBinding)
                    ?.DownstreamRestoreBinding;
            }
        }

        /// <summary>
        /// Scans the Host boundary for every ICoCoContextRestoreBinding
        /// implementation, wires the first as the Host root and chains the
        /// rest through DownstreamRestoreBinding — one click instead of
        /// two drag-and-drops across weakly-typed fields.
        /// </summary>
        private void AutoWireRestoreChain(SerializedProperty rootProperty)
        {
            var host = (CoCoStateGraphHost)target;
            var chain = new List<MonoBehaviour>();
            foreach (MonoBehaviour component in
                     FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (component != null &&
                    component is ICoCoContextRestoreBinding &&
                    CoCoStateGraphHostBoundary.Contains(host, component))
                {
                    chain.Add(component);
                }
            }

            if (chain.Count == 0)
            {
                Debug.LogWarning(
                    "[CoCoFlow] No ICoCoContextRestoreBinding components found " +
                    "inside the Host boundary — nothing to wire.");
                return;
            }

            SortByHierarchy(chain);

            rootProperty.objectReferenceValue = chain[0];
            for (int index = 0; index + 1 < chain.Count; index++)
            {
                SerializedObject upstream = new SerializedObject(chain[index]);
                SerializedProperty downstream = upstream.FindProperty(
                    "downstreamRestoreBinding");
                if (downstream != null)
                {
                    downstream.objectReferenceValue = chain[index + 1];
                    upstream.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Drop any stale downstream on the tail.
            SerializedObject tail = new SerializedObject(chain[chain.Count - 1]);
            SerializedProperty tailDownstream = tail.FindProperty(
                "downstreamRestoreBinding");
            if (tailDownstream != null)
            {
                tailDownstream.objectReferenceValue = null;
                tail.ApplyModifiedPropertiesWithoutUndo();
            }

            Debug.Log("[CoCoFlow] Restore chain wired: " +
                string.Join(" -> ", chain.ConvertAll(c => c.GetType().Name)));
        }

        private static void SortByHierarchy(List<MonoBehaviour> components)
        {
            components.Sort((left, right) =>
            {
                string leftPath = BuildHierarchyPath(left.transform);
                string rightPath = BuildHierarchyPath(right.transform);
                int order = string.CompareOrdinal(leftPath, rightPath);
                if (order != 0)
                {
                    return order;
                }

                return string.CompareOrdinal(
                    left.GetType().Name,
                    right.GetType().Name);
            });
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            var path = new StringBuilder(transform.name);
            Transform parent = transform.parent;
            while (parent != null)
            {
                path.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return path.ToString();
        }

        // ----- list drawing -----

        private void DrawHookList(
            SerializedProperty array,
            string label,
            Func<MonoBehaviour, bool> validity,
            Func<MonoBehaviour, string> describe,
            Func<IEnumerable<MonoBehaviour>> sceneScan,
            Func<MonoBehaviour, string> warning)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            for (int index = 0; index < array.arraySize; index++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(index);
                using (new EditorGUILayout.HorizontalScope())
                {
                    MonoBehaviour value = element.objectReferenceValue as MonoBehaviour;
                    EditorGUI.BeginChangeCheck();
                    UnityEngine.Object picked = EditorGUILayout.ObjectField(
                        value,
                        typeof(MonoBehaviour),
                        true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        element.objectReferenceValue = picked;
                    }

                    if (GUILayout.Button("×", GUILayout.Width(22f)))
                    {
                        array.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                MonoBehaviour current = element.objectReferenceValue as MonoBehaviour;
                if (current == null)
                {
                    continue;
                }

                if (!validity(current))
                {
                    EditorGUILayout.HelpBox(
                        describe(current) + " — it will be rejected at startup.",
                        MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField(describe(current), EditorStyles.miniLabel);
                    if (warning != null)
                    {
                        string message = warning(current);
                        if (message != null)
                        {
                            EditorGUILayout.HelpBox(message, MessageType.Warning);
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add from scene…", EditorStyles.miniButton))
                {
                    ShowSceneMenu(array, sceneScan, describe);
                }

                EditorGUILayout.LabelField(
                    "drop components above or pick from the scene",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);
        }

        private static void ShowSceneMenu(
            SerializedProperty array,
            Func<IEnumerable<MonoBehaviour>> sceneScan,
            Func<MonoBehaviour, string> describe)
        {
            var menu = new GenericMenu();
            var present = new HashSet<UnityEngine.Object>();
            for (int index = 0; index < array.arraySize; index++)
            {
                UnityEngine.Object existing = array.GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (existing != null)
                {
                    present.Add(existing);
                }
            }

            bool any = false;
            foreach (MonoBehaviour component in sceneScan())
            {
                if (component == null || present.Contains(component))
                {
                    continue;
                }

                any = true;
                MonoBehaviour captured = component;
                menu.AddItem(
                    new GUIContent(describe(captured) + " @ " + captured.name),
                    false,
                    () =>
                    {
                        int next = array.arraySize;
                        array.InsertArrayElementAtIndex(next);
                        array.GetArrayElementAtIndex(next).objectReferenceValue =
                            captured;
                        array.serializedObject.ApplyModifiedProperties();
                    });
            }

            if (!any)
            {
                menu.AddDisabledItem(
                    new GUIContent("no matching components in this scene"));
            }

            menu.ShowAsContext();
        }
    }
}
