using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_FullBody : CoCoStateBase
    {
        protected override string DisplayName => "FullBody";

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            builder
                .ReadsContext<CharacterContext>("Motion", "FullBody layer observes whole-body motion state")
                .ReadsContext<CharacterContext>("Intent.move", "FullBody layer can react to movement intent");
        }
    }
}
