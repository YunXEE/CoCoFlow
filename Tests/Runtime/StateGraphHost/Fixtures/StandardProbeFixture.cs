
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [CoCoState("StandardProbe")]
    [CoCoIntentConsume(typeof(RawInputIntent))]
    public sealed class StandardProbeLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public static List<RawInputRecord> Received;

        public void Update(CoCoStateExecutionContext context)
        {
            if (context.Intents != null &&
                context.Intents.TryFirst(out RawInputIntent intent) &&
                Received != null)
            {
                for (int index = 0; index < intent.Count; index++)
                {
                    if (intent.TryGet(index, out RawInputRecord record))
                    {
                        Received.Add(record);
                    }
                }
            }
        }
    }
}
