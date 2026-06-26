using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.RiggingSamples
{
    public class CCS_RiggingPlayer_Jump : CCS_RiggingPlayer_StateBase
    {
        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            DisableAirborneFootRig();
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            DisableAirborneFootRig();
        }

        public override void Exit(ICoCoContext context)
        {
            EnableGroundedFootRig();
            base.Exit(context);
        }

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder.ReadsContext<CharacterContext>("Motion.isGrounded", "External state selection can choose Jump while airborne");
        }
    }
}
