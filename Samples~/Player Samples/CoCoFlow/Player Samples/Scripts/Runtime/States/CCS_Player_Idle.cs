using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Idle : CCS_Player_StateBase
    {
        #region Public API

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            ChangeToBestAvailableState(context as CharacterContext);
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .ReadsContext<CharacterContext>("Intent", "Idle dispatches to the best available player state")
                .CanTransitionTo<CCS_Player_Attack>("Intent.attack")
                .CanTransitionTo<CCS_Player_Interact>("Intent.interact")
                .CanTransitionTo<CCS_Player_Jump>("Intent.jump")
                .CanTransitionTo<CCS_Player_Move>("Intent.move");
        }

        #endregion
    }
}
