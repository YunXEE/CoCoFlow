#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.ProjectScaffold
{
    public sealed class ProjectScaffoldWindow : EditorWindow
    {
        private string _projectRoot = ProjectScaffoldRequest.DefaultRoot;
        private ProjectScaffoldAssemblyMode _assemblyMode;
        private ProjectScaffoldPlan _plan;
        private Vector2 _scroll;
        private string _lastResult = string.Empty;

        [MenuItem("CoCoFlow/Setup/Project Scaffold")]
        public static void Open()
        {
            var window = GetWindow<ProjectScaffoldWindow>(
                "Project Scaffold");
            window.minSize = new Vector2(660f, 520f);
            window.RefreshPreview();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshPreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "CoCoFlow Project Scaffold",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Preview every output before Apply. The generator creates only missing files and never overwrites project code.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _projectRoot = EditorGUILayout.TextField(
                "Project Root",
                _projectRoot);
            _assemblyMode =
                (ProjectScaffoldAssemblyMode)EditorGUILayout.EnumPopup(
                    "Assembly Mode",
                    _assemblyMode);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshPreview();
            }

            if (GUILayout.Button("Refresh Full Preview", GUILayout.Height(26f)))
            {
                RefreshPreview();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawPreview();
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(
                       _plan == null || !_plan.CanApply))
            {
                if (GUILayout.Button(
                        "Apply Previewed Scaffold",
                        GUILayout.Height(32f)))
                {
                    Apply();
                }
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        private void DrawPreview()
        {
            if (_plan == null)
            {
                EditorGUILayout.HelpBox(
                    "No Preview is available.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Files (" + _plan.Files.Count + ")",
                EditorStyles.boldLabel);
            foreach (ProjectScaffoldFile file in _plan.Files)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    file.RelativePath,
                    EditorStyles.boldLabel);
                int lineCount = Mathf.Max(
                    4,
                    file.Content.Split('\n').Length);
                EditorGUILayout.SelectableLabel(
                    file.Content,
                    EditorStyles.textArea,
                    GUILayout.Height(Mathf.Min(280f, lineCount * 16f)));
            }

            if (_plan.ExistingProviderPaths.Count == 1)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(
                    "Existing Provider Integration",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    _plan.IntegrationGuidance,
                    MessageType.Warning);
            }

            foreach (string conflict in _plan.Conflicts)
            {
                EditorGUILayout.HelpBox(conflict, MessageType.Error);
            }
        }

        private void RefreshPreview()
        {
            _lastResult = string.Empty;
            try
            {
                _plan = ProjectScaffoldPlanner.Build(
                    new ProjectScaffoldRequest(
                        _projectRoot,
                        _assemblyMode),
                    Directory.GetCurrentDirectory());
            }
            catch (Exception exception)
            {
                _plan = null;
                _lastResult = exception.Message;
            }

            Repaint();
        }

        private void Apply()
        {
            if (_plan == null || !_plan.CanApply)
            {
                return;
            }

            string fileSummary = string.Join(
                "\n",
                Array.ConvertAll(
                    new System.Collections.Generic.List<ProjectScaffoldFile>(
                        _plan.Files).ToArray(),
                    file => file.RelativePath));
            if (!EditorUtility.DisplayDialog(
                    "Create Project Scaffold?",
                    "The following new files will be created:\n\n" +
                    fileSummary +
                    "\n\nExisting files will not be changed.",
                    "Create New Files",
                    "Cancel"))
            {
                return;
            }

            var writer = new ProjectScaffoldWriter();
            ProjectScaffoldApplyResult result =
                writer.Apply(_plan, Directory.GetCurrentDirectory());
            string resultMessage = result.Succeeded
                ? "Created " + result.CreatedPaths.Count +
                  " project scaffold file(s)."
                : result.Error;
            RefreshPreview();
            _lastResult = resultMessage;
        }
    }
}
#endif
