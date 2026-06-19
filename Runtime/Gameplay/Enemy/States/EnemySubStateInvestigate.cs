using UnityEngine;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Gameplay.Enemy.States
{
    /// <summary>
    /// 调查子状态 —— 移动到目标最后已知位置并等待。
    /// 若目标重新出现在视野内，切回追击；若超时仍未发现，标记为完成。
    /// 每帧必须调用 SetMovementVelocity，因为 CharacterLocomotion 会在 ApplyMovement 后清零速度。
    /// </summary>
    public class EnemySubStateInvestigate : CoCoStateMachineBase
    {
        private EnemyController _enemyController;
        private float _investigateStartTime;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            _enemyController = Controller.GetComponentInParent<EnemyController>();
            if (_enemyController == null)
            {
                CoCoLog.Error($"SubState_Investigate: 在 {gameObject.name} 的祖先节点中找不到 EnemyController！");
            }
        }

        public override void Enter()
        {
            base.Enter();
            _investigateStartTime = Time.time;
            _enemyController.Agent.SetDestination(_enemyController.Blackboard.lastKnownPosition);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // 驱动角色移动 —— NavMeshAgent 仅作路径规划，实际位移由 CharacterLocomotion 执行
            _enemyController.Locomotion.SetMovementVelocity(_enemyController.Agent.desiredVelocity);

            // 转向最后已知位置
            Vector3 dirToPoint = (_enemyController.Blackboard.lastKnownPosition - _enemyController.transform.position).normalized;
            if (dirToPoint.sqrMagnitude > 0.01f)
            {
                _enemyController.Locomotion.SetRotation(dirToPoint);
            }

            // 目标重新出现 → 切回追击
            if (_enemyController.Blackboard.isTargetVisible)
            {
                Controller.ChangeState<EnemySubStateChase>();
                return;
            }

            // 调查超时 → 标记完成，由父状态 State_FreeEngage 处理退出
            if (Time.time - _investigateStartTime > _enemyController.Config.InvestigateTime)
            {
                IsFinished = true;
            }
        }

        #endregion
    }
}
