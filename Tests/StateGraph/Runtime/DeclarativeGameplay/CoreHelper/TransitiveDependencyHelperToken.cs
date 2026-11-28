using CoCoFlow.Runtime.Core;
namespace CoCoFlow.Tests.StateGraphTransitiveDependencyHelper
{
    public readonly struct TransitiveDependencyHelperToken
    {
        private readonly CoCoLogLevel _level;

        private TransitiveDependencyHelperToken(CoCoLogLevel level)
        {
            _level = level;
        }

        public static T GetService<T>() where T : class => CoCoServices.Get<T>();

        public int Value => (int)_level;

        public static TransitiveDependencyHelperToken Create() =>
            new TransitiveDependencyHelperToken(CoCoLogLevel.Warning);
    }
}
