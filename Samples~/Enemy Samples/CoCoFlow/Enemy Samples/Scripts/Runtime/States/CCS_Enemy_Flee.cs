using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.EnemySamples
{
    public class CCS_Enemy_Flee : CCS_Enemy_StateBase
    {
        [Header("Flee")]
        [SerializeField] private float fleeSpeed = 6f;
        [SerializeField] private float fleeDistance = 7f;
        [SerializeField] private float fleeDuration = 1.5f;

        private float _finishTime;

        protected override string NavigationOwner => "CCS_Enemy_Flee";

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            _finishTime = Time.time + fleeDuration;

            if (TryClaimNavigation(true))
            {
                NavigationContext.SetDestination(
                    CalculateFleeDestination(context as CharacterContext),
                    fleeSpeed,
                    0.25f,
                    CharacterNavigationMode.ReturnToRoute);
            }
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            if (Time.time < _finishTime) return;

            var characterContext = context as CharacterContext;
            if (ShouldFlee(characterContext) && TryClaimNavigation(true))
            {
                _finishTime = Time.time + fleeDuration;
                NavigationContext.SetDestination(
                    CalculateFleeDestination(characterContext),
                    fleeSpeed,
                    0.25f,
                    CharacterNavigationMode.ReturnToRoute);
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

        #region Internal Logic

        private Vector3 CalculateFleeDestination(CharacterContext context)
        {
            Vector3 position = GetActorPosition();
            Vector3 threatPosition = context?.Perception.currentTarget != null
                ? context.Perception.currentTarget.position
                : context?.Perception.lastKnownPosition ?? position - ActorTransform.forward;

            Vector3 away = position - threatPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = ActorTransform != null ? -ActorTransform.forward : Vector3.back;
            }

            return position + away.normalized * fleeDistance;
        }

        #endregion
    }
}
