using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    internal static class PersistenceContextPrefabIds
    {
        internal sealed class Entry
        {
            internal string Path;
            internal PersistenceContext Context;
            internal string Id;
            internal bool Locked;
            internal string Location => Path + " :: " + Context.name;
        }

        internal sealed class Report
        {
            internal readonly List<Entry> Entries = new List<Entry>();
            internal int PrefabCount => Entries.Select(entry => entry.Path).Distinct().Count();
            internal int LockedCount => Entries.Count(entry => entry.Locked);
            internal IEnumerable<Entry> EmptyIds => Entries.Where(entry => string.IsNullOrWhiteSpace(entry.Id));
            internal IEnumerable<IGrouping<string, Entry>> Duplicates => Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .GroupBy(entry => entry.Id, StringComparer.Ordinal).Where(group => group.Count() > 1);
            internal bool IsValid => !EmptyIds.Any() && !Duplicates.Any();

            internal string Conflicts(PersistenceContext context)
            {
                string ownPath = AssetPath(context) ?? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(context);
                return string.Join("\n", Entries.Where(entry => entry.Path != ownPath &&
                    !string.IsNullOrWhiteSpace(entry.Id) && entry.Id == context.StableEntityId)
                    .Select(entry => entry.Location));
            }

            internal void Log()
            {
                Debug.Log($"[PersistenceContext] Prefabs: {PrefabCount}, Contexts: {Entries.Count}, " +
                          $"Locked: {LockedCount}, Unlocked: {Entries.Count - LockedCount}, " +
                          $"Empty IDs: {EmptyIds.Count()}, Duplicate groups: {Duplicates.Count()}, Valid: {IsValid}");
                foreach (Entry entry in EmptyIds)
                    Debug.LogWarning("[PersistenceContext] Empty ID: " + entry.Location, entry.Context);
                foreach (var group in Duplicates)
                    Debug.LogWarning($"[PersistenceContext] Duplicate ID '{group.Key}':\n" +
                                     string.Join("\n", group.Select(entry => entry.Location)), group.First().Context);
            }
        }

        internal static Report Scan(string[] folders = null)
        {
            var report = new Report();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", folders)
                         .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                GameObject root = stage != null && stage.assetPath == path
                    ? stage.prefabContentsRoot : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) throw new InvalidOperationException("Cannot scan Prefab: " + path);
                foreach (var context in root.GetComponentsInChildren<PersistenceContext>(true))
                {
                    // Nested references are counted at their owning asset; variants are independent assets.
                    if (!Owns(root, context)) continue;
                    var data = new SerializedObject(context);
                    report.Entries.Add(new Entry { Path = path, Context = context, Id = context.StableEntityId,
                        Locked = data.FindProperty("idLocked").boolValue });
                }
            }
            return report;
        }

        internal static bool CanEdit(PersistenceContext context)
        {
            string path = AssetPath(context);
            return !EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrEmpty(path) &&
                   AssetDatabase.IsOpenForEdit(path);
        }

        internal static string AssetPath(PersistenceContext context)
        {
            if (context == null) return null;
            var stage = PrefabStageUtility.GetPrefabStage(context.gameObject);
            string path = stage != null ? stage.assetPath : AssetDatabase.GetAssetPath(context);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return null;
            GameObject root = stage != null ? stage.prefabContentsRoot : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return root != null && Owns(root, context) ? path : null;
        }

        internal static string Regenerate(PersistenceContext context, string[] folders = null)
        {
            if (!CanEdit(context)) throw new InvalidOperationException("Open the owning editable Prefab asset first.");
            var data = new SerializedObject(context);
            if (data.FindProperty("idLocked").boolValue) throw new InvalidOperationException("Unlock the ID first.");
            Report report = Scan(folders);
            report.Log();
            string path = AssetPath(context);
            Entry dependent = report.Entries.FirstOrDefault(entry => entry.Path != path && InheritsIdFrom(entry.Context, path));
            if (dependent != null)
                throw new InvalidOperationException("Regenerate the inherited variant ID first: " + dependent.Path);
            var used = new HashSet<string>(report.Entries.Select(entry => entry.Id), StringComparer.Ordinal);
            string id;
            do { id = Guid.NewGuid().ToString("N"); } while (used.Contains(id));
            data.Update();
            data.FindProperty("stableEntityId").stringValue = id;
            data.ApplyModifiedProperties();
            if (EditorUtility.IsPersistent(context))
                PrefabUtility.SavePrefabAsset(context.transform.root.gameObject);
            else
                EditorSceneManager.MarkSceneDirty(context.gameObject.scene);
            Debug.Log($"[PersistenceContext] Generated ID '{id}' for {AssetPath(context)}", context);
            return id;
        }

        private static bool Owns(GameObject root, PersistenceContext context)
        {
            GameObject nearest = PrefabUtility.GetNearestPrefabInstanceRoot(context);
            return nearest == null || nearest == root;
        }

        private static bool InheritsIdFrom(PersistenceContext context, string path)
        {
            while (context != null && !new SerializedObject(context).FindProperty("stableEntityId").prefabOverride)
            {
                context = PrefabUtility.GetCorrespondingObjectFromSource(context);
                if (context != null && AssetDatabase.GetAssetPath(context) == path) return true;
            }
            return false;
        }
    }
}
