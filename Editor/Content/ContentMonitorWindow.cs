using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Editor.Content
{
    [MovedFrom(true, sourceClassName: "CoCoContentMonitorWindow")]
    internal sealed class ContentMonitorWindow : EditorWindow
    {
        private readonly ContentRuntimeMonitorView monitor =
            new ContentRuntimeMonitorView();
        private ContentHost host;
        private bool followSelection = true;

        [MenuItem("Window/CoCoFlow/Content Monitor")]
        internal static void OpenFromMenu()
        {
            ContentMonitorWindow window = GetWindow<ContentMonitorWindow>();
            window.titleContent = new GUIContent("Content Monitor");
            window.minSize = new Vector2(500f, 420f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(ContentHost selectedHost)
        {
            ContentMonitorWindow window = GetWindow<ContentMonitorWindow>();
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
                host = (ContentHost)EditorGUILayout.ObjectField(
                    host,
                    typeof(ContentHost),
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
                    "Select or assign one ContentHost to inspect its immutable snapshots.",
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
                : selected.GetComponentInParent<ContentHost>(true);
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
