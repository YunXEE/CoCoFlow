using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    public sealed class StateGraphHostContextAuthorityPlayModeTests
    {
        private const int AllocationWarmupIterations = 100;
        private const int AllocationMeasuredIterations = 10000;
        private const ulong OperationFactoryFingerprint = 70901UL;
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            HostProjectBindings.ResetForTests();
            ContextAuthorityFactoryProbe.Reset();
            ContextAuthorityMemoryStateBinding.Reset();
            ContextAuthorityLogic.Reset();
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
            HostProjectBindings.ResetForTests();
            ContextAuthorityFactoryProbe.Reset();
            ContextAuthorityMemoryStateBinding.Reset();
            ContextAuthorityLogic.Reset();
        }

        [Test]
        public void FirstTickUsesDefaultBackedPreviousAndCommitsRevisionOne()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard, true);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Require(host.TryStep(0.25d, out CoCoDiagnostic step), step);

            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(runtimeOperator.PreviousHadCommittedFrame, Is.False);
            Assert.That(
                runtimeOperator.PreviousState,
                Is.EqualTo(ContextAuthorityDefaults.First(ids)));
            Assert.That(host.CurrentContext.IsAlive, Is.True);
            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(1UL));
            Assert.That(host.CurrentContext.Header.TickFrame.Tick.Value, Is.EqualTo(1UL));

            GraphStateRecord<int> first = ReadGraphState(
                host.CurrentContext,
                ids.FirstGraphStateSlotId);
            GraphStateRecord<int> second = ReadGraphState(
                host.CurrentContext,
                ids.SecondGraphStateSlotId);
            Assert.That(first.IsValid, Is.True);
            Assert.That(first.StateId, Is.EqualTo(ids.FirstStateId));
            Assert.That(first.IsOnActivePath, Is.True);
            Assert.That(first.State, Is.EqualTo(1));
            Assert.That(first.MemoryFingerprint, Is.EqualTo(1UL));
            Assert.That(second, Is.EqualTo(ContextAuthorityDefaults.Second(ids)));
        }

        [Test]
        public void TransitionCommitCarriesEveryGraphStateRecord()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic firstStep), firstStep);
            ContextAuthorityLogic.RequestTransition = true;
            Require(host.TryStep(0.1d, out CoCoDiagnostic transitionStep), transitionStep);

            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(2UL));
            Assert.That(host.ActivePaths.Count, Is.EqualTo(1));
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));

            GraphStateRecord<int> source = ReadGraphState(
                host.CurrentContext,
                ids.FirstGraphStateSlotId);
            GraphStateRecord<int> target = ReadGraphState(
                host.CurrentContext,
                ids.SecondGraphStateSlotId);
            Assert.That(source.IsValid, Is.True);
            Assert.That(source.StateId, Is.EqualTo(ids.FirstStateId));
            Assert.That(source.IsOnActivePath, Is.False);
            Assert.That(source.ActivationId.IsValid, Is.True);
            Assert.That(source.State, Is.EqualTo(2));
            Assert.That(source.MemoryFingerprint, Is.EqualTo(2UL));
            Assert.That(target.IsValid, Is.True);
            Assert.That(target.StateId, Is.EqualTo(ids.SecondStateId));
            Assert.That(target.IsOnActivePath, Is.True);
            Assert.That(target.ActivationId.IsValid, Is.True);
            Assert.That(target.ActivationId, Is.Not.EqualTo(source.ActivationId));
            Assert.That(target.State, Is.Zero);
            Assert.That(target.MemoryFingerprint, Is.Zero);
        }

        [Test]
        public void GraphCaptureFailureOccursBeforeAnyOperatorCallback()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard, true);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            ContextAuthorityMemoryStateBinding.FailCapture = true;

            Assert.That(host.TryStep(0.1d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(host.RequiresWorldCorrection, Is.False);
            Assert.That(host.Fault.IsFaulted, Is.True);
        }

        [Test]
        public void SuccessfulGraphCaptureCannotMutateMemoryBeforeOperatorExecution()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard, true);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 32);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            ContextAuthorityMemoryStateBinding.MutateMemoryOnCapture = true;

            Assert.That(host.TryStep(0.1d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(host.Fault.IsFaulted, Is.True);

            var entries = new StateFlowTraceEntry[32];
            int count = host.Trace.CopyLatestTo(entries);
            Assert.That(count, Is.GreaterThanOrEqualTo(2));
            Assert.That(entries[0].Kind, Is.EqualTo(StateFlowTraceKind.Tick));
            Assert.That(entries[count - 1].Kind, Is.EqualTo(StateFlowTraceKind.Cancelled));
            for (int index = 0; index < count; index++)
            {
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.ContextCommit));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.EventSequence));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(StateFlowTraceKind.EventPublished));
            }
        }

        [Test]
        public void FingerprintThrowDuringGraphCaptureIntegrityIsContainedAndClearsTheTick()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard, true);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextFrame oldAuthority = host.CurrentContext;
            GraphStateRecord<int> oldGraphState = ReadGraphState(
                oldAuthority,
                ids.FirstGraphStateSlotId);
            StateGraphInstance runtime = GetRuntime(host);
            TimelineTick oldTick = runtime.Clock.Tick;
            int oldOperatorExecutions = runtimeOperator.ExecuteCount;
            ulong failedTick = oldTick.Value + 1UL;
            ContextAuthorityMemoryStateBinding.ArmFingerprintThrowAfterCapture = true;

            bool stepped = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() => stepped = host.TryStep(0.1d, out failure));

            Assert.That(stepped, Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintThrowCount, Is.EqualTo(1));
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(oldOperatorExecutions));
            Assert.That(host.CurrentContext, Is.EqualTo(oldAuthority));
            Assert.That(host.CurrentContext.Revision, Is.EqualTo(oldAuthority.Revision));
            Assert.That(runtime.Clock.Tick, Is.EqualTo(oldTick));
            Assert.That(
                ReadGraphState(host.CurrentContext, ids.FirstGraphStateSlotId),
                Is.EqualTo(oldGraphState));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.RequiresWorldCorrection, Is.False);
            AssertCancelledTickHasNoAuthorityTrace(host, failedTick, false);

            TickFrame resumed = CreateResumedTick(oldAuthority.Header.TickFrame);
            Assert.That(host.TryValidateRestore(
                oldAuthority,
                resumed,
                out ContextCommitStatus restoreStatus), Is.True);
            Assert.That(restoreStatus, Is.EqualTo(ContextCommitStatus.None));
            Assert.That(host.TryStep(0.1d, out CoCoDiagnostic rejected), Is.False);
            Assert.That(rejected.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(oldOperatorExecutions));
            Assert.That(host.CurrentContext, Is.EqualTo(oldAuthority));
        }

        [Test]
        public void FingerprintThrowDuringStagedCommitPreflightIsContainedAndClearsTheTick()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard, true);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextFrame oldAuthority = host.CurrentContext;
            GraphStateRecord<int> oldGraphState = ReadGraphState(
                oldAuthority,
                ids.FirstGraphStateSlotId);
            StateGraphInstance runtime = GetRuntime(host);
            TimelineTick oldTick = runtime.Clock.Tick;
            int oldOperatorExecutions = runtimeOperator.ExecuteCount;
            ulong failedTick = oldTick.Value + 1UL;
            runtimeOperator.ArmFingerprintThrowAfterExecution = true;

            bool stepped = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() => stepped = host.TryStep(0.1d, out failure));

            Assert.That(stepped, Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintThrowCount, Is.EqualTo(1));
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(oldOperatorExecutions + 1));
            Assert.That(host.CurrentContext, Is.EqualTo(oldAuthority));
            Assert.That(host.CurrentContext.Revision, Is.EqualTo(oldAuthority.Revision));
            Assert.That(runtime.Clock.Tick, Is.EqualTo(oldTick));
            Assert.That(
                ReadGraphState(host.CurrentContext, ids.FirstGraphStateSlotId),
                Is.EqualTo(oldGraphState));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.RequiresWorldCorrection, Is.True);
            AssertCancelledTickHasNoAuthorityTrace(host, failedTick, true);

            TickFrame resumed = CreateResumedTick(oldAuthority.Header.TickFrame);
            Assert.That(host.TryValidateRestore(
                oldAuthority,
                resumed,
                out ContextCommitStatus restoreStatus), Is.True);
            Assert.That(restoreStatus, Is.EqualTo(ContextCommitStatus.None));
            Assert.That(host.TryStep(0.1d, out CoCoDiagnostic rejected), Is.False);
            Assert.That(rejected.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(oldOperatorExecutions + 1));
            Assert.That(host.CurrentContext, Is.EqualTo(oldAuthority));
        }

        [Test]
        public void InitialGraphCaptureCannotMutateCommittedMemoryBeforePublication()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            ContextAuthorityMemoryStateBinding.MutateMemoryOnCapture = true;

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidContextProducer));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
        }

        [Test]
        public void DestroyDuringRuntimeStartStopsBeforeInitialGraphCapture()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            ContextAuthorityFactoryProbe.ArmNextMemoryFingerprintCallback(
                () => UnityEngine.Object.DestroyImmediate(gameObject));

            bool started = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() => started = host.TryStart(out failure));

            Assert.That(started, Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(
                failure.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host == null, Is.True);
            Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.Zero);
            Assert.That(ContextAuthorityLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void DestroyDuringFirstInitialGraphCaptureSkipsRemainingStateBindings()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            ContextAuthorityMemoryStateBinding.ArmNextCaptureCallback(
                () => UnityEngine.Object.DestroyImmediate(gameObject));

            bool started = true;
            CoCoDiagnostic failure = default;
            Assert.DoesNotThrow(() => started = host.TryStart(out failure));

            Assert.That(started, Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(
                failure.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host == null, Is.True);
            Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(1));
            Assert.That(ContextAuthorityLogic.UpdateCount, Is.Zero);
        }

        [TestCase(false, 3)]
        [TestCase(true, 3)]
        [TestCase(false, 0)]
        [TestCase(true, 0)]
        public void RestoreBindingLostDuringInitialCaptureUsesItsFinalBoundary(bool destroy, int capacity)
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject owner);
            var child = new GameObject("Initial restore binding");
            _objects.Add(child);
            child.transform.SetParent(owner.transform);
            var binding = child.AddComponent<TemporalActorRestoreBinding>();
            SetField(host, "contextRestoreBinding", binding);
            SetField(host, "temporalHistoryCapacity", capacity);
            ContextAuthorityMemoryStateBinding.ArmNextCaptureCallback(() =>
            {
                if (destroy) UnityEngine.Object.DestroyImmediate(binding);
                else child.transform.SetParent(null);
            });

            bool started = host.TryStart(out CoCoDiagnostic diagnostic);
            if (capacity > 0)
            {
                Assert.That(started, Is.False);
                Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidHostConfiguration));
                Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
                Assert.That(host.GraphInstanceId.IsValid, Is.False);
                Assert.That(host.CurrentContext.IsAlive, Is.False);
                SetField<MonoBehaviour>(host, "contextRestoreBinding", null);
                Require(host.TryStart(out diagnostic), diagnostic);
            }
            else
            {
                Require(started, diagnostic);
            }

            Require(host.TryStep(0.1d, out diagnostic), diagnostic);
            Require(host.TryCapturePersistencePayload(out byte[] payload, out diagnostic), diagnostic);
            Require(host.TryApplyPersistencePayload(payload, out diagnostic), diagnostic);
        }

        [Test]
        public void ActorBindingMustExactlyCoverSlotsAndStayWithinHostBoundary()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor);

            StateGraphHost missing = CreateHost(ids, out GameObject missingObject);
            AssertInvalidActorStartup(missing);

            StateGraphHost wrongCoverage = CreateHost(ids, out GameObject wrongObject);
            var wrongBinding = wrongObject.AddComponent<ContextAuthorityActorBinding>();
            wrongBinding.Configure(ids.FirstGraphStateSlotId);
            SetActorBinding(wrongCoverage, wrongBinding);
            AssertInvalidActorStartup(wrongCoverage);

            StateGraphHost parent = CreateHost(ids, out GameObject parentObject);
            var nestedObject = new GameObject("Nested Actor Context Host");
            _objects.Add(nestedObject);
            nestedObject.transform.SetParent(parentObject.transform);
            StateGraphHost nestedHost = nestedObject.AddComponent<StateGraphHost>();
            SetField(nestedHost, "autoStart", false);
            var nestedBinding = nestedObject.AddComponent<ContextAuthorityActorBinding>();
            nestedBinding.Configure(ids.ActorStateSlotId);
            SetActorBinding(parent, nestedBinding);
            AssertInvalidActorStartup(parent);

            Assert.That(ContextAuthorityFactoryProbe.LogicFactoryCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFactoryCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryResetCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintCount, Is.Zero);
        }

        [Test]
        public void ActorCaptureFailurePreservesAuthorityAndMarksWorldCorrection()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor, true);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            actorBinding.FailAfterWorldMutation = true;
            SetActorBinding(host, actorBinding);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(actorBinding.CaptureCount, Is.Zero);
            Assert.That(host.TryStep(0.1d, out CoCoDiagnostic failure), Is.False);

            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(1));
            Assert.That(actorBinding.CaptureCount, Is.EqualTo(1));
            Assert.That(gameObject.transform.localPosition.x, Is.EqualTo(23f));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.RequiresWorldCorrection, Is.True);
        }

        [Test]
        public void FaultedActorTransactionCanPrepareAndApplyLowLevelRestoreWithoutCallbacks()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            SetActorBinding(host, actorBinding);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextFrame source = host.CurrentContext;
            Require(source.Retain());
            try
            {
                actorBinding.FailAfterWorldMutation = true;
                Assert.That(host.TryStep(0.1d, out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
                Assert.That(host.Fault.IsFaulted, Is.True);
                Assert.That(host.RequiresWorldCorrection, Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(source));

                StateGraphInstance runtime = GetRuntime(host);
                TickFrame resumed = CreateResumedTick(source.Header.TickFrame);
                int actorCaptures = actorBinding.CaptureCount;
                int graphCaptures = ContextAuthorityMemoryStateBinding.CaptureCount;
                int logicUpdates = ContextAuthorityLogic.UpdateCount;
                ulong traceCount = host.Trace.TotalWritten;
                Vector3 dirtyWorldPosition = gameObject.transform.localPosition;
                RuntimeFault fault = host.Fault;

                Require(host.TryCaptureDebugSnapshot(
                    out HostDebugSnapshot snapshot,
                    out CoCoDiagnostic snapshotDiagnostic), snapshotDiagnostic);
                Assert.That(snapshot.Lifecycle, Is.EqualTo(host.Lifecycle));
                Assert.That(snapshot.Fault, Is.EqualTo(fault));
                Assert.That(snapshot.RequiresWorldCorrection, Is.True);
                Assert.That(
                    snapshot.LastDiagnostic.Code,
                    Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
                Assert.That(snapshot.ContextHeader, Is.EqualTo(source.Header));
                Assert.That(snapshot.ContextRevision, Is.EqualTo(source.Revision));
                Assert.That(snapshot.Tick, Is.EqualTo(source.Header.TickFrame.Tick));
                Assert.That(
                    snapshot.ExecutionSequence,
                    Is.EqualTo(source.Header.TickFrame.ExecutionSequence));
                Assert.That(
                    snapshot.Seconds,
                    Is.EqualTo(source.Header.TickFrame.TimelinePosition.Seconds));
                Assert.That(host.CurrentContext, Is.EqualTo(source));
                Assert.That(
                    runtime.Clock.TimelineId,
                    Is.EqualTo(source.Header.TickFrame.TimelineId));
                Assert.That(
                    runtime.Clock.ClockDomainId,
                    Is.EqualTo(source.Header.TickFrame.ClockDomainId));
                Assert.That(
                    runtime.Clock.TimelineEpoch,
                    Is.EqualTo(source.Header.TickFrame.TimelineEpoch));
                Assert.That(
                    runtime.Clock.Tick,
                    Is.EqualTo(source.Header.TickFrame.Tick));
                Assert.That(
                    runtime.Clock.ExecutionSequence,
                    Is.EqualTo(source.Header.TickFrame.ExecutionSequence));
                Assert.That(
                    runtime.Clock.Seconds,
                    Is.EqualTo(source.Header.TickFrame.TimelinePosition.Seconds));
                Assert.That(actorBinding.CaptureCount, Is.EqualTo(actorCaptures));
                Assert.That(
                    ContextAuthorityMemoryStateBinding.CaptureCount,
                    Is.EqualTo(graphCaptures));
                Assert.That(ContextAuthorityLogic.UpdateCount, Is.EqualTo(logicUpdates));
                Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
                Assert.That(
                    gameObject.transform.localPosition,
                    Is.EqualTo(dirtyWorldPosition));
                Assert.That(host.Fault, Is.EqualTo(fault));
                Assert.That(host.RequiresWorldCorrection, Is.True);

                Assert.That(host.TryValidateRestore(
                    source,
                    resumed,
                    out ContextCommitStatus validation), Is.True);
                Assert.That(validation, Is.EqualTo(ContextCommitStatus.None));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out PreparedHostRestore cancelled,
                    out ContextCommitStatus cancelStatus,
                    out CoCoDiagnostic cancelDiagnostic), cancelDiagnostic);
                Assert.That(cancelStatus, Is.EqualTo(ContextCommitStatus.None));
                Assert.That(cancelled.Cancel(), Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(source));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(source.Header.TickFrame.Tick));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out PreparedHostRestore prepared,
                    out ContextCommitStatus prepareStatus,
                    out CoCoDiagnostic prepareDiagnostic), prepareDiagnostic);
                Assert.That(prepareStatus, Is.EqualTo(ContextCommitStatus.None));
                prepared.CommitNoFail();

                Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(2UL));
                Assert.That(host.CurrentContext.Header.TickFrame, Is.EqualTo(resumed));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(resumed.Tick));
                Assert.That(runtime.Clock.TimelineEpoch, Is.EqualTo(resumed.TimelineEpoch));
                Assert.That(runtime.Clock.ExecutionSequence, Is.EqualTo(resumed.ExecutionSequence));
                Assert.That(runtime.Clock.Seconds, Is.EqualTo(resumed.TimelinePosition.Seconds));
                Assert.That(actorBinding.CaptureCount, Is.EqualTo(actorCaptures));
                Assert.That(ContextAuthorityLogic.UpdateCount, Is.EqualTo(logicUpdates));
                Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
                Assert.That(host.Fault.IsFaulted, Is.True);
                Assert.That(host.RequiresWorldCorrection, Is.True);
            }
            finally
            {
                Assert.That(source.Release(), Is.True);
            }
        }

        [Test]
        public void ResetToDefaultGraphRestoreCommitsPolicyEffectiveContextAndGraphCacheTogether()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.GraphResetToDefault);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextAuthorityLogic.RequestTransition = true;
            Require(host.TryStep(0.1d, out CoCoDiagnostic transition), transition);
            ContextAuthorityLogic.RequestTransition = false;
            ContextFrame source = host.CurrentContext;
            Require(source.Retain());
            try
            {
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                StateGraphInstance runtime = GetRuntime(host);
                TickFrame resumed = CreateResumedTick(source.Header.TickFrame);
                int logicUpdates = ContextAuthorityLogic.UpdateCount;
                int graphCaptures = ContextAuthorityMemoryStateBinding.CaptureCount;
                ulong traceCount = host.Trace.TotalWritten;

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out PreparedHostRestore cancelled,
                    out ContextCommitStatus cancelStatus,
                    out CoCoDiagnostic cancelDiagnostic), cancelDiagnostic);
                Assert.That(cancelStatus, Is.EqualTo(ContextCommitStatus.None));
                Assert.That(cancelled.Cancel(), Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(source));
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(source.Header.TickFrame.Tick));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out PreparedHostRestore prepared,
                    out ContextCommitStatus prepareStatus,
                    out CoCoDiagnostic prepareDiagnostic), prepareDiagnostic);
                Assert.That(prepareStatus, Is.EqualTo(ContextCommitStatus.None));
                prepared.CommitNoFail();

                Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(source.Revision.Value + 1UL));
                Assert.That(host.CurrentContext.Header.TickFrame, Is.EqualTo(resumed));
                Assert.That(
                    ReadGraphState(host.CurrentContext, ids.FirstGraphStateSlotId),
                    Is.EqualTo(ContextAuthorityDefaults.First(ids)));
                Assert.That(
                    ReadGraphState(host.CurrentContext, ids.SecondGraphStateSlotId),
                    Is.EqualTo(ContextAuthorityDefaults.Second(ids)));
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.FirstStateId));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(resumed.Tick));
                Assert.That(runtime.Clock.TimelineEpoch, Is.EqualTo(resumed.TimelineEpoch));
                Assert.That(ContextAuthorityLogic.UpdateCount, Is.EqualTo(logicUpdates));
                Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(graphCaptures));
                Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
            }
            finally
            {
                Assert.That(source.Release(), Is.True);
            }
        }

        [Test]
        public void MixedGraphRestorePoliciesRejectPolicyEffectiveInvalidPathBeforeMemoryCallbacks()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.GraphMixedRestorePolicies);
            StateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextAuthorityLogic.RequestTransition = true;
            Require(host.TryStep(0.1d, out CoCoDiagnostic transition), transition);
            ContextAuthorityLogic.RequestTransition = false;
            ContextFrame oldAuthority = host.CurrentContext;
            StateGraphInstance runtime = GetRuntime(host);
            TimelineTick oldTick = runtime.Clock.Tick;
            TickFrame resumed = CreateResumedTick(oldAuthority.Header.TickFrame);
            int logicUpdates = ContextAuthorityLogic.UpdateCount;
            int graphCaptures = ContextAuthorityMemoryStateBinding.CaptureCount;
            int restorePrepares = ContextAuthorityMemoryStateBinding.RestorePrepareCount;
            int memoryFingerprints = ContextAuthorityFactoryProbe.MemoryFingerprintCount;
            ulong traceCount = host.Trace.TotalWritten;

            Assert.That(host.TryValidateRestore(
                oldAuthority,
                resumed,
                out ContextCommitStatus validationStatus), Is.False);
            Assert.That(validationStatus, Is.EqualTo(ContextCommitStatus.RestoreFailed));
            Assert.That(ContextAuthorityMemoryStateBinding.RestorePrepareCount, Is.EqualTo(restorePrepares));
            Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(graphCaptures));
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintCount, Is.EqualTo(memoryFingerprints));

            Assert.That(host.TryPrepareRestore(
                oldAuthority,
                resumed,
                out _,
                out ContextCommitStatus prepareStatus,
                out CoCoDiagnostic prepareDiagnostic), Is.False);
            Assert.That(prepareStatus, Is.EqualTo(ContextCommitStatus.RestoreFailed));
            Assert.That(prepareDiagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidGraphRestore));
            Assert.That(host.CurrentContext, Is.EqualTo(oldAuthority));
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
            Assert.That(runtime.Clock.Tick, Is.EqualTo(oldTick));
            Assert.That(ContextAuthorityLogic.UpdateCount, Is.EqualTo(logicUpdates));
            Assert.That(ContextAuthorityMemoryStateBinding.RestorePrepareCount, Is.EqualTo(restorePrepares));
            Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(graphCaptures));
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintCount, Is.EqualTo(memoryFingerprints));
            Assert.That(host.Trace.TotalWritten, Is.EqualTo(traceCount));
        }

        [Test]
        public void GraphProducerOperatorAndActorBindingHaveZeroSteadyStateManagedAllocation()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor, true);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            SetActorBinding(host, actorBinding);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(
                ids.OperatorId,
                ids.FirstGraphStateSlotId,
                ids.OperationId);
            SetOperators(host, runtimeOperator);

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
            int expectedExecutions =
                AllocationWarmupIterations + AllocationMeasuredIterations;
            Assert.That(runtimeOperator.ExecuteCount, Is.EqualTo(expectedExecutions));
            Assert.That(actorBinding.CaptureCount, Is.EqualTo(expectedExecutions));
        }

        [Test]
        public void InitialGraphDefaultMismatchRejectsStartupBeforeHostPublication()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.GraphDefaultValueMismatch);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidContextProducer));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(ContextAuthorityFactoryProbe.LogicFactoryCount, Is.GreaterThan(0));
            Assert.That(ContextAuthorityFactoryProbe.MemoryFactoryCount, Is.GreaterThan(0));
        }

        [Test]
        public void TrustedDefaultFingerprintMismatchRejectsBeforeRuntimeFactories()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.GraphDefaultFingerprintMismatch);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Registry));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(ContextAuthorityFactoryProbe.LogicFactoryCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFactoryCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryResetCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintCount, Is.Zero);
        }

        [Test]
        public void RestoreValidationRejectsMultipleActiveLeaves()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic step), step);
            ContextFrame committed = host.CurrentContext;
            Require(ActivationId.TryCreate(2UL, out ActivationId secondActivation));
            Require(GraphStateRecord<int>.TryCreate(
                ids.LayerId,
                ids.SecondStateId,
                true,
                secondActivation,
                0d,
                0d,
                false,
                0UL,
                0,
                out GraphStateRecord<int> second));

            using (ContextFrameArena sourceArena = CreateRestoreSource(
                       host,
                       ids,
                       ContextAuthorityDefaults.First(ids),
                       second,
                       out ContextFrame source))
            {
                TickFrame resumed = CreateResumedTick(committed.Header.TickFrame);
                int captureCount = ContextAuthorityMemoryStateBinding.CaptureCount;

                Assert.That(host.TryValidateRestore(
                    source,
                    resumed,
                    out ContextCommitStatus status), Is.False);
                Assert.That(status, Is.EqualTo(ContextCommitStatus.RestoreFailed));
                Assert.That(host.CurrentContext, Is.EqualTo(committed));
                Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(captureCount));
            }
        }

        [Test]
        public void RestoreValidationRejectsDuplicateGraphActivationIds()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic step), step);
            ContextFrame committed = host.CurrentContext;
            GraphStateRecord<int> first = ContextAuthorityDefaults.First(ids);
            Require(GraphStateRecord<int>.TryCreate(
                ids.LayerId,
                ids.SecondStateId,
                false,
                first.ActivationId,
                0d,
                0d,
                false,
                0UL,
                0,
                out GraphStateRecord<int> second));

            using (ContextFrameArena sourceArena = CreateRestoreSource(
                       host,
                       ids,
                       first,
                       second,
                       out ContextFrame source))
            {
                TickFrame resumed = CreateResumedTick(committed.Header.TickFrame);
                int captureCount = ContextAuthorityMemoryStateBinding.CaptureCount;

                Assert.That(host.TryValidateRestore(
                    source,
                    resumed,
                    out ContextCommitStatus status), Is.False);
                Assert.That(status, Is.EqualTo(ContextCommitStatus.RestoreFailed));
                Assert.That(host.CurrentContext, Is.EqualTo(committed));
                Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(captureCount));
            }
        }

        [Test]
        public void DestroyedActorReferenceIsRejectedWhenLayoutHasNoActorSlots()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            StateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            SetActorBinding(host, actorBinding);
            UnityEngine.Object.DestroyImmediate(actorBinding);

            AssertInvalidActorStartup(host);
            Assert.That(ContextAuthorityFactoryProbe.LogicFactoryCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFactoryCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryResetCount, Is.Zero);
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintCount, Is.Zero);
        }

        private void InstallProvider(
            ContextAuthorityTestIds ids,
            ContextAuthorityBindingMode mode,
            bool includeOperation = false)
        {
            var provider = new ContextAuthorityBindingProvider(ids, mode, includeOperation);
            Require(
                HostProjectBindings.TryInstall(
                    provider,
                    out CoCoDiagnostic diagnostic),
                diagnostic);
        }

        private StateGraphHost CreateHost(
            ContextAuthorityTestIds ids,
            out GameObject gameObject,
            int traceCapacity = 0)
        {
            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var source = new StateGraphStateRecord(
                Serialize(ids.FirstStateId),
                default,
                "Context Source",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var target = new StateGraphStateRecord(
                Serialize(ids.SecondStateId),
                default,
                "Context Target",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 2 });
            var transition = new StateGraphTransitionRecord(
                Serialize(ids.TransitionId),
                Serialize(ids.FirstStateId),
                Serialize(ids.SecondStateId),
                1);
            var layer = new StateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.FirstStateId);
            layer.States.Add(source);
            layer.States.Add(target);
            layer.Transitions.Add(transition);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Context Authority Host");
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

        private static StateGraphInstance GetRuntime(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_instance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StateGraphInstance)field.GetValue(host);
        }

        private static GraphStateRecord<int> ReadGraphState(
            ContextFrame frame,
            StateSlotId slotId)
        {
            Require(frame.Layout.TryResolveSlot(
                slotId,
                out StateSlot<GraphStateRecord<int>> slot));
            return frame.Read(slot);
        }

        private static void AssertCancelledTickHasNoAuthorityTrace(
            StateGraphHost host,
            ulong failedTick,
            bool expectOperatorOutcome)
        {
            var entries = new StateFlowTraceEntry[64];
            int count = host.Trace.CopyLatestTo(entries);
            bool sawTick = false;
            bool sawCancelled = false;
            bool sawOperatorOutcome = false;
            for (int index = 0; index < count; index++)
            {
                StateFlowTraceEntry entry = entries[index];
                if (entry.TickFrame.Tick.Value != failedTick)
                {
                    continue;
                }

                sawTick |= entry.Kind == StateFlowTraceKind.Tick;
                sawCancelled |= entry.Kind == StateFlowTraceKind.Cancelled;
                sawOperatorOutcome |= entry.Kind == StateFlowTraceKind.OperatorOutcome;
                Assert.That(entry.Kind, Is.Not.EqualTo(StateFlowTraceKind.ContextCommit));
                Assert.That(entry.Kind, Is.Not.EqualTo(StateFlowTraceKind.EventSequence));
                Assert.That(entry.Kind, Is.Not.EqualTo(StateFlowTraceKind.EventPublished));
            }

            Assert.That(sawTick, Is.True);
            Assert.That(sawCancelled, Is.True);
            Assert.That(sawOperatorOutcome, Is.EqualTo(expectOperatorOutcome));
        }

        private static ContextFrameArena CreateRestoreSource(
            StateGraphHost host,
            ContextAuthorityTestIds ids,
            in GraphStateRecord<int> first,
            in GraphStateRecord<int> second,
            out ContextFrame source)
        {
            ContextFrame committed = host.CurrentContext;
            var arena = new ContextFrameArena(
                host.GraphInstanceId,
                committed.Layout,
                2);
            Require(committed.Layout.TryResolveBlock(
                ids.GraphStateBlockId,
                out StateBlockHandle block));
            Require(committed.Layout.TryResolveSlot(
                ids.FirstGraphStateSlotId,
                out StateSlot<GraphStateRecord<int>> firstSlot));
            Require(committed.Layout.TryResolveSlot(
                ids.SecondGraphStateSlotId,
                out StateSlot<GraphStateRecord<int>> secondSlot));
            Require(arena.TryPrepare(
                committed.Header.TickFrame,
                out PreparedContextCommit prepared,
                out ContextCommitStatus prepareStatus));
            Assert.That(prepareStatus, Is.EqualTo(ContextCommitStatus.None));
            Require(prepared.TryGetWriter(block, out ContextFrameWriter writer));
            Require(writer.Write(firstSlot, first));
            Require(writer.Write(secondSlot, second));
            Require(prepared.TryFinalize(
                out FinalizedContextCommit finalized,
                out ContextCommitStatus finalizeStatus));
            Assert.That(finalizeStatus, Is.EqualTo(ContextCommitStatus.None));
            ContextCommitResult result = finalized.Commit();
            Assert.That(result.Succeeded, Is.True, result.Status.ToString());
            source = result.Frame;
            return arena;
        }

        private static TickFrame CreateResumedTick(in TickFrame source)
        {
            Require(TimelinePosition.TryCreate(
                source.TimelinePosition.Seconds + 1d,
                out TimelinePosition position));
            Require(TickFrame.TryCreate(
                source.DeltaTime,
                source.TimelineId,
                position,
                new TimelineTick(source.Tick.Value + 1UL),
                source.ClockDomainId,
                new ExecutionSequence(source.ExecutionSequence.Value + 1UL),
                new TimelineEpoch(source.TimelineEpoch.Value + 1UL),
                out TickFrame resumed,
                out CoCoDiagnostic diagnostic), diagnostic);
            return resumed;
        }

        private static void AssertInvalidActorStartup(StateGraphHost host)
        {
            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidHostConfiguration));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
        }

        private static void SetOperators(
            StateGraphHost host,
            params MonoBehaviour[] operators) => SetField(host, "operators", operators);

        private static void SetActorBinding(
            StateGraphHost host,
            MonoBehaviour actorBinding) =>
            SetField(host, "hostContextBinding", actorBinding);

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

        private static SerializedId128 Serialize(LayerId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(TransitionId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateDescriptorId id) =>
            new SerializedId128(id.High, id.Low);

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }

        private enum ContextAuthorityBindingMode
        {
            Standard = 0,
            WithActor = 1,
            GraphDefaultValueMismatch = 2,
            GraphDefaultFingerprintMismatch = 3,
            GraphResetToDefault = 4,
            GraphMixedRestorePolicies = 5
        }

        private sealed class ContextAuthorityBindingProvider :
            IHostBindingProvider
        {
            private readonly ContextAuthorityTestIds _ids;
            private readonly ContextAuthorityBindingMode _mode;
            private readonly bool _includeOperation;
            private readonly OperatorCommitPrimarySectionFactory _operation =
                new OperatorCommitPrimarySectionFactory();

            public ContextAuthorityBindingProvider(
                ContextAuthorityTestIds ids,
                ContextAuthorityBindingMode mode,
                bool includeOperation)
            {
                _ids = ids;
                _mode = mode;
                _includeOperation = includeOperation;
                Catalog = BuildCatalog(ids, mode, includeOperation);
            }

            public GraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                HostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                if (_includeOperation &&
                    !builder.TryRegisterOperation(
                        _ids.OperationId,
                        Mode.Discrete,
                        _operation,
                        OperationFactoryFingerprint,
                        out OperationRequirement ignored,
                        out diagnostic))
                {
                    return false;
                }

                GraphStateRecord<int> first = ContextAuthorityDefaults.First(
                    _ids,
                    _mode == ContextAuthorityBindingMode.GraphDefaultValueMismatch ? 91 : 0);
                ulong firstFingerprint = _mode ==
                                         ContextAuthorityBindingMode.GraphDefaultFingerprintMismatch
                    ? ContextAuthorityDefaults.FirstGraphStateFingerprint + 1UL
                    : ContextAuthorityDefaults.FirstGraphStateFingerprint;
                var memoryBinding = new ContextAuthorityMemoryStateBinding();
                if (!builder.TryBindGraphStateSlot<
                        ContextAuthorityMemory,
                        int,
                        ContextAuthorityMemoryStateBinding>(
                        _ids.LayerId,
                        _ids.FirstStateId,
                        _ids.GraphStateBlockId,
                        _ids.FirstGraphStateSlotId,
                        first,
                        firstFingerprint,
                        memoryBinding,
                        out diagnostic) ||
                    !builder.TryBindGraphStateSlot<
                        ContextAuthorityMemory,
                        int,
                        ContextAuthorityMemoryStateBinding>(
                        _ids.LayerId,
                        _ids.SecondStateId,
                        _ids.GraphStateBlockId,
                        _ids.SecondGraphStateSlotId,
                        ContextAuthorityDefaults.Second(_ids),
                        ContextAuthorityDefaults.SecondGraphStateFingerprint,
                        memoryBinding,
                        out diagnostic))
                {
                    return false;
                }

                if (_mode == ContextAuthorityBindingMode.WithActor &&
                    !builder.TryBindContextSlot(
                        _ids.ActorStateBlockId,
                        _ids.ActorStateSlotId,
                        ContextAuthorityDefaults.ActorStateValue,
                        ContextAuthorityDefaults.ActorStateFingerprint,
                        out diagnostic))
                {
                    return false;
                }

                var factory = new StateRuntimeFactory<
                    ContextAuthorityLogic,
                    ContextAuthorityMemory>(
                    context =>
                    {
                        ContextAuthorityFactoryProbe.RecordLogicFactory();
                        return new ContextAuthorityLogic(
                            context,
                            _operation.Handle,
                            _operation.ValueField);
                    },
                    () =>
                    {
                        ContextAuthorityFactoryProbe.RecordMemoryFactory();
                        return new ContextAuthorityMemory();
                    },
                    (source, destination) => destination.Value = source.Value,
                    memory =>
                    {
                        ContextAuthorityFactoryProbe.RecordMemoryReset();
                        memory.Value = 0;
                    },
                    ContextAuthorityFactoryProbe.RecordMemoryFingerprint);
                return builder.TryBindState(
                    _ids.StateDescriptorId,
                    factory,
                    out diagnostic);
            }

            private static GraphDescriptorCatalog BuildCatalog(
                ContextAuthorityTestIds ids,
                ContextAuthorityBindingMode mode,
                bool includeOperation)
            {
                bool includeActor = mode == ContextAuthorityBindingMode.WithActor;
                ContextRestorePolicy firstRestorePolicy =
                    mode == ContextAuthorityBindingMode.GraphResetToDefault ||
                    mode == ContextAuthorityBindingMode.GraphMixedRestorePolicies
                        ? ContextRestorePolicy.ResetToDefault
                        : ContextRestorePolicy.Stored;
                ContextRestorePolicy secondRestorePolicy =
                    mode == ContextAuthorityBindingMode.GraphResetToDefault
                        ? ContextRestorePolicy.ResetToDefault
                        : ContextRestorePolicy.Stored;
                var builder = new GraphDescriptorCatalogBuilder();
                OperationId[] operations = null;
                if (includeOperation)
                {
                    Ensure(builder.TryRegisterOperation(
                        ids.OperationId,
                        Mode.Discrete,
                        new OperationViewFactoryToken<
                            IOperatorCommitPrimarySection,
                            OperatorCommitPrimarySectionFactory>(
                            OperationFactoryFingerprint),
                        out CoCoDiagnostic operation), operation);
                    operations = new[] { ids.OperationId };
                }

                Ensure(builder.TryRegisterStateBlock(
                    ids.GraphStateBlockId,
                    StateBlockOwner.Graph,
                    out CoCoDiagnostic graphBlock), graphBlock);
                Ensure(builder.TryRegisterStateSlot(
                    ids.GraphStateBlockId,
                    ids.FirstGraphStateSlotId,
                    ContextProjection.Temporal,
                    firstRestorePolicy,
                    ContextAuthorityDefaults.First(ids),
                    ContextAuthorityDefaults.FirstGraphStateFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic firstSlot), firstSlot);
                Ensure(builder.TryRegisterStateSlot(
                    ids.GraphStateBlockId,
                    ids.SecondGraphStateSlotId,
                    ContextProjection.Temporal,
                    secondRestorePolicy,
                    ContextAuthorityDefaults.Second(ids),
                    ContextAuthorityDefaults.SecondGraphStateFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic secondSlot), secondSlot);

                StateBlockId[] contextBlocks;
                if (includeActor)
                {
                    Ensure(builder.TryRegisterStateBlock(
                        ids.ActorStateBlockId,
                        StateBlockOwner.Host,
                        out CoCoDiagnostic actorBlock), actorBlock);
                    Ensure(builder.TryRegisterStateSlot(
                        ids.ActorStateBlockId,
                        ids.ActorStateSlotId,
                        ContextProjection.Temporal,
                        ContextRestorePolicy.Stored,
                        ContextAuthorityDefaults.ActorStateValue,
                        ContextAuthorityDefaults.ActorStateFingerprint,
                        default,
                        null,
                        out CoCoDiagnostic actorSlot), actorSlot);
                    contextBlocks = new[] { ids.GraphStateBlockId, ids.ActorStateBlockId };
                }
                else
                {
                    contextBlocks = new[] { ids.GraphStateBlockId };
                }

                Ensure(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        ContextAuthorityLogic,
                        HostTestStateConfigSchema,
                        ContextAuthorityMemory>(HostTestSchemas.State, false),
                    null,
                    operations,
                    contextBlocks,
                    out CoCoDiagnostic state), state);
                Ensure(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze), freeze);
                return catalog;
            }

            private static void Ensure(bool succeeded, CoCoDiagnostic diagnostic)
            {
                if (!succeeded)
                {
                    throw new InvalidOperationException(diagnostic.Message);
                }
            }
        }

        private sealed class ContextAuthorityProbeOperator : MonoBehaviour, IOperator
        {
            private StateSlotId _graphStateSlotId;
            private OperatorDescriptor _descriptor;

            public OperatorDescriptor Descriptor => _descriptor;
            public int ExecuteCount { get; private set; }
            public bool PreviousHadCommittedFrame { get; private set; }
            public GraphStateRecord<int> PreviousState { get; private set; }
            public bool ArmFingerprintThrowAfterExecution { get; set; }

            public void Configure(
                OperatorId operatorId,
                StateSlotId graphStateSlotId,
                OperationId operationSectionId)
            {
                _graphStateSlotId = graphStateSlotId;
                var builder = new OperatorDescriptorBuilder();
                CoCoDiagnostic requirement = default;
                CoCoDiagnostic freeze = default;
                if (!builder.TryRequire<IOperatorCommitPrimarySection>(
                        operationSectionId,
                        Mode.Discrete,
                        out OperationRequirement ignored,
                        out requirement) ||
                    !builder.TryFreeze<ContextAuthorityProbeOperator>(
                        operatorId,
                        out _descriptor,
                        out freeze))
                {
                    throw new InvalidOperationException(
                        requirement.IsError ? requirement.Message : freeze.Message);
                }
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                ExecuteCount++;
                PreviousHadCommittedFrame = context.PreviousContext.HasCommittedFrame;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _graphStateSlotId,
                        out StateSlot<GraphStateRecord<int>> slot))
                {
                    outcome = OperatorOutcome.Rejected(CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Operator,
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "Context authority probe could not resolve its Graph State Slot."));
                    return false;
                }

                PreviousState = context.PreviousContext.Read(slot);
                if (ArmFingerprintThrowAfterExecution)
                {
                    ArmFingerprintThrowAfterExecution = false;
                    ContextAuthorityFactoryProbe.ArmNextMemoryFingerprintThrow();
                }

                outcome = OperatorOutcome.Success;
                return true;
            }
        }

        private sealed class ContextAuthorityActorBinding :
            MonoBehaviour,
            IHostContextBinding
        {
            private StateSlotId _slotId;
            private HostContextBindingDescriptor _descriptor;

            public HostContextBindingDescriptor Descriptor => _descriptor;
            public int CaptureCount { get; private set; }
            public bool FailAfterWorldMutation { get; set; }
            public int Value { get; set; } = 41;

            public void Configure(StateSlotId slotId)
            {
                _slotId = slotId;
                var builder = new HostContextBindingDescriptorBuilder();
                if (!builder.TryProduce<int>(slotId, out CoCoDiagnostic produce))
                {
                    throw new InvalidOperationException(produce.Message);
                }

                if (!builder.TryFreeze<ContextAuthorityActorBinding>(
                        70901UL,
                        out _descriptor,
                        out CoCoDiagnostic freeze))
                {
                    throw new InvalidOperationException(freeze.Message);
                }
            }

            public bool TryCapture(
                in HostContextCaptureContext context,
                out CoCoDiagnostic diagnostic)
            {
                CaptureCount++;
                transform.localPosition = new Vector3(23f, 0f, 0f);
                if (FailAfterWorldMutation)
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Context,
                        CoCoDiagnosticCode.ContextCaptureFailed,
                        "Context authority Actor fixture failed after mutating Unity state.");
                    return false;
                }

                if (!context.Writer.TryWrite(_slotId, Value))
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Context,
                        CoCoDiagnosticCode.ContextCaptureFailed,
                        "Context authority Actor fixture could not write its exact Slot.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
