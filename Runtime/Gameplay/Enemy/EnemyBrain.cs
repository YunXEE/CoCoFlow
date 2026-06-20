using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    public class EnemyBrain : MonoBehaviour
    {
        [Header("Intent")]
        [SerializeField] private EnemyIntentData intentData;
        [SerializeField] private EnemyConfigData configData;

        [Header("Rules")]
        [SerializeField] private EnemyEngagementZone engagementZone;
        [SerializeField] private EnemySpline enemySpline;

        [Header("Context")]
        [SerializeField] private MonoBehaviour characterContextProvider;
        [SerializeField] private MonoBehaviour navigationProvider;

        [Header("Update")]
        [SerializeField] private bool updateAutomatically = true;

        private readonly Collider[] _hitBuffer = new Collider[32];
        private CharacterContext _characterContext;
        private CharacterNavigationContext _navigationContext;
        private float _nextThinkTime;
        private float _lostTargetStartTime = -1f;

        #region Public API

        public EnemyIntentData IntentData => intentData;

        public void SetIntentData(EnemyIntentData data)
        {
            intentData = data;
        }

        public void SetConfigData(EnemyConfigData data)
        {
            configData = data;
        }

        public void SetCharacterContextProvider(MonoBehaviour provider)
        {
            characterContextProvider = provider;
            _characterContext = null;
        }

        public void SetNavigationProvider(MonoBehaviour provider)
        {
            navigationProvider = provider;
            _navigationContext = null;
        }

        public void SetEnemySpline(EnemySpline spline)
        {
            enemySpline = spline;
        }

        public bool Tick(bool ignorePollInterval = false)
        {
            if (!ValidateIntentData()) return false;
            if (!ignorePollInterval && !CanThinkThisFrame()) return false;

            var characterContext = CharacterContext;
            if (characterContext == null || configData == null) return false;

            Transform currentTarget = characterContext.Perception.currentTarget;
            bool hasVisibleTarget = EnemyVisionQuery.TryFindVisibleTarget(
                transform,
                configData,
                intentData.TargetLayerMask,
                currentTarget,
                _hitBuffer,
                out EnemyVisionQueryResult visionResult);

            if (hasVisibleTarget && CanEngage(visionResult.LastKnownPosition))
            {
                ApplyVisibleTarget(characterContext, visionResult);
                return true;
            }

            ApplyLostTarget(characterContext);
            return true;
        }

        #endregion

        #region Internal Logic

        private CharacterContext CharacterContext => ResolveCharacterContext();
        private CharacterNavigationContext NavigationContext => ResolveNavigationContext();

        private void Awake()
        {
            ResolveCharacterContext();
            ResolveNavigationContext();
            if (enemySpline == null)
            {
                enemySpline = GetComponent<EnemySpline>();
            }
        }

        private void OnEnable()
        {
            ValidateIntentData();
        }

        private void Update()
        {
            if (updateAutomatically)
            {
                Tick();
            }
        }

        private bool ValidateIntentData()
        {
            if (intentData != null) return true;

            CoCoLog.Error($"[EnemyBrain] {gameObject.name} 缺少 EnemyIntentData，EnemyBrain 已禁用。");
            enabled = false;
            return false;
        }

        private bool CanThinkThisFrame()
        {
            float interval = configData != null ? configData.SensorPollInterval : 0f;
            if (Time.time < _nextThinkTime) return false;

            _nextThinkTime = Time.time + interval;
            return true;
        }

        private void ApplyVisibleTarget(
            CharacterContext characterContext,
            EnemyVisionQueryResult visionResult)
        {
            _lostTargetStartTime = -1f;
            Transform target = visionResult.Target;

            characterContext.Perception.currentTarget = target;
            characterContext.Perception.currentTargetId = ResolveTargetId(target);
            characterContext.Perception.lastKnownPosition = visionResult.LastKnownPosition;
            characterContext.Perception.isTargetVisible = true;

            characterContext.Intent.desiredTarget = target;
            characterContext.Intent.desiredTargetId = characterContext.Perception.currentTargetId;

            var navigationContext = NavigationContext;
            bool canWriteNavigation = navigationContext == null;
            if (navigationContext != null && intentData.ClaimNavigationOnTargetVisible)
            {
                canWriteNavigation = navigationContext.TryClaimControl(
                    intentData.BrainOwnerId,
                    intentData.NavigationPriority);
            }

            if (visionResult.Distance <= intentData.AttackRange)
            {
                characterContext.Intent.attack = true;
                characterContext.Intent.hasMovePosition = false;
                characterContext.Intent.desiredMovePosition = Vector3.zero;

                if (canWriteNavigation && navigationContext != null)
                {
                    navigationContext.SetMode(CharacterNavigationMode.Combat);
                    navigationContext.ClearDestination();
                    navigationContext.ClearDesiredVelocity();
                }
                return;
            }

            characterContext.Intent.attack = false;
            characterContext.Intent.hasMovePosition = true;
            characterContext.Intent.desiredMovePosition = target.position;

            if (canWriteNavigation && navigationContext != null)
            {
                navigationContext.SetDestination(
                    target.position,
                    configData.ChaseSpeed,
                    intentData.AttackRange,
                    CharacterNavigationMode.Chase);
            }
        }

        private void ApplyLostTarget(CharacterContext characterContext)
        {
            Transform previousTarget = characterContext.Perception.currentTarget;
            if (previousTarget == null)
            {
                characterContext.Perception.isTargetVisible = false;
                return;
            }

            characterContext.Perception.isTargetVisible = false;
            if (_lostTargetStartTime < 0f)
            {
                _lostTargetStartTime = Time.time;
            }

            float lostDuration = Time.time - _lostTargetStartTime;
            if (lostDuration < intentData.DisengageDelay)
            {
                characterContext.Intent.attack = false;
                characterContext.Intent.hasMovePosition = true;
                characterContext.Intent.desiredMovePosition = characterContext.Perception.lastKnownPosition;

                var navigationContext = NavigationContext;
                if (navigationContext != null &&
                    navigationContext.HasControl(intentData.BrainOwnerId))
                {
                    navigationContext.SetDestination(
                        characterContext.Perception.lastKnownPosition,
                        configData.ChaseSpeed,
                        intentData.AttackRange,
                        CharacterNavigationMode.Chase);
                }
                return;
            }

            ClearEngagement(characterContext);
        }

        private void ClearEngagement(CharacterContext characterContext)
        {
            characterContext.Perception.currentTarget = null;
            characterContext.Perception.currentTargetId = string.Empty;
            characterContext.Perception.isTargetVisible = false;

            characterContext.Intent.desiredTarget = null;
            characterContext.Intent.desiredTargetId = string.Empty;
            characterContext.Intent.hasMovePosition = false;
            characterContext.Intent.desiredMovePosition = Vector3.zero;
            characterContext.Intent.attack = false;

            var navigationContext = NavigationContext;
            if (navigationContext != null &&
                navigationContext.ReleaseControl(intentData.BrainOwnerId))
            {
                navigationContext.ClearDestination();
                navigationContext.ClearDesiredVelocity();
            }

            if (intentData.ReleaseToSplineOnTargetLost && enemySpline != null)
            {
                enemySpline.RequestResume(true);
            }

            _lostTargetStartTime = -1f;
        }

        private bool CanEngage(Vector3 targetPosition)
        {
            if (engagementZone == null || !intentData.RequireEngagementZone) return true;
            return engagementZone.IsPositionInsideZone(targetPosition);
        }

        private CharacterContext ResolveCharacterContext()
        {
            if (_characterContext != null) return _characterContext;

            if (TryGetContextFromProvider(characterContextProvider, out _characterContext))
            {
                return _characterContext;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _characterContext))
                {
                    if (characterContextProvider == null)
                    {
                        characterContextProvider = behaviour;
                    }
                    return _characterContext;
                }
            }

            return null;
        }

        private CharacterNavigationContext ResolveNavigationContext()
        {
            if (_navigationContext != null) return _navigationContext;

            if (TryGetNavigationContextFromProvider(navigationProvider, out _navigationContext))
            {
                return _navigationContext;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetNavigationContextFromProvider(behaviour, out _navigationContext))
                {
                    if (navigationProvider == null)
                    {
                        navigationProvider = behaviour;
                    }
                    return _navigationContext;
                }
            }

            return null;
        }

        private static bool TryGetContextFromProvider(
            object provider,
            out CharacterContext targetContext)
        {
            if (provider is ICoCoContextProvider<CharacterContext> typedProvider)
            {
                targetContext = typedProvider.Context;
                return targetContext != null;
            }

            targetContext = null;
            return false;
        }

        private static bool TryGetNavigationContextFromProvider(
            object provider,
            out CharacterNavigationContext targetContext)
        {
            if (provider is ICoCoContextProvider<CharacterNavigationContext> typedProvider)
            {
                targetContext = typedProvider.Context;
                return targetContext != null;
            }

            targetContext = null;
            return false;
        }

        private static string ResolveTargetId(Transform target)
        {
            if (target == null) return string.Empty;

            var stableIdProvider = target.GetComponentInParent<ICoCoStableEntityIdProvider>();
            if (stableIdProvider != null && !string.IsNullOrEmpty(stableIdProvider.StableEntityId))
            {
                return stableIdProvider.StableEntityId;
            }

            return target.GetInstanceID().ToString();
        }

        private void OnValidate()
        {
            if (ReferenceEquals(characterContextProvider, this))
            {
                characterContextProvider = null;
            }

            if (ReferenceEquals(navigationProvider, this))
            {
                navigationProvider = null;
            }
        }

        #endregion
    }
}
