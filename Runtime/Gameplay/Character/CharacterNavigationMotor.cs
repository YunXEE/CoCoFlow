using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    [RequireComponent(typeof(CharacterLocomotion))]
    public class CharacterNavigationMotor : MonoBehaviour
    {
        [Header("Context")]
        [CoCoContextProvider(typeof(CharacterContext))]
        [SerializeField] private MonoBehaviour contextProvider;

        [Header("Components")]
        [SerializeField] private CharacterLocomotion locomotion;
        [SerializeField] private NavMeshAgent navMeshAgent;

        [Header("Update")]
        [SerializeField] private bool updateAutomatically = true;
        [SerializeField] private bool rotateTowardsVelocity = true;

        private CharacterNavigationContext _navigationContext;
        private CharacterController _characterController;

        #region Public API

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _navigationContext = null;
        }

        public bool ExecuteNavigation(float deltaTime)
        {
            var context = NavigationContext;
            if (context == null) return false;

            ConsumeWarpRequest(context);
            UpdateDesiredVelocity(context);
            ApplyDesiredVelocity(context, deltaTime);
            return true;
        }

        #endregion

        #region Internal Logic

        private CharacterNavigationContext NavigationContext => ResolveNavigationContext();

        private void Awake()
        {
            ResolveComponents();
            ResolveNavigationContext();
        }

        private void Update()
        {
            if (updateAutomatically)
            {
                ExecuteNavigation(Time.deltaTime);
            }
        }

        private void ConsumeWarpRequest(CharacterNavigationContext context)
        {
            if (!context.TryConsumeWarpRequest(out Vector3 warpPosition)) return;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.Warp(warpPosition);
                navMeshAgent.ResetPath();
            }

            if (_characterController != null && _characterController.enabled)
            {
                _characterController.enabled = false;
                transform.position = warpPosition;
                _characterController.enabled = true;
            }
            else
            {
                transform.position = warpPosition;
            }
        }

        private void UpdateDesiredVelocity(CharacterNavigationContext context)
        {
            if (!context.HasDestination)
            {
                context.ClearDesiredVelocity();
                if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                {
                    navMeshAgent.ResetPath();
                }
                return;
            }

            if (TryUpdateAgentVelocity(context)) return;

            Vector3 toDestination = context.Destination - transform.position;
            toDestination.y = 0f;
            if (toDestination.magnitude <= context.StoppingDistance)
            {
                context.ClearDesiredVelocity();
                return;
            }

            float speed = context.DesiredSpeed > 0f ? context.DesiredSpeed : 1f;
            context.SetDesiredVelocity(toDestination.normalized * speed);
        }

        private bool TryUpdateAgentVelocity(CharacterNavigationContext context)
        {
            if (navMeshAgent == null ||
                !navMeshAgent.isActiveAndEnabled ||
                !navMeshAgent.isOnNavMesh)
            {
                return false;
            }

            navMeshAgent.updatePosition = false;
            navMeshAgent.updateRotation = false;
            navMeshAgent.nextPosition = transform.position;
            if (context.DesiredSpeed > 0f)
            {
                navMeshAgent.speed = context.DesiredSpeed;
            }

            navMeshAgent.stoppingDistance = context.StoppingDistance;
            navMeshAgent.SetDestination(context.Destination);
            context.SetDesiredVelocity(navMeshAgent.desiredVelocity);
            return true;
        }

        private void ApplyDesiredVelocity(CharacterNavigationContext context, float deltaTime)
        {
            if (locomotion == null || !context.HasDesiredVelocity) return;

            locomotion.SetMovementVelocity(context.DesiredVelocity);
            if (rotateTowardsVelocity && deltaTime >= 0f)
            {
                locomotion.SetRotation(context.DesiredVelocity);
            }
        }

        private CharacterNavigationContext ResolveNavigationContext()
        {
            if (_navigationContext != null) return _navigationContext;

            if (TryGetContextFromProvider(contextProvider, out _navigationContext))
            {
                return _navigationContext;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _navigationContext))
                {
                    if (contextProvider == null)
                    {
                        contextProvider = behaviour;
                    }
                    return _navigationContext;
                }
            }

            return null;
        }

        private static bool TryGetContextFromProvider(
            object provider,
            out CharacterNavigationContext targetContext)
        {
            if (provider is ICoCoContextProvider<CharacterContext> characterProvider)
            {
                targetContext = characterProvider.Context?.Navigation;
                return targetContext != null;
            }

            targetContext = null;
            return false;
        }

        private void ResolveComponents()
        {
            if (locomotion == null)
            {
                locomotion = GetComponent<CharacterLocomotion>();
            }

            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }
        }

        private void OnValidate()
        {
            if (ReferenceEquals(contextProvider, this))
            {
                contextProvider = null;
            }

            ResolveComponents();
        }

        private void Reset()
        {
            ResolveComponents();

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (contextProvider == null &&
                    behaviour is ICoCoContextProvider<CharacterContext>)
                {
                    contextProvider = behaviour;
                    break;
                }
            }
        }

        #endregion
    }
}
