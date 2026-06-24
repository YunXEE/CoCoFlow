using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Attack : CCS_Player_StateBase
    {
        [Header("Attack")]
        [SerializeField] private float attackDuration = 0.35f;
        [SerializeField] private bool allowMoveDuringAttack;
        [SerializeField] private float attackMoveSpeed = 2f;

        private float _finishTime;

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            _finishTime = Time.time + attackDuration;
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (allowMoveDuringAttack)
            {
                ApplyMove(characterContext, attackMoveSpeed);
            }

            if (Time.time < _finishTime) return;

            ChangeToBestAvailableState(characterContext);
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .ReadsContext<CharacterContext>("Intent.move", "Optional attack movement")
                .ReadsContext<CharacterContext>("Intent.look", "Optional attack-facing direction")
                .UsesOperation<CharacterLocomotion>("Optional movement and rotation during attack")
                .CanTransitionTo<CCS_Player_Idle>("Attack duration finished")
                .CanTransitionTo<CCS_Player_Move>("Attack duration finished with move input");
        }

        #endregion
    }
}
