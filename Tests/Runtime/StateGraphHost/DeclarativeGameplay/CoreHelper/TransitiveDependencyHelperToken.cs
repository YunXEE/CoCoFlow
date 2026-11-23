using CoCoFlow.Runtime.Core;
namespace CoCoFlow.Tests.StateGraphTransitiveDependencyHelper
{
    /// <summary>Reads Core through a helper without exposing its type in the consumer API.</summary>
    public readonly struct TransitiveDependencyHelperToken
    {
        private readonly CoCoLifecycleState _state;

        private TransitiveDependencyHelperToken(CoCoLifecycleState state)
        {
            _state = state;
        }

        public static T GetService<T>() where T : class => CoCoServices.Get<T>();

        public int Value => (int)_state;

        public static TransitiveDependencyHelperToken Create() =>
            new TransitiveDependencyHelperToken(CoCoLifecycleState.Uninitialized);
    }
}
