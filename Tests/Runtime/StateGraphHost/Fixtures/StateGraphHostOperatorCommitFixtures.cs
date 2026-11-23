using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

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
            LayerId layerId,
            StateId stateId,
            StateId secondStateId,
            TransitionId transitionId,
            StateDescriptorId stateDescriptorId,
            StateBlockId stateBlockId,
            StateSlotId stateSlotId,
            StateBlockId graphStateBlockId,
            StateSlotId firstGraphStateSlotId,
            StateSlotId secondGraphStateSlotId,
            StateSlotId primaryClaimStateSlotId,
            StateSlotId secondaryClaimStateSlotId,
            OperatorId firstOperatorId,
            OperatorId secondOperatorId,
            EventDomainId eventDomainId,
            EventTypeId eventTypeA,
            EventTypeId eventTypeB,
            OperationSectionId primarySectionId,
            OperationSectionId secondarySectionId,
            OperatorClaimId primaryClaimId,
            OperatorClaimId secondaryClaimId)
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

        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public StateId SecondStateId { get; }
        public TransitionId TransitionId { get; }
        public StateDescriptorId StateDescriptorId { get; }
        public StateBlockId StateBlockId { get; }
        public StateSlotId StateSlotId { get; }
        public StateBlockId GraphStateBlockId { get; }
        public StateSlotId FirstGraphStateSlotId { get; }
        public StateSlotId SecondGraphStateSlotId { get; }
        public StateSlotId PrimaryClaimStateSlotId { get; }
        public StateSlotId SecondaryClaimStateSlotId { get; }
        public OperatorId FirstOperatorId { get; }
        public OperatorId SecondOperatorId { get; }
        public EventDomainId EventDomainId { get; }
        public EventTypeId EventTypeA { get; }
        public EventTypeId EventTypeB { get; }
        public OperationSectionId PrimarySectionId { get; }
        public OperationSectionId SecondarySectionId { get; }
        public OperatorClaimId PrimaryClaimId { get; }
        public OperatorClaimId SecondaryClaimId { get; }

        public static OperatorCommitTestIds Create()
        {
            if (!LayerId.TryCreate(501UL, 1UL, out LayerId layerId) ||
                !StateId.TryCreate(502UL, 1UL, out StateId stateId) ||
                !StateId.TryCreate(502UL, 2UL, out StateId secondStateId) ||
                !TransitionId.TryCreate(502UL, 3UL, out TransitionId transitionId) ||
                !StateDescriptorId.TryCreate(503UL, 1UL, out StateDescriptorId descriptorId) ||
                !StateBlockId.TryCreate(504UL, 1UL, out StateBlockId blockId) ||
                !StateSlotId.TryCreate(505UL, 1UL, out StateSlotId slotId) ||
                !StateBlockId.TryCreate(504UL, 2UL, out StateBlockId graphStateBlockId) ||
                !StateSlotId.TryCreate(505UL, 2UL, out StateSlotId firstGraphStateSlotId) ||
                !StateSlotId.TryCreate(505UL, 3UL, out StateSlotId secondGraphStateSlotId) ||
                !StateSlotId.TryCreate(505UL, 4UL, out StateSlotId primaryClaimStateSlotId) ||
                !StateSlotId.TryCreate(505UL, 5UL, out StateSlotId secondaryClaimStateSlotId) ||
                !OperatorId.TryCreate(506UL, 1UL, out OperatorId firstOperatorId) ||
                !OperatorId.TryCreate(506UL, 2UL, out OperatorId secondOperatorId) ||
                !EventDomainId.TryCreate(507UL, out EventDomainId domainId) ||
                !EventTypeId.TryCreate(508UL, 1UL, out EventTypeId eventTypeA) ||
                !EventTypeId.TryCreate(508UL, 2UL, out EventTypeId eventTypeB) ||
                !OperationSectionId.TryCreate(509UL, 1UL, out OperationSectionId primarySectionId) ||
                !OperationSectionId.TryCreate(509UL, 2UL, out OperationSectionId secondarySectionId) ||
                !OperatorClaimId.TryCreate(510UL, 1UL, out OperatorClaimId primaryClaimId) ||
                !OperatorClaimId.TryCreate(510UL, 2UL, out OperatorClaimId secondaryClaimId))
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
        IActivationMemoryStateBinding<HostTestMemory, int>
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
        IActivationMemoryStateBinding<OperatorCommitClaimMemory, byte>
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
            GraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic)
        {
            GraphStateRecord<int> first = CreateActiveState(ids, 0);
            return builder.TryRegisterStateBlock(
                       ids.GraphStateBlockId,
                       StateBlockOwner.Graph,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.FirstGraphStateSlotId,
                       ContextProjection.Temporal,
                       ContextRestorePolicy.Stored,
                       first,
                       FirstStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic);
        }

        public static bool TryRegisterClaimGraph(
            GraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            out CoCoDiagnostic diagnostic) =>
            TryRegisterClaimGraph(
                builder,
                ids,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                ContextRestorePolicy.Stored,
                out diagnostic);

        public static bool TryRegisterClaimGraph(
            GraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            ContextRestorePolicy primaryClaimRestorePolicy,
            ContextRestorePolicy secondaryClaimRestorePolicy,
            out CoCoDiagnostic diagnostic) =>
            TryRegisterClaimGraph(
                builder,
                ids,
                ContextProjection.Temporal,
                primaryClaimRestorePolicy,
                secondaryClaimRestorePolicy,
                out diagnostic);

        public static bool TryRegisterClaimGraph(
            GraphDescriptorCatalogBuilder builder,
            OperatorCommitTestIds ids,
            ContextProjection projection,
            ContextRestorePolicy primaryClaimRestorePolicy,
            ContextRestorePolicy secondaryClaimRestorePolicy,
            out CoCoDiagnostic diagnostic)
        {
            GraphStateRecord<byte> first = CreateActiveState(ids, (byte)0);
            GraphStateRecord<byte> second = CreateInactiveState(ids, (byte)0);
            OperatorClaimState primary = OperatorClaimState.Unheld(
                ids.PrimaryClaimId,
                ids.PrimarySectionId);
            OperatorClaimState secondary = OperatorClaimState.Unheld(
                ids.SecondaryClaimId,
                ids.SecondarySectionId);
            return builder.TryRegisterStateBlock(
                       ids.GraphStateBlockId,
                       StateBlockOwner.Graph,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.FirstGraphStateSlotId,
                       projection,
                       ContextRestorePolicy.Stored,
                       first,
                       FirstStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.SecondGraphStateSlotId,
                       projection,
                       ContextRestorePolicy.Stored,
                       second,
                       SecondStateDefaultFingerprint,
                       default,
                       null,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.PrimaryClaimStateSlotId,
                       projection,
                       primaryClaimRestorePolicy,
                       primary,
                       PrimaryClaimDefaultFingerprint,
                       default,
                       null,
                       out diagnostic) &&
                   builder.TryRegisterStateSlot(
                       ids.GraphStateBlockId,
                       ids.SecondaryClaimStateSlotId,
                       projection,
                       secondaryClaimRestorePolicy,
                       secondary,
                       SecondaryClaimDefaultFingerprint,
                       default,
                       null,
                       out diagnostic);
        }

        public static GraphStateRecord<TState> CreateActiveState<TState>(
            OperatorCommitTestIds ids,
            TState state)
            where TState : unmanaged
        {
            if (!ActivationId.TryCreate(1UL, out ActivationId activationId) ||
                !GraphStateRecord<TState>.TryCreate(
                    ids.LayerId,
                    ids.StateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    state,
                    out GraphStateRecord<TState> record))
            {
                throw new InvalidOperationException(
                    "Operator Commit active Graph State default is invalid.");
            }

            return record;
        }

        public static GraphStateRecord<TState> CreateInactiveState<TState>(
            OperatorCommitTestIds ids,
            TState state)
            where TState : unmanaged
        {
            if (!GraphStateRecord<TState>.TryCreateInactive(
                    ids.LayerId,
                    ids.SecondStateId,
                    0UL,
                    state,
                    out GraphStateRecord<TState> record))
            {
                throw new InvalidOperationException(
                    "Operator Commit inactive Graph State default is invalid.");
            }

            return record;
        }
    }

    public interface IOperatorCommitPrimarySection : IOperationSection
    {
        int Value { get; }
    }

    public interface IOperatorCommitSecondarySection : IOperationSection
    {
        int Value { get; }
    }

    public sealed class OperatorCommitPrimarySectionView : IOperatorCommitPrimarySection
    {
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<int> _value;

        public OperatorCommitPrimarySectionView(
            OperationSectionReader reader,
            OperationSectionField<int> value)
        {
            _reader = reader;
            _value = value;
        }

        public int Value => _reader.Read(_value);
    }

    public sealed class OperatorCommitSecondarySectionView : IOperatorCommitSecondarySection
    {
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<int> _value;

        public OperatorCommitSecondarySectionView(
            OperationSectionReader reader,
            OperationSectionField<int> value)
        {
            _reader = reader;
            _value = value;
        }

        public int Value => _reader.Read(_value);
    }

    public sealed class OperatorCommitPrimarySectionFactory :
        IOperationSectionViewFactory<IOperatorCommitPrimarySection>
    {
        public OperationSectionHandle<IOperatorCommitPrimarySection> Handle { get; private set; }
        public OperationSectionField<int> ValueField { get; private set; }

        public IOperatorCommitPrimarySection Create(
            in OperationSectionViewContext<IOperatorCommitPrimarySection> context)
        {
            if (!context.TryGetField(0, out OperationSectionField<int> value))
            {
                throw new InvalidOperationException("Primary Claim Section field is unavailable.");
            }

            Handle = context.Handle;
            ValueField = value;
            return new OperatorCommitPrimarySectionView(context.Reader, value);
        }
    }

    public sealed class OperatorCommitSecondarySectionFactory :
        IOperationSectionViewFactory<IOperatorCommitSecondarySection>
    {
        public OperationSectionHandle<IOperatorCommitSecondarySection> Handle { get; private set; }
        public OperationSectionField<int> ValueField { get; private set; }

        public IOperatorCommitSecondarySection Create(
            in OperationSectionViewContext<IOperatorCommitSecondarySection> context)
        {
            if (!context.TryGetField(0, out OperationSectionField<int> value))
            {
                throw new InvalidOperationException("Secondary Claim Section field is unavailable.");
            }

            Handle = context.Handle;
            ValueField = value;
            return new OperatorCommitSecondarySectionView(context.Reader, value);
        }
    }

    public sealed class OperatorCommitClaimMemory : ActivationMemory
    {
        public int Value;
    }

    public sealed class AnyClaimLogic : AnyStateLogic
    {
        private readonly OperatorCommitClaimLogic _inner;
        public AnyClaimLogic(OperatorCommitClaimLogic inner) => _inner = inner;

        public override void Update(StateExecutionContext context)
        {
            _inner.Update(context);
            context.Memory<OperatorCommitClaimMemory>().Value++;
        }
    }

    public sealed class OperatorCommitClaimLogic : StateLogic, IStateUpdate
    {
        private readonly OperationSectionHandle<IOperatorCommitPrimarySection> _primary;
        private readonly OperationSectionField<int> _primaryValue;
        private readonly OperationSectionHandle<IOperatorCommitSecondarySection> _secondary;
        private readonly OperationSectionField<int> _secondaryValue;
        private readonly TransitionHandle _transition;

        public OperatorCommitClaimLogic(
            StateFactoryContext context,
            OperationSectionHandle<IOperatorCommitPrimarySection> primary,
            OperationSectionField<int> primaryValue,
            OperationSectionHandle<IOperatorCommitSecondarySection> secondary,
            OperationSectionField<int> secondaryValue)
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

        public void Update(StateExecutionContext context)
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
