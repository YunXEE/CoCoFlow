using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace CoCoFlow.Editor.AssetPipeline
{
    public class SmartRenamer : EditorWindow
    {
        private int _selectedTab = 0;
        private string[] _tabNames = { "🛠️ 规范化清洗模式", "✏️ 自定义批量模式" };

        private string _targetFolderPath = "Assets/Art/StagingArea";
        private Vector2 _scrollPos;

        // ================= 自定义模式变量 =================
        private string _customFind = "";
        private string _customReplace = "";
        private string _customPrefix = "";
        private string _customSuffix = "";
        private bool _includeSubFolders = true;

        // ================= 规范化模式规则 =================
        [System.Serializable]
        public class NamingRule
        {
            public bool isActive = true;
            public string keyword;       // 检索关键字 (如 mask, nrm)
            public string enforceSuffix; // 强制后缀 (如 _Mask)
            
            public NamingRule(string key, string suffix)
            {
                keyword = key;
                enforceSuffix = suffix;
            }
        }

        private List<NamingRule> _standardRules = new List<NamingRule>()
        {
            new NamingRule("mask", "_Mask"),
            new NamingRule("normal", "_Normal"),
            new NamingRule("nrm", "_Normal"),
            new NamingRule("basecolor", "_BC"),
            new NamingRule("albedo", "_BC"),
            new NamingRule("mat", "_Mat"),
            new NamingRule("prefab", "_Prefab")
        };

        [MenuItem("CoCoFlow/AssetPipeline/智能重命名器 (正则与规范)")]
        public static void ShowWindow()
        {
            var window = GetWindow<SmartRenamer>("智能重命名器");
            window.minSize = new Vector2(450, 500);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            GUILayout.Label("CoCoFlow 资产重命名中心", EditorStyles.boldLabel);
            _targetFolderPath = EditorGUILayout.TextField("目标处理文件夹", _targetFolderPath);

            EditorGUILayout.Space(10);
            
            // 绘制顶部标签页
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(30));
            EditorGUILayout.Space(10);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            if (_selectedTab == 0)
            {
                DrawStandardizationTab();
            }
            else
            {
                DrawCustomRenameTab();
            }

            EditorGUILayout.EndScrollView();
        }

        #region UI 绘制逻辑

        private void DrawStandardizationTab()
        {
            EditorGUILayout.HelpBox("执行逻辑：扫描文件名，若包含【检索关键字】，则清理其原有杂乱后缀，并强制替换为【标准后缀】。", MessageType.Info);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("启用", GUILayout.Width(40));
            GUILayout.Label("检索关键字 (忽略大小写)", GUILayout.Width(150));
            GUILayout.Label("强制标准后缀");
            GUILayout.EndHorizontal();

            for (int i = 0; i < _standardRules.Count; i++)
            {
                GUILayout.BeginHorizontal("box");
                _standardRules[i].isActive = EditorGUILayout.Toggle(_standardRules[i].isActive, GUILayout.Width(40));
                _standardRules[i].keyword = EditorGUILayout.TextField(_standardRules[i].keyword, GUILayout.Width(145));
                _standardRules[i].enforceSuffix = EditorGUILayout.TextField(_standardRules[i].enforceSuffix);
                
                if (GUILayout.Button("X", GUILayout.Width(30)))
                {
                    _standardRules.RemoveAt(i);
                    i--;
                }
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ 新增规范规则", GUILayout.Height(25)))
            {
                _standardRules.Add(new NamingRule("", ""));
            }

            EditorGUILayout.Space(20);
            if (GUILayout.Button("执行规范化清洗", GUILayout.Height(40)))
            {
                ExecuteStandardization();
            }
        }

        private void DrawCustomRenameTab()
        {
            EditorGUILayout.HelpBox("类似 PowerRename 的机械化处理模式。按照：替换 -> 加前缀 -> 加后缀 的顺序执行。", MessageType.Info);

            _includeSubFolders = EditorGUILayout.Toggle("包含子文件夹", _includeSubFolders);
            
            EditorGUILayout.Space(10);
            GUILayout.Label("1. 查找与替换", EditorStyles.boldLabel);
            _customFind = EditorGUILayout.TextField("查找字符串", _customFind);
            _customReplace = EditorGUILayout.TextField("替换为", _customReplace);

            EditorGUILayout.Space(10);
            GUILayout.Label("2. 前缀与后缀", EditorStyles.boldLabel);
            _customPrefix = EditorGUILayout.TextField("添加前缀", _customPrefix);
            _customSuffix = EditorGUILayout.TextField("添加后缀", _customSuffix);

            EditorGUILayout.Space(20);
            if (GUILayout.Button("执行自定义重命名", GUILayout.Height(40)))
            {
                ExecuteCustomRename();
            }
        }

        #endregion

        #region 核心处理逻辑

        private void ExecuteStandardization()
        {
            string[] guids = AssetDatabase.FindAssets("", new[] { _targetFolderPath });
            int renameCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    // 排除文件夹本身
                    if (AssetDatabase.IsValidFolder(path)) continue;

                    string oldName = Path.GetFileNameWithoutExtension(path);
                    string lowerName = oldName.ToLower();
                    string newName = oldName;

                    foreach (var rule in _standardRules)
                    {
                        if (!rule.isActive || string.IsNullOrEmpty(rule.keyword)) continue;

                        if (lowerName.Contains(rule.keyword.ToLower()))
                        {
                            // 暴力清理：如果名字里已经有关键字，我们先把它剥离，再加上标准后缀
                            // 例如：Tree_Mask_v1 -> Tree_Mask
                            // 这里的简单策略：找到关键字的位置，截断，然后加上标准后缀
                            int index = lowerName.IndexOf(rule.keyword.ToLower());
                            if (index > 0)
                            {
                                // 提取基础名字部分 (不包含下划线)
                                string baseName = oldName.Substring(0, index).TrimEnd('_', '-', ' ');
                                newName = baseName + rule.enforceSuffix;
                            }
                            else
                            {
                                // 关键字在最前面，或者名字太短，直接加后缀
                                newName = oldName + rule.enforceSuffix;
                            }
                            break; // 匹配到一个规则后就跳出，避免重复处理
                        }
                    }

                    if (newName != oldName)
                    {
                        AssetDatabase.RenameAsset(path, newName);
                        renameCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("处理完成", $"规范化清洗完毕！共重命名了 {renameCount} 个资产。", "确定");
            }
        }

        private void ExecuteCustomRename()
        {
            SearchOption searchOption = _includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            // 为了安全起见，我们使用 Directory.GetFiles 获取真实路径，然后转换为 Asset 路径
            string[] files = Directory.GetFiles(_targetFolderPath, "*.*", searchOption);
            int renameCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var file in files)
                {
                    if (file.EndsWith(".meta")) continue; // 忽略 meta 文件

                    string assetPath = file.Replace("\\", "/");
                    string oldName = Path.GetFileNameWithoutExtension(assetPath);
                    string newName = oldName;

                    // 1. 替换
                    if (!string.IsNullOrEmpty(_customFind))
                    {
                        newName = newName.Replace(_customFind, _customReplace);
                    }

                    // 2. 前缀
                    if (!string.IsNullOrEmpty(_customPrefix))
                    {
                        newName = _customPrefix + newName;
                    }

                    // 3. 后缀
                    if (!string.IsNullOrEmpty(_customSuffix))
                    {
                        newName = newName + _customSuffix;
                    }

                    if (newName != oldName)
                    {
                        // AssetDatabase.RenameAsset 需要相对于 Assets 的路径
                        int assetsIndex = assetPath.IndexOf("Assets");
                        if (assetsIndex != -1)
                        {
                            string relativePath = assetPath.Substring(assetsIndex);
                            AssetDatabase.RenameAsset(relativePath, newName);
                            renameCount++;
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("处理完成", $"自定义重命名完毕！共修改了 {renameCount} 个资产。", "确定");
            }
        }

        #endregion
    }
}