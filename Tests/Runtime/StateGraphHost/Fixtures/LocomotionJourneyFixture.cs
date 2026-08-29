using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>
    /// Shared observation board for the journey e2e fixtures. The state
    /// logics record engine facts here every tick so tests can assert on
    /// committed slot values without touching Host internals.
    /// </summary>
    public static class JourneyMemory
    {
        public static string Current = "None";
        public static float LastSlotZ;
        public static bool LastSlotGrounded = true;
        public static int LastAnimHash;
        public static float LastAnimTime;
        public static bool AttackTriggerWritten;
        public static bool WasAirborne;

        public static void Reset()
        {
            Current = "None";
            LastSlotZ = 0f;
            LastSlotGrounded = true;
            LastAnimHash = 0;
            LastAnimTime = 0f;
            AttackTriggerWritten = false;
            WasAirborne = false;
        }
    }

    /// <summary>
    /// Raw input action names and trigger binding contract shared by the
    /// journey fixture states and the e2e tests.
    /// </summary>
    public static class JourneyContract
    {
        public const string Move = "Move";
        public const string Jump = "Jump";
        public const string Attack = "Attack";
        public const ulong AttackTriggerBindingId = 77UL;

        // Graph transition serialized ids (runtime ids derive 1:1 from
        // High/Low — the fixture addresses edges by id, never by order).
        public const ulong IdleToMove = 11UL;
        public const ulong IdleToJump = 12UL;
        public const ulong IdleToAttack = 13UL;
        public const ulong MoveToIdle = 14UL;
        public const ulong MoveToJump = 15UL;
        public const ulong JumpToIdle = 16UL;
        public const ulong AttackToIdle = 17UL;

        // Dense field indices on ILocomotionSection (declaration order).
        public static void RequestTransition(
            CoCoStateExecutionContext context,
            ulong transitionId)
        {
            for (int index = 0; index < context.OutgoingTransitions.Count; index++)
            {
                CoCoTransitionHandle handle = context.OutgoingTransitions[index];
                if (handle.TransitionId.Low == transitionId)
                {
                    context.RequestTransition(handle);
                    return;
                }
            }
        }

        public const int FieldMoveX = LocomotionSectionFields.MoveX;
        public const int FieldMoveZ = LocomotionSectionFields.MoveZ;
        public const int FieldUseGravity = LocomotionSectionFields.UseGravity;
        public const int FieldJumpRequested = LocomotionSectionFields.JumpRequested;
        public const int FieldVerticalImpulse = LocomotionSectionFields.VerticalImpulse;
    }

    /// <summary>
    /// Idle: watches raw input and requests the matching outgoing edge
    /// (edge order fixed by the test graph build: 0=Move, 1=Jump, 2=Attack).
    /// </summary>
    [CoCoState("JourneyIdle")]
    [CoCoIntentConsume(typeof(RawInputIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    [CoCoOperationProvide(typeof(IAnimParameterOperationSection))]
    public sealed class JourneyIdleLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
            JourneyMemory.Current = "Idle";
            if (TryReadMove(context, out float moveX, out float z) &&
                (moveX != 0f || z != 0f))
            {
                JourneyContract.RequestTransition(context, JourneyContract.IdleToMove);
                return;
            }

            if (TryReadPressed(context, JourneyContract.Jump))
            {
                JourneyContract.RequestTransition(context, JourneyContract.IdleToJump);
            }
            else if (TryReadPressed(context, JourneyContract.Attack))
            {
                JourneyContract.RequestTransition(context, JourneyContract.IdleToAttack);
            }
        }

        internal static bool TryReadMove(
            CoCoStateExecutionContext context,
            out float moveX,
            out float moveZ)
        {
            moveX = 0f;
            moveZ = 0f;
            if (!context.Intents.TryFirst(out RawInputIntent intent))
            {
                return false;
            }

            for (int index = 0; index < intent.Count; index++)
            {
                if (intent.TryGet(index, out RawInputRecord record) &&
                    record.Action == CoCoFixedString64.FromString(JourneyContract.Move))
                {
                    moveX = record.ValueX;
                    moveZ = record.ValueY;
                    return true;
                }
            }

            return false;
        }

        internal static bool TryReadPressed(
            CoCoStateExecutionContext context,
            string action)
        {
            if (!context.Intents.TryFirst(out RawInputIntent intent))
            {
                return false;
            }

            var name = CoCoFixedString64.FromString(action);
            for (int index = 0; index < intent.Count; index++)
            {
                if (intent.TryGet(index, out RawInputRecord record) &&
                    record.Action == name &&
                    record.Phase == RawInputPhase.Started)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Move: translates raw Move records into locomotion section desires;
    /// returns to Idle (edge 0) when input stops, Jump (edge 1) on Space.
    /// Records the committed locomotion slot for convergence assertions.
    /// </summary>
    [CoCoState("JourneyMove")]
    [CoCoIntentConsume(typeof(RawInputIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class JourneyMoveLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
            JourneyMemory.Current = "Move";
            RecordSlot(context);
            bool hasMove = JourneyIdleLogic.TryReadMove(
                context,
                out float moveX,
                out float moveZ);

            var gravity = context.Operations.ResolveField<ILocomotionSection, bool>(
                JourneyContract.FieldUseGravity);
            var fieldX = context.Operations.ResolveField<ILocomotionSection, float>(
                JourneyContract.FieldMoveX);
            var fieldZ = context.Operations.ResolveField<ILocomotionSection, float>(
                JourneyContract.FieldMoveZ);
            if (gravity.IsValid)
            {
                _ = context.Operations.Write(gravity, true);
            }

            if (fieldX.IsValid)
            {
                _ = context.Operations.Write(fieldX, moveX);
            }

            if (fieldZ.IsValid)
            {
                _ = context.Operations.Write(fieldZ, moveZ);
            }

            if (JourneyIdleLogic.TryReadPressed(context, JourneyContract.Jump))
            {
                JourneyContract.RequestTransition(context, JourneyContract.MoveToJump);
            }
            else if (!hasMove || (moveX == 0f && moveZ == 0f))
            {
                JourneyContract.RequestTransition(context, JourneyContract.MoveToIdle);
            }
        }

        internal static void RecordSlot(CoCoStateExecutionContext context)
        {
            if (context.PreviousContext.Layout != null &&
                context.PreviousContext.Layout.TryResolveSlot(
                    LocoContractIds.StateSlotId,
                    out CoCoStateSlot<LocomotionState> slot))
            {
                LocomotionState state = context.PreviousContext.Read(slot);
                JourneyMemory.LastSlotZ = state.PositionZ;
                JourneyMemory.LastSlotGrounded = state.IsGrounded;
            }
        }
    }

    /// <summary>
    /// Jump: one-shot impulse on the first tick, hover-free gravity after;
    /// back to Idle (edge 0) once the committed slot reports grounded.
    /// Records the committed Animator snapshot for restore assertions.
    /// </summary>
    [CoCoState("JourneyJump")]
    [CoCoIntentConsume(typeof(RawInputIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class JourneyJumpLogic : CoCoStateLogic, ICoCoStateEnter, ICoCoStateUpdate
    {
        private bool _impulseWritten;

        public void OnEnter(CoCoStateExecutionContext context)
        {
            _impulseWritten = false;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            JourneyMemory.Current = "Jump";
            JourneyMoveLogic.RecordSlot(context);
            RecordAnimSlot(context);

            var gravity = context.Operations.ResolveField<ILocomotionSection, bool>(
                JourneyContract.FieldUseGravity);
            if (gravity.IsValid)
            {
                _ = context.Operations.Write(gravity, true);
            }

            if (!_impulseWritten)
            {
                var request = context.Operations.ResolveField<ILocomotionSection, bool>(
                    JourneyContract.FieldJumpRequested);
                var impulse = context.Operations.ResolveField<ILocomotionSection, float>(
                    JourneyContract.FieldVerticalImpulse);
                if (request.IsValid && impulse.IsValid)
                {
                    _ = context.Operations.Write(request, true);
                    _ = context.Operations.Write(impulse, 6f);
                    _impulseWritten = true;
                }
            }

            if (!JourneyMemory.LastSlotGrounded)
            {
                // Grounded lags the launch for a few ticks — only a real
                // airborne sample arms the landing check.
                JourneyMemory.WasAirborne = true;
            }

            if (context.PreviousContext.HasCommittedFrame &&
                JourneyMemory.WasAirborne &&
                JourneyMemory.LastSlotGrounded)
            {
                JourneyContract.RequestTransition(context, JourneyContract.JumpToIdle);
            }
        }

        internal static void RecordAnimSlot(CoCoStateExecutionContext context)
        {
            if (context.PreviousContext.Layout != null &&
                context.PreviousContext.Layout.TryResolveSlot(
                    AnimContractIds.SnapshotSlotId,
                    out CoCoStateSlot<AnimSnapshotState> slot))
            {
                AnimSnapshotState snapshot = context.PreviousContext.Read(slot);
                JourneyMemory.LastAnimHash = snapshot.LayerStateHash(0);
                JourneyMemory.LastAnimTime = snapshot.LayerTime(0);
            }
        }
    }

    /// <summary>
    /// Attack: presentation-only — fires the Attack trigger lane once on
    /// the first tick, holds ground (no locomotion writes), returns to
    /// Idle (edge 0) after a short beat.
    /// </summary>
    [CoCoState("JourneyAttack")]
    [CoCoIntentConsume(typeof(RawInputIntent))]
    [CoCoOperationProvide(typeof(IAnimTriggerOperationSection))]
    public sealed class JourneyAttackLogic : CoCoStateLogic, ICoCoStateEnter, ICoCoStateUpdate
    {
        private bool _triggerWritten;

        public void OnEnter(CoCoStateExecutionContext context)
        {
            _triggerWritten = false;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            JourneyMemory.Current = "Attack";
            if (!_triggerWritten &&
                context.Operations.TryEnableDiscrete<IAnimTriggerOperationSection>())
            {
                var lane = context.Operations.ResolveField
                    <IAnimTriggerOperationSection, AnimTriggerCommand>(0);
                if (lane.IsValid &&
                    AnimBindingId.TryCreate(
                        JourneyContract.AttackTriggerBindingId,
                        out AnimBindingId bindingId) &&
                    AnimTriggerCommand.TryCreate(
                        AnimTriggerCommandKind.Set,
                        bindingId,
                        context.ActivationId,
                        out AnimTriggerCommand command))
                {
                    _ = context.Operations.Write(lane, command);
                    _triggerWritten = true;
                    JourneyMemory.AttackTriggerWritten = true;
                }
            }

            if (context.LocalSeconds > 0.15d)
            {
                JourneyContract.RequestTransition(context, JourneyContract.AttackToIdle);
            }
        }
    }
}
