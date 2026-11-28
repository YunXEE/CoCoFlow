using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class OperatorContractTests
    {
        [Test]
        public void DescriptorFreezesCompleteAotSafeOperatorContract()
        {
            var builder = new OperatorDescriptorBuilder();
            Assert.IsTrue(builder.TryRequire<ITestSection>(
                CreateSectionId(1UL),
                Mode.Discrete,
                out OperationRequirement section,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryClaim(
                CreateClaimId(2UL),
                section,
                CreateSlotId(2UL),
                17,
                OperatorClaimSuspendPolicy.Retain,
                out OperatorClaimRequirement claim,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryOwnOutcome<int>(
                CreateSlotId(3UL),
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryEmit<TestEvent>(
                CreateEventTypeId(4UL),
                CreateEventDomainId(5UL),
                8,
                out EventOutboxRequirement emit,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze<TestOperator>(
                CreateOperatorId(6UL),
                out OperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);

            Assert.IsTrue(builder.IsFrozen);
            Assert.IsTrue(descriptor.IsValid);
            Assert.AreEqual(typeof(TestOperator), descriptor.OperatorType);
            Assert.AreEqual(1, descriptor.Requires.Count);
            Assert.AreEqual(section, descriptor.Requires[0]);
            Assert.AreEqual(claim, descriptor.Claims[0]);
            Assert.AreEqual(1, descriptor.OutcomeCount);
            Assert.AreEqual(CreateSlotId(3UL), descriptor.OutcomeRequirements[0].SlotId);
            Assert.AreEqual(emit, descriptor.Emits[0]);
            Assert.IsTrue(claim.IsValid);
            Assert.AreEqual(CreateSlotId(2UL), claim.StateSlotId);
            Assert.AreEqual(17, claim.Priority);
            Assert.AreEqual(OperatorClaimSuspendPolicy.Retain, claim.SuspendPolicy);
            Assert.AreEqual(typeof(int), descriptor.OutcomeRequirements[0].ValueType);
            Assert.AreEqual(typeof(TestEvent), emit.PayloadType);

            Assert.IsFalse(builder.TryOwnOutcome<int>(
                CreateSlotId(7UL),
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnostic.Code);
        }

        [Test]
        public void ClaimsMustBindOneRequiredDiscreteSectionAndRemainUnique()
        {
            var builder = new OperatorDescriptorBuilder();
            Assert.IsTrue(builder.TryRequire<ITestSection>(
                CreateSectionId(10UL),
                Mode.Continuous,
                out OperationRequirement continuous,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsFalse(builder.TryClaim(
                CreateClaimId(11UL),
                continuous,
                CreateSlotId(11UL),
                0,
                OperatorClaimSuspendPolicy.Release,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperatorDescriptor, diagnostic.Code);

            var discreteBuilder = new OperatorDescriptorBuilder();
            Assert.IsTrue(discreteBuilder.TryRequire<ITestSection>(
                CreateSectionId(12UL),
                Mode.Discrete,
                out OperationRequirement discrete,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(discreteBuilder.TryClaim(
                CreateClaimId(13UL),
                discrete,
                CreateSlotId(13UL),
                -4,
                OperatorClaimSuspendPolicy.Release,
                out _,
                out diagnostic),
                diagnostic.Message);
            Assert.IsFalse(discreteBuilder.TryClaim(
                CreateClaimId(14UL),
                discrete,
                CreateSlotId(14UL),
                9,
                OperatorClaimSuspendPolicy.Retain,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.OperatorClaimConflict, diagnostic.Code);
        }

        [Test]
        public void DistinctEventTypesMayReuseOneUnmanagedPayloadType()
        {
            var builder = new OperatorDescriptorBuilder();
            Assert.IsTrue(builder.TryRequire<ITestSection>(
                CreateSectionId(14UL),
                Mode.Continuous,
                out _,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            EventTypeId firstEventType = CreateEventTypeId(15UL);
            EventTypeId secondEventType = CreateEventTypeId(16UL);
            EventDomainId domain = CreateEventDomainId(17UL);
            Assert.IsTrue(builder.TryEmit<TestEvent>(
                firstEventType,
                domain,
                2,
                out EventOutboxRequirement first,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryEmit<TestEvent>(
                secondEventType,
                domain,
                2,
                out EventOutboxRequirement second,
                out diagnostic),
                diagnostic.Message);
            Assert.AreNotEqual(first.EventTypeId, second.EventTypeId);
            Assert.AreEqual(first.PayloadType, second.PayloadType);

            Assert.IsTrue(builder.TryEmit<TestEvent>(
                firstEventType,
                domain,
                2,
                out EventOutboxRequirement duplicate,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(first, duplicate);
            Assert.IsFalse(builder.TryEmit<OtherEvent>(
                firstEventType,
                domain,
                2,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperatorDescriptor, diagnostic.Code);

            Assert.IsTrue(builder.TryFreeze<TestOperator>(
                CreateOperatorId(18UL),
                out OperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(2, descriptor.Emits.Count);
            Assert.AreEqual(first, descriptor.Emits[0]);
            Assert.AreEqual(second, descriptor.Emits[1]);
        }

        [Test]
        public void DescriptorRequiresOneOperationSectionBeforeFreeze()
        {
            var builder = new OperatorDescriptorBuilder();
            OperatorId operatorId = CreateOperatorId(19UL);
            Assert.IsTrue(builder.TryOwnOutcome<int>(
                CreateSlotId(19UL),
                out CoCoDiagnostic diagnostic));

            Assert.IsFalse(builder.TryFreeze<TestOperator>(
                operatorId,
                out OperatorDescriptor rejected,
                out diagnostic));
            Assert.IsNull(rejected);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperatorDescriptor, diagnostic.Code);
            Assert.IsFalse(builder.IsFrozen);

            Assert.IsTrue(builder.TryRequire<ITestSection>(
                CreateSectionId(19UL),
                Mode.Continuous,
                out OperationRequirement requirement,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze<TestOperator>(
                operatorId,
                out OperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.IsFrozen);
            Assert.IsTrue(descriptor.IsValid);
            Assert.AreEqual(1, descriptor.Requires.Count);
            Assert.AreEqual(requirement, descriptor.Requires[0]);
        }

        [Test]
        public void OutcomesDistinguishExecutionFromNormalClaimDenial()
        {
            OperatorOutcome success = OperatorOutcome.Success;
            OperatorOutcome noOp = OperatorOutcome.NoOp;
            CoCoDiagnostic diagnostic = CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.OperatorClaimConflict,
                "Claim was denied by stable arbitration.");
            OperatorOutcome denied = OperatorOutcome.Denied(diagnostic);
            OperatorOutcome rejected = OperatorOutcome.Rejected(diagnostic);

            Assert.AreEqual(OperatorOutcomeStatus.Succeeded, success.Status);
            Assert.AreEqual(OperatorOutcomeStatus.NoOp, noOp.Status);
            Assert.AreEqual(OperatorOutcomeStatus.ClaimDenied, denied.Status);
            Assert.AreEqual(OperatorOutcomeStatus.Rejected, rejected.Status);
            Assert.IsTrue(success.IsValid);
            Assert.IsTrue(noOp.IsValid);
            Assert.IsTrue(denied.IsValid);
            Assert.IsTrue(rejected.IsValid);
            Assert.IsFalse(default(OperatorOutcome).IsValid);
            Assert.IsFalse(OperatorOutcome.Rejected(default).IsValid);
        }

        [Test]
        public void ExecutionContextWritesOnlyDeclaredOutcomeAndExpiresWithSinkToken()
        {
            OperatorId operatorId = CreateOperatorId(20UL);
            StateSlotId declaredId = CreateSlotId(21UL);
            StateSlotId undeclaredId = CreateSlotId(22UL);
            var descriptorBuilder = new OperatorDescriptorBuilder();
            Assert.IsTrue(descriptorBuilder.TryRequire<ITestSection>(
                CreateSectionId(20UL),
                Mode.Continuous,
                out _,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(descriptorBuilder.TryOwnOutcome<int>(declaredId, out diagnostic));
            Assert.IsTrue(descriptorBuilder.TryFreeze<TestOperator>(
                operatorId,
                out OperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);

            var layoutBuilder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(StateBlockId.TryCreate(0UL, 23UL, out StateBlockId blockId));
            Assert.IsTrue(layoutBuilder.TryAddBlock(
                blockId,
                StateBlockOwner.Operator,
                out CoCoDiagnosticCode code));
            Assert.IsTrue(layoutBuilder.TryAddSlot(
                blockId,
                declaredId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                0,
                default,
                null,
                out code));
            Assert.IsTrue(layoutBuilder.TryFreeze(
                CreateLayoutId(24UL),
                1U,
                out ContextFrameLayout layout,
                out code));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(declaredId, out StateSlot<int> declared));

            GraphInstanceId graph = CreateGraphInstanceId(25UL);
            var arena = new ContextFrameArena(graph, layout, 2);
            TickFrame tick = CreateTickFrame(1UL);
            Assert.IsTrue(arena.TryPrepare(tick, out PreparedContextCommit prepared, out _));
            var sink = new OutcomeSink(prepared, layout, block, operatorId, 99UL);

            var operationBuilder = new OperationRegistryBuilder();
            Assert.IsTrue(operationBuilder.TryFreeze(
                CreateLayoutId(26UL),
                out OperationRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                graph,
                Array.Empty<OperationRequirement>(),
                out OperationFrame operation,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(operation.TryBegin(tick, out OperationFrameWriter operationWriter));
            Assert.IsTrue(operationWriter.TryFinalize(out FinalizedOperationFrame finalizedOperation));

            var context = new OperatorExecutionContext(
                descriptor,
                tick,
                arena.Previous,
                finalizedOperation.ReadView,
                sink,
                default,
                99UL);
            Assert.IsTrue(context.IsValid);
            Assert.IsFalse(context.TryWriteOutcome(default(StateSlot<int>), 5));
            Assert.IsFalse(context.TryWriteOutcome(undeclaredId, 7));
            Assert.AreEqual(2, sink.RejectedWriteCount);
            Assert.IsTrue(context.TryWriteOutcome(declaredId, 42));
            Assert.IsTrue(context.TryWriteOutcome(declared, 43));
            sink.Deactivate();
            Assert.IsFalse(context.IsValid);
            Assert.IsFalse(context.TryWriteOutcome(declaredId, 99));
            Assert.AreEqual(2, sink.RejectedWriteCount);

            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.AreEqual(43, finalized.Commit().Frame.Read(declared));
            Assert.IsTrue(finalizedOperation.Cancel());
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

        private static StateSlotId CreateSlotId(ulong low)
        {
            Assert.IsTrue(StateSlotId.TryCreate(0UL, low, out StateSlotId id));
            return id;
        }

        private static EventTypeId CreateEventTypeId(ulong low)
        {
            Assert.IsTrue(EventTypeId.TryCreate(0UL, low, out EventTypeId id));
            return id;
        }

        private static EventDomainId CreateEventDomainId(ulong value)
        {
            Assert.IsTrue(EventDomainId.TryCreate(value, out EventDomainId id));
            return id;
        }

        private static GraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static FrameLayoutId CreateLayoutId(ulong low)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(0UL, low, out FrameLayoutId id));
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

        private interface ITestSection : IOperation
        {
            int Value { get; }
        }

        private readonly struct TestEvent
        {
            public TestEvent(int value)
            {
                Value = value;
            }

            public readonly int Value;
        }

        private readonly struct OtherEvent
        {
            public OtherEvent(int value)
            {
                Value = value;
            }

            public readonly int Value;
        }

        private sealed class TestOperator : IOperator
        {
            public OperatorDescriptor Descriptor { get; set; }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                outcome = OperatorOutcome.NoOp;
                return true;
            }
        }

        private sealed class OutcomeSink : IOperatorOutcomeSink
        {
            private readonly PreparedContextCommit _prepared;
            private readonly ContextFrameLayout _layout;
            private readonly StateBlockHandle _block;
            private readonly OperatorId _operatorId;
            private readonly ulong _token;
            private bool _active = true;

            public int RejectedWriteCount { get; private set; }

            public OutcomeSink(
                PreparedContextCommit prepared,
                ContextFrameLayout layout,
                StateBlockHandle block,
                OperatorId operatorId,
                ulong token)
            {
                _prepared = prepared;
                _layout = layout;
                _block = block;
                _operatorId = operatorId;
                _token = token;
            }

            public bool IsActive(ulong token, OperatorId operatorId) =>
                _active && token == _token && operatorId == _operatorId;

            public void RejectWrite(ulong token, OperatorId operatorId)
            {
                if (_active)
                {
                    RejectedWriteCount++;
                }
            }

            public bool TryWrite<TValue>(
                ulong token,
                OperatorId operatorId,
                StateSlotId slotId,
                in TValue value)
                where TValue : unmanaged
            {
                return IsActive(token, operatorId) &&
                       _layout.TryResolveSlot(slotId, out StateSlot<TValue> slot) &&
                       _prepared.TryGetWriter(_block, out ContextFrameWriter writer) &&
                       writer.Write(slot, value);
            }

            public void Deactivate()
            {
                _active = false;
            }
        }
    }
}
