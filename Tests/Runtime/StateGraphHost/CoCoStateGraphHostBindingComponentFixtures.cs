using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    internal sealed class HostTestEventAdapterComponent : MonoBehaviour, ICoCoEventInboxAdapter<HostTestEvent>
    {
        public bool Accept(in HostTestEvent payload) => true;
    }

    internal sealed class TemporalHostEventAdapterComponent : MonoBehaviour, ICoCoEventInboxAdapter<TemporalHostEvent>
    {
        public bool Accept(in TemporalHostEvent payload) => true;
    }

    internal sealed class ThrowingHostTestIntentSource : MonoBehaviour, ICoCoIntentFrameSource<HostTestIntent>
    {
        public bool TrySample(in CoCoTickFrame tickFrame, out HostTestIntent intent) =>
            throw new InvalidOperationException("Test Intent source failure.");
    }
}
