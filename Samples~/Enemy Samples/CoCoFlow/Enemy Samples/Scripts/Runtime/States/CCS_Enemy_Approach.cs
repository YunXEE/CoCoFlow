using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public class CCS_Enemy_Approach : CCS_Enemy_StateBase
    {
        [Header("Approach")]
        [SerializeField] private float approachSpeed = 5f;
        [SerializeField] private float stoppingDistance = 1.75f;

        protected override string NavigationOwner => "CCS_Enemy_Approach";

        #region Public API

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (ShouldFlee(characterContext))
            {
                ReleaseNavigation();
                ChangeState<CCS_Enemy_Flee>();
                return;
            }

            if (characterContext?.Intent.attack == true)
            {
                ReleaseNavigation();
                ChangeState<CCS_Enemy_Combat>();
                return;
            }

            if (!TryGetApproachDestination(characterContext, out Vector3 destination))
            {
                ReleaseNavigation();
                ChangeToBestAvailableState(characterContext);
                return;
            }

            if (TryClaimNavigation())
            {
                NavigationContext.SetDestination(
                    destination,
                    approachSpeed,
                    stoppingDistance,
                    CharacterNavigationMode.Chase);
            }
        }

        public override void Exit(ICoCoContext context)
        {
            ReleaseNavigation();
            base.Exit(context);
        }

        #endregion

        #region Internal Logic

        private static bool TryGetApproachDestination(
            CharacterContext context,
            out Vector3 destination)
        {
            destination = Vector3.zero;
            if (context == null) return false;

            if (context.Intent.hasMovePosition)
            {
                destination = context.Intent.desiredMovePosition;
                return true;
            }

            if (context.Intent.desiredTarget != null)
            {
                destination = context.Intent.desiredTarget.position;
                return true;
            }

            return false;
        }

        #endregion
    }
}
