using System;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoTickFrame : IEquatable<CoCoTickFrame>
    {
        private CoCoTickFrame(
            double deltaTime,
            CoCoTimelineId timelineId,
            CoCoTimelinePosition timelinePosition,
            CoCoTimelineTick tick,
            CoCoClockDomainId clockDomainId,
            CoCoExecutionSequence executionSequence,
            CoCoTimelineEpoch timelineEpoch)
        {
            DeltaTime = deltaTime;
            TimelineId = timelineId;
            TimelinePosition = timelinePosition;
            Tick = tick;
            ClockDomainId = clockDomainId;
            ExecutionSequence = executionSequence;
            TimelineEpoch = timelineEpoch;
        }

        public double DeltaTime { get; }
        public CoCoTimelineId TimelineId { get; }
        public CoCoTimelinePosition TimelinePosition { get; }
        public CoCoTimelineTick Tick { get; }
        public CoCoClockDomainId ClockDomainId { get; }
        public CoCoExecutionSequence ExecutionSequence { get; }
        public CoCoTimelineEpoch TimelineEpoch { get; }
        public bool IsValid => DeltaTime > 0d &&
                               !double.IsNaN(DeltaTime) &&
                               !double.IsInfinity(DeltaTime) &&
                               TimelineId.IsValid &&
                               TimelinePosition.IsValid &&
                               ClockDomainId.IsValid;

        public static bool TryCreate(
            double deltaTime,
            CoCoTimelineId timelineId,
            CoCoTimelinePosition timelinePosition,
            CoCoTimelineTick tick,
            CoCoClockDomainId clockDomainId,
            CoCoExecutionSequence executionSequence,
            CoCoTimelineEpoch timelineEpoch,
            out CoCoTickFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime))
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonFiniteDeltaTime,
                    "DeltaTime must be finite.");
                return false;
            }

            if (deltaTime <= 0d)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "DeltaTime must be greater than zero.");
                return false;
            }

            if (!timelineId.IsValid)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Identity,
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "TimelineId must be valid.");
                return false;
            }

            if (!timelinePosition.IsValid)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidTimelinePosition,
                    "TimelinePosition must be finite and non-negative.");
                return false;
            }

            if (!clockDomainId.IsValid)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidClockDomain,
                    "ClockDomainId must be non-zero.");
                return false;
            }

            frame = new CoCoTickFrame(
                deltaTime,
                timelineId,
                timelinePosition,
                tick,
                clockDomainId,
                executionSequence,
                timelineEpoch);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Equals(CoCoTickFrame other)
        {
            return DeltaTime.Equals(other.DeltaTime) &&
                   TimelineId == other.TimelineId &&
                   TimelinePosition == other.TimelinePosition &&
                   Tick == other.Tick &&
                   ClockDomainId == other.ClockDomainId &&
                   ExecutionSequence == other.ExecutionSequence &&
                   TimelineEpoch == other.TimelineEpoch;
        }

        public override bool Equals(object obj) => obj is CoCoTickFrame other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DeltaTime.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineId.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelinePosition.GetHashCode();
                hashCode = (hashCode * 397) ^ Tick.GetHashCode();
                hashCode = (hashCode * 397) ^ ClockDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ ExecutionSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineEpoch.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(CoCoTickFrame left, CoCoTickFrame right) => left.Equals(right);
        public static bool operator !=(CoCoTickFrame left, CoCoTickFrame right) => !left.Equals(right);
    }
}
