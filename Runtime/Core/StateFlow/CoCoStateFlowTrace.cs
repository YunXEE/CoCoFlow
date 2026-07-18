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
            CoCoLayerId layerId = default)
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
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               IsSingleKind(Kind) &&
                               HasValidKindIdentity();

        internal static CoCoStateFlowTraceEntry Tick(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame) =>
            Create(CoCoStateFlowTraceKind.Tick, graphInstanceId, tickFrame);

        internal static CoCoStateFlowTraceEntry Transition(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoLayerId layerId,
            CoCoTransitionId transitionId) =>
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
                layerId);

        internal static CoCoStateFlowTraceEntry Path(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoLayerId layerId,
            CoCoStateId stateId) =>
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
                layerId);

        internal static CoCoStateFlowTraceEntry Operation(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoOperationSectionId sectionId) =>
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
                default);

        internal static CoCoStateFlowTraceEntry Operator(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoOperatorId operatorId,
            CoCoOperatorOutcomeStatus outcome) =>
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
                default);

        internal static CoCoStateFlowTraceEntry Commit(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoContextRevision previousRevision,
            CoCoContextRevision newRevision) =>
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
                default);

        internal static CoCoStateFlowTraceEntry Sequence(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoEventSequence first,
            CoCoEventSequence last) =>
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
                default);

        internal static CoCoStateFlowTraceEntry Published(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoEventSequence sequence) =>
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
                default);

        internal static CoCoStateFlowTraceEntry Diagnostic(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoDiagnostic diagnostic) =>
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
                diagnostic.Code);

        internal static CoCoStateFlowTraceEntry Cancelled(
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame,
            CoCoDiagnostic diagnostic) =>
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
                diagnostic.Code);

        private static CoCoStateFlowTraceEntry Create(
            CoCoStateFlowTraceKind kind,
            CoCoGraphInstanceId graphInstanceId,
            CoCoTickFrame tickFrame) =>
            new CoCoStateFlowTraceEntry(
                kind,
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
                default);

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
                    return true;
                case CoCoStateFlowTraceKind.ActivePath:
                    return LayerId.IsValid && StateId.IsValid;
                case CoCoStateFlowTraceKind.Transition:
                    return LayerId.IsValid && TransitionId.IsValid;
                case CoCoStateFlowTraceKind.OperationSection:
                    return SectionId.IsValid;
                case CoCoStateFlowTraceKind.OperatorOutcome:
                    return OperatorId.IsValid &&
                           OperatorOutcome >= CoCoOperatorOutcomeStatus.Succeeded &&
                           OperatorOutcome <= CoCoOperatorOutcomeStatus.ClaimDenied;
                case CoCoStateFlowTraceKind.ContextCommit:
                    return NewRevision.IsValid;
                case CoCoStateFlowTraceKind.EventSequence:
                    return FirstEventSequence.IsValid &&
                           LastEventSequence.IsValid &&
                           FirstEventSequence.Value <= LastEventSequence.Value;
                case CoCoStateFlowTraceKind.EventPublished:
                    return FirstEventSequence.IsValid &&
                           FirstEventSequence == LastEventSequence;
                case CoCoStateFlowTraceKind.Diagnostic:
                case CoCoStateFlowTraceKind.Cancelled:
                    return DiagnosticDomain != CoCoDiagnosticDomain.None &&
                           DiagnosticCode != CoCoDiagnosticCode.None;
                default:
                    return false;
            }
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
                   DiagnosticCode == other.DiagnosticCode;
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
                hashCode = (hashCode * 397) ^ OperatorId.GetHashCode();
                hashCode = (hashCode * 397) ^ FirstEventSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)DiagnosticCode;
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
            CoCoLayerId layerId = default)
        {
            Kinds = kinds;
            GraphInstanceId = graphInstanceId;
            OperatorId = operatorId;
            SectionId = sectionId;
            DiagnosticCode = diagnosticCode;
            LayerId = layerId;
        }

        public static CoCoStateFlowTraceFilter All => new CoCoStateFlowTraceFilter(CoCoStateFlowTraceKind.All);

        public CoCoStateFlowTraceKind Kinds { get; }
        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoOperatorId OperatorId { get; }
        public CoCoOperationSectionId SectionId { get; }
        public CoCoDiagnosticCode DiagnosticCode { get; }
        public CoCoLayerId LayerId { get; }

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
