// 图目录登记普通状态的 Logic 类型，实例按节点创建 Logic 并调用它实现的生命周期回调。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>作为普通玩法状态的基类，一个 Logic 对象只能由一个运行实例认领。</summary>
    public abstract class StateLogic
    {
        private object _runtimeOwner;

        internal bool TryClaimRuntimeOwner(object owner)
        {
            if (owner == null || _runtimeOwner != null)
            {
                return false;
            }

            _runtimeOwner = owner;
            return true;
        }

        internal void ReleaseRuntimeOwner(object owner)
        {
            if (ReferenceEquals(_runtimeOwner, owner))
            {
                _runtimeOwner = null;
            }
        }
    }
}
