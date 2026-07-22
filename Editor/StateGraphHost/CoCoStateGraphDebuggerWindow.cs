using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    internal sealed class CoCoStateGraphDebuggerWindow : EditorWindow
    {
        private readonly CoCoStateGraphHostDebuggerView _debugger =
            new CoCoStateGraphHostDebuggerView();
        private CoCoStateGraphHost _host;
        private bool _followSelection = true;

        [MenuItem("Window/CoCoFlow/StateGraph Debugger")]
        internal static void OpenFromMenu()
        {
            CoCoStateGraphDebuggerWindow window = GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(CoCoStateGraphHost host)
        {
            CoCoStateGraphDebuggerWindow window = GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window._host = host;
            window._followSelection = false;
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _host = (CoCoStateGraphHost)EditorGUILayout.ObjectField(
                    _host,
                    typeof(CoCoStateGraphHost),
                    true,
                    GUILayout.MinWidth(180f));
                bool follow = GUILayout.Toggle(
                    _followSelection,
                    "Follow Selection",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(110f));
                if (follow != _followSelection)
                {
                    _followSelection = follow;
                    if (_followSelection)
                    {
                        TryFollowSelection();
                    }
                }
            }

            _debugger.Draw(_host);
        }

        private void OnSelectionChange()
        {
            if (_followSelection)
            {
                TryFollowSelection();
                Repaint();
            }
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void TryFollowSelection()
        {
            GameObject selected = Selection.activeGameObject;
            _host = selected == null
                ? null
                : selected.GetComponentInParent<CoCoStateGraphHost>(true);
        }
    }
}
