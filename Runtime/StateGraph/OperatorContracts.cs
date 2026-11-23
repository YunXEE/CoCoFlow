using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public enum OperatorClaimSuspendPolicy
    {
        None = 0,
        Release = 1,
        Retain = 2
    }

    public readonly struct OperatorClaimRequirement : IEquatable<OperatorClaimRequirement>
    {
        internal OperatorClaimRequirement(
            OperatorClaimId claimId,
            OperationSectionRequirement section,
            StateSlotId stateSlotId,
            int priority,
            OperatorClaimSuspendPolicy suspendPolicy)
        {
            ClaimId = claimId;
            Section = section;
            StateSlotId = stateSlotId;
            Priority = priority;
            SuspendPolicy = suspendPolicy;
        }

        public OperatorClaimId ClaimId { get; }
        public OperationSectionRequirement Section { get; }
        public StateSlotId StateSlotId { get; }
        public int Priority { get; }
        public OperatorClaimSuspendPolicy SuspendPolicy { get; }
        public bool IsValid => ClaimId.IsValid &&
                               Section.IsValid &&
                               StateSlotId.IsValid &&
                               Section.Mode == OperationSectionMode.Discrete &&
                               (SuspendPolicy == OperatorClaimSuspendPolicy.Release ||
                                SuspendPolicy == OperatorClaimSuspendPolicy.Retain);

        public bool Equals(OperatorClaimRequirement other)
        {
            return ClaimId == other.ClaimId &&
                   Section == other.Section &&
                   StateSlotId == other.StateSlotId &&
                   Priority == other.Priority &&
                   SuspendPolicy == other.SuspendPolicy;
        }

        public override bool Equals(object obj) =>
            obj is OperatorClaimRequirement other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ClaimId.GetHashCode();
                hashCode = (hashCode * 397) ^ Section.GetHashCode();
                hashCode = (hashCode * 397) ^ StateSlotId.GetHashCode();
                hashCode = (hashCode * 397) ^ Priority;
                hashCode = (hashCode * 397) ^ (int)SuspendPolicy;
                return hashCode;
            }
        }

        public static bool operator ==(
            OperatorClaimRequirement left,
            OperatorClaimRequirement right) => left.Equals(right);

        public static bool operator !=(
            OperatorClaimRequirement left,
            OperatorClaimRequirement right) => !left.Equals(right);
    }

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

    public sealed class OperatorDescriptor
    {
        private readonly OperationSectionRequirement[] _requires;
        private readonly OperatorClaimRequirement[] _claims;
        private readonly OperatorOutcomeRequirement[] _outcomes;
        private readonly EventOutboxRequirement[] _emits;
        private readonly bool _isValid;

        internal OperatorDescriptor(
            OperatorId operatorId,
            Type operatorType,
            OperationSectionRequirement[] requires,
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

        public OperatorId OperatorId { get; }
        public Type OperatorType { get; }
        public IReadOnlyList<OperationSectionRequirement> Requires { get; }
        public IReadOnlyList<OperatorClaimRequirement> Claims { get; }
        public IReadOnlyList<EventOutboxRequirement> Emits { get; }
        public int OutcomeCount => _outcomes.Length;
        internal IReadOnlyList<OperatorOutcomeRequirement> OutcomeRequirements { get; }
        public bool IsValid => _isValid;

        internal bool RequiresSection(OperationSectionRequirement requirement)
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

        private static bool HasValidRequirement(OperationSectionRequirement[] requirements)
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
    }

    public sealed class OperatorDescriptorBuilder
    {
        private readonly List<OperationSectionRequirement> _requires =
            new List<OperationSectionRequirement>();
        private readonly List<OperatorClaimRequirement> _claims =
            new List<OperatorClaimRequirement>();
        private readonly List<OperatorOutcomeRequirement> _outcomes =
            new List<OperatorOutcomeRequirement>();
        private readonly List<EventOutboxRequirement> _emits =
            new List<EventOutboxRequirement>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        public bool TryRequire<TSection>(
            OperationSectionId sectionId,
            OperationSectionMode mode,
            out OperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, IOperationSection
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Operator descriptor is already frozen.");
                return false;
            }

            if (!OperationSectionRequirement.TryCreate<TSection>(
                    sectionId,
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

                if (_requires[index].SectionId == sectionId ||
                    _requires[index].SectionType == typeof(TSection))
                {
                    requirement = default;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An Operator cannot require conflicting definitions for one Operation Section.");
                    return false;
                }
            }

            _requires.Add(requirement);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryClaim(
            OperatorClaimId claimId,
            OperationSectionRequirement section,
            StateSlotId stateSlotId,
            int priority,
            OperatorClaimSuspendPolicy suspendPolicy,
            out OperatorClaimRequirement claim,
            out CoCoDiagnostic diagnostic)
        {
            claim = new OperatorClaimRequirement(
                claimId,
                section,
                stateSlotId,
                priority,
                suspendPolicy);
            if (_isFrozen || !claim.IsValid || !ContainsRequirement(section))
            {
                claim = default;
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    _isFrozen
                        ? "Operator descriptor is already frozen."
                        : "An Operator Claim must bind one required discrete Section and its canonical Graph-owned Claim State Slot.");
                return false;
            }

            for (int index = 0; index < _claims.Count; index++)
            {
                if (_claims[index].ClaimId == claimId ||
                    _claims[index].Section == section ||
                    _claims[index].StateSlotId == stateSlotId)
                {
                    claim = default;
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "An Operator cannot declare the same Claim, claimed Section, or Claim State Slot twice.");
                    return false;
                }
            }

            _claims.Add(claim);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

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
                            : "An Operator must require at least one Operation Section.");
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

        private bool ContainsRequirement(OperationSectionRequirement requirement)
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
    }

    public enum OperatorOutcomeStatus
    {
        None = 0,
        Succeeded = 1,
        NoOp = 2,
        Rejected = 3,
        ClaimDenied = 4
    }

    public readonly struct OperatorOutcome : IEquatable<OperatorOutcome>
    {
        private OperatorOutcome(OperatorOutcomeStatus status, CoCoDiagnostic diagnostic)
        {
            Status = status;
            Diagnostic = diagnostic;
        }

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

        public static OperatorOutcome Rejected(CoCoDiagnostic diagnostic)
        {
            return diagnostic.IsNone
                ? default
                : new OperatorOutcome(OperatorOutcomeStatus.Rejected, diagnostic);
        }

        internal static OperatorOutcome Denied(CoCoDiagnostic diagnostic) =>
            diagnostic.IsNone
                ? default
                : new OperatorOutcome(OperatorOutcomeStatus.ClaimDenied, diagnostic);

        public bool Equals(OperatorOutcome other) =>
            Status == other.Status && Diagnostic == other.Diagnostic;

        public override bool Equals(object obj) => obj is OperatorOutcome other && Equals(other);
        public override int GetHashCode() => unchecked(((int)Status * 397) ^ Diagnostic.GetHashCode());

        public static bool operator ==(OperatorOutcome left, OperatorOutcome right) => left.Equals(right);
        public static bool operator !=(OperatorOutcome left, OperatorOutcome right) => !left.Equals(right);
    }

    /// <summary>Marks the static definition used by the standard StateGraph Host.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class OperatorDefinitionAttribute : Attribute { }

    public interface IOperator
    {
        OperatorDescriptor Descriptor { get; }
        bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome);
    }

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

    public readonly struct OperatorExecutionContext
    {
        private readonly OperatorDescriptor _descriptor;
        private readonly FinalizedOperationFrame _operationFrame;
        private readonly IOperatorOutcomeSink _outcomeSink;
        private readonly ulong _token;

        internal OperatorExecutionContext(
            OperatorDescriptor descriptor,
            TickFrame tickFrame,
            ContextFrameReadView previousContext,
            FinalizedOperationFrame operationFrame,
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

        public bool TryGet<TSection>(
            OperationSectionRequirement requirement,
            out OperationSectionEntry<TSection> entry)
            where TSection : class, IOperationSection
        {
            if (!IsValid ||
                requirement.SectionType != typeof(TSection) ||
                !_descriptor.RequiresSection(requirement) ||
                !_operationFrame.Registry.TryResolve(requirement, out OperationSectionHandle<TSection> handle))
            {
                entry = default;
                return false;
            }

            return _operationFrame.TryGet(handle, out entry);
        }

        public bool TryWriteOutcome<TValue>(StateSlot<TValue> slot, in TValue value)
            where TValue : unmanaged
        {
            return slot.IsValid
                ? TryWriteOutcome(slot.SlotId, value)
                : RejectOutcomeWrite();
        }

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

        private bool RejectOutcomeWrite()
        {
            _outcomeSink?.RejectWrite(_token, OperatorId);
            return false;
        }
    }
}
