using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoTimeContractTests
    {
        [Test]
        public void TickSequenceAndEpochAcceptZeroIndependently()
        {
            var tick = new CoCoTimelineTick(0UL);
            var sequence = new CoCoExecutionSequence(0UL);
            var epoch = new CoCoTimelineEpoch(0UL);

            Assert.AreEqual(0UL, tick.Value);
            Assert.AreEqual(0UL, sequence.Value);
            Assert.AreEqual(0UL, epoch.Value);
            Assert.AreNotEqual(typeof(CoCoTimelineTick), typeof(CoCoExecutionSequence));
            Assert.AreNotEqual(typeof(CoCoExecutionSequence), typeof(CoCoTimelineEpoch));
        }

        [Test]
        public void TimelinePositionAcceptsZeroAndPositiveFiniteSeconds()
        {
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(0d, out var zero));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(12.5d, out var positive));

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
                Assert.IsFalse(CoCoTimelinePosition.TryCreate(invalidValue, out _));
            }
        }

        [Test]
        public void TimelinePositionCanMoveBackwardOnlyAsAnExplicitValueRestore()
        {
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(12d, out var later));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(3d, out var restored));

            Assert.Less(restored.Seconds, later.Seconds);
        }

        [Test]
        public void TickFrameAcceptsPositiveFiniteDeltaAndZeroTimelineCounters()
        {
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(0d, out var timelinePosition));

            bool created = CoCoTickFrame.TryCreate(
                0.02d,
                timelineId,
                timelinePosition,
                new CoCoTimelineTick(0UL),
                clockDomainId,
                new CoCoExecutionSequence(0UL),
                new CoCoTimelineEpoch(0UL),
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
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(0d, out var timelinePosition));
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
                bool created = CoCoTickFrame.TryCreate(
                    invalidValue,
                    timelineId,
                    timelinePosition,
                    new CoCoTimelineTick(0UL),
                    clockDomainId,
                    new CoCoExecutionSequence(0UL),
                    new CoCoTimelineEpoch(0UL),
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
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out var clockDomainId));
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out var timelineId));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(0d, out var timelinePosition));

            Assert.IsFalse(CoCoTickFrame.TryCreate(
                0.02d,
                default,
                timelinePosition,
                new CoCoTimelineTick(1UL),
                clockDomainId,
                new CoCoExecutionSequence(2UL),
                new CoCoTimelineEpoch(3UL),
                out _,
                out var timelineDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidIdentifier, timelineDiagnostic.Code);

            Assert.IsFalse(CoCoTickFrame.TryCreate(
                0.02d,
                timelineId,
                timelinePosition,
                new CoCoTimelineTick(1UL),
                default,
                new CoCoExecutionSequence(2UL),
                new CoCoTimelineEpoch(3UL),
                out _,
                out var clockDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidClockDomain, clockDiagnostic.Code);
        }

        [Test]
        public void TickFrameHasNoDirectionOrPauseSurface()
        {
            Assert.IsEmpty(typeof(CoCoTickFrame).GetMember("Direction"));
            Assert.IsEmpty(typeof(CoCoTickFrame).GetMember("Paused"));
            Assert.IsEmpty(typeof(CoCoTickFrame).GetMember("IsPaused"));
        }
    }
}
