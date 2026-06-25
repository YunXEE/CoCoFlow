using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace CoCoFlow.Runtime.Modules.Persistence
{
    [ExecuteAlways]
    public sealed class PersistenceContext :
        MonoBehaviour,
        ISerializationCallbackReceiver,
        ICoCoStableEntityIdProvider
    {
        [Header("Persistence")]
        [SerializeField] private string stableEntityId = string.Empty;
        [SerializeField] private string prefabKey = string.Empty;

        public string StableEntityId => stableEntityId;
        public string PrefabKey => prefabKey;

        #region Public API

        public void EnsureStableEntityId()
        {
            if (!string.IsNullOrEmpty(stableEntityId)) return;

            stableEntityId = Application.isPlaying
                ? "RT_" + Guid.NewGuid().ToString("N").Substring(0, 12)
                : Guid.NewGuid().ToString("N");
        }

        public bool TryCapture(out PersistenceContextRecord record)
        {
            record = null;
            if (!TryResolveContext(out var context)) return false;

            bool captured = PersistenceContextAdapterRegistry.TryCapture(stableEntityId, context, out record);
            if (captured && record != null)
            {
                record.prefabKey = string.IsNullOrEmpty(record.prefabKey) ? prefabKey : record.prefabKey;
            }

            return captured;
        }

        public bool TryApply(PersistenceContextRecord record)
        {
            if (!TryResolveContext(out var context)) return false;
            return PersistenceContextAdapterRegistry.TryApply(record, context);
        }

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer) return;
            if (PrefabUtility.IsPartOfPrefabAsset(this)) return;
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return;

            if (IsSceneObject())
            {
                EnsureStableEntityId();
                EnsureUniqueSceneId();
            }
#endif
        }

        public void OnAfterDeserialize() { }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            if (Application.isPlaying)
            {
                EnsureStableEntityId();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying || ShouldGenerateEditorId())
            {
                EnsureStableEntityId();
                PersistenceContextRegistry.Register(this);
            }
        }

        private void OnDisable()
        {
            PersistenceContextRegistry.Unregister(this);
        }

        private bool TryResolveContext(out ICoCoContext context)
        {
            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || ReferenceEquals(behaviour, this)) continue;

                var interfaces = behaviour.GetType().GetInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    var contract = interfaces[i];
                    if (!contract.IsGenericType ||
                        contract.GetGenericTypeDefinition() != typeof(ICoCoContextProvider<>))
                    {
                        continue;
                    }

                    var property = contract.GetProperty(
                        "Context",
                        BindingFlags.Instance | BindingFlags.Public);
                    context = property?.GetValue(behaviour) as ICoCoContext;
                    if (context != null)
                    {
                        if (context is CoCoEntityContext entityContext)
                        {
                            entityContext.Identity.StableEntityId = stableEntityId;
                        }

                        return true;
                    }
                }
            }

            context = null;
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            if (ShouldGenerateEditorId())
            {
                EnsureStableEntityId();
                EditorUtility.SetDirty(this);
            }
        }

        private bool IsSceneObject()
        {
            Scene scene = gameObject.scene;
            return scene.IsValid() && !string.IsNullOrEmpty(scene.path);
        }

        private bool ShouldGenerateEditorId()
        {
            if (Application.isPlaying) return false;
            if (PrefabUtility.IsPartOfPrefabAsset(this)) return false;
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return false;
            return IsSceneObject();
        }

        private void EnsureUniqueSceneId()
        {
            if (string.IsNullOrEmpty(stableEntityId)) return;

            var allContexts = FindObjectsByType<PersistenceContext>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var context in allContexts)
            {
                if (context == null || context == this) continue;
                if (context.stableEntityId != stableEntityId) continue;

                stableEntityId = Guid.NewGuid().ToString("N");
                EditorUtility.SetDirty(this);
                if (!string.IsNullOrEmpty(gameObject.scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                }
                break;
            }
        }
#endif

        #endregion
    }

}
