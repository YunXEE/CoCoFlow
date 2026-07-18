using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public readonly struct ContextAuthorityTestIds
    {
        private ContextAuthorityTestIds(
            CoCoLayerId layerId,
            CoCoStateId firstStateId,
            CoCoStateId secondStateId,
            CoCoTransitionId transitionId,
            CoCoStateDescriptorId stateDescriptorId,
            CoCoStateBlockId graphStateBlockId,
            CoCoStateSlotId firstGraphStateSlotId,
            CoCoStateSlotId secondGraphStateSlotId,
            CoCoStateBlockId actorStateBlockId,
            CoCoStateSlotId actorStateSlotId,
            CoCoOperatorId operatorId)
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
            OperatorId = operatorId;
        }

        public CoCoLayerId LayerId { get; }
        public CoCoStateId FirstStateId { get; }
        public CoCoStateId SecondStateId { get; }
        public CoCoTransitionId TransitionId { get; }
        public CoCoStateDescriptorId StateDescriptorId { get; }
        public CoCoStateBlockId GraphStateBlockId { get; }
        public CoCoStateSlotId FirstGraphStateSlotId { get; }
        public CoCoStateSlotId SecondGraphStateSlotId { get; }
        public CoCoStateBlockId ActorStateBlockId { get; }
        public CoCoStateSlotId ActorStateSlotId { get; }
        public CoCoOperatorId OperatorId { get; }

        public static ContextAuthorityTestIds Create()
        {
            if (!CoCoLayerId.TryCreate(701UL, 1UL, out CoCoLayerId layerId) ||
                !CoCoStateId.TryCreate(702UL, 1UL, out CoCoStateId firstStateId) ||
                !CoCoStateId.TryCreate(702UL, 2UL, out CoCoStateId secondStateId) ||
                !CoCoTransitionId.TryCreate(702UL, 3UL, out CoCoTransitionId transitionId) ||
                !CoCoStateDescriptorId.TryCreate(
                    703UL,
                    1UL,
                    out CoCoStateDescriptorId stateDescriptorId) ||
                !CoCoStateBlockId.TryCreate(
                    704UL,
                    1UL,
                    out CoCoStateBlockId graphStateBlockId) ||
                !CoCoStateSlotId.TryCreate(
                    705UL,
                    1UL,
                    out CoCoStateSlotId firstGraphStateSlotId) ||
                !CoCoStateSlotId.TryCreate(
                    705UL,
                    2UL,
                    out CoCoStateSlotId secondGraphStateSlotId) ||
                !CoCoStateBlockId.TryCreate(
                    704UL,
                    2UL,
                    out CoCoStateBlockId actorStateBlockId) ||
                !CoCoStateSlotId.TryCreate(
                    705UL,
                    3UL,
                    out CoCoStateSlotId actorStateSlotId) ||
                !CoCoOperatorId.TryCreate(706UL, 1UL, out CoCoOperatorId operatorId))
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
                operatorId);
        }
    }

    public static class ContextAuthorityDefaults
    {
        public const ulong FirstGraphStateFingerprint = 70701UL;
        public const ulong SecondGraphStateFingerprint = 70702UL;
        public const ulong ActorStateFingerprint = 70703UL;
        public const int ActorStateValue = 7;

        public static CoCoGraphStateRecord<int> First(
            ContextAuthorityTestIds ids,
            int memoryState = 0)
        {
            if (!CoCoActivationId.TryCreate(1UL, out CoCoActivationId activationId) ||
                !CoCoGraphStateRecord<int>.TryCreate(
                    ids.LayerId,
                    ids.FirstStateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    memoryState,
                    out CoCoGraphStateRecord<int> record))
            {
                throw new InvalidOperationException(
                    "The initial active Graph State record is invalid.");
            }

            return record;
        }

        public static CoCoGraphStateRecord<int> Second(ContextAuthorityTestIds ids)
        {
            if (!CoCoGraphStateRecord<int>.TryCreateInactive(
                    ids.LayerId,
                    ids.SecondStateId,
                    0UL,
                    0,
                    out CoCoGraphStateRecord<int> record))
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
        }

        public static void RecordLogicFactory() => LogicFactoryCount++;

        public static void RecordMemoryFactory() => MemoryFactoryCount++;

        public static void RecordMemoryReset() => MemoryResetCount++;

        public static void ArmNextMemoryFingerprintThrow() =>
            _throwOnNextMemoryFingerprint = true;

        public static ulong RecordMemoryFingerprint(ContextAuthorityMemory memory)
        {
            MemoryFingerprintCount++;
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

    public sealed class ContextAuthorityMemory : CoCoActivationMemory
    {
        public int Value;
    }

    public sealed class ContextAuthorityMemoryStateBinding :
        ICoCoActivationMemoryStateBinding<ContextAuthorityMemory, int>
    {
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
        }

        public bool TryCapture(
            ContextAuthorityMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            CaptureCount++;
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

    public sealed class ContextAuthorityLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        private readonly CoCoTransitionHandle _transition;

        public ContextAuthorityLogic(CoCoStateFactoryContext context)
        {
            _transition = context.OutgoingTransitions.Count == 0
                ? default
                : context.OutgoingTransitions[0];
        }

        public static bool RequestTransition { get; set; }
        public static int UpdateCount { get; private set; }

        public static void Reset()
        {
            RequestTransition = false;
            UpdateCount = 0;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            UpdateCount++;
            context.Memory<ContextAuthorityMemory>().Value++;
            if (RequestTransition && _transition.IsValid)
            {
                context.RequestTransition(_transition);
            }
        }
    }
}
