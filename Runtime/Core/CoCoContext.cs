using System;

namespace CoCoFlow.Runtime.Core
{
    // UnityLinker recognizes this attribute name in engine-independent assemblies.
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class PreserveAttribute : Attribute { }

    public interface ICoCoContext { }

    public interface ICoCoContextProvider<out TContext> where TContext : class, ICoCoContext
    {
        // PersistenceContext 通过闭合泛型接口反射读取此属性。
        [Preserve]
        TContext Context { get; }
    }

    public interface ICoCoStableEntityIdProvider
    {
        string StableEntityId { get; }
    }

    public enum CoCoLifecycleState
    {
        Uninitialized,
        Spawning,
        Active,
        Disabled,
        Consumed,
        Despawning,
        Destroyed
    }

}
