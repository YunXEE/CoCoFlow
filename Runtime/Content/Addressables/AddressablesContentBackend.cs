using System;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Content
{
    /// <summary>
    /// Optional Addressables adapter. Addressables operation handles remain owned by
    /// the backend resource's private release closure and never cross the Content API.
    /// </summary>
    [AddComponentMenu("CoCoFlow/Content/Addressables Content Backend")]
    [DisallowMultipleComponent]
    public sealed class AddressablesContentBackend : MonoBehaviour, IContentBackend
    {
        private static readonly ContentBackendId Id = CreateBackendId();

        public ContentBackendId BackendId => Id;

        public bool CanHandle(ContentReference reference) =>
            reference.IsValid && reference.SourceKind == ContentSourceKind.Addressables;

        public async UniTask<ContentBackendLoadResult> LoadAsync(
            ContentBackendRequest request,
            CancellationToken lifetimeCancellationToken)
        {
            // Addressables operations cannot be cancelled reliably. ContentRuntime owns
            // lifetime cancellation and reclaims a resource that completes after teardown.
            _ = lifetimeCancellationToken;

            if (!CanHandle(request.Reference))
            {
                return ContentBackendLoadResult.Failure(Error(
                    CoCoDiagnosticCode.InvalidContentReference,
                    "Addressables backend requires one valid Addressables ContentReference."));
            }

            try
            {
                switch (request.Reference.Kind)
                {
                    case ContentKind.Asset:
                    case ContentKind.PrefabSource:
                        return await LoadObjectAsync(request.Reference);
                    case ContentKind.AdditiveScene:
                        return await LoadSceneAsync(request.Reference);
                    default:
                        return ContentBackendLoadResult.Failure(Error(
                            CoCoDiagnosticCode.InvalidContentReference,
                            "Addressables backend does not support the requested Content kind."));
                }
            }
            catch (Exception exception)
            {
                return ContentBackendLoadResult.Failure(Error(
                    CoCoDiagnosticCode.ContentLoadFailed,
                    "Addressables load failed before a resource became owned: " +
                    exception.Message));
            }
        }

        private static async UniTask<ContentBackendLoadResult> LoadObjectAsync(
            ContentReference reference)
        {
            AsyncOperationHandle<UnityEngine.Object> handle = default;
            bool ownsHandle = false;
            try
            {
                handle = Addressables.LoadAssetAsync<UnityEngine.Object>(reference.Location);
                ownsHandle = true;
                AddressablesCompletion<UnityEngine.Object> completion =
                    await ObserveAsync(handle);
                if (!completion.Succeeded || completion.Result == null)
                {
                    string reason = completion.ErrorMessage;
                    string cleanupFailure = ReleaseFailedLoad(handle, ref ownsHandle);
                    return ContentBackendLoadResult.Failure(Error(
                        CoCoDiagnosticCode.ContentLoadFailed,
                        "Addressables could not load Content '" + reference.Id + "' from '" +
                        reference.Location + "'. " + reason + cleanupFailure));
                }

                UnityEngine.Object value = completion.Result;
                ownsHandle = false;
                return ContentBackendLoadResult.Success(
                    value,
                    () => ReleaseObjectAsync(reference.Id, handle));
            }
            catch (Exception exception)
            {
                string cleanupFailure = string.Empty;
                if (ownsHandle)
                {
                    cleanupFailure = ReleaseFailedLoad(handle, ref ownsHandle);
                }

                return ContentBackendLoadResult.Failure(Error(
                    CoCoDiagnosticCode.ContentLoadFailed,
                    "Addressables could not load Content '" + reference.Id + "'. " +
                    exception.Message + cleanupFailure));
            }
        }

        private static async UniTask<ContentBackendLoadResult> LoadSceneAsync(
            ContentReference reference)
        {
            AsyncOperationHandle<SceneInstance> handle = default;
            bool ownsHandle = false;
            try
            {
                handle = Addressables.LoadSceneAsync(
                    reference.Location,
                    LoadSceneMode.Additive,
                    true);
                ownsHandle = true;
                AddressablesCompletion<SceneInstance> completion = await ObserveAsync(handle);
                if (!completion.Succeeded)
                {
                    string reason = completion.ErrorMessage;
                    string cleanupFailure = ReleaseFailedLoad(handle, ref ownsHandle);
                    return ContentBackendLoadResult.Failure(Error(
                        CoCoDiagnosticCode.ContentLoadFailed,
                        "Addressables could not load additive Scene Content '" +
                        reference.Id + "' from '" + reference.Location + "'. " + reason +
                        cleanupFailure));
                }

                Scene scene = completion.Result.Scene;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    string cleanupFailure = ReleaseFailedLoad(handle, ref ownsHandle);
                    return ContentBackendLoadResult.Failure(Error(
                        CoCoDiagnosticCode.ContentLoadFailed,
                        "Addressables returned an invalid or unloaded Scene for Content '" +
                        reference.Id + "' from '" + reference.Location + "'." +
                        cleanupFailure));
                }

                ownsHandle = false;
                return ContentBackendLoadResult.Success(
                    scene,
                    () => ReleaseSceneAsync(reference.Id, handle));
            }
            catch (Exception exception)
            {
                string cleanupFailure = string.Empty;
                if (ownsHandle)
                {
                    cleanupFailure = ReleaseFailedLoad(handle, ref ownsHandle);
                }

                return ContentBackendLoadResult.Failure(Error(
                    CoCoDiagnosticCode.ContentLoadFailed,
                    "Addressables could not load additive Scene Content '" +
                    reference.Id + "'. " + exception.Message + cleanupFailure));
            }
        }

        private static UniTask<CoCoDiagnostic> ReleaseObjectAsync(
            ContentId contentId,
            AsyncOperationHandle<UnityEngine.Object> handle)
        {
            try
            {
                if (!handle.IsValid())
                {
                    return UniTask.FromResult(Error(
                        CoCoDiagnosticCode.ContentReleaseFailed,
                        "Addressables Asset/Prefab Source handle for Content '" + contentId +
                        "' was invalid before release."));
                }

                Addressables.Release(handle);
                return UniTask.FromResult(CoCoDiagnostic.None);
            }
            catch (Exception exception)
            {
                return UniTask.FromResult(Error(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    "Addressables could not release Asset/Prefab Source Content '" + contentId +
                    "'. " + exception.Message));
            }
        }

        private static async UniTask<CoCoDiagnostic> ReleaseSceneAsync(
            ContentId contentId,
            AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid())
            {
                return Error(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    "Addressables Scene handle for Content '" + contentId +
                    "' was invalid before unload.");
            }

            try
            {
                AsyncOperationHandle<SceneInstance> unloadHandle =
                    Addressables.UnloadSceneAsync(handle, true);
                AddressablesCompletion<SceneInstance> completion =
                    await ObserveAsync(unloadHandle);
                return completion.Succeeded
                    ? CoCoDiagnostic.None
                    : Error(
                        CoCoDiagnosticCode.ContentReleaseFailed,
                        "Addressables could not unload Scene Content '" + contentId +
                        "'. " + completion.ErrorMessage);
            }
            catch (Exception exception)
            {
                return Error(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    "Addressables could not unload Scene Content '" + contentId +
                    "'. " + exception.Message);
            }
        }

        private static UniTask<AddressablesCompletion<T>> ObserveAsync<T>(
            AsyncOperationHandle<T> handle)
        {
            if (handle.IsDone)
            {
                return UniTask.FromResult(AddressablesCompletion<T>.Capture(handle));
            }

            return ObserveIncompleteAsync(handle);
        }

        private static async UniTask<AddressablesCompletion<T>> ObserveIncompleteAsync<T>(
            AsyncOperationHandle<T> handle)
        {
            var completionSource =
                new UniTaskCompletionSource<AddressablesCompletion<T>>();
            Action<AsyncOperationHandle<T>> onCompleted = completed =>
                completionSource.TrySetResult(AddressablesCompletion<T>.Capture(completed));
            handle.Completed += onCompleted;
            try
            {
                // Close the small race between the caller's IsDone check and event
                // subscription without depending on UniTask.Addressables adapters.
                if (handle.IsDone)
                {
                    completionSource.TrySetResult(
                        AddressablesCompletion<T>.Capture(handle));
                }

                return await completionSource.Task;
            }
            finally
            {
                handle.Completed -= onCompleted;
            }
        }

        private static string ReleaseFailedLoad<T>(
            AsyncOperationHandle<T> handle,
            ref bool ownsHandle)
        {
            if (!ownsHandle)
            {
                return string.Empty;
            }

            ownsHandle = false;
            if (!handle.IsValid())
            {
                return " Failed-load handle was already invalid during reclamation.";
            }

            try
            {
                Addressables.Release(handle);
                return string.Empty;
            }
            catch (Exception exception)
            {
                return " Failed-load handle reclamation also failed: " +
                       exception.Message;
            }
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Content, code, message);

        private static ContentBackendId CreateBackendId()
        {
            if (ContentBackendId.TryCreate("addressables", out ContentBackendId id))
            {
                return id;
            }

            throw new InvalidOperationException(
                "The frozen Addressables Content backend ID is invalid.");
        }

        private readonly struct AddressablesCompletion<T>
        {
            private AddressablesCompletion(
                bool succeeded,
                T result,
                string errorMessage)
            {
                Succeeded = succeeded;
                Result = result;
                ErrorMessage = errorMessage;
            }

            public bool Succeeded { get; }
            public T Result { get; }
            public string ErrorMessage { get; }

            public static AddressablesCompletion<T> Capture(
                AsyncOperationHandle<T> handle)
            {
                bool succeeded = handle.Status == AsyncOperationStatus.Succeeded;
                string message = handle.OperationException == null
                    ? string.Empty
                    : handle.OperationException.Message;
                return new AddressablesCompletion<T>(
                    succeeded,
                    succeeded ? handle.Result : default,
                    message);
            }
        }
    }
}
