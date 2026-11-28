using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateGraphRuntimeContractTests
    {

        #region Public API

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void AnyUpdatesAfterLeafAndWinsBeforeOrdinaryTransition(bool allowed, bool closedWindow)
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids);
            RuntimeFixture fixture = RuntimeFixture.Create(
                AnySource(ids, closedWindow: closedWindow), CreateCatalog(false, withAny: true), control, false);
            CompiledStateLayer layer = fixture.Runtime.Graph.Layers[0];
            Assert.AreEqual(4, layer.AnyStateIndex);
            Assert.IsTrue(layer.HasAnyState);
            Assert.IsFalse(layer.States[layer.AnyStateIndex].IsLeaf);
            Assert.IsFalse(layer.States[layer.AnyStateIndex].IsRoot);
            Assert.IsEmpty(layer.States[layer.AnyStateIndex].RootPathStateIndices);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage(0.1d));
            control.Callbacks.Clear();
            control.RequestFirst = true;
            control.RequestSecond = allowed;

            fixture.Accept(fixture.Stage(0.1d));
            AssertTrace(control.Callbacks.GetRange(0, 4),
                (RuntimeContractCallbackPhase.Update, ids.Root),
                (RuntimeContractCallbackPhase.Update, ids.Branch),
                (RuntimeContractCallbackPhase.Update, ids.StateA),
                (RuntimeContractCallbackPhase.Update, ids.StateC));
            Assert.AreEqual(allowed ? 5 : 6, control.Callbacks.Count);
            Assert.AreEqual(RuntimeContractCallbackPhase.Exit, control.Callbacks[4].Phase);
            Assert.AreEqual(ids.StateA, control.Callbacks[4].StateId);
            Assert.AreEqual(allowed ? ids.StateA : ids.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);
        }

        [Test]
        public void AnySelfReentryResetsLeafAndPreservesOwnMemory()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids)
            {
                ActionProgressState = ids.StateA,
                DesiredActionProgress = 0.7d
            };
            RuntimeFixture fixture = RuntimeFixture.Create(
                AnySource(ids), CreateCatalog(false, withAny: true), control, false);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage(0.1d));
            var activations = new Dictionary<StateId, ActivationId>(control.Activations);
            control.RequestSecond = true;
            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(0, ReadCommittedMemory(fixture.Runtime, 0, ids.StateA).UpdateCount);
            Assert.AreEqual(2, ReadCommittedMemory(fixture.Runtime, 0, ids.StateC).UpdateCount);

            control.RequestSecond = false;
            control.DesiredActionProgress = 0d;
            control.Callbacks.Clear();
            fixture.Accept(fixture.Stage(0.1d));
            AssertTrace(control.Callbacks,
                (RuntimeContractCallbackPhase.Enter, ids.StateA),
                (RuntimeContractCallbackPhase.Update, ids.Root),
                (RuntimeContractCallbackPhase.Update, ids.Branch),
                (RuntimeContractCallbackPhase.Update, ids.StateA),
                (RuntimeContractCallbackPhase.Update, ids.StateC));
            Assert.AreEqual(activations[ids.Root], control.Activations[ids.Root]);
            Assert.AreEqual(activations[ids.Branch], control.Activations[ids.Branch]);
            Assert.AreEqual(activations[ids.StateC], control.Activations[ids.StateC]);
            Assert.AreNotEqual(activations[ids.StateA], control.Activations[ids.StateA]);
            Assert.AreEqual(0d, control.Callbacks[3].PreviousLocalSeconds);
            Assert.AreEqual(0d, control.Callbacks[3].PreviousActionProgress);
            Assert.AreEqual(0.2d, control.Callbacks[4].PreviousLocalSeconds, 0.000001d);
            Assert.AreEqual(1, ReadCommittedMemory(fixture.Runtime, 0, ids.StateA).UpdateCount);
            Assert.AreEqual(3, ReadCommittedMemory(fixture.Runtime, 0, ids.StateC).UpdateCount);
        }

        [Test]
        public void AnyOperationDepthAndRejectedTickPreserveContract()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids) { WriteOperations = true, WriteOnExit = true };
            control.ConfigureOperationStates(ids.Root, ids.Branch, ids.StateA, ids.StateC);
            RuntimeFixture fixture = RuntimeFixture.Create(
                AnySource(ids), CreateCatalog(true, withAny: true), control, true);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage(0.1d));
            control.RequestSecond = true;
            StagedGraphStep rejected = fixture.Stage(0.1d);
            Assert.IsTrue(rejected.OperationFrame.TryGet(fixture.OperationHandle,
                out OperationEntry<IRuntimeContractOperationSection> entry));
            Assert.AreEqual(40, entry.View.Value, "Later ordinary Exit writes cannot override AnyState depth.");
            Assert.IsTrue(fixture.Runtime.TryRejectStagedStep(rejected, CoCoDiagnostic.None, false, out _));
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(1, ReadCommittedMemory(fixture.Runtime, 0, ids.StateA).UpdateCount);
            Assert.AreEqual(1, ReadCommittedMemory(fixture.Runtime, 0, ids.StateC).UpdateCount);

            control.RequestSecond = false;
            control.RequestFirst = true;
            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(ids.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            control.ConfigureOperationStates(ids.Root, ids.StateB, ids.StateA, ids.StateC);
            StagedGraphStep shallower = fixture.Stage(0.1d);
            Assert.IsTrue(shallower.OperationFrame.TryGet(fixture.OperationHandle, out entry));
            Assert.AreEqual(40, entry.View.Value);
            fixture.Accept(shallower);
            Assert.AreEqual(3, ReadCommittedMemory(fixture.Runtime, 0, ids.StateC).UpdateCount);
        }

        [TestCase(1, CoCoDiagnosticCode.DescriptorTypeMismatch)]
        [TestCase(2, CoCoDiagnosticCode.InvalidInitialState)]
        [TestCase(3, CoCoDiagnosticCode.NonLeafTransitionEndpoint)]
        public void AnyRejectsOrdinaryLogicHierarchyAndIncomingEdges(int invalidKind, CoCoDiagnosticCode code)
        {
            ContractIds ids = ContractIds.Create();
            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                AnySource(ids, invalidKind), CreateCatalog(false, withAny: invalidKind != 1));
            Assert.IsFalse(result.Succeeded);
            Assert.That(result.Diagnostics, Has.Some.Property("Diagnostic").Property("Code").EqualTo(code));
        }

        [Test]
        public void OptionalLifecyclePhasesPreserveEnterUpdateExitPathOrder()
        {
            ContractIds ids = ContractIds.Create();
            var control = new RuntimeContractControl(ids);
            StateGraphSource source = LifecycleSource(ids);
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
            StagedGraphStep transition = fixture.Stage(0.1d);
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
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.ActionProgress,
                0.4d,
                0.5d,
                out TransitionWindow window));
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
            StagedGraphStep rejected = fixture.Stage(0.1d);
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
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.ActionProgress,
                0.8d,
                1d,
                out TransitionWindow window));
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
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.LocalSeconds,
                0.2d,
                0.3d,
                out TransitionWindow window));
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
        public void HighestRequestedPriorityWinsAndUnrequestedEdgesAreSkipped()
        {
            ContractIds ids = ContractIds.Create();
            StateGraphSource source = ArbitrationSource(ids);
            GraphDescriptorCatalog catalog = CreateCatalog(false);

            var allAccepted = new RuntimeContractControl(ids)
            {
                RequestFirst = true,
                RequestSecond = true
            };
            RuntimeFixture highWins = RuntimeFixture.Create(source, catalog, allAccepted, false);
            Assert.IsTrue(highWins.Runtime.TryStart(out _));
            highWins.Accept(highWins.Stage(0.1d));
            Assert.AreEqual(ids.StateC, highWins.Runtime.GetActivePath(0).ActiveLeaf,
                "When both edges are requested, the higher priority must win.");

            var onlyLowRequested = new RuntimeContractControl(ids)
            {
                RequestFirst = true,
                RequestSecond = false
            };
            RuntimeFixture lowWins = RuntimeFixture.Create(source, catalog, onlyLowRequested, false);
            Assert.IsTrue(lowWins.Runtime.TryStart(out _));
            lowWins.Accept(lowWins.Stage(0.1d));
            Assert.AreEqual(ids.StateB, lowWins.Runtime.GetActivePath(0).ActiveLeaf,
                "An unrequested higher-priority edge cannot suppress the requested edge.");
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

            StagedGraphStep first = fixture.Stage(0.1d);
            Assert.IsTrue(first.OperationFrame.TryGet(
                fixture.OperationHandle,
                out OperationEntry<IRuntimeContractOperationSection> entry));
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
        public void PreparedCommitDoesNotConsumeDiscreteOperationSequenceUntilBarrier()
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
                CreateCatalog(true, Mode.Discrete),
                control,
                true,
                Mode.Discrete);
            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic start), start.Message);

            StagedGraphStep first = fixture.Stage(0.1d);
            Assert.IsTrue(first.OperationFrame.TryGet(
                fixture.OperationHandle,
                out OperationEntry<IRuntimeContractOperationSection> firstEntry));
            Assert.AreEqual(1UL, firstEntry.Header.OperationSequence.Value);
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                first,
                null,
                out PreparedGraphCommit prepared,
                out CoCoDiagnostic prepare), prepare.Message);
            Assert.AreEqual(0UL, fixture.Runtime.Clock.Tick.Value);
            Assert.IsTrue(fixture.Runtime.TryCancelStagedStep(
                first,
                out CoCoDiagnostic cancel), cancel.Message);
            Assert.IsFalse(prepared.IsValid);

            StagedGraphStep retry = fixture.Stage(0.1d);
            Assert.IsTrue(retry.OperationFrame.TryGet(
                fixture.OperationHandle,
                out OperationEntry<IRuntimeContractOperationSection> retryEntry));
            Assert.AreEqual(
                1UL,
                retryEntry.Header.OperationSequence.Value,
                "Cancelling a prepared candidate must not consume its OperationSequence.");
            fixture.Accept(retry);

            StagedGraphStep following = fixture.Stage(0.1d);
            Assert.IsTrue(following.OperationFrame.TryGet(
                fixture.OperationHandle,
                out OperationEntry<IRuntimeContractOperationSection> followingEntry));
            Assert.AreEqual(2UL, followingEntry.Header.OperationSequence.Value);
            Assert.IsTrue(fixture.Runtime.TryCancelStagedStep(following, out _));
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
                CreateCatalog(true, Mode.Discrete),
                control,
                true,
                Mode.Discrete);
            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic start), start.Message);

            fixture.Accept(fixture.Stage(0.1d));
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(1, ReadCommittedMemory(fixture.Runtime, 0, ids.StateA).UpdateCount);

            FieldInfo sequencesField = typeof(OperationFrame).GetField(
                "_committedSequences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(sequencesField);
            var sequences = (ulong[])sequencesField.GetValue(fixture.OperationFrame);
            sequences[fixture.OperationHandle.DenseIndex] = ulong.MaxValue;

            Assert.IsTrue(fixture.Runtime.TryPreviewNextTick(
                0.1d,
                1d,
                out TickFrame tick,
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

        #endregion

        #region Internal Logic

        private static long _nextGraphInstance = 3000L;

        private static StateGraphSource AnySource(
            ContractIds ids,
            int invalidKind = 0,
            bool closedWindow = false)
        {
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.LocalSeconds, 10d, 11d, out TransitionWindow laterWindow));
            var layer = new StateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.Root, default, ids.Branch, ids.AllPhasesDescriptor),
                    State(ids.Branch, ids.Root, ids.StateA, ids.AllPhasesDescriptor),
                    State(ids.StateA, ids.Branch, default, ids.AllPhasesDescriptor),
                    State(ids.StateB, ids.Root, default, ids.AllPhasesDescriptor)
                },
                new[]
                {
                    Transition(ids.FirstTransition, ids.StateA, ids.StateB, 100,
                        TransitionWindow.Always),
                    Transition(ids.SecondTransition, ids.StateC, invalidKind == 3 ? ids.StateC : ids.StateA, 0,
                        closedWindow ? laterWindow : TransitionWindow.Always)
                },
                State(ids.StateC, invalidKind == 2 ? ids.Root : default, default, ids.UpdateOnlyDescriptor));
            return Source(ids, 0xC0407UL, layer);
        }

        private static StateGraphSource LifecycleSource(ContractIds ids)
        {
            var transition = Transition(
                ids.FirstTransition,
                ids.StateA,
                ids.StateB,
                10,
                TransitionWindow.Always);
            var layer = new StateLayerSource(
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

        private static StateGraphSource WindowSource(
            ContractIds ids,
            TransitionWindow window)
        {
            var layer = new StateLayerSource(
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
                        window)
                });
            return Source(ids, 0xC0402UL, layer);
        }

        private static StateGraphSource ArbitrationSource(ContractIds ids)
        {
            var layer = new StateLayerSource(
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
                        TransitionWindow.Always),
                    Transition(
                        ids.SecondTransition,
                        ids.StateA,
                        ids.StateC,
                        20,
                        TransitionWindow.Always)
                });
            return Source(ids, 0xC0403UL, layer);
        }

        private static StateGraphSource ZeroOutgoingSource(ContractIds ids)
        {
            var layer = new StateLayerSource(
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
                        TransitionWindow.Always)
                });
            return Source(ids, 0xC0404UL, layer);
        }

        private static StateGraphSource OperationCompositionSource(ContractIds ids)
        {
            var low = new StateLayerSource(
                ids.LowLayer,
                ids.LowRoot,
                new[]
                {
                    State(ids.LowLeaf, ids.LowRoot, default, ids.UpdateOnlyDescriptor),
                    State(ids.LowRoot, default, ids.LowLeaf, ids.UpdateOnlyDescriptor)
                },
                Array.Empty<TransitionSource>());
            var high = new StateLayerSource(
                ids.HighLayer,
                ids.HighRoot,
                new[]
                {
                    State(ids.HighLeaf, ids.HighRoot, default, ids.UpdateOnlyDescriptor),
                    State(ids.HighRoot, default, ids.HighLeaf, ids.UpdateOnlyDescriptor)
                },
                Array.Empty<TransitionSource>());
            return Source(ids, 0xC0405UL, low, high);
        }

        private static StateGraphSource OperationFinalizeFailureSource(ContractIds ids)
        {
            var layer = new StateLayerSource(
                ids.LowLayer,
                ids.Root,
                new[]
                {
                    State(ids.StateA, ids.Root, default, ids.UpdateOnlyDescriptor),
                    State(ids.Root, default, ids.StateA, ids.UpdateOnlyDescriptor)
                },
                Array.Empty<TransitionSource>());
            return Source(ids, 0xC0406UL, layer);
        }

        private static StateGraphSource Source(
            ContractIds ids,
            ulong fingerprint,
            params StateLayerSource[] layers) =>
            new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion,
                fingerprint,
                ids.Graph,
                layers,
                Array.Empty<EventToIntentDeclarationSource>());

        private static StateSource State(
            StateId stateId,
            StateId parentId,
            StateId initialChildId,
            StateDescriptorId descriptorId) =>
            new StateSource(
                stateId,
                parentId,
                initialChildId,
                descriptorId,
                StateConfig(1));

        private static TransitionSource Transition(
            TransitionId id,
            StateId source,
            StateId target,
            int priority,
            TransitionWindow window) =>
            new TransitionSource(
                id,
                source,
                target,
                priority,
                window);

        private static GraphDescriptorCatalog CreateCatalog(
            bool withOperation,
            Mode operationMode = Mode.Continuous,
            bool withAny = false)
        {
            ContractIds ids = ContractIds.Create();
            var builder = new GraphDescriptorCatalogBuilder();
            OperationId[] operations = null;
            if (withOperation)
            {
                Assert.IsTrue(builder.TryRegisterOperation(
                    ids.Operation,
                    operationMode,
                    new OperationViewFactoryToken<
                        IRuntimeContractOperationSection,
                        RuntimeContractOperationSectionViewFactory>(0xC040UL),
                    out CoCoDiagnostic operationDiagnostic), operationDiagnostic.Message);
                operations = new[] { ids.Operation };
            }

            if (withAny)
            {
                RegisterState<RuntimeContractAnyLogic>(builder, ids.UpdateOnlyDescriptor, operations);
            }
            else
            {
                RegisterState<RuntimeContractUpdateOnlyLogic>(builder, ids.UpdateOnlyDescriptor, operations);
            }
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
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return catalog;
        }

        private static RuntimeContractMemory ReadCommittedMemory(
            StateGraphInstance runtime,
            int layerIndex,
            StateId stateId)
        {
            FieldInfo layersField = typeof(StateGraphInstance).GetField(
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
            IReadOnlyList<CompiledState> compiledStates = runtime.Graph.Layers[layerIndex].States;
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
            GraphDescriptorCatalogBuilder builder,
            StateDescriptorId descriptorId,
            OperationId[] operations)
            where TLogic : StateLogic
        {
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new RuntimeContractStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TLogic,
                    RuntimeContractStateConfigSchema,
                    RuntimeContractMemory>(RuntimeContractSchemas.State, true),
                null,
                operations,
                null,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static FrozenConfigSnapshot StateConfig(int value)
        {
            FrozenConfigWriter<RuntimeContractStateConfigSchema> writer =
                RuntimeContractSchemas.State.CreateWriter();
            Assert.IsTrue(writer.TryWrite(RuntimeContractSchemas.StateValue, value, out _));
            Assert.IsTrue(writer.TrySeal(out FrozenConfigSnapshot snapshot, out _));
            return snapshot;
        }

        private static void AssertTrace(
            IReadOnlyList<CallbackRecord> actual,
            params (RuntimeContractCallbackPhase Phase, StateId StateId)[] expected)
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
                StateGraphInstance runtime,
                OperationFrame operationFrame,
                OperationHandle<IRuntimeContractOperationSection> operationHandle)
            {
                Runtime = runtime;
                OperationFrame = operationFrame;
                OperationHandle = operationHandle;
            }

            public StateGraphInstance Runtime { get; }
            public OperationFrame OperationFrame { get; }
            public OperationHandle<IRuntimeContractOperationSection> OperationHandle { get; }

            public static RuntimeFixture Create(
                StateGraphSource source,
                GraphDescriptorCatalog catalog,
                RuntimeContractControl control,
                bool withOperation,
                Mode operationMode = Mode.Continuous)
            {
                StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);
                Assert.IsTrue(result.Succeeded, JoinDiagnostics(result.Diagnostics));
                CompiledStateGraph graph = result.Graph;

                var registryBuilder = new OperationRegistryBuilder();
                OperationRequirement operationRequirement = default;
                if (withOperation)
                {
                    Assert.IsTrue(registryBuilder.TryRegister(
                        control.Ids.Operation,
                        operationMode,
                        new RuntimeContractOperationSectionViewFactory(),
                        out operationRequirement,
                        out CoCoDiagnostic registerDiagnostic), registerDiagnostic.Message);
                }

                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out OperationRegistry registry,
                    out CoCoDiagnostic registryDiagnostic), registryDiagnostic.Message);
                OperationHandle<IRuntimeContractOperationSection> operationHandle = default;
                if (withOperation)
                {
                    Assert.IsTrue(registry.TryResolve(operationRequirement, out operationHandle));
                    Assert.IsTrue(registry.TryResolveField(
                        operationHandle,
                        0,
                        out OperationField<int> valueField));
                    control.OperationField = valueField;
                    control.OperationHandle = operationHandle;
                }

                long instanceValue = Interlocked.Increment(ref _nextGraphInstance);
                Assert.IsTrue(GraphInstanceId.TryCreate(
                    (ulong)instanceValue,
                    out GraphInstanceId graphInstanceId));
                IReadOnlyList<OperationRequirement> requirements = withOperation
                    ? new[] { operationRequirement }
                    : Array.Empty<OperationRequirement>();
                Assert.IsTrue(OperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    requirements,
                    out OperationFrame operationFrame,
                    out CoCoDiagnostic frameDiagnostic), frameDiagnostic.Message);
                Assert.IsTrue(InstanceClock.TryCreate(
                    control.Ids.Timeline,
                    control.Ids.ClockDomain,
                    new TimelineEpoch(1UL),
                    out InstanceClock clock,
                    out CoCoDiagnostic clockDiagnostic), clockDiagnostic.Message);

                StateGraphLogicBindings bindings = CreateBindings(graph, control);
                Assert.IsTrue(StateGraphInstance.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out StateGraphInstance runtime,
                    out CoCoDiagnostic runtimeDiagnostic), runtimeDiagnostic.Message);
                return new RuntimeFixture(runtime, operationFrame, operationHandle);
            }

            public StagedGraphStep Stage(double delta)
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    delta,
                    1d,
                    out TickFrame tick,
                    out CoCoDiagnostic previewDiagnostic), previewDiagnostic.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out StagedGraphStep staged,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                return staged;
            }

            public void Accept(StagedGraphStep staged)
            {
                Assert.IsTrue(Runtime.TryAcceptStagedStep(
                    staged,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            public bool TryStageAndAccept(double delta) =>
                Runtime.TryPreviewNextTick(delta, 1d, out TickFrame tick, out _) &&
                Runtime.TryStageStep(tick, null, default, out StagedGraphStep staged, out _) &&
                Runtime.TryAcceptStagedStep(staged, out _);

            private static StateGraphLogicBindings CreateBindings(
                CompiledStateGraph graph,
                RuntimeContractControl control)
            {
                bool updateOnly = false;
                bool anyState = false;
                bool enterUpdate = false;
                bool updateExit = false;
                bool allPhases = false;
                for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
                {
                    CompiledStateLayer layer = graph.Layers[layerIndex];
                    for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                    {
                        StateDescriptorId descriptorId = layer.States[stateIndex].Descriptor.DescriptorId;
                        anyState |= layer.States[stateIndex].IsAnyState;
                        updateOnly |= !layer.States[stateIndex].IsAnyState &&
                                      descriptorId == control.Ids.UpdateOnlyDescriptor;
                        enterUpdate |= descriptorId == control.Ids.EnterUpdateDescriptor;
                        updateExit |= descriptorId == control.Ids.UpdateExitDescriptor;
                        allPhases |= descriptorId == control.Ids.AllPhasesDescriptor;
                    }

                }

                var builder = new StateGraphLogicBindingsBuilder(graph);
                if (anyState)
                {
                    BindState(
                        builder,
                        control.Ids.UpdateOnlyDescriptor,
                        new StateRuntimeFactory<RuntimeContractAnyLogic, RuntimeContractMemory>(
                            context => new RuntimeContractAnyLogic(context, control),
                            CreateMemory,
                            CopyMemory,
                            ResetMemory,
                            FingerprintMemory));
                }

                if (updateOnly)
                {
                    BindState(
                        builder,
                        control.Ids.UpdateOnlyDescriptor,
                        new StateRuntimeFactory<
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
                        new StateRuntimeFactory<
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
                        new StateRuntimeFactory<
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
                        new StateRuntimeFactory<
                            RuntimeContractEnterUpdateExitLogic,
                            RuntimeContractMemory>(
                            context => new RuntimeContractEnterUpdateExitLogic(context, control),
                            CreateMemory,
                            CopyMemory,
                            ResetMemory,
                            FingerprintMemory));
                }

                Assert.IsTrue(builder.TryFreeze(
                    out StateGraphLogicBindings bindings,
                    out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
                return bindings;
            }

            private static void BindState(
                StateGraphLogicBindingsBuilder builder,
                StateDescriptorId descriptorId,
                IStateRuntimeFactory factory)
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

            private static string JoinDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
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
            private StateId _lowRoot;
            private StateId _lowLeaf;
            private StateId _highRoot;
            private StateId _highLeaf;

            public RuntimeContractControl(ContractIds ids)
            {
                Ids = ids;
            }

            public ContractIds Ids { get; }
            public List<CallbackRecord> Callbacks { get; } = new List<CallbackRecord>();
            public Dictionary<StateId, ActivationId> Activations { get; } =
                new Dictionary<StateId, ActivationId>();
            public bool RecordCallbacks = true;
            public bool RequestFirst;
            public bool RequestSecond;
            public bool WriteOperations;
            public bool WriteOnExit;
            public bool EnableDiscreteOperation;
            public int TransitionRequestCheckCount;
            public StateId ActionProgressState;
            public double DesiredActionProgress;
            public OperationField<int> OperationField;
            public OperationHandle<IRuntimeContractOperationSection> OperationHandle;

            public void ConfigureOperationStates(
                StateId lowRoot,
                StateId lowLeaf,
                StateId highRoot,
                StateId highLeaf)
            {
                _lowRoot = lowRoot;
                _lowLeaf = lowLeaf;
                _highRoot = highRoot;
                _highLeaf = highLeaf;
            }

            public bool ShouldRequest(TransitionId transitionId)
            {
                TransitionRequestCheckCount++;
                return transitionId == Ids.FirstTransition
                    ? RequestFirst
                    : transitionId == Ids.SecondTransition && RequestSecond;
            }

            public double GetActionProgress(StateId stateId, double currentValue) =>
                stateId == ActionProgressState ? DesiredActionProgress : currentValue;

            public void OnStateCallback(
                RuntimeContractCallbackPhase phase,
                StateId stateId,
                StateExecutionContext context)
            {
                if (RecordCallbacks)
                {
                    Activations[stateId] = context.ActivationId;
                    Callbacks.Add(new CallbackRecord(
                        phase,
                        stateId,
                        context.PreviousLocalSeconds,
                        context.LocalSeconds,
                        context.PreviousActionProgress,
                        context.ActionProgress));
                }

                if (WriteOperations &&
                    (phase == RuntimeContractCallbackPhase.Update ||
                     WriteOnExit && phase == RuntimeContractCallbackPhase.Exit) &&
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
                StateId stateId,
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
            public StateId StateId { get; }
            public double PreviousLocalSeconds { get; }
            public double LocalSeconds { get; }
            public double PreviousActionProgress { get; }
            public double ActionProgress { get; }
        }

        private readonly struct ContractIds
        {
            private const ulong High = 0xC04UL;

            private ContractIds(
                GraphId graph,
                LayerId lowLayer,
                LayerId highLayer,
                StateId root,
                StateId branch,
                StateId stateA,
                StateId stateB,
                StateId stateC,
                StateId lowRoot,
                StateId lowLeaf,
                StateId highRoot,
                StateId highLeaf,
                TransitionId firstTransition,
                TransitionId secondTransition,
                StateDescriptorId updateOnlyDescriptor,
                StateDescriptorId enterUpdateDescriptor,
                StateDescriptorId updateExitDescriptor,
                StateDescriptorId allPhasesDescriptor,
                OperationId operationSection,
                TimelineId timeline,
                ClockDomainId clockDomain)
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
                Operation = operationSection;
                Timeline = timeline;
                ClockDomain = clockDomain;
            }

            public GraphId Graph { get; }
            public LayerId LowLayer { get; }
            public LayerId HighLayer { get; }
            public StateId Root { get; }
            public StateId Branch { get; }
            public StateId StateA { get; }
            public StateId StateB { get; }
            public StateId StateC { get; }
            public StateId LowRoot { get; }
            public StateId LowLeaf { get; }
            public StateId HighRoot { get; }
            public StateId HighLeaf { get; }
            public TransitionId FirstTransition { get; }
            public TransitionId SecondTransition { get; }
            public StateDescriptorId UpdateOnlyDescriptor { get; }
            public StateDescriptorId EnterUpdateDescriptor { get; }
            public StateDescriptorId UpdateExitDescriptor { get; }
            public StateDescriptorId AllPhasesDescriptor { get; }
            public OperationId Operation { get; }
            public TimelineId Timeline { get; }
            public ClockDomainId ClockDomain { get; }

            public static ContractIds Create()
            {
                GraphId.TryCreate(High, 1UL, out GraphId graph);
                LayerId.TryCreate(High, 2UL, out LayerId lowLayer);
                LayerId.TryCreate(High, 3UL, out LayerId highLayer);
                StateId.TryCreate(High, 10UL, out StateId root);
                StateId.TryCreate(High, 11UL, out StateId branch);
                StateId.TryCreate(High, 12UL, out StateId stateA);
                StateId.TryCreate(High, 13UL, out StateId stateB);
                StateId.TryCreate(High, 14UL, out StateId stateC);
                StateId.TryCreate(High, 20UL, out StateId lowRoot);
                StateId.TryCreate(High, 21UL, out StateId lowLeaf);
                StateId.TryCreate(High, 22UL, out StateId highRoot);
                StateId.TryCreate(High, 23UL, out StateId highLeaf);
                TransitionId.TryCreate(High, 30UL, out TransitionId firstTransition);
                TransitionId.TryCreate(High, 31UL, out TransitionId secondTransition);
                StateDescriptorId.TryCreate(
                    High,
                    40UL,
                    out StateDescriptorId updateOnlyDescriptor);
                StateDescriptorId.TryCreate(
                    High,
                    41UL,
                    out StateDescriptorId enterUpdateDescriptor);
                StateDescriptorId.TryCreate(
                    High,
                    42UL,
                    out StateDescriptorId updateExitDescriptor);
                StateDescriptorId.TryCreate(
                    High,
                    43UL,
                    out StateDescriptorId allPhasesDescriptor);
                OperationId.TryCreate(
                    High,
                    60UL,
                    out OperationId operationSection);
                TimelineId.TryCreate(High, 70UL, out TimelineId timeline);
                ClockDomainId.TryCreate(71UL, out ClockDomainId clockDomain);
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
                    operationSection,
                    timeline,
                    clockDomain);
            }
        }

        #endregion
    }
}
