using System;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using UnityEngine;

namespace CoCoFlow.Tests.StateGraph
{
    internal sealed class HostTestEventAdapterComponent : MonoBehaviour, IEventInboxAdapter<HostTestEvent>
    {
        public bool Accept(in HostTestEvent payload) => true;
    }

    internal sealed class TemporalHostEventAdapterComponent : MonoBehaviour, IEventInboxAdapter<TemporalHostEvent>
    {
        public bool Accept(in TemporalHostEvent payload) => true;
    }

    internal sealed class ThrowingHostTestIntentSource : MonoBehaviour, IIntentFrameSource<HostTestIntent>
    {
        public bool TrySample(in TickFrame tickFrame, out HostTestIntent intent) =>
            throw new InvalidOperationException("Test Intent source failure.");
    }
}
