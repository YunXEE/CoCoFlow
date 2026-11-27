// Host 和实例在启动、挂起、恢复、停止与释放前共用状态转换规则。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>记录图实例从创建、运行、暂停到停止或释放的当前阶段。</summary>
    public enum RuntimeLifecycleState
    {
        Created = 0,
        Running = 1,
        Suspended = 2,
        Stopped = 3,
        Disposed = 4
    }

    /// <summary>为 Host 和运行时提供一致的生命周期迁移判定。</summary>
    public static class RuntimeLifecycleStateExtensions
    {
        /// <summary>判断给定生命周期阶段能否直接到达目标阶段，供 Host 在更改状态前检查。</summary>
        public static bool CanTransitionTo(
            this RuntimeLifecycleState currentState,
            RuntimeLifecycleState nextState)
        {
            switch (currentState)
            {
                case RuntimeLifecycleState.Created:
                    return nextState == RuntimeLifecycleState.Running ||
                           nextState == RuntimeLifecycleState.Disposed;
                case RuntimeLifecycleState.Running:
                    return nextState == RuntimeLifecycleState.Suspended ||
                           nextState == RuntimeLifecycleState.Stopped;
                case RuntimeLifecycleState.Suspended:
                    return nextState == RuntimeLifecycleState.Running ||
                           nextState == RuntimeLifecycleState.Stopped;
                case RuntimeLifecycleState.Stopped:
                    return nextState == RuntimeLifecycleState.Disposed;
                default:
                    return false;
            }
        }
    }
}
