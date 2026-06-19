using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace CoCoFlow.Editor.AssetPipeline
{
    /// <summary>
    /// CoCoFlow Asset Pipeline: 静态Prefab提取器
    /// </summary>
    public class StaticPrefabExtractor : EditorWindow
    {
        private string _sourcePath = "Assets/";
        private string _targetPath = "Assets/Art/Processed";

        private Vector2 _scrollPosition;

        // 全局开关：是否保留 Unity 原生组件（建议开启以保护粒子、光源、音效等）
        private bool _keepNativeUnityComponents = true;

        // 自定义保留组件（模糊匹配）
        private readonly Dictionary<string, bool> _componentSettings = new Dictionary<string, bool>()
        {
            { "MeshFilter", true },
            { "MeshRenderer", true },
            { "LODGroup", true },
            { "Collider", false },  
            { "Rigidbody", false },
            { "Animator", false },
            { "Wind", false },      // 匹配风场脚本
            { "Controller", false } // 匹配各类第三方自定义脚本
        };

        [MenuItem("CoCoFlow/AssetPipeline/静态Prefab提取器")]
        public static void ShowWindow()
        {
            var window = GetWindow<StaticPrefabExtractor>("静态Prefab提取器");
            window.minSize = new Vector2(450, 500);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("CoCoFlow 资产清洗管线", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("提示：Transform 组件将永久强制保留。开启原生保护可避免误删特效/音效组件。", MessageType.Info);
            
            EditorGUILayout.Space(5);
            _sourcePath = EditorGUILayout.TextField("原始资源路径", _sourcePath);
            _targetPath = EditorGUILayout.TextField("输出目标路径", _targetPath);

            EditorGUILayout.Space(10);
            GUILayout.Label("组件保留策略：", EditorStyles.boldLabel);

            // Unity 原生组件全局保护开关
            EditorGUILayout.BeginVertical("box");
            _keepNativeUnityComponents = EditorGUILayout.ToggleLeft(
                "🛡️ 保留所有 Unity 原生组件 (如 ParticleSystem, Light 等)", 
                _keepNativeUnityComponents, 
                EditorStyles.boldLabel
            );
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
            
            // 进度条
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, "box", GUILayout.Height(180));
            
            List<string> keys = new List<string>(_componentSettings.Keys);
            foreach (string key in keys)
            {
                GUILayout.BeginHorizontal();
                _componentSettings[key] = EditorGUILayout.ToggleLeft(
                    $"保留包含 '{key}' 的组件", 
                    _componentSettings[key], 
                    GUILayout.Width(250)
                );
                GUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            if (GUILayout.Button("开始迁移并清洗", GUILayout.Height(40)))
            {
                if (ValidatePaths())
                {
                    ExecuteMigration();
                }
            }
        }

        private bool ValidatePaths()
        {
            if (!AssetDatabase.IsValidFolder(_sourcePath))
            {
                EditorUtility.DisplayDialog("错误", "原始资源路径无效，请检查文件夹是否存在。", "确定");
                return false;
            }
            if (_sourcePath == _targetPath)
            {
                EditorUtility.DisplayDialog("错误", "源路径不能与目标路径相同！", "确定");
                return false;
            }
            return true;
        }

        private void ExecuteMigration()
        {
            string[] allGuids = AssetDatabase.FindAssets("t:Prefab t:Material", new[] { _sourcePath });
            int total = allGuids.Length;

            if (total == 0)
            {
                EditorUtility.DisplayDialog("提示", "未在源路径找到任何 Prefab 或 Material。", "确定");
                return;
            }

            try
            {
                // Step 1：镜像克隆
                for (int i = 0; i < total; i++)
                {
                    string srcPath = AssetDatabase.GUIDToAssetPath(allGuids[i]);
                    string relativePath = srcPath.Substring(_sourcePath.Length);
                    string destPath = _targetPath + relativePath;

                    EditorUtility.DisplayProgressBar("Step 1/2: 克隆资产", $"正在克隆: {Path.GetFileName(srcPath)}", (float)i / total);

                    string destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    if (!File.Exists(destPath))
                    {
                        AssetDatabase.CopyAsset(srcPath, destPath);
                    }
                }

                AssetDatabase.Refresh();

                // Step 2：材质重定向与组件清洗
                string[] newPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { _targetPath });
                for (int i = 0; i < newPrefabs.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(newPrefabs[i]);
                    EditorUtility.DisplayProgressBar("Step 2/2: 清洗资产", $"正在处理: {Path.GetFileName(path)}", (float)i / newPrefabs.Length);
                    
                    ProcessNewPrefab(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"<color=cyan>[CoCoFlow]</color> 迁移清洗完成！资产已存放至: <b>{_targetPath}</b>");
            }
        }

        private void ProcessNewPrefab(string path)
        {
            GameObject instance = PrefabUtility.LoadPrefabContents(path);
            bool isModified = false;

            // 重新绑定材质 
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                Material[] sharedMats = renderer.sharedMaterials;
                bool matChanged = false;

                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] == null) continue;

                    string oldMatPath = AssetDatabase.GetAssetPath(sharedMats[i]);
                    
                    if (oldMatPath.StartsWith(_sourcePath))
                    {
                        string relativeMatPath = oldMatPath.Substring(_sourcePath.Length);
                        string newMatPath = _targetPath + relativeMatPath;
                        
                        Material newMat = AssetDatabase.LoadAssetAtPath<Material>(newMatPath);
                        if (newMat != null)
                        {
                            sharedMats[i] = newMat;
                            matChanged = true;
                            isModified = true;
                        }
                    }
                }
                if (matChanged) renderer.sharedMaterials = sharedMats;
            }
            
            if (CleanTransform(instance.transform))
            {
                isModified = true;
            }

            if (isModified)
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            
            PrefabUtility.UnloadPrefabContents(instance);
        }

        private bool CleanTransform(Transform current)
        {
            bool hasChanged = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(current.gameObject) > 0;

            // 获取当前层级所有组件
            Component[] components = current.GetComponents<Component>();
            List<Component> componentsToDestroy = new List<Component>();

            foreach (var comp in components)
            {
                if (comp == null || comp is Transform) continue;
                
                System.Type compType = comp.GetType();
                string typeName = compType.Name;
                bool shouldKeep = false;

                // Unity 原生组件判定
                if (_keepNativeUnityComponents)
                {
                    string nameSpace = compType.Namespace ?? "";
                    string assemblyName = compType.Assembly.GetName().Name;

                    if (nameSpace.StartsWith("UnityEngine") || assemblyName.StartsWith("UnityEngine"))
                    {
                        shouldKeep = true;
                    }
                }

                // 白名单判定
                if (!shouldKeep)
                {
                    foreach (var kvp in _componentSettings)
                    {
                        if (typeName.Contains(kvp.Key) && kvp.Value)
                        {
                            shouldKeep = true;
                            break;
                        }
                    }
                }
                
                // 收集需要销毁的组件
                if (!shouldKeep)
                {
                    componentsToDestroy.Add(comp);
                }
            }

            // 倒序删除，最大程度避免 [RequireComponent] 依赖导致的报错
            for (int i = componentsToDestroy.Count - 1; i >= 0; i--)
            {
                DestroyImmediate(componentsToDestroy[i], true); 
                hasChanged = true;
            }

            // 递归处理所有子物体
            foreach (Transform child in current)
            {
                if (CleanTransform(child))
                    hasChanged = true;
            }

            return hasChanged;
        }
    }
}
