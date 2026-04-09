using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace CoCoFlow.Editor.AssetPipeline
{
    public class SmartPrefabExporter : EditorWindow
    {
        private string _exportDirectory = "D:/CoCoFlow_Exports"; // 建议设为工程外的一个固定中转文件夹
        private string _packageName = "Environment_Trees_01";

        [MenuItem("CoCoFlow/AssetPipeline/智能Prefab导出器（不推荐主工程使用）")]
        public static void ShowWindow()
        {
            var window = GetWindow<SmartPrefabExporter>("智能Prefab导出器");
            window.minSize = new Vector2(400, 200);
        }

        private void OnGUI()
        {
            GUILayout.Label("CoCoFlow 跨工程传输工具", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("请在 Project 窗口中选中你已经清洗完毕的 Prefab，工具将自动收集其依赖的贴图和模型，并打包为 .unitypackage 以供主工程使用。",
                MessageType.Info);

            EditorGUILayout.Space();
            _exportDirectory = EditorGUILayout.TextField("导出存放目录", _exportDirectory);
            _packageName = EditorGUILayout.TextField("数据包名称", _packageName);

            EditorGUILayout.Space();

            // 获取当前选中的资产
            Object[] selectedAssets = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);

            if (selectedAssets.Length == 0)
            {
                EditorGUILayout.HelpBox("请在下方 Project 窗口中至少选中一个资源。", MessageType.Warning);
                GUI.enabled = false;
            }

            if (GUILayout.Button($"打包选中的 {selectedAssets.Length} 个资产及其依赖", GUILayout.Height(40)))
            {
                ExecuteExport(selectedAssets);
            }

            GUI.enabled = true;
        }

        private void ExecuteExport(Object[] selectedAssets)
        {
            if (!Directory.Exists(_exportDirectory))
            {
                Directory.CreateDirectory(_exportDirectory);
            }

            string exportPath = Path.Combine(_exportDirectory, _packageName + ".unitypackage");

            // 收集选中资产的路径
            List<string> assetPaths = new List<string>();
            foreach (var asset in selectedAssets)
            {
                assetPaths.Add(AssetDatabase.GetAssetPath(asset));
            }

            // 核心魔法：ExportPackageOptions.IncludeDependencies 会自动把贴图和 FBX 卷进去
            AssetDatabase.ExportPackage(assetPaths.ToArray(), exportPath, ExportPackageOptions.IncludeDependencies | ExportPackageOptions.Interactive);

            Debug.Log($"<color=cyan>[CoCoFlow]</color> 成功导出依赖包至: <b>{exportPath}</b>");

            // 导出完成后自动在 Windows 资源管理器中打开该文件夹
            EditorUtility.RevealInFinder(exportPath);
        }
    }
}
