using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public interface ICoCoIntentReducer<TIntent>
        where TIntent : unmanaged
    {
        TIntent Reduce(in TIntent current, in TIntent candidate);
    }

    public interface ICoCoIntentFrameSource<TIntent>
        where TIntent : unmanaged
    {
        bool TrySample(in CoCoTickFrame tickFrame, out TIntent intent);
    }

    public interface ICoCoEventToIntentAdapter<TEvent, TIntent>
        where TEvent : unmanaged
        where TIntent : unmanaged
    {
        bool TryProject(in CoCoEventPacket<TEvent> packet, out TIntent intent);
    }

    public interface ICoCoIntentFrame
    {
        CoCoGraphInstanceId GraphInstanceId { get; }
        CoCoStateFlowFrameHeader Header { get; }
        CoCoFrameLayoutId LayoutId { get; }
        bool IsFrozen { get; }

        bool TryGet<TIntent>(
            CoCoIntentHandle<TIntent> handle,
            out TIntent value)
            where TIntent : unmanaged;

        bool IsPresent<TIntent>(CoCoIntentHandle<TIntent> handle)
            where TIntent : unmanaged;
    }

    public enum CoCoIntentContributionResult
    {
        None = 0,
        Accepted = 1,
        ArbiterNotCollecting = 2,
        InvalidHandle = 3,
        DuplicateContribution = 4,
        CapacityExceeded = 5
    }

    public enum CoCoIntentSourceSampleResult
    {
        None = 0,
        Contributed = 1,
        NoValue = 2,
        AlreadySampled = 3,
        ArbiterNotCollecting = 4,
        InvalidBinding = 5,
        ContributionRejected = 6
    }

    public enum CoCoIntentEventProjectionResult
    {
        None = 0,
        Contributed = 1,
        NoValue = 2,
        AlreadyProjected = 3,
        ArbiterNotCollecting = 4,
        InvalidBinding = 5,
        InvalidBatch = 6,
        InvalidPacket = 7,
        DuplicateContribution = 8,
        CapacityExceeded = 9
    }

    public readonly struct CoCoIntentDescriptor : IEquatable<CoCoIntentDescriptor>
    {
        internal CoCoIntentDescriptor(
            CoCoIntentId intentId,
            Type valueType,
            int denseIndex,
            int maxContributions)
        {
            IntentId = intentId;
            ValueType = valueType;
            DenseIndex = denseIndex;
            MaxContributions = maxContributions;
        }

        public CoCoIntentId IntentId { get; }
        public Type ValueType { get; }
        public int DenseIndex { get; }
        public int MaxContributions { get; }
        public bool IsValid => IntentId.IsValid &&
                               ValueType != null &&
                               DenseIndex >= 0 &&
                               MaxContributions > 0;

        public bool Equals(CoCoIntentDescriptor other)
        {
            return IntentId == other.IntentId &&
                   ValueType == other.ValueType &&
                   DenseIndex == other.DenseIndex &&
                   MaxContributions == other.MaxContributions;
        }

        public override bool Equals(object obj) => obj is CoCoIntentDescriptor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = IntentId.GetHashCode();
                hashCode = (hashCode * 397) ^ (ValueType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ DenseIndex;
                hashCode = (hashCode * 397) ^ MaxContributions;
                return hashCode;
            }
        }

        public static bool operator ==(CoCoIntentDescriptor left, CoCoIntentDescriptor right) => left.Equals(right);
        public static bool operator !=(CoCoIntentDescriptor left, CoCoIntentDescriptor right) => !left.Equals(right);
    }

    public readonly struct CoCoIntentHandle<TIntent> : IEquatable<CoCoIntentHandle<TIntent>>
        where TIntent : unmanaged
    {
        private readonly CoCoIntentFrameLayout _owner;

        internal CoCoIntentHandle(
            CoCoIntentFrameLayout owner,
            CoCoIntentId intentId,
            int denseIndex)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            IntentId = intentId;
            DenseIndex = denseIndex;
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public CoCoIntentId IntentId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               IntentId.IsValid &&
                               DenseIndex >= 0;

        internal bool IsOwnedBy(CoCoIntentFrameLayout owner) => ReferenceEquals(_owner, owner);

        public bool Equals(CoCoIntentHandle<TIntent> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   IntentId == other.IntentId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) => obj is CoCoIntentHandle<TIntent> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ IntentId.GetHashCode();
                hashCode = (hashCode * 397) ^ DenseIndex;
                return hashCode;
            }
        }

        public static bool operator ==(CoCoIntentHandle<TIntent> left, CoCoIntentHandle<TIntent> right) =>
            left.Equals(right);

        public static bool operator !=(CoCoIntentHandle<TIntent> left, CoCoIntentHandle<TIntent> right) =>
            !left.Equals(right);
    }

    public readonly struct CoCoIntentSourceRequirement<TIntent> :
        IEquatable<CoCoIntentSourceRequirement<TIntent>>
        where TIntent : unmanaged
    {
        private CoCoIntentSourceRequirement(
            CoCoIntentHandle<TIntent> handle,
            int priority)
        {
            Handle = handle;
            Priority = priority;
        }

        public CoCoIntentHandle<TIntent> Handle { get; }
        public int Priority { get; }
        public bool IsValid => Handle.IsValid;

        public static bool TryCreate(
            CoCoIntentHandle<TIntent> handle,
            int priority,
            out CoCoIntentSourceRequirement<TIntent> requirement)
        {
            if (!handle.IsValid)
            {
                requirement = default;
                return false;
            }

            requirement = new CoCoIntentSourceRequirement<TIntent>(handle, priority);
            return true;
        }

        public bool Equals(CoCoIntentSourceRequirement<TIntent> other)
        {
            return Handle == other.Handle && Priority == other.Priority;
        }

        public override bool Equals(object obj) =>
            obj is CoCoIntentSourceRequirement<TIntent> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Handle.GetHashCode() * 397) ^ Priority;
            }
        }

        public static bool operator ==(
            CoCoIntentSourceRequirement<TIntent> left,
            CoCoIntentSourceRequirement<TIntent> right) => left.Equals(right);

        public static bool operator !=(
            CoCoIntentSourceRequirement<TIntent> left,
            CoCoIntentSourceRequirement<TIntent> right) => !left.Equals(right);
    }

    public sealed class CoCoIntentSourceBinding<TIntent>
        where TIntent : unmanaged
    {
        private readonly CoCoIntentFrameRuntime _runtime;
        private readonly ICoCoIntentFrameSource<TIntent> _source;
        private ulong _lastSampledFrameGeneration;

        internal CoCoIntentSourceBinding(
            CoCoIntentFrameRuntime runtime,
            CoCoIntentSourceRequirement<TIntent> requirement,
            ICoCoIntentFrameSource<TIntent> source,
            int registrationOrder,
            int bindingToken)
        {
            _runtime = runtime;
            Requirement = requirement;
            _source = source;
            RegistrationOrder = registrationOrder;
            BindingToken = bindingToken;
        }

        public CoCoGraphInstanceId GraphInstanceId => _runtime.GraphInstanceId;
        public CoCoIntentSourceRequirement<TIntent> Requirement { get; }
        public int RegistrationOrder { get; }
        public bool IsValid => _runtime != null &&
                               !_runtime.IsDisposed &&
                               Requirement.IsValid &&
                               _source != null &&
                               RegistrationOrder >= 0 &&
                               BindingToken > 0;

        internal int BindingToken { get; }

        internal bool IsOwnedBy(CoCoIntentFrameRuntime runtime) => ReferenceEquals(_runtime, runtime);

        internal bool TrySample(
            ulong frameGeneration,
            in CoCoTickFrame tickFrame,
            out TIntent intent,
            out bool hasValue)
        {
            if (_lastSampledFrameGeneration == frameGeneration)
            {
                intent = default;
                hasValue = false;
                return false;
            }

            _lastSampledFrameGeneration = frameGeneration;
            hasValue = _source.TrySample(tickFrame, out intent);
            return true;
        }
    }

    public sealed class CoCoEventToIntentBinding<TEvent, TIntent>
        where TEvent : unmanaged
        where TIntent : unmanaged
    {
        private readonly CoCoIntentFrameRuntime _runtime;
        private readonly ICoCoEventToIntentAdapter<TEvent, TIntent> _adapter;
        private readonly CoCoIntentContribution<TIntent>[] _scratch;
        private ulong _lastProjectedFrameGeneration;

        internal CoCoEventToIntentBinding(
            CoCoIntentFrameRuntime runtime,
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeId,
            CoCoIntentSourceRequirement<TIntent> requirement,
            ICoCoEventToIntentAdapter<TEvent, TIntent> adapter,
            int registrationOrder,
            int bindingToken,
            int projectionCapacity)
        {
            _runtime = runtime;
            EventDomainId = eventDomainId;
            EventTypeId = eventTypeId;
            Requirement = requirement;
            _adapter = adapter;
            RegistrationOrder = registrationOrder;
            BindingToken = bindingToken;
            _scratch = new CoCoIntentContribution<TIntent>[projectionCapacity];
        }

        public CoCoGraphInstanceId GraphInstanceId => _runtime.GraphInstanceId;
        public CoCoEventDomainId EventDomainId { get; }
        public CoCoEventTypeId EventTypeId { get; }
        public CoCoIntentSourceRequirement<TIntent> Requirement { get; }
        public int RegistrationOrder { get; }
        public int ProjectionCapacity => _scratch.Length;
        public bool IsValid => _runtime != null &&
                               !_runtime.IsDisposed &&
                               EventDomainId.IsValid &&
                               EventTypeId.IsValid &&
                               Requirement.IsValid &&
                               _adapter != null &&
                               RegistrationOrder >= 0 &&
                               BindingToken > 0 &&
                               _scratch.Length > 0;

        internal int BindingToken { get; }
        internal CoCoIntentContribution<TIntent>[] Scratch => _scratch;

        internal bool IsOwnedBy(CoCoIntentFrameRuntime runtime) => ReferenceEquals(_runtime, runtime);

        internal bool TryClaimProjection(ulong frameGeneration)
        {
            if (frameGeneration == 0UL || _lastProjectedFrameGeneration == frameGeneration)
            {
                return false;
            }

            _lastProjectedFrameGeneration = frameGeneration;
            return true;
        }

        internal bool TryProject(in CoCoEventPacket<TEvent> packet, out TIntent intent)
        {
            return _adapter.TryProject(packet, out intent);
        }
    }

    internal readonly struct CoCoIntentContribution<TIntent>
        where TIntent : unmanaged
    {
        public CoCoIntentContribution(
            CoCoIntentHandle<TIntent> handle,
            int priority,
            int registrationOrder,
            int bindingToken,
            CoCoGraphInstanceId sourceGraphInstanceId,
            CoCoTimelineEpoch sourceTimelineEpoch,
            CoCoEventSequence eventSequence,
            in TIntent value)
        {
            Handle = handle;
            Priority = priority;
            RegistrationOrder = registrationOrder;
            BindingToken = bindingToken;
            SourceGraphInstanceId = sourceGraphInstanceId;
            SourceTimelineEpoch = sourceTimelineEpoch;
            EventSequence = eventSequence;
            Value = value;
        }

        public CoCoIntentHandle<TIntent> Handle { get; }
        public int Priority { get; }
        public int RegistrationOrder { get; }
        public int BindingToken { get; }
        public CoCoGraphInstanceId SourceGraphInstanceId { get; }
        public CoCoTimelineEpoch SourceTimelineEpoch { get; }
        public CoCoEventSequence EventSequence { get; }
        public TIntent Value { get; }
        public bool IsValid => Handle.IsValid &&
                               RegistrationOrder >= 0 &&
                               BindingToken > 0 &&
                               SourceGraphInstanceId.IsValid;
    }

    internal sealed class CoCoIntentFrame : ICoCoIntentFrame
    {
        private readonly CoCoIntentFrameLayout _layout;
        private readonly ICoCoIntentFrameSlot[] _slots;
        private CoCoStateFlowFrameHeader _header;
        private bool _isFrozen;

        public CoCoIntentFrame(
            CoCoIntentFrameLayout layout,
            CoCoGraphInstanceId graphInstanceId)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            GraphInstanceId = graphInstanceId;
            _slots = layout.CreateFrameSlots();
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoStateFlowFrameHeader Header => _header;
        public CoCoFrameLayoutId LayoutId => _layout.LayoutId;
        public bool IsFrozen => _isFrozen;

        public bool TryGet<TIntent>(
            CoCoIntentHandle<TIntent> handle,
            out TIntent value)
            where TIntent : unmanaged
        {
            if (!_isFrozen || !_layout.Matches(handle))
            {
                value = default;
                return false;
            }

            var slot = _slots[handle.DenseIndex] as CoCoIntentFrameSlot<TIntent>;
            if (slot == null || !slot.IsPresent)
            {
                value = default;
                return false;
            }

            value = slot.Value;
            return true;
        }

        public bool IsPresent<TIntent>(CoCoIntentHandle<TIntent> handle)
            where TIntent : unmanaged
        {
            return _isFrozen &&
                   _layout.Matches(handle) &&
                   _slots[handle.DenseIndex] is CoCoIntentFrameSlot<TIntent> slot &&
                   slot.IsPresent;
        }

        internal void Prepare(in CoCoStateFlowFrameHeader header)
        {
            for (int index = 0; index < _slots.Length; index++)
            {
                _slots[index].Clear();
            }

            _header = header;
            _isFrozen = false;
        }

        internal ICoCoIntentFrameSlot GetSlot(int denseIndex) => _slots[denseIndex];

        internal void Seal()
        {
            _isFrozen = true;
        }
    }

    public sealed class CoCoIntentFrameLayout
    {
        private readonly ICoCoIntentDefinition[] _definitions;
        private readonly Dictionary<CoCoGraphInstanceId, CoCoIntentFrameRuntime> _runtimes =
            new Dictionary<CoCoGraphInstanceId, CoCoIntentFrameRuntime>();
        private int _count;
        private bool _isFrozen;

        public CoCoIntentFrameLayout(CoCoFrameLayoutId layoutId, int maxIntentCount)
        {
            if (!layoutId.IsValid)
            {
                throw new ArgumentException("Intent layout id must be valid.", nameof(layoutId));
            }

            if (maxIntentCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxIntentCount),
                    maxIntentCount,
                    "Intent layout capacity must be positive.");
            }

            LayoutId = layoutId;
            _definitions = new ICoCoIntentDefinition[maxIntentCount];
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public int Count => _count;
        public int Capacity => _definitions.Length;
        public bool IsFrozen => _isFrozen;

        public bool TryRegister<TIntent>(
            CoCoIntentId intentId,
            int maxContributions,
            ICoCoIntentReducer<TIntent> reducer,
            out CoCoIntentHandle<TIntent> handle,
            out CoCoDiagnostic diagnostic)
            where TIntent : unmanaged
        {
            if (_isFrozen)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Intent layout is already frozen.");
                return false;
            }

            if (!intentId.IsValid || maxContributions <= 0)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "Intent id and contribution capacity must be valid.");
                return false;
            }

            if (reducer == null)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.MissingIntentReducer,
                    "Every intent requires an explicit reducer.");
                return false;
            }

            if (_count >= _definitions.Length)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "Intent layout capacity is exhausted.");
                return false;
            }

            for (int index = 0; index < _count; index++)
            {
                if (_definitions[index].Descriptor.IntentId == intentId)
                {
                    handle = default;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Identity,
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "Intent ids must be unique within a layout.");
                    return false;
                }
            }

            handle = new CoCoIntentHandle<TIntent>(this, intentId, _count);
            _definitions[_count] = new CoCoIntentDefinition<TIntent>(
                new CoCoIntentDescriptor(intentId, typeof(TIntent), _count, maxContributions),
                reducer);
            _count++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Freeze(out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            _isFrozen = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryGetDescriptor(int denseIndex, out CoCoIntentDescriptor descriptor)
        {
            if (denseIndex < 0 || denseIndex >= _count)
            {
                descriptor = default;
                return false;
            }

            descriptor = _definitions[denseIndex].Descriptor;
            return true;
        }

        public bool TryCreateRuntime(
            CoCoGraphInstanceId graphInstanceId,
            int bindingCapacity,
            out CoCoIntentFrameRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!_isFrozen || !graphInstanceId.IsValid || bindingCapacity < 0)
            {
                runtime = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "A frozen layout, valid GraphInstanceId, and non-negative binding capacity are required.");
                return false;
            }

            if (_runtimes.ContainsKey(graphInstanceId))
            {
                runtime = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Identity,
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "A GraphInstance may own only one Intent runtime for a layout.");
                return false;
            }

            runtime = new CoCoIntentFrameRuntime(this, graphInstanceId, bindingCapacity);
            _runtimes.Add(graphInstanceId, runtime);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool Matches<TIntent>(CoCoIntentHandle<TIntent> handle)
            where TIntent : unmanaged
        {
            if (!handle.IsValid ||
                !handle.IsOwnedBy(this) ||
                handle.LayoutId != LayoutId ||
                handle.DenseIndex < 0 ||
                handle.DenseIndex >= _count)
            {
                return false;
            }

            CoCoIntentDescriptor descriptor = _definitions[handle.DenseIndex].Descriptor;
            return descriptor.IntentId == handle.IntentId &&
                   descriptor.ValueType == typeof(TIntent);
        }

        internal int GetContributionCapacity<TIntent>(CoCoIntentHandle<TIntent> handle)
            where TIntent : unmanaged
        {
            return Matches(handle)
                ? _definitions[handle.DenseIndex].Descriptor.MaxContributions
                : 0;
        }

        internal ICoCoIntentFrameSlot[] CreateFrameSlots()
        {
            var slots = new ICoCoIntentFrameSlot[_count];
            for (int index = 0; index < _count; index++)
            {
                slots[index] = _definitions[index].CreateFrameSlot();
            }

            return slots;
        }

        internal ICoCoIntentArbitrationLane[] CreateArbitrationLanes()
        {
            var lanes = new ICoCoIntentArbitrationLane[_count];
            for (int index = 0; index < _count; index++)
            {
                lanes[index] = _definitions[index].CreateArbitrationLane();
            }

            return lanes;
        }

        internal void ReleaseRuntime(
            CoCoGraphInstanceId graphInstanceId,
            CoCoIntentFrameRuntime runtime)
        {
            if (_runtimes.TryGetValue(graphInstanceId, out CoCoIntentFrameRuntime current) &&
                ReferenceEquals(current, runtime))
            {
                _runtimes.Remove(graphInstanceId);
            }
        }
    }

    public sealed class CoCoIntentFrameRuntime : IDisposable
    {
        private readonly CoCoIntentFrameLayout _layout;
        private readonly CoCoIntentFrame _frame;
        private readonly CoCoIntentFrameArbiter _arbiter;
        private readonly object[] _bindingIdentities;
        private readonly int[] _reservedContributions;
        private CoCoActorEventInboxCore _inbox;
        private int _bindingCount;
        private bool _bindingsFrozen;
        private bool _isDisposed;

        internal CoCoIntentFrameRuntime(
            CoCoIntentFrameLayout layout,
            CoCoGraphInstanceId graphInstanceId,
            int bindingCapacity)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            GraphInstanceId = graphInstanceId;
            _bindingIdentities = new object[bindingCapacity];
            _reservedContributions = new int[layout.Count];
            _frame = new CoCoIntentFrame(layout, graphInstanceId);
            _arbiter = new CoCoIntentFrameArbiter(layout, graphInstanceId);
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoFrameLayoutId LayoutId => _layout.LayoutId;
        public ICoCoIntentFrame Frame => _frame;
        public int BindingCount => _bindingCount;
        public int BindingCapacity => _bindingIdentities.Length;
        public bool AreBindingsFrozen => _bindingsFrozen;
        public bool IsCollecting => _arbiter.IsCollecting;
        public bool IsDisposed => _isDisposed;

        public bool TryBindSource<TIntent>(
            CoCoIntentSourceRequirement<TIntent> requirement,
            ICoCoIntentFrameSource<TIntent> source,
            out CoCoIntentSourceBinding<TIntent> binding,
            out CoCoDiagnostic diagnostic)
            where TIntent : unmanaged
        {
            if (!TryReserveBinding(requirement.Handle, source, 1, out int registrationOrder, out int bindingToken,
                    out diagnostic))
            {
                binding = null;
                return false;
            }

            binding = new CoCoIntentSourceBinding<TIntent>(
                this,
                requirement,
                source,
                registrationOrder,
                bindingToken);
            return true;
        }

        public bool TryBindEventAdapter<TEvent, TIntent>(
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeId,
            CoCoIntentSourceRequirement<TIntent> requirement,
            int projectionCapacity,
            ICoCoEventToIntentAdapter<TEvent, TIntent> adapter,
            out CoCoEventToIntentBinding<TEvent, TIntent> binding,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
            where TIntent : unmanaged
        {
            if (!eventDomainId.IsValid || !eventTypeId.IsValid || projectionCapacity <= 0)
            {
                binding = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "An Event-to-Intent binding requires valid ids and a positive projection capacity.");
                return false;
            }

            if (!TryReserveBinding(
                    requirement.Handle,
                    adapter,
                    projectionCapacity,
                    out int registrationOrder,
                    out int bindingToken,
                    out diagnostic))
            {
                binding = null;
                return false;
            }

            binding = new CoCoEventToIntentBinding<TEvent, TIntent>(
                this,
                eventDomainId,
                eventTypeId,
                requirement,
                adapter,
                registrationOrder,
                bindingToken,
                projectionCapacity);
            return true;
        }

        public bool FreezeBindings(out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "A disposed Intent runtime cannot freeze bindings.");
                return false;
            }

            _bindingsFrozen = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryBegin(
            in CoCoStateFlowFrameHeader header,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || !_bindingsFrozen)
            {
                diagnostic = CoCoDiagnostic.Error(
                    _isDisposed ? CoCoDiagnosticDomain.Lifecycle : CoCoDiagnosticDomain.Registry,
                    _isDisposed
                        ? CoCoDiagnosticCode.InvalidLifecycleTransition
                        : CoCoDiagnosticCode.RegistryNotFrozen,
                    _isDisposed
                        ? "A disposed Intent runtime cannot begin collection."
                        : "Intent source and adapter bindings must be frozen before collection begins.");
                return false;
            }

            return _arbiter.TryBegin(header, out diagnostic);
        }

        public CoCoIntentSourceSampleResult TrySample<TIntent>(
            CoCoIntentSourceBinding<TIntent> binding,
            in CoCoTickFrame tickFrame)
            where TIntent : unmanaged
        {
            if (_isDisposed || binding == null || !binding.IsOwnedBy(this))
            {
                return CoCoIntentSourceSampleResult.InvalidBinding;
            }

            return _arbiter.TrySample(binding, tickFrame);
        }

        public CoCoIntentEventProjectionResult TryProject<TEvent, TIntent>(
            CoCoEventToIntentBinding<TEvent, TIntent> binding,
            in CoCoActorEventSealedBatch<TEvent> batch)
            where TEvent : unmanaged
            where TIntent : unmanaged
        {
            if (_isDisposed)
            {
                return CoCoIntentEventProjectionResult.InvalidBinding;
            }

            if (!_arbiter.IsCollecting)
            {
                return CoCoIntentEventProjectionResult.ArbiterNotCollecting;
            }

            if (binding == null || !binding.IsValid || !binding.IsOwnedBy(this) ||
                !_layout.Matches(binding.Requirement.Handle))
            {
                return CoCoIntentEventProjectionResult.InvalidBinding;
            }

            if (!batch.IsValid)
            {
                return CoCoIntentEventProjectionResult.InvalidBatch;
            }

            if (batch.Owner != GraphInstanceId ||
                batch.EventDomainId != binding.EventDomainId ||
                batch.EventTypeId != binding.EventTypeId ||
                !batch.IsProjectionRuntime(this))
            {
                return CoCoIntentEventProjectionResult.InvalidBatch;
            }

            int packetCount = batch.Count;
            for (int index = 0; index < packetCount; index++)
            {
                if (!batch.TryRead(index, out CoCoEventPacket<TEvent> packet) ||
                    !packet.IsValid ||
                    packet.Envelope.EventDomainId != binding.EventDomainId ||
                    packet.Envelope.EventTypeId != binding.EventTypeId ||
                    !packet.Envelope.SourceGraphInstanceId.IsValid ||
                    !packet.Envelope.SourceEventSequence.IsValid)
                {
                    return CoCoIntentEventProjectionResult.InvalidPacket;
                }
            }

            ulong frameGeneration = _arbiter.FrameGeneration;
            if (!batch.TryClaimProjection(this, frameGeneration) ||
                !binding.TryClaimProjection(frameGeneration))
            {
                return CoCoIntentEventProjectionResult.AlreadyProjected;
            }

            int projectedCount = 0;
            CoCoIntentContribution<TIntent>[] scratch = binding.Scratch;
            for (int index = 0; index < packetCount; index++)
            {
                batch.TryRead(index, out CoCoEventPacket<TEvent> packet);

                if (!binding.TryProject(packet, out TIntent value))
                {
                    continue;
                }

                if (projectedCount >= scratch.Length)
                {
                    return CoCoIntentEventProjectionResult.CapacityExceeded;
                }

                scratch[projectedCount] = new CoCoIntentContribution<TIntent>(
                    binding.Requirement.Handle,
                    binding.Requirement.Priority,
                    binding.RegistrationOrder,
                    binding.BindingToken,
                    packet.Envelope.SourceGraphInstanceId,
                    packet.Envelope.SourceTimelineEpoch,
                    packet.Envelope.SourceEventSequence,
                    value);
                projectedCount++;
            }

            if (projectedCount == 0)
            {
                return CoCoIntentEventProjectionResult.NoValue;
            }

            CoCoIntentContributionResult result = _arbiter.ContributeBatch(
                binding.Requirement.Handle,
                scratch,
                projectedCount);
            switch (result)
            {
                case CoCoIntentContributionResult.Accepted:
                    return CoCoIntentEventProjectionResult.Contributed;
                case CoCoIntentContributionResult.DuplicateContribution:
                    return CoCoIntentEventProjectionResult.DuplicateContribution;
                case CoCoIntentContributionResult.CapacityExceeded:
                    return CoCoIntentEventProjectionResult.CapacityExceeded;
                default:
                    return CoCoIntentEventProjectionResult.InvalidBinding;
            }
        }

        public bool TryFreeze(out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "A disposed Intent runtime cannot freeze a frame.");
                return false;
            }

            return _arbiter.TryFreeze(_frame, out diagnostic);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _bindingsFrozen = false;
            Array.Clear(_bindingIdentities, 0, _bindingIdentities.Length);
            Array.Clear(_reservedContributions, 0, _reservedContributions.Length);
            _bindingCount = 0;
            _arbiter.ResetForDispose();
            CoCoActorEventInboxCore inbox = _inbox;
            _inbox = null;
            inbox?.ReleaseIntentRuntime(this);
            _layout.ReleaseRuntime(GraphInstanceId, this);
        }

        internal bool TryClaimInbox(CoCoActorEventInboxCore inbox)
        {
            if (_isDisposed || inbox == null || inbox.Owner != GraphInstanceId)
            {
                return false;
            }

            if (_inbox == null)
            {
                _inbox = inbox;
                return true;
            }

            return ReferenceEquals(_inbox, inbox);
        }

        internal void ReleaseInbox(CoCoActorEventInboxCore inbox)
        {
            if (ReferenceEquals(_inbox, inbox))
            {
                _inbox = null;
            }
        }

        private bool TryReserveBinding<TIntent>(
            CoCoIntentHandle<TIntent> handle,
            object sourceIdentity,
            int contributionReservation,
            out int registrationOrder,
            out int bindingToken,
            out CoCoDiagnostic diagnostic)
            where TIntent : unmanaged
        {
            if (_isDisposed || _bindingsFrozen)
            {
                registrationOrder = -1;
                bindingToken = 0;
                diagnostic = CoCoDiagnostic.Error(
                    _isDisposed ? CoCoDiagnosticDomain.Lifecycle : CoCoDiagnosticDomain.Registry,
                    _isDisposed
                        ? CoCoDiagnosticCode.InvalidLifecycleTransition
                        : CoCoDiagnosticCode.RegistryFrozen,
                    _isDisposed
                        ? "A disposed Intent runtime cannot accept bindings."
                        : "Intent bindings are already frozen.");
                return false;
            }

            if (!_layout.Matches(handle) ||
                sourceIdentity == null ||
                sourceIdentity.GetType().IsValueType ||
                contributionReservation <= 0)
            {
                registrationOrder = -1;
                bindingToken = 0;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "Intent bindings require a matching handle and a reference-type source instance.");
                return false;
            }

            for (int index = 0; index < _bindingCount; index++)
            {
                if (ReferenceEquals(_bindingIdentities[index], sourceIdentity))
                {
                    registrationOrder = -1;
                    bindingToken = 0;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Identity,
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "The same source instance cannot be bound more than once to an Intent runtime.");
                    return false;
                }
            }

            if (_bindingCount >= _bindingIdentities.Length)
            {
                registrationOrder = -1;
                bindingToken = 0;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "Intent binding capacity is exhausted.");
                return false;
            }

            int contributionCapacity = _layout.GetContributionCapacity(handle);
            int reserved = _reservedContributions[handle.DenseIndex];
            if (contributionReservation > contributionCapacity - reserved)
            {
                registrationOrder = -1;
                bindingToken = 0;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentContribution,
                    "Intent binding reservations exceed the frozen contribution capacity.");
                return false;
            }

            registrationOrder = _bindingCount;
            bindingToken = _bindingCount + 1;
            _bindingIdentities[_bindingCount] = sourceIdentity;
            _reservedContributions[handle.DenseIndex] = reserved + contributionReservation;
            _bindingCount++;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    internal sealed class CoCoIntentFrameArbiter
    {
        private readonly CoCoIntentFrameLayout _layout;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly ICoCoIntentArbitrationLane[] _lanes;
        private CoCoStateFlowFrameHeader _header;
        private CoCoStateFlowFrameHeader _lastHeader;
        private bool _isCollecting;
        private bool _hasAcceptedHeader;
        private ulong _frameGeneration;

        public CoCoIntentFrameArbiter(
            CoCoIntentFrameLayout layout,
            CoCoGraphInstanceId graphInstanceId)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _graphInstanceId = graphInstanceId;
            _lanes = layout.CreateArbitrationLanes();
        }

        public bool IsCollecting => _isCollecting;
        public ulong FrameGeneration => _frameGeneration;

        public bool TryBegin(
            in CoCoStateFlowFrameHeader header,
            out CoCoDiagnostic diagnostic)
        {
            if (_isCollecting ||
                header.Identity.GraphInstanceId != _graphInstanceId ||
                !header.LayoutId.IsValid ||
                !header.TickFrame.IsValid ||
                header.LayoutId != _layout.LayoutId ||
                header.Identity.Kind != CoCoStateFlowFrameKind.Intent ||
                (_hasAcceptedHeader &&
                 !CoCoStateFlowTickOrder.IsStrictlyAfter(
                     header.TickFrame,
                     _lastHeader.TickFrame)))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "Intent frame header does not match the frozen intent runtime.");
                return false;
            }

            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].Reset();
            }

            _header = header;
            _lastHeader = header;
            _hasAcceptedHeader = true;
            _frameGeneration++;
            if (_frameGeneration == 0UL)
            {
                _frameGeneration = 1UL;
            }

            _isCollecting = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public CoCoIntentSourceSampleResult TrySample<TIntent>(
            CoCoIntentSourceBinding<TIntent> binding,
            in CoCoTickFrame tickFrame)
            where TIntent : unmanaged
        {
            if (!_isCollecting)
            {
                return CoCoIntentSourceSampleResult.ArbiterNotCollecting;
            }

            if (!binding.IsValid ||
                binding.GraphInstanceId != _graphInstanceId ||
                !_layout.Matches(binding.Requirement.Handle) ||
                tickFrame != _header.TickFrame)
            {
                return CoCoIntentSourceSampleResult.InvalidBinding;
            }

            if (!binding.TrySample(
                    _frameGeneration,
                    tickFrame,
                    out TIntent value,
                    out bool hasValue))
            {
                return CoCoIntentSourceSampleResult.AlreadySampled;
            }

            if (!hasValue)
            {
                return CoCoIntentSourceSampleResult.NoValue;
            }

            var contribution = new CoCoIntentContribution<TIntent>(
                binding.Requirement.Handle,
                binding.Requirement.Priority,
                binding.RegistrationOrder,
                binding.BindingToken,
                _graphInstanceId,
                _header.Identity.TimelineEpoch,
                CoCoEventSequence.Zero,
                value);
            CoCoIntentContributionResult result = Contribute(binding.Requirement.Handle, contribution);
            return result == CoCoIntentContributionResult.Accepted
                ? CoCoIntentSourceSampleResult.Contributed
                : CoCoIntentSourceSampleResult.ContributionRejected;
        }

        public CoCoIntentContributionResult ContributeBatch<TIntent>(
            CoCoIntentHandle<TIntent> handle,
            CoCoIntentContribution<TIntent>[] contributions,
            int count)
            where TIntent : unmanaged
        {
            if (!_isCollecting)
            {
                return CoCoIntentContributionResult.ArbiterNotCollecting;
            }

            if (!_layout.Matches(handle) || contributions == null || count <= 0 || count > contributions.Length)
            {
                return CoCoIntentContributionResult.InvalidHandle;
            }

            var lane = _lanes[handle.DenseIndex] as CoCoIntentArbitrationLane<TIntent>;
            return lane == null
                ? CoCoIntentContributionResult.InvalidHandle
                : lane.AddBatch(contributions, count);
        }

        public bool TryFreeze(CoCoIntentFrame destination, out CoCoDiagnostic diagnostic)
        {
            if (!_isCollecting || destination == null ||
                destination.GraphInstanceId != _graphInstanceId ||
                destination.LayoutId != _layout.LayoutId)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentContribution,
                    "Intent arbitration must be collecting and target its owned frame.");
                return false;
            }

            destination.Prepare(_header);
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].ReduceInto(destination.GetSlot(index));
            }

            destination.Seal();
            _isCollecting = false;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void ResetForDispose()
        {
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].Reset();
            }

            _isCollecting = false;
            _header = default;
        }

        private CoCoIntentContributionResult Contribute<TIntent>(
            CoCoIntentHandle<TIntent> handle,
            in CoCoIntentContribution<TIntent> contribution)
            where TIntent : unmanaged
        {
            if (!_layout.Matches(handle) || !contribution.IsValid || contribution.Handle != handle)
            {
                return CoCoIntentContributionResult.InvalidHandle;
            }

            var lane = _lanes[handle.DenseIndex] as CoCoIntentArbitrationLane<TIntent>;
            return lane == null
                ? CoCoIntentContributionResult.InvalidHandle
                : lane.Add(contribution);
        }
    }

    internal interface ICoCoIntentDefinition
    {
        CoCoIntentDescriptor Descriptor { get; }
        ICoCoIntentFrameSlot CreateFrameSlot();
        ICoCoIntentArbitrationLane CreateArbitrationLane();
    }

    internal sealed class CoCoIntentDefinition<TIntent> : ICoCoIntentDefinition
        where TIntent : unmanaged
    {
        private readonly ICoCoIntentReducer<TIntent> _reducer;

        public CoCoIntentDefinition(
            CoCoIntentDescriptor descriptor,
            ICoCoIntentReducer<TIntent> reducer)
        {
            Descriptor = descriptor;
            _reducer = reducer;
        }

        public CoCoIntentDescriptor Descriptor { get; }

        public ICoCoIntentFrameSlot CreateFrameSlot() => new CoCoIntentFrameSlot<TIntent>();

        public ICoCoIntentArbitrationLane CreateArbitrationLane()
        {
            return new CoCoIntentArbitrationLane<TIntent>(
                Descriptor.MaxContributions,
                _reducer);
        }
    }

    internal interface ICoCoIntentFrameSlot
    {
        void Clear();
    }

    internal sealed class CoCoIntentFrameSlot<TIntent> : ICoCoIntentFrameSlot
        where TIntent : unmanaged
    {
        public bool IsPresent { get; private set; }
        public TIntent Value { get; private set; }

        public void Clear()
        {
            IsPresent = false;
            Value = default;
        }

        public void Set(in TIntent value)
        {
            Value = value;
            IsPresent = true;
        }
    }

    internal interface ICoCoIntentArbitrationLane
    {
        void Reset();
        void ReduceInto(ICoCoIntentFrameSlot destination);
    }

    internal sealed class CoCoIntentArbitrationLane<TIntent> : ICoCoIntentArbitrationLane
        where TIntent : unmanaged
    {
        private readonly CoCoIntentContribution<TIntent>[] _contributions;
        private readonly ICoCoIntentReducer<TIntent> _reducer;
        private int _count;

        public CoCoIntentArbitrationLane(
            int maxContributions,
            ICoCoIntentReducer<TIntent> reducer)
        {
            _contributions = new CoCoIntentContribution<TIntent>[maxContributions];
            _reducer = reducer;
        }

        public CoCoIntentContributionResult Add(in CoCoIntentContribution<TIntent> contribution)
        {
            if (ContainsDuplicate(contribution))
            {
                return CoCoIntentContributionResult.DuplicateContribution;
            }

            if (_count >= _contributions.Length)
            {
                return CoCoIntentContributionResult.CapacityExceeded;
            }

            Insert(contribution);
            return CoCoIntentContributionResult.Accepted;
        }

        public CoCoIntentContributionResult AddBatch(
            CoCoIntentContribution<TIntent>[] contributions,
            int count)
        {
            if (_count + count > _contributions.Length)
            {
                return CoCoIntentContributionResult.CapacityExceeded;
            }

            for (int index = 0; index < count; index++)
            {
                CoCoIntentContribution<TIntent> candidate = contributions[index];
                if (!candidate.IsValid || ContainsDuplicate(candidate))
                {
                    return candidate.IsValid
                        ? CoCoIntentContributionResult.DuplicateContribution
                        : CoCoIntentContributionResult.InvalidHandle;
                }

                for (int earlierIndex = 0; earlierIndex < index; earlierIndex++)
                {
                    if (IsDuplicate(candidate, contributions[earlierIndex]))
                    {
                        return CoCoIntentContributionResult.DuplicateContribution;
                    }
                }
            }

            for (int index = 0; index < count; index++)
            {
                Insert(contributions[index]);
            }

            return CoCoIntentContributionResult.Accepted;
        }

        public void Reset()
        {
            _count = 0;
        }

        public void ReduceInto(ICoCoIntentFrameSlot destination)
        {
            if (_count == 0)
            {
                return;
            }

            var typedDestination = (CoCoIntentFrameSlot<TIntent>)destination;
            TIntent result = _contributions[0].Value;
            for (int index = 1; index < _count; index++)
            {
                result = _reducer.Reduce(result, _contributions[index].Value);
            }

            typedDestination.Set(result);
        }

        private bool ContainsDuplicate(in CoCoIntentContribution<TIntent> contribution)
        {
            for (int index = 0; index < _count; index++)
            {
                if (IsDuplicate(contribution, _contributions[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private void Insert(in CoCoIntentContribution<TIntent> contribution)
        {
            int insertIndex = _count;
            while (insertIndex > 0 && ComesBefore(contribution, _contributions[insertIndex - 1]))
            {
                _contributions[insertIndex] = _contributions[insertIndex - 1];
                insertIndex--;
            }

            _contributions[insertIndex] = contribution;
            _count++;
        }

        private static bool IsDuplicate(
            in CoCoIntentContribution<TIntent> left,
            in CoCoIntentContribution<TIntent> right)
        {
            return left.BindingToken == right.BindingToken &&
                   left.SourceGraphInstanceId == right.SourceGraphInstanceId &&
                   left.SourceTimelineEpoch == right.SourceTimelineEpoch &&
                   left.EventSequence == right.EventSequence;
        }

        private static bool ComesBefore(
            in CoCoIntentContribution<TIntent> left,
            in CoCoIntentContribution<TIntent> right)
        {
            if (left.Priority != right.Priority)
            {
                return left.Priority > right.Priority;
            }

            if (left.RegistrationOrder != right.RegistrationOrder)
            {
                return left.RegistrationOrder < right.RegistrationOrder;
            }

            if (left.SourceGraphInstanceId.Value != right.SourceGraphInstanceId.Value)
            {
                return left.SourceGraphInstanceId.Value < right.SourceGraphInstanceId.Value;
            }

            if (left.SourceTimelineEpoch.Value != right.SourceTimelineEpoch.Value)
            {
                return left.SourceTimelineEpoch.Value < right.SourceTimelineEpoch.Value;
            }

            return left.EventSequence.Value < right.EventSequence.Value;
        }
    }
}
