using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    public sealed class PoolRuntime
    {
        private sealed class ContentShutdownParticipant :
            IContentRuntimeShutdownParticipant
        {
            private readonly PoolRuntime runtime;

            internal ContentShutdownParticipant(PoolRuntime runtime)
            {
                this.runtime = runtime;
            }

            public UniTask<CoCoDiagnostic> DrainBeforeContentShutdownAsync()
            {
                return runtime.DrainBeforeContentShutdownAsync();
            }
        }

        private readonly ContentRuntime contentRuntime;
        private readonly ContentShutdownParticipant contentShutdownParticipant;
        private readonly HashSet<PoolScope> scopes = new HashSet<PoolScope>();
        private readonly PoolDiagnosticLedger ledger;
        private readonly bool captureRentalStacks;
        private readonly GameObject retentionRootObject;
        private long nextScopeSequence = 1;
        private long nextInstanceSequence = 1;
        private bool shutdownStarted;
        private bool isShuttingDown;
        private bool isDisposed;
        private bool forceShutdownRequested;
        private bool retentionDestroyScheduled;
        private Task<CoCoDiagnostic> shutdownTask;
        private TaskCompletionSource<CoCoDiagnostic> shutdownCompletion;

        private PoolRuntime(
            ContentRuntime contentRuntime,
            GameObject retentionRootObject,
            int diagnosticCapacity,
            bool captureRentalStacks)
        {
            this.contentRuntime = contentRuntime;
            this.retentionRootObject = retentionRootObject;
            this.captureRentalStacks = captureRentalStacks;
            ledger = new PoolDiagnosticLedger(diagnosticCapacity);
            contentShutdownParticipant = new ContentShutdownParticipant(this);
        }

        public bool IsShuttingDown => isShuttingDown;
        public bool IsDisposed => isDisposed;
        public bool CaptureRentalStacks => captureRentalStacks;

        internal ContentRuntime ContentRuntime => contentRuntime;
        internal Transform RetentionRoot => retentionRootObject == null
            ? null
            : retentionRootObject.transform;
        internal PoolDiagnosticLedger Ledger => ledger;

        public static bool TryCreate(
            ContentRuntime contentRuntime,
            Transform ownerRoot,
            int diagnosticCapacity,
            bool captureRentalStacks,
            out PoolRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (contentRuntime == null ||
                contentRuntime.IsShuttingDown ||
                contentRuntime.IsDisposed)
            {
                diagnostic = PoolingErrors.InvalidProfile(
                    "A live ContentRuntime is required.");
                return false;
            }

            if (ownerRoot == null)
            {
                diagnostic = PoolingErrors.InvalidProfile(
                    "A live owner Transform is required.");
                return false;
            }

            if (diagnosticCapacity <= 0)
            {
                diagnostic = PoolingErrors.InvalidProfile(
                    "Pooling diagnostic capacity must be greater than zero.");
                return false;
            }

            GameObject root = null;
            try
            {
                root = new GameObject("[CoCoFlow Pooling]");
                root.SetActive(false);
                root.transform.SetParent(ownerRoot, false);
                runtime = new PoolRuntime(
                    contentRuntime,
                    root,
                    diagnosticCapacity,
                    captureRentalStacks);
                if (!contentRuntime.TryRegisterShutdownParticipant(
                        runtime.contentShutdownParticipant))
                {
                    DestroyUnityObject(root);
                    runtime = null;
                    diagnostic = PoolingErrors.InvalidProfile(
                        "The ContentRuntime could not register Pooling shutdown ownership.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception exception)
            {
                if (root != null) DestroyUnityObject(root);
                diagnostic = PoolingErrors.InvalidProfile(
                    "Failed to create the inactive retention root. " +
                    exception.Message);
                return false;
            }
        }

        public static bool TryCreate(
            ContentRuntime contentRuntime,
            Transform ownerRoot,
            out PoolRuntime runtime,
            out CoCoDiagnostic diagnostic) =>
            TryCreate(
                contentRuntime,
                ownerRoot,
                256,
                false,
                out runtime,
                out diagnostic);

        public bool TryCreateScope(
            ContentOwnerId ownerId,
            out PoolScope scope,
            out CoCoDiagnostic diagnostic)
        {
            scope = null;
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            if (isShuttingDown || isDisposed)
            {
                diagnostic = PoolingErrors.RuntimeDisposed();
                return false;
            }

            if (!ownerId.IsValid)
            {
                diagnostic = PoolingErrors.InvalidProfile(
                    "A valid ContentOwnerId is required.");
                return false;
            }

            if (!contentRuntime.TryCreateScope(
                    ownerId,
                    out ContentScope contentScope,
                    out diagnostic))
            {
                return false;
            }

            long scopeSequence = nextScopeSequence++;
            scope = new PoolScope(
                this,
                contentScope,
                ownerId,
                scopeSequence);
            scopes.Add(scope);
            ledger.Record(
                PoolDiagnosticEventKind.ScopeCreated,
                ownerId,
                scopeSequence,
                default,
                0,
                0,
                PooledInstanceState.Internal,
                0,
                0,
                0,
                CoCoDiagnostic.None);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public PoolRuntimeSnapshot CaptureSnapshot()
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                throw new InvalidOperationException(
                    "Pool Runtime snapshots must be captured on the Unity main thread.");
            }

            var snapshots = new List<PoolScopeSnapshot>(scopes.Count);
            foreach (PoolScope scope in scopes)
            {
                snapshots.Add(scope.CaptureSnapshot());
            }

            snapshots.Sort((left, right) =>
                left.ScopeSequence.CompareTo(right.ScopeSequence));
            return new PoolRuntimeSnapshot(
                isShuttingDown,
                isDisposed,
                snapshots.ToArray(),
                ledger.Capture());
        }

        public UniTask<CoCoDiagnostic> ShutdownAsync()
        {
            if (shutdownStarted) return AwaitSharedTaskAsync(shutdownTask);
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                return UniTask.FromResult(PoolingErrors.MainThreadRequired());
            }

            if (isDisposed)
            {
                return UniTask.FromResult(PoolingErrors.RuntimeDisposed());
            }

            shutdownCompletion = new TaskCompletionSource<CoCoDiagnostic>();
            shutdownTask = shutdownCompletion.Task;
            shutdownStarted = true;
            CompleteShutdownAsync(shutdownCompletion).Forget();
            return AwaitSharedTaskAsync(shutdownTask);
        }

        internal long AllocateInstanceSequence()
        {
            return nextInstanceSequence++;
        }

        internal void OnScopeClosed(PoolScope scope)
        {
            scopes.Remove(scope);
            if (forceShutdownRequested)
            {
                TryFinalizeForcedShutdown();
            }
        }

        internal bool Owns(PoolScope scope)
        {
            return scope != null &&
                   ReferenceEquals(scope.Runtime, this) &&
                   scopes.Contains(scope);
        }

        internal bool TryClearInactive(
            long scopeSequence,
            PoolId poolId,
            out CoCoDiagnostic diagnostic)
        {
            if (!PoolingMainThreadGuard.IsMainThread)
            {
                diagnostic = PoolingErrors.MainThreadRequired();
                return false;
            }

            foreach (PoolScope scope in scopes)
            {
                if (scope.ScopeSequence != scopeSequence) continue;
                return scope.TryClearInactive(poolId, out diagnostic);
            }

            diagnostic = PoolingErrors.OwnerMismatch(poolId, scopeSequence);
            return false;
        }

        internal void ForceShutdown()
        {
            if (isDisposed || forceShutdownRequested) return;

            isShuttingDown = true;
            forceShutdownRequested = true;
            if (!shutdownStarted)
            {
                shutdownCompletion = new TaskCompletionSource<CoCoDiagnostic>();
                shutdownTask = shutdownCompletion.Task;
                shutdownStarted = true;
            }

            ledger.Record(
                PoolDiagnosticEventKind.ForcedShutdown,
                default,
                0,
                default,
                0,
                0,
                PooledInstanceState.Internal,
                0,
                0,
                0,
                PoolingErrors.ForcedShutdown());
            PoolScope[] liveScopes = CaptureScopes();
            foreach (PoolScope scope in liveScopes)
            {
                scope.ForceClose();
            }

            ScheduleRetentionRootDestroy();
            TryFinalizeForcedShutdown();
        }

        private async UniTask CompleteShutdownAsync(
            TaskCompletionSource<CoCoDiagnostic> completion)
        {
            CoCoDiagnostic result = CoCoDiagnostic.None;
            try
            {
                isShuttingDown = true;
                PoolScope[] liveScopes = CaptureScopes();
                var pendingClose = new UniTask<CoCoDiagnostic>[liveScopes.Length];
                for (int index = 0; index < liveScopes.Length; index++)
                {
                    pendingClose[index] = liveScopes[index].CloseAsync();
                }

                for (int index = 0; index < pendingClose.Length; index++)
                {
                    CoCoDiagnostic diagnostic = await pendingClose[index];
                    await UniTask.SwitchToMainThread();
                    if (!diagnostic.IsNone) result = diagnostic;
                }

                scopes.Clear();
                ScheduleRetentionRootDestroy();
                isDisposed = true;
                contentRuntime.UnregisterShutdownParticipant(
                    contentShutdownParticipant);
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                result = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Pooling,
                    CoCoDiagnosticCode.PoolForcedShutdown,
                    "Pool Runtime shutdown failed. " + exception.Message);
                ForceShutdown();
            }

            if (!forceShutdownRequested)
            {
                completion.TrySetResult(result);
            }
        }

        private PoolScope[] CaptureScopes()
        {
            var snapshot = new PoolScope[scopes.Count];
            scopes.CopyTo(snapshot);
            return snapshot;
        }

        private void TryFinalizeForcedShutdown()
        {
            if (!forceShutdownRequested || scopes.Count != 0)
            {
                return;
            }

            isDisposed = true;
            contentRuntime.UnregisterShutdownParticipant(
                contentShutdownParticipant);
            shutdownCompletion?.TrySetResult(PoolingErrors.ForcedShutdown());
        }

        private UniTask<CoCoDiagnostic> DrainBeforeContentShutdownAsync()
        {
            if (isDisposed)
            {
                return UniTask.FromResult(CoCoDiagnostic.None);
            }

            ForceShutdown();
            return AwaitSharedTaskAsync(shutdownTask);
        }

        private void ScheduleRetentionRootDestroy()
        {
            if (retentionDestroyScheduled)
            {
                return;
            }

            retentionDestroyScheduled = true;
            DestroyUnityObject(retentionRootObject);
        }

        private static async UniTask<T> AwaitSharedTaskAsync<T>(Task<T> task)
        {
            return await task;
        }

        private static void DestroyUnityObject(GameObject instance)
        {
            if (instance == null) return;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
