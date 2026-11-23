using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphRuntimeTests
    {
        [Test]
        public void TransitionTickKeepsSourcePathAndTargetEntersOnFollowingTick()
        {
            Fixture fixture = Fixture.Create();

            Assert.IsTrue(fixture.Runtime.TryStart(out CoCoDiagnostic diagnostic), diagnostic.Message);
            CollectionAssert.IsEmpty(fixture.Trace);

            StagedGraphStep first = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "enter:root:0", "enter:a:0", "update:root:0", "update:a:0" },
                fixture.Trace);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);
            fixture.Accept(first);

            fixture.Trace.Clear();
            fixture.Control.RequestAtoB = true;
            StagedGraphStep transition = fixture.Stage();
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
            StagedGraphStep targetTick = fixture.Stage();
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
            var trace = new StateFlowTraceBuffer(8);
            var entries = new StateFlowTraceEntry[3];

            StagedGraphStep noTransition = fixture.Stage();
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
            StagedGraphStep transition = fixture.Stage();
            Assert.AreEqual(
                default(TransitionId),
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
            Assert.AreEqual(StateFlowTraceKind.Transition, entries[2].Kind);
            Assert.AreEqual(fixture.Layer, entries[2].LayerId);
            Assert.AreEqual(fixture.AToB, entries[2].TransitionId);

            fixture.Control.RequestAtoB = false;
            StagedGraphStep following = fixture.Stage();
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

            StagedGraphStep staged = fixture.Stage();
            var trace = new StateFlowTraceBuffer(4);
            fixture.Runtime.AppendStagedTransitionTrace(trace, staged, default);
            var entries = new StateFlowTraceEntry[4];
            Assert.AreEqual(2, trace.CopyLatestTo(entries));
            Assert.AreEqual(StateFlowTraceKind.Transition, entries[0].Kind);
            Assert.AreEqual(StateFlowTransitionRole.Candidate, entries[0].TransitionRole);
            Assert.AreEqual(fixture.AToB, entries[0].TransitionId);
            Assert.AreEqual(StateFlowTraceKind.Transition, entries[1].Kind);
            Assert.AreEqual(StateFlowTransitionRole.Winner, entries[1].TransitionRole);
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
            StagedGraphStep rejected = fixture.Stage();
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
            StagedGraphStep retried = fixture.Stage();
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
            StagedGraphStep transition = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                transition,
                null,
                out PreparedGraphCommit prepared,
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

            StagedGraphStep following = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                following,
                null,
                out PreparedGraphCommit followingProof,
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
            StagedGraphStep transition = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                transition,
                null,
                out PreparedGraphCommit prepared,
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
            StagedGraphStep retry = fixture.Stage();
            Assert.AreEqual(2UL, retry.TickFrame.Tick.Value);
            fixture.Accept(retry);
        }

        [Test]
        public void PreparedCommitPreflightRejectsStaleStepWithoutDisturbingCurrentCandidate()
        {
            Fixture fixture = Fixture.Create();
            Assert.IsTrue(fixture.Runtime.TryStart(out _));
            fixture.Accept(fixture.Stage());

            StagedGraphStep stale = fixture.Stage();
            Assert.IsTrue(fixture.Runtime.TryCancelStagedStep(
                stale,
                out CoCoDiagnostic cancel), cancel.Message);
            StagedGraphStep current = fixture.Stage();

            Assert.IsFalse(fixture.Runtime.TryPrepareStagedCommit(
                stale,
                null,
                out PreparedGraphCommit staleProof,
                out CoCoDiagnostic staleDiagnostic));
            Assert.IsFalse(staleProof.IsValid);
            Assert.IsTrue(staleDiagnostic.IsError);
            Assert.IsTrue(current.IsValid);
            Assert.IsFalse(fixture.Runtime.IsFaulted);

            Assert.IsTrue(fixture.Runtime.TryPrepareStagedCommit(
                current,
                null,
                out PreparedGraphCommit currentProof,
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

            ContextFrameArena sourceArena = CreateRestoreSource(
                fixture.Runtime,
                out ContextFrame source);
            var restoreSource = new ContextRestoreReadView(source, source.Layout);
            TickFrame resumedTick = CreateResumedTick(fixture.Runtime.Clock, 10UL);
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
                out PreparedGraphRestore prepared,
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
            StagedGraphStep following = fixture.Stage();
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
            ContextFrameArena sourceArena = CreateRestoreSource(
                fixture.Runtime,
                out ContextFrame source);
            var restoreSource = new ContextRestoreReadView(source, source.Layout);
            TickFrame resumedTick = CreateResumedTick(fixture.Runtime.Clock, 20UL);
            var context = new RestoreContext(fixture.Graph, fixture.StateB);

            Assert.IsTrue(fixture.Runtime.TryPrepareRestore(
                context,
                restoreSource,
                resumedTick,
                out PreparedGraphRestore prepared,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            Assert.IsTrue(prepared.Cancel());
            Assert.IsFalse(prepared.IsValid);
            Assert.IsFalse(fixture.Runtime.HasPreparedRestore);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(fixture.StateA, fixture.Runtime.GetActivePath(0).ActiveLeaf);

            fixture.Trace.Clear();
            StagedGraphStep normal = fixture.Stage();
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
            ContextFrameArena sourceArena = CreateRestoreSource(
                fixture.Runtime,
                out ContextFrame source);
            var restoreSource = new ContextRestoreReadView(source, source.Layout);
            TickFrame resumedTick = CreateResumedTick(fixture.Runtime.Clock, 30UL);
            var context = new RestoreContext(fixture.Graph, fixture.StateB)
            {
                CorruptFingerprint = true
            };

            Assert.IsFalse(fixture.Runtime.TryPrepareRestore(
                context,
                restoreSource,
                resumedTick,
                out PreparedGraphRestore prepared,
                out CoCoDiagnostic diagnostic));
            Assert.IsFalse(prepared.IsValid);
            Assert.AreEqual(CoCoDiagnosticDomain.Restore, diagnostic.Domain);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidGraphRestore, diagnostic.Code);
            Assert.IsFalse(fixture.Runtime.HasPreparedRestore);
            Assert.AreEqual(1UL, fixture.Runtime.Clock.Tick.Value);
            Assert.AreEqual(new TimelineEpoch(1UL), fixture.Runtime.Clock.TimelineEpoch);
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
            StagedGraphStep selfLoop = fixture.Stage();
            CollectionAssert.AreEqual(
                new[] { "update:root:3", "update:b:1", "exit:b:2" },
                fixture.Trace);
            fixture.Accept(selfLoop);

            fixture.Trace.Clear();
            fixture.Control.RequestBSelf = false;
            StagedGraphStep next = fixture.Stage();
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
            Assert.IsTrue(fixture.Runtime.TryPreviewNextTick(0.1d, 1d, out TickFrame tick, out _));
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
            StagedGraphStep transition = fixture.Stage();
            CollectionAssert.Contains(fixture.Trace, "exit:a:2");
            fixture.Accept(transition);
            Assert.AreEqual(fixture.StateB, fixture.Runtime.GetActivePath(0).ActiveLeaf);
        }

        private static void AssertCommittedTrace(
            Fixture fixture,
            TransitionId expectedWinner,
            params StateId[] expectedPath)
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
            StateFlowTraceEntry entry,
            Fixture fixture,
            TickFrame tickFrame,
            StateId stateId)
        {
            Assert.AreEqual(StateFlowTraceKind.ActivePath, entry.Kind);
            Assert.AreEqual(fixture.Runtime.GraphInstanceId, entry.GraphInstanceId);
            Assert.AreEqual(tickFrame, entry.TickFrame);
            Assert.AreEqual(fixture.Layer, entry.LayerId);
            Assert.AreEqual(stateId, entry.StateId);
        }

        private static ContextFrameArena CreateRestoreSource(
            StateGraphRuntime runtime,
            out ContextFrame source)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(
                0xD16UL,
                1UL,
                out FrameLayoutId layoutId));
            var layoutBuilder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(layoutBuilder.TryFreeze(
                layoutId,
                1U,
                out ContextFrameLayout layout,
                out CoCoDiagnosticCode code), code.ToString());
            Assert.IsTrue(TimelinePosition.TryCreate(
                runtime.Clock.Seconds,
                out TimelinePosition position));
            Assert.IsTrue(TickFrame.TryCreate(
                0.1d,
                runtime.Clock.TimelineId,
                position,
                runtime.Clock.Tick,
                runtime.Clock.ClockDomainId,
                runtime.Clock.ExecutionSequence,
                runtime.Clock.TimelineEpoch,
                out TickFrame tickFrame,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            var arena = new ContextFrameArena(runtime.GraphInstanceId, layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                tickFrame,
                out PreparedContextCommit prepared,
                out ContextCommitStatus status), status.ToString());
            Assert.IsTrue(prepared.TryFinalize(
                out FinalizedContextCommit finalized,
                out status), status.ToString());
            ContextCommitResult result = finalized.Commit();
            Assert.IsTrue(result.Succeeded, result.Status.ToString());
            source = result.Frame;
            return arena;
        }

        private static TickFrame CreateResumedTick(
            ActorClock clock,
            ulong offset)
        {
            Assert.IsTrue(TimelinePosition.TryCreate(
                clock.Seconds + offset,
                out TimelinePosition position));
            Assert.IsTrue(TickFrame.TryCreate(
                0.1d,
                clock.TimelineId,
                position,
                new TimelineTick(clock.Tick.Value + offset),
                clock.ClockDomainId,
                new ExecutionSequence(clock.ExecutionSequence.Value + offset),
                new TimelineEpoch(clock.TimelineEpoch.Value + 1UL),
                out TickFrame tickFrame,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return tickFrame;
        }

        private sealed class RestoreContext : IStateGraphContextRuntime
        {
            private readonly CompiledStateGraph _graph;
            private readonly StateId _activeLeaf;

            public RestoreContext(
                CompiledStateGraph graph,
                StateId activeLeaf)
            {
                _graph = graph;
                _activeLeaf = activeLeaf;
            }

            public bool CorruptFingerprint { get; set; }
            public int PrepareCount { get; private set; }

            public bool TryBeginGraphCapture(
                in StagedGraphStep stagedStep,
                ContextFrameReadView previous,
                in PreparedContextCommit prepared,
                ulong token,
                out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public bool TryCaptureState(
                int orderedStateIndex,
                ActivationMemory memory,
                bool isOnActivePath,
                ActivationId activationId,
                double localSeconds,
                double actionProgress,
                bool enterPending,
                ulong memoryFingerprint,
                out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public bool TryValidateInitialStateDefault(
                int orderedStateIndex,
                ActivationMemory memory,
                bool isOnActivePath,
                ActivationId activationId,
                double localSeconds,
                double actionProgress,
                bool enterPending,
                ulong memoryFingerprint,
                ContextFrameReadView defaults,
                out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public bool TryCompleteGraphCapture(out CoCoDiagnostic diagnostic) =>
                Unsupported(out diagnostic);

            public void CancelCapture()
            {
            }

            public bool TryValidateRestore(
                ContextRestoreReadView source,
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
                ContextRestoreReadView source,
                ActivationMemory candidateMemory,
                out StateGraphRestoreState state,
                out CoCoDiagnostic diagnostic)
            {
                state = default;
                if (!source.IsValid || !(candidateMemory is RuntimeFixtureMemory memory) ||
                    !TryResolveState(
                        orderedStateIndex,
                        out CompiledStateLayer layer,
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

                Assert.IsTrue(ActivationId.TryCreate(
                    (ulong)(100 + orderedStateIndex),
                    out ActivationId activationId));
                state = new StateGraphRestoreState(
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
                out CompiledStateLayer layer,
                out int stateIndex)
            {
                int offset = 0;
                for (int layerIndex = 0; layerIndex < _graph.Layers.Count; layerIndex++)
                {
                    CompiledStateLayer candidate = _graph.Layers[layerIndex];
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
                CompiledStateGraph graph,
                StateGraphRuntime runtime,
                RuntimeControl control,
                List<string> trace,
                LayerId layer,
                StateId root,
                StateId stateA,
                StateId stateB,
                TransitionId aToB)
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

            public CompiledStateGraph Graph { get; }
            public StateGraphRuntime Runtime { get; }
            public RuntimeControl Control { get; }
            public List<string> Trace { get; }
            public LayerId Layer { get; }
            public StateId Root { get; }
            public StateId StateA { get; }
            public StateId StateB { get; }
            public TransitionId AToB { get; }

            public static Fixture Create(
                CompiledStateGraph sharedGraph = null,
                int aToBPriority = 20)
            {
                RuntimeIds ids = RuntimeIds.Create();
                CompiledStateGraph graph = sharedGraph ?? CreateGraph(ids, aToBPriority);
                GraphInstanceId.TryCreate(_nextInstanceId++, out GraphInstanceId graphInstanceId);
                var trace = new List<string>();
                var control = new RuntimeControl(trace);
                var bindingBuilder = new StateGraphLogicBindingsBuilder(graph);
                Assert.IsTrue(bindingBuilder.TryBindState(
                    ids.StateDescriptor,
                    new StateRuntimeFactory<RuntimeFixtureStateLogic, RuntimeFixtureMemory>(
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
                    new ConditionRuntimeFactory<RuntimeFixtureCondition>(
                        context => new RuntimeFixtureCondition(context, control)),
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(bindingBuilder.TryFreeze(
                    out StateGraphLogicBindings bindings,
                    out diagnostic), diagnostic.Message);

                var registryBuilder = new OperationSectionRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out OperationSectionRegistry registry,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(OperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    Array.Empty<OperationSectionRequirement>(),
                    out OperationFrame operationFrame,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(ActorClock.TryCreate(
                    ids.Timeline,
                    ids.ClockDomain,
                    new TimelineEpoch(1UL),
                    out ActorClock clock,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(StateGraphRuntime.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out StateGraphRuntime runtime,
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

            public StagedGraphStep Stage()
            {
                Assert.IsTrue(Runtime.TryPreviewNextTick(
                    0.1d,
                    1d,
                    out TickFrame tick,
                    out CoCoDiagnostic previewDiagnostic), previewDiagnostic.Message);
                Assert.IsTrue(Runtime.TryStageStep(
                    tick,
                    null,
                    default,
                    out StagedGraphStep staged,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                return staged;
            }

            public void Accept(StagedGraphStep staged)
            {
                Assert.IsTrue(Runtime.TryAcceptStagedStep(staged, out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            public bool TryStageAndAccept()
            {
                return Runtime.TryPreviewNextTick(0.1d, 1d, out TickFrame tick, out _) &&
                       Runtime.TryStageStep(tick, null, default, out StagedGraphStep staged, out _) &&
                       Runtime.TryAcceptStagedStep(staged, out _);
            }

            private static CompiledStateGraph CreateGraph(
                RuntimeIds ids,
                int aToBPriority)
            {
                var catalogBuilder = new GraphDescriptorCatalogBuilder();
                Assert.IsTrue(catalogBuilder.TryRegisterState(
                    ids.StateDescriptor,
                    1U,
                    new RuntimeFixtureStateConfigFreezer(),
                    new StateRuntimeRegistration<
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
                    new ConditionRuntimeRegistration<
                        RuntimeFixtureCondition,
                        RuntimeFixtureConditionConfigSchema>(RuntimeFixtureSchemas.Condition),
                    null,
                    null,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(catalogBuilder.TryFreeze(
                    out GraphDescriptorCatalog catalog,
                    out diagnostic), diagnostic.Message);

                StateSource root = State(ids.Root, default, ids.StateA, ids.StateDescriptor, 1);
                StateSource stateA = State(ids.StateA, ids.Root, default, ids.StateDescriptor, 2);
                StateSource stateB = State(ids.StateB, ids.Root, default, ids.StateDescriptor, 3);
                var aToB = new TransitionSource(
                    ids.AToB,
                    ids.StateA,
                    ids.StateB,
                    aToBPriority,
                    TransitionWindow.Always,
                    new[]
                    {
                        new ConditionSource(ids.ConditionDescriptor, ConditionConfig(1))
                    });
                var bSelf = new TransitionSource(
                    ids.BSelf,
                    ids.StateB,
                    ids.StateB,
                    10,
                    TransitionWindow.Always,
                    Array.Empty<ConditionSource>());
                var source = new StateGraphSource(
                    StateGraphCompiler.CurrentSchemaVersion,
                    0x404UL,
                    ids.Graph,
                    new[]
                    {
                        new StateLayerSource(
                            ids.Layer,
                            ids.Root,
                            new[] { stateB, root, stateA },
                            new[] { bSelf, aToB })
                    },
                    Array.Empty<EventToIntentDeclarationSource>());
                StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);
                Assert.IsTrue(result.Succeeded, JoinDiagnostics(result.Diagnostics));
                return result.Graph;
            }

            private static StateSource State(
                StateId stateId,
                StateId parentId,
                StateId initialChild,
                StateDescriptorId descriptor,
                int value) =>
                new StateSource(
                    stateId,
                    parentId,
                    initialChild,
                    descriptor,
                    StateConfig(value));

            private static FrozenConfigSnapshot StateConfig(int value)
            {
                FrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer =
                    RuntimeFixtureSchemas.State.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.StateValue, value, out _));
                Assert.IsTrue(writer.TrySeal(out FrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }

            private static FrozenConfigSnapshot ConditionConfig(int value)
            {
                FrozenConfigWriter<RuntimeFixtureConditionConfigSchema> writer =
                    RuntimeFixtureSchemas.Condition.CreateWriter();
                Assert.IsTrue(writer.TryWrite(RuntimeFixtureSchemas.ConditionValue, value, out _));
                Assert.IsTrue(writer.TrySeal(out FrozenConfigSnapshot snapshot, out _));
                return snapshot;
            }

            private static string JoinDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
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
                GraphId graph,
                LayerId layer,
                StateId root,
                StateId stateA,
                StateId stateB,
                TransitionId aToB,
                TransitionId bSelf,
                StateDescriptorId stateDescriptor,
                ConditionDescriptorId conditionDescriptor,
                FrameLayoutId operationLayout,
                TimelineId timeline,
                ClockDomainId clockDomain)
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

            public GraphId Graph { get; }
            public LayerId Layer { get; }
            public StateId Root { get; }
            public StateId StateA { get; }
            public StateId StateB { get; }
            public TransitionId AToB { get; }
            public TransitionId BSelf { get; }
            public StateDescriptorId StateDescriptor { get; }
            public ConditionDescriptorId ConditionDescriptor { get; }
            public FrameLayoutId OperationLayout { get; }
            public TimelineId Timeline { get; }
            public ClockDomainId ClockDomain { get; }

            public static RuntimeIds Create()
            {
                GraphId.TryCreate(0x40UL, 1UL, out GraphId graph);
                LayerId.TryCreate(0x40UL, 2UL, out LayerId layer);
                StateId.TryCreate(0x40UL, 3UL, out StateId root);
                StateId.TryCreate(0x40UL, 4UL, out StateId stateA);
                StateId.TryCreate(0x40UL, 5UL, out StateId stateB);
                TransitionId.TryCreate(0x40UL, 6UL, out TransitionId aToB);
                TransitionId.TryCreate(0x40UL, 7UL, out TransitionId bSelf);
                StateDescriptorId.TryCreate(
                    0x40UL,
                    8UL,
                    out StateDescriptorId stateDescriptor);
                ConditionDescriptorId.TryCreate(
                    0x40UL,
                    9UL,
                    out ConditionDescriptorId conditionDescriptor);
                FrameLayoutId.TryCreate(0x40UL, 10UL, out FrameLayoutId operationLayout);
                TimelineId.TryCreate(0x40UL, 11UL, out TimelineId timeline);
                ClockDomainId.TryCreate(12UL, out ClockDomainId clockDomain);
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
