// 每层可有一个常驻的 AnyState。实例在普通路径更新后调用它并先仲裁它请求的边。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>每层在普通活动路径更新后调用的常驻 Logic，其激活记忆跨普通状态切换保留。</summary>
    public abstract class AnyStateLogic : StateLogic, IStateUpdate
    {
        /// <summary>在本层普通路径更新后执行，可为该层登记转换请求。常驻激活的记忆跨普通状态切换保留。</summary>
        public abstract void Update(StateExecutionContext context);
    }
}
