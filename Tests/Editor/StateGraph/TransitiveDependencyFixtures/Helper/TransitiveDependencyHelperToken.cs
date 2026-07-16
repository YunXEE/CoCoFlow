namespace CoCoFlow.Runtime.Core.StateGraph.Tests.TransitiveDependencyHelper
{
    /// <summary>
    /// Keeps the fixture's legacy dependency observable without exposing a legacy type to the
    /// author assembly that consumes this token.
    /// </summary>
    public readonly struct TransitiveDependencyHelperToken
    {
        private readonly CoCoStateContextAccess _access;

        private TransitiveDependencyHelperToken(CoCoStateContextAccess access)
        {
            _access = access;
        }

        public int Value => (int)_access;

        public static TransitiveDependencyHelperToken Create() =>
            new TransitiveDependencyHelperToken(CoCoStateContextAccess.Read);
    }
}
