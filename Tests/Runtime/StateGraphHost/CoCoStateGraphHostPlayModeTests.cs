using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostPlayModeTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            HostTestLogic.Reset();
            HostTestEventAdapter.Reset();
            DualEventAdapter.Reset();
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
            HostTestEventAdapter.Reset();
            DualEventAdapter.Reset();
        }

        [Test]
        public void MissingAssetLeavesHostCreatedWithoutRouterRegistration()
        {
            CoCoStateGraphHost host = CreateHost(null, CoCoStateGraphDriver.Manual, 32);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.MissingDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void MissingProviderWithValidAssetLeavesHostCreated()
        {
            HostTestIds ids = HostTestIds.Create();
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 32);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.RegistryNotFrozen));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
        }

        [Test]
        public void IgnoredExtraFactoryBindingStillFailsSetupAtomically()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                withEvent: true,
                ignoreExtraBinding: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.IsError, Is.True);
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
        }

        [Test]
        public void MismatchedStateFactoryLeavesHostCreatedWithoutCallbacks()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                withEvent: false,
                mismatchedFactory: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
        }

        [Test]
        public void StartDefersEnterUntilAcceptedTickAndStopDoesNotForgeExit()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: false);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 32);

            Require(host.TryStart(out CoCoDiagnostic start));
            CoCoGraphInstanceId firstInstance = host.GraphInstanceId;
            Assert.That(host.ActivePaths.Count, Is.EqualTo(1));
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
            Assert.That(GetBindings(host).Inbox, Is.Null);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);

            Require(host.TryStep(0.02d, out CoCoDiagnostic step));
            Assert.That(HostTestLogic.EnterCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.UpdateCount, Is.EqualTo(1));

            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Stopped));
            Assert.That(HostTestLogic.ExitCount, Is.Zero);

            Require(host.TryStart(out CoCoDiagnostic restart));
            Assert.That(host.GraphInstanceId.IsValid, Is.True);
            Assert.That(host.GraphInstanceId, Is.Not.EqualTo(firstInstance));
            Assert.That(HostTestLogic.EnterCount, Is.EqualTo(1));
        }

        [Test]
        public void RunningDisposeUnregistersRouterWithoutExitAndIsSingleUse()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            Require(host.TryStep(0.02d, out CoCoDiagnostic step));
            CoCoGraphInstanceId disposedInstance = host.GraphInstanceId;
            CoCoEventPacket<HostTestEvent> packet = Packet(
                ids,
                disposedInstance,
                disposedInstance,
                1UL,
                10,
                CoCoEventReliability.Reliable);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.EqualTo(1));

            Require(host.TryDispose(out CoCoDiagnostic dispose));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Disposed));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(HostTestLogic.ExitCount, Is.Zero);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);

            CoCoEventBus.Publish(ref packet);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
            Assert.That(
                host.TryEnqueueLocal(packet),
                Is.EqualTo(CoCoInboxEnqueueResult.MailboxUnavailable));
            Assert.That(host.TryStart(out _), Is.False);
            Assert.That(host.TryResume(out _), Is.False);
            Assert.That(host.TryStep(0.02d, out _), Is.False);
            Assert.That(host.TryDispose(out _), Is.False);
        }

        [Test]
        public void BindingValidationRejectsMissingFactoryWithoutRuntimeOrRouter()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                withEvent: false,
                omitStateFactory: true);
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphAssetCompileResult result =
                new CoCoStateGraphAssetCompiler().Compile(asset, provider.Catalog);
            Assert.That(result.Succeeded, Is.True);

            Assert.That(
                CoCoStateGraphHostBindingValidation.TryValidate(
                    result.Graph,
                    provider,
                    4,
                    4,
                    4,
                    out CoCoDiagnostic diagnostic),
                Is.False);
            Assert.That(diagnostic.IsError, Is.True);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
        }

        [Test]
        public void SameEventTypeProjectsTwoIntentsThroughOneInboxLane()
        {
            HostTestIds ids = HostTestIds.Create();
            Require(CoCoIntentId.TryCreate(104UL, 2UL, out CoCoIntentId secondIntentId));
            var provider = new DualEventBindingProvider(ids, secondIntentId);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateDualEventAsset(ids, secondIntentId);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoStateGraphHostRuntimeBindings bindings = GetBindings(host);
            Assert.That(bindings.Inbox, Is.Not.Null);
            Assert.That(GetEventLaneCount(bindings), Is.EqualTo(1));
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.EqualTo(1));

            CoCoEventPacket<HostTestEvent> packet = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                42,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(packet), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            Require(host.TryStep(0.02d, out CoCoDiagnostic step));
            Assert.That(DualEventAdapter.FirstProjectionCount, Is.EqualTo(1));
            Assert.That(DualEventAdapter.SecondProjectionCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UpdateDriverAdvancesAtMostOncePerFrameAndContinuesNextFrame() =>
            VerifyAutomaticDriverFrameGate(CoCoStateGraphDriver.Update, "Update");

        [UnityTest]
        public IEnumerator FixedUpdateDriverAdvancesAtMostOncePerFrameAndContinuesNextFrame() =>
            VerifyAutomaticDriverFrameGate(CoCoStateGraphDriver.FixedUpdate, "FixedUpdate");

        [Test]
        public void LocalEventProjectsIntentAndReliableOverflowFaultsAtStepBoundary()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 1);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoEventPacket<HostTestEvent> first = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                41,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(first), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            Require(host.TryStep(0.02d, out CoCoDiagnostic firstStep));
            Assert.That(HostTestLogic.LastIntentValue, Is.EqualTo(41));

            Require(host.TrySuspend(out CoCoDiagnostic suspend));
            CoCoEventPacket<HostTestEvent> suspended = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                2UL,
                52,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(suspended), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            Require(host.TryResume(out CoCoDiagnostic resume));
            Require(host.TryStep(0.02d, out CoCoDiagnostic resumedStep));
            Assert.That(HostTestLogic.LastIntentValue, Is.EqualTo(52));

            CoCoEventPacket<HostTestEvent> fillsLane = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                3UL,
                63,
                CoCoEventReliability.Reliable);
            CoCoEventPacket<HostTestEvent> overflow = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                4UL,
                64,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(fillsLane), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(CoCoInboxEnqueueResult.ReliableOverflowFaultRequired));
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic fault), Is.False);
            Assert.That(fault.Code, Is.EqualTo(CoCoDiagnosticCode.MailboxOverflow));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(CoCoInboxEnqueueResult.MailboxUnavailable));
        }

        [Test]
        public void ReliableOverflowFaultsBeforeSuspendAndStopStillDiscardsTheInstance()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 1);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoEventPacket<HostTestEvent> fill = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                1,
                CoCoEventReliability.Reliable);
            CoCoEventPacket<HostTestEvent> overflow = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                2UL,
                2,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(fill), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(CoCoInboxEnqueueResult.ReliableOverflowFaultRequired));

            Assert.That(host.TrySuspend(out CoCoDiagnostic fault), Is.False);
            Assert.That(fault.Code, Is.EqualTo(CoCoDiagnosticCode.MailboxOverflow));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(CoCoInboxEnqueueResult.MailboxUnavailable));

            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Stopped));
        }

        [Test]
        public void ThrowingEventAdapterIsContainedAndRollsBackAllAuthorityUntilStop()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                withEvent: true,
                throwingEventAdapter: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoStateGraphRuntime runtime = GetRuntime(host);
            CoCoStateId committedLeaf = host.ActivePaths[0].ActiveLeaf;
            CoCoEventPacket<HostTestEvent> packet = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                10,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(packet), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Domain, Is.EqualTo(CoCoDiagnosticDomain.Intent));
            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.CommitPreparationFailed));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(runtime.Clock.Seconds, Is.Zero);
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(committedLeaf));
            Assert.That(GetCommittedMemoryValue(runtime), Is.Zero);
            Assert.That(GetCommittedContext(host), Is.EqualTo(default(CoCoContextFrame)));

            Assert.That(host.TryStep(0.02d, out _), Is.False);
            Assert.That(host.TryResume(out _), Is.False);
            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Stopped));
        }

        [Test]
        public void TargetedRouterDeliveryStopsAfterTargetHostStops()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost source = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            CoCoStateGraphHost target = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(source.TryStart(out CoCoDiagnostic sourceStart));
            Require(target.TryStart(out CoCoDiagnostic targetStart));
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.EqualTo(1));

            CoCoEventPacket<HostTestEvent> packet = Packet(
                ids,
                source.GraphInstanceId,
                target.GraphInstanceId,
                1UL,
                77,
                CoCoEventReliability.Reliable);
            CoCoEventBus.Publish(ref packet);
            Require(target.TryStep(0.02d, out CoCoDiagnostic targetStep));
            Require(source.TryStep(0.02d, out CoCoDiagnostic sourceStep));
            Assert.That(HostTestLogic.GetLastIntent(target.GraphInstanceId), Is.EqualTo(77));
            Assert.That(HostTestLogic.GetLastIntent(source.GraphInstanceId), Is.Zero);

            Require(target.TryStop(out CoCoDiagnostic targetStop));
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.EqualTo(1));
            Require(source.TryStop(out CoCoDiagnostic sourceStop));
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);

            CoCoEventBus.Publish(ref packet);
            Assert.That(CoCoStateGraphEventRouterRegistry.Count, Is.Zero);
        }

        [Test]
        public void CancelRetriesSameFrozenIntentTickWithoutResamplingOrLosingLaterEvent()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoEventPacket<HostTestEvent> first = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                10,
                CoCoEventReliability.Reliable);
            CoCoEventPacket<HostTestEvent> arrivedAfterSeal = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                2UL,
                20,
                CoCoEventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(first), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            var cancelThenAccept = new CancelThenAcceptCoordinator(
                () => Assert.That(
                    host.TryEnqueueLocal(arrivedAfterSeal),
                    Is.EqualTo(CoCoInboxEnqueueResult.Accepted)));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                cancelThenAccept,
                out CoCoDiagnostic coordinator));

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic cancelled), Is.False);
            Assert.That(cancelled.IsError, Is.False);
            Assert.That(host.Fault.IsFaulted, Is.False);
            Assert.That(HostTestEventAdapter.ProjectionCount, Is.EqualTo(1));

            Require(host.TryStep(0.02d, out CoCoDiagnostic retried));
            Assert.That(cancelThenAccept.FirstTick, Is.EqualTo(cancelThenAccept.SecondTick));
            Assert.That(HostTestEventAdapter.ProjectionCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.GetLastIntent(host.GraphInstanceId), Is.EqualTo(10));

            Require(host.TryStep(0.02d, out CoCoDiagnostic nextTick));
            Assert.That(HostTestEventAdapter.ProjectionCount, Is.EqualTo(2));
            Assert.That(HostTestLogic.GetLastIntent(host.GraphInstanceId), Is.EqualTo(20));
        }

        [Test]
        public void CoordinatorFailureRollsBackAllAuthorityAndLatchesFaultUntilStop()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: false);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new FailingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoStateGraphRuntime runtime = GetRuntime(host);
            CoCoStateId committedLeaf = host.ActivePaths[0].ActiveLeaf;
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(GetCommittedMemoryValue(runtime), Is.Zero);
            Assert.That(GetCommittedContext(host), Is.EqualTo(default(CoCoContextFrame)));

            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failed), Is.False);
            Assert.That(failed.Code, Is.EqualTo(CoCoDiagnosticCode.CommitPreparationFailed));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(HostTestLogic.UpdateCount, Is.EqualTo(1));
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(runtime.Clock.Seconds, Is.Zero);
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(committedLeaf));
            Assert.That(GetCommittedMemoryValue(runtime), Is.Zero);
            Assert.That(GetCommittedContext(host), Is.EqualTo(default(CoCoContextFrame)));

            Assert.That(host.TryStep(0.02d, out _), Is.False);
            Assert.That(host.TryResume(out _), Is.False);
            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Stopped));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CoordinatorFailureDoesNotConsumeDiscreteOperationSequence(bool throws)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                withEvent: false,
                withDiscreteOperation: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));

            CoCoOperationSectionHandle<IHostTestDiscreteSection> handle =
                provider.OperationFactory.Handle;
            Assert.That(handle.IsValid, Is.True);
            CoCoOperationFrame operationFrame = GetBindings(host).Operations;
            Assert.That(GetCommittedSequence(operationFrame, handle.DenseIndex), Is.Zero);
            var coordinator = new ObservingFailureCoordinator(handle, throws);
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                coordinator,
                out CoCoDiagnostic coordinatorInstall));

            CoCoStateGraphRuntime runtime = GetRuntime(host);
            CoCoStateId committedLeaf = host.ActivePaths[0].ActiveLeaf;
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);

            Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.CommitPreparationFailed));
            Assert.That(coordinator.SawCandidate, Is.True);
            Assert.That(coordinator.CandidateEnabled, Is.True);
            Assert.That(coordinator.CandidateValue, Is.EqualTo(1));
            Assert.That(coordinator.CandidateActivationId.IsValid, Is.True);
            Assert.That(coordinator.CandidateSequence, Is.EqualTo(1UL));
            Assert.That(GetCommittedSequence(operationFrame, handle.DenseIndex), Is.Zero);
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(runtime.Clock.Seconds, Is.Zero);
            Assert.That(host.ActivePaths[0].ActiveLeaf, Is.EqualTo(committedLeaf));
            Assert.That(GetCommittedMemoryValue(runtime), Is.Zero);
            Assert.That(GetCommittedContext(host), Is.EqualTo(default(CoCoContextFrame)));
        }

        [Test]
        public void DeclaredBroadcastReachesEveryRegisteredHostInTheDomain()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost first = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            CoCoStateGraphHost second = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(first.TryStart(out CoCoDiagnostic firstStart));
            Require(second.TryStart(out CoCoDiagnostic secondStart));
            Require(CoCoGraphInstanceId.TryCreate(999UL, out CoCoGraphInstanceId externalSource));

            CoCoEventPacket<HostTestEvent> packet = BroadcastPacket(
                ids,
                externalSource,
                1UL,
                88);
            CoCoEventBus.Publish(ref packet);
            Require(first.TryStep(0.02d, out CoCoDiagnostic firstStep));
            Require(second.TryStep(0.02d, out CoCoDiagnostic secondStep));
            Assert.That(HostTestLogic.GetLastIntent(first.GraphInstanceId), Is.EqualTo(88));
            Assert.That(HostTestLogic.GetLastIntent(second.GraphInstanceId), Is.EqualTo(88));
        }

        [Test]
        public void HostsSharingAssetKeepClockMemoryInboxAndFaultIsolated()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost healthy = CreateHost(asset, CoCoStateGraphDriver.Manual, 1);
            CoCoStateGraphHost faulted = CreateHost(asset, CoCoStateGraphDriver.Manual, 1);
            Require(healthy.TryStart(out CoCoDiagnostic healthyStart));
            Require(faulted.TryStart(out CoCoDiagnostic faultedStart));

            Require(healthy.TryStep(0.02d, out CoCoDiagnostic healthyStep));
            CoCoStateGraphRuntime healthyRuntime = GetRuntime(healthy);
            CoCoStateGraphRuntime faultedRuntime = GetRuntime(faulted);
            Assert.That(healthyRuntime.Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(faultedRuntime.Clock.Tick.Value, Is.Zero);
            Assert.That(HostTestLogic.GetMemoryValue(healthy.GraphInstanceId), Is.EqualTo(1));
            Assert.That(HostTestLogic.GetMemoryValue(faulted.GraphInstanceId), Is.Zero);

            CoCoEventPacket<HostTestEvent> fill = Packet(
                ids,
                faulted.GraphInstanceId,
                faulted.GraphInstanceId,
                1UL,
                1,
                CoCoEventReliability.Reliable);
            CoCoEventPacket<HostTestEvent> overflow = Packet(
                ids,
                faulted.GraphInstanceId,
                faulted.GraphInstanceId,
                2UL,
                2,
                CoCoEventReliability.Reliable);
            Assert.That(faulted.TryEnqueueLocal(fill), Is.EqualTo(CoCoInboxEnqueueResult.Accepted));
            Assert.That(
                faulted.TryEnqueueLocal(overflow),
                Is.EqualTo(CoCoInboxEnqueueResult.ReliableOverflowFaultRequired));
            Assert.That(faulted.TryStep(0.02d, out _), Is.False);
            Assert.That(faulted.Fault.IsFaulted, Is.True);
            Assert.That(healthy.Fault.IsFaulted, Is.False);
            Require(healthy.TryStep(0.02d, out CoCoDiagnostic secondHealthyStep));
            Assert.That(healthyRuntime.Clock.Tick.Value, Is.EqualTo(2UL));
            Assert.That(faultedRuntime.Clock.Tick.Value, Is.Zero);
        }

        [Test]
        public void RoutedStepAndSuspendResumeHaveZeroSteadyStateManagedAllocation()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: true);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: true);
            CoCoStateGraphHost host = CreateHost(asset, CoCoStateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            Require(CoCoGraphInstanceId.TryCreate(999UL, out CoCoGraphInstanceId source));

            ulong sequence = 1UL;
            for (int index = 0; index < 100; index++)
            {
                Assert.That(TryRunRoutedTick(host, ids, source, sequence++), Is.True);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            bool failed = false;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                failed |= !TryRunRoutedTick(host, ids, source, sequence++);
            }

            long routedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(failed, Is.False);
            Assert.That(routedBytes, Is.Zero);

            for (int index = 0; index < 100; index++)
            {
                Assert.That(host.TrySuspend(out _), Is.True);
                Assert.That(host.TryResume(out _), Is.True);
            }

            before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                failed |= !host.TrySuspend(out _);
                failed |= !host.TryResume(out _);
            }

            long lifecycleBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(failed, Is.False);
            Assert.That(lifecycleBytes, Is.Zero);
        }

        private CoCoStateGraphHost CreateHost(
            CoCoStateGraphAsset asset,
            CoCoStateGraphDriver selectedDriver,
            int laneCapacity)
        {
            var gameObject = new GameObject("StateGraphHost Test");
            _objects.Add(gameObject);
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", selectedDriver);
            SetField(host, "autoStart", false);
            SetField(host, "eventLaneCapacity", laneCapacity);
            return host;
        }

        private CoCoStateGraphAsset CreateAsset(HostTestIds ids, bool withEvent)
        {
            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(ids.StateId.High, ids.StateId.Low),
                default,
                "Leaf",
                new CoCoSerializedId128(ids.StateDescriptorId.High, ids.StateDescriptorId.Low),
                new HostTestStateConfig { Value = 5 });
            var layer = new CoCoStateGraphLayerRecord(
                new CoCoSerializedId128(ids.LayerId.High, ids.LayerId.Low),
                "Base");
            layer.InitialStateId = new CoCoSerializedId128(ids.StateId.High, ids.StateId.Low);
            layer.States.Add(state);
            asset.Layers.Add(layer);
            if (withEvent)
            {
                asset.EventAdapterDeclarations.Add(
                    new CoCoStateGraphEventAdapterDeclarationRecord(
                        new CoCoSerializedId128(ids.EventTypeId.High, ids.EventTypeId.Low),
                        new CoCoSerializedId128(ids.IntentId.High, ids.IntentId.Low)));
            }

            return asset;
        }

        private CoCoStateGraphAsset CreateDualEventAsset(
            HostTestIds ids,
            CoCoIntentId secondIntentId)
        {
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            asset.EventAdapterDeclarations.Add(
                new CoCoStateGraphEventAdapterDeclarationRecord(
                    new CoCoSerializedId128(ids.EventTypeId.High, ids.EventTypeId.Low),
                    new CoCoSerializedId128(ids.IntentId.High, ids.IntentId.Low)));
            asset.EventAdapterDeclarations.Add(
                new CoCoStateGraphEventAdapterDeclarationRecord(
                    new CoCoSerializedId128(ids.EventTypeId.High, ids.EventTypeId.Low),
                    new CoCoSerializedId128(secondIntentId.High, secondIntentId.Low)));
            return asset;
        }

        private IEnumerator VerifyAutomaticDriverFrameGate(
            CoCoStateGraphDriver selectedDriver,
            string callbackName)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids, withEvent: false);
            Require(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            Require(CoCoStateGraphTransactionCoordinatorRegistry.TryInstall(
                new AcceptingCoordinator(),
                out CoCoDiagnostic coordinator));
            CoCoStateGraphAsset asset = CreateAsset(ids, withEvent: false);
            CoCoStateGraphHost host = CreateHost(asset, selectedDriver, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            host.enabled = false;
            CoCoStateGraphRuntime runtime = GetRuntime(host);

            int firstFrame = Time.frameCount;
            InvokePrivateCallback(host, callbackName);
            InvokePrivateCallback(host, callbackName);
            Assert.That(Time.frameCount, Is.EqualTo(firstFrame));
            Assert.That(runtime.Clock.Tick.Value, Is.EqualTo(1UL));

            yield return null;

            Assert.That(Time.frameCount, Is.GreaterThan(firstFrame));
            InvokePrivateCallback(host, callbackName);
            InvokePrivateCallback(host, callbackName);
            Assert.That(runtime.Clock.Tick.Value, Is.EqualTo(2UL));
        }

        private static CoCoEventPacket<HostTestEvent> Packet(
            HostTestIds ids,
            CoCoGraphInstanceId source,
            CoCoGraphInstanceId target,
            ulong sequence,
            int value,
            CoCoEventReliability reliability)
        {
            Require(CoCoEventSequence.TryCreate(sequence, out CoCoEventSequence eventSequence));
            Require(CoCoActorEventEnvelope.TryCreate(
                ids.EventTypeId,
                ids.EventDomainId,
                source,
                target,
                new CoCoTimelineEpoch(0UL),
                new CoCoTimelineTick(0UL),
                eventSequence,
                CoCoEventDeliveryMode.Targeted,
                reliability,
                default,
                default,
                default,
                out CoCoActorEventEnvelope envelope));
            var payload = new HostTestEvent { Value = value };
            Require(CoCoEventPacket<HostTestEvent>.TryCreate(envelope, payload, out CoCoEventPacket<HostTestEvent> packet));
            return packet;
        }

        private static CoCoEventPacket<HostTestEvent> BroadcastPacket(
            HostTestIds ids,
            CoCoGraphInstanceId source,
            ulong sequence,
            int value)
        {
            Require(CoCoEventSequence.TryCreate(sequence, out CoCoEventSequence eventSequence));
            Require(CoCoActorEventEnvelope.TryCreate(
                ids.EventTypeId,
                ids.EventDomainId,
                source,
                default,
                new CoCoTimelineEpoch(0UL),
                new CoCoTimelineTick(0UL),
                eventSequence,
                CoCoEventDeliveryMode.DeclaredBroadcast,
                CoCoEventReliability.Reliable,
                default,
                default,
                default,
                out CoCoActorEventEnvelope envelope));
            var payload = new HostTestEvent { Value = value };
            Require(CoCoEventPacket<HostTestEvent>.TryCreate(
                envelope,
                payload,
                out CoCoEventPacket<HostTestEvent> packet));
            return packet;
        }

        private static bool TryRunRoutedTick(
            CoCoStateGraphHost host,
            HostTestIds ids,
            CoCoGraphInstanceId source,
            ulong sequence)
        {
            if (!CoCoEventSequence.TryCreate(sequence, out CoCoEventSequence eventSequence) ||
                !CoCoActorEventEnvelope.TryCreate(
                    ids.EventTypeId,
                    ids.EventDomainId,
                    source,
                    host.GraphInstanceId,
                    new CoCoTimelineEpoch(0UL),
                    new CoCoTimelineTick(0UL),
                    eventSequence,
                    CoCoEventDeliveryMode.Targeted,
                    CoCoEventReliability.Reliable,
                    default,
                    default,
                    default,
                    out CoCoActorEventEnvelope envelope))
            {
                return false;
            }

            var payload = new HostTestEvent { Value = (int)sequence };
            if (!CoCoEventPacket<HostTestEvent>.TryCreate(
                    envelope,
                    payload,
                    out CoCoEventPacket<HostTestEvent> packet))
            {
                return false;
            }

            CoCoEventBus.Publish(ref packet);
            return host.TryStep(0.001d, out _);
        }

        private static CoCoStateGraphRuntime GetRuntime(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphRuntime)field.GetValue(host);
        }

        private static CoCoStateGraphHostRuntimeBindings GetBindings(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoStateGraphHostRuntimeBindings)field.GetValue(host);
        }

        private static int GetEventLaneCount(CoCoStateGraphHostRuntimeBindings bindings)
        {
            FieldInfo field = typeof(CoCoStateGraphHostRuntimeBindings).GetField(
                "_eventLanes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return ((Array)field.GetValue(bindings)).Length;
        }

        private static ulong GetCommittedSequence(
            CoCoOperationFrame operationFrame,
            int denseIndex)
        {
            FieldInfo field = typeof(CoCoOperationFrame).GetField(
                "_committedSequences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var sequences = (ulong[])field.GetValue(operationFrame);
            Assert.That(denseIndex, Is.InRange(0, sequences.Length - 1));
            return sequences[denseIndex];
        }

        private static void InvokePrivateCallback(CoCoStateGraphHost host, string callbackName)
        {
            MethodInfo callback = typeof(CoCoStateGraphHost).GetMethod(
                callbackName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(callback, Is.Not.Null, callbackName);
            callback.Invoke(host, null);
        }

        private static int GetCommittedMemoryValue(CoCoStateGraphRuntime runtime)
        {
            FieldInfo layersField = typeof(CoCoStateGraphRuntime).GetField(
                "_layers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(layersField, Is.Not.Null);
            var layers = (Array)layersField.GetValue(runtime);
            object layer = layers.GetValue(0);
            PropertyInfo statesProperty = layer.GetType().GetProperty(
                "States",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(statesProperty, Is.Not.Null);
            var states = (Array)statesProperty.GetValue(layer);
            object state = states.GetValue(0);
            PropertyInfo memoryProperty = state.GetType().GetProperty(
                "CommittedMemory",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(memoryProperty, Is.Not.Null);
            return ((HostTestMemory)memoryProperty.GetValue(state)).Value;
        }

        private static CoCoContextFrame GetCommittedContext(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_committedContext",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (CoCoContextFrame)field.GetValue(host);
        }

        private static void SetField<TValue>(CoCoStateGraphHost host, string fieldName, TValue value)
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

        private sealed class AcceptingCoordinator : ICoCoStateGraphTransactionCoordinator
        {
            public bool TryFinalize(
                CoCoStateGraphHost host,
                in CoCoStagedGraphStep stagedStep,
                in CoCoContextFrame previousContext,
                out CoCoStateGraphTransactionDecision decision,
                out CoCoContextFrame committedContext,
                out CoCoDiagnostic diagnostic)
            {
                decision = CoCoStateGraphTransactionDecision.Accept;
                committedContext = previousContext;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class CancelThenAcceptCoordinator : ICoCoStateGraphTransactionCoordinator
        {
            private readonly Action _onFirstFinalize;
            private int _calls;

            public CancelThenAcceptCoordinator(Action onFirstFinalize)
            {
                _onFirstFinalize = onFirstFinalize;
            }

            public CoCoTimelineTick FirstTick { get; private set; }
            public CoCoTimelineTick SecondTick { get; private set; }

            public bool TryFinalize(
                CoCoStateGraphHost host,
                in CoCoStagedGraphStep stagedStep,
                in CoCoContextFrame previousContext,
                out CoCoStateGraphTransactionDecision decision,
                out CoCoContextFrame committedContext,
                out CoCoDiagnostic diagnostic)
            {
                _calls++;
                committedContext = previousContext;
                diagnostic = CoCoDiagnostic.None;
                if (_calls == 1)
                {
                    FirstTick = stagedStep.TickFrame.Tick;
                    _onFirstFinalize?.Invoke();
                    decision = CoCoStateGraphTransactionDecision.Cancel;
                    return true;
                }

                if (_calls == 2)
                {
                    SecondTick = stagedStep.TickFrame.Tick;
                }

                decision = CoCoStateGraphTransactionDecision.Accept;
                return true;
            }
        }

        private sealed class FailingCoordinator : ICoCoStateGraphTransactionCoordinator
        {
            public bool TryFinalize(
                CoCoStateGraphHost host,
                in CoCoStagedGraphStep stagedStep,
                in CoCoContextFrame previousContext,
                out CoCoStateGraphTransactionDecision decision,
                out CoCoContextFrame committedContext,
                out CoCoDiagnostic diagnostic)
            {
                decision = CoCoStateGraphTransactionDecision.RejectAndFault;
                committedContext = previousContext;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operation,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Test transaction finalization failed.");
                return false;
            }
        }

        private sealed class ObservingFailureCoordinator :
            ICoCoStateGraphTransactionCoordinator
        {
            private readonly CoCoOperationSectionHandle<IHostTestDiscreteSection> _handle;
            private readonly bool _throws;

            public ObservingFailureCoordinator(
                CoCoOperationSectionHandle<IHostTestDiscreteSection> handle,
                bool throws)
            {
                _handle = handle;
                _throws = throws;
            }

            public bool SawCandidate { get; private set; }
            public bool CandidateEnabled { get; private set; }
            public int CandidateValue { get; private set; }
            public CoCoActivationId CandidateActivationId { get; private set; }
            public ulong CandidateSequence { get; private set; }

            public bool TryFinalize(
                CoCoStateGraphHost host,
                in CoCoStagedGraphStep stagedStep,
                in CoCoContextFrame previousContext,
                out CoCoStateGraphTransactionDecision decision,
                out CoCoContextFrame committedContext,
                out CoCoDiagnostic diagnostic)
            {
                SawCandidate = stagedStep.OperationFrame.TryGet(
                    _handle,
                    out CoCoOperationSectionEntry<IHostTestDiscreteSection> entry);
                if (SawCandidate)
                {
                    CandidateEnabled = entry.Header.Enabled;
                    CandidateValue = entry.View.Value;
                    CandidateActivationId = entry.Header.ActivationId;
                    CandidateSequence = entry.Header.OperationSequence.Value;
                }

                if (_throws)
                {
                    throw new InvalidOperationException(
                        "Test coordinator threw after observing the Discrete candidate.");
                }

                decision = CoCoStateGraphTransactionDecision.RejectAndFault;
                committedContext = previousContext;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operation,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Test coordinator rejected the Discrete candidate.");
                return false;
            }
        }

        private sealed class ThrowingEventAdapter :
            ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
        {
            public bool TryProject(
                in CoCoEventPacket<HostTestEvent> packet,
                out HostTestIntent intent)
            {
                intent = default;
                throw new InvalidOperationException("Test Event Adapter failure.");
            }
        }

        private sealed class DualEventAdapter :
            ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
        {
            private readonly bool _isFirst;

            public DualEventAdapter(bool isFirst)
            {
                _isFirst = isFirst;
            }

            public static int FirstProjectionCount { get; private set; }
            public static int SecondProjectionCount { get; private set; }

            public static void Reset()
            {
                FirstProjectionCount = 0;
                SecondProjectionCount = 0;
            }

            public bool TryProject(
                in CoCoEventPacket<HostTestEvent> packet,
                out HostTestIntent intent)
            {
                if (_isFirst)
                {
                    FirstProjectionCount++;
                }
                else
                {
                    SecondProjectionCount++;
                }

                intent = new HostTestIntent { Value = packet.Payload.Value };
                return true;
            }
        }

        private sealed class DualEventBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private const ulong ReducerFingerprint = 991UL;
            private readonly HostTestIds _ids;
            private readonly CoCoIntentId _secondIntentId;

            public DualEventBindingProvider(HostTestIds ids, CoCoIntentId secondIntentId)
            {
                _ids = ids;
                _secondIntentId = secondIntentId;
                Catalog = BuildCatalog(ids, secondIntentId);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                if (!builder.TryRegisterIntent<
                        HostTestIntent,
                        HostTestIntentReducer,
                        HostTestIntentReducerFactory>(
                        _ids.IntentId,
                        new HostTestIntentReducerFactory(),
                        ReducerFingerprint,
                        out CoCoIntentHandle<HostTestIntent> firstIntent,
                        out diagnostic) ||
                    !builder.TryRegisterIntent<
                        HostTestIntent,
                        HostTestIntentReducer,
                        HostTestIntentReducerFactory>(
                        _secondIntentId,
                        new HostTestIntentReducerFactory(),
                        ReducerFingerprint,
                        out CoCoIntentHandle<HostTestIntent> secondIntent,
                        out diagnostic) ||
                    !builder.TryBeginIntentBindings(2, out diagnostic) ||
                    !CoCoIntentSourceRequirement<HostTestIntent>.TryCreate(
                        firstIntent,
                        1,
                        out CoCoIntentSourceRequirement<HostTestIntent> firstRequirement) ||
                    !CoCoIntentSourceRequirement<HostTestIntent>.TryCreate(
                        secondIntent,
                        1,
                        out CoCoIntentSourceRequirement<HostTestIntent> secondRequirement) ||
                    !builder.TryBindEventAdapter<HostTestEvent, HostTestIntent>(
                        _ids.EventDomainId,
                        _ids.EventTypeId,
                        firstRequirement,
                        4,
                        false,
                        new DualEventAdapter(true),
                        out diagnostic) ||
                    !builder.TryBindEventAdapter<HostTestEvent, HostTestIntent>(
                        _ids.EventDomainId,
                        _ids.EventTypeId,
                        secondRequirement,
                        4,
                        false,
                        new DualEventAdapter(false),
                        out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Registry,
                            CoCoDiagnosticCode.InvalidIntentDescriptor,
                            "Dual Event Adapter binding failed.");
                    }

                    return false;
                }

                var stateFactory = new CoCoStateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context => new HostTestLogic(context.GraphInstanceId, firstIntent),
                    () => new HostTestMemory(),
                    (source, destination) => destination.Value = source.Value,
                    memory => memory.Value = 0,
                    memory => unchecked((ulong)(uint)memory.Value));
                return builder.TryBindState(
                    _ids.StateDescriptorId,
                    stateFactory,
                    out diagnostic);
            }

            private static CoCoGraphDescriptorCatalog BuildCatalog(
                HostTestIds ids,
                CoCoIntentId secondIntentId)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                var reducerToken = new CoCoIntentReducerFactoryToken<
                    HostTestIntent,
                    HostTestIntentReducer,
                    HostTestIntentReducerFactory>(ReducerFingerprint);
                Require(builder.TryRegisterIntent(
                    ids.IntentId,
                    4,
                    reducerToken,
                    out CoCoDiagnostic firstIntent));
                Require(builder.TryRegisterIntent(
                    secondIntentId,
                    4,
                    reducerToken,
                    out CoCoDiagnostic secondIntent));
                Require(builder.TryRegisterEventToIntentDeclaration<HostTestEvent, HostTestIntent>(
                    ids.EventDomainId,
                    ids.EventTypeId,
                    ids.IntentId,
                    out CoCoDiagnostic firstEvent));
                Require(builder.TryRegisterEventToIntentDeclaration<HostTestEvent, HostTestIntent>(
                    ids.EventDomainId,
                    ids.EventTypeId,
                    secondIntentId,
                    out CoCoDiagnostic secondEvent));
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    new[] { ids.IntentId, secondIntentId },
                    null,
                    null,
                    out CoCoDiagnostic state));
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze));
                return catalog;
            }
        }

        private sealed class HostTestBindingProvider : ICoCoStateGraphProjectBindingProvider
        {
            private const ulong ReducerFingerprint = 991UL;
            private const ulong OperationFingerprint = 992UL;
            private readonly HostTestIds _ids;
            private readonly bool _withEvent;
            private readonly bool _ignoreExtraBinding;
            private readonly bool _mismatchedFactory;
            private readonly bool _throwingEventAdapter;
            private readonly bool _omitStateFactory;
            private readonly bool _withDiscreteOperation;

            public HostTestBindingProvider(
                HostTestIds ids,
                bool withEvent,
                bool ignoreExtraBinding = false,
                bool mismatchedFactory = false,
                bool throwingEventAdapter = false,
                bool omitStateFactory = false,
                bool withDiscreteOperation = false)
            {
                _ids = ids;
                _withEvent = withEvent;
                _ignoreExtraBinding = ignoreExtraBinding;
                _mismatchedFactory = mismatchedFactory;
                _throwingEventAdapter = throwingEventAdapter;
                _omitStateFactory = omitStateFactory;
                _withDiscreteOperation = withDiscreteOperation;
                OperationFactory = withDiscreteOperation
                    ? new HostTestDiscreteSectionViewFactory()
                    : null;
                Catalog = BuildCatalog(ids, withEvent, withDiscreteOperation);
            }

            public CoCoGraphDescriptorCatalog Catalog { get; }
            public HostTestDiscreteSectionViewFactory OperationFactory { get; }

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                CoCoIntentHandle<HostTestIntent> intent = default;
                if (_withEvent)
                {
                    if (!builder.TryRegisterIntent<
                            HostTestIntent,
                            HostTestIntentReducer,
                            HostTestIntentReducerFactory>(
                            _ids.IntentId,
                            new HostTestIntentReducerFactory(),
                            ReducerFingerprint,
                            out intent,
                            out diagnostic) ||
                        !builder.TryBeginIntentBindings(1, out diagnostic) ||
                        !CoCoIntentSourceRequirement<HostTestIntent>.TryCreate(
                            intent,
                            1,
                            out CoCoIntentSourceRequirement<HostTestIntent> requirement) ||
                        !builder.TryBindEventAdapter<HostTestEvent, HostTestIntent>(
                            _ids.EventDomainId,
                            _ids.EventTypeId,
                            requirement,
                            4,
                            false,
                            _throwingEventAdapter
                                ? new ThrowingEventAdapter()
                                : new HostTestEventAdapter(),
                            out diagnostic))
                    {
                        if (diagnostic.IsNone)
                        {
                            diagnostic = CoCoDiagnostic.Error(
                                CoCoDiagnosticDomain.Registry,
                                CoCoDiagnosticCode.InvalidIntentDescriptor,
                                "Host test Intent binding failed.");
                        }

                        return false;
                    }
                }

                if (_withDiscreteOperation &&
                    !builder.TryRegisterOperation(
                        _ids.OperationSectionId,
                        CoCoOperationSectionMode.Discrete,
                        OperationFactory,
                        OperationFingerprint,
                        out CoCoOperationSectionRequirement operationRequirement,
                        out diagnostic))
                {
                    return false;
                }

                if (_omitStateFactory)
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                if (_mismatchedFactory)
                {
                    var mismatched = new CoCoStateRuntimeFactory<
                        HostTestMismatchedLogic,
                        HostTestMemory>(
                        _ => new HostTestMismatchedLogic(),
                        () => new HostTestMemory(),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        memory => unchecked((ulong)(uint)memory.Value));
                    return builder.TryBindState(
                        _ids.StateDescriptorId,
                        mismatched,
                        out diagnostic);
                }

                var stateFactory = new CoCoStateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context => new HostTestLogic(
                        context.GraphInstanceId,
                        _withEvent ? intent : default,
                        OperationFactory?.Handle ?? default,
                        OperationFactory?.ValueField ?? default),
                    () => new HostTestMemory(),
                    (source, destination) => destination.Value = source.Value,
                    memory => memory.Value = 0,
                    memory => unchecked((ulong)(uint)memory.Value));
                if (!builder.TryBindState(
                    _ids.StateDescriptorId,
                    stateFactory,
                    out diagnostic))
                {
                    return false;
                }

                if (_ignoreExtraBinding)
                {
                    CoCoStateDescriptorId.TryCreate(
                        987UL,
                        654UL,
                        out CoCoStateDescriptorId undeclared);
                    builder.TryBindState(undeclared, stateFactory, out _);
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            private static CoCoGraphDescriptorCatalog BuildCatalog(
                HostTestIds ids,
                bool withEvent,
                bool withDiscreteOperation)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                CoCoIntentId[] intents = null;
                CoCoOperationSectionId[] operations = null;
                if (withEvent)
                {
                    Require(builder.TryRegisterIntent(
                        ids.IntentId,
                        4,
                        new CoCoIntentReducerFactoryToken<
                            HostTestIntent,
                            HostTestIntentReducer,
                            HostTestIntentReducerFactory>(ReducerFingerprint),
                        out CoCoDiagnostic intent));
                    Require(builder.TryRegisterEventToIntentDeclaration<HostTestEvent, HostTestIntent>(
                        ids.EventDomainId,
                        ids.EventTypeId,
                        ids.IntentId,
                        out CoCoDiagnostic eventDeclaration));
                    intents = new[] { ids.IntentId };
                }

                if (withDiscreteOperation)
                {
                    Require(builder.TryRegisterOperationSection(
                        ids.OperationSectionId,
                        CoCoOperationSectionMode.Discrete,
                        new CoCoOperationSectionViewFactoryToken<
                            IHostTestDiscreteSection,
                            HostTestDiscreteSectionViewFactory>(OperationFingerprint),
                        out CoCoDiagnostic operation));
                    operations = new[] { ids.OperationSectionId };
                }

                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    intents,
                    operations,
                    null,
                    out CoCoDiagnostic state));
                Require(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze));
                return catalog;
            }
        }

        private readonly struct HostTestIds
        {
            private HostTestIds(
                CoCoLayerId layerId,
                CoCoStateId stateId,
                CoCoStateDescriptorId stateDescriptorId,
                CoCoIntentId intentId,
                CoCoEventTypeId eventTypeId,
                CoCoEventDomainId eventDomainId,
                CoCoOperationSectionId operationSectionId)
            {
                LayerId = layerId;
                StateId = stateId;
                StateDescriptorId = stateDescriptorId;
                IntentId = intentId;
                EventTypeId = eventTypeId;
                EventDomainId = eventDomainId;
                OperationSectionId = operationSectionId;
            }

            public CoCoLayerId LayerId { get; }
            public CoCoStateId StateId { get; }
            public CoCoStateDescriptorId StateDescriptorId { get; }
            public CoCoIntentId IntentId { get; }
            public CoCoEventTypeId EventTypeId { get; }
            public CoCoEventDomainId EventDomainId { get; }
            public CoCoOperationSectionId OperationSectionId { get; }

            public static HostTestIds Create()
            {
                Require(CoCoLayerId.TryCreate(101UL, 1UL, out CoCoLayerId layerId));
                Require(CoCoStateId.TryCreate(102UL, 1UL, out CoCoStateId stateId));
                Require(CoCoStateDescriptorId.TryCreate(
                    103UL,
                    1UL,
                    out CoCoStateDescriptorId descriptorId));
                Require(CoCoIntentId.TryCreate(104UL, 1UL, out CoCoIntentId intentId));
                Require(CoCoEventTypeId.TryCreate(105UL, 1UL, out CoCoEventTypeId eventTypeId));
                Require(CoCoEventDomainId.TryCreate(106UL, out CoCoEventDomainId domainId));
                Require(CoCoOperationSectionId.TryCreate(
                    107UL,
                    1UL,
                    out CoCoOperationSectionId operationSectionId));
                return new HostTestIds(
                    layerId,
                    stateId,
                    descriptorId,
                    intentId,
                    eventTypeId,
                    domainId,
                    operationSectionId);
            }
        }
    }
}
