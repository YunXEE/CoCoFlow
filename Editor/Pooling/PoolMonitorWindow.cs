using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Editor.Pooling
{
    [MovedFrom(true, sourceClassName: "CoCoPoolMonitorWindow")]
    internal sealed class PoolMonitorWindow : EditorWindow
    {
        private readonly PoolRuntimeMonitorView monitor =
            new PoolRuntimeMonitorView();
        private PoolHost host;
        private bool followSelection = true;

        [MenuItem("Window/CoCoFlow/Pool Monitor")]
        internal static void OpenFromMenu()
        {
            PoolMonitorWindow window = GetWindow<PoolMonitorWindow>();
            window.titleContent = new GUIContent("Pool Monitor");
            window.minSize = new Vector2(580f, 460f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(PoolHost selectedHost)
        {
            PoolMonitorWindow window = GetWindow<PoolMonitorWindow>();
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
                host = (PoolHost)EditorGUILayout.ObjectField(
                    host,
                    typeof(PoolHost),
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
                    "Select or assign one PoolHost to inspect immutable snapshots.",
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
                : selected.GetComponentInParent<PoolHost>(true);
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
