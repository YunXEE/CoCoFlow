using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Content
{
    internal sealed class DirectContentBackend : IContentBackend
    {
        private readonly struct ResolvedSceneLocation
        {
            internal ResolvedSceneLocation(string canonicalPath, int buildIndex)
            {
                CanonicalPath = canonicalPath;
                BuildIndex = buildIndex;
            }

            internal string CanonicalPath { get; }
            internal int BuildIndex { get; }
        }

        private static readonly ContentBackendId Id = CreateId();
        private static readonly SemaphoreSlim SceneLoadGate = new SemaphoreSlim(1, 1);

        public ContentBackendId BackendId => Id;

        public bool CanHandle(ContentReference reference) =>
            reference.IsValid && reference.SourceKind == ContentSourceKind.Direct;

        public UniTask<ContentBackendLoadResult> LoadAsync(
            ContentBackendRequest request,
            CancellationToken lifetimeCancellationToken)
        {
            if (!CanHandle(request.Reference))
            {
                return UniTask.FromResult(ContentBackendLoadResult.Failure(
                    ContentErrors.InvalidReference(
                        "Direct backend requires one valid Direct ContentReference.")));
            }

            switch (request.Reference.Kind)
            {
                case ContentKind.Asset:
                case ContentKind.PrefabSource:
                    return UniTask.FromResult(LoadObject(request.Reference));
                case ContentKind.AdditiveScene:
                    return LoadSceneAsync(request.Reference, lifetimeCancellationToken);
                default:
                    return UniTask.FromResult(ContentBackendLoadResult.Failure(
                        ContentErrors.InvalidReference(
                            "Direct backend does not support the requested Content kind.")));
            }
        }

        private static ContentBackendLoadResult LoadObject(ContentReference reference)
        {
            UnityEngine.Object value = reference.DirectObject;
            if (value == null)
            {
                return ContentBackendLoadResult.Failure(ContentErrors.InvalidReference(
                    "Direct Asset/Prefab Source Content requires a live Unity Object."));
            }

            return ContentBackendLoadResult.Success(
                value,
                () => UniTask.FromResult(CoCoDiagnostic.None));
        }

        private static async UniTask<ContentBackendLoadResult> LoadSceneAsync(
            ContentReference reference,
            CancellationToken lifetimeCancellationToken)
        {
            bool gateEntered = false;
            bool physicalLoadStarted = false;
            HashSet<int> priorHandles = null;
            ResolvedSceneLocation resolvedLocation = default;
            try
            {
                lifetimeCancellationToken.ThrowIfCancellationRequested();
                await UniTask.SwitchToMainThread();
                if (!TryResolveBuildSettingsScene(reference.Location, out resolvedLocation))
                {
                    return ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                        reference.Id,
                        "Direct additive Scene locator '" + reference.Location +
                        "' did not match a Scene in Build Settings."));
                }

                await SceneLoadGate.WaitAsync(lifetimeCancellationToken);
                gateEntered = true;
                await UniTask.SwitchToMainThread();
                lifetimeCancellationToken.ThrowIfCancellationRequested();

                priorHandles = new HashSet<int>();
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    priorHandles.Add(SceneManager.GetSceneAt(index).handle);
                }

                lifetimeCancellationToken.ThrowIfCancellationRequested();
                AsyncOperation operation = SceneManager.LoadSceneAsync(
                    resolvedLocation.CanonicalPath,
                    LoadSceneMode.Additive);
                if (operation == null)
                {
                    return ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                        reference.Id,
                        "SceneManager did not create a load operation."));
                }

                physicalLoadStarted = true;
                // SceneManager cannot cancel an in-progress scene load. Once the
                // operation exists, observe it to completion so ContentRuntime can
                // reclaim a successful late completion after all waiters leave.
                await operation.ToUniTask();

                // SceneManager does not return the loaded Scene from AsyncOperation.
                // The process-wide gate serializes Direct backend loads, and the
                // before/after handle set identifies this operation's new instance.
                // Hosts must not start an out-of-band SceneManager load of the same
                // canonical path while a Direct load is in flight: Unity exposes no
                // operation-to-Scene handle correlation for two identical loads.
                if (!TryFindNewScene(priorHandles, resolvedLocation, out Scene loadedScene))
                {
                    return CreateUnidentifiedSceneFailure(reference.Id);
                }

                if (!loadedScene.isLoaded ||
                    !string.Equals(
                        NormalizeScenePath(loadedScene.path),
                        NormalizeScenePath(resolvedLocation.CanonicalPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ContentBackendLoadResult.FailureWithCleanup(
                        ContentErrors.LoadFailed(
                            reference.Id,
                            "The identified additive Scene did not match the resolved " +
                            "Build Settings Scene."),
                        () => ReleaseSceneAsync(reference.Id, loadedScene));
                }

                return ContentBackendLoadResult.Success(
                    loadedScene,
                    () => ReleaseSceneAsync(reference.Id, loadedScene));
            }
            catch (OperationCanceledException) when (
                !physicalLoadStarted && lifetimeCancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                CoCoDiagnostic diagnostic = ContentErrors.LoadFailed(
                    reference.Id,
                    exception.Message);
                if (physicalLoadStarted && priorHandles != null &&
                    TryFindNewScene(priorHandles, resolvedLocation, out Scene loadedScene))
                {
                    return ContentBackendLoadResult.FailureWithCleanup(
                        diagnostic,
                        () => ReleaseSceneAsync(reference.Id, loadedScene));
                }

                if (physicalLoadStarted)
                {
                    return ContentBackendLoadResult.FailureWithCleanup(
                        diagnostic,
                        () => UniTask.FromResult(ContentErrors.ReleaseFailed(
                            reference.Id,
                            "A started Direct Scene load did not expose an identifiable " +
                            "Scene instance for cleanup.")));
                }

                return ContentBackendLoadResult.Failure(diagnostic);
            }
            finally
            {
                if (gateEntered)
                {
                    SceneLoadGate.Release();
                }
            }
        }

        private static bool TryResolveBuildSettingsScene(
            string location,
            out ResolvedSceneLocation resolvedLocation)
        {
            string normalizedLocation = NormalizeScenePath(location);
            if (string.IsNullOrEmpty(normalizedLocation))
            {
                resolvedLocation = default;
                return false;
            }

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            for (int buildIndex = 0; buildIndex < sceneCount; buildIndex++)
            {
                string canonicalPath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                if (!MatchesBuildSettingsLocation(canonicalPath, normalizedLocation)) continue;

                resolvedLocation = new ResolvedSceneLocation(canonicalPath, buildIndex);
                return true;
            }

            resolvedLocation = default;
            return false;
        }

        private static bool MatchesBuildSettingsLocation(
            string canonicalPath,
            string normalizedLocation)
        {
            string normalizedCanonicalPath = NormalizeScenePath(canonicalPath);
            if (string.Equals(
                    normalizedCanonicalPath,
                    normalizedLocation,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string sceneName = Path.GetFileName(normalizedCanonicalPath);
            if (normalizedLocation.IndexOf('/') < 0)
            {
                return string.Equals(
                    sceneName,
                    normalizedLocation,
                    StringComparison.OrdinalIgnoreCase);
            }

            return normalizedCanonicalPath.EndsWith(
                "/" + normalizedLocation,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeScenePath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            normalized = normalized.TrimStart('/').TrimEnd('/');
            if (normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - ".unity".Length);
            }

            return normalized;
        }

        private static bool TryFindNewScene(
            HashSet<int> priorHandles,
            ResolvedSceneLocation resolvedLocation,
            out Scene loadedScene)
        {
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (priorHandles.Contains(candidate.handle)) continue;
                if (candidate.buildIndex != resolvedLocation.BuildIndex &&
                    !string.Equals(
                        NormalizeScenePath(candidate.path),
                        NormalizeScenePath(resolvedLocation.CanonicalPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                loadedScene = candidate;
                return loadedScene.IsValid();
            }

            loadedScene = default;
            return false;
        }

        private static ContentBackendLoadResult CreateUnidentifiedSceneFailure(
            ContentId contentId) =>
            ContentBackendLoadResult.FailureWithCleanup(
                ContentErrors.LoadFailed(
                    contentId,
                    "The exact additive Scene instance could not be identified."),
                () => UniTask.FromResult(ContentErrors.ReleaseFailed(
                    contentId,
                    "A started Direct Scene load did not expose an identifiable Scene " +
                    "instance for cleanup.")));

        private static async UniTask<CoCoDiagnostic> ReleaseSceneAsync(
            ContentId contentId,
            Scene scene)
        {
            try
            {
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return ContentErrors.ReleaseFailed(
                        contentId,
                        "The owned Scene instance was unloaded outside Content.");
                }

                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                if (operation == null)
                {
                    return ContentErrors.ReleaseFailed(
                        contentId,
                        "SceneManager did not create an unload operation.");
                }

                await operation.ToUniTask();
                return CoCoDiagnostic.None;
            }
            catch (Exception exception)
            {
                return ContentErrors.ReleaseFailed(contentId, exception.Message);
            }
        }

        private static ContentBackendId CreateId()
        {
            ContentBackendId.TryCreate("direct", out ContentBackendId id);
            return id;
        }
    }
}
