using System;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Content
{
    internal sealed class DirectContentBackend : IContentBackend
    {
        private static readonly ContentBackendId Id = CreateId();
        private static readonly object SceneLoadGate = new object();
        private static readonly Queue<UniTaskCompletionSource> SceneLoadWaiters =
            new Queue<UniTaskCompletionSource>();
        private static bool sceneLoadInProgress;

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
            // SceneManager cannot cancel an in-progress scene load. ContentRuntime will
            // reclaim a successful late completion when every waiter has left.
            _ = lifetimeCancellationToken;
            await EnterSceneLoadGateAsync();
            await UniTask.SwitchToMainThread();
            try
            {
                var priorHandles = new HashSet<int>();
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    priorHandles.Add(SceneManager.GetSceneAt(index).handle);
                }

                AsyncOperation operation = SceneManager.LoadSceneAsync(
                    reference.Location,
                    LoadSceneMode.Additive);
                if (operation == null)
                {
                    return ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                        reference.Id,
                        "SceneManager did not create a load operation."));
                }

                await operation.ToUniTask();

                Scene loadedScene = default;
                // SceneManager does not return the loaded Scene from AsyncOperation.
                // Direct loads are serialized across every ContentRuntime, so the
                // newest matching handle that was absent before this operation is the
                // exact instance owned by this backend resource.
                for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
                {
                    Scene candidate = SceneManager.GetSceneAt(index);
                    if (priorHandles.Contains(candidate.handle)) continue;
                    if (!MatchesLocation(candidate, reference.Location)) continue;

                    loadedScene = candidate;
                    break;
                }

                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    return ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                        reference.Id,
                        "The exact additive Scene instance could not be identified."));
                }

                return ContentBackendLoadResult.Success(
                    loadedScene,
                    () => ReleaseSceneAsync(reference.Id, loadedScene));
            }
            catch (Exception exception)
            {
                return ContentBackendLoadResult.Failure(ContentErrors.LoadFailed(
                    reference.Id,
                    exception.Message));
            }
            finally
            {
                ExitSceneLoadGate();
            }
        }

        private static UniTask EnterSceneLoadGateAsync()
        {
            lock (SceneLoadGate)
            {
                if (!sceneLoadInProgress)
                {
                    sceneLoadInProgress = true;
                    return UniTask.CompletedTask;
                }

                var waiter = new UniTaskCompletionSource();
                SceneLoadWaiters.Enqueue(waiter);
                return waiter.Task;
            }
        }

        private static void ExitSceneLoadGate()
        {
            UniTaskCompletionSource next = null;
            lock (SceneLoadGate)
            {
                if (SceneLoadWaiters.Count == 0)
                {
                    sceneLoadInProgress = false;
                }
                else
                {
                    next = SceneLoadWaiters.Dequeue();
                }
            }

            next?.TrySetResult();
        }

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

        private static bool MatchesLocation(Scene scene, string location) =>
            string.Equals(scene.path, location, StringComparison.Ordinal) ||
            string.Equals(scene.name, location, StringComparison.Ordinal) ||
            string.Equals(System.IO.Path.GetFileNameWithoutExtension(scene.path), location,
                StringComparison.Ordinal);

        private static ContentBackendId CreateId()
        {
            ContentBackendId.TryCreate("direct", out ContentBackendId id);
            return id;
        }
    }
}
