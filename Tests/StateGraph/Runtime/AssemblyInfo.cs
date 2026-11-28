using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CoCoFlow.Tests.Runtime.Pooling.Temporal")]

namespace CoCoFlow.Tests.StateGraph
{
    internal static class StateGraphHostPoolingTestBridge
    {
        internal static void ResetProjectBindings()
        {
            CoCoFlow.Runtime.StateGraph.HostProjectBindings.ResetForTests();
        }
    }
}
