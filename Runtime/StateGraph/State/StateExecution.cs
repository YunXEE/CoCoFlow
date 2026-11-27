using System;
using System.Collections.Generic;

// Instance 在进入、更新与退出回调时填入本步数据，State 通过这里读上一提交、写输出并请求已有的边。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>为玩法提供每步回调，实例在当前活动路径或层的 AnyState 更新时调用。</summary>
    public interface IStateUpdate
    {
        /// <summary>实例在当前步调用，用上下文读取输入、更新激活记忆或请求已有出边。候选通过整步屏障后才提交。</summary>
        void Update(StateExecutionContext context);
    }

    /// <summary>允许普通状态在首次激活时初始化本次进入所需的玩法状态。</summary>
    public interface IStateEnter
    {
        /// <summary>在普通状态新激活的进入回调中执行，传入的收发件及输出视图仅在此次回调有效。</summary>
        void OnEnter(StateExecutionContext context);
    }

    /// <summary>允许普通状态在退出活动路径前处理最后一次回调。</summary>
    public interface IStateExit
    {
        /// <summary>在普通状态离开活动路径时执行，供玩法在激活结束前处理退出。</summary>
        void OnExit(StateExecutionContext context);
    }

    /// <summary>把一条出边绑定到实际运行实例、所属层及源节点，供玩法请求切换。</summary>
    public readonly struct TransitionHandle : IEquatable<TransitionHandle>
    {
        private readonly object _owner;

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

        #region Public API

        /// <summary>按<c>owner</c>、<c>LayerIndex</c>、<c>TransitionIndex</c>、<c>TransitionId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TransitionHandle other) =>
            ReferenceEquals(_owner, other._owner) &&
            LayerIndex == other.LayerIndex &&
            TransitionIndex == other.TransitionIndex &&
            TransitionId == other.TransitionId;

        /// <summary>仅当对象属于同类型并且<c>owner</c>、<c>LayerIndex</c>、<c>TransitionIndex</c>、<c>TransitionId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TransitionHandle other && Equals(other);

        /// <summary>为哈希集合计算与<c>owner</c>、<c>LayerIndex</c>、<c>TransitionIndex</c>、<c>TransitionId</c>的相等判定一致的散列值。</summary>
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

        /// <summary>比较两端的<c>owner</c>、<c>LayerIndex</c>、<c>TransitionIndex</c>、<c>TransitionId</c>，相等时返回 true。</summary>
        public static bool operator ==(TransitionHandle left, TransitionHandle right) => left.Equals(right);
        /// <summary>比较两端的<c>owner</c>、<c>LayerIndex</c>、<c>TransitionIndex</c>、<c>TransitionId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(TransitionHandle left, TransitionHandle right) => !left.Equals(right);

        #endregion

        #region Internal Logic

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

        internal bool IsOwnedBy(object owner) => ReferenceEquals(_owner, owner);

        #endregion
    }

    /// <summary>记录当前回调的写入期限，过期写入会使正在执行的候选失败。</summary>
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

    /// <summary>让已绑定的回调以框架确定的顺序写入本状态声明过的输出。</summary>
    internal readonly struct StateOperationWriter
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
            OperationField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return _context != null &&
                   _context.TryWriteOperation(_callbackToken, field, value);
        }

                public OperationField<TValue> ResolveField<TOperation, TValue>(
            int fieldIndex)
            where TOperation : class, IOperation
            where TValue : unmanaged
        {
            return _context != null &&
                   _context.TryResolveOperationField<TOperation, TValue>(
                       _callbackToken,
                       fieldIndex,
                       out OperationField<TValue> field)
                ? field
                : default;
        }

        public bool EnableDiscrete<TOperation>(OperationHandle<TOperation> handle)
            where TOperation : class, IOperation
        {
            return _context != null &&
                   _context.TryEnableDiscreteOperation(_callbackToken, handle);
        }

                public bool TryEnableDiscrete<TOperation>()
            where TOperation : class, IOperation
        {
            return _context != null &&
                   _context.TryResolveAndEnableDiscreteOperation<TOperation>(
                       _callbackToken);
        }
    }

    /// <summary>将按目标 Logic 查询、出边查找和请求登记交给当前实例。</summary>
    internal interface IStateTransitionRequests
    {
        bool TryGetStateLogicType(StateId stateId, out Type logicType);
        bool TryRequestTransition(TransitionHandle handle, StateId source);
    }

    /// <summary>在一次玩法回调内提供当前输入、上一份 Context、收发件与出边请求。</summary>
    public sealed class StateExecutionContext : IOperationWriteTarget
    {
        private IStateTransitionRequests _transitions;
        private StateActivationMemory _memory;
        private FrozenConfigSnapshot _config;
        private IIntentFrame _intents;
        private IStateEventInboxReader _eventInbox;
        private IStateEventOutbox _eventOutbox;
        private ContextFrameReadView _previousContext;
        private TickFrame _tickFrame;
        private OperationFrameWriter _operationWriter;
        private OperationWriteRank _operationRank;
        private bool[] _allowedOperations;
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

        public TickFrame TickFrame => _tickFrame;
        public IIntentFrame Intents => _intents;
        public StateEventReadView Inbox => new StateEventReadView(this, _callbackGeneration);
        public StateEventOutboxWriter Outbox => new StateEventOutboxWriter(this, _callbackGeneration);
        public ContextFrameReadView PreviousContext => _previousContext;
        public FrozenConfigSnapshot Config => _config;
        internal StateOperationWriter Operations => _isCallbackActive
            ? new StateOperationWriter(this, _callbackGeneration)
            : default;
        public LayerId LayerId => _layerId;
        public StateId StateId => _stateId;
                public StateId ActiveLeafStateId => _activeLeafStateId;
        public ActivationId ActivationId => _activationId;
        public double PreviousLocalSeconds => _previousLocalSeconds;
        public double LocalSeconds => _localSeconds;
        public double PreviousActionProgress => _previousActionProgress;
        public double ActionProgress => _actionProgress;

                public IReadOnlyList<TransitionHandle> OutgoingTransitions =>
            _readOnlyOutgoingTransitions;

        internal double ResultActionProgress => _actionProgress;
        internal bool HasError => _hasError;

        #region Public API

        /// <summary>从当前激活取得指定记忆类型。类型不匹配时返回 null 并标记本步错误。</summary>
        public TMemory Memory<TMemory>()
            where TMemory : StateActivationMemory
        {
            if (_memory is TMemory typed)
            {
                return typed;
            }

            _hasError = true;
            return null;
        }

        /// <summary>向当前状态匹配目标 Logic 的出边登记请求，找不到匹配边时返回 false。实际切换取决于本步仲裁与提交。</summary>
        public bool TryRequestTransitionTo<TTargetLogic>()
            where TTargetLogic : StateLogic
        {
            for (int index = 0; index < _outgoingTransitions.Length; index++)
            {
                TransitionHandle handle = _outgoingTransitions[index];
                if (_transitions != null &&
                    _transitions.TryGetStateLogicType(
                        handle.TargetStateId,
                        out Type logicType) &&
                    logicType == typeof(TTargetLogic))
                {
                    return RequestTransition(handle);
                }
            }

            return false;
        }

        /// <summary>在允许请求的回调中登记当前节点的出边。句柄过期、属于其他实例或源节点不符时返回 false 并标记本步错误。</summary>
        public bool RequestTransition(TransitionHandle handle)
        {
            if (!_canRequestTransition ||
                _transitions == null ||
                !_transitions.TryRequestTransition(handle, _stateId))
            {
                _hasError = true;
                return false;
            }

            return true;
        }

        /// <summary>在声明提供进度的回调中写入零到一之间的有限值，回退或越界时返回 false 并使候选失败。</summary>
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

        void IOperationWriteTarget.Write<TValue>(
            OperationField<TValue> field, in TValue value) =>
            TryWriteOperation(_callbackGeneration, field, value);

        void IOperationWriteTarget.Write(OperationDescriptor operation, int index, ReadOnlySpan<byte> bytes)
        {
            if (!IsOperationWriterValid(_callbackGeneration) || !IsOperationAllowed(operation.DenseIndex) ||
                !_operationWriter.WriteField(_operationRank, operation, index, bytes))
                MarkInvalidOperationUse();
        }

        void IOperationWriteTarget.Enable<TOperation>() =>
            TryResolveAndEnableDiscreteOperation<TOperation>(_callbackGeneration);

        #endregion

        #region Internal Logic

        internal StateExecutionContext()
        {
        }

        internal void AttachOutgoingTransitions(
            TransitionHandle[] transitions)
        {
            _outgoingTransitions = transitions;
            _readOnlyOutgoingTransitions = transitions == null
                ? null
                : Array.AsReadOnly(transitions);
        }

        internal bool IsOperationWriterValid(ulong callbackToken) =>
            _isCallbackActive &&
            callbackToken != 0UL &&
            callbackToken == _callbackGeneration &&
            _operationWriter.IsValid &&
            _operationRank.IsValid &&
            _activationId.IsValid;

        internal bool TryResolveOperationField<TOperation, TValue>(
            ulong callbackToken,
            int fieldIndex,
            out OperationField<TValue> field)
            where TOperation : class, IOperation
            where TValue : unmanaged
        {
            field = default;
            if (!IsOperationWriterValid(callbackToken))
            {
                return false;
            }

            field = _operationWriter.ResolveField<TOperation, TValue>(fieldIndex);
            return field.IsValid;
        }

        internal bool TryWriteOperation<TValue>(
            ulong callbackToken,
            OperationField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !IsOperationAllowed(field.OperationIndex) ||
                !_operationWriter.Write(_operationRank, field, value))
            {
                MarkInvalidOperationUse();
                return false;
            }

            return true;
        }

        internal bool TryResolveAndEnableDiscreteOperation<TOperation>(
            ulong callbackToken)
            where TOperation : class, IOperation
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !_operationWriter.TryResolveTypedHandle(
                    out OperationHandle<TOperation> handle) ||
                !IsOperationAllowed(handle.DenseIndex) ||
                !_operationWriter.EnableDiscrete(_operationRank, handle, _activationId))
            {
                MarkInvalidOperationUse();
                return false;
            }

            return true;
        }

        internal bool TryEnableDiscreteOperation<TOperation>(
            ulong callbackToken,
            OperationHandle<TOperation> handle)
            where TOperation : class, IOperation
        {
            if (!IsOperationWriterValid(callbackToken) ||
                !IsOperationAllowed(handle.DenseIndex) ||
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

        internal void AttachEvents(IStateEventInboxReader inbox, IStateEventOutbox outbox)
        {
            _eventInbox = inbox;
            _eventOutbox = outbox;
        }

        internal void Prepare(
            IStateTransitionRequests transitions,
            StateActivationMemory memory,
            FrozenConfigSnapshot config,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            in TickFrame tickFrame,
            OperationFrameWriter operationWriter,
            OperationWriteRank operationRank,
            bool[] allowedOperations,
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
            _transitions = transitions;
            _memory = memory;
            _config = config;
            _intents = intents;
            _previousContext = previousContext;
            _tickFrame = tickFrame;
            _operationWriter = operationWriter;
            _operationRank = operationRank;
            _allowedOperations = allowedOperations;
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
            _transitions = null;
            _memory = null;
            _config = null;
            _intents = null;
            _eventInbox = null;
            _eventOutbox = null;
            _previousContext = default;
            _tickFrame = default;
            _operationWriter = default;
            _operationRank = default;
            _allowedOperations = null;
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

        private bool IsOperationAllowed(int denseIndex) =>
            _allowedOperations != null &&
            denseIndex >= 0 &&
            denseIndex < _allowedOperations.Length &&
            _allowedOperations[denseIndex];

        private void MarkInvalidOperationUse()
        {
            if (_isCallbackActive)
            {
                _hasError = true;
            }

            _operationLease?.ReportInvalidUse();
        }

        #endregion
    }

}
