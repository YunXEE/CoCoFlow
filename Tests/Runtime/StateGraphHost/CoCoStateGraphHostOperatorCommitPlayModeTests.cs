using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostOperatorCommitPlayModeTests
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
            CoCoStateGraphProjectBindings.ResetForTests();
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
            CoCoStateGraphProjectBindings.ResetForTests();
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
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
                out CoCoStateSlot<int> slot));
            Assert.That(host.CurrentContext.Read(slot), Is.EqualTo(6));
        }

        [Test]
        public void OperatorFailureAfterWorldMutationKeepsOldAuthorityAndMarksCorrection()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame committed = host.CurrentContext;
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);
            var listener = new PublishLifecycleListener(host);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventB>>(listener);
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);
            }
            finally
            {
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventB>>(listener);
            }

            CollectionAssert.AreEqual(new[] { "A", "B" }, listener.Order);
            CollectionAssert.AreEqual(new[] { 1UL, 2UL }, listener.Sequences);
            Assert.That(listener.ObservedRevision, Is.EqualTo(1UL));
            Assert.That(listener.ObservedTick, Is.EqualTo(1UL));
            Assert.That(listener.ReentrantStepAccepted, Is.False);
            Assert.That(listener.StopAccepted, Is.True);
            Assert.That(listener.DisposeAccepted, Is.True);
            Assert.That(listener.LifecycleDuringPublish, Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Disposed));
        }

        [Test]
        public void IgnoredInvalidOutboxWriteFaultsAndPublishesNothing()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 4);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            runtimeOperator.AttemptInvalidTarget = true;
            SetOperators(host, runtimeOperator);
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.OperatorExecutionFailed));
            }
            finally
            {
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, eventOutboxCapacity: 3);
            var runtimeOperator = gameObject.AddComponent<OutboxOperator>();
            runtimeOperator.Configure(host, ids);
            SetOperators(host, runtimeOperator);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.EventOutbox));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.EventOutboxOverflow));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            AssertNoProjectFactoryCallbacks();
        }

        [Test]
        public void TransactionPreflightRejectsInvalidOperatorBeforeProjectFactoryCallbacks()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new OperatorCommitBindingProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
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
            CoCoStateGraphHost host = CreateHost(
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
            Require(CoCoStateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Operator));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidOperatorDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            AssertNoProjectFactoryCallbacks();
        }

        [Test]
        public void ExactCustomContextCodecIsFrozenIntoRuntimeBindings()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, CodecBindingMode.ExactCustom);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            CoCoContextCodecRegistry codecs = GetBindings(host).ContextCodecs;
            Assert.That(codecs.IsFrozen, Is.True);
            Assert.That(codecs.Count, Is.EqualTo(1));
            Assert.That(codecs.TryResolve(
                provider.ManifestCodec,
                out ICoCoContextValueCodec<int> resolved,
                out CoCoDiagnosticCode diagnosticCode), Is.True, diagnosticCode.ToString());
            Assert.That(resolved, Is.SameAs(provider.BoundCodec));
        }

        [Test]
        public void DestroyDuringTemporalCodecCaptureCancelsAuthorityOutboxSequenceAndHistory()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            var provider = new CodecBindingProvider(ids, CodecBindingMode.ExactCustom);
            Require(CoCoStateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install);
            CoCoStateGraphHost host = CreateHost(
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

            CoCoStateGraphRuntime runtime = GetRuntime(host);
            CoCoStateGraphTransaction transaction = GetTransaction(host);
            CoCoStateGraphTemporalController temporal = GetTemporal(host);
            ICoCoStateFlowTrace trace = host.Trace;
            Assert.That(temporal.State.Mode, Is.EqualTo(CoCoTemporalMode.Ready));
            Assert.That(temporal.State.Count, Is.Zero);
            int historyCountDuringDestroy = -1;
            provider.BoundCodec.EncodeCallback = () =>
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                historyCountDuringDestroy = temporal.State.Count;
            };
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventB>>(listener);

            bool stepped = true;
            CoCoDiagnostic failure = default;
            try
            {
                Assert.DoesNotThrow(() => stepped = host.TryStep(0.02d, out failure));
            }
            finally
            {
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventB>>(listener);
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
            Assert.That(runtime.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Disposed));
            Assert.That(temporal.State.Mode, Is.EqualTo(CoCoTemporalMode.Disabled));
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
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Registry));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
        }

        [Test]
        public void OperatorListRejectsInvalidEntriesDuplicateIdsAndNestedHostCrossing()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);

            CoCoStateGraphHost nullHost = CreateHost(ids, out GameObject nullObject);
            SetOperators(nullHost, (MonoBehaviour)null);
            AssertStartupOperatorFailure(nullHost);

            CoCoStateGraphHost nonOperatorHost = CreateHost(ids, out GameObject nonOperatorObject);
            SetOperators(nonOperatorHost, nonOperatorHost);
            AssertStartupOperatorFailure(nonOperatorHost);

            CoCoStateGraphHost duplicateHost = CreateHost(ids, out GameObject duplicateObject);
            var duplicate = duplicateObject.AddComponent<ContextWritingOperator>();
            duplicate.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(duplicateHost, duplicate, duplicate);
            AssertStartupOperatorFailure(duplicateHost);

            CoCoStateGraphHost destroyedHost = CreateHost(ids, out GameObject destroyedObject);
            var destroyed = destroyedObject.AddComponent<ContextWritingOperator>();
            destroyed.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(destroyedHost, destroyed);
            UnityEngine.Object.DestroyImmediate(destroyed);
            AssertStartupOperatorFailure(destroyedHost);

            CoCoStateGraphHost duplicateIdHost = CreateHost(ids, out GameObject duplicateIdObject);
            var owner = duplicateIdObject.AddComponent<ContextWritingOperator>();
            owner.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            var duplicateId = duplicateIdObject.AddComponent<DuplicateIdOperator>();
            duplicateId.Configure(ids.FirstOperatorId, ids.PrimarySectionId);
            SetOperators(duplicateIdHost, owner, duplicateId);
            AssertStartupOperatorFailure(duplicateIdHost);

            CoCoStateGraphHost parentHost = CreateHost(ids, out GameObject parentObject);
            var nestedObject = new GameObject("Nested Host Boundary");
            _objects.Add(nestedObject);
            nestedObject.transform.SetParent(parentObject.transform);
            nestedObject.AddComponent<CoCoStateGraphHost>();
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
            CoCoStateGraphHost extraHost = CreateHost(ids, out GameObject extraObject);
            var extra = extraObject.AddComponent<ExtraneousRequirementOperator>();
            extra.Configure(ids);
            SetOperators(extraHost, extra);
            Assert.That(extraHost.TryStart(out CoCoDiagnostic extraFailure), Is.False);
            Assert.That(extraFailure.Code, Is.EqualTo(CoCoDiagnosticCode.MissingOperatorBinding));
            AssertNoProjectFactoryCallbacks();

            CoCoStateGraphProjectBindings.ResetForTests();
            InstallClaimProvider(ids);
            CoCoStateGraphHost missingHost = CreateClaimHost(ids, out GameObject missingObject, 0);
            var low = missingObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            SetOperators(missingHost, low);
            Assert.That(missingHost.TryStart(out CoCoDiagnostic missingFailure), Is.False);
            Assert.That(missingFailure.Code, Is.EqualTo(CoCoDiagnosticCode.MissingOperatorBinding));
        }

        [Test]
        public void ClaimDeniedIsRecordedBeforeCallbacksAndDisabledSectionUsesExplicitNoOp()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            var noOp = gameObject.AddComponent<NoOpSecondaryOperator>();
            noOp.Configure(ids);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            SetOperators(host, high, noOp, low);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);

            Assert.That(high.ExecuteCount, Is.Zero);
            Assert.That(noOp.ExecuteCount, Is.EqualTo(1));
            Assert.That(noOp.SawDisabledSection, Is.True);
            Assert.That(low.ExecuteCount, Is.EqualTo(1));
            var outcomes = new CoCoStateFlowTraceEntry[4];
            int count = host.Trace.CopyLatestTo(
                outcomes,
                new CoCoStateFlowTraceFilter(CoCoStateFlowTraceKind.OperatorOutcome));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(outcomes[0].OperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.ClaimDenied));
            Assert.That(
                outcomes[1].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.NoOp));
            Assert.That(
                outcomes[2].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.Succeeded));
        }

        [Test]
        public void ClaimDeniedIsMaterializedBeforeEarlierWinnerCallbackCanFail()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 16);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
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

            var outcomes = new CoCoStateFlowTraceEntry[4];
            int count = host.Trace.CopyLatestTo(
                outcomes,
                new CoCoStateFlowTraceFilter(CoCoStateFlowTraceKind.OperatorOutcome));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(outcomes[0].OperatorId, Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.ClaimDenied));

            var traceEntries = new CoCoStateFlowTraceEntry[16];
            int traceCount = host.Trace.CopyLatestTo(traceEntries);
            Assert.That(traceCount, Is.GreaterThan(0));
            Assert.That(
                traceEntries[traceCount - 1].Kind,
                Is.EqualTo(CoCoStateFlowTraceKind.Cancelled));
            Assert.That(traceEntries[traceCount - 1].PreviousContext.IsValid, Is.True);
            Assert.That(traceEntries[traceCount - 1].PreviousContext.HasCommittedFrame, Is.False);
            for (int index = 0; index < traceCount; index++)
            {
                Assert.That(
                    traceEntries[index].Kind,
                    Is.Not.EqualTo(CoCoStateFlowTraceKind.ContextCommit));
                Assert.That(
                    traceEntries[index].Kind,
                    Is.Not.EqualTo(CoCoStateFlowTraceKind.EventSequence));
                Assert.That(
                    traceEntries[index].Kind,
                    Is.Not.EqualTo(CoCoStateFlowTraceKind.EventPublished));
            }
        }

        [TestCase(0)]
        [TestCase(64)]
        public void GraphProducerClaimOperatorAndTraceHaveZeroSteadyStateManagedAllocation(
            int traceCapacity)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            var noOp = gameObject.AddComponent<NoOpSecondaryOperator>();
            noOp.Configure(ids);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
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
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
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

            var entries = new CoCoStateFlowTraceEntry[32];
            int count = host.Trace.CopyLatestTo(entries);
            Assert.That(count, Is.GreaterThanOrEqualTo(4));
            Assert.That(entries[0].Kind, Is.EqualTo(CoCoStateFlowTraceKind.Tick));
            Assert.That(entries[1].Kind, Is.EqualTo(CoCoStateFlowTraceKind.OperationSection));
            Assert.That(entries[2].Kind, Is.EqualTo(CoCoStateFlowTraceKind.OperationSection));
            Assert.That(entries[count - 1].Kind, Is.EqualTo(CoCoStateFlowTraceKind.Cancelled));
            for (int index = 0; index < count; index++)
            {
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.OperatorOutcome));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.ContextCommit));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.EventSequence));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.EventPublished));
            }
        }

        [Test]
        public void RestoreCompatibilityValidationIsPureReadOnlyHostSeam()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject, traceCapacity: 16);
            var runtimeOperator = gameObject.AddComponent<ContextWritingOperator>();
            runtimeOperator.Configure(
                ids.FirstOperatorId,
                ids.StateSlotId,
                ids.PrimarySectionId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic step), step);
            CoCoContextFrame current = host.CurrentContext;
            CoCoTickFrame sourceTick = current.Header.TickFrame;
            Require(CoCoTimelinePosition.TryCreate(
                sourceTick.TimelinePosition.Seconds + 1d,
                out CoCoTimelinePosition resumedPosition));
            Require(CoCoTickFrame.TryCreate(
                0.02d,
                sourceTick.TimelineId,
                resumedPosition,
                new CoCoTimelineTick(sourceTick.Tick.Value + 1UL),
                sourceTick.ClockDomainId,
                new CoCoExecutionSequence(sourceTick.ExecutionSequence.Value + 1UL),
                new CoCoTimelineEpoch(sourceTick.TimelineEpoch.Value + 1UL),
                out CoCoTickFrame resumed,
                out CoCoDiagnostic frame), frame);
            ulong traceCount = host.Trace.TotalWritten;

            Assert.That(host.TryValidateRestore(
                current,
                resumed,
                out CoCoContextCommitStatus status), Is.True);
            Assert.That(status, Is.EqualTo(CoCoContextCommitStatus.None));
            Assert.That(host.TryValidateRestore(
                default,
                resumed,
                out CoCoContextCommitStatus invalidStatus), Is.False);
            Assert.That(invalidStatus, Is.EqualTo(CoCoContextCommitStatus.LayoutMismatch));
            Assert.That(host.CurrentContext, Is.EqualTo(current));
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Running));
        }

        [Test]
        public void PreparedActorRestoreAtomicallyRestoresContextGraphClockAndClaims()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);
            var listener = new PublishLifecycleListener(host, requestLifecycle: false);
            CoCoEventBus.Subscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            CoCoContextFrame source = default;
            bool retained = false;
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
                source = host.CurrentContext;
                retained = source.Retain();
                Require(retained);
                CoCoGraphStateRecord<byte> sourceFirstState = ReadClaimGraphState(
                    source,
                    ids.FirstGraphStateSlotId);
                CoCoGraphStateRecord<byte> sourceSecondState = ReadClaimGraphState(
                    source,
                    ids.SecondGraphStateSlotId);
                CoCoOperatorClaimState sourcePrimary = ReadClaim(
                    source,
                    ids.PrimaryClaimStateSlotId);
                CoCoOperatorClaimState sourceSecondary = ReadClaim(
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
                CoCoContextFrame changedContext = host.CurrentContext;
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(
                    ReadClaim(changedContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                    Is.EqualTo(ids.SecondOperatorId));
                Assert.That(
                    ReadClaim(changedContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                    Is.EqualTo(ids.SecondOperatorId));

                CoCoStateGraphRuntime runtime = GetRuntime(host);
                CoCoTickFrame resumed = CreateResumedTick(changedContext.Header.TickFrame);
                ulong traceCount = host.Trace.TotalWritten;
                int lowExecutions = low.ExecuteCount;
                int highExecutions = high.ExecuteCount;
                int logicUpdates = OperatorCommitClaimLogic.UpdateCount;
                CoCoTimelineTick changedTick = runtime.Clock.Tick;
                CoCoTimelineEpoch changedEpoch = runtime.Clock.TimelineEpoch;

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out CoCoPreparedActorRestore cancelled,
                    out CoCoContextCommitStatus cancelStatus,
                    out CoCoDiagnostic cancelPrepare), cancelPrepare);
                Assert.That(cancelStatus, Is.EqualTo(CoCoContextCommitStatus.None));
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
                    out CoCoPreparedActorRestore committed,
                    out CoCoContextCommitStatus commitStatus,
                    out CoCoDiagnostic commitPrepare), commitPrepare);
                Assert.That(commitStatus, Is.EqualTo(CoCoContextCommitStatus.None));
                Assert.That(committed.IsValid, Is.True);
                committed.CommitNoFail();

                Assert.That(committed.IsValid, Is.False);
                Assert.That(host.CurrentContext, Is.Not.EqualTo(changedContext));
                Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(4UL));
                Assert.That(host.CurrentContext.Header.TickFrame, Is.EqualTo(resumed));
                Assert.That(host.CurrentContext.Origin.Kind, Is.EqualTo(CoCoContextFrameOriginKind.Restore));
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

                CoCoEventBus.Unsubscribe<CoCoEventPacket<OperatorCommitEventA>>(listener);
            }
        }

        [Test]
        public void TemporalConfirmRestoresHistoricalGraphPathAndHeldClaims()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame source = host.CurrentContext;
            CoCoContextRevision sourceRevision = source.Revision;
            CoCoGraphStateRecord<byte> sourceFirstState = ReadClaimGraphState(
                source,
                ids.FirstGraphStateSlotId);
            CoCoGraphStateRecord<byte> sourceSecondState = ReadClaimGraphState(
                source,
                ids.SecondGraphStateSlotId);
            CoCoOperatorClaimState sourcePrimary = ReadClaim(
                source,
                ids.PrimaryClaimStateSlotId);
            CoCoOperatorClaimState sourceSecondary = ReadClaim(
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
            CoCoContextFrame changedAuthority = host.CurrentContext;
            CoCoOperatorClaimState changedPrimary = ReadClaim(
                changedAuthority,
                ids.PrimaryClaimStateSlotId);
            CoCoOperatorClaimState changedSecondary = ReadClaim(
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

            CoCoContextFrame restored = host.CurrentContext;
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
            CoCoStateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Release);
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
            CoCoStateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            Require(host.TryStep(0.02d, out CoCoDiagnostic second), second);
            CoCoContextFrame authority = host.CurrentContext;
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
            CoCoStateGraphHost host = CreateClaimHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 128);
            var restoreBinding = gameObject.AddComponent<TemporalCodecRestoreBinding>();
            SetField(host, "temporalHistoryCapacity", 5);
            SetField(host, "contextRestoreBinding", restoreBinding);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
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

            CoCoContextFrame authority = host.CurrentContext;
            CoCoTickFrame authorityTick = authority.Header.TickFrame;
            CoCoOperatorClaimState authorityPrimary = ReadClaim(
                authority,
                ids.PrimaryClaimStateSlotId);
            CoCoOperatorClaimState authoritySecondary = ReadClaim(
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
            CoCoOperatorClaimSuspendPolicy.Release,
            true)]
        [TestCase(
            ClaimRestoreLifecycleCase.ResumedBeforeRestore,
            CoCoOperatorClaimSuspendPolicy.Release,
            true)]
        [TestCase(
            ClaimRestoreLifecycleCase.Running,
            CoCoOperatorClaimSuspendPolicy.Release,
            false)]
        [TestCase(
            ClaimRestoreLifecycleCase.Suspended,
            CoCoOperatorClaimSuspendPolicy.Retain,
            false)]
        public void RestorePreservesOnlyApplicableSuspendReleaseOverlay(
            ClaimRestoreLifecycleCase lifecycleCase,
            CoCoOperatorClaimSuspendPolicy suspendPolicy,
            bool expectRelease)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, suspendPolicy);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame source = host.CurrentContext;
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

            CoCoTickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Require(host.TryPrepareRestore(
                source,
                resumed,
                out CoCoPreparedActorRestore prepared,
                out CoCoContextCommitStatus restoreStatus,
                out CoCoDiagnostic prepare), prepare);
            Assert.That(restoreStatus, Is.EqualTo(CoCoContextCommitStatus.None));
            prepared.CommitNoFail();

            if (lifecycleCase == ClaimRestoreLifecycleCase.Suspended)
            {
                Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Suspended));
                Require(host.TryResume(out CoCoDiagnostic resume), resume);
            }

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStep(0.02d, out CoCoDiagnostic following), following);

            CoCoOperatorId expectedOwner = expectRelease
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
                    : Is.EqualTo(default(CoCoOperatorId)));
        }

        [Test]
        public void CancellingSuspendedRestoreDoesNotClearPendingClaimRelease()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame source = host.CurrentContext;
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            CoCoTickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Require(host.TryPrepareRestore(
                source,
                resumed,
                out CoCoPreparedActorRestore prepared,
                out CoCoContextCommitStatus restoreStatus,
                out CoCoDiagnostic prepare), prepare);
            Assert.That(restoreStatus, Is.EqualTo(CoCoContextCommitStatus.None));
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
        public void ResetToDefaultClaimRestoreKeepsContextAndClaimCacheConsistent()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(
                ids,
                CoCoContextRestorePolicy.ResetToDefault,
                CoCoContextRestorePolicy.Stored);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame source = host.CurrentContext;
            Assert.That(
                ReadClaim(source, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.FirstOperatorId));

            CoCoTickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Require(host.TryPrepareRestore(
                source,
                resumed,
                out CoCoPreparedActorRestore prepared,
                out CoCoContextCommitStatus restoreStatus,
                out CoCoDiagnostic prepare), prepare);
            Assert.That(restoreStatus, Is.EqualTo(CoCoContextCommitStatus.None));
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
                CoCoContextRestorePolicy.ResetToDefault,
                CoCoContextRestorePolicy.Stored);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            OperatorCommitClaimLogic.EnableSecondary = true;
            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame source = host.CurrentContext;
            Assert.That(
                ReadClaim(source, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));
            Assert.That(
                ReadClaim(source, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(ids.SecondOperatorId));

            int lowExecutions = low.ExecuteCount;
            int highExecutions = high.ExecuteCount;
            CoCoTickFrame resumed = CreateResumedTick(source.Header.TickFrame);
            Assert.That(host.TryValidateRestore(
                source,
                resumed,
                out CoCoContextCommitStatus status), Is.False);
            Assert.That(status, Is.EqualTo(CoCoContextCommitStatus.RestoreFailed));
            Assert.That(host.CurrentContext, Is.EqualTo(source));
            Assert.That(low.ExecuteCount, Is.EqualTo(lowExecutions));
            Assert.That(high.ExecuteCount, Is.EqualTo(highExecutions));
        }

        [Test]
        public void TraceOverwritesInOrderAndFiltersCommittedPathAndOperator()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(
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

            ICoCoStateFlowTrace trace = host.Trace;
            Assert.That(trace, Is.Not.Null);
            Assert.That(trace.Capacity, Is.EqualTo(4));
            Assert.That(trace.Count, Is.EqualTo(4));
            Assert.That(trace.TotalWritten, Is.EqualTo(10UL));
            var latest = new CoCoStateFlowTraceEntry[4];
            Assert.That(trace.CopyLatestTo(latest), Is.EqualTo(4));
            CollectionAssert.AreEqual(
                new[]
                {
                    CoCoStateFlowTraceKind.OperationSection,
                    CoCoStateFlowTraceKind.OperatorOutcome,
                    CoCoStateFlowTraceKind.ContextCommit,
                    CoCoStateFlowTraceKind.ActivePath
                },
                Array.ConvertAll(latest, entry => entry.Kind));
            Assert.That(latest[0].TickFrame.Tick.Value, Is.EqualTo(2UL));

            var path = new CoCoStateFlowTraceEntry[2];
            int pathCount = trace.CopyLatestTo(
                path,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.ActivePath,
                    layerId: ids.LayerId));
            Assert.That(pathCount, Is.EqualTo(1));
            Assert.That(path[0].LayerId, Is.EqualTo(ids.LayerId));
            Assert.That(path[0].StateId, Is.EqualTo(ids.StateId));

            var outcomes = new CoCoStateFlowTraceEntry[2];
            int outcomeCount = trace.CopyLatestTo(
                outcomes,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.OperatorOutcome,
                    operatorId: ids.FirstOperatorId));
            Assert.That(outcomeCount, Is.EqualTo(1));
            Assert.That(
                outcomes[0].OperatorOutcome,
                Is.EqualTo(CoCoOperatorOutcomeStatus.Succeeded));
        }

        [Test]
        public void FirstTickTraceKeepsExactDefaultPreviousAndCommittedFrameReferences()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallProvider(ids);
            CoCoStateGraphHost host = CreateHost(
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

            var entries = new CoCoStateFlowTraceEntry[16];
            int count = host.Trace.CopyLatestTo(entries);
            Assert.That(count, Is.GreaterThanOrEqualTo(4));
            Assert.That(entries[0].Kind, Is.EqualTo(CoCoStateFlowTraceKind.Tick));
            CoCoStateFlowTraceFrameReference previous = entries[0].PreviousContext;
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
                if (entries[index].Kind == CoCoStateFlowTraceKind.ContextCommit)
                {
                    commitIndex = index;
                }
                else if (entries[index].Kind == CoCoStateFlowTraceKind.ActivePath)
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
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 64);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Retain);
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

            var entries = new CoCoStateFlowTraceEntry[8];
            int transitionCount = host.Trace.CopyLatestTo(
                entries,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.Transition,
                    layerId: ids.LayerId));
            Assert.That(transitionCount, Is.EqualTo(2));
            Assert.That(entries[0].TransitionId, Is.EqualTo(ids.TransitionId));
            Assert.That(
                entries[0].TransitionRole,
                Is.EqualTo(CoCoStateFlowTransitionRole.Candidate));
            Assert.That(entries[1].TransitionId, Is.EqualTo(ids.TransitionId));
            Assert.That(
                entries[1].TransitionRole,
                Is.EqualTo(CoCoStateFlowTransitionRole.Winner));
            int primarySections = host.Trace.CopyLatestTo(
                entries,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.OperationSection,
                    sectionId: ids.PrimarySectionId));
            int secondarySections = host.Trace.CopyLatestTo(
                entries,
                new CoCoStateFlowTraceFilter(
                    CoCoStateFlowTraceKind.OperationSection,
                    sectionId: ids.SecondarySectionId));
            Assert.That(primarySections, Is.EqualTo(4));
            Assert.That(secondarySections, Is.EqualTo(4));
        }

        [TestCase(CoCoOperatorClaimSuspendPolicy.Retain, 2, 0)]
        [TestCase(CoCoOperatorClaimSuspendPolicy.Release, 1, 1)]
        public void SuspendPolicyPreservesOrReleasesCompleteClaimSet(
            CoCoOperatorClaimSuspendPolicy suspendPolicy,
            int expectedLowExecutions,
            int expectedHighExecutions)
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 0);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, suspendPolicy);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame firstContext = host.CurrentContext;
            CoCoOperatorClaimState firstPrimary = ReadClaim(
                firstContext,
                ids.PrimaryClaimStateSlotId);
            CoCoOperatorClaimState firstSecondary = ReadClaim(
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
            CoCoOperatorId expectedOwner = suspendPolicy == CoCoOperatorClaimSuspendPolicy.Retain
                ? ids.FirstOperatorId
                : ids.SecondOperatorId;
            Assert.That(
                ReadClaim(host.CurrentContext, ids.PrimaryClaimStateSlotId).OwnerOperatorId,
                Is.EqualTo(expectedOwner));
            Assert.That(
                ReadClaim(host.CurrentContext, ids.SecondaryClaimStateSlotId).OwnerOperatorId,
                suspendPolicy == CoCoOperatorClaimSuspendPolicy.Retain
                    ? Is.EqualTo(default(CoCoOperatorId))
                    : Is.EqualTo(ids.SecondOperatorId));
        }

        [Test]
        public void CancelledTickAfterSuspendReleaseDoesNotMutateCommittedClaimAuthority()
        {
            OperatorCommitTestIds ids = OperatorCommitTestIds.Create();
            InstallClaimProvider(ids);
            CoCoStateGraphHost host = CreateClaimHost(ids, out GameObject gameObject, 32);
            var low = gameObject.AddComponent<LowClaimOperator>();
            low.Configure(ids, CoCoOperatorClaimSuspendPolicy.Release);
            var high = gameObject.AddComponent<HighClaimOperator>();
            high.Configure(ids);
            SetOperators(host, low, high);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.02d, out CoCoDiagnostic first), first);
            CoCoContextFrame committed = host.CurrentContext;
            CoCoOperatorClaimState primary = ReadClaim(
                committed,
                ids.PrimaryClaimStateSlotId);
            CoCoOperatorClaimState secondary = ReadClaim(
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
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
        }

        private static bool TryBindPrimaryOperation(
            CoCoStateGraphHostBindingBuilder builder,
            OperatorCommitTestIds ids,
            HostTestDiscreteSectionViewFactory factory,
            out CoCoDiagnostic diagnostic) =>
            builder.TryRegisterOperation(
                ids.PrimarySectionId,
                CoCoOperationSectionMode.Discrete,
                factory,
                PrimaryOperationFactoryFingerprint,
                out CoCoOperationSectionRequirement ignored,
                out diagnostic);

        private static bool TryRegisterPrimaryOperation(
            CoCoGraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic) =>
            builder.TryRegisterOperationSection(
                ids.PrimarySectionId,
                CoCoOperationSectionMode.Discrete,
                new CoCoOperationSectionViewFactoryToken<
                    IHostTestDiscreteSection,
                    HostTestDiscreteSectionViewFactory>(
                    PrimaryOperationFactoryFingerprint),
                out diagnostic);

        private void InstallClaimProvider(OperatorCommitTestIds ids)
        {
            InstallClaimProvider(
                ids,
                CoCoContextRestorePolicy.Stored,
                CoCoContextRestorePolicy.Stored);
        }

        private void InstallClaimProvider(
            OperatorCommitTestIds ids,
            CoCoContextRestorePolicy primaryClaimRestorePolicy,
            CoCoContextRestorePolicy secondaryClaimRestorePolicy)
        {
            var provider = new ClaimBindingProvider(
                ids,
                false,
                primaryClaimRestorePolicy,
                secondaryClaimRestorePolicy);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
        }

        private CoCoStateGraphHost CreateHost(
            OperatorCommitTestIds ids,
            out GameObject gameObject,
            int eventOutboxCapacity = 0,
            int traceCapacity = 0)
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(ids.StateId.High, ids.StateId.Low),
                default,
                "Operator Commit Leaf",
                new CoCoSerializedId128(ids.StateDescriptorId.High, ids.StateDescriptorId.Low),
                new HostTestStateConfig { Value = 5 });
            var layer = new CoCoStateGraphLayerRecord(
                new CoCoSerializedId128(ids.LayerId.High, ids.LayerId.Low),
                "Base");
            layer.InitialStateId = new CoCoSerializedId128(ids.StateId.High, ids.StateId.Low);
            layer.States.Add(state);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Operator Commit Host");
            _objects.Add(gameObject);
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 3);
            SetField(host, "eventOutboxCapacity", eventOutboxCapacity);
            SetField(host, "traceCapacity", traceCapacity);
            return host;
        }

        private CoCoStateGraphHost CreateClaimHost(
            OperatorCommitTestIds ids,
            out GameObject gameObject,
            int traceCapacity)
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var firstState = new CoCoStateGraphStateRecord(
                Serialize(ids.StateId),
                default,
                "Claim Source",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var secondState = new CoCoStateGraphStateRecord(
                Serialize(ids.SecondStateId),
                default,
                "Claim Target",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 2 });
            var transition = new CoCoStateGraphTransitionRecord(
                Serialize(ids.TransitionId),
                Serialize(ids.StateId),
                Serialize(ids.SecondStateId),
                1);
            var layer = new CoCoStateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.StateId);
            layer.States.Add(firstState);
            layer.States.Add(secondState);
            layer.Transitions.Add(transition);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Claim Host");
            _objects.Add(gameObject);
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 3);
            SetField(host, "eventOutboxCapacity", 0);
            SetField(host, "traceCapacity", traceCapacity);
            return host;
        }

        private static CoCoSerializedId128 Serialize(CoCoLayerId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoTransitionId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoStateGraphRuntime GetRuntime(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphRuntime)field.GetValue(host);
        }

        private static CoCoStateGraphTransaction GetTransaction(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_transaction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphTransaction)field.GetValue(host);
        }

        private static CoCoStateGraphTemporalController GetTemporal(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_temporal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphTemporalController)field.GetValue(host);
        }

        private static ulong ReadLastEventSequence(CoCoStateGraphTransaction transaction)
        {
            FieldInfo field = typeof(CoCoStateGraphTransaction).GetField(
                "_lastEventSequence",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (ulong)field.GetValue(transaction);
        }

        private static CoCoStateGraphHostRuntimeBindings GetBindings(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphHostRuntimeBindings)field.GetValue(host);
        }

        private static CoCoOperatorClaimState ReadClaim(
            CoCoContextFrame frame,
            CoCoStateSlotId slotId)
        {
            Require(frame.Layout.TryResolveSlot(
                slotId,
                out CoCoStateSlot<CoCoOperatorClaimState> slot));
            return frame.Read(slot);
        }

        private static CoCoGraphStateRecord<byte> ReadClaimGraphState(
            CoCoContextFrame frame,
            CoCoStateSlotId slotId)
        {
            Require(frame.Layout.TryResolveSlot(
                slotId,
                out CoCoStateSlot<CoCoGraphStateRecord<byte>> slot));
            return frame.Read(slot);
        }

        private static CoCoTickFrame CreateResumedTick(CoCoTickFrame current)
        {
            Require(CoCoTimelinePosition.TryCreate(
                current.TimelinePosition.Seconds + 1d,
                out CoCoTimelinePosition position));
            Require(CoCoTickFrame.TryCreate(
                0.02d,
                current.TimelineId,
                position,
                new CoCoTimelineTick(current.Tick.Value + 1UL),
                current.ClockDomainId,
                new CoCoExecutionSequence(current.ExecutionSequence.Value + 1UL),
                new CoCoTimelineEpoch(current.TimelineEpoch.Value + 1UL),
                out CoCoTickFrame resumed,
                out CoCoDiagnostic diagnostic), diagnostic);
            return resumed;
        }

        private static void SetOperators(CoCoStateGraphHost host, params MonoBehaviour[] operators) =>
            SetField(host, "operators", operators);

        private static void SetField<TValue>(
            CoCoStateGraphHost host,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(host, value);
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }

        private static void AssertStartupOperatorFailure(CoCoStateGraphHost host)
        {
            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidOperatorDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
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
            CoCoStateGraphHost host,
            ICoCoStateGraphProjectBindingProvider provider,
            CoCoDiagnosticDomain expectedDomain,
            CoCoDiagnosticCode expectedCode)
        {
            CoCoStateGraphAssetCompileResult compileResult =
                new CoCoStateGraphAssetCompiler().Compile(host.StateGraphAsset, provider.Catalog);
            Require(compileResult.Succeeded);
            Require(CoCoGraphInstanceId.TryCreate(9001UL, out CoCoGraphInstanceId graphInstanceId));
            var builder = new CoCoStateGraphHostBindingBuilder(
                compileResult.Graph,
                graphInstanceId);
            Require(provider.TryConfigure(builder, out CoCoDiagnostic configure), configure);
            Require(builder.TryFreeze(
                host.EventLaneCapacity,
                host.EventSourceCapacity,
                host.EventDedupCapacity,
                out CoCoStateGraphHostRuntimeBindings bindings,
                out CoCoDiagnostic freeze), freeze);

            CoCoStateGraphTransaction transaction = null;
            try
            {
                Assert.That(CoCoStateGraphTransaction.TryPreflight(
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
                CoCoStateGraphHostBindingBuilder builder,
                OperatorCommitTestIds ids,
                out CoCoDiagnostic diagnostic)
            {
                CoCoGraphStateRecord<int> first =
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
                CoCoStateGraphHostBindingBuilder builder,
                OperatorCommitTestIds ids,
                out CoCoDiagnostic diagnostic) =>
                TryBindClaimGraph(builder, ids, false, out diagnostic);

            public static bool TryBindClaimGraph(
                CoCoStateGraphHostBindingBuilder builder,
                OperatorCommitTestIds ids,
                bool mismatchPrimaryIdentity,
                out CoCoDiagnostic diagnostic)
            {
                CoCoGraphStateRecord<byte> first =
                    OperatorCommitGraphContextFixture.CreateActiveState(ids, (byte)0);
                CoCoGraphStateRecord<byte> second =
                    OperatorCommitGraphContextFixture.CreateInactiveState(ids, (byte)0);
                CoCoOperatorClaimState primary = CoCoOperatorClaimState.Unheld(
                    mismatchPrimaryIdentity ? ids.SecondaryClaimId : ids.PrimaryClaimId,
                    ids.PrimarySectionId);
                CoCoOperatorClaimState secondary = CoCoOperatorClaimState.Unheld(
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

        private sealed class OperatorCommitBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private readonly OperatorCommitTestIds _ids;
            private readonly HostTestDiscreteSectionViewFactory _primary =
                new HostTestDiscreteSectionViewFactory();

            public OperatorCommitBindingProvider(OperatorCommitTestIds ids)
            {
                _ids = ids;
                Catalog = BuildCatalog(ids);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
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

                var stateFactory = new CoCoStateRuntimeFactory<HostTestLogic, HostTestMemory>(
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

            private static CoCoGraphDescriptorCatalog BuildCatalog(OperatorCommitTestIds ids)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Require(TryRegisterPrimaryOperation(
                    builder,
                    ids,
                    out CoCoDiagnostic operation), operation);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    CoCoStateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
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
                    new CoCoStateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId },
                    new[] { ids.StateBlockId, ids.GraphStateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
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

        private sealed class CodecBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private readonly OperatorCommitTestIds _ids;
            private readonly CodecBindingMode _mode;
            private readonly HostTestDiscreteSectionViewFactory _primary =
                new HostTestDiscreteSectionViewFactory();

            public CodecBindingProvider(OperatorCommitTestIds ids, CodecBindingMode mode)
            {
                _ids = ids;
                _mode = mode;
                if (!CoCoCodecId.TryCreate(520UL, 1UL, out CoCoCodecId manifestCodecId) ||
                    !CoCoCodecId.TryCreate(520UL, 2UL, out CoCoCodecId mismatchCodecId))
                {
                    throw new InvalidOperationException("Context codec fixture ids are invalid.");
                }

                ManifestCodec = mode == CodecBindingMode.ExtraOnRaw
                    ? default
                    : new CoCoCodecDescriptor(manifestCodecId, 1U);
                CoCoCodecDescriptor boundDescriptor = mode == CodecBindingMode.MismatchedCustom
                    ? new CoCoCodecDescriptor(mismatchCodecId, 1U)
                    : new CoCoCodecDescriptor(manifestCodecId, 1U);
                BoundCodec = mode == CodecBindingMode.MissingCustom
                    ? null
                    : new Int32ContextCodec(boundDescriptor);
                Catalog = BuildCatalog(ids, ManifestCodec);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }
            public CoCoCodecDescriptor ManifestCodec { get; }
            public Int32ContextCodec BoundCodec { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
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

                var stateFactory = new CoCoStateRuntimeFactory<HostTestLogic, HostTestMemory>(
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

            private static CoCoGraphDescriptorCatalog BuildCatalog(
                OperatorCommitTestIds ids,
                CoCoCodecDescriptor codec)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Require(TryRegisterPrimaryOperation(
                    builder,
                    ids,
                    out CoCoDiagnostic operation), operation);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    CoCoStateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
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
                    new CoCoStateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId },
                    new[] { ids.StateBlockId, ids.GraphStateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        public sealed class Int32ContextCodec : ICoCoContextValueCodec<int>
        {
            public Int32ContextCodec(CoCoCodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CoCoCodecDescriptor Descriptor { get; }
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

        private sealed class TemporalCodecRestoreBinding :
            MonoBehaviour,
            ICoCoContextRestoreBinding
        {
            public TemporalRestoreFixtureFailure Failure { get; set; }

            public bool TryApply(
                in CoCoContextRestoreBindingContext context,
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

                if (context.ApplyKind == CoCoContextRestoreApplyKind.Confirm)
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

        private sealed class ClaimBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private const ulong PrimaryFactoryFingerprint = 5091UL;
            private const ulong SecondaryFactoryFingerprint = 5092UL;
            private readonly OperatorCommitTestIds _ids;
            private readonly bool _mismatchPrimaryClaimDefault;
            private readonly OperatorCommitPrimarySectionFactory _primary =
                new OperatorCommitPrimarySectionFactory();
            private readonly OperatorCommitSecondarySectionFactory _secondary =
                new OperatorCommitSecondarySectionFactory();

            public ClaimBindingProvider(
                OperatorCommitTestIds ids,
                bool mismatchPrimaryClaimDefault = false,
                CoCoContextRestorePolicy primaryClaimRestorePolicy =
                    CoCoContextRestorePolicy.Stored,
                CoCoContextRestorePolicy secondaryClaimRestorePolicy =
                    CoCoContextRestorePolicy.Stored)
            {
                _ids = ids;
                _mismatchPrimaryClaimDefault = mismatchPrimaryClaimDefault;
                Catalog = BuildCatalog(
                    ids,
                    primaryClaimRestorePolicy,
                    secondaryClaimRestorePolicy);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                if (!builder.TryRegisterOperation(
                        _ids.PrimarySectionId,
                        CoCoOperationSectionMode.Discrete,
                        _primary,
                        PrimaryFactoryFingerprint,
                        out CoCoOperationSectionRequirement primary,
                        out diagnostic) ||
                    !builder.TryRegisterOperation(
                        _ids.SecondarySectionId,
                        CoCoOperationSectionMode.Discrete,
                        _secondary,
                        SecondaryFactoryFingerprint,
                        out CoCoOperationSectionRequirement secondary,
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

                var stateFactory = new CoCoStateRuntimeFactory<
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
                return builder.TryBindState(_ids.StateDescriptorId, stateFactory, out diagnostic);
            }

            private static CoCoGraphDescriptorCatalog BuildCatalog(
                OperatorCommitTestIds ids,
                CoCoContextRestorePolicy primaryClaimRestorePolicy,
                CoCoContextRestorePolicy secondaryClaimRestorePolicy)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Require(builder.TryRegisterOperationSection(
                    ids.PrimarySectionId,
                    CoCoOperationSectionMode.Discrete,
                    new CoCoOperationSectionViewFactoryToken<
                        IOperatorCommitPrimarySection,
                        OperatorCommitPrimarySectionFactory>(PrimaryFactoryFingerprint),
                    out CoCoDiagnostic primary), primary);
                Require(builder.TryRegisterOperationSection(
                    ids.SecondarySectionId,
                    CoCoOperationSectionMode.Discrete,
                    new CoCoOperationSectionViewFactoryToken<
                        IOperatorCommitSecondarySection,
                        OperatorCommitSecondarySectionFactory>(SecondaryFactoryFingerprint),
                    out CoCoDiagnostic secondary), secondary);
                Require(builder.TryRegisterStateBlock(
                    ids.StateBlockId,
                    CoCoStateBlockOwner.Operator,
                    out CoCoDiagnostic block), block);
                Require(builder.TryRegisterStateSlot(
                    ids.StateBlockId,
                    ids.StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    5,
                    ContextDefaultFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic slot), slot);
                Require(OperatorCommitGraphContextFixture.TryRegisterClaimGraph(
                    builder,
                    ids,
                    primaryClaimRestorePolicy,
                    secondaryClaimRestorePolicy,
                    out CoCoDiagnostic graphContext), graphContext);
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        OperatorCommitClaimLogic,
                        HostTestStateConfigSchema,
                        OperatorCommitClaimMemory>(HostTestSchemas.State, false),
                    null,
                    new[] { ids.PrimarySectionId, ids.SecondarySectionId },
                    new[] { ids.StateBlockId, ids.GraphStateBlockId },
                    out CoCoDiagnostic state), state);
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }
        }

        private sealed class LowClaimOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperatorClaimSuspendPolicy _suspendPolicy;
            private CoCoOperationSectionRequirement _primary;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }

            public bool FailExecution { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic require = default;
                    CoCoDiagnostic claimDiagnostic = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _primary,
                            out require) ||
                        !builder.TryClaim(
                            _ids.PrimaryClaimId,
                            _primary,
                            _ids.PrimaryClaimStateSlotId,
                            1,
                            _suspendPolicy,
                            out CoCoOperatorClaimRequirement claim,
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
                CoCoOperatorClaimSuspendPolicy suspendPolicy)
            {
                _ids = ids;
                _suspendPolicy = suspendPolicy;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (FailExecution)
                {
                    outcome = default;
                    return false;
                }

                if (!context.TryGet(
                        _primary,
                        out CoCoOperationSectionEntry<IOperatorCommitPrimarySection> section) ||
                    !section.Header.Enabled ||
                    !context.PreviousContext.Layout.TryResolveSlot(
                        _ids.StateSlotId,
                        out CoCoStateSlot<int> slot) ||
                    !context.TryWriteOutcome(slot, context.PreviousContext.Read(slot) + 1))
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class HighClaimOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperationSectionRequirement _primary;
            private CoCoOperationSectionRequirement _secondary;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public bool FailExecution { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic primaryDiagnostic = default;
                    CoCoDiagnostic secondaryDiagnostic = default;
                    CoCoDiagnostic primaryClaimDiagnostic = default;
                    CoCoDiagnostic secondaryClaimDiagnostic = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                            _ids.PrimarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _primary,
                            out primaryDiagnostic) ||
                        !builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out _secondary,
                            out secondaryDiagnostic) ||
                        !builder.TryClaim(
                            _ids.PrimaryClaimId,
                            _primary,
                            _ids.PrimaryClaimStateSlotId,
                            100,
                            CoCoOperatorClaimSuspendPolicy.Retain,
                            out CoCoOperatorClaimRequirement primaryClaim,
                            out primaryClaimDiagnostic) ||
                        !builder.TryClaim(
                            _ids.SecondaryClaimId,
                            _secondary,
                            _ids.SecondaryClaimStateSlotId,
                            100,
                            CoCoOperatorClaimSuspendPolicy.Retain,
                            out CoCoOperatorClaimRequirement secondaryClaim,
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
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (FailExecution)
                {
                    outcome = default;
                    return false;
                }

                if (!context.TryGet(
                        _primary,
                        out CoCoOperationSectionEntry<IOperatorCommitPrimarySection> primary) ||
                    !context.TryGet(
                        _secondary,
                        out CoCoOperationSectionEntry<IOperatorCommitSecondarySection> secondary) ||
                    !primary.Header.Enabled ||
                    !secondary.Header.Enabled)
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class DuplicateIdOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoOperatorId _operatorId;
            private CoCoOperationSectionId _sectionId;
            private CoCoOperatorDescriptor _descriptor;

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor == null)
                    {
                        var builder = new CoCoOperatorDescriptorBuilder();
                        CoCoDiagnostic requirement = default;
                        CoCoDiagnostic freeze = default;
                        if (!builder.TryRequire<IHostTestDiscreteSection>(
                                _sectionId,
                                CoCoOperationSectionMode.Discrete,
                                out CoCoOperationSectionRequirement ignored,
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
                CoCoOperatorId operatorId,
                CoCoOperationSectionId sectionId)
            {
                _operatorId = operatorId;
                _sectionId = sectionId;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class ExtraneousRequirementOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperatorDescriptor _descriptor;

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out CoCoOperationSectionRequirement ignored,
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
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class NoOpSecondaryOperator : MonoBehaviour, ICoCoOperator
        {
            private OperatorCommitTestIds _ids;
            private CoCoOperatorId _operatorId;
            private CoCoOperationSectionRequirement _secondary;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public bool SawDisabledSection { get; private set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IOperatorCommitSecondarySection>(
                            _ids.SecondarySectionId,
                            CoCoOperationSectionMode.Discrete,
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
                if (!CoCoOperatorId.TryCreate(506UL, 3UL, out _operatorId))
                {
                    throw new InvalidOperationException("No-Op Operator fixture id is invalid.");
                }
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.TryGet(
                        _secondary,
                        out CoCoOperationSectionEntry<IOperatorCommitSecondarySection> section))
                {
                    outcome = default;
                    return false;
                }

                SawDisabledSection = !section.Header.Enabled;
                outcome = SawDisabledSection
                    ? CoCoOperatorOutcome.NoOp
                    : CoCoOperatorOutcome.Success;
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

        private sealed class ContextWritingOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoOperatorId _operatorId;
            private CoCoOperationSectionId _sectionId;
            private CoCoStateSlotId _slotId;
            private CoCoStateSlotId _unauthorizedSlotId;
            private CoCoOperatorDescriptor _descriptor;

            public int ExecuteCount { get; private set; }
            public int PreviousValue { get; private set; }
            public bool PreviousHadCommittedFrame { get; private set; }
            public bool FailAfterWorldMutation { get; set; }
            public bool AttemptUnauthorizedOutcome { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IHostTestDiscreteSection>(
                            _sectionId,
                            CoCoOperationSectionMode.Discrete,
                            out CoCoOperationSectionRequirement ignored,
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
                CoCoOperatorId operatorId,
                CoCoStateSlotId slotId,
                CoCoOperationSectionId sectionId)
            {
                _operatorId = operatorId;
                _slotId = slotId;
                _sectionId = sectionId;
                if (!CoCoStateSlotId.TryCreate(999UL, 1UL, out _unauthorizedSlotId))
                {
                    throw new InvalidOperationException("Unauthorized Slot fixture id is invalid.");
                }
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                PreviousHadCommittedFrame = context.PreviousContext.HasCommittedFrame;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _slotId,
                        out CoCoStateSlot<int> slot))
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
                    outcome = CoCoOperatorOutcome.Success;
                    return true;
                }

                if (!context.TryWriteOutcome(slot, PreviousValue + 1))
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class OutboxOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoStateGraphHost _host;
            private OperatorCommitTestIds _ids;
            private CoCoOperatorDescriptor _descriptor;
            private CoCoEventOutboxRequirement _eventA;
            private CoCoEventOutboxRequirement _eventB;

            public int ExecuteCount { get; private set; }
            public bool AttemptInvalidTarget { get; set; }

            public CoCoOperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null)
                    {
                        return _descriptor;
                    }

                    var builder = new CoCoOperatorDescriptorBuilder();
                    CoCoDiagnostic requirement = default;
                    CoCoDiagnostic outcome = default;
                    CoCoDiagnostic eventA = default;
                    CoCoDiagnostic eventB = default;
                    CoCoDiagnostic freeze = default;
                    if (!builder.TryRequire<IHostTestDiscreteSection>(
                            _ids.PrimarySectionId,
                            CoCoOperationSectionMode.Discrete,
                            out CoCoOperationSectionRequirement ignored,
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

            public void Configure(CoCoStateGraphHost host, OperatorCommitTestIds ids)
            {
                _host = host;
                _ids = ids;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _ids.StateSlotId,
                        out CoCoStateSlot<int> slot) ||
                    !context.TryWriteOutcome(slot, context.PreviousContext.Read(slot) + 1))
                {
                    outcome = default;
                    return false;
                }

                if (AttemptInvalidTarget)
                {
                    var invalidPayload = new OperatorCommitEventA { Value = 99 };
                    context.EventOutbox.TryWrite(_eventA, default, invalidPayload);
                    outcome = CoCoOperatorOutcome.Success;
                    return true;
                }

                if (!CoCoEventOutboxTarget.TryTargeted(
                        _host.GraphInstanceId,
                        CoCoEventReliability.Reliable,
                        default,
                        default,
                        default,
                        out CoCoEventOutboxTarget target) ||
                    context.EventOutbox.TryWrite(
                        _eventA,
                        target,
                        new OperatorCommitEventA { Value = 1 }) !=
                    CoCoEventOutboxWriteResult.Accepted ||
                    context.EventOutbox.TryWrite(
                        _eventB,
                        target,
                        new OperatorCommitEventB { Value = 2 }) !=
                    CoCoEventOutboxWriteResult.Accepted)
                {
                    outcome = default;
                    return false;
                }

                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class PublishLifecycleListener :
            IEventListener<CoCoEventPacket<OperatorCommitEventA>>,
            IEventListener<CoCoEventPacket<OperatorCommitEventB>>
        {
            private readonly CoCoStateGraphHost _host;
            private readonly bool _requestLifecycle;

            public PublishLifecycleListener(
                CoCoStateGraphHost host,
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
            public CoCoRuntimeLifecycleState LifecycleDuringPublish { get; private set; }

            public void OnEvent(ref CoCoEventPacket<OperatorCommitEventA> eventData)
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

            public void OnEvent(ref CoCoEventPacket<OperatorCommitEventB> eventData)
            {
                Order.Add("B");
                Sequences.Add(eventData.Envelope.SourceEventSequence.Value);
            }
        }
    }
}
