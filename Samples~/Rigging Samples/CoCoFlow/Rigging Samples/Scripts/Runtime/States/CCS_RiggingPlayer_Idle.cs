using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.RiggingSamples
{
    public class CCS_RiggingPlayer_Idle : CCS_RiggingPlayer_StateBase
    {
        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            EnableGroundedFootRig();
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            EnableGroundedFootRig();
        }

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder.ReadsContext<CharacterContext>("Motion.isGrounded", "External state selection can keep Idle foot IK active while grounded");
        }
    }
}
