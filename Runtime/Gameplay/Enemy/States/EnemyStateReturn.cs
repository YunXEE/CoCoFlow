using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Gameplay.Enemy.States
{
    /// <summary>
    /// 脱战返回状态 —— 导航回到巡逻样条线，到达后切换至 FixedRoutine（巡逻）状态。
    /// NavMeshAgent 仅用作路径规划器，实际移动由 CharacterLocomotion 执行。
    /// </summary>
    public class EnemyStateReturn : CoCoStateMachineBase
    {
        private EnemyController _enemyController;
        [SerializeField] private SplineContainer splineContainer;

        /// <summary>采样得到的最近样条 t 值（0~1），作为目标点</summary>
        private float _targetSplineT;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            _enemyController = Controller.GetComponentInParent<EnemyController>();
            if (_enemyController == null)
            {
                CoCoLog.Error($"State_Return: 在 {gameObject.name} 的祖先节点中找不到 EnemyController！");
            }
        }

        public override void Enter()
        {
            base.Enter();

            // 脱战返回时清除当前目标
            _enemyController.Blackboard.currentTarget = null;
            _enemyController.Blackboard.isTargetVisible = false;

            if (splineContainer == null)
            {
                CoCoLog.Error($"State_Return: {gameObject.name} 未指定 SplineContainer！");
                _targetSplineT = 0f;
                return;
            }

            // 在样条上采样 100 个点，找到离当前位置最近的点作为目标
            Vector3 enemyPosition = _enemyController.transform.position;
            float bestT = 0f;
            float bestDist = float.MaxValue;
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                // EvaluatePosition 返回本地坐标，需转换为世界坐标
                Vector3 splinePos = splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(t));
                float dist = Vector3.Distance(enemyPosition, splinePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestT = t;
                }
            }

            _targetSplineT = bestT;
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (splineContainer == null) return;

            Vector3 enemyPosition = _enemyController.transform.position;

            // 获取样条上的目标位置（世界坐标）
            Vector3 targetPos = splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(_targetSplineT));

            // 在 NavMesh 上采样最近的有效点（容差 5 米）
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                _enemyController.Agent.SetDestination(hit.position);
            }

            // 读取 Agent 路径规划出的期望速度，交给 Locomotion 执行
            Vector3 desiredVelocity = _enemyController.Agent.desiredVelocity;
            _enemyController.Locomotion.SetMovementVelocity(desiredVelocity);
            _enemyController.Locomotion.SetRotation(desiredVelocity);

            // 到达检测：距离目标点 < 2 米时切换回巡逻状态
            if (Vector3.Distance(enemyPosition, targetPos) < 2f)
            {
                Controller.ChangeState<EnemyStateFixedRoutine>();
            }
        }

        #endregion
    }
}
