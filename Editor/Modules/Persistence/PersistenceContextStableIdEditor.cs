using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Editor.Modules.Persistence
{
    [InitializeOnLoad]
    internal static class PersistenceContextStableIdEditor
    {
        private static readonly Dictionary<string, PersistenceContext> _owners =
            new Dictionary<string, PersistenceContext>(StringComparer.Ordinal);
        private static readonly HashSet<PersistenceContext> _registered =
            new HashSet<PersistenceContext>();
        private static int _processedRevision = -1;
        private static bool _synchronizing;

        static PersistenceContextStableIdEditor()
        {
            EditorApplication.update += EditorApplicationOnUpdate;
            EditorApplication.hierarchyChanged += EditorApplicationOnHierarchyChanged;
            EditorApplication.playModeStateChanged += EditorApplicationOnPlayModeStateChanged;
            EditorSceneManager.sceneSaving += EditorSceneManagerOnSceneSaving;
        }

        private static void EditorApplicationOnUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (_processedRevision == PersistenceContext.EditorIdentityRevision) return;
            Synchronize();
        }

        private static void EditorApplicationOnHierarchyChanged()
        {
            PersistenceContext.RequestEditorIdentityValidation();
        }

        private static void EditorApplicationOnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Synchronize();
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                _registered.Clear();
                PersistenceContext.RequestEditorIdentityValidation();
            }
        }

        private static void EditorSceneManagerOnSceneSaving(Scene scene, string path)
        {
            // Do this synchronously: a new scene still has an empty path, and a copy
            // can be saved before the next hierarchyChanged/update notification.
            Synchronize();
        }

        internal static void Synchronize()
        {
            if (_synchronizing || Application.isPlaying || BuildPipeline.isBuildingPlayer) return;
            _synchronizing = true;
            try
            {
                int revision = PersistenceContext.EditorIdentityRevision;
                var contexts = CollectSceneContexts();
                var live = new HashSet<PersistenceContext>(contexts);
                var staleIds = new List<string>();
                foreach (var pair in _owners)
                {
                    if (pair.Value == null || !live.Contains(pair.Value) ||
                        pair.Value.StableEntityId != pair.Key) staleIds.Add(pair.Key);
                }
                foreach (string id in staleIds) _owners.Remove(id);
                _registered.RemoveWhere(context => context == null || !live.Contains(context) ||
                                                   !context.isActiveAndEnabled);

                foreach (PersistenceContext context in contexts)
                {
                    string id = context.StableEntityId;
                    bool duplicate = !string.IsNullOrEmpty(id) &&
                                     _owners.TryGetValue(id, out var owner) && owner != context;
                    if (string.IsNullOrEmpty(id) || duplicate)
                    {
                        PersistenceContextRegistry.Unregister(context);
                        _registered.Remove(context);
                        do { id = Guid.NewGuid().ToString("N"); }
                        while (_owners.ContainsKey(id));
                        var serialized = new SerializedObject(context);
                        serialized.FindProperty("stableEntityId").stringValue = id;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        if (PrefabUtility.IsPartOfPrefabInstance(context))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(context);
                        EditorUtility.SetDirty(context);
                        EditorSceneManager.MarkSceneDirty(context.gameObject.scene);
                    }

                    // Keep known owners across passes so a new copy cannot take its
                    // source's ID even if hierarchy traversal visits the copy first.
                    _owners[id] = context;
                    if (context.isActiveAndEnabled && _registered.Add(context))
                        PersistenceContextRegistry.Register(context);
                }

                _processedRevision = revision;
            }
            finally
            {
                _synchronizing = false;
            }
        }

        private static List<PersistenceContext> CollectSceneContexts()
        {
            var contexts = new List<PersistenceContext>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (PersistenceContext context in root.GetComponentsInChildren<PersistenceContext>(true))
                    {
                        if (context == null || EditorUtility.IsPersistent(context)) continue;
                        if ((context.hideFlags & HideFlags.DontSaveInEditor) != 0 ||
                            (context.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
                        if (PrefabStageUtility.GetPrefabStage(context.gameObject) != null) continue;
                        contexts.Add(context);
                    }
                }
            }
            return contexts;
        }
    }
}
