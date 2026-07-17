using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphRuntimeContractTests
    {
        private static long _nextGraphInstance = 3000L;

        [Test]
        public void OptionalLifecyclePhasesPreserveEnterUpdateExitPathOrder()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids);
            CoCoStateGraphSource source = LifecycleSource(ids);
            RuntimeFixture fixture = RuntimeFixture.Create(source, CreateCatalog(false), control, false);

            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic diagnostic), diagnostic.Message);
            Assert.AreEqual(0, control.Callbacks.Count, "Start must not invoke State callbacks.");

            fixture.Accept(fixture.Stage(0.1d));
            AssertTrace(
                control.Callbacks,
                (RuntimeContractCallbackPhase.Enter, ids.Branch),
                (RuntimeContractCallbackPhase.Update, ids.Root),
                (RuntimeContractCallbackPhase.Update, ids.Branch),
                (RuntimeContractCallbackPhase.Update, ids.StateA));

            control.Callbacks.Clear();
            control.RequestFirst = true;
            CoCoStagedGraphStep transition = fixture.Stage(0.1d);
            AssertTrace(
                control.Callbacks,
                (RuntimeContractCallbackPhase.Update, ids.Root),
                (RuntimeContractCallbackPhase.Update, ids.Branch),
                (RuntimeContractCallbackPhase.Update, ids.StateA),
                (RuntimeContractCallbackPhase.Exit, ids.StateA),
                (RuntimeContractCallbackPhase.Exit, ids.Branch));
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            fixture.Accept(transition);

            control.Callbacks.Clear();
            control.RequestFirst = false;
            fixture.Accept(fixture.Stage(0.1d));
            AssertTrace(
                control.Callbacks,
                (RuntimeContractCallbackPhase.Enter, ids.StateB),
                (RuntimeContractCallbackPhase.Update, ids.Root),
                (RuntimeContractCallbackPhase.Update, ids.StateB));
        }

        [Test]
        public void ActionProgressStallLargeSweepAndRejectedTickAreTransactional()
        {
            ContractIds ids = ContractIds.Create();
            Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                CoCoTransitionWindowMode.ActionProgress,
                0.4d,
                0.5d,
                out CoCoTransitionWindow window));
            var control = new RuntimeContractControl(ids)
            {
                ActionProgressState = ids.StateA,
                DesiredActionProgress = 0.2d
            };
            RuntimeFixture fixture = RuntimeFixture.Create(
                WindowSource(ids, window),
                CreateCatalog(false),
                control,
                false);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));

            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);

            control.RequestFirst = true;
            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "A stalled ActionProgress value must not open a later Window.");
            Assert.AreEqual(2UL, fixture.Runtime.Clock.Tick.Value);

            control.DesiredActionProgress = 0.9d;
            CoCoStagedGraphStep rejected = fixture.Stage(0.1d);
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "A large sweep selects only a candidate target before commit.");
            Assert.IsTrue(fixture.Runtime.TryRejectStagedStep(
                rejected,
                CoCoDiagnostic.None,
                false,
                out CoCoDiagnostic rejectDiagnostic), rejectDiagnostic.Message);
            Assert.AreEqual(2UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            control.DesiredActionProgress = 0.2d;
            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "Reject must roll ActionProgress back to its last committed value.");

            control.DesiredActionProgress = 0.9d;
            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(ids.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "The [start,end) sweep must detect a jump that passes over the whole Window.");
        }

        [Test]
        public void ReachingActionProgressOneDoesNotAutomaticallyExit()
        {
            ContractIds ids = ContractIds.Create();
            Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                CoCoTransitionWindowMode.ActionProgress,
                0.8d,
                1d,
                out CoCoTransitionWindow window));
            var control = new RuntimeContractControl(ids)
            {
                ActionProgressState = ids.StateA,
                DesiredActionProgress = 1d,
                RequestFirst = false
            };
            RuntimeFixture fixture = RuntimeFixture.Create(
                WindowSource(ids, window),
                CreateCatalog(false),
                control,
                false);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));

            fixture.Accept(fixture.Stage(0.1d));
            fixture.Accept(fixture.Stage(0.1d));

            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            for (int index = 0; index < control.Callbacks.Count; index++)
            {
                Assert.IsFalse(
                    control.Callbacks[index].Phase == RuntimeContractCallbackPhase.Exit &&
                    control.Callbacks[index].StateId == ids.StateA,
                    "ActionProgress=1 is data, not an implicit completion signal.");
            }
        }

        [Test]
        public void LocalSecondsWindowUsesSweepForLargeDelta()
        {
            ContractIds ids = ContractIds.Create();
            Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                CoCoTransitionWindowMode.LocalSeconds,
                0.2d,
                0.3d,
                out CoCoTransitionWindow window));
            var control = new RuntimeContractControl(ids) { RequestFirst = true };
            RuntimeFixture fixture = RuntimeFixture.Create(
                WindowSource(ids, window),
                CreateCatalog(false),
                control,
                false);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));

            fixture.Accept(fixture.Stage(1d));

            Assert.AreEqual(ids.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "A large Delta must sweep across a LocalSeconds Window instead of skipping it.");
        }

        [Test]
        public void MultipleRequestsUseConditionsThenPriorityToChooseOneWinner()
        {
            ContractIds ids = ContractIds.Create();
            CoCoStateGraphSource source = ArbitrationSource(ids);
            CoCoGraphDescriptorCatalog catalog = CreateCatalog(false);

            var allAccepted = new RuntimeContractControl(ids)
            {
                RequestFirst = true,
                RequestSecond = true
            };
            RuntimeFixture highWins = RuntimeFixture.Create(source, catalog, allAccepted, false);
            Assert.IsTrue(highWins.Runtime.TryStart(out _));
            highWins.Accept(highWins.Stage(0.1d));
            Assert.AreEqual(2, allAccepted.ConditionEvaluationCount);
            Assert.AreEqual(ids.StateC, highWins.Runtime.GetActivePath(0).ActiveLeaf,
                "When both Conditions pass, the higher explicitly-declared Priority must win.");

            var highRejected = new RuntimeContractControl(ids)
            {
                RequestFirst = true,
                RequestSecond = true,
                RejectedConditionTransition = ids.SecondTransition
            };
            RuntimeFixture lowWins = RuntimeFixture.Create(source, catalog, highRejected, false);
            Assert.IsTrue(lowWins.Runtime.TryStart(out _));
            lowWins.Accept(lowWins.Stage(0.1d));
            Assert.AreEqual(2, highRejected.ConditionEvaluationCount);
            Assert.AreEqual(ids.StateB, lowWins.Runtime.GetActivePath(0).ActiveLeaf,
                "A higher-priority request whose Condition fails cannot suppress an accepted request.");
        }

        [Test]
        public void ConditionExceptionCancelsCandidateAndLatchesFault()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids);
            RuntimeFixture fixture = RuntimeFixture.Create(
                ArbitrationSource(ids),
                CreateCatalog(false),
                control,
                false);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage(0.1d));

            control.RequestFirst = true;
            control.ThrowOnCondition = true;
            Assert.IsTrue(fixture.Runtime.TryPreviewNextTick(0.1d, 1d, out CoCoTickFrame tick, out _));
            Assert.IsFalse(fixture.Runtime.TryStageStep(
                tick,
                null,
                default,
                out _,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsError);
            Assert.IsTrue(fixture.Runtime.IsFaulted);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.IsFalse(fixture.Runtime.TryResume(out _));
        }

        [Test]
        public void ActiveLeafWithoutOutgoingEdgesDoesNotEvaluateTransitions()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids) { RequestFirst = true };
            RuntimeFixture fixture = RuntimeFixture.Create(
                ZeroOutgoingSource(ids),
                CreateCatalog(false),
                control,
                false);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));

            fixture.Accept(fixture.Stage(0.1d));
            fixture.Accept(fixture.Stage(0.1d));
            fixture.Accept(fixture.Stage(0.1d));

            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(0, control.TransitionRequestCheckCount,
                "A leaf with no declared outgoing Handles must not calculate requests.");
            Assert.AreEqual(0, control.ConditionEvaluationCount,
                "Conditions on another leaf's outgoing edge must remain untouched.");
        }

        [Test]
        public void TwoLayerParentChildOperationCompositionHasZeroSteadyStateAllocations()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids)
            {
                RecordCallbacks = false,
                WriteOperations = true
            };
            RuntimeFixture fixture = RuntimeFixture.Create(
                OperationCompositionSource(ids),
                CreateCatalog(true),
                control,
                true);
            control.ConfigureOperationStates(
                ids.LowRoot,
                ids.LowLeaf,
                ids.HighRoot,
                ids.HighLeaf);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));

            CoCoStagedGraphStep first = fixture.Stage(0.1d);
            Assert.IsTrue(first.OperationFrame.TryGet(
                fixture.OperationHandle,
                out CoCoOperationSectionEntry<IRuntimeContractOperationSection> entry));
            Assert.AreEqual(40, entry.View.Value,
                "Higher Layer then deeper child must determine the final composed field.");
            fixture.Accept(first);

            for (int index = 0; index < 100; index++)
            {
                Assert.IsTrue(fixture.TryStageAndAccept(0.1d));
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool succeeded = true;
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= fixture.TryStageAndAccept(0.1d);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void OperationFinalizeFailureFaultsAndRollsBackClockPathAndMemory()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids)
            {
                RecordCallbacks = false,
                WriteOperations = true,
                EnableDiscreteOperation = true
            };
            RuntimeFixture fixture = RuntimeFixture.Create(
                OperationFinalizeFailureSource(ids),
                CreateCatalog(true, CoCoOperationSectionMode.Discrete),
                control,
                true,
                CoCoOperationSectionMode.Discrete);
            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic start), start.Message);

            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(1, ReadCommittedMemory(fixture.Runtime, 0, ids.StateA).UpdateCount);

            FieldInfo sequencesField = typeof(CoCoOperationFrame).GetField(
                "_committedSequences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(sequencesField);
            var sequences = (ulong[])sequencesField.GetValue(fixture.OperationFrame);
            sequences[fixture.OperationHandle.DenseIndex] = ulong.MaxValue;

            Assert.IsTrue(fixture.Runtime.TryPreviewNextTick(
                0.1d,
                1d,
                out CoCoTickFrame tick,
                out CoCoDiagnostic preview), preview.Message);
            Assert.IsFalse(fixture.Runtime.TryStageStep(
                tick,
                null,
                default,
                out _,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsError);
            Assert.IsTrue(fixture.Runtime.IsFaulted);
            Assert.IsFalse(fixture.Runtime.HasStagedStep);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(ids.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.AreEqual(1, ReadCommittedMemory(fixture.Runtime, 0, ids.StateA).UpdateCount);
        }

        private static CoCoStateGraphSource LifecycleSource(ContractIds ids)
        {
            var transition = Transition(
                ids.FirstTransition,
                ids.StateA,
                ids.StateB,
                10,
                CoCoTransitionWindow.Always,
                false,
                ids);
            var layer = new CoCoStateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.StateB, ids.Root, default, ids.EnterUpdateDescriptor),
                    State(ids.StateA, ids.Branch, default, ids.UpdateExitDescriptor),
                    State(ids.Root, default, ids.Branch, ids.UpdateOnlyDescriptor),
                    State(ids.Branch, ids.Root, ids.StateA, ids.AllPhasesDescriptor)
                },
                new[] { transition });
            return Source(ids, 0xC0401UL, layer);
        }

        private static CoCoStateGraphSource WindowSource(
            ContractIds ids,
            CoCoTransitionWindow window)
        {
            var layer = new CoCoStateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.StateB, ids.Root, default, ids.UpdateOnlyDescriptor),
                    State(ids.Root, default, ids.StateA, ids.UpdateOnlyDescriptor),
                    State(ids.StateA, ids.Root, default, ids.UpdateOnlyDescriptor)
                },
                new[]
                {
                    Transition(
                        ids.FirstTransition,
                        ids.StateA,
                        ids.StateB,
                        10,
                        window,
                        false,
                        ids)
                });
            return Source(ids, 0xC0402UL, layer);
        }

        private static CoCoStateGraphSource ArbitrationSource(ContractIds ids)
        {
            var layer = new CoCoStateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.StateC, ids.Root, default, ids.UpdateOnlyDescriptor),
                    State(ids.StateB, ids.Root, default, ids.UpdateOnlyDescriptor),
                    State(ids.Root, default, ids.StateA, ids.UpdateOnlyDescriptor),
                    State(ids.StateA, ids.Root, default, ids.UpdateOnlyDescriptor)
                },
                new[]
                {
                    Transition(
                        ids.FirstTransition,
                        ids.StateA,
                        ids.StateB,
                        10,
                        CoCoTransitionWindow.Always,
                        true,
                        ids),
                    Transition(
                        ids.SecondTransition,
                        ids.StateA,
                        ids.StateC,
                        20,
                        CoCoTransitionWindow.Always,
                        true,
                        ids)
                });
            return Source(ids, 0xC0403UL, layer);
        }

        private static CoCoStateGraphSource ZeroOutgoingSource(ContractIds ids)
        {
            var layer = new CoCoStateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.StateB, ids.Root, default, ids.UpdateOnlyDescriptor),
                    State(ids.Root, default, ids.StateA, ids.UpdateOnlyDescriptor),
                    State(ids.StateA, ids.Root, default, ids.UpdateOnlyDescriptor)
                },
                new[]
                {
                    Transition(
                        ids.FirstTransition,
                        ids.StateB,
                        ids.StateA,
                        10,
                        CoCoTransitionWindow.Always,
                        true,
                        ids)
                });
            return Source(ids, 0xC0404UL, layer);
        }

        private static CoCoStateGraphSource OperationCompositionSource(ContractIds ids)
        {
            var low = new CoCoStateLayerSource(
                ids.LowLayer,
                ids.LowRoot,
                new[]
                {
                    State(ids.LowLeaf, ids.LowRoot, default, ids.UpdateOnlyDescriptor),
                    State(ids.LowRoot, default, ids.LowLeaf, ids.UpdateOnlyDescriptor)
                },
                Array.Empty<CoCoTransitionSource>());
            var high = new CoCoStateLayerSource(
                ids.HighLayer,
                ids.HighRoot,
                new[]
                {
                    State(ids.HighLeaf, ids.HighRoot, default, ids.UpdateOnlyDescriptor),
                    State(ids.HighRoot, default, ids.HighLeaf, ids.UpdateOnlyDescriptor)
                },
                Array.Empty<CoCoTransitionSource>());
            return Source(ids, 0xC0405UL, low, high);
        }

        private static CoCoStateGraphSource OperationFinalizeFailureSource(ContractIds ids)
        {
            var layer = new CoCoStateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.StateA, ids.Root, default, ids.UpdateOnlyDescriptor),
                    State(ids.Root, default, ids.StateA, ids.UpdateOnlyDescriptor)
                },
                Array.Empty<CoCoTransitionSource>());
            return Source(ids, 0xC0406UL, layer);
        }

        private static CoCoStateGraphSource Source(
            ContractIds ids,
            ulong fingerprint,
            params CoCoStateLayerSource[] layers) =>
            new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion,
                fingerprint,
                ids.Graph,
                layers,
                Array.Empty<CoCoEventToIntentDeclarationSource>());

        private static CoCoStateSource State(
            CoCoStateId stateId,
            CoCoStateId parentId,
            CoCoStateId initialChildId,
            CoCoStateDescriptorId descriptorId) =>
            new CoCoStateSource(
                stateId,
                parentId,
                initialChildId,
                descriptorId,
                StateConfig(1));

        private static CoCoTransitionSource Transition(
            CoCoTransitionId id,
            CoCoStateId source,
            CoCoStateId target,
            int priority,
            CoCoTransitionWindow window,
            bool withCondition,
            ContractIds ids) =>
            new CoCoTransitionSource(
                id,
                source,
                target,
                priority,
                window,
                withCondition
                    ? new[]
                    {
                        new CoCoConditionSource(ids.ConditionDescriptor, ConditionConfig(1))
                    }
                    : Array.Empty<CoCoConditionSource>());

        private static CoCoGraphDescriptorCatalog CreateCatalog(
            bool withOperation,
            CoCoOperationSectionMode operationMode = CoCoOperationSectionMode.Continuous)
        {
            ContractIds ids = ContractIds.Create();
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            CoCoOperationSectionId[] operations = null;
            if (withOperation)
            {
                Assert.IsTrue(builder.TryRegisterOperationSection(
                    ids.OperationSection,
                    operationMode,
                    new CoCoOperationSectionViewFactoryToken<
                        IRuntimeContractOperationSection,
                        RuntimeContractOperationSectionViewFactory>(0xC040UL),
                    out CoCoDiagnostic operationDiagnostic), operationDiagnostic.Message);
                operations = new[] { ids.OperationSection };
            }

            RegisterState<RuntimeContractUpdateOnlyLogic>(
                builder,
                ids.UpdateOnlyDescriptor,
                operations);
            RegisterState<RuntimeContractEnterUpdateLogic>(
                builder,
                ids.EnterUpdateDescriptor,
                operations);
            RegisterState<RuntimeContractUpdateExitLogic>(
                builder,
                ids.UpdateExitDescriptor,
                operations);
            RegisterState<RuntimeContractEnterUpdateExitLogic>(
                builder,
                ids.AllPhasesDescriptor,
                operations);
            Assert.IsTrue(builder.TryRegisterCondition(
                ids.ConditionDescriptor,
                1U,
                new RuntimeContractConditionConfigFreezer(),
                new CoCoConditionRuntimeRegistration<
                    RuntimeContractCondition,
                    RuntimeContractConditionConfigSchema>(RuntimeContractSchemas.Condition),
                null,
                null,
                out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return catalog;
        }

        private static RuntimeContractMemory ReadCommittedMemory(
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
            IReadOnlyList<CoCoCompiledState> compiledStates = runtime.Graph.Layers[layerIndex].States;
            for (int index = 0; index < compiledStates.Count; index++)
            {
                if (compiledStates[index].StateId != stateId)
                {
                    continue;
                }

                object state = states.GetValue(index);
                PropertyInfo memoryProperty = state.GetType().GetProperty(
                    "CommittedMemory",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.IsNotNull(memoryProperty);
                return (RuntimeContractMemory)memoryProperty.GetValue(state);
            }

            Assert.Fail("The requested State was not found in the Runtime layer.");
            return null;
        }

        private static void RegisterState<TLogic>(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoStateDescriptorId descriptorId,
            CoCoOperationSectionId[] operations)
            where TLogic : CoCoStateLogic
        {
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new RuntimeContractStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TLogic,
                    RuntimeContractStateConfigSchema,
                    RuntimeContractMemory>(RuntimeContractSchemas.State, true),
                null,
                operations,
                null,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static CoCoFrozenConfigSnapshot StateConfig(int value)
        {
            CoCoFrozenConfigWriter<RuntimeContractStateConfigSchema> writer =
                RuntimeContractSchemas.State.CreateWriter();
            Assert.IsTrue(writer.TryWrite(RuntimeContractSchemas.StateValue, value, out _));
            Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
            return snapshot;
        }

        private static CoCoFrozenConfigSnapshot ConditionConfig(int value)
        {
            CoCoFrozenConfigWriter<RuntimeContractConditionConfigSchema> writer =
                RuntimeContractSchemas.Condition.CreateWriter();
            Assert.IsTrue(writer.TryWrite(RuntimeContractSchemas.ConditionValue, value, out _));
            Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
            return snapshot;
        }

        private static void AssertTrace(
            IReadOnlyList<CallbackRecord> actual,
            params (RuntimeContractCallbackPhase Phase, CoCoStateId StateId)[] expected)
        {
            Assert.AreEqual(expected.Length, actual.Count, "Unexpected callback count.");
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(expected[index].Phase, actual[index].Phase, $"Phase at {index}");
                Assert.AreEqual(expected[index].StateId, actual[index].StateId, $"State at {index}");
            }
        }

        private sealed class RuntimeFixture
        {
            private RuntimeFixture(
                CoCoStateGraphRuntime runtime,
                CoCoOperationFrame operationFrame,
                CoCoOperationSectionHandle<IRuntimeContractOperationSection> operationHandle)
            {
                Runtime = runtime;
                OperationFrame = operationFrame;
                OperationHandle = operationHandle;
            }

            public CoCoStateGraphRuntime Runtime { get; }
            public CoCoOperationFrame OperationFrame { get; }
            public CoCoOperationSectionHandle<IRuntimeContractOperationSection> OperationHandle { get; }

            public static RuntimeFixture Create(
                CoCoStateGraphSource source,
                CoCoGraphDescriptorCatalog catalog,
                RuntimeContractControl control,
                bool withOperation,
                CoCoOperationSectionMode operationMode = CoCoOperationSectionMode.Continuous)
            {
                CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);
                Assert.IsTrue(result.Succeeded, JoinDiagnostics(result.Diagnostics));
                CoCoCompiledStateGraph graph = result.Graph;

                var registryBuilder = new CoCoOperationSectionRegistryBuilder();
                CoCoOperationSectionRequirement operationRequirement = default;
                if (withOperation)
                {
                    Assert.IsTrue(registryBuilder.TryRegister(
                        control.Ids.OperationSection,
                        operationMode,
                        new RuntimeContractOperationSectionViewFactory(),
                        out operationRequirement,
                        out CoCoDiagnostic registerDiagnostic), registerDiagnostic.Message);
                }

                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out CoCoOperationSectionRegistry registry,
                    out CoCoDiagnostic registryDiagnostic), registryDiagnostic.Message);
                CoCoOperationSectionHandle<IRuntimeContractOperationSection> operationHandle = default;
                if (withOperation)
                {
                    Assert.IsTrue(registry.TryResolve(operationRequirement, out operationHandle));
                    Assert.IsTrue(registry.TryResolveField(
                        operationHandle,
                        0,
                        out CoCoOperationSectionField<int> valueField));
                    control.OperationField = valueField;
                    control.OperationHandle = operationHandle;
                }

                long instanceValue = Interlocked.Increment(ref _nextGraphInstance);
                Assert.IsTrue(CoCoGraphInstanceId.TryCreate(
                    (ulong)instanceValue,
                    out CoCoGraphInstanceId graphInstanceId));
                IReadOnlyList<CoCoOperationSectionRequirement> requirements = withOperation
                    ? new[] { operationRequirement }
                    : Array.Empty<CoCoOperationSectionRequirement>();
                Assert.IsTrue(CoCoOperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    requirements,
                    out CoCoOperationFrame operationFrame,
                    out CoCoDiagnostic frameDiagnostic), frameDiagnostic.Message);
                Assert.IsTrue(CoCoActorClock.TryCreate(
                    control.Ids.Timeline,
                    control.Ids.ClockDomain,
                    new CoCoTimelineEpoch(1UL),
                    out CoCoActorClock clock,
                    out CoCoDiagnostic clockDiagnostic), clockDiagnostic.Message);

                CoCoStateGraphLogicBindings bindings = CreateBindings(graph, control);
                Assert.IsTrue(CoCoStateGraphRuntime.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out CoCoStateGraphRuntime runtime,
                    out CoCoDiagnostic runtimeDiagnostic), runtimeDiagnostic.Message);
                return new RuntimeFixture(runtime, operationFrame, operationHandle);
            }

            public CoCoStagedGraphStep Stage(double delta)
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    delta,
                    1d,
                    out CoCoTickFrame tick,
                    out CoCoDiagnostic previewDiagnostic), previewDiagnostic.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out CoCoStagedGraphStep staged,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                return staged;
            }

            public void Accept(CoCoStagedGraphStep staged)
            {
                Assert.IsTrue(Runtime.TryAcceptStagedStep(
                    staged,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            public bool TryStageAndAccept(double delta) =>
                Runtime.TryPreviewNextTick(delta, 1d, out CoCoTickFrame tick, out _) &&
                Runtime.TryStageStep(tick, null, default, out CoCoStagedGraphStep staged, out _) &&
                Runtime.TryAcceptStagedStep(staged, out _);

            private static CoCoStateGraphLogicBindings CreateBindings(
                CoCoCompiledStateGraph graph,
                RuntimeContractControl control)
            {
                bool updateOnly = false;
                bool enterUpdate = false;
                bool updateExit = false;
                bool allPhases = false;
                bool condition = false;
                for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
                {
                    CoCoCompiledStateLayer layer = graph.Layers[layerIndex];
                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        CoCoStateDescriptorId descriptorId = layer.States[stateIndex].Descriptor.DescriptorId;
                        updateOnly |= descriptorId == control.Ids.UpdateOnlyDescriptor;
                        enterUpdate |= descriptorId == control.Ids.EnterUpdateDescriptor;
                        updateExit |= descriptorId == control.Ids.UpdateExitDescriptor;
                        allPhases |= descriptorId == control.Ids.AllPhasesDescriptor;
                    }

                    for (int transitionIndex = 0;
                         transitionIndex < layer.Transitions.Count;
                         transitionIndex++)
                    {
                        condition |= layer.Transitions[transitionIndex].Conditions.Count > 0;
                    }
                }

                var builder = new CoCoStateGraphLogicBindingsBuilder(graph);
                if (updateOnly)
                {
                    BindState(
                        builder,
                        control.Ids.UpdateOnlyDescriptor,
                        new CoCoStateRuntimeFactory<
                            RuntimeContractUpdateOnlyLogic,
                            RuntimeContractMemory>(
                            context => new RuntimeContractUpdateOnlyLogic(context, control),
                            CreateMemory,
                            CopyMemory,
                            ResetMemory,
                            FingerprintMemory));
                }

                if (enterUpdate)
                {
                    BindState(
                        builder,
                        control.Ids.EnterUpdateDescriptor,
                        new CoCoStateRuntimeFactory<
                            RuntimeContractEnterUpdateLogic,
                            RuntimeContractMemory>(
                            context => new RuntimeContractEnterUpdateLogic(context, control),
                            CreateMemory,
                            CopyMemory,
                            ResetMemory,
                            FingerprintMemory));
                }

                if (updateExit)
                {
                    BindState(
                        builder,
                        control.Ids.UpdateExitDescriptor,
                        new CoCoStateRuntimeFactory<
                            RuntimeContractUpdateExitLogic,
                            RuntimeContractMemory>(
                            context => new RuntimeContractUpdateExitLogic(context, control),
                            CreateMemory,
                            CopyMemory,
                            ResetMemory,
                            FingerprintMemory));
                }

                if (allPhases)
                {
                    BindState(
                        builder,
                        control.Ids.AllPhasesDescriptor,
                        new CoCoStateRuntimeFactory<
                            RuntimeContractEnterUpdateExitLogic,
                            RuntimeContractMemory>(
                            context => new RuntimeContractEnterUpdateExitLogic(context, control),
                            CreateMemory,
                            CopyMemory,
                            ResetMemory,
                            FingerprintMemory));
                }

                if (condition)
                {
                    Assert.IsTrue(builder.TryBindCondition(
                        control.Ids.ConditionDescriptor,
                        new CoCoConditionRuntimeFactory<RuntimeContractCondition>(
                            context => new RuntimeContractCondition(context, control)),
                        out CoCoDiagnostic diagnostic), diagnostic.Message);
                }

                Assert.IsTrue(builder.TryFreeze(
                    out CoCoStateGraphLogicBindings bindings,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return bindings;
            }

            private static void BindState(
                CoCoStateGraphLogicBindingsBuilder builder,
                CoCoStateDescriptorId descriptorId,
                ICoCoStateRuntimeFactory factory)
            {
                Assert.IsTrue(builder.TryBindState(
                    descriptorId,
                    factory,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            private static RuntimeContractMemory CreateMemory() => new RuntimeContractMemory();

            private static void CopyMemory(
                RuntimeContractMemory source,
                RuntimeContractMemory destination) =>
                destination.UpdateCount = source.UpdateCount;

            private static void ResetMemory(RuntimeContractMemory memory) => memory.UpdateCount = 0;

            private static ulong FingerprintMemory(RuntimeContractMemory memory) =>
                unchecked((ulong)(uint)memory.UpdateCount);

            private static string JoinDiagnostics(IReadOnlyList<CoCoGraphDiagnostic> diagnostics)
            {
                string message = string.Empty;
                for (int index = 0; index < diagnostics.Count; index++)
                {
                    message += diagnostics[index].Diagnostic.Message + "\n";
                }

                return message;
            }
        }

        private sealed class RuntimeContractControl : IRuntimeContractObserver
        {
            private CoCoStateId _lowRoot;
            private CoCoStateId _lowLeaf;
            private CoCoStateId _highRoot;
            private CoCoStateId _highLeaf;

            public RuntimeContractControl(ContractIds ids)
            {
                Ids = ids;
            }

            public ContractIds Ids { get; }
            public List<CallbackRecord> Callbacks { get; } = new List<CallbackRecord>();
            public bool RecordCallbacks = true;
            public bool RequestFirst;
            public bool RequestSecond;
            public bool ThrowOnCondition;
            public bool WriteOperations;
            public bool EnableDiscreteOperation;
            public int TransitionRequestCheckCount;
            public int ConditionEvaluationCount;
            public CoCoTransitionId RejectedConditionTransition;
            public CoCoStateId ActionProgressState;
            public double DesiredActionProgress;
            public CoCoOperationSectionField<int> OperationField;
            public CoCoOperationSectionHandle<IRuntimeContractOperationSection> OperationHandle;

            public void ConfigureOperationStates(
                CoCoStateId lowRoot,
                CoCoStateId lowLeaf,
                CoCoStateId highRoot,
                CoCoStateId highLeaf)
            {
                _lowRoot = lowRoot;
                _lowLeaf = lowLeaf;
                _highRoot = highRoot;
                _highLeaf = highLeaf;
            }

            public bool ShouldRequest(CoCoTransitionId transitionId)
            {
                TransitionRequestCheckCount++;
                return transitionId == Ids.FirstTransition
                    ? RequestFirst
                    : transitionId == Ids.SecondTransition && RequestSecond;
            }

            public double GetActionProgress(CoCoStateId stateId, double currentValue) =>
                stateId == ActionProgressState ? DesiredActionProgress : currentValue;

            public bool EvaluateCondition(
                CoCoTransitionId transitionId,
                CoCoConditionEvaluationContext context)
            {
                ConditionEvaluationCount++;
                if (ThrowOnCondition)
                {
                    throw new InvalidOperationException("synthetic Condition failure");
                }

                return transitionId != RejectedConditionTransition;
            }

            public void OnStateCallback(
                RuntimeContractCallbackPhase phase,
                CoCoStateId stateId,
                CoCoStateExecutionContext context)
            {
                if (RecordCallbacks)
                {
                    Callbacks.Add(new CallbackRecord(
                        phase,
                        stateId,
                        context.PreviousLocalSeconds,
                        context.LocalSeconds,
                        context.PreviousActionProgress,
                        context.ActionProgress));
                }

                if (WriteOperations &&
                    phase == RuntimeContractCallbackPhase.Update &&
                    OperationField.IsValid)
                {
                    int value = stateId == _lowRoot
                        ? 10
                        : stateId == _lowLeaf
                            ? 20
                            : stateId == _highRoot
                                ? 30
                                : stateId == _highLeaf
                                    ? 40
                                    : 0;
                    context.Operations.Write(OperationField, value);
                    if (EnableDiscreteOperation)
                    {
                        context.Operations.EnableDiscrete(OperationHandle);
                    }
                }
            }
        }

        private readonly struct CallbackRecord
        {
            public CallbackRecord(
                RuntimeContractCallbackPhase phase,
                CoCoStateId stateId,
                double previousLocalSeconds,
                double localSeconds,
                double previousActionProgress,
                double actionProgress)
            {
                Phase = phase;
                StateId = stateId;
                PreviousLocalSeconds = previousLocalSeconds;
                LocalSeconds = localSeconds;
                PreviousActionProgress = previousActionProgress;
                ActionProgress = actionProgress;
            }

            public RuntimeContractCallbackPhase Phase { get; }
            public CoCoStateId StateId { get; }
            public double PreviousLocalSeconds { get; }
            public double LocalSeconds { get; }
            public double PreviousActionProgress { get; }
            public double ActionProgress { get; }
        }

        private readonly struct ContractIds
        {
            private const ulong High = 0xC04UL;

            private ContractIds(
                CoCoGraphId graph,
                CoCoLayerId lowLayer,
                CoCoLayerId highLayer,
                CoCoStateId root,
                CoCoStateId branch,
                CoCoStateId stateA,
                CoCoStateId stateB,
                CoCoStateId stateC,
                CoCoStateId lowRoot,
                CoCoStateId lowLeaf,
                CoCoStateId highRoot,
                CoCoStateId highLeaf,
                CoCoTransitionId firstTransition,
                CoCoTransitionId secondTransition,
                CoCoStateDescriptorId updateOnlyDescriptor,
                CoCoStateDescriptorId enterUpdateDescriptor,
                CoCoStateDescriptorId updateExitDescriptor,
                CoCoStateDescriptorId allPhasesDescriptor,
                CoCoConditionDescriptorId conditionDescriptor,
                CoCoOperationSectionId operationSection,
                CoCoTimelineId timeline,
                CoCoClockDomainId clockDomain)
            {
                Graph = graph;
                LowLayer = lowLayer;
                HighLayer = highLayer;
                Root = root;
                Branch = branch;
                StateA = stateA;
                StateB = stateB;
                StateC = stateC;
                LowRoot = lowRoot;
                LowLeaf = lowLeaf;
                HighRoot = highRoot;
                HighLeaf = highLeaf;
                FirstTransition = firstTransition;
                SecondTransition = secondTransition;
                UpdateOnlyDescriptor = updateOnlyDescriptor;
                EnterUpdateDescriptor = enterUpdateDescriptor;
                UpdateExitDescriptor = updateExitDescriptor;
                AllPhasesDescriptor = allPhasesDescriptor;
                ConditionDescriptor = conditionDescriptor;
                OperationSection = operationSection;
                Timeline = timeline;
                ClockDomain = clockDomain;
            }

            public CoCoGraphId Graph { get; }
            public CoCoLayerId LowLayer { get; }
            public CoCoLayerId HighLayer { get; }
            public CoCoStateId Root { get; }
            public CoCoStateId Branch { get; }
            public CoCoStateId StateA { get; }
            public CoCoStateId StateB { get; }
            public CoCoStateId StateC { get; }
            public CoCoStateId LowRoot { get; }
            public CoCoStateId LowLeaf { get; }
            public CoCoStateId HighRoot { get; }
            public CoCoStateId HighLeaf { get; }
            public CoCoTransitionId FirstTransition { get; }
            public CoCoTransitionId SecondTransition { get; }
            public CoCoStateDescriptorId UpdateOnlyDescriptor { get; }
            public CoCoStateDescriptorId EnterUpdateDescriptor { get; }
            public CoCoStateDescriptorId UpdateExitDescriptor { get; }
            public CoCoStateDescriptorId AllPhasesDescriptor { get; }
            public CoCoConditionDescriptorId ConditionDescriptor { get; }
            public CoCoOperationSectionId OperationSection { get; }
            public CoCoTimelineId Timeline { get; }
            public CoCoClockDomainId ClockDomain { get; }

            public static ContractIds Create()
            {
                CoCoGraphId.TryCreate(High, 1UL, out CoCoGraphId graph);
                CoCoLayerId.TryCreate(High, 2UL, out CoCoLayerId lowLayer);
                CoCoLayerId.TryCreate(High, 3UL, out CoCoLayerId highLayer);
                CoCoStateId.TryCreate(High, 10UL, out CoCoStateId root);
                CoCoStateId.TryCreate(High, 11UL, out CoCoStateId branch);
                CoCoStateId.TryCreate(High, 12UL, out CoCoStateId stateA);
                CoCoStateId.TryCreate(High, 13UL, out CoCoStateId stateB);
                CoCoStateId.TryCreate(High, 14UL, out CoCoStateId stateC);
                CoCoStateId.TryCreate(High, 20UL, out CoCoStateId lowRoot);
                CoCoStateId.TryCreate(High, 21UL, out CoCoStateId lowLeaf);
                CoCoStateId.TryCreate(High, 22UL, out CoCoStateId highRoot);
                CoCoStateId.TryCreate(High, 23UL, out CoCoStateId highLeaf);
                CoCoTransitionId.TryCreate(High, 30UL, out CoCoTransitionId firstTransition);
                CoCoTransitionId.TryCreate(High, 31UL, out CoCoTransitionId secondTransition);
                CoCoStateDescriptorId.TryCreate(
                    High,
                    40UL,
                    out CoCoStateDescriptorId updateOnlyDescriptor);
                CoCoStateDescriptorId.TryCreate(
                    High,
                    41UL,
                    out CoCoStateDescriptorId enterUpdateDescriptor);
                CoCoStateDescriptorId.TryCreate(
                    High,
                    42UL,
                    out CoCoStateDescriptorId updateExitDescriptor);
                CoCoStateDescriptorId.TryCreate(
                    High,
                    43UL,
                    out CoCoStateDescriptorId allPhasesDescriptor);
                CoCoConditionDescriptorId.TryCreate(
                    High,
                    50UL,
                    out CoCoConditionDescriptorId conditionDescriptor);
                CoCoOperationSectionId.TryCreate(
                    High,
                    60UL,
                    out CoCoOperationSectionId operationSection);
                CoCoTimelineId.TryCreate(High, 70UL, out CoCoTimelineId timeline);
                CoCoClockDomainId.TryCreate(71UL, out CoCoClockDomainId clockDomain);
                return new ContractIds(
                    graph,
                    lowLayer,
                    highLayer,
                    root,
                    branch,
                    stateA,
                    stateB,
                    stateC,
                    lowRoot,
                    lowLeaf,
                    highRoot,
                    highLeaf,
                    firstTransition,
                    secondTransition,
                    updateOnlyDescriptor,
                    enterUpdateDescriptor,
                    updateExitDescriptor,
                    allPhasesDescriptor,
                    conditionDescriptor,
                    operationSection,
                    timeline,
                    clockDomain);
            }
        }
    }
}
