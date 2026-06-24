using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public abstract class CCS_Player_StateBase : CoCoStateBase
    {
        [Header("Movement")]
        [SerializeField] private float moveDeadZone = 0.05f;

        protected CharacterContext CharacterContext => Controller != null ? Controller.Context as CharacterContext : null;
        protected Transform ActorTransform { get; private set; }
        protected CharacterLocomotion Locomotion { get; private set; }
        protected CharacterNavigationContext NavigationContext => CharacterContext?.Navigation;

        #region Public API

        public override void Init(CoCoStateController targetController)
        {
            base.Init(targetController);
            ActorTransform = targetController.transform.parent != null
                ? targetController.transform.parent
                : targetController.transform;
            Locomotion = ActorTransform.GetComponent<CharacterLocomotion>();
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
        }

        #endregion

        #region Internal Logic

        protected bool HasMoveInput(CharacterContext context)
        {
            return context != null && context.Intent.move.sqrMagnitude > moveDeadZone * moveDeadZone;
        }

        protected Vector3 GetMoveDirection(CharacterContext context)
        {
            if (!HasMoveInput(context)) return Vector3.zero;

            Vector2 move = Vector2.ClampMagnitude(context.Intent.move, 1f);
            return new Vector3(move.x, 0f, move.y);
        }

        protected void ApplyMove(CharacterContext context, float speed)
        {
            if (Locomotion == null) return;

            Vector3 direction = GetMoveDirection(context);
            if (direction.sqrMagnitude < 0.0001f) return;

            Locomotion.SetMovementVelocity(direction.normalized * speed);

            Vector3 lookDirection = context != null && context.Intent.look.sqrMagnitude > 0.0001f
                ? new Vector3(context.Intent.look.x, 0f, context.Intent.look.y)
                : direction;
            Locomotion.SetRotation(lookDirection);
        }

        protected void ClearNavigationDrive(string owner)
        {
            if (NavigationContext == null) return;

            if (NavigationContext.HasControl(owner))
            {
                NavigationContext.ClearDestination();
                NavigationContext.ClearDesiredVelocity();
                NavigationContext.ReleaseControl(owner);
            }
        }

        protected void ChangeToBestAvailableState(CharacterContext context)
        {
            if (context != null && context.Intent.attack && IfHasState<CCS_Player_Attack>())
            {
                ChangeState<CCS_Player_Attack>();
                return;
            }

            if (context != null && context.Intent.interact && IfHasState<CCS_Player_Interact>())
            {
                ChangeState<CCS_Player_Interact>();
                return;
            }

            if (context != null && context.Intent.jump && IfHasState<CCS_Player_Jump>())
            {
                ChangeState<CCS_Player_Jump>();
                return;
            }

            if (HasMoveInput(context) && IfHasState<CCS_Player_Move>())
            {
                ChangeState<CCS_Player_Move>();
                return;
            }

            if (IfHasState<CCS_Player_Idle>())
            {
                ChangeState<CCS_Player_Idle>();
            }
        }

        #endregion
    }
}
