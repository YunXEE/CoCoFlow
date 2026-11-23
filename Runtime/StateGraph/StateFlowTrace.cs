using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    [Flags]
    public enum StateFlowTraceKind
    {
        None = 0,
        Tick = 1 << 0,
        Transition = 1 << 1,
        OperationSection = 1 << 2,
        OperatorOutcome = 1 << 3,
        ContextCommit = 1 << 4,
        EventSequence = 1 << 5,
        EventPublished = 1 << 6,
        Diagnostic = 1 << 7,
        Cancelled = 1 << 8,
        ActivePath = 1 << 9,
        All = Tick | Transition | OperationSection | OperatorOutcome |
              ContextCommit | EventSequence | EventPublished | Diagnostic | Cancelled |
              ActivePath
    }

    public enum StateFlowTransitionRole
    {
        None = 0,
        Candidate = 1,
        Winner = 2
    }

    /// <summary>
    /// Reference-free identity snapshot of a ContextFrame used by trace entries.
    /// The default-backed form preserves the exact layout identity without
    /// fabricating a committed frame header or revision.
    /// </summary>
    public readonly struct StateFlowTraceFrameReference : IEquatable<StateFlowTraceFrameReference>
    {
        private StateFlowTraceFrameReference(
            StateFlowFrameIdentity identity,
            FrameLayoutId layoutId,
            uint layoutVersion,
            ulong layoutSchemaHash,
            ContextRevision revision,
            bool hasCommittedFrame)
        {
            Identity = identity;
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            LayoutSchemaHash = layoutSchemaHash;
            Revision = revision;
            HasCommittedFrame = hasCommittedFrame;
        }

        public StateFlowFrameIdentity Identity { get; }
        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public ContextRevision Revision { get; }
        public bool HasCommittedFrame { get; }

        public bool IsValid => LayoutId.IsValid &&
                               LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL &&
                               (HasCommittedFrame
                                   ? Identity.IsValid &&
                                     Identity.Kind == StateFlowFrameKind.Context &&
                                     Revision.IsValid
                                   : !Identity.IsValid && !Revision.IsValid);

        public static bool TryCreate(
            ContextFrameReadView context,
            out StateFlowTraceFrameReference reference)
        {
            if (!context.IsValid || context.Layout == null)
            {
                reference = default;
                return false;
            }

            ContextFrameLayout layout = context.Layout;
            if (context.HasCommittedFrame)
            {
                StateFlowFrameHeader header = context.Header;
                if (!header.IsValid ||
                    !header.HasExactLayoutIdentity ||
                    header.Identity.Kind != StateFlowFrameKind.Context ||
                    header.LayoutId != layout.LayoutId ||
                    header.LayoutVersion != layout.Version ||
                    header.LayoutSchemaHash != layout.SchemaHash ||
                    !context.Revision.IsValid)
                {
                    reference = default;
                    return false;
                }

                reference = new StateFlowTraceFrameReference(
                    header.Identity,
                    header.LayoutId,
                    header.LayoutVersion,
                    header.LayoutSchemaHash,
                    context.Revision,
                    true);
                return true;
            }

            reference = new StateFlowTraceFrameReference(
                default,
                layout.LayoutId,
                layout.Version,
                layout.SchemaHash,
                default,
                false);
            return reference.IsValid;
        }

        internal static bool TryCreateCommitted(
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            in TickFrame tickFrame,
            ContextRevision revision,
            out StateFlowTraceFrameReference reference)
        {
            if (layout == null ||
                !revision.IsValid ||
                !StateFlowFrameHeader.TryCreate(
                    graphInstanceId,
                    layout,
                    StateFlowFrameKind.Context,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                reference = default;
                return false;
            }

            reference = new StateFlowTraceFrameReference(
                header.Identity,
                header.LayoutId,
                header.LayoutVersion,
                header.LayoutSchemaHash,
                revision,
                true);
            return reference.IsValid;
        }

        public bool Equals(StateFlowTraceFrameReference other)
        {
            return Identity == other.Identity &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   Revision == other.Revision &&
                   HasCommittedFrame == other.HasCommittedFrame;
        }

        public override bool Equals(object obj) =>
            obj is StateFlowTraceFrameReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Identity.GetHashCode();
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)LayoutVersion;
                hashCode = (hashCode * 397) ^ LayoutSchemaHash.GetHashCode();
                hashCode = (hashCode * 397) ^ Revision.GetHashCode();
                hashCode = (hashCode * 397) ^ HasCommittedFrame.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(
            StateFlowTraceFrameReference left,
            StateFlowTraceFrameReference right) => left.Equals(right);

        public static bool operator !=(
            StateFlowTraceFrameReference left,
            StateFlowTraceFrameReference right) => !left.Equals(right);
    }

    public readonly struct StateFlowTraceEntry : IEquatable<StateFlowTraceEntry>
    {
        internal StateFlowTraceEntry(
            StateFlowTraceKind kind,
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            ContextRevision previousRevision,
            ContextRevision newRevision,
            StateId stateId,
            TransitionId transitionId,
            OperationSectionId sectionId,
            OperatorId operatorId,
            OperatorOutcomeStatus operatorOutcome,
            EventSequence firstEventSequence,
            EventSequence lastEventSequence,
            CoCoDiagnosticDomain diagnosticDomain,
            CoCoDiagnosticCode diagnosticCode,
            LayerId layerId = default,
            StateFlowTraceFrameReference frame = default,
            StateFlowTraceFrameReference previousContext = default,
            StateFlowTransitionRole transitionRole = StateFlowTransitionRole.None)
        {
            Kind = kind;
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousRevision = previousRevision;
            NewRevision = newRevision;
            StateId = stateId;
            TransitionId = transitionId;
            SectionId = sectionId;
            OperatorId = operatorId;
            OperatorOutcome = operatorOutcome;
            FirstEventSequence = firstEventSequence;
            LastEventSequence = lastEventSequence;
            DiagnosticDomain = diagnosticDomain;
            DiagnosticCode = diagnosticCode;
            LayerId = layerId;
            Frame = frame;
            PreviousContext = previousContext;
            TransitionRole = transitionRole;
        }

        public StateFlowTraceKind Kind { get; }
        public GraphInstanceId GraphInstanceId { get; }
        public TickFrame TickFrame { get; }
        public ContextRevision PreviousRevision { get; }
        public ContextRevision NewRevision { get; }
        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public TransitionId TransitionId { get; }
        public OperationSectionId SectionId { get; }
        public OperatorId OperatorId { get; }
        public OperatorOutcomeStatus OperatorOutcome { get; }
        public EventSequence FirstEventSequence { get; }
        public EventSequence LastEventSequence { get; }
        public CoCoDiagnosticDomain DiagnosticDomain { get; }
        public CoCoDiagnosticCode DiagnosticCode { get; }
        public StateFlowTraceFrameReference Frame { get; }
        public StateFlowTraceFrameReference PreviousContext { get; }
        public StateFlowTransitionRole TransitionRole { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               IsSingleKind(Kind) &&
                               HasValidContextReferences() &&
                               HasValidKindIdentity();

        internal static StateFlowTraceEntry Tick(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Tick,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Transition(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            LayerId layerId,
            TransitionId transitionId,
            StateFlowTransitionRole transitionRole = StateFlowTransitionRole.Winner,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Transition,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                transitionId,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                layerId,
                previousContext: previousContext,
                transitionRole: transitionRole);

        internal static StateFlowTraceEntry Path(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            LayerId layerId,
            StateId stateId,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.ActivePath,
                graphInstanceId,
                tickFrame,
                default,
                default,
                stateId,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                layerId,
                frame: frame);

        internal static StateFlowTraceEntry Operation(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            OperationSectionId sectionId,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.OperationSection,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                sectionId,
                default,
                default,
                default,
                default,
                default,
                default,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Operator(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            OperatorId operatorId,
            OperatorOutcomeStatus outcome,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.OperatorOutcome,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                operatorId,
                outcome,
                default,
                default,
                default,
                default,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Commit(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            ContextRevision previousRevision,
            ContextRevision newRevision,
            StateFlowTraceFrameReference previousContext = default,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.ContextCommit,
                graphInstanceId,
                tickFrame,
                previousRevision,
                newRevision,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                frame: frame,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Sequence(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            EventSequence first,
            EventSequence last,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.EventSequence,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                first,
                last,
                default,
                default,
                frame: frame);

        internal static StateFlowTraceEntry Published(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            EventSequence sequence,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.EventPublished,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                sequence,
                sequence,
                default,
                default,
                frame: frame);

        internal static StateFlowTraceEntry Diagnostic(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            CoCoDiagnostic diagnostic,
            StateFlowTraceFrameReference previousContext = default,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Diagnostic,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                diagnostic.Domain,
                diagnostic.Code,
                frame: frame,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Cancelled(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            CoCoDiagnostic diagnostic,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Cancelled,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                diagnostic.Domain,
                diagnostic.Code,
                previousContext: previousContext);

        private static bool IsSingleKind(StateFlowTraceKind kind)
        {
            int value = (int)kind;
            return value > 0 && (value & (value - 1)) == 0 &&
                   (kind & StateFlowTraceKind.All) == kind;
        }

        private bool HasValidKindIdentity()
        {
            switch (Kind)
            {
                case StateFlowTraceKind.Tick:
                    return TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.ActivePath:
                    return LayerId.IsValid &&
                           StateId.IsValid &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.Transition:
                    return LayerId.IsValid &&
                           TransitionId.IsValid &&
                           (TransitionRole == StateFlowTransitionRole.Candidate ||
                            TransitionRole == StateFlowTransitionRole.Winner);
                case StateFlowTraceKind.OperationSection:
                    return SectionId.IsValid && TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.OperatorOutcome:
                    return OperatorId.IsValid &&
                           OperatorOutcome >= OperatorOutcomeStatus.Succeeded &&
                           OperatorOutcome <= OperatorOutcomeStatus.ClaimDenied &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.ContextCommit:
                    return NewRevision.IsValid &&
                           TransitionRole == StateFlowTransitionRole.None &&
                           (!Frame.IsValid ||
                            (Frame.HasCommittedFrame && Frame.Revision == NewRevision)) &&
                           (!PreviousContext.IsValid ||
                            (PreviousContext.HasCommittedFrame
                                ? PreviousContext.Revision == PreviousRevision
                                : !PreviousRevision.IsValid));
                case StateFlowTraceKind.EventSequence:
                    return FirstEventSequence.IsValid &&
                           LastEventSequence.IsValid &&
                           FirstEventSequence.Value <= LastEventSequence.Value &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.EventPublished:
                    return FirstEventSequence.IsValid &&
                           FirstEventSequence == LastEventSequence &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.Diagnostic:
                case StateFlowTraceKind.Cancelled:
                    return DiagnosticDomain != CoCoDiagnosticDomain.None &&
                           DiagnosticCode != CoCoDiagnosticCode.None &&
                           TransitionRole == StateFlowTransitionRole.None;
                default:
                    return false;
            }
        }

        private bool HasValidContextReferences()
        {
            return (Frame == default || Frame.IsValid) &&
                   (PreviousContext == default || PreviousContext.IsValid);
        }

        public bool Equals(StateFlowTraceEntry other)
        {
            return Kind == other.Kind &&
                   GraphInstanceId == other.GraphInstanceId &&
                   TickFrame == other.TickFrame &&
                   PreviousRevision == other.PreviousRevision &&
                   NewRevision == other.NewRevision &&
                   LayerId == other.LayerId &&
                   StateId == other.StateId &&
                   TransitionId == other.TransitionId &&
                   SectionId == other.SectionId &&
                   OperatorId == other.OperatorId &&
                   OperatorOutcome == other.OperatorOutcome &&
                   FirstEventSequence == other.FirstEventSequence &&
                   LastEventSequence == other.LastEventSequence &&
                   DiagnosticDomain == other.DiagnosticDomain &&
                   DiagnosticCode == other.DiagnosticCode &&
                   Frame == other.Frame &&
                   PreviousContext == other.PreviousContext &&
                   TransitionRole == other.TransitionRole;
        }

        public override bool Equals(object obj) => obj is StateFlowTraceEntry other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ GraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ TickFrame.GetHashCode();
                hashCode = (hashCode * 397) ^ LayerId.GetHashCode();
                hashCode = (hashCode * 397) ^ StateId.GetHashCode();
                hashCode = (hashCode * 397) ^ TransitionId.GetHashCode();
                hashCode = (hashCode * 397) ^ SectionId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperatorId.GetHashCode();
                hashCode = (hashCode * 397) ^ FirstEventSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)DiagnosticCode;
                hashCode = (hashCode * 397) ^ Frame.GetHashCode();
                hashCode = (hashCode * 397) ^ PreviousContext.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)TransitionRole;
                return hashCode;
            }
        }

        public static bool operator ==(StateFlowTraceEntry left, StateFlowTraceEntry right) =>
            left.Equals(right);

        public static bool operator !=(StateFlowTraceEntry left, StateFlowTraceEntry right) =>
            !left.Equals(right);
    }

    public readonly struct StateFlowTraceFilter
    {
        public StateFlowTraceFilter(
            StateFlowTraceKind kinds,
            GraphInstanceId graphInstanceId = default,
            OperatorId operatorId = default,
            OperationSectionId sectionId = default,
            CoCoDiagnosticCode diagnosticCode = CoCoDiagnosticCode.None,
            LayerId layerId = default,
            StateId stateId = default,
            TransitionId transitionId = default)
        {
            Kinds = kinds;
            GraphInstanceId = graphInstanceId;
            OperatorId = operatorId;
            SectionId = sectionId;
            DiagnosticCode = diagnosticCode;
            LayerId = layerId;
            StateId = stateId;
            TransitionId = transitionId;
        }

        public static StateFlowTraceFilter All => new StateFlowTraceFilter(StateFlowTraceKind.All);

        public StateFlowTraceKind Kinds { get; }
        public GraphInstanceId GraphInstanceId { get; }
        public OperatorId OperatorId { get; }
        public OperationSectionId SectionId { get; }
        public CoCoDiagnosticCode DiagnosticCode { get; }
        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public TransitionId TransitionId { get; }

        internal bool Matches(in StateFlowTraceEntry entry)
        {
            StateFlowTraceKind kinds = Kinds == StateFlowTraceKind.None
                ? StateFlowTraceKind.All
                : Kinds;
            return entry.IsValid &&
                   (kinds & entry.Kind) != 0 &&
                   (!GraphInstanceId.IsValid || entry.GraphInstanceId == GraphInstanceId) &&
                   (!OperatorId.IsValid || entry.OperatorId == OperatorId) &&
                   (!SectionId.IsValid || entry.SectionId == SectionId) &&
                   (!LayerId.IsValid || entry.LayerId == LayerId) &&
                   (!StateId.IsValid || entry.StateId == StateId) &&
                   (!TransitionId.IsValid || entry.TransitionId == TransitionId) &&
                   (DiagnosticCode == CoCoDiagnosticCode.None || entry.DiagnosticCode == DiagnosticCode);
        }
    }

    public interface IStateFlowTrace
    {
        int Capacity { get; }
        int Count { get; }
        ulong TotalWritten { get; }
        int CopyLatestTo(Span<StateFlowTraceEntry> destination, StateFlowTraceFilter filter = default);
    }

    internal sealed class StateFlowTraceBuffer : IStateFlowTrace
    {
        private readonly StateFlowTraceEntry[] _entries;
        private int _next;

        public StateFlowTraceBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must be positive.");
            }

            _entries = new StateFlowTraceEntry[capacity];
        }

        public int Capacity => _entries.Length;
        public int Count { get; private set; }
        public ulong TotalWritten { get; private set; }

        internal bool Append(in StateFlowTraceEntry entry)
        {
            if (!entry.IsValid)
            {
                return false;
            }

            _entries[_next] = entry;
            _next++;
            if (_next == _entries.Length)
            {
                _next = 0;
            }

            if (Count < _entries.Length)
            {
                Count++;
            }

            if (TotalWritten < ulong.MaxValue)
            {
                TotalWritten++;
            }

            return true;
        }

        public int CopyLatestTo(
            Span<StateFlowTraceEntry> destination,
            StateFlowTraceFilter filter = default)
        {
            int written = 0;
            for (int offset = 0; offset < Count && written < destination.Length; offset++)
            {
                int index = _next - 1 - offset;
                if (index < 0)
                {
                    index += _entries.Length;
                }

                StateFlowTraceEntry entry = _entries[index];
                if (filter.Matches(entry))
                {
                    destination[written++] = entry;
                }
            }

            for (int left = 0, right = written - 1; left < right; left++, right--)
            {
                StateFlowTraceEntry swap = destination[left];
                destination[left] = destination[right];
                destination[right] = swap;
            }

            return written;
        }

        internal void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _next = 0;
            Count = 0;
        }
    }
}
