using UnityEngine;
using UnityEditor;
using System.IO;

namespace CoCoFlow.Editor.AssetPipeline
{
    public class TextureCompressor : EditorWindow
    {
        /// <summary>
        /// CoCoFlow Asset Pipeline: 纹理压缩器
        /// 目前仅设计针对PC端
        /// </summary>
        private string _targetPath = "Assets/Art";
        
        // 压缩策略配置
        private bool _forceBC7 = true;
        private int _maxTextureSize = 2048; 
        private bool _fixNormalMaps = true;
        private bool _fixMasks_sRGB = true; 

        [MenuItem("CoCoFlow/AssetPipeline/纹理压缩器")]
        public static void ShowWindow()
        {
            var window = GetWindow<TextureCompressor>("纹理压缩器");
            window.minSize = new Vector2(400, 350);
        }

        private void OnGUI()
        {
            GUILayout.Label("CoCoFlow 纹理原位压缩与格式化工具", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("警告：该工具将直接修改指定目录下所有贴图的导入设置（.meta）。重新导入大量贴图可能需要数分钟甚至更久。", MessageType.Warning);

            EditorGUILayout.Space();
            _targetPath = EditorGUILayout.TextField("目标文件夹", _targetPath);

            EditorGUILayout.Space();
            GUILayout.Label("压缩独裁策略：", EditorStyles.boldLabel);
            _forceBC7 = EditorGUILayout.ToggleLeft("PC 端强制使用 BC7 (高质量/高压缩率)", _forceBC7);
            _maxTextureSize = EditorGUILayout.IntPopup("强制最大分辨率 (Max Size)", _maxTextureSize, 
                new string[] { "1024", "2048", "4096" }, 
                new int[] { 1024, 2048, 4096 });

            EditorGUILayout.Space();
            GUILayout.Label("规范化纠正策略：", EditorStyles.boldLabel);
            _fixNormalMaps = EditorGUILayout.ToggleLeft("自动识别并设置 Normal Map (基于命名)", _fixNormalMaps);
            _fixMasks_sRGB = EditorGUILayout.ToggleLeft("自动识别 Mask/Metallic/AO 并关闭 sRGB", _fixMasks_sRGB);

            EditorGUILayout.Space();
            if (GUILayout.Button("开始压缩 (不可撤销)", GUILayout.Height(40)))
            {
                ExecuteCompression();
            }
        }

        private void ExecuteCompression()
        {
            string[] allTextureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { _targetPath });
            int total = allTextureGuids.Length;
            int processedCount = 0;

            if (total == 0)
            {
                EditorUtility.DisplayDialog("提示", "未找到任何纹理贴图。", "确定");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing(); // 暂停自动导入，提升批量处理性能

                for (int i = 0; i < total; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(allTextureGuids[i]);
                    string fileName = Path.GetFileNameWithoutExtension(path).ToLower();

                    EditorUtility.DisplayProgressBar("纹理清洗中", $"正在处理: {fileName}", (float)i / total);

                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    bool needReimport = false;

                    // 法线贴图
                    if (_fixNormalMaps && (fileName.Contains("normal") || fileName.Contains("nrm") || fileName.Contains("norm")))
                    {
                        if (importer.textureType != TextureImporterType.NormalMap)
                        {
                            importer.textureType = TextureImporterType.NormalMap;
                            needReimport = true;
                        }
                    }

                    // 遮罩图 (Mask/Metallic/Roughness/AO) 关闭 sRGB
                    if (_fixMasks_sRGB && importer.textureType == TextureImporterType.Default)
                    {
                        if (fileName.Contains("mask") || fileName.Contains("metallic") || 
                            fileName.Contains("roughness") || fileName.Contains("ao") || fileName.Contains("ambient"))
                        {
                            if (importer.sRGBTexture)
                            {
                                importer.sRGBTexture = false;
                                needReimport = true;
                            }
                        }
                    }

                    // 压缩设置：PC平台设置
                    TextureImporterPlatformSettings pcSettings = importer.GetPlatformTextureSettings("Standalone");
                    
                    if (!pcSettings.overridden || pcSettings.maxTextureSize != _maxTextureSize || pcSettings.format != TextureImporterFormat.BC7)
                    {
                        pcSettings.overridden = true;
                        pcSettings.maxTextureSize = _maxTextureSize;
                        
                        if (_forceBC7)
                        {
                            pcSettings.format = TextureImporterFormat.BC7;
                        }
                        
                        importer.SetPlatformTextureSettings(pcSettings);
                        needReimport = true;
                    }

                    // 如果有改动，则保存设置
                    if (needReimport)
                    {
                        importer.SaveAndReimport();
                        processedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing(); // 恢复自动导入
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("清洗完成", $"成功处理并重新导入了 {processedCount} / {total} 张贴图。\nBC7 和 sRGB 规则已应用。", "确定");
            }
        }
    }
}