using System;
using System.Collections;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Pooling
{
    public sealed class PoolRuntimeShutdownPlayModeTests
    {
        [UnityTest]
        public IEnumerator ShutdownMarksEveryScopeClosingBeforeAwaitingHandles() =>
            UniTask.ToCoroutine(RunShutdownOrderingAsync);

        private static async UniTask RunShutdownOrderingAsync()
        {
            PoolingTestFixture fixture = PoolingTestFixture.Create(0, 1);
            try
            {
                Assert.That(
                    ContentOwnerId.TryCreate(
                        "tests.pooling.shutdown." + Guid.NewGuid().ToString("N"),
                        out ContentOwnerId secondOwner),
                    Is.True);
                Assert.That(
                    fixture.PoolRuntime.TryCreateScope(
                        secondOwner,
                        out PoolScope secondScope,
                        out CoCoDiagnostic secondScopeDiagnostic),
                    Is.True,
                    secondScopeDiagnostic.Message);
                Require(await fixture.Scope.PrepareAsync(fixture.Profile));
                Require(await secondScope.PrepareAsync(fixture.Profile));
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle first,
                        out CoCoDiagnostic firstRent),
                    Is.True,
                    firstRent.Message);
                Assert.That(
                    secondScope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle second,
                        out CoCoDiagnostic secondRent),
                    Is.True,
                    secondRent.Message);

                UniTask<CoCoDiagnostic> shutdown =
                    fixture.PoolRuntime.ShutdownAsync();

                Assert.That(
                    fixture.Scope.State,
                    Is.EqualTo(PoolScopeState.Closing));
                Assert.That(
                    secondScope.State,
                    Is.EqualTo(PoolScopeState.Closing),
                    "Shutdown must close all Scopes before awaiting the first live handle.");
                Assert.That(
                    first.TryReturn(out CoCoDiagnostic firstReturn),
                    Is.True,
                    firstReturn.Message);
                Assert.That(
                    second.TryReturn(out CoCoDiagnostic secondReturn),
                    Is.True,
                    secondReturn.Message);
                CoCoDiagnostic completed = await shutdown;

                Assert.That(completed.IsNone, Is.True, completed.Message);
                Assert.That(fixture.Scope.State, Is.EqualTo(PoolScopeState.Closed));
                Assert.That(secondScope.State, Is.EqualTo(PoolScopeState.Closed));
                Assert.That(fixture.PoolRuntime.IsDisposed, Is.True);
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
