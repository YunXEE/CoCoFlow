#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// Generates a fully compilable state logic script from a name:
    /// CoCoState attribute, CoCoIntentConsume(RawInputIntent), class shell
    /// with an empty Update. The user pastes logic into Update only. The
    /// descriptor id is derived from the name deterministically, so no
    /// registration file is required (bootstrap scans attributes).
    /// </summary>
    internal static class CoCoStateScriptWizard
    {
        private const string DefaultFolder = "Assets/Scripts/States";

        internal static string CreateStateScriptFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Name is required.";
            }

            name = name.Trim();
            if (!IsValidName(name))
            {
                return "'" + name + "' is not a valid PascalCase identifier.";
            }

            string folder = EditorPrefs.GetString(
                "CoCoFlow.StateScriptFolder",
                DefaultFolder);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                CreateFolderRecursive("Assets", folder.Substring("Assets/".Length));
            }

            string path = folder + "/" + name + "Logic.cs";
            File.WriteAllText(path, BuildScript(name), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();

            // ensure the generated script lands in an engine-free assembly:
            // require/create a state graph asmdef folder when none exists.
            EnsureGraphAssembly(folder);

            CoCoStandardCatalogBootstrap.Rescan();
            EditorSceneManager.MarkSceneDirty(
                EditorSceneManager.GetActiveScene());
            Debug.Log("[CoCoFlow] state script generated: " + path +
                      " — paste your logic into Update().");
            return null;
        }

        internal static string TryCreate(string name) => CreateStateScriptFromName(name);

        internal static string BuildScript(string name)
        {
            return
                "using CoCoFlow.Runtime.Core;\n" +
                "\n" +
                "// State logic for the '" + name + "' graph state. Everything " +
                "outside Update was generated; paste your logic into Update.\n" +
                "[CoCoState(\"" + name + "\")]\n" +
                "[CoCoIntentConsume(typeof(RawInputIntent))]\n" +
                "public sealed class " + name + "Logic : CoCoStateLogic, ICoCoStateUpdate\n" +
                "{\n" +
                "    public void Update(CoCoStateExecutionContext context)\n" +
                "    {\n" +
                "        // TODO: your logic here\n" +
                "    }\n" +
                "}\n";
        }

        private static bool IsValidName(string name)
        {
            if (char.IsDigit(name[0]))
            {
                return false;
            }

            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureGraphAssembly(string folder)
        {
            string asmdef = folder + "/StateGraphScripts.asmdef";
            if (File.Exists(asmdef) ||
                FolderHasAncestorAsmdef(folder))
            {
                return;
            }

            File.WriteAllText(asmdef,
                "{\n" +
                "    \"name\": \"StateGraphScripts\",\n" +
                "    \"rootNamespace\": \"\",\n" +
                "    \"references\": [\n" +
                "        \"CoCoFlow.Runtime.Core.Contracts\",\n" +
                "        \"CoCoFlow.Runtime.Core.StateFlow\",\n" +
                "        \"CoCoFlow.Runtime.Core.StateGraph\",\n" +
                "        \"CoCoFlow.Runtime.Locomotion.Contracts\"\n" +
                "    ],\n" +
                "    \"includePlatforms\": [],\n" +
                "    \"excludePlatforms\": [],\n" +
                "    \"allowUnsafeCode\": false,\n" +
                "    \"overrideReferences\": true,\n" +
                "    \"precompiledReferences\": [],\n" +
                "    \"autoReferenced\": true,\n" +
                "    \"defineConstraints\": [],\n" +
                "    \"versionDefines\": [],\n" +
                "    \"noEngineReferences\": true\n" +
                "}\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(asmdef);
            AssetDatabase.Refresh();
            Debug.Log("[CoCoFlow] engine-free state assembly created: " + asmdef);
        }

        private static bool FolderHasAncestorAsmdef(string folder)
        {
            string current = folder;
            while (!string.IsNullOrEmpty(current) && current != "Assets")
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:AssemblyDefinitionAsset",
                    new[] { current });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path) == current)
                    {
                        return true;
                    }
                }

                current = Path.GetDirectoryName(current)?.Replace('\\', '/');
            }

            return false;
        }

        private static void CreateFolderRecursive(string parent, string remainder)
        {
            if (string.IsNullOrEmpty(remainder))
            {
                return;
            }

            int slash = remainder.IndexOf('/');
            string folder = slash < 0 ? remainder : remainder.Substring(0, slash);
            string next = slash < 0 ? string.Empty : remainder.Substring(slash + 1);
            string full = parent + "/" + folder;
            if (!AssetDatabase.IsValidFolder(full))
            {
                AssetDatabase.CreateFolder(parent, folder);
            }

            CreateFolderRecursive(full, next);
        }

            }

}
#endif
