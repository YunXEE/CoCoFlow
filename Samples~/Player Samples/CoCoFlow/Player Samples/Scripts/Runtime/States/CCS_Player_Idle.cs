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
    }
}
