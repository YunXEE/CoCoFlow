using System;
using System.Reflection;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoContextProducerContractTests
    {
        [Test]
        public void GraphStateRecordCarriesPortableActivationState()
        {
            CoCoLayerId layerId = CreateLayerId(1UL);
            CoCoStateId stateId = CreateStateId(2UL);
            CoCoActivationId activationId = CreateActivationId(3UL);
            var memory = new TestMemoryState(17);
            Assert.IsTrue(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                1.25d,
                0.5d,
                false,
                0xABCDUL,
                memory,
                out CoCoGraphStateRecord<TestMemoryState> record));

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
            Assert.IsTrue(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                1.25d,
                0.5d,
                false,
                0xABCDUL,
                memory,
                out CoCoGraphStateRecord<TestMemoryState> equivalent));
            Assert.AreEqual(record, equivalent);
            Assert.IsFalse(default(CoCoGraphStateRecord<TestMemoryState>).IsActive);
            Assert.IsTrue(CoCoGraphStateRecord<TestMemoryState>.TryCreateInactive(
                layerId,
                stateId,
                0x1234UL,
                memory,
                out CoCoGraphStateRecord<TestMemoryState> inactive));
            Assert.IsTrue(inactive.IsValid);
            Assert.IsFalse(inactive.IsOnActivePath);
            Assert.IsFalse(inactive.ActivationId.IsValid);
            Assert.IsFalse(inactive.EnterPending);
            Assert.IsTrue(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                false,
                activationId,
                4d,
                1d,
                false,
                0x5678UL,
                memory,
                out CoCoGraphStateRecord<TestMemoryState> exited));
            Assert.IsTrue(exited.IsValid);
            Assert.IsFalse(exited.IsOnActivePath);
            Assert.AreEqual(activationId, exited.ActivationId);
            Assert.AreEqual(4d, exited.LocalSeconds);
            Assert.IsFalse(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
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
            Assert.IsFalse(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
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
            Assert.IsFalse(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
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
            Assert.IsFalse(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
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
            Assert.IsTrue(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                activationId,
                0d,
                0d,
                true,
                0UL,
                memory,
                out CoCoGraphStateRecord<TestMemoryState> enterPending));
            Assert.IsTrue(enterPending.EnterPending);
        }

        [Test]
        public void StagedOperationFrameHasNoPublicCommitOrCancelAuthority()
        {
            const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;
            Type stagedType = typeof(CoCoStagedOperationFrame);

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
            CoCoGraphInstanceId graphId = CreateGraphInstanceId(10UL);
            CoCoLayerId layerId = CreateLayerId(11UL);
            CoCoStateId stateId = CreateStateId(12UL);
            Assert.IsTrue(CoCoGraphStateRecord<TestMemoryState>.TryCreate(
                layerId,
                stateId,
                true,
                CreateActivationId(13UL),
                2d,
                0.75d,
                false,
                0x73UL,
                new TestMemoryState(73),
                out CoCoGraphStateRecord<TestMemoryState> record));
            var source = new StagedGraphSource(99UL, record);
            CoCoStagedGraphReadView stagedGraph = new CoCoStagedGraphReadView(source, 99UL);
            CoCoTickFrame tick = CreateTickFrame(1UL);

            CoCoContextFrameArena arena = CreateArena(
                graphId,
                out _,
                out _,
                out _);
            CoCoStagedOperationFrame operation = CreateEmptyOperationFrame(graphId, tick);
            var context = new CoCoGraphContextCaptureContext(
                graphId,
                tick,
                arena.Previous,
                stagedGraph,
                operation);
            var producer = new TestGraphValueProducer(layerId, stateId);

            Assert.IsTrue(context.IsValid);
            Assert.IsTrue(context.StagedGraph.TryGetActiveLeaf(layerId, out CoCoStateId leaf));
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
            CoCoOperatorClaimId claimId = CreateClaimId(20UL);
            CoCoOperationSectionId sectionId = CreateSectionId(21UL);
            CoCoOperatorClaimState unheld = CoCoOperatorClaimState.Unheld(claimId, sectionId);
            Assert.IsFalse(unheld.IsHeld);
            Assert.IsTrue(unheld.IsValid);
            Assert.AreEqual(claimId, unheld.ClaimId);
            Assert.AreEqual(sectionId, unheld.SectionId);
            Assert.IsFalse(unheld.OwnerOperatorId.IsValid);
            Assert.IsFalse(unheld.ActivationId.IsValid);
            Assert.IsFalse(default(CoCoOperatorClaimState).IsValid);

            Assert.IsTrue(CoCoOperatorClaimState.TryCreateHeld(
                claimId,
                sectionId,
                CreateOperatorId(22UL),
                CreateActivationId(23UL),
                out CoCoOperatorClaimState held));
            Assert.IsTrue(held.IsHeld);
            Assert.IsTrue(held.IsValid);
            Assert.IsTrue(CoCoOperatorClaimState.TryCreateHeld(
                claimId,
                sectionId,
                CreateOperatorId(22UL),
                CreateActivationId(23UL),
                out CoCoOperatorClaimState equivalent));
            Assert.AreEqual(held, equivalent);
            Assert.IsFalse(CoCoOperatorClaimState.TryCreateHeld(
                claimId,
                sectionId,
                default,
                CreateActivationId(24UL),
                out _));
        }

        [Test]
        public void ActorDescriptorFreezesExactTypedSlotWhitelist()
        {
            CoCoStateSlotId intSlot = CreateSlotId(30UL);
            CoCoStateSlotId floatSlot = CreateSlotId(31UL);
            var builder = new CoCoActorContextBindingDescriptorBuilder();

            Assert.IsTrue(builder.TryProduce<int>(intSlot, out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryProduce<float>(floatSlot, out diagnostic),
                diagnostic.Message);
            Assert.IsFalse(builder.TryProduce<long>(intSlot, out diagnostic));
            Assert.AreEqual(CoCoDiagnosticDomain.Context, diagnostic.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnostic.Code);

            Assert.IsTrue(builder.TryFreeze<TestActorBinding>(
                0xA17C0UL,
                out CoCoActorContextBindingDescriptor descriptor,
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

            var empty = new CoCoActorContextBindingDescriptorBuilder();
            Assert.IsFalse(empty.TryFreeze<TestActorBinding>(1UL, out _, out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateBlock, diagnostic.Code);
        }

        [Test]
        public void ActorCaptureWriterEnforcesWhitelistAndExpiresWithToken()
        {
            CoCoStateSlotId declaredId = CreateSlotId(51UL);
            CoCoStateSlotId undeclaredId = CreateSlotId(41UL);
            var descriptorBuilder = new CoCoActorContextBindingDescriptorBuilder();
            Assert.IsTrue(descriptorBuilder.TryProduce<int>(
                declaredId,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(descriptorBuilder.TryFreeze<TestActorBinding>(
                0xB17DUL,
                out CoCoActorContextBindingDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            var binding = new TestActorBinding(descriptor, declaredId, 42);

            CoCoGraphInstanceId graphId = CreateGraphInstanceId(42UL);
            CoCoContextFrameArena arena = CreateArena(
                graphId,
                out CoCoContextFrameLayout layout,
                out CoCoStateBlockHandle block,
                out CoCoStateSlot<int> declared);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            var sink = new ActorValueSink(
                prepared,
                layout,
                block,
                binding,
                101UL);
            var writer = new CoCoActorContextWriter(binding, descriptor, sink, 101UL);
            var context = new CoCoActorContextCaptureContext(
                graphId,
                CreateTickFrame(1UL),
                arena.Previous,
                writer);

            Assert.IsTrue(context.IsValid);
            Assert.IsFalse(context.Writer.TryWrite(default(CoCoStateSlot<int>), 1));
            Assert.IsFalse(context.Writer.TryWrite(undeclaredId, 2));
            Assert.IsFalse(context.Writer.TryWrite<long>(declaredId, 3L));
            Assert.AreEqual(3, sink.RejectedWriteCount);
            Assert.IsTrue(binding.TryCapture(context, out diagnostic), diagnostic.Message);

            sink.Deactivate();
            Assert.IsFalse(context.IsValid);
            Assert.IsFalse(context.Writer.TryWrite(declaredId, 99));
            Assert.AreEqual(3, sink.RejectedWriteCount);

            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
            Assert.AreEqual(42, finalized.Commit().Frame.Read(declared));
            arena.Dispose();
        }

        private static CoCoContextFrameArena CreateArena(
            CoCoGraphInstanceId graphId,
            out CoCoContextFrameLayout layout,
            out CoCoStateBlockHandle block,
            out CoCoStateSlot<int> slot)
        {
            CoCoStateBlockId blockId = CreateBlockId(50UL);
            CoCoStateSlotId slotId = CreateSlotId(51UL);
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode code));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                slotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
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
            return new CoCoContextFrameArena(graphId, layout, 2);
        }

        private static CoCoStagedOperationFrame CreateEmptyOperationFrame(
            CoCoGraphInstanceId graphId,
            CoCoTickFrame tick)
        {
            var builder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(60UL),
                out CoCoOperationSectionRegistry registry,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoOperationFrame.TryCreate(
                registry,
                graphId,
                Array.Empty<CoCoOperationSectionRequirement>(),
                out CoCoOperationFrame frame,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(frame.TryBegin(tick, out CoCoOperationFrameWriter writer));
            Assert.IsTrue(writer.TryFinalize(out CoCoFinalizedOperationFrame finalized));
            return new CoCoStagedOperationFrame(finalized);
        }

        private static CoCoGraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoActivationId CreateActivationId(ulong value)
        {
            Assert.IsTrue(CoCoActivationId.TryCreate(value, out CoCoActivationId id));
            return id;
        }

        private static CoCoLayerId CreateLayerId(ulong low)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(0UL, low, out CoCoLayerId id));
            return id;
        }

        private static CoCoStateId CreateStateId(ulong low)
        {
            Assert.IsTrue(CoCoStateId.TryCreate(0UL, low, out CoCoStateId id));
            return id;
        }

        private static CoCoStateBlockId CreateBlockId(ulong low)
        {
            Assert.IsTrue(CoCoStateBlockId.TryCreate(0UL, low, out CoCoStateBlockId id));
            return id;
        }

        private static CoCoStateSlotId CreateSlotId(ulong low)
        {
            Assert.IsTrue(CoCoStateSlotId.TryCreate(0UL, low, out CoCoStateSlotId id));
            return id;
        }

        private static CoCoFrameLayoutId CreateLayoutId(ulong low)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(0UL, low, out CoCoFrameLayoutId id));
            return id;
        }

        private static CoCoOperatorId CreateOperatorId(ulong low)
        {
            Assert.IsTrue(CoCoOperatorId.TryCreate(0UL, low, out CoCoOperatorId id));
            return id;
        }

        private static CoCoOperatorClaimId CreateClaimId(ulong low)
        {
            Assert.IsTrue(CoCoOperatorClaimId.TryCreate(0UL, low, out CoCoOperatorClaimId id));
            return id;
        }

        private static CoCoOperationSectionId CreateSectionId(ulong low)
        {
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(0UL, low, out CoCoOperationSectionId id));
            return id;
        }

        private static CoCoTickFrame CreateTickFrame(ulong tick)
        {
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out CoCoTimelineId timeline));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(tick, out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out CoCoClockDomainId clock));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                0.016d,
                timeline,
                position,
                new CoCoTimelineTick(tick),
                clock,
                new CoCoExecutionSequence(tick),
                new CoCoTimelineEpoch(1UL),
                out CoCoTickFrame frame,
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

        private sealed class TestMemory : CoCoActivationMemory
        {
            public int Value { get; set; }
        }

        private sealed class TestMemoryBinding :
            ICoCoActivationMemoryStateBinding<TestMemory, TestMemoryState>
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

        private sealed class TestGraphValueProducer : ICoCoGraphContextValueProducer<int>
        {
            private readonly CoCoLayerId _layerId;
            private readonly CoCoStateId _stateId;

            public TestGraphValueProducer(CoCoLayerId layerId, CoCoStateId stateId)
            {
                _layerId = layerId;
                _stateId = stateId;
            }

            public ulong SemanticFingerprint => 0xC0C06UL;

            public bool TryProduce(
                in CoCoGraphContextCaptureContext context,
                out int value,
                out CoCoDiagnostic diagnostic)
            {
                if (!context.IsValid ||
                    !context.StagedGraph.TryGetActiveLeaf(_layerId, out CoCoStateId leaf) ||
                    leaf != _stateId ||
                    !context.StagedGraph.TryGetState(
                        _stateId,
                        out CoCoGraphStateRecord<TestMemoryState> state))
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

        private sealed class StagedGraphSource : ICoCoStagedGraphReadSource
        {
            private readonly ulong _token;
            private readonly CoCoGraphStateRecord<TestMemoryState> _state;
            private bool _active = true;

            public StagedGraphSource(
                ulong token,
                CoCoGraphStateRecord<TestMemoryState> state)
            {
                _token = token;
                _state = state;
            }

            public bool IsActive(ulong token) => _active && token == _token;

            public bool TryGetActiveLeaf(
                ulong token,
                CoCoLayerId layerId,
                out CoCoStateId stateId)
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
                CoCoStateId stateId,
                out CoCoGraphStateRecord<TState> state)
                where TState : unmanaged
            {
                if (IsActive(token) &&
                    stateId == _state.StateId &&
                    typeof(TState) == typeof(TestMemoryState))
                {
                    object boxed = _state;
                    state = (CoCoGraphStateRecord<TState>)boxed;
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

        private sealed class TestActorBinding : ICoCoActorContextBinding
        {
            private readonly CoCoStateSlotId _slotId;
            private readonly int _value;

            public TestActorBinding(
                CoCoActorContextBindingDescriptor descriptor,
                CoCoStateSlotId slotId,
                int value)
            {
                Descriptor = descriptor;
                _slotId = slotId;
                _value = value;
            }

            public CoCoActorContextBindingDescriptor Descriptor { get; }

            public bool TryCapture(
                in CoCoActorContextCaptureContext context,
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

        private sealed class ActorValueSink : ICoCoActorContextValueSink
        {
            private readonly CoCoPreparedContextCommit _prepared;
            private readonly CoCoContextFrameLayout _layout;
            private readonly CoCoStateBlockHandle _block;
            private readonly ICoCoActorContextBinding _binding;
            private readonly ulong _token;
            private bool _active = true;

            public ActorValueSink(
                CoCoPreparedContextCommit prepared,
                CoCoContextFrameLayout layout,
                CoCoStateBlockHandle block,
                ICoCoActorContextBinding binding,
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
                ICoCoActorContextBinding binding) =>
                _active && token == _token && ReferenceEquals(binding, _binding);

            public void RejectWrite(
                ulong token,
                ICoCoActorContextBinding binding)
            {
                if (IsActive(token, binding))
                {
                    RejectedWriteCount++;
                }
            }

            public bool TryWrite<TValue>(
                ulong token,
                ICoCoActorContextBinding binding,
                CoCoStateSlotId slotId,
                in TValue value)
                where TValue : unmanaged
            {
                return IsActive(token, binding) &&
                       _layout.TryResolveSlot(slotId, out CoCoStateSlot<TValue> slot) &&
                       _prepared.TryGetWriter(_block, out CoCoContextFrameWriter writer) &&
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
