using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.StateGraph;

// 手动 Host 在该状态写入移动操作，移动模块测试由角色的实际位移核对提交结果。
namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>让真实 Host 通过属性视图写入角色移动，以验证模块的手写绑定。</summary>
    [State("LocoMoveProbe")]
    public sealed class LocoMoveProbeLogic : StateLogic, IStateUpdate
    {
        [Operation] public ILocomotionOperation Movement { get; set; }
        public static float? LastDelta;

        /// <summary>把夹具指定的移动或跳跃值写入本步连续输出，供真实移动 Operator 消费。</summary>
        public void Update(StateExecutionContext context)
        {
            Movement.MoveZ = 2f;
            Movement.UseGravity = true;
            LastDelta = 2f;
        }
    }
}
