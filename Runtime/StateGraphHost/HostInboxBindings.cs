using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// 把总线事件和定向事件包交给一个实例。场景订阅、过滤组件和退订均随该次启动生效。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>维护一台 Host 的订阅，并向 State 步进时封存的收件队列投递。</summary>
    internal sealed class HostInboxBindings : IDisposable
    {
        private static readonly MethodInfo BindStateEventMethod = typeof(HostInboxBindings)
            .GetMethod(nameof(BindStateEvent), BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly GraphInstanceId _instanceId;
        private readonly Func<bool> _canReceive;
        private readonly Action _markReliableOverflow;
        private readonly CoCoEventAgent _agent = new CoCoEventAgent();
        private readonly Dictionary<Type, object> _subscriptions = new Dictionary<Type, object>();
        private bool _disposed;

        internal HostInboxBindings(GraphInstanceId instanceId, int laneCapacity, Func<bool> canReceive,
            Action markReliableOverflow)
        {
            _instanceId = instanceId;
            Inbox = new StateEventInbox(laneCapacity);
            _canReceive = canReceive;
            _markReliableOverflow = markReliableOverflow;
        }

        internal StateEventInbox Inbox { get; }

        internal static bool TryValidateAdapters(HostComponentBoundary boundary,
            IReadOnlyList<MonoBehaviour> adapters, out CoCoDiagnostic diagnostic)
        {
            foreach (MonoBehaviour adapter in adapters)
            {
                if (adapter == null || boundary.Contains(adapter)) continue;
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Event Adapter must belong to this Host boundary.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void Subscribe(HostComponentBoundary boundary, IReadOnlyList<MonoBehaviour> adapters,
            IReadOnlyList<MonoBehaviour> descendants, IEnumerable<Type> payloads)
        {
            foreach (Type payload in payloads)
            {
                BindStateEventMethod.MakeGenericMethod(payload).Invoke(this, new object[] { null });
            }

            var components = new List<MonoBehaviour>(adapters);
            foreach (MonoBehaviour component in descendants)
            {
                if (component != null && boundary.Contains(component) && !components.Contains(component))
                {
                    components.Add(component);
                }
            }

            foreach (MonoBehaviour component in components)
            {
                if (component == null) continue;
                foreach (Type contract in component.GetType().GetInterfaces())
                {
                    if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEventInboxAdapter<>))
                    {
                        BindStateEventMethod.MakeGenericMethod(contract.GetGenericArguments())
                            .Invoke(this, new object[] { component });
                    }
                }
            }
        }

        private void BindStateEvent<TEvent>(MonoBehaviour component) where TEvent : unmanaged
        {
            if (!_subscriptions.TryGetValue(typeof(TEvent), out object found))
            {
                var subscription = new Subscription<TEvent>(Inbox.Register<TEvent>());
                found = subscription;
                _subscriptions.Add(typeof(TEvent), subscription);
                _agent.Subscribe((ref TEvent payload) =>
                {
                    if (!_disposed && _canReceive() && subscription.Accepts(payload))
                    {
                        subscription.Lane.TryEnqueue(payload);
                    }
                });
                _agent.Subscribe((ref EventPacket<TEvent> packet) =>
                {
                    if (TryReceive(packet) == InboxEnqueueResult.ReliableOverflowFaultRequired)
                    {
                        _markReliableOverflow();
                    }
                });
            }

            if (component != null)
            {
                ((Subscription<TEvent>)found).Adapters.Add((IEventInboxAdapter<TEvent>)component);
            }
        }

        internal InboxEnqueueResult TryReceive<TEvent>(in EventPacket<TEvent> packet) where TEvent : unmanaged
        {
            if (_disposed || !_canReceive()) return InboxEnqueueResult.MailboxUnavailable;
            if (!packet.IsValid) return InboxEnqueueResult.InvalidPacket;
            if (packet.Envelope.DeliveryMode == EventDeliveryMode.Targeted &&
                packet.Envelope.TargetGraphInstanceId != _instanceId) return InboxEnqueueResult.EventTargetMismatch;
            if (!_subscriptions.TryGetValue(typeof(TEvent), out object found)) return InboxEnqueueResult.UndeclaredEventType;

            var subscription = (Subscription<TEvent>)found;
            if (!subscription.Accepts(packet.Payload)) return InboxEnqueueResult.UndeclaredEventType;
            if (subscription.Lane.TryEnqueue(packet.Payload)) return InboxEnqueueResult.Accepted;
            return packet.Envelope.Reliability == EventReliability.Reliable
                ? InboxEnqueueResult.ReliableOverflowFaultRequired
                : InboxEnqueueResult.UnreliableOverflowDropped;
        }

        public void Dispose()
        {
            _disposed = true;
            _agent.UnsubscribeAll();
            _subscriptions.Clear();
            Inbox.Clear();
        }

        /// <summary>把某种载荷的场景过滤器与该实例的对应队列配对。</summary>
        private sealed class Subscription<TEvent> where TEvent : unmanaged
        {
            internal readonly List<IEventInboxAdapter<TEvent>> Adapters = new List<IEventInboxAdapter<TEvent>>();
            internal readonly StateEventLane<TEvent> Lane;

            internal Subscription(StateEventLane<TEvent> lane) => Lane = lane;

            internal bool Accepts(in TEvent payload)
            {
                if (Adapters.Count == 0) return true;
                foreach (IEventInboxAdapter<TEvent> adapter in Adapters)
                {
                    if (adapter is MonoBehaviour component && component != null && component.isActiveAndEnabled &&
                        adapter.Accept(payload)) return true;
                }

                return false;
            }
        }
    }
}
