using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace CoCoFlow.Runtime.Modules.Persistence
{
    [ExecuteAlways]
    public abstract class SavableEntityBase : MonoBehaviour,
        ISerializationCallbackReceiver
    {
        [Header("Save Settings")]
        [SerializeField]
        [InspectorName("Unique ID")]
        private string uniqueID = string.Empty;
        public string UniqueID => uniqueID;
        protected bool HasRegisteredStore => PersistenceRuntimeStateManager.CurrentData != null;

        #region Public API & Contract

        protected virtual void Awake()
        {
            if (Application.isPlaying && string.IsNullOrEmpty(uniqueID))
            {
                GenerateRuntimeUniqueID();
            }
        }

        protected virtual void Start()
        {
            if (Application.isPlaying)
            {
                LoadState();
            }
        }

        /// <summary>
        /// 从 RuntimeStateManager 读取数据并应用到物体 (Start/Enable调用）
        /// </summary>
        public abstract void LoadState();

        /// <summary>
        /// 将物体当前状态写入 RuntimeStateManager
        /// </summary>
        public abstract void SaveState();

        protected virtual void OnDestroy()
        {
            // 当 Addressables 卸载 Scene 导致物体销毁时，自动将当前状态写回 RuntimeStateManager
            if (Application.isPlaying && HasRegisteredStore)
            {
                SaveState();
            }
        }

        #endregion


        #region Inner Logic

        private void GenerateRuntimeUniqueID()
        {
            uniqueID = "RT_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        public void OnAfterDeserialize() { }

#if UNITY_EDITOR
        public void OnBeforeSerialize()
        {
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer) return;

            if (string.IsNullOrEmpty(uniqueID))
            {
                GenerateSerializedGuid();
                return;
            }

            // 检查 Prefab 实例化时的 ID 冲突
            // 如果是在 Prefab 舞台（孤立模式）编辑，不生成新的
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return;

            // 如果是 Scene 里的物体
            if (!string.IsNullOrEmpty(gameObject.scene.path))
            {
                var allEntities =
                    FindObjectsByType<SavableEntityBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var entity in allEntities)
                {
                    if (entity == this) continue;

                    // 发现 ID 冲突（通常是 Ctrl+D 复制出来的物体）
                    if (entity.uniqueID == this.uniqueID)
                    {
                        // 自身重新生成，且确保对方不是 Asset 里的 Prefab 原型
                        if (!PrefabUtility.IsPartOfPrefabAsset(this))
                        {
                            GenerateSerializedGuid();
                            break;
                        }
                    }
                }
            }
        }

        private void GenerateSerializedGuid()
        {
            uniqueID = Guid.NewGuid().ToString("N"); // "N" 格式是只有数字和字母的短格式

            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
                if (!string.IsNullOrEmpty(gameObject.scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                }
            }
        }

        // 当在 Inspector 修改参数或复制物体时触发
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            // 简单防空，核心逻辑在 OnBeforeSerialize 里处理复制
            if (string.IsNullOrEmpty(uniqueID))
            {
                GenerateSerializedGuid();
            }
        }
#endif
        #endregion

    }
}
