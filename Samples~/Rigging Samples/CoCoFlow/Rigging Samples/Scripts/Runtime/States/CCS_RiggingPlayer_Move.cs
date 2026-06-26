using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.RiggingSamples
{
    public class CCS_RiggingPlayer_Move : CCS_RiggingPlayer_StateBase
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
            builder
                .ReadsContext<CharacterContext>("Intent.move", "External state selection can choose Move before this operation state runs");
        }
    }
}
