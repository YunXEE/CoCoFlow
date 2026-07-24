using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    public sealed class PoolAdversarialReentryPlayModeTests
    {
        [UnityTest]
        public IEnumerator CloseDuringExplicitPrewarmCancelsBatchAndReleasesOwnership() =>
            UniTask.ToCoroutine(RunCloseDuringExplicitPrewarmAsync);

        [UnityTest]
        public IEnumerator OnEnableCannotReturnCurrentHandleDuringActivation() =>
            UniTask.ToCoroutine(RunActivationReentryAsync);

        [UnityTest]
        public IEnumerator OnEnableClosingScopePreventsActivationPublication() =>
            UniTask.ToCoroutine(RunActivationCloseReentryAsync);

        [UnityTest]
        public IEnumerator ExternalDestroyThenForceShutdownRetainsLeaseUntilSentinelFinalizes() =>
            UniTask.ToCoroutine(RunExternalDestroyForceShutdownRaceAsync);

        [UnityTest]
        public IEnumerator ContentShutdownDrainsActivePoolBeforeReleasingLease() =>
            UniTask.ToCoroutine(RunContentShutdownDependencyAsync);

        [UnityTest]
        public IEnumerator ContentShutdownGracefullyDrainsIdlePoolWithoutWarning() =>
            UniTask.ToCoroutine(RunCleanContentShutdownDependencyAsync);

        [UnityTest]
        public IEnumerator ContentShutdownGracefullyAwaitsPendingDestroyWithoutWarning() =>
            UniTask.ToCoroutine(RunPendingDestroyContentShutdownDependencyAsync);

        [UnityTest]
        public IEnumerator AlreadyCancelledPrewarmLeavesReadySnapshotUntouched() =>
            UniTask.ToCoroutine(RunAlreadyCancelledPrewarmAsync);

        [UnityTest]
        public IEnumerator OldPrewarmWaiterCannotCancelImmediatelyRestartedGeneration() =>
            UniTask.ToCoroutine(RunPrewarmWaiterGenerationAsync);

        [UnityTest]
        public IEnumerator ActivationWithoutConsumerReparentIsRejectedAndDestroyed() =>
            UniTask.ToCoroutine(RunActivationWithoutReparentAsync);

        [UnityTest]
        public IEnumerator DestroyedPreparedIdleIsSkippedByRentAndRecordedOnClose() =>
            UniTask.ToCoroutine(RunDestroyedPreparedIdleAsync);

        [UnityTest]
        public IEnumerator DestroyedLeasedInactiveRejectsHandleAndGracefullyCloses() =>
            UniTask.ToCoroutine(RunDestroyedLeasedInactiveAsync);

        [UnityTest]
        public IEnumerator PhysicalOnDestroyObservesLeaseBeforeRelease() =>
            UniTask.ToCoroutine(RunPhysicalOnDestroyLeaseBoundaryAsync);

        [UnityTest]
        public IEnumerator ConcurrentInitialPrepareIsSingleFlightAndCancellationIsolated() =>
            UniTask.ToCoroutine(RunConcurrentInitialPrepareAsync);

        [UnityTest]
        public IEnumerator CancelledAndFailedInitialPrepareRemoveUnpublishedOwnership() =>
            UniTask.ToCoroutine(RunCancelledAndFailedInitialPrepareAsync);

        private static async UniTask RunCloseDuringExplicitPrewarmAsync()
        {
            const int TargetCount = 9;
            PoolingTestFixture fixture =
                PoolingTestFixture.Create(TargetCount, TargetCount);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryClearInactive(
                        fixture.Profile.Id,
                        out CoCoDiagnostic cleared),
                    Is.True,
                    cleared.Message);

                for (int frame = 0; frame < 10; frame++)
                {
                    PoolEntrySnapshot pending =
                        fixture.Scope.CaptureSnapshot().Entries.Single();
                    if (pending.PendingDestroyCount == 0)
                    {
                        break;
                    }

                    await UniTask.NextFrame();
                }

                PoolEntrySnapshot empty =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(empty.State, Is.EqualTo(PoolEntryState.Ready));
                Assert.That(empty.InactiveCount, Is.Zero);
                Assert.That(empty.PendingDestroyCount, Is.Zero);
                Assert.That(empty.HoldsSourceLease, Is.True);
                long createdBeforeExplicitPrewarm = empty.CreatedCount;

                UniTask<PoolPrewarmResult> prewarming =
                    fixture.Scope.PrewarmAsync(fixture.Profile.Id);
                PoolEntrySnapshot inFlight =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(
                    inFlight.State,
                    Is.EqualTo(PoolEntryState.Prewarming),
                    "The ninth item must be pending after the deterministic eight-item batch yield.");
                Assert.That(
                    inFlight.CreatedCount - createdBeforeExplicitPrewarm,
                    Is.EqualTo(8));
                Assert.That(inFlight.InactiveCount, Is.Zero);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries.Single().LeaseCount,
                    Is.EqualTo(1));

                UniTask<CoCoDiagnostic> closing = fixture.Scope.CloseAsync();
                Assert.That(fixture.Scope.State, Is.EqualTo(PoolScopeState.Closing));

                PoolPrewarmResult cancelled = await prewarming;
                Assert.That(cancelled.Cancelled, Is.True, cancelled.Diagnostic.Message);
                Assert.That(
                    cancelled.CreatedCount,
                    Is.EqualTo(8),
                    "Closing at the batch yield must prevent the ninth instance from being created.");

                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                Assert.That(fixture.Scope.State, Is.EqualTo(PoolScopeState.Closed));
                Assert.That(fixture.PoolRuntime.CaptureSnapshot().Scopes, Is.Empty);

                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty,
                    "Closing must release the entry's single ContentLease after prewarm cancellation.");
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunActivationReentryAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                0,
                1,
                prefab => prefab.AddComponent<PoolActivationReturnReentryProbe>());
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                PoolActivationReturnReentryProbe probe =
                    instance.GetComponent<PoolActivationReturnReentryProbe>();
                probe.Arm(handle);

                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Assert.That(probe.Attempted, Is.True);
                Assert.That(
                    probe.NestedReturnSucceeded,
                    Is.False,
                    "OnEnable must not be able to return the handle owned by the outer activation.");
                Assert.That(
                    probe.NestedDiagnostic.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolCallbackReentry));
                Assert.That(instance.activeInHierarchy, Is.True);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject stillOwned,
                        out CoCoDiagnostic stillOwnedDiagnostic),
                    Is.True,
                    stillOwnedDiagnostic.Message);
                Assert.That(stillOwned, Is.SameAs(instance));

                PoolEntrySnapshot active =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(active.ActiveCount, Is.EqualTo(1));
                Assert.That(active.InactiveCount, Is.Zero);
                Assert.That(active.TemporalRetainedCount, Is.Zero);
                Assert.That(active.QuarantineCount, Is.Zero);
                Assert.That(active.PendingDestroyCount, Is.Zero);

                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic returned),
                    Is.True,
                    returned.Message);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunActivationCloseReentryAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                0,
                1,
                prefab => prefab.AddComponent<PoolActivationCloseReentryProbe>());
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                PoolActivationCloseReentryProbe probe =
                    instance.GetComponent<PoolActivationCloseReentryProbe>();
                probe.Arm(fixture.Scope);

                bool activated = handle.TryActivate(out CoCoDiagnostic diagnostic);
                Assert.That(probe.Attempted, Is.True);
                Assert.That(
                    activated,
                    Is.False,
                    "Activation must not publish success after OnEnable closes its Scope.");
                Assert.That(diagnostic.IsNone, Is.False);
                Assert.That(
                    diagnostic.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolScopeClosing));

                CoCoDiagnostic closed = await probe.ClosingOperation;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                Assert.That(fixture.Scope.State, Is.EqualTo(PoolScopeState.Closed));
                Assert.That(fixture.PoolRuntime.CaptureSnapshot().Scopes, Is.Empty);
                Assert.That(instance == null || !instance.activeInHierarchy, Is.True);
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(fixture.ContentRuntime.CaptureSnapshot().Entries, Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunExternalDestroyForceShutdownRaceAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);

                UnityEngine.Object.Destroy(instance);
                fixture.PoolRuntime.ForceShutdown();

                Assert.That(
                    handle.TryGetInstance(
                        out _,
                        out CoCoDiagnostic invalidated),
                    Is.False,
                    "ForceShutdown must invalidate the live rental before physical OnDestroy.");
                Assert.That(invalidated.IsNone, Is.False);
                Assert.That(
                    invalidated.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PooledHandleAlreadyReturned)
                        .Or.EqualTo(CoCoDiagnosticCode.StalePooledHandle));
                PoolRuntimeSnapshot pending =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(pending.IsShuttingDown, Is.True);
                Assert.That(pending.IsDisposed, Is.False);
                Assert.That(pending.Scopes, Has.Count.EqualTo(1));
                Assert.That(
                    pending.Diagnostics.Any(
                        record =>
                            record.EventKind == PoolDiagnosticEventKind.ForcedShutdown),
                    Is.True);
                Assert.That(
                    pending.Diagnostics.Any(
                        record =>
                            record.EventKind == PoolDiagnosticEventKind.ExternalDestroy),
                    Is.False,
                    "External-destroy evidence belongs to the sentinel terminal callback.");
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries.Single().LeaseCount,
                    Is.EqualTo(1),
                    "The source ContentLease must outlive the pending physical record.");

                for (int frame = 0;
                     frame < 20 && !fixture.PoolRuntime.IsDisposed;
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                PoolRuntimeSnapshot finalized =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(finalized.IsDisposed, Is.True);
                Assert.That(finalized.Scopes, Is.Empty);
                Assert.That(
                    finalized.Diagnostics.Any(
                        record =>
                            record.EventKind == PoolDiagnosticEventKind.ExternalDestroy ||
                            record.EventKind == PoolDiagnosticEventKind.InstanceDestroyed),
                    Is.True,
                    "The overlapping external/forced destroy must publish one terminal destruction record.");
                Assert.That(
                    finalized.Diagnostics.Count(
                        record =>
                            record.InstanceSequence == handle.InstanceSequence &&
                            (record.EventKind ==
                             PoolDiagnosticEventKind.ExternalDestroy ||
                             record.EventKind ==
                             PoolDiagnosticEventKind.InstanceDestroyed)),
                    Is.EqualTo(1),
                    "The overlapping paths must reconcile exactly one terminal event.");
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty,
                    "The source ContentLease may release only after sentinel finalization.");
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunContentShutdownDependencyAsync()
        {
            PoolLifecycleProbe.ResetEvents();
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                0,
                1,
                prefab =>
                    prefab.AddComponent<PoolLifecycleProbe>()
                        .Configure("content-shutdown"));
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                PoolLifecycleProbe.ResetEvents();

                UniTask<CoCoDiagnostic> shuttingDown =
                    fixture.ContentRuntime.ShutdownAsync();

                Assert.That(fixture.ContentRuntime.IsShuttingDown, Is.True);
                Assert.That(fixture.ContentRuntime.IsDisposed, Is.False);
                PoolRuntimeSnapshot pendingPool =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(pendingPool.IsShuttingDown, Is.True);
                Assert.That(pendingPool.IsDisposed, Is.False);
                Assert.That(pendingPool.Scopes, Has.Count.EqualTo(1));
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot()
                        .Entries.Single().LeaseCount,
                    Is.EqualTo(1),
                    "Content shutdown must await Pool's physical destroy barrier.");
                Assert.That(
                    handle.TryGetInstance(
                        out _,
                        out CoCoDiagnostic invalidated),
                    Is.False);
                Assert.That(invalidated.IsNone, Is.False);
                Assert.That(
                    PoolLifecycleProbe.Events.Count(
                        value =>
                            value ==
                            "return:content-shutdown:ForcedShutdown:False"),
                    Is.EqualTo(1),
                    "Content-triggered Pool drain must run exactly one ForcedShutdown reset.");

                for (int frame = 0;
                     frame < 20 &&
                     (!fixture.PoolRuntime.IsDisposed ||
                      !fixture.ContentRuntime.IsDisposed);
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                Assert.That(fixture.PoolRuntime.IsDisposed, Is.True);
                Assert.That(fixture.ContentRuntime.IsDisposed, Is.True);
                CoCoDiagnostic shutdown = await shuttingDown;
                Assert.That(shutdown.IsWarning, Is.True, shutdown.Message);
                Assert.That(
                    shutdown.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolForcedShutdown));
                PoolRuntimeSnapshot finalized =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(finalized.Scopes, Is.Empty);
                Assert.That(
                    finalized.Diagnostics.Count(
                        record =>
                            record.EventKind ==
                            PoolDiagnosticEventKind.ForcedShutdown &&
                            !record.PoolId.IsValid),
                    Is.EqualTo(1));
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunCleanContentShutdownDependencyAsync()
        {
            PoolLifecycleProbe.ResetEvents();
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                0,
                1,
                prefab =>
                    prefab.AddComponent<PoolLifecycleProbe>()
                        .Configure("clean-content-shutdown"));
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic returned),
                    Is.True,
                    returned.Message);

                PoolEntrySnapshot idle =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(idle.ActiveCount, Is.Zero);
                Assert.That(idle.InactiveCount, Is.EqualTo(1));
                Assert.That(idle.TemporalRetainedCount, Is.Zero);
                Assert.That(idle.QuarantineCount, Is.Zero);
                PoolLifecycleProbe.ResetEvents();

                UniTask<CoCoDiagnostic> shuttingDown =
                    fixture.ContentRuntime.ShutdownAsync();

                Assert.That(fixture.ContentRuntime.IsShuttingDown, Is.True);
                Assert.That(fixture.ContentRuntime.IsDisposed, Is.False);
                PoolRuntimeSnapshot pendingPool =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(pendingPool.IsShuttingDown, Is.True);
                Assert.That(pendingPool.IsDisposed, Is.False);
                Assert.That(pendingPool.Scopes, Has.Count.EqualTo(1));
                Assert.That(
                    pendingPool.Diagnostics.Any(
                        record =>
                            record.EventKind ==
                            PoolDiagnosticEventKind.ForcedShutdown),
                    Is.False);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot()
                        .Entries.Single().LeaseCount,
                    Is.EqualTo(1),
                    "Graceful Content-first shutdown must retain the source lease " +
                    "until the idle physical instance is terminal.");
                Assert.That(
                    PoolLifecycleProbe.Events.Any(
                        value => value.Contains("ForcedShutdown")),
                    Is.False);

                for (int frame = 0;
                     frame < 20 &&
                     (!fixture.PoolRuntime.IsDisposed ||
                      !fixture.ContentRuntime.IsDisposed);
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                Assert.That(fixture.PoolRuntime.IsDisposed, Is.True);
                Assert.That(fixture.ContentRuntime.IsDisposed, Is.True);
                CoCoDiagnostic shutdown = await shuttingDown;
                Assert.That(shutdown.IsNone, Is.True, shutdown.Message);
                PoolRuntimeSnapshot finalized =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(finalized.Scopes, Is.Empty);
                Assert.That(
                    finalized.Diagnostics.Any(
                        record =>
                            record.EventKind ==
                            PoolDiagnosticEventKind.ForcedShutdown),
                    Is.False);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask
            RunPendingDestroyContentShutdownDependencyAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic returned),
                    Is.True,
                    returned.Message);
                PoolEntrySnapshot idle =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(idle.ActiveCount, Is.Zero);
                Assert.That(idle.InactiveCount, Is.EqualTo(1));
                Assert.That(
                    fixture.Scope.TryClearInactive(
                        fixture.Profile.Id,
                        out CoCoDiagnostic cleared),
                    Is.True,
                    cleared.Message);

                PoolEntrySnapshot pendingDestroy =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(pendingDestroy.ActiveCount, Is.Zero);
                Assert.That(pendingDestroy.InactiveCount, Is.Zero);
                Assert.That(pendingDestroy.TemporalRetainedCount, Is.Zero);
                Assert.That(pendingDestroy.QuarantineCount, Is.Zero);
                Assert.That(pendingDestroy.PendingDestroyCount, Is.EqualTo(1));

                UniTask<CoCoDiagnostic> shuttingDown =
                    fixture.ContentRuntime.ShutdownAsync();

                PoolRuntimeSnapshot pendingPool =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(pendingPool.IsShuttingDown, Is.True);
                Assert.That(pendingPool.IsDisposed, Is.False);
                Assert.That(pendingPool.Scopes, Has.Count.EqualTo(1));
                Assert.That(
                    pendingPool.Scopes.Single()
                        .Entries.Single().PendingDestroyCount,
                    Is.EqualTo(1));
                Assert.That(
                    pendingPool.Diagnostics.Any(
                        record =>
                            record.EventKind ==
                            PoolDiagnosticEventKind.ForcedShutdown),
                    Is.False);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot()
                        .Entries.Single().LeaseCount,
                    Is.EqualTo(1),
                    "Graceful Content-first shutdown must retain the source lease " +
                    "until the pending physical destroy is terminal.");

                for (int frame = 0;
                     frame < 20 &&
                     (!fixture.PoolRuntime.IsDisposed ||
                      !fixture.ContentRuntime.IsDisposed);
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                Assert.That(fixture.PoolRuntime.IsDisposed, Is.True);
                Assert.That(fixture.ContentRuntime.IsDisposed, Is.True);
                CoCoDiagnostic shutdown = await shuttingDown;
                Assert.That(shutdown.IsNone, Is.True, shutdown.Message);
                PoolRuntimeSnapshot finalized =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(finalized.Scopes, Is.Empty);
                Assert.That(
                    finalized.Diagnostics.Any(
                        record =>
                            record.EventKind ==
                            PoolDiagnosticEventKind.ForcedShutdown),
                    Is.False);
                Assert.That(
                    finalized.Diagnostics.Count(
                        record =>
                            record.InstanceSequence ==
                            handle.InstanceSequence &&
                            record.EventKind ==
                            PoolDiagnosticEventKind.InstanceDestroyed),
                    Is.EqualTo(1),
                    "The pending Pool-owned destroy must reach exactly one terminal event.");
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunAlreadyCancelledPrewarmAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            var cancellation = new CancellationTokenSource();
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                PoolEntrySnapshot before =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                cancellation.Cancel();

                PoolPrewarmResult cancelled =
                    await fixture.Scope.PrewarmAsync(
                        fixture.Profile.Id,
                        cancellation.Token);

                Assert.That(cancelled.Cancelled, Is.True);
                Assert.That(
                    cancelled.Diagnostic.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolOperationCancelled));
                PoolEntrySnapshot after =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(after.State, Is.EqualTo(PoolEntryState.Ready));
                Assert.That(after.CreatedCount, Is.EqualTo(before.CreatedCount));
                Assert.That(after.InactiveCount, Is.EqualTo(before.InactiveCount));
                Assert.That(after.ActiveCount, Is.EqualTo(before.ActiveCount));
                Assert.That(
                    after.PendingDestroyCount,
                    Is.EqualTo(before.PendingDestroyCount));
            }
            finally
            {
                cancellation.Dispose();
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunPrewarmWaiterGenerationAsync()
        {
            const int TargetCount = 9;
            PoolingTestFixture fixture =
                PoolingTestFixture.Create(TargetCount, TargetCount);
            var held = new List<PooledHandle>(TargetCount * 2);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                RentBatch(fixture, TargetCount, held);

                UniTask<PoolPrewarmResult> first =
                    fixture.Scope.PrewarmAsync(fixture.Profile.Id);
                UniTask<PoolPrewarmResult> restarted =
                    RestartPrewarmFromFirstWaiterAsync(
                        fixture,
                        first,
                        TargetCount,
                        held);
                UniTask<PoolPrewarmResult> second =
                    fixture.Scope.PrewarmAsync(fixture.Profile.Id);

                PoolPrewarmResult secondResult = await second;
                PoolPrewarmResult restartedResult = await restarted;
                Assert.That(
                    secondResult.Succeeded,
                    Is.True,
                    secondResult.Diagnostic.Message);
                Assert.That(
                    restartedResult.Succeeded,
                    Is.True,
                    "A stale waiter-finally cancelled the immediately restarted generation. " +
                    restartedResult.Diagnostic.Message);
                Assert.That(restartedResult.CreatedCount, Is.EqualTo(TargetCount));

                PoolEntrySnapshot snapshot =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.State, Is.EqualTo(PoolEntryState.Ready));
                Assert.That(snapshot.CreatedCount, Is.EqualTo(TargetCount * 3));
                Assert.That(snapshot.ActiveCount, Is.EqualTo(TargetCount * 2));
                Assert.That(snapshot.InactiveCount, Is.EqualTo(TargetCount));

                foreach (PooledHandle handle in held)
                {
                    Assert.That(
                        handle.TryReturn(out CoCoDiagnostic returned),
                        Is.True,
                        returned.Message);
                }
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunActivationWithoutReparentAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                Assert.That(instance.activeInHierarchy, Is.False);

                bool activated =
                    handle.TryActivate(out CoCoDiagnostic activationDiagnostic);
                Assert.That(
                    activated,
                    Is.False,
                    "Consumers must reparent out of the inactive retention root before activation.");
                Assert.That(activationDiagnostic.IsError, Is.True);
                PoolEntrySnapshot pending =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(pending.ActiveCount, Is.Zero);
                Assert.That(pending.InactiveCount, Is.Zero);
                Assert.That(pending.PendingDestroyCount, Is.EqualTo(1));

                UniTask<CoCoDiagnostic> closing = fixture.Scope.CloseAsync();
                for (int frame = 0;
                     frame < 20 && fixture.Scope.State != PoolScopeState.Closed;
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                Assert.That(instance == null, Is.True);
                Assert.That(
                    fixture.Scope.State,
                    Is.EqualTo(PoolScopeState.Closed));
                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                Assert.That(fixture.PoolRuntime.CaptureSnapshot().Scopes, Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunDestroyedPreparedIdleAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(1, 1);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                PoolEntrySnapshot ready =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(ready.InactiveCount, Is.EqualTo(1));
                Assert.That(fixture.PoolRuntime.RetentionRoot.childCount, Is.EqualTo(1));
                GameObject orphan =
                    fixture.PoolRuntime.RetentionRoot.GetChild(0).gameObject;

                UnityEngine.Object.Destroy(orphan);
                await UniTask.NextFrame();
                Assert.That(orphan == null, Is.True);

                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle replacement,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    replacement.TryGetInstance(
                        out GameObject replacementInstance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                Assert.That(replacementInstance == null, Is.False);
                Assert.That(replacementInstance, Is.Not.SameAs(orphan));
                PoolEntrySnapshot replaced =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(replaced.CreatedCount, Is.EqualTo(2));
                Assert.That(replaced.ActiveCount, Is.EqualTo(1));
                Assert.That(replaced.InactiveCount, Is.Zero);

                Assert.That(
                    replacement.TryReturn(out CoCoDiagnostic returned),
                    Is.True,
                    returned.Message);
                UniTask<CoCoDiagnostic> closing = fixture.Scope.CloseAsync();
                for (int frame = 0;
                     frame < 20 && fixture.Scope.State != PoolScopeState.Closed;
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                Assert.That(
                    fixture.Scope.State,
                    Is.EqualTo(PoolScopeState.Closed),
                    "Graceful close must drain both the skipped orphan and replacement.");
                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                PoolRuntimeSnapshot finalized =
                    fixture.PoolRuntime.CaptureSnapshot();
                Assert.That(finalized.Scopes, Is.Empty);
                Assert.That(
                    finalized.Diagnostics.Any(
                        record =>
                            record.EventKind == PoolDiagnosticEventKind.ExternalDestroy),
                    Is.True,
                    "Skipping an externally destroyed idle record must retain diagnostic evidence.");
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(fixture.ContentRuntime.CaptureSnapshot().Entries, Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunDestroyedLeasedInactiveAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                Assert.That(instance.activeInHierarchy, Is.False);

                UnityEngine.Object.Destroy(instance);
                await UniTask.NextFrame();
                Assert.That(instance == null, Is.True);
                Assert.That(
                    handle.TryGetInstance(
                        out _,
                        out CoCoDiagnostic unavailable),
                    Is.False);
                Assert.That(unavailable.IsNone, Is.False);
                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic rejectedReturn),
                    Is.False);
                Assert.That(rejectedReturn.IsNone, Is.False);

                UniTask<CoCoDiagnostic> closing = fixture.Scope.CloseAsync();
                for (int frame = 0;
                     frame < 20 && fixture.Scope.State != PoolScopeState.Closed;
                     frame++)
                {
                    await UniTask.NextFrame();
                }

                Assert.That(
                    fixture.Scope.State,
                    Is.EqualTo(PoolScopeState.Closed),
                    "A destroyed leased-inactive record must not block graceful close.");
                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                Assert.That(fixture.PoolRuntime.CaptureSnapshot().Scopes, Is.Empty);
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(fixture.ContentRuntime.CaptureSnapshot().Entries, Is.Empty);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunPhysicalOnDestroyLeaseBoundaryAsync()
        {
            PoolPhysicalDestroyLeaseProbe.ResetObservation();
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                0,
                1,
                prefab => prefab.AddComponent<PoolPhysicalDestroyLeaseProbe>());
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                instance.GetComponent<PoolPhysicalDestroyLeaseProbe>()
                    .Arm(fixture.ContentRuntime);

                UniTask<CoCoDiagnostic> closing = fixture.Scope.CloseAsync();
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot()
                        .Entries.Single().LeaseCount,
                    Is.EqualTo(1));
                UnityEngine.Object.Destroy(instance);
                await UniTask.NextFrame();

                Assert.That(PoolPhysicalDestroyLeaseProbe.Invoked, Is.True);
                Assert.That(
                    PoolPhysicalDestroyLeaseProbe.Failure,
                    Is.Empty);
                Assert.That(
                    PoolPhysicalDestroyLeaseProbe.ObservedEntryCount,
                    Is.EqualTo(1),
                    "Content ownership must still exist inside physical OnDestroy.");
                Assert.That(
                    PoolPhysicalDestroyLeaseProbe.ObservedLeaseCount,
                    Is.EqualTo(1),
                    "The Pool source lease must outlive every physical OnDestroy callback.");
                Assert.That(instance == null, Is.True);

                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                Assert.That(fixture.PoolRuntime.CaptureSnapshot().Scopes, Is.Empty);
                await UniTask.NextFrame();
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty,
                    "Content may release only after the physical destroy boundary.");
            }
            finally
            {
                await fixture.CleanupAsync();
                PoolPhysicalDestroyLeaseProbe.ResetObservation();
            }
        }

        private static async UniTask RunConcurrentInitialPrepareAsync()
        {
            PoolingTestFixture fixture =
                PoolingTestFixture.CreateDelayed(1, 1);
            var cancelledWaiter = new CancellationTokenSource();
            try
            {
                UniTask<PoolPrepareResult> first =
                    fixture.Scope.PrepareAsync(
                        fixture.Profile,
                        cancelledWaiter.Token);
                UniTask<PoolPrepareResult> second =
                    fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(fixture.DelayedBackend.LoadCount, Is.EqualTo(1));
                Assert.That(fixture.DelayedBackend.PendingCount, Is.EqualTo(1));

                cancelledWaiter.Cancel();
                PoolPrepareResult cancelled = await first;
                Assert.That(cancelled.Cancelled, Is.True);
                Assert.That(
                    fixture.DelayedBackend.LoadCount,
                    Is.EqualTo(1),
                    "Cancelling one waiter must not restart or cancel the shared physical load.");

                fixture.DelayedBackend.CompleteNextSuccess(fixture.Prefab);
                PoolPrepareResult prepared = await second;
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                Assert.That(prepared.CreatedCount, Is.EqualTo(1));
                PoolEntrySnapshot snapshot =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.State, Is.EqualTo(PoolEntryState.Ready));
                Assert.That(snapshot.InactiveCount, Is.EqualTo(1));
                Assert.That(snapshot.HoldsSourceLease, Is.True);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot()
                        .Entries.Single().LeaseCount,
                    Is.EqualTo(1));

                CoCoDiagnostic closed = await fixture.Scope.CloseAsync();
                Assert.That(closed.IsNone, Is.True, closed.Message);
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(fixture.DelayedBackend.ReleaseCount, Is.EqualTo(1));
            }
            finally
            {
                cancelledWaiter.Dispose();
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunCancelledAndFailedInitialPrepareAsync()
        {
            PoolingTestFixture fixture =
                PoolingTestFixture.CreateDelayed(0, 1);
            var firstCancellation = new CancellationTokenSource();
            var secondCancellation = new CancellationTokenSource();
            try
            {
                UniTask<PoolPrepareResult> first =
                    fixture.Scope.PrepareAsync(
                        fixture.Profile,
                        firstCancellation.Token);
                UniTask<PoolPrepareResult> second =
                    fixture.Scope.PrepareAsync(
                        fixture.Profile,
                        secondCancellation.Token);
                Assert.That(fixture.DelayedBackend.LoadCount, Is.EqualTo(1));

                firstCancellation.Cancel();
                secondCancellation.Cancel();
                PoolPrepareResult firstCancelled = await first;
                PoolPrepareResult secondCancelled = await second;
                Assert.That(firstCancelled.Cancelled, Is.True);
                Assert.That(secondCancelled.Cancelled, Is.True);
                await WaitUntilPrepareEntryRemovedAsync(fixture);
                Assert.That(fixture.Scope.CaptureSnapshot().Entries, Is.Empty);
                Assert.That(fixture.ContentRuntime.CaptureSnapshot().Entries, Is.Empty);
                Assert.That(fixture.DelayedBackend.ReleaseCount, Is.Zero);

                UniTask<PoolPrepareResult> failedPrepare =
                    fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(fixture.DelayedBackend.LoadCount, Is.EqualTo(2));
                fixture.DelayedBackend.CompleteNextFailure();
                PoolPrepareResult failed = await failedPrepare;
                Assert.That(failed.Succeeded, Is.False);
                Assert.That(failed.Cancelled, Is.False);
                Assert.That(
                    failed.Diagnostic.Code,
                    Is.EqualTo(CoCoDiagnosticCode.ContentLoadFailed));
                await WaitUntilPrepareEntryRemovedAsync(fixture);
                Assert.That(fixture.Scope.CaptureSnapshot().Entries, Is.Empty);
                Assert.That(fixture.ContentRuntime.CaptureSnapshot().Entries, Is.Empty);
                Assert.That(fixture.DelayedBackend.ReleaseCount, Is.Zero);
            }
            finally
            {
                firstCancellation.Dispose();
                secondCancellation.Dispose();
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask WaitUntilPrepareEntryRemovedAsync(
            PoolingTestFixture fixture)
        {
            for (int frame = 0; frame < 20; frame++)
            {
                if (fixture.Scope.CaptureSnapshot().Entries.Count == 0 &&
                    fixture.ContentRuntime.CaptureSnapshot().Entries.Count == 0)
                {
                    return;
                }

                await UniTask.NextFrame();
            }
        }

        private static void RentBatch(
            PoolingTestFixture fixture,
            int count,
            List<PooledHandle> destination)
        {
            for (int index = 0; index < count; index++)
            {
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic diagnostic),
                    Is.True,
                    diagnostic.Message);
                destination.Add(handle);
            }
        }

        private static async UniTask<PoolPrewarmResult>
            RestartPrewarmFromFirstWaiterAsync(
                PoolingTestFixture fixture,
                UniTask<PoolPrewarmResult> first,
                int targetCount,
                List<PooledHandle> held)
        {
            PoolPrewarmResult firstResult = await first;
            Assert.That(
                firstResult.Succeeded,
                Is.True,
                firstResult.Diagnostic.Message);
            RentBatch(fixture, targetCount, held);
            return await fixture.Scope.PrewarmAsync(fixture.Profile.Id);
        }

        private static async UniTask WaitUntilContentEmptyAsync(
            ContentRuntime runtime)
        {
            for (int frame = 0;
                 frame < 20 && runtime.CaptureSnapshot().Entries.Count != 0;
                 frame++)
            {
                await UniTask.NextFrame();
            }
        }
    }

    internal sealed class PoolActivationReturnReentryProbe : MonoBehaviour
    {
        private PooledHandle _handle;
        private bool _armed;

        internal bool Attempted { get; private set; }
        internal bool NestedReturnSucceeded { get; private set; }
        internal CoCoDiagnostic NestedDiagnostic { get; private set; }

        internal void Arm(PooledHandle handle)
        {
            _handle = handle;
            _armed = true;
        }

        private void OnEnable()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            Attempted = true;
            NestedReturnSucceeded = _handle.TryReturn(out CoCoDiagnostic diagnostic);
            NestedDiagnostic = diagnostic;
        }
    }

    internal sealed class PoolActivationCloseReentryProbe : MonoBehaviour
    {
        private PoolScope _scope;
        private bool _armed;

        internal bool Attempted { get; private set; }
        internal UniTask<CoCoDiagnostic> ClosingOperation { get; private set; }

        internal void Arm(PoolScope scope)
        {
            _scope = scope;
            _armed = true;
        }

        private void OnEnable()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            Attempted = true;
            ClosingOperation = _scope.CloseAsync();
        }
    }

    internal sealed class PoolPhysicalDestroyLeaseProbe : MonoBehaviour
    {
        private ContentRuntime _runtime;
        private bool _armed;

        internal static bool Invoked { get; private set; }
        internal static int ObservedEntryCount { get; private set; }
        internal static int ObservedLeaseCount { get; private set; }
        internal static string Failure { get; private set; } = string.Empty;

        internal static void ResetObservation()
        {
            Invoked = false;
            ObservedEntryCount = 0;
            ObservedLeaseCount = 0;
            Failure = string.Empty;
        }

        internal void Arm(ContentRuntime runtime)
        {
            _runtime = runtime;
            _armed = true;
        }

        private void OnDestroy()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            Invoked = true;
            try
            {
                ContentRuntimeSnapshot snapshot = _runtime.CaptureSnapshot();
                ObservedEntryCount = snapshot.Entries.Count;
                ObservedLeaseCount = snapshot.Entries.Count == 0
                    ? 0
                    : snapshot.Entries[0].LeaseCount;
            }
            catch (System.Exception exception)
            {
                Failure = exception.Message;
            }
        }
    }

}
