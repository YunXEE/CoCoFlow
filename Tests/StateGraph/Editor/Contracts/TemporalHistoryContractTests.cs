using System;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class TemporalHistoryContractTests
    {
        [Test]
        public void CaptureUsesFinalizedCandidateAndCapacityIncludesCurrent()
        {
            TestLayout fixture = CreateLayout(100UL);
            ContextCodecRegistry codecs = CreateFrozenCodecs();
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                codecs,
                2,
                out TemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new ContextFrameArena(GraphId(100UL), fixture.Layout, 2);

            ContextFrame first = CommitForward(
                arena,
                history,
                fixture,
                Tick(1UL, 1UL, 1UL),
                10,
                91,
                900);

            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(2, history.Capacity);
            Assert.Greater(history.AllocatedPayloadBytes, 0L);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo firstInfo));
            Assert.AreEqual(first.Header, firstInfo.Header);
            Assert.AreEqual(1UL, firstInfo.Revision.Value);

            CommitForward(
                arena,
                history,
                fixture,
                Tick(2UL, 1UL, 2UL),
                20,
                92,
                901);
            CommitForward(
                arena,
                history,
                fixture,
                Tick(3UL, 1UL, 3UL),
                30,
                93,
                902);

            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo newest));
            Assert.IsTrue(history.TryGetInfo(1, out TemporalHistoryEntryInfo oldest));
            Assert.AreEqual(3UL, newest.Revision.Value);
            Assert.AreEqual(2UL, oldest.Revision.Value);
            Assert.IsFalse(history.TryGetInfo(2, out _));
            Assert.IsFalse(history.TrySelect(0, out _, out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
        }

        [Test]
        public void PreviewMaterializesTemporalStoredDefaultsAndDerivedWithoutRetainingFrames()
        {
            TestLayout fixture = CreateLayout(200UL);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                3,
                out TemporalHistory history,
                out _));
            var arena = new ContextFrameArena(GraphId(200UL), fixture.Layout, 2);

            CommitForward(
                arena,
                history,
                fixture,
                Tick(1UL, 1UL, 1UL),
                11,
                91,
                901);
            CommitForward(
                arena,
                history,
                fixture,
                Tick(2UL, 1UL, 2UL),
                22,
                92,
                902);

            Assert.IsTrue(history.TrySelect(
                1,
                out TemporalSelection selection,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(selection.IsValid);
            ContextRestoreReadView view = selection.RestoreView;
            Assert.IsTrue(view.IsValid);
            Assert.IsTrue(view.TryRead(fixture.TemporalStored, out int temporal));
            Assert.IsTrue(view.TryRead(fixture.TemporalReset, out int reset));
            Assert.IsTrue(view.TryRead(fixture.NonTemporalStored, out int nonTemporal));
            Assert.IsTrue(view.TryRead(fixture.TemporalDerived, out int derived));
            Assert.AreEqual(11, temporal);
            Assert.AreEqual(5, reset);
            Assert.AreEqual(7, nonTemporal);
            Assert.AreEqual(22, derived);

            Assert.IsTrue(history.TrySelect(1, out TemporalSelection replacement, out diagnosticCode));
            Assert.IsFalse(selection.IsValid);
            Assert.IsFalse(view.IsValid);
            Assert.IsTrue(replacement.IsValid);
            var retained = Array.FindAll(fixture.Layout.Slots.ToArray(),
                slot => slot.SlotId == fixture.TemporalStored.SlotId);
            Assert.IsTrue(history.TrySelect(1, out selection, out _, retained, arena.Current.Buffer));
            Assert.IsTrue(selection.RestoreView.TryRead(fixture.TemporalDerived, out derived));
            Assert.AreEqual(44, derived, "Derived must use the retained current value, not the historical one.");
            arena.RetainedRestoreSlots = retained;
            Assert.IsTrue(history.TryPrepareRestore(selection, arena, Tick(1UL, 2UL, 3UL),
                out FinalizedContextCommit mixed, out _, out _));
            Assert.IsTrue(mixed.TryCreateRestoreReadView(out var mixedView));
            Assert.IsTrue(mixedView.TryRead(fixture.TemporalDerived, out derived));
            Assert.AreEqual(44, derived);
            mixed.Cancel();
            Assert.IsTrue(history.TrySelect(1, out selection, out _, retained));
            Assert.IsTrue(selection.RestoreView.TryRead(fixture.TemporalDerived, out derived));
            Assert.AreEqual(2, derived, "No committed authority uses layout defaults for retained slots.");
        }

        [Test]
        public void BranchPublishDiscardsFutureOnlyAfterCandidateCommit()
        {
            TestLayout fixture = CreateLayout(300UL);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                4,
                out TemporalHistory history,
                out _));
            var arena = new ContextFrameArena(GraphId(300UL), fixture.Layout, 2);

            CommitForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10, 90, 900);
            CommitForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20, 91, 901);
            CommitForward(arena, history, fixture, Tick(3UL, 1UL, 3UL), 30, 92, 902);
            Assert.IsTrue(history.TrySelect(2, out TemporalSelection source, out _));

            Assert.IsTrue(history.TryPrepareRestore(
                source,
                arena,
                Tick(1UL, 2UL, 4UL),
                out FinalizedContextCommit restored,
                out ContextCommitStatus status,
                out CoCoDiagnosticCode diagnosticCode), $"{status}/{diagnosticCode}");
            Assert.IsTrue(restored.TryCreateRestoreReadView(out ContextRestoreReadView candidateView));
            Assert.IsTrue(candidateView.TryRead(fixture.TemporalStored, out int temporal));
            Assert.IsTrue(candidateView.TryRead(fixture.NonTemporalStored, out int nonTemporal));
            Assert.AreEqual(10, temporal);
            Assert.AreEqual(7, nonTemporal);

            Assert.IsTrue(history.TryPrepareBranchCapture(restored, source, out diagnosticCode), diagnosticCode.ToString());
            Assert.AreEqual(3, history.Count, "Preparing a branch must not discard the future.");
            Assert.IsTrue(source.IsValid);

            ContextFrame restoredFrame = restored.CommitNoFailUnchecked();
            history.PublishBranchCaptureNoFail();

            Assert.AreEqual(2, history.Count);
            Assert.IsFalse(source.IsValid);
            Assert.AreEqual(4UL, restoredFrame.Revision.Value);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo branchHead));
            Assert.AreEqual(2UL, branchHead.Header.Identity.TimelineEpoch.Value);
            Assert.AreEqual(4UL, branchHead.Header.Identity.ExecutionSequence.Value);
            Assert.IsTrue(branchHead.Origin.IsRestore);
            Assert.AreEqual(1UL, branchHead.Origin.SourceRevision.Value);

            Assert.IsTrue(history.TrySelect(1, out TemporalSelection retainedSource, out _));
            Assert.IsTrue(retainedSource.RestoreView.TryRead(fixture.TemporalStored, out temporal));
            Assert.AreEqual(10, temporal);
            Assert.IsFalse(history.TryGetInfo(2, out _));
        }

        [Test]
        public void AuthorityResetPublishesOnlyImportedBaselineInvalidatesSelectionAndAllowsNextCapture()
        {
            TestLayout fixture = CreateLayout(350UL);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                4,
                out TemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new ContextFrameArena(GraphId(350UL), fixture.Layout, 2);

            CommitForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10, 90, 900);
            CommitForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20, 91, 901);
            CommitForward(arena, history, fixture, Tick(3UL, 1UL, 3UL), 30, 92, 902);
            Assert.IsTrue(history.TrySelect(2, out TemporalSelection oldSelection, out diagnosticCode));
            ContextRestoreReadView oldView = oldSelection.RestoreView;

            Assert.IsTrue(arena.TryPrepare(
                Tick(4UL, 2UL, 4UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, 40));
            Assert.IsTrue(writer.Write(fixture.TemporalReset, 93));
            Assert.IsTrue(writer.Write(fixture.NonTemporalStored, 903));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.IsTrue(history.TryPrepareAuthorityReset(finalized, out diagnosticCode), diagnosticCode.ToString());
            Assert.AreEqual(3, history.Count, "Preparing the reset must not mutate published history.");
            Assert.IsTrue(oldSelection.IsValid);

            ContextFrame importedBaseline = finalized.CommitNoFailUnchecked();
            history.PublishAuthorityResetNoFail();

            Assert.AreEqual(1, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo baselineInfo));
            Assert.AreEqual(importedBaseline.Header, baselineInfo.Header);
            Assert.AreEqual(importedBaseline.Revision, baselineInfo.Revision);
            Assert.AreEqual(importedBaseline.Origin, baselineInfo.Origin);
            Assert.IsFalse(history.TryGetInfo(1, out _));
            Assert.IsFalse(oldSelection.IsValid);
            Assert.IsFalse(oldView.IsValid);
            Assert.IsFalse(history.TrySelect(1, out _, out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);

            CommitForward(arena, history, fixture, Tick(5UL, 2UL, 5UL), 50, 94, 904);

            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo nextInfo));
            Assert.IsTrue(history.TryGetInfo(1, out TemporalHistoryEntryInfo retainedBaseline));
            Assert.AreEqual(5UL, nextInfo.Header.Identity.Tick.Value);
            Assert.AreEqual(baselineInfo, retainedBaseline);
        }

        [Test]
        public void CancelledAuthorityResetLeavesPublishedHistoryAndSelectionUnchanged()
        {
            TestLayout fixture = CreateLayout(375UL);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                3,
                out TemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new ContextFrameArena(GraphId(375UL), fixture.Layout, 2);

            CommitForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10, 90, 900);
            CommitForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20, 91, 901);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo current));
            Assert.IsTrue(history.TryGetInfo(1, out TemporalHistoryEntryInfo previous));
            Assert.IsTrue(history.TrySelect(1, out TemporalSelection selection, out diagnosticCode));
            ContextRestoreReadView view = selection.RestoreView;

            Assert.IsTrue(arena.TryPrepare(
                Tick(3UL, 2UL, 3UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, 30));
            Assert.IsTrue(writer.Write(fixture.TemporalReset, 92));
            Assert.IsTrue(writer.Write(fixture.NonTemporalStored, 902));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.IsTrue(history.TryPrepareAuthorityReset(finalized, out diagnosticCode), diagnosticCode.ToString());

            history.CancelPreparedCapture();
            Assert.AreEqual(ContextCommitStatus.Cancelled, finalized.Cancel());

            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo currentAfterCancel));
            Assert.IsTrue(history.TryGetInfo(1, out TemporalHistoryEntryInfo previousAfterCancel));
            Assert.AreEqual(current, currentAfterCancel);
            Assert.AreEqual(previous, previousAfterCancel);
            Assert.IsTrue(selection.IsValid);
            Assert.IsTrue(view.IsValid);
            Assert.IsTrue(view.TryRead(fixture.TemporalStored, out int restored));
            Assert.AreEqual(10, restored);
            Assert.AreEqual(2UL, arena.Current.Revision.Value);
            Assert.AreEqual(20, arena.Current.Read(fixture.TemporalStored));
        }

        [Test]
        public void CancelledCaptureAndClearLeaveAuthorityHistoryUnchangedAndInvalidateViews()
        {
            TestLayout fixture = CreateLayout(400UL);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                3,
                out TemporalHistory history,
                out _));
            var arena = new ContextFrameArena(GraphId(400UL), fixture.Layout, 2);
            CommitForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10, 90, 900);
            CommitForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20, 91, 901);
            Assert.IsTrue(history.TrySelect(1, out TemporalSelection selection, out _));
            ContextRestoreReadView oldView = selection.RestoreView;

            Assert.IsTrue(arena.TryPrepare(
                Tick(3UL, 1UL, 3UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.IsTrue(finalized.TryCreateRestoreReadView(out ContextRestoreReadView candidateView));
            Assert.IsTrue(history.TryPrepareCapture(finalized, out _));
            history.CancelPreparedCapture();
            Assert.AreEqual(ContextCommitStatus.Cancelled, finalized.Cancel());
            Assert.AreEqual(2, history.Count);
            Assert.IsFalse(candidateView.IsValid);
            Assert.IsTrue(selection.IsValid);

            history.Clear();
            Assert.AreEqual(0, history.Count);
            Assert.IsFalse(selection.IsValid);
            Assert.IsFalse(oldView.IsValid);
            Assert.IsFalse(history.TryGetInfo(0, out _));
        }

        [TestCase(CodecFailure.ReturnFalse)]
        [TestCase(CodecFailure.Throw)]
        public void CaptureCodecFailureLeavesAuthorityAndPublishedHistoryUnchanged(
            CodecFailure failure)
        {
            CustomCodecLayout fixture = CreateCustomCodecLayout(500UL);
            var codec = new FaultingInt32Codec(fixture.Codec);
            ContextCodecRegistry codecs = CreateFrozenCodecs(codec);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                codecs,
                3,
                out TemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new ContextFrameArena(GraphId(500UL), fixture.Layout, 2);
            CommitCustomForward(
                arena,
                history,
                fixture,
                Tick(1UL, 1UL, 1UL),
                10);
            ContextFrame authority = arena.Current;
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo published));

            Assert.IsTrue(arena.TryPrepare(
                Tick(2UL, 1UL, 2UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, 20));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            codec.EncodeFailure = failure;

            bool captureSucceeded = false;
            InvalidOperationException thrown = null;
            try
            {
                captureSucceeded = history.TryPrepareCapture(finalized, out diagnosticCode);
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            Assert.IsFalse(captureSucceeded);
            Assert.IsFalse(history.HasPreparedCapture);
            if (failure == CodecFailure.ReturnFalse)
            {
                Assert.IsNull(thrown);
                Assert.AreEqual(CoCoDiagnosticCode.UnknownCodec, diagnosticCode);
            }
            else
            {
                Assert.IsNotNull(thrown);
                Assert.AreEqual(FaultingInt32Codec.EncodeFailureMessage, thrown.Message);
            }

            ContextCommitStatus cancelStatus = finalized.Cancel();
            Assert.AreEqual(
                failure == CodecFailure.Throw
                    ? ContextCommitStatus.InvalidPreparation
                    : ContextCommitStatus.Cancelled,
                cancelStatus);
            Assert.IsTrue(authority.IsAlive);
            Assert.AreEqual(authority, arena.Current);
            Assert.AreEqual(1UL, arena.Current.Revision.Value);
            Assert.AreEqual(10, arena.Current.Read(fixture.TemporalStored));
            Assert.AreEqual(1, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo afterFailure));
            Assert.AreEqual(published, afterFailure);

            codec.EncodeFailure = CodecFailure.None;
            CommitCustomForward(
                arena,
                history,
                fixture,
                Tick(2UL, 1UL, 2UL),
                20);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(20, arena.Current.Read(fixture.TemporalStored));
        }

        [TestCase(CodecFailure.ReturnFalse)]
        [TestCase(CodecFailure.Throw)]
        public void SelectionCodecFailureLeavesAuthorityAndPublishedHistoryUnchanged(
            CodecFailure failure)
        {
            CustomCodecLayout fixture = CreateCustomCodecLayout(600UL);
            var codec = new FaultingInt32Codec(fixture.Codec);
            ContextCodecRegistry codecs = CreateFrozenCodecs(codec);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                codecs,
                3,
                out TemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new ContextFrameArena(GraphId(600UL), fixture.Layout, 2);
            CommitCustomForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10);
            CommitCustomForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20);
            ContextFrame authority = arena.Current;
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo current));
            Assert.IsTrue(history.TryGetInfo(1, out TemporalHistoryEntryInfo previous));
            codec.DecodeFailure = failure;

            bool selectionSucceeded = false;
            InvalidOperationException thrown = null;
            try
            {
                selectionSucceeded = history.TrySelect(1, out _, out diagnosticCode);
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            Assert.IsFalse(selectionSucceeded);
            if (failure == CodecFailure.ReturnFalse)
            {
                Assert.IsNull(thrown);
                Assert.AreEqual(CoCoDiagnosticCode.UnknownCodec, diagnosticCode);
            }
            else
            {
                Assert.IsNotNull(thrown);
                Assert.AreEqual(FaultingInt32Codec.DecodeFailureMessage, thrown.Message);
            }

            Assert.IsTrue(authority.IsAlive);
            Assert.AreEqual(authority, arena.Current);
            Assert.AreEqual(2UL, arena.Current.Revision.Value);
            Assert.AreEqual(20, arena.Current.Read(fixture.TemporalStored));
            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo currentAfterFailure));
            Assert.IsTrue(history.TryGetInfo(1, out TemporalHistoryEntryInfo previousAfterFailure));
            Assert.AreEqual(current, currentAfterFailure);
            Assert.AreEqual(previous, previousAfterFailure);

            codec.DecodeFailure = CodecFailure.None;
            Assert.IsTrue(history.TrySelect(
                1,
                out TemporalSelection selection,
                out diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(selection.RestoreView.TryRead(fixture.TemporalStored, out int restored));
            Assert.AreEqual(10, restored);
        }

        [Test]
        public void RingAppendOverwriteAndSelectAllocateNoManagedMemoryAfterWarmup()
        {
            const int capacity = 8;
            const int warmupIterations = 100;
            const int measuredIterations = 100000;
            TestLayout fixture = CreateLayout(700UL);
            Assert.IsTrue(TemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                capacity,
                out TemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new ContextFrameArena(GraphId(700UL), fixture.Layout, 2);
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out TimelineId timeline));
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out ClockDomainId clock));
            CommitForward(
                arena,
                history,
                fixture,
                Tick(1UL, 1UL, 1UL),
                1,
                101,
                1001);

            bool failed = false;
            long checksum = 0L;
            for (int index = 0; index < warmupIterations; index++)
            {
                ulong ordinal = (ulong)index + 2UL;
                failed |= !RunHistoryCycle(
                    arena,
                    history,
                    fixture,
                    timeline,
                    clock,
                    ordinal,
                    ref checksum);
            }

            Assert.IsFalse(failed);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < measuredIterations; index++)
            {
                ulong ordinal = (ulong)index + warmupIterations + 2UL;
                failed |= !RunHistoryCycle(
                    arena,
                    history,
                    fixture,
                    timeline,
                    clock,
                    ordinal,
                    ref checksum);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            ulong expectedRevision = 1UL + warmupIterations + measuredIterations;
            Assert.IsFalse(failed);
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(capacity, history.Count);
            Assert.AreEqual(expectedRevision, arena.Current.Revision.Value);
            Assert.IsTrue(history.TryGetInfo(0, out TemporalHistoryEntryInfo newest));
            Assert.IsTrue(history.TryGetInfo(capacity - 1, out TemporalHistoryEntryInfo oldest));
            Assert.AreEqual(expectedRevision, newest.Revision.Value);
            Assert.AreEqual(expectedRevision - (ulong)capacity + 1UL, oldest.Revision.Value);
            Assert.AreNotEqual(0L, checksum);
        }

        private static ContextFrame CommitForward(
            ContextFrameArena arena,
            TemporalHistory history,
            in TestLayout fixture,
            TickFrame tick,
            int temporal,
            int reset,
            int nonTemporal)
        {
            Assert.IsTrue(arena.TryPrepare(tick, out PreparedContextCommit prepared, out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, temporal));
            Assert.IsTrue(writer.Write(fixture.TemporalReset, reset));
            Assert.IsTrue(writer.Write(fixture.NonTemporalStored, nonTemporal));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.IsTrue(finalized.TryCreateRestoreReadView(out ContextRestoreReadView candidateView));
            Assert.IsTrue(candidateView.IsValid);
            Assert.IsTrue(history.TryPrepareCapture(
                finalized,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            ContextFrame committed = finalized.CommitNoFailUnchecked();
            Assert.IsFalse(candidateView.IsValid);
            history.PublishCaptureNoFail();
            return committed;
        }

        private static ContextFrame CommitCustomForward(
            ContextFrameArena arena,
            TemporalHistory history,
            in CustomCodecLayout fixture,
            TickFrame tick,
            int value)
        {
            Assert.IsTrue(arena.TryPrepare(tick, out PreparedContextCommit prepared, out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, value));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.IsTrue(history.TryPrepareCapture(
                finalized,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            ContextFrame committed = finalized.CommitNoFailUnchecked();
            history.PublishCaptureNoFail();
            return committed;
        }

        private static bool RunHistoryCycle(
            ContextFrameArena arena,
            TemporalHistory history,
            in TestLayout fixture,
            TimelineId timeline,
            ClockDomainId clock,
            ulong ordinal,
            ref long checksum)
        {
            if (!TimelinePosition.TryCreate(
                    ordinal * 0.016d,
                    out TimelinePosition position) ||
                !TickFrame.TryCreate(
                    0.016d,
                    timeline,
                    position,
                    new TimelineTick(ordinal),
                    clock,
                    new ExecutionSequence(ordinal),
                    new TimelineEpoch(1UL),
                    out TickFrame tick,
                    out _))
            {
                return false;
            }

            if (!arena.TryPrepare(tick, out PreparedContextCommit prepared, out _))
            {
                return false;
            }

            if (!prepared.TryGetWriter(fixture.Block, out ContextFrameWriter writer) ||
                !writer.Write(fixture.TemporalStored, (int)ordinal) ||
                !writer.Write(fixture.TemporalReset, (int)ordinal + 100) ||
                !writer.Write(fixture.NonTemporalStored, (int)ordinal + 1000) ||
                !prepared.TryFinalize(out FinalizedContextCommit finalized, out _))
            {
                prepared.Cancel();
                return false;
            }

            if (!history.TryPrepareCapture(finalized, out _))
            {
                history.CancelPreparedCapture();
                finalized.Cancel();
                return false;
            }

            ContextFrame committed = finalized.CommitNoFailUnchecked();
            history.PublishCaptureNoFail();
            if (!history.TrySelect(1, out TemporalSelection selection, out _) ||
                !selection.RestoreView.TryRead(fixture.TemporalStored, out int previous))
            {
                return false;
            }

            checksum += (long)committed.Revision.Value + previous;
            return true;
        }

        private static TestLayout CreateLayout(ulong seed)
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId blockId = BlockId(seed, 1UL);
            StateSlotId temporalStoredId = SlotId(seed, 1UL);
            StateSlotId temporalResetId = SlotId(seed, 2UL);
            StateSlotId nonTemporalStoredId = SlotId(seed, 3UL);
            StateSlotId temporalDerivedId = SlotId(seed, 4UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Host,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalStoredId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalResetId,
                ContextProjection.Temporal,
                ContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                nonTemporalStoredId,
                ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                7,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                temporalDerivedId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { temporalStoredId },
                new DoubleRebuilder(temporalStoredId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                LayoutId(seed, 1UL),
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(temporalStoredId, out StateSlot<int> temporalStored));
            Assert.IsTrue(layout.TryResolveSlot(temporalResetId, out StateSlot<int> temporalReset));
            Assert.IsTrue(layout.TryResolveSlot(nonTemporalStoredId, out StateSlot<int> nonTemporalStored));
            Assert.IsTrue(layout.TryResolveSlot(temporalDerivedId, out StateSlot<int> temporalDerived));
            return new TestLayout(
                layout,
                block,
                temporalStored,
                temporalReset,
                nonTemporalStored,
                temporalDerived);
        }

        private static CustomCodecLayout CreateCustomCodecLayout(ulong seed)
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId blockId = BlockId(seed, 1UL);
            StateSlotId temporalStoredId = SlotId(seed, 1UL);
            CodecDescriptor codec = Codec(seed, 1UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Host,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalStoredId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                LayoutId(seed, 1UL),
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(
                temporalStoredId,
                out StateSlot<int> temporalStored));
            return new CustomCodecLayout(layout, block, temporalStored, codec);
        }

        private static ContextCodecRegistry CreateFrozenCodecs()
        {
            var codecs = new ContextCodecRegistry();
            Assert.IsTrue(codecs.TryFreeze(out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            return codecs;
        }

        private static ContextCodecRegistry CreateFrozenCodecs(
            IContextValueCodec<int> codec)
        {
            var codecs = new ContextCodecRegistry();
            Assert.IsTrue(codecs.TryRegister(codec, out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(codecs.TryFreeze(out diagnosticCode), diagnosticCode.ToString());
            return codecs;
        }

        private static TickFrame Tick(ulong tick, ulong epoch, ulong sequence)
        {
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out TimelineId timeline));
            Assert.IsTrue(TimelinePosition.TryCreate(tick * 0.016d, out TimelinePosition position));
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out ClockDomainId clock));
            Assert.IsTrue(TickFrame.TryCreate(
                0.016d,
                timeline,
                position,
                new TimelineTick(tick),
                clock,
                new ExecutionSequence(sequence),
                new TimelineEpoch(epoch),
                out TickFrame frame,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return frame;
        }

        private static GraphInstanceId GraphId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static FrameLayoutId LayoutId(ulong high, ulong low)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(high, low, out FrameLayoutId id));
            return id;
        }

        private static StateBlockId BlockId(ulong high, ulong low)
        {
            Assert.IsTrue(StateBlockId.TryCreate(high, low, out StateBlockId id));
            return id;
        }

        private static StateSlotId SlotId(ulong high, ulong low)
        {
            Assert.IsTrue(StateSlotId.TryCreate(high, low, out StateSlotId id));
            return id;
        }

        private static CodecDescriptor Codec(ulong high, ulong low)
        {
            Assert.IsTrue(CodecId.TryCreate(high, low, out CodecId id));
            return new CodecDescriptor(id, 1U);
        }

        private readonly struct TestLayout
        {
            internal TestLayout(
                ContextFrameLayout layout,
                StateBlockHandle block,
                StateSlot<int> temporalStored,
                StateSlot<int> temporalReset,
                StateSlot<int> nonTemporalStored,
                StateSlot<int> temporalDerived)
            {
                Layout = layout;
                Block = block;
                TemporalStored = temporalStored;
                TemporalReset = temporalReset;
                NonTemporalStored = nonTemporalStored;
                TemporalDerived = temporalDerived;
            }

            internal ContextFrameLayout Layout { get; }
            internal StateBlockHandle Block { get; }
            internal StateSlot<int> TemporalStored { get; }
            internal StateSlot<int> TemporalReset { get; }
            internal StateSlot<int> NonTemporalStored { get; }
            internal StateSlot<int> TemporalDerived { get; }
        }

        private readonly struct CustomCodecLayout
        {
            internal CustomCodecLayout(
                ContextFrameLayout layout,
                StateBlockHandle block,
                StateSlot<int> temporalStored,
                CodecDescriptor codec)
            {
                Layout = layout;
                Block = block;
                TemporalStored = temporalStored;
                Codec = codec;
            }

            internal ContextFrameLayout Layout { get; }
            internal StateBlockHandle Block { get; }
            internal StateSlot<int> TemporalStored { get; }
            internal CodecDescriptor Codec { get; }
        }

        public enum CodecFailure
        {
            None = 0,
            ReturnFalse = 1,
            Throw = 2
        }

        private sealed class FaultingInt32Codec : IContextValueCodec<int>
        {
            internal const string EncodeFailureMessage = "Temporal test codec encode failure.";
            internal const string DecodeFailureMessage = "Temporal test codec decode failure.";

            internal FaultingInt32Codec(CodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;
            internal CodecFailure EncodeFailure { get; set; }
            internal CodecFailure DecodeFailure { get; set; }

            public bool TryEncode(
                in int value,
                Span<byte> destination,
                out int bytesWritten)
            {
                if (EncodeFailure == CodecFailure.Throw)
                {
                    throw new InvalidOperationException(EncodeFailureMessage);
                }

                if (EncodeFailure == CodecFailure.ReturnFalse || destination.Length < MaxEncodedSize)
                {
                    bytesWritten = 0;
                    return false;
                }

                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                destination[3] = (byte)(value >> 24);
                bytesWritten = MaxEncodedSize;
                return true;
            }

            public bool TryDecode(
                ReadOnlySpan<byte> source,
                out int value,
                out int bytesRead)
            {
                if (DecodeFailure == CodecFailure.Throw)
                {
                    throw new InvalidOperationException(DecodeFailureMessage);
                }

                if (DecodeFailure == CodecFailure.ReturnFalse || source.Length < MaxEncodedSize)
                {
                    value = default;
                    bytesRead = 0;
                    return false;
                }

                value = source[0] |
                        source[1] << 8 |
                        source[2] << 16 |
                        source[3] << 24;
                bytesRead = MaxEncodedSize;
                return true;
            }
        }

        private sealed class DoubleRebuilder : IDerivedStateRebuilder<int>
        {
            private readonly StateSlotId _source;

            internal DoubleRebuilder(StateSlotId source)
            {
                _source = source;
            }

            public bool TryRebuild(in DerivedStateReadContext context, out int value)
            {
                if (!context.TryRead(_source, out int source))
                {
                    value = default;
                    return false;
                }

                value = source * 2;
                return true;
            }
        }
    }
}
