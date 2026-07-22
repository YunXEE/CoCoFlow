using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    internal sealed class HostTestEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
    {
        private readonly HostTestEventAdapter _adapter = new HostTestEventAdapter();

        public bool TryProject(
            in CoCoEventPacket<HostTestEvent> packet,
            out HostTestIntent intent) =>
            _adapter.TryProject(packet, out intent);
    }

    internal sealed class ThrowingHostTestEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<HostTestEvent> packet,
            out HostTestIntent intent)
        {
            intent = default;
            throw new InvalidOperationException("Test Event Adapter failure.");
        }
    }

    internal sealed class FirstOrderedHostTestEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<HostTestEvent> packet,
            out HostTestIntent intent)
        {
            intent = new HostTestIntent { Value = packet.Payload.Value };
            return true;
        }
    }

    internal sealed class SecondOrderedHostTestEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<SecondHostTestEvent, HostTestIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<SecondHostTestEvent> packet,
            out HostTestIntent intent)
        {
            intent = new HostTestIntent { Value = packet.Payload.Value };
            return true;
        }
    }

    internal sealed class OrderedHostTestIntentSourceComponent :
        MonoBehaviour,
        ICoCoIntentFrameSource<HostTestIntent>
    {
        internal int Value { get; set; } = 9;

        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out HostTestIntent intent)
        {
            intent = new HostTestIntent { Value = Value };
            return true;
        }
    }

    internal sealed class DualHostTestEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
    {
        private static int _projectionSequence;

        internal bool IsFirst { get; set; }
        internal static int FirstProjectionCount { get; private set; }
        internal static int SecondProjectionCount { get; private set; }
        internal static int FirstProjectionOrder { get; private set; }
        internal static int SecondProjectionOrder { get; private set; }

        internal static void Reset()
        {
            FirstProjectionCount = 0;
            SecondProjectionCount = 0;
            FirstProjectionOrder = 0;
            SecondProjectionOrder = 0;
            _projectionSequence = 0;
        }

        public bool TryProject(
            in CoCoEventPacket<HostTestEvent> packet,
            out HostTestIntent intent)
        {
            if (IsFirst)
            {
                FirstProjectionCount++;
                FirstProjectionOrder = ++_projectionSequence;
            }
            else
            {
                SecondProjectionCount++;
                SecondProjectionOrder = ++_projectionSequence;
            }

            intent = new HostTestIntent { Value = packet.Payload.Value };
            return true;
        }
    }

    internal sealed class TemporalHostEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<TemporalHostEvent, TemporalHostIntent>
    {
        private readonly TemporalHostEventAdapter _adapter = new TemporalHostEventAdapter();

        public bool TryProject(
            in CoCoEventPacket<TemporalHostEvent> packet,
            out TemporalHostIntent intent) =>
            _adapter.TryProject(packet, out intent);
    }
}
