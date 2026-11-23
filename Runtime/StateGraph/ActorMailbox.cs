using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public enum EventDeliveryMode
    {
        None = 0,
        Targeted = 1,
        DeclaredBroadcast = 2
    }

    public enum EventReliability
    {
        None = 0,
        Unreliable = 1,
        Reliable = 2
    }

    public enum ActorEventInboxState
    {
        None = 0,
        Created = 1,
        Running = 2,
        Suspended = 3,
        RewindingOrRestoring = 4,
        Stopped = 5,
        Disposed = 6
    }

    public enum InboxEnqueueResult
    {
        None = 0,
        Accepted = 1,
        InvalidPacket = 2,
        MailboxUnavailable = 3,
        EventDomainMismatch = 4,
        EventTargetMismatch = 5,
        UndeclaredEventType = 6,
        SourceEchoRejected = 7,
        Duplicate = 8,
        StaleTimelineEpoch = 9,
        EventSequenceConflict = 10,
        SourceWindowFull = 11,
        UnreliableOverflowDropped = 12,
        ReliableOverflowFaultRequired = 13,
        RewindOrRestoreDropped = 14
    }

    public readonly struct ActorEventEnvelope : IEquatable<ActorEventEnvelope>
    {
        private ActorEventEnvelope(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            GraphInstanceId sourceGraphInstanceId,
            GraphInstanceId targetGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            EventSequence sourceEventSequence,
            EventDeliveryMode deliveryMode,
            EventReliability reliability,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId)
        {
            EventTypeId = eventTypeId;
            EventDomainId = eventDomainId;
            SourceGraphInstanceId = sourceGraphInstanceId;
            TargetGraphInstanceId = targetGraphInstanceId;
            SourceTimelineEpoch = sourceTimelineEpoch;
            SourceTick = sourceTick;
            SourceEventSequence = sourceEventSequence;
            DeliveryMode = deliveryMode;
            Reliability = reliability;
            StableEntityId = stableEntityId;
            ActivationId = activationId;
            CorrelationId = correlationId;
        }

        public EventTypeId EventTypeId { get; }
        public EventDomainId EventDomainId { get; }
        public GraphInstanceId SourceGraphInstanceId { get; }
        public GraphInstanceId TargetGraphInstanceId { get; }
        public TimelineEpoch SourceTimelineEpoch { get; }
        public TimelineTick SourceTick { get; }
        public EventSequence SourceEventSequence { get; }
        public EventDeliveryMode DeliveryMode { get; }
        public EventReliability Reliability { get; }
        public StableEntityId StableEntityId { get; }
        public ActivationId ActivationId { get; }
        public CorrelationId CorrelationId { get; }

        public bool IsValid => EventTypeId.IsValid &&
                               EventDomainId.IsValid &&
                               SourceGraphInstanceId.IsValid &&
                               SourceEventSequence.IsValid &&
                               (DeliveryMode == EventDeliveryMode.Targeted ||
                                DeliveryMode == EventDeliveryMode.DeclaredBroadcast) &&
                               (Reliability == EventReliability.Unreliable ||
                                Reliability == EventReliability.Reliable) &&
                               ((DeliveryMode == EventDeliveryMode.Targeted &&
                                 TargetGraphInstanceId.IsValid) ||
                                (DeliveryMode == EventDeliveryMode.DeclaredBroadcast &&
                                 !TargetGraphInstanceId.IsValid));

        public static bool TryCreate(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            GraphInstanceId sourceGraphInstanceId,
            GraphInstanceId targetGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            EventSequence sourceEventSequence,
            EventDeliveryMode deliveryMode,
            EventReliability reliability,
            StableEntityId stableEntityId,
            ActivationId activationId,
            CorrelationId correlationId,
            out ActorEventEnvelope envelope)
        {
            envelope = new ActorEventEnvelope(
                eventTypeId,
                eventDomainId,
                sourceGraphInstanceId,
                targetGraphInstanceId,
                sourceTimelineEpoch,
                sourceTick,
                sourceEventSequence,
                deliveryMode,
                reliability,
                stableEntityId,
                activationId,
                correlationId);

            if (envelope.IsValid)
            {
                return true;
            }

            envelope = default;
            return false;
        }

        public bool Equals(ActorEventEnvelope other)
        {
            return EventTypeId == other.EventTypeId &&
                   EventDomainId == other.EventDomainId &&
                   SourceGraphInstanceId == other.SourceGraphInstanceId &&
                   TargetGraphInstanceId == other.TargetGraphInstanceId &&
                   SourceTimelineEpoch == other.SourceTimelineEpoch &&
                   SourceTick == other.SourceTick &&
                   SourceEventSequence == other.SourceEventSequence &&
                   DeliveryMode == other.DeliveryMode &&
                   Reliability == other.Reliability &&
                   StableEntityId == other.StableEntityId &&
                   ActivationId == other.ActivationId &&
                   CorrelationId == other.CorrelationId;
        }

        public override bool Equals(object obj) => obj is ActorEventEnvelope other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ EventDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceGraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ TargetGraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceTimelineEpoch.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceTick.GetHashCode();
                hashCode = (hashCode * 397) ^ SourceEventSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)DeliveryMode;
                hashCode = (hashCode * 397) ^ (int)Reliability;
                hashCode = (hashCode * 397) ^ StableEntityId.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ CorrelationId.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(ActorEventEnvelope left, ActorEventEnvelope right) =>
            left.Equals(right);

        public static bool operator !=(ActorEventEnvelope left, ActorEventEnvelope right) =>
            !left.Equals(right);
    }

    public readonly struct EventPacket<TEvent>
        where TEvent : unmanaged
    {
        private EventPacket(ActorEventEnvelope envelope, TEvent payload)
        {
            Envelope = envelope;
            Payload = payload;
        }

        public ActorEventEnvelope Envelope { get; }
        public TEvent Payload { get; }
        public bool IsValid => Envelope.IsValid;

        public static bool TryCreate(
            ActorEventEnvelope envelope,
            in TEvent payload,
            out EventPacket<TEvent> packet)
        {
            if (!envelope.IsValid)
            {
                packet = default;
                return false;
            }

            packet = new EventPacket<TEvent>(envelope, payload);
            return true;
        }
    }

    public readonly struct ActorEventManifestEntry : IEquatable<ActorEventManifestEntry>
    {
        internal ActorEventManifestEntry(
            EventTypeId eventTypeId,
            Type payloadType,
            int capacity,
            bool allowSourceEcho)
        {
            EventTypeId = eventTypeId;
            PayloadType = payloadType;
            Capacity = capacity;
            AllowSourceEcho = allowSourceEcho;
        }

        public EventTypeId EventTypeId { get; }
        public Type PayloadType { get; }
        public int Capacity { get; }
        public bool AllowSourceEcho { get; }
        public bool IsValid => EventTypeId.IsValid && PayloadType != null && Capacity > 0;

        public bool Equals(ActorEventManifestEntry other)
        {
            return EventTypeId == other.EventTypeId &&
                   PayloadType == other.PayloadType &&
                   Capacity == other.Capacity &&
                   AllowSourceEcho == other.AllowSourceEcho;
        }

        public override bool Equals(object obj) =>
            obj is ActorEventManifestEntry other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ (PayloadType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ Capacity;
                hashCode = (hashCode * 397) ^ AllowSourceEcho.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(
            ActorEventManifestEntry left,
            ActorEventManifestEntry right) => left.Equals(right);

        public static bool operator !=(
            ActorEventManifestEntry left,
            ActorEventManifestEntry right) => !left.Equals(right);
    }

    public readonly struct ActorEventLaneHandle<TEvent> :
        IEquatable<ActorEventLaneHandle<TEvent>>
        where TEvent : unmanaged
    {
        private readonly ActorEventInboxCore _inbox;

        internal ActorEventLaneHandle(
            ActorEventInboxCore inbox,
            GraphInstanceId owner,
            EventDomainId eventDomainId,
            EventTypeId eventTypeId,
            int denseIndex)
        {
            _inbox = inbox;
            Owner = owner;
            EventDomainId = eventDomainId;
            EventTypeId = eventTypeId;
            DenseIndex = denseIndex;
        }

        public GraphInstanceId Owner { get; }
        public EventDomainId EventDomainId { get; }
        public EventTypeId EventTypeId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _inbox != null &&
                               Owner.IsValid &&
                               EventDomainId.IsValid &&
                               EventTypeId.IsValid &&
                               DenseIndex >= 0;

        internal bool IsOwnedBy(ActorEventInboxCore inbox) =>
            ReferenceEquals(_inbox, inbox);

        public bool Equals(ActorEventLaneHandle<TEvent> other)
        {
            return ReferenceEquals(_inbox, other._inbox) &&
                   Owner == other.Owner &&
                   EventDomainId == other.EventDomainId &&
                   EventTypeId == other.EventTypeId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) =>
            obj is ActorEventLaneHandle<TEvent> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _inbox?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ Owner.GetHashCode();
                hashCode = (hashCode * 397) ^ EventDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ EventTypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ DenseIndex;
                return hashCode;
            }
        }

        public static bool operator ==(
            ActorEventLaneHandle<TEvent> left,
            ActorEventLaneHandle<TEvent> right) => left.Equals(right);

        public static bool operator !=(
            ActorEventLaneHandle<TEvent> left,
            ActorEventLaneHandle<TEvent> right) => !left.Equals(right);
    }

    public readonly struct ActorEventSealedBatch<TEvent>
        where TEvent : unmanaged
    {
        private readonly ActorEventInboxCore _inbox;
        private readonly ActorEventLaneHandle<TEvent> _handle;
        private readonly ulong _generation;

        internal ActorEventSealedBatch(
            ActorEventInboxCore inbox,
            ActorEventLaneHandle<TEvent> handle,
            ulong generation)
        {
            _inbox = inbox;
            _handle = handle;
            _generation = generation;
        }

        public bool IsValid => _inbox != null &&
                               _inbox.IsSealedBatchCurrent(_handle, _generation);
        public int Count => _inbox?.GetSealedCount(_handle, _generation) ?? 0;
        internal GraphInstanceId Owner => _handle.Owner;
        internal EventDomainId EventDomainId => _handle.EventDomainId;
        internal EventTypeId EventTypeId => _handle.EventTypeId;

        public bool TryRead(int index, out EventPacket<TEvent> packet)
        {
            if (_inbox == null)
            {
                packet = default;
                return false;
            }

            return _inbox.TryReadSealed(_handle, _generation, index, out packet);
        }
    }

    public readonly struct ActorInboxCounters
    {
        internal ActorInboxCounters(
            ulong accepted,
            ulong duplicate,
            ulong rejected,
            ulong rewindRestoreDropped,
            ulong unreliableOverflowDropped,
            ulong reliableOverflowFaults)
        {
            Accepted = accepted;
            Duplicate = duplicate;
            Rejected = rejected;
            RewindRestoreDropped = rewindRestoreDropped;
            UnreliableOverflowDropped = unreliableOverflowDropped;
            ReliableOverflowFaults = reliableOverflowFaults;
        }

        public ulong Accepted { get; }
        public ulong Duplicate { get; }
        public ulong Rejected { get; }
        public ulong RewindRestoreDropped { get; }
        public ulong UnreliableOverflowDropped { get; }
        public ulong ReliableOverflowFaults { get; }
    }

    public sealed class ActorEventInboxCore : IDisposable
    {
        private readonly IActorEventInboxLane[] _lanes;
        private readonly InboxSourceEpoch[] _sourceEpochs;
        private readonly InboxDedupEntry[] _dedupEntries;
        private int _laneCount;
        private int _sourceCount;
        private int _dedupWriteIndex;
        private ActorEventInboxState _state;
        private TickFrame _lastSealedTickFrame;
        private bool _hasLastSealedTick;
        private bool _requiresNewTimelineEpoch;
        private TimelineEpoch _requiredOwnerTimelineEpoch;
        private bool _hasRequiredOwnerTimelineEpoch;
        private IntentFrameRuntime _intentRuntime;
        private ActorEventInboxState _deferredLifecycleState;
        private ulong _accepted;
        private ulong _duplicate;
        private ulong _rejected;
        private ulong _rewindRestoreDropped;
        private ulong _unreliableOverflowDropped;
        private ulong _reliableOverflowFaults;

        public ActorEventInboxCore(
            GraphInstanceId owner,
            EventDomainId eventDomainId,
            int maxEventTypes,
            int maxSources,
            int dedupWindowCapacity)
        {
            if (!owner.IsValid)
            {
                throw new ArgumentException("Inbox owner must be valid.", nameof(owner));
            }

            if (!eventDomainId.IsValid)
            {
                throw new ArgumentException("Inbox event domain must be valid.", nameof(eventDomainId));
            }

            if (maxEventTypes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEventTypes));
            }

            if (maxSources <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSources));
            }

            if (dedupWindowCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dedupWindowCapacity));
            }

            Owner = owner;
            EventDomainId = eventDomainId;
            _lanes = new IActorEventInboxLane[maxEventTypes];
            _sourceEpochs = new InboxSourceEpoch[maxSources];
            _dedupEntries = new InboxDedupEntry[dedupWindowCapacity];
            _state = ActorEventInboxState.Created;
        }

        public GraphInstanceId Owner { get; }
        public EventDomainId EventDomainId { get; }
        public int ManifestCount => _laneCount;
        public ActorEventInboxState State => _state;
        public bool HasReliableOverflowFault => _reliableOverflowFaults != 0UL;
        public ActorInboxCounters Counters => new ActorInboxCounters(
            _accepted,
            _duplicate,
            _rejected,
            _rewindRestoreDropped,
            _unreliableOverflowDropped,
            _reliableOverflowFaults);

        public bool TryRegisterLane<TEvent>(
            EventTypeId eventTypeId,
            int capacity,
            bool allowSourceEcho,
            out ActorEventLaneHandle<TEvent> handle,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (_state != ActorEventInboxState.Created ||
                _deferredLifecycleState != ActorEventInboxState.None)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Mailbox manifest is frozen after Start.");
                return false;
            }

            if (!eventTypeId.IsValid || capacity <= 0)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.InvalidEventPacket,
                    "Event type and lane capacity must be valid.");
                return false;
            }

            if (_laneCount >= _lanes.Length)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxOverflow,
                    "Mailbox manifest capacity is exhausted.");
                return false;
            }

            for (int index = 0; index < _laneCount; index++)
            {
                if (_lanes[index].Manifest.EventTypeId == eventTypeId)
                {
                    handle = default;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Identity,
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "Mailbox event type ids must be unique.");
                    return false;
                }
            }

            handle = new ActorEventLaneHandle<TEvent>(
                this,
                Owner,
                EventDomainId,
                eventTypeId,
                _laneCount);
            _lanes[_laneCount] = new ActorEventInboxLane<TEvent>(
                eventTypeId,
                capacity,
                allowSourceEcho);
            _laneCount++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryBindIntentRuntime(
            IntentFrameRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (_state != ActorEventInboxState.Created ||
                _deferredLifecycleState != ActorEventInboxState.None ||
                runtime == null ||
                runtime.IsDisposed ||
                runtime.GraphInstanceId != Owner ||
                (_intentRuntime != null && !ReferenceEquals(_intentRuntime, runtime)) ||
                !runtime.TryClaimInbox(this))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "An Inbox may bind exactly one live Intent runtime owned by the same GraphInstance before Start.");
                return false;
            }

            _intentRuntime = runtime;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryGetManifestEntry(int denseIndex, out ActorEventManifestEntry entry)
        {
            if (denseIndex < 0 || denseIndex >= _laneCount)
            {
                entry = default;
                return false;
            }

            entry = _lanes[denseIndex].Manifest;
            return true;
        }

        public bool Start(out CoCoDiagnostic diagnostic)
        {
            if (_state != ActorEventInboxState.Created ||
                _deferredLifecycleState != ActorEventInboxState.None)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Mailbox can only start from Created.");
                return false;
            }

            if (_intentRuntime == null || _intentRuntime.IsDisposed ||
                _intentRuntime.IsExecutingUserCallback || _intentRuntime.IsCollecting)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Mailbox requires one idle live Intent runtime before Start.");
                return false;
            }

            if (!_intentRuntime.AreBindingsFrozen)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "Intent bindings must be frozen before Inbox Start.");
                return false;
            }

            _state = ActorEventInboxState.Running;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Suspend()
        {
            if (_state != ActorEventInboxState.Running ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked())
            {
                return false;
            }

            _state = ActorEventInboxState.Suspended;
            return true;
        }

        public bool Resume()
        {
            if (_state != ActorEventInboxState.Suspended ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked())
            {
                return false;
            }

            _state = ActorEventInboxState.Running;
            return true;
        }

        public bool BeginRewindOrRestore()
        {
            if (_state != ActorEventInboxState.Running ||
                !HasLiveIntentRuntime() ||
                IsLifecycleTransitionBlocked() ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            ClearQueuedState();
            _state = ActorEventInboxState.RewindingOrRestoring;
            return true;
        }

        internal bool CanResumeAfterTimelineReset =>
            _state == ActorEventInboxState.RewindingOrRestoring &&
            HasLiveIntentRuntime() &&
            !_intentRuntime.IsCollecting &&
            !IsLifecycleTransitionBlocked();

        public bool ResumeAfterTimelineReset()
        {
            if (!CanResumeAfterTimelineReset ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            ResumeAfterTimelineResetNoFail();
            return true;
        }

        internal void ResumeAfterTimelineResetNoFail()
        {
            if (!CanResumeAfterTimelineReset)
            {
                return;
            }

            ClearQueuedState();
            _requiresNewTimelineEpoch = true;
            _state = ActorEventInboxState.Running;
        }

        internal void ResumeAfterTimelineResetNoFail(TimelineEpoch ownerTimelineEpoch)
        {
            if (!CanResumeAfterTimelineReset)
            {
                return;
            }

            ClearQueuedState();
            ClearOwnerSourceEpoch();
            _requiredOwnerTimelineEpoch = ownerTimelineEpoch;
            _hasRequiredOwnerTimelineEpoch = true;
            _requiresNewTimelineEpoch = true;
            _state = ActorEventInboxState.Running;
        }

        internal bool CanCancelRewindOrRestore =>
            _state == ActorEventInboxState.RewindingOrRestoring &&
            HasLiveIntentRuntime() &&
            !_intentRuntime.IsCollecting &&
            !IsLifecycleTransitionBlocked();

        public bool CancelRewindOrRestore()
        {
            if (!CanCancelRewindOrRestore ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            CancelRewindOrRestoreNoFail();
            return true;
        }

        internal void CancelRewindOrRestoreNoFail()
        {
            if (!CanCancelRewindOrRestore)
            {
                return;
            }

            ClearQueuedState();
            _state = ActorEventInboxState.Running;
        }

        public void Stop()
        {
            if (_state == ActorEventInboxState.Disposed ||
                _deferredLifecycleState == ActorEventInboxState.Disposed)
            {
                return;
            }

            if (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback)
            {
                if (_deferredLifecycleState == ActorEventInboxState.None)
                {
                    _deferredLifecycleState = ActorEventInboxState.Stopped;
                }

                return;
            }

            CancelActiveIntentCollection();

            ClearAllRuntimeState();
            ReleaseIntentRuntimeBinding();
            _deferredLifecycleState = ActorEventInboxState.None;
            _state = ActorEventInboxState.Stopped;
        }

        public void Dispose()
        {
            if (_state == ActorEventInboxState.Disposed)
            {
                return;
            }

            if (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback)
            {
                _deferredLifecycleState = ActorEventInboxState.Disposed;
                return;
            }

            CancelActiveIntentCollection();

            ClearAllRuntimeState();
            ReleaseIntentRuntimeBinding();
            _deferredLifecycleState = ActorEventInboxState.None;
            _state = ActorEventInboxState.Disposed;
        }

        public InboxEnqueueResult TryEnqueue<TEvent>(
            ActorEventLaneHandle<TEvent> handle,
            in EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (_state == ActorEventInboxState.RewindingOrRestoring)
            {
                _rewindRestoreDropped++;
                _rejected++;
                return InboxEnqueueResult.RewindOrRestoreDropped;
            }

            if (_state != ActorEventInboxState.Running &&
                _state != ActorEventInboxState.Suspended)
            {
                _rejected++;
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (_deferredLifecycleState != ActorEventInboxState.None)
            {
                _rejected++;
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (!HasLiveIntentRuntime())
            {
                _rejected++;
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (!packet.IsValid || !Matches(handle))
            {
                _rejected++;
                return InboxEnqueueResult.InvalidPacket;
            }

            ActorEventEnvelope envelope = packet.Envelope;
            if (envelope.EventDomainId != EventDomainId)
            {
                _rejected++;
                return InboxEnqueueResult.EventDomainMismatch;
            }

            var lane = _lanes[handle.DenseIndex] as ActorEventInboxLane<TEvent>;
            if (lane == null || envelope.EventTypeId != handle.EventTypeId)
            {
                _rejected++;
                return InboxEnqueueResult.UndeclaredEventType;
            }

            if (envelope.DeliveryMode == EventDeliveryMode.Targeted)
            {
                if (envelope.TargetGraphInstanceId != Owner)
                {
                    _rejected++;
                    return InboxEnqueueResult.EventTargetMismatch;
                }
            }
            else
            {
                if (envelope.TargetGraphInstanceId.IsValid)
                {
                    _rejected++;
                    return InboxEnqueueResult.EventTargetMismatch;
                }

                if (!lane.Manifest.AllowSourceEcho && envelope.SourceGraphInstanceId == Owner)
                {
                    _rejected++;
                    return InboxEnqueueResult.SourceEchoRejected;
                }
            }

            InboxEnqueueResult sourceResult = InspectSourceEpoch(
                envelope.SourceGraphInstanceId,
                envelope.SourceTimelineEpoch,
                out int sourceIndex,
                out bool sourceIsNew,
                out bool epochIsNew);
            if (sourceResult != InboxEnqueueResult.Accepted)
            {
                if (sourceResult == InboxEnqueueResult.SourceWindowFull)
                {
                    return RejectOverflow(envelope.Reliability);
                }

                _rejected++;
                return sourceResult;
            }

            InboxEnqueueResult dedupResult = InspectDedup(envelope);
            if (dedupResult != InboxEnqueueResult.Accepted)
            {
                if (dedupResult == InboxEnqueueResult.Duplicate)
                {
                    _duplicate++;
                }

                _rejected++;
                return dedupResult;
            }

            if (!sourceIsNew &&
                !epochIsNew &&
                envelope.SourceEventSequence.Value <= _sourceEpochs[sourceIndex].LastSequence.Value)
            {
                _rejected++;
                return InboxEnqueueResult.EventSequenceConflict;
            }

            if (!lane.HasIncomingCapacity)
            {
                return RejectOverflow(envelope.Reliability);
            }

            if (sourceIsNew)
            {
                _sourceEpochs[sourceIndex] = new InboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
                _sourceCount++;
            }
            else if (epochIsNew)
            {
                ClearDedupForSource(envelope.SourceGraphInstanceId);
                _sourceEpochs[sourceIndex] = new InboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
            }
            else
            {
                _sourceEpochs[sourceIndex] = new InboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
            }

            lane.Enqueue(packet);
            RecordDedup(envelope);
            _accepted++;
            return InboxEnqueueResult.Accepted;
        }

        public bool SealForTick(in TickFrame tickFrame)
        {
            if (_state != ActorEventInboxState.Running ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked() ||
                !tickFrame.IsValid ||
                (_requiresNewTimelineEpoch &&
                 _hasLastSealedTick &&
                 tickFrame.TimelineEpoch.Value <=
                 _lastSealedTickFrame.TimelineEpoch.Value) ||
                (_hasLastSealedTick &&
                 !StateFlowTickOrder.IsStrictlyAfter(
                     tickFrame,
                     _lastSealedTickFrame)))
            {
                return false;
            }

            for (int index = 0; index < _laneCount; index++)
            {
                if (!_lanes[index].CanAdvanceGeneration)
                {
                    return false;
                }
            }

            for (int index = 0; index < _laneCount; index++)
            {
                _lanes[index].SealForTick();
            }

            _lastSealedTickFrame = tickFrame;
            _hasLastSealedTick = true;
            _requiresNewTimelineEpoch = false;
            return true;
        }

        public int GetSealedCount<TEvent>(ActorEventLaneHandle<TEvent> handle)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                return 0;
            }

            return (_lanes[handle.DenseIndex] as ActorEventInboxLane<TEvent>)?.SealedCount ?? 0;
        }

        public bool TryReadSealed<TEvent>(
            ActorEventLaneHandle<TEvent> handle,
            int index,
            out EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                packet = default;
                return false;
            }

            var lane = _lanes[handle.DenseIndex] as ActorEventInboxLane<TEvent>;
            if (lane == null)
            {
                packet = default;
                return false;
            }

            return lane.TryReadSealed(index, out packet);
        }

        public bool TryGetSealedBatch<TEvent>(
            ActorEventLaneHandle<TEvent> handle,
            out ActorEventSealedBatch<TEvent> batch)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                batch = default;
                return false;
            }

            IActorEventInboxLane lane = _lanes[handle.DenseIndex];
            ulong generation = lane.SealedGeneration;
            if (!lane.HasSealedBatch)
            {
                batch = default;
                return false;
            }

            batch = new ActorEventSealedBatch<TEvent>(this, handle, generation);
            return true;
        }

        internal int GetSealedCount<TEvent>(
            ActorEventLaneHandle<TEvent> handle,
            ulong generation)
            where TEvent : unmanaged
        {
            if (!Matches(handle) ||
                !_lanes[handle.DenseIndex].HasSealedBatch ||
                _lanes[handle.DenseIndex].SealedGeneration != generation)
            {
                return 0;
            }

            return GetSealedCount(handle);
        }

        internal bool IsSealedBatchCurrent<TEvent>(
            ActorEventLaneHandle<TEvent> handle,
            ulong generation)
            where TEvent : unmanaged
        {
            return generation != 0UL && Matches(handle) &&
                   _lanes[handle.DenseIndex].HasSealedBatch &&
                   _lanes[handle.DenseIndex].SealedGeneration == generation;
        }

        internal void OnIntentRuntimeDisposed(IntentFrameRuntime runtime)
        {
            if (!ReferenceEquals(_intentRuntime, runtime))
            {
                return;
            }

            ActorEventInboxState deferredState = _deferredLifecycleState;
            _deferredLifecycleState = ActorEventInboxState.None;
            _intentRuntime = null;
            if (_state == ActorEventInboxState.Created)
            {
                if (deferredState == ActorEventInboxState.Disposed)
                {
                    ClearAllRuntimeState();
                    _state = ActorEventInboxState.Disposed;
                }

                return;
            }

            if (_state != ActorEventInboxState.Disposed)
            {
                ClearAllRuntimeState();
                _state = deferredState == ActorEventInboxState.Disposed
                    ? ActorEventInboxState.Disposed
                    : ActorEventInboxState.Stopped;
            }
        }

        internal bool HasDeferredLifecycle(IntentFrameRuntime runtime)
        {
            return runtime != null &&
                   ReferenceEquals(_intentRuntime, runtime) &&
                   _deferredLifecycleState != ActorEventInboxState.None;
        }

        internal void CompleteDeferredLifecycle(IntentFrameRuntime runtime)
        {
            if (!HasDeferredLifecycle(runtime) || runtime.IsExecutingUserCallback)
            {
                return;
            }

            ActorEventInboxState targetState = _deferredLifecycleState;
            _deferredLifecycleState = ActorEventInboxState.None;
            if (targetState == ActorEventInboxState.Disposed)
            {
                Dispose();
            }
            else
            {
                Stop();
            }
        }

        internal bool TryReadSealed<TEvent>(
            ActorEventLaneHandle<TEvent> handle,
            ulong generation,
            int index,
            out EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!Matches(handle) ||
                !_lanes[handle.DenseIndex].HasSealedBatch ||
                _lanes[handle.DenseIndex].SealedGeneration != generation)
            {
                packet = default;
                return false;
            }

            return TryReadSealed(handle, index, out packet);
        }

        private bool Matches<TEvent>(ActorEventLaneHandle<TEvent> handle)
            where TEvent : unmanaged
        {
            if (!handle.IsValid ||
                !handle.IsOwnedBy(this) ||
                handle.Owner != Owner ||
                handle.EventDomainId != EventDomainId ||
                handle.DenseIndex < 0 ||
                handle.DenseIndex >= _laneCount)
            {
                return false;
            }

            ActorEventManifestEntry manifest = _lanes[handle.DenseIndex].Manifest;
            return manifest.EventTypeId == handle.EventTypeId;
        }

        private InboxEnqueueResult InspectSourceEpoch(
            GraphInstanceId source,
            TimelineEpoch epoch,
            out int sourceIndex,
            out bool sourceIsNew,
            out bool epochIsNew)
        {
            if (_hasRequiredOwnerTimelineEpoch && source == Owner)
            {
                sourceIndex = -1;
                sourceIsNew = false;
                epochIsNew = false;
                if (epoch.Value < _requiredOwnerTimelineEpoch.Value)
                {
                    return InboxEnqueueResult.StaleTimelineEpoch;
                }

                if (epoch.Value > _requiredOwnerTimelineEpoch.Value)
                {
                    return InboxEnqueueResult.InvalidPacket;
                }
            }

            for (int index = 0; index < _sourceCount; index++)
            {
                if (_sourceEpochs[index].Source != source)
                {
                    continue;
                }

                sourceIndex = index;
                sourceIsNew = false;
                epochIsNew = epoch.Value > _sourceEpochs[index].Epoch.Value;
                return epoch.Value < _sourceEpochs[index].Epoch.Value
                    ? InboxEnqueueResult.StaleTimelineEpoch
                    : InboxEnqueueResult.Accepted;
            }

            if (_sourceCount >= _sourceEpochs.Length)
            {
                sourceIndex = -1;
                sourceIsNew = false;
                epochIsNew = false;
                return InboxEnqueueResult.SourceWindowFull;
            }

            sourceIndex = _sourceCount;
            sourceIsNew = true;
            epochIsNew = false;
            return InboxEnqueueResult.Accepted;
        }

        private InboxEnqueueResult InspectDedup(in ActorEventEnvelope envelope)
        {
            for (int index = 0; index < _dedupEntries.Length; index++)
            {
                InboxDedupEntry entry = _dedupEntries[index];
                if (!entry.IsValid ||
                    entry.Source != envelope.SourceGraphInstanceId ||
                    entry.Epoch != envelope.SourceTimelineEpoch ||
                    entry.Sequence != envelope.SourceEventSequence)
                {
                    continue;
                }

                return entry.EventTypeId == envelope.EventTypeId
                    ? InboxEnqueueResult.Duplicate
                    : InboxEnqueueResult.EventSequenceConflict;
            }

            return InboxEnqueueResult.Accepted;
        }

        private void RecordDedup(in ActorEventEnvelope envelope)
        {
            _dedupEntries[_dedupWriteIndex] = new InboxDedupEntry(
                envelope.SourceGraphInstanceId,
                envelope.SourceTimelineEpoch,
                envelope.SourceEventSequence,
                envelope.EventTypeId);
            _dedupWriteIndex++;
            if (_dedupWriteIndex == _dedupEntries.Length)
            {
                _dedupWriteIndex = 0;
            }
        }

        private void ClearDedupForSource(GraphInstanceId source)
        {
            for (int index = 0; index < _dedupEntries.Length; index++)
            {
                if (_dedupEntries[index].Source == source)
                {
                    _dedupEntries[index] = default;
                }
            }
        }

        private void ClearOwnerSourceEpoch()
        {
            for (int index = 0; index < _sourceCount; index++)
            {
                if (_sourceEpochs[index].Source != Owner)
                {
                    continue;
                }

                _sourceCount--;
                for (int moveIndex = index; moveIndex < _sourceCount; moveIndex++)
                {
                    _sourceEpochs[moveIndex] = _sourceEpochs[moveIndex + 1];
                }

                _sourceEpochs[_sourceCount] = default;
                return;
            }
        }

        private InboxEnqueueResult RejectOverflow(EventReliability reliability)
        {
            _rejected++;
            if (reliability == EventReliability.Reliable)
            {
                _reliableOverflowFaults++;
                return InboxEnqueueResult.ReliableOverflowFaultRequired;
            }

            _unreliableOverflowDropped++;
            return InboxEnqueueResult.UnreliableOverflowDropped;
        }

        private void ClearQueuedState()
        {
            for (int index = 0; index < _laneCount; index++)
            {
                _lanes[index].Clear();
            }

            Array.Clear(_dedupEntries, 0, _dedupEntries.Length);
            _dedupWriteIndex = 0;
        }

        private void ClearAllRuntimeState()
        {
            ClearQueuedState();
            Array.Clear(_sourceEpochs, 0, _sourceEpochs.Length);
            _sourceCount = 0;
            _lastSealedTickFrame = default;
            _hasLastSealedTick = false;
            _requiresNewTimelineEpoch = false;
            _requiredOwnerTimelineEpoch = default;
            _hasRequiredOwnerTimelineEpoch = false;
        }

        private bool HasLiveIntentRuntime()
        {
            return _intentRuntime != null &&
                   !_intentRuntime.IsDisposed &&
                   _intentRuntime.GraphInstanceId == Owner;
        }

        private bool IsLifecycleTransitionBlocked()
        {
            return _deferredLifecycleState != ActorEventInboxState.None ||
                   (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback);
        }

        private void CancelActiveIntentCollection()
        {
            if (HasLiveIntentRuntime() &&
                !_intentRuntime.IsExecutingUserCallback &&
                _intentRuntime.IsCollecting)
            {
                _intentRuntime.CancelCollection();
            }
        }

        private void ReleaseIntentRuntimeBinding()
        {
            IntentFrameRuntime runtime = _intentRuntime;
            _intentRuntime = null;
            runtime?.ReleaseInbox(this);
        }
    }

    internal interface IActorEventInboxLane
    {
        ActorEventManifestEntry Manifest { get; }
        ulong SealedGeneration { get; }
        bool HasSealedBatch { get; }
        bool CanAdvanceGeneration { get; }
        void SealForTick();
        void Clear();
    }

    internal sealed class ActorEventInboxLane<TEvent> : IActorEventInboxLane
        where TEvent : unmanaged
    {
        private EventPacket<TEvent>[] _incoming;
        private EventPacket<TEvent>[] _sealed;
        private int _incomingCount;
        private int _sealedCount;
        private ulong _sealedGeneration;
        private bool _hasSealedBatch;

        public ActorEventInboxLane(
            EventTypeId eventTypeId,
            int capacity,
            bool allowSourceEcho)
        {
            Manifest = new ActorEventManifestEntry(
                eventTypeId,
                typeof(TEvent),
                capacity,
                allowSourceEcho);
            _incoming = new EventPacket<TEvent>[capacity];
            _sealed = new EventPacket<TEvent>[capacity];
        }

        public ActorEventManifestEntry Manifest { get; }
        public ulong SealedGeneration => _sealedGeneration;
        public bool HasSealedBatch => _hasSealedBatch;
        public bool CanAdvanceGeneration => _sealedGeneration != ulong.MaxValue;
        public bool HasIncomingCapacity => _incomingCount < _incoming.Length;
        public int SealedCount => _sealedCount;

        public void Enqueue(in EventPacket<TEvent> packet)
        {
            _incoming[_incomingCount] = packet;
            _incomingCount++;
        }

        public void SealForTick()
        {
            Array.Clear(_sealed, 0, _sealedCount);
            EventPacket<TEvent>[] previousSealed = _sealed;
            _sealed = _incoming;
            _incoming = previousSealed;
            _sealedCount = _incomingCount;
            _incomingCount = 0;
            _sealedGeneration++;
            _hasSealedBatch = true;
        }

        public bool TryReadSealed(int index, out EventPacket<TEvent> packet)
        {
            if (index < 0 || index >= _sealedCount)
            {
                packet = default;
                return false;
            }

            packet = _sealed[index];
            return true;
        }

        public void Clear()
        {
            Array.Clear(_incoming, 0, _incoming.Length);
            Array.Clear(_sealed, 0, _sealed.Length);
            _incomingCount = 0;
            _sealedCount = 0;
            if (_sealedGeneration != ulong.MaxValue)
            {
                _sealedGeneration++;
            }

            _hasSealedBatch = false;
        }
    }

    internal readonly struct InboxSourceEpoch
    {
        public InboxSourceEpoch(
            GraphInstanceId source,
            TimelineEpoch epoch,
            EventSequence lastSequence)
        {
            Source = source;
            Epoch = epoch;
            LastSequence = lastSequence;
        }

        public GraphInstanceId Source { get; }
        public TimelineEpoch Epoch { get; }
        public EventSequence LastSequence { get; }
    }

    internal readonly struct InboxDedupEntry
    {
        public InboxDedupEntry(
            GraphInstanceId source,
            TimelineEpoch epoch,
            EventSequence sequence,
            EventTypeId eventTypeId)
        {
            Source = source;
            Epoch = epoch;
            Sequence = sequence;
            EventTypeId = eventTypeId;
        }

        public GraphInstanceId Source { get; }
        public TimelineEpoch Epoch { get; }
        public EventSequence Sequence { get; }
        public EventTypeId EventTypeId { get; }
        public bool IsValid => Source.IsValid && Sequence.IsValid && EventTypeId.IsValid;
    }
}

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>Records the event types used by compiled State Inbox reads.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class StateInboxEventAttribute : Attribute
    {
        public StateInboxEventAttribute(Type logic, Type payload)
        {
            Logic = logic;
            Payload = payload;
        }

        public Type Logic { get; }
        public Type Payload { get; }
    }

    internal interface IStateEventLane
    {
        void Seal();
        void Clear();
    }

    internal sealed class StateEventInbox
    {
        private readonly Dictionary<Type, IStateEventLane> _lanes = new Dictionary<Type, IStateEventLane>();
        private readonly int _capacity;
        private TickFrame _tick;
        internal StateEventInbox(int capacity) { _capacity = capacity; }
        internal StateEventLane<TEvent> Find<TEvent>() where TEvent : unmanaged =>
            _lanes.TryGetValue(typeof(TEvent), out var lane) ? (StateEventLane<TEvent>)lane : null;
        internal StateEventLane<TEvent> Register<TEvent>() where TEvent : unmanaged
        {
            var lane = Find<TEvent>();
            if (lane != null) return lane;
            lane = new StateEventLane<TEvent>(_capacity);
            _lanes.Add(typeof(TEvent), lane);
            return lane;
        }
        internal void Seal(in TickFrame tick)
        {
            if (_tick == tick) return;
            foreach (var lane in _lanes.Values) lane.Seal();
            _tick = tick;
        }
        internal void Clear()
        {
            foreach (var lane in _lanes.Values) lane.Clear();
            _tick = default;
        }
    }

    internal sealed class StateEventLane<TEvent> : IStateEventLane where TEvent : unmanaged
    {
        private TEvent[] _incoming;
        private TEvent[] _sealed;
        private int _incomingCount;
        internal readonly List<IEventInboxAdapter<TEvent>> Adapters = new List<IEventInboxAdapter<TEvent>>();
        internal int Count { get; private set; }
        internal StateEventLane(int capacity)
        {
            _incoming = new TEvent[capacity];
            _sealed = new TEvent[capacity];
        }
        internal bool TryEnqueue(in TEvent payload)
        {
            if (_incomingCount == _incoming.Length) return false;
            _incoming[_incomingCount++] = payload;
            return true;
        }
        internal TEvent Read(int index) => _sealed[index];
        public void Seal()
        {
            var previous = _sealed;
            _sealed = _incoming;
            _incoming = previous;
            Count = _incomingCount;
            _incomingCount = 0;
        }
        public void Clear() { Count = _incomingCount = 0; }
    }
}
