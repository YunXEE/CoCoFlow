using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public readonly struct ContextAuthorityTestIds
    {
        private ContextAuthorityTestIds(
            LayerId layerId,
            StateId firstStateId,
            StateId secondStateId,
            TransitionId transitionId,
            StateDescriptorId stateDescriptorId,
            StateBlockId graphStateBlockId,
            StateSlotId firstGraphStateSlotId,
            StateSlotId secondGraphStateSlotId,
            StateBlockId actorStateBlockId,
            StateSlotId actorStateSlotId,
            OperationSectionId operationSectionId,
            OperatorId operatorId)
        {
            LayerId = layerId;
            FirstStateId = firstStateId;
            SecondStateId = secondStateId;
            TransitionId = transitionId;
            StateDescriptorId = stateDescriptorId;
            GraphStateBlockId = graphStateBlockId;
            FirstGraphStateSlotId = firstGraphStateSlotId;
            SecondGraphStateSlotId = secondGraphStateSlotId;
            ActorStateBlockId = actorStateBlockId;
            ActorStateSlotId = actorStateSlotId;
            OperationSectionId = operationSectionId;
            OperatorId = operatorId;
        }

        public LayerId LayerId { get; }
        public StateId FirstStateId { get; }
        public StateId SecondStateId { get; }
        public TransitionId TransitionId { get; }
        public StateDescriptorId StateDescriptorId { get; }
        public StateBlockId GraphStateBlockId { get; }
        public StateSlotId FirstGraphStateSlotId { get; }
        public StateSlotId SecondGraphStateSlotId { get; }
        public StateBlockId ActorStateBlockId { get; }
        public StateSlotId ActorStateSlotId { get; }
        public OperationSectionId OperationSectionId { get; }
        public OperatorId OperatorId { get; }

        public static ContextAuthorityTestIds Create()
        {
            if (!LayerId.TryCreate(701UL, 1UL, out LayerId layerId) ||
                !StateId.TryCreate(702UL, 1UL, out StateId firstStateId) ||
                !StateId.TryCreate(702UL, 2UL, out StateId secondStateId) ||
                !TransitionId.TryCreate(702UL, 3UL, out TransitionId transitionId) ||
                !StateDescriptorId.TryCreate(
                    703UL,
                    1UL,
                    out StateDescriptorId stateDescriptorId) ||
                !StateBlockId.TryCreate(
                    704UL,
                    1UL,
                    out StateBlockId graphStateBlockId) ||
                !StateSlotId.TryCreate(
                    705UL,
                    1UL,
                    out StateSlotId firstGraphStateSlotId) ||
                !StateSlotId.TryCreate(
                    705UL,
                    2UL,
                    out StateSlotId secondGraphStateSlotId) ||
                !StateBlockId.TryCreate(
                    704UL,
                    2UL,
                    out StateBlockId actorStateBlockId) ||
                !StateSlotId.TryCreate(
                    705UL,
                    3UL,
                    out StateSlotId actorStateSlotId) ||
                !OperationSectionId.TryCreate(
                    709UL,
                    1UL,
                    out OperationSectionId operationSectionId) ||
                !OperatorId.TryCreate(706UL, 1UL, out OperatorId operatorId))
            {
                throw new InvalidOperationException(
                    "Context authority fixture identities are invalid.");
            }

            return new ContextAuthorityTestIds(
                layerId,
                firstStateId,
                secondStateId,
                transitionId,
                stateDescriptorId,
                graphStateBlockId,
                firstGraphStateSlotId,
                secondGraphStateSlotId,
                actorStateBlockId,
                actorStateSlotId,
                operationSectionId,
                operatorId);
        }
    }

    public static class ContextAuthorityDefaults
    {
        public const ulong FirstGraphStateFingerprint = 70701UL;
        public const ulong SecondGraphStateFingerprint = 70702UL;
        public const ulong ActorStateFingerprint = 70703UL;
        public const int ActorStateValue = 7;

        public static GraphStateRecord<int> First(
            ContextAuthorityTestIds ids,
            int memoryState = 0)
        {
            if (!ActivationId.TryCreate(1UL, out ActivationId activationId) ||
                !GraphStateRecord<int>.TryCreate(
                    ids.LayerId,
                    ids.FirstStateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    memoryState,
                    out GraphStateRecord<int> record))
            {
                throw new InvalidOperationException(
                    "The initial active Graph State record is invalid.");
            }

            return record;
        }

        public static GraphStateRecord<int> Second(ContextAuthorityTestIds ids)
        {
            if (!GraphStateRecord<int>.TryCreateInactive(
                    ids.LayerId,
                    ids.SecondStateId,
                    0UL,
                    0,
                    out GraphStateRecord<int> record))
            {
                throw new InvalidOperationException(
                    "The initial inactive Graph State record is invalid.");
            }

            return record;
        }
    }

    public static class ContextAuthorityFactoryProbe
    {
        private static bool _throwOnNextMemoryFingerprint;
        private static Action _onNextMemoryFingerprint;

        public static int LogicFactoryCount { get; private set; }
        public static int MemoryFactoryCount { get; private set; }
        public static int MemoryResetCount { get; private set; }
        public static int MemoryFingerprintCount { get; private set; }
        public static int MemoryFingerprintThrowCount { get; private set; }

        public static void Reset()
        {
            LogicFactoryCount = 0;
            MemoryFactoryCount = 0;
            MemoryResetCount = 0;
            MemoryFingerprintCount = 0;
            MemoryFingerprintThrowCount = 0;
            _throwOnNextMemoryFingerprint = false;
            _onNextMemoryFingerprint = null;
        }

        public static void RecordLogicFactory() => LogicFactoryCount++;

        public static void RecordMemoryFactory() => MemoryFactoryCount++;

        public static void RecordMemoryReset() => MemoryResetCount++;

        public static void ArmNextMemoryFingerprintThrow() =>
            _throwOnNextMemoryFingerprint = true;

        public static void ArmNextMemoryFingerprintCallback(Action callback) =>
            _onNextMemoryFingerprint = callback ??
                                       throw new ArgumentNullException(nameof(callback));

        public static ulong RecordMemoryFingerprint(ContextAuthorityMemory memory)
        {
            MemoryFingerprintCount++;
            Action callback = _onNextMemoryFingerprint;
            _onNextMemoryFingerprint = null;
            callback?.Invoke();
            if (_throwOnNextMemoryFingerprint)
            {
                _throwOnNextMemoryFingerprint = false;
                MemoryFingerprintThrowCount++;
                throw new InvalidOperationException(
                    "Context authority fixture threw from the armed memory fingerprint phase.");
            }

            return unchecked((ulong)(uint)memory.Value);
        }
    }

    public sealed class ContextAuthorityMemory : ActivationMemory
    {
        public int Value;
    }

    public sealed class ContextAuthorityMemoryStateBinding :
        IActivationMemoryStateBinding<ContextAuthorityMemory, int>
    {
        private static Action _onNextCapture;

        public const ulong Fingerprint = 70801UL;

        public static bool FailCapture { get; set; }
        public static bool MutateMemoryOnCapture { get; set; }
        public static bool ArmFingerprintThrowAfterCapture { get; set; }
        public static int CaptureCount { get; private set; }
        public static int RestorePrepareCount { get; private set; }

        public ulong SemanticFingerprint => Fingerprint;

        public static void Reset()
        {
            FailCapture = false;
            MutateMemoryOnCapture = false;
            ArmFingerprintThrowAfterCapture = false;
            CaptureCount = 0;
            RestorePrepareCount = 0;
            _onNextCapture = null;
        }

        public static void ArmNextCaptureCallback(Action callback) =>
            _onNextCapture = callback ?? throw new ArgumentNullException(nameof(callback));

        public bool TryCapture(
            ContextAuthorityMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            CaptureCount++;
            Action callback = _onNextCapture;
            _onNextCapture = null;
            callback?.Invoke();
            if (memory == null || FailCapture)
            {
                state = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Context authority fixture rejected Graph State capture.");
                return false;
            }

            state = memory.Value;
            if (MutateMemoryOnCapture)
            {
                memory.Value++;
            }

            if (ArmFingerprintThrowAfterCapture)
            {
                ArmFingerprintThrowAfterCapture = false;
                ContextAuthorityFactoryProbe.ArmNextMemoryFingerprintThrow();
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in int state,
            ContextAuthorityMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            RestorePrepareCount++;
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Context authority restore requires candidate memory.");
                return false;
            }

            candidateMemory.Value = state;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class ContextAuthorityLogic : StateLogic, IStateUpdate
    {
        private readonly TransitionHandle _transition;
        private readonly OperationSectionHandle<IOperatorCommitPrimarySection> _operation;
        private readonly OperationSectionField<int> _operationValue;

        public ContextAuthorityLogic(
            StateFactoryContext context,
            OperationSectionHandle<IOperatorCommitPrimarySection> operation = default,
            OperationSectionField<int> operationValue = default)
        {
            _transition = context.OutgoingTransitions.Count == 0
                ? default
                : context.OutgoingTransitions[0];
            _operation = operation;
            _operationValue = operationValue;
        }

        public static bool RequestTransition { get; set; }
        public static int UpdateCount { get; private set; }

        public static void Reset()
        {
            RequestTransition = false;
            UpdateCount = 0;
        }

        public void Update(StateExecutionContext context)
        {
            UpdateCount++;
            ContextAuthorityMemory memory = context.Memory<ContextAuthorityMemory>();
            memory.Value++;
            if (_operation.IsValid && _operationValue.IsValid)
            {
                context.Operations.Write(_operationValue, memory.Value);
                context.Operations.EnableDiscrete(_operation);
            }

            if (RequestTransition && _transition.IsValid)
            {
                context.RequestTransition(_transition);
            }
        }
    }
}
