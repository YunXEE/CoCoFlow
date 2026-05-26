using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Addon.Network.Bootstrap
{
    /// <summary>
    /// Bootstrap 默认空状态，用于确认 CoCoFlow 状态机生命周期已接入测试场景。
    /// </summary>
    public class GameBootstrapReadyState : CoCoStateMachineBase
    {
        public override void Enter()
        {
            base.Enter();
            CoCoLog.Log("[GameBootstrapReadyState] Bootstrap 状态机已进入 Ready。");
        }
    }
}
