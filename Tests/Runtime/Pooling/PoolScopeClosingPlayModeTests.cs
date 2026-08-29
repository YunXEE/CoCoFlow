using System.Collections;
using System.Linq;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    public sealed class PoolScopeClosingPlayModeTests
    {
        [SetUp]
        public void SetUp()
        {
            PoolLifecycleProbe.ResetEvents();
        }

        [UnityTest]
        public IEnumerator ClosingRejectsRentAndKeepsSourceLeaseUntilLateReturnDies() =>
            UniTask.ToCoroutine(RunClosingOwnershipAsync);

        private static async UniTask RunClosingOwnershipAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                0,
                2,
                prefab =>
                    prefab.AddComponent<PoolLifecycleProbe>().Configure("late"));
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
                handle.TryGetInstance(out GameObject instance, out _);
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activate),
                    Is.True,
                    activate.Message);
                PoolLifecycleProbe.ResetEvents();

                UniTask<CoCoDiagnostic> closing = fixture.Scope.CloseAsync();
                Assert.That(fixture.Scope.State, Is.EqualTo(PoolScopeState.Closing));
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out _,
                        out CoCoDiagnostic rejected),
                    Is.False);
                Assert.That(
                    rejected.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolScopeClosing));
                ContentRuntimeSnapshot held =
                    fixture.ContentRuntime.CaptureSnapshot();
                Assert.That(held.Entries, Has.Count.EqualTo(1));
                Assert.That(held.Entries[0].LeaseCount, Is.EqualTo(1));

                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic returned),
                    Is.True,
                    returned.Message);
                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                await UniTask.NextFrame();

                CollectionAssert.AreEqual(
                    new[] { "return:late:ScopeClosing:False" },
                    PoolLifecycleProbe.Events
                        .Where(value => value.StartsWith("return:"))
                        .ToArray());
                Assert.That(instance == null, Is.True);
                Assert.That(fixture.Scope.State, Is.EqualTo(PoolScopeState.Closed));
                Assert.That(
                    fixture.PoolRuntime.CaptureSnapshot().Scopes,
                    Is.Empty);
                await WaitUntilContentEmptyAsync(fixture.ContentRuntime);
                Assert.That(
                    fixture.ContentRuntime.CaptureSnapshot().Entries,
                    Is.Empty);
                Assert.That(fixture.Prefab != null, Is.True);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask WaitUntilContentEmptyAsync(
            ContentRuntime runtime)
        {
            for (int frame = 0;
                 frame < 10 && runtime.CaptureSnapshot().Entries.Count != 0;
                 frame++)
            {
                await UniTask.NextFrame();
            }
        }
    }
}
