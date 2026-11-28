using System;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling.Temporal.Tests
{
    public sealed class PoolTemporalSidecarTests
    {
        [Test]
        public void ForwardCaptureStoresPresentIdsAndOverwritesOldestFrame()
        {
            TemporalEntityId firstId = EntityId(1UL);
            TemporalEntityId secondId = EntityId(2UL);
            var first = Record(firstId, present: true);
            var second = Record(secondId, present: false);
            var records = new[] { first, second };
            using (var history = new PoolTemporalSidecar(2))
            {
                PublishForward(history, records, FrameInfo(1UL));
                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history.ContainsAtDepth(0, firstId), Is.True);
                Assert.That(history.ContainsAtDepth(0, secondId), Is.False);

                first.AuthorityPresent = false;
                second.AuthorityPresent = true;
                PublishForward(history, records, FrameInfo(2UL));
                Assert.That(history.Count, Is.EqualTo(2));
                Assert.That(history.ContainsAtDepth(0, secondId), Is.True);
                Assert.That(history.ContainsAtDepth(1, firstId), Is.True);

                first.AuthorityPresent = true;
                PublishForward(history, records, FrameInfo(3UL));
                Assert.That(history.Count, Is.EqualTo(2));
                Assert.That(history.GetEntityCountAtDepth(0), Is.EqualTo(2));
                Assert.That(history.ContainsAtDepth(0, firstId), Is.True);
                Assert.That(history.ContainsAtDepth(0, secondId), Is.True);
                Assert.That(
                    history.ContainsAtDepth(1, firstId),
                    Is.False,
                    "The oldest frame must be overwritten by the fixed-capacity ring.");
                Assert.That(history.ContainsAtDepth(1, secondId), Is.True);
            }
        }

        [Test]
        public void CancelledCapturePublishesNoPartialPresence()
        {
            TemporalEntityId entityId = EntityId(3UL);
            var record = Record(entityId, present: true);
            using (var history = new PoolTemporalSidecar(3))
            {
                PublishForward(history, new[] { record }, FrameInfo(1UL));
                record.AuthorityPresent = false;

                Assert.That(
                    history.TryPrepareForwardCapture(
                        new[] { record },
                        1,
                        FrameInfo(2UL)),
                    Is.True);
                Assert.That(history.HasPreparedCapture, Is.True);
                history.CancelPreparedCaptureNoFail();

                Assert.That(history.HasPreparedCapture, Is.False);
                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history.ContainsAtDepth(0, entityId), Is.True);
            }
        }

        [Test]
        public void BranchCaptureDiscardsFuturePresenceFrames()
        {
            TemporalEntityId firstId = EntityId(4UL);
            TemporalEntityId futureId = EntityId(5UL);
            var first = Record(firstId, present: true);
            var future = Record(futureId, present: false);
            var records = new[] { first, future };
            using (var history = new PoolTemporalSidecar(4))
            {
                PublishForward(history, records, FrameInfo(1UL));
                first.AuthorityPresent = false;
                future.AuthorityPresent = true;
                PublishForward(history, records, FrameInfo(2UL));
                PublishForward(history, records, FrameInfo(3UL));

                Assert.That(history.Count, Is.EqualTo(3));
                Assert.That(history.IsReachable(futureId), Is.True);
                Assert.That(
                    history.TryPrepareBranchCapture(2, FrameInfo(4UL)),
                    Is.True);
                history.PublishBranchCaptureNoFail();

                Assert.That(history.Count, Is.EqualTo(2));
                Assert.That(history.ContainsAtDepth(0, firstId), Is.True);
                Assert.That(history.ContainsAtDepth(1, firstId), Is.True);
                Assert.That(history.IsReachable(futureId), Is.False);
            }
        }

        [Test]
        public void AuthorityResetReplacesHistoryWithCurrentPresenceBaseline()
        {
            TemporalEntityId retainedId = EntityId(6UL);
            TemporalEntityId historicalId = EntityId(7UL);
            var retained = Record(retainedId, present: false);
            var historical = Record(historicalId, present: true);
            var records = new[] { retained, historical };
            using (var history = new PoolTemporalSidecar(4))
            {
                PublishForward(history, records, FrameInfo(1UL));
                historical.AuthorityPresent = false;
                retained.AuthorityPresent = true;
                PublishForward(history, records, FrameInfo(2UL));
                PublishForward(history, records, FrameInfo(3UL));

                Assert.That(history.Count, Is.EqualTo(3));
                Assert.That(history.IsReachable(historicalId), Is.True);
                Assert.That(
                    history.TryPrepareAuthorityReset(
                        records,
                        records.Length,
                        FrameInfo(10UL)),
                    Is.True);
                Assert.That(history.Count, Is.EqualTo(3));
                Assert.That(history.IsReachable(historicalId), Is.True);

                history.PublishAuthorityResetNoFail();

                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history.ContainsAtDepth(0, retainedId), Is.True);
                Assert.That(history.IsReachable(historicalId), Is.False);
                Assert.That(history.GetEntityCountAtDepth(1), Is.Zero);
            }
        }

        [Test]
        public void CancelledAuthorityResetPreservesEveryPublishedFrame()
        {
            TemporalEntityId historicalId = EntityId(8UL);
            TemporalEntityId currentId = EntityId(9UL);
            var historical = Record(historicalId, present: true);
            var current = Record(currentId, present: false);
            var records = new[] { historical, current };
            using (var history = new PoolTemporalSidecar(3))
            {
                PublishForward(history, records, FrameInfo(1UL));
                historical.AuthorityPresent = false;
                current.AuthorityPresent = true;
                PublishForward(history, records, FrameInfo(2UL));

                Assert.That(
                    history.TryPrepareAuthorityReset(
                        records,
                        records.Length,
                        FrameInfo(10UL)),
                    Is.True);
                history.CancelPreparedCaptureNoFail();

                Assert.That(history.HasPreparedCapture, Is.False);
                Assert.That(history.Count, Is.EqualTo(2));
                Assert.That(history.ContainsAtDepth(0, currentId), Is.True);
                Assert.That(
                    history.ContainsAtDepth(1, historicalId),
                    Is.True);
            }
        }

        [Test]
        public void HistoryStorageContainsOnlyPureIdentityAndFrameValues()
        {
            Type historyType = typeof(PoolTemporalSidecar);
            Type frameType = historyType.GetNestedType(
                "Frame",
                BindingFlags.NonPublic);
            Assert.That(frameType, Is.Not.Null);

            Type[] forbiddenAuthorities =
            {
                typeof(UnityEngine.Object),
                typeof(ContentLease),
                typeof(PooledHandle),
                typeof(PoolTemporalToken),
                typeof(Delegate)
            };
            FieldInfo[] fields = new[] { historyType, frameType }
                .SelectMany(type => type.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly))
                .ToArray();

            foreach (FieldInfo field in fields)
            {
                Type valueType = Unwrap(field.FieldType);
                foreach (Type forbidden in forbiddenAuthorities)
                {
                    Assert.That(
                        forbidden.IsAssignableFrom(valueType),
                        Is.False,
                        field.DeclaringType?.FullName + "." + field.Name +
                        " retains forbidden " + valueType.FullName);
                }

                Assert.That(
                    valueType,
                    Is.Not.EqualTo(typeof(object)),
                    field.Name + " must not retain an arbitrary object payload.");
                Assert.That(
                    valueType == typeof(byte),
                    Is.False,
                    field.Name + " must not retain an arbitrary byte payload.");
            }
        }

        private static PoolTemporalRecord Record(
            TemporalEntityId entityId,
            bool present) =>
            new PoolTemporalRecord
            {
                EntityId = entityId,
                AuthorityPresent = present,
                ProjectedPresent = present,
                BaselinePresent = present
            };

        private static void PublishForward(
            PoolTemporalSidecar history,
            PoolTemporalRecord[] records,
            TemporalFrameInfo frameInfo)
        {
            Assert.That(
                history.TryPrepareForwardCapture(
                    records,
                    records.Length,
                    frameInfo),
                Is.True);
            history.PublishForwardCaptureNoFail();
        }

        private static TemporalEntityId EntityId(ulong low)
        {
            Assert.That(
                TemporalEntityId.TryCreate(0xCAFEUL, low, out TemporalEntityId id),
                Is.True);
            return id;
        }

        private static TemporalFrameInfo FrameInfo(ulong value)
        {
            Assert.That(
                GraphInstanceId.TryCreate(77UL, out GraphInstanceId graphId),
                Is.True);
            Assert.That(
                TimelineId.TryCreate(88UL, 99UL, out TimelineId timelineId),
                Is.True);
            Assert.That(
                TimelinePosition.TryCreate(
                    value * 0.016d,
                    out TimelinePosition position),
                Is.True);
            Assert.That(
                ClockDomainId.TryCreate(1UL, out ClockDomainId clockId),
                Is.True);
            Assert.That(
                TickFrame.TryCreate(
                    0.016d,
                    timelineId,
                    position,
                    new TimelineTick(value),
                    clockId,
                    new ExecutionSequence(value),
                    new TimelineEpoch(1UL),
                    out TickFrame tickFrame,
                    out CoCoDiagnostic tickDiagnostic),
                Is.True,
                tickDiagnostic.Message);

            ConstructorInfo constructor = typeof(TemporalFrameInfo).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(GraphInstanceId),
                    typeof(TickFrame).MakeByRefType(),
                    typeof(ContextRevision),
                    typeof(ContextFrameOrigin)
                },
                null);
            Assert.That(constructor, Is.Not.Null);
            var revision = new ContextRevision(value);
            ContextFrameOrigin origin = ContextFrameOrigin.Commit();
            object boxed = constructor.Invoke(
                new object[] { graphId, tickFrame, revision, origin });
            var info = (TemporalFrameInfo)boxed;
            Assert.That(info.IsValid, Is.True);
            return info;
        }

        private static Type Unwrap(Type type)
        {
            while (type.HasElementType)
            {
                type = type.GetElementType();
            }

            return type;
        }
    }
}
