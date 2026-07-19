using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public readonly struct TemporalHostTestIds
    {
        private TemporalHostTestIds(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoStateDescriptorId stateDescriptorId,
            CoCoStateBlockId graphStateBlockId,
            CoCoStateSlotId graphStateSlotId,
            CoCoStateBlockId actorStateBlockId,
            CoCoStateSlotId actorStateSlotId,
            CoCoIntentId intentId,
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeId)
        {
            LayerId = layerId;
            StateId = stateId;
            StateDescriptorId = stateDescriptorId;
            GraphStateBlockId = graphStateBlockId;
            GraphStateSlotId = graphStateSlotId;
            ActorStateBlockId = actorStateBlockId;
            ActorStateSlotId = actorStateSlotId;
            IntentId = intentId;
            EventDomainId = eventDomainId;
            EventTypeId = eventTypeId;
        }

        public CoCoLayerId LayerId { get; }
        public CoCoStateId StateId { get; }
        public CoCoStateDescriptorId StateDescriptorId { get; }
        public CoCoStateBlockId GraphStateBlockId { get; }
        public CoCoStateSlotId GraphStateSlotId { get; }
        public CoCoStateBlockId ActorStateBlockId { get; }
        public CoCoStateSlotId ActorStateSlotId { get; }
        public CoCoIntentId IntentId { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeId { get; }

        public static TemporalHostTestIds Create()
        {
            if (!CoCoLayerId.TryCreate(801UL, 1UL, out CoCoLayerId layerId) ||
                !CoCoStateId.TryCreate(802UL, 1UL, out CoCoStateId stateId) ||
                !CoCoStateDescriptorId.TryCreate(
                    803UL,
                    1UL,
                    out CoCoStateDescriptorId stateDescriptorId) ||
                !CoCoStateBlockId.TryCreate(
                    804UL,
                    1UL,
                    out CoCoStateBlockId graphStateBlockId) ||
                !CoCoStateSlotId.TryCreate(
                    805UL,
                    1UL,
                    out CoCoStateSlotId graphStateSlotId) ||
                !CoCoStateBlockId.TryCreate(
                    804UL,
                    2UL,
                    out CoCoStateBlockId actorStateBlockId) ||
                !CoCoStateSlotId.TryCreate(
                    805UL,
                    2UL,
                    out CoCoStateSlotId actorStateSlotId) ||
                !CoCoIntentId.TryCreate(806UL, 1UL, out CoCoIntentId intentId) ||
                !CoCoEventDomainId.TryCreate(807UL, out CoCoEventDomainId eventDomainId) ||
                !CoCoEventTypeId.TryCreate(808UL, 1UL, out CoCoEventTypeId eventTypeId))
            {
                throw new InvalidOperationException("Temporal Host fixture identities are invalid.");
            }

            return new TemporalHostTestIds(
                layerId,
                stateId,
                stateDescriptorId,
                graphStateBlockId,
                graphStateSlotId,
                actorStateBlockId,
                actorStateSlotId,
                intentId,
                eventDomainId,
                eventTypeId);
        }
    }

    public static class TemporalHostDefaults
    {
        public const ulong GraphStateFingerprint = 80901UL;
        public const ulong ActorStateFingerprint = 80902UL;
        public const ulong MemoryBindingFingerprint = 80903UL;
        public const ulong IntentReducerFingerprint = 80904UL;
        public const int ActorStateValue = 7;

        public static CoCoGraphStateRecord<int> GraphState(TemporalHostTestIds ids)
        {
            if (!CoCoActivationId.TryCreate(1UL, out CoCoActivationId activationId) ||
                !CoCoGraphStateRecord<int>.TryCreate(
                    ids.LayerId,
                    ids.StateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    0,
                    out CoCoGraphStateRecord<int> state))
            {
                throw new InvalidOperationException(
                    "Temporal Host default Graph State is invalid.");
            }

            return state;
        }
    }

    public sealed class TemporalHostMemory : CoCoActivationMemory
    {
        public int Value;
    }

    public sealed class TemporalHostMemoryStateBinding :
        ICoCoActivationMemoryStateBinding<TemporalHostMemory, int>
    {
        public ulong SemanticFingerprint => TemporalHostDefaults.MemoryBindingFingerprint;

        public static int CaptureCount { get; private set; }
        public static int RestorePrepareCount { get; private set; }

        public static void Reset()
        {
            CaptureCount = 0;
            RestorePrepareCount = 0;
        }

        public bool TryCapture(
            TemporalHostMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            CaptureCount++;
            if (memory == null)
            {
                state = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Temporal Host memory is missing.");
                return false;
            }

            state = memory.Value;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in int state,
            TemporalHostMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            RestorePrepareCount++;
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Temporal Host restore memory is missing.");
                return false;
            }

            candidateMemory.Value = state;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public struct TemporalHostIntent
    {
        public int Value;
    }

    public struct TemporalHostEvent
    {
        public int Value;
    }

    public struct TemporalHostIntentReducer : ICoCoIntentReducer<TemporalHostIntent>
    {
        public TemporalHostIntent Reduce(
            in TemporalHostIntent current,
            in TemporalHostIntent candidate) => candidate;
    }

    public sealed class TemporalHostIntentReducerFactory :
        ICoCoIntentReducerFactory<TemporalHostIntent, TemporalHostIntentReducer>
    {
        public TemporalHostIntentReducer Create(
            CoCoGraphInstanceId graphInstanceId) => default;
    }

    public sealed class TemporalHostEventAdapter :
        ICoCoEventToIntentAdapter<TemporalHostEvent, TemporalHostIntent>
    {
        public static int ProjectionCount { get; private set; }

        public static void Reset()
        {
            ProjectionCount = 0;
        }

        public bool TryProject(
            in CoCoEventPacket<TemporalHostEvent> packet,
            out TemporalHostIntent intent)
        {
            ProjectionCount++;
            intent = new TemporalHostIntent { Value = packet.Payload.Value };
            return true;
        }
    }

    public sealed class TemporalHostLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoIntentHandle<TemporalHostIntent> _intent;

        public TemporalHostLogic(
            CoCoGraphInstanceId graphInstanceId,
            CoCoIntentHandle<TemporalHostIntent> intent)
        {
            _graphInstanceId = graphInstanceId;
            _intent = intent;
        }

        public static int UpdateCount { get; private set; }
        public static int LastMemoryValue { get; private set; }
        public static int LastIntentValue { get; private set; }
        public static Action UpdateCallback { get; set; }

        public static void Reset()
        {
            UpdateCount = 0;
            LastMemoryValue = 0;
            LastIntentValue = 0;
            UpdateCallback = null;
        }

        public static ulong GetMemoryFingerprint(TemporalHostMemory memory) =>
            unchecked((ulong)(uint)memory.Value);

        public void Update(CoCoStateExecutionContext context)
        {
            UpdateCount++;
            TemporalHostMemory memory = context.Memory<TemporalHostMemory>();
            memory.Value++;
            LastMemoryValue = memory.Value;
            if (_graphInstanceId.IsValid &&
                _intent.IsValid &&
                context.Intents != null &&
                context.Intents.TryGet(_intent, out TemporalHostIntent intent))
            {
                LastIntentValue = intent.Value;
            }

            UpdateCallback?.Invoke();
        }
    }
}
