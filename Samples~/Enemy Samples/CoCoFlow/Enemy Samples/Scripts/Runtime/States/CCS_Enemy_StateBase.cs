using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public abstract class CCS_Enemy_StateBase : CoCoStateMachineBase
    {
        [Header("Transition")]
        [SerializeField] private float fleeHealthRatio = 0.25f;

        [Header("Navigation")]
        [SerializeField] private int navigationPriority = 20;

        protected CharacterNavigation Navigation { get; private set; }
        protected CharacterNavigationContext NavigationContext => Navigation != null ? Navigation.Context : null;
        protected Transform ActorTransform { get; private set; }
        protected CharacterContext CharacterContext => Controller != null ? Controller.Context as CharacterContext : null;
        protected abstract string NavigationOwner { get; }
        protected int NavigationPriority => navigationPriority;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            ActorTransform = targetController.transform.parent != null
                ? targetController.transform.parent
                : targetController.transform;
            Navigation = ActorTransform.GetComponent<CharacterNavigation>();
        }

        #endregion

        #region Internal Logic

        protected bool HasTargetIntent(CharacterContext context)
        {
            return context != null &&
                   (context.Intent.desiredTarget != null || context.Intent.hasMovePosition);
        }

        protected bool ShouldFlee(CharacterContext context)
        {
            if (context == null || context.Resources.MaxHealth <= 0f) return false;
            return context.Resources.CurrentHealth / context.Resources.MaxHealth <= fleeHealthRatio;
        }

        protected bool TryClaimNavigation(bool force = false)
        {
            return NavigationContext != null &&
                   NavigationContext.TryClaimControl(NavigationOwner, navigationPriority, force);
        }

        protected void ReleaseNavigation()
        {
            if (NavigationContext == null ||
                !NavigationContext.HasControl(NavigationOwner))
            {
                return;
            }

            NavigationContext.ClearDestination();
            NavigationContext.ClearDesiredVelocity();
            NavigationContext.ReleaseControl(NavigationOwner);
        }

        protected void ChangeToBestAvailableState(CharacterContext context)
        {
            if (ShouldFlee(context) && Controller.IfHasState<CCS_Enemy_Flee>())
            {
                Controller.ChangeState<CCS_Enemy_Flee>();
                return;
            }

            if (context != null && context.Intent.attack && Controller.IfHasState<CCS_Enemy_Combat>())
            {
                Controller.ChangeState<CCS_Enemy_Combat>();
                return;
            }

            if (HasTargetIntent(context) && Controller.IfHasState<CCS_Enemy_Approach>())
            {
                Controller.ChangeState<CCS_Enemy_Approach>();
                return;
            }

            if (NavigationContext != null &&
                NavigationContext.Mode == CharacterNavigationMode.Patrol &&
                NavigationContext.HasDestination &&
                Controller.IfHasState<CCS_Enemy_Patrol>())
            {
                Controller.ChangeState<CCS_Enemy_Patrol>();
                return;
            }

            if (Controller.IfHasState<CCS_Enemy_Idle>())
            {
                Controller.ChangeState<CCS_Enemy_Idle>();
            }
        }

        protected Vector3 GetActorPosition()
        {
            return ActorTransform != null ? ActorTransform.position : transform.position;
        }

        #endregion
    }
}
