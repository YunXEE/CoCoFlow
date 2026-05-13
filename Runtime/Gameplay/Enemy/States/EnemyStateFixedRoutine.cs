using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Gameplay.Enemy.States
{
    /// <summary>
    /// 默认 / 巡逻顶层状态 —— 作为敌人的出生状态。
    /// 该状态拥有一个子状态机（通过基类的 [SerializeField] subStateMachine 在 Inspector 中赋值），
    /// 自动运行 SubState_SplinePatrol 子状态。
    /// 当前状态本身不处理移动逻辑，仅负责检测是否满足切换至战斗态的条件。
    /// </summary>
    public class EnemyStateFixedRoutine : CoCoStateMachineBase
    {
        private EnemyController _enemyController;

        #region Public API

        public override void Init(CoCoStateMachineController targetController)
        {
            base.Init(targetController);
            _enemyController = Controller.GetComponentInParent<EnemyController>();
            if (_enemyController == null)
            {
                CoCoLog.Error($"State_FixedRoutine: 在 {gameObject.name} 的祖先节点中找不到 EnemyController！");
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate(); // 自动驱动 subStateMachine

            // 检测是否应切换到战斗状态
            if (!_enemyController.Blackboard.isTargetVisible) return;

            // 已探测到目标，进一步判断目标是否在战斗接敌区域内
            bool shouldEngage = true;
            if (_enemyController.EngagementZone != null)
            {
                shouldEngage = _enemyController.EngagementZone.IsPositionInsideZone(
                    _enemyController.Blackboard.currentTarget.position);
            }

            if (shouldEngage)
            {
                Controller.ChangeState<EnemyStateFreeEngage>();
            }
        }

        #endregion
    }
}
