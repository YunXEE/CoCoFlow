using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    internal sealed class CoCoMapMonitorWindow :
        EditorWindow
    {
        private readonly RegionRuntimeMonitorView monitor =
            new RegionRuntimeMonitorView();
        private CoCoMapHost host;
        private bool followSelection = true;

        [MenuItem("Window/CoCoFlow/Map Region Monitor")]
        internal static void OpenFromMenu()
        {
            CoCoMapMonitorWindow window =
                GetWindow<CoCoMapMonitorWindow>();
            window.titleContent =
                new GUIContent("Map Region Monitor");
            window.minSize = new Vector2(620f, 500f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(
            CoCoMapHost selectedHost)
        {
            CoCoMapMonitorWindow window =
                GetWindow<CoCoMapMonitorWindow>();
            window.titleContent =
                new GUIContent("Map Region Monitor");
            window.minSize = new Vector2(620f, 500f);
            window.host = selectedHost;
            window.followSelection = false;
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(
                       EditorStyles.toolbar))
            {
                host = (CoCoMapHost)
                    EditorGUILayout.ObjectField(
                        host,
                        typeof(CoCoMapHost),
                        true,
                        GUILayout.MinWidth(260f));
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

            monitor.Draw(host);
        }

        private void OnSelectionChange()
        {
            if (!followSelection) return;
            TryFollowSelection();
            Repaint();
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
            GameObject selected =
                Selection.activeGameObject;
            host = selected == null
                ? null
                : selected.GetComponentInParent<
                    CoCoMapHost>(true);
        }
    }
}
