using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphRuntimeTests
    {
        [Test]
        public void TransitionTickKeepsSourcePathAndTargetEntersOnFollowingTick()
        {
            Fixture fixture = Fixture.Create();

            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic diagnostic), diagnostic.Message);
            CollectionAssert.IsEmpty(fixture.Trace);

            CoCoStagedGraphStep first = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "enter:root:0", "enter:a:0", "update:root:0", "update:a:0" },
                fixture.Trace);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            fixture.Accept(first);

            fixture.Trace.Clear();
            fixture.Control.RequestAtoB = true;
            CoCoStagedGraphStep transition = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "update:root:1", "update:a:1", "condition:a-b", "exit:a:2" },
                fixture.Trace);
            Assert.AreEqual(
                fixture.StateA,
                fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "The source path remains authoritative until the staged Tick commits.");
            fixture.Accept(transition);
            Assert.AreEqual(fixture.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            fixture.Trace.Clear();
            fixture.Control.RequestAtoB = false;
            CoCoStagedGraphStep targetTick = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "enter:b:0", "update:root:2", "update:b:0" },
                fixture.Trace);
            fixture.Accept(targetTick);
        }

        [Test]
        public void CommittedTraceSeamReportsPathAndOnlyTheCurrentTickWinner()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            Assert.AreEqual(1, fixture.Runtime.CommittedTraceLayerCount);
            Assert.AreEqual(fixture.Layer, fixture.Runtime.GetCommittedTraceLayerId(0));
            var trace = new CoCoStateFlowTraceBuffer(8);
            var entries = new CoCoStateFlowTraceEntry[3];

            CoCoStagedGraphStep noTransition = fixture.Stage();
            fixture.Accept(noTransition);
            AssertCommittedTrace(
                fixture,
                default,
                fixture.Root,
                fixture.StateA);
            fixture.Runtime.AppendCommittedStateTrace(trace, noTransition.TickFrame);
            Assert.AreEqual(2, trace.CopyLatestTo(entries));
            AssertPath(entries[0], fixture, noTransition.TickFrame, fixture.Root);
            AssertPath(entries[1], fixture, noTransition.TickFrame, fixture.StateA);

            fixture.Control.RequestAtoB = true;
            CoCoStagedGraphStep transition = fixture.Stage();
            Assert.AreEqual(
                default(CoCoTransitionId),
                fixture.Runtime.GetCommittedTraceWinnerTransitionId(0),
                "A staged winner must remain hidden until its Tick commits.");
            fixture.Accept(transition);
            AssertCommittedTrace(
                fixture,
                fixture.AToB,
                fixture.Root,
                fixture.StateB);
            trace.Clear();
            fixture.Runtime.AppendCommittedStateTrace(trace, transition.TickFrame);
            Assert.AreEqual(3, trace.CopyLatestTo(entries));
            AssertPath(entries[0], fixture, transition.TickFrame, fixture.Root);
            AssertPath(entries[1], fixture, transition.TickFrame, fixture.StateB);
            Assert.AreEqual(CoCoStateFlowTraceKind.Transition, entries[2].Kind);
            Assert.AreEqual(fixture.Layer, entries[2].LayerId);
            Assert.AreEqual(fixture.AToB, entries[2].TransitionId);

            fixture.Control.RequestAtoB = false;
            CoCoStagedGraphStep following = fixture.Stage();
            fixture.Accept(following);
            AssertCommittedTrace(
                fixture,
                default,
                fixture.Root,
                fixture.StateB);
            trace.Clear();
            fixture.Runtime.AppendCommittedStateTrace(trace, following.TickFrame);
            Assert.AreEqual(2, trace.CopyLatestTo(entries));
            AssertPath(entries[0], fixture, following.TickFrame, fixture.Root);
            AssertPath(entries[1], fixture, following.TickFrame, fixture.StateB);
        }

        [Test]
        public void StagedTraceReportsAcceptedCandidateThenWinnerBeforeCommit()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic start), start.Message);
            fixture.Accept(fixture.Stage());
            fixture.Control.RequestAtoB = true;

            CoCoStagedGraphStep staged = fixture.Stage();
            var trace = new CoCoStateFlowTraceBuffer(4);
            fixture.Runtime.AppendStagedTransitionTrace(trace, staged, default);
            var entries = new CoCoStateFlowTraceEntry[4];
            Assert.AreEqual(2, trace.CopyLatestTo(entries));
            Assert.AreEqual(CoCoStateFlowTraceKind.Transition, entries[0].Kind);
            Assert.AreEqual(CoCoStateFlowTransitionRole.Candidate, entries[0].TransitionRole);
            Assert.AreEqual(fixture.AToB, entries[0].TransitionId);
            Assert.AreEqual(CoCoStateFlowTraceKind.Transition, entries[1].Kind);
            Assert.AreEqual(CoCoStateFlowTransitionRole.Winner, entries[1].TransitionRole);
            Assert.AreEqual(fixture.AToB, entries[1].TransitionId);
            Assert.AreEqual(
                fixture.StateA,
                fixture.Runtime.GetActivePath(0).ActiveLeaf,
                "Trace capture must not publish the staged Graph authority.");

            fixture.Accept(staged);
        }

        [Test]
        public void RejectedTickRollsBackPathMemoryClockAndOperationTransaction()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());

            fixture.Trace.Clear();
            fixture.Control.RequestAtoB = true;
            CoCoStagedGraphStep rejected = fixture.Stage();
            Assert.AreEqual(2UL, rejected.TickFrame.Tick.Value);
            Assert.IsTrue(fixture.Runtime.TryRejectStagedStep(
                rejected,
                CoCoDiagnostic.None,
                false,
                out CoCoDiagnostic rejectDiagnostic), rejectDiagnostic.Message);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.IsFalse(fixture.Runtime.IsFaulted);

            fixture.Trace.Clear();
            fixture.Control.RequestAtoB = false;
            CoCoStagedGraphStep retried = fixture.Stage();
            Assert.AreEqual(2UL, retried.TickFrame.Tick.Value);
            CollectionAssert.AreEqual(
                new[] { "update:root:1", "update:a:1" },
                fixture.Trace,
                "Candidate memory writes from the rejected Tick must not become authoritative.");
            fixture.Accept(retried);
        }

        [Test]
        public void PreparedCommitKeepsAuthorityHiddenUntilNoFailBarrier()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());

            fixture.Control.RequestAtoB = true;
            CoCoStagedGraphStep transition = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                transition,
                null,
                out CoCoPreparedGraphCommit prepared,
                out CoCoDiagnostic diagnostic), diagnostic.Message);

            Assert.IsTrue(prepared.IsValid);
            Assert.AreEqual(transition.TickFrame, prepared.TickFrame);
            Assert.IsTrue(transition.IsValid);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            prepared.CommitNoFail();

            Assert.IsFalse(prepared.IsValid);
            Assert.IsFalse(transition.IsValid);
            Assert.IsFalse(transition.OperationFrame.IsValid);
            Assert.IsFalse(fixture.Runtime.HasStagedStep);
            Assert.AreEqual(2UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            CoCoStagedGraphStep following = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                following,
                null,
                out CoCoPreparedGraphCommit followingProof,
                out CoCoDiagnostic followingDiagnostic), followingDiagnostic.Message);
            followingProof.CommitNoFail();

            Assert.IsFalse(followingProof.IsValid);
            Assert.IsFalse(following.IsValid);
            Assert.IsFalse(following.OperationFrame.IsValid);
            Assert.IsFalse(fixture.Runtime.HasStagedStep);
            Assert.AreEqual(3UL, fixture.Runtime.Clock.Tick.Value);
            Assert.Throws<InvalidOperationException>(
                () => fixture.Runtime.CommitPreparedStep(followingProof),
                "The checked compatibility wrapper must reject a consumed proof.");
        }

        [Test]
        public void PreparedCommitCanStillCancelWithoutAdvancingAuthority()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());

            fixture.Control.RequestAtoB = true;
            CoCoStagedGraphStep transition = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                transition,
                null,
                out CoCoPreparedGraphCommit prepared,
                out CoCoDiagnostic prepare), prepare.Message);
            Assert.IsTrue(fixture.Runtime.TryCancelStagedStep(
                transition,
                out CoCoDiagnostic cancel), cancel.Message);

            Assert.IsFalse(prepared.IsValid);
            Assert.IsFalse(transition.IsValid);
            Assert.IsFalse(fixture.Runtime.IsFaulted);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            fixture.Control.RequestAtoB = false;
            CoCoStagedGraphStep retry = fixture.Stage();
            Assert.AreEqual(2UL, retry.TickFrame.Tick.Value);
            fixture.Accept(retry);
        }

        [Test]
        public void PreparedCommitPreflightRejectsStaleStepWithoutDisturbingCurrentCandidate()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());

            CoCoStagedGraphStep stale = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryCancelStagedStep(
                stale,
                out CoCoDiagnostic cancel), cancel.Message);
            CoCoStagedGraphStep current = fixture.Stage();

            Assert.IsFalse(fixture.Runtime.TryPrepareStagedCommit(
                stale,
                null,
                out CoCoPreparedGraphCommit staleProof,
                out CoCoDiagnostic staleDiagnostic));
            Assert.IsFalse(staleProof.IsValid);
            Assert.IsTrue(staleDiagnostic.IsError);
            Assert.IsTrue(current.IsValid);
            Assert.IsFalse(fixture.Runtime.IsFaulted);

            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                current,
                null,
                out CoCoPreparedGraphCommit currentProof,
                out CoCoDiagnostic currentDiagnostic), currentDiagnostic.Message);
            currentProof.CommitNoFail();

            Assert.IsFalse(currentProof.IsValid);
            Assert.IsFalse(current.IsValid);
            Assert.IsFalse(fixture.Runtime.HasStagedStep);
            Assert.AreEqual(2UL, fixture.Runtime.Clock.Tick.Value);
        }

        [Test]
        public void RestorePrepareKeepsAuthorityHiddenUntilNoFailApply()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            CoCoContextFrameArena sourceArena = CreateRestoreSource(
                fixture.Runtime,
                out CoCoContextFrame source);
            var restoreSource = new CoCoContextRestoreReadView(source, source.Layout);
            CoCoTickFrame resumedTick = CreateResumedTick(fixture.Runtime.Clock, 10UL);
            var context = new RestoreContext(fixture.Graph, fixture.StateB);
            int callbackCount = fixture.Trace.Count;

            Assert.IsTrue(fixture.Runtime.TryValidateRestore(
                context,
                restoreSource,
                resumedTick,
                out CoCoDiagnostic validation), validation.Message);
            Assert.AreEqual(0, context.PrepareCount);
            Assert.AreEqual(callbackCount, fixture.Trace.Count);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            Assert.IsTrue(fixture.Runtime.TryPrepareRestore(
                context,
                restoreSource,
                resumedTick,
                out CoCoPreparedGraphRestore prepared,
                out CoCoDiagnostic prepare), prepare.Message);
            Assert.IsTrue(prepared.IsValid);
            Assert.IsTrue(fixture.Runtime.HasPreparedRestore);
            Assert.AreEqual(3, context.PrepareCount);
            Assert.AreEqual(callbackCount, fixture.Trace.Count);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.IsFalse(fixture.Runtime.TryPreviewNextTick(0.1d, 1d, out _, out _));

            prepared.ApplyNoFail();

            Assert.IsFalse(prepared.IsValid);
            Assert.IsFalse(fixture.Runtime.HasPreparedRestore);
            Assert.AreEqual(callbackCount, fixture.Trace.Count);
            Assert.AreEqual(resumedTick.TimelineEpoch, fixture.Runtime.Clock.TimelineEpoch);
            Assert.AreEqual(resumedTick.Tick, fixture.Runtime.Clock.Tick);
            Assert.AreEqual(resumedTick.ExecutionSequence, fixture.Runtime.Clock.ExecutionSequence);
            Assert.AreEqual(resumedTick.TimelinePosition.Seconds, fixture.Runtime.Clock.Seconds);
            Assert.AreEqual(fixture.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            fixture.Trace.Clear();
            fixture.Control.ActionProgress = 0.25d;
            CoCoStagedGraphStep following = fixture.Stage();
            Assert.IsTrue(fixture.Trace.Exists(entry => entry.StartsWith("enter:b:")));
            fixture.Accept(following);
            sourceArena.Dispose();
        }

        [Test]
        public void CancelledRestorePreparationLeavesCurrentGraphAndClockAuthoritative()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());
            CoCoContextFrameArena sourceArena = CreateRestoreSource(
                fixture.Runtime,
                out CoCoContextFrame source);
            var restoreSource = new CoCoContextRestoreReadView(source, source.Layout);
            CoCoTickFrame resumedTick = CreateResumedTick(fixture.Runtime.Clock, 20UL);
            var context = new RestoreContext(fixture.Graph, fixture.StateB);

            Assert.IsTrue(fixture.Runtime.TryPrepareRestore(
                context,
                restoreSource,
                resumedTick,
                out CoCoPreparedGraphRestore prepared,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            Assert.IsTrue(prepared.Cancel());
            Assert.IsFalse(prepared.IsValid);
            Assert.IsFalse(fixture.Runtime.HasPreparedRestore);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            fixture.Trace.Clear();
            CoCoStagedGraphStep normal = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "update:root:1", "update:a:1" },
                fixture.Trace,
                "Cancelled restore memory must remain outside committed authority.");
            fixture.Accept(normal);
            sourceArena.Dispose();
        }

        [Test]
        public void RestoreFingerprintMismatchCancelsClockCandidateWithoutChangingAuthority()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());
            CoCoContextFrameArena sourceArena = CreateRestoreSource(
                fixture.Runtime,
                out CoCoContextFrame source);
            var restoreSource = new CoCoContextRestoreReadView(source, source.Layout);
            CoCoTickFrame resumedTick = CreateResumedTick(fixture.Runtime.Clock, 30UL);
            var context = new RestoreContext(fixture.Graph, fixture.StateB)
            {
                CorruptFingerprint = true
            };

            Assert.IsFalse(fixture.Runtime.TryPrepareRestore(
                context,
                restoreSource,
                resumedTick,
                out CoCoPreparedGraphRestore prepared,
                out CoCoDiagnostic diagnostic));
            Assert.IsFalse(prepared.IsValid);
            Assert.AreEqual(CoCoDiagnosticDomain.Restore, diagnostic.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidGraphRestore, diagnostic.Code);
            Assert.IsFalse(fixture.Runtime.HasPreparedRestore);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(new CoCoTimelineEpoch(1UL), fixture.Runtime.Clock.TimelineEpoch);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.IsTrue(fixture.Runtime.TryPreviewNextTick(0.1d, 1d, out _, out _));
            sourceArena.Dispose();
        }

        [Test]
        public void SelfLoopExitsNowResetsLeafActivationAndEntersNextTick()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());
            fixture.Control.RequestAtoB = true;
            fixture.Accept(fixture.Stage());
            fixture.Control.RequestAtoB = false;
            fixture.Accept(fixture.Stage());

            fixture.Trace.Clear();
            fixture.Control.RequestBSelf = true;
            CoCoStagedGraphStep selfLoop = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "update:root:3", "update:b:1", "exit:b:2" },
                fixture.Trace);
            fixture.Accept(selfLoop);

            fixture.Trace.Clear();
            fixture.Control.RequestBSelf = false;
            CoCoStagedGraphStep next = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "enter:b:0", "update:root:4", "update:b:0" },
                fixture.Trace);
            fixture.Accept(next);
        }

        [Test]
        public void ActionProgressDecreaseFaultsAndPreservesLastCommittedTick()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Control.ActionProgress = 0.6d;
            fixture.Accept(fixture.Stage());
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);

            fixture.Control.ActionProgress = 0.5d;
            Assert.IsTrue(fixture.Runtime.TryPreviewNextTick(0.1d, 1d, out CoCoTickFrame tick, out _));
            Assert.IsFalse(fixture.Runtime.TryStageStep(
                tick,
                null,
                default,
                out _,
                out CoCoDiagnostic diagnostic));
            Assert.IsTrue(fixture.Runtime.IsFaulted);
            Assert.IsTrue(diagnostic.IsError);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.IsFalse(fixture.Runtime.TryResume(out _));
            Assert.IsTrue(fixture.Runtime.TryStop(out _));
        }

        [Test]
        public void TwoRuntimesSharingCompiledGraphKeepClockMemoryPathAndFaultIsolated()
        {
            Fixture first = Fixture.Create();
            Fixture second = Fixture.Create(first.Graph);
            Assert.IsTrue(first.Runtime.TryStart(out _));
            Assert.IsTrue(second.Runtime.TryStart(out _));

            first.Accept(first.Stage());
            Assert.AreEqual(1UL, first.Runtime.Clock.Tick.Value);
            Assert.AreEqual(0UL, second.Runtime.Clock.Tick.Value);

            first.Control.ThrowOnUpdate = true;
            Assert.Throws<AssertionException>(() => first.Stage());
            Assert.IsTrue(first.Runtime.IsFaulted);
            Assert.IsFalse(second.Runtime.IsFaulted);

            second.Accept(second.Stage());
            Assert.AreEqual(1UL, second.Runtime.Clock.Tick.Value);
            Assert.AreEqual(second.StateA, second.Runtime.GetActivePath(0).ActiveLeaf);
        }

        [Test]
        public void SuspendPreservesStateAndDoesNotProduceTicks()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());
            Assert.IsTrue(fixture.Runtime.TrySuspend(out _));
            Assert.IsFalse(fixture.Runtime.TryPreviewNextTick(0.1d, 1d, out _, out _));
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            Assert.IsTrue(fixture.Runtime.TryResume(out _));
            fixture.Accept(fixture.Stage());
            Assert.AreEqual(2UL, fixture.Runtime.Clock.Tick.Value);
        }

        [Test]
        public void NormalAndTransitionStepsHaveZeroSteadyStateManagedAllocations()
        {
            Fixture fixture = Fixture.Create();
            fixture.Control.RecordCallbacks = false;
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            for (int index = 0; index < 100; index++)
            {
                Assert.IsTrue(fixture.TryStageAndAccept());
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool succeeded = true;
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= fixture.TryStageAndAccept();
            }

            long normalAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, normalAllocated);

            fixture.Control.RequestAtoB = true;
            Assert.IsTrue(fixture.TryStageAndAccept());
            fixture.Control.RequestAtoB = false;
            fixture.Control.RequestBSelf = true;
            for (int index = 0; index < 100; index++)
            {
                Assert.IsTrue(fixture.TryStageAndAccept());
            }

            before = GC.GetAllocatedBytesForCurrentThread();
            succeeded = true;
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= fixture.TryStageAndAccept();
            }

            long transitionAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, transitionAllocated);
        }

        [Test]
        public void SuspendResumeHasZeroSteadyStateManagedAllocations()
        {
            Fixture fixture = Fixture.Create();
            fixture.Control.RecordCallbacks = false;
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            for (int index = 0; index < 100; index++)
            {
                Assert.IsTrue(fixture.Runtime.TrySuspend(out _));
                Assert.IsTrue(fixture.Runtime.TryResume(out _));
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool succeeded = true;
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= fixture.Runtime.TrySuspend(out _);
                succeeded &= fixture.Runtime.TryResume(out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void MinimumPriorityIsStillAValidTransitionWinner()
        {
            Fixture fixture = Fixture.Create(aToBPriority: int.MinValue);
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());

            fixture.Control.RequestAtoB = true;
            CoCoStagedGraphStep transition = fixture.Stage();
            CollectionAssert.Contains(fixture.Trace, "exit:a:2");
            fixture.Accept(transition);
            Assert.AreEqual(fixture.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);
        }

        private static void AssertCommittedTrace(
            Fixture fixture,
            CoCoTransitionId expectedWinner,
            params CoCoStateId[] expectedPath)
        {
            Assert.AreEqual(
                expectedWinner,
                fixture.Runtime.GetCommittedTraceWinnerTransitionId(0));
            Assert.AreEqual(
                expectedPath.Length,
                fixture.Runtime.GetCommittedTracePathCount(0));
            for (int pathIndex = 0; pathIndex < expectedPath.Length; pathIndex++)
            {
                Assert.AreEqual(
                    expectedPath[pathIndex],
                    fixture.Runtime.GetCommittedTracePathStateId(0, pathIndex));
            }
        }

        private static void AssertPath(
            CoCoStateFlowTraceEntry entry,
            Fixture fixture,
            CoCoTickFrame tickFrame,
            CoCoStateId stateId)
        {
            Assert.AreEqual(CoCoStateFlowTraceKind.ActivePath, entry.Kind);
            Assert.AreEqual(fixture.Runtime.GraphInstanceId, entry.GraphInstanceId);
            Assert.AreEqual(tickFrame, entry.TickFrame);
            Assert.AreEqual(fixture.Layer, entry.LayerId);
            Assert.AreEqual(stateId, entry.StateId);
        }

        private static CoCoContextFrameArena CreateRestoreSource(
            CoCoStateGraphRuntime runtime,
            out CoCoContextFrame source)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(
                0xD16UL,
                1UL,
                out CoCoFrameLayoutId layoutId));
            var layoutBuilder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(layoutBuilder.TryFreeze(
                layoutId,
                1U,
                out CoCoContextFrameLayout layout,
                out CoCoDiagnosticCode code), code.ToString());
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(
                runtime.Clock.Seconds,
                out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                0.1d,
                runtime.Clock.TimelineId,
                position,
                runtime.Clock.Tick,
                runtime.Clock.ClockDomainId,
                runtime.Clock.ExecutionSequence,
                runtime.Clock.TimelineEpoch,
                out CoCoTickFrame tickFrame,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            var arena = new CoCoContextFrameArena(runtime.GraphInstanceId, layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                tickFrame,
                out CoCoPreparedContextCommit prepared,
                out CoCoContextCommitStatus status), status.ToString());
            Assert.IsTrue(prepared.TryFinalize(
                out CoCoFinalizedContextCommit finalized,
                out status), status.ToString());
            CoCoContextCommitResult result = finalized.Commit();
            Assert.IsTrue(result.Succeeded, result.Status.ToString());
            source = result.Frame;
            return arena;
        }

        private static CoCoTickFrame CreateResumedTick(
            CoCoActorClock clock,
            ulong offset)
        {
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(
                clock.Seconds + offset,
                out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                0.1d,
                clock.TimelineId,
                position,
                new CoCoTimelineTick(clock.Tick.Value + offset),
                clock.ClockDomainId,
                new CoCoExecutionSequence(clock.ExecutionSequence.Value + offset),
                new CoCoTimelineEpoch(clock.TimelineEpoch.Value + 1UL),
                out CoCoTickFrame tickFrame,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return tickFrame;
        }

        private sealed class RestoreContext : ICoCoStateGraphContextRuntime
        {
            private readonly CoCoCompiledStateGraph _graph;
            private readonly CoCoStateId _activeLeaf;

            public RestoreContext(
                CoCoCompiledStateGraph graph,
                CoCoStateId activeLeaf)
            {
                _graph = graph;
                _activeLeaf = activeLeaf;
            }

            public bool CorruptFingerprint { get; set; }
            public int PrepareCount { get; private set; }

            public bool TryBeginGraphCapture(
                in CoCoStagedGraphStep stagedStep,
                CoCoContextFrameReadView previous,
                in CoCoPreparedContextCommit prepared,
                ulong token,
                out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public bool TryCaptureState(
                int orderedStateIndex,
                CoCoActivationMemory memory,
                bool isOnActivePath,
                CoCoActivationId activationId,
                double localSeconds,
                double actionProgress,
                bool enterPending,
                ulong memoryFingerprint,
                out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public bool TryValidateInitialStateDefault(
                int orderedStateIndex,
                CoCoActivationMemory memory,
                bool isOnActivePath,
                CoCoActivationId activationId,
                double localSeconds,
                double actionProgress,
                bool enterPending,
                ulong memoryFingerprint,
                CoCoContextFrameReadView defaults,
                out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public bool TryCompleteGraphCapture(out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public void CancelCapture()
            {
            }

            public bool TryValidateRestore(
                CoCoContextRestoreReadView source,
                out ulong nextActivationValue,
                out CoCoDiagnostic diagnostic)
            {
                nextActivationValue = 200UL;
                if (!source.IsValid)
                {
                    diagnostic = RestoreError("Source frame is unavailable.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryPrepareStateRestore(
                int orderedStateIndex,
                CoCoContextRestoreReadView source,
                CoCoActivationMemory candidateMemory,
                out CoCoStateGraphRestoreState state,
                out CoCoDiagnostic diagnostic)
            {
                state = default;
                if (!source.IsValid || !(candidateMemory is RuntimeFixtureMemory memory) ||
                    !TryResolveState(
                        orderedStateIndex,
                        out CoCoCompiledStateLayer layer,
                        out int stateIndex))
                {
                    diagnostic = RestoreError("Restore fixture received an invalid State mapping.");
                    return false;
                }

                int activeLeafIndex = -1;
                for (int index = 0; index < layer.States.Count; index++)
                {
                    if (layer.States[index].StateId == _activeLeaf)
                    {
                        activeLeafIndex = index;
                        break;
                    }
                }

                if (activeLeafIndex < 0)
                {
                    diagnostic = RestoreError("Restore fixture active leaf is missing.");
                    return false;
                }

                IReadOnlyList<int> path =
                    layer.States[activeLeafIndex].RootPathStateIndices;
                bool isActive = false;
                for (int depth = 0; depth < path.Count; depth++)
                {
                    if (path[depth] == stateIndex)
                    {
                        isActive = true;
                        break;
                    }
                }

                int value = 10 + orderedStateIndex;
                memory.Value = value;
                ulong fingerprint = unchecked((ulong)(uint)value);
                if (CorruptFingerprint)
                {
                    fingerprint++;
                }

                Assert.IsTrue(CoCoActivationId.TryCreate(
                    (ulong)(100 + orderedStateIndex),
                    out CoCoActivationId activationId));
                state = new CoCoStateGraphRestoreState(
                    layer.LayerId,
                    layer.States[stateIndex].StateId,
                    isActive,
                    activationId,
                    orderedStateIndex + 0.5d,
                    0.25d,
                    isActive && stateIndex == activeLeafIndex,
                    fingerprint,
                    true);
                PrepareCount++;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            private bool TryResolveState(
                int orderedStateIndex,
                out CoCoCompiledStateLayer layer,
                out int stateIndex)
            {
                int offset = 0;
                for (int layerIndex = 0; layerIndex < _graph.Layers.Count; layerIndex++)
                {
                    CoCoCompiledStateLayer candidate = _graph.Layers[layerIndex];
                    if (orderedStateIndex < offset + candidate.States.Count)
                    {
                        layer = candidate;
                        stateIndex = orderedStateIndex - offset;
                        return true;
                    }

                    offset += candidate.States.Count;
                }

                layer = null;
                stateIndex = -1;
                return false;
            }

            private static bool Unsupported(out CoCoDiagnostic diagnostic)
            {
                diagnostic = RestoreError("Capture is outside this restore fixture.");
                return false;
            }

            private static CoCoDiagnostic RestoreError(string message) =>
                CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    message);
        }

        private sealed class Fixture
        {
            private static ulong _nextInstanceId = 100UL;

            private Fixture(
                CoCoCompiledStateGraph graph,
                CoCoStateGraphRuntime runtime,
                RuntimeControl control,
                List<string> trace,
                CoCoLayerId layer,
                CoCoStateId root,
                CoCoStateId stateA,
                CoCoStateId stateB,
                CoCoTransitionId aToB)
            {
                Graph = graph;
                Runtime = runtime;
                Control = control;
                Trace = trace;
                Layer = layer;
                Root = root;
                StateA = stateA;
                StateB = stateB;
                AToB = aToB;
            }

            public CoCoCompiledStateGraph Graph { get; }
            public CoCoStateGraphRuntime Runtime { get; }
            public RuntimeControl Control { get; }
            public List<string> Trace { get; }
            public CoCoLayerId Layer { get; }
            public CoCoStateId Root { get; }
            public CoCoStateId StateA { get; }
            public CoCoStateId StateB { get; }
            public CoCoTransitionId AToB { get; }

            public static Fixture Create(
                CoCoCompiledStateGraph sharedGraph = null,
                int aToBPriority = 20)
            {
                RuntimeIds ids = RuntimeIds.Create();
                CoCoCompiledStateGraph graph = sharedGraph ?? CreateGraph(ids, aToBPriority);
                CoCoGraphInstanceId.TryCreate(_nextInstanceId++, out CoCoGraphInstanceId graphInstanceId);
                var trace = new List<string>();
                var control = new RuntimeControl(trace);
                var bindingBuilder = new CoCoStateGraphLogicBindingsBuilder(graph);
                Assert.IsTrue(bindingBuilder.TryBindState(
                    ids.StateDescriptor,
                    new CoCoStateRuntimeFactory<RuntimeFixtureStateLogic, RuntimeFixtureMemory>(
                        context => new RuntimeFixtureStateLogic(
                            context,
                            control,
                            ids.Root,
                            ids.StateA,
                            ids.StateB),
                        () => new RuntimeFixtureMemory(),
                        (source, destination) => destination.Value = source.Value,
                        memory => memory.Value = 0,
                        memory => unchecked((ulong)(uint)memory.Value)),
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                Assert.IsTrue(bindingBuilder.TryBindCondition(
                    ids.ConditionDescriptor,
                    new CoCoConditionRuntimeFactory<RuntimeFixtureCondition>(
                        context => new RuntimeFixtureCondition(context, control)),
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(bindingBuilder.TryFreeze(
                    out CoCoStateGraphLogicBindings bindings,
                    out diagnostic), diagnostic.Message);

                var registryBuilder = new CoCoOperationSectionRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out CoCoOperationSectionRegistry registry,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(CoCoOperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    Array.Empty<CoCoOperationSectionRequirement>(),
                    out CoCoOperationFrame operationFrame,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(CoCoActorClock.TryCreate(
                    ids.Timeline,
                    ids.ClockDomain,
                    new CoCoTimelineEpoch(1UL),
                    out CoCoActorClock clock,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(CoCoStateGraphRuntime.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out CoCoStateGraphRuntime runtime,
                    out diagnostic), diagnostic.Message);
                return new Fixture(
                    graph,
                    runtime,
                    control,
                    trace,
                    ids.Layer,
                    ids.Root,
                    ids.StateA,
                    ids.StateB,
                    ids.AToB);
            }

            public CoCoStagedGraphStep Stage()
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    0.1d,
                    1d,
                    out CoCoTickFrame tick,
                    out CoCoDiagnostic previewDiagnostic), previewDiagnostic.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out CoCoStagedGraphStep staged,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                return staged;
            }

            public void Accept(CoCoStagedGraphStep staged)
            {
                Assert.IsTrue(Runtime.TryAcceptStagedStep(staged, out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            public bool TryStageAndAccept()
            {
                return Runtime.TryPreviewNextTick(0.1d, 1d, out CoCoTickFrame tick, out _) &&
                       Runtime.TryStageStep(tick, null, default, out CoCoStagedGraphStep staged, out _) &&
                       Runtime.TryAcceptStagedStep(staged, out _);
            }

            private static CoCoCompiledStateGraph CreateGraph(
                RuntimeIds ids,
                int aToBPriority)
            {
                var catalogBuilder = new CoCoGraphDescriptorCatalogBuilder();
                Assert.IsTrue(catalogBuilder.TryRegisterState(
                    ids.StateDescriptor,
                    1U,
                    new RuntimeFixtureStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        RuntimeFixtureStateLogic,
                        RuntimeFixtureStateConfigSchema,
                        RuntimeFixtureMemory>(RuntimeFixtureSchemas.State, true),
                    null,
                    null,
                    null,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                Assert.IsTrue(catalogBuilder.TryRegisterCondition(
                    ids.ConditionDescriptor,
                    1U,
                    new RuntimeFixtureConditionConfigFreezer(),
                    new CoCoConditionRuntimeRegistration<
                        RuntimeFixtureCondition,
                        RuntimeFixtureConditionConfigSchema>(RuntimeFixtureSchemas.Condition),
                    null,
                    null,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(catalogBuilder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out diagnostic), diagnostic.Message);

                CoCoStateSource root = State(ids.Root, default, ids.StateA, ids.StateDescriptor, 1);
                CoCoStateSource stateA = State(ids.StateA, ids.Root, default, ids.StateDescriptor, 2);
                CoCoStateSource stateB = State(ids.StateB, ids.Root, default, ids.StateDescriptor, 3);
                var aToB = new CoCoTransitionSource(
                    ids.AToB,
                    ids.StateA,
                    ids.StateB,
                    aToBPriority,
                    CoCoTransitionWindow.Always,
                    new[]
                    {
                        new CoCoConditionSource(ids.ConditionDescriptor, ConditionConfig(1))
                    });
                var bSelf = new CoCoTransitionSource(
                    ids.BSelf,
                    ids.StateB,
                    ids.StateB,
                    10,
                    CoCoTransitionWindow.Always,
                    Array.Empty<CoCoConditionSource>());
                var source = new CoCoStateGraphSource(
                    CoCoStateGraphCompiler.CurrentSchemaVersion,
                    0x404UL,
                    ids.Graph,
                    new[]
                    {
                        new CoCoStateLayerSource(
                            ids.Layer,
                            ids.Root,
                            new[] { stateB, root, stateA },
                            new[] { bSelf, aToB })
                    },
                    Array.Empty<CoCoEventToIntentDeclarationSource>());
                CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);
                Assert.IsTrue(result.Succeeded, JoinDiagnostics(result.Diagnostics));
                return result.Graph;
            }

            private static CoCoStateSource State(
                CoCoStateId stateId,
                CoCoStateId parentId,
                CoCoStateId initialChild,
                CoCoStateDescriptorId descriptor,
                int value) =>
                new CoCoStateSource(
                    stateId,
                    parentId,
                    initialChild,
                    descriptor,
                    StateConfig(value));

            private static CoCoFrozenConfigSnapshot StateConfig(int value)
            {
                CoCoFrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer =
                    RuntimeFixtureSchemas.State.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.StateValue, value, out _));
                Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }

            private static CoCoFrozenConfigSnapshot ConditionConfig(int value)
            {
                CoCoFrozenConfigWriter<RuntimeFixtureConditionConfigSchema> writer =
                    RuntimeFixtureSchemas.Condition.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.ConditionValue, value, out _));
                Assert.IsTrue(writer.TrySeal(out CoCoFrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }

            private static string JoinDiagnostics(IReadOnlyList<CoCoGraphDiagnostic> diagnostics)
            {
                string result = string.Empty;
                for (int index = 0; index < diagnostics.Count; index++)
                {
                    result += diagnostics[index].Diagnostic.Message + "\n";
                }

                return result;
            }
        }

        private sealed class RuntimeControl : IRuntimeStateGraphFixtureObserver
        {
            private readonly List<string> _trace;

            public RuntimeControl(List<string> trace)
            {
                _trace = trace;
            }

            public bool RequestAtoB;
            public bool RequestBSelf;
            public bool ThrowOnUpdate;
            public bool RecordCallbacks = true;
            public double ActionProgress = 0d;

            bool IRuntimeStateGraphFixtureObserver.RequestAtoB => RequestAtoB;
            bool IRuntimeStateGraphFixtureObserver.RequestBSelf => RequestBSelf;
            bool IRuntimeStateGraphFixtureObserver.ThrowOnUpdate => ThrowOnUpdate;
            bool IRuntimeStateGraphFixtureObserver.RecordCallbacks => RecordCallbacks;
            double IRuntimeStateGraphFixtureObserver.ActionProgress => ActionProgress;

            public void Record(string value)
            {
                _trace.Add(value);
            }
        }

        private readonly struct RuntimeIds
        {
            private RuntimeIds(
                CoCoGraphId graph,
                CoCoLayerId layer,
                CoCoStateId root,
                CoCoStateId stateA,
                CoCoStateId stateB,
                CoCoTransitionId aToB,
                CoCoTransitionId bSelf,
                CoCoStateDescriptorId stateDescriptor,
                CoCoConditionDescriptorId conditionDescriptor,
                CoCoFrameLayoutId operationLayout,
                CoCoTimelineId timeline,
                CoCoClockDomainId clockDomain)
            {
                Graph = graph;
                Layer = layer;
                Root = root;
                StateA = stateA;
                StateB = stateB;
                AToB = aToB;
                BSelf = bSelf;
                StateDescriptor = stateDescriptor;
                ConditionDescriptor = conditionDescriptor;
                OperationLayout = operationLayout;
                Timeline = timeline;
                ClockDomain = clockDomain;
            }

            public CoCoGraphId Graph { get; }
            public CoCoLayerId Layer { get; }
            public CoCoStateId Root { get; }
            public CoCoStateId StateA { get; }
            public CoCoStateId StateB { get; }
            public CoCoTransitionId AToB { get; }
            public CoCoTransitionId BSelf { get; }
            public CoCoStateDescriptorId StateDescriptor { get; }
            public CoCoConditionDescriptorId ConditionDescriptor { get; }
            public CoCoFrameLayoutId OperationLayout { get; }
            public CoCoTimelineId Timeline { get; }
            public CoCoClockDomainId ClockDomain { get; }

            public static RuntimeIds Create()
            {
                CoCoGraphId.TryCreate(0x40UL, 1UL, out CoCoGraphId graph);
                CoCoLayerId.TryCreate(0x40UL, 2UL, out CoCoLayerId layer);
                CoCoStateId.TryCreate(0x40UL, 3UL, out CoCoStateId root);
                CoCoStateId.TryCreate(0x40UL, 4UL, out CoCoStateId stateA);
                CoCoStateId.TryCreate(0x40UL, 5UL, out CoCoStateId stateB);
                CoCoTransitionId.TryCreate(0x40UL, 6UL, out CoCoTransitionId aToB);
                CoCoTransitionId.TryCreate(0x40UL, 7UL, out CoCoTransitionId bSelf);
                CoCoStateDescriptorId.TryCreate(
                    0x40UL,
                    8UL,
                    out CoCoStateDescriptorId stateDescriptor);
                CoCoConditionDescriptorId.TryCreate(
                    0x40UL,
                    9UL,
                    out CoCoConditionDescriptorId conditionDescriptor);
                CoCoFrameLayoutId.TryCreate(0x40UL, 10UL, out CoCoFrameLayoutId operationLayout);
                CoCoTimelineId.TryCreate(0x40UL, 11UL, out CoCoTimelineId timeline);
                CoCoClockDomainId.TryCreate(12UL, out CoCoClockDomainId clockDomain);
                return new RuntimeIds(
                    graph,
                    layer,
                    root,
                    stateA,
                    stateB,
                    aToB,
                    bSelf,
                    stateDescriptor,
                    conditionDescriptor,
                    operationLayout,
                    timeline,
                    clockDomain);
            }
        }
    }
}
