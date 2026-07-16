using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class CoCoStateGraphAssetIdentityPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingSavePaths =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool saveScheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            for (int index = 0; index < deletedAssets.Length; index++)
            {
                PendingSavePaths.Remove(deletedAssets[index]);
            }

            int movedCount = Math.Min(movedAssets.Length, movedFromAssetPaths.Length);
            for (int index = 0; index < movedCount; index++)
            {
                if (PendingSavePaths.Remove(movedFromAssetPaths[index]))
                {
                    PendingSavePaths.Add(movedAssets[index]);
                }
            }

            foreach (string assetPath in importedAssets)
            {
                ProcessAsset(assetPath);
            }

            foreach (string assetPath in movedAssets)
            {
                ProcessAsset(assetPath);
            }

            if (PendingSavePaths.Count > 0 && !saveScheduled)
            {
                saveScheduled = true;
                EditorApplication.delayCall += SavePendingAssets;
            }
        }

        private static void ProcessAsset(string assetPath)
        {
            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CoCoStateGraphAsset asset = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(assetPath);
            if (asset == null)
            {
                return;
            }

            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!asset.EnsureAssetIdentity(currentGuid))
            {
                return;
            }

            EditorUtility.SetDirty(asset);
            PendingSavePaths.Add(assetPath);
        }

        private static void SavePendingAssets()
        {
            saveScheduled = false;
            string[] assetPaths = new string[PendingSavePaths.Count];
            PendingSavePaths.CopyTo(assetPaths);
            PendingSavePaths.Clear();

            foreach (string assetPath in assetPaths)
            {
                CoCoStateGraphAsset asset = AssetDatabase.LoadAssetAtPath<CoCoStateGraphAsset>(assetPath);
                if (asset != null)
                {
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
        }
    }
}
