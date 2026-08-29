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

    internal sealed class ContentBackendFailureCleanup
    {
        private Func<UniTask<CoCoDiagnostic>> cleanupAsync;
        private int claimed;
        private int executionStarted;

        internal ContentBackendFailureCleanup(
            Func<UniTask<CoCoDiagnostic>> cleanupAsync)
        {
            this.cleanupAsync = cleanupAsync;
        }

        internal bool RetainsAuthority =>
            Volatile.Read(ref cleanupAsync) != null;

        internal bool ExecutionStarted =>
            Volatile.Read(ref executionStarted) != 0;

        internal bool TryClaim()
        {
            return Interlocked.CompareExchange(ref claimed, 1, 0) == 0;
        }

        internal bool TryBeginExecution(
            out Func<UniTask<CoCoDiagnostic>> cleanup)
        {
            if (Interlocked.CompareExchange(ref executionStarted, 1, 0) != 0)
            {
                cleanup = null;
                return false;
            }

            cleanup = Volatile.Read(ref cleanupAsync);
            return cleanup != null;
        }

        internal void ClearAuthority()
        {
            Interlocked.Exchange(ref cleanupAsync, null);
        }
    }

    public readonly struct ContentBackendLoadResult
    {
        private ContentBackendLoadResult(
            bool succeeded,
            ContentBackendResource resource,
            CoCoDiagnostic diagnostic,
            ContentBackendFailureCleanup failureCleanup)
        {
            Succeeded = succeeded;
            Resource = resource;
            Diagnostic = diagnostic;
            FailureCleanup = failureCleanup;
        }

        public bool Succeeded { get; }
        public ContentBackendResource Resource { get; }
        public CoCoDiagnostic Diagnostic { get; }
        internal ContentBackendFailureCleanup FailureCleanup { get; }

        public static ContentBackendLoadResult Success<T>(
            T value,
            Func<UniTask<CoCoDiagnostic>> releaseAsync)
        {
            Type valueType = ReferenceEquals(value, null) ? typeof(T) : value.GetType();
            return new ContentBackendLoadResult(
                true,
                new ContentBackendResource(value, valueType, releaseAsync),
                CoCoDiagnostic.None,
                null);
        }

        public static ContentBackendLoadResult Failure(CoCoDiagnostic diagnostic)
        {
            ValidateFailureDiagnostic(diagnostic);

            return new ContentBackendLoadResult(false, null, diagnostic, null);
        }

        /// <summary>
        /// Reports a failed load that acquired backend ownership requiring one
        /// Runtime-controlled cleanup attempt.
        /// </summary>
        /// <param name="diagnostic">The original load-failure diagnostic.</param>
        /// <param name="cleanupAsync">
        /// The cleanup authority retained internally by the Content Runtime.
        /// </param>
        /// <returns>A failed result whose resource and raw backend handle remain hidden.</returns>
        public static ContentBackendLoadResult FailureWithCleanup(
            CoCoDiagnostic diagnostic,
            Func<UniTask<CoCoDiagnostic>> cleanupAsync)
        {
            ValidateFailureDiagnostic(diagnostic);
            if (cleanupAsync == null)
            {
                throw new ArgumentNullException(nameof(cleanupAsync));
            }

            return new ContentBackendLoadResult(
                false,
                null,
                diagnostic,
                new ContentBackendFailureCleanup(cleanupAsync));
        }

        internal bool TryTakeFailureCleanup(
            out ContentBackendFailureCleanup failureCleanup)
        {
            if (FailureCleanup != null && FailureCleanup.TryClaim())
            {
                failureCleanup = FailureCleanup;
                return true;
            }

            failureCleanup = null;
            return false;
        }

        private static void ValidateFailureDiagnostic(CoCoDiagnostic diagnostic)
        {
            if (diagnostic.IsError) return;

            throw new ArgumentException(
                "A failed backend result requires an error diagnostic.",
                nameof(diagnostic));
        }
    }
}
