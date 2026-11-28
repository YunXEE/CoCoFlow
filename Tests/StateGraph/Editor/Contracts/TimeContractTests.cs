using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class TimeContractTests
    {
        [Test]
        public void TickSequenceAndEpochAcceptZeroIndependently()
        {
            var tick = new TimelineTick(0UL);
            var sequence = new ExecutionSequence(0UL);
            var epoch = new TimelineEpoch(0UL);

            Assert.AreEqual(0UL, tick.Value);
            Assert.AreEqual(0UL, sequence.Value);
            Assert.AreEqual(0UL, epoch.Value);
            Assert.AreNotEqual(typeof(TimelineTick), typeof(ExecutionSequence));
            Assert.AreNotEqual(typeof(ExecutionSequence), typeof(TimelineEpoch));
        }

        [Test]
        public void TimelinePositionAcceptsZeroAndPositiveFiniteSeconds()
        {
            Assert.IsTrue(TimelinePosition.TryCreate(0d, out var zero));
            Assert.IsTrue(TimelinePosition.TryCreate(12.5d, out var positive));
            TimelinePosition initial = default;

            Assert.IsTrue(initial.IsValid);
            Assert.AreEqual(0d, initial.Seconds);
            Assert.IsTrue(zero.IsValid);
            Assert.AreEqual(0d, zero.Seconds);
            Assert.IsTrue(positive.IsValid);
            Assert.AreEqual(12.5d, positive.Seconds);
        }

        [Test]
        public void TimelinePositionRejectsNegativeNanAndInfiniteSeconds()
        {
            double[] invalidValues =
            {
                -0.01d,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity
            };

            foreach (double invalidValue in invalidValues)
            {
                Assert.IsFalse(TimelinePosition.TryCreate(invalidValue, out var position));
                Assert.IsFalse(position.IsValid);
                Assert.IsTrue(double.IsNaN(position.Seconds));
            }
        }

        [Test]
        public void TimelinePositionCanMoveBackwardOnlyAsAnExplicitValueRestore()
        {
            Assert.IsTrue(TimelinePosition.TryCreate(12d, out var later));
            Assert.IsTrue(TimelinePosition.TryCreate(3d, out var restored));

            Assert.Less(restored.Seconds, later.Seconds);
        }

        [Test]
        public void TickFrameAcceptsPositiveFiniteDeltaAndZeroTimelineCounters()
        {
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsTrue(TimelinePosition.TryCreate(0d, out var timelinePosition));

            bool created = TickFrame.TryCreate(
                0.02d,
                timelineId,
                timelinePosition,
                new TimelineTick(0UL),
                clockDomainId,
                new ExecutionSequence(0UL),
                new TimelineEpoch(0UL),
                out var frame,
                out var diagnostic);

            Assert.IsTrue(created);
            Assert.IsTrue(frame.IsValid);
            Assert.AreEqual(0.02d, frame.DeltaTime);
            Assert.AreEqual(timelineId, frame.TimelineId);
            Assert.AreEqual(0d, frame.TimelinePosition.Seconds);
            Assert.AreEqual(0UL, frame.Tick.Value);
            Assert.AreEqual(0UL, frame.ExecutionSequence.Value);
            Assert.AreEqual(0UL, frame.TimelineEpoch.Value);
            Assert.IsTrue(diagnostic.IsNone);
        }

        [Test]
        public void TickFrameRejectsNonPositiveNanAndInfiniteDelta()
        {
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsTrue(TimelinePosition.TryCreate(0d, out var timelinePosition));
            double[] invalidValues =
            {
                0d,
                -0.01d,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity
            };

            foreach (double invalidValue in invalidValues)
            {
                bool created = TickFrame.TryCreate(
                    invalidValue,
                    timelineId,
                    timelinePosition,
                    new TimelineTick(0UL),
                    clockDomainId,
                    new ExecutionSequence(0UL),
                    new TimelineEpoch(0UL),
                    out var frame,
                    out var diagnostic);

                Assert.IsFalse(created);
                Assert.IsFalse(frame.IsValid);
                Assert.AreEqual(CoCoDiagnosticDomain.Time, diagnostic.Domain);
                CoCoDiagnosticCode expectedCode = double.IsNaN(invalidValue) || double.IsInfinity(invalidValue)
                    ? CoCoDiagnosticCode.NonFiniteDeltaTime
                    : CoCoDiagnosticCode.NonPositiveDeltaTime;
                Assert.AreEqual(expectedCode, diagnostic.Code);
                Assert.IsTrue(diagnostic.IsError);
            }
        }

        [Test]
        public void TickFrameRejectsInvalidTimelineAndClockIdentities()
        {
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsTrue(TimelinePosition.TryCreate(0d, out var timelinePosition));

            Assert.IsFalse(TickFrame.TryCreate(
                0.02d,
                default,
                timelinePosition,
                new TimelineTick(1UL),
                clockDomainId,
                new ExecutionSequence(2UL),
                new TimelineEpoch(3UL),
                out _,
                out var timelineDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidIdentifier, timelineDiagnostic.Code);

            Assert.IsFalse(TickFrame.TryCreate(
                0.02d,
                timelineId,
                timelinePosition,
                new TimelineTick(1UL),
                default,
                new ExecutionSequence(2UL),
                new TimelineEpoch(3UL),
                out _,
                out var clockDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidClockDomain, clockDiagnostic.Code);
        }

        [Test]
        public void TickFrameRejectsFailedTimelinePositionOutput()
        {
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsFalse(TimelinePosition.TryCreate(-1d, out var invalidPosition));

            Assert.IsFalse(TickFrame.TryCreate(
                0.02d,
                timelineId,
                invalidPosition,
                new TimelineTick(1UL),
                clockDomainId,
                new ExecutionSequence(2UL),
                new TimelineEpoch(3UL),
                out var frame,
                out var diagnostic));

            Assert.IsFalse(frame.IsValid);
            Assert.AreEqual(CoCoDiagnosticDomain.Time, diagnostic.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidTimelinePosition, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void TickFrameHasNoDirectionOrPauseSurface()
        {
            Assert.IsEmpty(typeof(TickFrame).GetMember("Direction"));
            Assert.IsEmpty(typeof(TickFrame).GetMember("Paused"));
            Assert.IsEmpty(typeof(TickFrame).GetMember("IsPaused"));
        }
    }
}
