using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using CoCoFlow.Tests.Runtime.StateGraphHost;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Runtime.Pooling.Temporal.Tests
{
    public sealed class PoolTemporalAdversarialReentryPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            StateGraphHostPoolingTestBridge.ResetProjectBindings();
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
        }

        [UnityTest]
        public IEnumerator TemporalActivationRejectsNestedDespawnWithoutLosingEntity() =>
            UniTask.ToCoroutine(RunTemporalActivationReentryAsync);

        [UnityTest]
        public IEnumerator TemporalResetHostStopRejectsDespawnAndDrainsDestroyPending() =>
            UniTask.ToCoroutine(RunTemporalResetHostStopAsync);

        [UnityTest]
        public IEnumerator ExternalDestroyWithoutResolveFaultsNextHostStep() =>
            UniTask.ToCoroutine(RunExternalDestroyBeforeHostStepAsync);

        [UnityTest]
        public IEnumerator TemporalActivationWithoutConsumerReparentIsRejectedAndDestroyed() =>
            UniTask.ToCoroutine(RunTemporalActivationWithoutReparentAsync);

        [UnityTest]
        public IEnumerator PendingActivationDespawnIsRejectedAndHostStopIsCallbackFree() =>
            UniTask.ToCoroutine(RunPendingActivationLifecycleAsync);

        [UnityTest]
        public IEnumerator HostStopReleasesActiveTemporalEntityWithOneReturn() =>
            UniTask.ToCoroutine(RunActiveHostStopLifecycleAsync);

        [UnityTest]
        public IEnumerator DestroyedPhysicalAfterAdoptFailsActivationWithoutThrowing() =>
            UniTask.ToCoroutine(RunDestroyedPendingActivationAsync);

        private async UniTask RunTemporalActivationReentryAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                        0xA11CEUL,
                        0xBEEFUL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                PoolTemporalDespawnReentryProbe probe =
                    instance.GetComponent<PoolTemporalDespawnReentryProbe>();
                probe.Arm(fixture.Binding, entityId);

                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);
                Assert.That(handle.IsValid, Is.False);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic activated),
                    Is.True,
                    activated.Message);

                Assert.That(probe.Attempted, Is.True);
                Assert.That(
                    probe.NestedDespawnSucceeded,
                    Is.False,
                    "A lifecycle callback must not mutate the same Temporal entity during activation.");
                Assert.That(probe.NestedDiagnostic.IsError, Is.True);
                Assert.That(
                    probe.NestedDiagnostic.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalConflict)
                        .Or.EqualTo(CoCoDiagnosticCode.PoolCallbackReentry));
                Assert.That(
                    fixture.Binding.TryResolveInstance(
                        entityId,
                        out GameObject resolved,
                        out CoCoDiagnostic resolvedDiagnostic),
                    Is.True,
                    resolvedDiagnostic.Message);
                Assert.That(resolved, Is.SameAs(instance));
                Assert.That(resolved.activeInHierarchy, Is.True);

                PoolEntrySnapshot snapshot =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(snapshot.ActiveCount, Is.Zero);
                Assert.That(snapshot.InactiveCount, Is.Zero);
                Assert.That(snapshot.TemporalRetainedCount, Is.EqualTo(1));
                Assert.That(snapshot.QuarantineCount, Is.Zero);
                Assert.That(snapshot.PendingDestroyCount, Is.Zero);
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

        private async UniTask RunTemporalResetHostStopAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                        0xA11CEUL,
                        0xCAFEUL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                PoolTemporalDespawnReentryProbe probe =
                    instance.GetComponent<PoolTemporalDespawnReentryProbe>();

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
                probe.ArmHostStopOnReturn(fixture.Binding);

                bool despawned = fixture.Binding.TryDespawn(
                    entityId,
                    out CoCoDiagnostic despawnDiagnostic);
                Assert.That(probe.ReturnAttempted, Is.True);
                Assert.That(
                    probe.HostStopSucceeded,
                    Is.True,
                    probe.HostStopDiagnostic.Message);
                Assert.That(
                    despawned,
                    Is.False,
                    "TryDespawn must not publish success after reset detaches its Temporal Host.");
                Assert.That(despawnDiagnostic.IsError, Is.True);

                PoolEntrySnapshot pending =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(pending.ActiveCount, Is.Zero);
                Assert.That(pending.InactiveCount, Is.Zero);
                Assert.That(pending.TemporalRetainedCount, Is.Zero);
                Assert.That(
                    pending.QuarantineCount,
                    Is.Zero,
                    "A terminally detached record must never be replayed into quarantine.");
                Assert.That(
                    pending.PendingDestroyCount,
                    Is.EqualTo(1),
                    "Terminal detach must leave exactly one physical destroy barrier.");

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
                    "Scope close must drain the terminal destroy barrier.");
                CoCoDiagnostic closed = await closing;
                Assert.That(closed.IsNone, Is.True, closed.Message);
                Assert.That(fixture.PoolHost.Runtime.CaptureSnapshot().Scopes, Is.Empty);
            }
            finally
            {
                if (fixture != null &&
                    fixture.Scope.State != PoolScopeState.Closed &&
                    fixture.PoolHost.Runtime != null &&
                    !fixture.PoolHost.Runtime.IsDisposed)
                {
                    fixture.PoolHost.Runtime.ForceShutdown();
                    await UniTask.NextFrame();
                }

                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunExternalDestroyBeforeHostStepAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                        0xA11CEUL,
                        0xD15EA5EUL,
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
                Assert.That(instance == null, Is.True);

                bool stepped = fixture.HostScenario.Host.TryStep(
                    0.1d,
                    out CoCoDiagnostic stepDiagnostic);
                Assert.That(
                    stepped,
                    Is.False,
                    "Forward capture must validate retained Temporal physical identity.");
                Assert.That(stepDiagnostic.IsError, Is.True);
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

        private async UniTask RunTemporalActivationWithoutReparentAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                Assert.That(instance.activeInHierarchy, Is.False);
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xA11CEUL,
                        0x1A071EUL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);

                bool activated = fixture.Binding.TryActivate(
                    entityId,
                    out CoCoDiagnostic activationDiagnostic);
                Assert.That(
                    activated,
                    Is.False,
                    "Temporal consumers must reparent out of the inactive retention root.");
                Assert.That(activationDiagnostic.IsError, Is.True);
                PoolEntrySnapshot pending =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(pending.ActiveCount, Is.Zero);
                Assert.That(pending.InactiveCount, Is.Zero);
                Assert.That(pending.TemporalRetainedCount, Is.Zero);
                Assert.That(pending.QuarantineCount, Is.Zero);
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
                Assert.That(fixture.PoolHost.Runtime.CaptureSnapshot().Scopes, Is.Empty);
            }
            finally
            {
                if (fixture != null &&
                    fixture.Scope.State != PoolScopeState.Closed &&
                    fixture.PoolHost.Runtime != null &&
                    !fixture.PoolHost.Runtime.IsDisposed)
                {
                    fixture.PoolHost.Runtime.ForceShutdown();
                    await UniTask.NextFrame();
                }

                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunPendingActivationLifecycleAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                PoolTemporalDespawnReentryProbe probe =
                    instance.GetComponent<PoolTemporalDespawnReentryProbe>();
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xA11CEUL,
                        0x0EADUL,
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
                    fixture.Binding.TryDespawn(
                        entityId,
                        out CoCoDiagnostic rejected),
                    Is.False);
                Assert.That(
                    rejected.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalConflict));
                Assert.That(probe.RentCount, Is.Zero);
                Assert.That(probe.ReturnCount, Is.Zero);
                PoolEntrySnapshot retained =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(retained.TemporalRetainedCount, Is.EqualTo(1));
                Assert.That(retained.QuarantineCount, Is.Zero);

                Assert.That(
                    fixture.HostScenario.Host.TryStop(
                        out CoCoDiagnostic stopped),
                    Is.True,
                    stopped.Message);
                Assert.That(probe.RentCount, Is.Zero);
                Assert.That(
                    probe.ReturnCount,
                    Is.Zero,
                    "A pending activation has not received Rent and must not receive Return.");
                PoolEntrySnapshot released =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(released.TemporalRetainedCount, Is.Zero);
                Assert.That(released.QuarantineCount, Is.Zero);
                Assert.That(released.InactiveCount, Is.EqualTo(1));
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunActiveHostStopLifecycleAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                PoolTemporalDespawnReentryProbe probe =
                    instance.GetComponent<PoolTemporalDespawnReentryProbe>();
                Assert.That(
                    CoCoTemporalEntityId.TryCreate(
                        0xA11CEUL,
                        0xA071EUL,
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
                Assert.That(probe.RentCount, Is.EqualTo(1));
                Assert.That(probe.ReturnCount, Is.Zero);

                Assert.That(
                    fixture.HostScenario.Host.TryStop(
                        out CoCoDiagnostic stopped),
                    Is.True,
                    stopped.Message);
                Assert.That(probe.RentCount, Is.EqualTo(1));
                Assert.That(probe.ReturnCount, Is.EqualTo(1));
                Assert.That(
                    probe.LastReturnReason,
                    Is.EqualTo(PoolReturnReason.TemporalRelease));
                PoolEntrySnapshot released =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(released.TemporalRetainedCount, Is.Zero);
                Assert.That(released.QuarantineCount, Is.Zero);
                Assert.That(released.PendingDestroyCount, Is.Zero);
                Assert.That(released.InactiveCount, Is.EqualTo(1));
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask RunDestroyedPendingActivationAsync()
        {
            TemporalReentryFixture fixture = await CreateFixtureAsync();
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
                        0xA11CEUL,
                        0xDEADUL,
                        out CoCoTemporalEntityId entityId),
                    Is.True);
                Assert.That(
                    fixture.Binding.TryAdopt(
                        entityId,
                        ref handle,
                        out CoCoDiagnostic adopted),
                    Is.True,
                    adopted.Message);

                Object.Destroy(instance);
                await UniTask.NextFrame();
                Assert.That(instance == null, Is.True);
                Assert.That(
                    fixture.Binding.TryActivate(
                        entityId,
                        out CoCoDiagnostic unavailable),
                    Is.False);
                Assert.That(unavailable.IsError, Is.True);
                Assert.That(
                    unavailable.Code,
                    Is.EqualTo(CoCoDiagnosticCode.PoolTemporalEntityUnavailable)
                        .Or.EqualTo(CoCoDiagnosticCode.PooledInstanceDestroyed));
                for (int frame = 0; frame < 20; frame++)
                {
                    PoolEntrySnapshot pending =
                        fixture.Scope.CaptureSnapshot().Entries.Single();
                    if (pending.TemporalRetainedCount == 0 &&
                        pending.PendingDestroyCount == 0)
                    {
                        break;
                    }

                    await UniTask.NextFrame();
                }

                PoolEntrySnapshot terminal =
                    fixture.Scope.CaptureSnapshot().Entries.Single();
                Assert.That(terminal.TemporalRetainedCount, Is.Zero);
                Assert.That(terminal.PendingDestroyCount, Is.Zero);
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private async UniTask<TemporalReentryFixture> CreateFixtureAsync()
        {
            TemporalHostTestScenario hostScenario =
                TemporalHostTestHarness.Create(historyCapacity: 3);
            _objects.Add(hostScenario.Asset);
            _objects.Add(hostScenario.GameObject);

            var poolObject = new GameObject("Pre9 Temporal Reentry Pool Hosts");
            poolObject.SetActive(false);
            poolObject.transform.SetParent(hostScenario.GameObject.transform, false);
            CoCoContentHost contentHost =
                poolObject.AddComponent<CoCoContentHost>();
            CoCoPoolHost poolHost =
                poolObject.AddComponent<CoCoPoolHost>();
            SetField(poolHost, "contentHost", contentHost);
            poolObject.SetActive(true);
            Assert.That(
                poolHost.IsInitialized,
                Is.True,
                poolHost.LastDiagnostic.Message);

            CoCoPoolTemporalBinding binding =
                hostScenario.GameObject.AddComponent<CoCoPoolTemporalBinding>();
            SetField(binding, "stateGraphHost", hostScenario.Host);
            SetField(binding, "poolHost", poolHost);
            SetField(binding, "downstreamRestoreBinding", hostScenario.Binding);
            TemporalHostTestHarness.SetRestoreBinding(hostScenario.Host, binding);
            Assert.That(
                hostScenario.Host.TryStart(out CoCoDiagnostic start),
                Is.True,
                start.Message);

            string suffix = Guid.NewGuid().ToString("N");
            Assert.That(
                ContentOwnerId.TryCreate(
                    "tests.pooling.temporal.reentry." + suffix,
                    out ContentOwnerId ownerId),
                Is.True);
            Assert.That(
                poolHost.TryCreateScope(
                    ownerId,
                    out PoolScope scope,
                    out CoCoDiagnostic scopeDiagnostic),
                Is.True,
                scopeDiagnostic.Message);

            var prefab = new GameObject("Pre9 Temporal Reentry Pool Prefab");
            prefab.SetActive(false);
            prefab.AddComponent<PoolTemporalDespawnReentryProbe>();
            _objects.Add(prefab);
            Assert.That(
                ContentId.TryCreate(
                    "tests.pooling.temporal.reentry.prefab." + suffix,
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
                    "tests.pooling.temporal.reentry.pool." + suffix,
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
            return new TemporalReentryFixture(
                hostScenario,
                contentHost,
                poolHost,
                binding,
                scope,
                profile);
        }

        private static async UniTask CleanupFixtureAsync(
            TemporalReentryFixture fixture)
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

        private sealed class TemporalReentryFixture
        {
            internal TemporalReentryFixture(
                TemporalHostTestScenario hostScenario,
                CoCoContentHost contentHost,
                CoCoPoolHost poolHost,
                CoCoPoolTemporalBinding binding,
                PoolScope scope,
                PoolProfile profile)
            {
                HostScenario = hostScenario;
                ContentHost = contentHost;
                PoolHost = poolHost;
                Binding = binding;
                Scope = scope;
                Profile = profile;
            }

            internal TemporalHostTestScenario HostScenario { get; }
            internal CoCoContentHost ContentHost { get; }
            internal CoCoPoolHost PoolHost { get; }
            internal CoCoPoolTemporalBinding Binding { get; }
            internal PoolScope Scope { get; }
            internal PoolProfile Profile { get; }
        }
    }

    internal sealed class PoolTemporalDespawnReentryProbe :
        MonoBehaviour,
        IPoolable
    {
        private CoCoPoolTemporalBinding _binding;
        private CoCoTemporalEntityId _entityId;
        private bool _despawnOnRentArmed;
        private bool _stopHostOnReturnArmed;

        internal bool Attempted { get; private set; }
        internal bool NestedDespawnSucceeded { get; private set; }
        internal CoCoDiagnostic NestedDiagnostic { get; private set; }
        internal bool ReturnAttempted { get; private set; }
        internal bool HostStopSucceeded { get; private set; }
        internal CoCoDiagnostic HostStopDiagnostic { get; private set; }
        internal int RentCount { get; private set; }
        internal int ReturnCount { get; private set; }
        internal PoolReturnReason LastReturnReason { get; private set; }

        internal void Arm(
            CoCoPoolTemporalBinding binding,
            CoCoTemporalEntityId entityId)
        {
            _binding = binding;
            _entityId = entityId;
            _despawnOnRentArmed = true;
        }

        internal void ArmHostStopOnReturn(
            CoCoPoolTemporalBinding binding)
        {
            _binding = binding;
            _stopHostOnReturnArmed = true;
        }

        public bool TryOnRent(
            in PoolRentContext context,
            out CoCoDiagnostic diagnostic)
        {
            RentCount++;
            if (_despawnOnRentArmed)
            {
                _despawnOnRentArmed = false;
                Attempted = true;
                NestedDespawnSucceeded =
                    _binding.TryDespawn(_entityId, out CoCoDiagnostic nested);
                NestedDiagnostic = nested;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryOnReturn(
            in PoolReturnContext context,
            out CoCoDiagnostic diagnostic)
        {
            ReturnCount++;
            LastReturnReason = context.Reason;
            if (_stopHostOnReturnArmed)
            {
                _stopHostOnReturnArmed = false;
                ReturnAttempted = true;
                HostStopSucceeded = _binding.StateGraphHost.TryStop(
                    out CoCoDiagnostic stopped);
                HostStopDiagnostic = stopped;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }
}
