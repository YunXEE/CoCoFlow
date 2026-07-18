using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
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

        public static void Reset()
        {
            EnableSecondary = false;
            RequestTransition = false;
        }

        public void Update(CoCoStateExecutionContext context)
        {
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
