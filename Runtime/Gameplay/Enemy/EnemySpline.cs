using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    public class EnemySpline :
        MonoBehaviour,
        ICharacterContextSource,
        ICharacterContextSourceUpdateMode
    {
        [Header("Route")]
        [SerializeField] private SplineContainer splineContainer;
        [SerializeField] private EnemyConfigData configData;

        [Header("Context")]
        [SerializeField] private MonoBehaviour contextProvider;

        [Header("Navigation Control")]
        [SerializeField] private string splineOwnerId = "EnemySpline";
        [SerializeField] private int navigationPriority;
        [SerializeField] private bool updateAutomatically = true;
        [SerializeField] private bool warpToRouteOnResume = true;
        [SerializeField] private float navMeshSampleRadius = 3f;

        private CharacterContext _characterContext;
        private float _splineT;
        private float _splineLength;
        private bool _reverseDirection;
        private bool _routeInitialized;
        private bool _resumeRequested;
        private bool _resumeShouldWarp;
        private bool _isProviderDriven;

        #region Public API

        public SplineContainer SplineContainer => splineContainer;
        public float RouteProgress => _splineT;
        public bool ReverseDirection => _reverseDirection;
        public int Priority => navigationPriority;
        public bool IsProviderDriven => _isProviderDriven;

        public void SetSplineContainer(SplineContainer container)
        {
            splineContainer = container;
            _routeInitialized = false;
        }

        public void SetConfigData(EnemyConfigData data)
        {
            configData = data;
        }

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _characterContext = null;
        }

        public void RequestResume(bool warpToRoute)
        {
            _resumeRequested = true;
            _resumeShouldWarp = warpToRoute && warpToRouteOnResume;
        }

        public void WriteToContext(CharacterContext context)
        {
            Tick(context, Time.deltaTime);
        }

        public void SetProviderDriven(bool providerDriven)
        {
            _isProviderDriven = providerDriven;
        }

        public bool Tick(float deltaTime)
        {
            return Tick(CharacterContext, deltaTime);
        }

        public bool Tick(CharacterContext characterContext, float deltaTime)
        {
            var navigationContext = characterContext?.Navigation;
            if (navigationContext == null || splineContainer == null || configData == null)
            {
                return false;
            }

            EnsureRouteInitialized();
            if (!_routeInitialized) return false;

            if (navigationContext.HasAnyControl &&
                !navigationContext.HasControl(splineOwnerId))
            {
                return false;
            }

            if (!navigationContext.TryClaimControl(splineOwnerId, navigationPriority))
            {
                return false;
            }

            Vector3 routePosition = EvaluateRoutePosition(_splineT);
            if (_resumeRequested)
            {
                if (_resumeShouldWarp)
                {
                    navigationContext.RequestWarp(routePosition);
                }

                _resumeRequested = false;
                _resumeShouldWarp = false;
            }

            AdvanceRoute(deltaTime);
            routePosition = EvaluateRoutePosition(_splineT);
            Vector3 destination = TryProjectToNavMesh(routePosition, out Vector3 navMeshPosition)
                ? navMeshPosition
                : routePosition;

            navigationContext.SetRoute(routePosition, _splineT, _reverseDirection);
            navigationContext.SetDestination(
                destination,
                configData.PatrolSpeed,
                0.1f,
                CharacterNavigationMode.Patrol);

            return true;
        }

        #endregion

        #region Internal Logic

        private CharacterContext CharacterContext => ResolveCharacterContext();

        private void Awake()
        {
            ResolveCharacterContext();
        }

        private void Update()
        {
            if (updateAutomatically && !_isProviderDriven)
            {
                Tick(Time.deltaTime);
            }
        }

        private void EnsureRouteInitialized()
        {
            if (_routeInitialized) return;
            if (splineContainer == null) return;

            _splineLength = CalculateSplineLength();
            _splineT = FindNearestRouteProgress(transform.position);
            _reverseDirection = false;
            _routeInitialized = _splineLength > 0.001f;
        }

        private float CalculateSplineLength()
        {
            const int sampleCount = 100;
            float length = 0f;
            Vector3 previousPoint = EvaluateRoutePosition(0f);
            for (int i = 1; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                Vector3 point = EvaluateRoutePosition(t);
                length += Vector3.Distance(previousPoint, point);
                previousPoint = point;
            }

            return length;
        }

        private float FindNearestRouteProgress(Vector3 worldPosition)
        {
            const int sampleCount = 100;
            float bestT = 0f;
            float bestDistance = float.MaxValue;
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                Vector3 point = EvaluateRoutePosition(t);
                float distance = Vector3.Distance(worldPosition, point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestT = t;
                }
            }

            return bestT;
        }

        private void AdvanceRoute(float deltaTime)
        {
            if (_splineLength <= 0.001f) return;

            float step = configData.PatrolSpeed / _splineLength * Mathf.Max(0f, deltaTime);
            _splineT = _reverseDirection ? _splineT - step : _splineT + step;

            if (_splineT > 1f)
            {
                _splineT = 2f - _splineT;
                _reverseDirection = true;
            }
            else if (_splineT < 0f)
            {
                _splineT = -_splineT;
                _reverseDirection = false;
            }
        }

        private Vector3 EvaluateRoutePosition(float progress)
        {
            return splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(Mathf.Clamp01(progress)));
        }

        private bool TryProjectToNavMesh(Vector3 position, out Vector3 navMeshPosition)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                navMeshPosition = hit.position;
                return true;
            }

            navMeshPosition = position;
            return false;
        }

        private CharacterContext ResolveCharacterContext()
        {
            if (_characterContext != null) return _characterContext;

            if (TryGetContextFromProvider(contextProvider, out _characterContext))
            {
                return _characterContext;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _characterContext))
                {
                    if (contextProvider == null)
                    {
                        contextProvider = behaviour;
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

        private void OnValidate()
        {
            if (ReferenceEquals(contextProvider, this))
            {
                contextProvider = null;
            }
        }

        #endregion
    }
}
