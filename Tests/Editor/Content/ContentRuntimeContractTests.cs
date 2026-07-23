using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Content.Tests
{
    public sealed class ContentRuntimeContractTests
    {
        [UnityTest]
        public IEnumerator TwoOverlappingWaitersShareOneMultiAwaitLoadCompletion() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scopeA = CreateScope(runtime, "owner.multi-a");
                ContentScope scopeB = CreateScope(runtime, "owner.multi-b");
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.multi-waiter");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> requestA =
                        scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> requestB =
                        scopeB.AcquireAssetAsync<RuntimeContractAsset>(reference);

                    Assert.AreEqual(1, backend.LoadCount);
                    backend.CompleteSuccess(0, asset);
                    ContentAcquireResult<RuntimeContractAsset> resultA = await requestA;
                    ContentAcquireResult<RuntimeContractAsset> resultB = await requestB;

                    Assert.IsTrue(resultA.Succeeded, resultA.Diagnostic.Message);
                    Assert.IsTrue(resultB.Succeeded, resultB.Diagnostic.Message);
                    Assert.AreSame(asset, resultA.Lease.Value);
                    Assert.AreSame(asset, resultB.Lease.Value);
                    Assert.AreNotSame(resultA.Lease, resultB.Lease);
                    resultA.Lease.Dispose();
                    Assert.AreEqual(0, backend.ReleaseCount);
                    resultB.Lease.Dispose();
                    Assert.AreEqual(1, backend.ReleaseCount);
                }
                finally
                {
                    scopeA.Dispose();
                    scopeB.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator TwoReacquiresShareReleaseThenSuccessorLoadCompletions() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope firstScope = CreateScope(runtime, "owner.release-first");
                ContentScope scopeA = CreateScope(runtime, "owner.release-a");
                ContentScope scopeB = CreateScope(runtime, "owner.release-b");
                var firstAsset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var successorAsset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.release-waiters");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> firstRequest =
                        firstScope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    backend.CompleteSuccess(0, firstAsset, holdRelease: true);
                    ContentAcquireResult<RuntimeContractAsset> first = await firstRequest;
                    first.Lease.Dispose();
                    Assert.AreEqual(1, backend.PendingReleaseCount);

                    UniTask<ContentAcquireResult<RuntimeContractAsset>> requestA =
                        scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> requestB =
                        scopeB.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.AreEqual(1, backend.LoadCount);

                    backend.CompleteRelease(0);
                    for (int index = 0; index < 128 && backend.LoadCount < 2; index++)
                    {
                        await UniTask.Yield();
                    }

                    Assert.AreEqual(2, backend.LoadCount);
                    backend.CompleteSuccess(1, successorAsset);
                    ContentAcquireResult<RuntimeContractAsset> resultA = await requestA;
                    ContentAcquireResult<RuntimeContractAsset> resultB = await requestB;
                    Assert.IsTrue(resultA.Succeeded, resultA.Diagnostic.Message);
                    Assert.IsTrue(resultB.Succeeded, resultB.Diagnostic.Message);
                    Assert.AreSame(successorAsset, resultA.Lease.Value);
                    Assert.AreSame(successorAsset, resultB.Lease.Value);
                    resultA.Lease.Dispose();
                    resultB.Lease.Dispose();
                    Assert.AreEqual(2, backend.ReleaseCount);
                }
                finally
                {
                    firstScope.Dispose();
                    scopeA.Dispose();
                    scopeB.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(firstAsset);
                    UnityEngine.Object.DestroyImmediate(successorAsset);
                }
            });

        [UnityTest]
        public IEnumerator TwoShutdownCallersShareOneCompletion() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.shutdown");
                var lateAsset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.shutdown");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> request =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    UniTask<CoCoDiagnostic> shutdownA = runtime.ShutdownAsync();
                    UniTask<CoCoDiagnostic> shutdownB = runtime.ShutdownAsync();

                    ContentAcquireResult<RuntimeContractAsset> cancelled = await request;
                    Assert.IsTrue(cancelled.Cancelled);
                    backend.CompleteSuccess(0, lateAsset);
                    CoCoDiagnostic diagnosticA = await shutdownA;
                    CoCoDiagnostic diagnosticB = await shutdownB;

                    Assert.AreEqual(diagnosticA, diagnosticB);
                    Assert.IsTrue(diagnosticA.IsNone, diagnosticA.Message);
                    Assert.IsTrue(runtime.IsDisposed);
                    Assert.AreEqual(1, backend.ReleaseCount);
                }
                finally
                {
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(lateAsset);
                }
            });

        [UnityTest]
        public IEnumerator WorkerCancellationReturnsAndMutatesRegistryOnMainThread() =>
            UniTask.ToCoroutine(async () =>
            {
                int mainThreadId = Environment.CurrentManagedThreadId;
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.worker-cancel");
                var lateAsset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var cancellation = new CancellationTokenSource();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.worker-cancel");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> request =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(
                            reference,
                            cancellation.Token);
                    await Task.Run(() => cancellation.Cancel());
                    ContentAcquireResult<RuntimeContractAsset> result = await request;

                    Assert.IsTrue(result.Cancelled);
                    Assert.AreEqual(mainThreadId, Environment.CurrentManagedThreadId);
                    Assert.AreEqual(mainThreadId, backend.GetLoadCancellationThreadId(0));

                    Type snapshotException = await Task.Run(() =>
                    {
                        try
                        {
                            runtime.CaptureSnapshot();
                            return null;
                        }
                        catch (Exception exception)
                        {
                            return exception.GetType();
                        }
                    });
                    Assert.AreEqual(typeof(InvalidOperationException), snapshotException);

                    backend.CompleteSuccess(0, lateAsset);
                    for (int index = 0; index < 128 && backend.ReleaseCount == 0; index++)
                    {
                        await UniTask.Yield();
                    }

                    Assert.AreEqual(1, backend.ReleaseCount);
                }
                finally
                {
                    cancellation.Dispose();
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(lateAsset);
                }
            });

        [UnityTest]
        public IEnumerator WorkerTryCreateIsRejectedBeforeBackendEnumeration() =>
            UniTask.ToCoroutine(async () =>
            {
                ContentMainThreadGuard.CaptureCurrentThread();
                var backends = new CountingBackendEnumerable();

                WorkerCreationResult workerResult = await Task.Run(() =>
                {
                    bool created = ContentRuntime.TryCreate(
                        backends,
                        32,
                        false,
                        out ContentRuntime runtime,
                        out CoCoDiagnostic diagnostic);
                    return new WorkerCreationResult(created, runtime, diagnostic);
                });

                Assert.IsFalse(workerResult.Created);
                Assert.IsNull(workerResult.Runtime);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentMainThreadRequired,
                    workerResult.Diagnostic.Code);
                Assert.AreEqual(
                    0,
                    backends.EnumerationCount,
                    "Worker creation must fail before custom backends are enumerated.");

                Assert.IsTrue(ContentRuntime.TryCreate(
                    backends,
                    32,
                    false,
                    out ContentRuntime mainThreadRuntime,
                    out CoCoDiagnostic mainThreadDiagnostic),
                    mainThreadDiagnostic.Message);
                Assert.AreEqual(1, backends.EnumerationCount);
                await mainThreadRuntime.ShutdownAsync();
            });

        [UnityTest]
        public IEnumerator WorkerCompletedMismatchReleaseFinalizesOnMainThread() =>
            UniTask.ToCoroutine(async () =>
            {
                int mainThreadId = Environment.CurrentManagedThreadId;
                var wrongAsset = ScriptableObject.CreateInstance<WrongRuntimeContractAsset>();
                var backend = new WorkerMismatchBackend(wrongAsset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.worker-mismatch");
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.worker-mismatch");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> request =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    await backend.CompleteReleaseFromWorkerAsync();
                    ContentAcquireResult<RuntimeContractAsset> result = await request;

                    Assert.AreEqual(ContentAcquireStatus.Failed, result.Status);
                    Assert.AreEqual(CoCoDiagnosticCode.ContentTypeMismatch,
                        result.Diagnostic.Code);
                    Assert.AreEqual(mainThreadId, Environment.CurrentManagedThreadId);
                    Assert.AreNotEqual(mainThreadId, backend.ReleaseCompletionThreadId);
                    Assert.AreEqual(1, backend.ReleaseCount);
                    Assert.AreEqual(0, runtime.CaptureSnapshot().Entries.Count);
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            ContentDiagnosticEventKind.LoadFailed,
                            ContentDiagnosticEventKind.ReleaseStarted,
                            ContentDiagnosticEventKind.ReleaseSucceeded
                        },
                        runtime.CaptureSnapshot().Diagnostics
                            .Where(record =>
                                record.ContentId == reference.Id &&
                                (record.EventKind ==
                                 ContentDiagnosticEventKind.LoadFailed ||
                                 record.EventKind ==
                                 ContentDiagnosticEventKind.ReleaseStarted ||
                                 record.EventKind ==
                                 ContentDiagnosticEventKind.ReleaseSucceeded ||
                                 record.EventKind ==
                                 ContentDiagnosticEventKind.ReleaseFailed))
                            .Select(record => record.EventKind)
                            .ToArray());
                }
                finally
                {
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(wrongAsset);
                }
            });

        [UnityTest]
        public IEnumerator MismatchReleaseDiagnosticLeavesOneReleaseTombstone() =>
            UniTask.ToCoroutine(async () =>
            {
                await AssertMismatchReleaseFailureLeavesTombstoneAsync(
                    MismatchReleaseOutcome.DiagnosticFailure,
                    "content.mismatch-release-diagnostic");
            });

        [UnityTest]
        public IEnumerator MismatchReleaseThrowLeavesOneReleaseTombstone() =>
            UniTask.ToCoroutine(async () =>
            {
                await AssertMismatchReleaseFailureLeavesTombstoneAsync(
                    MismatchReleaseOutcome.Throw,
                    "content.mismatch-release-throw");
            });

        [UnityTest]
        public IEnumerator SynchronousReentrantLoadAndReleaseSeePublishedPromises() =>
            UniTask.ToCoroutine(async () =>
            {
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var backend = new ReentrantBackend(asset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scopeA = CreateScope(runtime, "owner.reentrant-a");
                ContentScope scopeB = CreateScope(runtime, "owner.reentrant-b");
                ContentScope scopeC = CreateScope(runtime, "owner.reentrant-c");
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.reentrant");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> nestedLoad = default;
                    backend.OnLoad = () =>
                    {
                        backend.OnLoad = null;
                        nestedLoad = scopeB.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    };

                    ContentAcquireResult<RuntimeContractAsset> outer =
                        await scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    ContentAcquireResult<RuntimeContractAsset> nested = await nestedLoad;
                    Assert.IsTrue(outer.Succeeded, outer.Diagnostic.Message);
                    Assert.IsTrue(nested.Succeeded, nested.Diagnostic.Message);
                    Assert.AreEqual(1, backend.LoadCount);

                    UniTask<ContentAcquireResult<RuntimeContractAsset>> duringRelease = default;
                    backend.OnRelease = () =>
                    {
                        backend.OnRelease = null;
                        duringRelease = scopeC.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    };
                    outer.Lease.Dispose();
                    nested.Lease.Dispose();
                    ContentAcquireResult<RuntimeContractAsset> reacquired =
                        await duringRelease;

                    Assert.IsTrue(reacquired.Succeeded, reacquired.Diagnostic.Message);
                    Assert.AreEqual(2, backend.LoadCount);
                    Assert.AreEqual(1, backend.ReleaseCount);
                    reacquired.Lease.Dispose();
                    Assert.AreEqual(2, backend.ReleaseCount);
                }
                finally
                {
                    scopeA.Dispose();
                    scopeB.Dispose();
                    scopeC.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator SynchronousReleaseReentrySeesPublishedShutdownPromise() =>
            UniTask.ToCoroutine(async () =>
            {
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var backend = new ReentrantBackend(asset);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.reentrant-shutdown");
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.reentrant-shutdown");
                    ContentAcquireResult<RuntimeContractAsset> acquired =
                        await scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.IsTrue(acquired.Succeeded, acquired.Diagnostic.Message);

                    UniTask<CoCoDiagnostic> reentrantShutdown = default;
                    backend.OnRelease = () =>
                    {
                        backend.OnRelease = null;
                        reentrantShutdown = runtime.ShutdownAsync();
                    };
                    UniTask<CoCoDiagnostic> firstShutdown = runtime.ShutdownAsync();
                    CoCoDiagnostic first = await firstShutdown;
                    CoCoDiagnostic reentrant = await reentrantShutdown;

                    Assert.AreEqual(first, reentrant);
                    Assert.IsTrue(first.IsNone, first.Message);
                    Assert.IsTrue(runtime.IsDisposed);
                }
                finally
                {
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator OneCancelledWaiterDoesNotCancelSharedPhysicalLoad() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scopeA = CreateScope(runtime, "owner.a");
                ContentScope scopeB = CreateScope(runtime, "owner.b");
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var cancellation = new CancellationTokenSource();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.shared");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> requestA =
                        scopeA.AcquireAssetAsync<RuntimeContractAsset>(
                            reference,
                            cancellation.Token);
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> requestB =
                        scopeB.AcquireAssetAsync<RuntimeContractAsset>(reference);

                    Assert.AreEqual(1, backend.LoadCount);
                    cancellation.Cancel();
                    ContentAcquireResult<RuntimeContractAsset> cancelled = await requestA;
                    Assert.IsTrue(cancelled.Cancelled);
                    Assert.AreEqual(CoCoDiagnosticCode.ContentRequestCancelled,
                        cancelled.Diagnostic.Code);
                    Assert.IsFalse(backend.GetLoadCancellation(0).IsCancellationRequested);

                    backend.CompleteSuccess(0, asset);
                    ContentAcquireResult<RuntimeContractAsset> acquired = await requestB;
                    Assert.IsTrue(acquired.Succeeded, acquired.Diagnostic.Message);
                    Assert.AreSame(asset, acquired.Lease.Value);
                    Assert.AreEqual(1, backend.LoadCount);

                    acquired.Lease.Dispose();
                    Assert.AreEqual(1, backend.ReleaseCount);
                }
                finally
                {
                    cancellation.Dispose();
                    scopeA.Dispose();
                    scopeB.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator AbandonedGenerationIsRecycledBeforeSuccessorLoads() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope firstScope = CreateScope(runtime, "owner.abandoned");
                ContentScope successorScope = CreateScope(runtime, "owner.successor");
                var lateAsset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var successorAsset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var cancellation = new CancellationTokenSource();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.abandoned");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> firstRequest =
                        firstScope.AcquireAssetAsync<RuntimeContractAsset>(
                            reference,
                            cancellation.Token);
                    cancellation.Cancel();
                    ContentAcquireResult<RuntimeContractAsset> cancelled = await firstRequest;
                    Assert.IsTrue(cancelled.Cancelled);
                    Assert.IsTrue(backend.GetLoadCancellation(0).IsCancellationRequested);

                    UniTask<ContentAcquireResult<RuntimeContractAsset>> successorRequest =
                        successorScope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.AreEqual(
                        1,
                        backend.LoadCount,
                        "A successor must not attach to or overlap the abandoned generation.");

                    backend.CompleteSuccess(0, lateAsset);
                    for (int index = 0; index < 128 && backend.LoadCount < 2; index++)
                    {
                        await UniTask.Yield();
                    }

                    Assert.AreEqual(1, backend.ReleaseCount,
                        "Late success must be released without publishing a lease.");
                    Assert.AreEqual(2, backend.LoadCount,
                        "The successor starts only after the old generation is removed.");

                    backend.CompleteSuccess(1, successorAsset);
                    ContentAcquireResult<RuntimeContractAsset> successor =
                        await successorRequest;
                    Assert.IsTrue(successor.Succeeded, successor.Diagnostic.Message);
                    Assert.AreSame(successorAsset, successor.Lease.Value);
                    Assert.AreNotSame(lateAsset, successor.Lease.Value);
                    successor.Lease.Dispose();
                    Assert.AreEqual(2, backend.ReleaseCount);
                }
                finally
                {
                    cancellation.Dispose();
                    firstScope.Dispose();
                    successorScope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(lateAsset);
                    UnityEngine.Object.DestroyImmediate(successorAsset);
                }
            });

        [UnityTest]
        public IEnumerator DisposedLeaseClearsValueAndLeavesLiveScope() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend, captureStacks: true);
                ContentScope scope = CreateScope(runtime, "owner.long-lived");
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.short-lease");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> pending =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    backend.CompleteSuccess(0, asset);
                    ContentAcquireResult<RuntimeContractAsset> result = await pending;
                    ContentLease<RuntimeContractAsset> lease = result.Lease;
                    long scopeSequence = scope.ScopeSequence;

                    Assert.IsTrue(result.Succeeded, result.Diagnostic.Message);
                    Assert.AreSame(asset, lease.Value);
                    Assert.AreEqual(scopeSequence, lease.ScopeSequence);
                    lease.Dispose();

                    Assert.IsFalse(scope.IsDisposed);
                    Assert.IsTrue(lease.IsReleased);
                    Assert.IsNull(lease.Value,
                        "A released lease must not keep the Unity Object strongly referenced.");
                    Assert.AreEqual(1, backend.ReleaseCount);
                    Assert.AreEqual(0, runtime.CaptureSnapshot().Entries.Count);

                    ContentDiagnosticRecord released = runtime.CaptureSnapshot().Diagnostics
                        .Last(record => record.EventKind ==
                                        ContentDiagnosticEventKind.LeaseReleased);
                    Assert.AreEqual(scopeSequence, released.ScopeSequence);
                    Assert.AreEqual(scope.OwnerId, released.OwnerId);
                    Assert.AreEqual(lease.LeaseSequence, released.LeaseSequence);
                    Assert.IsNotEmpty(released.AllocationStack);
                    Assert.IsNotEmpty(released.ReleaseStack);
                }
                finally
                {
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator ScopeDisposeWaitsForCancellationBeforeEndRequestDisposesSource() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.scope-race");
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var requestCancellation = new CancellationTokenSource();
                var disposalMarked = new ManualResetEventSlim(false);
                var allowCancellation = new ManualResetEventSlim(false);
                Task disposeTask = null;
                try
                {
                    ContentReference heldReference = CreateAddressableAssetReference(
                        "content.scope-race-held");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> heldRequest =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(heldReference);
                    backend.CompleteSuccess(0, asset);
                    ContentAcquireResult<RuntimeContractAsset> held = await heldRequest;
                    Assert.IsTrue(held.Succeeded, held.Diagnostic.Message);

                    ContentReference pendingReference = CreateAddressableAssetReference(
                        "content.scope-race-pending");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> pendingRequest =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(
                            pendingReference,
                            requestCancellation.Token);

                    disposeTask = Task.Run(() => scope.Dispose(() =>
                    {
                        disposalMarked.Set();
                        if (!allowCancellation.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "The Scope disposal race barrier timed out.");
                        }
                    }));

                    for (int index = 0; index < 256 && !disposalMarked.IsSet; index++)
                    {
                        await UniTask.Yield();
                    }

                    Assert.IsTrue(disposalMarked.IsSet);
                    requestCancellation.Cancel();
                    ContentAcquireResult<RuntimeContractAsset> cancelled = await pendingRequest;
                    Assert.IsTrue(cancelled.Cancelled);

                    allowCancellation.Set();
                    await disposeTask;
                    backend.CompleteFailure(1, "late cancelled load");
                    for (int index = 0;
                         index < 128 &&
                         (backend.ReleaseCount == 0 ||
                          runtime.CaptureSnapshot().Entries.Count != 0);
                         index++)
                    {
                        await UniTask.Yield();
                    }

                    Assert.IsTrue(scope.IsDisposed);
                    Assert.IsTrue(held.Lease.IsReleased);
                    Assert.IsNull(held.Lease.Value);
                    Assert.AreEqual(1, backend.ReleaseCount);
                    Assert.AreEqual(0, runtime.CaptureSnapshot().Entries.Count);
                    Assert.AreEqual(0, GetTrackedScopeCount(runtime));

                    scope.Dispose();
                    Assert.AreEqual(
                        1,
                        backend.ReleaseCount,
                        "Repeated Scope disposal must remain idempotent.");
                    Assert.AreEqual(0, GetTrackedScopeCount(runtime));
                }
                finally
                {
                    allowCancellation.Set();
                    if (disposeTask != null)
                    {
                        await disposeTask;
                    }

                    for (int index = 0; index < backend.LoadCount; index++)
                    {
                        backend.CompleteFailure(index, "test cleanup");
                    }

                    requestCancellation.Dispose();
                    disposalMarked.Dispose();
                    allowCancellation.Dispose();
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator LoadFailureRetriesButReleaseFailureLeavesTombstone() =>
            UniTask.ToCoroutine(async () =>
            {
                var backend = new ControlledBackend();
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.retry");
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.retry");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> failedRequest =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    backend.CompleteFailure(0, "first load failed");
                    ContentAcquireResult<RuntimeContractAsset> failed = await failedRequest;
                    Assert.AreEqual(ContentAcquireStatus.Failed, failed.Status);

                    UniTask<ContentAcquireResult<RuntimeContractAsset>> retryRequest =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.AreEqual(2, backend.LoadCount);
                    backend.CompleteSuccess(1, asset, releaseFails: true);
                    ContentAcquireResult<RuntimeContractAsset> acquired = await retryRequest;
                    Assert.IsTrue(acquired.Succeeded, acquired.Diagnostic.Message);

                    acquired.Lease.Dispose();
                    ContentRuntimeSnapshot tombstone = runtime.CaptureSnapshot();
                    Assert.AreEqual(1, tombstone.Entries.Count);
                    Assert.AreEqual(ContentEntryState.ReleaseFailed,
                        tombstone.Entries[0].State);
                    Assert.AreEqual(CoCoDiagnosticCode.ContentReleaseFailed,
                        tombstone.Entries[0].Diagnostic.Code);

                    ContentAcquireResult<RuntimeContractAsset> blocked =
                        await scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.AreEqual(ContentAcquireStatus.Failed, blocked.Status);
                    Assert.AreEqual(CoCoDiagnosticCode.ContentReleaseFailed,
                        blocked.Diagnostic.Code);
                    Assert.AreEqual(2, backend.LoadCount,
                        "A release tombstone must block a second physical generation.");
                }
                finally
                {
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator FailedLoadCleanupSuccessRemovesEntryAndAllowsRetry() =>
            UniTask.ToCoroutine(async () =>
            {
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                var backend = new FailureCleanupBackend(
                    asset,
                    FailureCleanupOutcome.Success);
                ContentRuntime runtime = CreateRuntime(backend);
                ContentScope scope = CreateScope(runtime, "owner.cleanup-success");
                try
                {
                    ContentReference reference = CreateAddressableAssetReference(
                        "content.cleanup-success");
                    UniTask<ContentAcquireResult<RuntimeContractAsset>> failedRequest =
                        scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    backend.CompleteFailedLoad();
                    ContentAcquireResult<RuntimeContractAsset> failed = await failedRequest;

                    Assert.AreEqual(ContentAcquireStatus.Failed, failed.Status);
                    Assert.AreEqual(
                        CoCoDiagnosticCode.ContentLoadFailed,
                        failed.Diagnostic.Code);
                    Assert.AreEqual(1, backend.CleanupCount);
                    Assert.AreEqual(0, runtime.CaptureSnapshot().Entries.Count);
                    Assert.IsTrue(runtime.CaptureSnapshot().Diagnostics.Any(record =>
                        record.EventKind == ContentDiagnosticEventKind.ReleaseSucceeded));

                    ContentAcquireResult<RuntimeContractAsset> retried =
                        await scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.IsTrue(retried.Succeeded, retried.Diagnostic.Message);
                    Assert.AreEqual(2, backend.LoadCount);
                    retried.Lease.Dispose();
                    Assert.AreEqual(1, backend.ReleaseCount);
                    Assert.AreEqual(1, backend.CleanupCount);
                }
                finally
                {
                    backend.CompleteFailedLoad();
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        [UnityTest]
        public IEnumerator FailedLoadCleanupDiagnosticLeavesOneReleaseTombstone() =>
            UniTask.ToCoroutine(async () =>
            {
                await AssertFailedLoadCleanupLeavesTombstoneAsync(
                    FailureCleanupOutcome.DiagnosticFailure,
                    "owner.cleanup-diagnostic",
                    "content.cleanup-diagnostic");
            });

        [UnityTest]
        public IEnumerator FailedLoadCleanupThrowLeavesOneReleaseTombstone() =>
            UniTask.ToCoroutine(async () =>
            {
                await AssertFailedLoadCleanupLeavesTombstoneAsync(
                    FailureCleanupOutcome.Throw,
                    "owner.cleanup-throw",
                    "content.cleanup-throw");
            });

        [UnityTest]
        public IEnumerator LedgerIsFixedCapacityAndDirectReleaseKeepsSourceAlive() =>
            UniTask.ToCoroutine(async () =>
            {
                Assert.IsTrue(ContentRuntime.TryCreate(
                    null,
                    3,
                    true,
                    out ContentRuntime runtime,
                    out CoCoDiagnostic createDiagnostic),
                    createDiagnostic.Message);
                ContentScope scope = CreateScope(runtime, "owner.direct");
                var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
                try
                {
                    Assert.IsTrue(ContentId.TryCreate(
                        "content.direct",
                        out ContentId contentId));
                    Assert.IsTrue(ContentReference.TryCreateDirectAsset(
                        contentId,
                        asset,
                        out ContentReference reference));
                    ContentAcquireResult<RuntimeContractAsset> result =
                        await scope.AcquireAssetAsync<RuntimeContractAsset>(reference);
                    Assert.IsTrue(result.Succeeded, result.Diagnostic.Message);
                    result.Lease.Dispose();

                    ContentRuntimeSnapshot snapshot = runtime.CaptureSnapshot();
                    Assert.AreEqual(0, snapshot.Entries.Count);
                    Assert.AreEqual(3, snapshot.Diagnostics.Count);
                    Assert.IsTrue(snapshot.Diagnostics.Any(record =>
                        record.EventKind == ContentDiagnosticEventKind.LeaseReleased &&
                        !string.IsNullOrEmpty(record.ReleaseStack)));
                    Assert.Throws<NotSupportedException>(() =>
                        ((IList<ContentDiagnosticRecord>)snapshot.Diagnostics).Add(default));
                    Assert.IsTrue(asset != null,
                        "Direct release must not destroy the serialized source object.");
                }
                finally
                {
                    scope.Dispose();
                    await runtime.ShutdownAsync();
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });

        private static async UniTask AssertMismatchReleaseFailureLeavesTombstoneAsync(
            MismatchReleaseOutcome outcome,
            string contentValue)
        {
            var wrongAsset = ScriptableObject.CreateInstance<WrongRuntimeContractAsset>();
            var backend = new MismatchReleaseBackend(wrongAsset, outcome);
            ContentRuntime runtime = CreateRuntime(backend);
            ContentScope scopeA = CreateScope(runtime, "owner." + contentValue + ".a");
            ContentScope scopeB = CreateScope(runtime, "owner." + contentValue + ".b");
            try
            {
                ContentReference reference = CreateAddressableAssetReference(contentValue);
                UniTask<ContentAcquireResult<RuntimeContractAsset>> requestA =
                    scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                UniTask<ContentAcquireResult<RuntimeContractAsset>> requestB =
                    scopeB.AcquireAssetAsync<RuntimeContractAsset>(reference);
                Assert.AreEqual(
                    1,
                    backend.LoadCount,
                    "Overlapping waiters must share one physical mismatch load.");

                backend.CompleteLoad();
                ContentAcquireResult<RuntimeContractAsset> resultA = await requestA;
                ContentAcquireResult<RuntimeContractAsset> resultB = await requestB;

                Assert.AreEqual(ContentAcquireStatus.Failed, resultA.Status);
                Assert.AreEqual(ContentAcquireStatus.Failed, resultB.Status);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    resultA.Diagnostic.Code);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    resultB.Diagnostic.Code);
                Assert.AreEqual(resultA.Diagnostic, resultB.Diagnostic);
                Assert.AreEqual(
                    1,
                    backend.ReleaseCount,
                    "One unpublished mismatch resource must be released exactly once.");

                ContentRuntimeSnapshot snapshot = runtime.CaptureSnapshot();
                Assert.AreEqual(1, snapshot.Entries.Count);
                Assert.AreEqual(
                    ContentEntryState.ReleaseFailed,
                    snapshot.Entries[0].State);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    snapshot.Entries[0].Diagnostic.Code);
                Assert.AreSame(wrongAsset, GetOnlyEntryResource(runtime).Value);

                ContentDiagnosticRecord[] lifecycle = snapshot.Diagnostics
                    .Where(record =>
                        record.ContentId == reference.Id &&
                        (record.EventKind == ContentDiagnosticEventKind.LoadFailed ||
                         record.EventKind == ContentDiagnosticEventKind.ReleaseStarted ||
                         record.EventKind == ContentDiagnosticEventKind.ReleaseSucceeded ||
                         record.EventKind == ContentDiagnosticEventKind.ReleaseFailed))
                    .ToArray();
                CollectionAssert.AreEqual(
                    new[]
                    {
                        ContentDiagnosticEventKind.LoadFailed,
                        ContentDiagnosticEventKind.ReleaseStarted,
                        ContentDiagnosticEventKind.ReleaseFailed
                    },
                    lifecycle.Select(record => record.EventKind).ToArray());
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentTypeMismatch,
                    lifecycle[0].Diagnostic.Code);
                Assert.IsTrue(lifecycle[1].Diagnostic.IsNone);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    lifecycle[2].Diagnostic.Code);

                ContentAcquireResult<RuntimeContractAsset> blocked =
                    await scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                Assert.AreEqual(ContentAcquireStatus.Failed, blocked.Status);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    blocked.Diagnostic.Code);
                Assert.AreEqual(
                    1,
                    backend.LoadCount,
                    "A mismatch release tombstone must block a new generation.");
                Assert.AreEqual(1, backend.ReleaseCount);

                scopeA.Dispose();
                scopeB.Dispose();
                CoCoDiagnostic shutdownDiagnostic = await runtime.ShutdownAsync();
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    shutdownDiagnostic.Code);
                Assert.AreEqual(resultA.Diagnostic, shutdownDiagnostic);
                Assert.AreEqual(1, backend.ReleaseCount);
            }
            finally
            {
                scopeA.Dispose();
                scopeB.Dispose();
                await runtime.ShutdownAsync();
                UnityEngine.Object.DestroyImmediate(wrongAsset);
            }
        }

        private static async UniTask AssertFailedLoadCleanupLeavesTombstoneAsync(
            FailureCleanupOutcome outcome,
            string ownerValue,
            string contentValue)
        {
            var asset = ScriptableObject.CreateInstance<RuntimeContractAsset>();
            var backend = new FailureCleanupBackend(asset, outcome);
            ContentRuntime runtime = CreateRuntime(backend);
            ContentScope scopeA = CreateScope(runtime, ownerValue + ".a");
            ContentScope scopeB = CreateScope(runtime, ownerValue + ".b");
            try
            {
                ContentReference reference = CreateAddressableAssetReference(contentValue);
                UniTask<ContentAcquireResult<RuntimeContractAsset>> requestA =
                    scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                UniTask<ContentAcquireResult<RuntimeContractAsset>> requestB =
                    scopeB.AcquireAssetAsync<RuntimeContractAsset>(reference);
                Assert.AreEqual(1, backend.LoadCount);

                backend.CompleteFailedLoad();
                ContentAcquireResult<RuntimeContractAsset> resultA = await requestA;
                ContentAcquireResult<RuntimeContractAsset> resultB = await requestB;
                Assert.AreEqual(ContentAcquireStatus.Failed, resultA.Status);
                Assert.AreEqual(ContentAcquireStatus.Failed, resultB.Status);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    resultA.Diagnostic.Code);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    resultB.Diagnostic.Code);
                Assert.AreEqual(
                    1,
                    backend.CleanupCount,
                    "One physical failed load grants exactly one cleanup authority.");

                ContentRuntimeSnapshot snapshot = runtime.CaptureSnapshot();
                Assert.AreEqual(1, snapshot.Entries.Count);
                Assert.AreEqual(
                    ContentEntryState.ReleaseFailed,
                    snapshot.Entries[0].State);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    snapshot.Entries[0].Diagnostic.Code);
                Assert.AreEqual(
                    1,
                    snapshot.Diagnostics.Count(record =>
                        record.EventKind == ContentDiagnosticEventKind.ReleaseFailed));
                ContentBackendFailureCleanup cleanupAuthority =
                    GetOnlyFailureCleanupAuthority(runtime);
                Assert.IsTrue(cleanupAuthority.RetainsAuthority);
                Assert.IsTrue(cleanupAuthority.ExecutionStarted);
                Assert.IsFalse(cleanupAuthority.TryBeginExecution(out _));

                ContentAcquireResult<RuntimeContractAsset> blocked =
                    await scopeA.AcquireAssetAsync<RuntimeContractAsset>(reference);
                Assert.AreEqual(ContentAcquireStatus.Failed, blocked.Status);
                Assert.AreEqual(
                    CoCoDiagnosticCode.ContentReleaseFailed,
                    blocked.Diagnostic.Code);
                Assert.AreEqual(1, backend.LoadCount);
                Assert.AreEqual(1, backend.CleanupCount);
                Assert.AreSame(
                    cleanupAuthority,
                    GetOnlyFailureCleanupAuthority(runtime));
                Assert.IsTrue(cleanupAuthority.RetainsAuthority);
                Assert.IsTrue(cleanupAuthority.ExecutionStarted);
            }
            finally
            {
                backend.CompleteFailedLoad();
                scopeA.Dispose();
                scopeB.Dispose();
                await runtime.ShutdownAsync();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static int GetTrackedScopeCount(ContentRuntime runtime)
        {
            FieldInfo scopesField = typeof(ContentRuntime).GetField(
                "scopes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(scopesField);
            object scopes = scopesField.GetValue(runtime);
            Assert.IsNotNull(scopes);
            PropertyInfo countProperty = scopes.GetType().GetProperty("Count");
            Assert.IsNotNull(countProperty);
            return (int)countProperty.GetValue(scopes);
        }

        private static ContentBackendResource GetOnlyEntryResource(ContentRuntime runtime)
        {
            FieldInfo entriesField = typeof(ContentRuntime).GetField(
                "entries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(entriesField);
            var entries = entriesField.GetValue(runtime) as IDictionary;
            Assert.IsNotNull(entries);
            Assert.AreEqual(1, entries.Count);

            object entry = entries.Values.Cast<object>().Single();
            PropertyInfo resourceProperty = entry.GetType().GetProperty(
                "Resource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(resourceProperty);
            var resource = resourceProperty.GetValue(entry) as ContentBackendResource;
            Assert.IsNotNull(resource);
            return resource;
        }

        private static ContentBackendFailureCleanup GetOnlyFailureCleanupAuthority(
            ContentRuntime runtime)
        {
            FieldInfo entriesField = typeof(ContentRuntime).GetField(
                "entries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(entriesField);
            var entries = entriesField.GetValue(runtime) as IDictionary;
            Assert.IsNotNull(entries);
            Assert.AreEqual(1, entries.Count);

            object entry = entries.Values.Cast<object>().Single();
            PropertyInfo cleanupProperty = entry.GetType().GetProperty(
                "FailureCleanup",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(cleanupProperty);
            var cleanupAuthority =
                cleanupProperty.GetValue(entry) as ContentBackendFailureCleanup;
            Assert.IsNotNull(cleanupAuthority);
            return cleanupAuthority;
        }

        private static ContentRuntime CreateRuntime(
            IContentBackend backend,
            bool captureStacks = false)
        {
            Assert.IsTrue(ContentRuntime.TryCreate(
                new[] { backend },
                128,
                captureStacks,
                out ContentRuntime runtime,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return runtime;
        }

        private static ContentScope CreateScope(ContentRuntime runtime, string ownerValue)
        {
            Assert.IsTrue(ContentOwnerId.TryCreate(ownerValue, out ContentOwnerId ownerId));
            Assert.IsTrue(runtime.TryCreateScope(
                ownerId,
                out ContentScope scope,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return scope;
        }

        private static ContentReference CreateAddressableAssetReference(string contentValue)
        {
            Assert.IsTrue(ContentId.TryCreate(contentValue, out ContentId contentId));
            Assert.IsTrue(ContentReference.TryCreateAddressableAsset(
                contentId,
                "tests/" + contentValue,
                out ContentReference reference));
            return reference;
        }

        private sealed class RuntimeContractAsset : ScriptableObject
        {
        }

        private readonly struct WorkerCreationResult
        {
            internal WorkerCreationResult(
                bool created,
                ContentRuntime runtime,
                CoCoDiagnostic diagnostic)
            {
                Created = created;
                Runtime = runtime;
                Diagnostic = diagnostic;
            }

            internal bool Created { get; }
            internal ContentRuntime Runtime { get; }
            internal CoCoDiagnostic Diagnostic { get; }
        }

        private sealed class CountingBackendEnumerable : IEnumerable<IContentBackend>
        {
            private int enumerationCount;

            internal int EnumerationCount => Volatile.Read(ref enumerationCount);

            public IEnumerator<IContentBackend> GetEnumerator()
            {
                Interlocked.Increment(ref enumerationCount);
                return Enumerable.Empty<IContentBackend>().GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private enum FailureCleanupOutcome
        {
            Success = 0,
            DiagnosticFailure = 1,
            Throw = 2
        }

        private enum MismatchReleaseOutcome
        {
            DiagnosticFailure = 0,
            Throw = 1
        }

        private sealed class MismatchReleaseBackend : IContentBackend
        {
            private static readonly ContentBackendId Id = CreateBackendId();
            private readonly UnityEngine.Object value;
            private readonly MismatchReleaseOutcome releaseOutcome;
            private readonly UniTaskCompletionSource<ContentBackendLoadResult>
                loadCompletion = new UniTaskCompletionSource<ContentBackendLoadResult>();

            internal MismatchReleaseBackend(
                UnityEngine.Object value,
                MismatchReleaseOutcome releaseOutcome)
            {
                this.value = value;
                this.releaseOutcome = releaseOutcome;
            }

            internal int LoadCount { get; private set; }
            internal int ReleaseCount { get; private set; }
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.Asset;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                Assert.IsTrue(CanHandle(request.Reference));
                _ = lifetimeCancellationToken;
                LoadCount++;
                return loadCompletion.Task;
            }

            internal void CompleteLoad()
            {
                loadCompletion.TrySetResult(ContentBackendLoadResult.Success(
                    value,
                    ReleaseAsync));
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                switch (releaseOutcome)
                {
                    case MismatchReleaseOutcome.DiagnosticFailure:
                        return UniTask.FromResult(CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Content,
                            CoCoDiagnosticCode.ContentLoadFailed,
                            "controlled mismatch release diagnostic"));
                    case MismatchReleaseOutcome.Throw:
                        throw new InvalidOperationException(
                            "controlled mismatch release throw");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.mismatch-release",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class FailureCleanupBackend : IContentBackend
        {
            private static readonly ContentBackendId Id = CreateBackendId();
            private readonly RuntimeContractAsset asset;
            private readonly FailureCleanupOutcome cleanupOutcome;
            private readonly UniTaskCompletionSource<ContentBackendLoadResult>
                failedLoadCompletion =
                    new UniTaskCompletionSource<ContentBackendLoadResult>();

            internal FailureCleanupBackend(
                RuntimeContractAsset asset,
                FailureCleanupOutcome cleanupOutcome)
            {
                this.asset = asset;
                this.cleanupOutcome = cleanupOutcome;
            }

            internal int LoadCount { get; private set; }
            internal int CleanupCount { get; private set; }
            internal int ReleaseCount { get; private set; }
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.Asset;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                Assert.IsTrue(CanHandle(request.Reference));
                _ = lifetimeCancellationToken;
                LoadCount++;
                return LoadCount == 1
                    ? failedLoadCompletion.Task
                    : UniTask.FromResult(ContentBackendLoadResult.Success(
                        asset,
                        ReleaseAsync));
            }

            internal void CompleteFailedLoad()
            {
                failedLoadCompletion.TrySetResult(
                    ContentBackendLoadResult.FailureWithCleanup(
                        CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Content,
                            CoCoDiagnosticCode.ContentLoadFailed,
                            "controlled load failure"),
                        CleanupAsync));
            }

            private UniTask<CoCoDiagnostic> CleanupAsync()
            {
                CleanupCount++;
                switch (cleanupOutcome)
                {
                    case FailureCleanupOutcome.Success:
                        return UniTask.FromResult(CoCoDiagnostic.None);
                    case FailureCleanupOutcome.DiagnosticFailure:
                        return UniTask.FromResult(CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Content,
                            CoCoDiagnosticCode.ContentLoadFailed,
                            "controlled cleanup diagnostic"));
                    case FailureCleanupOutcome.Throw:
                        throw new InvalidOperationException("controlled cleanup throw");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                return UniTask.FromResult(CoCoDiagnostic.None);
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.failure-cleanup",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class ControlledBackend : IContentBackend
        {
            private sealed class PendingLoad
            {
                internal PendingLoad(CancellationToken cancellationToken)
                {
                    CancellationToken = cancellationToken;
                    Completion = new UniTaskCompletionSource<ContentBackendLoadResult>();
                    cancellationToken.Register(() =>
                        CancellationThreadId = Environment.CurrentManagedThreadId);
                }

                internal CancellationToken CancellationToken { get; }
                internal UniTaskCompletionSource<ContentBackendLoadResult> Completion { get; }
                internal int CancellationThreadId { get; private set; }
            }

            private static readonly ContentBackendId Id = CreateBackendId();
            private readonly List<PendingLoad> pendingLoads = new List<PendingLoad>();
            private readonly List<UniTaskCompletionSource<CoCoDiagnostic>> pendingReleases =
                new List<UniTaskCompletionSource<CoCoDiagnostic>>();

            internal int LoadCount => pendingLoads.Count;
            internal int ReleaseCount { get; private set; }
            internal int PendingReleaseCount => pendingReleases.Count;
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.Asset;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                Assert.IsTrue(CanHandle(request.Reference));
                var pending = new PendingLoad(lifetimeCancellationToken);
                pendingLoads.Add(pending);
                return pending.Completion.Task;
            }

            internal CancellationToken GetLoadCancellation(int index) =>
                pendingLoads[index].CancellationToken;

            internal int GetLoadCancellationThreadId(int index) =>
                pendingLoads[index].CancellationThreadId;

            internal void CompleteSuccess(
                int index,
                UnityEngine.Object value,
                bool releaseFails = false,
                bool holdRelease = false)
            {
                pendingLoads[index].Completion.TrySetResult(
                    ContentBackendLoadResult.Success(
                        value,
                        () => ReleaseAsync(releaseFails, holdRelease)));
            }

            internal void CompleteRelease(int index)
            {
                pendingReleases[index].TrySetResult(CoCoDiagnostic.None);
            }

            internal void CompleteFailure(int index, string message)
            {
                pendingLoads[index].Completion.TrySetResult(
                    ContentBackendLoadResult.Failure(CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Content,
                        CoCoDiagnosticCode.ContentLoadFailed,
                        message)));
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync(bool fails, bool holdRelease)
            {
                ReleaseCount++;
                if (holdRelease)
                {
                    var completion = new UniTaskCompletionSource<CoCoDiagnostic>();
                    pendingReleases.Add(completion);
                    return completion.Task;
                }

                CoCoDiagnostic diagnostic = fails
                    ? CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Content,
                        CoCoDiagnosticCode.ContentReleaseFailed,
                        "controlled release failed")
                    : CoCoDiagnostic.None;
                return UniTask.FromResult(diagnostic);
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.controlled",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class WorkerMismatchBackend : IContentBackend
        {
            private static readonly ContentBackendId Id = CreateBackendId();
            private readonly UnityEngine.Object value;
            private readonly UniTaskCompletionSource<CoCoDiagnostic> releaseCompletion =
                new UniTaskCompletionSource<CoCoDiagnostic>();

            internal WorkerMismatchBackend(UnityEngine.Object value)
            {
                this.value = value;
            }

            internal int ReleaseCompletionThreadId { get; private set; }
            internal int ReleaseCount { get; private set; }
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.Asset;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                _ = request;
                _ = lifetimeCancellationToken;
                return UniTask.FromResult(ContentBackendLoadResult.Success(
                    value,
                    ReleaseAsync));
            }

            internal Task CompleteReleaseFromWorkerAsync() => Task.Run(() =>
            {
                ReleaseCompletionThreadId = Environment.CurrentManagedThreadId;
                releaseCompletion.TrySetResult(CoCoDiagnostic.None);
            });

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                return releaseCompletion.Task;
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.worker-mismatch",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class ReentrantBackend : IContentBackend
        {
            private static readonly ContentBackendId Id = CreateBackendId();
            private readonly RuntimeContractAsset asset;

            internal ReentrantBackend(RuntimeContractAsset asset)
            {
                this.asset = asset;
            }

            internal Action OnLoad { get; set; }
            internal Action OnRelease { get; set; }
            internal int LoadCount { get; private set; }
            internal int ReleaseCount { get; private set; }
            public ContentBackendId BackendId => Id;

            public bool CanHandle(ContentReference reference) =>
                reference.IsValid &&
                reference.SourceKind == ContentSourceKind.Addressables &&
                reference.Kind == ContentKind.Asset;

            public UniTask<ContentBackendLoadResult> LoadAsync(
                ContentBackendRequest request,
                CancellationToken lifetimeCancellationToken)
            {
                _ = request;
                _ = lifetimeCancellationToken;
                LoadCount++;
                OnLoad?.Invoke();
                return UniTask.FromResult(ContentBackendLoadResult.Success(
                    asset,
                    ReleaseAsync));
            }

            private UniTask<CoCoDiagnostic> ReleaseAsync()
            {
                ReleaseCount++;
                OnRelease?.Invoke();
                return UniTask.FromResult(CoCoDiagnostic.None);
            }

            private static ContentBackendId CreateBackendId()
            {
                ContentBackendId.TryCreate(
                    "tests.reentrant",
                    out ContentBackendId backendId);
                return backendId;
            }
        }

        private sealed class WrongRuntimeContractAsset : ScriptableObject
        {
        }
    }
}
