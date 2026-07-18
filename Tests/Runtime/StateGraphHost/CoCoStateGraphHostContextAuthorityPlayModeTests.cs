using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostContextAuthorityPlayModeTests
    {
        private const int AllocationWarmupIterations = 100;
        private const int AllocationMeasuredIterations = 10000;
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
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
            CoCoStateGraphProjectBindings.ResetForTests();
            ContextAuthorityFactoryProbe.Reset();
            ContextAuthorityMemoryStateBinding.Reset();
            ContextAuthorityLogic.Reset();
        }

        [Test]
        public void FirstTickUsesDefaultBackedPreviousAndCommitsRevisionOne()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(ids.OperatorId, ids.FirstGraphStateSlotId);
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

            CoCoGraphStateRecord<int> first = ReadGraphState(
                host.CurrentContext,
                ids.FirstGraphStateSlotId);
            CoCoGraphStateRecord<int> second = ReadGraphState(
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic firstStep), firstStep);
            ContextAuthorityLogic.RequestTransition = true;
            Require(host.TryStep(0.1d, out CoCoDiagnostic transitionStep), transitionStep);

            Assert.That(host.CurrentContext.Revision.Value, Is.EqualTo(2UL));
            Assert.That(host.ActivePaths, Has.Count.EqualTo(1));
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));

            CoCoGraphStateRecord<int> source = ReadGraphState(
                host.CurrentContext,
                ids.FirstGraphStateSlotId);
            CoCoGraphStateRecord<int> target = ReadGraphState(
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
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(ids.OperatorId, ids.FirstGraphStateSlotId);
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
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            CoCoStateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 32);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(ids.OperatorId, ids.FirstGraphStateSlotId);
            SetOperators(host, runtimeOperator);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            ContextAuthorityMemoryStateBinding.MutateMemoryOnCapture = true;

            Assert.That(host.TryStep(0.1d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(runtimeOperator.ExecuteCount, Is.Zero);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
            Assert.That(host.Fault.IsFaulted, Is.True);

            var entries = new CoCoStateFlowTraceEntry[32];
            int count = host.Trace.CopyLatestTo(entries);
            Assert.That(count, Is.GreaterThanOrEqualTo(2));
            Assert.That(entries[0].Kind, Is.EqualTo(CoCoStateFlowTraceKind.Tick));
            Assert.That(entries[count - 1].Kind, Is.EqualTo(CoCoStateFlowTraceKind.Cancelled));
            for (int index = 0; index < count; index++)
            {
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.ContextCommit));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.EventSequence));
                Assert.That(entries[index].Kind, Is.Not.EqualTo(CoCoStateFlowTraceKind.EventPublished));
            }
        }

        [Test]
        public void InitialGraphCaptureCannotMutateCommittedMemoryBeforePublication()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            ContextAuthorityMemoryStateBinding.MutateMemoryOnCapture = true;

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidContextProducer));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
        }

        [Test]
        public void ActorBindingMustExactlyCoverSlotsAndStayWithinHostBoundary()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor);

            CoCoStateGraphHost missing = CreateHost(ids, out GameObject missingObject);
            AssertInvalidActorStartup(missing);

            CoCoStateGraphHost wrongCoverage = CreateHost(ids, out GameObject wrongObject);
            var wrongBinding = wrongObject.AddComponent<ContextAuthorityActorBinding>();
            wrongBinding.Configure(ids.FirstGraphStateSlotId);
            SetActorBinding(wrongCoverage, wrongBinding);
            AssertInvalidActorStartup(wrongCoverage);

            CoCoStateGraphHost parent = CreateHost(ids, out GameObject parentObject);
            var nestedObject = new GameObject("Nested Actor Context Host");
            _objects.Add(nestedObject);
            nestedObject.transform.SetParent(parentObject.transform);
            CoCoStateGraphHost nestedHost = nestedObject.AddComponent<CoCoStateGraphHost>();
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
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            actorBinding.FailAfterWorldMutation = true;
            SetActorBinding(host, actorBinding);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(ids.OperatorId, ids.FirstGraphStateSlotId);
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
            CoCoStateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            SetActorBinding(host, actorBinding);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            CoCoContextFrame source = host.CurrentContext;
            Require(source.Retain());
            try
            {
                actorBinding.FailAfterWorldMutation = true;
                Assert.That(host.TryStep(0.1d, out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.ContextCaptureFailed));
                Assert.That(host.Fault.IsFaulted, Is.True);
                Assert.That(host.RequiresWorldCorrection, Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(source));

                CoCoStateGraphRuntime runtime = GetRuntime(host);
                CoCoTickFrame resumed = CreateResumedTick(source.Header.TickFrame);
                int actorCaptures = actorBinding.CaptureCount;
                int logicUpdates = ContextAuthorityLogic.UpdateCount;
                ulong traceCount = host.Trace.TotalWritten;
                Assert.That(host.TryValidateRestore(
                    source,
                    resumed,
                    out CoCoContextCommitStatus validation), Is.True);
                Assert.That(validation, Is.EqualTo(CoCoContextCommitStatus.None));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out CoCoPreparedActorRestore cancelled,
                    out CoCoContextCommitStatus cancelStatus,
                    out CoCoDiagnostic cancelDiagnostic), cancelDiagnostic);
                Assert.That(cancelStatus, Is.EqualTo(CoCoContextCommitStatus.None));
                Assert.That(cancelled.Cancel(), Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(source));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(source.Header.TickFrame.Tick));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out CoCoPreparedActorRestore prepared,
                    out CoCoContextCommitStatus prepareStatus,
                    out CoCoDiagnostic prepareDiagnostic), prepareDiagnostic);
                Assert.That(prepareStatus, Is.EqualTo(CoCoContextCommitStatus.None));
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
            CoCoStateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextAuthorityLogic.RequestTransition = true;
            Require(host.TryStep(0.1d, out CoCoDiagnostic transition), transition);
            ContextAuthorityLogic.RequestTransition = false;
            CoCoContextFrame source = host.CurrentContext;
            Require(source.Retain());
            try
            {
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                CoCoStateGraphRuntime runtime = GetRuntime(host);
                CoCoTickFrame resumed = CreateResumedTick(source.Header.TickFrame);
                int logicUpdates = ContextAuthorityLogic.UpdateCount;
                int graphCaptures = ContextAuthorityMemoryStateBinding.CaptureCount;
                ulong traceCount = host.Trace.TotalWritten;

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out CoCoPreparedActorRestore cancelled,
                    out CoCoContextCommitStatus cancelStatus,
                    out CoCoDiagnostic cancelDiagnostic), cancelDiagnostic);
                Assert.That(cancelStatus, Is.EqualTo(CoCoContextCommitStatus.None));
                Assert.That(cancelled.Cancel(), Is.True);
                Assert.That(host.CurrentContext, Is.EqualTo(source));
                Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(ids.SecondStateId));
                Assert.That(runtime.Clock.Tick, Is.EqualTo(source.Header.TickFrame.Tick));

                Require(host.TryPrepareRestore(
                    source,
                    resumed,
                    out CoCoPreparedActorRestore prepared,
                    out CoCoContextCommitStatus prepareStatus,
                    out CoCoDiagnostic prepareDiagnostic), prepareDiagnostic);
                Assert.That(prepareStatus, Is.EqualTo(CoCoContextCommitStatus.None));
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
            CoCoStateGraphHost host = CreateHost(
                ids,
                out GameObject gameObject,
                traceCapacity: 64);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic first), first);
            ContextAuthorityLogic.RequestTransition = true;
            Require(host.TryStep(0.1d, out CoCoDiagnostic transition), transition);
            ContextAuthorityLogic.RequestTransition = false;
            CoCoContextFrame oldAuthority = host.CurrentContext;
            CoCoStateGraphRuntime runtime = GetRuntime(host);
            CoCoTimelineTick oldTick = runtime.Clock.Tick;
            CoCoTickFrame resumed = CreateResumedTick(oldAuthority.Header.TickFrame);
            int logicUpdates = ContextAuthorityLogic.UpdateCount;
            int graphCaptures = ContextAuthorityMemoryStateBinding.CaptureCount;
            int restorePrepares = ContextAuthorityMemoryStateBinding.RestorePrepareCount;
            int memoryFingerprints = ContextAuthorityFactoryProbe.MemoryFingerprintCount;
            ulong traceCount = host.Trace.TotalWritten;

            Assert.That(host.TryValidateRestore(
                oldAuthority,
                resumed,
                out CoCoContextCommitStatus validationStatus), Is.False);
            Assert.That(validationStatus, Is.EqualTo(CoCoContextCommitStatus.RestoreFailed));
            Assert.That(ContextAuthorityMemoryStateBinding.RestorePrepareCount, Is.EqualTo(restorePrepares));
            Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(graphCaptures));
            Assert.That(ContextAuthorityFactoryProbe.MemoryFingerprintCount, Is.EqualTo(memoryFingerprints));

            Assert.That(host.TryPrepareRestore(
                oldAuthority,
                resumed,
                out _,
                out CoCoContextCommitStatus prepareStatus,
                out CoCoDiagnostic prepareDiagnostic), Is.False);
            Assert.That(prepareStatus, Is.EqualTo(CoCoContextCommitStatus.RestoreFailed));
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
            InstallProvider(ids, ContextAuthorityBindingMode.WithActor);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
            var actorBinding = gameObject.AddComponent<ContextAuthorityActorBinding>();
            actorBinding.Configure(ids.ActorStateSlotId);
            SetActorBinding(host, actorBinding);
            var runtimeOperator = gameObject.AddComponent<ContextAuthorityProbeOperator>();
            runtimeOperator.Configure(ids.OperatorId, ids.FirstGraphStateSlotId);
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidContextProducer));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Registry));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
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
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic step), step);
            CoCoContextFrame committed = host.CurrentContext;
            Require(CoCoActivationId.TryCreate(2UL, out CoCoActivationId secondActivation));
            Require(CoCoGraphStateRecord<int>.TryCreate(
                ids.LayerId,
                ids.SecondStateId,
                true,
                secondActivation,
                0d,
                0d,
                false,
                0UL,
                0,
                out CoCoGraphStateRecord<int> second));

            using (CoCoContextFrameArena sourceArena = CreateRestoreSource(
                       host,
                       ids,
                       ContextAuthorityDefaults.First(ids),
                       second,
                       out CoCoContextFrame source))
            {
                CoCoTickFrame resumed = CreateResumedTick(committed.Header.TickFrame);
                int captureCount = ContextAuthorityMemoryStateBinding.CaptureCount;

                Assert.That(host.TryValidateRestore(
                    source,
                    resumed,
                    out CoCoContextCommitStatus status), Is.False);
                Assert.That(status, Is.EqualTo(CoCoContextCommitStatus.RestoreFailed));
                Assert.That(host.CurrentContext, Is.EqualTo(committed));
                Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(captureCount));
            }
        }

        [Test]
        public void RestoreValidationRejectsDuplicateGraphActivationIds()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TryStep(0.1d, out CoCoDiagnostic step), step);
            CoCoContextFrame committed = host.CurrentContext;
            CoCoGraphStateRecord<int> first = ContextAuthorityDefaults.First(ids);
            Require(CoCoGraphStateRecord<int>.TryCreate(
                ids.LayerId,
                ids.SecondStateId,
                false,
                first.ActivationId,
                0d,
                0d,
                false,
                0UL,
                0,
                out CoCoGraphStateRecord<int> second));

            using (CoCoContextFrameArena sourceArena = CreateRestoreSource(
                       host,
                       ids,
                       first,
                       second,
                       out CoCoContextFrame source))
            {
                CoCoTickFrame resumed = CreateResumedTick(committed.Header.TickFrame);
                int captureCount = ContextAuthorityMemoryStateBinding.CaptureCount;

                Assert.That(host.TryValidateRestore(
                    source,
                    resumed,
                    out CoCoContextCommitStatus status), Is.False);
                Assert.That(status, Is.EqualTo(CoCoContextCommitStatus.RestoreFailed));
                Assert.That(host.CurrentContext, Is.EqualTo(committed));
                Assert.That(ContextAuthorityMemoryStateBinding.CaptureCount, Is.EqualTo(captureCount));
            }
        }

        [Test]
        public void DestroyedActorReferenceIsRejectedWhenLayoutHasNoActorSlots()
        {
            ContextAuthorityTestIds ids = ContextAuthorityTestIds.Create();
            InstallProvider(ids, ContextAuthorityBindingMode.Standard);
            CoCoStateGraphHost host = CreateHost(ids, out GameObject gameObject);
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
            ContextAuthorityBindingMode mode)
        {
            var provider = new ContextAuthorityBindingProvider(ids, mode);
            Require(
                CoCoStateGraphProjectBindings.TryInstall(
                    provider,
                    out CoCoDiagnostic diagnostic),
                diagnostic);
        }

        private CoCoStateGraphHost CreateHost(
            ContextAuthorityTestIds ids,
            out GameObject gameObject,
            int traceCapacity = 0)
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var source = new CoCoStateGraphStateRecord(
                Serialize(ids.FirstStateId),
                default,
                "Context Source",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var target = new CoCoStateGraphStateRecord(
                Serialize(ids.SecondStateId),
                default,
                "Context Target",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 2 });
            var transition = new CoCoStateGraphTransitionRecord(
                Serialize(ids.TransitionId),
                Serialize(ids.FirstStateId),
                Serialize(ids.SecondStateId),
                1);
            var layer = new CoCoStateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.FirstStateId);
            layer.States.Add(source);
            layer.States.Add(target);
            layer.Transitions.Add(transition);
            asset.Layers.Add(layer);

            gameObject = new GameObject("Pre5 Context Authority Host");
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

        private static CoCoStateGraphRuntime GetRuntime(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphRuntime)field.GetValue(host);
        }

        private static CoCoGraphStateRecord<int> ReadGraphState(
            CoCoContextFrame frame,
            CoCoStateSlotId slotId)
        {
            Require(frame.Layout.TryResolveSlot(
                slotId,
                out CoCoStateSlot<CoCoGraphStateRecord<int>> slot));
            return frame.Read(slot);
        }

        private static CoCoContextFrameArena CreateRestoreSource(
            CoCoStateGraphHost host,
            ContextAuthorityTestIds ids,
            in CoCoGraphStateRecord<int> first,
            in CoCoGraphStateRecord<int> second,
            out CoCoContextFrame source)
        {
            CoCoContextFrame committed = host.CurrentContext;
            var arena = new CoCoContextFrameArena(
                host.GraphInstanceId,
                committed.Layout,
                2);
            Require(committed.Layout.TryResolveBlock(
                ids.GraphStateBlockId,
                out CoCoStateBlockHandle block));
            Require(committed.Layout.TryResolveSlot(
                ids.FirstGraphStateSlotId,
                out CoCoStateSlot<CoCoGraphStateRecord<int>> firstSlot));
            Require(committed.Layout.TryResolveSlot(
                ids.SecondGraphStateSlotId,
                out CoCoStateSlot<CoCoGraphStateRecord<int>> secondSlot));
            Require(arena.TryPrepare(
                committed.Header.TickFrame,
                out CoCoPreparedContextCommit prepared,
                out CoCoContextCommitStatus prepareStatus));
            Assert.That(prepareStatus, Is.EqualTo(CoCoContextCommitStatus.None));
            Require(prepared.TryGetWriter(block, out CoCoContextFrameWriter writer));
            Require(writer.Write(firstSlot, first));
            Require(writer.Write(secondSlot, second));
            Require(prepared.TryFinalize(
                out CoCoFinalizedContextCommit finalized,
                out CoCoContextCommitStatus finalizeStatus));
            Assert.That(finalizeStatus, Is.EqualTo(CoCoContextCommitStatus.None));
            CoCoContextCommitResult result = finalized.Commit();
            Assert.That(result.Succeeded, Is.True, result.Status.ToString());
            source = result.Frame;
            return arena;
        }

        private static CoCoTickFrame CreateResumedTick(in CoCoTickFrame source)
        {
            Require(CoCoTimelinePosition.TryCreate(
                source.TimelinePosition.Seconds + 1d,
                out CoCoTimelinePosition position));
            Require(CoCoTickFrame.TryCreate(
                source.DeltaTime,
                source.TimelineId,
                position,
                new CoCoTimelineTick(source.Tick.Value + 1UL),
                source.ClockDomainId,
                new CoCoExecutionSequence(source.ExecutionSequence.Value + 1UL),
                new CoCoTimelineEpoch(source.TimelineEpoch.Value + 1UL),
                out CoCoTickFrame resumed,
                out CoCoDiagnostic diagnostic), diagnostic);
            return resumed;
        }

        private static void AssertInvalidActorStartup(CoCoStateGraphHost host)
        {
            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Context));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidActorBinding));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
        }

        private static void SetOperators(
            CoCoStateGraphHost host,
            params MonoBehaviour[] operators) => SetField(host, "operators", operators);

        private static void SetActorBinding(
            CoCoStateGraphHost host,
            MonoBehaviour actorBinding) =>
            SetField(host, "actorContextBinding", actorBinding);

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

        private static CoCoSerializedId128 Serialize(CoCoLayerId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoTransitionId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

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
            ICoCoStateGraphProjectBindingProvider
        {
            private readonly ContextAuthorityTestIds _ids;
            private readonly ContextAuthorityBindingMode _mode;

            public ContextAuthorityBindingProvider(
                ContextAuthorityTestIds ids,
                ContextAuthorityBindingMode mode)
            {
                _ids = ids;
                _mode = mode;
                Catalog = BuildCatalog(ids, mode);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                CoCoGraphStateRecord<int> first = ContextAuthorityDefaults.First(
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

                var factory = new CoCoStateRuntimeFactory<
                    ContextAuthorityLogic,
                    ContextAuthorityMemory>(
                    context =>
                    {
                        ContextAuthorityFactoryProbe.RecordLogicFactory();
                        return new ContextAuthorityLogic(context);
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

            private static CoCoGraphDescriptorCatalog BuildCatalog(
                ContextAuthorityTestIds ids,
                ContextAuthorityBindingMode mode)
            {
                bool includeActor = mode == ContextAuthorityBindingMode.WithActor;
                CoCoContextRestorePolicy firstRestorePolicy =
                    mode == ContextAuthorityBindingMode.GraphResetToDefault ||
                    mode == ContextAuthorityBindingMode.GraphMixedRestorePolicies
                        ? CoCoContextRestorePolicy.ResetToDefault
                        : CoCoContextRestorePolicy.Stored;
                CoCoContextRestorePolicy secondRestorePolicy =
                    mode == ContextAuthorityBindingMode.GraphResetToDefault
                        ? CoCoContextRestorePolicy.ResetToDefault
                        : CoCoContextRestorePolicy.Stored;
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Ensure(builder.TryRegisterStateBlock(
                    ids.GraphStateBlockId,
                    CoCoStateBlockOwner.Graph,
                    out CoCoDiagnostic graphBlock), graphBlock);
                Ensure(builder.TryRegisterStateSlot(
                    ids.GraphStateBlockId,
                    ids.FirstGraphStateSlotId,
                    CoCoContextProjection.Temporal,
                    firstRestorePolicy,
                    ContextAuthorityDefaults.First(ids),
                    ContextAuthorityDefaults.FirstGraphStateFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic firstSlot), firstSlot);
                Ensure(builder.TryRegisterStateSlot(
                    ids.GraphStateBlockId,
                    ids.SecondGraphStateSlotId,
                    CoCoContextProjection.Temporal,
                    secondRestorePolicy,
                    ContextAuthorityDefaults.Second(ids),
                    ContextAuthorityDefaults.SecondGraphStateFingerprint,
                    default,
                    null,
                    out CoCoDiagnostic secondSlot), secondSlot);

                CoCoStateBlockId[] contextBlocks;
                if (includeActor)
                {
                    Ensure(builder.TryRegisterStateBlock(
                        ids.ActorStateBlockId,
                        CoCoStateBlockOwner.Actor,
                        out CoCoDiagnostic actorBlock), actorBlock);
                    Ensure(builder.TryRegisterStateSlot(
                        ids.ActorStateBlockId,
                        ids.ActorStateSlotId,
                        CoCoContextProjection.Temporal,
                        CoCoContextRestorePolicy.Stored,
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
                    new CoCoStateRuntimeRegistration<
                        ContextAuthorityLogic,
                        HostTestStateConfigSchema,
                        ContextAuthorityMemory>(HostTestSchemas.State, false),
                    null,
                    null,
                    contextBlocks,
                    out CoCoDiagnostic state), state);
                Ensure(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
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

        private sealed class ContextAuthorityProbeOperator : MonoBehaviour, ICoCoOperator
        {
            private CoCoStateSlotId _graphStateSlotId;
            private CoCoOperatorDescriptor _descriptor;

            public CoCoOperatorDescriptor Descriptor => _descriptor;
            public int ExecuteCount { get; private set; }
            public bool PreviousHadCommittedFrame { get; private set; }
            public CoCoGraphStateRecord<int> PreviousState { get; private set; }

            public void Configure(
                CoCoOperatorId operatorId,
                CoCoStateSlotId graphStateSlotId)
            {
                _graphStateSlotId = graphStateSlotId;
                var builder = new CoCoOperatorDescriptorBuilder();
                if (!builder.TryFreeze<ContextAuthorityProbeOperator>(
                        operatorId,
                        out _descriptor,
                        out CoCoDiagnostic diagnostic))
                {
                    throw new InvalidOperationException(diagnostic.Message);
                }
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                ExecuteCount++;
                PreviousHadCommittedFrame = context.PreviousContext.HasCommittedFrame;
                if (!context.PreviousContext.Layout.TryResolveSlot(
                        _graphStateSlotId,
                        out CoCoStateSlot<CoCoGraphStateRecord<int>> slot))
                {
                    outcome = CoCoOperatorOutcome.Rejected(CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Operator,
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "Context authority probe could not resolve its Graph State Slot."));
                    return false;
                }

                PreviousState = context.PreviousContext.Read(slot);
                outcome = CoCoOperatorOutcome.Success;
                return true;
            }
        }

        private sealed class ContextAuthorityActorBinding :
            MonoBehaviour,
            ICoCoActorContextBinding
        {
            private CoCoStateSlotId _slotId;
            private CoCoActorContextBindingDescriptor _descriptor;

            public CoCoActorContextBindingDescriptor Descriptor => _descriptor;
            public int CaptureCount { get; private set; }
            public bool FailAfterWorldMutation { get; set; }
            public int Value { get; set; } = 41;

            public void Configure(CoCoStateSlotId slotId)
            {
                _slotId = slotId;
                var builder = new CoCoActorContextBindingDescriptorBuilder();
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
                in CoCoActorContextCaptureContext context,
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
