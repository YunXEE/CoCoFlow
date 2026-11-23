using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    public sealed partial class CoCoStateGraphHost
    {
        private static readonly MethodInfo BindStateEventMethod = typeof(CoCoStateGraphHost)
            .GetMethod(nameof(BindStateEvent), BindingFlags.Instance | BindingFlags.NonPublic);
        private CoCoStateEventInbox _stateInbox;
        private CoCoEventAgent _stateEventAgent;

        private bool TryValidateEventAdapterBoundaries(out CoCoDiagnostic diagnostic)
        {
            foreach (var component in EventAdapters)
            {
                if (component == null || CoCoStateGraphHostBoundary.Contains(this, component)) continue;
                diagnostic = RegistryError(CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Event Adapter must belong to this Host boundary.");
                return false;
            }
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void StartStateEvents(ICoCoStateGraphProjectBindingProvider provider, CoCoCompiledStateGraph graph)
        {
            _stateInbox = new CoCoStateEventInbox(eventLaneCapacity);
            _stateEventAgent = new CoCoEventAgent();
            _runtime.AttachEvents(_stateInbox, _transaction);
            if (provider is CoCoStandardBindingProvider standard)
            {
                foreach (Type payload in standard.InboxEventTypes(graph))
                    BindStateEventMethod.MakeGenericMethod(payload).Invoke(this, new object[] { null });
            }

            var components = new List<MonoBehaviour>(EventAdapters);
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != null && CoCoStateGraphHostBoundary.Contains(this, component) &&
                    !components.Contains(component)) components.Add(component);
            }
            foreach (var component in components)
            {
                if (component == null) continue;
                foreach (var contract in component.GetType().GetInterfaces())
                {
                    if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(ICoCoEventInboxAdapter<>))
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
                _stateEventAgent.Subscribe((ref CoCoEventPacket<TEvent> packet) =>
                {
                    if (TryReceiveStatePacket(in packet) == CoCoInboxEnqueueResult.ReliableOverflowFaultRequired)
                        MarkReliableOverflowPending();
                });
            }
            if (component != null) lane.Adapters.Add((ICoCoEventInboxAdapter<TEvent>)component);
        }

        private bool CanReceiveStateEvents => CanAcceptEventInput &&
            _runtime.Lifecycle == CoCoRuntimeLifecycleState.Running && !_isTemporalOperation &&
            _temporal?.Mode != CoCoTemporalMode.Previewing;

        private static bool Accepts<TEvent>(CoCoStateEventLane<TEvent> lane, in TEvent payload) where TEvent : unmanaged
        {
            if (lane.Adapters.Count == 0) return true;
            foreach (var adapter in lane.Adapters)
            {
                if (adapter is MonoBehaviour component && component != null && component.isActiveAndEnabled &&
                    adapter.Accept(in payload)) return true;
            }
            return false;
        }

        private CoCoInboxEnqueueResult TryReceiveStatePacket<TEvent>(in CoCoEventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!CanReceiveStateEvents) return CoCoInboxEnqueueResult.MailboxUnavailable;
            if (!packet.IsValid) return CoCoInboxEnqueueResult.InvalidPacket;
            if (packet.Envelope.DeliveryMode == CoCoEventDeliveryMode.Targeted &&
                packet.Envelope.TargetGraphInstanceId != GraphInstanceId)
                return CoCoInboxEnqueueResult.EventTargetMismatch;
            var lane = _stateInbox.Find<TEvent>();
            if (lane == null || !Accepts(lane, packet.Payload)) return CoCoInboxEnqueueResult.UndeclaredEventType;
            if (lane.TryEnqueue(packet.Payload)) return CoCoInboxEnqueueResult.Accepted;
            return packet.Envelope.Reliability == CoCoEventReliability.Reliable
                ? CoCoInboxEnqueueResult.ReliableOverflowFaultRequired
                : CoCoInboxEnqueueResult.UnreliableOverflowDropped;
        }
    }
}
