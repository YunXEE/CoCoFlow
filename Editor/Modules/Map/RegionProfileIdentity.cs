using System;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;

namespace CoCoFlow.Editor.Modules.Map
{
    [InitializeOnLoad]
    internal sealed class RegionProfileIdentity : AssetPostprocessor
    {
        static RegionProfileIdentity()
        {
            EditorApplication.delayCall += SynchronizeAllProfiles;
        }

        internal static bool Synchronize(
            RegionProfile profile)
        {
            if (profile == null) return false;

            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string guid =
                AssetDatabase.AssetPathToGUID(path).ToLowerInvariant();
            if (!RegionProfileId.TryCreate(
                    guid,
                    out RegionProfileId profileId))
            {
                return false;
            }

            if (!profile.SetEditorIdentity(profileId))
            {
                return true;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return true;
        }

        private static void SynchronizeAllProfiles()
        {
            string[] guids =
                AssetDatabase.FindAssets(
                    "t:" + nameof(RegionProfile));
            Array.Sort(guids, StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                SynchronizePath(
                    AssetDatabase.GUIDToAssetPath(guids[index]));
            }
        }

        private static void SynchronizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            RegionProfile profile =
                AssetDatabase.LoadAssetAtPath<RegionProfile>(path);
            if (profile != null)
            {
                Synchronize(profile);
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            for (int index = 0;
                 index < importedAssets.Length;
                 index++)
            {
                SynchronizePath(importedAssets[index]);
            }

            for (int index = 0;
                 index < movedAssets.Length;
                 index++)
            {
                SynchronizePath(movedAssets[index]);
            }
        }
    }
}
