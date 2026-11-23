using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>
    /// Portable value projection of one compiled staged Graph State. Active-path metadata
    /// and project-owned unmanaged memory make the complete record suitable for a Context Slot.
    /// </summary>
    public readonly struct GraphStateRecord<TState> : IEquatable<GraphStateRecord<TState>>
        where TState : unmanaged
    {
        private GraphStateRecord(
            LayerId layerId,
            StateId stateId,
            bool isOnActivePath,
            ActivationId activationId,
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

        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public bool IsOnActivePath { get; }
        public ActivationId ActivationId { get; }
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
            LayerId layerId,
            StateId stateId,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in TState state,
            out GraphStateRecord<TState> record)
        {
            record = new GraphStateRecord<TState>(
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
            LayerId layerId,
            StateId stateId,
            ulong memoryFingerprint,
            in TState state,
            out GraphStateRecord<TState> record) =>
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

        public bool Equals(GraphStateRecord<TState> other)
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
            obj is GraphStateRecord<TState> other && Equals(other);

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
            GraphStateRecord<TState> left,
            GraphStateRecord<TState> right) => left.Equals(right);

        public static bool operator !=(
            GraphStateRecord<TState> left,
            GraphStateRecord<TState> right) => !left.Equals(right);

        private static bool IsFiniteNonNegative(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    }

    /// <summary>
    /// AOT-safe, bidirectional projection between one runtime ActivationMemory and its
    /// portable unmanaged Context representation. Restore preparation may only mutate
    /// the supplied candidate memory; applying live authority remains a higher-level concern.
    /// </summary>
    public interface IActivationMemoryStateBinding<TMemory, TState>
        where TMemory : ActivationMemory
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
    public interface IGraphContextValueProducer<TValue>
        where TValue : unmanaged
    {
        ulong SemanticFingerprint { get; }

        bool TryProduce(
            in GraphContextCaptureContext context,
            out TValue value,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>
    /// Portable committed ownership of one Operator Claim. Default is the canonical
    /// unheld value; a held value always carries its Claim, Operator, and Activation identities.
    /// </summary>
    public readonly struct OperatorClaimState : IEquatable<OperatorClaimState>
    {
        private OperatorClaimState(
            OperatorClaimId claimId,
            OperationSectionId sectionId,
            OperatorId ownerOperatorId,
            ActivationId activationId)
        {
            ClaimId = claimId;
            SectionId = sectionId;
            OwnerOperatorId = ownerOperatorId;
            ActivationId = activationId;
        }

        public OperatorClaimId ClaimId { get; }
        public OperationSectionId SectionId { get; }
        public OperatorId OwnerOperatorId { get; }
        public ActivationId ActivationId { get; }
        public bool IsHeld => OwnerOperatorId.IsValid && ActivationId.IsValid;
        public bool IsValid => ClaimId.IsValid &&
                               SectionId.IsValid &&
                               (IsHeld ||
                                (!OwnerOperatorId.IsValid && !ActivationId.IsValid));

        public static OperatorClaimState Unheld(
            OperatorClaimId claimId,
            OperationSectionId sectionId)
        {
            if (!claimId.IsValid)
            {
                throw new ArgumentException("ClaimId must be valid.", nameof(claimId));
            }

            if (!sectionId.IsValid)
            {
                throw new ArgumentException("SectionId must be valid.", nameof(sectionId));
            }

            return new OperatorClaimState(claimId, sectionId, default, default);
        }

        public static bool TryCreateHeld(
            OperatorClaimId claimId,
            OperationSectionId sectionId,
            OperatorId ownerOperatorId,
            ActivationId activationId,
            out OperatorClaimState state)
        {
            state = new OperatorClaimState(
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

        public bool Equals(OperatorClaimState other) =>
            ClaimId == other.ClaimId &&
            SectionId == other.SectionId &&
            OwnerOperatorId == other.OwnerOperatorId &&
            ActivationId == other.ActivationId;

        public override bool Equals(object obj) =>
            obj is OperatorClaimState other && Equals(other);

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
            OperatorClaimState left,
            OperatorClaimState right) => left.Equals(right);

        public static bool operator !=(
            OperatorClaimState left,
            OperatorClaimState right) => !left.Equals(right);
    }

    internal interface IStagedGraphReadSource
    {
        bool IsActive(ulong token);

        bool TryGetActiveLeaf(
            ulong token,
            LayerId layerId,
            out StateId stateId);

        bool TryGetState<TState>(
            ulong token,
            StateId stateId,
            out GraphStateRecord<TState> state)
            where TState : unmanaged;
    }

    /// <summary>
    /// Callback-scoped read view over the currently staged Graph candidate.
    /// </summary>
    public readonly struct StagedGraphReadView
    {
        private readonly IStagedGraphReadSource _source;
        private readonly ulong _token;

        internal StagedGraphReadView(
            IStagedGraphReadSource source,
            ulong token)
        {
            _source = source;
            _token = token;
        }

        public bool IsValid => _source != null && _source.IsActive(_token);

        public bool TryGetActiveLeaf(
            LayerId layerId,
            out StateId stateId)
        {
            if (!IsValid || !layerId.IsValid)
            {
                stateId = default;
                return false;
            }

            return _source.TryGetActiveLeaf(_token, layerId, out stateId) && stateId.IsValid;
        }

        public bool TryGetState<TState>(
            StateId stateId,
            out GraphStateRecord<TState> state)
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
    public readonly struct GraphContextCaptureContext
    {
        internal GraphContextCaptureContext(
            GraphInstanceId graphInstanceId,
            in TickFrame tickFrame,
            ContextFrameReadView previousContext,
            StagedGraphReadView stagedGraph,
            StagedOperationFrame operationFrame)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            StagedGraph = stagedGraph;
            OperationFrame = operationFrame;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TickFrame TickFrame { get; }
        public ContextFrameReadView PreviousContext { get; }
        public StagedGraphReadView StagedGraph { get; }
        public StagedOperationFrame OperationFrame { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               StagedGraph.IsValid &&
                               OperationFrame.IsValid;

        public bool TryGetOperation<TSection>(
            OperationSectionRequirement requirement,
            out OperationSectionEntry<TSection> entry)
            where TSection : class, IOperationSection
        {
            if (!IsValid ||
                !requirement.IsValid ||
                requirement.SectionType != typeof(TSection) ||
                OperationFrame.Registry == null ||
                !OperationFrame.Registry.TryResolve(
                    requirement,
                    out OperationSectionHandle<TSection> handle))
            {
                entry = default;
                return false;
            }

            return OperationFrame.TryGet(handle, out entry);
        }
    }

    internal readonly struct ActorContextValueRequirement :
        IEquatable<ActorContextValueRequirement>
    {
        internal ActorContextValueRequirement(
            StateSlotId slotId,
            Type valueType)
        {
            SlotId = slotId;
            ValueType = valueType;
        }

        internal StateSlotId SlotId { get; }
        internal Type ValueType { get; }
        internal bool IsValid => SlotId.IsValid &&
                                 ValueType != null &&
                                 StateFlowTypeRules.IsReferenceFreeValueType(ValueType);

        public bool Equals(ActorContextValueRequirement other) =>
            SlotId == other.SlotId && ValueType == other.ValueType;

        public override bool Equals(object obj) =>
            obj is ActorContextValueRequirement other && Equals(other);

        public override int GetHashCode() =>
            unchecked((SlotId.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));
    }

    /// <summary>
    /// Frozen exact Actor-owned Context Slot whitelist for one Actor binding type.
    /// </summary>
    public sealed class ActorContextBindingDescriptor
    {
        private readonly ActorContextValueRequirement[] _values;
        private readonly IReadOnlyList<ActorContextValueRequirement> _readOnlyValues;
        private readonly bool _isValid;

        internal ActorContextBindingDescriptor(
            Type bindingType,
            ulong semanticFingerprint,
            ActorContextValueRequirement[] values)
        {
            BindingType = bindingType;
            SemanticFingerprint = semanticFingerprint;
            _values = values ?? Array.Empty<ActorContextValueRequirement>();
            _readOnlyValues = Array.AsReadOnly(_values);
            _isValid = bindingType != null &&
                       typeof(IActorContextBinding).IsAssignableFrom(bindingType) &&
                       semanticFingerprint != 0UL &&
                       _values.Length > 0;
        }

        public Type BindingType { get; }
        public ulong SemanticFingerprint { get; }
        public int ValueCount => _values.Length;
        public bool IsValid => _isValid;
        internal IReadOnlyList<ActorContextValueRequirement> Values => _readOnlyValues;

        internal bool Produces<TValue>(StateSlotId slotId)
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
    public sealed class ActorContextBindingDescriptorBuilder
    {
        private readonly List<ActorContextValueRequirement> _values =
            new List<ActorContextValueRequirement>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        public bool TryProduce<TValue>(
            StateSlotId slotId,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            var requirement = new ActorContextValueRequirement(slotId, typeof(TValue));
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
            out ActorContextBindingDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
            where TBinding : class, IActorContextBinding
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
            descriptor = new ActorContextBindingDescriptor(
                typeof(TBinding),
                semanticFingerprint,
                _values.ToArray());
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);
    }

    public interface IActorContextBinding
    {
        ActorContextBindingDescriptor Descriptor { get; }

        bool TryCapture(
            in ActorContextCaptureContext context,
            out CoCoDiagnostic diagnostic);
    }

    internal interface IActorContextValueSink
    {
        bool IsActive(
            ulong token,
            IActorContextBinding binding);

        void RejectWrite(
            ulong token,
            IActorContextBinding binding);

        bool TryWrite<TValue>(
            ulong token,
            IActorContextBinding binding,
            StateSlotId slotId,
            in TValue value)
            where TValue : unmanaged;
    }

    /// <summary>
    /// Callback-scoped Actor Context writer. The descriptor is the public whitelist;
    /// the sink token prevents escaped writers from mutating later transactions.
    /// </summary>
    public readonly struct ActorContextWriter
    {
        private readonly IActorContextBinding _binding;
        private readonly ActorContextBindingDescriptor _descriptor;
        private readonly IActorContextValueSink _sink;
        private readonly ulong _token;

        internal ActorContextWriter(
            IActorContextBinding binding,
            ActorContextBindingDescriptor descriptor,
            IActorContextValueSink sink,
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
            StateSlot<TValue> slot,
            in TValue value)
            where TValue : unmanaged
        {
            return slot.IsValid
                ? TryWrite(slot.SlotId, value)
                : RejectWrite();
        }

        public bool TryWrite<TValue>(
            StateSlotId slotId,
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
    public readonly struct ActorContextCaptureContext
    {
        internal ActorContextCaptureContext(
            GraphInstanceId graphInstanceId,
            in TickFrame tickFrame,
            ContextFrameReadView previousContext,
            ActorContextWriter writer)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            Writer = writer;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TickFrame TickFrame { get; }
        public ContextFrameReadView PreviousContext { get; }
        public ActorContextWriter Writer { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               Writer.IsValid;
    }
}
