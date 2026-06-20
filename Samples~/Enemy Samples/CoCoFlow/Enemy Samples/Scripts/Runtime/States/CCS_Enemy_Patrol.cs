using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public class CCS_Enemy_Patrol : CCS_Enemy_StateBase
    {
        protected override string NavigationOwner => "CCS_Enemy_Patrol";

        #region Public API

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (ShouldFlee(characterContext) ||
                characterContext?.Intent.attack == true ||
                HasTargetIntent(characterContext))
            {
                ChangeToBestAvailableState(characterContext);
                return;
            }

            if (NavigationContext == null ||
                NavigationContext.Mode != CharacterNavigationMode.Patrol ||
                !NavigationContext.HasDestination)
            {
                Controller.ChangeState<CCS_Enemy_Idle>();
            }
        }

        #endregion
    }
}
