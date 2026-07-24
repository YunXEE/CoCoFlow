using System;
using System.Collections;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    public sealed class PoolWarmPathAllocationPlayModeTests
    {
        [UnityTest]
        public IEnumerator WarmIdleHitRentActivateReturnAllocatesNoManagedBytes() =>
            UniTask.ToCoroutine(RunWarmPathAllocationAsync);

        private static async UniTask RunWarmPathAllocationAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(1, 1);
            try
            {
                PoolPrepareResult prepared =
                    await fixture.Scope.PrepareAsync(fixture.Profile);
                Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);

                for (int index = 0; index < 16; index++)
                {
                    Assert.That(
                        fixture.Scope.TryRent(
                            fixture.Profile.Id,
                            out PooledHandle warmHandle,
                            out CoCoDiagnostic warmRent),
                        Is.True,
                        warmRent.Message);
                    warmHandle.TryGetInstance(out UnityEngine.GameObject instance, out _);
                    instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                    Assert.That(
                        warmHandle.TryActivate(out CoCoDiagnostic warmActivate),
                        Is.True,
                        warmActivate.Message);
                    Assert.That(
                        warmHandle.TryReturn(out CoCoDiagnostic warmReturn),
                        Is.True,
                        warmReturn.Message);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                _ = GC.GetAllocatedBytesForCurrentThread();
                bool succeeded = true;
                CoCoDiagnostic failure = default;
                const int IterationCount = 256;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < IterationCount; index++)
                {
                    if (!fixture.Scope.TryRent(
                            fixture.Profile.Id,
                            out PooledHandle handle,
                            out failure) ||
                        !handle.TryGetInstance(
                            out UnityEngine.GameObject instance,
                            out failure))
                    {
                        succeeded = false;
                        break;
                    }

                    instance.transform.SetParent(fixture.OwnerRoot.transform, false);
                    if (!handle.TryActivate(out failure) ||
                        !handle.TryReturn(out failure))
                    {
                        succeeded = false;
                        break;
                    }
                }

                long allocated =
                    GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(succeeded, Is.True, failure.Message);
                Assert.That(
                    allocated,
                    Is.Zero,
                    "The warmed idle-hit path allocated managed memory.");
                PoolEntrySnapshot snapshot =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.InactiveCount, Is.EqualTo(1));
                Assert.That(
                    snapshot.IdleHitCount,
                    Is.EqualTo(16 + IterationCount));
                Assert.That(snapshot.CreateMissCount, Is.Zero);
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }
    }
}
