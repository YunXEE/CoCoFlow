using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    [Serializable]
    public sealed class CoCoEmptyConditionConfig : CoCoConditionConfig
    {
        public sealed class Freezer : ICoCoConfigFreezer<CoCoEmptyConditionConfig, EmptyConfigSchema>
        {
            public bool TryFreeze(CoCoEmptyConditionConfig source,
                CoCoFrozenConfigWriter<EmptyConfigSchema> writer, out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return source != null;
            }
        }
    }
}
