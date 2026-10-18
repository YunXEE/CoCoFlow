using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    internal sealed class PersistenceContainerTypePicker : PopupWindowContent
    {
        internal sealed class Entry
        {
            internal string Id { get; }
            internal string Label { get; }
            internal string Detail { get; }
            internal string SearchText { get; }

            internal Entry(Type type, string label)
            {
                Id = type == null ? string.Empty : PersistenceDataTypeRegistry.GetTypeId(type);
                Label = label;
                Detail = type == null ? "Clear the selected data struct." : type.FullName + "\nAssembly: " + type.Assembly.GetName().Name;
                SearchText = type == null ? string.Empty : label + " " + Detail;
            }
        }

        private const float RowHeight = 22f;
        private readonly Entry[] _entries;
        private readonly string _currentId;
        private readonly Action<string> _onSelected;
        private readonly float _width;
        private Entry[] _filtered;
        private SearchField _searchField;
        private GUIStyle _rowStyle;
        private GUIStyle _selectedRowStyle;
        private string _query = string.Empty;
        private Vector2 _scroll;
        private int _activeIndex;

        internal PersistenceContainerTypePicker(Entry[] entries, string currentId, Action<string> onSelected, float width)
        {
            _entries = _filtered = entries;
            _currentId = currentId;
            _onSelected = onSelected;
            _width = Mathf.Clamp(width, 360f, 560f);
            _activeIndex = Mathf.Max(0, Array.FindIndex(entries, entry => entry.Id == currentId));
        }

        public override Vector2 GetWindowSize() => new Vector2(_width, 360f);

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
            _searchField = new SearchField();
            _searchField.SetFocus();
            _rowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(24, 6, 0, 0), clipping = TextClipping.Clip };
            _selectedRowStyle = new GUIStyle(EditorStyles.whiteLabel) { padding = new RectOffset(24, 6, 0, 0), clipping = TextClipping.Clip };
            RevealActiveRow(248f);
        }

        public override void OnGUI(Rect rect)
        {
            var listRect = new Rect(4, 36, rect.width - 8, rect.height - 112);
            if (HandleKeyboard(listRect.height)) return;
            string next = _searchField.OnGUI(new Rect(8, 8, rect.width - 16, 20), _query);
            if (next != _query)
            {
                string activeId = _activeIndex >= 0 ? _filtered[_activeIndex].Id : _currentId;
                _query = next;
                _filtered = Filter(_entries, _query);
                _activeIndex = _filtered.Length == 0 ? -1 : Mathf.Max(0, Array.FindIndex(_filtered, entry => entry.Id == activeId));
                _scroll = Vector2.zero;
                RevealActiveRow(listRect.height);
            }

            Entry clicked = null;
            if (_filtered.Length == 0)
                GUI.Label(listRect, "No matching data structs", EditorStyles.centeredGreyMiniLabel);
            else
            {
                float width = listRect.width - 18;
                _scroll = GUI.BeginScrollView(listRect, _scroll, new Rect(0, 0, width, _filtered.Length * RowHeight), false, true);
                // Draw only the visible rows, including when thousands of structs are available.
                int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / RowHeight));
                int last = Mathf.Min(_filtered.Length, first + Mathf.CeilToInt(listRect.height / RowHeight) + 1);
                for (int i = first; i < last; i++)
                {
                    Entry entry = _filtered[i];
                    var row = new Rect(0, i * RowHeight, width, RowHeight);
                    if (Event.current.type == EventType.MouseMove && row.Contains(Event.current.mousePosition))
                    {
                        _activeIndex = i;
                        editorWindow.Repaint();
                    }
                    bool active = i == _activeIndex;
                    if (active) EditorGUI.DrawRect(row, EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.24f, 0.49f, 0.9f));
                    if (GUI.Button(row, new GUIContent(entry.Label, entry.Detail), active ? _selectedRowStyle : _rowStyle))
                        clicked = entry;
                    if (entry.Id == _currentId)
                        GUI.Label(new Rect(6, row.y, 18, RowHeight), "✓", active ? EditorStyles.whiteLabel : EditorStyles.label);
                }
                GUI.EndScrollView();
            }

            if (clicked != null) { Select(clicked); return; }
            EditorGUI.DrawRect(new Rect(8, rect.height - 70, rect.width - 16, 1), new Color(0.5f, 0.5f, 0.5f, 0.3f));
            string detail = _activeIndex < 0 ? "Search by type name, namespace or assembly." : _filtered[_activeIndex].Detail;
            GUI.Label(new Rect(8, rect.height - 64, rect.width - 16, 60), new GUIContent(detail, detail), EditorStyles.wordWrappedMiniLabel);
        }

        private bool HandleKeyboard(float height)
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown) return false;
            if (current.keyCode == KeyCode.Escape)
            {
                current.Use();
                editorWindow.Close();
                return true;
            }
            if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            {
                current.Use();
                if (_activeIndex >= 0) Select(_filtered[_activeIndex]);
                return true;
            }
            if (current.keyCode != KeyCode.UpArrow && current.keyCode != KeyCode.DownArrow) return false;
            _activeIndex = _filtered.Length == 0 ? -1 : Mathf.Clamp(_activeIndex + (current.keyCode == KeyCode.DownArrow ? 1 : -1), 0, _filtered.Length - 1);
            RevealActiveRow(height);
            current.Use();
            editorWindow.Repaint();
            return false;
        }

        private void RevealActiveRow(float height)
        {
            if (_activeIndex < 0) return;
            _scroll.y = Mathf.Clamp(_scroll.y, Mathf.Max(0, (_activeIndex + 1) * RowHeight - height), _activeIndex * RowHeight);
        }

        private void Select(Entry entry)
        {
            _onSelected(entry.Id);
            editorWindow.Close();
        }

        internal static GUIContent GetButtonContent(Entry[] entries, string selectedId)
        {
            if (string.IsNullOrEmpty(selectedId)) return new GUIContent("Select a data struct");
            Entry selected = Array.Find(entries, entry => entry.Id == selectedId);
            return selected == null ? new GUIContent("Missing data struct", selectedId) : new GUIContent(selected.Label, selected.Detail);
        }

        internal static Entry[] Filter(Entry[] entries, string query)
        {
            string[] terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return terms.Length == 0 ? entries : entries.Where(entry => entry.Id.Length != 0 &&
                terms.All(term => entry.SearchText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        }

        internal static Entry[] CreateEntries(IEnumerable<Type> types)
        {
            var entries = new List<Entry> { new Entry(null, "None") };
            foreach (IGrouping<string, Type> group in types.GroupBy(ShortName))
            {
                Type[] matches = group.ToArray();
                foreach (Type type in matches)
                {
                    string label = group.Key;
                    if (matches.Length > 1)
                    {
                        string[] parts = (type.Namespace ?? "Global").Split('.');
                        string suffix = parts[parts.Length - 1];
                        for (int count = 1; count <= parts.Length; count++)
                        {
                            suffix = string.Join(".", parts.Skip(parts.Length - count));
                            if (matches.All(other => other.Namespace == type.Namespace || !NamespaceEndsWith(other, suffix))) break;
                        }
                        if (matches.Any(other => other != type && other.Namespace == type.Namespace))
                            suffix += " / " + type.Assembly.GetName().Name;
                        label += " (" + suffix + ")";
                    }
                    entries.Add(new Entry(type, label));
                }
            }
            return entries.Take(1).Concat(entries.Skip(1).OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)).ToArray();
        }

        private static string ShortName(Type type) => type.FullName!.Substring(string.IsNullOrEmpty(type.Namespace) ? 0 : type.Namespace.Length + 1).Replace('+', '.');
        private static bool NamespaceEndsWith(Type type, string suffix) =>
            (type.Namespace ?? "Global") == suffix || (type.Namespace ?? "Global").EndsWith("." + suffix, StringComparison.Ordinal);
    }
}
