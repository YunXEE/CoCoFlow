using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Marks a class as a StateGraph state logic. The state's descriptor id
    /// is derived deterministically from the graph's identity scope and the
    /// provided name, so ids never need hand maintenance.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CoCoStateAttribute : Attribute
    {
        public CoCoStateAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }
    }

    /// <summary>
    /// Declares that a state logic consumes one intent type. The standard
    /// binding binds the intent's package source (for RawInputIntent, the
    /// scene InputReader) with the built-in pass-through reducer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class CoCoIntentConsumeAttribute : Attribute
    {
        public CoCoIntentConsumeAttribute(Type intentType)
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
    public sealed class CoCoStateConfigAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a public instance field on state logic as activation memory:
    /// captured into the Context snapshot on commit, restored on rewind.
    /// The field type must be unmanaged.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class CoCoStateMemoryAttribute : Attribute
    {
    }

    /// <summary>
    /// Declares the standard-path registrar carried by one Operator. The
    /// registrar owns that Operator's Operation Sections and Context blocks;
    /// new Operators therefore ship their own registration without editing a
    /// package-wide central list.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class CoCoOperatorRegistrationAttribute : Attribute
    {
        public CoCoOperatorRegistrationAttribute(Type registrarType)
        {
            RegistrarType = registrarType ??
                throw new ArgumentNullException(nameof(registrarType));
        }

        public Type RegistrarType { get; }
    }

    /// <summary>
    /// Declares that a State logic provides one Operation Section. This is
    /// the output-side mirror of CoCoIntentConsume: the State reads an Intent
    /// at the head of the graph and writes an Operation at its tail.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class CoCoOperationProvideAttribute : Attribute
    {
        public CoCoOperationProvideAttribute(Type sectionType)
        {
            SectionType = sectionType ??
                throw new ArgumentNullException(nameof(sectionType));
        }

        public Type SectionType { get; }
    }
}
