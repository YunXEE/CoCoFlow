using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace CoCoFlow.Editor.AssetPipeline
{
    public class AnimationProcessor : EditorWindow
    {
        private string _sourceFbxFolder = "Assets/RawAnimations";
        private string _targetAnimFolder = "Assets/Art/Animations";
        private string _prefixToRemove = "Mixamo_"; // 常用的需要被干掉的前缀

        // 智能路由字典：匹配到关键字 -> 放入对应的子文件夹
        [System.Serializable]
        public class RoutingRule
        {
            public string keyword;
            public string targetSubFolder;

            public RoutingRule(string key, string folder)
            {
                keyword = key;
                targetSubFolder = folder;
            }
        }

        private List<RoutingRule> _routingRules = new List<RoutingRule>()
        {
            new RoutingRule("walk", "Locomotion"),
            new RoutingRule("run", "Locomotion"),
            new RoutingRule("idle", "Locomotion"),
            new RoutingRule("jump", "Locomotion"),
            new RoutingRule("attack", "Combat"),
            new RoutingRule("hit", "Combat"),
            new RoutingRule("death", "Combat")
        };

        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            var window = GetWindow<AnimationProcessor>("动画处理器");
            window.minSize = new Vector2(450, 450);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            GUILayout.Label("CoCoFlow 动画资产提纯管线 (2.2 专项)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("该工具将从 FBX 中提取独立的 .anim 文件，剥离冗余前缀，并根据关键字自动路由到指定的分类文件夹中。", MessageType.Info);

            EditorGUILayout.Space();
            _sourceFbxFolder = EditorGUILayout.TextField("原始 FBX 文件夹", _sourceFbxFolder);
            _targetAnimFolder = EditorGUILayout.TextField("独立 Anim 输出根目录", _targetAnimFolder);
            _prefixToRemove = EditorGUILayout.TextField("一键剔除前缀 (如 NM_, Mixamo_)", _prefixToRemove);

            EditorGUILayout.Space(10);
            GUILayout.Label("智能分类路由表 (Keyword -> SubFolder)", EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, "box", GUILayout.Height(150));
            for (int i = 0; i < _routingRules.Count; i++)
            {
                GUILayout.BeginHorizontal();
                _routingRules[i].keyword = EditorGUILayout.TextField(_routingRules[i].keyword, GUILayout.Width(120));
                GUILayout.Label(" -> ", GUILayout.Width(25));
                _routingRules[i].targetSubFolder = EditorGUILayout.TextField(_routingRules[i].targetSubFolder);

                if (GUILayout.Button("X", GUILayout.Width(30)))
                {
                    _routingRules.RemoveAt(i);
                    i--;
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 新增路由规则"))
            {
                _routingRules.Add(new RoutingRule("", ""));
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(15);
            if (GUILayout.Button("执行提取与分类", GUILayout.Height(40)))
            {
                ExecuteHarvesting();
            }
        }

        private void ExecuteHarvesting()
        {
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { _sourceFbxFolder });
            int extractedCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < fbxGuids.Length; i++)
                {
                    string fbxPath = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
                    EditorUtility.DisplayProgressBar("动画提取中", $"处理: {Path.GetFileName(fbxPath)}", (float)i / fbxGuids.Length);

                    // 获取 FBX 内部的所有子资产
                    Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

                    foreach (var asset in allAssets)
                    {
                        // 我们只关心 AnimationClip，并且要过滤掉 Unity 自动生成的预览 Clip
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                        {
                            ExtractAndRouteClip(clip);
                            extractedCount++;
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("收割完成", $"共提取并分类了 {extractedCount} 个独立动画片段！", "确定");
            }
        }

        private void ExtractAndRouteClip(AnimationClip originalClip)
        {
            // 1. 拷贝原始数据以生成独立的 Clip
            AnimationClip newClip = new AnimationClip();
            EditorUtility.CopySerialized(originalClip, newClip);

            // 2. 清洗名称
            string finalName = originalClip.name;
            if (!string.IsNullOrEmpty(_prefixToRemove) && finalName.StartsWith(_prefixToRemove))
            {
                finalName = finalName.Substring(_prefixToRemove.Length);
            }

            // 3. 匹配路由规则，决定放入哪个子文件夹
            string targetSubDir = "Uncategorized"; // 默认兜底文件夹
            string lowerName = finalName.ToLower();

            foreach (var rule in _routingRules)
            {
                if (!string.IsNullOrEmpty(rule.keyword) && lowerName.Contains(rule.keyword.ToLower()))
                {
                    targetSubDir = rule.targetSubFolder;
                    break; // 匹配到第一个规则就停止
                }
            }

            // 4. 构建路径并保存
            string finalFolderPath = Path.Combine(_targetAnimFolder, targetSubDir).Replace("\\", "/");
            if (!Directory.Exists(finalFolderPath))
            {
                Directory.CreateDirectory(finalFolderPath);
            }

            string finalAssetPath = $"{finalFolderPath}/{finalName}.anim";

            // 防止重名覆盖
            finalAssetPath = AssetDatabase.GenerateUniqueAssetPath(finalAssetPath);

            AssetDatabase.CreateAsset(newClip, finalAssetPath);
        }
    }
}
