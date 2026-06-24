using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    public class EnemyBrain :
        MonoBehaviour,
        ICharacterContextSource,
        ICharacterContextSourceUpdateMode
    {
        [Header("Intent")]
        [SerializeField] private EnemyIntentData intentData;
        [SerializeField] private EnemyConfigData configData;

        [Header("Rules")]
        [SerializeField] private EnemyEngagementZone engagementZone;
        [SerializeField] private EnemySpline enemySpline;

        [Header("Context")]
        [SerializeField] private MonoBehaviour characterContextProvider;

        [Header("Update")]
        [SerializeField] private bool updateAutomatically = true;

        private readonly Collider[] _hitBuffer = new Collider[32];
        private CharacterContext _characterContext;
        private float _nextThinkTime;
        private float _lostTargetStartTime = -1f;
        private bool _isProviderDriven;

        #region Public API

        public EnemyIntentData IntentData => intentData;
        public int Priority => intentData != null ? intentData.NavigationPriority : 40;
        public bool IsProviderDriven => _isProviderDriven;

        public void WriteToContext(CharacterContext context)
        {
            Tick(context);
        }

        public void SetProviderDriven(bool providerDriven)
        {
            _isProviderDriven = providerDriven;
        }

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

        public void SetEnemySpline(EnemySpline spline)
        {
            enemySpline = spline;
        }

        public bool Tick(bool ignorePollInterval = false)
        {
            return Tick(CharacterContext, ignorePollInterval);
        }

        public bool Tick(CharacterContext characterContext, bool ignorePollInterval = false)
        {
            if (!ValidateIntentData()) return false;
            if (!ignorePollInterval && !CanThinkThisFrame()) return false;

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

        private void Awake()
        {
            ResolveCharacterContext();
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
            if (updateAutomatically && !_isProviderDriven)
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

            var navigationContext = characterContext.Navigation;
            bool canWriteNavigation = navigationContext != null;
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
                if (HasEngagementFacts(characterContext))
                {
                    ClearEngagement(characterContext);
                }
                else
                {
                    characterContext.Perception.isTargetVisible = false;
                }

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

                var navigationContext = characterContext.Navigation;
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

            var navigationContext = characterContext.Navigation;
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

        private bool HasEngagementFacts(CharacterContext characterContext)
        {
            if (characterContext == null) return false;

            if (characterContext.Perception.isTargetVisible ||
                !string.IsNullOrEmpty(characterContext.Perception.currentTargetId) ||
                !string.IsNullOrEmpty(characterContext.Intent.desiredTargetId) ||
                characterContext.Intent.desiredTarget != null ||
                characterContext.Intent.hasMovePosition ||
                characterContext.Intent.attack)
            {
                return true;
            }

            var navigationContext = characterContext.Navigation;
            return navigationContext != null &&
                   navigationContext.HasControl(intentData.BrainOwnerId);
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

        }

        #endregion
    }
}
