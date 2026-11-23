using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct TickFrame : IEquatable<TickFrame>
    {
        private TickFrame(
            double deltaTime,
            TimelineId timelineId,
            TimelinePosition timelinePosition,
            TimelineTick tick,
            ClockDomainId clockDomainId,
            ExecutionSequence executionSequence,
            TimelineEpoch timelineEpoch)
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
        public TimelineId TimelineId { get; }
        public TimelinePosition TimelinePosition { get; }
        public TimelineTick Tick { get; }
        public ClockDomainId ClockDomainId { get; }
        public ExecutionSequence ExecutionSequence { get; }
        public TimelineEpoch TimelineEpoch { get; }
        public bool IsValid => DeltaTime > 0d &&
                               !double.IsNaN(DeltaTime) &&
                               !double.IsInfinity(DeltaTime) &&
                               TimelineId.IsValid &&
                               TimelinePosition.IsValid &&
                               ClockDomainId.IsValid;

        public static bool TryCreate(
            double deltaTime,
            TimelineId timelineId,
            TimelinePosition timelinePosition,
            TimelineTick tick,
            ClockDomainId clockDomainId,
            ExecutionSequence executionSequence,
            TimelineEpoch timelineEpoch,
            out TickFrame frame,
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

            frame = new TickFrame(
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

        public bool Equals(TickFrame other)
        {
            return DeltaTime.Equals(other.DeltaTime) &&
                   TimelineId == other.TimelineId &&
                   TimelinePosition == other.TimelinePosition &&
                   Tick == other.Tick &&
                   ClockDomainId == other.ClockDomainId &&
                   ExecutionSequence == other.ExecutionSequence &&
                   TimelineEpoch == other.TimelineEpoch;
        }

        public override bool Equals(object obj) => obj is TickFrame other && Equals(other);

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

        public static bool operator ==(TickFrame left, TickFrame right) => left.Equals(right);
        public static bool operator !=(TickFrame left, TickFrame right) => !left.Equals(right);
    }
}
