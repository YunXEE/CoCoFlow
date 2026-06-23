using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_UpperLayer : CoCoStateBase
    {
        protected override string DisplayName => "UpperLayer";

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            builder
                .ReadsContext<CharacterContext>("Intent.attack", "UpperLayer layer can react to attack intent")
                .ReadsContext<CharacterContext>("Intent.interact", "UpperLayer layer can react to interaction intent");
        }
    }
}
