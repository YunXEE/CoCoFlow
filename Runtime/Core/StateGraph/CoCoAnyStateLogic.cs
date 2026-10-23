namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Layer-owned logic that updates after the ordinary active path.
    /// Its Memory and activation persist across ordinary State transitions.
    /// AnyState supports the complete Update surface, without Enter or Exit.
    /// </summary>
    public abstract class CoCoAnyStateLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public abstract void Update(CoCoStateExecutionContext context);
    }
}
