using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public class CCS_Enemy_Combat : CCS_Enemy_StateBase
    {
        protected override string NavigationOwner => "CCS_Enemy_Combat";

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            if (TryClaimNavigation())
            {
                NavigationContext.SetMode(CharacterNavigationMode.Combat);
                NavigationContext.ClearDestination();
                NavigationContext.ClearDesiredVelocity();
            }
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (ShouldFlee(characterContext))
            {
                ReleaseNavigation();
                Controller.ChangeState<CCS_Enemy_Flee>();
                return;
            }

            if (characterContext?.Intent.attack == true)
            {
                return;
            }

            ReleaseNavigation();
            ChangeToBestAvailableState(characterContext);
        }

        public override void Exit(ICoCoContext context)
        {
            ReleaseNavigation();
            base.Exit(context);
        }

        #endregion
    }
}
