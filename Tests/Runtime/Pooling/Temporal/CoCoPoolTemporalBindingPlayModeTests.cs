using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Runtime.Pooling.Temporal.Tests
{
    public sealed class CoCoPoolTemporalBindingPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            StateGraphHostPoolingTestBridge.ResetProjectBindings();
            PoolTemporalApplyProbe.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            StateGraphHostPoolingTestBridge.ResetProjectBindings();
            PoolTemporalApplyProbe.Reset();
        }

        [UnityTest]
        public IEnumerator AdoptProjectionCancelAndRingExpiryPreservePhysicalAuthority() =>
            UniTask.ToCoroutine(RunProjectionLifecycleAsync);

        [UnityTest]
        public IEnumerator DestroyedTemporalPhysicalFailsSafelyAndFaultsLiveAuthority() =>
            UniTask.ToCoroutine(RunUnavailablePhysicalAsync);

        [UnityTest]
        public IEnumerator TemporalStatePollingDoesNotReconcileDestroyedAuthority() =>
            UniTask.ToCoroutine(RunTemporalStateSnapshotPurityAsync);

        [UnityTest]
        public IEnumerator SceneRootAndLatestLiveParentSurviveTemporalReplay() =>
            UniTask.ToCoroutine(RunPresentationParentReplayAsync);

        [UnityTest]
        public IEnumerator DestroyedPresentationParentFailsReplayWithoutThrowing() =>
            UniTask.ToCoroutine(RunDestroyedPresentationParentAsync);

        [UnityTest]
        public IEnumerator DestroyedDownstreamIsRejectedBeforePoolMutation() =>
            UniTask.ToCoroutine(
                () => RunDownstreamPreflightFailureAsync(
                    DownstreamPreflightFailure.Destroy));

        [UnityTest]
        public IEnumerator ReplacedDownstreamIsRejectedBeforePoolMutation() =>
            UniTask.ToCoroutine(
                () => RunDownstreamPreflightFailureAsync(
                    DownstreamPreflightFailure.Replace));

        [UnityTest]
        public IEnumerator MovedDownstreamIsRejectedBeforePoolMutation() =>
            UniTask.ToCoroutine(
                () => RunDownstreamPreflightFailureAsync(
                    DownstreamPreflightFailure.MoveOutside));

        [UnityTest]
        public IEnumerator DownstreamRejectionRequiresCorrectionBeforeActivation() =>
            UniTask.ToCoroutine(
                () => RunDownstreamCallbackFailureAsync(
                    PoolDownstreamFailure.Reject));

        [UnityTest]
        public IEnumerator DownstreamExceptionRequiresCorrectionBeforeActivation() =>
            UniTask.ToCoroutine(
                () => RunDownstreamCallbackFailureAsync(
                    PoolDownstreamFailure.Throw));

        [UnityTest]
        public IEnumerator DownstreamSelfMoveRequiresCorrectionBeforeActivation() =>
            UniTask.ToCoroutine(
                () => RunDownstreamCallbackFailureAsync(
                    PoolDownstreamFailure.MoveOutside));

        [UnityTest]
        public IEnumerator ProjectedOnlyPhysicalLossCancelsInOneAttempt() =>
            UniTask.ToCoroutine(RunProjectedOnlyCancelRecoveryAsync);

        [UnityTest]
        public IEnumerator ProjectedOnlyPhysicalLossCorrectsInOneAttempt() =>
            UniTask.ToCoroutine(RunProjectedOnlyCorrectionRecoveryAsync);

        [UnityTest]
        public IEnumerator ConfirmAbsentDiscardsFutureAndReleasesLastPhysicalReference() =>
            UniTask.ToCoroutine(RunConfirmAbsentBranchReleaseAsync);

        [UnityTest]
        public IEnumerator AuthorityResetKeepsCurrentPhysicalWithoutApplyCallbacks() =>
            UniTask.ToCoroutine(RunAuthorityResetCurrentPhysicalAsync);

        [UnityTest]
        public IEnumerator AuthorityResetCancelIsPureAndCommitDrainsOldHistoryOnlyRecord() =>
            UniTask.ToCoroutine(RunAuthorityResetCleanupAsync);

        [UnityTest]
        public IEnumerator AuthorityResetFreezesMutationAndPreservesPendingActivation() =>
            UniTask.ToCoroutine(RunAuthorityResetPendingActivationAsync);

        private async UniTask RunProjectionLifecycleAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                PooledHandle copied = handle;
                Assert.That(
                    handle.TryGetInstance(
                        out GameObject instance,
                        out CoCoDiagnostic resolve),
                    Is.True,
                    resolve.Message);
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                PoolTemporalApplyProbe applyProbe =
                    instance.GetComponent<PoolTemporalApplyProbe>();
                applyProbe.ObservedBinding = fixture.HostScenario.Binding;
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        1UL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);

                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(handle.IsValid, Is.False);
                Assert.That(
                    copied.TryReturn(out CoCoDiagnostic staleCopy),
                    Is.False);
                Assert.That(
                    staleCopy.Code,
                    Is.EqualTo(CoCoDiagnosticCode.StalePooledHandle));
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out GameObject active,
                        out CoCoDiagnostic activeResolve),
                    Is.True,
                    activeResolve.Message);
                Assert.That(active, Is.SameAs(instance));
                Assert.That(active.activeInHierarchy, Is.True);

                Step(fixture.HostScenario, 10);
                Assert.That(
                    fixture.Binding.TryDespawn(
                        entityId,
                        out CoCoDiagnostic despawned),
                    Is.True,
                    despawned.Message);
                Assert.That(instance.activeSelf, Is.False);
                PoolEntrySnapshot quarantined =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(quarantined.QuarantineCount, Is.EqualTo(1));
                Assert.That(quarantined.InactiveCount, Is.Zero);

                Step(fixture.HostScenario, 20);
                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic preview),
                    Is.True,
                    preview.Message);

                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out GameObject previewed,
                        out CoCoDiagnostic previewResolve),
                    Is.True,
                    previewResolve.Message);
                Assert.That(previewed, Is.SameAs(instance));
                Assert.That(previewed.activeInHierarchy, Is.True);
                Assert.That(applyProbe.ApplyCount, Is.EqualTo(1));
                Assert.That(
                    applyProbe.LastContext.ApplyKind,
                    Is.EqualTo(PoolTemporalApplyKind.Preview));
                Assert.That(applyProbe.LastContext.IsPresent, Is.True);
                Assert.That(
                    applyProbe.ObservedValueDuringApply,
                    Is.EqualTo(10),
                    "Pool apply must run after the downstream Context restore.");

                Assert.That(
                    fixture.HostScenario.Host.TryCancelTemporalPreview(
                        out CoCoDiagnostic cancel),
                    Is.True,
                    cancel.Message);
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out _,
                        out CoCoDiagnostic absent),
                    Is.False);
                Assert.That(
                    absent.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalEntityUnavailable));
                Assert.That(instance.activeSelf, Is.False);

                Step(fixture.HostScenario, 30);
                Step(fixture.HostScenario, 40);
                PoolEntrySnapshot expired =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(expired.QuarantineCount, Is.Zero);
                Assert.That(expired.TemporalRetainedCount, Is.Zero);
                Assert.That(expired.InactiveCount, Is.EqualTo(1));
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out _,
                        out CoCoDiagnostic released),
                    Is.False);
                Assert.That(
                    released.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalEntityUnavailable));
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunUnavailablePhysicalAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
                Assert.That(
                    fixture.Scope.TryRent(
                        fixture.Profile.Id,
                        out PooledHandle handle,
                        out CoCoDiagnostic rent),
                    Is.True,
                    rent.Message);
                handle.TryGetInstance(out GameObject instance, out _);
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        2UL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);

                Object.Destroy(instance);
                await UniTask.NextFrame();

                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out _,
                        out CoCoDiagnostic unavailable),
                    Is.False);
                Assert.That(unavailable.IsError, Is.True);
                Assert.That(
                    unavailable.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalEntityUnavailable));
                Assert.That(fixture.HostScenario.Host.Fault.IsFaulted, Is.True);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.True);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunTemporalStateSnapshotPurityAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
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
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0x57A7EUL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Step(fixture.HostScenario, 10);
                Step(fixture.HostScenario, 20);
                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic preview),
                    Is.True,
                    preview.Message);

                Object.Destroy(instance);
                await UniTask.NextFrame();
                for (int poll = 0; poll < 3; poll++)
                {
                    CoCoTemporalState state =
                        fixture.HostScenario.Host.TemporalState;
                    Assert.That(
                        state.Mode,
                        Is.EqualTo(CoCoTemporalMode.Previewing));
                    Assert.That(state.PreviewDepth, Is.EqualTo(1));
                    Assert.That(state.CanConfirm, Is.True);
                    Assert.That(
                        fixture.HostScenario.Host.Fault.IsFaulted,
                        Is.False);
                    Assert.That(
                        fixture.HostScenario.Host.RequiresWorldCorrection,
                        Is.False);
                    Assert.That(
                        fixture.HostScenario.Host.LastDiagnostic.IsNone,
                        Is.True);
                    Assert.That(
                        fixture.Binding.LastDiagnostic.IsNone,
                        Is.True);
                }

                Assert.That(
                    fixture.HostScenario.Host.TryConfirmTemporalRestore(
                        out CoCoDiagnostic unavailable),
                    Is.False);
                Assert.That(
                    unavailable.Code,
                    Is.EqualTo(
                        CoCoDiagnosticCode.PoolTemporalEntityUnavailable));
                Assert.That(
                    fixture.HostScenario.Host.Fault.IsFaulted,
                    Is.True);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.True);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunPresentationParentReplayAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
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
                instance.transform.SetParent(null, false);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0x5100UL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Step(fixture.HostScenario, 10);
                Assert.That(
                    fixture.Binding.TryDespawn(
                        entityId,
                        out CoCoDiagnostic despawned),
                    Is.True,
                    despawned.Message);
                Step(fixture.HostScenario, 20);

                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic preview),
                    Is.True,
                    preview.Message);
                Assert.That(instance.transform.parent, Is.Null);
                Assert.That(instance.activeInHierarchy, Is.True);
                Assert.That(
                    fixture.HostScenario.Host.TryConfirmTemporalRestore(
                        out CoCoDiagnostic confirmed),
                    Is.True,
                    confirmed.Message);

                var latestParent =
                    new GameObject("Pre9 Latest Temporal Presentation Parent");
                latestParent.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                _objects.Add(latestParent);
                instance.transform.SetParent(latestParent.transform, false);
                Assert.That(
                    fixture.Binding.TryDespawn(
                        entityId,
                        out CoCoDiagnostic secondDespawn),
                    Is.True,
                    secondDespawn.Message);
                Step(fixture.HostScenario, 30);

                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic secondBegin),
                    Is.True,
                    secondBegin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic secondPreview),
                    Is.True,
                    secondPreview.Message);
                Assert.That(
                    instance.transform.parent,
                    Is.SameAs(latestParent.transform));
                Assert.That(instance.activeInHierarchy, Is.True);
                Assert.That(
                    fixture.HostScenario.Host.TryCancelTemporalPreview(
                        out CoCoDiagnostic cancelled),
                    Is.True,
                    cancelled.Message);
                Assert.That(instance.activeSelf, Is.False);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunDestroyedPresentationParentAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
                var presentationParent =
                    new GameObject("Pre9 Destroyed Temporal Presentation Parent");
                presentationParent.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                _objects.Add(presentationParent);
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
                instance.transform.SetParent(presentationParent.transform, false);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0xD357UL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Step(fixture.HostScenario, 10);
                Assert.That(
                    fixture.Binding.TryDespawn(
                        entityId,
                        out CoCoDiagnostic despawned),
                    Is.True,
                    despawned.Message);
                Step(fixture.HostScenario, 20);
                Object.Destroy(presentationParent);
                await UniTask.NextFrame();

                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic unavailable),
                    Is.False);
                Assert.That(unavailable.IsError, Is.True);
                Assert.That(fixture.HostScenario.Host.Fault.IsFaulted, Is.True);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.True);
                Assert.That(instance == null || !instance.activeInHierarchy, Is.True);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunDownstreamPreflightFailureAsync(
            DownstreamPreflightFailure failure)
        {
            TemporalPoolingFixture fixture =
                await CreateFixtureAsync(useProbeDownstream: true);
            try
            {
                Step(fixture.HostScenario, 10);
                Step(fixture.HostScenario, 20);
                Assert.That(
                    fixture.HostScenario.Host.TemporalState.Count,
                    Is.GreaterThanOrEqualTo(2));
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
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                PoolTemporalApplyProbe applyProbe =
                    instance.GetComponent<PoolTemporalApplyProbe>();
                PoolTemporalDespawnReentryProbe lifecycleProbe =
                    instance.GetComponent<PoolTemporalDespawnReentryProbe>();
                Assert.That(lifecycleProbe, Is.Not.Null);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0xD001UL + (ulong)failure,
                        out CoCoTemporalEntityId entityId),
                    Is.True);

                if (failure != DownstreamPreflightFailure.Destroy)
                {
                    Assert.That(
                        fixture.Binding.TryAdopt(
                            entityId,
                            ref handle,
                            out CoCoDiagnostic adopted),
                        Is.True,
                        adopted.Message);
                }

                if (failure == DownstreamPreflightFailure.MoveOutside)
                {
                    Assert.That(
                        fixture.Binding.TryActivate(
                            entityId,
                            out CoCoDiagnostic activated),
                        Is.True,
                        activated.Message);
                }

                PoolEntrySnapshot before =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                bool wasActive = instance.activeSelf;
                int rentCallbacksBefore = lifecycleProbe.RentCount;
                int returnCallbacksBefore = lifecycleProbe.ReturnCount;

                switch (failure)
                {
                    case DownstreamPreflightFailure.Destroy:
                        Object.DestroyImmediate(fixture.DownstreamProbe);
                        break;
                    case DownstreamPreflightFailure.Replace:
                        PoolDownstreamRestoreProbe replacement =
                            fixture.HostScenario.GameObject
                                .AddComponent<PoolDownstreamRestoreProbe>();
                        SetField(
                            fixture.Binding,
                            "downstreamRestoreBinding",
                            replacement);
                        break;
                    case DownstreamPreflightFailure.MoveOutside:
                        fixture.DownstreamProbe.transform.SetParent(null, false);
                        break;
                }

                bool mutated;
                CoCoDiagnostic diagnostic;
                switch (failure)
                {
                    case DownstreamPreflightFailure.Destroy:
                        mutated = fixture.Binding.TryAdopt(
                            entityId,
                            ref handle,
                            out diagnostic);
                        Assert.That(mutated, Is.False);
                        Assert.That(diagnostic.IsError, Is.True);
                        break;
                    case DownstreamPreflightFailure.Replace:
                        mutated = fixture.Binding.TryActivate(
                            entityId,
                            out diagnostic);
                        Assert.That(mutated, Is.False);
                        Assert.That(diagnostic.IsError, Is.True);
                        break;
                    default:
                        mutated = fixture.Binding.TryDespawn(
                            entityId,
                            out diagnostic);
                        Assert.That(mutated, Is.False);
                        Assert.That(diagnostic.IsError, Is.True);
                        break;
                }

                Assert.That(
                    diagnostic.Code,
                    Is.EqualTo(
                        CoCoDiagnosticCode.PoolTemporalProjectionFailed));
                PoolEntrySnapshot after =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                AssertPoolOccupancyEqual(before, after);
                Assert.That(instance.activeSelf, Is.EqualTo(wasActive));
                Assert.That(applyProbe.ApplyCount, Is.Zero);
                Assert.That(
                    lifecycleProbe.RentCount,
                    Is.EqualTo(rentCallbacksBefore));
                Assert.That(
                    lifecycleProbe.ReturnCount,
                    Is.EqualTo(returnCallbacksBefore));

                if (failure == DownstreamPreflightFailure.MoveOutside)
                {
                    Assert.That(
                        fixture.Binding.TryResolveInstance(
                            entityId,
                            out GameObject preserved,
                            out CoCoDiagnostic preservedDiagnostic),
                        Is.True,
                        preservedDiagnostic.Message);
                    Assert.That(preserved, Is.SameAs(instance));
                }

                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic hostRejected),
                    Is.False);
                Assert.That(hostRejected.IsError, Is.True);
                Assert.That(
                    hostRejected.Code,
                    Is.EqualTo(CoCoDiagnosticCode.InvalidActorBinding));
                PoolEntrySnapshot afterHostRejection =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                AssertPoolOccupancyEqual(after, afterHostRejection);
                Assert.That(instance.activeSelf, Is.EqualTo(wasActive));
                Assert.That(applyProbe.ApplyCount, Is.Zero);
                Assert.That(
                    lifecycleProbe.RentCount,
                    Is.EqualTo(rentCallbacksBefore));
                Assert.That(
                    lifecycleProbe.ReturnCount,
                    Is.EqualTo(returnCallbacksBefore));

                if (failure == DownstreamPreflightFailure.Destroy)
                {
                    Assert.That(handle.IsValid, Is.True);
                    Assert.That(
                        handle.TryGetInstance(
                            out GameObject preserved,
                            out CoCoDiagnostic preservedDiagnostic),
                        Is.True,
                        preservedDiagnostic.Message);
                    Assert.That(preserved, Is.SameAs(instance));
                    Assert.That(
                        fixture.Binding.TryResolveInstance(
                            entityId,
                            out _,
                            out CoCoDiagnostic absent),
                        Is.False);
                    Assert.That(
                        absent.Code,
                        Is.EqualTo(
                            CoCoDiagnosticCode.PoolTemporalEntityUnavailable));
                    Assert.That(
                        handle.TryReturn(
                            out CoCoDiagnostic returned),
                        Is.True,
                        returned.Message);
                    return;
                }

                SetField(
                    fixture.Binding,
                    "downstreamRestoreBinding",
                    fixture.DownstreamProbe);
                if (failure == DownstreamPreflightFailure.MoveOutside)
                {
                    fixture.DownstreamProbe.transform.SetParent(
                        fixture.HostScenario.GameObject.transform,
                        false);
                }
                else
                {
                    Assert.That(
                        fixture.Binding.TryActivate(
                            entityId,
                            out CoCoDiagnostic activated),
                        Is.True,
                        activated.Message);
                    Assert.That(instance.activeSelf, Is.True);
                }

                Assert.That(
                    fixture.Binding.TryDespawn(
                        entityId,
                        out CoCoDiagnostic despawned),
                    Is.True,
                    despawned.Message);
                Assert.That(instance.activeSelf, Is.False);
                Assert.That(applyProbe.ApplyCount, Is.Zero);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunDownstreamCallbackFailureAsync(
            PoolDownstreamFailure failure)
        {
            TemporalPoolingFixture fixture =
                await CreateFixtureAsync(useProbeDownstream: true);
            try
            {
                RetainedTemporalEntity retained =
                    await CreateRetainedHistoryAsync(fixture, 0xD002UL);
                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                fixture.DownstreamProbe.Failure = failure;

                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic rejected),
                    Is.False);
                Assert.That(rejected.IsError, Is.True);
                Assert.That(retained.ApplyProbe.ApplyCount, Is.Zero);
                Assert.That(fixture.HostScenario.Host.Fault.IsFaulted, Is.True);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.True);

                if (failure == PoolDownstreamFailure.MoveOutside)
                {
                    fixture.DownstreamProbe.transform.SetParent(
                        fixture.HostScenario.GameObject.transform,
                        false);
                }

                fixture.DownstreamProbe.Failure = PoolDownstreamFailure.None;
                Assert.That(
                    fixture.HostScenario.Host.TryCorrectWorld(
                        out CoCoDiagnostic corrected),
                    Is.True,
                    corrected.Message);
                Assert.That(fixture.HostScenario.Host.Fault.IsFaulted, Is.False);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.False);
                Assert.That(
                    fixture.HostScenario.Host.TemporalState.Mode,
                    Is.EqualTo(CoCoTemporalMode.Ready));
                Assert.That(retained.Instance.activeSelf, Is.False);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunProjectedOnlyCancelRecoveryAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
                RetainedTemporalEntity retained =
                    await CreateRetainedHistoryAsync(fixture, 0xCA11UL);
                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic preview),
                    Is.True,
                    preview.Message);
                Assert.That(retained.Instance.activeInHierarchy, Is.True);

                Object.Destroy(retained.Instance);
                await UniTask.NextFrame();
                for (int poll = 0; poll < 3; poll++)
                {
                    CoCoTemporalState state =
                        fixture.HostScenario.Host.TemporalState;
                    Assert.That(
                        state.Mode,
                        Is.EqualTo(CoCoTemporalMode.Previewing));
                    Assert.That(state.PreviewDepth, Is.EqualTo(1));
                    Assert.That(state.CanConfirm, Is.True);
                    Assert.That(
                        fixture.HostScenario.Host.Fault.IsFaulted,
                        Is.False);
                    Assert.That(
                        fixture.HostScenario.Host.RequiresWorldCorrection,
                        Is.False);
                    Assert.That(
                        fixture.HostScenario.Host.LastDiagnostic.IsNone,
                        Is.True);
                    Assert.That(
                        fixture.Binding.LastDiagnostic.IsNone,
                        Is.True);
                }

                Assert.That(
                    fixture.HostScenario.Host.TryCancelTemporalPreview(
                        out CoCoDiagnostic cancelled),
                    Is.True,
                    cancelled.Message);
                Assert.That(
                    fixture.HostScenario.Host.TemporalState.Mode,
                    Is.EqualTo(CoCoTemporalMode.Ready));
                Assert.That(fixture.HostScenario.Host.Fault.IsFaulted, Is.False);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.False);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunProjectedOnlyCorrectionRecoveryAsync()
        {
            TemporalPoolingFixture fixture =
                await CreateFixtureAsync(useProbeDownstream: true);
            try
            {
                RetainedTemporalEntity retained =
                    await CreateRetainedHistoryAsync(fixture, 0xC022UL);
                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                fixture.DownstreamProbe.Failure = PoolDownstreamFailure.Reject;
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic rejected),
                    Is.False);
                Assert.That(rejected.IsError, Is.True);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.True);

                Object.Destroy(retained.Instance);
                await UniTask.NextFrame();
                fixture.DownstreamProbe.Failure = PoolDownstreamFailure.None;
                Assert.That(
                    fixture.HostScenario.Host.TryCorrectWorld(
                        out CoCoDiagnostic corrected),
                    Is.True,
                    corrected.Message);
                Assert.That(
                    fixture.HostScenario.Host.TemporalState.Mode,
                    Is.EqualTo(CoCoTemporalMode.Ready));
                Assert.That(fixture.HostScenario.Host.Fault.IsFaulted, Is.False);
                Assert.That(
                    fixture.HostScenario.Host.RequiresWorldCorrection,
                    Is.False);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunConfirmAbsentBranchReleaseAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
                Step(fixture.HostScenario, 5);
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
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0xAB5EUL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Step(fixture.HostScenario, 10);

                Assert.That(
                    fixture.HostScenario.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.HostScenario.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic preview),
                    Is.True,
                    preview.Message);
                Assert.That(instance.activeSelf, Is.False);
                Assert.That(
                    fixture.HostScenario.Host.TryConfirmTemporalRestore(
                        out CoCoDiagnostic confirmed),
                    Is.True,
                    confirmed.Message);
                Assert.That(
                    fixture.HostScenario.Host.TemporalState.Mode,
                    Is.EqualTo(CoCoTemporalMode.Ready));

                for (int frame = 0; frame < 20; frame++)
                {
                    PoolEntrySnapshot pending =
                        fixture.Scope.CaptureSnapshot().Entries.Single();
                    if (pending.TemporalRetainedCount == 0 &&
                        pending.QuarantineCount == 0)
                    {
                        break;
                    }

                    await UniTask.NextFrame();
                }

                PoolEntrySnapshot released =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(released.TemporalRetainedCount, Is.Zero);
                Assert.That(released.QuarantineCount, Is.Zero);
                Assert.That(released.InactiveCount, Is.EqualTo(1));
                Assert.That(instance.activeSelf, Is.False);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private UniTask<RetainedTemporalEntity>
            CreateRetainedHistoryAsync(
                TemporalPoolingFixture fixture,
                ulong entityLow)
        {
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
            instance.transform.SetParent(
                fixture.HostScenario.GameObject.transform,
                false);
            PoolTemporalApplyProbe applyProbe =
                instance.GetComponent<PoolTemporalApplyProbe>();
            applyProbe.ObservedBinding = fixture.HostScenario.Binding;
            Assert.That(
                CoCoTemporalEntityId.TryCreate(
                    0xABCDUL,
                    entityLow,
                    out CoCoTemporalEntityId entityId),
                Is.True);
            Assert.That(
                fixture.Binding.TryAdopt(
                    entityId,
                    ref handle,
                    out CoCoDiagnostic adopted),
                Is.True,
                adopted.Message);
            Assert.That(
                fixture.Binding.TryActivate(
                    entityId,
                    out CoCoDiagnostic activated),
                Is.True,
                activated.Message);
            Step(fixture.HostScenario, 10);
            Assert.That(
                fixture.Binding.TryDespawn(
                    entityId,
                    out CoCoDiagnostic despawned),
                Is.True,
                despawned.Message);
            Step(fixture.HostScenario, 20);
            return UniTask.FromResult(
                new RetainedTemporalEntity(
                    entityId,
                    instance,
                    applyProbe));
        }

        private async UniTask RunAuthorityResetCurrentPhysicalAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
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
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                PoolTemporalApplyProbe applyProbe =
                    instance.GetComponent<PoolTemporalApplyProbe>();
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0xA071UL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Step(fixture.HostScenario, 10);
                Step(fixture.HostScenario, 20);

                PoolEntrySnapshot before =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                var participant =
                    (ICoCoStateGraphTemporalParticipant)fixture.Binding;
                Assert.That(
                    participant.TryPrepareAuthorityReset(
                        fixture.HostScenario.Host.TemporalState.Current,
                        out CoCoDiagnostic prepared),
                    Is.True,
                    prepared.Message);
                participant.CommitPreparedAuthorityResetNoFail();
                participant.DrainPublishedCleanupNoFail();

                Assert.That(GetSidecarCount(fixture.Binding), Is.EqualTo(1));
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out GameObject retained,
                        out CoCoDiagnostic retainedDiagnostic),
                    Is.True,
                    retainedDiagnostic.Message);
                Assert.That(retained, Is.SameAs(instance));
                Assert.That(retained.activeInHierarchy, Is.True);
                Assert.That(applyProbe.ApplyCount, Is.Zero);
                AssertPoolOccupancyEqual(
                    before,
                    fixture.Scope.CaptureSnapshot().Entries.Single());
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunAuthorityResetCleanupAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
                RetainedTemporalEntity historical =
                    await CreateRetainedHistoryAsync(
                        fixture,
                        0xA072UL);
                PoolEntrySnapshot before =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(GetSidecarCount(fixture.Binding), Is.EqualTo(2));

                var participant =
                    (ICoCoStateGraphTemporalParticipant)fixture.Binding;
                Assert.That(
                    participant.TryPrepareAuthorityReset(
                        fixture.HostScenario.Host.TemporalState.Current,
                        out CoCoDiagnostic prepared),
                    Is.True,
                    prepared.Message);
                participant.CancelPreparedAuthorityResetNoFail();

                Assert.That(GetSidecarCount(fixture.Binding), Is.EqualTo(2));
                AssertPoolOccupancyEqual(
                    before,
                    fixture.Scope.CaptureSnapshot().Entries.Single());

                Assert.That(
                    participant.TryPrepareAuthorityReset(
                        fixture.HostScenario.Host.TemporalState.Current,
                        out prepared),
                    Is.True,
                    prepared.Message);
                participant.CommitPreparedAuthorityResetNoFail();
                participant.DrainPublishedCleanupNoFail();

                Assert.That(GetSidecarCount(fixture.Binding), Is.EqualTo(1));
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        historical.EntityId,
                        out _,
                        out CoCoDiagnostic released),
                    Is.False);
                Assert.That(
                    released.Code,
                    Is.EqualTo(
                        CoCoDiagnosticCode.PoolTemporalEntityUnavailable));
                Assert.That(historical.ApplyProbe.ApplyCount, Is.Zero);
                PoolEntrySnapshot after =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(after.TemporalRetainedCount, Is.Zero);
                Assert.That(after.QuarantineCount, Is.Zero);
                Assert.That(after.InactiveCount, Is.EqualTo(1));
                Assert.That(historical.Instance.activeSelf, Is.False);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunAuthorityResetPendingActivationAsync()
        {
            TemporalPoolingFixture fixture = await CreateFixtureAsync();
            try
            {
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
                instance.transform.SetParent(
                    fixture.HostScenario.GameObject.transform,
                    false);
                PoolTemporalApplyProbe applyProbe =
                    instance.GetComponent<PoolTemporalApplyProbe>();
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xABCDUL,
                        0xA073UL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Step(fixture.HostScenario, 10);
                PoolEntrySnapshot before =
                    fixture.Scope.CaptureSnapshot().Entries.Single();

                var participant =
                    (ICoCoStateGraphTemporalParticipant)fixture.Binding;
                Assert.That(
                    participant.TryPrepareAuthorityReset(
                        fixture.HostScenario.Host.TemporalState.Current,
                        out CoCoDiagnostic prepared),
                    Is.True,
                    prepared.Message);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic frozen),
                    Is.False);
                Assert.That(
                    frozen.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalConflict));

                participant.CommitPreparedAuthorityResetNoFail();
                participant.DrainPublishedCleanupNoFail();

                Assert.That(GetSidecarCount(fixture.Binding), Is.EqualTo(1));
                AssertPoolOccupancyEqual(
                    before,
                    fixture.Scope.CaptureSnapshot().Entries.Single());
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out GameObject active,
                        out CoCoDiagnostic activeDiagnostic),
                    Is.True,
                    activeDiagnostic.Message);
                Assert.That(active, Is.SameAs(instance));
                Assert.That(active.activeInHierarchy, Is.True);
                Assert.That(applyProbe.ApplyCount, Is.Zero);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask<TemporalPoolingFixture> CreateFixtureAsync(
            bool useProbeDownstream = false)
        {
            TemporalHostTestScenario hostScenario =
                TemporalHostTestHarness.Create(historyCapacity: 3);
            _objects.Add(hostScenario.Asset);
            _objects.Add(hostScenario.GameObject);

            var poolObject = new GameObject("Pre9 Temporal Pool Hosts");
            poolObject.SetActive(false);
            poolObject.transform.SetParent(hostScenario.GameObject.transform, false);
            CoCoContentHost contentHost =
                poolObject.AddComponent<CoCoContentHost>();
            CoCoPoolHost poolHost =
                poolObject.AddComponent<CoCoPoolHost>();
            SetField(poolHost, "contentHost", contentHost);
            poolObject.SetActive(true);
            Assert.That(poolHost.IsInitialized, Is.True, poolHost.LastDiagnostic.Message);

            CoCoPoolTemporalBinding binding =
                hostScenario.GameObject.AddComponent<CoCoPoolTemporalBinding>();
            PoolDownstreamRestoreProbe downstreamProbe = null;
            MonoBehaviour downstream = hostScenario.Binding;
            if (useProbeDownstream)
            {
                var downstreamObject =
                    new GameObject("Pre9 Pool Temporal Downstream Probe");
                downstreamObject.transform.SetParent(
                    hostScenario.GameObject.transform,
                    false);
                downstreamProbe =
                    downstreamObject.AddComponent<PoolDownstreamRestoreProbe>();
                _objects.Add(downstreamObject);
                downstream = downstreamProbe;
            }

            SetField(binding, "stateGraphHost", hostScenario.Host);
            SetField(binding, "poolHost", poolHost);
            SetField(binding, "downstreamRestoreBinding", downstream);
            TemporalHostTestHarness.SetRestoreBinding(hostScenario.Host, binding);
            Assert.That(
                hostScenario.Host.TryStart(out CoCoDiagnostic start),
                Is.True,
                start.Message);

            Assert.That(
                ContentOwnerId.TryCreate(
                    "tests.pooling.temporal." + Guid.NewGuid().ToString("N"),
                    out ContentOwnerId ownerId),
                Is.True);
            Assert.That(
                poolHost.TryCreateScope(
                    ownerId,
                    out PoolScope scope,
                    out CoCoDiagnostic scopeDiagnostic),
                Is.True,
                scopeDiagnostic.Message);
            var prefab = new GameObject("Pre9 Temporal Pool Prefab");
            prefab.SetActive(false);
            prefab.AddComponent<PoolTemporalApplyProbe>();
            if (useProbeDownstream)
            {
                prefab.AddComponent<PoolTemporalDespawnReentryProbe>();
            }

            _objects.Add(prefab);
            string suffix = Guid.NewGuid().ToString("N");
            Assert.That(
                ContentId.TryCreate(
                    "tests.pooling.temporal.prefab." + suffix,
                    out ContentId contentId),
                Is.True);
            Assert.That(
                ContentReference.TryCreateDirectPrefabSource(
                    contentId,
                    prefab,
                    out ContentReference source),
                Is.True);
            Assert.That(
                PoolId.TryCreate(
                    "tests.pooling.temporal.pool." + suffix,
                    out PoolId poolId),
                Is.True);
            Assert.That(
                PoolProfile.TryCreate(
                    poolId,
                    source,
                    0,
                    1,
                    out PoolProfile profile),
                Is.True);
            PoolPrepareResult prepared = await scope.PrepareAsync(profile);
            Assert.That(prepared.Succeeded, Is.True, prepared.Diagnostic.Message);
            return new TemporalPoolingFixture(
                hostScenario,
                contentHost,
                poolHost,
                binding,
                scope,
                profile,
                downstreamProbe);
        }

        private static async UniTask CleanupFixtureAsync(
            TemporalPoolingFixture fixture)
        {
            if (fixture == null)
            {
                return;
            }

            fixture.HostScenario.Host.TryStop(out _);
            if (fixture.Scope.State != PoolScopeState.Closed)
            {
                await fixture.Scope.CloseAsync();
            }

            await fixture.PoolHost.ShutdownAsync();
            await fixture.ContentHost.ShutdownAsync();
        }

        private static void Step(
            TemporalHostTestScenario scenario,
            int actorValue)
        {
            scenario.Binding.Value = actorValue;
            Assert.That(
                scenario.Host.TryStep(0.1d, out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Not.Null,
                target.GetType().FullName + "." + fieldName);
            field.SetValue(target, value);
        }

        private static int GetSidecarCount(
            CoCoPoolTemporalBinding binding)
        {
            FieldInfo runtimeField = typeof(CoCoPoolTemporalBinding).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(runtimeField, Is.Not.Null);
            var runtime =
                (PoolTemporalRuntime)runtimeField.GetValue(binding);
            Assert.That(runtime, Is.Not.Null);

            FieldInfo historyField = typeof(PoolTemporalRuntime).GetField(
                "_history",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(historyField, Is.Not.Null);
            var history =
                (PoolTemporalSidecar)historyField.GetValue(runtime);
            Assert.That(history, Is.Not.Null);
            return history.Count;
        }

        private static void AssertPoolOccupancyEqual(
            in PoolEntrySnapshot expected,
            in PoolEntrySnapshot actual)
        {
            Assert.That(actual.ActiveCount, Is.EqualTo(expected.ActiveCount));
            Assert.That(actual.InactiveCount, Is.EqualTo(expected.InactiveCount));
            Assert.That(
                actual.TemporalRetainedCount,
                Is.EqualTo(expected.TemporalRetainedCount));
            Assert.That(
                actual.QuarantineCount,
                Is.EqualTo(expected.QuarantineCount));
            Assert.That(
                actual.PendingDestroyCount,
                Is.EqualTo(expected.PendingDestroyCount));
            Assert.That(actual.CreatedCount, Is.EqualTo(expected.CreatedCount));
            Assert.That(actual.DestroyedCount, Is.EqualTo(expected.DestroyedCount));
            Assert.That(
                actual.ExternalDestroyCount,
                Is.EqualTo(expected.ExternalDestroyCount));
        }

        private enum DownstreamPreflightFailure
        {
            Destroy = 0,
            Replace = 1,
            MoveOutside = 2
        }

        private sealed class RetainedTemporalEntity
        {
            internal RetainedTemporalEntity(
                CoCoTemporalEntityId entityId,
                GameObject instance,
                PoolTemporalApplyProbe applyProbe)
            {
                EntityId = entityId;
                Instance = instance;
                ApplyProbe = applyProbe;
            }

            internal CoCoTemporalEntityId EntityId { get; }
            internal GameObject Instance { get; }
            internal PoolTemporalApplyProbe ApplyProbe { get; }
        }

        private sealed class TemporalPoolingFixture
        {
            internal TemporalPoolingFixture(
                TemporalHostTestScenario hostScenario,
                CoCoContentHost contentHost,
                CoCoPoolHost poolHost,
                CoCoPoolTemporalBinding binding,
                PoolScope scope,
                PoolProfile profile,
                PoolDownstreamRestoreProbe downstreamProbe)
            {
                HostScenario = hostScenario;
                ContentHost = contentHost;
                PoolHost = poolHost;
                Binding = binding;
                Scope = scope;
                Profile = profile;
                DownstreamProbe = downstreamProbe;
            }

            internal TemporalHostTestScenario HostScenario { get; }
            internal CoCoContentHost ContentHost { get; }
            internal CoCoPoolHost PoolHost { get; }
            internal CoCoPoolTemporalBinding Binding { get; }
            internal PoolScope Scope { get; }
            internal PoolProfile Profile { get; }
            internal PoolDownstreamRestoreProbe DownstreamProbe { get; }
        }
    }

    internal enum PoolDownstreamFailure
    {
        None = 0,
        Reject = 1,
        Throw = 2,
        MoveOutside = 3
    }

    internal sealed class PoolDownstreamRestoreProbe :
        MonoBehaviour,
        ICoCoContextRestoreBinding
    {
        internal PoolDownstreamFailure Failure { get; set; }
        internal int ApplyCount { get; private set; }

        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            _ = context;
            ApplyCount++;
            switch (Failure)
            {
                case PoolDownstreamFailure.Reject:
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Pooling,
                        CoCoDiagnosticCode.PoolTemporalProjectionFailed,
                        "The Pool downstream test probe rejected projection.");
                    return false;
                case PoolDownstreamFailure.Throw:
                    throw new InvalidOperationException(
                        "The Pool downstream test probe threw during projection.");
                case PoolDownstreamFailure.MoveOutside:
                    transform.SetParent(null, false);
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                default:
                    diagnostic = CoCoDiagnostic.None;
                    return true;
            }
        }
    }

    internal sealed class PoolTemporalApplyProbe :
        MonoBehaviour,
        IPoolTemporalApply
    {
        internal static void Reset()
        {
        }

        internal TemporalActorRestoreBinding ObservedBinding { get; set; }
        internal int ApplyCount { get; private set; }
        internal int ObservedValueDuringApply { get; private set; }
        internal PoolTemporalApplyContext LastContext { get; private set; }

        public bool TryApply(
            in PoolTemporalApplyContext context,
            out CoCoDiagnostic diagnostic)
        {
            ApplyCount++;
            LastContext = context;
            ObservedValueDuringApply = ObservedBinding == null
                ? int.MinValue
                : ObservedBinding.LastAppliedValue;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }
}
