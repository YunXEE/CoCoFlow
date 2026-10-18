using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    [CustomEditor(typeof(PersistenceContainerHost))]
    public sealed class PersistenceContainerHostEditor : UnityEditor.Editor
    {
        private bool _showTables;
        private Vector2 _scroll;

        public override void OnInspectorGUI()
        {
            var host = (PersistenceContainerHost)target;
            KeyValuePair<Type, int>[] tables = host.GetTableCounts().OrderBy(pair => pair.Key.FullName, StringComparer.Ordinal).ToArray();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(host), typeof(MonoScript), false);
                EditorGUILayout.Toggle("Active Global Host", PersistenceContainerHost.ActiveHost == host);
                EditorGUILayout.ObjectField("Active Persistence Host", PersistenceHost.ActiveHost, typeof(PersistenceHost), true);
                EditorGUILayout.IntField("Data Tables", host.TableCount);
                EditorGUILayout.IntField("Data Rows", tables.Sum(pair => pair.Value));
                EditorGUILayout.IntField("Bound Containers", host.BoundContainerCount);
            }
            if (!Application.isPlaying)
                EditorGUILayout.LabelField("Tables and bindings are populated at runtime.", EditorStyles.wordWrappedMiniLabel);
            _showTables = EditorGUILayout.Foldout(_showTables, "Table Details", true);
            if (!_showTables) return;
            if (tables.Length == 0)
            {
                EditorGUILayout.LabelField("No registered data tables.", EditorStyles.miniLabel);
                return;
            }
            const float rowHeight = 22;
            Rect position = EditorGUILayout.GetControlRect(false, Mathf.Min(220, tables.Length * rowHeight));
            float width = position.width - 18;
            _scroll = GUI.BeginScrollView(position, _scroll, new Rect(0, 0, width, tables.Length * rowHeight), false, true);
            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / rowHeight));
            int last = Mathf.Min(tables.Length, first + Mathf.CeilToInt(position.height / rowHeight) + 1);
            for (int i = first; i < last; i++)
            {
                var row = new Rect(0, i * rowHeight, width, rowHeight);
                GUI.Label(row, new GUIContent(tables[i].Key.Name + "  —  " + tables[i].Value + " rows", tables[i].Key.FullName));
            }
            GUI.EndScrollView();
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
