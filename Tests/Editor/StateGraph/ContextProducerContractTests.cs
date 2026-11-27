using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class ContextProducerContractTests
    {
        [Test]
        public void GraphStateRecordCarriesPortableActivationState()
        {
            LayerId layerId = CreateLayerId(1UL);
            StateId stateId = CreateStateId(2UL);
            ActivationId activationId = CreateActivationId(3UL);
            var memory = new TestMemoryState(17);
            Assert.IsTrue(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                1.25d,
                0.5d,
                false,
                0xABCDUL,
                memory,
                out GraphStateRecord<TestMemoryState> record));

            Assert.IsTrue(record.IsActive);
            Assert.IsTrue(record.IsValid);
            Assert.IsTrue(record.IsOnActivePath);
            Assert.AreEqual(layerId, record.LayerId);
            Assert.AreEqual(stateId, record.StateId);
            Assert.AreEqual(activationId, record.ActivationId);
            Assert.AreEqual(1.25d, record.LocalSeconds);
            Assert.AreEqual(0.5d, record.ActionProgress);
            Assert.IsFalse(record.EnterPending);
            Assert.AreEqual(0xABCDUL, record.MemoryFingerprint);
            Assert.AreEqual(memory, record.State);
            Assert.IsTrue(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                1.25d,
                0.5d,
                false,
                0xABCDUL,
                memory,
                out GraphStateRecord<TestMemoryState> equivalent));
            Assert.AreEqual(record, equivalent);
            Assert.IsFalse(default(GraphStateRecord<TestMemoryState>).IsActive);
            Assert.IsTrue(GraphStateRecord<TestMemoryState>.TryCreateInactive(
                layerId,
                stateId,
                0x1234UL,
                memory,
                out GraphStateRecord<TestMemoryState> inactive));
            Assert.IsTrue(inactive.IsValid);
            Assert.IsFalse(inactive.IsOnActivePath);
            Assert.IsFalse(inactive.ActivationId.IsValid);
            Assert.IsFalse(inactive.EnterPending);
            Assert.IsTrue(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                false,
                activationId,
                4d,
                1d,
                false,
                0x5678UL,
                memory,
                out GraphStateRecord<TestMemoryState> exited));
            Assert.IsTrue(exited.IsValid);
            Assert.IsFalse(exited.IsOnActivePath);
            Assert.AreEqual(activationId, exited.ActivationId);
            Assert.AreEqual(4d, exited.LocalSeconds);
            Assert.IsFalse(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                -1d,
                0.5d,
                false,
                0UL,
                memory,
                out _));
            Assert.IsFalse(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                false,
                default,
                0d,
                double.NaN,
                false,
                0UL,
                memory,
                out _));
            Assert.IsFalse(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                0.01d,
                0d,
                true,
                0UL,
                memory,
                out _));
            Assert.IsFalse(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                0d,
                0.01d,
                true,
                0UL,
                memory,
                out _));
            Assert.IsTrue(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                0d,
                0d,
                true,
                0UL,
                memory,
                out GraphStateRecord<TestMemoryState> enterPending));
            Assert.IsTrue(enterPending.EnterPending);
        }

        [Test]
        public void StagedOperationFrameHasNoPublicCommitOrCancelAuthority()
        {
            const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;
            Type stagedType = typeof(StagedOperationFrame);

            Assert.IsNull(stagedType.GetMethod("Commit", publicInstance));
            Assert.IsNull(stagedType.GetMethod("Cancel", publicInstance));
            Assert.IsNull(stagedType.GetMethod("CommitPrepared", publicInstance));
            Assert.IsNull(stagedType.GetMethod("CommitPreparedNoFail", publicInstance));
        }

        [Test]
        public void ActivationMemoryBindingCapturesAndPreparesCandidateWithoutLiveApply()
        {
            var binding = new TestMemoryBinding();
            var committed = new TestMemory { Value = 41 };
            var candidate = new TestMemory { Value = -1 };

            Assert.AreNotEqual(0UL, binding.SemanticFingerprint);
            Assert.IsTrue(binding.TryCapture(
                committed,
                out TestMemoryState state,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.AreEqual(41, state.Value);
            Assert.AreEqual(-1, candidate.Value);

            Assert.IsTrue(binding.TryPrepareRestore(
                state,
                candidate,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(41, candidate.Value);
            Assert.AreEqual(41, committed.Value);
        }

        [Test]
        public void GraphValueProducerReadsOnlyCallbackScopedStagedViews()
        {
            GraphInstanceId graphId = CreateGraphInstanceId(10UL);
            LayerId layerId = CreateLayerId(11UL);
            StateId stateId = CreateStateId(12UL);
            Assert.IsTrue(GraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                CreateActivationId(13UL),
                2d,
                0.75d,
                false,
                0x73UL,
                new TestMemoryState(73),
                out GraphStateRecord<TestMemoryState> record));
            var source = new StagedGraphSource(99UL, record);
            StagedGraphReadView stagedGraph = new StagedGraphReadView(source, 99UL);
            TickFrame tick = CreateTickFrame(1UL);

            ContextFrameArena arena = CreateArena(
                graphId,
                out _,
                out _,
                out _);
            StagedOperationFrame operation = CreateEmptyOperationFrame(graphId, tick);
            var context = new GraphContextCaptureContext(
                graphId,
                tick,
                arena.Previous,
                stagedGraph,
                operation);
            var producer = new TestGraphValueProducer(layerId, stateId);

            Assert.IsTrue(context.IsValid);
            Assert.IsTrue(context.StagedGraph.TryGetActiveLeaf(layerId, out StateId leaf));
            Assert.AreEqual(stateId, leaf);
            Assert.IsTrue(producer.TryProduce(
                context,
                out int value,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.AreEqual(73, value);
            Assert.AreNotEqual(0UL, producer.SemanticFingerprint);

            source.Deactivate();
            Assert.IsFalse(context.IsValid);
            Assert.IsFalse(context.StagedGraph.TryGetState<TestMemoryState>(stateId, out _));
            Assert.IsFalse(producer.TryProduce(context, out _, out diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            arena.Dispose();
        }

        [Test]
        public void ClaimStateHasCanonicalUnheldAndCompleteHeldForms()
        {
            OperatorClaimId claimId = CreateClaimId(20UL);
            OperationId operationId = CreateSectionId(21UL);
            OperatorClaimState unheld = OperatorClaimState.Unheld(claimId, operationId);
            Assert.IsFalse(unheld.IsHeld);
            Assert.IsTrue(unheld.IsValid);
            Assert.AreEqual(claimId, unheld.ClaimId);
            Assert.AreEqual(operationId, unheld.OperationId);
            Assert.IsFalse(unheld.OwnerOperatorId.IsValid);
            Assert.IsFalse(unheld.ActivationId.IsValid);
            Assert.IsFalse(default(OperatorClaimState).IsValid);

            Assert.IsTrue(OperatorClaimState.TryCreateHeld(
                claimId,
                operationId,
                CreateOperatorId(22UL),
                CreateActivationId(23UL),
                out OperatorClaimState held));
            Assert.IsTrue(held.IsHeld);
            Assert.IsTrue(held.IsValid);
            Assert.IsTrue(OperatorClaimState.TryCreateHeld(
                claimId,
                operationId,
                CreateOperatorId(22UL),
                CreateActivationId(23UL),
                out OperatorClaimState equivalent));
            Assert.AreEqual(held, equivalent);
            Assert.IsFalse(OperatorClaimState.TryCreateHeld(
                claimId,
                operationId,
                default,
                CreateActivationId(24UL),
                out _));
        }

        [Test]
        public void ActorDescriptorFreezesExactTypedSlotWhitelist()
        {
            StateSlotId intSlot = CreateSlotId(30UL);
            StateSlotId floatSlot = CreateSlotId(31UL);
            var builder = new HostContextBindingDescriptorBuilder();

            Assert.IsTrue(builder.TryProduce<int>(intSlot, out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryProduce<float>(floatSlot, out diagnostic),
                diagnostic.Message);
            Assert.IsFalse(builder.TryProduce<long>(intSlot, out diagnostic));
            Assert.AreEqual(CoCoDiagnosticDomain.Context, diagnostic.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnostic.Code);

            Assert.IsTrue(builder.TryFreeze<TestActorBinding>(
                0xA17C0UL,
                out HostContextBindingDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.IsFrozen);
            Assert.IsTrue(descriptor.IsValid);
            Assert.AreEqual(typeof(TestActorBinding), descriptor.BindingType);
            Assert.AreEqual(0xA17C0UL, descriptor.SemanticFingerprint);
            Assert.AreEqual(2, descriptor.ValueCount);
            Assert.IsTrue(descriptor.Produces<int>(intSlot));
            Assert.IsTrue(descriptor.Produces<float>(floatSlot));
            Assert.IsFalse(descriptor.Produces<long>(intSlot));

            Assert.IsFalse(builder.TryProduce<int>(CreateSlotId(32UL), out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnostic.Code);
            Assert.IsFalse(builder.TryFreeze<TestActorBinding>(
                0xA17C0UL,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnostic.Code);

            var empty = new HostContextBindingDescriptorBuilder();
            Assert.IsFalse(empty.TryFreeze<TestActorBinding>(1UL, out _, out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateBlock, diagnostic.Code);
        }

        [Test]
        public void ActorCaptureWriterEnforcesWhitelistAndExpiresWithToken()
        {
            StateSlotId declaredId = CreateSlotId(51UL);
            StateSlotId undeclaredId = CreateSlotId(41UL);
            var descriptorBuilder = new HostContextBindingDescriptorBuilder();
            Assert.IsTrue(descriptorBuilder.TryProduce<int>(
                declaredId,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(descriptorBuilder.TryFreeze<TestActorBinding>(
                0xB17DUL,
                out HostContextBindingDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            var binding = new TestActorBinding(descriptor, declaredId, 42);

            GraphInstanceId graphId = CreateGraphInstanceId(42UL);
            ContextFrameArena arena = CreateArena(
                graphId,
                out ContextFrameLayout layout,
                out StateBlockHandle block,
                out StateSlot<int> declared);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL),
                out PreparedContextCommit prepared,
                out _));
            var sink = new ActorValueSink(
                prepared,
                layout,
                block,
                binding,
                101UL);
            var writer = new HostContextWriter(binding, descriptor, sink, 101UL);
            var context = new HostContextCaptureContext(
                graphId,
                CreateTickFrame(1UL),
                arena.Previous,
                writer);

            Assert.IsTrue(context.IsValid);
            Assert.IsFalse(context.Writer.TryWrite(default(StateSlot<int>), 1));
            Assert.IsFalse(context.Writer.TryWrite(undeclaredId, 2));
            Assert.IsFalse(context.Writer.TryWrite<long>(declaredId, 3L));
            Assert.AreEqual(3, sink.RejectedWriteCount);
            Assert.IsTrue(binding.TryCapture(context, out diagnostic), diagnostic.Message);

            sink.Deactivate();
            Assert.IsFalse(context.IsValid);
            Assert.IsFalse(context.Writer.TryWrite(declaredId, 99));
            Assert.AreEqual(3, sink.RejectedWriteCount);

            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.AreEqual(42, finalized.Commit().Frame.Read(declared));
            arena.Dispose();
        }

        private static ContextFrameArena CreateArena(
            GraphInstanceId graphId,
            out ContextFrameLayout layout,
            out StateBlockHandle block,
            out StateSlot<int> slot)
        {
            StateBlockId blockId = CreateBlockId(50UL);
            StateSlotId slotId = CreateSlotId(51UL);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Host,
                out CoCoDiagnosticCode code));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                slotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                0,
                default,
                null,
                out code));
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(52UL),
                1U,
                out layout,
                out code));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out block));
            Assert.IsTrue(layout.TryResolveSlot(slotId, out slot));
            return new ContextFrameArena(graphId, layout, 2);
        }

        private static StagedOperationFrame CreateEmptyOperationFrame(
            GraphInstanceId graphId,
            TickFrame tick)
        {
            var builder = new OperationRegistryBuilder();
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(60UL),
                out OperationRegistry registry,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                graphId,
                Array.Empty<OperationRequirement>(),
                out OperationFrame frame,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(frame.TryBegin(tick, out OperationFrameWriter writer));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            return finalized.ReadView;
        }

        private static GraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static ActivationId CreateActivationId(ulong value)
        {
            Assert.IsTrue(ActivationId.TryCreate(value, out ActivationId id));
            return id;
        }

        private static LayerId CreateLayerId(ulong low)
        {
            Assert.IsTrue(LayerId.TryCreate(0UL, low, out LayerId id));
            return id;
        }

        private static StateId CreateStateId(ulong low)
        {
            Assert.IsTrue(StateId.TryCreate(0UL, low, out StateId id));
            return id;
        }

        private static StateBlockId CreateBlockId(ulong low)
        {
            Assert.IsTrue(StateBlockId.TryCreate(0UL, low, out StateBlockId id));
            return id;
        }

        private static StateSlotId CreateSlotId(ulong low)
        {
            Assert.IsTrue(StateSlotId.TryCreate(0UL, low, out StateSlotId id));
            return id;
        }

        private static FrameLayoutId CreateLayoutId(ulong low)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(0UL, low, out FrameLayoutId id));
            return id;
        }

        private static OperatorId CreateOperatorId(ulong low)
        {
            Assert.IsTrue(OperatorId.TryCreate(0UL, low, out OperatorId id));
            return id;
        }

        private static OperatorClaimId CreateClaimId(ulong low)
        {
            Assert.IsTrue(OperatorClaimId.TryCreate(0UL, low, out OperatorClaimId id));
            return id;
        }

        private static OperationId CreateSectionId(ulong low)
        {
            Assert.IsTrue(OperationId.TryCreate(0UL, low, out OperationId id));
            return id;
        }

        private static TickFrame CreateTickFrame(ulong tick)
        {
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out TimelineId timeline));
            Assert.IsTrue(TimelinePosition.TryCreate(tick, out TimelinePosition position));
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out ClockDomainId clock));
            Assert.IsTrue(TickFrame.TryCreate(
                0.016d,
                timeline,
                position,
                new TimelineTick(tick),
                clock,
                new ExecutionSequence(tick),
                new TimelineEpoch(1UL),
                out TickFrame frame,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return frame;
        }

        private readonly struct TestMemoryState : IEquatable<TestMemoryState>
        {
            public TestMemoryState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(TestMemoryState other) => Value == other.Value;
            public override bool Equals(object obj) => obj is TestMemoryState other && Equals(other);
            public override int GetHashCode() => Value;
        }

        private sealed class TestMemory : StateActivationMemory
        {
            public int Value { get; set; }
        }

        private sealed class TestMemoryBinding :
            IActivationMemoryStateBinding<TestMemory, TestMemoryState>
        {
            public ulong SemanticFingerprint => 0xC0C05UL;

            public bool TryCapture(
                TestMemory memory,
                out TestMemoryState state,
                out CoCoDiagnostic diagnostic)
            {
                if (memory == null)
                {
                    state = default;
                    diagnostic = Error("Memory is required.");
                    return false;
                }

                state = new TestMemoryState(memory.Value);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryPrepareRestore(
                in TestMemoryState state,
                TestMemory candidateMemory,
                out CoCoDiagnostic diagnostic)
            {
                if (candidateMemory == null)
                {
                    diagnostic = Error("Candidate memory is required.");
                    return false;
                }

                candidateMemory.Value = state.Value;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class TestGraphValueProducer : IGraphContextValueProducer<int>
        {
            private readonly LayerId _layerId;
            private readonly StateId _stateId;

            public TestGraphValueProducer(LayerId layerId, StateId stateId)
            {
                _layerId = layerId;
                _stateId = stateId;
            }

            public ulong SemanticFingerprint => 0xC0C06UL;

            public bool TryProduce(
                in GraphContextCaptureContext context,
                out int value,
                out CoCoDiagnostic diagnostic)
            {
                if (!context.IsValid ||
                    !context.StagedGraph.TryGetActiveLeaf(_layerId, out StateId leaf) ||
                    leaf != _stateId ||
                    !context.StagedGraph.TryGetState(
                        _stateId,
                        out GraphStateRecord<TestMemoryState> state))
                {
                    value = default;
                    diagnostic = Error("Staged State is unavailable.");
                    return false;
                }

                value = state.State.Value;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class StagedGraphSource : IStagedGraphReadSource
        {
            private readonly ulong _token;
            private readonly GraphStateRecord<TestMemoryState> _state;
            private bool _active = true;

            public StagedGraphSource(
                ulong token,
                GraphStateRecord<TestMemoryState> state)
            {
                _token = token;
                _state = state;
            }

            public bool IsActive(ulong token) => _active && token == _token;

            public bool TryGetActiveLeaf(
                ulong token,
                LayerId layerId,
                out StateId stateId)
            {
                if (IsActive(token) && layerId == _state.LayerId)
                {
                    stateId = _state.StateId;
                    return true;
                }

                stateId = default;
                return false;
            }

            public bool TryGetState<TState>(
                ulong token,
                StateId stateId,
                out GraphStateRecord<TState> state)
                where TState : unmanaged
            {
                if (IsActive(token) &&
                    stateId == _state.StateId &&
                    typeof(TState) == typeof(TestMemoryState))
                {
                    object boxed = _state;
                    state = (GraphStateRecord<TState>)boxed;
                    return true;
                }

                state = default;
                return false;
            }

            public void Deactivate()
            {
                _active = false;
            }
        }

        private sealed class TestActorBinding : IHostContextBinding
        {
            private readonly StateSlotId _slotId;
            private readonly int _value;

            public TestActorBinding(
                HostContextBindingDescriptor descriptor,
                StateSlotId slotId,
                int value)
            {
                Descriptor = descriptor;
                _slotId = slotId;
                _value = value;
            }

            public HostContextBindingDescriptor Descriptor { get; }

            public bool TryCapture(
                in HostContextCaptureContext context,
                out CoCoDiagnostic diagnostic)
            {
                if (!context.IsValid || !context.Writer.TryWrite(_slotId, _value))
                {
                    diagnostic = Error("Actor Context capture failed.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ActorValueSink : IHostContextValueSink
        {
            private readonly PreparedContextCommit _prepared;
            private readonly ContextFrameLayout _layout;
            private readonly StateBlockHandle _block;
            private readonly IHostContextBinding _binding;
            private readonly ulong _token;
            private bool _active = true;

            public ActorValueSink(
                PreparedContextCommit prepared,
                ContextFrameLayout layout,
                StateBlockHandle block,
                IHostContextBinding binding,
                ulong token)
            {
                _prepared = prepared;
                _layout = layout;
                _block = block;
                _binding = binding;
                _token = token;
            }

            public int RejectedWriteCount { get; private set; }

            public bool IsActive(
                ulong token,
                IHostContextBinding binding) =>
                _active && token == _token && ReferenceEquals(binding, _binding);

            public void RejectWrite(
                ulong token,
                IHostContextBinding binding)
            {
                if (IsActive(token, binding))
                {
                    RejectedWriteCount++;
                }
            }

            public bool TryWrite<TValue>(
                ulong token,
                IHostContextBinding binding,
                StateSlotId slotId,
                in TValue value)
                where TValue : unmanaged
            {
                return IsActive(token, binding) &&
                       _layout.TryResolveSlot(slotId, out StateSlot<TValue> slot) &&
                       _prepared.TryGetWriter(_block, out ContextFrameWriter writer) &&
                       writer.Write(slot, value);
            }

            public void Deactivate()
            {
                _active = false;
            }
        }

        private static CoCoDiagnostic Error(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.CommitPreparationFailed,
                message);
    }
}
