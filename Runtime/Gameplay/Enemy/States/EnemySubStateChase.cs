using UnityEngine;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Gameplay.Enemy.States
{
    /// <summary>
    /// 追击子状态 —— 持续向目标当前位置导航，并驱动角色移动和朝向。
    /// 当目标脱离视野时，切换到调查子状态。
    /// 每帧必须调用 SetMovementVelocity，因为 CharacterLocomotion 会在 ApplyMovement 后清零速度。
    /// </summary>
    public class EnemySubStateChase : CoCoStateMachineBase
    {
        private EnemyController _enemyController;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            _enemyController = Controller.GetComponentInParent<EnemyController>();
            if (_enemyController == null)
            {
                CoCoLog.Error($"SubState_Chase: 在 {gameObject.name} 的祖先节点中找不到 EnemyController！");
            }
        }

        public override void Enter()
        {
            base.Enter();
            _enemyController.Agent.speed = _enemyController.Config.ChaseSpeed;
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // 安全检查：目标可能在状态切换间隙丢失
            if (_enemyController.Blackboard.currentTarget == null)
            {
                Controller.ChangeState<EnemySubStateInvestigate>();
                return;
            }

            // 更新导航目标为目标的当前位置
            _enemyController.Agent.SetDestination(_enemyController.Blackboard.currentTarget.position);

            // 驱动角色移动 —— NavMeshAgent 仅作路径规划，实际位移由 CharacterLocomotion 执行
            Vector3 desiredVelocity = _enemyController.Agent.desiredVelocity;
            _enemyController.Locomotion.SetMovementVelocity(desiredVelocity);

            // 面向目标
            Vector3 dirToTarget = (_enemyController.Blackboard.currentTarget.position - _enemyController.transform.position).normalized;
            _enemyController.Locomotion.SetRotation(dirToTarget);

            // 目标丢失视线 → 切入调查
            if (!_enemyController.Blackboard.isTargetVisible)
            {
                Controller.ChangeState<EnemySubStateInvestigate>();
            }
        }

        #endregion
    }
}
