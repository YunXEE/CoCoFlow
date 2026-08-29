using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    /// <summary>
    /// 敌人 AI 配置数据，可在资源编辑器中创建和调整
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "CoCoFlow/Enemy/Config Data")]
    public class EnemyConfigData : ScriptableObject
    {
        [Header("Movement")]
        [SerializeField] private float patrolSpeed = 2f;
        [SerializeField] private float chaseSpeed = 5f;

        [Header("Detection")]
        [SerializeField] private float aggroRadius = 10f;
        [SerializeField, Tooltip("Field of view in degrees")] private float fieldOfView = 120f;

        [Header("Behavior")]
        [SerializeField] private float investigateTime = 3f;

        [SerializeField, Range(0.05f, 1f)] private float sensorPollInterval = 0.2f;
        [SerializeField] private LayerMask obstacleLayerMask = 1 << 0; // Default layer

        #region Public API

        /// <summary>巡逻速度</summary>
        public float PatrolSpeed => patrolSpeed;

        /// <summary>追击速度</summary>
        public float ChaseSpeed => chaseSpeed;

        /// <summary>发现玩家的半径</summary>
        public float AggroRadius => aggroRadius;

        /// <summary>视野角度（度）</summary>
        public float FieldOfView => fieldOfView;

        /// <summary>调查持续时间</summary>
        public float InvestigateTime => investigateTime;

        /// <summary>传感器轮询间隔</summary>
        public float SensorPollInterval => sensorPollInterval;

        /// <summary>障碍物层级</summary>
        public LayerMask ObstacleLayerMask => obstacleLayerMask;

        #endregion
    }
}
