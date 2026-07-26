using System;
using System.Collections;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    internal enum PersistenceContextOperationResult
    {
        Applied = 0,
        Deferred = 1,
        Unsupported = 2,
        Failed = 3
    }

    [ExecuteAlways]
    public sealed class PersistenceContext :
        MonoBehaviour,
        ISerializationCallbackReceiver,
        ICoCoStableEntityIdProvider
    {
        [Header("Persistence")]
        [SerializeField] private string stableEntityId = string.Empty;
        [SerializeField] private string prefabKey = string.Empty;

        private Coroutine _deferredApplyCoroutine;
        private PersistenceContextRecord _deferredApplyRecord;

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
            return TryCaptureDetailed(out record, out _) ==
                   PersistenceContextOperationResult.Applied;
        }

        public bool TryApply(PersistenceContextRecord record)
        {
            PersistenceContextOperationResult result = TryApplyDetailed(record, out _);
            if (result == PersistenceContextOperationResult.Deferred)
            {
                return TryScheduleDeferredApply(record, out _);
            }

            CancelDeferredApply();
            return result == PersistenceContextOperationResult.Applied;
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
#if UNITY_EDITOR
            if (!Application.isPlaying && !ShouldGenerateEditorId())
            {
                return;
            }
#endif

            EnsureStableEntityId();
            PersistenceContextRegistry.Register(this);
        }

        private void OnDisable()
        {
            CancelDeferredApply();
            PersistenceContextRegistry.Unregister(this);
        }

        internal PersistenceContextOperationResult TryCaptureDetailed(
            out PersistenceContextRecord record,
            out string failure)
        {
            record = null;
            failure = string.Empty;

            var host = GetComponent<CoCoStateGraphHost>();
            if (host != null)
            {
                if (string.IsNullOrEmpty(stableEntityId))
                {
                    failure = "StateGraph capture requires a stable entity id.";
                    return PersistenceContextOperationResult.Failed;
                }

                try
                {
                    bool stateGraphCaptured = host.TryCapturePersistencePayload(
                        out byte[] payload,
                        out CoCoDiagnostic diagnostic);
                    if (!stateGraphCaptured || diagnostic.IsError)
                    {
                        failure = FormatDiagnostic(
                            "StateGraph ContextFrame capture was rejected.",
                            diagnostic);
                        return PersistenceContextOperationResult.Failed;
                    }

                    if (payload == null || payload.Length == 0)
                    {
                        failure = "StateGraph ContextFrame capture returned an empty payload.";
                        return PersistenceContextOperationResult.Failed;
                    }

                    record = PersistenceContextRecord.CreateStateGraphContextRecord(
                        stableEntityId,
                        prefabKey,
                        payload);
                    return PersistenceContextOperationResult.Applied;
                }
                catch (Exception exception)
                {
                    failure = "StateGraph ContextFrame capture threw: " + exception.Message;
                    return PersistenceContextOperationResult.Failed;
                }
            }

            if (!TryResolveContext(out var context))
            {
                return PersistenceContextOperationResult.Unsupported;
            }

            bool captured = PersistenceContextAdapterRegistry.TryCapture(
                stableEntityId,
                context,
                out record);
            if (!captured || record == null)
            {
                return PersistenceContextOperationResult.Unsupported;
            }

            record.prefabKey = string.IsNullOrEmpty(record.prefabKey)
                ? prefabKey
                : record.prefabKey;
            return PersistenceContextOperationResult.Applied;
        }

        internal PersistenceContextOperationResult TryApplyDetailed(
            PersistenceContextRecord record,
            out string failure)
        {
            failure = string.Empty;
            if (record == null)
            {
                return PersistenceContextOperationResult.Unsupported;
            }

            if (record.IsStateGraphContextRecord)
            {
                return TryApplyStateGraphRecord(record, out failure);
            }

            if (record.HasStateGraphContextPayload)
            {
                failure =
                    "A StateGraph ContextFrame payload was found without its required record discriminator.";
                return PersistenceContextOperationResult.Failed;
            }

            if (!TryResolveContext(out var context))
            {
                return PersistenceContextOperationResult.Unsupported;
            }

            return PersistenceContextAdapterRegistry.TryApply(record, context)
                ? PersistenceContextOperationResult.Applied
                : PersistenceContextOperationResult.Unsupported;
        }

        internal bool TryScheduleDeferredApply(
            PersistenceContextRecord record,
            out string failure)
        {
            failure = string.Empty;
            if (record == null || !record.IsStateGraphContextRecord)
            {
                failure = "Only a StateGraph ContextFrame record can be deferred.";
                return false;
            }

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                failure =
                    "StateGraph ContextFrame apply cannot be deferred on an inactive runtime component.";
                return false;
            }

            _deferredApplyRecord = record;
            if (_deferredApplyCoroutine == null)
            {
                try
                {
                    _deferredApplyCoroutine = StartCoroutine(ApplyDeferredWhenHostIsLive());
                }
                catch (Exception exception)
                {
                    _deferredApplyRecord = null;
                    failure =
                        "StateGraph ContextFrame deferred apply could not start: " +
                        exception.Message;
                    return false;
                }
            }

            return true;
        }

        private PersistenceContextOperationResult TryApplyStateGraphRecord(
            PersistenceContextRecord record,
            out string failure)
        {
            failure = string.Empty;
            if (!record.HasUsableStateGraphContextPayload)
            {
                failure = "StateGraph ContextFrame record has no payload.";
                return PersistenceContextOperationResult.Failed;
            }

            var host = GetComponent<CoCoStateGraphHost>();
            if (host == null)
            {
                failure =
                    "StateGraph ContextFrame record requires a CoCoStateGraphHost on the same GameObject.";
                return PersistenceContextOperationResult.Failed;
            }

            switch (host.Lifecycle)
            {
                case CoCoRuntimeLifecycleState.Created:
                case CoCoRuntimeLifecycleState.Stopped:
                    return PersistenceContextOperationResult.Deferred;
                case CoCoRuntimeLifecycleState.Disposed:
                    failure = "StateGraph ContextFrame cannot be applied to a disposed Host.";
                    return PersistenceContextOperationResult.Failed;
                case CoCoRuntimeLifecycleState.Running:
                case CoCoRuntimeLifecycleState.Suspended:
                    break;
                default:
                    failure = $"StateGraph Host has unsupported lifecycle {host.Lifecycle}.";
                    return PersistenceContextOperationResult.Failed;
            }

            if (!record.TryGetStateGraphContextPayload(out byte[] payload))
            {
                failure = "StateGraph ContextFrame payload became unavailable before apply.";
                return PersistenceContextOperationResult.Failed;
            }

            try
            {
                bool applied = host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic diagnostic);
                if (!applied || diagnostic.IsError)
                {
                    failure = FormatDiagnostic(
                        "StateGraph ContextFrame apply was rejected.",
                        diagnostic);
                    return PersistenceContextOperationResult.Failed;
                }

                return PersistenceContextOperationResult.Applied;
            }
            catch (Exception exception)
            {
                failure = "StateGraph ContextFrame apply threw: " + exception.Message;
                return PersistenceContextOperationResult.Failed;
            }
        }

        private IEnumerator ApplyDeferredWhenHostIsLive()
        {
            while (_deferredApplyRecord != null)
            {
                PersistenceContextRecord record = _deferredApplyRecord;
                PersistenceContextOperationResult result = TryApplyDetailed(
                    record,
                    out string failure);
                if (result == PersistenceContextOperationResult.Deferred)
                {
                    yield return null;
                    continue;
                }

                if (!ReferenceEquals(record, _deferredApplyRecord))
                {
                    continue;
                }

                _deferredApplyRecord = null;
                _deferredApplyCoroutine = null;
                if (result == PersistenceContextOperationResult.Failed)
                {
                    Debug.LogError(
                        $"[PersistenceContext] Deferred StateGraph apply failed for " +
                        $"'{stableEntityId}': {failure}",
                        this);
                }
                else if (result == PersistenceContextOperationResult.Unsupported)
                {
                    Debug.LogError(
                        $"[PersistenceContext] Deferred StateGraph apply became unsupported for " +
                        $"'{stableEntityId}'.",
                        this);
                }

                yield break;
            }

            _deferredApplyCoroutine = null;
        }

        internal void CancelDeferredApply()
        {
            _deferredApplyRecord = null;
            if (_deferredApplyCoroutine == null) return;

            StopCoroutine(_deferredApplyCoroutine);
            _deferredApplyCoroutine = null;
        }

        private static string FormatDiagnostic(
            string fallback,
            in CoCoDiagnostic diagnostic)
        {
            return string.IsNullOrEmpty(diagnostic.Message)
                ? fallback
                : diagnostic.Message;
        }

        private bool TryResolveContext(out ICoCoContext context)
        {
            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null ||
                    ReferenceEquals(behaviour, this) ||
                    behaviour is CoCoStateGraphHost)
                {
                    continue;
                }

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
