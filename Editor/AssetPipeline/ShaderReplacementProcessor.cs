using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace CoCoFlow.Editor.AssetPipeline
{
    public class ShaderReplacementProcessor : EditorWindow
    {
        private string _targetFolderPath = "Assets/Art/Processed";
        private Shader _targetShader;
        private bool _autoSetupCutout = true; // 自动处理树叶的透明遮罩

        // 核心：贴图属性映射字典
        // Key: 你的目标 Shader 的属性名
        // Value: 外部资源可能使用的各种属性名（按优先级排列）
        private readonly Dictionary<string, string[]> _textureMappings = new Dictionary<string, string[]>()
        {
            { "_BaseMap", new[] { "_MainTex", "_BaseColorMap", "_Albedo", "_Color" } }, // 漫反射
            { "_BumpMap", new[] { "_BumpMap", "_NormalMap", "_Normal" } },             // 法线
            { "_MetallicGlossMap", new[] { "_MetallicGlossMap", "_MaskMap", "_Mask" } },// 遮罩/金属度
            { "_OcclusionMap", new[] { "_OcclusionMap", "_AO" } },                     // AO
            { "_EmissionMap", new[] { "_EmissionMap", "_Emission" } }                  // 自发光
        };

        // 颜色属性映射字典
        private readonly Dictionary<string, string[]> _colorMappings = new Dictionary<string, string[]>()
        {
            { "_BaseColor", new[] { "_Color", "_BaseColor", "_TintColor" } },
            { "_EmissionColor", new[] { "_EmissionColor" } }
        };

        [MenuItem("CoCoFlow/AssetPipeline/Shader 转换器")]
        public static void ShowWindow()
        {
            var window = GetWindow<ShaderReplacementProcessor>("Shader转换器");
            window.minSize = new Vector2(400, 350);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            GUILayout.Label("CoCoFlow 材质管线升级器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("该工具会基于‘字典映射表’，将材质无损转换到你指定的 Shader，并尽可能保留贴图和颜色信息。", MessageType.Info);

            EditorGUILayout.Space(10);
            _targetFolderPath = EditorGUILayout.TextField("目标处理文件夹", _targetFolderPath);

            EditorGUILayout.Space(5);
            _targetShader = (Shader)EditorGUILayout.ObjectField("目标 Shader (如 URP/Lit)", _targetShader, typeof(Shader), false);

            EditorGUILayout.Space(10);
            _autoSetupCutout = EditorGUILayout.ToggleLeft("自动识别并配置 Cutout (树叶/草透明裁剪)", _autoSetupCutout);

            EditorGUILayout.Space(20);

            if (_targetShader == null)
            {
                EditorGUILayout.HelpBox("请先指派一个目标 Shader。", MessageType.Warning);
                GUI.enabled = false;
            }

            if (GUILayout.Button("执行强行替换与数据映射", GUILayout.Height(40)))
            {
                ExecuteReplacement();
            }

            GUI.enabled = true;
        }

        private void ExecuteReplacement()
        {
            string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { _targetFolderPath });
            int processedCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < matGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(matGuids[i]);
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                    if (mat == null || mat.shader == _targetShader) continue;

                    EditorUtility.DisplayProgressBar("Shader 转换中", $"正在处理: {mat.name}", (float)i / matGuids.Length);

                    // 1. 缓存旧数据
                    var oldTextures = ExtractTextures(mat);
                    var oldColors = ExtractColors(mat);
                    bool wasCutout = mat.renderQueue == (int)UnityEngine.Rendering.RenderQueue.AlphaTest || mat.IsKeywordEnabled("_ALPHATEST_ON");

                    // 2. 强行替换 Shader
                    mat.shader = _targetShader;

                    // 3. 数据回填 (映射)
                    ApplyTextures(mat, oldTextures);
                    ApplyColors(mat, oldColors);

                    // 4. 重建渲染状态 (树叶裁剪)
                    if (_autoSetupCutout && wasCutout)
                    {
                        SetupMaterialBlendMode(mat, BlendMode.Cutout);
                    }
                    else if (_autoSetupCutout && !wasCutout)
                    {
                        SetupMaterialBlendMode(mat, BlendMode.Opaque);
                    }

                    EditorUtility.SetDirty(mat);
                    processedCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("处理完成", $"成功将 {processedCount} 个材质转换为 {_targetShader.name}。", "确定");
            }
        }

        #region 数据提取与回填逻辑

        private Dictionary<string, Texture> ExtractTextures(Material mat)
        {
            var dict = new Dictionary<string, Texture>();
            // 利用序列化对象获取所有包含的贴图名，或者直接暴力遍历常见命名
            foreach (var kvp in _textureMappings)
            {
                foreach (string oldName in kvp.Value)
                {
                    if (mat.HasProperty(oldName))
                    {
                        Texture tex = mat.GetTexture(oldName);
                        if (tex != null)
                        {
                            dict[oldName] = tex;
                            break; // 找到优先级最高的就停止
                        }
                    }
                }
            }
            return dict;
        }

        private void ApplyTextures(Material mat, Dictionary<string, Texture> oldData)
        {
            foreach (var kvp in _textureMappings)
            {
                string targetProp = kvp.Key;
                if (!mat.HasProperty(targetProp)) continue;

                foreach (string oldName in kvp.Value)
                {
                    if (oldData.TryGetValue(oldName, out Texture tex))
                    {
                        mat.SetTexture(targetProp, tex);
                        break;
                    }
                }
            }
        }

        private Dictionary<string, Color> ExtractColors(Material mat)
        {
            var dict = new Dictionary<string, Color>();
            foreach (var kvp in _colorMappings)
            {
                foreach (string oldName in kvp.Value)
                {
                    if (mat.HasProperty(oldName))
                    {
                        dict[oldName] = mat.GetColor(oldName);
                        break;
                    }
                }
            }
            return dict;
        }

        private void ApplyColors(Material mat, Dictionary<string, Color> oldData)
        {
            foreach (var kvp in _colorMappings)
            {
                string targetProp = kvp.Key;
                if (!mat.HasProperty(targetProp)) continue;

                foreach (string oldName in kvp.Value)
                {
                    if (oldData.TryGetValue(oldName, out Color col))
                    {
                        mat.SetColor(targetProp, col);
                        break;
                    }
                }
            }
        }

        #endregion

        #region URP 渲染状态重建 (核心黑魔法)

        public enum BlendMode { Opaque, Cutout, Fade, Transparent }

        // 这个函数模拟了 Unity 官方 URP 材质的 Blend 设置逻辑
        private void SetupMaterialBlendMode(Material material, BlendMode blendMode)
        {
            switch (blendMode)
            {
                case BlendMode.Opaque:
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    break;
                case BlendMode.Cutout:
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.EnableKeyword("_ALPHATEST_ON"); // 开启 Alpha Test 裁剪宏
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    break;
            }
        }

        #endregion
    }
}
