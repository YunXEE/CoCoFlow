using System;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    /// <summary>Marks a business struct as eligible for Container persistence.</summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class PersistenceDataAttribute : Attribute { }
}
