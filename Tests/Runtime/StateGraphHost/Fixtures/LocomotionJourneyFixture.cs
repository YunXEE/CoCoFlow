using CoCoFlow.Runtime.Input.Contracts;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

// 端到端移动测试用这些状态响应输入、请求行走或跳跃，并通过移动操作驱动角色。
namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>保存端到端移动用例从实际状态回调读到的最近节点、位置和着地事实。</summary>
    public static class JourneyMemory
    {
        public static string Current = "None";
        public static float LastSlotZ;
        public static bool LastSlotGrounded = true;
        public static bool WasAirborne;

        /// <summary>清空各旅程状态记录的节点与位姿，使新一轮步进独立。</summary>
        public static void Reset()
        {
            Current = "None";
            LastSlotZ = 0f;
            LastSlotGrounded = true;
            WasAirborne = false;
        }
    }

    /// <summary>为测试图的移动、跳跃和攻击输入以及它们的出边提供稳定身份。</summary>
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

        /// <summary>在当前节点出边中按稳定 ID 选择匹配项并向框架提出请求。</summary>
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

    /// <summary>在待机时检查玩家本步输入，按实际动作请求已有的出边。</summary>
    [State("JourneyIdle")]
    public sealed class JourneyIdleLogic : StateLogic, IStateUpdate
    {

        #region Public API

        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        /// <summary>按原始输入中的移动、跳跃或攻击记录请求对应的已连接出边。</summary>
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

    /// <summary>从本步输入写入角色移动操作并记录上一提交的移动结果。</summary>
    [State("JourneyMove")]
    public sealed class JourneyMoveLogic : StateLogic, IStateUpdate
    {

        #region Public API

        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        /// <summary>保存上一提交的移动结果并按输入方向写连续输出，再按动作决定是否申请离开。</summary>
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

    /// <summary>在进入跳跃后写一次跳跃冲量，并依据实际移动状态决定返回待机。</summary>
    [State("JourneyJump")]
    public sealed class JourneyJumpLogic : StateLogic, IStateEnter, IStateUpdate
    {
        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        private bool _impulseWritten;

        #region Public API

        /// <summary>清除本次激活的冲量标记，使首次更新能够提出一次跳跃。</summary>
        public void OnEnter(StateExecutionContext context)
        {
            _impulseWritten = false;
        }

        /// <summary>写入尚未提交过的跳跃请求，并根据角色着地信息申请返回待机。</summary>
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

    /// <summary>经过创作的动作时段后显式请求回到待机节点。</summary>
    [State("JourneyAttack")]
    public sealed class JourneyAttackLogic : StateLogic, IStateUpdate
    {
        /// <summary>记录当前攻击态，本地时间超过测试图规定的时长时请求回到待机。</summary>
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
