using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

namespace CocoFlow.Editor.AssetPipeline
{
    /// <summary>
    /// CoCoFrame 资产管线：非破坏性资产清洗工具
    /// 专项针对 Nature Manufacture 等重型环境包
    /// </summary>
    public class AssetCloneAndStrip : EditorWindow
    {
        private string _sourcePath = "Assets/NatureManufacture";
        private string _targetPath = "Assets/Art/Processed_NM";

        // 组件白名单：只有这些类型的组件会被保留
        private readonly System.Type[] _whiteList = {
            typeof(Transform),
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(LODGroup),
            typeof(Collider), // 自动包含 Box, Sphere, Capsule, MeshCollider 等
            typeof(Rigidbody)
        };

        [MenuItem("CoCoFrame/AssetPipeline/一键克隆并清洗资产")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetCloneAndStrip>("资产克隆清洗器");
            window.minSize = new Vector2(400, 200);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("CoCoFrame 资产清洗管线 (2.1 自然资源专项)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("该工具会克隆 Prefab 和 Material 到目标文件夹，并剔除所有冗余脚本。纹理将保留对原始路径的引用，不占用额外空间。", MessageType.Info);
            
            EditorGUILayout.Space(5);
            _sourcePath = EditorGUILayout.TextField("原始资源路径", _sourcePath);
            _targetPath = EditorGUILayout.TextField("输出目标路径", _targetPath);

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
            // 1. 获取所有源文件（Prefab 和 Material）
            string[] allGuids = AssetDatabase.FindAssets("t:Prefab t:Material", new[] { _sourcePath });
            int total = allGuids.Length;

            try
            {
                // 第一遍扫描：镜像克隆
                for (int i = 0; i < total; i++)
                {
                    string srcPath = AssetDatabase.GUIDToAssetPath(allGuids[i]);
                    string relativePath = srcPath.Substring(_sourcePath.Length);
                    string destPath = _targetPath + relativePath;

                    EditorUtility.DisplayProgressBar("CoCoFrame 资产清洗", $"正在克隆资产: {Path.GetFileName(srcPath)}", (float)i / total);

                    // 自动创建目录
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                    // 如果目标已存在则不覆盖（或者你可以根据需要选择覆写）
                    if (!File.Exists(destPath))
                    {
                        AssetDatabase.CopyAsset(srcPath, destPath);
                    }
                }

                AssetDatabase.Refresh();

                // 第二遍扫描：处理 Prefab 内部逻辑
                string[] newPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { _targetPath });
                for (int i = 0; i < newPrefabs.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(newPrefabs[i]);
                    EditorUtility.DisplayProgressBar("CoCoFrame 资产清洗", $"正在剥离组件与重定向材质: {Path.GetFileName(path)}", (float)i / newPrefabs.Length);
                    
                    ProcessNewPrefab(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"<color=green>[CoCoFrame]</color> 迁移清洗完成！逻辑资产已存放至: <b>{_targetPath}</b>");
            }
        }

        private void ProcessNewPrefab(string path)
        {
            // 加载 Prefab 内容（内存中）
            GameObject instance = PrefabUtility.LoadPrefabContents(path);
            bool isModified = false;

            // --- 逻辑 A: 重新绑定材质 ---
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                Material[] sharedMats = renderer.sharedMaterials;
                bool matChanged = false;

                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] == null) continue;

                    string oldMatPath = AssetDatabase.GetAssetPath(sharedMats[i]);
                    
                    // 如果该材质还在原始路径下，尝试在目标路径找克隆版本
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

            // --- 逻辑 B: 递归组件剥离 ---
            if (CleanTransform(instance.transform))
            {
                isModified = true;
            }

            // 只有在修改过的情况下才保存，提高效率
            if (isModified)
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            
            PrefabUtility.UnloadPrefabContents(instance);
        }

        private bool CleanTransform(Transform current)
        {
            bool hasChanged = false;

            // 获取节点上所有组件
            Component[] components = current.GetComponents<Component>();

            foreach (var comp in components)
            {
                // 如果脚本丢失(Missing Script)，comp 会为 null，直接清理
                if (comp == null)
                {
                    // 使用 SerializedObject 来清理丢失的脚本项
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(current.gameObject);
                    hasChanged = true;
                    continue;
                }

                // 检查是否在白名单中
                bool isWhiteListed = _whiteList.Any(type => type.IsInstanceOfType(comp));

                if (!isWhiteListed)
                {
                    // 特殊逻辑：如果是 Nature Manufacture 的风场或控制器，直接干掉
                    // Debug.Log($"[Stripper] 移除组件: {comp.GetType().Name} 来自 {current.name}");
                    DestroyImmediate(comp);
                    hasChanged = true;
                }
            }

            // 递归处理子物体
            foreach (Transform child in current)
            {
                if (CleanTransform(child))
                    hasChanged = true;
            }

            return hasChanged;
        }
    }
}