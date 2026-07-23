using System.Collections;
using System.Linq;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    public sealed class PoolCapacityAndOwnershipPlayModeTests
    {
        [UnityTest]
        public IEnumerator MaxRetainedLimitsIdleOnlyAndBurstCanExceedIt() =>
            UniTask.ToCoroutine(RunIdleRetentionAsync);

        [UnityTest]
        public IEnumerator ZeroRetentionDestroysEveryReturnButKeepsPreparedSourceLease() =>
            UniTask.ToCoroutine(RunZeroRetentionAsync);

        [UnityTest]
        public IEnumerator ClearAndPrewarmAreExplicitAndKeepEntryReady() =>
            UniTask.ToCoroutine(RunClearAndPrewarmAsync);

        [UnityTest]
        public IEnumerator EachPreparedEntryOwnsExactlyOneContentLease() =>
            UniTask.ToCoroutine(RunContentLeasePerEntryAsync);

        private static async UniTask RunIdleRetentionAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                Require(await fixture.Scope.PrepareAsync(fixture.Profile));
                PooledHandle[] handles = new PooledHandle[3];
                for (int index = 0; index < handles.Length; index++)
                {
                    Assert.That(
                        fixture.Scope.TryRent(
                            fixture.Profile.Id,
                            out handles[index],
                            out CoCoDiagnostic rent),
                        Is.True,
                        rent.Message);
                }

                PoolEntrySnapshot burst = fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(burst.ActiveCount, Is.EqualTo(3));
                Assert.That(burst.CreatedCount, Is.EqualTo(3));
                Assert.That(burst.MaxRetained, Is.EqualTo(1));

                foreach (PooledHandle handle in handles)
                {
                    Assert.That(
                        handle.TryReturn(out CoCoDiagnostic returned),
                        Is.True,
                        returned.Message);
                }

                await WaitForDestroyedCountAsync(fixture.Scope, 2);
                PoolEntrySnapshot settled =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(settled.ActiveCount, Is.Zero);
                Assert.That(settled.InactiveCount, Is.EqualTo(1));
                Assert.That(settled.DestroyedCount, Is.EqualTo(2));
                Assert.That(settled.RentCount, Is.EqualTo(3));
                Assert.That(settled.CreateMissCount, Is.EqualTo(3));
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunZeroRetentionAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 0);
            try
            {
                Require(await fixture.Scope.PrepareAsync(fixture.Profile));
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
                await WaitForDestroyedCountAsync(fixture.Scope, 1);

                PoolEntrySnapshot snapshot =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.InactiveCount, Is.Zero);
                Assert.That(snapshot.DestroyedCount, Is.EqualTo(1));
                Assert.That(snapshot.HoldsSourceLease, Is.True);
                AssertSingleContentEntry(fixture.ContentRuntime, 1);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunClearAndPrewarmAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(2, 2);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Require(prepared);
                Assert.That(prepared.CreatedCount, Is.EqualTo(2));
                PoolEntrySnapshot initial =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(initial.InactiveCount, Is.EqualTo(2));

                Assert.That(
                    fixture.Scope.TryClearInactive(
                        fixture.Profile.Id,
                        out CoCoDiagnostic cleared),
                    Is.True,
                    cleared.Message);
                await WaitForDestroyedCountAsync(fixture.Scope, 2);
                PoolEntrySnapshot empty =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(empty.State, Is.EqualTo(PoolEntryState.Ready));
                Assert.That(empty.InactiveCount, Is.Zero);
                Assert.That(empty.DestroyedCount, Is.EqualTo(2));
                Assert.That(empty.HoldsSourceLease, Is.True);

                PoolPrewarmResult prewarmed =
                    await fixture.Scope.PrewarmAsync(fixture.Profile.Id);
                Assert.That(
                    prewarmed.Succeeded,
                    Is.True,
                    prewarmed.Diagnostic.Message);
                Assert.That(prewarmed.CreatedCount, Is.EqualTo(2));
                Assert.That(prewarmed.InactiveCount, Is.EqualTo(2));
                PoolEntrySnapshot refilled =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(refilled.State, Is.EqualTo(PoolEntryState.Ready));
                Assert.That(refilled.InactiveCount, Is.EqualTo(2));
                Assert.That(refilled.CreatedCount, Is.EqualTo(4));
                AssertSingleContentEntry(fixture.ContentRuntime, 1);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunContentLeasePerEntryAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                PoolProfile sibling =
                    fixture.CreateSiblingProfile("second", 0, 1);
                Require(await fixture.Scope.PrepareAsync(fixture.Profile));
                Require(await fixture.Scope.PrepareAsync(sibling));

                PoolScopeSnapshot poolSnapshot = fixture.Scope.CaptureSnapshot();
                Assert.That(poolSnapshot.Entries, Has.Count.EqualTo(2));
                Assert.That(
                    poolSnapshot.Entries.All(entry => entry.HoldsSourceLease),
                    Is.True);
                AssertSingleContentEntry(fixture.ContentRuntime, 2);

                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle first,
                        out CoCoDiagnostic firstRent),
                    Is.True,
                    firstRent.Message);
                Assert.That(
                    fixture.Scope.TryRent(
                        sibling.Id,
                        out PooledHandle second,
                        out CoCoDiagnostic secondRent),
                    Is.True,
                    secondRent.Message);
                AssertSingleContentEntry(fixture.ContentRuntime, 2);
                first.Dispose();
                second.Dispose();
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static void AssertSingleContentEntry(
            ContentRuntime runtime,
            int leaseCount)
        {
            ContentRuntimeSnapshot snapshot = runtime.CaptureSnapshot();
            Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
            Assert.That(snapshot.Entries[0].LeaseCount, Is.EqualTo(leaseCount));
            Assert.That(
                snapshot.Entries[0].Kind,
                Is.EqualTo(ContentKind.PrefabSource));
        }

        private static async UniTask WaitForDestroyedCountAsync(
            PoolScope scope,
            long expectedCount)
        {
            for (int frame = 0; frame < 20; frame++)
            {
                PoolEntrySnapshot snapshot =
                    scope.CaptureSnapshot().Entries.Single();
                if (snapshot.DestroyedCount >= expectedCount)
                {
                    return;
                }

                await UniTask.NextFrame();
            }
        }

        private static void Require(PoolPrepareResult result)
        {
            Assert.That(result.Succeeded, Is.True, result.Diagnostic.Message);
        }
    }
}
