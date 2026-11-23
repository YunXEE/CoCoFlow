using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphRuntimeSafetyTests
    {
        [Test]
        public void CreatedRuntimeRejectsStopAndPreservesCreatedState()
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());

            Assert.IsFalse(setup.Runtime.TryStop(out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsError);
            Assert.AreEqual(CoCoDiagnosticDomain.Lifecycle, diagnostic.Domain);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                diagnostic.Code);
            Assert.AreEqual(RuntimeLifecycleState.Created, setup.Runtime.Lifecycle);
            Assert.IsFalse(setup.Runtime.IsFaulted);
        }

        [TestCase(RuntimeLifecycleState.Running)]
        [TestCase(RuntimeLifecycleState.Suspended)]
        public void RunningAndSuspendedRuntimeStopThroughFrozenEdge(
            RuntimeLifecycleState initialState)
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            if (initialState == RuntimeLifecycleState.Suspended)
            {
                Assert.IsTrue(
                    setup.Runtime.TrySuspend(out CoCoDiagnostic suspend),
                    suspend.Message);
            }

            Assert.IsTrue(setup.Runtime.TryStop(out CoCoDiagnostic stop), stop.Message);

            Assert.AreEqual(RuntimeLifecycleState.Stopped, setup.Runtime.Lifecycle);
            Assert.IsFalse(setup.Runtime.IsFaulted);
        }

        [TestCase(RuntimeLifecycleState.Created)]
        [TestCase(RuntimeLifecycleState.Running)]
        [TestCase(RuntimeLifecycleState.Suspended)]
        [TestCase(RuntimeLifecycleState.Stopped)]
        public void DisposeClosesEveryLiveRuntimeThroughFrozenEdges(
            RuntimeLifecycleState initialState)
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());
            if (initialState != RuntimeLifecycleState.Created)
            {
                Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            }

            if (initialState == RuntimeLifecycleState.Suspended)
            {
                Assert.IsTrue(
                    setup.Runtime.TrySuspend(out CoCoDiagnostic suspend),
                    suspend.Message);
            }
            else if (initialState == RuntimeLifecycleState.Stopped)
            {
                Assert.IsTrue(setup.Runtime.TryStop(out CoCoDiagnostic stop), stop.Message);
            }

            Assert.DoesNotThrow(setup.Runtime.Dispose);

            Assert.AreEqual(RuntimeLifecycleState.Disposed, setup.Runtime.Lifecycle);
            Assert.DoesNotThrow(setup.Runtime.Dispose);
            Assert.AreEqual(RuntimeLifecycleState.Disposed, setup.Runtime.Lifecycle);
        }

        [TestCase(ReentryAction.Suspend)]
        [TestCase(ReentryAction.Stop)]
        [TestCase(ReentryAction.Step)]
        public void StateUpdateCannotReenterRuntimeLifecycleOrStep(ReentryAction action)
        {
            var control = new SafetyControl { Action = action };
            SafetySetup setup = SafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);

            StagedGraphStep staged = setup.Stage();

            Assert.IsFalse(control.ReentrantResult);
            Assert.AreEqual(RuntimeLifecycleState.Running, setup.Runtime.Lifecycle);
            Assert.IsFalse(setup.Runtime.IsFaulted);
            Assert.IsTrue(staged.IsValid);
            Assert.IsTrue(setup.Runtime.TryAcceptStagedStep(staged, out CoCoDiagnostic accept), accept.Message);
            Assert.AreEqual(1UL, setup.Runtime.Clock.Tick.Value);
        }

        [Test]
        public void DisposeRequestedInsideUpdateCancelsCandidateAtSafeBoundary()
        {
            var control = new SafetyControl { Action = ReentryAction.Dispose };
            SafetySetup setup = SafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out _));
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(0.1d, 1d, out TickFrame tick, out _));

            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out StagedGraphStep staged,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsError);
            Assert.IsFalse(staged.IsValid);
            Assert.AreEqual(RuntimeLifecycleState.Disposed, setup.Runtime.Lifecycle);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.IsFalse(setup.Runtime.HasStagedStep);
        }

        [Test]
        public void StoppedRuntimeCannotAcceptPreviouslyStagedTick()
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());
            Assert.IsTrue(setup.Runtime.TryStart(out _));
            StagedGraphStep staged = setup.Stage();

            Assert.IsTrue(setup.Runtime.TryStop(out _));
            Assert.IsFalse(setup.Runtime.TryAcceptStagedStep(staged, out _));
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(RuntimeLifecycleState.Stopped, setup.Runtime.Lifecycle);
        }

        [Test]
        public void RuntimeRejectsOperationFrameAndClockFromAnotherGraphInstance()
        {
            SafetyDefinition definition = SafetyDefinition.Create(false);
            GraphInstanceId expected = Ids.GraphInstance(1001UL);
            GraphInstanceId other = Ids.GraphInstance(1002UL);
            StateGraphLogicBindings bindings = definition.CreateBindings(new SafetyControl());
            OperationFrame wrongFrame = definition.CreateOperationFrame(other);
            ActorClock expectedClock = definition.CreateClock(expected);

            Assert.IsFalse(StateGraphRuntime.TryCreate(
                definition.Graph,
                expected,
                bindings,
                wrongFrame,
                expectedClock,
                out _,
                out CoCoDiagnostic frameDiagnostic));
            Assert.IsTrue(frameDiagnostic.IsError);

            OperationFrame expectedFrame = definition.CreateOperationFrame(expected);
            ActorClock wrongClock = definition.CreateClock(other);
            Assert.IsFalse(StateGraphRuntime.TryCreate(
                definition.Graph,
                expected,
                bindings,
                expectedFrame,
                wrongClock,
                out _,
                out CoCoDiagnostic clockDiagnostic));
            Assert.IsTrue(clockDiagnostic.IsError);
        }

        [Test]
        public void RuntimeClaimsClockAndOperationFrameExactlyOnce()
        {
            SafetyDefinition definition = SafetyDefinition.Create(false);
            GraphInstanceId instanceId = Ids.GraphInstance(1101UL);
            StateGraphLogicBindings bindings = definition.CreateBindings(new SafetyControl());
            ActorClock sharedClock = definition.CreateClock(instanceId);
            OperationFrame sharedFrame = definition.CreateOperationFrame(instanceId);
            Assert.IsTrue(StateGraphRuntime.TryCreate(
                definition.Graph,
                instanceId,
                bindings,
                sharedFrame,
                sharedClock,
                out StateGraphRuntime first,
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);

            Assert.IsFalse(sharedClock.TryPreviewNext(0.1d, 1d, out _, out _));
            Assert.IsFalse(sharedFrame.TryBegin(Ids.Tick(1UL), out _));
            Assert.IsFalse(StateGraphRuntime.TryCreate(
                definition.Graph,
                instanceId,
                bindings,
                definition.CreateOperationFrame(instanceId),
                sharedClock,
                out _,
                out _));
            Assert.IsFalse(StateGraphRuntime.TryCreate(
                definition.Graph,
                instanceId,
                bindings,
                sharedFrame,
                definition.CreateClock(instanceId),
                out _,
                out _));
            first.Dispose();
        }

        [Test]
        public void RuntimeRejectsAliasedMemoryBanks()
        {
            SafetyDefinition definition = SafetyDefinition.Create(false);
            var singleton = new RuntimeSafetyFixtureMemory();
            StateGraphLogicBindings bindings = definition.CreateBindings(
                new SafetyControl(),
                memoryFactory: () => singleton);

            Assert.IsFalse(definition.TryCreateRuntime(
                Ids.GraphInstance(1201UL),
                bindings,
                out _,
                out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void RuntimeRejectsLogicMemoryAndConditionSingletonsAcrossInstances()
        {
            AssertCrossRuntimeSingletonRejected(SingletonKind.Logic);
            AssertCrossRuntimeSingletonRejected(SingletonKind.Memory);
            AssertCrossRuntimeSingletonRejected(SingletonKind.Condition);
        }

        [Test]
        public void StagedMemoryMutationIsRejectedAndFaultedBeforeCommit()
        {
            var control = new SafetyControl();
            SafetySetup setup = SafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out _));
            StagedGraphStep staged = setup.Stage();
            Assert.NotNull(control.RetainedMemory);

            control.RetainedMemory.Value += 100;

            Assert.IsFalse(setup.Runtime.TryAcceptStagedStep(
                staged,
                out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.IsFalse(setup.Runtime.HasStagedStep);
        }

        [Test]
        public void OperationReadinessFailureFaultsBeforeGraphOrClockAuthorityChanges()
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            StagedGraphStep staged = setup.Stage();
            Assert.IsTrue(staged.FinalizedOperationFrame.Cancel());

            Assert.IsFalse(setup.Runtime.TryPrepareStagedCommit(
                staged,
                null,
                out PreparedGraphCommit prepared,
                out CoCoDiagnostic diagnostic));

            Assert.IsFalse(prepared.IsValid);
            Assert.IsTrue(diagnostic.IsError);
            Assert.AreEqual(
                CoCoDiagnosticCode.CommitPreparationFailed,
                diagnostic.Code);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.IsFalse(setup.Runtime.HasStagedStep);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(Ids.Root, setup.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(
                0,
                ReadCommittedSafetyMemory(setup.Runtime, 0, Ids.Root).Value);
        }

        [Test]
        public void ClockReadinessFailureFaultsBeforeOperationOrGraphAuthorityChanges()
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            StagedGraphStep staged = setup.Stage();
            FieldInfo ownerField = typeof(StateGraphRuntime).GetField(
                "_transitionHandleOwner",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(ownerField);
            object owner = ownerField.GetValue(setup.Runtime);
            Assert.IsTrue(setup.Runtime.Clock.Cancel(owner, staged.TickFrame));

            Assert.IsFalse(setup.Runtime.TryPrepareStagedCommit(
                staged,
                null,
                out PreparedGraphCommit prepared,
                out CoCoDiagnostic diagnostic));

            Assert.IsFalse(prepared.IsValid);
            Assert.IsTrue(diagnostic.IsError);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                diagnostic.Code);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.IsFalse(setup.Runtime.HasStagedStep);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(Ids.Root, setup.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(
                0,
                ReadCommittedSafetyMemory(setup.Runtime, 0, Ids.Root).Value);
            Assert.IsFalse(staged.OperationFrame.IsValid);
        }

        [Test]
        public void CommittedMemoryMutationIsDetectedBeforeNextCallback()
        {
            var control = new SafetyControl();
            SafetySetup setup = SafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out _));
            StagedGraphStep first = setup.Stage();
            Assert.IsTrue(setup.Runtime.TryAcceptStagedStep(first, out _));
            Assert.AreEqual(1, control.UpdateCalls);

            control.RetainedMemory.Value += 100;
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(0.1d, 1d, out TickFrame tick, out _));
            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out _,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsError);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.AreEqual(1, control.UpdateCalls, "No callback may observe corrupted committed Memory.");
            Assert.AreEqual(1UL, setup.Runtime.Clock.Tick.Value);
        }

        [Test]
        public void UndeclaredOperationWriteFaultsAndRollsBackTickPathAndMemory()
        {
            var control = new OperationSafetyControl
            {
                UnauthorizedWriteState = Ids.LeafA
            };
            OperationSafetySetup setup = OperationSafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            Assert.AreEqual(Ids.LeafA, setup.Runtime.GetActivePath(0).ActiveLeaf);

            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(
                0.1d,
                1d,
                out TickFrame tick,
                out CoCoDiagnostic preview), preview.Message);
            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out StagedGraphStep staged,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(control.UnauthorizedWriteAttempted);
            Assert.IsTrue(diagnostic.IsError);
            Assert.IsFalse(staged.IsValid);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(Ids.LeafA, setup.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(
                0,
                ReadCommittedSafetyMemory(setup.Runtime, 0, Ids.Root).Value);
            Assert.AreEqual(
                0,
                ReadCommittedSafetyMemory(setup.Runtime, 0, Ids.LeafA).Value);
        }

        [Test]
        public void UndeclaredDiscreteOperationEnableFaultsAndRollsBackTick()
        {
            var control = new OperationSafetyControl
            {
                UnauthorizedWriteState = Ids.LeafA,
                UnauthorizedEnableDiscrete = true
            };
            OperationSafetySetup setup = OperationSafetySetup.Create(
                false,
                control,
                OperationSectionMode.Discrete);
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(
                0.1d,
                1d,
                out TickFrame tick,
                out CoCoDiagnostic preview), preview.Message);

            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out _,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(control.UnauthorizedWriteAttempted);
            Assert.IsTrue(diagnostic.IsError);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(Ids.LeafA, setup.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(
                0,
                ReadCommittedSafetyMemory(setup.Runtime, 0, Ids.LeafA).Value);
        }

        [Test]
        public void EscapedOperationWriterExpiresAndFaultsTheLaterCallback()
        {
            var control = new OperationSafetyControl
            {
                CaptureWriterState = Ids.Root,
                UseEscapedWriterState = Ids.LeafA
            };
            OperationSafetySetup setup = OperationSafetySetup.Create(true, control);
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(
                0.1d,
                1d,
                out TickFrame tick,
                out CoCoDiagnostic preview), preview.Message);

            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out StagedGraphStep staged,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(control.CapturedWriterWasValid);
            Assert.IsFalse(control.EscapedWriterWasValidAtUse);
            Assert.IsFalse(control.EscapedWriteResult);
            Assert.IsTrue(control.CurrentWriterWriteResult);
            Assert.IsFalse(control.EscapedWriter.IsValid);
            Assert.IsFalse(control.EscapedWriter.Write(control.OperationField, 999));
            Assert.IsTrue(diagnostic.IsError);
            Assert.IsFalse(staged.IsValid);
            Assert.IsTrue(setup.Runtime.IsFaulted);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(Ids.LeafA, setup.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(
                0,
                ReadCommittedSafetyMemory(setup.Runtime, 0, Ids.LeafA).Value);
        }

        private static void AssertCrossRuntimeSingletonRejected(SingletonKind kind)
        {
            bool withCondition = kind == SingletonKind.Condition;
            SafetyDefinition definition = SafetyDefinition.Create(withCondition);
            var control = new SafetyControl();
            var singletonLogic = new RuntimeSafetyFixtureLogic(control);
            var memory0 = new RuntimeSafetyFixtureMemory();
            var memory1 = new RuntimeSafetyFixtureMemory();
            var singletonCondition = new RuntimeSafetyFixtureCondition();
            int memoryIndex = 0;
            StateGraphLogicBindings bindings = definition.CreateBindings(
                control,
                logicFactory: kind == SingletonKind.Logic
                    ? new Func<StateFactoryContext, RuntimeSafetyFixtureLogic>(_ => singletonLogic)
                    : null,
                memoryFactory: kind == SingletonKind.Memory
                    ? new Func<RuntimeSafetyFixtureMemory>(
                        () => (memoryIndex++ & 1) == 0 ? memory0 : memory1)
                    : null,
                conditionFactory: kind == SingletonKind.Condition
                    ? new Func<ConditionFactoryContext, RuntimeSafetyFixtureCondition>(
                        _ => singletonCondition)
                    : null);

            Assert.IsTrue(definition.TryCreateRuntime(
                Ids.GraphInstance((ulong)kind * 10UL + 1UL),
                bindings,
                out StateGraphRuntime first,
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);
            Assert.IsFalse(definition.TryCreateRuntime(
                Ids.GraphInstance((ulong)kind * 10UL + 2UL),
                bindings,
                out _,
                out CoCoDiagnostic secondDiagnostic));
            Assert.IsTrue(secondDiagnostic.IsError);
            first.Dispose();
        }

        private static RuntimeSafetyFixtureMemory ReadCommittedSafetyMemory(
            StateGraphRuntime runtime,
            int layerIndex,
            StateId stateId)
        {
            FieldInfo layersField = typeof(StateGraphRuntime).GetField(
                "_layers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(layersField);
            var layers = (Array)layersField.GetValue(runtime);
            object layer = layers.GetValue(layerIndex);
            PropertyInfo statesProperty = layer.GetType().GetProperty(
                "States",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(statesProperty);
            var states = (Array)statesProperty.GetValue(layer);
            for (int stateIndex = 0;
                 stateIndex < runtime.Graph.Layers[layerIndex].States.Count;
                 stateIndex++)
            {
                if (runtime.Graph.Layers[layerIndex].States[stateIndex].StateId != stateId)
                {
                    continue;
                }

                object state = states.GetValue(stateIndex);
                PropertyInfo memoryProperty = state.GetType().GetProperty(
                    "CommittedMemory",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.IsNotNull(memoryProperty);
                return (RuntimeSafetyFixtureMemory)memoryProperty.GetValue(state);
            }

            Assert.Fail("The requested State was not found in the Runtime layer.");
            return null;
        }

        public enum ReentryAction
        {
            None = 0,
            Suspend = 1,
            Stop = 2,
            Step = 3,
            Dispose = 4
        }

        private enum SingletonKind : ulong
        {
            Logic = 1UL,
            Memory = 2UL,
            Condition = 3UL
        }

        private sealed class SafetyControl : IRuntimeSafetyFixtureObserver
        {
            public ReentryAction Action;
            public StateGraphRuntime Runtime;
            public RuntimeSafetyFixtureMemory RetainedMemory;
            public bool ReentrantResult;
            public int UpdateCalls;

            public void OnUpdate(
                StateExecutionContext context,
                RuntimeSafetyFixtureMemory memory)
            {
                UpdateCalls++;
                RetainedMemory = memory;
                switch (Action)
                {
                    case ReentryAction.Suspend:
                        ReentrantResult = Runtime.TrySuspend(out _);
                        break;
                    case ReentryAction.Stop:
                        ReentrantResult = Runtime.TryStop(out _);
                        break;
                    case ReentryAction.Step:
                        ReentrantResult = Runtime.TryStageStep(
                            context.TickFrame,
                            null,
                            default,
                            out _,
                            out _);
                        break;
                    case ReentryAction.Dispose:
                        Runtime.Dispose();
                        break;
                }
            }
        }

        private sealed class OperationSafetyControl : IRuntimeSafetyOperationFixtureObserver
        {
            public StateId UnauthorizedWriteState;
            public StateId CaptureWriterState;
            public StateId UseEscapedWriterState;
            public OperationSectionField<int> OperationField;
            public OperationSectionHandle<IRuntimeSafetyOperationSection> OperationHandle;
            public StateOperationWriter EscapedWriter;
            public bool UnauthorizedEnableDiscrete;
            public bool UnauthorizedWriteAttempted;
            public bool CapturedWriterWasValid;
            public bool EscapedWriterWasValidAtUse;
            public bool EscapedWriteResult;
            public bool CurrentWriterWriteResult;

            public void OnUpdate(
                StateId stateId,
                StateExecutionContext context,
                RuntimeSafetyFixtureMemory memory)
            {
                if (stateId == UnauthorizedWriteState)
                {
                    UnauthorizedWriteAttempted = true;
                    if (UnauthorizedEnableDiscrete)
                    {
                        context.Operations.EnableDiscrete(OperationHandle);
                    }
                    else
                    {
                        context.Operations.Write(OperationField, 100);
                    }
                }

                if (stateId == CaptureWriterState)
                {
                    EscapedWriter = context.Operations;
                    CapturedWriterWasValid = EscapedWriter.IsValid;
                    context.Operations.Write(OperationField, 10);
                }

                if (stateId == UseEscapedWriterState)
                {
                    EscapedWriterWasValidAtUse = EscapedWriter.IsValid;
                    EscapedWriteResult = EscapedWriter.Write(OperationField, 999);
                    CurrentWriterWriteResult = context.Operations.Write(OperationField, 20);
                }
            }
        }

        private sealed class OperationSafetySetup
        {
            private OperationSafetySetup(
                StateGraphRuntime runtime,
                OperationSectionHandle<IRuntimeSafetyOperationSection> operationHandle)
            {
                Runtime = runtime;
                OperationHandle = operationHandle;
            }

            public StateGraphRuntime Runtime { get; }
            public OperationSectionHandle<IRuntimeSafetyOperationSection> OperationHandle { get; }

            public static OperationSafetySetup Create(
                bool leafDeclaresOperation,
                OperationSafetyControl control,
                OperationSectionMode operationMode =
                    OperationSectionMode.Continuous)
            {
                GraphDescriptorCatalog catalog = CreateOperationCatalog(operationMode);
                StateGraphCompileResult result = new StateGraphCompiler().Compile(
                    CreateOperationSource(leafDeclaresOperation),
                    catalog);
                Assert.IsTrue(result.Succeeded, "Operation safety graph must compile.");
                CompiledStateGraph graph = result.Graph;

                var registryBuilder = new OperationSectionRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryRegister(
                    Ids.OperationSection,
                    operationMode,
                    new RuntimeSafetyOperationSectionViewFactory(),
                    out OperationSectionRequirement operationRequirement,
                    out CoCoDiagnostic registerDiagnostic), registerDiagnostic.Message);
                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out OperationSectionRegistry registry,
                    out CoCoDiagnostic registryDiagnostic), registryDiagnostic.Message);
                Assert.IsTrue(registry.TryResolve(
                    operationRequirement,
                    out OperationSectionHandle<IRuntimeSafetyOperationSection> operationHandle));
                Assert.IsTrue(registry.TryResolveField(
                    operationHandle,
                    0,
                    out OperationSectionField<int> operationField));
                control.OperationField = operationField;
                control.OperationHandle = operationHandle;

                GraphInstanceId graphInstanceId = Ids.GraphInstance(
                    leafDeclaresOperation ? 9302UL : 9301UL);
                Assert.IsTrue(OperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    new[] { operationRequirement },
                    out OperationFrame operationFrame,
                    out CoCoDiagnostic frameDiagnostic), frameDiagnostic.Message);
                Assert.IsTrue(ActorClock.TryCreate(
                    Ids.Timeline,
                    Ids.ClockDomain,
                    new TimelineEpoch(1UL),
                    graphInstanceId,
                    out ActorClock clock,
                    out CoCoDiagnostic clockDiagnostic), clockDiagnostic.Message);

                var bindingsBuilder = new StateGraphLogicBindingsBuilder(graph);
                BindOperationState(
                    bindingsBuilder,
                    Ids.OperationProviderDescriptor,
                    control);
                if (!leafDeclaresOperation)
                {
                    BindOperationState(
                        bindingsBuilder,
                        Ids.OperationConsumerDescriptor,
                        control);
                }

                Assert.IsTrue(bindingsBuilder.TryFreeze(
                    out StateGraphLogicBindings bindings,
                    out CoCoDiagnostic bindingsDiagnostic), bindingsDiagnostic.Message);
                Assert.IsTrue(StateGraphRuntime.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out StateGraphRuntime runtime,
                    out CoCoDiagnostic runtimeDiagnostic), runtimeDiagnostic.Message);
                return new OperationSafetySetup(runtime, operationHandle);
            }

            public StagedGraphStep Stage()
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    0.1d,
                    1d,
                    out TickFrame tick,
                    out CoCoDiagnostic preview), preview.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out StagedGraphStep staged,
                    out CoCoDiagnostic stage), stage.Message);
                return staged;
            }

            private static GraphDescriptorCatalog CreateOperationCatalog(
                OperationSectionMode operationMode)
            {
                var builder = new GraphDescriptorCatalogBuilder();
                Assert.IsTrue(builder.TryRegisterOperationSection(
                    Ids.OperationSection,
                    operationMode,
                    new OperationSectionViewFactoryToken<
                        IRuntimeSafetyOperationSection,
                        RuntimeSafetyOperationSectionViewFactory>(0x5101UL),
                    out CoCoDiagnostic operationDiagnostic), operationDiagnostic.Message);
                RegisterOperationState(
                    builder,
                    Ids.OperationProviderDescriptor,
                    new[] { Ids.OperationSection });
                RegisterOperationState(
                    builder,
                    Ids.OperationConsumerDescriptor,
                    null);
                Assert.IsTrue(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return catalog;
            }

            private static void RegisterOperationState(
                GraphDescriptorCatalogBuilder builder,
                StateDescriptorId descriptorId,
                OperationSectionId[] operationProvides)
            {
                Assert.IsTrue(builder.TryRegisterState(
                    descriptorId,
                    1U,
                    new RuntimeFixtureStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        RuntimeSafetyOperationFixtureLogic,
                        RuntimeFixtureStateConfigSchema,
                        RuntimeSafetyFixtureMemory>(RuntimeFixtureSchemas.State, false),
                    null,
                    operationProvides,
                    null,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            private static void BindOperationState(
                StateGraphLogicBindingsBuilder builder,
                StateDescriptorId descriptorId,
                OperationSafetyControl control)
            {
                Assert.IsTrue(builder.TryBindState(
                    descriptorId,
                    new StateRuntimeFactory<
                        RuntimeSafetyOperationFixtureLogic,
                        RuntimeSafetyFixtureMemory>(
                        context => new RuntimeSafetyOperationFixtureLogic(context, control),
                        () => new RuntimeSafetyFixtureMemory(),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        memory => unchecked((ulong)(uint)memory.Value)),
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            private static StateGraphSource CreateOperationSource(bool leafDeclaresOperation)
            {
                return new StateGraphSource(
                    StateGraphCompiler.CurrentSchemaVersion,
                    leafDeclaresOperation ? 804UL : 803UL,
                    Ids.Graph,
                    new[]
                    {
                        new StateLayerSource(
                            Ids.Layer,
                            Ids.Root,
                            new[]
                            {
                                OperationState(
                                    Ids.Root,
                                    default,
                                    Ids.LeafA,
                                    Ids.OperationProviderDescriptor),
                                OperationState(
                                    Ids.LeafA,
                                    Ids.Root,
                                    default,
                                    leafDeclaresOperation
                                        ? Ids.OperationProviderDescriptor
                                        : Ids.OperationConsumerDescriptor)
                            },
                            Array.Empty<TransitionSource>())
                    },
                    Array.Empty<EventToIntentDeclarationSource>());
            }

            private static StateSource OperationState(
                StateId stateId,
                StateId parent,
                StateId initialChild,
                StateDescriptorId descriptorId)
            {
                return new StateSource(
                    stateId,
                    parent,
                    initialChild,
                    descriptorId,
                    OperationStateConfig());
            }

            private static FrozenConfigSnapshot OperationStateConfig()
            {
                FrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer =
                    RuntimeFixtureSchemas.State.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.StateValue, 0, out _));
                Assert.IsTrue(writer.TrySeal(out FrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }
        }

        private sealed class SafetySetup
        {
            private SafetySetup(StateGraphRuntime runtime)
            {
                Runtime = runtime;
            }

            public StateGraphRuntime Runtime { get; }

            public static SafetySetup Create(bool withCondition, SafetyControl control)
            {
                SafetyDefinition definition = SafetyDefinition.Create(withCondition);
                StateGraphLogicBindings bindings = definition.CreateBindings(control);
                Assert.IsTrue(definition.TryCreateRuntime(
                    Ids.GraphInstance(9001UL + (withCondition ? 1UL : 0UL)),
                    bindings,
                    out StateGraphRuntime runtime,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                control.Runtime = runtime;
                return new SafetySetup(runtime);
            }

            public StagedGraphStep Stage()
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    0.1d,
                    1d,
                    out TickFrame tick,
                    out CoCoDiagnostic preview), preview.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out StagedGraphStep staged,
                    out CoCoDiagnostic stage), stage.Message);
                return staged;
            }
        }

        private sealed class SafetyDefinition
        {
            private SafetyDefinition(CompiledStateGraph graph, bool withCondition)
            {
                Graph = graph;
                WithCondition = withCondition;
            }

            public CompiledStateGraph Graph { get; }
            public bool WithCondition { get; }

            public static SafetyDefinition Create(bool withCondition)
            {
                GraphDescriptorCatalog catalog = CreateCatalog(withCondition);
                StateGraphSource source = CreateSource(withCondition);
                StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);
                Assert.IsTrue(result.Succeeded, "Safety graph must compile.");
                return new SafetyDefinition(result.Graph, withCondition);
            }

            public StateGraphLogicBindings CreateBindings(
                SafetyControl control,
                Func<StateFactoryContext, RuntimeSafetyFixtureLogic> logicFactory = null,
                Func<RuntimeSafetyFixtureMemory> memoryFactory = null,
                Func<ConditionFactoryContext, RuntimeSafetyFixtureCondition> conditionFactory = null)
            {
                var builder = new StateGraphLogicBindingsBuilder(Graph);
                Assert.IsTrue(builder.TryBindState(
                    Ids.StateDescriptor,
                    new StateRuntimeFactory<
                        RuntimeSafetyFixtureLogic,
                        RuntimeSafetyFixtureMemory>(
                        logicFactory ?? (_ => new RuntimeSafetyFixtureLogic(control)),
                        memoryFactory ?? (() => new RuntimeSafetyFixtureMemory()),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        memory => unchecked((ulong)(uint)memory.Value)),
                    out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
                if (WithCondition)
                {
                    Assert.IsTrue(builder.TryBindCondition(
                        Ids.ConditionDescriptor,
                        new ConditionRuntimeFactory<RuntimeSafetyFixtureCondition>(
                            conditionFactory ?? (_ => new RuntimeSafetyFixtureCondition())),
                        out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
                }

                Assert.IsTrue(builder.TryFreeze(
                    out StateGraphLogicBindings bindings,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return bindings;
            }

            public bool TryCreateRuntime(
                GraphInstanceId graphInstanceId,
                StateGraphLogicBindings bindings,
                out StateGraphRuntime runtime,
                out CoCoDiagnostic diagnostic) =>
                StateGraphRuntime.TryCreate(
                    Graph,
                    graphInstanceId,
                    bindings,
                    CreateOperationFrame(graphInstanceId),
                    CreateClock(graphInstanceId),
                    out runtime,
                    out diagnostic);

            public OperationFrame CreateOperationFrame(GraphInstanceId graphInstanceId)
            {
                var registryBuilder = new OperationSectionRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryFreeze(
                    Graph.OperationProvides.LayoutId,
                    out OperationSectionRegistry registry,
                    out CoCoDiagnostic registryDiagnostic), registryDiagnostic.Message);
                Assert.IsTrue(OperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    Array.Empty<OperationSectionRequirement>(),
                    out OperationFrame frame,
                    out CoCoDiagnostic frameDiagnostic), frameDiagnostic.Message);
                return frame;
            }

            public ActorClock CreateClock(GraphInstanceId graphInstanceId)
            {
                Assert.IsTrue(ActorClock.TryCreate(
                    Ids.Timeline,
                    Ids.ClockDomain,
                    new TimelineEpoch(1UL),
                    graphInstanceId,
                    out ActorClock clock,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                return clock;
            }

            private static GraphDescriptorCatalog CreateCatalog(bool withCondition)
            {
                var builder = new GraphDescriptorCatalogBuilder();
                Assert.IsTrue(builder.TryRegisterState(
                    Ids.StateDescriptor,
                    1U,
                    new RuntimeFixtureStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        RuntimeSafetyFixtureLogic,
                        RuntimeFixtureStateConfigSchema,
                        RuntimeSafetyFixtureMemory>(RuntimeFixtureSchemas.State, false),
                    null,
                    null,
                    null,
                    out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
                if (withCondition)
                {
                    Assert.IsTrue(builder.TryRegisterCondition(
                        Ids.ConditionDescriptor,
                        1U,
                        new RuntimeFixtureConditionConfigFreezer(),
                        new ConditionRuntimeRegistration<
                            RuntimeSafetyFixtureCondition,
                            RuntimeFixtureConditionConfigSchema>(RuntimeFixtureSchemas.Condition),
                        null,
                        null,
                        out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
                }

                Assert.IsTrue(builder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return catalog;
            }

            private static StateGraphSource CreateSource(bool withCondition)
            {
                StateSource root = State(Ids.Root, default, withCondition ? Ids.LeafA : default);
                StateSource[] states;
                TransitionSource[] transitions;
                if (withCondition)
                {
                    states = new[]
                    {
                        root,
                        State(Ids.LeafA, Ids.Root, default),
                        State(Ids.LeafB, Ids.Root, default)
                    };
                    transitions = new[]
                    {
                        new TransitionSource(
                            Ids.Transition,
                            Ids.LeafA,
                            Ids.LeafB,
                            1,
                            TransitionWindow.Always,
                            new[]
                            {
                                new ConditionSource(
                                    Ids.ConditionDescriptor,
                                    ConditionConfig())
                            })
                    };
                }
                else
                {
                    states = new[] { root };
                    transitions = Array.Empty<TransitionSource>();
                }

                return new StateGraphSource(
                    StateGraphCompiler.CurrentSchemaVersion,
                    withCondition ? 802UL : 801UL,
                    Ids.Graph,
                    new[]
                    {
                        new StateLayerSource(Ids.Layer, Ids.Root, states, transitions)
                    },
                    Array.Empty<EventToIntentDeclarationSource>());
            }

            private static StateSource State(
                StateId stateId,
                StateId parent,
                StateId initialChild) =>
                new StateSource(
                    stateId,
                    parent,
                    initialChild,
                    Ids.StateDescriptor,
                    StateConfig());

            private static FrozenConfigSnapshot StateConfig()
            {
                FrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer =
                    RuntimeFixtureSchemas.State.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.StateValue, 0, out _));
                Assert.IsTrue(writer.TrySeal(out FrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }

            private static FrozenConfigSnapshot ConditionConfig()
            {
                FrozenConfigWriter<RuntimeFixtureConditionConfigSchema> writer =
                    RuntimeFixtureSchemas.Condition.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.ConditionValue, 0, out _));
                Assert.IsTrue(writer.TrySeal(out FrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }
        }

        private static class Ids
        {
            static Ids()
            {
                GraphId.TryCreate(0x51UL, 1UL, out Graph);
                LayerId.TryCreate(0x51UL, 2UL, out Layer);
                StateId.TryCreate(0x51UL, 3UL, out Root);
                StateId.TryCreate(0x51UL, 4UL, out LeafA);
                StateId.TryCreate(0x51UL, 5UL, out LeafB);
                TransitionId.TryCreate(0x51UL, 6UL, out Transition);
                StateDescriptorId.TryCreate(0x51UL, 7UL, out StateDescriptor);
                ConditionDescriptorId.TryCreate(0x51UL, 8UL, out ConditionDescriptor);
                TimelineId.TryCreate(0x51UL, 9UL, out Timeline);
                ClockDomainId.TryCreate(10UL, out ClockDomain);
                StateDescriptorId.TryCreate(
                    0x51UL,
                    11UL,
                    out OperationProviderDescriptor);
                StateDescriptorId.TryCreate(
                    0x51UL,
                    12UL,
                    out OperationConsumerDescriptor);
                OperationSectionId.TryCreate(
                    0x51UL,
                    13UL,
                    out OperationSection);
            }

            public static readonly GraphId Graph;
            public static readonly LayerId Layer;
            public static readonly StateId Root;
            public static readonly StateId LeafA;
            public static readonly StateId LeafB;
            public static readonly TransitionId Transition;
            public static readonly StateDescriptorId StateDescriptor;
            public static readonly ConditionDescriptorId ConditionDescriptor;
            public static readonly TimelineId Timeline;
            public static readonly ClockDomainId ClockDomain;
            public static readonly StateDescriptorId OperationProviderDescriptor;
            public static readonly StateDescriptorId OperationConsumerDescriptor;
            public static readonly OperationSectionId OperationSection;

            public static GraphInstanceId GraphInstance(ulong value)
            {
                GraphInstanceId.TryCreate(value, out GraphInstanceId id);
                return id;
            }

            public static TickFrame Tick(ulong value)
            {
                TimelinePosition.TryCreate(value, out TimelinePosition position);
                TickFrame.TryCreate(
                    1d,
                    Timeline,
                    position,
                    new TimelineTick(value),
                    ClockDomain,
                    new ExecutionSequence(value),
                    new TimelineEpoch(1UL),
                    out TickFrame tick,
                    out _);
                return tick;
            }
        }
    }
}
