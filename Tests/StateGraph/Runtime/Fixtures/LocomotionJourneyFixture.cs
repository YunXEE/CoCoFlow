using CoCoFlow.Runtime.Input.Contracts;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public static class JourneyMemory
    {
        public static string Current = "None";
        public static float LastSlotZ;
        public static bool LastSlotGrounded = true;
        public static bool WasAirborne;

        public static void Reset()
        {
            Current = "None";
            LastSlotZ = 0f;
            LastSlotGrounded = true;
            WasAirborne = false;
        }
    }

    public static class JourneyContract
    {
        public const string Move = "Move";
        public const string Jump = "Jump";
        public const string Attack = "Attack";

        public const ulong IdleToMove = 11UL;
        public const ulong IdleToJump = 12UL;
        public const ulong IdleToAttack = 13UL;
        public const ulong MoveToIdle = 14UL;
        public const ulong MoveToJump = 15UL;
        public const ulong JumpToIdle = 16UL;
        public const ulong AttackToIdle = 17UL;

        public static void RequestTransition(
            StateExecutionContext context,
            ulong transitionId)
        {
            for (int index = 0; index < context.OutgoingTransitions.Count; index++)
            {
                TransitionHandle handle = context.OutgoingTransitions[index];
                if (handle.TransitionId.Low == transitionId)
                {
                    context.RequestTransition(handle);
                    return;
                }
            }
        }

    }

    [State("JourneyIdle")]
    public sealed class JourneyIdleLogic : StateLogic, IStateUpdate
    {

        #region Public API

        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        public void Update(StateExecutionContext context)
        {
            JourneyMemory.Current = "Idle";
            if (TryReadMove(Input, out float moveX, out float z) &&
                (moveX != 0f || z != 0f))
            {
                JourneyContract.RequestTransition(context, JourneyContract.IdleToMove);
                return;
            }

            if (TryReadPressed(Input, JourneyContract.Jump))
            {
                JourneyContract.RequestTransition(context, JourneyContract.IdleToJump);
            }
            else if (TryReadPressed(Input, JourneyContract.Attack))
            {
                JourneyContract.RequestTransition(context, JourneyContract.IdleToAttack);
            }
        }

        #endregion

        #region Internal Logic

        internal static bool TryReadMove(
            InputIntentContract intent,
            out float moveX,
            out float moveZ)
        {
            moveX = 0f;
            moveZ = 0f;
            for (int index = 0; index < intent.Count; index++)
            {
                if (intent.TryGet(index, out RawInputRecord record) &&
                    record.Action == FixedString64.FromString(JourneyContract.Move))
                {
                    moveX = record.ValueX;
                    moveZ = record.ValueY;
                    return true;
                }
            }

            return false;
        }

        internal static bool TryReadPressed(
            InputIntentContract intent,
            string action)
        {
            var name = FixedString64.FromString(action);
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

        #endregion
    }

    [State("JourneyMove")]
    public sealed class JourneyMoveLogic : StateLogic, IStateUpdate
    {

        #region Public API

        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        public void Update(StateExecutionContext context)
        {
            JourneyMemory.Current = "Move";
            RecordSlot(context);
            bool hasMove = JourneyIdleLogic.TryReadMove(
                Input,
                out float moveX,
                out float moveZ);

            Movement.UseGravity = true;
            Movement.MoveX = moveX;
            Movement.MoveZ = moveZ;

            if (JourneyIdleLogic.TryReadPressed(Input, JourneyContract.Jump))
            {
                JourneyContract.RequestTransition(context, JourneyContract.MoveToJump);
            }
            else if (!hasMove || (moveX == 0f && moveZ == 0f))
            {
                JourneyContract.RequestTransition(context, JourneyContract.MoveToIdle);
            }
        }

        #endregion

        #region Internal Logic

        internal static void RecordSlot(StateExecutionContext context)
        {
            if (context.PreviousContext.Layout != null &&
                context.PreviousContext.Layout.TryResolveSlot(
                    LocoContractIds.StateSlotId,
                    out StateSlot<LocomotionState> slot))
            {
                LocomotionState state = context.PreviousContext.Read(slot);
                JourneyMemory.LastSlotZ = state.PositionZ;
                JourneyMemory.LastSlotGrounded = state.IsGrounded;
            }
        }

        #endregion
    }

    [State("JourneyJump")]
    public sealed class JourneyJumpLogic : StateLogic, IStateEnter, IStateUpdate
    {
        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        private bool _impulseWritten;

        #region Public API

        public void OnEnter(StateExecutionContext context)
        {
            _impulseWritten = false;
        }

        public void Update(StateExecutionContext context)
        {
            JourneyMemory.Current = "Jump";
            JourneyMoveLogic.RecordSlot(context);
            Movement.UseGravity = true;
            if (!_impulseWritten)
            {
                Movement.Jump(6f);
                _impulseWritten = true;
            }

            if (!JourneyMemory.LastSlotGrounded)
            {

                JourneyMemory.WasAirborne = true;
            }

            if (context.PreviousContext.HasCommittedFrame &&
                JourneyMemory.WasAirborne &&
                JourneyMemory.LastSlotGrounded)
            {
                JourneyContract.RequestTransition(context, JourneyContract.JumpToIdle);
            }
        }

        #endregion

    }

    [State("JourneyAttack")]
    public sealed class JourneyAttackLogic : StateLogic, IStateUpdate
    {
        public void Update(StateExecutionContext context)
        {
            JourneyMemory.Current = "Attack";

            if (context.LocalSeconds > 0.15d)
            {
                JourneyContract.RequestTransition(context, JourneyContract.AttackToIdle);
            }
        }
    }
}
