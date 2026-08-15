namespace CoCoFlow.Runtime.Core.StateGraph.Tests.TransitiveDependencyHelper
{
    /// <summary>
    /// Keeps the fixture's forbidden Core dependency observable without exposing a Core type to
    /// the author assembly that consumes this token.
    /// </summary>
    public readonly struct TransitiveDependencyHelperToken
    {
        private readonly CoCoLifecycleState _state;

        private TransitiveDependencyHelperToken(CoCoLifecycleState state)
        {
            _state = state;
        }

        public int Value => (int)_state;

        public static TransitiveDependencyHelperToken Create() =>
            new TransitiveDependencyHelperToken(CoCoLifecycleState.Uninitialized);
    }
}
