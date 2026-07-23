using System;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Content
{
    public readonly struct ContentBackendRequest
    {
        internal ContentBackendRequest(ContentReference reference, Type valueType)
        {
            Reference = reference;
            ValueType = valueType;
        }

        public ContentReference Reference { get; }
        public Type ValueType { get; }
    }

    public interface IContentBackend
    {
        ContentBackendId BackendId { get; }
        bool CanHandle(ContentReference reference);

        UniTask<ContentBackendLoadResult> LoadAsync(
            ContentBackendRequest request,
            CancellationToken lifetimeCancellationToken);
    }

    public sealed class ContentBackendResource
    {
        private readonly Func<UniTask<CoCoDiagnostic>> releaseAsync;

        internal ContentBackendResource(
            object value,
            Type valueType,
            Func<UniTask<CoCoDiagnostic>> releaseAsync)
        {
            Value = value;
            ValueType = valueType;
            this.releaseAsync = releaseAsync;
        }

        public object Value { get; }
        public Type ValueType { get; }

        internal UniTask<CoCoDiagnostic> ReleaseAsync() =>
            releaseAsync == null
                ? UniTask.FromResult(CoCoDiagnostic.None)
                : releaseAsync();
    }

    public readonly struct ContentBackendLoadResult
    {
        private ContentBackendLoadResult(
            bool succeeded,
            ContentBackendResource resource,
            CoCoDiagnostic diagnostic)
        {
            Succeeded = succeeded;
            Resource = resource;
            Diagnostic = diagnostic;
        }

        public bool Succeeded { get; }
        public ContentBackendResource Resource { get; }
        public CoCoDiagnostic Diagnostic { get; }

        public static ContentBackendLoadResult Success<T>(
            T value,
            Func<UniTask<CoCoDiagnostic>> releaseAsync)
        {
            Type valueType = ReferenceEquals(value, null) ? typeof(T) : value.GetType();
            return new ContentBackendLoadResult(
                true,
                new ContentBackendResource(value, valueType, releaseAsync),
                CoCoDiagnostic.None);
        }

        public static ContentBackendLoadResult Failure(CoCoDiagnostic diagnostic)
        {
            if (!diagnostic.IsError)
            {
                throw new ArgumentException(
                    "A failed backend result requires an error diagnostic.",
                    nameof(diagnostic));
            }

            return new ContentBackendLoadResult(false, null, diagnostic);
        }
    }
}
