using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Content
{
    internal sealed class CoCoContentMonitorWindow : EditorWindow
    {
        private readonly ContentRuntimeMonitorView monitor =
            new ContentRuntimeMonitorView();
        private CoCoContentHost host;
        private bool followSelection = true;

        [MenuItem("Window/CoCoFlow/Content Monitor")]
        internal static void OpenFromMenu()
        {
            CoCoContentMonitorWindow window = GetWindow<CoCoContentMonitorWindow>();
            window.titleContent = new GUIContent("Content Monitor");
            window.minSize = new Vector2(500f, 420f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(CoCoContentHost selectedHost)
        {
            CoCoContentMonitorWindow window = GetWindow<CoCoContentMonitorWindow>();
            window.titleContent = new GUIContent("Content Monitor");
            window.minSize = new Vector2(500f, 420f);
            window.host = selectedHost;
            window.followSelection = false;
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                host = (CoCoContentHost)EditorGUILayout.ObjectField(
                    host,
                    typeof(CoCoContentHost),
                    true,
                    GUILayout.MinWidth(220f));
                bool follow = GUILayout.Toggle(
                    followSelection,
                    "Follow Selection",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(110f));
                if (follow != followSelection)
                {
                    followSelection = follow;
                    if (followSelection)
                    {
                        TryFollowSelection();
                    }
                }
            }

            if (host == null)
            {
                EditorGUILayout.HelpBox(
                    "Select or assign one CoCoContentHost to inspect its immutable snapshots.",
                    MessageType.Info);
                return;
            }

            DrawHostDiagnostic(host.LastDiagnostic);
            monitor.Draw(host.Runtime);
        }

        private void OnSelectionChange()
        {
            if (followSelection)
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
            host = selected == null
                ? null
                : selected.GetComponentInParent<CoCoContentHost>(true);
        }

        private static void DrawHostDiagnostic(CoCoDiagnostic diagnostic)
        {
            if (diagnostic.IsNone)
            {
                return;
            }

            MessageType messageType = diagnostic.IsError
                ? MessageType.Error
                : diagnostic.IsWarning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(
                "Host: " + diagnostic.Domain + "/" + diagnostic.Code + ": " +
                diagnostic.Message,
                messageType);
        }
    }
}
