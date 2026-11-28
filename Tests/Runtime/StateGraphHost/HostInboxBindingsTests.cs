using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;
using UnityEngine;
using HostComponent = CoCoFlow.Runtime.StateGraph.StateGraphHost;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class HostInboxBindingsTests
    {
        [Test]
        public void NestedHostOwnsItsAdaptersEvenWhenItsObjectIsInactive()
        {
            var parent = new GameObject("Parent Host");
            try
            {
                var child = new GameObject("Nested Host");
                child.transform.SetParent(parent.transform);
                var outer = HostComponentBoundary.For(parent.AddComponent<HostComponent>());
                var inner = HostComponentBoundary.For(child.AddComponent<HostComponent>());
                var filter = child.AddComponent<PositiveFilter>();
                child.SetActive(false);
                var explicitAdapters = new MonoBehaviour[] { filter };

                Assert.That(HostInboxBindings.TryValidateAdapters(outer, explicitAdapters, out var rejected), Is.False);
                Assert.That(rejected.Code, Is.EqualTo(CoCoDiagnosticCode.DescriptorTypeMismatch));
                Assert.That(HostInboxBindings.TryValidateAdapters(inner, explicitAdapters, out _), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CommittedEventsReachOnlyTheTargetAndDisposedSubscriptionsStopFiltering()
        {
            var receiver = new GameObject("Receiver");
            var second = new GameObject("Other receiver");
            GraphInstanceId firstId = InstanceIdAllocator.Next();
            GraphInstanceId secondId = InstanceIdAllocator.Next();
            Assert.That(secondId, Is.Not.EqualTo(firstId));
            int overflows = 0;
            using var firstInbox = new HostInboxBindings(firstId, 2, () => true, () => overflows++);
            using var secondInbox = new HostInboxBindings(secondId, 2, () => true, () => overflows++);
            try
            {
                var boundary = HostComponentBoundary.For(receiver.AddComponent<HostComponent>());
                var filter = receiver.AddComponent<PositiveFilter>();
                firstInbox.Subscribe(boundary, new MonoBehaviour[] { filter },
                    receiver.GetComponentsInChildren<MonoBehaviour>(true), new[] { typeof(Payload) });
                var otherBoundary = HostComponentBoundary.For(second.AddComponent<HostComponent>());
                secondInbox.Subscribe(otherBoundary, Array.Empty<MonoBehaviour>(), Array.Empty<MonoBehaviour>(),
                    new[] { typeof(Payload) });
                Assert.That(EventDomainId.TryCreate(47, out var domain), Is.True);
                Assert.That(EventTypeId.TryCreate(1, 47, out var type), Is.True);
                Assert.That(EventOutboxRequirement.TryCreate<Payload>(type, domain, 2, out var requirement, out _), Is.True);
                Assert.That(EventOutboxTarget.TryTargeted(firstId, EventReliability.Reliable,
                    default, default, default, out var target), Is.True);
                var outbox = new HostEventOutbox(new[] { requirement.CreateLane() }, 2);
                var rejected = new Payload { Value = -1 };
                var accepted = new Payload { Value = 12 };

                Assert.That(outbox.TryWrite(requirement, target, rejected), Is.EqualTo(EventOutboxWriteResult.Accepted));
                Assert.That(outbox.TryWrite(requirement, target, accepted), Is.EqualTo(EventOutboxWriteResult.Accepted));
                Assert.That(filter.Calls, Is.Zero);
                Assert.That(outbox.TryReserveSequences(5, out var first, out var last, out _), Is.True);
                Assert.That(first.Value, Is.EqualTo(6UL));
                Assert.That(last.Value, Is.EqualTo(7UL));
                var tick = MakeTick();
                Assert.That(outbox.PublishCommitted(secondId, tick, first, null, default, out var diagnostic),
                    Is.True, diagnostic.Message);

                firstInbox.Inbox.Seal(tick);
                secondInbox.Inbox.Seal(tick);
                var lane = firstInbox.Inbox.Find<Payload>();
                Assert.That(lane.Count, Is.EqualTo(1));
                Assert.That(lane.Read(0).Value, Is.EqualTo(12));
                Assert.That(secondInbox.Inbox.Find<Payload>().Count, Is.Zero);
                Assert.That(filter.Calls, Is.EqualTo(2));
                Assert.That(overflows, Is.Zero);

                firstInbox.Dispose();
                CoCoEventBus.Publish(ref accepted);
                Assert.That(filter.Calls, Is.EqualTo(2));
                var otherLane = secondInbox.Inbox.Find<Payload>();
                otherLane.Seal();
                Assert.That(otherLane.Count, Is.EqualTo(1));
                Assert.That(otherLane.Read(0).Value, Is.EqualTo(12));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(receiver);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        private static TickFrame MakeTick()
        {
            Assert.That(TimelineId.TryCreate(1, 47, out var timeline), Is.True);
            Assert.That(ClockDomainId.TryCreate(1, out var clock), Is.True);
            Assert.That(TimelinePosition.TryCreate(0.016, out var position), Is.True);
            Assert.That(TickFrame.TryCreate(0.016, timeline, position, new TimelineTick(1), clock,
                new ExecutionSequence(1), new TimelineEpoch(1), out var tick, out _), Is.True);
            return tick;
        }

        private struct Payload
        {
            internal int Value;
        }

        private sealed class PositiveFilter : MonoBehaviour, IEventInboxAdapter<Payload>
        {
            internal int Calls;
            public bool Accept(in Payload value)
            {
                Calls++;
                return value.Value > 0;
            }
        }
    }
}
