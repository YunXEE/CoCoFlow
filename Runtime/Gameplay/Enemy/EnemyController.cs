using UnityEngine;
using UnityEngine.AI;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    /// <summary>
    /// 敌人 AI 大脑 —— 所有组件引用的中枢和数据黑板。
    /// Sensor 写入 Blackboard，State 读取 Blackboard，实现 Sensor-State 解耦。
    /// NavMeshAgent 仅用作路径规划器（updatePosition/updateRotation = false），
    /// 实际移动由 CharacterLocomotion 执行。
    /// </summary>
    public class EnemyController : MonoBehaviour
    {
        [FormerlySerializedAs("_config")]
        [Header("Config")]
        [SerializeField] private EnemyConfigData config;

        [FormerlySerializedAs("_engagementZone")]
        [Header("Zone")]
        [SerializeField] private EnemyEngagementZone engagementZone;

        [FormerlySerializedAs("_blackboard")]
        [Header("Data")]
        [SerializeField] private EnemyBlackboard blackboard = new EnemyBlackboard();

        // 运行时组件（Awake 中自动获取）
        private CharacterLocomotion _locomotion;
        private NavMeshAgent _agent;
        private CoCoStateMachineController _stateMachine;

        private void Awake()
        {
            _locomotion = GetComponent<CharacterLocomotion>();
            _agent = GetComponent<NavMeshAgent>();
            _stateMachine = GetComponent<CoCoStateMachineController>();

            // NavMeshAgent 仅作路径规划，实际移动由 CharacterLocomotion 负责
            _agent.updatePosition = false;
            _agent.updateRotation = false;

            // 记录出生点，用于巡逻 / 返回原点
            blackboard.spawnPoint = transform.position;
        }

        #region Public API

        public EnemyConfigData Config => config;
        public EnemyBlackboard Blackboard => blackboard;
        public EnemyEngagementZone EngagementZone => engagementZone;
        public CharacterLocomotion Locomotion => _locomotion;
        public NavMeshAgent Agent => _agent;
        public CoCoStateMachineController StateMachine => _stateMachine;

        #endregion

        #region Internal Logic

        /// <summary>
        /// 敌人数据黑板 —— Sensor 写入，State 读取，所有敌方状态的中枢。
        /// 作为 EnemyController 的内部可序列化类，在 Inspector 中以折叠形式显示。
        /// </summary>
        [System.Serializable]
        public class EnemyBlackboard
        {
            [Tooltip("当前锁定目标")]
            public Transform currentTarget;

            [Tooltip("目标最后一次被观测到的位置")]
            public Vector3 lastKnownPosition;

            [Tooltip("出生点（巡逻/返回原点）")]
            public Vector3 spawnPoint;

            [Tooltip("当前目标是否在视野内")]
            public bool isTargetVisible;
        }

        #endregion
    }
}
