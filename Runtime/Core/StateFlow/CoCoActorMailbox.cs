using System;

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoEventDeliveryMode
    {
        None = 0,
        Targeted = 1,
        DeclaredBroadcast = 2
    }

    public enum CoCoEventReliability
    {
        None = 0,
        Unreliable = 1,
        Reliable = 2
    }

    public enum CoCoActorEventInboxState
    {
        None = 0,
        Created = 1,
        Running = 2,
        Suspended = 3,
        RewindingOrRestoring = 4,
        Stopped = 5,
        Disposed = 6
    }

    public enum CoCoInboxEnqueueResult
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

    public readonly struct CoCoActorEventEnvelope : IEquatable<CoCoActorEventEnvelope>
    {
        private CoCoActorEventEnvelope(
            CoCoEventTypeId eventTypeId,
            CoCoEventDomainId eventDomainId,
            CoCoGraphInstanceId sourceGraphInstanceId,
            CoCoGraphInstanceId targetGraphInstanceId,
            CoCoTimelineEpoch sourceTimelineEpoch,
            CoCoTimelineTick sourceTick,
            CoCoEventSequence sourceEventSequence,
            CoCoEventDeliveryMode deliveryMode,
            CoCoEventReliability reliability,
            CoCoStableEntityId stableEntityId,
            CoCoActivationId activationId,
            CoCoCorrelationId correlationId)
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

        public CoCoEventTypeId EventTypeId { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public CoCoGraphInstanceId SourceGraphInstanceId { get; }
        public CoCoGraphInstanceId TargetGraphInstanceId { get; }
        public CoCoTimelineEpoch SourceTimelineEpoch { get; }
        public CoCoTimelineTick SourceTick { get; }
        public CoCoEventSequence SourceEventSequence { get; }
        public CoCoEventDeliveryMode DeliveryMode { get; }
        public CoCoEventReliability Reliability { get; }
        public CoCoStableEntityId StableEntityId { get; }
        public CoCoActivationId ActivationId { get; }
        public CoCoCorrelationId CorrelationId { get; }

        public bool IsValid => EventTypeId.IsValid &&
                               EventDomainId.IsValid &&
                               SourceGraphInstanceId.IsValid &&
                               SourceEventSequence.IsValid &&
                               (DeliveryMode == CoCoEventDeliveryMode.Targeted ||
                                DeliveryMode == CoCoEventDeliveryMode.DeclaredBroadcast) &&
                               (Reliability == CoCoEventReliability.Unreliable ||
                                Reliability == CoCoEventReliability.Reliable) &&
                               ((DeliveryMode == CoCoEventDeliveryMode.Targeted &&
                                 TargetGraphInstanceId.IsValid) ||
                                (DeliveryMode == CoCoEventDeliveryMode.DeclaredBroadcast &&
                                 !TargetGraphInstanceId.IsValid));

        public static bool TryCreate(
            CoCoEventTypeId eventTypeId,
            CoCoEventDomainId eventDomainId,
            CoCoGraphInstanceId sourceGraphInstanceId,
            CoCoGraphInstanceId targetGraphInstanceId,
            CoCoTimelineEpoch sourceTimelineEpoch,
            CoCoTimelineTick sourceTick,
            CoCoEventSequence sourceEventSequence,
            CoCoEventDeliveryMode deliveryMode,
            CoCoEventReliability reliability,
            CoCoStableEntityId stableEntityId,
            CoCoActivationId activationId,
            CoCoCorrelationId correlationId,
            out CoCoActorEventEnvelope envelope)
        {
            envelope = new CoCoActorEventEnvelope(
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

        public bool Equals(CoCoActorEventEnvelope other)
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

        public override bool Equals(object obj) => obj is CoCoActorEventEnvelope other && Equals(other);

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

        public static bool operator ==(CoCoActorEventEnvelope left, CoCoActorEventEnvelope right) =>
            left.Equals(right);

        public static bool operator !=(CoCoActorEventEnvelope left, CoCoActorEventEnvelope right) =>
            !left.Equals(right);
    }

    public readonly struct CoCoEventPacket<TEvent>
        where TEvent : unmanaged
    {
        private CoCoEventPacket(CoCoActorEventEnvelope envelope, TEvent payload)
        {
            Envelope = envelope;
            Payload = payload;
        }

        public CoCoActorEventEnvelope Envelope { get; }
        public TEvent Payload { get; }
        public bool IsValid => Envelope.IsValid;

        public static bool TryCreate(
            CoCoActorEventEnvelope envelope,
            in TEvent payload,
            out CoCoEventPacket<TEvent> packet)
        {
            if (!envelope.IsValid)
            {
                packet = default;
                return false;
            }

            packet = new CoCoEventPacket<TEvent>(envelope, payload);
            return true;
        }
    }

    public readonly struct CoCoActorEventManifestEntry : IEquatable<CoCoActorEventManifestEntry>
    {
        internal CoCoActorEventManifestEntry(
            CoCoEventTypeId eventTypeId,
            Type payloadType,
            int capacity,
            bool allowSourceEcho)
        {
            EventTypeId = eventTypeId;
            PayloadType = payloadType;
            Capacity = capacity;
            AllowSourceEcho = allowSourceEcho;
        }

        public CoCoEventTypeId EventTypeId { get; }
        public Type PayloadType { get; }
        public int Capacity { get; }
        public bool AllowSourceEcho { get; }
        public bool IsValid => EventTypeId.IsValid && PayloadType != null && Capacity > 0;

        public bool Equals(CoCoActorEventManifestEntry other)
        {
            return EventTypeId == other.EventTypeId &&
                   PayloadType == other.PayloadType &&
                   Capacity == other.Capacity &&
                   AllowSourceEcho == other.AllowSourceEcho;
        }

        public override bool Equals(object obj) =>
            obj is CoCoActorEventManifestEntry other && Equals(other);

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
            CoCoActorEventManifestEntry left,
            CoCoActorEventManifestEntry right) => left.Equals(right);

        public static bool operator !=(
            CoCoActorEventManifestEntry left,
            CoCoActorEventManifestEntry right) => !left.Equals(right);
    }

    public readonly struct CoCoActorEventLaneHandle<TEvent> :
        IEquatable<CoCoActorEventLaneHandle<TEvent>>
        where TEvent : unmanaged
    {
        private readonly CoCoActorEventInboxCore _inbox;

        internal CoCoActorEventLaneHandle(
            CoCoActorEventInboxCore inbox,
            CoCoGraphInstanceId owner,
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeId,
            int denseIndex)
        {
            _inbox = inbox;
            Owner = owner;
            EventDomainId = eventDomainId;
            EventTypeId = eventTypeId;
            DenseIndex = denseIndex;
        }

        public CoCoGraphInstanceId Owner { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _inbox != null &&
                               Owner.IsValid &&
                               EventDomainId.IsValid &&
                               EventTypeId.IsValid &&
                               DenseIndex >= 0;

        internal bool IsOwnedBy(CoCoActorEventInboxCore inbox) =>
            ReferenceEquals(_inbox, inbox);

        public bool Equals(CoCoActorEventLaneHandle<TEvent> other)
        {
            return ReferenceEquals(_inbox, other._inbox) &&
                   Owner == other.Owner &&
                   EventDomainId == other.EventDomainId &&
                   EventTypeId == other.EventTypeId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) =>
            obj is CoCoActorEventLaneHandle<TEvent> other && Equals(other);

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
            CoCoActorEventLaneHandle<TEvent> left,
            CoCoActorEventLaneHandle<TEvent> right) => left.Equals(right);

        public static bool operator !=(
            CoCoActorEventLaneHandle<TEvent> left,
            CoCoActorEventLaneHandle<TEvent> right) => !left.Equals(right);
    }

    public readonly struct CoCoActorEventSealedBatch<TEvent>
        where TEvent : unmanaged
    {
        private readonly CoCoActorEventInboxCore _inbox;
        private readonly CoCoActorEventLaneHandle<TEvent> _handle;
        private readonly ulong _generation;

        internal CoCoActorEventSealedBatch(
            CoCoActorEventInboxCore inbox,
            CoCoActorEventLaneHandle<TEvent> handle,
            ulong generation)
        {
            _inbox = inbox;
            _handle = handle;
            _generation = generation;
        }

        public bool IsValid => _inbox != null &&
                               _inbox.IsSealedBatchCurrent(_handle, _generation);
        public int Count => _inbox?.GetSealedCount(_handle, _generation) ?? 0;
        internal CoCoGraphInstanceId Owner => _handle.Owner;
        internal CoCoEventDomainId EventDomainId => _handle.EventDomainId;
        internal CoCoEventTypeId EventTypeId => _handle.EventTypeId;

        internal bool TryClaimProjection(object frameOwner, ulong frameGeneration) =>
            _inbox != null &&
            _inbox.TryClaimProjection(_handle, _generation, frameOwner, frameGeneration);

        internal bool IsProjectionRuntime(CoCoIntentFrameRuntime runtime) =>
            _inbox != null && _inbox.IsProjectionRuntime(runtime);

        public bool TryRead(int index, out CoCoEventPacket<TEvent> packet)
        {
            if (_inbox == null)
            {
                packet = default;
                return false;
            }

            return _inbox.TryReadSealed(_handle, _generation, index, out packet);
        }
    }

    public readonly struct CoCoActorInboxCounters
    {
        internal CoCoActorInboxCounters(
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

    public sealed class CoCoActorEventInboxCore : IDisposable
    {
        private readonly ICoCoActorEventInboxLane[] _lanes;
        private readonly CoCoInboxSourceEpoch[] _sourceEpochs;
        private readonly CoCoInboxDedupEntry[] _dedupEntries;
        private int _laneCount;
        private int _sourceCount;
        private int _dedupWriteIndex;
        private CoCoActorEventInboxState _state;
        private CoCoTickFrame _lastSealedTickFrame;
        private bool _hasLastSealedTick;
        private bool _requiresNewTimelineEpoch;
        private CoCoIntentFrameRuntime _intentRuntime;
        private CoCoActorEventInboxState _deferredLifecycleState;
        private ulong _accepted;
        private ulong _duplicate;
        private ulong _rejected;
        private ulong _rewindRestoreDropped;
        private ulong _unreliableOverflowDropped;
        private ulong _reliableOverflowFaults;

        public CoCoActorEventInboxCore(
            CoCoGraphInstanceId owner,
            CoCoEventDomainId eventDomainId,
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
            _lanes = new ICoCoActorEventInboxLane[maxEventTypes];
            _sourceEpochs = new CoCoInboxSourceEpoch[maxSources];
            _dedupEntries = new CoCoInboxDedupEntry[dedupWindowCapacity];
            _state = CoCoActorEventInboxState.Created;
        }

        public CoCoGraphInstanceId Owner { get; }
        public CoCoEventDomainId EventDomainId { get; }
        public int ManifestCount => _laneCount;
        public CoCoActorEventInboxState State => _state;
        public bool HasReliableOverflowFault => _reliableOverflowFaults != 0UL;
        public CoCoActorInboxCounters Counters => new CoCoActorInboxCounters(
            _accepted,
            _duplicate,
            _rejected,
            _rewindRestoreDropped,
            _unreliableOverflowDropped,
            _reliableOverflowFaults);

        public bool TryRegisterLane<TEvent>(
            CoCoEventTypeId eventTypeId,
            int capacity,
            bool allowSourceEcho,
            out CoCoActorEventLaneHandle<TEvent> handle,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (_state != CoCoActorEventInboxState.Created ||
                _deferredLifecycleState != CoCoActorEventInboxState.None)
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

            handle = new CoCoActorEventLaneHandle<TEvent>(
                this,
                Owner,
                EventDomainId,
                eventTypeId,
                _laneCount);
            _lanes[_laneCount] = new CoCoActorEventInboxLane<TEvent>(
                eventTypeId,
                capacity,
                allowSourceEcho);
            _laneCount++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryBindIntentRuntime(
            CoCoIntentFrameRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (_state != CoCoActorEventInboxState.Created ||
                _deferredLifecycleState != CoCoActorEventInboxState.None ||
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

        public bool TryGetManifestEntry(int denseIndex, out CoCoActorEventManifestEntry entry)
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
            if (_state != CoCoActorEventInboxState.Created ||
                _deferredLifecycleState != CoCoActorEventInboxState.None)
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

            if (!MatchesIntentAdapterManifest())
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.InvalidEventPacket,
                    "Inbox lanes must exactly match the bound Intent runtime adapter manifest.");
                return false;
            }

            _state = CoCoActorEventInboxState.Running;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Suspend()
        {
            if (_state != CoCoActorEventInboxState.Running ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked())
            {
                return false;
            }

            _state = CoCoActorEventInboxState.Suspended;
            return true;
        }

        public bool Resume()
        {
            if (_state != CoCoActorEventInboxState.Suspended ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked())
            {
                return false;
            }

            _state = CoCoActorEventInboxState.Running;
            return true;
        }

        public bool BeginRewindOrRestore()
        {
            if ((_state != CoCoActorEventInboxState.Running &&
                 _state != CoCoActorEventInboxState.Suspended) ||
                !HasLiveIntentRuntime() ||
                IsLifecycleTransitionBlocked() ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            ClearQueuedState();
            _state = CoCoActorEventInboxState.RewindingOrRestoring;
            return true;
        }

        public bool ResumeAfterTimelineReset()
        {
            if (_state != CoCoActorEventInboxState.RewindingOrRestoring ||
                !HasLiveIntentRuntime() ||
                IsLifecycleTransitionBlocked() ||
                !_intentRuntime.TryResetForTimelineChange())
            {
                return false;
            }

            ClearQueuedState();
            _requiresNewTimelineEpoch = true;
            _state = CoCoActorEventInboxState.Running;
            return true;
        }

        public void Stop()
        {
            if (_state == CoCoActorEventInboxState.Disposed ||
                _deferredLifecycleState == CoCoActorEventInboxState.Disposed)
            {
                return;
            }

            if (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback)
            {
                if (_deferredLifecycleState == CoCoActorEventInboxState.None)
                {
                    _deferredLifecycleState = CoCoActorEventInboxState.Stopped;
                }

                return;
            }

            CancelActiveIntentCollection();

            ClearAllRuntimeState();
            ReleaseIntentRuntimeBinding();
            _deferredLifecycleState = CoCoActorEventInboxState.None;
            _state = CoCoActorEventInboxState.Stopped;
        }

        public void Dispose()
        {
            if (_state == CoCoActorEventInboxState.Disposed)
            {
                return;
            }

            if (HasLiveIntentRuntime() && _intentRuntime.IsExecutingUserCallback)
            {
                _deferredLifecycleState = CoCoActorEventInboxState.Disposed;
                return;
            }

            CancelActiveIntentCollection();

            ClearAllRuntimeState();
            ReleaseIntentRuntimeBinding();
            _deferredLifecycleState = CoCoActorEventInboxState.None;
            _state = CoCoActorEventInboxState.Disposed;
        }

        public CoCoInboxEnqueueResult TryEnqueue<TEvent>(
            CoCoActorEventLaneHandle<TEvent> handle,
            in CoCoEventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (_state == CoCoActorEventInboxState.RewindingOrRestoring)
            {
                _rewindRestoreDropped++;
                _rejected++;
                return CoCoInboxEnqueueResult.RewindOrRestoreDropped;
            }

            if (_state != CoCoActorEventInboxState.Running &&
                _state != CoCoActorEventInboxState.Suspended)
            {
                _rejected++;
                return CoCoInboxEnqueueResult.MailboxUnavailable;
            }

            if (_deferredLifecycleState != CoCoActorEventInboxState.None)
            {
                _rejected++;
                return CoCoInboxEnqueueResult.MailboxUnavailable;
            }

            if (!HasLiveIntentRuntime())
            {
                _rejected++;
                return CoCoInboxEnqueueResult.MailboxUnavailable;
            }

            if (!packet.IsValid || !Matches(handle))
            {
                _rejected++;
                return CoCoInboxEnqueueResult.InvalidPacket;
            }

            CoCoActorEventEnvelope envelope = packet.Envelope;
            if (envelope.EventDomainId != EventDomainId)
            {
                _rejected++;
                return CoCoInboxEnqueueResult.EventDomainMismatch;
            }

            var lane = _lanes[handle.DenseIndex] as CoCoActorEventInboxLane<TEvent>;
            if (lane == null || envelope.EventTypeId != handle.EventTypeId)
            {
                _rejected++;
                return CoCoInboxEnqueueResult.UndeclaredEventType;
            }

            if (envelope.DeliveryMode == CoCoEventDeliveryMode.Targeted)
            {
                if (envelope.TargetGraphInstanceId != Owner)
                {
                    _rejected++;
                    return CoCoInboxEnqueueResult.EventTargetMismatch;
                }
            }
            else
            {
                if (envelope.TargetGraphInstanceId.IsValid)
                {
                    _rejected++;
                    return CoCoInboxEnqueueResult.EventTargetMismatch;
                }

                if (!lane.Manifest.AllowSourceEcho && envelope.SourceGraphInstanceId == Owner)
                {
                    _rejected++;
                    return CoCoInboxEnqueueResult.SourceEchoRejected;
                }
            }

            CoCoInboxEnqueueResult sourceResult = InspectSourceEpoch(
                envelope.SourceGraphInstanceId,
                envelope.SourceTimelineEpoch,
                out int sourceIndex,
                out bool sourceIsNew,
                out bool epochIsNew);
            if (sourceResult != CoCoInboxEnqueueResult.Accepted)
            {
                if (sourceResult == CoCoInboxEnqueueResult.SourceWindowFull)
                {
                    return RejectOverflow(envelope.Reliability);
                }

                _rejected++;
                return sourceResult;
            }

            CoCoInboxEnqueueResult dedupResult = InspectDedup(envelope);
            if (dedupResult != CoCoInboxEnqueueResult.Accepted)
            {
                if (dedupResult == CoCoInboxEnqueueResult.Duplicate)
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
                return CoCoInboxEnqueueResult.EventSequenceConflict;
            }

            if (!lane.HasIncomingCapacity)
            {
                return RejectOverflow(envelope.Reliability);
            }

            if (sourceIsNew)
            {
                _sourceEpochs[sourceIndex] = new CoCoInboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
                _sourceCount++;
            }
            else if (epochIsNew)
            {
                ClearDedupForSource(envelope.SourceGraphInstanceId);
                _sourceEpochs[sourceIndex] = new CoCoInboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
            }
            else
            {
                _sourceEpochs[sourceIndex] = new CoCoInboxSourceEpoch(
                    envelope.SourceGraphInstanceId,
                    envelope.SourceTimelineEpoch,
                    envelope.SourceEventSequence);
            }

            lane.Enqueue(packet);
            RecordDedup(envelope);
            _accepted++;
            return CoCoInboxEnqueueResult.Accepted;
        }

        public bool SealForTick(in CoCoTickFrame tickFrame)
        {
            if (_state != CoCoActorEventInboxState.Running ||
                !HasLiveIntentRuntime() ||
                _intentRuntime.IsCollecting ||
                IsLifecycleTransitionBlocked() ||
                !tickFrame.IsValid ||
                (_requiresNewTimelineEpoch &&
                 _hasLastSealedTick &&
                 tickFrame.TimelineEpoch.Value <=
                 _lastSealedTickFrame.TimelineEpoch.Value) ||
                (_hasLastSealedTick &&
                 !CoCoStateFlowTickOrder.IsStrictlyAfter(
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

        public int GetSealedCount<TEvent>(CoCoActorEventLaneHandle<TEvent> handle)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                return 0;
            }

            return (_lanes[handle.DenseIndex] as CoCoActorEventInboxLane<TEvent>)?.SealedCount ?? 0;
        }

        public bool TryReadSealed<TEvent>(
            CoCoActorEventLaneHandle<TEvent> handle,
            int index,
            out CoCoEventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                packet = default;
                return false;
            }

            var lane = _lanes[handle.DenseIndex] as CoCoActorEventInboxLane<TEvent>;
            if (lane == null)
            {
                packet = default;
                return false;
            }

            return lane.TryReadSealed(index, out packet);
        }

        public bool TryGetSealedBatch<TEvent>(
            CoCoActorEventLaneHandle<TEvent> handle,
            out CoCoActorEventSealedBatch<TEvent> batch)
            where TEvent : unmanaged
        {
            if (!Matches(handle))
            {
                batch = default;
                return false;
            }

            ICoCoActorEventInboxLane lane = _lanes[handle.DenseIndex];
            ulong generation = lane.SealedGeneration;
            if (!lane.HasSealedBatch)
            {
                batch = default;
                return false;
            }

            batch = new CoCoActorEventSealedBatch<TEvent>(this, handle, generation);
            return true;
        }

        internal int GetSealedCount<TEvent>(
            CoCoActorEventLaneHandle<TEvent> handle,
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
            CoCoActorEventLaneHandle<TEvent> handle,
            ulong generation)
            where TEvent : unmanaged
        {
            return generation != 0UL && Matches(handle) &&
                   _lanes[handle.DenseIndex].HasSealedBatch &&
                   _lanes[handle.DenseIndex].SealedGeneration == generation;
        }

        internal bool TryClaimProjection<TEvent>(
            CoCoActorEventLaneHandle<TEvent> handle,
            ulong generation,
            object frameOwner,
            ulong frameGeneration)
            where TEvent : unmanaged
        {
            if (!Matches(handle) ||
                _intentRuntime == null ||
                !ReferenceEquals(_intentRuntime, frameOwner) ||
                _intentRuntime.IsDisposed)
            {
                return false;
            }

            return _lanes[handle.DenseIndex].TryClaimProjection(
                generation,
                frameOwner,
                frameGeneration);
        }

        internal bool IsProjectionRuntime(CoCoIntentFrameRuntime runtime) =>
            runtime != null &&
            _intentRuntime != null &&
            !_intentRuntime.IsDisposed &&
            ReferenceEquals(_intentRuntime, runtime);

        internal void OnIntentRuntimeDisposed(CoCoIntentFrameRuntime runtime)
        {
            if (!ReferenceEquals(_intentRuntime, runtime))
            {
                return;
            }

            CoCoActorEventInboxState deferredState = _deferredLifecycleState;
            _deferredLifecycleState = CoCoActorEventInboxState.None;
            _intentRuntime = null;
            if (_state == CoCoActorEventInboxState.Created)
            {
                if (deferredState == CoCoActorEventInboxState.Disposed)
                {
                    ClearAllRuntimeState();
                    _state = CoCoActorEventInboxState.Disposed;
                }

                return;
            }

            if (_state != CoCoActorEventInboxState.Disposed)
            {
                ClearAllRuntimeState();
                _state = deferredState == CoCoActorEventInboxState.Disposed
                    ? CoCoActorEventInboxState.Disposed
                    : CoCoActorEventInboxState.Stopped;
            }
        }

        internal bool HasDeferredLifecycle(CoCoIntentFrameRuntime runtime)
        {
            return runtime != null &&
                   ReferenceEquals(_intentRuntime, runtime) &&
                   _deferredLifecycleState != CoCoActorEventInboxState.None;
        }

        internal void CompleteDeferredLifecycle(CoCoIntentFrameRuntime runtime)
        {
            if (!HasDeferredLifecycle(runtime) || runtime.IsExecutingUserCallback)
            {
                return;
            }

            CoCoActorEventInboxState targetState = _deferredLifecycleState;
            _deferredLifecycleState = CoCoActorEventInboxState.None;
            if (targetState == CoCoActorEventInboxState.Disposed)
            {
                Dispose();
            }
            else
            {
                Stop();
            }
        }

        internal void ReleaseProjectionClaims(
            CoCoIntentFrameRuntime runtime,
            ulong frameGeneration)
        {
            if (runtime == null || frameGeneration == 0UL || !ReferenceEquals(_intentRuntime, runtime))
            {
                return;
            }

            for (int index = 0; index < _laneCount; index++)
            {
                _lanes[index].ReleaseProjection(runtime, frameGeneration);
            }
        }

        internal bool TryReadSealed<TEvent>(
            CoCoActorEventLaneHandle<TEvent> handle,
            ulong generation,
            int index,
            out CoCoEventPacket<TEvent> packet)
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

        private bool Matches<TEvent>(CoCoActorEventLaneHandle<TEvent> handle)
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

            CoCoActorEventManifestEntry manifest = _lanes[handle.DenseIndex].Manifest;
            return manifest.EventTypeId == handle.EventTypeId;
        }

        private CoCoInboxEnqueueResult InspectSourceEpoch(
            CoCoGraphInstanceId source,
            CoCoTimelineEpoch epoch,
            out int sourceIndex,
            out bool sourceIsNew,
            out bool epochIsNew)
        {
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
                    ? CoCoInboxEnqueueResult.StaleTimelineEpoch
                    : CoCoInboxEnqueueResult.Accepted;
            }

            if (_sourceCount >= _sourceEpochs.Length)
            {
                sourceIndex = -1;
                sourceIsNew = false;
                epochIsNew = false;
                return CoCoInboxEnqueueResult.SourceWindowFull;
            }

            sourceIndex = _sourceCount;
            sourceIsNew = true;
            epochIsNew = false;
            return CoCoInboxEnqueueResult.Accepted;
        }

        private CoCoInboxEnqueueResult InspectDedup(in CoCoActorEventEnvelope envelope)
        {
            for (int index = 0; index < _dedupEntries.Length; index++)
            {
                CoCoInboxDedupEntry entry = _dedupEntries[index];
                if (!entry.IsValid ||
                    entry.Source != envelope.SourceGraphInstanceId ||
                    entry.Epoch != envelope.SourceTimelineEpoch ||
                    entry.Sequence != envelope.SourceEventSequence)
                {
                    continue;
                }

                return entry.EventTypeId == envelope.EventTypeId
                    ? CoCoInboxEnqueueResult.Duplicate
                    : CoCoInboxEnqueueResult.EventSequenceConflict;
            }

            return CoCoInboxEnqueueResult.Accepted;
        }

        private void RecordDedup(in CoCoActorEventEnvelope envelope)
        {
            _dedupEntries[_dedupWriteIndex] = new CoCoInboxDedupEntry(
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

        private void ClearDedupForSource(CoCoGraphInstanceId source)
        {
            for (int index = 0; index < _dedupEntries.Length; index++)
            {
                if (_dedupEntries[index].Source == source)
                {
                    _dedupEntries[index] = default;
                }
            }
        }

        private CoCoInboxEnqueueResult RejectOverflow(CoCoEventReliability reliability)
        {
            _rejected++;
            if (reliability == CoCoEventReliability.Reliable)
            {
                _reliableOverflowFaults++;
                return CoCoInboxEnqueueResult.ReliableOverflowFaultRequired;
            }

            _unreliableOverflowDropped++;
            return CoCoInboxEnqueueResult.UnreliableOverflowDropped;
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
        }

        private bool HasLiveIntentRuntime()
        {
            return _intentRuntime != null &&
                   !_intentRuntime.IsDisposed &&
                   _intentRuntime.GraphInstanceId == Owner;
        }

        private bool IsLifecycleTransitionBlocked()
        {
            return _deferredLifecycleState != CoCoActorEventInboxState.None ||
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

        private bool MatchesIntentAdapterManifest()
        {
            if (!HasLiveIntentRuntime() ||
                _intentRuntime.EventAdapterManifestCount != _laneCount)
            {
                return false;
            }

            for (int index = 0; index < _laneCount; index++)
            {
                CoCoActorEventManifestEntry manifest = _lanes[index].Manifest;
                if (!_intentRuntime.MatchesEventAdapterManifest(
                        EventDomainId,
                        manifest.EventTypeId,
                        manifest.PayloadType,
                        manifest.Capacity))
                {
                    return false;
                }
            }

            return true;
        }

        private void ReleaseIntentRuntimeBinding()
        {
            CoCoIntentFrameRuntime runtime = _intentRuntime;
            _intentRuntime = null;
            runtime?.ReleaseInbox(this);
        }
    }

    internal interface ICoCoActorEventInboxLane
    {
        CoCoActorEventManifestEntry Manifest { get; }
        ulong SealedGeneration { get; }
        bool HasSealedBatch { get; }
        bool CanAdvanceGeneration { get; }
        void SealForTick();
        bool TryClaimProjection(ulong generation, object frameOwner, ulong frameGeneration);
        void ReleaseProjection(object frameOwner, ulong frameGeneration);
        void Clear();
    }

    internal sealed class CoCoActorEventInboxLane<TEvent> : ICoCoActorEventInboxLane
        where TEvent : unmanaged
    {
        private CoCoEventPacket<TEvent>[] _incoming;
        private CoCoEventPacket<TEvent>[] _sealed;
        private int _incomingCount;
        private int _sealedCount;
        private ulong _sealedGeneration;
        private bool _hasSealedBatch;
        private bool _projectionClaimed;
        private object _projectionFrameOwner;
        private ulong _projectionFrameGeneration;

        public CoCoActorEventInboxLane(
            CoCoEventTypeId eventTypeId,
            int capacity,
            bool allowSourceEcho)
        {
            Manifest = new CoCoActorEventManifestEntry(
                eventTypeId,
                typeof(TEvent),
                capacity,
                allowSourceEcho);
            _incoming = new CoCoEventPacket<TEvent>[capacity];
            _sealed = new CoCoEventPacket<TEvent>[capacity];
        }

        public CoCoActorEventManifestEntry Manifest { get; }
        public ulong SealedGeneration => _sealedGeneration;
        public bool HasSealedBatch => _hasSealedBatch;
        public bool CanAdvanceGeneration => _sealedGeneration != ulong.MaxValue;
        public bool HasIncomingCapacity => _incomingCount < _incoming.Length;
        public int SealedCount => _sealedCount;

        public void Enqueue(in CoCoEventPacket<TEvent> packet)
        {
            _incoming[_incomingCount] = packet;
            _incomingCount++;
        }

        public void SealForTick()
        {
            Array.Clear(_sealed, 0, _sealedCount);
            CoCoEventPacket<TEvent>[] previousSealed = _sealed;
            _sealed = _incoming;
            _incoming = previousSealed;
            _sealedCount = _incomingCount;
            _incomingCount = 0;
            _sealedGeneration++;
            _hasSealedBatch = true;
            _projectionClaimed = false;
            _projectionFrameOwner = null;
            _projectionFrameGeneration = 0UL;
        }

        public bool TryClaimProjection(
            ulong generation,
            object frameOwner,
            ulong frameGeneration)
        {
            if (!_hasSealedBatch ||
                generation == 0UL ||
                generation != _sealedGeneration ||
                frameOwner == null ||
                frameGeneration == 0UL)
            {
                return false;
            }

            if (_projectionClaimed)
            {
                return ReferenceEquals(_projectionFrameOwner, frameOwner) &&
                       _projectionFrameGeneration == frameGeneration;
            }

            _projectionClaimed = true;
            _projectionFrameOwner = frameOwner;
            _projectionFrameGeneration = frameGeneration;
            return true;
        }

        public void ReleaseProjection(object frameOwner, ulong frameGeneration)
        {
            if (_projectionClaimed &&
                ReferenceEquals(_projectionFrameOwner, frameOwner) &&
                _projectionFrameGeneration == frameGeneration)
            {
                _projectionClaimed = false;
                _projectionFrameOwner = null;
                _projectionFrameGeneration = 0UL;
            }
        }

        public bool TryReadSealed(int index, out CoCoEventPacket<TEvent> packet)
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
            _projectionClaimed = false;
            _projectionFrameOwner = null;
            _projectionFrameGeneration = 0UL;
        }
    }

    internal readonly struct CoCoInboxSourceEpoch
    {
        public CoCoInboxSourceEpoch(
            CoCoGraphInstanceId source,
            CoCoTimelineEpoch epoch,
            CoCoEventSequence lastSequence)
        {
            Source = source;
            Epoch = epoch;
            LastSequence = lastSequence;
        }

        public CoCoGraphInstanceId Source { get; }
        public CoCoTimelineEpoch Epoch { get; }
        public CoCoEventSequence LastSequence { get; }
    }

    internal readonly struct CoCoInboxDedupEntry
    {
        public CoCoInboxDedupEntry(
            CoCoGraphInstanceId source,
            CoCoTimelineEpoch epoch,
            CoCoEventSequence sequence,
            CoCoEventTypeId eventTypeId)
        {
            Source = source;
            Epoch = epoch;
            Sequence = sequence;
            EventTypeId = eventTypeId;
        }

        public CoCoGraphInstanceId Source { get; }
        public CoCoTimelineEpoch Epoch { get; }
        public CoCoEventSequence Sequence { get; }
        public CoCoEventTypeId EventTypeId { get; }
        public bool IsValid => Source.IsValid && Sequence.IsValid && EventTypeId.IsValid;
    }
}
