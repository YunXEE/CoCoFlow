using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Host 根据 Operator 声明的输出、占用与结果槽安装执行者，并在一次步进中收集执行结果。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>定义 Operator 暂停时一项占用的保留或释放行为。</summary>
    public enum OperatorClaimSuspendPolicy
    {
        None = 0,
        Release = 1,
        Retain = 2
    }

    /// <summary>描述 Operator 操作共享资源前应持有的占用合同。</summary>
    public readonly struct OperatorClaimRequirement : IEquatable<OperatorClaimRequirement>
    {

        #region Public API

        public OperatorClaimId ClaimId { get; }
        public OperationRequirement Operation { get; }
        public StateSlotId StateSlotId { get; }
        public int Priority { get; }
        public OperatorClaimSuspendPolicy SuspendPolicy { get; }
        public bool IsValid => ClaimId.IsValid &&
                               Operation.IsValid &&
                               StateSlotId.IsValid &&
                               Operation.Mode == Mode.Discrete &&
                               (SuspendPolicy == OperatorClaimSuspendPolicy.Release ||
                                SuspendPolicy == OperatorClaimSuspendPolicy.Retain);

        /// <summary>按<c>ClaimId</c>、<c>Operation</c>、<c>StateSlotId</c>、<c>Priority</c>、<c>SuspendPolicy</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperatorClaimRequirement other)
        {
            return ClaimId == other.ClaimId &&
                   Operation == other.Operation &&
                   StateSlotId == other.StateSlotId &&
                   Priority == other.Priority &&
                   SuspendPolicy == other.SuspendPolicy;
        }

        /// <summary>仅当对象属于同类型并且<c>ClaimId</c>、<c>Operation</c>、<c>StateSlotId</c>、<c>Priority</c>、<c>SuspendPolicy</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperatorClaimRequirement other && Equals(other);

        /// <summary>为哈希集合计算与<c>ClaimId</c>、<c>Operation</c>、<c>StateSlotId</c>、<c>Priority</c>、<c>SuspendPolicy</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ClaimId.GetHashCode();
                hashCode = (hashCode * 397) ^ Operation.GetHashCode();
                hashCode = (hashCode * 397) ^ StateSlotId.GetHashCode();
                hashCode = (hashCode * 397) ^ Priority;
                hashCode = (hashCode * 397) ^ (int)SuspendPolicy;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>ClaimId</c>、<c>Operation</c>、<c>StateSlotId</c>、<c>Priority</c>、<c>SuspendPolicy</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperatorClaimRequirement left,
            OperatorClaimRequirement right) => left.Equals(right);

        /// <summary>比较两端的<c>ClaimId</c>、<c>Operation</c>、<c>StateSlotId</c>、<c>Priority</c>、<c>SuspendPolicy</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperatorClaimRequirement left,
            OperatorClaimRequirement right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal OperatorClaimRequirement(
            OperatorClaimId claimId,
            OperationRequirement operation,
            StateSlotId stateSlotId,
            int priority,
            OperatorClaimSuspendPolicy suspendPolicy)
        {
            ClaimId = claimId;
            Operation = operation;
            StateSlotId = stateSlotId;
            Priority = priority;
            SuspendPolicy = suspendPolicy;
        }

        #endregion
    }

    /// <summary>描述 Operator 在 Context 中拥有的结果槽及其类型。</summary>
    internal readonly struct OperatorOutcomeRequirement : IEquatable<OperatorOutcomeRequirement>
    {
        internal OperatorOutcomeRequirement(StateSlotId slotId, Type valueType)
        {
            SlotId = slotId;
            ValueType = valueType;
        }

        public StateSlotId SlotId { get; }
        public Type ValueType { get; }
        public bool IsValid => SlotId.IsValid && ValueType != null;

        public bool Equals(OperatorOutcomeRequirement other) =>
            SlotId == other.SlotId && ValueType == other.ValueType;

        public override bool Equals(object obj) =>
            obj is OperatorOutcomeRequirement other && Equals(other);

        public override int GetHashCode() =>
            unchecked((SlotId.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));

        public static bool operator ==(
            OperatorOutcomeRequirement left,
            OperatorOutcomeRequirement right) => left.Equals(right);

        public static bool operator !=(
            OperatorOutcomeRequirement left,
            OperatorOutcomeRequirement right) => !left.Equals(right);
    }

    /// <summary>汇总 Host 装配 Operator 时需要匹配的输入命令、占用、结果与事件。</summary>
    public sealed class OperatorDescriptor
    {
        private readonly OperationRequirement[] _requires;
        private readonly OperatorClaimRequirement[] _claims;
        private readonly OperatorOutcomeRequirement[] _outcomes;
        private readonly EventOutboxRequirement[] _emits;
        private readonly bool _isValid;

        public OperatorId OperatorId { get; }
        public Type OperatorType { get; }
        public IReadOnlyList<OperationRequirement> Requires { get; }
        public IReadOnlyList<OperatorClaimRequirement> Claims { get; }
        public IReadOnlyList<EventOutboxRequirement> Emits { get; }
        public int OutcomeCount => _outcomes.Length;
        internal IReadOnlyList<OperatorOutcomeRequirement> OutcomeRequirements { get; }
        public bool IsValid => _isValid;

        #region Internal Logic

        internal OperatorDescriptor(
            OperatorId operatorId,
            Type operatorType,
            OperationRequirement[] requires,
            OperatorClaimRequirement[] claims,
            OperatorOutcomeRequirement[] outcomes,
            EventOutboxRequirement[] emits)
        {
            OperatorId = operatorId;
            OperatorType = operatorType;
            _requires = requires;
            _claims = claims;
            _outcomes = outcomes;
            _emits = emits;
            _isValid = operatorId.IsValid &&
                       operatorType != null &&
                       typeof(IOperator).IsAssignableFrom(operatorType) &&
                       HasValidRequirement(_requires);
            Requires = Array.AsReadOnly(_requires);
            Claims = Array.AsReadOnly(_claims);
            OutcomeRequirements = Array.AsReadOnly(_outcomes);
            Emits = Array.AsReadOnly(_emits);
        }

        internal bool RequiresOperation(OperationRequirement requirement)
        {
            for (int index = 0; index < _requires.Length; index++)
            {
                if (_requires[index] == requirement)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool OwnsOutcome<TValue>(StateSlotId slotId)
            where TValue : unmanaged
        {
            for (int index = 0; index < _outcomes.Length; index++)
            {
                if (_outcomes[index].SlotId == slotId &&
                    _outcomes[index].ValueType == typeof(TValue))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool EmitsEvent(EventOutboxRequirement requirement)
        {
            for (int index = 0; index < _emits.Length; index++)
            {
                if (_emits[index] == requirement)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValidRequirement(OperationRequirement[] requirements)
        {
            if (requirements == null || requirements.Length == 0)
            {
                return false;
            }

            for (int index = 0; index < requirements.Length; index++)
            {
                if (!requirements[index].IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }

    /// <summary>收集一个执行者的命令、占用和输出合同，冻结后供 Host 验证。</summary>
    public sealed class OperatorDescriptorBuilder
    {
        private readonly List<OperationRequirement> _requires =
            new List<OperationRequirement>();
        private readonly List<OperatorClaimRequirement> _claims =
            new List<OperatorClaimRequirement>();
        private readonly List<OperatorOutcomeRequirement> _outcomes =
            new List<OperatorOutcomeRequirement>();
        private readonly List<EventOutboxRequirement> _emits =
            new List<EventOutboxRequirement>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        #region Public API

        /// <summary>在冻结前登记 Operator 消费的类型化操作和模式。合同无效或冲突时返回 false 并提供诊断。</summary>
        public bool TryRequire<TOperation>(
            OperationId operationId,
            Mode mode,
            out OperationRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TOperation : class, IOperation
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Operator descriptor is already frozen.");
                return false;
            }

            if (!OperationRequirement.TryCreate<TOperation>(
                    operationId,
                    mode,
                    out requirement,
                    out diagnostic))
            {
                return false;
            }

            for (int index = 0; index < _requires.Count; index++)
            {
                if (_requires[index] == requirement)
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                if (_requires[index].OperationId == operationId ||
                    _requires[index].OperationType == typeof(TOperation))
                {
                    requirement = default;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An Operator cannot require conflicting definitions for one Operation Operation.");
                    return false;
                }
            }

            _requires.Add(requirement);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>在冻结前声明该 Operator 对指定资源的占用，标识无效或与已有声明冲突时返回 false。</summary>
        public bool TryClaim(
            OperatorClaimId claimId,
            OperationRequirement operation,
            StateSlotId stateSlotId,
            int priority,
            OperatorClaimSuspendPolicy suspendPolicy,
            out OperatorClaimRequirement claim,
            out CoCoDiagnostic diagnostic)
        {
            claim = new OperatorClaimRequirement(
                claimId,
                operation,
                stateSlotId,
                priority,
                suspendPolicy);
            if (_isFrozen || !claim.IsValid || !ContainsRequirement(operation))
            {
                claim = default;
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    _isFrozen
                        ? "Operator descriptor is already frozen."
                        : "An Operator Claim must bind one required discrete Operation and its canonical Graph-owned Claim State Slot.");
                return false;
            }

            for (int index = 0; index < _claims.Count; index++)
            {
                if (_claims[index].ClaimId == claimId ||
                    _claims[index].Operation == operation ||
                    _claims[index].StateSlotId == stateSlotId)
                {
                    claim = default;
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "An Operator cannot declare the same Claim, claimed Operation, or Claim State Slot twice.");
                    return false;
                }
            }

            _claims.Add(claim);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>为 Operator 声明可写的结果槽和类型，冻结后或声明不合法时返回 false。</summary>
        public bool TryOwnOutcome<TValue>(
            StateSlotId slotId,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            var outcome = new OperatorOutcomeRequirement(slotId, typeof(TValue));
            if (_isFrozen || !outcome.IsValid ||
                !StateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)))
            {
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    _isFrozen
                        ? "Operator descriptor is already frozen."
                        : "Operator Outcome identity and value type must be valid.");
                return false;
            }

            for (int index = 0; index < _outcomes.Count; index++)
            {
                if (_outcomes[index].SlotId == slotId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OutcomeOwnershipConflict,
                        "An Operator cannot declare one Outcome Slot more than once.");
                    return false;
                }
            }

            _outcomes.Add(outcome);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>在冻结前登记可发出的载荷、事件域和容量，声明不合法或冲突时返回 false。</summary>
        public bool TryEmit<TEvent>(
            EventTypeId eventTypeId,
            EventDomainId eventDomainId,
            int capacity,
            out EventOutboxRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Operator descriptor is already frozen.");
                return false;
            }

            if (!EventOutboxRequirement.TryCreate<TEvent>(
                    eventTypeId,
                    eventDomainId,
                    capacity,
                    out requirement,
                    out diagnostic))
            {
                return false;
            }

            for (int index = 0; index < _emits.Count; index++)
            {
                if (_emits[index] == requirement)
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                if (_emits[index].EventTypeId == eventTypeId)
                {
                    requirement = default;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An Operator cannot emit conflicting definitions for one Event lane.");
                    return false;
                }
            }

            _emits.Add(requirement);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>将要件、占用、结果及出件声明固化为指定 Operator 的描述符，失败时返回诊断。</summary>
        public bool TryFreeze<TOperator>(
            OperatorId operatorId,
            out OperatorDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
            where TOperator : class, IOperator
        {
            if (_isFrozen || !operatorId.IsValid || _requires.Count == 0)
            {
                descriptor = null;
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    _isFrozen
                        ? "Operator descriptor may only be frozen once."
                        : !operatorId.IsValid
                            ? "OperatorId must be valid."
                            : "An Operator must require at least one Operation Operation.");
                return false;
            }

            _isFrozen = true;
            descriptor = new OperatorDescriptor(
                operatorId,
                typeof(TOperator),
                _requires.ToArray(),
                _claims.ToArray(),
                _outcomes.ToArray(),
                _emits.ToArray());
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        private bool ContainsRequirement(OperationRequirement requirement)
        {
            for (int index = 0; index < _requires.Count; index++)
            {
                if (_requires[index] == requirement)
                {
                    return true;
                }
            }

            return false;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator, code, message);

        #endregion
    }

    /// <summary>区分执行者对本次调用返回的成功、无操作或拒绝结果。</summary>
    public enum OperatorOutcomeStatus
    {
        None = 0,
        Succeeded = 1,
        NoOp = 2,
        Rejected = 3,
        ClaimDenied = 4
    }

    /// <summary>将执行结果和诊断传回 Host 的整步事务。</summary>
    public readonly struct OperatorOutcome : IEquatable<OperatorOutcome>
    {

        #region Public API

        public static OperatorOutcome Success =>
            new OperatorOutcome(OperatorOutcomeStatus.Succeeded, CoCoDiagnostic.None);

        public static OperatorOutcome NoOp =>
            new OperatorOutcome(OperatorOutcomeStatus.NoOp, CoCoDiagnostic.None);

        public OperatorOutcomeStatus Status { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public bool IsValid => Status >= OperatorOutcomeStatus.Succeeded &&
                               Status <= OperatorOutcomeStatus.ClaimDenied &&
                               (Status == OperatorOutcomeStatus.Rejected ||
                                Status == OperatorOutcomeStatus.ClaimDenied ||
                                Diagnostic.IsNone);

        /// <summary>用给定诊断构造拒绝结果，供 Host 取消该步候选。</summary>
        public static OperatorOutcome Rejected(CoCoDiagnostic diagnostic)
        {
            return diagnostic.IsNone
                ? default
                : new OperatorOutcome(OperatorOutcomeStatus.Rejected, diagnostic);
        }

        /// <summary>按<c>Status</c>、<c>Diagnostic</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperatorOutcome other) =>
            Status == other.Status && Diagnostic == other.Diagnostic;

        /// <summary>仅当对象属于同类型并且<c>Status</c>、<c>Diagnostic</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is OperatorOutcome other && Equals(other);
        /// <summary>为哈希集合计算与<c>Status</c>、<c>Diagnostic</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked(((int)Status * 397) ^ Diagnostic.GetHashCode());

        /// <summary>比较两端的<c>Status</c>、<c>Diagnostic</c>，相等时返回 true。</summary>
        public static bool operator ==(OperatorOutcome left, OperatorOutcome right) => left.Equals(right);
        /// <summary>比较两端的<c>Status</c>、<c>Diagnostic</c>，存在差异时返回 true。</summary>
        public static bool operator !=(OperatorOutcome left, OperatorOutcome right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperatorOutcome(OperatorOutcomeStatus status, CoCoDiagnostic diagnostic)
        {
            Status = status;
            Diagnostic = diagnostic;
        }

        internal static OperatorOutcome Denied(CoCoDiagnostic diagnostic) =>
            diagnostic.IsNone
                ? default
                : new OperatorOutcome(OperatorOutcomeStatus.ClaimDenied, diagnostic);

        #endregion
    }

    /// <summary>指定标准 Host 从哪里取得某个 Operator 的静态定义。</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class OperatorDefinitionAttribute : Attribute { }

    /// <summary>由模块实现，按已声明的合同读取本步 Operation 并向 Host 返回结果。</summary>
    public interface IOperator
    {
        OperatorDescriptor Descriptor { get; }
        /// <summary>在 Host 的准备阶段消费本步操作并填入结果。返回 false 或拒绝结果会使该步取消。</summary>
        bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome);
    }

    /// <summary>接收执行者声明范围内的候选结果槽写入。</summary>
    internal interface IOperatorOutcomeSink
    {
        bool IsActive(ulong token, OperatorId operatorId);

        void RejectWrite(ulong token, OperatorId operatorId);

        bool TryWrite<TValue>(
            ulong token,
            OperatorId operatorId,
            StateSlotId slotId,
            in TValue value)
            where TValue : unmanaged;
    }

    /// <summary>向运行中的 Operator 提供本步输出、上一提交以及结果和事件的写入口。</summary>
    public readonly struct OperatorExecutionContext
    {
        private readonly OperatorDescriptor _descriptor;
        private readonly StagedOperationFrame _operationFrame;
        private readonly IOperatorOutcomeSink _outcomeSink;
        private readonly ulong _token;

        public OperatorId OperatorId => _descriptor?.OperatorId ?? default;
        public TickFrame TickFrame { get; }
        public ContextFrameReadView PreviousContext { get; }
        public EventOutboxWriter EventOutbox { get; }
        public bool IsValid => _descriptor != null &&
                               _descriptor.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               _operationFrame.IsValid &&
                               _outcomeSink != null &&
                               _outcomeSink.IsActive(_token, OperatorId);

        #region Public API

        /// <summary>取本 Operator 已声明的输出，类型、要件或候选不匹配时返回 false。</summary>
        public bool TryGet<TOperation>(
            OperationRequirement requirement,
            out OperationEntry<TOperation> entry)
            where TOperation : class, IOperation
        {
            if (!IsValid ||
                requirement.OperationType != typeof(TOperation) ||
                !_descriptor.RequiresOperation(requirement) ||
                !_operationFrame.Registry.TryResolve(requirement, out OperationHandle<TOperation> handle))
            {
                entry = default;
                return false;
            }

            return _operationFrame.TryGet(handle, out entry);
        }

        /// <summary>将值暂存到本 Operator 声明负责的结果槽。槽、类型或当前候选无效时返回 false。</summary>
        public bool TryWriteOutcome<TValue>(StateSlot<TValue> slot, in TValue value)
            where TValue : unmanaged
        {
            return slot.IsValid
                ? TryWriteOutcome(slot.SlotId, value)
                : RejectOutcomeWrite();
        }

        /// <summary>将值暂存到本 Operator 声明负责的结果槽。槽、类型或当前候选无效时返回 false。</summary>
        public bool TryWriteOutcome<TValue>(StateSlotId slotId, in TValue value)
            where TValue : unmanaged
        {
            if (!IsValid || !_descriptor.OwnsOutcome<TValue>(slotId))
            {
                return RejectOutcomeWrite();
            }

            if (_outcomeSink.TryWrite(_token, OperatorId, slotId, value))
            {
                return true;
            }

            return RejectOutcomeWrite();
        }

        #endregion

        #region Internal Logic

        internal OperatorExecutionContext(
            OperatorDescriptor descriptor,
            TickFrame tickFrame,
            ContextFrameReadView previousContext,
            StagedOperationFrame operationFrame,
            IOperatorOutcomeSink outcomeSink,
            EventOutboxWriter eventOutbox,
            ulong token)
        {
            _descriptor = descriptor;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            _operationFrame = operationFrame;
            _outcomeSink = outcomeSink;
            EventOutbox = eventOutbox;
            _token = token;
        }

        private bool RejectOutcomeWrite()
        {
            _outcomeSink?.RejectWrite(_token, OperatorId);
            return false;
        }

        #endregion
    }
}
