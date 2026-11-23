namespace CoCoFlow.Runtime.StateGraph
{
    public enum RuntimeLifecycleState
    {
        Created = 0,
        Running = 1,
        Suspended = 2,
        Stopped = 3,
        Disposed = 4
    }

    public static class RuntimeLifecycleStateExtensions
    {
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
