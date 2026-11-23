using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>
    /// Mandatory execution surface for every State logic instance.
    /// </summary>
    public interface IStateUpdate
    {
        void Update(StateExecutionContext context);
    }

    /// <summary>
    /// Optional first-Tick callback for a newly activated State.
    /// </summary>
    public interface IStateEnter
    {
        void OnEnter(StateExecutionContext context);
    }

    /// <summary>
    /// Optional last-Tick callback for a State that is leaving its ActivePath.
    /// </summary>
    public interface IStateExit
    {
        void OnExit(StateExecutionContext context);
    }

    /// <summary>
    /// Pure read-only Transition predicate. Conditions cannot request Transitions or write Operations.
    /// </summary>
    public interface IStateConditionEvaluator
    {
        bool Evaluate(ConditionEvaluationContext context);
    }

    public readonly struct TransitionHandle : IEquatable<TransitionHandle>
    {
        private readonly object _owner;

        internal TransitionHandle(
            object owner,
            int layerIndex,
            int transitionIndex,
            TransitionId transitionId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority)
        {
            _owner = owner;
            LayerIndex = layerIndex;
            TransitionIndex = transitionIndex;
            TransitionId = transitionId;
            SourceStateId = sourceStateId;
            TargetStateId = targetStateId;
            Priority = priority;
        }

        public TransitionId TransitionId { get; }
        public StateId SourceStateId { get; }
        public StateId TargetStateId { get; }
        public int Priority { get; }
        public bool IsValid => _owner != null &&
                               LayerIndex >= 0 &&
                               TransitionIndex >= 0 &&
                               TransitionId.IsValid &&
                               SourceStateId.IsValid &&
                               TargetStateId.IsValid;

        internal int LayerIndex { get; }
        internal int TransitionIndex { get; }

        internal bool IsOwnedBy(object owner) => ReferenceEquals(_owner, owner);

        public bool Equals(TransitionHandle other) =>
            ReferenceEquals(_owner, other._owner) &&
            LayerIndex == other.LayerIndex &&
            TransitionIndex == other.TransitionIndex &&
            TransitionId == other.TransitionId;

        public override bool Equals(object obj) => obj is TransitionHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayerIndex;
                hashCode = (hashCode * 397) ^ TransitionIndex;
                hashCode = (hashCode * 397) ^ TransitionId.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(TransitionHandle left, TransitionHandle right) => left.Equals(right);
        public static bool operator !=(TransitionHandle left, TransitionHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Runtime-owned error latch for the currently executing State callback. Every State context
    /// shares this preallocated lease so an escaped writer from an older callback cannot fail
    /// silently while another callback is active.
    /// </summary>
    internal sealed class StateCallbackOperationLease
    {
        private bool _isActive;
        private bool _hasError;

        public bool HasError => _isActive && _hasError;

        public bool TryBegin()
        {
            if (_isActive)
            {
                return false;
            }

            _hasError = false;
            _isActive = true;
            return true;
        }

        public void ReportInvalidUse()
        {
            if (_isActive)
            {
                _hasError = true;
            }
        }

        public void End()
        {
            _isActive = false;
            _hasError = false;
        }
    }

    /// <summary>
    /// Callback-scoped, fixed-rank Operation writer supplied to one State callback.
    /// Callers cannot raise their own rank or write undeclared Operation Sections.
    /// </summary>
    public readonly struct StateOperationWriter
    {
        private readonly StateExecutionContext _context;
        private readonly ulong _callbackToken;

        internal StateOperationWriter(
            StateExecutionContext context,
            ulong callbackToken)
        {
            _context = context;
            _callbackToken = callbackToken;
        }

        public bool IsValid =>
            _context != null && _context.IsOperationWriterValid(_callbackToken);

        public bool Write<TValue>(
            OperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return _context != null &&
                   _context.TryWriteOperation(_callbackToken, field, value);
        }

        /// <summary>
        /// Resolves a Section field by its stable dense index for writing.
        /// Standard-path State logics use the constants published by the
        /// Section contract instead of constructor-injected handles. Write
        /// still fails unless this State declares the Section as provided.
        /// </summary>
        public OperationSectionField<TValue> ResolveField<TSection, TValue>(
            int fieldIndex)
            where TSection : class, IOperationSection
            where TValue : unmanaged
        {
            return _context != null &&
                   _context.TryResolveOperationField<TSection, TValue>(
                       _callbackToken,
                       fieldIndex,
                       out OperationSectionField<TValue> field)
                ? field
                : default;
        }

        public bool EnableDiscrete<TSection>(OperationSectionHandle<TSection> handle)
            where TSection : class, IOperationSection
        {
            return _context != null &&
                   _context.TryEnableDiscreteOperation(_callbackToken, handle);
        }

        /// <summary>
        /// Resolves a discrete section handle by its interface type, then
        /// enables it for this tick — the standard-path way to fire a
        /// discrete section (triggers, one-shots) without an injected
        /// requirement.
        /// </summary>
        public bool TryEnableDiscrete<TSection>()
            where TSection : class, IOperationSection
        {
            return _context != null &&
                   _context.TryResolveAndEnableDiscreteOperation<TSection>(
                       _callbackToken);
        }
    }

    public sealed class StateFactoryContext
    {
        private readonly TransitionHandle[] _transitions;
        private readonly IReadOnlyList<TransitionHandle> _readOnlyTransitions;

        internal StateFactoryContext(
            GraphInstanceId graphInstanceId,
            LayerId layerId,
            int layerIndex,
            StateId stateId,
            int pathDepth,
            FrozenConfigSnapshot config,
            TransitionHandle[] transitions)
        {
            GraphInstanceId = graphInstanceId;
            LayerId = layerId;
            LayerIndex = layerIndex;
            StateId = stateId;
            PathDepth = pathDepth;
            Config = config;
            _transitions = transitions ?? Array.Empty<TransitionHandle>();
            _readOnlyTransitions = Array.AsReadOnly(_transitions);
        }

        public GraphInstanceId GraphInstanceId { get; }
        public LayerId LayerId { get; }
        public int LayerIndex { get; }
        public StateId StateId { get; }
        public int PathDepth { get; }
        public FrozenConfigSnapshot Config { get; }
        public IReadOnlyList<TransitionHandle> OutgoingTransitions => _readOnlyTransitions;

        public bool TryGetTransition(TransitionId transitionId, out TransitionHandle handle)
        {
            for (int index = 0; index < _transitions.Length; index++)
            {
                if (_transitions[index].TransitionId == transitionId)
                {
                    handle = _transitions[index];
                    return true;
                }
            }

            handle = default;
            return false;
        }
    }

    public sealed class ConditionFactoryContext
    {
        internal ConditionFactoryContext(
            GraphInstanceId graphInstanceId,
            LayerId layerId,
            TransitionId transitionId,
            int authoringIndex,
            FrozenConfigSnapshot config)
        {
            GraphInstanceId = graphInstanceId;
            LayerId = layerId;
            TransitionId = transitionId;
            AuthoringIndex = authoringIndex;
            Config = config;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public LayerId LayerId { get; }
        public TransitionId TransitionId { get; }
        public int AuthoringIndex { get; }
        public FrozenConfigSnapshot Config { get; }
    }

    public sealed class StateExecutionContext : IOperationSectionWriteTarget
    {
        private StateGraphRuntime _runtime;
        private ActivationMemory _memory;
        private FrozenConfigSnapshot _config;
        private IIntentFrame _intents;
        private StateEventInbox _eventInbox;
        private IStateEventOutbox _eventOutbox;
        private ContextFrameReadView _previousContext;
        private TickFrame _tickFrame;
        private OperationFrameWriter _operationWriter;
        private OperationWriteRank _operationRank;
        private bool[] _allowedOperationSections;
        private StateCallbackOperationLease _operationLease;
        private LayerId _layerId;
        private StateId _stateId;
        private StateId _activeLeafStateId;
        private ActivationId _activationId;
        private double _previousLocalSeconds;
        private double _localSeconds;
        private double _previousActionProgress;
        private double _actionProgress;
        private bool _canRequestTransition;
        private bool _canProvideActionProgress;
        private bool _hasError;
        private bool _isCallbackActive;
        private ulong _callbackGeneration;
        private TransitionHandle[] _outgoingTransitions;
        private IReadOnlyList<TransitionHandle> _readOnlyOutgoingTransitions;

        internal StateExecutionContext()
        {
        }

        public TickFrame TickFrame => _tickFrame;
        public IIntentFrame Intents => _intents;
        public StateEventReadView Inbox => new StateEventReadView(this, _callbackGeneration);
        public StateEventOutboxWriter Outbox => new StateEventOutboxWriter(this, _callbackGeneration);
        public ContextFrameReadView PreviousContext => _previousContext;
        public FrozenConfigSnapshot Config => _config;
        public StateOperationWriter Operations => _isCallbackActive
            ? new StateOperationWriter(this, _callbackGeneration)
            : default;
        public LayerId LayerId => _layerId;
        public StateId StateId => _stateId;
        /// <summary>The ordinary leaf whose lifecycle a Transition changes.</summary>
        public StateId ActiveLeafStateId => _activeLeafStateId;
        public ActivationId ActivationId => _activationId;
        public double PreviousLocalSeconds => _previousLocalSeconds;
        public double LocalSeconds => _localSeconds;
        public double PreviousActionProgress => _previousActionProgress;
        public double ActionProgress => _actionProgress;

        public TMemory Memory<TMemory>()
            where TMemory : ActivationMemory
        {
            if (_memory is TMemory typed)
            {
                return typed;
            }

            _hasError = true;
            return null;
        }

        /// <summary>
        /// Outgoing transitions of this state in declaration order —
        /// the standard-path way for an Update callback to request a
        /// transition without constructor-injected handles. Attached
        /// once when the runtime builds the layer.
        /// </summary>
        public IReadOnlyList<TransitionHandle> OutgoingTransitions =>
            _readOnlyOutgoingTransitions;

        internal void AttachOutgoingTransitions(
            TransitionHandle[] transitions)
        {
            _outgoingTransitions = transitions;
            _readOnlyOutgoingTransitions = transitions == null
                ? null
                : Array.AsReadOnly(transitions);
        }

        /// <summary>
        /// Requests the outgoing transition whose target state runs the
        /// given logic type — the standard-path way to address transitions
        /// on authored graphs, whose edge ids are generated Guids (D74
        /// name-addressing, typed form). At most one edge per source may
        /// target a given logic.
        /// </summary>
        public bool TryRequestTransitionTo<TTargetLogic>()
            where TTargetLogic : StateLogic
        {
            for (int index = 0; index < _outgoingTransitions.Length; index++)
            {
                TransitionHandle handle = _outgoingTransitions[index];
                if (_runtime != null &&
                    _runtime.TryGetStateLogicType(
                        handle.TargetStateId,
                        out Type logicType) &&
                    logicType == typeof(TTargetLogic))
                {
                    return RequestTransition(handle);
                }
            }

            return false;
        }

        public bool RequestTransition(TransitionHandle handle)
        {
            if (!_canRequestTransition ||
                _runtime == null ||
                !_runtime.TryRequestTransition(handle, _stateId))
            {
                _hasError = true;
                return false;
            }

            return true;
        }

        public bool TrySetActionProgress(double value)
        {
            if (!_canProvideActionProgress ||
                double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < _actionProgress ||
                value < 0d ||
                value > 1d)
            {
                _hasError = true;
                return false;
            }

            _actionProgress = value;
            return true;
        }

        internal double ResultActionProgress => _actionProgress;
        internal bool HasError => _hasError;

        internal bool IsOperationWriterValid(ulong callbackToken) =>
            _isCallbackActive &&
            callbackToken != 0UL &&
            callbackToken == _callbackGeneration &&
            _operationWriter.IsValid &&
            _operationRank.IsValid &&
            _activationId.IsValid;

        internal bool TryResolveOperationField<TSection, TValue>(
            ulong callbackToken,
            int fieldIndex,
            out OperationSectionField<TValue> field)
            where TSection : class, IOperationSection
            where TValue : unmanaged
        {
            field = default;
            if (!IsOperationWriterValid(callbackToken))
            {
                return false;
            }

            field = _operationWriter.ResolveField<TSection, TValue>(fieldIndex);
            return field.IsValid;
        }

        void IOperationSectionWriteTarget.Write<TValue>(
            OperationSectionField<TValue> field, in TValue value) =>
            TryWriteOperation(_callbackGeneration, field, value);

        void IOperationSectionWriteTarget.Enable<TSection>() =>
            TryResolveAndEnableDiscreteOperation<TSection>(_callbackGeneration);

        internal bool TryWriteOperation<TValue>(
            ulong callbackToken,
            OperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !IsOperationSectionAllowed(field.SectionIndex) ||
                !_operationWriter.Write(_operationRank, field, value))
            {
                MarkInvalidOperationUse();
                return false;
            }

            return true;
        }

        internal bool TryResolveAndEnableDiscreteOperation<TSection>(
            ulong callbackToken)
            where TSection : class, IOperationSection
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !_operationWriter.TryResolveTypedHandle(
                    out OperationSectionHandle<TSection> handle) ||
                !IsOperationSectionAllowed(handle.DenseIndex) ||
                !_operationWriter.EnableDiscrete(_operationRank, handle, _activationId))
            {
                MarkInvalidOperationUse();
                return false;
            }

            return true;
        }

        internal bool TryEnableDiscreteOperation<TSection>(
            ulong callbackToken,
            OperationSectionHandle<TSection> handle)
            where TSection : class, IOperationSection
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !IsOperationSectionAllowed(handle.DenseIndex) ||
                !_operationWriter.EnableDiscrete(_operationRank, handle, _activationId))
            {
                MarkInvalidOperationUse();
                return false;
            }

            return true;
        }

        internal bool IsEventCallbackCurrent(ulong generation) =>
            _isCallbackActive && generation == _callbackGeneration;

        internal bool TryGetEventBatch<TEvent>(ulong generation, out StateEventBatch<TEvent> batch)
            where TEvent : unmanaged
        {
            batch = default;
            if (!IsEventCallbackCurrent(generation)) return false;
            var lane = _eventInbox?.Find<TEvent>();
            if (lane == null) return false;
            batch = new StateEventBatch<TEvent>(this, generation, lane);
            return true;
        }

        internal void PublishEvent<TEvent>(ulong generation, in TEvent payload) where TEvent : unmanaged
        {
            if (!IsEventCallbackCurrent(generation) || _eventOutbox == null)
            {
                if (_isCallbackActive) _hasError = true;
                throw new InvalidOperationException("State event writer is outside its callback.");
            }
            _eventOutbox.Publish(in payload);
        }

        internal void AttachEvents(StateEventInbox inbox, IStateEventOutbox outbox)
        {
            _eventInbox = inbox;
            _eventOutbox = outbox;
        }

        internal void Prepare(
            StateGraphRuntime runtime,
            ActivationMemory memory,
            FrozenConfigSnapshot config,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            in TickFrame tickFrame,
            OperationFrameWriter operationWriter,
            OperationWriteRank operationRank,
            bool[] allowedOperationSections,
            StateCallbackOperationLease operationLease,
            LayerId layerId,
            StateId stateId,
            StateId activeLeafStateId,
            ActivationId activationId,
            double previousLocalSeconds,
            double localSeconds,
            double previousActionProgress,
            double actionProgress,
            bool canRequestTransition,
            bool canProvideActionProgress)
        {
            _runtime = runtime;
            _memory = memory;
            _config = config;
            _intents = intents;
            _previousContext = previousContext;
            _tickFrame = tickFrame;
            _operationWriter = operationWriter;
            _operationRank = operationRank;
            _allowedOperationSections = allowedOperationSections;
            _operationLease = operationLease;
            _layerId = layerId;
            _stateId = stateId;
            _activeLeafStateId = activeLeafStateId;
            _activationId = activationId;
            _previousLocalSeconds = previousLocalSeconds;
            _localSeconds = localSeconds;
            _previousActionProgress = previousActionProgress;
            _actionProgress = actionProgress;
            _canRequestTransition = canRequestTransition;
            _canProvideActionProgress = canProvideActionProgress;
            _hasError = false;
            _callbackGeneration = _callbackGeneration == ulong.MaxValue
                ? 1UL
                : _callbackGeneration + 1UL;
            _isCallbackActive = true;
        }

        internal void Clear()
        {
            _isCallbackActive = false;
            _runtime = null;
            _memory = null;
            _config = null;
            _intents = null;
            _eventInbox = null;
            _eventOutbox = null;
            _previousContext = default;
            _tickFrame = default;
            _operationWriter = default;
            _operationRank = default;
            _allowedOperationSections = null;
            _layerId = default;
            _stateId = default;
            _activeLeafStateId = default;
            _activationId = default;
            _previousLocalSeconds = 0d;
            _localSeconds = 0d;
            _previousActionProgress = 0d;
            _actionProgress = 0d;
            _canRequestTransition = false;
            _canProvideActionProgress = false;
            _hasError = false;
        }

        private bool IsOperationSectionAllowed(int denseIndex) =>
            _allowedOperationSections != null &&
            denseIndex >= 0 &&
            denseIndex < _allowedOperationSections.Length &&
            _allowedOperationSections[denseIndex];

        private void MarkInvalidOperationUse()
        {
            if (_isCallbackActive)
            {
                _hasError = true;
            }

            _operationLease?.ReportInvalidUse();
        }
    }

    public sealed class ConditionEvaluationContext
    {
        internal ConditionEvaluationContext()
        {
        }

        public TickFrame TickFrame { get; private set; }
        public IIntentFrame Intents { get; private set; }
        public ContextFrameReadView PreviousContext { get; private set; }
        public FrozenConfigSnapshot Config { get; private set; }
        public LayerId LayerId { get; private set; }
        public StateId SourceStateId { get; private set; }
        public StateId ActiveLeafStateId { get; private set; }
        public TransitionId TransitionId { get; private set; }
        public double PreviousLocalSeconds { get; private set; }
        public double LocalSeconds { get; private set; }
        public double PreviousActionProgress { get; private set; }
        public double ActionProgress { get; private set; }

        internal void Prepare(
            in TickFrame tickFrame,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            FrozenConfigSnapshot config,
            LayerId layerId,
            StateId sourceStateId,
            StateId activeLeafStateId,
            TransitionId transitionId,
            double previousLocalSeconds,
            double localSeconds,
            double previousActionProgress,
            double actionProgress)
        {
            TickFrame = tickFrame;
            Intents = intents;
            PreviousContext = previousContext;
            Config = config;
            LayerId = layerId;
            SourceStateId = sourceStateId;
            ActiveLeafStateId = activeLeafStateId;
            TransitionId = transitionId;
            PreviousLocalSeconds = previousLocalSeconds;
            LocalSeconds = localSeconds;
            PreviousActionProgress = previousActionProgress;
            ActionProgress = actionProgress;
        }

        internal void Clear()
        {
            TickFrame = default;
            Intents = null;
            PreviousContext = default;
            Config = null;
            LayerId = default;
            SourceStateId = default;
            ActiveLeafStateId = default;
            TransitionId = default;
            PreviousLocalSeconds = 0d;
            LocalSeconds = 0d;
            PreviousActionProgress = 0d;
            ActionProgress = 0d;
        }
    }
}
