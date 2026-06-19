using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Gameplay.Enemy.States
{
    /// <summary>
    /// 自由接敌状态 —— 顶层战斗容器状态。
    /// 子状态机包含 SubState_Chase（默认）和 SubState_Investigate。
    /// 每帧检查两个脱离条件：离开接敌区域、调查超时。
    /// </summary>
    public class EnemyStateFreeEngage : CoCoStateMachineBase
    {
        private EnemyController _enemyController;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            _enemyController = Controller.GetComponentInParent<EnemyController>();
            if (_enemyController == null)
            {
                CoCoLog.Error($"State_FreeEngage: 在 {gameObject.name} 的祖先节点中找不到 EnemyController！");
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // 脱离检查 A —— 离开接敌区域
            if (_enemyController.EngagementZone != null &&
                !_enemyController.EngagementZone.IsPositionInsideZone(_enemyController.transform.position))
            {
                Controller.ChangeState<EnemyStateReturn>();
                return;
            }

            // 脱离检查 B —— 调查超时（子状态标记为已完成）
            if (subStateMachine != null &&
                subStateMachine.CurrentCoCoState != null &&
                subStateMachine.CurrentCoCoState.IsFinished)
            {
                Controller.ChangeState<EnemyStateReturn>();
            }
        }

        #endregion
    }
}
