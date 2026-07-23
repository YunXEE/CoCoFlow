using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    [DisallowMultipleComponent]
    public sealed class CoCoPoolHost : MonoBehaviour
    {
        [SerializeField] private CoCoContentHost contentHost;
        [SerializeField, Min(1)] private int diagnosticCapacity = 256;
        [SerializeField] private bool captureRentalStacks = true;
        [SerializeField] private bool captureRentalStacksInRelease;

        public CoCoContentHost ContentHost => contentHost;
        public PoolRuntime Runtime { get; private set; }
        public CoCoDiagnostic LastDiagnostic { get; private set; }
        public bool IsInitialized => Runtime != null && !Runtime.IsDisposed;

        private void Awake()
        {
            if (TryInitialize(out CoCoDiagnostic diagnostic)) return;

            Debug.LogError(
                "CoCoPoolHost initialization failed: " + diagnostic.Message,
                this);
        }

        public bool TryInitialize(out CoCoDiagnostic diagnostic)
        {
            if (Runtime != null)
            {
                diagnostic = Runtime.IsDisposed
                    ? PoolingErrors.RuntimeDisposed()
                    : CoCoDiagnostic.None;
                LastDiagnostic = diagnostic;
                return !Runtime.IsDisposed;
            }

            if (contentHost == null)
            {
                diagnostic = PoolingErrors.InvalidProfile(
                    "CoCoPoolHost requires an explicit CoCoContentHost reference.");
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!contentHost.TryInitialize(out diagnostic))
            {
                LastDiagnostic = diagnostic;
                return false;
            }

            bool enableStacks = captureRentalStacks &&
                                (Debug.isDebugBuild || captureRentalStacksInRelease);
            if (!PoolRuntime.TryCreate(
                    contentHost.Runtime,
                    transform,
                    diagnosticCapacity,
                    enableStacks,
                    out PoolRuntime runtime,
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
            out PoolScope scope,
            out CoCoDiagnostic diagnostic)
        {
            if (Runtime == null && !TryInitialize(out diagnostic))
            {
                scope = null;
                return false;
            }

            bool succeeded = Runtime.TryCreateScope(
                ownerId,
                out scope,
                out diagnostic);
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
            Runtime?.ForceShutdown();
        }

        private void OnValidate()
        {
            if (diagnosticCapacity < 1) diagnosticCapacity = 1;
        }
    }
}
