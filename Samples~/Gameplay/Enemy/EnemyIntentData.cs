using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    [CreateAssetMenu(fileName = "EnemyIntent", menuName = "CoCoFlow/Enemy/Intent Data")]
    public class EnemyIntentData : ScriptableObject
    {
        [Header("Targeting")]
        [SerializeField] private LayerMask targetLayerMask = 1 << 6;
        [SerializeField] private bool requireEngagementZone = true;

        [Header("Engagement")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float disengageDelay = 2f;
        [SerializeField] private bool releaseToSplineOnTargetLost = true;

        [Header("Navigation Control")]
        [SerializeField] private bool claimNavigationOnTargetVisible = true;
        [SerializeField] private int navigationPriority = 10;
        [SerializeField] private string brainOwnerId = "EnemyBrain";

        #region Public API

        public LayerMask TargetLayerMask => targetLayerMask;
        public bool RequireEngagementZone => requireEngagementZone;
        public float AttackRange => Mathf.Max(0f, attackRange);
        public float DisengageDelay => Mathf.Max(0f, disengageDelay);
        public bool ReleaseToSplineOnTargetLost => releaseToSplineOnTargetLost;
        public bool ClaimNavigationOnTargetVisible => claimNavigationOnTargetVisible;
        public int NavigationPriority => navigationPriority;
        public string BrainOwnerId => string.IsNullOrEmpty(brainOwnerId) ? "EnemyBrain" : brainOwnerId;

        #endregion
    }
}
