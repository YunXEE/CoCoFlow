using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    public sealed class StateGraphHostPlayModeTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        private readonly HashSet<StateGraphAsset> _eventGraphs = new HashSet<StateGraphAsset>();

        [SetUp]
        public void SetUp()
        {
            HostProjectBindings.ResetForTests();
            HostTestLogic.Reset();
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
            _eventGraphs.Clear();
            HostProjectBindings.ResetForTests();
            HostTestLogic.Reset();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StartupExceptionPreservesCauseAndAllowsCleanRetry(bool duringCompilation)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new StartupExceptionBindingProvider(
                new HostTestBindingProvider(ids, withIntentSource: true), duringCompilation);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            StateGraphHost host = CreateHost(CreateAsset(ids, withEvent: true), StateGraphDriver.Manual, 4);
            var source = host.gameObject.AddComponent<ThrowingHostTestIntentSource>();
            SetField(host, "intentSources", new MonoBehaviour[] { source });
            provider.Fail = true;
            string exceptionLog = null;
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Exception) exceptionLog = message + "\n" + stack;
            };
            ILogHandler originalLogHandler = Debug.unityLogger.logHandler;
            var logHandler = new StartupExceptionLogHandler(originalLogHandler);
            Debug.unityLogger.logHandler = logHandler;
            Application.logMessageReceived += capture;
            try
            {
                LogAssert.Expect(LogType.Exception,
                    new System.Text.RegularExpressions.Regex("ArgumentException: BUG-115 root"));
                Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.CommitPreparationFailed));
                Assert.That(diagnostic.Message, Is.EqualTo(duringCompilation
                    ? "StateGraph Asset compilation threw during Host setup."
                    : "Host setup failed before the StateGraph instance became observable."));
                Assert.That(host.LastDiagnostic, Is.EqualTo(diagnostic));
            }
            finally
            {
                Application.logMessageReceived -= capture;
                Debug.unityLogger.logHandler = originalLogHandler;
            }

            Assert.That(logHandler.LoggedException, Is.SameAs(provider.StartupException));
            Assert.That(logHandler.LoggedException, Is.TypeOf<InvalidOperationException>());
            Assert.That(logHandler.LoggedException.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(logHandler.LoggedContext, Is.SameAs(host));
            Assert.That(logHandler.ExceptionCount, Is.EqualTo(1));
            StringAssert.Contains("ArgumentException: BUG-115 root", exceptionLog);
            StringAssert.Contains("InvalidOperationException: BUG-115 wrapper", exceptionLog);
            StringAssert.Contains("ThrowStartupException", exceptionLog);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.HasLiveRuntime, Is.False);
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            if (!duringCompilation)
            {
                Assert.That(provider.IntentRuntime, Is.Not.Null);
                Assert.That(provider.IntentRuntime.IsDisposed, Is.True);
            }

            provider.Fail = false;
            Require(host.TryStart(out CoCoDiagnostic retry), retry);
            Assert.That(host.LastDiagnostic.IsNone, Is.True);
            Require(host.TryStop(out CoCoDiagnostic stop), stop);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StandardFactoryReportsMissingUpdateAndStartsAfterCorrection(bool missingUpdate)
        {
            Type logicType = missingUpdate ? typeof(HostTestMissingUpdateLogic) : typeof(HostTestStartupLogic);
            var provider = HostBindingProvider.Build(new[] { logicType.Assembly });
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            StateGraphAsset asset = CreateAsset(HostTestIds.Create(), withEvent: false);
            StateGraphStateRecord state = asset.Layers[0].States[0];
            Require(StandardDescriptors.TryCreate(logicType, logicType.Name, out StateDescriptorId descriptor));
            state.StateDescriptorId = new SerializedId128(descriptor.High, descriptor.Low);
            state.Config = new EmptyStateConfig();
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            if (missingUpdate)
            {
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(
                    "(?s)ArgumentException:.*(Invalid generic arguments|[Cc]onstraint)"));
                Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.CommitPreparationFailed));
                Assert.That(host.LastDiagnostic, Is.EqualTo(failure));
                Assert.That(host.HasLiveRuntime, Is.False);
                Assert.That(host.GraphInstanceId.IsValid, Is.False);
                logicType = typeof(HostTestStartupLogic);
                Require(StandardDescriptors.TryCreate(logicType, logicType.Name, out descriptor));
                state.StateDescriptorId = new SerializedId128(descriptor.High, descriptor.Low);
            }

            Require(host.TryStart(out CoCoDiagnostic start), start);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Running));
            Assert.That(host.LastDiagnostic.IsNone, Is.True);
            Require(host.TryStep(0.1, out CoCoDiagnostic step), step);
            Require(host.TryStop(out CoCoDiagnostic stop), stop);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissingAssetLeavesHostCreatedWithoutRouterRegistration()
        {
            StateGraphHost host = CreateHost(null, StateGraphDriver.Manual, 32);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.MissingDescriptor));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void MissingProviderWithValidAssetLeavesHostCreated()
        {
            HostTestIds ids = HostTestIds.Create();
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 32);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.RegistryNotFrozen));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void IgnoredExtraFactoryBindingStillFailsSetupAtomically()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                ignoreExtraBinding: true);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.IsError, Is.True);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void MismatchedStateFactoryLeavesHostCreatedWithoutCallbacks()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                mismatchedFactory: true);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);

            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void CreatedRejectsStopButAcceptsDispose()
        {
            StateGraphHost host = CreateHost(null, StateGraphDriver.Manual, 4);

            Assert.That(host.TryStop(out CoCoDiagnostic stop), Is.False);
            Assert.That(stop.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(stop.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));

            Require(host.TryDispose(out CoCoDiagnostic dispose), dispose);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
            Assert.That(host.TryDispose(out _), Is.False);
        }

        [Test]
        public void ProviderCannotDisposeHostDuringStartup()
        {
            HostTestIds ids = HostTestIds.Create();
            var inner = new HostTestBindingProvider(ids);
            StateGraphHost host = null;
            bool nestedDispose = true;
            CoCoDiagnostic nestedDiagnostic = default;
            var provider = new StartCallbackBindingProvider(
                inner,
                () => nestedDispose = host.TryDispose(out nestedDiagnostic));
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            host = CreateHost(asset, StateGraphDriver.Manual, 4);

            Require(host.TryStart(out CoCoDiagnostic start), start);

            Assert.That(provider.CallbackCount, Is.EqualTo(1));
            Assert.That(nestedDispose, Is.False);
            Assert.That(nestedDiagnostic.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(
                nestedDiagnostic.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Running));
        }

        [Test]
        public void ProviderDestroyDuringStartupCancelsNestedStepWithoutGhostRouter()
        {
            HostTestIds ids = HostTestIds.Create();
            var inner = new HostTestBindingProvider(ids);
            StateGraphHost host = null;
            bool nestedStep = true;
            CoCoDiagnostic nestedDiagnostic = default;
            var provider = new StartCallbackBindingProvider(
                inner,
                () =>
                {
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
                    nestedStep = host.TryStep(0.02d, out nestedDiagnostic);
                });
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            host = CreateHost(asset, StateGraphDriver.Manual, 4);

            bool started = host.TryStart(out CoCoDiagnostic start);

            Assert.That(started, Is.False);
            Assert.That(start.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(start.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(provider.CallbackCount, Is.EqualTo(1));
            Assert.That(nestedStep, Is.False);
            Assert.That(nestedDiagnostic.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(host == null, Is.True);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void StartDefersEnterUntilAcceptedTickAndStopDoesNotForgeExit()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 32);

            Require(host.TryStart(out CoCoDiagnostic start));
            GraphInstanceId firstInstance = host.GraphInstanceId;
            Assert.That(host.ActivePaths.Count, Is.EqualTo(1));
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);

            Require(host.TryStep(0.02d, out CoCoDiagnostic step));
            Assert.That(HostTestLogic.EnterCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.UpdateCount, Is.EqualTo(1));

            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Stopped));
            Assert.That(HostTestLogic.ExitCount, Is.Zero);

            Require(host.TryStart(out CoCoDiagnostic restart));
            Assert.That(host.GraphInstanceId.IsValid, Is.True);
            Assert.That(host.GraphInstanceId, Is.Not.EqualTo(firstInstance));
            Assert.That(HostTestLogic.EnterCount, Is.EqualTo(1));
        }

        [Test]
        public void InputAuthorityRevisionAdvancesAcrossTransientLifecycleBoundaries()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids);
            Require(HostProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(
                ids,
                withEvent: false);
            StateGraphHost host = CreateHost(
                asset,
                StateGraphDriver.Manual,
                32);

            ulong createdRevision = host.InputAuthorityRevision;
            Require(host.TryStart(out CoCoDiagnostic start), start);
            ulong runningRevision = host.InputAuthorityRevision;
            Assert.That(runningRevision, Is.Not.EqualTo(createdRevision));

            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            ulong suspendedRevision = host.InputAuthorityRevision;
            Assert.That(suspendedRevision, Is.Not.EqualTo(runningRevision));

            Require(host.TryResume(out CoCoDiagnostic resume), resume);
            ulong resumedRevision = host.InputAuthorityRevision;
            Assert.That(resumedRevision, Is.Not.EqualTo(suspendedRevision));

            Require(host.TryStop(out CoCoDiagnostic stop), stop);
            ulong stoppedRevision = host.InputAuthorityRevision;
            Assert.That(stoppedRevision, Is.Not.EqualTo(resumedRevision));

            Require(host.TryDispose(out CoCoDiagnostic dispose), dispose);
            Assert.That(
                host.InputAuthorityRevision,
                Is.Not.EqualTo(stoppedRevision));
        }

        [Test]
        public void SynchronousDestroyDuringStateUpdateCancelsBeforeFinalizeThenDisposes()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            StateGraphInstance runtime = GetRuntime(host);
            HostTestLogic.UpdateCallback = () =>
                UnityEngine.Object.DestroyImmediate(host.gameObject);

            bool stepped = host.TryStep(0.02d, out CoCoDiagnostic step);

            Assert.That(stepped, Is.False);
            Assert.That(step.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(step.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host == null, Is.True);
            Assert.That(runtime.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(GetCommittedMemoryValue(runtime), Is.Zero);
            Assert.That(HostTestLogic.EnterCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.UpdateCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.ExitCount, Is.Zero);
        }

        [Test]
        public void SynchronousDestroyDuringCommitValidationCancelsBeforeAuthoritySwap()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            StateGraphInstance runtime = GetRuntime(host);
            bool destroyed = false;
            HostTestLogic.MemoryFingerprintCallback = () =>
            {
                if (!destroyed && runtime.HasStagedStep)
                {
                    destroyed = true;
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
                }
            };

            bool stepped = host.TryStep(0.02d, out CoCoDiagnostic step);

            Assert.That(stepped, Is.False);
            Assert.That(step.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(step.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(destroyed, Is.True);
            Assert.That(host == null, Is.True);
            Assert.That(runtime.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
            Assert.That(runtime.Fault.IsFaulted, Is.False);
            Assert.That(runtime.Clock.Tick.Value, Is.Zero);
            Assert.That(GetCommittedMemoryValue(runtime), Is.Zero);
            Assert.That(HostTestLogic.EnterCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.UpdateCount, Is.EqualTo(1));
            Assert.That(HostTestLogic.ExitCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PublicDisposeRejectsLiveInstanceWithoutChangingAuthority(bool suspend)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            Require(host.TryStep(0.02d, out CoCoDiagnostic step));
            if (suspend)
            {
                Require(host.TrySuspend(out CoCoDiagnostic suspendDiagnostic), suspendDiagnostic);
            }

            StateGraphInstance runtime = GetRuntime(host);
            HostRuntimeBindings bindings = GetBindings(host);
            GraphInstanceId graphInstanceId = host.GraphInstanceId;
            RuntimeLifecycleState lifecycle = host.Lifecycle;
            EventPacket<HostTestEvent> queued = Packet(
                ids,
                graphInstanceId,
                graphInstanceId,
                1UL,
                10,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(queued), Is.EqualTo(suspend
                ? InboxEnqueueResult.MailboxUnavailable
                : InboxEnqueueResult.Accepted));

            Assert.That(host.TryDispose(out CoCoDiagnostic dispose), Is.False);
            Assert.That(dispose.Domain, Is.EqualTo(CoCoDiagnosticDomain.Lifecycle));
            Assert.That(dispose.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host.Lifecycle, Is.EqualTo(lifecycle));
            Assert.That(host.GraphInstanceId, Is.EqualTo(graphInstanceId));
            Assert.That(GetRuntime(host), Is.SameAs(runtime));
            Assert.That(GetBindings(host), Is.SameAs(bindings));
            Assert.That(HostTestLogic.ExitCount, Is.Zero);

            if (suspend)
            {
                Require(host.TryResume(out CoCoDiagnostic resume), resume);
                Assert.That(host.TryEnqueueLocal(queued), Is.EqualTo(InboxEnqueueResult.Accepted));
            }

            Require(host.TryStep(0.02d, out CoCoDiagnostic queuedStep), queuedStep);
            Assert.That(HostTestLogic.LastEventValue, Is.EqualTo(10));
            Require(host.TryStop(out CoCoDiagnostic stop), stop);
            Require(host.TryDispose(out CoCoDiagnostic finalDispose), finalDispose);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunningAndSuspendedHostsStopThenAcceptDispose(bool suspend)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            if (suspend)
            {
                Require(host.TrySuspend(out CoCoDiagnostic suspendDiagnostic), suspendDiagnostic);
            }

            Require(host.TryStop(out CoCoDiagnostic stop));

            Require(host.TryDispose(out CoCoDiagnostic dispose), dispose);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
            Assert.That(host.GraphInstanceId.IsValid, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnityDestroyForcesLiveInstanceThroughSafeDisposalWithoutCallbacks(bool suspend)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            Require(host.TryStep(0.02d, out CoCoDiagnostic step));
            if (suspend)
            {
                Require(host.TrySuspend(out CoCoDiagnostic suspendDiagnostic), suspendDiagnostic);
            }

            StateGraphInstance runtime = GetRuntime(host);
            ulong tick = runtime.Clock.Tick.Value;
            int updates = HostTestLogic.UpdateCount;
            int exits = HostTestLogic.ExitCount;

            UnityEngine.Object.DestroyImmediate(host.gameObject);

            Assert.That(host == null, Is.True);
            Assert.That(runtime.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Disposed));
            Assert.That(runtime.Clock.Tick.Value, Is.EqualTo(tick));
            Assert.That(HostTestLogic.UpdateCount, Is.EqualTo(updates));
            Assert.That(HostTestLogic.ExitCount, Is.EqualTo(exits));
        }

        [Test]
        public void TimelineIdentityUsesGraphInstanceAsHighAndGraphXorAsLow()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            SetAssetGraphId(asset, 1UL, 0UL);
            StateGraphHost first = CreateHost(asset, StateGraphDriver.Manual, 4);
            StateGraphHost second = CreateHost(asset, StateGraphDriver.Manual, 4);

            Require(first.TryStart(out CoCoDiagnostic firstStart), firstStart);
            Require(second.TryStart(out CoCoDiagnostic secondStart), secondStart);

            TimelineId firstTimeline = GetRuntime(first).Clock.TimelineId;
            TimelineId secondTimeline = GetRuntime(second).Clock.TimelineId;
            Assert.That(first.GraphInstanceId.Value, Is.EqualTo(1UL));
            Assert.That(firstTimeline.High, Is.EqualTo(1UL));
            Assert.That(firstTimeline.Low, Is.EqualTo(1UL));
            Assert.That(firstTimeline.IsValid, Is.True);
            Assert.That(secondTimeline.IsValid, Is.True);
            Assert.That(secondTimeline, Is.Not.EqualTo(firstTimeline));
        }

        [Test]
        public void TimelineIdentityRejectsInvalidInputsWithTimeDiagnostic()
        {
            Require(GraphId.TryCreate(1UL, 0UL, out GraphId graphId));
            Require(GraphInstanceId.TryCreate(1UL, out GraphInstanceId graphInstanceId));

            AssertTimelineIdentityFailure(default, graphInstanceId);
            AssertTimelineIdentityFailure(graphId, default);
        }

        [Test]
        public void BindingValidationRejectsMissingFactoryWithoutRuntimeOrRouter()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                omitStateFactory: true);
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            GraphAssetCompileResult result =
                new GraphAssetCompiler().Compile(asset, provider.Catalog);
            Assert.That(result.Succeeded, Is.True);

            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install), install);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Assert.That(host.TryStart(out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(host.HasLiveRuntime, Is.False);
            Assert.That(diagnostic.IsError, Is.True);
            Assert.That(HostTestLogic.EnterCount, Is.Zero);
            Assert.That(HostTestLogic.UpdateCount, Is.Zero);
        }

        [Test]
        public void ExplicitInboxAdaptersMustStayInsideNearestHost()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);

            StateGraphHost outside =
                CreateHost(asset, StateGraphDriver.Manual, 4);
            var outsideObject = new GameObject("Outside StateGraph Host");
            _objects.Add(outsideObject);
            var outsideAdapter = outsideObject.AddComponent<HostTestEventAdapterComponent>();
            SetField(outside, "eventAdapters", new MonoBehaviour[] { outsideAdapter });
            Assert.That(
                outside.TryStart(out CoCoDiagnostic outsideDiagnostic),
                Is.False);
            Assert.That(
                outsideDiagnostic.Code,
                Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
            Assert.That(
                outside.Lifecycle,
                Is.EqualTo(RuntimeLifecycleState.Created));

            StateGraphHost outer = CreateHost(asset, StateGraphDriver.Manual, 4);
            var nestedObject = new GameObject("Nested StateGraph Host");
            nestedObject.transform.SetParent(outer.transform, false);
            nestedObject.AddComponent<StateGraphHost>();
            var nestedAdapter = nestedObject.AddComponent<HostTestEventAdapterComponent>();
            SetField(outer, "eventAdapters", new MonoBehaviour[] { nestedAdapter });
            Assert.That(outer.TryStart(out CoCoDiagnostic nestedDiagnostic), Is.False);
            Assert.That(nestedDiagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
            Assert.That(outer.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Created));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SuspendedDebugStepRunsOneScaledTickForEveryDriver(
            int driverValue)
        {
            var selectedDriver = (StateGraphDriver)driverValue;
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, selectedDriver, 4);
            SetField(host, "timeScale", 2f);
            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            Require(host.TryCaptureDebugSnapshot(
                out HostDebugSnapshot before,
                out CoCoDiagnostic beforeCapture), beforeCapture);
            HostDebugActiveState beforeState =
                before.GetLayer(0).GetActiveState(0);

            Require(host.TryDebugStepWhileSuspended(0.125d, out CoCoDiagnostic step), step);
            Require(host.TryCaptureDebugSnapshot(
                out HostDebugSnapshot after,
                out CoCoDiagnostic afterCapture), afterCapture);

            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
            Assert.That(before.GraphId, Is.EqualTo(asset.GraphId));
            Assert.That(before.GraphInstanceId, Is.EqualTo(host.GraphInstanceId));
            Assert.That(before.ContentFingerprint, Is.Not.Zero);
            Assert.That(before.CatalogFingerprint, Is.EqualTo(provider.Catalog.Fingerprint));
            Assert.That(before.TimelineId, Is.EqualTo(after.TimelineId));
            Assert.That(before.ClockDomainId, Is.EqualTo(after.ClockDomainId));
            Assert.That(before.TimelineEpoch, Is.EqualTo(after.TimelineEpoch));
            Assert.That(before.Tick.Value, Is.Zero);
            Assert.That(before.Seconds, Is.Zero);
            Assert.That(after.Tick.Value, Is.EqualTo(1UL));
            Assert.That(after.Seconds, Is.EqualTo(0.25d));
            Assert.That(before.Tick.Value, Is.Zero, "The previous snapshot must stay immutable.");
            Assert.That(beforeState.LocalSeconds, Is.Zero);
            Assert.That(
                before.GetLayer(0).GetActiveState(0).LocalSeconds,
                Is.Zero,
                "The previous active-state copy must stay immutable.");
            Assert.That(
                after.GetLayer(0).GetActiveState(0).LocalSeconds,
                Is.EqualTo(0.25d));
            Assert.That(after.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
            Assert.That(after.LayerCount, Is.EqualTo(1));
            Assert.That(after.GetLayer(0).ActiveStateCount, Is.EqualTo(1));
            Assert.That(after.ContextHeader.IsValid, Is.True);
            Assert.That(
                after.ContextHeader.Identity.GraphInstanceId,
                Is.EqualTo(host.GraphInstanceId));
            Assert.That(after.ContextHeader.Identity.Kind, Is.EqualTo(StateFlowFrameKind.Context));
            Assert.That(after.ContextHeader.TickFrame.TimelineId, Is.EqualTo(after.TimelineId));
            Assert.That(after.ContextHeader.TickFrame.ClockDomainId, Is.EqualTo(after.ClockDomainId));
            Assert.That(after.ContextHeader.TickFrame.TimelineEpoch, Is.EqualTo(after.TimelineEpoch));
            Assert.That(after.ContextHeader.TickFrame.Tick, Is.EqualTo(after.Tick));
            Assert.That(
                after.ContextHeader.TickFrame.ExecutionSequence,
                Is.EqualTo(after.ExecutionSequence));
        }

        [Test]
        public void SuspendedDebugStepKeepsInboxClosedAndWorksWithoutTrace()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Update, 4);
            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);
            EventPacket<HostTestEvent> packet = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                7,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(packet), Is.EqualTo(InboxEnqueueResult.MailboxUnavailable));

            Require(host.TryDebugStepWhileSuspended(0.02d, out CoCoDiagnostic first), first);
            Assert.That(HostTestLogic.EventCount, Is.Zero);
            Require(host.TryDebugStepWhileSuspended(0.02d, out CoCoDiagnostic second), second);
            Assert.That(HostTestLogic.EventCount, Is.Zero);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
            Assert.That(host.Trace, Is.Null);
            Require(host.TryCaptureDebugSnapshot(
                out HostDebugSnapshot snapshot,
                out CoCoDiagnostic capture), capture);
            Assert.That(snapshot.Tick.Value, Is.EqualTo(2UL));
            Assert.That(snapshot.ContextRevision.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void InvalidSuspendedDebugDeltaDoesNotResumeOrAdvance()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start), start);
            Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);

            Assert.That(host.TryDebugStepWhileSuspended(0d, out CoCoDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
            Assert.That(GetRuntime(host).Clock.Tick.Value, Is.Zero);
        }

        [Test]
        public void RetryableSuspendedDebugStepFailureReturnsToSuspendedWithoutAdvancing()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            SetField(host, "contextFrameCapacity", 2);
            ContextFrame first = default;
            ContextFrame second = default;
            bool firstRetained = false;
            bool secondRetained = false;
            try
            {
                Require(host.TryStart(out CoCoDiagnostic start), start);
                Require(host.TryStep(0.02d, out CoCoDiagnostic firstStep), firstStep);
                first = host.CurrentContext;
                firstRetained = first.Retain();
                Require(firstRetained);
                Require(host.TryStep(0.02d, out CoCoDiagnostic secondStep), secondStep);
                second = host.CurrentContext;
                secondRetained = second.Retain();
                Require(secondRetained);
                Require(host.TrySuspend(out CoCoDiagnostic suspend), suspend);

                Assert.That(
                    host.TryDebugStepWhileSuspended(0.02d, out CoCoDiagnostic failure),
                    Is.False);
                Assert.That(failure.Code, Is.EqualTo(CoCoDiagnosticCode.CommitPreparationFailed));
                Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
                Assert.That(host.Fault.IsFaulted, Is.False);
                Assert.That(host.RequiresWorldCorrection, Is.False);
                Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(2UL));
                Assert.That(host.CurrentContext, Is.EqualTo(second));

                Assert.That(first.Release(), Is.True);
                firstRetained = false;
                Assert.That(second.Release(), Is.True);
                secondRetained = false;
                Require(
                    host.TryDebugStepWhileSuspended(0.02d, out CoCoDiagnostic retry),
                    retry);
                Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Suspended));
                Assert.That(GetRuntime(host).Clock.Tick.Value, Is.EqualTo(3UL));
            }
            finally
            {
                if (secondRetained)
                {
                    Assert.That(second.Release(), Is.True);
                }

                if (firstRetained)
                {
                    Assert.That(first.Release(), Is.True);
                }
            }
        }

        [UnityTest]
        public IEnumerator UpdateDriverAdvancesAtMostOncePerFrameAndContinuesNextFrame() =>
            VerifyAutomaticDriverFrameGate(StateGraphDriver.Update, "Update");

        [UnityTest]
        public IEnumerator FixedUpdateDriverAdvancesAtMostOncePerFrameAndContinuesNextFrame() =>
            VerifyAutomaticDriverFrameGate(StateGraphDriver.FixedUpdate, "FixedUpdate");

        [Test]
        public void LocalInboxDeliversPayloadAndReliableOverflowFaultsAtStepBoundary()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 1);
            Require(host.TryStart(out CoCoDiagnostic start));

            EventPacket<HostTestEvent> first = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                41,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(first), Is.EqualTo(InboxEnqueueResult.Accepted));
            Require(host.TryStep(0.02d, out CoCoDiagnostic firstStep));
            Assert.That(HostTestLogic.LastEventValue, Is.EqualTo(41));

            Require(host.TrySuspend(out CoCoDiagnostic suspend));
            EventPacket<HostTestEvent> suspended = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                2UL,
                52,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(suspended), Is.EqualTo(InboxEnqueueResult.MailboxUnavailable));
            Require(host.TryResume(out CoCoDiagnostic resume));
            Assert.That(host.TryEnqueueLocal(suspended), Is.EqualTo(InboxEnqueueResult.Accepted));
            Require(host.TryStep(0.02d, out CoCoDiagnostic resumedStep));
            Assert.That(HostTestLogic.LastEventValue, Is.EqualTo(52));

            EventPacket<HostTestEvent> fillsLane = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                3UL,
                63,
                EventReliability.Reliable);
            EventPacket<HostTestEvent> overflow = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                4UL,
                64,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(fillsLane), Is.EqualTo(InboxEnqueueResult.Accepted));
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(InboxEnqueueResult.ReliableOverflowFaultRequired));
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic fault), Is.False);
            Assert.That(fault.Code, Is.EqualTo(CoCoDiagnosticCode.MailboxOverflow));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(InboxEnqueueResult.MailboxUnavailable));
        }

        [Test]
        public void ReliableOverflowFaultsBeforeSuspendAndStopStillDiscardsTheInstance()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 1);
            Require(host.TryStart(out CoCoDiagnostic start));

            EventPacket<HostTestEvent> fill = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                1,
                EventReliability.Reliable);
            EventPacket<HostTestEvent> overflow = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                2UL,
                2,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(fill), Is.EqualTo(InboxEnqueueResult.Accepted));
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(InboxEnqueueResult.ReliableOverflowFaultRequired));

            Assert.That(host.TrySuspend(out CoCoDiagnostic fault), Is.False);
            Assert.That(fault.Code, Is.EqualTo(CoCoDiagnosticCode.MailboxOverflow));
            Assert.That(host.Fault.IsFaulted, Is.True);
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Running));
            Assert.That(
                host.TryEnqueueLocal(overflow),
                Is.EqualTo(InboxEnqueueResult.MailboxUnavailable));

            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Stopped));
        }

        [Test]
        public void ThrowingIntentSourceIsContainedAndRollsBackAllAuthorityUntilStop()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(
                ids,
                withIntentSource: true);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            var throwing =
                host.gameObject.AddComponent<ThrowingHostTestIntentSource>();
            SetField(host, "intentSources", new MonoBehaviour[] { throwing });
            Require(host.TryStart(out CoCoDiagnostic start));

            StateGraphInstance runtime = GetRuntime(host);
            StateId committedLeaf = host.ActivePaths[0].ActiveLeaf;
            EventPacket<HostTestEvent> packet = Packet(
                ids,
                host.GraphInstanceId,
                host.GraphInstanceId,
                1UL,
                10,
                EventReliability.Reliable);
            Assert.That(host.TryEnqueueLocal(packet), Is.EqualTo(InboxEnqueueResult.Accepted));

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
            Assert.That(GetCommittedContext(host), Is.EqualTo(default(ContextFrame)));
            Require(host.TryCaptureDebugSnapshot(
                out HostDebugSnapshot snapshot,
                out CoCoDiagnostic capture), capture);
            Assert.That(snapshot.Fault.IsFaulted, Is.True);
            Assert.That(snapshot.Tick.Value, Is.Zero);
            Assert.That(snapshot.ContextRevision.Value, Is.Zero);

            Assert.That(host.TryStep(0.02d, out _), Is.False);
            Assert.That(host.TryResume(out _), Is.False);
            Require(host.TryStop(out CoCoDiagnostic stop));
            Assert.That(host.Lifecycle, Is.EqualTo(RuntimeLifecycleState.Stopped));
        }

        [Test]
        public void TargetedRouterDeliveryStopsAfterTargetHostStops()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost source = CreateHost(asset, StateGraphDriver.Manual, 4);
            StateGraphHost target = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(source.TryStart(out CoCoDiagnostic sourceStart));
            Require(target.TryStart(out CoCoDiagnostic targetStart));

            EventPacket<HostTestEvent> packet = Packet(
                ids,
                source.GraphInstanceId,
                target.GraphInstanceId,
                1UL,
                77,
                EventReliability.Reliable);
            CoCoEventBus.Publish(ref packet);
            Require(target.TryStep(0.02d, out CoCoDiagnostic targetStep));
            Require(source.TryStep(0.02d, out CoCoDiagnostic sourceStep));
            Assert.That(HostTestLogic.GetLastEvent(target.GraphInstanceId), Is.EqualTo(77));
            Assert.That(HostTestLogic.GetLastEvent(source.GraphInstanceId), Is.Zero);

            Require(target.TryStop(out CoCoDiagnostic targetStop));
            CoCoEventBus.Publish(ref packet);
            Require(source.TryStep(0.02d, out CoCoDiagnostic afterStop), afterStop);
            Assert.That(HostTestLogic.EventCount, Is.EqualTo(1));
            Require(source.TryStop(out CoCoDiagnostic sourceStop));

            CoCoEventBus.Publish(ref packet);
        }

        [Test]
        public void DeclaredBroadcastReachesEveryRegisteredHostInTheDomain()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost first = CreateHost(asset, StateGraphDriver.Manual, 4);
            StateGraphHost second = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(first.TryStart(out CoCoDiagnostic firstStart));
            Require(second.TryStart(out CoCoDiagnostic secondStart));
            Require(GraphInstanceId.TryCreate(999UL, out GraphInstanceId externalSource));

            EventPacket<HostTestEvent> packet = BroadcastPacket(
                ids,
                externalSource,
                1UL,
                88);
            CoCoEventBus.Publish(ref packet);
            Require(first.TryStep(0.02d, out CoCoDiagnostic firstStep));
            Require(second.TryStep(0.02d, out CoCoDiagnostic secondStep));
            Assert.That(HostTestLogic.GetLastEvent(first.GraphInstanceId), Is.EqualTo(88));
            Assert.That(HostTestLogic.GetLastEvent(second.GraphInstanceId), Is.EqualTo(88));
        }

        [Test]
        public void HostsSharingAssetKeepClockMemoryInboxAndFaultIsolated()
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost healthy = CreateHost(asset, StateGraphDriver.Manual, 1);
            StateGraphHost faulted = CreateHost(asset, StateGraphDriver.Manual, 1);
            Require(healthy.TryStart(out CoCoDiagnostic healthyStart));
            Require(faulted.TryStart(out CoCoDiagnostic faultedStart));

            Require(healthy.TryStep(0.02d, out CoCoDiagnostic healthyStep));
            StateGraphInstance healthyRuntime = GetRuntime(healthy);
            StateGraphInstance faultedRuntime = GetRuntime(faulted);
            Assert.That(healthyRuntime.Clock.Tick.Value, Is.EqualTo(1UL));
            Assert.That(faultedRuntime.Clock.Tick.Value, Is.Zero);
            Assert.That(HostTestLogic.GetMemoryValue(healthy.GraphInstanceId), Is.EqualTo(1));
            Assert.That(HostTestLogic.GetMemoryValue(faulted.GraphInstanceId), Is.Zero);

            EventPacket<HostTestEvent> fill = Packet(
                ids,
                faulted.GraphInstanceId,
                faulted.GraphInstanceId,
                1UL,
                1,
                EventReliability.Reliable);
            EventPacket<HostTestEvent> overflow = Packet(
                ids,
                faulted.GraphInstanceId,
                faulted.GraphInstanceId,
                2UL,
                2,
                EventReliability.Reliable);
            Assert.That(faulted.TryEnqueueLocal(fill), Is.EqualTo(InboxEnqueueResult.Accepted));
            Assert.That(
                faulted.TryEnqueueLocal(overflow),
                Is.EqualTo(InboxEnqueueResult.ReliableOverflowFaultRequired));
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
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: true);
            StateGraphHost host = CreateHost(asset, StateGraphDriver.Manual, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            Require(GraphInstanceId.TryCreate(999UL, out GraphInstanceId source));

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
            Assert.That(HostTestLogic.EventCount, Is.EqualTo(10100));

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

        private StateGraphHost CreateHost(
            StateGraphAsset asset,
            StateGraphDriver selectedDriver,
            int laneCapacity)
        {
            var gameObject = new GameObject("StateGraphHost Test");
            _objects.Add(gameObject);
            StateGraphHost host = gameObject.AddComponent<StateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", selectedDriver);
            SetField(host, "autoStart", false);
            SetField(host, "eventLaneCapacity", laneCapacity);
            if (_eventGraphs.Contains(asset))
            {
                gameObject.AddComponent<HostTestEventAdapterComponent>();
            }

            return host;
        }

        private StateGraphAsset CreateAsset(HostTestIds ids, bool withEvent)
        {
            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new StateGraphStateRecord(
                new SerializedId128(ids.StateId.High, ids.StateId.Low),
                default,
                "Leaf",
                new SerializedId128(ids.StateDescriptorId.High, ids.StateDescriptorId.Low),
                new HostTestStateConfig { Value = 5 });
            var layer = new StateGraphLayerRecord(
                new SerializedId128(ids.LayerId.High, ids.LayerId.Low),
                "Base");
            layer.InitialStateId = new SerializedId128(ids.StateId.High, ids.StateId.Low);
            layer.States.Add(state);
            asset.Layers.Add(layer);
            if (withEvent)
            {
                _eventGraphs.Add(asset);
            }

            return asset;
        }

        private IEnumerator VerifyAutomaticDriverFrameGate(
            StateGraphDriver selectedDriver,
            string callbackName)
        {
            HostTestIds ids = HostTestIds.Create();
            var provider = new HostTestBindingProvider(ids);
            Require(HostProjectBindings.TryInstall(provider, out CoCoDiagnostic install));
            StateGraphAsset asset = CreateAsset(ids, withEvent: false);
            StateGraphHost host = CreateHost(asset, selectedDriver, 4);
            Require(host.TryStart(out CoCoDiagnostic start));
            host.enabled = false;
            StateGraphInstance runtime = GetRuntime(host);

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

        private static EventPacket<HostTestEvent> Packet(
            HostTestIds ids,
            GraphInstanceId source,
            GraphInstanceId target,
            ulong sequence,
            int value,
            EventReliability reliability)
        {
            Require(EventSequence.TryCreate(sequence, out EventSequence eventSequence));
            Require(GraphEventEnvelope.TryCreate(
                ids.EventTypeId,
                ids.EventDomainId,
                source,
                target,
                new TimelineEpoch(0UL),
                new TimelineTick(0UL),
                eventSequence,
                EventDeliveryMode.Targeted,
                reliability,
                default,
                default,
                default,
                out GraphEventEnvelope envelope));
            var payload = new HostTestEvent { Value = value };
            Require(EventPacket<HostTestEvent>.TryCreate(envelope, payload, out EventPacket<HostTestEvent> packet));
            return packet;
        }

        private static EventPacket<HostTestEvent> BroadcastPacket(
            HostTestIds ids,
            GraphInstanceId source,
            ulong sequence,
            int value)
        {
            Require(EventSequence.TryCreate(sequence, out EventSequence eventSequence));
            Require(GraphEventEnvelope.TryCreate(
                ids.EventTypeId,
                ids.EventDomainId,
                source,
                default,
                new TimelineEpoch(0UL),
                new TimelineTick(0UL),
                eventSequence,
                EventDeliveryMode.DeclaredBroadcast,
                EventReliability.Reliable,
                default,
                default,
                default,
                out GraphEventEnvelope envelope));
            var payload = new HostTestEvent { Value = value };
            Require(EventPacket<HostTestEvent>.TryCreate(
                envelope,
                payload,
                out EventPacket<HostTestEvent> packet));
            return packet;
        }

        private static bool TryRunRoutedTick(
            StateGraphHost host,
            HostTestIds ids,
            GraphInstanceId source,
            ulong sequence)
        {
            if (!EventSequence.TryCreate(sequence, out EventSequence eventSequence) ||
                !GraphEventEnvelope.TryCreate(
                    ids.EventTypeId,
                    ids.EventDomainId,
                    source,
                    host.GraphInstanceId,
                    new TimelineEpoch(0UL),
                    new TimelineTick(0UL),
                    eventSequence,
                    EventDeliveryMode.Targeted,
                    EventReliability.Reliable,
                    default,
                    default,
                    default,
                    out GraphEventEnvelope envelope))
            {
                return false;
            }

            var payload = new HostTestEvent { Value = (int)sequence };
            if (!EventPacket<HostTestEvent>.TryCreate(
                    envelope,
                    payload,
                    out EventPacket<HostTestEvent> packet))
            {
                return false;
            }

            CoCoEventBus.Publish(ref packet);
            return host.TryStep(0.001d, out _);
        }

        private static StateGraphInstance GetRuntime(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_instance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StateGraphInstance)field.GetValue(host);
        }

        private static HostRuntimeBindings GetBindings(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (HostRuntimeBindings)field.GetValue(host);
        }

        private static ulong GetCommittedSequence(
            OperationFrame operationFrame,
            int denseIndex)
        {
            FieldInfo field = typeof(OperationFrame).GetField(
                "_committedSequences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var sequences = (ulong[])field.GetValue(operationFrame);
            Assert.That(denseIndex, Is.InRange(0, sequences.Length - 1));
            return sequences[denseIndex];
        }

        private static void InvokePrivateCallback(StateGraphHost host, string callbackName)
        {
            MethodInfo callback = typeof(StateGraphHost).GetMethod(
                callbackName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(callback, Is.Not.Null, callbackName);
            callback.Invoke(host, null);
        }

        private static int GetCommittedMemoryValue(StateGraphInstance runtime)
        {
            FieldInfo layersField = typeof(StateGraphInstance).GetField(
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

        private static ContextFrame GetCommittedContext(StateGraphHost host)
        {
            return host.CurrentContext;
        }

        private static void SetField<TValue>(StateGraphHost host, string fieldName, TValue value)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(host, value);
        }

        private static void SetAssetGraphId(StateGraphAsset asset, ulong high, ulong low)
        {
            FieldInfo field = typeof(StateGraphAsset).GetField(
                "graphId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(asset, new SerializedId128(high, low));
        }

        private static void AssertTimelineIdentityFailure(
            GraphId graphId,
            GraphInstanceId graphInstanceId)
        {
            MethodInfo method = typeof(HostStartup).GetMethod(
                "TryCreateTimelineId",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments =
            {
                graphId,
                graphInstanceId,
                default(TimelineId),
                default(CoCoDiagnostic)
            };

            Assert.That((bool)method.Invoke(null, arguments), Is.False);
            var diagnostic = (CoCoDiagnostic)arguments[3];
            Assert.That(diagnostic.IsNone, Is.False);
            Assert.That(diagnostic.Domain, Is.EqualTo(CoCoDiagnosticDomain.Time));
            Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.InvalidIdentifier));
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic = default)
        {
            Assert.That(succeeded, Is.True, diagnostic.Message);
        }

        private sealed class StartupExceptionLogHandler : ILogHandler
        {
            private readonly ILogHandler _inner;

            public StartupExceptionLogHandler(ILogHandler inner)
            {
                _inner = inner;
            }

            public Exception LoggedException { get; private set; }
            public UnityEngine.Object LoggedContext { get; private set; }
            public int ExceptionCount { get; private set; }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                LoggedException = exception;
                LoggedContext = context;
                ExceptionCount++;
                _inner.LogException(exception, context);
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
                _inner.LogFormat(logType, context, format, args);
        }

        private sealed class StartupExceptionBindingProvider : IHostBindingProvider
        {
            private readonly HostTestBindingProvider _inner;
            private readonly bool _duringCompilation;

            public StartupExceptionBindingProvider(HostTestBindingProvider inner, bool duringCompilation)
            {
                _inner = inner;
                _duringCompilation = duringCompilation;
            }

            public bool Fail { get; set; }
            public Exception StartupException { get; private set; }
            public IntentFrameRuntime IntentRuntime { get; private set; }
            public GraphDescriptorCatalog Catalog
            {
                get
                {
                    if (Fail && _duringCompilation) ThrowStartupException();
                    return _inner.Catalog;
                }
            }

            public bool TryConfigure(HostBindingBuilder builder, out CoCoDiagnostic diagnostic)
            {
                if (!_inner.TryConfigure(builder, out diagnostic)) return false;
                IntentRuntime = (IntentFrameRuntime)typeof(HostBindingBuilder)
                    .GetField("_intentRuntime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(builder);
                if (Fail) ThrowStartupException();
                return true;
            }

            private void ThrowStartupException()
            {
                try
                {
                    throw new ArgumentException("BUG-115 root");
                }
                catch (ArgumentException exception)
                {
                    StartupException = new InvalidOperationException("BUG-115 wrapper", exception);
                    throw StartupException;
                }
            }
        }

        private sealed class StartCallbackBindingProvider :
            IHostBindingProvider
        {
            private readonly IHostBindingProvider _inner;
            private readonly Action _callback;

            public StartCallbackBindingProvider(
                IHostBindingProvider inner,
                Action callback)
            {
                _inner = inner;
                _callback = callback;
            }

            public int CallbackCount { get; private set; }
            public GraphDescriptorCatalog Catalog => _inner.Catalog;

            public bool TryConfigure(
                HostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                CallbackCount++;
                _callback();
                return _inner.TryConfigure(builder, out diagnostic);
            }
        }

        private sealed class HostTestBindingProvider : IHostBindingProvider
        {
            private const ulong ReducerFingerprint = 991UL;
            private const ulong OperationFingerprint = 992UL;
            private readonly HostTestIds _ids;
            private readonly bool _ignoreExtraBinding;
            private readonly bool _mismatchedFactory;
            private readonly bool _withIntentSource;
            private readonly bool _omitStateFactory;
            private readonly bool _withDiscreteOperation;

            public HostTestBindingProvider(
                HostTestIds ids,
                bool ignoreExtraBinding = false,
                bool mismatchedFactory = false,
                bool withIntentSource = false,
                bool omitStateFactory = false,
                bool withDiscreteOperation = false)
            {
                _ids = ids;
                _ignoreExtraBinding = ignoreExtraBinding;
                _mismatchedFactory = mismatchedFactory;
                _withIntentSource = withIntentSource;
                _omitStateFactory = omitStateFactory;
                _withDiscreteOperation = withDiscreteOperation;
                OperationFactory = withDiscreteOperation
                    ? new HostTestDiscreteSectionViewFactory()
                    : null;
                Catalog = BuildCatalog(ids, withIntentSource, withDiscreteOperation);
            }

            public GraphDescriptorCatalog Catalog { get; }
            public HostTestDiscreteSectionViewFactory OperationFactory { get; }

            public bool TryConfigure(
                HostBindingBuilder builder,
                out CoCoDiagnostic diagnostic)
            {
                if (_withIntentSource)
                {
                    if (!builder.TryRegisterIntent<
                            HostTestIntent,
                            HostTestIntentReducer,
                            HostTestIntentReducerFactory>(
                            _ids.IntentId,
                            new HostTestIntentReducerFactory(),
                            ReducerFingerprint,
                            out IntentHandle<HostTestIntent> intent,
                            out diagnostic) ||
                        !builder.TryBeginIntentBindings(out diagnostic) ||
                        !IntentSourceRequirement<HostTestIntent>.TryCreate(
                            intent,
                            1,
                            out IntentSourceRequirement<HostTestIntent> requirement) ||
                        !builder.TryBindIntentSource(0, requirement, out diagnostic))
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
                        _ids.OperationId,
                        Mode.Discrete,
                        OperationFactory,
                        OperationFingerprint,
                        out OperationRequirement operationRequirement,
                        out diagnostic))
                {
                    return false;
                }

                if (!TryBindGraphState(builder, _ids, out diagnostic))
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
                    var mismatched = new StateRuntimeFactory<
                        HostTestMismatchedLogic,
                        HostTestMemory>(
                        _ => new HostTestMismatchedLogic(),
                        () => new HostTestMemory(),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        HostTestLogic.GetMemoryFingerprint);
                    return builder.TryBindState(
                        _ids.StateDescriptorId,
                        mismatched,
                        out diagnostic);
                }

                var stateFactory = new StateRuntimeFactory<HostTestLogic, HostTestMemory>(
                    context => new HostTestLogic(
                        context.GraphInstanceId,
                        OperationFactory?.Handle ?? default,
                        OperationFactory?.ValueField ?? default),
                    () => new HostTestMemory(),
                    (source, destination) => destination.Value = source.Value,
                    memory => memory.Value = 0,
                    HostTestLogic.GetMemoryFingerprint);
                if (!builder.TryBindState(
                    _ids.StateDescriptorId,
                    stateFactory,
                    out diagnostic))
                {
                    return false;
                }

                if (_ignoreExtraBinding)
                {
                    StateDescriptorId.TryCreate(
                        987UL,
                        654UL,
                        out StateDescriptorId undeclared);
                    builder.TryBindState(undeclared, stateFactory, out _);
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            private static GraphDescriptorCatalog BuildCatalog(
                HostTestIds ids,
                bool withIntentSource,
                bool withDiscreteOperation)
            {
                var builder = new GraphDescriptorCatalogBuilder();
                IntentId[] intents = null;
                OperationId[] operations = null;
                if (withIntentSource)
                {
                    Require(builder.TryRegisterIntent(
                        ids.IntentId,
                        4,
                        new IntentReducerFactoryToken<
                            HostTestIntent,
                            HostTestIntentReducer,
                            HostTestIntentReducerFactory>(ReducerFingerprint),
                        out CoCoDiagnostic intent));
                    intents = new[] { ids.IntentId };
                }

                if (withDiscreteOperation)
                {
                    Require(builder.TryRegisterOperation(
                        ids.OperationId,
                        Mode.Discrete,
                        new OperationViewFactoryToken<
                            IHostTestDiscreteSection,
                            HostTestDiscreteSectionViewFactory>(OperationFingerprint),
                        out CoCoDiagnostic operation));
                    operations = new[] { ids.OperationId };
                }

                Require(TryRegisterGraphState(builder, ids, out CoCoDiagnostic graphState));
                Require(builder.TryRegisterState(
                    ids.StateDescriptorId,
                    1U,
                    new HostTestStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        HostTestLogic,
                        HostTestStateConfigSchema,
                        HostTestMemory>(HostTestSchemas.State, false),
                    intents,
                    operations,
                    new[] { ids.GraphStateBlockId },
                    out CoCoDiagnostic state));
                Require(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freeze));
                return catalog;
            }
        }

        private readonly struct HostTestIds
        {
            private HostTestIds(
                LayerId layerId,
                StateId stateId,
                StateDescriptorId stateDescriptorId,
                IntentId intentId,
                EventTypeId eventTypeId,
                EventDomainId eventDomainId,
                OperationId operationSectionId,
                StateBlockId graphStateBlockId,
                StateSlotId graphStateSlotId)
            {
                LayerId = layerId;
                StateId = stateId;
                StateDescriptorId = stateDescriptorId;
                IntentId = intentId;
                EventTypeId = eventTypeId;
                EventDomainId = eventDomainId;
                OperationId = operationSectionId;
                GraphStateBlockId = graphStateBlockId;
                GraphStateSlotId = graphStateSlotId;
            }

            public LayerId LayerId { get; }
            public StateId StateId { get; }
            public StateDescriptorId StateDescriptorId { get; }
            public IntentId IntentId { get; }
            public EventTypeId EventTypeId { get; }
            public EventDomainId EventDomainId { get; }
            public OperationId OperationId { get; }
            public StateBlockId GraphStateBlockId { get; }
            public StateSlotId GraphStateSlotId { get; }

            public static HostTestIds Create()
            {
                Require(LayerId.TryCreate(101UL, 1UL, out LayerId layerId));
                Require(StateId.TryCreate(102UL, 1UL, out StateId stateId));
                Require(StateDescriptorId.TryCreate(
                    103UL,
                    1UL,
                    out StateDescriptorId descriptorId));
                Require(IntentId.TryCreate(104UL, 1UL, out IntentId intentId));
                Require(EventTypeId.TryCreate(105UL, 1UL, out EventTypeId eventTypeId));
                Require(EventDomainId.TryCreate(106UL, out EventDomainId domainId));
                Require(OperationId.TryCreate(
                    107UL,
                    1UL,
                    out OperationId operationSectionId));
                Require(StateBlockId.TryCreate(
                    108UL,
                    1UL,
                    out StateBlockId graphStateBlockId));
                Require(StateSlotId.TryCreate(
                    109UL,
                    1UL,
                    out StateSlotId graphStateSlotId));
                return new HostTestIds(
                    layerId,
                    stateId,
                    descriptorId,
                    intentId,
                    eventTypeId,
                    domainId,
                    operationSectionId,
                    graphStateBlockId,
                    graphStateSlotId);
            }
        }

        private const ulong GraphStateDefaultFingerprint = 993UL;

        private static bool TryRegisterGraphState(
            GraphDescriptorCatalogBuilder builder,
            HostTestIds ids,
            out CoCoDiagnostic diagnostic)
        {
            GraphStateRecord<int> defaultState = CreateDefaultGraphState(ids);
            return builder.TryRegisterStateBlock(
                       ids.GraphStateBlockId,
                       StateBlockOwner.Graph,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.GraphStateSlotId,
                       ContextProjection.Temporal,
                       ContextRestorePolicy.Stored,
                       defaultState,
                       GraphStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic);
        }

        private static bool TryBindGraphState(
            HostBindingBuilder builder,
            HostTestIds ids,
            out CoCoDiagnostic diagnostic) =>
            builder.TryBindGraphStateSlot<
                HostTestMemory,
                int,
                OperatorCommitHostMemoryBinding>(
                ids.LayerId,
                ids.StateId,
                ids.GraphStateBlockId,
                ids.GraphStateSlotId,
                CreateDefaultGraphState(ids),
                GraphStateDefaultFingerprint,
                new OperatorCommitHostMemoryBinding(),
                out diagnostic);

        private static GraphStateRecord<int> CreateDefaultGraphState(HostTestIds ids)
        {
            if (!ActivationId.TryCreate(1UL, out ActivationId activationId) ||
                !GraphStateRecord<int>.TryCreate(
                    ids.LayerId,
                    ids.StateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    0,
                    out GraphStateRecord<int> state))
            {
                throw new InvalidOperationException(
                    "Host test Graph State default is invalid.");
            }

            return state;
        }
    }
}
