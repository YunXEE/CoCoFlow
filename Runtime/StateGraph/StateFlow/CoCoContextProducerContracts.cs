using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Portable value projection of one compiled staged Graph State. Active-path metadata
    /// and project-owned unmanaged memory make the complete record suitable for a Context Slot.
    /// </summary>
    public readonly struct CoCoGraphStateRecord<TState> : IEquatable<CoCoGraphStateRecord<TState>>
        where TState : unmanaged
    {
        private CoCoGraphStateRecord(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in TState state)
        {
            LayerId = layerId;
            StateId = stateId;
            IsOnActivePath = isOnActivePath;
            ActivationId = activationId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
            EnterPending = enterPending;
            MemoryFingerprint = memoryFingerprint;
            State = state;
        }

        public CoCoLayerId LayerId { get; }
        public CoCoStateId StateId { get; }
        public bool IsOnActivePath { get; }
        public CoCoActivationId ActivationId { get; }
        public double LocalSeconds { get; }
        public double ActionProgress { get; }
        public bool EnterPending { get; }
        public ulong MemoryFingerprint { get; }
        public TState State { get; }
        public bool IsActive => IsOnActivePath;
        public bool IsValid => LayerId.IsValid &&
                               StateId.IsValid &&
                               IsFiniteNonNegative(LocalSeconds) &&
                               ActionProgress >= 0d &&
                               ActionProgress <= 1d &&
                               (!EnterPending ||
                                LocalSeconds == 0d && ActionProgress == 0d) &&
                               (IsOnActivePath
                                   ? ActivationId.IsValid
                                   : !EnterPending &&
                                     (ActivationId.IsValid ||
                                      LocalSeconds == 0d && ActionProgress == 0d));

        public static bool TryCreate(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in TState state,
            out CoCoGraphStateRecord<TState> record)
        {
            record = new CoCoGraphStateRecord<TState>(
                layerId,
                stateId,
                isOnActivePath,
                activationId,
                localSeconds,
                actionProgress,
                enterPending,
                memoryFingerprint,
                state);
            if (record.IsValid)
            {
                return true;
            }

            record = default;
            return false;
        }

        public static bool TryCreateInactive(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            ulong memoryFingerprint,
            in TState state,
            out CoCoGraphStateRecord<TState> record) =>
            TryCreate(
                layerId,
                stateId,
                false,
                default,
                0d,
                0d,
                false,
                memoryFingerprint,
                state,
                out record);

        public bool Equals(CoCoGraphStateRecord<TState> other)
        {
            return LayerId == other.LayerId &&
                   StateId == other.StateId &&
                   IsOnActivePath == other.IsOnActivePath &&
                   ActivationId == other.ActivationId &&
                   LocalSeconds.Equals(other.LocalSeconds) &&
                   ActionProgress.Equals(other.ActionProgress) &&
                   EnterPending == other.EnterPending &&
                   MemoryFingerprint == other.MemoryFingerprint &&
                   EqualityComparer<TState>.Default.Equals(State, other.State);
        }

        public override bool Equals(object obj) =>
            obj is CoCoGraphStateRecord<TState> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = LayerId.GetHashCode();
                hashCode = (hashCode * 397) ^ StateId.GetHashCode();
                hashCode = (hashCode * 397) ^ IsOnActivePath.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ LocalSeconds.GetHashCode();
                hashCode = (hashCode * 397) ^ ActionProgress.GetHashCode();
                hashCode = (hashCode * 397) ^ EnterPending.GetHashCode();
                hashCode = (hashCode * 397) ^ MemoryFingerprint.GetHashCode();
                hashCode = (hashCode * 397) ^ EqualityComparer<TState>.Default.GetHashCode(State);
                return hashCode;
            }
        }

        public static bool operator ==(
            CoCoGraphStateRecord<TState> left,
            CoCoGraphStateRecord<TState> right) => left.Equals(right);

        public static bool operator !=(
            CoCoGraphStateRecord<TState> left,
            CoCoGraphStateRecord<TState> right) => !left.Equals(right);

        private static bool IsFiniteNonNegative(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    }

    /// <summary>
    /// AOT-safe, bidirectional projection between one runtime ActivationMemory and its
    /// portable unmanaged Context representation. Restore preparation may only mutate
    /// the supplied candidate memory; applying live authority remains a higher-level concern.
    /// </summary>
    public interface ICoCoActivationMemoryStateBinding<TMemory, TState>
        where TMemory : CoCoActivationMemory
        where TState : unmanaged
    {
        ulong SemanticFingerprint { get; }

        bool TryCapture(
            TMemory memory,
            out TState state,
            out CoCoDiagnostic diagnostic);

        bool TryPrepareRestore(
            in TState state,
            TMemory candidateMemory,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>
    /// One project-owned producer for one Graph-owned unmanaged Context value.
    /// Implementations return the complete value and retain no transaction state.
    /// </summary>
    public interface ICoCoGraphContextValueProducer<TValue>
        where TValue : unmanaged
    {
        ulong SemanticFingerprint { get; }

        bool TryProduce(
            in CoCoGraphContextCaptureContext context,
            out TValue value,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>
    /// Portable committed ownership of one Operator Claim. Default is the canonical
    /// unheld value; a held value always carries its Claim, Operator, and Activation identities.
    /// </summary>
    public readonly struct CoCoOperatorClaimState : IEquatable<CoCoOperatorClaimState>
    {
        private CoCoOperatorClaimState(
            CoCoOperatorClaimId claimId,
            CoCoOperationSectionId sectionId,
            CoCoOperatorId ownerOperatorId,
            CoCoActivationId activationId)
        {
            ClaimId = claimId;
            SectionId = sectionId;
            OwnerOperatorId = ownerOperatorId;
            ActivationId = activationId;
        }

        public CoCoOperatorClaimId ClaimId { get; }
        public CoCoOperationSectionId SectionId { get; }
        public CoCoOperatorId OwnerOperatorId { get; }
        public CoCoActivationId ActivationId { get; }
        public bool IsHeld => OwnerOperatorId.IsValid && ActivationId.IsValid;
        public bool IsValid => ClaimId.IsValid &&
                               SectionId.IsValid &&
                               (IsHeld ||
                                (!OwnerOperatorId.IsValid && !ActivationId.IsValid));

        public static CoCoOperatorClaimState Unheld(
            CoCoOperatorClaimId claimId,
            CoCoOperationSectionId sectionId)
        {
            if (!claimId.IsValid)
            {
                throw new ArgumentException("ClaimId must be valid.", nameof(claimId));
            }

            if (!sectionId.IsValid)
            {
                throw new ArgumentException("SectionId must be valid.", nameof(sectionId));
            }

            return new CoCoOperatorClaimState(claimId, sectionId, default, default);
        }

        public static bool TryCreateHeld(
            CoCoOperatorClaimId claimId,
            CoCoOperationSectionId sectionId,
            CoCoOperatorId ownerOperatorId,
            CoCoActivationId activationId,
            out CoCoOperatorClaimState state)
        {
            state = new CoCoOperatorClaimState(
                claimId,
                sectionId,
                ownerOperatorId,
                activationId);
            if (state.IsValid && state.IsHeld)
            {
                return true;
            }

            state = default;
            return false;
        }

        public bool Equals(CoCoOperatorClaimState other) =>
            ClaimId == other.ClaimId &&
            SectionId == other.SectionId &&
            OwnerOperatorId == other.OwnerOperatorId &&
            ActivationId == other.ActivationId;

        public override bool Equals(object obj) =>
            obj is CoCoOperatorClaimState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ClaimId.GetHashCode();
                hashCode = (hashCode * 397) ^ SectionId.GetHashCode();
                hashCode = (hashCode * 397) ^ OwnerOperatorId.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(
            CoCoOperatorClaimState left,
            CoCoOperatorClaimState right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperatorClaimState left,
            CoCoOperatorClaimState right) => !left.Equals(right);
    }

    internal interface ICoCoStagedGraphReadSource
    {
        bool IsActive(ulong token);

        bool TryGetActiveLeaf(
            ulong token,
            CoCoLayerId layerId,
            out CoCoStateId stateId);

        bool TryGetState<TState>(
            ulong token,
            CoCoStateId stateId,
            out CoCoGraphStateRecord<TState> state)
            where TState : unmanaged;
    }

    /// <summary>
    /// Callback-scoped read view over the currently staged Graph candidate.
    /// </summary>
    public readonly struct CoCoStagedGraphReadView
    {
        private readonly ICoCoStagedGraphReadSource _source;
        private readonly ulong _token;

        internal CoCoStagedGraphReadView(
            ICoCoStagedGraphReadSource source,
            ulong token)
        {
            _source = source;
            _token = token;
        }

        public bool IsValid => _source != null && _source.IsActive(_token);

        public bool TryGetActiveLeaf(
            CoCoLayerId layerId,
            out CoCoStateId stateId)
        {
            if (!IsValid || !layerId.IsValid)
            {
                stateId = default;
                return false;
            }

            return _source.TryGetActiveLeaf(_token, layerId, out stateId) && stateId.IsValid;
        }

        public bool TryGetState<TState>(
            CoCoStateId stateId,
            out CoCoGraphStateRecord<TState> state)
            where TState : unmanaged
        {
            if (!IsValid || !stateId.IsValid)
            {
                state = default;
                return false;
            }

            return _source.TryGetState(_token, stateId, out state) && state.IsValid;
        }
    }

    /// <summary>
    /// Immutable callback context used by Graph-owned Context value producers.
    /// It exposes only the previous committed/default-backed Context and staged read views.
    /// </summary>
    public readonly struct CoCoGraphContextCaptureContext
    {
        internal CoCoGraphContextCaptureContext(
            CoCoGraphInstanceId graphInstanceId,
            in CoCoTickFrame tickFrame,
            CoCoContextFrameReadView previousContext,
            CoCoStagedGraphReadView stagedGraph,
            CoCoStagedOperationFrame operationFrame)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            StagedGraph = stagedGraph;
            OperationFrame = operationFrame;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTickFrame TickFrame { get; }
        public CoCoContextFrameReadView PreviousContext { get; }
        public CoCoStagedGraphReadView StagedGraph { get; }
        public CoCoStagedOperationFrame OperationFrame { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               StagedGraph.IsValid &&
                               OperationFrame.IsValid;

        public bool TryGetOperation<TSection>(
            CoCoOperationSectionRequirement requirement,
            out CoCoOperationSectionEntry<TSection> entry)
            where TSection : class, ICoCoOperationSection
        {
            if (!IsValid ||
                !requirement.IsValid ||
                requirement.SectionType != typeof(TSection) ||
                OperationFrame.Registry == null ||
                !OperationFrame.Registry.TryResolve(
                    requirement,
                    out CoCoOperationSectionHandle<TSection> handle))
            {
                entry = default;
                return false;
            }

            return OperationFrame.TryGet(handle, out entry);
        }
    }

    internal readonly struct CoCoActorContextValueRequirement :
        IEquatable<CoCoActorContextValueRequirement>
    {
        internal CoCoActorContextValueRequirement(
            CoCoStateSlotId slotId,
            Type valueType)
        {
            SlotId = slotId;
            ValueType = valueType;
        }

        internal CoCoStateSlotId SlotId { get; }
        internal Type ValueType { get; }
        internal bool IsValid => SlotId.IsValid &&
                                 ValueType != null &&
                                 CoCoStateFlowTypeRules.IsReferenceFreeValueType(ValueType);

        public bool Equals(CoCoActorContextValueRequirement other) =>
            SlotId == other.SlotId && ValueType == other.ValueType;

        public override bool Equals(object obj) =>
            obj is CoCoActorContextValueRequirement other && Equals(other);

        public override int GetHashCode() =>
            unchecked((SlotId.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));
    }

    /// <summary>
    /// Frozen exact Actor-owned Context Slot whitelist for one Actor binding type.
    /// </summary>
    public sealed class CoCoActorContextBindingDescriptor
    {
        private readonly CoCoActorContextValueRequirement[] _values;
        private readonly IReadOnlyList<CoCoActorContextValueRequirement> _readOnlyValues;
        private readonly bool _isValid;

        internal CoCoActorContextBindingDescriptor(
            Type bindingType,
            ulong semanticFingerprint,
            CoCoActorContextValueRequirement[] values)
        {
            BindingType = bindingType;
            SemanticFingerprint = semanticFingerprint;
            _values = values ?? Array.Empty<CoCoActorContextValueRequirement>();
            _readOnlyValues = Array.AsReadOnly(_values);
            _isValid = bindingType != null &&
                       typeof(ICoCoActorContextBinding).IsAssignableFrom(bindingType) &&
                       semanticFingerprint != 0UL &&
                       _values.Length > 0;
        }

        public Type BindingType { get; }
        public ulong SemanticFingerprint { get; }
        public int ValueCount => _values.Length;
        public bool IsValid => _isValid;
        internal IReadOnlyList<CoCoActorContextValueRequirement> Values => _readOnlyValues;

        internal bool Produces<TValue>(CoCoStateSlotId slotId)
            where TValue : unmanaged
        {
            for (int index = 0; index < _values.Length; index++)
            {
                if (_values[index].SlotId == slotId &&
                    _values[index].ValueType == typeof(TValue))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Single-use builder for an Actor binding's exact Context Slot whitelist.
    /// </summary>
    public sealed class CoCoActorContextBindingDescriptorBuilder
    {
        private readonly List<CoCoActorContextValueRequirement> _values =
            new List<CoCoActorContextValueRequirement>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        public bool TryProduce<TValue>(
            CoCoStateSlotId slotId,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            var requirement = new CoCoActorContextValueRequirement(slotId, typeof(TValue));
            if (_isFrozen || !requirement.IsValid)
            {
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidStateSlot,
                    _isFrozen
                        ? "Actor Context binding descriptor is already frozen."
                        : "An Actor Context value requires a valid Slot id and reference-free value type.");
                return false;
            }

            for (int index = 0; index < _values.Count; index++)
            {
                if (_values[index].SlotId == slotId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "An Actor Context binding cannot produce one Slot more than once.");
                    return false;
                }
            }

            _values.Add(requirement);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryFreeze<TBinding>(
            ulong semanticFingerprint,
            out CoCoActorContextBindingDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
            where TBinding : class, ICoCoActorContextBinding
        {
            if (_isFrozen || semanticFingerprint == 0UL || _values.Count == 0)
            {
                descriptor = null;
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidStateBlock,
                    _isFrozen
                        ? "Actor Context binding descriptor may only be frozen once."
                        : "An Actor Context binding requires a non-zero semantic fingerprint and at least one Slot.");
                return false;
            }

            _isFrozen = true;
            descriptor = new CoCoActorContextBindingDescriptor(
                typeof(TBinding),
                semanticFingerprint,
                _values.ToArray());
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);
    }

    public interface ICoCoActorContextBinding
    {
        CoCoActorContextBindingDescriptor Descriptor { get; }

        bool TryCapture(
            in CoCoActorContextCaptureContext context,
            out CoCoDiagnostic diagnostic);
    }

    internal interface ICoCoActorContextValueSink
    {
        bool IsActive(
            ulong token,
            ICoCoActorContextBinding binding);

        void RejectWrite(
            ulong token,
            ICoCoActorContextBinding binding);

        bool TryWrite<TValue>(
            ulong token,
            ICoCoActorContextBinding binding,
            CoCoStateSlotId slotId,
            in TValue value)
            where TValue : unmanaged;
    }

    /// <summary>
    /// Callback-scoped Actor Context writer. The descriptor is the public whitelist;
    /// the sink token prevents escaped writers from mutating later transactions.
    /// </summary>
    public readonly struct CoCoActorContextWriter
    {
        private readonly ICoCoActorContextBinding _binding;
        private readonly CoCoActorContextBindingDescriptor _descriptor;
        private readonly ICoCoActorContextValueSink _sink;
        private readonly ulong _token;

        internal CoCoActorContextWriter(
            ICoCoActorContextBinding binding,
            CoCoActorContextBindingDescriptor descriptor,
            ICoCoActorContextValueSink sink,
            ulong token)
        {
            _binding = binding;
            _descriptor = descriptor;
            _sink = sink;
            _token = token;
        }

        public bool IsValid => _binding != null &&
                               _descriptor != null &&
                               _descriptor.IsValid &&
                               _sink != null &&
                               _sink.IsActive(_token, _binding);

        public bool TryWrite<TValue>(
            CoCoStateSlot<TValue> slot,
            in TValue value)
            where TValue : unmanaged
        {
            return slot.IsValid
                ? TryWrite(slot.SlotId, value)
                : RejectWrite();
        }

        public bool TryWrite<TValue>(
            CoCoStateSlotId slotId,
            in TValue value)
            where TValue : unmanaged
        {
            if (!IsValid || !_descriptor.Produces<TValue>(slotId))
            {
                return RejectWrite();
            }

            if (_sink.TryWrite(_token, _binding, slotId, value))
            {
                return true;
            }

            return RejectWrite();
        }

        private bool RejectWrite()
        {
            _sink?.RejectWrite(_token, _binding);
            return false;
        }
    }

    /// <summary>
    /// Immutable callback context for one per-Host Actor snapshot. It never exposes the
    /// current candidate for reads; project code observes only PreviousContext and writes
    /// through the token-bound whitelist writer.
    /// </summary>
    public readonly struct CoCoActorContextCaptureContext
    {
        internal CoCoActorContextCaptureContext(
            CoCoGraphInstanceId graphInstanceId,
            in CoCoTickFrame tickFrame,
            CoCoContextFrameReadView previousContext,
            CoCoActorContextWriter writer)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            Writer = writer;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTickFrame TickFrame { get; }
        public CoCoContextFrameReadView PreviousContext { get; }
        public CoCoActorContextWriter Writer { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               Writer.IsValid;
    }
}
