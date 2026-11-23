using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public interface IIntentReducer<TIntent>
    {
        TIntent Reduce(in TIntent current, in TIntent candidate);
    }

    public interface IIntentReducerFactory<TIntent, TReducer>
        where TReducer : unmanaged, IIntentReducer<TIntent>
    {
        TReducer Create(GraphInstanceId graphInstanceId);
    }

    internal struct IntentPassThrough<T> : IIntentReducer<T>
    {
        public T Reduce(in T current, in T candidate) => candidate;
    }

    internal sealed class IntentPassThroughFactory<T> : IIntentReducerFactory<T, IntentPassThrough<T>>
    {
        public IntentPassThrough<T> Create(GraphInstanceId graphInstanceId) => default;
    }

    public interface IIntentFrameSource<TIntent>
    {
        bool TrySample(in TickFrame tickFrame, out TIntent intent);
    }

    internal interface ICursorIntentSource<TIntent>
    {
        void AttachCursor(IntentSourceCursor cursor);
        bool TrySample(IntentSourceCursor cursor, out TIntent intent);
    }

    internal sealed class IntentSourceCursor
    {
        private readonly IntentFrameRuntime _runtime;

        internal IntentSourceCursor(IntentFrameRuntime runtime) => _runtime = runtime;

        internal bool IsActive => !_runtime.IsDisposed;
        internal object Reader;
        internal ulong Committed;
        internal ulong Prepared;
    }

    public interface IIntentFrame
    {
        GraphInstanceId GraphInstanceId { get; }
        StateFlowFrameHeader Header { get; }
        FrameLayoutId LayoutId { get; }
        bool IsFrozen { get; }

        bool TryGet<TIntent>(
            IntentHandle<TIntent> handle,
            out TIntent value);

        bool IsPresent<TIntent>(IntentHandle<TIntent> handle);

        /// <summary>
        /// Returns the first present slot of type TIntent in this frame.
        /// Standard single-intent-per-type wiring uses this; frames with
        /// multiple ids of one type resolve handles explicitly.
        /// </summary>
        bool TryFirst<TIntent>(out TIntent value);
    }

    public enum IntentContributionResult
    {
        None = 0,
        Accepted = 1,
        ArbiterNotCollecting = 2,
        InvalidHandle = 3,
        DuplicateContribution = 4,
        CapacityExceeded = 5
    }

    public enum IntentSourceSampleResult
    {
        None = 0,
        Contributed = 1,
        NoValue = 2,
        AlreadySampled = 3,
        ArbiterNotCollecting = 4,
        InvalidBinding = 5,
        ContributionRejected = 6
    }

    public readonly struct IntentDescriptor : IEquatable<IntentDescriptor>
    {
        internal IntentDescriptor(
            IntentId intentId,
            Type valueType,
            int denseIndex,
            int maxContributions)
        {
            IntentId = intentId;
            ValueType = valueType;
            DenseIndex = denseIndex;
            MaxContributions = maxContributions;
        }

        public IntentId IntentId { get; }
        public Type ValueType { get; }
        public int DenseIndex { get; }
        public int MaxContributions { get; }
        public bool IsValid => IntentId.IsValid &&
                               ValueType != null &&
                               DenseIndex >= 0 &&
                               MaxContributions > 0;

        public bool Equals(IntentDescriptor other)
        {
            return IntentId == other.IntentId &&
                   ValueType == other.ValueType &&
                   DenseIndex == other.DenseIndex &&
                   MaxContributions == other.MaxContributions;
        }

        public override bool Equals(object obj) => obj is IntentDescriptor other && Equals(other);

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

        public static bool operator ==(IntentDescriptor left, IntentDescriptor right) => left.Equals(right);
        public static bool operator !=(IntentDescriptor left, IntentDescriptor right) => !left.Equals(right);
    }

    public readonly struct IntentHandle<TIntent> : IEquatable<IntentHandle<TIntent>>
    {
        private readonly IntentFrameLayout _owner;

        internal IntentHandle(
            IntentFrameLayout owner,
            IntentId intentId,
            int denseIndex)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            IntentId = intentId;
            DenseIndex = denseIndex;
        }

        public FrameLayoutId LayoutId { get; }
        public IntentId IntentId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               IntentId.IsValid &&
                               DenseIndex >= 0;

        internal bool IsOwnedBy(IntentFrameLayout owner) => ReferenceEquals(_owner, owner);

        public bool Equals(IntentHandle<TIntent> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   IntentId == other.IntentId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) => obj is IntentHandle<TIntent> other && Equals(other);

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

        public static bool operator ==(IntentHandle<TIntent> left, IntentHandle<TIntent> right) =>
            left.Equals(right);

        public static bool operator !=(IntentHandle<TIntent> left, IntentHandle<TIntent> right) =>
            !left.Equals(right);
    }

    internal readonly struct IntentSourceRequirement<TIntent> :
        IEquatable<IntentSourceRequirement<TIntent>>
    {
        private IntentSourceRequirement(
            IntentHandle<TIntent> handle,
            int priority)
        {
            Handle = handle;
            Priority = priority;
        }

        public IntentHandle<TIntent> Handle { get; }
        public int Priority { get; }
        public bool IsValid => Handle.IsValid;

        public static bool TryCreate(
            IntentHandle<TIntent> handle,
            int priority,
            out IntentSourceRequirement<TIntent> requirement)
        {
            if (!handle.IsValid)
            {
                requirement = default;
                return false;
            }

            requirement = new IntentSourceRequirement<TIntent>(handle, priority);
            return true;
        }

        public bool Equals(IntentSourceRequirement<TIntent> other)
        {
            return Handle == other.Handle && Priority == other.Priority;
        }

        public override bool Equals(object obj) =>
            obj is IntentSourceRequirement<TIntent> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Handle.GetHashCode() * 397) ^ Priority;
            }
        }

        public static bool operator ==(
            IntentSourceRequirement<TIntent> left,
            IntentSourceRequirement<TIntent> right) => left.Equals(right);

        public static bool operator !=(
            IntentSourceRequirement<TIntent> left,
            IntentSourceRequirement<TIntent> right) => !left.Equals(right);
    }

    internal sealed class IntentSourceBinding<TIntent>
    {
        private readonly IntentFrameRuntime _runtime;
        private readonly IIntentFrameSource<TIntent> _source;
        private ICursorIntentSource<TIntent> _cursorSource;
        private ulong _lastSampledFrameGeneration;

        internal IntentSourceBinding(
            IntentFrameRuntime runtime,
            IntentSourceRequirement<TIntent> requirement,
            IIntentFrameSource<TIntent> source,
            int registrationOrder,
            int bindingToken)
        {
            _runtime = runtime;
            Requirement = requirement;
            _source = source;
            RegistrationOrder = registrationOrder;
            BindingToken = bindingToken;
        }

        public GraphInstanceId GraphInstanceId => _runtime.GraphInstanceId;
        public IntentSourceRequirement<TIntent> Requirement { get; }
        public int RegistrationOrder { get; private set; }
        public bool IsValid => _runtime != null &&
                               !_runtime.IsDisposed &&
                               Requirement.IsValid &&
                               _source != null &&
                               RegistrationOrder >= 0 &&
                               BindingToken > 0;

        internal int BindingToken { get; }
        internal IntentSourceCursor Cursor { get; private set; }

        internal void AttachHostCursor()
        {
            if (_source is ICursorIntentSource<TIntent> source)
            {
                _cursorSource = source;
                Cursor = new IntentSourceCursor(_runtime);
                source.AttachCursor(Cursor);
            }
        }

        internal bool IsOwnedBy(IntentFrameRuntime runtime) => ReferenceEquals(_runtime, runtime);

        internal bool TryAssignRegistrationOrder(int registrationOrder)
        {
            if (registrationOrder < 0 || _runtime == null || _runtime.IsDisposed ||
                _runtime.AreBindingsFrozen)
            {
                return false;
            }

            RegistrationOrder = registrationOrder;
            return true;
        }

        internal bool TrySample(
            ulong frameGeneration,
            in TickFrame tickFrame,
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
            hasValue = _cursorSource != null
                ? _cursorSource.TrySample(Cursor, out intent)
                : _source.TrySample(tickFrame, out intent);
            return true;
        }
    }

    internal readonly struct IntentContribution<TIntent>
    {
        public IntentContribution(
            IntentHandle<TIntent> handle,
            int priority,
            int registrationOrder,
            int bindingToken,
            GraphInstanceId sourceGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            EventSequence eventSequence,
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

        public IntentHandle<TIntent> Handle { get; }
        public int Priority { get; }
        public int RegistrationOrder { get; }
        public int BindingToken { get; }
        public GraphInstanceId SourceGraphInstanceId { get; }
        public TimelineEpoch SourceTimelineEpoch { get; }
        public EventSequence EventSequence { get; }
        public TIntent Value { get; }
        public bool IsValid => Handle.IsValid &&
                               RegistrationOrder >= 0 &&
                               BindingToken > 0 &&
                               SourceGraphInstanceId.IsValid;
    }

    internal sealed class IntentFrame : IIntentFrame
    {
        private readonly IntentFrameLayout _layout;
        private readonly IIntentFrameSlot[] _slots;
        private StateFlowFrameHeader _header;
        private bool _isFrozen;

        public IntentFrame(
            IntentFrameLayout layout,
            GraphInstanceId graphInstanceId)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            GraphInstanceId = graphInstanceId;
            _slots = layout.CreateFrameSlots();
        }

        public GraphInstanceId GraphInstanceId { get; }
        public StateFlowFrameHeader Header => _header;
        public FrameLayoutId LayoutId => _layout.LayoutId;
        public bool IsFrozen => _isFrozen;

        public bool TryGet<TIntent>(
            IntentHandle<TIntent> handle,
            out TIntent value)
        {
            if (!_isFrozen || !_layout.Matches(handle))
            {
                value = default;
                return false;
            }

            var slot = _slots[handle.DenseIndex] as IntentFrameSlot<TIntent>;
            if (slot == null || !slot.IsPresent)
            {
                value = default;
                return false;
            }

            value = slot.Value;
            return true;
        }

        public bool IsPresent<TIntent>(IntentHandle<TIntent> handle)
        {
            return _isFrozen &&
                   _layout.Matches(handle) &&
                   _slots[handle.DenseIndex] is IntentFrameSlot<TIntent> slot &&
                   slot.IsPresent;
        }

        public bool TryFirst<TIntent>(out TIntent value)
        {
            if (_isFrozen)
            {
                for (int index = 0; index < _slots.Length; index++)
                {
                    if (_slots[index] is IntentFrameSlot<TIntent> slot &&
                        slot.IsPresent)
                    {
                        value = slot.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        internal void Prepare(in StateFlowFrameHeader header)
        {
            for (int index = 0; index < _slots.Length; index++)
            {
                _slots[index].Clear();
            }

            _header = header;
            _isFrozen = false;
        }

        internal void Invalidate()
        {
            for (int index = 0; index < _slots.Length; index++)
            {
                _slots[index].Clear();
            }

            _header = default;
            _isFrozen = false;
        }

        internal IIntentFrameSlot GetSlot(int denseIndex) => _slots[denseIndex];

        internal void Seal()
        {
            _isFrozen = true;
        }
    }

    internal sealed class IntentFrameLayout
    {
        private readonly IIntentDefinition[] _definitions;
        private readonly Dictionary<GraphInstanceId, IntentFrameRuntime> _runtimes =
            new Dictionary<GraphInstanceId, IntentFrameRuntime>();
        private int _count;
        private bool _isFrozen;

        public IntentFrameLayout(FrameLayoutId layoutId, int maxIntentCount)
        {
            if (!layoutId.IsValid)
            {
                throw new ArgumentException("Intent layout id must be valid.", nameof(layoutId));
            }

            if (maxIntentCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxIntentCount),
                    maxIntentCount,
                    "Intent layout capacity cannot be negative.");
            }

            LayoutId = layoutId;
            _definitions = new IIntentDefinition[maxIntentCount];
        }

        public FrameLayoutId LayoutId { get; }
        public int Count => _count;
        public int Capacity => _definitions.Length;
        public bool IsFrozen => _isFrozen;

        public bool TryRegister<TIntent, TReducer>(
            IntentId intentId,
            int maxContributions,
            IIntentReducerFactory<TIntent, TReducer> reducerFactory,
            out IntentHandle<TIntent> handle,
            out CoCoDiagnostic diagnostic)
            where TReducer : unmanaged, IIntentReducer<TIntent>
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

            if (reducerFactory == null)
            {
                handle = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.MissingIntentReducer,
                    "Every intent requires an explicit per-runtime reducer factory.");
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

            handle = new IntentHandle<TIntent>(this, intentId, _count);
            _definitions[_count] = new IntentDefinition<TIntent, TReducer>(
                new IntentDescriptor(intentId, typeof(TIntent), _count, maxContributions),
                reducerFactory);
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

        public bool TryGetDescriptor(int denseIndex, out IntentDescriptor descriptor)
        {
            if (denseIndex < 0 || denseIndex >= _count)
            {
                descriptor = default;
                return false;
            }

            descriptor = _definitions[denseIndex].Descriptor;
            return true;
        }

        internal bool TryCreateRuntime(
            GraphInstanceId graphInstanceId,
            int bindingCapacity,
            out IntentFrameRuntime runtime,
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

            _runtimes.Add(graphInstanceId, null);
            try
            {
                runtime = new IntentFrameRuntime(this, graphInstanceId, bindingCapacity);
                _runtimes[graphInstanceId] = runtime;
            }
            catch
            {
                _runtimes.Remove(graphInstanceId);
                runtime = null;
                throw;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool Matches<TIntent>(IntentHandle<TIntent> handle)
        {
            if (!handle.IsValid ||
                !handle.IsOwnedBy(this) ||
                handle.LayoutId != LayoutId ||
                handle.DenseIndex < 0 ||
                handle.DenseIndex >= _count)
            {
                return false;
            }

            IntentDescriptor descriptor = _definitions[handle.DenseIndex].Descriptor;
            return descriptor.IntentId == handle.IntentId &&
                   descriptor.ValueType == typeof(TIntent);
        }

        internal int GetContributionCapacity<TIntent>(IntentHandle<TIntent> handle)
        {
            return Matches(handle)
                ? _definitions[handle.DenseIndex].Descriptor.MaxContributions
                : 0;
        }

        internal IIntentFrameSlot[] CreateFrameSlots()
        {
            var slots = new IIntentFrameSlot[_count];
            for (int index = 0; index < _count; index++)
            {
                slots[index] = _definitions[index].CreateFrameSlot();
            }

            return slots;
        }

        internal IIntentArbitrationLane[] CreateArbitrationLanes(
            GraphInstanceId graphInstanceId)
        {
            var lanes = new IIntentArbitrationLane[_count];
            for (int index = 0; index < _count; index++)
            {
                lanes[index] = _definitions[index].CreateArbitrationLane(graphInstanceId);
            }

            return lanes;
        }

        internal void ReleaseRuntime(
            GraphInstanceId graphInstanceId,
            IntentFrameRuntime runtime)
        {
            if (_runtimes.TryGetValue(graphInstanceId, out IntentFrameRuntime current) &&
                ReferenceEquals(current, runtime))
            {
                _runtimes.Remove(graphInstanceId);
            }
        }
    }

    internal sealed class IntentFrameRuntime : IDisposable
    {
        private readonly IntentFrameLayout _layout;
        private readonly IntentFrame _frame;
        private readonly IntentFrameArbiter _arbiter;
        private readonly object[] _bindingIdentities;
        private readonly int[] _reservedContributions;
        private ActorEventInboxCore _inbox;
        private int _bindingCount;
        private bool _bindingsFrozen;
        private bool _isDisposed;
        private bool _isExecutingUserCallback;
        private bool _disposeRequested;

        internal IntentFrameRuntime(
            IntentFrameLayout layout,
            GraphInstanceId graphInstanceId,
            int bindingCapacity)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            GraphInstanceId = graphInstanceId;
            _bindingIdentities = new object[bindingCapacity];
            _reservedContributions = new int[layout.Count];
            _frame = new IntentFrame(layout, graphInstanceId);
            _arbiter = new IntentFrameArbiter(layout, graphInstanceId, this);
        }

        public GraphInstanceId GraphInstanceId { get; }
        public FrameLayoutId LayoutId => _layout.LayoutId;
        public IIntentFrame Frame => _frame;
        public int BindingCount => _bindingCount;
        public int BindingCapacity => _bindingIdentities.Length;
        public bool AreBindingsFrozen => _bindingsFrozen;
        public bool IsCollecting => _arbiter.IsCollecting;
        public bool IsDisposed => _isDisposed;
        internal bool IsExecutingUserCallback => _isExecutingUserCallback;
        internal bool IsReductionAbortRequested =>
            _disposeRequested || (_inbox != null && _inbox.HasDeferredLifecycle(this));

        internal bool TryBindSource<TIntent>(
            IntentSourceRequirement<TIntent> requirement,
            IIntentFrameSource<TIntent> source,
            out IntentSourceBinding<TIntent> binding,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryReserveBinding(requirement.Handle, source, 1, out int registrationOrder, out int bindingToken,
                    out diagnostic))
            {
                binding = null;
                return false;
            }

            binding = new IntentSourceBinding<TIntent>(
                this,
                requirement,
                source,
                registrationOrder,
                bindingToken);
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
            in StateFlowFrameHeader header,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || !_bindingsFrozen || _isExecutingUserCallback)
            {
                diagnostic = CoCoDiagnostic.Error(
                    _isDisposed || _isExecutingUserCallback
                        ? CoCoDiagnosticDomain.Lifecycle
                        : CoCoDiagnosticDomain.Registry,
                    _isDisposed
                        ? CoCoDiagnosticCode.InvalidLifecycleTransition
                        : _isExecutingUserCallback
                            ? CoCoDiagnosticCode.InvalidLifecycleTransition
                            : CoCoDiagnosticCode.RegistryNotFrozen,
                    _isDisposed
                        ? "A disposed Intent runtime cannot begin collection."
                        : _isExecutingUserCallback
                            ? "Intent runtime callbacks cannot reenter frame collection."
                            : "Intent source and adapter bindings must be frozen before collection begins.");
                return false;
            }

            if (!_arbiter.TryBegin(header, out diagnostic))
            {
                return false;
            }

            _frame.Invalidate();
            return true;
        }

        public IntentSourceSampleResult TrySample<TIntent>(
            IntentSourceBinding<TIntent> binding,
            in TickFrame tickFrame)
        {
            if (_isDisposed || _isExecutingUserCallback || binding == null || !binding.IsOwnedBy(this))
            {
                return IntentSourceSampleResult.InvalidBinding;
            }

            if (!_arbiter.IsCollecting)
            {
                return IntentSourceSampleResult.ArbiterNotCollecting;
            }

            ulong expectedGeneration = _arbiter.FrameGeneration;
            _isExecutingUserCallback = true;
            IntentSourceSampleResult result;
            try
            {
                result = _arbiter.TrySample(binding, tickFrame, expectedGeneration);
            }
            catch
            {
                _isExecutingUserCallback = false;
                CancelCollectionCore();
                CompleteDeferredInboxLifecycle();
                CompleteDeferredDispose();
                throw;
            }

            _isExecutingUserCallback = false;
            if (_disposeRequested)
            {
                CancelCollectionCore();
                CompleteDeferredInboxLifecycle();
                CompleteDeferredDispose();
                return IntentSourceSampleResult.InvalidBinding;
            }

            if (HasDeferredInboxLifecycle())
            {
                CancelCollectionCore();
                CompleteDeferredInboxLifecycle();
                return IntentSourceSampleResult.ArbiterNotCollecting;
            }

            if (!_arbiter.IsGenerationCurrent(expectedGeneration))
            {
                CancelCollectionCore();
                return IntentSourceSampleResult.ArbiterNotCollecting;
            }

            return result;
        }

        public bool TryFreeze(out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || _isExecutingUserCallback)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    _isDisposed
                        ? "A disposed Intent runtime cannot freeze a frame."
                        : "Intent runtime callbacks cannot reenter frame freezing.");
                return false;
            }

            ulong expectedGeneration = _arbiter.FrameGeneration;
            _isExecutingUserCallback = true;
            bool succeeded;
            try
            {
                succeeded = _arbiter.TryFreeze(_frame, expectedGeneration, out diagnostic);
            }
            catch
            {
                _isExecutingUserCallback = false;
                CancelCollectionCore();
                CompleteDeferredInboxLifecycle();
                CompleteDeferredDispose();
                throw;
            }

            _isExecutingUserCallback = false;
            if (_disposeRequested)
            {
                CancelCollectionCore();
                CompleteDeferredInboxLifecycle();
                CompleteDeferredDispose();
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "Intent runtime disposal requested during reducer execution.");
                return false;
            }

            if (HasDeferredInboxLifecycle())
            {
                CancelCollectionCore();
                CompleteDeferredInboxLifecycle();
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "Intent reduction was cancelled by an Inbox lifecycle transition.");
                return false;
            }

            if (!_arbiter.MatchesGeneration(expectedGeneration))
            {
                CancelCollectionCore();
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentContribution,
                    "Intent frame generation changed during reduction.");
                return false;
            }

            return succeeded;
        }

        public bool CancelCollection()
        {
            if (_isDisposed || _isExecutingUserCallback || !_arbiter.IsCollecting)
            {
                return false;
            }

            CancelCollectionCore();
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isExecutingUserCallback)
            {
                _disposeRequested = true;
                return;
            }

            DisposeCore();
        }

        internal bool TryClaimInbox(ActorEventInboxCore inbox)
        {
            if (_isDisposed || _isExecutingUserCallback || _arbiter.IsCollecting ||
                inbox == null || inbox.Owner != GraphInstanceId)
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

        internal void ReleaseInbox(ActorEventInboxCore inbox)
        {
            if (ReferenceEquals(_inbox, inbox))
            {
                _inbox = null;
            }
        }

        internal bool TryResetForTimelineChange()
        {
            if (_isDisposed || _isExecutingUserCallback)
            {
                return false;
            }

            CancelCollectionCore();
            return true;
        }

        private void CancelCollectionCore()
        {
            _arbiter.CancelCollection();
            _frame.Invalidate();
        }

        private bool HasDeferredInboxLifecycle()
        {
            return _inbox != null && _inbox.HasDeferredLifecycle(this);
        }

        private void CompleteDeferredInboxLifecycle()
        {
            ActorEventInboxCore inbox = _inbox;
            inbox?.CompleteDeferredLifecycle(this);
        }

        private void CompleteDeferredDispose()
        {
            if (_disposeRequested && !_isDisposed && !_isExecutingUserCallback)
            {
                DisposeCore();
            }
        }

        private void DisposeCore()
        {
            if (_isDisposed)
            {
                return;
            }

            CancelCollectionCore();
            _isDisposed = true;
            _disposeRequested = false;
            _bindingsFrozen = false;
            Array.Clear(_bindingIdentities, 0, _bindingIdentities.Length);
            Array.Clear(_reservedContributions, 0, _reservedContributions.Length);
            _bindingCount = 0;
            _arbiter.ResetForDispose();
            ActorEventInboxCore inbox = _inbox;
            _inbox = null;
            inbox?.OnIntentRuntimeDisposed(this);
            _layout.ReleaseRuntime(GraphInstanceId, this);
        }

        private bool TryReserveBinding<TIntent>(
            IntentHandle<TIntent> handle,
            object sourceIdentity,
            int contributionReservation,
            out int registrationOrder,
            out int bindingToken,
            out CoCoDiagnostic diagnostic)
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

    internal sealed class IntentFrameArbiter
    {
        private readonly IntentFrameLayout _layout;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly IntentFrameRuntime _runtime;
        private readonly IIntentArbitrationLane[] _lanes;
        private StateFlowFrameHeader _header;
        private StateFlowFrameHeader _lastHeader;
        private bool _isCollecting;
        private bool _hasAcceptedHeader;
        private ulong _frameGeneration;

        public IntentFrameArbiter(
            IntentFrameLayout layout,
            GraphInstanceId graphInstanceId,
            IntentFrameRuntime runtime)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _graphInstanceId = graphInstanceId;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _lanes = layout.CreateArbitrationLanes(graphInstanceId);
        }

        public bool IsCollecting => _isCollecting;
        public ulong FrameGeneration => _frameGeneration;
        public bool IsGenerationCurrent(ulong expectedGeneration) =>
            _isCollecting && MatchesGeneration(expectedGeneration);

        public bool MatchesGeneration(ulong expectedGeneration) =>
            expectedGeneration != 0UL && _frameGeneration == expectedGeneration;

        public bool TryBegin(
            in StateFlowFrameHeader header,
            out CoCoDiagnostic diagnostic)
        {
            if (_isCollecting ||
                header.Identity.GraphInstanceId != _graphInstanceId ||
                !header.LayoutId.IsValid ||
                !header.TickFrame.IsValid ||
                header.LayoutId != _layout.LayoutId ||
                header.Identity.Kind != StateFlowFrameKind.Intent ||
                (_hasAcceptedHeader &&
                 !StateFlowTickOrder.IsStrictlyAfter(
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

        public IntentSourceSampleResult TrySample<TIntent>(
            IntentSourceBinding<TIntent> binding,
            in TickFrame tickFrame,
            ulong expectedGeneration)
        {
            if (!IsGenerationCurrent(expectedGeneration))
            {
                return IntentSourceSampleResult.ArbiterNotCollecting;
            }

            if (!binding.IsValid ||
                binding.GraphInstanceId != _graphInstanceId ||
                !_layout.Matches(binding.Requirement.Handle) ||
                tickFrame != _header.TickFrame)
            {
                return IntentSourceSampleResult.InvalidBinding;
            }

            if (!binding.TrySample(
                    expectedGeneration,
                    tickFrame,
                    out TIntent value,
                    out bool hasValue))
            {
                return IntentSourceSampleResult.AlreadySampled;
            }

            if (!IsGenerationCurrent(expectedGeneration))
            {
                return IntentSourceSampleResult.ArbiterNotCollecting;
            }

            if (!hasValue)
            {
                return IntentSourceSampleResult.NoValue;
            }

            var contribution = new IntentContribution<TIntent>(
                binding.Requirement.Handle,
                binding.Requirement.Priority,
                binding.RegistrationOrder,
                binding.BindingToken,
                _graphInstanceId,
                _header.Identity.TimelineEpoch,
                EventSequence.Zero,
                value);
            IntentContributionResult result = Contribute(binding.Requirement.Handle, contribution);
            return result == IntentContributionResult.Accepted
                ? IntentSourceSampleResult.Contributed
                : IntentSourceSampleResult.ContributionRejected;
        }

        public IntentContributionResult ContributeBatch<TIntent>(
            IntentHandle<TIntent> handle,
            IntentContribution<TIntent>[] contributions,
            int count,
            ulong expectedGeneration)
        {
            if (!IsGenerationCurrent(expectedGeneration))
            {
                return IntentContributionResult.ArbiterNotCollecting;
            }

            if (!_layout.Matches(handle) || contributions == null || count <= 0 || count > contributions.Length)
            {
                return IntentContributionResult.InvalidHandle;
            }

            var lane = _lanes[handle.DenseIndex] as IIntentArbitrationLane<TIntent>;
            return lane == null
                ? IntentContributionResult.InvalidHandle
                : lane.AddBatch(contributions, count);
        }

        public bool TryFreeze(
            IntentFrame destination,
            ulong expectedGeneration,
            out CoCoDiagnostic diagnostic)
        {
            if (!IsGenerationCurrent(expectedGeneration) || destination == null ||
                destination.GraphInstanceId != _graphInstanceId ||
                destination.LayoutId != _layout.LayoutId)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentContribution,
                    "Intent arbitration must be collecting and target its owned frame.");
                return false;
            }

            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].BeginReductionCheckpoint();
            }

            try
            {
                destination.Prepare(_header);
                for (int index = 0; index < _lanes.Length; index++)
                {
                    if (!_lanes[index].ReduceInto(destination.GetSlot(index), _runtime) ||
                        _runtime.IsReductionAbortRequested)
                    {
                        RollbackReduction(destination);
                        diagnostic = CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Lifecycle,
                            CoCoDiagnosticCode.InvalidLifecycleTransition,
                            "Intent reduction was interrupted by a lifecycle transition.");
                        return false;
                    }
                }

                destination.Seal();
                for (int index = 0; index < _lanes.Length; index++)
                {
                    _lanes[index].CommitReductionCheckpoint();
                }

                _isCollecting = false;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch
            {
                RollbackReduction(destination);
                throw;
            }
        }

        public void CancelCollection()
        {
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].Reset();
            }

            _isCollecting = false;
            _header = default;
        }

        public void ResetForDispose()
        {
            CancelCollection();
        }

        private void RollbackReduction(IntentFrame destination)
        {
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].RollbackReductionCheckpoint();
            }

            destination.Invalidate();
        }

        private IntentContributionResult Contribute<TIntent>(
            IntentHandle<TIntent> handle,
            in IntentContribution<TIntent> contribution)
        {
            if (!_layout.Matches(handle) || !contribution.IsValid || contribution.Handle != handle)
            {
                return IntentContributionResult.InvalidHandle;
            }

            var lane = _lanes[handle.DenseIndex] as IIntentArbitrationLane<TIntent>;
            return lane == null
                ? IntentContributionResult.InvalidHandle
                : lane.Add(contribution);
        }
    }

    internal interface IIntentDefinition
    {
        IntentDescriptor Descriptor { get; }
        IIntentFrameSlot CreateFrameSlot();
        IIntentArbitrationLane CreateArbitrationLane(GraphInstanceId graphInstanceId);
    }

    internal sealed class IntentDefinition<TIntent, TReducer> : IIntentDefinition
        where TReducer : unmanaged, IIntentReducer<TIntent>
    {
        private readonly IIntentReducerFactory<TIntent, TReducer> _reducerFactory;

        public IntentDefinition(
            IntentDescriptor descriptor,
            IIntentReducerFactory<TIntent, TReducer> reducerFactory)
        {
            Descriptor = descriptor;
            _reducerFactory = reducerFactory;
        }

        public IntentDescriptor Descriptor { get; }

        public IIntentFrameSlot CreateFrameSlot() => new IntentFrameSlot<TIntent>();

        public IIntentArbitrationLane CreateArbitrationLane(
            GraphInstanceId graphInstanceId)
        {
            TReducer reducer = _reducerFactory.Create(graphInstanceId);
            return new IntentArbitrationLane<TIntent, TReducer>(
                Descriptor.MaxContributions,
                reducer);
        }
    }

    internal interface IIntentFrameSlot
    {
        void Clear();
    }

    internal sealed class IntentFrameSlot<TIntent> : IIntentFrameSlot
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

    internal interface IIntentArbitrationLane
    {
        void Reset();
        void BeginReductionCheckpoint();
        bool ReduceInto(IIntentFrameSlot destination, IntentFrameRuntime runtime);
        void CommitReductionCheckpoint();
        void RollbackReductionCheckpoint();
    }

    internal interface IIntentArbitrationLane<TIntent> : IIntentArbitrationLane
    {
        IntentContributionResult Add(in IntentContribution<TIntent> contribution);
        IntentContributionResult AddBatch(IntentContribution<TIntent>[] contributions, int count);
    }

    internal sealed class IntentArbitrationLane<TIntent, TReducer> : IIntentArbitrationLane<TIntent>
        where TReducer : unmanaged, IIntentReducer<TIntent>
    {
        private readonly IntentContribution<TIntent>[] _contributions;
        private TReducer _reducer;
        private TReducer _reducerCheckpoint;
        private int _count;
        private bool _hasReductionCheckpoint;

        public IntentArbitrationLane(
            int maxContributions,
            TReducer reducer)
        {
            _contributions = new IntentContribution<TIntent>[maxContributions];
            _reducer = reducer;
        }

        public IntentContributionResult Add(in IntentContribution<TIntent> contribution)
        {
            if (ContainsDuplicate(contribution))
            {
                return IntentContributionResult.DuplicateContribution;
            }

            if (_count >= _contributions.Length)
            {
                return IntentContributionResult.CapacityExceeded;
            }

            Insert(contribution);
            return IntentContributionResult.Accepted;
        }

        public IntentContributionResult AddBatch(
            IntentContribution<TIntent>[] contributions,
            int count)
        {
            if (_count + count > _contributions.Length)
            {
                return IntentContributionResult.CapacityExceeded;
            }

            for (int index = 0; index < count; index++)
            {
                IntentContribution<TIntent> candidate = contributions[index];
                if (!candidate.IsValid || ContainsDuplicate(candidate))
                {
                    return candidate.IsValid
                        ? IntentContributionResult.DuplicateContribution
                        : IntentContributionResult.InvalidHandle;
                }

                for (int earlierIndex = 0; earlierIndex < index; earlierIndex++)
                {
                    if (IsDuplicate(candidate, contributions[earlierIndex]))
                    {
                        return IntentContributionResult.DuplicateContribution;
                    }
                }
            }

            for (int index = 0; index < count; index++)
            {
                Insert(contributions[index]);
            }

            return IntentContributionResult.Accepted;
        }

        public void Reset()
        {
            RollbackReductionCheckpoint();
            Array.Clear(_contributions, 0, _count);
            _count = 0;
        }

        public void BeginReductionCheckpoint()
        {
            _reducerCheckpoint = _reducer;
            _hasReductionCheckpoint = true;
        }

        public bool ReduceInto(
            IIntentFrameSlot destination,
            IntentFrameRuntime runtime)
        {
            if (_count == 0)
            {
                return true;
            }

            var typedDestination = (IntentFrameSlot<TIntent>)destination;
            TIntent result = _contributions[0].Value;
            for (int index = 1; index < _count; index++)
            {
                result = _reducer.Reduce(result, _contributions[index].Value);
                if (runtime.IsReductionAbortRequested)
                {
                    return false;
                }
            }

            typedDestination.Set(result);
            return true;
        }

        public void CommitReductionCheckpoint()
        {
            _reducerCheckpoint = default;
            _hasReductionCheckpoint = false;
        }

        public void RollbackReductionCheckpoint()
        {
            if (!_hasReductionCheckpoint)
            {
                return;
            }

            _reducer = _reducerCheckpoint;
            _reducerCheckpoint = default;
            _hasReductionCheckpoint = false;
        }

        private bool ContainsDuplicate(in IntentContribution<TIntent> contribution)
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

        private void Insert(in IntentContribution<TIntent> contribution)
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
            in IntentContribution<TIntent> left,
            in IntentContribution<TIntent> right)
        {
            return left.BindingToken == right.BindingToken &&
                   left.SourceGraphInstanceId == right.SourceGraphInstanceId &&
                   left.SourceTimelineEpoch == right.SourceTimelineEpoch &&
                   left.EventSequence == right.EventSequence;
        }

        private static bool ComesBefore(
            in IntentContribution<TIntent> left,
            in IntentContribution<TIntent> right)
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
