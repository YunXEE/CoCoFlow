using System;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.Modules.Persistence
{
    [CustomEditor(typeof(PersistenceContext))]
    internal sealed class PersistenceContextEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var context = (PersistenceContext)target;
            var root = new VisualElement();
            var script = new PropertyField(serializedObject.FindProperty("m_Script"));
            script.SetEnabled(false);
            root.Add(script);
            var locked = new PropertyField(serializedObject.FindProperty("idLocked"), "Lock ID");
            var id = new PropertyField(serializedObject.FindProperty("stableEntityId"), "Stable Entity ID");
            root.Add(locked);
            root.Add(id);
            root.Add(new PropertyField(serializedObject.FindProperty("prefabKey"), "Prefab Key"));
            root.Add(new HelpBox("Scan is read-only. Unlock and regenerate on the owning Prefab asset. " +
                                 "Regeneration changes its save identity; existing save IDs are not migrated.",
                HelpBoxMessageType.Info));
            root.Add(new Button(() => Run(() =>
            {
                var report = PersistenceContextPrefabIds.Scan();
                report.Log();
                string conflicts = report.Conflicts(context);
                if (!string.IsNullOrEmpty(conflicts))
                    EditorUtility.DisplayDialog("Duplicate Persistence ID",
                        $"'{context.StableEntityId}' is also used by:\n\n{conflicts}", "OK");
            })) { text = "Scan" });
            var regenerate = new Button(() => Run(() =>
            {
                PersistenceContextPrefabIds.Regenerate(context);
                serializedObject.Update();
            })) { text = "Regenerate ID" };
            root.Add(regenerate);
            void Refresh()
            {
                bool editable = PersistenceContextPrefabIds.CanEdit(context);
                bool isLocked = serializedObject.FindProperty("idLocked").boolValue;
                locked.SetEnabled(editable);
                id.SetEnabled(editable && !isLocked);
                regenerate.SetEnabled(editable && !isLocked);
            }
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            Refresh();
            return root;
        }

        private static void Run(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                Debug.LogError("[PersistenceContext] " + exception.Message);
                EditorUtility.DisplayDialog("Persistence ID", exception.Message, "OK");
            }
        }
    }
}
