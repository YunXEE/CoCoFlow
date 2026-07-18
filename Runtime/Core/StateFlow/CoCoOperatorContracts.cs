using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoOperatorClaimSuspendPolicy
    {
        None = 0,
        Release = 1,
        Retain = 2
    }

    public readonly struct CoCoOperatorClaimRequirement : IEquatable<CoCoOperatorClaimRequirement>
    {
        internal CoCoOperatorClaimRequirement(
            CoCoOperatorClaimId claimId,
            CoCoOperationSectionRequirement section,
            CoCoStateSlotId stateSlotId,
            int priority,
            CoCoOperatorClaimSuspendPolicy suspendPolicy)
        {
            ClaimId = claimId;
            Section = section;
            StateSlotId = stateSlotId;
            Priority = priority;
            SuspendPolicy = suspendPolicy;
        }

        public CoCoOperatorClaimId ClaimId { get; }
        public CoCoOperationSectionRequirement Section { get; }
        public CoCoStateSlotId StateSlotId { get; }
        public int Priority { get; }
        public CoCoOperatorClaimSuspendPolicy SuspendPolicy { get; }
        public bool IsValid => ClaimId.IsValid &&
                               Section.IsValid &&
                               StateSlotId.IsValid &&
                               Section.Mode == CoCoOperationSectionMode.Discrete &&
                               (SuspendPolicy == CoCoOperatorClaimSuspendPolicy.Release ||
                                SuspendPolicy == CoCoOperatorClaimSuspendPolicy.Retain);

        public bool Equals(CoCoOperatorClaimRequirement other)
        {
            return ClaimId == other.ClaimId &&
                   Section == other.Section &&
                   StateSlotId == other.StateSlotId &&
                   Priority == other.Priority &&
                   SuspendPolicy == other.SuspendPolicy;
        }

        public override bool Equals(object obj) =>
            obj is CoCoOperatorClaimRequirement other && Equals(other);

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
            CoCoOperatorClaimRequirement left,
            CoCoOperatorClaimRequirement right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperatorClaimRequirement left,
            CoCoOperatorClaimRequirement right) => !left.Equals(right);
    }

    internal readonly struct CoCoOperatorOutcomeRequirement : IEquatable<CoCoOperatorOutcomeRequirement>
    {
        internal CoCoOperatorOutcomeRequirement(CoCoStateSlotId slotId, Type valueType)
        {
            SlotId = slotId;
            ValueType = valueType;
        }

        public CoCoStateSlotId SlotId { get; }
        public Type ValueType { get; }
        public bool IsValid => SlotId.IsValid && ValueType != null;

        public bool Equals(CoCoOperatorOutcomeRequirement other) =>
            SlotId == other.SlotId && ValueType == other.ValueType;

        public override bool Equals(object obj) =>
            obj is CoCoOperatorOutcomeRequirement other && Equals(other);

        public override int GetHashCode() =>
            unchecked((SlotId.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));

        public static bool operator ==(
            CoCoOperatorOutcomeRequirement left,
            CoCoOperatorOutcomeRequirement right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperatorOutcomeRequirement left,
            CoCoOperatorOutcomeRequirement right) => !left.Equals(right);
    }

    public sealed class CoCoOperatorDescriptor
    {
        private readonly CoCoOperationSectionRequirement[] _requires;
        private readonly CoCoOperatorClaimRequirement[] _claims;
        private readonly CoCoOperatorOutcomeRequirement[] _outcomes;
        private readonly CoCoEventOutboxRequirement[] _emits;
        private readonly bool _isValid;

        internal CoCoOperatorDescriptor(
            CoCoOperatorId operatorId,
            Type operatorType,
            CoCoOperationSectionRequirement[] requires,
            CoCoOperatorClaimRequirement[] claims,
            CoCoOperatorOutcomeRequirement[] outcomes,
            CoCoEventOutboxRequirement[] emits)
        {
            OperatorId = operatorId;
            OperatorType = operatorType;
            _requires = requires;
            _claims = claims;
            _outcomes = outcomes;
            _emits = emits;
            _isValid = operatorId.IsValid &&
                       operatorType != null &&
                       typeof(ICoCoOperator).IsAssignableFrom(operatorType) &&
                       HasValidRequirement(_requires);
            Requires = Array.AsReadOnly(_requires);
            Claims = Array.AsReadOnly(_claims);
            OutcomeRequirements = Array.AsReadOnly(_outcomes);
            Emits = Array.AsReadOnly(_emits);
        }

        public CoCoOperatorId OperatorId { get; }
        public Type OperatorType { get; }
        public IReadOnlyList<CoCoOperationSectionRequirement> Requires { get; }
        public IReadOnlyList<CoCoOperatorClaimRequirement> Claims { get; }
        public IReadOnlyList<CoCoEventOutboxRequirement> Emits { get; }
        public int OutcomeCount => _outcomes.Length;
        internal IReadOnlyList<CoCoOperatorOutcomeRequirement> OutcomeRequirements { get; }
        public bool IsValid => _isValid;

        internal bool RequiresSection(CoCoOperationSectionRequirement requirement)
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

        internal bool OwnsOutcome<TValue>(CoCoStateSlotId slotId)
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

        internal bool EmitsEvent(CoCoEventOutboxRequirement requirement)
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

        private static bool HasValidRequirement(CoCoOperationSectionRequirement[] requirements)
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

    public sealed class CoCoOperatorDescriptorBuilder
    {
        private readonly List<CoCoOperationSectionRequirement> _requires =
            new List<CoCoOperationSectionRequirement>();
        private readonly List<CoCoOperatorClaimRequirement> _claims =
            new List<CoCoOperatorClaimRequirement>();
        private readonly List<CoCoOperatorOutcomeRequirement> _outcomes =
            new List<CoCoOperatorOutcomeRequirement>();
        private readonly List<CoCoEventOutboxRequirement> _emits =
            new List<CoCoEventOutboxRequirement>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        public bool TryRequire<TSection>(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, ICoCoOperationSection
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Operator descriptor is already frozen.");
                return false;
            }

            if (!CoCoOperationSectionRequirement.TryCreate<TSection>(
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
            CoCoOperatorClaimId claimId,
            CoCoOperationSectionRequirement section,
            CoCoStateSlotId stateSlotId,
            int priority,
            CoCoOperatorClaimSuspendPolicy suspendPolicy,
            out CoCoOperatorClaimRequirement claim,
            out CoCoDiagnostic diagnostic)
        {
            claim = new CoCoOperatorClaimRequirement(
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
            CoCoStateSlotId slotId,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            var outcome = new CoCoOperatorOutcomeRequirement(slotId, typeof(TValue));
            if (_isFrozen || !outcome.IsValid ||
                !CoCoStateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)))
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
            CoCoEventTypeId eventTypeId,
            CoCoEventDomainId eventDomainId,
            int capacity,
            out CoCoEventOutboxRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Operator descriptor is already frozen.");
                return false;
            }

            if (!CoCoEventOutboxRequirement.TryCreate<TEvent>(
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
            CoCoOperatorId operatorId,
            out CoCoOperatorDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
            where TOperator : class, ICoCoOperator
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
            descriptor = new CoCoOperatorDescriptor(
                operatorId,
                typeof(TOperator),
                _requires.ToArray(),
                _claims.ToArray(),
                _outcomes.ToArray(),
                _emits.ToArray());
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool ContainsRequirement(CoCoOperationSectionRequirement requirement)
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

    public enum CoCoOperatorOutcomeStatus
    {
        None = 0,
        Succeeded = 1,
        NoOp = 2,
        Rejected = 3,
        ClaimDenied = 4
    }

    public readonly struct CoCoOperatorOutcome : IEquatable<CoCoOperatorOutcome>
    {
        private CoCoOperatorOutcome(CoCoOperatorOutcomeStatus status, CoCoDiagnostic diagnostic)
        {
            Status = status;
            Diagnostic = diagnostic;
        }

        public static CoCoOperatorOutcome Success =>
            new CoCoOperatorOutcome(CoCoOperatorOutcomeStatus.Succeeded, CoCoDiagnostic.None);

        public static CoCoOperatorOutcome NoOp =>
            new CoCoOperatorOutcome(CoCoOperatorOutcomeStatus.NoOp, CoCoDiagnostic.None);

        public CoCoOperatorOutcomeStatus Status { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public bool IsValid => Status >= CoCoOperatorOutcomeStatus.Succeeded &&
                               Status <= CoCoOperatorOutcomeStatus.ClaimDenied &&
                               (Status == CoCoOperatorOutcomeStatus.Rejected ||
                                Status == CoCoOperatorOutcomeStatus.ClaimDenied ||
                                Diagnostic.IsNone);

        public static CoCoOperatorOutcome Rejected(CoCoDiagnostic diagnostic)
        {
            return diagnostic.IsNone
                ? default
                : new CoCoOperatorOutcome(CoCoOperatorOutcomeStatus.Rejected, diagnostic);
        }

        internal static CoCoOperatorOutcome Denied(CoCoDiagnostic diagnostic) =>
            diagnostic.IsNone
                ? default
                : new CoCoOperatorOutcome(CoCoOperatorOutcomeStatus.ClaimDenied, diagnostic);

        public bool Equals(CoCoOperatorOutcome other) =>
            Status == other.Status && Diagnostic == other.Diagnostic;

        public override bool Equals(object obj) => obj is CoCoOperatorOutcome other && Equals(other);
        public override int GetHashCode() => unchecked(((int)Status * 397) ^ Diagnostic.GetHashCode());

        public static bool operator ==(CoCoOperatorOutcome left, CoCoOperatorOutcome right) => left.Equals(right);
        public static bool operator !=(CoCoOperatorOutcome left, CoCoOperatorOutcome right) => !left.Equals(right);
    }

    public interface ICoCoOperator
    {
        CoCoOperatorDescriptor Descriptor { get; }
        bool TryExecute(in CoCoOperatorExecutionContext context, out CoCoOperatorOutcome outcome);
    }

    internal interface ICoCoOperatorOutcomeSink
    {
        bool IsActive(ulong token, CoCoOperatorId operatorId);

        void RejectWrite(ulong token, CoCoOperatorId operatorId);

        bool TryWrite<TValue>(
            ulong token,
            CoCoOperatorId operatorId,
            CoCoStateSlotId slotId,
            in TValue value)
            where TValue : unmanaged;
    }

    public readonly struct CoCoOperatorExecutionContext
    {
        private readonly CoCoOperatorDescriptor _descriptor;
        private readonly CoCoFinalizedOperationFrame _operationFrame;
        private readonly ICoCoOperatorOutcomeSink _outcomeSink;
        private readonly ulong _token;

        internal CoCoOperatorExecutionContext(
            CoCoOperatorDescriptor descriptor,
            CoCoTickFrame tickFrame,
            CoCoContextFrameReadView previousContext,
            CoCoFinalizedOperationFrame operationFrame,
            ICoCoOperatorOutcomeSink outcomeSink,
            CoCoEventOutboxWriter eventOutbox,
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

        public CoCoOperatorId OperatorId => _descriptor?.OperatorId ?? default;
        public CoCoTickFrame TickFrame { get; }
        public CoCoContextFrameReadView PreviousContext { get; }
        public CoCoEventOutboxWriter EventOutbox { get; }
        public bool IsValid => _descriptor != null &&
                               _descriptor.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               _operationFrame.IsValid &&
                               _outcomeSink != null &&
                               _outcomeSink.IsActive(_token, OperatorId);

        public bool TryGet<TSection>(
            CoCoOperationSectionRequirement requirement,
            out CoCoOperationSectionEntry<TSection> entry)
            where TSection : class, ICoCoOperationSection
        {
            if (!IsValid ||
                requirement.SectionType != typeof(TSection) ||
                !_descriptor.RequiresSection(requirement) ||
                !_operationFrame.Registry.TryResolve(requirement, out CoCoOperationSectionHandle<TSection> handle))
            {
                entry = default;
                return false;
            }

            return _operationFrame.TryGet(handle, out entry);
        }

        public bool TryWriteOutcome<TValue>(CoCoStateSlot<TValue> slot, in TValue value)
            where TValue : unmanaged
        {
            return slot.IsValid
                ? TryWriteOutcome(slot.SlotId, value)
                : RejectOutcomeWrite();
        }

        public bool TryWriteOutcome<TValue>(CoCoStateSlotId slotId, in TValue value)
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
