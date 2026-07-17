using System;
using System.Reflection;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphRuntimeSafetyTests
    {
        [TestCase(ReentryAction.Suspend)]
        [TestCase(ReentryAction.Stop)]
        [TestCase(ReentryAction.Step)]
        public void StateUpdateCannotReenterRuntimeLifecycleOrStep(ReentryAction action)
        {
            var control = new SafetyControl { Action = action };
            SafetySetup setup = SafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);

            CoCoStagedGraphStep staged = setup.Stage();

            Assert.IsFalse(control.ReentrantResult);
            Assert.AreEqual(CoCoRuntimeLifecycleState.Running, setup.Runtime.Lifecycle);
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
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(0.1d, 1d, out CoCoTickFrame tick, out _));

            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out CoCoStagedGraphStep staged,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsError);
            Assert.IsFalse(staged.IsValid);
            Assert.AreEqual(CoCoRuntimeLifecycleState.Disposed, setup.Runtime.Lifecycle);
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.IsFalse(setup.Runtime.HasStagedStep);
        }

        [Test]
        public void StoppedRuntimeCannotAcceptPreviouslyStagedTick()
        {
            SafetySetup setup = SafetySetup.Create(false, new SafetyControl());
            Assert.IsTrue(setup.Runtime.TryStart(out _));
            CoCoStagedGraphStep staged = setup.Stage();

            Assert.IsTrue(setup.Runtime.TryStop(out _));
            Assert.IsFalse(setup.Runtime.TryAcceptStagedStep(staged, out _));
            Assert.AreEqual(0UL, setup.Runtime.Clock.Tick.Value);
            Assert.AreEqual(CoCoRuntimeLifecycleState.Stopped, setup.Runtime.Lifecycle);
        }

        [Test]
        public void RuntimeRejectsOperationFrameAndClockFromAnotherGraphInstance()
        {
            SafetyDefinition definition = SafetyDefinition.Create(false);
            CoCoGraphInstanceId expected = Ids.GraphInstance(1001UL);
            CoCoGraphInstanceId other = Ids.GraphInstance(1002UL);
            CoCoStateGraphLogicBindings bindings = definition.CreateBindings(new SafetyControl());
            CoCoOperationFrame wrongFrame = definition.CreateOperationFrame(other);
            CoCoActorClock expectedClock = definition.CreateClock(expected);

            Assert.IsFalse(CoCoStateGraphRuntime.TryCreate(
                definition.Graph,
                expected,
                bindings,
                wrongFrame,
                expectedClock,
                out _,
                out CoCoDiagnostic frameDiagnostic));
            Assert.IsTrue(frameDiagnostic.IsError);

            CoCoOperationFrame expectedFrame = definition.CreateOperationFrame(expected);
            CoCoActorClock wrongClock = definition.CreateClock(other);
            Assert.IsFalse(CoCoStateGraphRuntime.TryCreate(
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
            CoCoGraphInstanceId instanceId = Ids.GraphInstance(1101UL);
            CoCoStateGraphLogicBindings bindings = definition.CreateBindings(new SafetyControl());
            CoCoActorClock sharedClock = definition.CreateClock(instanceId);
            CoCoOperationFrame sharedFrame = definition.CreateOperationFrame(instanceId);
            Assert.IsTrue(CoCoStateGraphRuntime.TryCreate(
                definition.Graph,
                instanceId,
                bindings,
                sharedFrame,
                sharedClock,
                out CoCoStateGraphRuntime first,
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);

            Assert.IsFalse(sharedClock.TryPreviewNext(0.1d, 1d, out _, out _));
            Assert.IsFalse(sharedFrame.TryBegin(Ids.Tick(1UL), out _));
            Assert.IsFalse(CoCoStateGraphRuntime.TryCreate(
                definition.Graph,
                instanceId,
                bindings,
                definition.CreateOperationFrame(instanceId),
                sharedClock,
                out _,
                out _));
            Assert.IsFalse(CoCoStateGraphRuntime.TryCreate(
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
            CoCoStateGraphLogicBindings bindings = definition.CreateBindings(
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
            CoCoStagedGraphStep staged = setup.Stage();
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
        public void CommittedMemoryMutationIsDetectedBeforeNextCallback()
        {
            var control = new SafetyControl();
            SafetySetup setup = SafetySetup.Create(false, control);
            Assert.IsTrue(setup.Runtime.TryStart(out _));
            CoCoStagedGraphStep first = setup.Stage();
            Assert.IsTrue(setup.Runtime.TryAcceptStagedStep(first, out _));
            Assert.AreEqual(1, control.UpdateCalls);

            control.RetainedMemory.Value += 100;
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(0.1d, 1d, out CoCoTickFrame tick, out _));
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
                out CoCoTickFrame tick,
                out CoCoDiagnostic preview), preview.Message);
            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out CoCoStagedGraphStep staged,
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
                CoCoOperationSectionMode.Discrete);
            Assert.IsTrue(setup.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            Assert.IsTrue(setup.Runtime.TryPreviewNextTick(
                0.1d,
                1d,
                out CoCoTickFrame tick,
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
                out CoCoTickFrame tick,
                out CoCoDiagnostic preview), preview.Message);

            Assert.IsFalse(setup.Runtime.TryStageStep(
                tick,
                null,
                default,
                out CoCoStagedGraphStep staged,
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
            CoCoStateGraphLogicBindings bindings = definition.CreateBindings(
                control,
                logicFactory: kind == SingletonKind.Logic
                    ? new Func<CoCoStateFactoryContext, RuntimeSafetyFixtureLogic>(_ => singletonLogic)
                    : null,
                memoryFactory: kind == SingletonKind.Memory
                    ? new Func<RuntimeSafetyFixtureMemory>(
                        () => (memoryIndex++ & 1) == 0 ? memory0 : memory1)
                    : null,
                conditionFactory: kind == SingletonKind.Condition
                    ? new Func<CoCoConditionFactoryContext, RuntimeSafetyFixtureCondition>(
                        _ => singletonCondition)
                    : null);

            Assert.IsTrue(definition.TryCreateRuntime(
                Ids.GraphInstance((ulong)kind * 10UL + 1UL),
                bindings,
                out CoCoStateGraphRuntime first,
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
            CoCoStateGraphRuntime runtime,
            int layerIndex,
            CoCoStateId stateId)
        {
            FieldInfo layersField = typeof(CoCoStateGraphRuntime).GetField(
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
            public CoCoStateGraphRuntime Runtime;
            public RuntimeSafetyFixtureMemory RetainedMemory;
            public bool ReentrantResult;
            public int UpdateCalls;

            public void OnUpdate(
                CoCoStateExecutionContext context,
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
            public CoCoStateId UnauthorizedWriteState;
            public CoCoStateId CaptureWriterState;
            public CoCoStateId UseEscapedWriterState;
            public CoCoOperationSectionField<int> OperationField;
            public CoCoOperationSectionHandle<IRuntimeSafetyOperationSection> OperationHandle;
            public CoCoStateOperationWriter EscapedWriter;
            public bool UnauthorizedEnableDiscrete;
            public bool UnauthorizedWriteAttempted;
            public bool CapturedWriterWasValid;
            public bool EscapedWriterWasValidAtUse;
            public bool EscapedWriteResult;
            public bool CurrentWriterWriteResult;

            public void OnUpdate(
                CoCoStateId stateId,
                CoCoStateExecutionContext context,
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
                CoCoStateGraphRuntime runtime,
                CoCoOperationSectionHandle<IRuntimeSafetyOperationSection> operationHandle)
            {
                Runtime = runtime;
                OperationHandle = operationHandle;
            }

            public CoCoStateGraphRuntime Runtime { get; }
            public CoCoOperationSectionHandle<IRuntimeSafetyOperationSection> OperationHandle { get; }

            public static OperationSafetySetup Create(
                bool leafDeclaresOperation,
                OperationSafetyControl control,
                CoCoOperationSectionMode operationMode =
                    CoCoOperationSectionMode.Continuous)
            {
                CoCoGraphDescriptorCatalog catalog = CreateOperationCatalog(operationMode);
                CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                    CreateOperationSource(leafDeclaresOperation),
                    catalog);
                Assert.IsTrue(result.Succeeded, "Operation safety graph must compile.");
                CoCoCompiledStateGraph graph = result.Graph;

                var registryBuilder = new CoCoOperationSectionRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryRegister(
                    Ids.OperationSection,
                    operationMode,
                    new RuntimeSafetyOperationSectionViewFactory(),
                    out CoCoOperationSectionRequirement operationRequirement,
                    out CoCoDiagnostic registerDiagnostic), registerDiagnostic.Message);
                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out CoCoOperationSectionRegistry registry,
                    out CoCoDiagnostic registryDiagnostic), registryDiagnostic.Message);
                Assert.IsTrue(registry.TryResolve(
                    operationRequirement,
                    out CoCoOperationSectionHandle<IRuntimeSafetyOperationSection> operationHandle));
                Assert.IsTrue(registry.TryResolveField(
                    operationHandle,
                    0,
                    out CoCoOperationSectionField<int> operationField));
                control.OperationField = operationField;
                control.OperationHandle = operationHandle;

                CoCoGraphInstanceId graphInstanceId = Ids.GraphInstance(
                    leafDeclaresOperation ? 9302UL : 9301UL);
                Assert.IsTrue(CoCoOperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    new[] { operationRequirement },
                    out CoCoOperationFrame operationFrame,
                    out CoCoDiagnostic frameDiagnostic), frameDiagnostic.Message);
                Assert.IsTrue(CoCoActorClock.TryCreate(
                    Ids.Timeline,
                    Ids.ClockDomain,
                    new CoCoTimelineEpoch(1UL),
                    graphInstanceId,
                    out CoCoActorClock clock,
                    out CoCoDiagnostic clockDiagnostic), clockDiagnostic.Message);

                var bindingsBuilder = new CoCoStateGraphLogicBindingsBuilder(graph);
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
                    out CoCoStateGraphLogicBindings bindings,
                    out CoCoDiagnostic bindingsDiagnostic), bindingsDiagnostic.Message);
                Assert.IsTrue(CoCoStateGraphRuntime.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out CoCoStateGraphRuntime runtime,
                    out CoCoDiagnostic runtimeDiagnostic), runtimeDiagnostic.Message);
                return new OperationSafetySetup(runtime, operationHandle);
            }

            public CoCoStagedGraphStep Stage()
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    0.1d,
                    1d,
                    out CoCoTickFrame tick,
                    out CoCoDiagnostic preview), preview.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out CoCoStagedGraphStep staged,
                    out CoCoDiagnostic stage), stage.Message);
                return staged;
            }

            private static CoCoGraphDescriptorCatalog CreateOperationCatalog(
                CoCoOperationSectionMode operationMode)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Assert.IsTrue(builder.TryRegisterOperationSection(
                    Ids.OperationSection,
                    operationMode,
                    new CoCoOperationSectionViewFactoryToken<
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
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return catalog;
            }

            private static void RegisterOperationState(
                CoCoGraphDescriptorCatalogBuilder builder,
                CoCoStateDescriptorId descriptorId,
                CoCoOperationSectionId[] operationProvides)
            {
                Assert.IsTrue(builder.TryRegisterState(
                    descriptorId,
                    1U,
                    new RuntimeFixtureStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        RuntimeSafetyOperationFixtureLogic,
                        RuntimeFixtureStateConfigSchema,
                        RuntimeSafetyFixtureMemory>(RuntimeFixtureSchemas.State, false),
                    null,
                    operationProvides,
                    null,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            private static void BindOperationState(
                CoCoStateGraphLogicBindingsBuilder builder,
                CoCoStateDescriptorId descriptorId,
                OperationSafetyControl control)
            {
                Assert.IsTrue(builder.TryBindState(
                    descriptorId,
                    new CoCoStateRuntimeFactory<
                        RuntimeSafetyOperationFixtureLogic,
                        RuntimeSafetyFixtureMemory>(
                        context => new RuntimeSafetyOperationFixtureLogic(context, control),
                        () => new RuntimeSafetyFixtureMemory(),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        memory => unchecked((ulong)(uint)memory.Value)),
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            private static CoCoStateGraphSource CreateOperationSource(bool leafDeclaresOperation)
            {
                return new CoCoStateGraphSource(
                    CoCoStateGraphCompiler.CurrentSchemaVersion,
                    leafDeclaresOperation ? 804UL : 803UL,
                    Ids.Graph,
                    new[]
                    {
                        new CoCoStateLayerSource(
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
                            Array.Empty<CoCoTransitionSource>())
                    },
                    Array.Empty<CoCoEventToIntentDeclarationSource>());
            }

            private static CoCoStateSource OperationState(
                CoCoStateId stateId,
                CoCoStateId parent,
                CoCoStateId initialChild,
                CoCoStateDescriptorId descriptorId)
            {
                return new CoCoStateSource(
                    stateId,
                    parent,
                    initialChild,
                    descriptorId,
                    OperationStateConfig());
            }

            private static CoCoFrozenConfigSnapshot OperationStateConfig()
            {
                CoCoFrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer =
                    RuntimeFixtureSchemas.State.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.StateValue, 0, out _));
                Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }
        }

        private sealed class SafetySetup
        {
            private SafetySetup(CoCoStateGraphRuntime runtime)
            {
                Runtime = runtime;
            }

            public CoCoStateGraphRuntime Runtime { get; }

            public static SafetySetup Create(bool withCondition, SafetyControl control)
            {
                SafetyDefinition definition = SafetyDefinition.Create(withCondition);
                CoCoStateGraphLogicBindings bindings = definition.CreateBindings(control);
                Assert.IsTrue(definition.TryCreateRuntime(
                    Ids.GraphInstance(9001UL + (withCondition ? 1UL : 0UL)),
                    bindings,
                    out CoCoStateGraphRuntime runtime,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                control.Runtime = runtime;
                return new SafetySetup(runtime);
            }

            public CoCoStagedGraphStep Stage()
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    0.1d,
                    1d,
                    out CoCoTickFrame tick,
                    out CoCoDiagnostic preview), preview.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out CoCoStagedGraphStep staged,
                    out CoCoDiagnostic stage), stage.Message);
                return staged;
            }
        }

        private sealed class SafetyDefinition
        {
            private SafetyDefinition(CoCoCompiledStateGraph graph, bool withCondition)
            {
                Graph = graph;
                WithCondition = withCondition;
            }

            public CoCoCompiledStateGraph Graph { get; }
            public bool WithCondition { get; }

            public static SafetyDefinition Create(bool withCondition)
            {
                CoCoGraphDescriptorCatalog catalog = CreateCatalog(withCondition);
                CoCoStateGraphSource source = CreateSource(withCondition);
                CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);
                Assert.IsTrue(result.Succeeded, "Safety graph must compile.");
                return new SafetyDefinition(result.Graph, withCondition);
            }

            public CoCoStateGraphLogicBindings CreateBindings(
                SafetyControl control,
                Func<CoCoStateFactoryContext, RuntimeSafetyFixtureLogic> logicFactory = null,
                Func<RuntimeSafetyFixtureMemory> memoryFactory = null,
                Func<CoCoConditionFactoryContext, RuntimeSafetyFixtureCondition> conditionFactory = null)
            {
                var builder = new CoCoStateGraphLogicBindingsBuilder(Graph);
                Assert.IsTrue(builder.TryBindState(
                    Ids.StateDescriptor,
                    new CoCoStateRuntimeFactory<
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
                        new CoCoConditionRuntimeFactory<RuntimeSafetyFixtureCondition>(
                            conditionFactory ?? (_ => new RuntimeSafetyFixtureCondition())),
                        out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
                }

                Assert.IsTrue(builder.TryFreeze(
                    out CoCoStateGraphLogicBindings bindings,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return bindings;
            }

            public bool TryCreateRuntime(
                CoCoGraphInstanceId graphInstanceId,
                CoCoStateGraphLogicBindings bindings,
                out CoCoStateGraphRuntime runtime,
                out CoCoDiagnostic diagnostic) =>
                CoCoStateGraphRuntime.TryCreate(
                    Graph,
                    graphInstanceId,
                    bindings,
                    CreateOperationFrame(graphInstanceId),
                    CreateClock(graphInstanceId),
                    out runtime,
                    out diagnostic);

            public CoCoOperationFrame CreateOperationFrame(CoCoGraphInstanceId graphInstanceId)
            {
                var registryBuilder = new CoCoOperationSectionRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryFreeze(
                    Graph.OperationProvides.LayoutId,
                    out CoCoOperationSectionRegistry registry,
                    out CoCoDiagnostic registryDiagnostic), registryDiagnostic.Message);
                Assert.IsTrue(CoCoOperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    Array.Empty<CoCoOperationSectionRequirement>(),
                    out CoCoOperationFrame frame,
                    out CoCoDiagnostic frameDiagnostic), frameDiagnostic.Message);
                return frame;
            }

            public CoCoActorClock CreateClock(CoCoGraphInstanceId graphInstanceId)
            {
                Assert.IsTrue(CoCoActorClock.TryCreate(
                    Ids.Timeline,
                    Ids.ClockDomain,
                    new CoCoTimelineEpoch(1UL),
                    graphInstanceId,
                    out CoCoActorClock clock,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                return clock;
            }

            private static CoCoGraphDescriptorCatalog CreateCatalog(bool withCondition)
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                Assert.IsTrue(builder.TryRegisterState(
                    Ids.StateDescriptor,
                    1U,
                    new RuntimeFixtureStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
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
                        new CoCoConditionRuntimeRegistration<
                            RuntimeSafetyFixtureCondition,
                            RuntimeFixtureConditionConfigSchema>(RuntimeFixtureSchemas.Condition),
                        null,
                        null,
                        out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
                }

                Assert.IsTrue(builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return catalog;
            }

            private static CoCoStateGraphSource CreateSource(bool withCondition)
            {
                CoCoStateSource root = State(Ids.Root, default, withCondition ? Ids.LeafA : default);
                CoCoStateSource[] states;
                CoCoTransitionSource[] transitions;
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
                        new CoCoTransitionSource(
                            Ids.Transition,
                            Ids.LeafA,
                            Ids.LeafB,
                            1,
                            CoCoTransitionWindow.Always,
                            new[]
                            {
                                new CoCoConditionSource(
                                    Ids.ConditionDescriptor,
                                    ConditionConfig())
                            })
                    };
                }
                else
                {
                    states = new[] { root };
                    transitions = Array.Empty<CoCoTransitionSource>();
                }

                return new CoCoStateGraphSource(
                    CoCoStateGraphCompiler.CurrentSchemaVersion,
                    withCondition ? 802UL : 801UL,
                    Ids.Graph,
                    new[]
                    {
                        new CoCoStateLayerSource(Ids.Layer, Ids.Root, states, transitions)
                    },
                    Array.Empty<CoCoEventToIntentDeclarationSource>());
            }

            private static CoCoStateSource State(
                CoCoStateId stateId,
                CoCoStateId parent,
                CoCoStateId initialChild) =>
                new CoCoStateSource(
                    stateId,
                    parent,
                    initialChild,
                    Ids.StateDescriptor,
                    StateConfig());

            private static CoCoFrozenConfigSnapshot StateConfig()
            {
                CoCoFrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer =
                    RuntimeFixtureSchemas.State.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.StateValue, 0, out _));
                Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }

            private static CoCoFrozenConfigSnapshot ConditionConfig()
            {
                CoCoFrozenConfigWriter<RuntimeFixtureConditionConfigSchema> writer =
                    RuntimeFixtureSchemas.Condition.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.ConditionValue, 0, out _));
                Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }
        }

        private static class Ids
        {
            static Ids()
            {
                CoCoGraphId.TryCreate(0x51UL, 1UL, out Graph);
                CoCoLayerId.TryCreate(0x51UL, 2UL, out Layer);
                CoCoStateId.TryCreate(0x51UL, 3UL, out Root);
                CoCoStateId.TryCreate(0x51UL, 4UL, out LeafA);
                CoCoStateId.TryCreate(0x51UL, 5UL, out LeafB);
                CoCoTransitionId.TryCreate(0x51UL, 6UL, out Transition);
                CoCoStateDescriptorId.TryCreate(0x51UL, 7UL, out StateDescriptor);
                CoCoConditionDescriptorId.TryCreate(0x51UL, 8UL, out ConditionDescriptor);
                CoCoTimelineId.TryCreate(0x51UL, 9UL, out Timeline);
                CoCoClockDomainId.TryCreate(10UL, out ClockDomain);
                CoCoStateDescriptorId.TryCreate(
                    0x51UL,
                    11UL,
                    out OperationProviderDescriptor);
                CoCoStateDescriptorId.TryCreate(
                    0x51UL,
                    12UL,
                    out OperationConsumerDescriptor);
                CoCoOperationSectionId.TryCreate(
                    0x51UL,
                    13UL,
                    out OperationSection);
            }

            public static readonly CoCoGraphId Graph;
            public static readonly CoCoLayerId Layer;
            public static readonly CoCoStateId Root;
            public static readonly CoCoStateId LeafA;
            public static readonly CoCoStateId LeafB;
            public static readonly CoCoTransitionId Transition;
            public static readonly CoCoStateDescriptorId StateDescriptor;
            public static readonly CoCoConditionDescriptorId ConditionDescriptor;
            public static readonly CoCoTimelineId Timeline;
            public static readonly CoCoClockDomainId ClockDomain;
            public static readonly CoCoStateDescriptorId OperationProviderDescriptor;
            public static readonly CoCoStateDescriptorId OperationConsumerDescriptor;
            public static readonly CoCoOperationSectionId OperationSection;

            public static CoCoGraphInstanceId GraphInstance(ulong value)
            {
                CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id);
                return id;
            }

            public static CoCoTickFrame Tick(ulong value)
            {
                CoCoTimelinePosition.TryCreate(value, out CoCoTimelinePosition position);
                CoCoTickFrame.TryCreate(
                    1d,
                    Timeline,
                    position,
                    new CoCoTimelineTick(value),
                    ClockDomain,
                    new CoCoExecutionSequence(value),
                    new CoCoTimelineEpoch(1UL),
                    out CoCoTickFrame tick,
                    out _);
                return tick;
            }
        }
    }
}
