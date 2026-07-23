using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Content
{
    [DisallowMultipleComponent]
    public sealed class CoCoContentHost : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour[] backendComponents =
            System.Array.Empty<MonoBehaviour>();
        [SerializeField, Min(1)] private int diagnosticCapacity = 256;
        [SerializeField] private bool captureLeaseStacks = true;
        [SerializeField] private bool captureLeaseStacksInRelease;

        public ContentRuntime Runtime { get; private set; }
        public CoCoDiagnostic LastDiagnostic { get; private set; }
        public bool IsInitialized => Runtime != null && !Runtime.IsDisposed;

        private void Awake()
        {
            if (TryInitialize(out CoCoDiagnostic diagnostic)) return;

            Debug.LogError(
                "CoCoContentHost initialization failed: " + diagnostic.Message,
                this);
        }

        public bool TryInitialize(out CoCoDiagnostic diagnostic)
        {
            if (Runtime != null)
            {
                diagnostic = Runtime.IsDisposed
                    ? ContentErrors.RuntimeDisposed()
                    : CoCoDiagnostic.None;
                LastDiagnostic = diagnostic;
                return !Runtime.IsDisposed;
            }

            var additionalBackends = new List<IContentBackend>(backendComponents.Length);
            foreach (MonoBehaviour component in backendComponents)
            {
                if (component == null) continue;
                if (!(component is IContentBackend backend))
                {
                    diagnostic = ContentErrors.InvalidReference(
                        "Every CoCoContentHost backend component must implement IContentBackend.");
                    LastDiagnostic = diagnostic;
                    return false;
                }

                additionalBackends.Add(backend);
            }

            bool enableStacks = captureLeaseStacks &&
                                (Debug.isDebugBuild || captureLeaseStacksInRelease);
            if (!ContentRuntime.TryCreate(
                    additionalBackends,
                    diagnosticCapacity,
                    enableStacks,
                    out ContentRuntime runtime,
                    out diagnostic))
            {
                LastDiagnostic = diagnostic;
                return false;
            }

            Runtime = runtime;
            LastDiagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCreateScope(
            ContentOwnerId ownerId,
            out ContentScope scope,
            out CoCoDiagnostic diagnostic)
        {
            if (Runtime == null && !TryInitialize(out diagnostic))
            {
                scope = null;
                return false;
            }

            bool succeeded = Runtime.TryCreateScope(ownerId, out scope, out diagnostic);
            LastDiagnostic = diagnostic;
            return succeeded;
        }

        public async UniTask<CoCoDiagnostic> ShutdownAsync()
        {
            if (Runtime == null) return CoCoDiagnostic.None;

            LastDiagnostic = await Runtime.ShutdownAsync();
            return LastDiagnostic;
        }

        private void OnDestroy()
        {
            ShutdownAsync().Forget();
        }

        private void OnValidate()
        {
            if (diagnosticCapacity < 1) diagnosticCapacity = 1;
        }
    }
}
