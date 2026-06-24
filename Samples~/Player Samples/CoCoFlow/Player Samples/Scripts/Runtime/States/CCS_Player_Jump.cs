using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Jump : CCS_Player_StateBase
    {
        [Header("Jump")]
        [SerializeField] private float jumpForce = 5f;
        [SerializeField] private float airMoveSpeed = 3f;
        [SerializeField] private float minAirTime = 0.2f;

        private float _enterTime;

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            _enterTime = Time.time;
            Locomotion?.Jump(jumpForce);
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            ApplyMove(characterContext, airMoveSpeed);

            if (Time.time - _enterTime < minAirTime) return;

            if (Locomotion == null || Locomotion.IsGrounded)
            {
                ChangeToBestAvailableState(characterContext);
            }
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .ReadsContext<CharacterContext>("Intent.move", "Air movement input")
                .ReadsContext<CharacterContext>("Intent.look", "Air-facing direction")
                .UsesOperation<CharacterLocomotion>("Jump / SetMovementVelocity / SetRotation / IsGrounded")
                .CanTransitionTo<CCS_Player_Idle>("Grounded after minimum air time")
                .CanTransitionTo<CCS_Player_Move>("Grounded after minimum air time with move input");
        }

        #endregion
    }
}
