using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    [Serializable]
    public sealed class EmptyConditionConfig : ConditionConfig
    {
        public sealed class Freezer : IConfigFreezer<EmptyConditionConfig, EmptyConfigSchema>
        {
            public bool TryFreeze(EmptyConditionConfig source,
                FrozenConfigWriter<EmptyConfigSchema> writer, out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return source != null;
            }
        }
    }
}
