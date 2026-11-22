using System;

namespace CoCoFlow.Runtime.Core
{
    [Flags]
    public enum CoCoStateFlowTraceKind
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

    public enum CoCoStateFlowTransitionRole
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
    public readonly struct CoCoStateFlowTraceFrameReference : IEquatable<CoCoStateFlowTraceFrameReference>
    {
        private CoCoStateFlowTraceFrameReference(
            CoCoStateFlowFrameIdentity identity,
            CoCoFrameLayoutId layoutId,
            uint layoutVersion,
            ulong layoutSchemaHash,
            CoCoContextRevision revision,
            bool hasCommittedFrame)
        {
            Identity = identity;
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            LayoutSchemaHash = layoutSchemaHash;
            Revision = revision;
            HasCommittedFrame = hasCommittedFrame;
        }

        public CoCoStateFlowFrameIdentity Identity { get; }
        public CoCoFrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public CoCoContextRevision Revision { get; }
        public bool HasCommittedFrame { get; }

        public bool IsValid => LayoutId.IsValid &&
                               LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL &&
                               (HasCommittedFrame
                                   ? Identity.IsValid &&
                                     Identity.Kind == CoCoStateFlowFrameKind.Context &&
                                     Revision.IsValid
                                   : !Identity.IsValid && !Revision.IsValid);

        public static bool TryCreate(
            CoCoContextFrameReadView context,
            out CoCoStateFlowTraceFrameReference reference)
        {
            if (!context.IsValid || context.Layout == null)
            {
                reference = default;
                return false;
            }

            CoCoContextFrameLayout layout = context.Layout;
            if (context.HasCommittedFrame)
            {
                CoCoStateFlowFrameHeader header = context.Header;
                if (!header.IsValid ||
                    !header.HasExactLayoutIdentity ||
                    header.Identity.Kind != CoCoStateFlowFrameKind.Context ||
                    header.LayoutId != layout.LayoutId ||
                    header.LayoutVersion != layout.Version ||
                    header.LayoutSchemaHash != layout.SchemaHash ||
                    !context.Revision.IsValid)
                {
                    reference = default;
                    return false;
                }

                reference = new CoCoStateFlowTraceFrameReference(
                    header.Identity,
                    header.LayoutId,
                    header.LayoutVersion,
                    header.LayoutSchemaHash,
                    context.Revision,
                    true);
                return true;
            }

            reference = new CoCoStateFlowTraceFrameReference(
                default,
                layout.LayoutId,
                layout.Version,
                layout.SchemaHash,
                default,
                false);
            return reference.IsValid;
        }

        internal static bool TryCreateCommitted(
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout layout,
            in CoCoTickFrame tickFrame,
            CoCoContextRevision revision,
            out CoCoStateFlowTraceFrameReference reference)
        {
            if (layout == null ||
                !revision.IsValid ||
                !CoCoStateFlowFrameHeader.TryCreate(
                    graphInstanceId,
                    layout,
                    CoCoStateFlowFrameKind.Context,
                    tickFrame,
                    out CoCoStateFlowFrameHeader header))
            {
                reference = default;
                return false;
            }

            reference = new CoCoStateFlowTraceFrameReference(
                header.Identity,
                header.LayoutId,
                header.LayoutVersion,
                header.LayoutSchemaHash,
                revision,
                true);
            return reference.IsValid;
        }

        public bool Equals(CoCoStateFlowTraceFrameReference other)
        {
            return Identity == other.Identity &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   Revision == other.Revision &&
                   HasCommittedFrame == other.HasCommittedFrame;
        }

        public override bool Equals(object obj) =>
            obj is CoCoStateFlowTraceFrameReference other && Equals(other);

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
            CoCoStateFlowTraceFrameReference left,
            CoCoStateFlowTraceFrameReference right) => left.Equals(right);

        public static bool operator !=(
            CoCoStateFlowTraceFrameReference left,
            CoCoStateFlowTraceFrameReference right) => !left.Equals(right);
    }

    public readonly struct CoCoStateFlowTraceEntry : IEquatable<CoCoStateFlowTraceEntry>
    {
        internal CoCoStateFlowTraceEntry(
            CoCoStateFlowTraceKind kind,
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoContextRevision previousRevision,
            CoCoContextRevision newRevision,
            CoCoStateId stateId,
            CoCoTransitionId transitionId,
            CoCoOperationSectionId sectionId,
            CoCoOperatorId operatorId,
            CoCoOperatorOutcomeStatus operatorOutcome,
            CoCoEventSequence firstEventSequence,
            CoCoEventSequence lastEventSequence,
            CoCoDiagnosticDomain diagnosticDomain,
            CoCoDiagnosticCode diagnosticCode,
            CoCoLayerId layerId = default,
            CoCoStateFlowTraceFrameReference frame = default,
            CoCoStateFlowTraceFrameReference previousContext = default,
            CoCoStateFlowTransitionRole transitionRole = CoCoStateFlowTransitionRole.None)
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

        public CoCoStateFlowTraceKind Kind { get; }
        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTickFrame TickFrame { get; }
        public CoCoContextRevision PreviousRevision { get; }
        public CoCoContextRevision NewRevision { get; }
        public CoCoLayerId LayerId { get; }
        public CoCoStateId StateId { get; }
        public CoCoTransitionId TransitionId { get; }
        public CoCoOperationSectionId SectionId { get; }
        public CoCoOperatorId OperatorId { get; }
        public CoCoOperatorOutcomeStatus OperatorOutcome { get; }
        public CoCoEventSequence FirstEventSequence { get; }
        public CoCoEventSequence LastEventSequence { get; }
        public CoCoDiagnosticDomain DiagnosticDomain { get; }
        public CoCoDiagnosticCode DiagnosticCode { get; }
        public CoCoStateFlowTraceFrameReference Frame { get; }
        public CoCoStateFlowTraceFrameReference PreviousContext { get; }
        public CoCoStateFlowTransitionRole TransitionRole { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               IsSingleKind(Kind) &&
                               HasValidContextReferences() &&
                               HasValidKindIdentity();

        internal static CoCoStateFlowTraceEntry Tick(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoStateFlowTraceFrameReference previousContext = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.Tick,
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

        internal static CoCoStateFlowTraceEntry Transition(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoLayerId layerId,
            CoCoTransitionId transitionId,
            CoCoStateFlowTransitionRole transitionRole = CoCoStateFlowTransitionRole.Winner,
            CoCoStateFlowTraceFrameReference previousContext = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.Transition,
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

        internal static CoCoStateFlowTraceEntry Path(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoStateFlowTraceFrameReference frame = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.ActivePath,
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

        internal static CoCoStateFlowTraceEntry Operation(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoOperationSectionId sectionId,
            CoCoStateFlowTraceFrameReference previousContext = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.OperationSection,
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

        internal static CoCoStateFlowTraceEntry Operator(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoOperatorId operatorId,
            CoCoOperatorOutcomeStatus outcome,
            CoCoStateFlowTraceFrameReference previousContext = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.OperatorOutcome,
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

        internal static CoCoStateFlowTraceEntry Commit(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoContextRevision previousRevision,
            CoCoContextRevision newRevision,
            CoCoStateFlowTraceFrameReference previousContext = default,
            CoCoStateFlowTraceFrameReference frame = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.ContextCommit,
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

        internal static CoCoStateFlowTraceEntry Sequence(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoEventSequence first,
            CoCoEventSequence last,
            CoCoStateFlowTraceFrameReference frame = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.EventSequence,
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

        internal static CoCoStateFlowTraceEntry Published(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoEventSequence sequence,
            CoCoStateFlowTraceFrameReference frame = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.EventPublished,
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

        internal static CoCoStateFlowTraceEntry Diagnostic(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoDiagnostic diagnostic,
            CoCoStateFlowTraceFrameReference previousContext = default,
            CoCoStateFlowTraceFrameReference frame = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.Diagnostic,
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

        internal static CoCoStateFlowTraceEntry Cancelled(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoDiagnostic diagnostic,
            CoCoStateFlowTraceFrameReference previousContext = default) =>
            new CoCoStateFlowTraceEntry(
                CoCoStateFlowTraceKind.Cancelled,
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

        private static bool IsSingleKind(CoCoStateFlowTraceKind kind)
        {
            int value = (int)kind;
            return value > 0 && (value & (value - 1)) == 0 &&
                   (kind & CoCoStateFlowTraceKind.All) == kind;
        }

        private bool HasValidKindIdentity()
        {
            switch (Kind)
            {
                case CoCoStateFlowTraceKind.Tick:
                    return TransitionRole == CoCoStateFlowTransitionRole.None;
                case CoCoStateFlowTraceKind.ActivePath:
                    return LayerId.IsValid &&
                           StateId.IsValid &&
                           TransitionRole == CoCoStateFlowTransitionRole.None;
                case CoCoStateFlowTraceKind.Transition:
                    return LayerId.IsValid &&
                           TransitionId.IsValid &&
                           (TransitionRole == CoCoStateFlowTransitionRole.Candidate ||
                            TransitionRole == CoCoStateFlowTransitionRole.Winner);
                case CoCoStateFlowTraceKind.OperationSection:
                    return SectionId.IsValid && TransitionRole == CoCoStateFlowTransitionRole.None;
                case CoCoStateFlowTraceKind.OperatorOutcome:
                    return OperatorId.IsValid &&
                           OperatorOutcome >= CoCoOperatorOutcomeStatus.Succeeded &&
                           OperatorOutcome <= CoCoOperatorOutcomeStatus.ClaimDenied &&
                           TransitionRole == CoCoStateFlowTransitionRole.None;
                case CoCoStateFlowTraceKind.ContextCommit:
                    return NewRevision.IsValid &&
                           TransitionRole == CoCoStateFlowTransitionRole.None &&
                           (!Frame.IsValid ||
                            (Frame.HasCommittedFrame && Frame.Revision == NewRevision)) &&
                           (!PreviousContext.IsValid ||
                            (PreviousContext.HasCommittedFrame
                                ? PreviousContext.Revision == PreviousRevision
                                : !PreviousRevision.IsValid));
                case CoCoStateFlowTraceKind.EventSequence:
                    return FirstEventSequence.IsValid &&
                           LastEventSequence.IsValid &&
                           FirstEventSequence.Value <= LastEventSequence.Value &&
                           TransitionRole == CoCoStateFlowTransitionRole.None;
                case CoCoStateFlowTraceKind.EventPublished:
                    return FirstEventSequence.IsValid &&
                           FirstEventSequence == LastEventSequence &&
                           TransitionRole == CoCoStateFlowTransitionRole.None;
                case CoCoStateFlowTraceKind.Diagnostic:
                case CoCoStateFlowTraceKind.Cancelled:
                    return DiagnosticDomain != CoCoDiagnosticDomain.None &&
                           DiagnosticCode != CoCoDiagnosticCode.None &&
                           TransitionRole == CoCoStateFlowTransitionRole.None;
                default:
                    return false;
            }
        }

        private bool HasValidContextReferences()
        {
            return (Frame == default || Frame.IsValid) &&
                   (PreviousContext == default || PreviousContext.IsValid);
        }

        public bool Equals(CoCoStateFlowTraceEntry other)
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

        public override bool Equals(object obj) => obj is CoCoStateFlowTraceEntry other && Equals(other);

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

        public static bool operator ==(CoCoStateFlowTraceEntry left, CoCoStateFlowTraceEntry right) =>
            left.Equals(right);

        public static bool operator !=(CoCoStateFlowTraceEntry left, CoCoStateFlowTraceEntry right) =>
            !left.Equals(right);
    }

    public readonly struct CoCoStateFlowTraceFilter
    {
        public CoCoStateFlowTraceFilter(
            CoCoStateFlowTraceKind kinds,
            CoCoGraphInstanceId graphInstanceId = default,
            CoCoOperatorId operatorId = default,
            CoCoOperationSectionId sectionId = default,
            CoCoDiagnosticCode diagnosticCode = CoCoDiagnosticCode.None,
            CoCoLayerId layerId = default,
            CoCoStateId stateId = default,
            CoCoTransitionId transitionId = default)
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

        public static CoCoStateFlowTraceFilter All => new CoCoStateFlowTraceFilter(CoCoStateFlowTraceKind.All);

        public CoCoStateFlowTraceKind Kinds { get; }
        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoOperatorId OperatorId { get; }
        public CoCoOperationSectionId SectionId { get; }
        public CoCoDiagnosticCode DiagnosticCode { get; }
        public CoCoLayerId LayerId { get; }
        public CoCoStateId StateId { get; }
        public CoCoTransitionId TransitionId { get; }

        internal bool Matches(in CoCoStateFlowTraceEntry entry)
        {
            CoCoStateFlowTraceKind kinds = Kinds == CoCoStateFlowTraceKind.None
                ? CoCoStateFlowTraceKind.All
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

    public interface ICoCoStateFlowTrace
    {
        int Capacity { get; }
        int Count { get; }
        ulong TotalWritten { get; }
        int CopyLatestTo(Span<CoCoStateFlowTraceEntry> destination, CoCoStateFlowTraceFilter filter = default);
    }

    internal sealed class CoCoStateFlowTraceBuffer : ICoCoStateFlowTrace
    {
        private readonly CoCoStateFlowTraceEntry[] _entries;
        private int _next;

        public CoCoStateFlowTraceBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must be positive.");
            }

            _entries = new CoCoStateFlowTraceEntry[capacity];
        }

        public int Capacity => _entries.Length;
        public int Count { get; private set; }
        public ulong TotalWritten { get; private set; }

        internal bool Append(in CoCoStateFlowTraceEntry entry)
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
            Span<CoCoStateFlowTraceEntry> destination,
            CoCoStateFlowTraceFilter filter = default)
        {
            int written = 0;
            for (int offset = 0; offset < Count && written < destination.Length; offset++)
            {
                int index = _next - 1 - offset;
                if (index < 0)
                {
                    index += _entries.Length;
                }

                CoCoStateFlowTraceEntry entry = _entries[index];
                if (filter.Matches(entry))
                {
                    destination[written++] = entry;
                }
            }

            for (int left = 0, right = written - 1; left < right; left++, right--)
            {
                CoCoStateFlowTraceEntry swap = destination[left];
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
