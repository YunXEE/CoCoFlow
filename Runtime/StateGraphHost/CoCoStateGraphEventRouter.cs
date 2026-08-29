using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    internal interface ICoCoHostEventLane
    {
        CoCoEventDomainId EventDomainId { get; }
        CoCoEventTypeId EventTypeId { get; }
        bool AllowSourceEcho { get; }

        bool TryRegister(
            CoCoActorEventInboxCore inbox,
            int configuredCapacity,
            out CoCoDiagnostic diagnostic);

        bool RegisterRouter(CoCoStateGraphHost host);
        void UnregisterRouter();
    }

    internal interface ICoCoHostEventSink<TEvent>
        where TEvent : unmanaged
    {
        CoCoGraphInstanceId Owner { get; }
        CoCoEventTypeId EventTypeId { get; }
        void ReceiveFromRouter(ref CoCoEventPacket<TEvent> packet);
    }

    internal sealed class CoCoHostEventLane<TEvent> :
        ICoCoHostEventLane,
        ICoCoHostEventSink<TEvent>
        where TEvent : unmanaged
    {
        private CoCoActorEventInboxCore _inbox;
        private CoCoActorEventLaneHandle<TEvent> _handle;
        private CoCoStateGraphHost _host;
        private CoCoStateGraphDomainRouter _router;
        private int _restrictedCapacity;

        public CoCoHostEventLane(
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeId,
            int projectionCapacity,
            bool allowSourceEcho)
        {
            EventDomainId = eventDomainId;
            EventTypeId = eventTypeId;
            _restrictedCapacity = projectionCapacity;
            AllowSourceEcho = allowSourceEcho;
        }

        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeId { get; }
        public bool AllowSourceEcho { get; }
        public CoCoGraphInstanceId Owner => _inbox?.Owner ?? default;

        public void RestrictCapacity(int projectionCapacity)
        {
            if (projectionCapacity > 0 && projectionCapacity < _restrictedCapacity)
            {
                _restrictedCapacity = projectionCapacity;
            }
        }

        public bool TryRegister(
            CoCoActorEventInboxCore inbox,
            int configuredCapacity,
            out CoCoDiagnostic diagnostic)
        {
            int capacity = Math.Min(configuredCapacity, _restrictedCapacity);
            if (inbox == null || capacity <= 0)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxOverflow,
                    "Event lane requires one Inbox and a positive effective capacity.");
                return false;
            }

            if (!inbox.TryRegisterLane(
                    EventTypeId,
                    capacity,
                    AllowSourceEcho,
                    out _handle,
                    out diagnostic))
            {
                return false;
            }

            _inbox = inbox;
            return true;
        }

        public CoCoInboxEnqueueResult TryEnqueue(in CoCoEventPacket<TEvent> packet)
        {
            return _inbox == null
                ? CoCoInboxEnqueueResult.MailboxUnavailable
                : _inbox.TryEnqueue(_handle, packet);
        }

        public bool TryGetSealedBatch(out CoCoActorEventSealedBatch<TEvent> batch)
        {
            if (_inbox == null)
            {
                batch = default;
                return false;
            }

            return _inbox.TryGetSealedBatch(_handle, out batch);
        }

        public bool RegisterRouter(CoCoStateGraphHost host)
        {
            if (_router != null || host == null)
            {
                return false;
            }

            _host = host;
            _router = CoCoStateGraphEventRouterRegistry.Acquire(EventDomainId);
            if (_router.Register(this))
            {
                return true;
            }

            CoCoStateGraphDomainRouter failedRouter = _router;
            _router = null;
            _host = null;
            CoCoStateGraphEventRouterRegistry.Release(failedRouter);
            return false;
        }

        public void UnregisterRouter()
        {
            if (_router == null)
            {
                return;
            }

            CoCoStateGraphDomainRouter router = _router;
            _router = null;
            _host = null;
            router.Unregister(this);
            CoCoStateGraphEventRouterRegistry.Release(router);
        }

        public void ReceiveFromRouter(ref CoCoEventPacket<TEvent> packet)
        {
            CoCoStateGraphHost host = _host;
            if (host == null || !host.CanAcceptEventInput ||
                !packet.IsValid ||
                packet.Envelope.EventDomainId != EventDomainId ||
                packet.Envelope.EventTypeId != EventTypeId)
            {
                return;
            }

            CoCoInboxEnqueueResult result = TryEnqueue(packet);
            if (result == CoCoInboxEnqueueResult.ReliableOverflowFaultRequired)
            {
                host.MarkReliableOverflowPending();
            }
        }
    }

    internal static class CoCoStateGraphEventRouterRegistry
    {
        private static readonly Dictionary<CoCoEventDomainId, CoCoStateGraphDomainRouter> Routers =
            new Dictionary<CoCoEventDomainId, CoCoStateGraphDomainRouter>();

        public static CoCoStateGraphDomainRouter Acquire(CoCoEventDomainId domainId)
        {
            if (!Routers.TryGetValue(domainId, out CoCoStateGraphDomainRouter router))
            {
                router = new CoCoStateGraphDomainRouter(domainId);
                Routers.Add(domainId, router);
            }

            router.AddReference();
            return router;
        }

        public static void Release(CoCoStateGraphDomainRouter router)
        {
            if (router == null || !router.ReleaseReference())
            {
                return;
            }

            Routers.Remove(router.DomainId);
            router.Dispose();
        }

        public static void Reset()
        {
            foreach (CoCoStateGraphDomainRouter router in Routers.Values)
            {
                router.Dispose();
            }

            Routers.Clear();
        }

        internal static int Count => Routers.Count;
    }

    internal sealed class CoCoStateGraphDomainRouter : IDisposable
    {
        private readonly Dictionary<Type, ICoCoStateGraphEventRoute> _routes =
            new Dictionary<Type, ICoCoStateGraphEventRoute>();
        private readonly CoCoEventAgent _eventAgent = new CoCoEventAgent();
        private int _referenceCount;

        public CoCoStateGraphDomainRouter(CoCoEventDomainId domainId)
        {
            DomainId = domainId;
        }

        public CoCoEventDomainId DomainId { get; }

        public void AddReference()
        {
            _referenceCount++;
        }

        public bool ReleaseReference()
        {
            if (_referenceCount > 0)
            {
                _referenceCount--;
            }

            return _referenceCount == 0;
        }

        public bool Register<TEvent>(ICoCoHostEventSink<TEvent> sink)
            where TEvent : unmanaged
        {
            Type packetType = typeof(CoCoEventPacket<TEvent>);
            if (!_routes.TryGetValue(packetType, out ICoCoStateGraphEventRoute route))
            {
                var typed = new CoCoStateGraphEventRoute<TEvent>(DomainId);
                _routes.Add(packetType, typed);
                _eventAgent.Subscribe<CoCoEventPacket<TEvent>>(typed.OnPacket);
                route = typed;
            }

            return ((CoCoStateGraphEventRoute<TEvent>)route).Register(sink);
        }

        public void Unregister<TEvent>(ICoCoHostEventSink<TEvent> sink)
            where TEvent : unmanaged
        {
            if (_routes.TryGetValue(
                    typeof(CoCoEventPacket<TEvent>),
                    out ICoCoStateGraphEventRoute route))
            {
                ((CoCoStateGraphEventRoute<TEvent>)route).Unregister(sink);
            }
        }

        public void Dispose()
        {
            _eventAgent.UnsubscribeAll();
            _routes.Clear();
            _referenceCount = 0;
        }
    }

    internal interface ICoCoStateGraphEventRoute
    {
    }

    internal sealed class CoCoStateGraphEventRoute<TEvent> : ICoCoStateGraphEventRoute
        where TEvent : unmanaged
    {
        private readonly CoCoEventDomainId _domainId;
        private readonly Dictionary<
            CoCoEventTypeId,
            Dictionary<CoCoGraphInstanceId, ICoCoHostEventSink<TEvent>>> _sinksByEventType =
            new Dictionary<
                CoCoEventTypeId,
                Dictionary<CoCoGraphInstanceId, ICoCoHostEventSink<TEvent>>>();

        public CoCoStateGraphEventRoute(CoCoEventDomainId domainId)
        {
            _domainId = domainId;
        }

        public bool Register(ICoCoHostEventSink<TEvent> sink)
        {
            if (sink == null || !sink.Owner.IsValid || !sink.EventTypeId.IsValid)
            {
                return false;
            }

            if (!_sinksByEventType.TryGetValue(
                    sink.EventTypeId,
                    out Dictionary<CoCoGraphInstanceId, ICoCoHostEventSink<TEvent>> sinks))
            {
                sinks = new Dictionary<CoCoGraphInstanceId, ICoCoHostEventSink<TEvent>>();
                _sinksByEventType.Add(sink.EventTypeId, sinks);
            }

            if (sinks.ContainsKey(sink.Owner))
            {
                return false;
            }

            sinks.Add(sink.Owner, sink);
            return true;
        }

        public void Unregister(ICoCoHostEventSink<TEvent> sink)
        {
            if (sink != null && sink.Owner.IsValid &&
                _sinksByEventType.TryGetValue(
                    sink.EventTypeId,
                    out Dictionary<CoCoGraphInstanceId, ICoCoHostEventSink<TEvent>> sinks) &&
                sinks.TryGetValue(sink.Owner, out ICoCoHostEventSink<TEvent> current) &&
                ReferenceEquals(current, sink))
            {
                sinks.Remove(sink.Owner);
                if (sinks.Count == 0)
                {
                    _sinksByEventType.Remove(sink.EventTypeId);
                }
            }
        }

        public void OnPacket(ref CoCoEventPacket<TEvent> packet)
        {
            if (!packet.IsValid || packet.Envelope.EventDomainId != _domainId)
            {
                return;
            }

            CoCoActorEventEnvelope envelope = packet.Envelope;
            if (!_sinksByEventType.TryGetValue(
                    envelope.EventTypeId,
                    out Dictionary<CoCoGraphInstanceId, ICoCoHostEventSink<TEvent>> sinks))
            {
                return;
            }

            if (envelope.DeliveryMode == CoCoEventDeliveryMode.Targeted)
            {
                if (sinks.TryGetValue(
                        envelope.TargetGraphInstanceId,
                        out ICoCoHostEventSink<TEvent> target))
                {
                    target.ReceiveFromRouter(ref packet);
                }

                return;
            }

            if (envelope.DeliveryMode != CoCoEventDeliveryMode.DeclaredBroadcast)
            {
                return;
            }

            foreach (ICoCoHostEventSink<TEvent> sink in sinks.Values)
            {
                sink.ReceiveFromRouter(ref packet);
            }
        }
    }
}
