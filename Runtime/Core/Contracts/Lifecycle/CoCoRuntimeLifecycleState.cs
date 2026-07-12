namespace CoCoFlow.Runtime.Core
{
    public enum CoCoRuntimeLifecycleState
    {
        Created = 0,
        Running = 1,
        Suspended = 2,
        Stopped = 3,
        Disposed = 4
    }

    public static class CoCoRuntimeLifecycleStateExtensions
    {
        public static bool CanTransitionTo(
            this CoCoRuntimeLifecycleState currentState,
            CoCoRuntimeLifecycleState nextState)
        {
            switch (currentState)
            {
                case CoCoRuntimeLifecycleState.Created:
                    return nextState == CoCoRuntimeLifecycleState.Running;
                case CoCoRuntimeLifecycleState.Running:
                    return nextState == CoCoRuntimeLifecycleState.Suspended ||
                           nextState == CoCoRuntimeLifecycleState.Stopped;
                case CoCoRuntimeLifecycleState.Suspended:
                    return nextState == CoCoRuntimeLifecycleState.Running ||
                           nextState == CoCoRuntimeLifecycleState.Stopped;
                case CoCoRuntimeLifecycleState.Stopped:
                    return nextState == CoCoRuntimeLifecycleState.Disposed;
                default:
                    return false;
            }
        }
    }
}
