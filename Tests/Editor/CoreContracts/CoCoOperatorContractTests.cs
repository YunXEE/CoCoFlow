using System;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoOperatorContractTests
    {
        [Test]
        public void DescriptorFreezesCompleteAotSafeOperatorContract()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            Assert.IsTrue(builder.TryRequire<ITestSection>(
                CreateSectionId(1UL),
                CoCoOperationSectionMode.Discrete,
                out CoCoOperationSectionRequirement section,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryClaim(
                CreateClaimId(2UL),
                section,
                17,
                CoCoOperatorClaimSuspendPolicy.Retain,
                out CoCoOperatorClaimRequirement claim,
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
                out CoCoEventOutboxRequirement emit,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze<TestOperator>(
                CreateOperatorId(6UL),
                out CoCoOperatorDescriptor descriptor,
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
            Assert.AreEqual(17, claim.Priority);
            Assert.AreEqual(CoCoOperatorClaimSuspendPolicy.Retain, claim.SuspendPolicy);
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
            var builder = new CoCoOperatorDescriptorBuilder();
            Assert.IsTrue(builder.TryRequire<ITestSection>(
                CreateSectionId(10UL),
                CoCoOperationSectionMode.Continuous,
                out CoCoOperationSectionRequirement continuous,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsFalse(builder.TryClaim(
                CreateClaimId(11UL),
                continuous,
                0,
                CoCoOperatorClaimSuspendPolicy.Release,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperatorDescriptor, diagnostic.Code);

            var discreteBuilder = new CoCoOperatorDescriptorBuilder();
            Assert.IsTrue(discreteBuilder.TryRequire<ITestSection>(
                CreateSectionId(12UL),
                CoCoOperationSectionMode.Discrete,
                out CoCoOperationSectionRequirement discrete,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(discreteBuilder.TryClaim(
                CreateClaimId(13UL),
                discrete,
                -4,
                CoCoOperatorClaimSuspendPolicy.Release,
                out _,
                out diagnostic),
                diagnostic.Message);
            Assert.IsFalse(discreteBuilder.TryClaim(
                CreateClaimId(14UL),
                discrete,
                9,
                CoCoOperatorClaimSuspendPolicy.Retain,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.OperatorClaimConflict, diagnostic.Code);
        }

        [Test]
        public void DistinctEventTypesMayReuseOneUnmanagedPayloadType()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            CoCoEventTypeId firstEventType = CreateEventTypeId(15UL);
            CoCoEventTypeId secondEventType = CreateEventTypeId(16UL);
            CoCoEventDomainId domain = CreateEventDomainId(17UL);
            Assert.IsTrue(builder.TryEmit<TestEvent>(
                firstEventType,
                domain,
                2,
                out CoCoEventOutboxRequirement first,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryEmit<TestEvent>(
                secondEventType,
                domain,
                2,
                out CoCoEventOutboxRequirement second,
                out diagnostic),
                diagnostic.Message);
            Assert.AreNotEqual(first.EventTypeId, second.EventTypeId);
            Assert.AreEqual(first.PayloadType, second.PayloadType);

            Assert.IsTrue(builder.TryEmit<TestEvent>(
                firstEventType,
                domain,
                2,
                out CoCoEventOutboxRequirement duplicate,
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
                out CoCoOperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(2, descriptor.Emits.Count);
            Assert.AreEqual(first, descriptor.Emits[0]);
            Assert.AreEqual(second, descriptor.Emits[1]);
        }

        [Test]
        public void OutcomesDistinguishExecutionFromNormalClaimDenial()
        {
            CoCoOperatorOutcome success = CoCoOperatorOutcome.Success;
            CoCoOperatorOutcome noOp = CoCoOperatorOutcome.NoOp;
            CoCoDiagnostic diagnostic = CoCoDiagnostic.Warning(
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.OperatorClaimConflict,
                "Claim was denied by stable arbitration.");
            CoCoOperatorOutcome denied = CoCoOperatorOutcome.Denied(diagnostic);
            CoCoOperatorOutcome rejected = CoCoOperatorOutcome.Rejected(diagnostic);

            Assert.AreEqual(CoCoOperatorOutcomeStatus.Succeeded, success.Status);
            Assert.AreEqual(CoCoOperatorOutcomeStatus.NoOp, noOp.Status);
            Assert.AreEqual(CoCoOperatorOutcomeStatus.ClaimDenied, denied.Status);
            Assert.AreEqual(CoCoOperatorOutcomeStatus.Rejected, rejected.Status);
            Assert.IsTrue(success.IsValid);
            Assert.IsTrue(noOp.IsValid);
            Assert.IsTrue(denied.IsValid);
            Assert.IsTrue(rejected.IsValid);
            Assert.IsFalse(default(CoCoOperatorOutcome).IsValid);
            Assert.IsFalse(CoCoOperatorOutcome.Rejected(default).IsValid);
        }

        [Test]
        public void ExecutionContextWritesOnlyDeclaredOutcomeAndExpiresWithSinkToken()
        {
            CoCoOperatorId operatorId = CreateOperatorId(20UL);
            CoCoStateSlotId declaredId = CreateSlotId(21UL);
            CoCoStateSlotId undeclaredId = CreateSlotId(22UL);
            var descriptorBuilder = new CoCoOperatorDescriptorBuilder();
            Assert.IsTrue(descriptorBuilder.TryOwnOutcome<int>(declaredId, out CoCoDiagnostic diagnostic));
            Assert.IsTrue(descriptorBuilder.TryFreeze<TestOperator>(
                operatorId,
                out CoCoOperatorDescriptor descriptor,
                out diagnostic),
                diagnostic.Message);

            var layoutBuilder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(CoCoStateBlockId.TryCreate(0UL, 23UL, out CoCoStateBlockId blockId));
            Assert.IsTrue(layoutBuilder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Operator,
                out CoCoDiagnosticCode code));
            Assert.IsTrue(layoutBuilder.TryAddSlot(
                blockId,
                declaredId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                0,
                default,
                null,
                out code));
            Assert.IsTrue(layoutBuilder.TryFreeze(
                CreateLayoutId(24UL),
                1U,
                out CoCoContextFrameLayout layout,
                out code));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(declaredId, out CoCoStateSlot<int> declared));

            CoCoGraphInstanceId graph = CreateGraphInstanceId(25UL);
            var arena = new CoCoContextFrameArena(graph, layout, 2);
            CoCoTickFrame tick = CreateTickFrame(1UL);
            Assert.IsTrue(arena.TryPrepare(tick, out CoCoPreparedContextCommit prepared, out _));
            var sink = new OutcomeSink(prepared, layout, block, operatorId, 99UL);

            var operationBuilder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(operationBuilder.TryFreeze(
                CreateLayoutId(26UL),
                out CoCoOperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoOperationFrame.TryCreate(
                registry,
                graph,
                Array.Empty<CoCoOperationSectionRequirement>(),
                out CoCoOperationFrame operation,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(operation.TryBegin(tick, out CoCoOperationFrameWriter operationWriter));
            Assert.IsTrue(operationWriter.TryFinalize(out CoCoFinalizedOperationFrame finalizedOperation));

            var context = new CoCoOperatorExecutionContext(
                descriptor,
                tick,
                arena.Previous,
                finalizedOperation,
                sink,
                default,
                99UL);
            Assert.IsTrue(context.IsValid);
            Assert.IsFalse(context.TryWriteOutcome(default(CoCoStateSlot<int>), 5));
            Assert.IsFalse(context.TryWriteOutcome(undeclaredId, 7));
            Assert.AreEqual(2, sink.RejectedWriteCount);
            Assert.IsTrue(context.TryWriteOutcome(declaredId, 42));
            Assert.IsTrue(context.TryWriteOutcome(declared, 43));
            sink.Deactivate();
            Assert.IsFalse(context.IsValid);
            Assert.IsFalse(context.TryWriteOutcome(declaredId, 99));
            Assert.AreEqual(2, sink.RejectedWriteCount);

            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
            Assert.AreEqual(43, finalized.Commit().Frame.Read(declared));
            Assert.IsTrue(finalizedOperation.Cancel());
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

        private static CoCoStateSlotId CreateSlotId(ulong low)
        {
            Assert.IsTrue(CoCoStateSlotId.TryCreate(0UL, low, out CoCoStateSlotId id));
            return id;
        }

        private static CoCoEventTypeId CreateEventTypeId(ulong low)
        {
            Assert.IsTrue(CoCoEventTypeId.TryCreate(0UL, low, out CoCoEventTypeId id));
            return id;
        }

        private static CoCoEventDomainId CreateEventDomainId(ulong value)
        {
            Assert.IsTrue(CoCoEventDomainId.TryCreate(value, out CoCoEventDomainId id));
            return id;
        }

        private static CoCoGraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoFrameLayoutId CreateLayoutId(ulong low)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(0UL, low, out CoCoFrameLayoutId id));
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

        private interface ITestSection : ICoCoOperationSection
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

        private sealed class TestOperator : ICoCoOperator
        {
            public CoCoOperatorDescriptor Descriptor { get; set; }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                outcome = CoCoOperatorOutcome.NoOp;
                return true;
            }
        }

        private sealed class OutcomeSink : ICoCoOperatorOutcomeSink
        {
            private readonly CoCoPreparedContextCommit _prepared;
            private readonly CoCoContextFrameLayout _layout;
            private readonly CoCoStateBlockHandle _block;
            private readonly CoCoOperatorId _operatorId;
            private readonly ulong _token;
            private bool _active = true;

            public int RejectedWriteCount { get; private set; }

            public OutcomeSink(
                CoCoPreparedContextCommit prepared,
                CoCoContextFrameLayout layout,
                CoCoStateBlockHandle block,
                CoCoOperatorId operatorId,
                ulong token)
            {
                _prepared = prepared;
                _layout = layout;
                _block = block;
                _operatorId = operatorId;
                _token = token;
            }

            public bool IsActive(ulong token, CoCoOperatorId operatorId) =>
                _active && token == _token && operatorId == _operatorId;

            public void RejectWrite(ulong token, CoCoOperatorId operatorId)
            {
                if (_active)
                {
                    RejectedWriteCount++;
                }
            }

            public bool TryWrite<TValue>(
                ulong token,
                CoCoOperatorId operatorId,
                CoCoStateSlotId slotId,
                in TValue value)
                where TValue : unmanaged
            {
                return IsActive(token, operatorId) &&
                       _layout.TryResolveSlot(slotId, out CoCoStateSlot<TValue> slot) &&
                       _prepared.TryGetWriter(_block, out CoCoContextFrameWriter writer) &&
                       writer.Write(slot, value);
            }

            public void Deactivate()
            {
                _active = false;
            }
        }
    }
}
