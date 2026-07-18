using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public static class OperatorCommitProjectFactoryProbe
    {
        public static int LogicFactoryCount { get; private set; }
        public static int MemoryFactoryCount { get; private set; }
        public static int MemoryResetCount { get; private set; }
        public static int MemoryFingerprintCount { get; private set; }

        public static void Reset()
        {
            LogicFactoryCount = 0;
            MemoryFactoryCount = 0;
            MemoryResetCount = 0;
            MemoryFingerprintCount = 0;
        }

        public static void RecordLogicFactory() => LogicFactoryCount++;

        public static void RecordMemoryFactory() => MemoryFactoryCount++;

        public static void RecordMemoryReset() => MemoryResetCount++;

        public static void RecordMemoryFingerprint() => MemoryFingerprintCount++;
    }

    public struct OperatorCommitEventA
    {
        public int Value;
    }

    public struct OperatorCommitEventB
    {
        public int Value;
    }

    public readonly struct OperatorCommitTestIds
    {
        private OperatorCommitTestIds(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoStateId secondStateId,
            CoCoTransitionId transitionId,
            CoCoStateDescriptorId stateDescriptorId,
            CoCoStateBlockId stateBlockId,
            CoCoStateSlotId stateSlotId,
            CoCoStateBlockId graphStateBlockId,
            CoCoStateSlotId firstGraphStateSlotId,
            CoCoStateSlotId secondGraphStateSlotId,
            CoCoStateSlotId primaryClaimStateSlotId,
            CoCoStateSlotId secondaryClaimStateSlotId,
            CoCoOperatorId firstOperatorId,
            CoCoOperatorId secondOperatorId,
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeA,
            CoCoEventTypeId eventTypeB,
            CoCoOperationSectionId primarySectionId,
            CoCoOperationSectionId secondarySectionId,
            CoCoOperatorClaimId primaryClaimId,
            CoCoOperatorClaimId secondaryClaimId)
        {
            LayerId = layerId;
            StateId = stateId;
            SecondStateId = secondStateId;
            TransitionId = transitionId;
            StateDescriptorId = stateDescriptorId;
            StateBlockId = stateBlockId;
            StateSlotId = stateSlotId;
            GraphStateBlockId = graphStateBlockId;
            FirstGraphStateSlotId = firstGraphStateSlotId;
            SecondGraphStateSlotId = secondGraphStateSlotId;
            PrimaryClaimStateSlotId = primaryClaimStateSlotId;
            SecondaryClaimStateSlotId = secondaryClaimStateSlotId;
            FirstOperatorId = firstOperatorId;
            SecondOperatorId = secondOperatorId;
            EventDomainId = eventDomainId;
            EventTypeA = eventTypeA;
            EventTypeB = eventTypeB;
            PrimarySectionId = primarySectionId;
            SecondarySectionId = secondarySectionId;
            PrimaryClaimId = primaryClaimId;
            SecondaryClaimId = secondaryClaimId;
        }

        public CoCoLayerId LayerId { get; }
        public CoCoStateId StateId { get; }
        public CoCoStateId SecondStateId { get; }
        public CoCoTransitionId TransitionId { get; }
        public CoCoStateDescriptorId StateDescriptorId { get; }
        public CoCoStateBlockId StateBlockId { get; }
        public CoCoStateSlotId StateSlotId { get; }
        public CoCoStateBlockId GraphStateBlockId { get; }
        public CoCoStateSlotId FirstGraphStateSlotId { get; }
        public CoCoStateSlotId SecondGraphStateSlotId { get; }
        public CoCoStateSlotId PrimaryClaimStateSlotId { get; }
        public CoCoStateSlotId SecondaryClaimStateSlotId { get; }
        public CoCoOperatorId FirstOperatorId { get; }
        public CoCoOperatorId SecondOperatorId { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeA { get; }
        public CoCoEventTypeId EventTypeB { get; }
        public CoCoOperationSectionId PrimarySectionId { get; }
        public CoCoOperationSectionId SecondarySectionId { get; }
        public CoCoOperatorClaimId PrimaryClaimId { get; }
        public CoCoOperatorClaimId SecondaryClaimId { get; }

        public static OperatorCommitTestIds Create()
        {
            if (!CoCoLayerId.TryCreate(501UL, 1UL, out CoCoLayerId layerId) ||
                !CoCoStateId.TryCreate(502UL, 1UL, out CoCoStateId stateId) ||
                !CoCoStateId.TryCreate(502UL, 2UL, out CoCoStateId secondStateId) ||
                !CoCoTransitionId.TryCreate(502UL, 3UL, out CoCoTransitionId transitionId) ||
                !CoCoStateDescriptorId.TryCreate(503UL, 1UL, out CoCoStateDescriptorId descriptorId) ||
                !CoCoStateBlockId.TryCreate(504UL, 1UL, out CoCoStateBlockId blockId) ||
                !CoCoStateSlotId.TryCreate(505UL, 1UL, out CoCoStateSlotId slotId) ||
                !CoCoStateBlockId.TryCreate(504UL, 2UL, out CoCoStateBlockId graphStateBlockId) ||
                !CoCoStateSlotId.TryCreate(505UL, 2UL, out CoCoStateSlotId firstGraphStateSlotId) ||
                !CoCoStateSlotId.TryCreate(505UL, 3UL, out CoCoStateSlotId secondGraphStateSlotId) ||
                !CoCoStateSlotId.TryCreate(505UL, 4UL, out CoCoStateSlotId primaryClaimStateSlotId) ||
                !CoCoStateSlotId.TryCreate(505UL, 5UL, out CoCoStateSlotId secondaryClaimStateSlotId) ||
                !CoCoOperatorId.TryCreate(506UL, 1UL, out CoCoOperatorId firstOperatorId) ||
                !CoCoOperatorId.TryCreate(506UL, 2UL, out CoCoOperatorId secondOperatorId) ||
                !CoCoEventDomainId.TryCreate(507UL, out CoCoEventDomainId domainId) ||
                !CoCoEventTypeId.TryCreate(508UL, 1UL, out CoCoEventTypeId eventTypeA) ||
                !CoCoEventTypeId.TryCreate(508UL, 2UL, out CoCoEventTypeId eventTypeB) ||
                !CoCoOperationSectionId.TryCreate(509UL, 1UL, out CoCoOperationSectionId primarySectionId) ||
                !CoCoOperationSectionId.TryCreate(509UL, 2UL, out CoCoOperationSectionId secondarySectionId) ||
                !CoCoOperatorClaimId.TryCreate(510UL, 1UL, out CoCoOperatorClaimId primaryClaimId) ||
                !CoCoOperatorClaimId.TryCreate(510UL, 2UL, out CoCoOperatorClaimId secondaryClaimId))
            {
                throw new InvalidOperationException("Operator Commit fixture identities are invalid.");
            }

            return new OperatorCommitTestIds(
                layerId,
                stateId,
                secondStateId,
                transitionId,
                descriptorId,
                blockId,
                slotId,
                graphStateBlockId,
                firstGraphStateSlotId,
                secondGraphStateSlotId,
                primaryClaimStateSlotId,
                secondaryClaimStateSlotId,
                firstOperatorId,
                secondOperatorId,
                domainId,
                eventTypeA,
                eventTypeB,
                primarySectionId,
                secondarySectionId,
                primaryClaimId,
                secondaryClaimId);
        }
    }

    public sealed class OperatorCommitHostMemoryBinding :
        ICoCoActivationMemoryStateBinding<HostTestMemory, int>
    {
        public const ulong Fingerprint = 50521UL;

        public ulong SemanticFingerprint => Fingerprint;

        public bool TryCapture(
            HostTestMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            if (memory == null)
            {
                state = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Host test memory is required for Graph State capture.");
                return false;
            }

            state = memory.Value;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in int state,
            HostTestMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Host test candidate memory is required for Graph State restore preparation.");
                return false;
            }

            candidateMemory.Value = state;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class OperatorCommitClaimMemoryBinding :
        ICoCoActivationMemoryStateBinding<OperatorCommitClaimMemory, byte>
    {
        public const ulong Fingerprint = 50522UL;

        public static bool MutateMemoryOnCapture { get; set; }

        public ulong SemanticFingerprint => Fingerprint;

        public static void Reset()
        {
            MutateMemoryOnCapture = false;
        }

        public bool TryCapture(
            OperatorCommitClaimMemory memory,
            out byte state,
            out CoCoDiagnostic diagnostic)
        {
            state = default;
            if (memory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Claim test memory is required for Graph State capture.");
                return false;
            }

            state = checked((byte)memory.Value);
            if (MutateMemoryOnCapture)
            {
                memory.Value++;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in byte state,
            OperatorCommitClaimMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Claim test candidate memory is required.");
                return false;
            }

            candidateMemory.Value = state;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public static class OperatorCommitGraphContextFixture
    {
        public const ulong FirstStateDefaultFingerprint = 50531UL;
        public const ulong SecondStateDefaultFingerprint = 50532UL;
        public const ulong PrimaryClaimDefaultFingerprint = 50533UL;
        public const ulong SecondaryClaimDefaultFingerprint = 50534UL;

        public static bool TryRegisterSingleState(
            CoCoGraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic)
        {
            CoCoGraphStateRecord<int> first = CreateActiveState(ids, 0);
            return builder.TryRegisterStateBlock(
                       ids.GraphStateBlockId,
                       CoCoStateBlockOwner.Graph,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.FirstGraphStateSlotId,
                       CoCoContextProjection.Temporal,
                       CoCoContextRestorePolicy.Stored,
                       first,
                       FirstStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic);
        }

        public static bool TryBindSingleState(
            CoCoStateGraphHostBindingBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic)
        {
            CoCoGraphStateRecord<int> first = CreateActiveState(ids, 0);
            return builder.TryBindGraphStateSlot<
                HostTestMemory,
                int,
                OperatorCommitHostMemoryBinding>(
                ids.LayerId,
                ids.StateId,
                ids.GraphStateBlockId,
                ids.FirstGraphStateSlotId,
                first,
                FirstStateDefaultFingerprint,
                new OperatorCommitHostMemoryBinding(),
                out diagnostic);
        }

        public static bool TryRegisterClaimGraph(
            CoCoGraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic)
        {
            CoCoGraphStateRecord<byte> first = CreateActiveState(ids, (byte)0);
            CoCoGraphStateRecord<byte> second = CreateInactiveState(ids, (byte)0);
            CoCoOperatorClaimState primary = CoCoOperatorClaimState.Unheld(
                ids.PrimaryClaimId,
                ids.PrimarySectionId);
            CoCoOperatorClaimState secondary = CoCoOperatorClaimState.Unheld(
                ids.SecondaryClaimId,
                ids.SecondarySectionId);
            return builder.TryRegisterStateBlock(
                       ids.GraphStateBlockId,
                       CoCoStateBlockOwner.Graph,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.FirstGraphStateSlotId,
                       CoCoContextProjection.Temporal,
                       CoCoContextRestorePolicy.Stored,
                       first,
                       FirstStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.SecondGraphStateSlotId,
                       CoCoContextProjection.Temporal,
                       CoCoContextRestorePolicy.Stored,
                       second,
                       SecondStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.PrimaryClaimStateSlotId,
                       CoCoContextProjection.Temporal,
                       CoCoContextRestorePolicy.Stored,
                       primary,
                       PrimaryClaimDefaultFingerprint,
                       default,
                       null,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.SecondaryClaimStateSlotId,
                       CoCoContextProjection.Temporal,
                       CoCoContextRestorePolicy.Stored,
                       secondary,
                       SecondaryClaimDefaultFingerprint,
                       default,
                       null,
                       out diagnostic);
        }

        public static bool TryBindClaimGraph(
            CoCoStateGraphHostBindingBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic) =>
            TryBindClaimGraph(builder, ids, false, out diagnostic);

        public static bool TryBindClaimGraph(
            CoCoStateGraphHostBindingBuilder builder,
            OperatorCommitTestIds ids,
            bool mismatchPrimaryIdentity,
            out CoCoDiagnostic diagnostic)
        {
            CoCoGraphStateRecord<byte> first = CreateActiveState(ids, (byte)0);
            CoCoGraphStateRecord<byte> second = CreateInactiveState(ids, (byte)0);
            CoCoOperatorClaimState primary = CoCoOperatorClaimState.Unheld(
                mismatchPrimaryIdentity ? ids.SecondaryClaimId : ids.PrimaryClaimId,
                ids.PrimarySectionId);
            CoCoOperatorClaimState secondary = CoCoOperatorClaimState.Unheld(
                ids.SecondaryClaimId,
                ids.SecondarySectionId);
            var memoryBinding = new OperatorCommitClaimMemoryBinding();
            return builder.TryBindGraphStateSlot<
                       OperatorCommitClaimMemory,
                       byte,
                       OperatorCommitClaimMemoryBinding>(
                       ids.LayerId,
                       ids.StateId,
                       ids.GraphStateBlockId,
                       ids.FirstGraphStateSlotId,
                       first,
                       FirstStateDefaultFingerprint,
                       memoryBinding,
                       out diagnostic) &&
                   builder.TryBindGraphStateSlot<
                       OperatorCommitClaimMemory,
                       byte,
                       OperatorCommitClaimMemoryBinding>(
                       ids.LayerId,
                       ids.SecondStateId,
                       ids.GraphStateBlockId,
                       ids.SecondGraphStateSlotId,
                       second,
                       SecondStateDefaultFingerprint,
                       memoryBinding,
                       out diagnostic) &&
                   builder.TryBindClaimStateSlot(
                       ids.GraphStateBlockId,
                       ids.PrimaryClaimStateSlotId,
                       primary,
                       PrimaryClaimDefaultFingerprint,
                       out diagnostic) &&
                   builder.TryBindClaimStateSlot(
                       ids.GraphStateBlockId,
                       ids.SecondaryClaimStateSlotId,
                       secondary,
                       SecondaryClaimDefaultFingerprint,
                       out diagnostic);
        }

        private static CoCoGraphStateRecord<TState> CreateActiveState<TState>(
            OperatorCommitTestIds ids,
            TState state)
            where TState : unmanaged
        {
            if (!CoCoActivationId.TryCreate(1UL, out CoCoActivationId activationId) ||
                !CoCoGraphStateRecord<TState>.TryCreate(
                    ids.LayerId,
                    ids.StateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    state,
                    out CoCoGraphStateRecord<TState> record))
            {
                throw new InvalidOperationException(
                    "Operator Commit active Graph State default is invalid.");
            }

            return record;
        }

        private static CoCoGraphStateRecord<TState> CreateInactiveState<TState>(
            OperatorCommitTestIds ids,
            TState state)
            where TState : unmanaged
        {
            if (!CoCoGraphStateRecord<TState>.TryCreateInactive(
                    ids.LayerId,
                    ids.SecondStateId,
                    0UL,
                    state,
                    out CoCoGraphStateRecord<TState> record))
            {
                throw new InvalidOperationException(
                    "Operator Commit inactive Graph State default is invalid.");
            }

            return record;
        }
    }

    public interface IOperatorCommitPrimarySection : ICoCoOperationSection
    {
        int Value { get; }
    }

    public interface IOperatorCommitSecondarySection : ICoCoOperationSection
    {
        int Value { get; }
    }

    public sealed class OperatorCommitPrimarySectionView : IOperatorCommitPrimarySection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<int> _value;

        public OperatorCommitPrimarySectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<int> value)
        {
            _reader = reader;
            _value = value;
        }

        public int Value => _reader.Read(_value);
    }

    public sealed class OperatorCommitSecondarySectionView : IOperatorCommitSecondarySection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<int> _value;

        public OperatorCommitSecondarySectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<int> value)
        {
            _reader = reader;
            _value = value;
        }

        public int Value => _reader.Read(_value);
    }

    public sealed class OperatorCommitPrimarySectionFactory :
        ICoCoOperationSectionViewFactory<IOperatorCommitPrimarySection>
    {
        public CoCoOperationSectionHandle<IOperatorCommitPrimarySection> Handle { get; private set; }
        public CoCoOperationSectionField<int> ValueField { get; private set; }

        public IOperatorCommitPrimarySection Create(
            in CoCoOperationSectionViewContext<IOperatorCommitPrimarySection> context)
        {
            if (!context.TryGetField(0, out CoCoOperationSectionField<int> value))
            {
                throw new InvalidOperationException("Primary Claim Section field is unavailable.");
            }

            Handle = context.Handle;
            ValueField = value;
            return new OperatorCommitPrimarySectionView(context.Reader, value);
        }
    }

    public sealed class OperatorCommitSecondarySectionFactory :
        ICoCoOperationSectionViewFactory<IOperatorCommitSecondarySection>
    {
        public CoCoOperationSectionHandle<IOperatorCommitSecondarySection> Handle { get; private set; }
        public CoCoOperationSectionField<int> ValueField { get; private set; }

        public IOperatorCommitSecondarySection Create(
            in CoCoOperationSectionViewContext<IOperatorCommitSecondarySection> context)
        {
            if (!context.TryGetField(0, out CoCoOperationSectionField<int> value))
            {
                throw new InvalidOperationException("Secondary Claim Section field is unavailable.");
            }

            Handle = context.Handle;
            ValueField = value;
            return new OperatorCommitSecondarySectionView(context.Reader, value);
        }
    }

    public sealed class OperatorCommitClaimMemory : CoCoActivationMemory
    {
        public int Value;
    }

    public sealed class OperatorCommitClaimLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        private readonly CoCoOperationSectionHandle<IOperatorCommitPrimarySection> _primary;
        private readonly CoCoOperationSectionField<int> _primaryValue;
        private readonly CoCoOperationSectionHandle<IOperatorCommitSecondarySection> _secondary;
        private readonly CoCoOperationSectionField<int> _secondaryValue;
        private readonly CoCoTransitionHandle _transition;

        public OperatorCommitClaimLogic(
            CoCoStateFactoryContext context,
            CoCoOperationSectionHandle<IOperatorCommitPrimarySection> primary,
            CoCoOperationSectionField<int> primaryValue,
            CoCoOperationSectionHandle<IOperatorCommitSecondarySection> secondary,
            CoCoOperationSectionField<int> secondaryValue)
        {
            _primary = primary;
            _primaryValue = primaryValue;
            _secondary = secondary;
            _secondaryValue = secondaryValue;
            _transition = context.OutgoingTransitions.Count == 0
                ? default
                : context.OutgoingTransitions[0];
        }

        public static bool EnableSecondary { get; set; }
        public static bool RequestTransition { get; set; }
        public static int UpdateCount { get; private set; }

        public static void Reset()
        {
            EnableSecondary = false;
            RequestTransition = false;
            UpdateCount = 0;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            UpdateCount++;
            context.Operations.Write(_primaryValue, 1);
            context.Operations.EnableDiscrete(_primary);
            if (EnableSecondary)
            {
                context.Operations.Write(_secondaryValue, 2);
                context.Operations.EnableDiscrete(_secondary);
            }

            if (RequestTransition && _transition.IsValid)
            {
                context.RequestTransition(_transition);
            }
        }
    }
}
