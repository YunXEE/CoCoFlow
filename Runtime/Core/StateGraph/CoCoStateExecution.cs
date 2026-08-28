using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Mandatory execution surface for every State logic instance.
    /// </summary>
    public interface ICoCoStateUpdate
    {
        void Update(CoCoStateExecutionContext context);
    }

    /// <summary>
    /// Optional first-Tick callback for a newly activated State.
    /// </summary>
    public interface ICoCoStateEnter
    {
        void OnEnter(CoCoStateExecutionContext context);
    }

    /// <summary>
    /// Optional last-Tick callback for a State that is leaving its ActivePath.
    /// </summary>
    public interface ICoCoStateExit
    {
        void OnExit(CoCoStateExecutionContext context);
    }

    /// <summary>
    /// Pure read-only Transition predicate. Conditions cannot request Transitions or write Operations.
    /// </summary>
    public interface ICoCoStateConditionEvaluator
    {
        bool Evaluate(CoCoConditionEvaluationContext context);
    }

    public readonly struct CoCoTransitionHandle : IEquatable<CoCoTransitionHandle>
    {
        private readonly object _owner;

        internal CoCoTransitionHandle(
            object owner,
            int layerIndex,
            int transitionIndex,
            CoCoTransitionId transitionId,
            CoCoStateId sourceStateId,
            CoCoStateId targetStateId,
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

        public CoCoTransitionId TransitionId { get; }
        public CoCoStateId SourceStateId { get; }
        public CoCoStateId TargetStateId { get; }
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

        public bool Equals(CoCoTransitionHandle other) =>
            ReferenceEquals(_owner, other._owner) &&
            LayerIndex == other.LayerIndex &&
            TransitionIndex == other.TransitionIndex &&
            TransitionId == other.TransitionId;

        public override bool Equals(object obj) => obj is CoCoTransitionHandle other && Equals(other);

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

        public static bool operator ==(CoCoTransitionHandle left, CoCoTransitionHandle right) => left.Equals(right);
        public static bool operator !=(CoCoTransitionHandle left, CoCoTransitionHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Runtime-owned error latch for the currently executing State callback. Every State context
    /// shares this preallocated lease so an escaped writer from an older callback cannot fail
    /// silently while another callback is active.
    /// </summary>
    internal sealed class CoCoStateCallbackOperationLease
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
    public readonly struct CoCoStateOperationWriter
    {
        private readonly CoCoStateExecutionContext _context;
        private readonly ulong _callbackToken;

        internal CoCoStateOperationWriter(
            CoCoStateExecutionContext context,
            ulong callbackToken)
        {
            _context = context;
            _callbackToken = callbackToken;
        }

        public bool IsValid =>
            _context != null && _context.IsOperationWriterValid(_callbackToken);

        public bool Write<TValue>(
            CoCoOperationSectionField<TValue> field,
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
        public CoCoOperationSectionField<TValue> ResolveField<TSection, TValue>(
            int fieldIndex)
            where TSection : class, ICoCoOperationSection
            where TValue : unmanaged
        {
            return _context != null &&
                   _context.TryResolveOperationField<TSection, TValue>(
                       _callbackToken,
                       fieldIndex,
                       out CoCoOperationSectionField<TValue> field)
                ? field
                : default;
        }

        public bool EnableDiscrete<TSection>(CoCoOperationSectionHandle<TSection> handle)
            where TSection : class, ICoCoOperationSection
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
            where TSection : class, ICoCoOperationSection
        {
            return _context != null &&
                   _context.TryResolveAndEnableDiscreteOperation<TSection>(
                       _callbackToken);
        }
    }

    public sealed class CoCoStateFactoryContext
    {
        private readonly CoCoTransitionHandle[] _transitions;
        private readonly IReadOnlyList<CoCoTransitionHandle> _readOnlyTransitions;

        internal CoCoStateFactoryContext(
            CoCoGraphInstanceId graphInstanceId,
            CoCoLayerId layerId,
            int layerIndex,
            CoCoStateId stateId,
            int pathDepth,
            CoCoFrozenConfigSnapshot config,
            CoCoTransitionHandle[] transitions)
        {
            GraphInstanceId = graphInstanceId;
            LayerId = layerId;
            LayerIndex = layerIndex;
            StateId = stateId;
            PathDepth = pathDepth;
            Config = config;
            _transitions = transitions ?? Array.Empty<CoCoTransitionHandle>();
            _readOnlyTransitions = Array.AsReadOnly(_transitions);
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoLayerId LayerId { get; }
        public int LayerIndex { get; }
        public CoCoStateId StateId { get; }
        public int PathDepth { get; }
        public CoCoFrozenConfigSnapshot Config { get; }
        public IReadOnlyList<CoCoTransitionHandle> OutgoingTransitions => _readOnlyTransitions;

        public bool TryGetTransition(CoCoTransitionId transitionId, out CoCoTransitionHandle handle)
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

    public sealed class CoCoConditionFactoryContext
    {
        internal CoCoConditionFactoryContext(
            CoCoGraphInstanceId graphInstanceId,
            CoCoLayerId layerId,
            CoCoTransitionId transitionId,
            int authoringIndex,
            CoCoFrozenConfigSnapshot config)
        {
            GraphInstanceId = graphInstanceId;
            LayerId = layerId;
            TransitionId = transitionId;
            AuthoringIndex = authoringIndex;
            Config = config;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoLayerId LayerId { get; }
        public CoCoTransitionId TransitionId { get; }
        public int AuthoringIndex { get; }
        public CoCoFrozenConfigSnapshot Config { get; }
    }

    public sealed class CoCoStateExecutionContext
    {
        private CoCoStateGraphRuntime _runtime;
        private CoCoActivationMemory _memory;
        private CoCoFrozenConfigSnapshot _config;
        private ICoCoIntentFrame _intents;
        private CoCoContextFrameReadView _previousContext;
        private CoCoTickFrame _tickFrame;
        private CoCoOperationFrameWriter _operationWriter;
        private CoCoOperationWriteRank _operationRank;
        private bool[] _allowedOperationSections;
        private CoCoStateCallbackOperationLease _operationLease;
        private CoCoLayerId _layerId;
        private CoCoStateId _stateId;
        private CoCoActivationId _activationId;
        private double _previousLocalSeconds;
        private double _localSeconds;
        private double _previousActionProgress;
        private double _actionProgress;
        private bool _canRequestTransition;
        private bool _canProvideActionProgress;
        private bool _hasError;
        private bool _isCallbackActive;
        private ulong _callbackGeneration;
        private CoCoTransitionHandle[] _outgoingTransitions;
        private IReadOnlyList<CoCoTransitionHandle> _readOnlyOutgoingTransitions;

        internal CoCoStateExecutionContext()
        {
        }

        public CoCoTickFrame TickFrame => _tickFrame;
        public ICoCoIntentFrame Intents => _intents;
        public CoCoContextFrameReadView PreviousContext => _previousContext;
        public CoCoFrozenConfigSnapshot Config => _config;
        public CoCoStateOperationWriter Operations => _isCallbackActive
            ? new CoCoStateOperationWriter(this, _callbackGeneration)
            : default;
        public CoCoLayerId LayerId => _layerId;
        public CoCoStateId StateId => _stateId;
        public CoCoActivationId ActivationId => _activationId;
        public double PreviousLocalSeconds => _previousLocalSeconds;
        public double LocalSeconds => _localSeconds;
        public double PreviousActionProgress => _previousActionProgress;
        public double ActionProgress => _actionProgress;

        public TMemory Memory<TMemory>()
            where TMemory : CoCoActivationMemory
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
        public IReadOnlyList<CoCoTransitionHandle> OutgoingTransitions =>
            _readOnlyOutgoingTransitions;

        internal void AttachOutgoingTransitions(
            CoCoTransitionHandle[] transitions)
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
            where TTargetLogic : CoCoStateLogic
        {
            for (int index = 0; index < _outgoingTransitions.Length; index++)
            {
                CoCoTransitionHandle handle = _outgoingTransitions[index];
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

        public bool RequestTransition(CoCoTransitionHandle handle)
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
            out CoCoOperationSectionField<TValue> field)
            where TSection : class, ICoCoOperationSection
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

        internal bool TryWriteOperation<TValue>(
            ulong callbackToken,
            CoCoOperationSectionField<TValue> field,
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
            where TSection : class, ICoCoOperationSection
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !_operationWriter.TryResolveTypedHandle(
                    out CoCoOperationSectionHandle<TSection> handle) ||
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
            CoCoOperationSectionHandle<TSection> handle)
            where TSection : class, ICoCoOperationSection
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

        internal void Prepare(
            CoCoStateGraphRuntime runtime,
            CoCoActivationMemory memory,
            CoCoFrozenConfigSnapshot config,
            ICoCoIntentFrame intents,
            in CoCoContextFrameReadView previousContext,
            in CoCoTickFrame tickFrame,
            CoCoOperationFrameWriter operationWriter,
            CoCoOperationWriteRank operationRank,
            bool[] allowedOperationSections,
            CoCoStateCallbackOperationLease operationLease,
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoActivationId activationId,
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
            _previousContext = default;
            _tickFrame = default;
            _operationWriter = default;
            _operationRank = default;
            _allowedOperationSections = null;
            _layerId = default;
            _stateId = default;
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

    public sealed class CoCoConditionEvaluationContext
    {
        internal CoCoConditionEvaluationContext()
        {
        }

        public CoCoTickFrame TickFrame { get; private set; }
        public ICoCoIntentFrame Intents { get; private set; }
        public CoCoContextFrameReadView PreviousContext { get; private set; }
        public CoCoFrozenConfigSnapshot Config { get; private set; }
        public CoCoLayerId LayerId { get; private set; }
        public CoCoStateId SourceStateId { get; private set; }
        public CoCoTransitionId TransitionId { get; private set; }
        public double PreviousLocalSeconds { get; private set; }
        public double LocalSeconds { get; private set; }
        public double PreviousActionProgress { get; private set; }
        public double ActionProgress { get; private set; }

        internal void Prepare(
            in CoCoTickFrame tickFrame,
            ICoCoIntentFrame intents,
            in CoCoContextFrameReadView previousContext,
            CoCoFrozenConfigSnapshot config,
            CoCoLayerId layerId,
            CoCoStateId sourceStateId,
            CoCoTransitionId transitionId,
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
            TransitionId = default;
            PreviousLocalSeconds = 0d;
            LocalSeconds = 0d;
            PreviousActionProgress = 0d;
            ActionProgress = 0d;
        }
    }
}
