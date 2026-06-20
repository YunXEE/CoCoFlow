using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    public readonly struct EnemyVisionQueryResult
    {
        public EnemyVisionQueryResult(
            Transform target,
            Vector3 lastKnownPosition,
            float distance,
            bool isVisible)
        {
            Target = target;
            LastKnownPosition = lastKnownPosition;
            Distance = distance;
            IsVisible = isVisible;
        }

        public Transform Target { get; }
        public Vector3 LastKnownPosition { get; }
        public float Distance { get; }
        public bool IsVisible { get; }
    }

    public static class EnemyVisionQuery
    {
        #region Public API

        public static bool TryFindVisibleTarget(
            Transform observer,
            EnemyConfigData config,
            LayerMask targetLayerMask,
            Transform currentTarget,
            Collider[] hitBuffer,
            out EnemyVisionQueryResult result)
        {
            result = default;
            if (observer == null || config == null) return false;

            if (currentTarget != null &&
                IsTargetVisible(observer, currentTarget, config, targetLayerMask, out result))
            {
                return true;
            }

            Collider[] buffer = hitBuffer ?? new Collider[16];
            int targetMask = ResolveTargetMask(targetLayerMask);
            int hitCount = Physics.OverlapSphereNonAlloc(
                observer.position,
                config.AggroRadius,
                buffer,
                targetMask,
                QueryTriggerInteraction.Ignore);

            Transform bestTarget = null;
            EnemyVisionQueryResult bestResult = default;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = buffer[i];
                if (hitCollider == null) continue;

                Transform target = hitCollider.transform;
                if (target == observer || target.IsChildOf(observer)) continue;

                if (IsTargetVisible(observer, target, config, targetLayerMask, out var candidate) &&
                    candidate.Distance < bestDistance)
                {
                    bestDistance = candidate.Distance;
                    bestTarget = target;
                    bestResult = candidate;
                }
            }

            if (bestTarget == null) return false;

            result = bestResult;
            return true;
        }

        public static bool IsTargetVisible(
            Transform observer,
            Transform target,
            EnemyConfigData config,
            LayerMask targetLayerMask,
            out EnemyVisionQueryResult result)
        {
            result = default;
            if (observer == null || target == null || config == null) return false;

            Vector3 direction = target.position - observer.position;
            float distance = direction.magnitude;
            if (distance <= 0.001f || distance > config.AggroRadius) return false;

            Vector3 normalizedDirection = direction / distance;
            float angle = Vector3.Angle(observer.forward, normalizedDirection);
            if (angle > config.FieldOfView * 0.5f) return false;

            int raycastMask = ResolveTargetMask(targetLayerMask) | config.ObstacleLayerMask.value;
            if (raycastMask == 0)
            {
                raycastMask = Physics.DefaultRaycastLayers;
            }

            if (!Physics.Raycast(
                    observer.position,
                    normalizedDirection,
                    out RaycastHit hit,
                    distance,
                    raycastMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (!IsTargetHit(hit.transform, target)) return false;

            result = new EnemyVisionQueryResult(target, target.position, distance, true);
            return true;
        }

        #endregion

        #region Internal Logic

        private static int ResolveTargetMask(LayerMask targetLayerMask)
        {
            if (targetLayerMask.value != 0) return targetLayerMask.value;

            int playerMask = LayerMask.GetMask("Player");
            return playerMask != 0 ? playerMask : 1 << 6;
        }

        private static bool IsTargetHit(Transform hitTransform, Transform target)
        {
            if (hitTransform == null || target == null) return false;
            return hitTransform == target ||
                   hitTransform.IsChildOf(target) ||
                   target.IsChildOf(hitTransform);
        }

        #endregion
    }
}
