using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

// EditMode 驱动图实例执行、暂停和恢复，读取实际路径、记忆与时间线。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>EditMode 驱动图实例执行、暂停和恢复，读取实际路径、记忆与时间线。</summary>
    public sealed class StateGraphRuntimeTests
    {

        #region Public API

        /// <summary>请求转换并连续步进两次，核对源在当前步退出、目标在下一步进入。</summary>
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
                new[] { "update:root:1", "update:a:1", "exit:a:2" },
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

        /// <summary>检查提交跟踪的路径和胜者，确保前一步的胜出边不会延续到当前步。</summary>
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

        /// <summary>读取候选至提交期间的跟踪，核对已接受请求、胜出边和提交的记录顺序。</summary>
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

        /// <summary>拒绝一次带转换与输出的步进，复核先前时钟、路径、记忆和操作状态。</summary>
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

        /// <summary>准备提交后在最终确认前读取实例，检查公开状态仍指向上次成功结果。</summary>
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

        /// <summary>取消已经准备的提交，核对时钟、节点与操作并未推进。</summary>
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

        /// <summary>在新候选存在时尝试使用陈旧凭据，验证只拒绝旧凭据且新候选仍可处理。</summary>
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

        /// <summary>准备历史恢复但尚未应用，核对外部仍读到当前成功提交。</summary>
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

        /// <summary>取消已经准备的恢复，确认现时路径与时钟没有变化。</summary>
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

        /// <summary>使用不匹配指纹准备恢复，验证取消时钟候选并保留现时实例。</summary>
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

        /// <summary>触发自环并分两步观察，核对当前退出、激活重置和随后重新进入。</summary>
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

        /// <summary>在回调中写入更低的动作进度，检查错误与最近成功提交均可查询。</summary>
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

        /// <summary>交错驱动共享同一编译图的两个实例，核对时钟、记忆、路径与故障互相独立。</summary>
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

        /// <summary>暂停实例后尝试步进，验证没有新帧产生且原状态保留。</summary>
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

        /// <summary>预热正常与转换两种步进路径，然后以托管分配计数核查稳定阶段。</summary>
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

        /// <summary>重复预热后的暂停与恢复，测量该路径的托管分配。</summary>
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

        /// <summary>仅请求最低整数优先级的边，核对它仍是合法候选并可胜出。</summary>
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

        #endregion

        #region Internal Logic

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
            StateGraphInstance runtime,
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
            InstanceClock clock,
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

        /// <summary>在运行测试中保存恢复步骤所需的准备状态与回调结果。</summary>
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
                StateActivationMemory memory,
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
                StateActivationMemory memory,
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
                StateActivationMemory candidateMemory,
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

        /// <summary>将执行器、时钟和观察器组合为运行及回滚测试的场景。</summary>
        private sealed class Fixture
        {
            private static ulong _nextInstanceId = 100UL;

            private Fixture(
                CompiledStateGraph graph,
                StateGraphInstance runtime,
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
            public StateGraphInstance Runtime { get; }
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
                Assert.IsTrue(bindingBuilder.TryFreeze(
                    out StateGraphLogicBindings bindings,
                    out diagnostic), diagnostic.Message);

                var registryBuilder = new OperationRegistryBuilder();
                Assert.IsTrue(registryBuilder.TryFreeze(
                    graph.OperationProvides.LayoutId,
                    out OperationRegistry registry,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(OperationFrame.TryCreate(
                    registry,
                    graphInstanceId,
                    Array.Empty<OperationRequirement>(),
                    out OperationFrame operationFrame,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(InstanceClock.TryCreate(
                    ids.Timeline,
                    ids.ClockDomain,
                    new TimelineEpoch(1UL),
                    out InstanceClock clock,
                    out diagnostic), diagnostic.Message);
                Assert.IsTrue(StateGraphInstance.TryCreate(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    out StateGraphInstance runtime,
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
                    TransitionWindow.Always);
                var bSelf = new TransitionSource(
                    ids.BSelf,
                    ids.StateB,
                    ids.StateB,
                    10,
                    TransitionWindow.Always);
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

        /// <summary>指定运行测试的出边请求和故障，并记录状态调用次序。</summary>
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

        /// <summary>为运行测试的状态、层和转换提供固定标识。</summary>
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
                    operationLayout,
                    timeline,
                    clockDomain);
            }
        }

        #endregion
    }
}
