using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public abstract class CCS_Enemy_StateBase : CoCoStateBase
    {
        [Header("Transition")]
        [SerializeField] private float fleeHealthRatio = 0.25f;

        [Header("Navigation")]
        [SerializeField] private int navigationPriority = 20;

        protected CharacterNavigationContext NavigationContext => CharacterContext?.Navigation;
        protected Transform ActorTransform { get; private set; }
        protected CharacterContext CharacterContext => Controller != null ? Controller.Context as CharacterContext : null;
        protected abstract string NavigationOwner { get; }
        protected int NavigationPriority => navigationPriority;

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            builder
                .ReadsContext<CharacterContext>("Intent.attack")
                .ReadsContext<CharacterContext>("Intent.desiredTarget")
                .ReadsContext<CharacterContext>("Intent.hasMovePosition")
                .ReadsContext<CharacterContext>("Resources.CurrentHealth")
                .ReadsContext<CharacterContext>("Resources.MaxHealth")
                .ReadsContext<CharacterContext>("Navigation")
                .WritesContext<CharacterContext>("Navigation")
                .UsesOperation<CharacterNavigationMotor>("Consumes Navigation commands written by enemy states")
                .CanTransitionTo<CCS_Enemy_Flee>("Shared enemy selector can flee on low health.")
                .CanTransitionTo<CCS_Enemy_Combat>("Shared enemy selector can enter combat intent.")
                .CanTransitionTo<CCS_Enemy_Approach>("Shared enemy selector can chase a target or move position.")
                .CanTransitionTo<CCS_Enemy_Patrol>("Shared enemy selector can return to patrol navigation.")
                .CanTransitionTo<CCS_Enemy_Idle>("Shared enemy selector can fall back to idle.");
        }

        #endregion

        #region Public API

        public override void Init(CoCoStateController targetController)
        {
            base.Init(targetController);
            ActorTransform = targetController.transform.parent != null
                ? targetController.transform.parent
                : targetController.transform;
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
            if (ShouldFlee(context) && IfHasState<CCS_Enemy_Flee>())
            {
                ChangeState<CCS_Enemy_Flee>();
                return;
            }

            if (context != null && context.Intent.attack && IfHasState<CCS_Enemy_Combat>())
            {
                ChangeState<CCS_Enemy_Combat>();
                return;
            }

            if (HasTargetIntent(context) && IfHasState<CCS_Enemy_Approach>())
            {
                ChangeState<CCS_Enemy_Approach>();
                return;
            }

            if (NavigationContext != null &&
                NavigationContext.Mode == CharacterNavigationMode.Patrol &&
                NavigationContext.HasDestination &&
                IfHasState<CCS_Enemy_Patrol>())
            {
                ChangeState<CCS_Enemy_Patrol>();
                return;
            }

            if (IfHasState<CCS_Enemy_Idle>())
            {
                ChangeState<CCS_Enemy_Idle>();
            }
        }

        protected Vector3 GetActorPosition()
        {
            return ActorTransform != null ? ActorTransform.position : transform.position;
        }

        #endregion
    }
}
