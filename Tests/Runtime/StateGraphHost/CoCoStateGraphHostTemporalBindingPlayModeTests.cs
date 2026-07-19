using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostTemporalBindingPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
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
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
        }

        [Test]
        public void EnabledHistoryRequiresOneLiveExplicitRestoreBinding()
        {
            TemporalHostTestScenario missing = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    assignRestoreBinding: false));

            Assert.That(missing.Host.TryStart(out CoCoDiagnostic missingBinding), Is.False);
            Assert.That(missingBinding.IsError, Is.True);
            Assert.That(
                missing.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(missing.Host.CurrentContext.IsAlive, Is.False);

            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostTestScenario destroyed = Track(
                TemporalHostTestHarness.Create(historyCapacity: 3));
            var restoreOnly = destroyed.GameObject.AddComponent<TemporalActorRestoreBinding>();
            restoreOnly.Configure(destroyed.Ids.ActorStateSlotId);
            TemporalHostTestHarness.SetRestoreBinding(destroyed.Host, restoreOnly);
            Object.DestroyImmediate(restoreOnly);

            Assert.That(destroyed.Host.TryStart(out CoCoDiagnostic destroyedBinding), Is.False);
            Assert.That(destroyedBinding.IsError, Is.True);
            Assert.That(
                destroyed.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(destroyed.Host.CurrentContext.IsAlive, Is.False);
        }

        [Test]
        public void DisabledHistoryIgnoresWrongTypeRestoreBindingAndDoesNotRetainIt()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 0));
            TemporalHostTestHarness.SetRestoreBinding(scenario.Host, scenario.Host);

            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);

            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            AssertDisabledControllerDoesNotRetainBinding(scenario.Host);
        }

        [Test]
        public void DisabledHistoryIgnoresRestoreBindingOutsideHostBoundary()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 0));
            TemporalActorRestoreBinding outsideBinding = CreateOutsideBinding(scenario);
            TemporalHostTestHarness.SetRestoreBinding(scenario.Host, outsideBinding);

            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);

            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            AssertDisabledControllerDoesNotRetainBinding(scenario.Host);
        }

        [Test]
        public void EnabledHistoryRejectsWrongTypeRestoreBinding()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 3));
            TemporalHostTestHarness.SetRestoreBinding(scenario.Host, scenario.Host);

            Assert.That(scenario.Host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(scenario.Host.CurrentContext.IsAlive, Is.False);
        }

        [Test]
        public void EnabledHistoryRejectsRestoreBindingOutsideHostBoundary()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 3));
            TemporalActorRestoreBinding outsideBinding = CreateOutsideBinding(scenario);
            TemporalHostTestHarness.SetRestoreBinding(scenario.Host, outsideBinding);

            Assert.That(scenario.Host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(scenario.Host.CurrentContext.IsAlive, Is.False);
        }

        [TestCase(TemporalRestoreFixtureFailure.Reject)]
        [TestCase(TemporalRestoreFixtureFailure.Throw)]
        public void ConfirmBindingFailurePreservesAuthorityAndRequiresCorrection(
            TemporalRestoreFixtureFailure failureMode)
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            CoCoContextFrame authority = scenario.Host.CurrentContext;
            CoCoTickFrame authorityTick = authority.Header.TickFrame;
            int traceCount = CopyTraceCount(scenario.Host);

            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            scenario.Binding.Failure = failureMode;
            scenario.Binding.MutateBeforeFailure = true;

            bool confirmed = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() =>
                confirmed = scenario.Host.TryConfirmTemporalRestore(out failure));

            Assert.That(confirmed, Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(
                scenario.Host.CurrentContext.Header.TickFrame,
                Is.EqualTo(authorityTick));
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.EqualTo(1));
            Assert.That(CopyTraceCount(scenario.Host), Is.EqualTo(traceCount));
            AssertTraceHasNoCommittedOutputAfter(scenario.Host, authorityTick.Tick.Value);

            scenario.Binding.Failure = TemporalRestoreFixtureFailure.None;
            scenario.Binding.MutateBeforeFailure = false;
            Require(
                scenario.Host.TryCorrectWorld(out CoCoDiagnostic correction),
                correction);
            Assert.That(scenario.Binding.CorrectionCount, Is.EqualTo(1));
            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(20));
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
        }

        [Test]
        public void DestroyedBindingDuringConfirmCannotPublishRestore()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            CoCoContextFrame authority = scenario.Host.CurrentContext;
            int traceCount = CopyTraceCount(scenario.Host);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            scenario.Binding.Failure = TemporalRestoreFixtureFailure.Destroy;

            Assert.That(
                scenario.Host.TryConfirmTemporalRestore(out CoCoDiagnostic failure),
                Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(scenario.Binding == null, Is.True);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(CopyTraceCount(scenario.Host), Is.EqualTo(traceCount));

            Require(scenario.Host.TryStop(out CoCoDiagnostic stop), stop);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Stopped));
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Disabled));
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
        }

        [Test]
        public void RestoreCallbackCannotReenterAndReaderExpiresOnReturn()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            bool nestedSucceeded = true;
            CoCoDiagnostic nestedDiagnostic = default;
            scenario.Binding.ApplyCallback = kind =>
            {
                if (kind == CoCoContextRestoreApplyKind.Preview)
                {
                    nestedSucceeded = scenario.Host.TryCancelTemporalPreview(
                        out nestedDiagnostic);
                }
            };

            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);

            Assert.That(nestedSucceeded, Is.False);
            Assert.That(nestedDiagnostic.IsError, Is.True);
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.EqualTo(1));
            Assert.That(scenario.Binding.EscapedReader.IsValid, Is.False);
            Assert.That(
                scenario.Binding.EscapedReader.TryRead(
                    scenario.Ids.ActorStateSlotId,
                    out int escapedValue),
                Is.False);
            Assert.That(escapedValue, Is.Zero);

            scenario.Binding.ApplyCallback = null;
            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
        }

        [Test]
        public void StopDisposeAndRestartClearHistoryAndOldReaders()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 3));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            CoCoContextRestoreReader escaped = scenario.Binding.EscapedReader;

            Require(scenario.Host.TryStop(out CoCoDiagnostic stop), stop);
            Assert.That(escaped.IsValid, Is.False);
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Disabled));

            Require(scenario.Host.TryStart(out CoCoDiagnostic restart), restart);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
            StepWithActorValue(scenario, 30);
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(1));
            Require(scenario.Host.TryStop(out CoCoDiagnostic secondStop), secondStop);
            Require(scenario.Host.TryDispose(out CoCoDiagnostic dispose), dispose);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Disposed));
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
        }

        [Test]
        public void EscapedReaderDetachesAndDoesNotRetainStoppedTemporalController()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 3));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            CoCoContextRestoreReader escaped = scenario.Binding.EscapedReader;

            Assert.That(escaped.IsValid, Is.False);
            Assert.That(
                escaped.TryRead(
                    scenario.Ids.ActorStateSlotId,
                    out int escapedValue),
                Is.False);
            Assert.That(escapedValue, Is.Zero);

            FieldInfo readerSource = typeof(CoCoContextRestoreReader).GetField(
                "_source",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(readerSource, Is.Not.Null);
            object lease = readerSource.GetValue(escaped);
            Assert.That(lease, Is.Not.Null);
            Assert.That(lease, Is.Not.TypeOf<CoCoStateGraphTemporalController>());

            FieldInfo leaseSource = lease.GetType().GetField(
                "_source",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(leaseSource, Is.Not.Null);
            Assert.That(leaseSource.GetValue(lease), Is.Null);

            WeakReference controller = CaptureControllerAndStop(scenario);
            Assert.That(leaseSource.GetValue(lease), Is.Null);
            CollectGarbage();
            Assert.That(
                controller.IsAlive,
                Is.False,
                "An escaped Reader must retain only its detached lease, not the stopped Host object graph.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CaptureControllerAndStop(
            TemporalHostTestScenario scenario)
        {
            FieldInfo temporalField = typeof(CoCoStateGraphHost).GetField(
                "_temporal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(temporalField, Is.Not.Null);
            object controller = temporalField.GetValue(scenario.Host);
            Assert.That(controller, Is.Not.Null);
            var reference = new WeakReference(controller);

            Require(scenario.Host.TryStop(out CoCoDiagnostic stop), stop);
            Assert.That(temporalField.GetValue(scenario.Host), Is.Null);
            return reference;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private TemporalHostTestScenario Track(TemporalHostTestScenario scenario)
        {
            _objects.Add(scenario.Asset);
            _objects.Add(scenario.GameObject);
            return scenario;
        }

        private TemporalActorRestoreBinding CreateOutsideBinding(
            TemporalHostTestScenario scenario)
        {
            var outside = new GameObject("Pre6 Temporal Outside Restore Binding");
            _objects.Add(outside);
            var binding = outside.AddComponent<TemporalActorRestoreBinding>();
            binding.Configure(scenario.Ids.ActorStateSlotId);
            return binding;
        }

        private static void AssertDisabledControllerDoesNotRetainBinding(
            CoCoStateGraphHost host)
        {
            Assert.That(
                host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Disabled));
            FieldInfo temporalField = typeof(CoCoStateGraphHost).GetField(
                "_temporal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(temporalField, Is.Not.Null);
            object controller = temporalField.GetValue(host);
            Assert.That(controller, Is.Not.Null);

            FieldInfo componentField = controller.GetType().GetField(
                "_bindingComponent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo bindingField = controller.GetType().GetField(
                "_binding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(componentField, Is.Not.Null);
            Assert.That(bindingField, Is.Not.Null);
            Assert.That(componentField.GetValue(controller), Is.Null);
            Assert.That(bindingField.GetValue(controller), Is.Null);
        }

        private static void StepWithActorValue(
            TemporalHostTestScenario scenario,
            int value)
        {
            scenario.Binding.Value = value;
            Require(
                scenario.Host.TryStep(0.1d, out CoCoDiagnostic diagnostic),
                diagnostic);
        }

        private static int CopyTraceCount(CoCoStateGraphHost host)
        {
            var entries = new CoCoStateFlowTraceEntry[64];
            return host.Trace.CopyLatestTo(entries);
        }

        private static void AssertTraceHasNoCommittedOutputAfter(
            CoCoStateGraphHost host,
            ulong lastCommittedTick)
        {
            var entries = new CoCoStateFlowTraceEntry[64];
            int count = host.Trace.CopyLatestTo(entries);
            for (int index = 0; index < count; index++)
            {
                CoCoStateFlowTraceEntry entry = entries[index];
                if (entry.TickFrame.Tick.Value <= lastCommittedTick)
                {
                    continue;
                }

                Assert.That(
                    entry.Kind,
                    Is.Not.EqualTo(CoCoStateFlowTraceKind.ContextCommit));
                Assert.That(
                    entry.Kind,
                    Is.Not.EqualTo(CoCoStateFlowTraceKind.EventSequence));
                Assert.That(
                    entry.Kind,
                    Is.Not.EqualTo(CoCoStateFlowTraceKind.EventPublished));
            }
        }

        private static void Require(
            bool succeeded,
            CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }
    }
}
