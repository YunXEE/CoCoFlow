using UnityEngine;
using UnityEditor;


namespace CoCoFlow.Editor.AssetPipeline
{
    public class LodAndMaterialStandardizer : EditorWindow
    {
        private string _targetFolderPath = "Assets/Art/Processed";

        // 材质设置
        private bool _enableGPUInstancing = true;

        // LOD 设置
        private bool _standardizeLods = true;
        private bool _enableCrossFade = true; // 强烈建议开启 Dither，过渡极其平滑
        private float _crossFadeWidth = 0.1f;

        // 标准化阈值预设 (从高到低递减，必须保证递减关系)
        private float _lod0Threshold = 0.6f;  // 60% 以下切 LOD1
        private float _lod1Threshold = 0.3f;  // 30% 以下切 LOD2
        private float _lod2Threshold = 0.1f;  // 10% 以下切 LOD3
        private float _cullThreshold = 0.02f; // 2% 以下直接剔除 (不渲染)

        private Vector2 _scrollPos;

        [MenuItem("CoCoFlow/AssetPipeline/LOD 与材质标准化处理器")]
        public static void ShowWindow()
        {
            var window = GetWindow<LodAndMaterialStandardizer>("LOD与材质处理");
            window.minSize = new Vector2(400, 500);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            GUILayout.Label("CoCoFlow 环境资产标准化中心", EditorStyles.boldLabel);
            _targetFolderPath = EditorGUILayout.TextField("目标处理文件夹", _targetFolderPath);

            EditorGUILayout.Space(10);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // --- 材质处理板块 ---
            GUILayout.BeginVertical("box");
            GUILayout.Label("1. 材质处理 (Material Batching)", EditorStyles.boldLabel);
            _enableGPUInstancing = EditorGUILayout.ToggleLeft("强制开启 Enable GPU Instancing", _enableGPUInstancing);
            GUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- LOD 处理板块 ---
            GUILayout.BeginVertical("box");
            GUILayout.Label("2. LOD 阈值标准化 (LOD Group)", EditorStyles.boldLabel);
            _standardizeLods = EditorGUILayout.ToggleLeft("开启 LOD 一键覆盖", _standardizeLods);

            if (_standardizeLods)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("提示：阈值代表屏幕占比。若资产只有 3 层 (LOD0,1,2)，将依次取用下方的阈值，最后层级采用剔除阈值。", MessageType.Info);

                _lod0Threshold = EditorGUILayout.Slider("LOD 0 -> 1 阈值", _lod0Threshold, 0.1f, 0.9f);
                _lod1Threshold = EditorGUILayout.Slider("LOD 1 -> 2 阈值", _lod1Threshold, 0.05f, _lod0Threshold - 0.01f);
                _lod2Threshold = EditorGUILayout.Slider("LOD 2 -> 3 阈值", _lod2Threshold, 0.02f, _lod1Threshold - 0.01f);
                _cullThreshold = EditorGUILayout.Slider("Cull (剔除) 阈值", _cullThreshold, 0.001f, 0.1f);

                EditorGUILayout.Space(5);
                _enableCrossFade = EditorGUILayout.ToggleLeft("开启平滑过渡 (Fade Mode: Cross Fade)", _enableCrossFade);
                if (_enableCrossFade)
                {
                    _crossFadeWidth = EditorGUILayout.Slider("过渡带宽度", _crossFadeWidth, 0f, 1f);
                }
            }
            GUILayout.EndVertical();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            if (GUILayout.Button("执行标准化处理", GUILayout.Height(40)))
            {
                ExecuteStandardization();
            }
        }

        private void ExecuteStandardization()
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                int matCount = 0;
                int prefabCount = 0;

                // 1. 处理材质
                if (_enableGPUInstancing)
                {
                    string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { _targetFolderPath });
                    foreach (var guid in matGuids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat != null && !mat.enableInstancing)
                        {
                            mat.enableInstancing = true;
                            EditorUtility.SetDirty(mat); // 标记为已修改
                            matCount++;
                        }
                    }
                }

                // 2. 处理 Prefab 的 LODGroup
                if (_standardizeLods)
                {
                    string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { _targetFolderPath });
                    for (int i = 0; i < prefabGuids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                        EditorUtility.DisplayProgressBar("LOD 处理中", $"正在处理: {path}", (float)i / prefabGuids.Length);

                        GameObject instance = PrefabUtility.LoadPrefabContents(path);
                        bool isModified = false;

                        LODGroup[] lodGroups = instance.GetComponentsInChildren<LODGroup>(true);
                        foreach (var lodGroup in lodGroups)
                        {
                            if (ProcessLODGroup(lodGroup))
                            {
                                isModified = true;
                            }
                        }

                        if (isModified)
                        {
                            PrefabUtility.SaveAsPrefabAsset(instance, path);
                            prefabCount++;
                        }

                        PrefabUtility.UnloadPrefabContents(instance);
                    }
                }

                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets(); // 保存所有标记为 Dirty 的资产

                EditorUtility.DisplayDialog("处理完成", $"标准化完毕！\n处理了 {matCount} 个材质。\n更新了 {prefabCount} 个 Prefab 的 LOD。", "确定");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        private bool ProcessLODGroup(LODGroup lodGroup)
        {
            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length == 0) return false;

            bool changed = false;

            // 设置 Fade Mode
            LODFadeMode targetFadeMode = _enableCrossFade ? LODFadeMode.CrossFade : LODFadeMode.None;
            if (lodGroup.fadeMode != targetFadeMode || lodGroup.animateCrossFading != _enableCrossFade)
            {
                lodGroup.fadeMode = targetFadeMode;
                lodGroup.animateCrossFading = _enableCrossFade;
                changed = true;
            }

            // 自适应分配阈值
            float[] standardThresholds = { _lod0Threshold, _lod1Threshold, _lod2Threshold };

            for (int i = 0; i < lods.Length; i++)
            {
                float newThreshold;

                if (i == lods.Length - 1)
                {
                    // 最后一级 LOD，直接应用 Cull (剔除) 阈值
                    newThreshold = _cullThreshold;
                }
                else
                {
                    // 取标准预设数组中的值，如果超过了预设数量，就递减一个固定值兜底
                    newThreshold = i < standardThresholds.Length ? standardThresholds[i] : lods[i-1].screenRelativeTransitionHeight * 0.5f;

                    // 防止倒挂（必须严格递减）
                    newThreshold = Mathf.Max(newThreshold, _cullThreshold + 0.01f);
                }

                if (Mathf.Abs(lods[i].screenRelativeTransitionHeight - newThreshold) > 0.001f)
                {
                    lods[i].screenRelativeTransitionHeight = newThreshold;

                    if (_enableCrossFade)
                    {
                        lods[i].fadeTransitionWidth = _crossFadeWidth;
                    }

                    changed = true;
                }
            }

            if (changed)
            {
                lodGroup.SetLODs(lods);
            }

            return changed;
        }
    }
}
