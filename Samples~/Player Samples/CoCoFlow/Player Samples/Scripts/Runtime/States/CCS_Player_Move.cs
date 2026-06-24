using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Move : CCS_Player_StateBase
    {
        [Header("Move")]
        [SerializeField] private float moveSpeed = 5f;

        #region Public API

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (!HasMoveInput(characterContext) ||
                characterContext?.Intent.attack == true ||
                characterContext?.Intent.interact == true ||
                characterContext?.Intent.jump == true)
            {
                ChangeToBestAvailableState(characterContext);
                return;
            }

            ApplyMove(characterContext, moveSpeed);
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .UsesOperation<CharacterLocomotion>("SetMovementVelocity / SetRotation")
                .ReadsContext<CharacterContext>("Intent.move", "Stay in Move while movement input is active")
                .ReadsContext<CharacterContext>("Intent.look", "Facing direction while moving")
                .ReadsContext<CharacterContext>("Intent.attack / Intent.interact / Intent.jump", "Interrupt Move into action states")
                .CanTransitionTo<CCS_Player_Attack>("Intent.attack interrupts Move")
                .CanTransitionTo<CCS_Player_Interact>("Intent.interact interrupts Move")
                .CanTransitionTo<CCS_Player_Jump>("Intent.jump interrupts Move")
                .CanTransitionTo<CCS_Player_Idle>("Move input released");
        }

        #endregion
    }
}
