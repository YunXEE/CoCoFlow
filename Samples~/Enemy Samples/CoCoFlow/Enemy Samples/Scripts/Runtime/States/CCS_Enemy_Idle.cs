using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public class CCS_Enemy_Idle : CCS_Enemy_StateBase
    {
        protected override string NavigationOwner => "CCS_Enemy_Idle";

        #region Public API

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            ChangeToBestAvailableState(context as CharacterContext);
        }

        #endregion
    }
}
