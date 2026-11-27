using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    public sealed partial class StateGraphHost
    {
        private static readonly MethodInfo BindStateEventMethod = typeof(StateGraphHost)
            .GetMethod(nameof(BindStateEvent), BindingFlags.Instance | BindingFlags.NonPublic);
        private StateEventInbox _stateInbox;
        private CoCoEventAgent _stateEventAgent;

        private bool TryValidateEventAdapterBoundaries(out CoCoDiagnostic diagnostic)
        {
            foreach (var component in EventAdapters)
            {
                if (component == null || StateGraphHostBoundary.Contains(this, component)) continue;
                diagnostic = RegistryError(CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Event Adapter must belong to this Host boundary.");
                return false;
            }
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void StartStateEvents(IStateGraphProjectBindingProvider provider, CompiledStateGraph graph)
        {
            _stateInbox = new StateEventInbox(eventLaneCapacity);
            _stateEventAgent = new CoCoEventAgent();
            _runtime.AttachEvents(_stateInbox, _transaction);
            if (provider is StandardBindingProvider standard)
            {
                foreach (Type payload in standard.InboxEventTypes(graph))
                    BindStateEventMethod.MakeGenericMethod(payload).Invoke(this, new object[] { null });
            }

            var components = new List<MonoBehaviour>(EventAdapters);
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != null && StateGraphHostBoundary.Contains(this, component) &&
                    !components.Contains(component)) components.Add(component);
            }
            foreach (var component in components)
            {
                if (component == null) continue;
                foreach (var contract in component.GetType().GetInterfaces())
                {
                    if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IEventInboxAdapter<>))
                        continue;
                    BindStateEventMethod.MakeGenericMethod(contract.GetGenericArguments())
                        .Invoke(this, new object[] { component });
                }
            }
        }

        private void BindStateEvent<TEvent>(MonoBehaviour component) where TEvent : unmanaged
        {
            var lane = _stateInbox.Find<TEvent>();
            if (lane == null)
            {
                lane = _stateInbox.Register<TEvent>();
                _stateEventAgent.Subscribe((ref TEvent payload) =>
                {
                    if (CanReceiveStateEvents && Accepts(lane, payload)) lane.TryEnqueue(in payload);
                });
                _stateEventAgent.Subscribe((ref EventPacket<TEvent> packet) =>
                {
                    if (TryReceiveStatePacket(in packet) == InboxEnqueueResult.ReliableOverflowFaultRequired)
                        MarkReliableOverflowPending();
                });
            }
            if (component != null) lane.Adapters.Add((IEventInboxAdapter<TEvent>)component);
        }

        private bool CanReceiveStateEvents => CanAcceptEventInput &&
            _runtime.Lifecycle == RuntimeLifecycleState.Running && !_isTemporalOperation &&
            _temporal?.Mode != TemporalMode.Previewing;

        private static bool Accepts<TEvent>(StateEventLane<TEvent> lane, in TEvent payload) where TEvent : unmanaged
        {
            if (lane.Adapters.Count == 0) return true;
            foreach (var adapter in lane.Adapters)
            {
                if (adapter is MonoBehaviour component && component != null && component.isActiveAndEnabled &&
                    adapter.Accept(in payload)) return true;
            }
            return false;
        }

        private InboxEnqueueResult TryReceiveStatePacket<TEvent>(in EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!CanReceiveStateEvents) return InboxEnqueueResult.MailboxUnavailable;
            if (!packet.IsValid) return InboxEnqueueResult.InvalidPacket;
            if (packet.Envelope.DeliveryMode == EventDeliveryMode.Targeted &&
                packet.Envelope.TargetGraphInstanceId != GraphInstanceId)
                return InboxEnqueueResult.EventTargetMismatch;
            var lane = _stateInbox.Find<TEvent>();
            if (lane == null || !Accepts(lane, packet.Payload)) return InboxEnqueueResult.UndeclaredEventType;
            if (lane.TryEnqueue(packet.Payload)) return InboxEnqueueResult.Accepted;
            return packet.Envelope.Reliability == EventReliability.Reliable
                ? InboxEnqueueResult.ReliableOverflowFaultRequired
                : InboxEnqueueResult.UnreliableOverflowDropped;
        }
    }
}
