using System;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.StateGraphHost
{
    // Unity resolves old SerializeReference records here. The public gameplay types
    // stay in the engine-free StateGraph assembly. Shared references stay shared.
    internal interface ICoCoLegacyGraphConfig<out T>
    {
        T Current { get; }
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "EmptyStateConfig")]
    internal sealed class CoCoLegacyEmptyStateConfig : CoCoStateConfig, ICoCoLegacyGraphConfig<CoCoStateConfig>
    {
        [NonSerialized] private EmptyStateConfig _current;
        public CoCoStateConfig Current => _current ??= new EmptyStateConfig();
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "CoCoFieldConfig")]
    internal sealed class CoCoLegacyFieldConfig : CoCoStateConfig, ICoCoLegacyGraphConfig<CoCoStateConfig>
    {
        [FormerlySerializedAs(nameof(CoCoFieldConfig.Values)), SerializeField]
        private string[] values = Array.Empty<string>();
        [NonSerialized] private CoCoFieldConfig _current;
        public CoCoStateConfig Current => _current ??= new CoCoFieldConfig { Values = values };
    }

    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "CoCoEmptyConditionConfig")]
    internal sealed class CoCoLegacyEmptyConditionConfig : CoCoConditionConfig, ICoCoLegacyGraphConfig<CoCoConditionConfig>
    {
        [NonSerialized] private CoCoEmptyConditionConfig _current;
        public CoCoConditionConfig Current => _current ??= new CoCoEmptyConditionConfig();
    }
}
