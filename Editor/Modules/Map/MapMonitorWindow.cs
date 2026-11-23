using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Editor.Modules.Map
{
    [MovedFrom(true, sourceClassName: "CoCoMapMonitorWindow")]
    internal sealed class MapMonitorWindow :
        EditorWindow
    {
        private readonly RegionRuntimeMonitorView monitor =
            new RegionRuntimeMonitorView();
        private MapHost host;
        private bool followSelection = true;

        [MenuItem("Window/CoCoFlow/Map Region Monitor")]
        internal static void OpenFromMenu()
        {
            MapMonitorWindow window =
                GetWindow<MapMonitorWindow>();
            window.titleContent =
                new GUIContent("Map Region Monitor");
            window.minSize = new Vector2(620f, 500f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(
            MapHost selectedHost)
        {
            MapMonitorWindow window =
                GetWindow<MapMonitorWindow>();
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
                host = (MapHost)
                    EditorGUILayout.ObjectField(
                        host,
                        typeof(MapHost),
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
                    MapHost>(true);
        }
    }
}
