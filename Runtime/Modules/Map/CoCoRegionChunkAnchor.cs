using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Modules.Map
{
    [DisallowMultipleComponent]
    public sealed class CoCoRegionChunkAnchor :
        MonoBehaviour,
        IRegionFragmentResolver
    {
        [SerializeField] private RegionId regionId;
        [SerializeField] private RegionChunkId chunkId;
        [SerializeField] private List<GameObject> managedRoots =
            new List<GameObject>();

        public RegionId RegionId => regionId;
        public RegionChunkId ChunkId => chunkId;
        public IReadOnlyList<GameObject> ManagedRoots =>
            managedRoots ??
            (IReadOnlyList<GameObject>)Array.Empty<GameObject>();

        public bool TryValidateColdStart(out CoCoDiagnostic diagnostic) =>
            TryValidateColdStart(regionId, chunkId, out diagnostic);

        public bool TryValidateColdStart(
            RegionId expectedRegionId,
            RegionChunkId expectedChunkId,
            out CoCoDiagnostic diagnostic)
        {
            if (!expectedRegionId.IsValid ||
                !expectedChunkId.IsValid ||
                regionId != expectedRegionId ||
                chunkId != expectedChunkId)
            {
                diagnostic = RegionErrors.SceneContract(
                    "The chunk Anchor metadata does not match its compiled Region and Chunk identity.");
                return false;
            }

            Scene scene = gameObject.scene;
            if (!scene.IsValid() ||
                !scene.isLoaded ||
                transform.parent != null)
            {
                diagnostic = RegionErrors.SceneContract(
                    "The chunk Anchor must be a root in its loaded leased Scene.");
                return false;
            }

            Component[] metadataComponents = GetComponents<Component>();
            if (metadataComponents.Length != 2 ||
                metadataComponents[0] == null ||
                metadataComponents[1] == null)
            {
                diagnostic = RegionErrors.SceneContract(
                    "The chunk Anchor root may contain only Transform and CoCoRegionChunkAnchor metadata.");
                return false;
            }

            GameObject[] sceneRoots = scene.GetRootGameObjects();
            int anchorCount = 0;
            var expectedManagedRoots = new HashSet<GameObject>();
            for (int rootIndex = 0; rootIndex < sceneRoots.Length; rootIndex++)
            {
                GameObject root = sceneRoots[rootIndex];
                CoCoRegionChunkAnchor[] anchors =
                    root.GetComponentsInChildren<CoCoRegionChunkAnchor>(true);
                anchorCount += anchors.Length;

                if (root == gameObject) continue;
                if (root.activeSelf)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "Every managed chunk root must be inactive in the cold-start Scene.");
                    return false;
                }

                expectedManagedRoots.Add(root);
            }

            if (anchorCount != 1)
            {
                diagnostic = RegionErrors.SceneContract(
                    "A managed chunk Scene must contain exactly one Anchor.");
                return false;
            }

            if (managedRoots == null ||
                managedRoots.Count != expectedManagedRoots.Count)
            {
                diagnostic = RegionErrors.SceneContract(
                    "The Anchor managed-root list must contain every non-metadata Scene root exactly once.");
                return false;
            }

            var unique = new HashSet<GameObject>();
            for (int index = 0; index < managedRoots.Count; index++)
            {
                GameObject root = managedRoots[index];
                if (root == null ||
                    !unique.Add(root) ||
                    !expectedManagedRoots.Contains(root) ||
                    root.scene != scene ||
                    root.transform.parent != null ||
                    root.activeSelf)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "Managed roots must be unique, inactive roots from the Anchor Scene.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryResolveGameObject(
            string fragmentId,
            out GameObject gameObject,
            out CoCoDiagnostic diagnostic)
        {
            gameObject = null;
            if (!TrySplitFragment(fragmentId, out string[] segments))
            {
                diagnostic = RegionErrors.SceneContract(
                    "Fragment ids must be non-empty '/' separated relative paths.");
                return false;
            }

            GameObject rootMatch = null;
            for (int index = 0; index < ManagedRoots.Count; index++)
            {
                GameObject candidate = ManagedRoots[index];
                if (candidate == null ||
                    !string.Equals(
                        candidate.name,
                        segments[0],
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (rootMatch != null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "Fragment root '" + segments[0] +
                        "' is ambiguous in the leased chunk Scene.");
                    return false;
                }

                rootMatch = candidate;
            }

            if (rootMatch == null)
            {
                diagnostic = RegionErrors.SceneContract(
                    "Fragment root '" + segments[0] +
                    "' was not found in the leased chunk Scene.");
                return false;
            }

            Transform current = rootMatch.transform;
            for (int segmentIndex = 1;
                 segmentIndex < segments.Length;
                 segmentIndex++)
            {
                Transform childMatch = null;
                for (int childIndex = 0;
                     childIndex < current.childCount;
                     childIndex++)
                {
                    Transform child = current.GetChild(childIndex);
                    if (!string.Equals(
                            child.name,
                            segments[segmentIndex],
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (childMatch != null)
                    {
                        diagnostic = RegionErrors.SceneContract(
                            "Fragment segment '" + segments[segmentIndex] +
                            "' is ambiguous in the leased chunk Scene.");
                        return false;
                    }

                    childMatch = child;
                }

                if (childMatch == null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "Fragment '" + fragmentId +
                        "' was not found in the leased chunk Scene.");
                    return false;
                }

                current = childMatch;
            }

            gameObject = current.gameObject;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TrySplitFragment(
            string fragmentId,
            out string[] segments)
        {
            segments = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(fragmentId) ||
                !string.Equals(
                    fragmentId,
                    fragmentId.Trim(),
                    StringComparison.Ordinal) ||
                fragmentId.StartsWith("/", StringComparison.Ordinal) ||
                fragmentId.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            segments = fragmentId.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (string.IsNullOrEmpty(segments[index]) ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    segments = Array.Empty<string>();
                    return false;
                }
            }

            return true;
        }
    }
}
