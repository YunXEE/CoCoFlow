using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Gameplay.Enemy.States
{
    /// <summary>
    /// 样条巡逻子状态 —— 沿 SplineContainer 往返巡逻，使用 NavMeshAgent 路径规划 + CharacterLocomotion 执行移动。
    /// 作为 State_FixedRoutine 的子状态运行，实现沿固定路线的来回巡逻行为。
    /// </summary>
    public class EnemySubStateSplinePatrol : CoCoStateMachineBase
    {
        private EnemyController _enemyController;

        [SerializeField] private SplineContainer splineContainer;

        /// <summary>当前样条进度（0~1 归一化）</summary>
        private float _splineT;

        /// <summary>样条总长度，Enter 时缓存避免每帧重算</summary>
        private float _splineLength;

        /// <summary>是否反向巡逻（从终点往起点）</summary>
        private bool _reverseDirection;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            _enemyController = Controller.GetComponentInParent<EnemyController>();
            if (_enemyController == null)
            {
                CoCoLog.Error($"SubState_SplinePatrol: 在 {gameObject.name} 的祖先节点中找不到 EnemyController！");
            }
        }

        public override void Enter()
        {
            base.Enter();

            if (splineContainer == null)
            {
                CoCoLog.Error($"SubState_SplinePatrol: {gameObject.name} 未指定 SplineContainer！");
                return;
            }

            // 切换为巡逻速度
            _enemyController.Agent.speed = _enemyController.Config.PatrolSpeed;

            // 采样计算样条总长度（100 个采样点）
            _splineLength = 0f;
            Vector3 previousPoint = splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(0f));
            const int sampleCount = 100;
            for (int i = 1; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                Vector3 currentPoint = splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(t));
                _splineLength += Vector3.Distance(previousPoint, currentPoint);
                previousPoint = currentPoint;
            }

            // 在样条上找到离当前位置最近的点作为起始进度
            Vector3 enemyPosition = _enemyController.transform.position;
            float bestT = 0f;
            float bestDist = float.MaxValue;
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                Vector3 splinePos = splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(t));
                float dist = Vector3.Distance(enemyPosition, splinePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestT = t;
                }
            }
            _splineT = bestT;

            _reverseDirection = false;
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (splineContainer == null) return;

            // 1. 沿样条推进
            float step = (_enemyController.Config.PatrolSpeed / _splineLength) * Time.deltaTime;
            if (_reverseDirection)
                _splineT -= step;
            else
                _splineT += step;

            // 2. 到达端点时反弹（折返）
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

            // 3. 获取样条上的目标位置
            Vector3 splinePos = splineContainer.transform.TransformPoint(splineContainer.EvaluatePosition(_splineT));

            // 4. 在 NavMesh 上采样最近有效点（容差 3 米），用于路径规划
            if (NavMesh.SamplePosition(splinePos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                _enemyController.Agent.SetDestination(hit.position);
            }

            // 5. 读取 Agent 的期望速度，交给 Locomotion 执行实际移动与旋转
            //    注意：必须每帧调用！Locomotion 在 ApplyMovement 后会清零速度值
            Vector3 desiredVelocity = _enemyController.Agent.desiredVelocity;
            _enemyController.Locomotion.SetMovementVelocity(desiredVelocity);
            _enemyController.Locomotion.SetRotation(desiredVelocity);
        }

        public override void Exit()
        {
            base.Exit();

            // 清除 Agent 路径，避免残留导航目的地影响后续状态
            _enemyController.Agent.ResetPath();
        }

        #endregion
    }
}
