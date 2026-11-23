using System;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.StateGraphHost
{
    // Unity resolves old SerializeReference records here. The public gameplay types
    // stay in the engine-free StateGraph assembly. Shared references stay shared.
    internal interface ILegacyGraphConfig<out T>
    {
        T Current { get; }
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "EmptyStateConfig")]
    internal sealed class LegacyEmptyStateConfig : StateConfig, ILegacyGraphConfig<StateConfig>
    {
        [NonSerialized] private EmptyStateConfig _current;
        public StateConfig Current => _current ??= new EmptyStateConfig();
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "CoCoFieldConfig")]
    internal class LegacyFieldConfig : StateConfig, ILegacyGraphConfig<StateConfig>
    {
        [FormerlySerializedAs(nameof(FieldConfig.Values)), SerializeField]
        private string[] values = Array.Empty<string>();
        [NonSerialized] private FieldConfig _current;
        public StateConfig Current => _current ??= new FieldConfig { Values = values };
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "CoCoEmptyConditionConfig")]
    internal class LegacyEmptyConditionConfig : ConditionConfig, ILegacyGraphConfig<ConditionConfig>
    {
        [NonSerialized] private EmptyConditionConfig _current;
        public ConditionConfig Current => _current ??= new EmptyConditionConfig();
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.StateGraph", "CoCoFlow.Runtime.StateGraph", "CoCoFieldConfig")]
    internal sealed class LegacyPrefixedFieldConfig : LegacyFieldConfig { }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.StateGraph", "CoCoFlow.Runtime.StateGraph", "CoCoEmptyConditionConfig")]
    internal sealed class LegacyPrefixedEmptyConditionConfig : LegacyEmptyConditionConfig { }
}
