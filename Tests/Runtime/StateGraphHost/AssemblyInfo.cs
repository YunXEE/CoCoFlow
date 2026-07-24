using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CoCoFlow.Tests.Runtime.Pooling.Temporal")]

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    internal static class StateGraphHostPoolingTestBridge
    {
        internal static void ResetProjectBindings()
        {
            CoCoFlow.Runtime.Core.CoCoStateGraphProjectBindings.ResetForTests();
        }
    }
}
