using System;
using System.IO;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    internal sealed class CoCoRegionProfileTemplateWizard : EditorWindow
    {
        private const string TemplateGuid =
            "7a04045d8302471a8dd3bb4b57041104";

        private string status = string.Empty;
        private MessageType statusType = MessageType.None;

        [MenuItem("CoCoFlow/Map/Create Default Region Profile")]
        internal static void Open()
        {
            CoCoRegionProfileTemplateWizard window =
                GetWindow<CoCoRegionProfileTemplateWizard>(true);
            window.titleContent =
                new GUIContent("Region Profile Template");
            window.minSize = new Vector2(470f, 190f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Default Region Fidelity Profile",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The package template is read-only. The Wizard creates an ordinary " +
                "project-owned CoCoRegionProfile under Assets so its five tiers and " +
                "participant matrix can be customized safely.",
                MessageType.Info);

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    "Copy Template to Project…",
                    GUILayout.Height(30f)))
            {
                CopyTemplate();
            }
        }

        private void CopyTemplate()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Copy Default Region Profile",
                "CoCoRegionProfile",
                "asset",
                "Choose a project-owned location for the copied Region Profile.");
            if (string.IsNullOrEmpty(path)) return;

            if (TryCreateProfileAtPath(
                    path,
                    out CoCoRegionProfile profile,
                    out string failure))
            {
                status =
                    "Created project-owned Region Profile at " + path + ".";
                statusType = MessageType.Info;
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
                Close();
                return;
            }

            status = failure;
            statusType = MessageType.Error;
        }

        internal static bool TryCreateProfileAtPath(
            string path,
            out CoCoRegionProfile profile,
            out string failure)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetExtension(path),
                    ".asset",
                    StringComparison.OrdinalIgnoreCase))
            {
                failure =
                    "Region Profile copies must be .asset files under Assets/.";
                return false;
            }

            string templatePath =
                AssetDatabase.GUIDToAssetPath(TemplateGuid);
            TextAsset template =
                AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath);
            if (template == null ||
                string.IsNullOrWhiteSpace(template.text))
            {
                failure =
                    "The package default Region Profile template is unavailable.";
                return false;
            }

            string uniquePath =
                AssetDatabase.GenerateUniqueAssetPath(path);
            CoCoRegionProfile created =
                CreateInstance<CoCoRegionProfile>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(
                    template.text,
                    created);
                AssetDatabase.CreateAsset(created, uniquePath);
                EditorUtility.SetDirty(created);
                AssetDatabase.SaveAssetIfDirty(created);
                profile = created;
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                if (!string.IsNullOrEmpty(
                        AssetDatabase.GetAssetPath(created)))
                {
                    AssetDatabase.DeleteAsset(uniquePath);
                }
                else
                {
                    DestroyImmediate(created);
                }

                failure =
                    "The Region Profile template could not be copied: " +
                    exception.Message;
                return false;
            }
        }
    }
}
