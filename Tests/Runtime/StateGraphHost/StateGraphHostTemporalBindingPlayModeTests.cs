using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    public sealed class StateGraphHostTemporalBindingPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            HostProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
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
            HostProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
        }

        [Test]
        public void EnabledHistoryAllowsNoBindingButRejectsDestroyedExplicitBinding()
        {
            TemporalHostTestScenario missing = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    assignRestoreBinding: false));

            Assert.That(missing.Host.TryStart(out CoCoDiagnostic missingBinding), Is.True, missingBinding.Message);
            Assert.That(missingBinding.IsError, Is.False);
            Assert.That(
                missing.Host.Lifecycle,
                Is.EqualTo(RuntimeLifecycleState.Running));
            Assert.That(missing.Host.CurrentContext.IsAlive, Is.False);

            HostProjectBindings.ResetForTests();
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
                Is.EqualTo(RuntimeLifecycleState.Created));
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
                Is.EqualTo(RuntimeLifecycleState.Running));
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
                Is.EqualTo(RuntimeLifecycleState.Running));
            AssertDisabledControllerDoesNotRetainBinding(scenario.Host);
        }

        [Test]
        public void DisabledHistoryIgnoresDestroyedRestoreBindingAndDoesNotRetainIt()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 0));
            TemporalActorRestoreBinding restoreOnly = CreateRestoreOnlyBinding(scenario);
            Object.DestroyImmediate(restoreOnly);

            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);

            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(RuntimeLifecycleState.Running));
            AssertDisabledControllerDoesNotRetainBinding(scenario.Host);
        }

        [Test]
        public void DisabledHistoryRetainsValidRestoreBindingForWorldCorrection()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 0));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            AssertDisabledControllerRetainsBinding(scenario.Host, scenario.Binding);
            Assert.That(scenario.Binding.ApplyCount, Is.Zero);
            StepWithActorValue(scenario, 10);
            Assert.That(scenario.Binding.ApplyCount, Is.Zero);

            ContextFrame authority = scenario.Host.CurrentContext;
            scenario.Binding.Value = 20;
            scenario.Binding.FailCaptureAfterWorldMutation = true;

            Assert.That(
                scenario.Host.TryStep(0.1d, out CoCoDiagnostic failure),
                Is.False);
            Assert.That(
                failure.Code,
                Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    authority,
                    scenario.Ids.ActorStateSlotId),
                Is.EqualTo(10));
            Assert.That(
                scenario.Binding.transform.localPosition,
                Is.EqualTo(new Vector3(20f, 2f, 3f)));
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(HostHistoryMode.Disabled));
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
            Assert.That(scenario.Binding.ApplyCount, Is.Zero);

            scenario.Binding.FailCaptureAfterWorldMutation = false;
            Require(
                scenario.Host.TryCorrectWorld(out CoCoDiagnostic correction),
                correction);

            Assert.That(scenario.Binding.CorrectionCount, Is.EqualTo(1));
            Assert.That(scenario.Binding.ApplyCount, Is.EqualTo(1));
            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(10));
            Assert.That(
                scenario.Binding.transform.localPosition,
                Is.EqualTo(new Vector3(10f, 0f, 0f)));
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(scenario.Host.TemporalState.Mode, Is.EqualTo(HostHistoryMode.Disabled));
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);

            StepWithActorValue(scenario, 30);
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
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
                Is.EqualTo(RuntimeLifecycleState.Created));
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
                Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(scenario.Host.CurrentContext.IsAlive, Is.False);
        }

        [Test]
        public void DestroyedBindingBeforeBeginRejectsWithoutFault()
        {
            AssertBeginWithoutLiveRestoreBindingDoesNotFault(destroyBinding: true);
        }

        [Test]
        public void BindingMovedOutsideHostBeforeBeginRejectsWithoutFault()
        {
            AssertBeginWithoutLiveRestoreBindingDoesNotFault(destroyBinding: false);
        }

        [Test]
        public void PreviewPreflightFailureBeforeProjectionCanCancelWithoutBinding()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            TemporalActorRestoreBinding restoreOnly = CreateRestoreOnlyBinding(scenario);
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            restoreOnly.transform.SetParent(null);

            Assert.That(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                Is.False);
            Assert.That(preview.IsError, Is.True);
            Assert.That(restoreOnly.ApplyCount, Is.Zero);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Previewing));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);
            Assert.That(restoreOnly.ApplyCount, Is.Zero);
            Assert.That(restoreOnly.CancelCount, Is.Zero);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));

            Assert.That(scenario.Host.TryEnqueueLocal(TemporalHostTestHarness.Packet(scenario,
                1UL, 83, scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch)),
                Is.EqualTo(InboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 30);
            Assert.That(TemporalHostLogic.LastEventValue, Is.EqualTo(83));
        }

        [Test]
        public void CancelBeforeAnyPreviewProjectionDoesNotInvokeBinding()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);

            Assert.That(scenario.Binding.ApplyCount, Is.Zero);
            Assert.That(scenario.Binding.CancelCount, Is.Zero);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));
        }

        [Test]
        public void PreviewDepthZeroStillRequiresCancelProjection()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(0, out CoCoDiagnostic preview),
                preview);

            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);
            Assert.That(scenario.Binding.PreviewCount, Is.EqualTo(1));
            Assert.That(scenario.Binding.CancelCount, Is.Zero);

            Require(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic cancel),
                cancel);

            Assert.That(scenario.Binding.ApplyCount, Is.EqualTo(2));
            Assert.That(scenario.Binding.CancelCount, Is.EqualTo(1));
            Assert.That(
                scenario.Binding.LastApplyKind,
                Is.EqualTo(HostRestoreKind.Cancel));
        }

        [Test]
        public void BindingLossAfterPreviewProjectionRequiresCorrection()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            TemporalActorRestoreBinding restoreOnly = CreateRestoreOnlyBinding(scenario);
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic firstPreview),
                firstPreview);
            restoreOnly.transform.SetParent(null);

            Assert.That(
                scenario.Host.TryPreviewTemporal(0, out CoCoDiagnostic rejected),
                Is.False);
            Assert.That(rejected.IsError, Is.True);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.EqualTo(1));

            restoreOnly.transform.SetParent(scenario.GameObject.transform);
            Require(
                scenario.Host.TryCorrectWorld(out CoCoDiagnostic correction),
                correction);

            Assert.That(restoreOnly.CorrectionCount, Is.EqualTo(1));
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));
        }

        [Test]
        public void CancelAfterHistoricalPreviewWithUnavailableBindingRequiresCorrection()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            TemporalActorRestoreBinding restoreOnly = CreateRestoreOnlyBinding(scenario);
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            Require(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic preview),
                preview);
            restoreOnly.transform.SetParent(null);

            Assert.That(
                scenario.Host.TryCancelTemporalPreview(out CoCoDiagnostic rejected),
                Is.False);
            Assert.That(rejected.IsError, Is.True);
            Assert.That(restoreOnly.CancelCount, Is.Zero);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Previewing));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.EqualTo(1));

            restoreOnly.transform.SetParent(scenario.GameObject.transform);
            Require(
                scenario.Host.TryCorrectWorld(out CoCoDiagnostic correction),
                correction);

            Assert.That(restoreOnly.CorrectionCount, Is.EqualTo(1));
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));
        }

        [TestCase(TemporalRestoreFixtureFailure.Reject)]
        [TestCase(TemporalRestoreFixtureFailure.Throw)]
        public void FirstPreviewCallbackFailureRequiresCorrection(
            TemporalRestoreFixtureFailure failureMode)
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(historyCapacity: 4));
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            ContextFrame authority = scenario.Host.CurrentContext;
            Require(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic begin),
                begin);
            scenario.Binding.Failure = failureMode;
            scenario.Binding.MutateBeforeFailure = true;

            Assert.That(
                scenario.Host.TryPreviewTemporal(1, out CoCoDiagnostic failure),
                Is.False);

            Assert.That(failure.IsError, Is.True);
            Assert.That(scenario.Binding.PreviewCount, Is.EqualTo(1));
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(scenario.Host.TemporalState.PreviewDepth, Is.Zero);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.True);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.True);

            scenario.Binding.Failure = TemporalRestoreFixtureFailure.None;
            scenario.Binding.MutateBeforeFailure = false;
            Require(
                scenario.Host.TryCorrectWorld(out CoCoDiagnostic correction),
                correction);
            Assert.That(scenario.Binding.CorrectionCount, Is.EqualTo(1));
            Assert.That(scenario.Binding.LastAppliedValue, Is.EqualTo(20));
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));
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
            ContextFrame authority = scenario.Host.CurrentContext;
            TickFrame authorityTick = authority.Header.TickFrame;
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
                Is.EqualTo(HostHistoryMode.Ready));
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
            ContextFrame authority = scenario.Host.CurrentContext;
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
                Is.EqualTo(RuntimeLifecycleState.Stopped));
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Disabled));
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
                if (kind == HostRestoreKind.Preview)
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
            HostRestoreReader escaped = scenario.Binding.EscapedReader;

            Require(scenario.Host.TryStop(out CoCoDiagnostic stop), stop);
            Assert.That(escaped.IsValid, Is.False);
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Disabled));

            Require(scenario.Host.TryStart(out CoCoDiagnostic restart), restart);
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));
            Assert.That(scenario.Host.TemporalState.Count, Is.Zero);
            StepWithActorValue(scenario, 30);
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(1));
            Require(scenario.Host.TryStop(out CoCoDiagnostic secondStop), secondStop);
            Require(scenario.Host.TryDispose(out CoCoDiagnostic dispose), dispose);
            Assert.That(
                scenario.Host.Lifecycle,
                Is.EqualTo(RuntimeLifecycleState.Disposed));
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
            HostRestoreReader escaped = scenario.Binding.EscapedReader;

            Assert.That(escaped.IsValid, Is.False);
            Assert.That(
                escaped.TryRead(
                    scenario.Ids.ActorStateSlotId,
                    out int escapedValue),
                Is.False);
            Assert.That(escapedValue, Is.Zero);

            FieldInfo leaseField = typeof(HostRestoreReader).GetField(
                "_lease", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(HostRestoreReader).FullName, "_lease");
            var lease = (HostRestoreReadLease)leaseField.GetValue(escaped);
            Assert.That(lease, Is.Not.Null);
            Assert.That(escaped.IsValid, Is.False);
            WeakReference controller = CaptureControllerAndStop(scenario);
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
            FieldInfo temporalField = typeof(StateGraphHost).GetField(
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

        private TemporalActorRestoreBinding CreateRestoreOnlyBinding(
            TemporalHostTestScenario scenario)
        {
            var restoreObject = new GameObject("Pre6 Temporal Restore Only Binding");
            _objects.Add(restoreObject);
            restoreObject.transform.SetParent(scenario.GameObject.transform);
            var binding = restoreObject.AddComponent<TemporalActorRestoreBinding>();
            binding.Configure(scenario.Ids.ActorStateSlotId);
            TemporalHostTestHarness.SetRestoreBinding(scenario.Host, binding);
            return binding;
        }

        private void AssertBeginWithoutLiveRestoreBindingDoesNotFault(
            bool destroyBinding)
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 4,
                    withEvent: true));
            TemporalActorRestoreBinding restoreOnly = CreateRestoreOnlyBinding(scenario);
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 10);
            StepWithActorValue(scenario, 20);
            ContextFrame authority = scenario.Host.CurrentContext;
            if (destroyBinding)
            {
                Object.DestroyImmediate(restoreOnly);
            }
            else
            {
                restoreOnly.transform.SetParent(null);
            }

            Assert.That(
                scenario.Host.TryBeginTemporalPreview(out CoCoDiagnostic rejected),
                Is.False);
            Assert.That(rejected.IsError, Is.True);
            Assert.That(scenario.Host.Fault.IsFaulted, Is.False);
            Assert.That(scenario.Host.RequiresWorldCorrection, Is.False);
            Assert.That(scenario.Host.CurrentContext, Is.EqualTo(authority));
            Assert.That(
                scenario.Host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Ready));

            Assert.That(scenario.Host.TryEnqueueLocal(TemporalHostTestHarness.Packet(scenario,
                1UL, 83, scenario.Host.CurrentContext.Header.TickFrame.TimelineEpoch)),
                Is.EqualTo(InboxEnqueueResult.Accepted));
            StepWithActorValue(scenario, 30);
            Assert.That(TemporalHostLogic.LastEventValue, Is.EqualTo(83));
        }

        private static void AssertDisabledControllerDoesNotRetainBinding(
            StateGraphHost host)
        {
            Assert.That(
                host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Disabled));
            FieldInfo temporalField = typeof(StateGraphHost).GetField(
                "_restore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(temporalField, Is.Not.Null);
            object controller = temporalField.GetValue(host);
            Assert.That(controller, Is.Not.Null);

            FieldInfo componentField = controller.GetType().GetField(
                "_bindingComponent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo bindingField = controller.GetType().GetField(
                "_applyBinding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(componentField, Is.Not.Null);
            Assert.That(bindingField, Is.Not.Null);
            Assert.That(componentField.GetValue(controller), Is.Null);
            Assert.That(bindingField.GetValue(controller), Is.Null);
        }

        private static void AssertDisabledControllerRetainsBinding(
            StateGraphHost host,
            TemporalActorRestoreBinding expected)
        {
            Assert.That(
                host.TemporalState.Mode,
                Is.EqualTo(HostHistoryMode.Disabled));
            FieldInfo temporalField = typeof(StateGraphHost).GetField(
                "_restore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(temporalField, Is.Not.Null);
            object controller = temporalField.GetValue(host);
            Assert.That(controller, Is.Not.Null);

            FieldInfo componentField = controller.GetType().GetField(
                "_bindingComponent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo bindingField = controller.GetType().GetField(
                "_applyBinding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(componentField, Is.Not.Null);
            Assert.That(bindingField, Is.Not.Null);
            Assert.That(componentField.GetValue(controller), Is.SameAs(expected));
            Assert.That(((Delegate)bindingField.GetValue(controller)).Target, Is.SameAs(expected));
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

        private static int CopyTraceCount(StateGraphHost host)
        {
            var entries = new StateFlowTraceEntry[64];
            return host.Trace.CopyLatestTo(entries);
        }

        private static void AssertTraceHasNoCommittedOutputAfter(
            StateGraphHost host,
            ulong lastCommittedTick)
        {
            var entries = new StateFlowTraceEntry[64];
            int count = host.Trace.CopyLatestTo(entries);
            for (int index = 0; index < count; index++)
            {
                StateFlowTraceEntry entry = entries[index];
                if (entry.TickFrame.Tick.Value <= lastCommittedTick)
                {
                    continue;
                }

                Assert.That(
                    entry.Kind,
                    Is.Not.EqualTo(StateFlowTraceKind.ContextCommit));
                Assert.That(
                    entry.Kind,
                    Is.Not.EqualTo(StateFlowTraceKind.EventSequence));
                Assert.That(
                    entry.Kind,
                    Is.Not.EqualTo(StateFlowTraceKind.EventPublished));
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
