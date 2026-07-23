using System.Collections;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    public sealed class PoolLifecyclePlayModeTests
    {
        [SetUp]
        public void SetUp()
        {
            PoolLifecycleProbe.ResetEvents();
        }

        [UnityTest]
        public IEnumerator ConsumerBindsInactiveInstanceBeforeForwardRentAndReverseReturn() =>
            UniTask.ToCoroutine(RunBindActivateReturnAsync);

        [UnityTest]
        public IEnumerator CopiedHandleCannotReturnAReissuedGeneration() =>
            UniTask.ToCoroutine(RunGenerationSafetyAsync);

        [UnityTest]
        public IEnumerator ExternalDestroyReconcilesCountsAndNextRentCreatesReplacement() =>
            UniTask.ToCoroutine(RunExternalDestroyAsync);

        [UnityTest]
        public IEnumerator RentCallbackFailureDestroysUnknownInstanceState() =>
            UniTask.ToCoroutine(RunRentFailureAsync);

        private static async UniTask RunBindActivateReturnAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                1,
                2,
                prefab =>
                {
                    prefab.AddComponent<PoolLifecycleProbe>().Configure("root-a");
                    prefab.AddComponent<PoolLifecycleProbe>().Configure("root-b");
                    var child = new GameObject("Probe Child");
                    child.transform.SetParent(prefab.transform, false);
                    child.AddComponent<PoolLifecycleProbe>().Configure("child");
                });
            try
            {
                PoolPrepareResult prepared = await fixture.Scope.PrepareAsync(
                    fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
                PoolLifecycleProbe.ResetEvents();

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
                Assert.That(instance.activeSelf, Is.False);
                PoolLifecycleProbe[] probes =
                    instance.GetComponentsInChildren<PoolLifecycleProbe>(true);
                Assert.That(probes, Has.Length.EqualTo(3));
                foreach (PoolLifecycleProbe probe in probes)
                {
                    probe.BoundValue = "bound-before-activate";
                }
                instance.transform.SetParent(fixture.OwnerRoot.transform, false);

                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic activate),
                    Is.True,
                    activate.Message);
                Assert.That(instance.activeSelf, Is.True);
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "rent:root-a:bound-before-activate:False",
                        "rent:root-b:bound-before-activate:False",
                        "rent:child:bound-before-activate:False"
                    },
                    PoolLifecycleProbe.Events
                        .Where(value => value.StartsWith("rent:"))
                        .ToArray());
                int lastRentIndex = PoolLifecycleProbe.Events
                    .Select((value, index) => new { value, index })
                    .Where(item => item.value.StartsWith("rent:"))
                    .Max(item => item.index);
                int firstEnableIndex = PoolLifecycleProbe.Events
                    .Select((value, index) => new { value, index })
                    .Where(item => item.value.StartsWith("enable:"))
                    .Min(item => item.index);
                Assert.That(lastRentIndex, Is.LessThan(firstEnableIndex));

                uint rentalGeneration = handle.Generation;
                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic returned),
                    Is.True,
                    returned.Message);
                Assert.That(instance.activeSelf, Is.False);
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "return:child:ConsumerReturn:False",
                        "return:root-b:ConsumerReturn:False",
                        "return:root-a:ConsumerReturn:False"
                    },
                    PoolLifecycleProbe.Events
                        .Where(value => value.StartsWith("return:"))
                        .ToArray());
                foreach (PoolLifecycleProbe probe in probes)
                {
                    Assert.That(
                        probe.LastRentContext.Generation,
                        Is.EqualTo(rentalGeneration));
                    Assert.That(
                        probe.LastReturnContext.Generation,
                        Is.EqualTo(rentalGeneration));
                }

                PoolEntrySnapshot snapshot = fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.ActiveCount, Is.Zero);
                Assert.That(snapshot.InactiveCount, Is.EqualTo(1));
                Assert.That(snapshot.RentCount, Is.EqualTo(1));
                Assert.That(snapshot.IdleHitCount, Is.EqualTo(1));
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunGenerationSafetyAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(1, 1);
            try
            {
                Require(await fixture.Scope.PrepareAsync(fixture.Profile));
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle first,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                PooledHandle copied = first;
                Assert.That(
                    first.TryGetInstance(
                        out GameObject firstInstance,
                        out CoCoDiagnostic firstResolve),
                    Is.True,
                    firstResolve.Message);
                Assert.That(
                    first.TryReturn(out CoCoDiagnostic firstReturn),
                    Is.True,
                    firstReturn.Message);

                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle second,
                        out CoCoDiagnostic secondRent),
                    Is.True,
                    secondRent.Message);
                Assert.That(
                    second.TryGetInstance(
                        out GameObject secondInstance,
                        out CoCoDiagnostic secondResolve),
                    Is.True,
                    secondResolve.Message);

                Assert.That(secondInstance, Is.SameAs(firstInstance));
                Assert.That(second.InstanceSequence, Is.EqualTo(first.InstanceSequence));
                Assert.That(second.Generation, Is.Not.EqualTo(first.Generation));
                Assert.That(
                    copied.TryReturn(out CoCoDiagnostic rejected),
                    Is.False);
                Assert.That(
                    rejected.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PooledHandleAlreadyReturned));
                Assert.That(
                    second.TryGetInstance(
                        out _,
                        out CoCoDiagnostic stillCurrent),
                    Is.True,
                    stillCurrent.Message);
                Assert.That(
                    second.TryReturn(out CoCoDiagnostic secondReturn),
                    Is.True,
                    secondReturn.Message);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunExternalDestroyAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
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
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);

                Object.Destroy(instance);
                await UniTask.NextFrame();

                Assert.That(
                    handle.TryGetInstance(
                        out _,
                        out CoCoDiagnostic unavailable),
                    Is.False);
                Assert.That(
                    unavailable.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PooledInstanceDestroyed));
                for (int frame = 0; frame < 20; frame++)
                {
                    PoolEntrySnapshot pending =
                        fixture.Scope.CaptureSnapshot().Entries.Single();
                    if (pending.ExternalDestroyCount == 1)
                    {
                        break;
                    }

                    await UniTask.NextFrame();
                }

                Assert.That(
                    handle.TryReturn(out CoCoDiagnostic stale),
                    Is.False);
                Assert.That(stale.Code, Is.EqualTo(CoCoDiagnosticCode.StalePooledHandle));
                PoolEntrySnapshot destroyed =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(destroyed.ActiveCount, Is.Zero);
                Assert.That(destroyed.ExternalDestroyCount, Is.EqualTo(1));
                Assert.That(destroyed.DestroyedCount, Is.EqualTo(1));

                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle replacement,
                        out CoCoDiagnostic replacementRent),
                    Is.True,
                    replacementRent.Message);
                Assert.That(
                    replacement.InstanceSequence,
                    Is.Not.EqualTo(handle.InstanceSequence));
                Assert.That(
                    replacement.TryReturn(out CoCoDiagnostic replacementReturn),
                    Is.True,
                    replacementReturn.Message);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static async UniTask RunRentFailureAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(
                1,
                1,
                prefab =>
                {
                    prefab.AddComponent<PoolLifecycleProbe>().Configure("first");
                    prefab.AddComponent<PoolLifecycleProbe>().Configure("reject");
                });
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
                handle.TryGetInstance(out GameObject instance, out _);
                PoolLifecycleProbe[] probes =
                    instance.GetComponents<PoolLifecycleProbe>();
                probes[1].FailRent = true;
                PoolLifecycleProbe.ResetEvents();

                Assert.That(
                    handle.TryActivate(out CoCoDiagnostic rejected),
                    Is.False);
                Assert.That(
                    rejected.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolActivationFailed));
                await UniTask.NextFrame();

                CollectionAssert.AreEqual(
                    new[]
                    {
                        "rent:first::False",
                        "rent:reject::False",
                        "return:first:ActivationFailure:False"
                    },
                    PoolLifecycleProbe.Events
                        .Where(value =>
                            value.StartsWith("rent:") ||
                            value.StartsWith("return:"))
                        .ToArray());
                Assert.That(
                    handle.TryGetInstance(
                        out _,
                        out CoCoDiagnostic consumed),
                    Is.False);
                Assert.That(
                    consumed.Code == CoCoDiagnosticCode.PooledHandleAlreadyReturned ||
                    consumed.Code == CoCoDiagnosticCode.StalePooledHandle,
                    Is.True);
                PoolEntrySnapshot snapshot =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.InactiveCount, Is.Zero);
                Assert.That(snapshot.ActiveCount, Is.Zero);
                Assert.That(snapshot.DestroyedCount, Is.EqualTo(1));
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }

        private static void Require(PoolPrepareResult result)
        {
            Assert.That(result.Succeeded, Is.True, result.Diagnostic.Message);
        }
    }
}
