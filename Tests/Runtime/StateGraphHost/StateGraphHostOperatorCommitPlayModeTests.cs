using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraphHost.StateGraphHost;

    public sealed class StateGraphHostOperatorCommitPlayModeTests
    {
        private const int AllocationWarmupIterations = 100;
        private const int AllocationMeasuredIterations = 10000;
        private const ulong ContextDefaultFingerprint = 5051UL;
        private const ulong PrimaryOperationFactoryFingerprint = 5091UL;
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        public enum ClaimRestoreLifecycleCase
        {
            Suspended = 0,
            ResumedBeforeRestore = 1,
            Running = 2
        }

        [SetUp]
        public void SetUp()
        {
            StateGraphProjectBindings.ResetForTests();
            HostTestLogic.Reset();
            OperatorCommitClaimLogic.Reset();
            OperatorCommitClaimMemoryBinding.Reset();
            OperatorCommitProjectFactoryProbe.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            StateGraphProjectBindings.ResetForTests();
            HostTestLogic.Reset();
            OperatorCommitClaimLogic.Reset();
            OperatorCommitClaimMemoryBinding.Reset();
            OperatorCommitProjectFactoryProbe.Reset();
        }

        [Test]
        public void FirstTickReadsLayoutDefaultsAndCommitsRevisionOne()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);

            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(runtimeOperator.PreviousHadCommittedFrame, Is.False);
            Assert.That(runtimeOperator.PreviousValue, Is.EqualTo(5));
            Assert.That(host.CurrentContext.IsAlive, Is.True);
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(host.CurrentContext.Header.TickFrame.Tick.Value, Is.EqualTo(1UL));
            Require(host.CurrentContext.Layout.TryResolveSlot(
                ids.StateSlotId,
                out StateSlot<int> slot));
            Assert.That(host.CurrentContext.Read(slot), Is.EqualTo(6));
        }

        [Test]
        public void OperatorFailureAfterWorldMutationKeepsOldAuthorityAndMarksCorrection()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            runtimeOperator.FailAfterWorldMutation = true;
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);

            Assert.That(failure.IsError, Is.True);
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(gameObject.transform.localPosition.x, Is.EqualTo(17f));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.Zero);
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.RequiresWorldCorrection, Is.True);
        }

        [Test]
        public void IgnoredUnauthorizedOutcomeWriteFaultsAndPreservesCommittedRevision()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame committed = host.CurrentContext;
            runtimeOperator.AttemptUnauthorizedOutcome = true;

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            Assert.That(host.CurrentContext, Is.EqualTo(committed));
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(host.Fault.IsFaulted, Is.True);
        }

        [Test]
        public void PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);
            var listener = new PublishLifecycleListener(host);
            CoCoEventBus.Subscribe<EventPacket<OperatorCommitEventA>>(listener);
            CoCoEventBus.Subscribe<EventPacket<OperatorCommitEventB>>(listener);
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                runtimeOperator.PublishEvents = false;
                Require(host.TryStep(0.02d, out CoCoDiagnostic empty), empty);
                Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
                Assert.That(listener.Order, Is.Empty);
                runtimeOperator.PublishEvents = true;
                Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);
            }
            finally
            {
                CoCoEventBus.Unsubscribe<EventPacket<OperatorCommitEventA>>(listener);
                CoCoEventBus.Unsubscribe<EventPacket<OperatorCommitEventB>>(listener);
            }

            CollectionAssert.AreEqual(new[] { "A", "B" }, listener.Order);
            CollectionAssert.AreEqual(new[] { 1UL, 2UL }, listener.Sequences);
            Assert.That(listener.ObservedRevision, Is.EqualTo(2UL));
            Assert.That(listener.ObservedTick, Is.EqualTo(2UL));
            Assert.That(listener.ReentrantStepAccepted, Is.False);
            Assert.That(listener.StopAccepted, Is.True);
            Assert.That(listener.DisposeAccepted, Is.True);
            Assert.That(listener.LifecycleDuringPublish, Is.EqualTo(RuntimeLifecycleState.Running));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
        }

        [Test]
        public void IgnoredInvalidOutboxWriteFaultsAndPublishesNothing()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            runtimeOperator.AttemptInvalidTarget = true;
            SetOperators(host, runtimeOperator);
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<EventPacket<OperatorCommitEventA>>(listener);
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            }
            finally
            {
                CoCoEventBus.Unsubscribe<EventPacket<OperatorCommitEventA>>(listener);
            }

            Assert.That(listener.Order, Is.Empty);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.Zero);
            Assert.That(host.Fault.IsFaulted, Is.True);
        }

        [Test]
        public void StartupRejectsGlobalOutboxCapacityBelowUniqueLaneReservation()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 3);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.EventOutbox));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.EventOutboxOverflow));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            AssertNoProjectFactoryCallbacks();
        }

        [Test]
        public void TransactionPreflightRejectsInvalidOperatorBeforeProjectFactoryCallbacks()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new OperatorCommitBindingProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            SetOperators(host, (MonoBehaviour)null);

            AssertTransactionPreflightFailure(
                host,
                provider,
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.InvalidOperatorDescriptor);
        }

        [Test]
        public void TransactionPreflightRejectsOutboxCapacityBeforeProjectFactoryCallbacks()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new OperatorCommitBindingProvider(ids);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                eventOutboxCapacity: 3);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);

            AssertTransactionPreflightFailure(
                host,
                provider,
                CoCoDiagnosticDomain.EventOutbox,
                CoCoDiagnosticCode.EventOutboxOverflow);
        }

        [Test]
        public void TransactionPreflightRejectsMismatchedTrustedClaimDefaultBeforeFactories()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new ClaimBindingProvider(ids, mismatchPrimaryClaimDefault: true);
            Require(StateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Operator));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidOperatorDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            AssertNoProjectFactoryCallbacks();
        }

        [Test]
        public void ExactCustomContextCodecIsFrozenIntoRuntimeBindings()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, CodecBindingMode.ExactCustom);
            Require(StateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            ContextCodecRegistry codecs = GetBindings(host).ContextCodecs;
            Assert.That(codecs.IsFrozen, Is.True);
            Assert.That(codecs.Count, Is.EqualTo(1));
            Assert.That(codecs.TryResolve(
                provider.ManifestCodec,
                out IContextValueCodec<int> resolved,
                out CoCoDiagnosticCode diagnosticCode), Is.True, diagnosticCode.ToString());
            Assert.That(resolved, Is.SameAs(provider.BoundCodec));
        }

        [Test]
        public void DestroyDuringTemporalCodecCaptureCancelsAuthorityOutboxSequenceAndHistory()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, CodecBindingMode.ExactCustom);
            Require(StateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                eventOutboxCapacity: 4,
                traceCapacity: 64);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 3);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);
            Require(host.TryStart(out CoCoDiagnostic start), start);

            StateGraphRuntime runtime = GetRuntime(host);
            StateGraphTransaction transaction = GetTransaction(host);
            StateGraphTemporalController temporal = GetTemporal(host);
            IStateFlowTrace trace = host.Trace;
            Assert.That(temporal.State.Mode, Is.EqualTo(TemporalMode.Ready));
            Assert.That(temporal.State.Count, Is.Zero);
            int historyCountDuringDestroy = -1;
            provider.BoundCodec.EncodeCallback = () =>
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                historyCountDuringDestroy = temporal.State.Count;
            };
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<EventPacket<OperatorCommitEventA>>(listener);
            CoCoEventBus.Subscribe<EventPacket<OperatorCommitEventB>>(listener);

            bool stepped = true;
            CoCoDiagnostic failure = default;
            try
            {
                Assert.DoesNotThrow(() => stepped = host.TryStep(0.02d, out failure));
            }
            finally
            {
                CoCoEventBus.Unsubscribe<EventPacket<OperatorCommitEventA>>(listener);
                CoCoEventBus.Unsubscribe<EventPacket<OperatorCommitEventB>>(listener);
                provider.BoundCodec.EncodeCallback = null;
            }

            Assert.That(stepped, Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(
                failure.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(historyCountDuringDestroy, Is.Zero);
            Assert.That(provider.BoundCodec.EncodeCount, Is.EqualTo(1));
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(listener.Order, Is.Empty);
            Assert.That(listener.Sequences, Is.Empty);
            Assert.That(ReadLastEventSequence(transaction), Is.Zero);
            Assert.That(transaction.CandidateEventCount, Is.Zero);
            Assert.That(transaction.CurrentContext.IsAlive, Is.False);
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(runtime.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
            Assert.That(temporal.State.Mode, Is.EqualTo(TemporalMode.Disabled));
            Assert.That(temporal.State.Count, Is.Zero);
            Assert.That(trace.Count, Is.Zero);
            Assert.That(host == null, Is.True);
            Assert.That(gameObject == null, Is.True);
        }

        [TestCase(CodecBindingMode.MissingCustom)]
        [TestCase(CodecBindingMode.MismatchedCustom)]
        [TestCase(CodecBindingMode.ExtraOnRaw)]
        public void ContextCodecBindingMustExactlyMatchManifest(CodecBindingMode mode)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, mode);
            Require(StateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Registry));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
        }

        [Test]
        public void OperatorListRejectsInvalidEntriesDuplicateIdsAndNestedHostCrossing()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);

            StateGraphHost nullHost = CreateHost(ids, out GameObject nullObject);
            SetOperators(nullHost, (MonoBehaviour)null);
            AssertStartupOperatorFailure(nullHost);

            StateGraphHost nonOperatorHost = CreateHost(ids, out GameObject nonOperatorObject);
            SetOperators(nonOperatorHost, nonOperatorHost);
            AssertStartupOperatorFailure(nonOperatorHost);

            StateGraphHost duplicateHost = CreateHost(ids, out GameObject duplicateObject);
            var duplicate = duplicateObject.AddComponent<ContextWritingOperator>();
            duplicate.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(duplicateHost, duplicate, duplicate);
            AssertStartupOperatorFailure(duplicateHost);

            StateGraphHost destroyedHost = CreateHost(ids, out GameObject destroyedObject);
            var destroyed = destroyedObject.AddComponent<ContextWritingOperator>();
            destroyed.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(destroyedHost, destroyed);
            UnityEngine.Object.DestroyImmediate(destroyed);
            AssertStartupOperatorFailure(destroyedHost);

            StateGraphHost duplicateIdHost = CreateHost(ids, out GameObject duplicateIdObject);
            var owner = duplicateIdObject.AddComponent<ContextWritingOperator>();
            owner.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            var duplicateId = duplicateIdObject.AddComponent<DuplicateIdOperator>();
            duplicateId.Configure(ids.FirstOperatorId, ids.PrimarySectionId);
            SetOperators(duplicateIdHost, owner, duplicateId);
            AssertStartupOperatorFailure(duplicateIdHost);

            StateGraphHost parentHost = CreateHost(ids, out GameObject parentObject);
            var nestedObject = new GameObject("Nested Host Boundary");
            _objects.Add(nestedObject);
            nestedObject.transform.SetParent(parentObject.transform);
            nestedObject.AddComponent<StateGraphHost>();
            var nestedOperator = nestedObject.AddComponent<ContextWritingOperator>();
            nestedOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(parentHost, nestedOperator);
            AssertStartupOperatorFailure(parentHost);
        }

        [Test]
        public void OperatorRequiresMustExactlyCoverCompiledOperationProvides()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost extraHost = CreateHost(ids, out GameObject extraObject);
            var extra = extraObject.AddComponent<ExtraneousRequirementOperator>();
            extra.Configure(ids);
            SetOperators(extraHost, extra);
            Assert.That(extraHost.TryStart(out CoCoDiagnostic extraFailure), Is.False);
            Assert.That(extraFailure.Code, Is.EqualTo(CoCoDiagnosticCode.MissingOperatorBinding));
            AssertNoProjectFactoryCallbacks();

            StateGraphProjectBindings.ResetForTests();
            InstallClaimProvider(ids);
            StateGraphHost missingHost = CreateClaimHost(ids, out GameObject missingObject, 0);
            var low = missingObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            SetOperators(missingHost, low);
            Assert.That(missingHost.TryStart(out CoCoDiagnostic missingFailure), Is.False);
            Assert.That(missingFailure.Code, Is.EqualTo(CoCoDiagnosticCode.MissingOperatorBinding));
        }

        [Test]
        public void ClaimDeniedIsRecordedBeforeCallbacksAndDisabledSectionUsesExplicitNoOp()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            var noOp = gameObject.AddComponent<NoOpSecondaryOperator>();
            noOp.Configure(ids);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            SetOperators(host, high, noOp, low);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);

            Assert.That(high.ExecuteCount, Is.Zero);
            Assert.That(noOp.ExecuteCount, Is.EqualTo(1));
            Assert.That(noOp.SawDisabledSection, Is.True);
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            var outcomes = new StateFlowTraceEntry[4];
            int count = host.Trace.CopyLatestTo(
                outcomes,
                new StateFlowTraceFilter(StateFlowTraceKind.OperatorOutcome));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(outcomes[0].OperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(OperatorOutcomeStatus.ClaimDenied));
            Assert.That(
                outcomes[1].OperatorOutcome,
                Is.EqualTo(OperatorOutcomeStatus.NoOp));
            Assert.That(
                outcomes[2].OperatorOutcome,
                Is.EqualTo(OperatorOutcomeStatus.Succeeded));
        }

        [Test]
        public void ClaimDeniedIsMaterializedBeforeEarlierWinnerCallbackCanFail()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 16);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            low.FailExecution = true;
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(
                failure.Code,
                Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.Zero);

            var outcomes = new StateFlowTraceEntry[4];
            int count = host.Trace.CopyLatestTo(
                outcomes,
                new StateFlowTraceFilter(StateFlowTraceKind.OperatorOutcome));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(outcomes[0].OperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(OperatorOutcomeStatus.ClaimDenied));

            var traceEntries = new StateFlowTraceEntry[16];
            int traceCount = host.Trace.CopyLatestTo(traceEntries);
            Assert.That(traceCount, Is.GreaterThan(0));
            Assert.That(
                traceEntries[traceCount - 1].Kind,
                Is.EqualTo(StateFlowTraceKind.Cancelled));
            Assert.That(traceEntries[traceCount - 1].PreviousContext.IsValid, Is.True);
            Assert.That(traceEntries[traceCount - 1].PreviousContext.HasCommittedFrame, Is.False);
            for (int index = 0; index < traceCount; index++)
            {
                Assert.That(
                    traceEntries[index].Kind,
                    Is.Not.EqualTo(StateFlowTraceKind.ContextCommit));
                Assert.That(
                    traceEntries[index].Kind,
                    Is.Not.EqualTo(StateFlowTraceKind.EventSequence));
                Assert.That(
                    traceEntries[index].Kind,
                    Is.Not.EqualTo(StateFlowTraceKind.EventPublished));
            }
        }

        [TestCase(0)]
        [TestCase(64)]
        public void GraphProducerClaimOperatorAndTraceHaveZeroSteadyStateManagedAllocation(
            int traceCapacity)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            var noOp = gameObject.AddComponent<NoOpSecondaryOperator>();
            noOp.Configure(ids);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            SetOperators(host, high, noOp, low);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            bool failed = false;
            for (int index = 0; index < AllocationWarmupIterations; index++)
            {
                failed |= !host.TryStep(0.02d, out _);
            }

            Assert.That(failed, Is.False);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < AllocationMeasuredIterations; index++)
            {
                failed |= !host.TryStep(0.02d, out _);
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(failed, Is.False);
            Assert.That(allocatedBytes, Is.Zero);
            Assert.That(low.ExecuteCount, Is.EqualTo(
                AllocationWarmupIterations + AllocationMeasuredIterations));
            Assert.That(high.ExecuteCount, Is.Zero);
        }

        [Test]
        public void GraphCaptureIntegrityFailureKeepsStagedOperationTraceBeforeCancellation()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            OperatorCommitClaimMemoryBinding.MutateMemoryOnCapture = true;

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(low.ExecuteCount, Is.Zero);
            Assert.That(high.ExecuteCount, Is.Zero);
            Assert.That(host.CurrentContext.IsAlive, Is.False);

            var entries = new StateFlowTraceEntry[32];
            int count = host.Trace.CopyLatestTo(entries);
            Assert.That(count, Is.GreaterThanOrEqualTo(4));
            Assert.That(entries[0].Kind, Is.EqualTo(StateFlowTraceKind.Tick));
            Assert.That(entries[1].Kind, Is.EqualTo(StateFlowTraceKind.OperationSection));
            Assert.That(entries[2].Kind, Is.EqualTo(StateFlowTraceKind.OperationSection));
            Assert.That(entries[count - 1].Kind, Is.EqualTo(StateFlowTraceKind.Cancelled));
            for (int index = 0; index < count; index++)
            {
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.OperatorOutcome));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.ContextCommit));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.EventSequence));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.EventPublished));
            }
        }

        [Test]
        public void RestoreCompatibilityValidationIsPureReadOnlyHostSeam()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject, traceCapacity: 16);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);
            ContextFrame current = host.CurrentContext;
            TickFrame sourceTick = current.Header.TickFrame;
            Require(TimelinePosition.TryCreate(
                sourceTick.TimelinePosition.Seconds + 1d,
                out TimelinePosition resumedPosition));
            Require(TickFrame.TryCreate(
                0.02d,
                sourceTick.TimelineId,
                resumedPosition,
                new TimelineTick(sourceTick.Tick.Value + 1UL),
                sourceTick.ClockDomainId,
                new ExecutionSequence(sourceTick.ExecutionSequence.Value + 1UL),
                new TimelineEpoch(sourceTick.TimelineEpoch.Value + 1UL),
                out TickFrame resumed,
                out CoCoDiagnostic frame), frame);
            ulong traceCount = host.Trace.TotalWritten;

            Assert.That(host.TryValidateRestore(
                current,
                resumed,
                out ContextCommitStatus status), Is.True);
            Assert.That(status, Is.EqualTo(ContextCommitStatus.None));
            Assert.That(host.TryValidateRestore(
                default,
                resumed,
                out ContextCommitStatus invalidStatus), Is.False);
            Assert.That(invalidStatus, Is.EqualTo(ContextCommitStatus.LayoutMismatch));
            Assert.That(host.CurrentContext, Is.EqualTo(current));
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Running));
        }

        [Test]
        public void PreparedActorRestoreAtomicallyRestoresContextGraphClockAndClaims()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<EventPacket<OperatorCommitEventA>>(listener);
            ContextFrame source = default;
            bool retained = false;
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
                source = host.CurrentContext;
                retained = source.Retain();
                Require(retained);
                GraphStateRecord<byte> sourceFirstState = ReadClaimGraphState(
                    source,
                    ids.FirstGraphStateSlotId);
                GraphStateRecord<byte> sourceSecondState = ReadClaimGraphState(
                    source,
                    ids.SecondGraphStateSlotId);
                OperatorClaimState sourcePrimary = ReadClaim(
                    source,
                    ids.PrimaryClaimStateSlotId);
                OperatorClaimState sourceSecondary = ReadClaim(
                    source,
                    ids.SecondaryClaimStateSlotId);
                Assert.That(sourceFirstState.IsOnActivePath, Is.True);
                Assert.That(sourceSecondState.IsOnActivePath, Is.False);
                Assert.That(sourcePrimary.OwnerOperatorId, Is.EqualTo(ids.FirstOperatorId));
                Assert.That(sourceSecondary.IsHeld, Is.False);

                OperatorCommitClaimLogic.EnableSecondary = true;
                OperatorCommitClaimLogic.RequestTransition = true;
                Require(host.TryStep(0.02d, out CoCoDiagnostic transition), transition);
                OperatorCommitClaimLogic.RequestTransition = false;
                Require(host.TryStep(0.02d, out CoCoDiagnostic changed), changed);
                ContextFrame changedContext = host.CurrentContext;
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(
                    ReadClaim(changedContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                    Is.EqualTo(ids.SecondOperatorId));
                Assert.That(
                    ReadClaim(changedContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                    Is.EqualTo(ids.SecondOperatorId));

                StateGraphRuntime runtime = GetRuntime(host);
                TickFrame resumed = CreateResumedTick(changedContext.Header.TickFrame);
                ulong traceCount = host.Trace.TotalWritten;
                int lowExecutions = low.ExecuteCount;
                int highExecutions = high.ExecuteCount;
                int logicUpdates = OperatorCommitClaimLogic.UpdateCount;
                TimelineTick changedTick = runtime.Clock.Tick;
                TimelineEpoch changedEpoch = runtime.Clock.TimelineEpoch;

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out PreparedActorRestore cancelled,
                    out ContextCommitStatus cancelStatus,
                    out CoCoDiagnostic cancelPrepare), cancelPrepare);
                Assert.That(cancelStatus, Is.EqualTo(ContextCommitStatus.None));
                Assert.That(cancelled.IsValid, Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(changedContext));
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(changedTick));
                Assert.That(runtime.Clock.TimelineEpoch, Is.EqualTo(changedEpoch));
                Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
                Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
                Assert.That(OperatorCommitClaimLogic.UpdateCount, Is.EqualTo(logicUpdates));
                Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
                Assert.That(listener.Order, Is.Empty);
                Assert.That(cancelled.Cancel(), Is.True);
                Assert.That(cancelled.IsValid, Is.False);
                Assert.That(host.CurrentContext, Is.EqualTo(changedContext));
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(changedTick));
                Assert.That(runtime.Clock.TimelineEpoch, Is.EqualTo(changedEpoch));
                Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out PreparedActorRestore committed,
                    out ContextCommitStatus commitStatus,
                    out CoCoDiagnostic commitPrepare), commitPrepare);
                Assert.That(commitStatus, Is.EqualTo(ContextCommitStatus.None));
                Assert.That(committed.IsValid, Is.True);
                committed.CommitNoFail();

                Assert.That(committed.IsValid, Is.False);
                Assert.That(host.CurrentContext, Is.Not.EqualTo(changedContext));
                Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(4UL));
                Assert.That(host.CurrentContext.Header.TickFrame, Is.EqualTo(resumed));
                Assert.That(host.CurrentContext.Origin.Kind, Is.EqualTo(ContextFrameOriginKind.Restore));
                Assert.That(host.CurrentContext.Origin.SourceRevision, Is.EqualTo(source.Revision));
                Assert.That(
                    ReadClaimGraphState(host.CurrentContext, ids.FirstGraphStateSlotId),
                    Is.EqualTo(sourceFirstState));
                Assert.That(
                    ReadClaimGraphState(host.CurrentContext, ids.SecondGraphStateSlotId),
                    Is.EqualTo(sourceSecondState));
                Assert.That(
                    ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId),
                    Is.EqualTo(sourcePrimary));
                Assert.That(
                    ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId),
                    Is.EqualTo(sourceSecondary));
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.StateId));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(resumed.Tick));
                Assert.That(runtime.Clock.TimelineEpoch, Is.EqualTo(resumed.TimelineEpoch));
                Assert.That(runtime.Clock.ExecutionSequence, Is.EqualTo(resumed.ExecutionSequence));
                Assert.That(runtime.Clock.Seconds, Is.EqualTo(resumed.TimelinePosition.Seconds));
                Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
                Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
                Assert.That(OperatorCommitClaimLogic.UpdateCount, Is.EqualTo(logicUpdates));
                Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
                Assert.That(listener.Order, Is.Empty);

                Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.StateId));
                Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions + 1));
                Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
                Assert.That(
                    ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                    Is.EqualTo(ids.FirstOperatorId));
                Assert.That(host.CurrentContext.Header.TickFrame.Tick.Value, Is.EqualTo(resumed.Tick.Value + 1UL));
            }
            finally
            {
                if (retained)
                {
                    Assert.That(source.Release(), Is.True);
                }

                CoCoEventBus.Unsubscribe<EventPacket<OperatorCommitEventA>>(listener);
            }
        }

        [Test]
        public void TemporalConfirmRestoresHistoricalGraphPathAndHeldClaims()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            ContextRevision sourceRevision = source.Revision;
            GraphStateRecord<byte> sourceFirstState = ReadClaimGraphState(
                source,
                ids.FirstGraphStateSlotId);
            GraphStateRecord<byte> sourceSecondState = ReadClaimGraphState(
                source,
                ids.SecondGraphStateSlotId);
            OperatorClaimState sourcePrimary = ReadClaim(
                source,
                ids.PrimaryClaimStateSlotId);
            OperatorClaimState sourceSecondary = ReadClaim(
                source,
                ids.SecondaryClaimStateSlotId);
            Assert.That(sourcePrimary.IsHeld, Is.True);
            Assert.That(sourcePrimary.OwnerOperatorId, Is.EqualTo(ids.FirstOperatorId));
            Assert.That(sourcePrimary.ActivationId.IsValid, Is.True);
            Assert.That(sourceSecondary.IsHeld, Is.False);

            OperatorCommitClaimLogic.EnableSecondary = true;
            OperatorCommitClaimLogic.RequestTransition = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic transition), transition);
            OperatorCommitClaimLogic.RequestTransition = false;
            Require(host.TryStep(0.02d, out CoCoDiagnostic changed), changed);
            ContextFrame changedAuthority = host.CurrentContext;
            OperatorClaimState changedPrimary = ReadClaim(
                changedAuthority,
                ids.PrimaryClaimStateSlotId);
            OperatorClaimState changedSecondary = ReadClaim(
                changedAuthority,
                ids.SecondaryClaimStateSlotId);
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
            Assert.That(changedPrimary.OwnerOperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(changedSecondary.OwnerOperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(changedPrimary.ActivationId, Is.Not.EqualTo(sourcePrimary.ActivationId));
            int lowExecutions = low.ExecuteCount;
            int highExecutions = high.ExecuteCount;
            int logicUpdates = OperatorCommitClaimLogic.UpdateCount;

            Require(host.TryBeginTemporalPreview(out CoCoDiagnostic begin), begin);
            Require(host.TryPreviewTemporal(2, out CoCoDiagnostic preview), preview);
            Assert.That(
                host.TemporalState.Preview.Revision.Value,
                Is.EqualTo(sourceRevision.Value));
            Require(host.TryConfirmTemporalRestore(out CoCoDiagnostic confirm), confirm);

            ContextFrame restored = host.CurrentContext;
            Assert.That(restored.Origin.IsRestore, Is.True);
            Assert.That(restored.Origin.SourceRevision, Is.EqualTo(sourceRevision));
            Assert.That(
                ReadClaimGraphState(restored, ids.FirstGraphStateSlotId),
                Is.EqualTo(sourceFirstState));
            Assert.That(
                ReadClaimGraphState(restored, ids.SecondGraphStateSlotId),
                Is.EqualTo(sourceSecondState));
            Assert.That(
                ReadClaim(restored, ids.PrimaryClaimStateSlotId),
                Is.EqualTo(sourcePrimary));
            Assert.That(
                ReadClaim(restored, ids.SecondaryClaimStateSlotId),
                Is.EqualTo(sourceSecondary));
            Assert.That(
                ReadClaim(restored, ids.PrimaryClaimStateSlotId).ActivationId,
                Is.EqualTo(sourcePrimary.ActivationId));
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.StateId));
            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
            Assert.That(OperatorCommitClaimLogic.UpdateCount, Is.EqualTo(logicUpdates));

            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);
            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions + 1));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));
        }

        [Test]
        public void TemporalConfirmDiscardsAbandonedSuspendReleaseOverlay()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            Require(host.TryBeginTemporalPreview(out CoCoDiagnostic begin), begin);
            Require(host.TryPreviewTemporal(1, out CoCoDiagnostic preview), preview);
            Require(host.TryConfirmTemporalRestore(out CoCoDiagnostic confirm), confirm);

            int lowExecutions = low.ExecuteCount;
            int highExecutions = high.ExecuteCount;
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);

            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions + 1));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).IsHeld,
                Is.False);
        }

        [TestCase(TemporalRestoreFixtureFailure.Reject)]
        [TestCase(TemporalRestoreFixtureFailure.Throw)]
        public void TemporalConfirmBindingFailurePreservesPendingSuspendReleaseOverlay(
            TemporalRestoreFixtureFailure failureMode)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);
            ContextFrame authority = host.CurrentContext;
            Assert.That(
                ReadClaim(authority, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            Require(host.TryBeginTemporalPreview(out CoCoDiagnostic begin), begin);
            Require(host.TryPreviewTemporal(1, out CoCoDiagnostic preview), preview);
            restoreBinding.Failure = failureMode;
            bool confirmed = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() =>
                confirmed = host.TryConfirmTemporalRestore(out failure));

            Assert.That(confirmed, Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(host.CurrentContext, Is.EqualTo(authority));
            Assert.That(host.RequiresWorldCorrection, Is.True);
            Assert.That(host.Fault.IsFaulted, Is.True);

            int lowExecutions = low.ExecuteCount;
            int highExecutions = high.ExecuteCount;
            restoreBinding.Failure = TemporalRestoreFixtureFailure.None;
            Require(host.TryCorrectWorld(out CoCoDiagnostic correction), correction);
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);

            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions + 1));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
        }

        [TestCase(TemporalRestoreFixtureFailure.Reject)]
        [TestCase(TemporalRestoreFixtureFailure.Throw)]
        public void TemporalConfirmBindingFailurePreservesCurrentClaimAuthority(
            TemporalRestoreFixtureFailure failureMode)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            OperatorCommitClaimLogic.EnableSecondary = true;
            OperatorCommitClaimLogic.RequestTransition = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic transition), transition);
            OperatorCommitClaimLogic.RequestTransition = false;
            Require(host.TryStep(0.02d, out CoCoDiagnostic changed), changed);

            ContextFrame authority = host.CurrentContext;
            TickFrame authorityTick = authority.Header.TickFrame;
            OperatorClaimState authorityPrimary = ReadClaim(
                authority,
                ids.PrimaryClaimStateSlotId);
            OperatorClaimState authoritySecondary = ReadClaim(
                authority,
                ids.SecondaryClaimStateSlotId);
            Assert.That(authorityPrimary.OwnerOperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(authoritySecondary.OwnerOperatorId, Is.EqualTo(ids.SecondOperatorId));
            int historyCount = host.TemporalState.Count;
            int lowExecutions = low.ExecuteCount;
            int highExecutions = high.ExecuteCount;
            int logicUpdates = OperatorCommitClaimLogic.UpdateCount;

            Require(host.TryBeginTemporalPreview(out CoCoDiagnostic begin), begin);
            Require(host.TryPreviewTemporal(2, out CoCoDiagnostic preview), preview);
            restoreBinding.Failure = failureMode;
            bool confirmed = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() =>
                confirmed = host.TryConfirmTemporalRestore(out failure));

            Assert.That(confirmed, Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(host.CurrentContext, Is.EqualTo(authority));
            Assert.That(host.CurrentContext.Header.TickFrame, Is.EqualTo(authorityTick));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId),
                Is.EqualTo(authorityPrimary));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId),
                Is.EqualTo(authoritySecondary));
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
            Assert.That(host.TemporalState.Count, Is.EqualTo(historyCount));
            Assert.That(host.TemporalState.PreviewDepth, Is.EqualTo(2));
            Assert.That(host.RequiresWorldCorrection, Is.True);
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
            Assert.That(OperatorCommitClaimLogic.UpdateCount, Is.EqualTo(logicUpdates));

            restoreBinding.Failure = TemporalRestoreFixtureFailure.None;
            Require(host.TryCorrectWorld(out CoCoDiagnostic correction), correction);
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);
            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions + 1));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
        }

        [TestCase(
            ClaimRestoreLifecycleCase.Suspended,
            OperatorClaimSuspendPolicy.Release,
            true)]
        [TestCase(
            ClaimRestoreLifecycleCase.ResumedBeforeRestore,
            OperatorClaimSuspendPolicy.Release,
            true)]
        [TestCase(
            ClaimRestoreLifecycleCase.Running,
            OperatorClaimSuspendPolicy.Release,
            false)]
        [TestCase(
            ClaimRestoreLifecycleCase.Suspended,
            OperatorClaimSuspendPolicy.Retain,
            false)]
        public void RestorePreservesOnlyApplicableSuspendReleaseOverlay(
            ClaimRestoreLifecycleCase lifecycleCase,
            OperatorClaimSuspendPolicy suspendPolicy,
            bool expectRelease)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, suspendPolicy);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            Assert.That(
                ReadClaim(source, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            if (lifecycleCase != ClaimRestoreLifecycleCase.Running)
            {
                Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
                if (lifecycleCase == ClaimRestoreLifecycleCase.ResumedBeforeRestore)
                {
                    Require(host.TryResume(out CoCoDiagnostic earlyResume), earlyResume);
                }
            }

            TickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Require(host.TryPrepareRestore(
                source,
                resumed,
                out PreparedActorRestore prepared,
                out ContextCommitStatus restoreStatus,
                out CoCoDiagnostic prepare), prepare);
            Assert.That(restoreStatus, Is.EqualTo(ContextCommitStatus.None));
            prepared.CommitNoFail();

            if (lifecycleCase == ClaimRestoreLifecycleCase.Suspended)
            {
                Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
                Require(host.TryResume(out CoCoDiagnostic resume), resume);
            }

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);

            OperatorId expectedOwner = expectRelease
                ? ids.SecondOperatorId
                : ids.FirstOperatorId;
            Assert.That(low.ExecuteCount, Is.EqualTo(expectRelease ? 1 : 2));
            Assert.That(high.ExecuteCount, Is.EqualTo(expectRelease ? 1 : 0));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(expectedOwner));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                expectRelease
                    ? Is.EqualTo(ids.SecondOperatorId)
                    : Is.EqualTo(default(OperatorId)));
        }

        [Test]
        public void CancellingSuspendedRestoreDoesNotClearPendingClaimRelease()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            TickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Require(host.TryPrepareRestore(
                source,
                resumed,
                out PreparedActorRestore prepared,
                out ContextCommitStatus restoreStatus,
                out CoCoDiagnostic prepare), prepare);
            Assert.That(restoreStatus, Is.EqualTo(ContextCommitStatus.None));
            Assert.That(prepared.Cancel(), Is.True);

            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);

            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
        }

        [Test]
        public void PersistenceImportIntoSuspendedSiblingRebuildsPendingClaimRelease()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallDurableClaimProvider(ids);
            StateGraphAsset asset = CreateClaimGraphAsset(ids);
            byte[] payload = CaptureClaimPersistencePayload(
                ids,
                asset,
                out StateGraphHost source);
            StateGraphHost target = CreateClaimHost(
                asset,
                out GameObject targetObject,
                traceCapacity: 0);
            var low = targetObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = targetObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(target, low, high);

            Require(target.TryStart(out CoCoDiagnostic start), start);
            Require(target.TryStep(0.02d, out CoCoDiagnostic first), first);
            Assert.That(target.GraphInstanceId, Is.Not.EqualTo(source.GraphInstanceId));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            Require(target.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(
                target.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic imported),
                imported);
            Assert.That(target.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            Require(target.TryResume(out CoCoDiagnostic resume), resume);
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(target.TryStep(0.02d, out CoCoDiagnostic following), following);

            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
        }

        [Test]
        public void PersistenceImportIntoRunningSiblingDiscardsAbandonedClaimRelease()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallDurableClaimProvider(ids);
            StateGraphAsset asset = CreateClaimGraphAsset(ids);
            byte[] payload = CaptureClaimPersistencePayload(
                ids,
                asset,
                out StateGraphHost source);
            StateGraphHost target = CreateClaimHost(
                asset,
                out GameObject targetObject,
                traceCapacity: 0);
            var low = targetObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = targetObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(target, low, high);

            Require(target.TryStart(out CoCoDiagnostic start), start);
            Require(target.TryStep(0.02d, out CoCoDiagnostic first), first);
            Assert.That(target.GraphInstanceId, Is.Not.EqualTo(source.GraphInstanceId));
            Require(target.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(target.TryResume(out CoCoDiagnostic resume), resume);
            Require(
                target.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic imported),
                imported);
            Assert.That(target.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Running));

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(target.TryStep(0.02d, out CoCoDiagnostic following), following);

            Assert.That(low.ExecuteCount, Is.EqualTo(2));
            Assert.That(high.ExecuteCount, Is.Zero);
            Assert.That(
                ReadClaim(target.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.SecondaryClaimStateSlotId).IsHeld,
                Is.False);
        }

        [TestCase(TemporalRestoreFixtureFailure.Reject)]
        [TestCase(TemporalRestoreFixtureFailure.Throw)]
        public void PersistenceImportBindingFailurePreservesPendingClaimRelease(
            TemporalRestoreFixtureFailure failureMode)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallDurableClaimProvider(ids);
            StateGraphAsset asset = CreateClaimGraphAsset(ids);
            byte[] payload = CaptureClaimPersistencePayload(
                ids,
                asset,
                out StateGraphHost source);
            StateGraphHost target = CreateClaimHost(
                asset,
                out GameObject targetObject,
                traceCapacity: 0);
            var restoreBinding = targetObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(target, "contextRestoreBinding", restoreBinding);
            var low = targetObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = targetObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(target, low, high);

            Require(target.TryStart(out CoCoDiagnostic start), start);
            Require(target.TryStep(0.02d, out CoCoDiagnostic first), first);
            Assert.That(target.GraphInstanceId, Is.Not.EqualTo(source.GraphInstanceId));
            ContextFrame authority = target.CurrentContext;
            Require(target.TrySuspend(out CoCoDiagnostic suspend), suspend);
            restoreBinding.Failure = failureMode;
            bool applied = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() =>
                applied = target.TryApplyPersistencePayload(payload, out failure));

            Assert.That(applied, Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(target.CurrentContext, Is.EqualTo(authority));
            Assert.That(target.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
            Assert.That(target.RequiresWorldCorrection, Is.True);
            Assert.That(target.Fault.IsFaulted, Is.True);

            restoreBinding.Failure = TemporalRestoreFixtureFailure.None;
            Require(target.TryCorrectWorld(out CoCoDiagnostic correction), correction);
            Require(target.TryResume(out CoCoDiagnostic resume), resume);
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(target.TryStep(0.02d, out CoCoDiagnostic following), following);

            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(target.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
        }

        [Test]
        public void ResetToDefaultClaimRestoreKeepsContextAndClaimCacheConsistent()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(
                ids,
                ContextRestorePolicy.ResetToDefault,
                ContextRestorePolicy.Stored);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            Assert.That(
                ReadClaim(source, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            TickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Require(host.TryPrepareRestore(
                source,
                resumed,
                out PreparedActorRestore prepared,
                out ContextCommitStatus restoreStatus,
                out CoCoDiagnostic prepare), prepare);
            Assert.That(restoreStatus, Is.EqualTo(ContextCommitStatus.None));
            prepared.CommitNoFail();
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).IsHeld,
                Is.False);

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
        }

        [Test]
        public void RestoreValidationRejectsPostPolicyPartialClaimOwnership()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(
                ids,
                ContextRestorePolicy.ResetToDefault,
                ContextRestorePolicy.Stored);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            Assert.That(
                ReadClaim(source, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(source, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));

            int lowExecutions = low.ExecuteCount;
            int highExecutions = high.ExecuteCount;
            TickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Assert.That(host.TryValidateRestore(
                source,
                resumed,
                out ContextCommitStatus status), Is.False);
            Assert.That(status, Is.EqualTo(ContextCommitStatus.RestoreFailed));
            Assert.That(host.CurrentContext, Is.EqualTo(source));
            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
        }

        [Test]
        public void TraceOverwritesInOrderAndFiltersCommittedPathAndOperator()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);

            IStateFlowTrace trace = host.Trace;
            Assert.That(trace, Is.Not.Null);
            Assert.That(trace.Capacity, Is.EqualTo(4));
            Assert.That(trace.Count, Is.EqualTo(4));
            Assert.That(trace.TotalWritten, Is.EqualTo(10UL));
            var latest = new StateFlowTraceEntry[4];
            Assert.That(trace.CopyLatestTo(latest), Is.EqualTo(4));
            CollectionAssert.AreEqual(
                new[]
                {
                    StateFlowTraceKind.OperationSection,
                    StateFlowTraceKind.OperatorOutcome,
                    StateFlowTraceKind.ContextCommit,
                    StateFlowTraceKind.ActivePath
                },
                Array.ConvertAll(latest, entry => entry.Kind));
            Assert.That(latest[0].TickFrame.Tick.Value, Is.EqualTo(2UL));

            var path = new StateFlowTraceEntry[2];
            int pathCount = trace.CopyLatestTo(
                path,
                new StateFlowTraceFilter(
                    StateFlowTraceKind.ActivePath,
                    layerId: ids.LayerId));
            Assert.That(pathCount, Is.EqualTo(1));
            Assert.That(path[0].LayerId, Is.EqualTo(ids.LayerId));
            Assert.That(path[0].StateId, Is.EqualTo(ids.StateId));

            var outcomes = new StateFlowTraceEntry[2];
            int outcomeCount = trace.CopyLatestTo(
                outcomes,
                new StateFlowTraceFilter(
                    StateFlowTraceKind.OperatorOutcome,
                    operatorId: ids.FirstOperatorId));
            Assert.That(outcomeCount, Is.EqualTo(1));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(OperatorOutcomeStatus.Succeeded));
        }

        [Test]
        public void FirstTickTraceKeepsExactDefaultPreviousAndCommittedFrameReferences()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 16);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);

            var entries = new StateFlowTraceEntry[16];
            int count = host.Trace.CopyLatestTo(entries);
            Assert.That(count, Is.GreaterThanOrEqualTo(4));
            Assert.That(entries[0].Kind, Is.EqualTo(StateFlowTraceKind.Tick));
            StateFlowTraceFrameReference previous = entries[0].PreviousContext;
            Assert.That(previous.IsValid, Is.True);
            Assert.That(previous.HasCommittedFrame, Is.False);
            Assert.That(previous.Identity.IsValid, Is.False);
            Assert.That(previous.Revision.IsValid, Is.False);
            Assert.That(previous.LayoutId, Is.EqualTo(host.CurrentContext.Header.LayoutId));
            Assert.That(previous.LayoutVersion, Is.EqualTo(host.CurrentContext.Header.LayoutVersion));
            Assert.That(
                previous.LayoutSchemaHash,
                Is.EqualTo(host.CurrentContext.Header.LayoutSchemaHash));

            int commitIndex = -1;
            int pathIndex = -1;
            for (int index = 0; index < count; index++)
            {
                if (entries[index].Kind == StateFlowTraceKind.ContextCommit)
                {
                    commitIndex = index;
                }
                else if (entries[index].Kind == StateFlowTraceKind.ActivePath)
                {
                    pathIndex = index;
                }
            }

            Assert.That(commitIndex, Is.GreaterThan(0));
            Assert.That(pathIndex, Is.GreaterThan(commitIndex));
            Assert.That(entries[commitIndex].PreviousContext, Is.EqualTo(previous));
            Assert.That(entries[commitIndex].Frame.HasCommittedFrame, Is.True);
            Assert.That(entries[commitIndex].Frame.Revision.Value, Is.EqualTo(1UL));
            Assert.That(entries[pathIndex].Frame, Is.EqualTo(entries[commitIndex].Frame));
        }

        [Test]
        public void CommittedClaimRetainsSameActivationThenReArbitratesAfterActivationChange()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 64);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            Assert.That(high.ExecuteCount, Is.Zero);

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic retained), retained);
            Assert.That(low.ExecuteCount, Is.EqualTo(2));
            Assert.That(high.ExecuteCount, Is.Zero);

            OperatorCommitClaimLogic.RequestTransition = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic transition), transition);
            Assert.That(low.ExecuteCount, Is.EqualTo(3));
            Assert.That(high.ExecuteCount, Is.Zero);
            OperatorCommitClaimLogic.RequestTransition = false;

            Require(host.TryStep(0.02d, out CoCoDiagnostic changed), changed);
            Assert.That(low.ExecuteCount, Is.EqualTo(3));
            Assert.That(high.ExecuteCount, Is.EqualTo(1));

            var entries = new StateFlowTraceEntry[8];
            int transitionCount = host.Trace.CopyLatestTo(
                entries,
                new StateFlowTraceFilter(
                    StateFlowTraceKind.Transition,
                    layerId: ids.LayerId));
            Assert.That(transitionCount, Is.EqualTo(2));
            Assert.That(entries[0].TransitionId, Is.EqualTo(ids.TransitionId));
            Assert.That(
                entries[0].TransitionRole,
                Is.EqualTo(StateFlowTransitionRole.Candidate));
            Assert.That(entries[1].TransitionId, Is.EqualTo(ids.TransitionId));
            Assert.That(
                entries[1].TransitionRole,
                Is.EqualTo(StateFlowTransitionRole.Winner));
            int primarySections = host.Trace.CopyLatestTo(
                entries,
                new StateFlowTraceFilter(
                    StateFlowTraceKind.OperationSection,
                    sectionId: ids.PrimarySectionId));
            int secondarySections = host.Trace.CopyLatestTo(
                entries,
                new StateFlowTraceFilter(
                    StateFlowTraceKind.OperationSection,
                    sectionId: ids.SecondarySectionId));
            Assert.That(primarySections, Is.EqualTo(4));
            Assert.That(secondarySections, Is.EqualTo(4));
        }

        [Test]
        public void AnyClaimSurvivesLeafChangeAndRestoresWithOwnActivation()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            Require(StateGraphProjectBindings.TryInstall(
                new ClaimBindingProvider(ids, anyState: true), out CoCoDiagnostic install), install);
            StateGraphAsset asset = CreateClaimGraphAsset(ids);
            asset.Layers[0].AnyState = new StateGraphStateRecord(
                Serialize(AnyClaimDefault(ids).StateId), default, "Any Claim",
                Serialize(AnyClaimDescriptor), new HostTestStateConfig());
            StateGraphHost host = CreateClaimHost(asset, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            Require(source.Retain());
            try
            {
                StateSlotId anySlot = StandardGraphState.SlotFor(AnyClaimDescriptor);
                GraphStateRecord<byte> sourceAny = ReadClaimGraphState(source, anySlot);
                OperatorClaimState sourceClaim = ReadClaim(source, ids.PrimaryClaimStateSlotId);
                Assert.That(sourceAny.IsOnActivePath || sourceAny.EnterPending, Is.False);
                Assert.That(sourceAny.ActivationId.Value, Is.EqualTo(2UL));
                Assert.That(sourceAny.State, Is.EqualTo(1));
                Assert.That(sourceClaim.ActivationId, Is.EqualTo(sourceAny.ActivationId));

                OperatorCommitClaimLogic.EnableSecondary = true;
                OperatorCommitClaimLogic.RequestTransition = true;
                Require(host.TryStep(0.02d, out CoCoDiagnostic transition), transition);
                OperatorCommitClaimLogic.RequestTransition = false;
                Require(host.TryStep(0.02d, out CoCoDiagnostic changed), changed);
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId), Is.EqualTo(sourceClaim));
                Assert.That(ReadClaimGraphState(host.CurrentContext, anySlot).State, Is.EqualTo(3));
                Assert.That(low.ExecuteCount, Is.EqualTo(3));
                Assert.That(high.ExecuteCount, Is.Zero);
                Require(host.TryPrepareRestore(source, CreateResumedTick(host.CurrentContext.Header.TickFrame),
                    out PreparedActorRestore prepared, out _, out CoCoDiagnostic restore), restore);
                prepared.CommitNoFail();
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.StateId));
                Assert.That(ReadClaimGraphState(host.CurrentContext, anySlot), Is.EqualTo(sourceAny));
                Assert.That(ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId), Is.EqualTo(sourceClaim));
                Require(host.TryStep(0.02d, out CoCoDiagnostic resumed), resumed);
                Assert.That(ReadClaimGraphState(host.CurrentContext, anySlot).State, Is.EqualTo(2));
                Assert.That(ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId), Is.EqualTo(sourceClaim));
            }
            finally
            {
                source.Release();
            }
        }

        [TestCase(OperatorClaimSuspendPolicy.Retain, 2, 0)]
        [TestCase(OperatorClaimSuspendPolicy.Release, 1, 1)]
        public void SuspendPolicyPreservesOrReleasesCompleteClaimSet(
            OperatorClaimSuspendPolicy suspendPolicy,
            int expectedLowExecutions,
            int expectedHighExecutions)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, suspendPolicy);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame firstContext = host.CurrentContext;
            OperatorClaimState firstPrimary = ReadClaim(
                firstContext,
                ids.PrimaryClaimStateSlotId);
            OperatorClaimState firstSecondary = ReadClaim(
                firstContext,
                ids.SecondaryClaimStateSlotId);
            Assert.That(firstPrimary.OwnerOperatorId, Is.EqualTo(ids.FirstOperatorId));
            Assert.That(firstSecondary.IsHeld, Is.False);
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Assert.That(host.CurrentContext, Is.EqualTo(firstContext));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId),
                Is.EqualTo(firstPrimary));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId),
                Is.EqualTo(firstSecondary));
            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            Assert.That(host.CurrentContext, Is.EqualTo(firstContext));
            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);

            Assert.That(low.ExecuteCount, Is.EqualTo(expectedLowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(expectedHighExecutions));
            OperatorId expectedOwner = suspendPolicy == OperatorClaimSuspendPolicy.Retain
                ? ids.FirstOperatorId
                : ids.SecondOperatorId;
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(expectedOwner));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                suspendPolicy == OperatorClaimSuspendPolicy.Retain
                    ? Is.EqualTo(default(OperatorId))
                    : Is.EqualTo(ids.SecondOperatorId));
        }

        [Test]
        public void CancelledTickAfterSuspendReleaseDoesNotMutateCommittedClaimAuthority()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            StateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            ContextFrame committed = host.CurrentContext;
            OperatorClaimState primary = ReadClaim(
                committed,
                ids.PrimaryClaimStateSlotId);
            OperatorClaimState secondary = ReadClaim(
                committed,
                ids.SecondaryClaimStateSlotId);
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            OperatorCommitClaimLogic.EnableSecondary = true;
            high.FailExecution = true;

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(host.CurrentContext, Is.EqualTo(committed));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId),
                Is.EqualTo(primary));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId),
                Is.EqualTo(secondary));
            Assert.That(primary.OwnerOperatorId, Is.EqualTo(ids.FirstOperatorId));
            Assert.That(secondary.IsHeld, Is.False);
        }

        private void InstallProvider(OperatorCommitTestIds ids)
        {
            var provider = new OperatorCommitBindingProvider(ids);
            Require(StateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
        }

        private static bool TryBindPrimaryOperation(
            StateGraphHostBindingBuilder builder,
            OperatorCommitTestIds ids,
            HostTestDiscreteSectionViewFactory factory,
            out CoCoDiagnostic diagnostic) =>
            builder.TryRegisterOperation(
                ids.PrimarySectionId,
                OperationSectionMode.Discrete,
                factory,
                PrimaryOperationFactoryFingerprint,
                out OperationSectionRequirement ignored,
                out diagnostic);

        private static bool TryRegisterPrimaryOperation(
            GraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic) =>
            builder.TryRegisterOperationSection(
                ids.PrimarySectionId,
                OperationSectionMode.Discrete,
                new OperationSectionViewFactoryToken<
                    IHostTestDiscreteSection,
                    HostTestDiscreteSectionViewFactory>(
                    PrimaryOperationFactoryFingerprint),
                out diagnostic);

        private void InstallClaimProvider(OperatorCommitTestIds ids)
        {
            InstallClaimProvider(
                ids,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                ContextRestorePolicy.Stored);
        }

        private void InstallDurableClaimProvider(OperatorCommitTestIds ids)
        {
            InstallClaimProvider(
                ids,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                ContextRestorePolicy.Stored);
        }

        private void InstallClaimProvider(
            OperatorCommitTestIds ids,
            ContextRestorePolicy primaryClaimRestorePolicy,
            ContextRestorePolicy secondaryClaimRestorePolicy)
        {
            InstallClaimProvider(
                ids,
                ContextProjection.Temporal,
                primaryClaimRestorePolicy,
                secondaryClaimRestorePolicy);
        }

        private void InstallClaimProvider(
            OperatorCommitTestIds ids,
            ContextProjection claimProjection,
            ContextRestorePolicy primaryClaimRestorePolicy,
            ContextRestorePolicy secondaryClaimRestorePolicy)
        {
            var provider = new ClaimBindingProvider(
                ids,
                false,
                primaryClaimRestorePolicy,
                secondaryClaimRestorePolicy,
                claimProjection);
            Require(StateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
        }

        private StateGraphHost CreateHost(
            OperatorCommitTestIds ids,
            out GameObject gameObject,
            int eventOutboxCapacity = 0,
            int traceCapacity = 0)
        {
            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new StateGraphStateRecord(
                new SerializedId128(ids.StateId.High, ids.StateId.Low),
                default,
                "Operator Commit Leaf",
                new SerializedId128(ids.StateDescriptorId.High, ids.StateDescriptorId.Low),
                new HostTestStateConfig { Value = 5 });
            var layer = new StateGraphLayerRecord(
                new SerializedId128(ids.LayerId.High, ids.LayerId.Low),
                "Base");
            layer.InitialStateId = new SerializedId128(ids.StateId.High, ids.StateId.Low);
            layer.States.Add(state);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Operator Commit Host");
            _objects.Add(gameObject);
            StateGraphHost host = gameObject.AddComponent<StateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 3);
            SetField(host, "eventOutboxCapacity", eventOutboxCapacity);
            SetField(host, "traceCapacity", traceCapacity);
            return host;
        }

        private StateGraphHost CreateClaimHost(
            OperatorCommitTestIds ids,
            out GameObject gameObject,
            int traceCapacity)
        {
            return CreateClaimHost(
                CreateClaimGraphAsset(ids),
                out gameObject,
                traceCapacity);
        }

        private StateGraphAsset CreateClaimGraphAsset(OperatorCommitTestIds ids)
        {
            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var firstState = new StateGraphStateRecord(
                Serialize(ids.StateId),
                default,
                "Claim Source",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var secondState = new StateGraphStateRecord(
                Serialize(ids.SecondStateId),
                default,
                "Claim Target",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 2 });
            var transition = new StateGraphTransitionRecord(
                Serialize(ids.TransitionId),
                Serialize(ids.StateId),
                Serialize(ids.SecondStateId),
                1);
            var layer = new StateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.StateId);
            layer.States.Add(firstState);
            layer.States.Add(secondState);
            layer.Transitions.Add(transition);
            asset.Layers.Add(layer);
            return asset;
        }

        private StateGraphHost CreateClaimHost(
            StateGraphAsset asset,
            out GameObject gameObject,
            int traceCapacity)
        {
            gameObject = new GameObject("Pre5 Claim Host");
            _objects.Add(gameObject);
            StateGraphHost host = gameObject.AddComponent<StateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 3);
            SetField(host, "eventOutboxCapacity", 0);
            SetField(host, "traceCapacity", traceCapacity);
            return host;
        }

        private byte[] CaptureClaimPersistencePayload(
            OperatorCommitTestIds ids,
            StateGraphAsset asset,
            out StateGraphHost source)
        {
            source = CreateClaimHost(asset, out GameObject sourceObject, traceCapacity: 0);
            var low = sourceObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, OperatorClaimSuspendPolicy.Release);
            var high = sourceObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(source, low, high);

            Require(source.TryStart(out CoCoDiagnostic start), start);
            Require(source.TryStep(0.02d, out CoCoDiagnostic first), first);
            Assert.That(
                ReadClaim(source.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));
            Require(
                source.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic capture),
                capture);
            return payload;
        }

        private static SerializedId128 Serialize(LayerId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateDescriptorId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(TransitionId id) =>
            new SerializedId128(id.High, id.Low);

        private static StateGraphRuntime GetRuntime(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StateGraphRuntime)field.GetValue(host);
        }

        private static StateGraphTransaction GetTransaction(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_transaction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StateGraphTransaction)field.GetValue(host);
        }

        private static StateGraphTemporalController GetTemporal(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_temporal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StateGraphTemporalController)field.GetValue(host);
        }

        private static ulong ReadLastEventSequence(StateGraphTransaction transaction)
        {
            FieldInfo field = typeof(StateGraphTransaction).GetField(
                "_lastEventSequence",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (ulong)field.GetValue(transaction);
        }

        private static StateGraphHostRuntimeBindings GetBindings(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StateGraphHostRuntimeBindings)field.GetValue(host);
        }

        private static OperatorClaimState ReadClaim(
            ContextFrame frame,
            StateSlotId slotId)
        {
            Require(frame.Layout.TryResolveSlot(
                slotId,
                out StateSlot<OperatorClaimState> slot));
            return frame.Read(slot);
        }

        private static GraphStateRecord<byte> ReadClaimGraphState(
            ContextFrame frame,
            StateSlotId slotId)
        {
            Require(frame.Layout.TryResolveSlot(
                slotId,
                out StateSlot<GraphStateRecord<byte>> slot));
            return frame.Read(slot);
        }

        private static TickFrame CreateResumedTick(TickFrame current)
        {
            Require(TimelinePosition.TryCreate(
                current.TimelinePosition.Seconds + 1d,
                out TimelinePosition position));
            Require(TickFrame.TryCreate(
                0.02d,
                current.TimelineId,
                position,
                new TimelineTick(current.Tick.Value + 1UL),
                current.ClockDomainId,
                new ExecutionSequence(current.ExecutionSequence.Value + 1UL),
                new TimelineEpoch(current.TimelineEpoch.Value + 1UL),
                out TickFrame resumed,
                out CoCoDiagnostic diagnostic), diagnostic);
            return resumed;
        }

        private static void SetOperators(StateGraphHost host, params MonoBehaviour[] operators) =>
            SetField(host, "operators", operators);

        private static void SetField<TValue>(
            StateGraphHost host,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(host, value);
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }

        private static void AssertStartupOperatorFailure(StateGraphHost host)
        {
            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidOperatorDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            AssertNoProjectFactoryCallbacks();
        }

        private static void AssertNoProjectFactoryCallbacks()
        {
            Assert.That(OperatorCommitProjectFactoryProbe.LogicFactoryCount, Is.Zero);
            Assert.That(OperatorCommitProjectFactoryProbe.MemoryFactoryCount, Is.Zero);
            Assert.That(OperatorCommitProjectFactoryProbe.MemoryResetCount, Is.Zero);
            Assert.That(OperatorCommitProjectFactoryProbe.MemoryFingerprintCount, Is.Zero);
        }

        private static void AssertTransactionPreflightFailure(
            StateGraphHost host,
            IStateGraphProjectBindingProvider provider,
            CoCoDiagnosticDomain expectedDomain,
            CoCoDiagnosticCode expectedCode)
        {
            StateGraphAssetCompileResult compileResult =
                new StateGraphAssetCompiler().Compile(host.StateGraphAsset, provider.Catalog);
            Require(compileResult.Succeeded);
            Require(GraphInstanceId.TryCreate(9001UL, out GraphInstanceId graphInstanceId));
            var builder = new StateGraphHostBindingBuilder(
                compileResult.Graph,
                graphInstanceId,
                host);
            Require(provider.TryConfigure(builder, out CoCoDiagnostic configure), configure);
            Require(builder.TryFreeze(
                out StateGraphHostRuntimeBindings bindings,
                out CoCoDiagnostic freeze), freeze);

            StateGraphTransaction transaction = null;
            try
            {
                Assert.That(StateGraphTransaction.TryPreflight(
                    host,
                    compileResult.Graph,
                    graphInstanceId,
                    bindings.ContextLayout,
                    bindings.Operations,
                    bindings.ContextProducers,
                    host.ContextFrameCapacity,
                    host.EventOutboxCapacity,
                    host.TraceCapacity,
                    out transaction,
                    out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Domain, Is.EqualTo(expectedDomain));
                Assert.That(failure.Code, Is.EqualTo(expectedCode));
            }
            finally
            {
                transaction?.Dispose();
                bindings.Dispose();
            }

            AssertNoProjectFactoryCallbacks();
        }

        private static class OperatorCommitHostBindingFixture
        {
            public static bool TryBindSingleState(
                StateGraphHostBindingBuilder builder,
                OperatorCommitTestIds ids,
                out CoCoDiagnostic diagnostic)
            {
                GraphStateRecord<int> first =
                    OperatorCommitGraphContextFixture.CreateActiveState(ids, 0);
                return builder.TryBindGraphStateSlot<
                    HostTestMemory,
                    int,
                    OperatorCommitHostMemoryBinding>(
                    ids.LayerId,
                    ids.StateId,
                    ids.GraphStateBlockId,
                    ids.FirstGraphStateSlotId,
                    first,
                    OperatorCommitGraphContextFixture.FirstStateDefaultFingerprint,
                    new OperatorCommitHostMemoryBinding(),
                    out diagnostic);
            }

            public static bool TryBindClaimGraph(
                StateGraphHostBindingBuilder builder,
                OperatorCommitTestIds ids,
                out CoCoDiagnostic diagnostic) =>
                TryBindClaimGraph(builder, ids, false, out diagnostic);

            public static bool TryBindClaimGraph(
                StateGraphHostBindingBuilder builder,
                OperatorCommitTestIds ids,
                bool mismatchPrimaryIdentity,
                out CoCoDiagnostic diagnostic)
            {
                GraphStateRecord<byte> first =
                    OperatorCommitGraphContextFixture.CreateActiveState(ids, (byte)0);
                GraphStateRecord<byte> second =
                    OperatorCommitGraphContextFixture.CreateInactiveState(ids, (byte)0);
                OperatorClaimState primary = OperatorClaimState.Unheld(
                    mismatchPrimaryIdentity ? ids.SecondaryClaimId : ids.PrimaryClaimId,
                    ids.PrimarySectionId);
                OperatorClaimState secondary = OperatorClaimState.Unheld(
                    ids.SecondaryClaimId,
                    ids.SecondarySectionId);
                var memoryBinding = new OperatorCommitClaimMemoryBinding();
                return builder.TryBindGraphStateSlot<
                           OperatorCommitClaimMemory,
                           byte,
                           OperatorCommitClaimMemoryBinding>(
                           ids.LayerId,
                           ids.StateId,
                           ids.GraphStateBlockId,
                           ids.FirstGraphStateSlotId,
                           first,
                           OperatorCommitGraphContextFixture.FirstStateDefaultFingerprint,
                           memoryBinding,
                           out diagnostic) &&
                       builder.TryBindGraphStateSlot<
                           OperatorCommitClaimMemory,
                           byte,
                           OperatorCommitClaimMemoryBinding>(
                           ids.LayerId,
                           ids.SecondStateId,
                           ids.GraphStateBlockId,
                           ids.SecondGraphStateSlotId,
                           second,
                           OperatorCommitGraphContextFixture.SecondStateDefaultFingerprint,
                           memoryBinding,
                           out diagnostic) &&
                       builder.TryBindClaimStateSlot(
                           ids.GraphStateBlockId,
                           ids.PrimaryClaimStateSlotId,
                           primary,
                           OperatorCommitGraphContextFixture.PrimaryClaimDefaultFingerprint,
                           out diagnostic) &&
                       builder.TryBindClaimStateSlot(
                           ids.GraphStateBlockId,
                           ids.SecondaryClaimStateSlotId,
                           secondary,
                           OperatorCommitGraphContextFixture.SecondaryClaimDefaultFingerprint,
                           out diagnostic);
            }
        }

        private sealed class OperatorCommitBindingProvider : IStateGraphProjectBindingProvider
        {
            private readonly OperatorCommitTestIds _ids;
            private readonly HostTestDiscreteSectionViewFactory _primary =
                new HostTestDiscreteSectionViewFactory();

            public OperatorCommitBindingProvider(OperatorCommitTestIds ids)
            {
                _ids = ids;
                Catalog = BuildCatalog(ids);
            }

            public GraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                StateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                const int defaultValue = 5;
                if (!TryBindPrimaryOperation(builder, _ids, _primary, out diagnostic) ||
                    !builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        out diagnostic) ||
                    !OperatorCommitHostBindingFixture.TryBindSingleState(
                        builder,
                        _ids,
                        out diagnostic))
                {
                    return false;
                }

                var stateFactory = new StateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordLogicFactory();
                        return new HostTestLogic(
                            context.GraphInstanceId,
                            operationHandle: _primary.Handle,
                            operationField: _primary.ValueField);
                    },
                    () =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordMemoryFactory();
                        return new HostTestMemory();
                    },
                    (source, destination) => destination.Value = source.Value,
                    memory =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordMemoryReset();
                        memory.Value = 0;
                    },
                    memory =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordMemoryFingerprint();
                        return HostTestLogic.GetMemoryFingerprint(memory);
                    });
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static GraphDescriptorCatalog BuildCatalog(OperatorCommitTestIds ids)
            {
                var builder = new GraphDescriptorCatalogBuilder();
                Require(TryRegisterPrimaryOperation(
                    builder,
                    ids,
                    out CoCoDiagnostic operation), operation);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    StateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    ContextProjection.Temporal,
                    ContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(OperatorCommitGraphContextFixture.TryRegisterSingleState(
                    builder,
                    ids,
                    out CoCoDiagnostic graphContext), graphContext);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId },
                    new[] { ids.StateBlockId, ids.GraphStateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        public enum CodecBindingMode
        {
            ExactCustom = 0,
            MissingCustom = 1,
            MismatchedCustom = 2,
            ExtraOnRaw = 3
        }

        private sealed class CodecBindingProvider : IStateGraphProjectBindingProvider
        {
            private readonly OperatorCommitTestIds _ids;
            private readonly CodecBindingMode _mode;
            private readonly HostTestDiscreteSectionViewFactory _primary =
                new HostTestDiscreteSectionViewFactory();

            public CodecBindingProvider(OperatorCommitTestIds ids, CodecBindingMode mode)
            {
                _ids = ids;
                _mode = mode;
                if (!CodecId.TryCreate(520UL, 1UL, out CodecId manifestCodecId) ||
                    !CodecId.TryCreate(520UL, 2UL, out CodecId mismatchCodecId))
                {
                    throw new InvalidOperationException("Context codec fixture ids are invalid.");
                }

                ManifestCodec = mode == CodecBindingMode.ExtraOnRaw
                    ? default
                    : new CodecDescriptor(manifestCodecId, 1U);
                CodecDescriptor boundDescriptor = mode == CodecBindingMode.MismatchedCustom
                    ? new CodecDescriptor(mismatchCodecId, 1U)
                    : new CodecDescriptor(manifestCodecId, 1U);
                BoundCodec = mode == CodecBindingMode.MissingCustom
                    ? null
                    : new Int32ContextCodec(boundDescriptor);
                Catalog = BuildCatalog(ids, ManifestCodec);
            }

            public GraphDescriptorCatalog Catalog { get; }
            public CodecDescriptor ManifestCodec { get; }
            public Int32ContextCodec BoundCodec { get; }

            public bool TryConfigure(
                StateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                const int defaultValue = 5;
                if (!TryBindPrimaryOperation(builder, _ids, _primary, out diagnostic))
                {
                    return false;
                }

                bool bound = _mode == CodecBindingMode.MissingCustom
                    ? builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        out diagnostic)
                    : builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        BoundCodec,
                        out diagnostic);
                if (!bound)
                {
                    return false;
                }

                if (!OperatorCommitHostBindingFixture.TryBindSingleState(
                        builder,
                        _ids,
                        out diagnostic))
                {
                    return false;
                }

                var stateFactory = new StateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context => new HostTestLogic(
                        context.GraphInstanceId,
                        operationHandle: _primary.Handle,
                        operationField: _primary.ValueField),
                    () => new HostTestMemory(),
                    (source, destination) => destination.Value = source.Value,
                    memory => memory.Value = 0,
                    HostTestLogic.GetMemoryFingerprint);
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static GraphDescriptorCatalog BuildCatalog(
                OperatorCommitTestIds ids,
                CodecDescriptor codec)
            {
                var builder = new GraphDescriptorCatalogBuilder();
                Require(TryRegisterPrimaryOperation(
                    builder,
                    ids,
                    out CoCoDiagnostic operation), operation);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    StateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    ContextProjection.Temporal,
                    ContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    codec,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(OperatorCommitGraphContextFixture.TryRegisterSingleState(
                    builder,
                    ids,
                    out CoCoDiagnostic graphContext), graphContext);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId },
                    new[] { ids.StateBlockId, ids.GraphStateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        public sealed class Int32ContextCodec : IContextValueCodec<int>
        {
            public Int32ContextCodec(CodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;
            public Action EncodeCallback { get; set; }
            public int EncodeCount { get; private set; }

            public bool TryEncode(
                in int value,
                Span<byte> destination,
                out int bytesWritten)
            {
                if (destination.Length < 4)
                {
                    bytesWritten = 0;
                    return false;
                }

                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                destination[3] = (byte)(value >> 24);
                bytesWritten = 4;
                EncodeCount++;
                EncodeCallback?.Invoke();
                return true;
            }

            public bool TryDecode(
                ReadOnlySpan<byte> source,
                out int value,
                out int bytesRead)
            {
                if (source.Length < 4)
                {
                    value = 0;
                    bytesRead = 0;
                    return false;
                }

                value = source[0] |
                        (source[1] << 8) |
                        (source[2] << 16) |
                        (source[3] << 24);
                bytesRead = 4;
                return true;
            }
        }

        internal sealed class TemporalCodecRestoreBinding :
            MonoBehaviour,
            IContextRestoreBinding
        {
            public TemporalRestoreFixtureFailure Failure { get; set; }

            public bool TryApply(
                in ContextRestoreBindingContext context,
                out CoCoDiagnostic diagnostic)
            {
                if (!context.IsValid)
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Context,
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "Temporal Codec restore fixture received an invalid reader.");
                    return false;
                }

                if (context.ApplyKind == ContextRestoreApplyKind.Confirm)
                {
                    switch (Failure)
                    {
                        case TemporalRestoreFixtureFailure.Reject:
                            diagnostic = CoCoDiagnostic.Error(
                                CoCoDiagnosticDomain.Context,
                                CoCoDiagnosticCode.InvalidRestoreMetadata,
                                "Temporal Codec restore fixture rejected Confirm.");
                            return false;
                        case TemporalRestoreFixtureFailure.Throw:
                            throw new InvalidOperationException(
                                "Temporal Codec restore fixture threw during Confirm.");
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private static StateDescriptorId AnyClaimDescriptor
        {
            get
            {
                StandardDescriptors.TryCreate(typeof(AnyClaimLogic), nameof(AnyClaimLogic), out var descriptor);
                return descriptor;
            }
        }

        private static GraphStateRecord<byte> AnyClaimDefault(OperatorCommitTestIds ids)
        {
            Require(StateId.TryCreate(ids.StateId.High, 999UL, out StateId stateId));
            Require(ActivationId.TryCreate(2UL, out ActivationId activation));
            Require(GraphStateRecord<byte>.TryCreate(ids.LayerId, stateId, false, activation,
                0d, 0d, false, 0UL, 0, out GraphStateRecord<byte> record));
            return record;
        }

        private sealed class ClaimBindingProvider : IStateGraphProjectBindingProvider
        {
            private const ulong PrimaryFactoryFingerprint = 5091UL;
            private const ulong SecondaryFactoryFingerprint = 5092UL;
            private const ulong AnyDefaultFingerprint = 50535UL;
            private readonly OperatorCommitTestIds _ids;
            private readonly bool _mismatchPrimaryClaimDefault;
            private readonly bool _anyState;
            private readonly OperatorCommitPrimarySectionFactory _primary =
                new OperatorCommitPrimarySectionFactory();
            private readonly OperatorCommitSecondarySectionFactory _secondary =
                new OperatorCommitSecondarySectionFactory();

            public ClaimBindingProvider(
                OperatorCommitTestIds ids,
                bool mismatchPrimaryClaimDefault = false,
                ContextRestorePolicy primaryClaimRestorePolicy =
                    ContextRestorePolicy.Stored,
                ContextRestorePolicy secondaryClaimRestorePolicy =
                    ContextRestorePolicy.Stored,
                ContextProjection claimProjection =
                    ContextProjection.Temporal,
                bool anyState = false)
            {
                _ids = ids;
                _mismatchPrimaryClaimDefault = mismatchPrimaryClaimDefault;
                _anyState = anyState;
                Catalog = BuildCatalog(
                    ids,
                    primaryClaimRestorePolicy,
                    secondaryClaimRestorePolicy,
                    claimProjection,
                    anyState);
            }

            public GraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                StateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                if (!builder.TryRegisterOperation(
                        _ids.PrimarySectionId,
                        OperationSectionMode.Discrete,
                        _primary,
                        PrimaryFactoryFingerprint,
                        out OperationSectionRequirement primary,
                        out diagnostic) ||
                    !builder.TryRegisterOperation(
                        _ids.SecondarySectionId,
                        OperationSectionMode.Discrete,
                        _secondary,
                        SecondaryFactoryFingerprint,
                        out OperationSectionRequirement secondary,
                        out diagnostic))
                {
                    return false;
                }

                const int defaultValue = 5;
                if (!builder.TryBindContextSlot(
                        _ids.StateBlockId,
                        _ids.StateSlotId,
                        defaultValue,
                        ContextDefaultFingerprint,
                        out diagnostic) ||
                    !OperatorCommitHostBindingFixture.TryBindClaimGraph(
                        builder,
                        _ids,
                        _mismatchPrimaryClaimDefault,
                        out diagnostic))
                {
                    return false;
                }

                var stateFactory = new StateRuntimeFactory<
                    OperatorCommitClaimLogic,
                    OperatorCommitClaimMemory>(
                    context =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordLogicFactory();
                        return new OperatorCommitClaimLogic(
                            context,
                            _primary.Handle,
                            _primary.ValueField,
                            _secondary.Handle,
                            _secondary.ValueField);
                    },
                    () =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordMemoryFactory();
                        return new OperatorCommitClaimMemory();
                    },
                    (source, destination) => destination.Value = source.Value,
                    memory =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordMemoryReset();
                        memory.Value = 0;
                    },
                    memory =>
                    {
                        OperatorCommitProjectFactoryProbe.RecordMemoryFingerprint();
                        return unchecked((ulong)(uint)memory.Value);
                    });
                if (_anyState)
                {
                    var anyFactory = new StateRuntimeFactory<AnyClaimLogic, OperatorCommitClaimMemory>(
                        context => new AnyClaimLogic(new OperatorCommitClaimLogic(context,
                            _primary.Handle, _primary.ValueField, _secondary.Handle, _secondary.ValueField)),
                        () => new OperatorCommitClaimMemory(),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        memory => unchecked((ulong)(uint)memory.Value));
                    GraphStateRecord<byte> record = AnyClaimDefault(_ids);
                    if (!builder.TryBindState(AnyClaimDescriptor, anyFactory, out diagnostic) ||
                        !builder.TryBindGraphStateSlot<OperatorCommitClaimMemory, byte, OperatorCommitClaimMemoryBinding>(
                            _ids.LayerId, record.StateId, _ids.GraphStateBlockId,
                            StandardGraphState.SlotFor(AnyClaimDescriptor), record, AnyDefaultFingerprint,
                            new OperatorCommitClaimMemoryBinding(), out diagnostic))
                    {
                        return false;
                    }
                }
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static GraphDescriptorCatalog BuildCatalog(
                OperatorCommitTestIds ids,
                ContextRestorePolicy primaryClaimRestorePolicy,
                ContextRestorePolicy secondaryClaimRestorePolicy,
                ContextProjection claimProjection,
                bool anyState)
            {
                var builder = new GraphDescriptorCatalogBuilder();
                Require(builder.TryRegisterOperationSection(
                    ids.PrimarySectionId,
                    OperationSectionMode.Discrete,
                    new OperationSectionViewFactoryToken<
                        IOperatorCommitPrimarySection,
                        OperatorCommitPrimarySectionFactory>(PrimaryFactoryFingerprint),
                    out CoCoDiagnostic primary), primary);
                Require(builder.TryRegisterOperationSection(
                    ids.SecondarySectionId,
                    OperationSectionMode.Discrete,
                    new OperationSectionViewFactoryToken<
                        IOperatorCommitSecondarySection,
                        OperatorCommitSecondarySectionFactory>(SecondaryFactoryFingerprint),
                    out CoCoDiagnostic secondary), secondary);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    StateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    ContextProjection.Temporal,
                    ContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(OperatorCommitGraphContextFixture.TryRegisterClaimGraph(
                    builder,
                    ids,
                    claimProjection,
                    primaryClaimRestorePolicy,
                    secondaryClaimRestorePolicy,
                    out CoCoDiagnostic graphContext), graphContext);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        OperatorCommitClaimLogic,
                        HostTestStateConfigSchema,
                        OperatorCommitClaimMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId, ids.SecondarySectionId },
                    new[] { ids.StateBlockId, ids.GraphStateBlockId },
                    out CoCoDiagnostic state), state);
                if (anyState)
                {
                    Require(builder.TryRegisterState(AnyClaimDescriptor, 1U, new HostTestStateConfigFreezer(),
                        new StateRuntimeRegistration<AnyClaimLogic, HostTestStateConfigSchema,
                            OperatorCommitClaimMemory>(HostTestSchemas.State, false), null,
                        new[] { ids.PrimarySectionId, ids.SecondarySectionId },
                        new[] { ids.StateBlockId, ids.GraphStateBlockId }, out CoCoDiagnostic any), any);
                    Require(builder.TryRegisterStateSlot(ids.GraphStateBlockId,
                        StandardGraphState.SlotFor(AnyClaimDescriptor), claimProjection, ContextRestorePolicy.Stored,
                        AnyClaimDefault(ids), AnyDefaultFingerprint, default, null, out any), any);
                }
                Require(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        private sealed class LowClaimOperator : MonoBehaviour, IOperator
        {
            private OperatorCommitTestIds _ids;
            private OperatorClaimSuspendPolicy _suspendPolicy;
            private OperationSectionRequirement _primary;
            private OperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }

            public bool FailExecution { get; set; }

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new OperatorDescriptorBuilder();
                    CoCoDiagnostic require = default;
                    CoCoDiagnostic claimDiagnostic = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            OperationSectionMode.Discrete,
                            out _primary,
                            out require) ||
                        !builder.TryClaim(
                            _ids.PrimaryClaimId,
                            _primary,
                            _ids.PrimaryClaimStateSlotId,
                            1,
                            _suspendPolicy,
                            out OperatorClaimRequirement claim,
                            out claimDiagnostic) ||
                        !builder.TryOwnOutcome<int>(
                            _ids.StateSlotId,
                            out outcome) ||
                        !builder.TryFreeze<LowClaimOperator>(
                            _ids.FirstOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            FirstError(require, claimDiagnostic, outcome, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(
                OperatorCommitTestIds ids,
                OperatorClaimSuspendPolicy suspendPolicy)
            {
                _ids = ids;
                _suspendPolicy = suspendPolicy;
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                ExecuteCount++;
                if (FailExecution)
                {
                    outcome = default;
                    return false;
                }

                if (!context.TryGet(
                        _primary,
                        out OperationSectionEntry<IOperatorCommitPrimarySection> section) ||
                    !section.Header.Enabled ||
                    !context.PreviousContext.Layout.TryResolveSlot(
                        _ids.StateSlotId,
                        out StateSlot<int> slot) ||
                    !context.TryWriteOutcome(slot, context.PreviousContext.Read(slot) + 1))
                {
                    outcome = default;
                    return false;
                }

                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class HighClaimOperator : MonoBehaviour, IOperator
        {
            private OperatorCommitTestIds _ids;
            private OperationSectionRequirement _primary;
            private OperationSectionRequirement _secondary;
            private OperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public bool FailExecution { get; set; }

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new OperatorDescriptorBuilder();
                    CoCoDiagnostic primaryDiagnostic = default;
                    CoCoDiagnostic secondaryDiagnostic = default;
                    CoCoDiagnostic primaryClaimDiagnostic = default;
                    CoCoDiagnostic secondaryClaimDiagnostic = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            OperationSectionMode.Discrete,
                            out _primary,
                            out primaryDiagnostic) ||
                        !builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            OperationSectionMode.Discrete,
                            out _secondary,
                            out secondaryDiagnostic) ||
                        !builder.TryClaim(
                            _ids.PrimaryClaimId,
                            _primary,
                            _ids.PrimaryClaimStateSlotId,
                            100,
                            OperatorClaimSuspendPolicy.Retain,
                            out OperatorClaimRequirement primaryClaim,
                            out primaryClaimDiagnostic) ||
                        !builder.TryClaim(
                            _ids.SecondaryClaimId,
                            _secondary,
                            _ids.SecondaryClaimStateSlotId,
                            100,
                            OperatorClaimSuspendPolicy.Retain,
                            out OperatorClaimRequirement secondaryClaim,
                            out secondaryClaimDiagnostic) ||
                        !builder.TryFreeze<HighClaimOperator>(
                            _ids.SecondOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(FirstError(
                            primaryDiagnostic,
                            secondaryDiagnostic,
                            primaryClaimDiagnostic,
                            secondaryClaimDiagnostic,
                            freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(OperatorCommitTestIds ids)
            {
                _ids = ids;
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                ExecuteCount++;
                if (FailExecution)
                {
                    outcome = default;
                    return false;
                }

                if (!context.TryGet(
                        _primary,
                        out OperationSectionEntry<IOperatorCommitPrimarySection> primary) ||
                    !context.TryGet(
                        _secondary,
                        out OperationSectionEntry<IOperatorCommitSecondarySection> secondary) ||
                    !primary.Header.Enabled ||
                    !secondary.Header.Enabled)
                {
                    outcome = default;
                    return false;
                }

                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class DuplicateIdOperator : MonoBehaviour, IOperator
        {
            private OperatorId _operatorId;
            private OperationSectionId _sectionId;
            private OperatorDescriptor _descriptor;

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor == null)
                    {
                        var builder = new OperatorDescriptorBuilder();
                        CoCoDiagnostic requirement = default;
                        CoCoDiagnostic freeze = default;
                        if (!builder.TryRequire<IHostTestDiscreteSection>(
                                _sectionId,
                                OperationSectionMode.Discrete,
                                out OperationSectionRequirement ignored,
                                out requirement) ||
                            !builder.TryFreeze<DuplicateIdOperator>(
                                _operatorId,
                                out _descriptor,
                                out freeze))
                        {
                            throw new InvalidOperationException(FirstError(requirement, freeze));
                        }
                    }

                    return _descriptor;
                }
            }

            public void Configure(
                OperatorId operatorId,
                OperationSectionId sectionId)
            {
                _operatorId = operatorId;
                _sectionId = sectionId;
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class ExtraneousRequirementOperator : MonoBehaviour, IOperator
        {
            private OperatorCommitTestIds _ids;
            private OperatorDescriptor _descriptor;

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new OperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            OperationSectionMode.Discrete,
                            out OperationSectionRequirement ignored,
                            out requirement) ||
                        !builder.TryOwnOutcome<int>(_ids.StateSlotId, out outcome) ||
                        !builder.TryFreeze<ExtraneousRequirementOperator>(
                            _ids.FirstOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            FirstError(requirement, outcome, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(OperatorCommitTestIds ids) => _ids = ids;

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class NoOpSecondaryOperator : MonoBehaviour, IOperator
        {
            private OperatorCommitTestIds _ids;
            private OperatorId _operatorId;
            private OperationSectionRequirement _secondary;
            private OperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public bool SawDisabledSection { get; private set; }

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new OperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            OperationSectionMode.Discrete,
                            out _secondary,
                            out requirement) ||
                        !builder.TryFreeze<NoOpSecondaryOperator>(
                            _operatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(FirstError(requirement, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(OperatorCommitTestIds ids)
            {
                _ids = ids;
                if (!OperatorId.TryCreate(506UL, 3UL, out _operatorId))
                {
                    throw new InvalidOperationException("No-Op Operator fixture id is invalid.");
                }
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.TryGet(
                        _secondary,
                        out OperationSectionEntry<IOperatorCommitSecondarySection> section))
                {
                    outcome = default;
                    return false;
                }

                SawDisabledSection = !section.Header.Enabled;
                outcome = SawDisabledSection
                    ? OperatorOutcome.NoOp
                    : OperatorOutcome.Success;
                return true;
            }
        }

        private static string FirstError(params CoCoDiagnostic[] diagnostics)
        {
            for (int index = 0; index < diagnostics.Length; index++)
            {
                if (diagnostics[index].IsError)
                {
                    return diagnostics[index].Message;
                }
            }

            return "Operator descriptor setup failed without an error diagnostic.";
        }

        private sealed class ContextWritingOperator : MonoBehaviour, IOperator
        {
            private OperatorId _operatorId;
            private OperationSectionId _sectionId;
            private StateSlotId _slotId;
            private StateSlotId _unauthorizedSlotId;
            private OperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public int PreviousValue { get; private set; }
            public bool PreviousHadCommittedFrame { get; private set; }
            public bool FailAfterWorldMutation { get; set; }
            public bool AttemptUnauthorizedOutcome { get; set; }

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new OperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IHostTestDiscreteSection>(
                            _sectionId,
                            OperationSectionMode.Discrete,
                            out OperationSectionRequirement ignored,
                            out requirement) ||
                        !builder.TryOwnOutcome<int>(_slotId, out outcome) ||
                        !builder.TryFreeze<ContextWritingOperator>(
                            _operatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            FirstError(requirement, outcome, freeze));
                    }

                    return _descriptor;
                }
            }

            public void Configure(
                OperatorId operatorId,
                StateSlotId slotId,
                OperationSectionId sectionId)
            {
                _operatorId = operatorId;
                _slotId = slotId;
                _sectionId = sectionId;
                if (!StateSlotId.TryCreate(999UL, 1UL, out _unauthorizedSlotId))
                {
                    throw new InvalidOperationException("Unauthorized Slot fixture id is invalid.");
                }
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                ExecuteCount++;
                PreviousHadCommittedFrame = context.PreviousContext.HasCommittedFrame;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _slotId,
                        out StateSlot<int> slot))
                {
                    outcome = default;
                    return false;
                }

                PreviousValue = context.PreviousContext.Read(slot);
                if (FailAfterWorldMutation)
                {
                    transform.localPosition = new Vector3(17f, 0f, 0f);
                    outcome = default;
                    return false;
                }

                if (AttemptUnauthorizedOutcome)
                {
                    context.TryWriteOutcome(_unauthorizedSlotId, PreviousValue + 10);
                    outcome = OperatorOutcome.Success;
                    return true;
                }

                if (!context.TryWriteOutcome(slot, PreviousValue + 1))
                {
                    outcome = default;
                    return false;
                }

                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class OutboxOperator : MonoBehaviour, IOperator
        {
            private StateGraphHost _host;
            private OperatorCommitTestIds _ids;
            private OperatorDescriptor _descriptor;
            private EventOutboxRequirement _eventA;
            private EventOutboxRequirement _eventB;

            public int ExecuteCount { get; private set; }
            public bool PublishEvents { get; set; } = true;
            public bool AttemptInvalidTarget { get; set; }

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new OperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic eventA = default;
                    CoCoDiagnostic eventB = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IHostTestDiscreteSection>(
                            _ids.PrimarySectionId,
                            OperationSectionMode.Discrete,
                            out OperationSectionRequirement ignored,
                            out requirement) ||
                        !builder.TryOwnOutcome<int>(_ids.StateSlotId, out outcome) ||
                        !builder.TryEmit<OperatorCommitEventA>(
                            _ids.EventTypeA,
                            _ids.EventDomainId,
                            2,
                            out _eventA,
                            out eventA) ||
                        !builder.TryEmit<OperatorCommitEventB>(
                            _ids.EventTypeB,
                            _ids.EventDomainId,
                            2,
                            out _eventB,
                            out eventB) ||
                        !builder.TryFreeze<OutboxOperator>(
                            _ids.FirstOperatorId,
                            out _descriptor,
                            out freeze))
                    {
                        throw new InvalidOperationException(
                            requirement.IsError ? requirement.Message :
                            outcome.IsError ? outcome.Message :
                            eventA.IsError ? eventA.Message :
                            eventB.IsError ? eventB.Message : freeze.Message);
                    }

                    return _descriptor;
                }
            }

            public void Configure(StateGraphHost host, OperatorCommitTestIds ids)
            {
                _host = host;
                _ids = ids;
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _ids.StateSlotId,
                        out StateSlot<int> slot) ||
                    !context.TryWriteOutcome(slot, context.PreviousContext.Read(slot) + 1))
                {
                    outcome = default;
                    return false;
                }

                if (!PublishEvents)
                {
                    outcome = OperatorOutcome.Success;
                    return true;
                }

                if (AttemptInvalidTarget)
                {
                    var invalidPayload = new OperatorCommitEventA { Value = 99 };
                    context.EventOutbox.TryWrite(_eventA, default, invalidPayload);
                    outcome = OperatorOutcome.Success;
                    return true;
                }

                if (!EventOutboxTarget.TryTargeted(
                        _host.GraphInstanceId,
                        EventReliability.Reliable,
                        default,
                        default,
                        default,
                        out EventOutboxTarget target) ||
                    context.EventOutbox.TryWrite(
                        _eventA,
                        target,
                        new OperatorCommitEventA { Value = 1 }) !=
                    EventOutboxWriteResult.Accepted ||
                    context.EventOutbox.TryWrite(
                        _eventB,
                        target,
                        new OperatorCommitEventB { Value = 2 }) !=
                    EventOutboxWriteResult.Accepted)
                {
                    outcome = default;
                    return false;
                }

                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class PublishLifecycleListener :
            ICoCoEventListener<EventPacket<OperatorCommitEventA>>,
            ICoCoEventListener<EventPacket<OperatorCommitEventB>>
        {
            private readonly StateGraphHost _host;
            private readonly bool _requestLifecycle;

            public PublishLifecycleListener(
                StateGraphHost host,
                bool requestLifecycle = true)
            {
                _host = host;
                _requestLifecycle = requestLifecycle;
            }

            public List<string> Order { get; } = new List<string>();
            public List<ulong> Sequences { get; } = new List<ulong>();
            public ulong ObservedRevision { get; private set; }
            public ulong ObservedTick { get; private set; }
            public bool ReentrantStepAccepted { get; private set; }
            public bool StopAccepted { get; private set; }
            public bool DisposeAccepted { get; private set; }
            public RuntimeLifecycleState LifecycleDuringPublish { get; private set; }

            public void OnEvent(ref EventPacket<OperatorCommitEventA> eventData)
            {
                Order.Add("A");
                Sequences.Add(eventData.Envelope.SourceEventSequence.Value);
                ObservedRevision = _host.CurrentContext.Revision.Value;
                ObservedTick = _host.CurrentContext.Header.TickFrame.Tick.Value;
                if (!_requestLifecycle)
                {
                    return;
                }

                ReentrantStepAccepted = _host.TryStep(0.01d, out _);
                StopAccepted = _host.TryStop(out _);
                DisposeAccepted = _host.TryDispose(out _);
                LifecycleDuringPublish = _host.Lifecycle;
            }

            public void OnEvent(ref EventPacket<OperatorCommitEventB> eventData)
            {
                Order.Add("B");
                Sequences.Add(eventData.Envelope.SourceEventSequence.Value);
            }
        }
    }
}
