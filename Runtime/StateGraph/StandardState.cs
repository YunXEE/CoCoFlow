using System;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>Declares the module registration carried by an Intent source component.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class IntentRegistrationAttribute : Attribute
    {
        public IntentRegistrationAttribute(Type registrationType)
        {
            RegistrationType = registrationType ?? throw new ArgumentNullException(nameof(registrationType));
        }
        public Type RegistrationType { get; }
    }

    /// <summary>Refreshes an Intent data property before each State callback.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class IntentAttribute : Attribute
    {
    }

    /// <summary>Binds an Operation Section property to the State's existing writer.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class OperationAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a class as a StateGraph state logic. The state's descriptor id
    /// is derived deterministically from the graph's identity scope and the
    /// provided name, so ids never need hand maintenance.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class StateAttribute : Attribute
    {
        public StateAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }
        public bool ProvidesActionProgress { get; set; }
    }

    /// <summary>
    /// Declares that a state logic consumes one intent type. The standard
    /// binding binds the intent's package source (for InputIntentData, the
    /// Host InputIntent) with the built-in pass-through reducer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class IntentConsumeAttribute : Attribute
    {
        public IntentConsumeAttribute(Type intentType)
        {
            IntentType = intentType ?? throw new ArgumentNullException(nameof(intentType));
        }

        public Type IntentType { get; }
    }

    /// <summary>
    /// Marks a public instance field on state logic as authored state
    /// configuration: editable on the graph asset, frozen at compile time.
    /// The field type must be unmanaged.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class StateConfigAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a public instance field on state logic as activation memory:
    /// captured into the Context snapshot on commit, restored on rewind.
    /// The field type must be unmanaged.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class StateMemoryAttribute : Attribute
    {
    }

    public sealed class ConfigAttribute : StateConfigAttribute { }
    public sealed class MemoryAttribute : StateMemoryAttribute { }

    /// <summary>
    /// Declares that a State logic provides one Operation Section. This is
    /// the output-side mirror of IntentConsume: the State reads an Intent
    /// at the head of the graph and writes an Operation at its tail.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class OperationProvideAttribute : Attribute
    {
        public OperationProvideAttribute(Type sectionType)
        {
            SectionType = sectionType ??
                throw new ArgumentNullException(nameof(sectionType));
        }

        public Type SectionType { get; }
    }
}
