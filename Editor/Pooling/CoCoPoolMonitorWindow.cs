using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Pooling
{
    internal sealed class CoCoPoolMonitorWindow : EditorWindow
    {
        private readonly PoolRuntimeMonitorView monitor =
            new PoolRuntimeMonitorView();
        private CoCoPoolHost host;
        private bool followSelection = true;

        [MenuItem("Window/CoCoFlow/Pool Monitor")]
        internal static void OpenFromMenu()
        {
            CoCoPoolMonitorWindow window = GetWindow<CoCoPoolMonitorWindow>();
            window.titleContent = new GUIContent("Pool Monitor");
            window.minSize = new Vector2(580f, 460f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(CoCoPoolHost selectedHost)
        {
            CoCoPoolMonitorWindow window = GetWindow<CoCoPoolMonitorWindow>();
            window.titleContent = new GUIContent("Pool Monitor");
            window.minSize = new Vector2(580f, 460f);
            window.host = selectedHost;
            window.followSelection = false;
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                host = (CoCoPoolHost)EditorGUILayout.ObjectField(
                    host,
                    typeof(CoCoPoolHost),
                    true,
                    GUILayout.MinWidth(240f));
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
                    "Select or assign one CoCoPoolHost to inspect immutable snapshots.",
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
                : selected.GetComponentInParent<CoCoPoolHost>(true);
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
