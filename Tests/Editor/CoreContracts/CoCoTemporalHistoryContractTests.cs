using System;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoTemporalHistoryContractTests
    {
        [Test]
        public void CaptureUsesFinalizedCandidateAndCapacityIncludesCurrent()
        {
            TestLayout fixture = CreateLayout(100UL);
            CoCoContextCodecRegistry codecs = CreateFrozenCodecs();
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                codecs,
                2,
                out CoCoTemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new CoCoContextFrameArena(GraphId(100UL), fixture.Layout, 2);

            CoCoContextFrame first = CommitForward(
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
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo firstInfo));
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
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo newest));
            Assert.IsTrue(history.TryGetInfo(1, out CoCoTemporalHistoryEntryInfo oldest));
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
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                3,
                out CoCoTemporalHistory history,
                out _));
            var arena = new CoCoContextFrameArena(GraphId(200UL), fixture.Layout, 2);

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
                out CoCoTemporalSelection selection,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(selection.IsValid);
            CoCoContextRestoreReadView view = selection.RestoreView;
            Assert.IsTrue(view.IsValid);
            Assert.IsTrue(view.TryRead(fixture.TemporalStored, out int temporal));
            Assert.IsTrue(view.TryRead(fixture.TemporalReset, out int reset));
            Assert.IsTrue(view.TryRead(fixture.NonTemporalStored, out int nonTemporal));
            Assert.IsTrue(view.TryRead(fixture.TemporalDerived, out int derived));
            Assert.AreEqual(11, temporal);
            Assert.AreEqual(5, reset);
            Assert.AreEqual(7, nonTemporal);
            Assert.AreEqual(22, derived);

            Assert.IsTrue(history.TrySelect(1, out CoCoTemporalSelection replacement, out diagnosticCode));
            Assert.IsFalse(selection.IsValid);
            Assert.IsFalse(view.IsValid);
            Assert.IsTrue(replacement.IsValid);
        }

        [Test]
        public void BranchPublishDiscardsFutureOnlyAfterCandidateCommit()
        {
            TestLayout fixture = CreateLayout(300UL);
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                4,
                out CoCoTemporalHistory history,
                out _));
            var arena = new CoCoContextFrameArena(GraphId(300UL), fixture.Layout, 2);

            CommitForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10, 90, 900);
            CommitForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20, 91, 901);
            CommitForward(arena, history, fixture, Tick(3UL, 1UL, 3UL), 30, 92, 902);
            Assert.IsTrue(history.TrySelect(2, out CoCoTemporalSelection source, out _));

            Assert.IsTrue(history.TryPrepareRestore(
                source,
                arena,
                Tick(1UL, 2UL, 4UL),
                out CoCoFinalizedContextCommit restored,
                out CoCoContextCommitStatus status,
                out CoCoDiagnosticCode diagnosticCode), $"{status}/{diagnosticCode}");
            Assert.IsTrue(restored.TryCreateRestoreReadView(out CoCoContextRestoreReadView candidateView));
            Assert.IsTrue(candidateView.TryRead(fixture.TemporalStored, out int temporal));
            Assert.IsTrue(candidateView.TryRead(fixture.NonTemporalStored, out int nonTemporal));
            Assert.AreEqual(10, temporal);
            Assert.AreEqual(7, nonTemporal);

            Assert.IsTrue(history.TryPrepareBranchCapture(restored, source, out diagnosticCode), diagnosticCode.ToString());
            Assert.AreEqual(3, history.Count, "Preparing a branch must not discard the future.");
            Assert.IsTrue(source.IsValid);

            CoCoContextFrame restoredFrame = restored.CommitNoFailUnchecked();
            history.PublishBranchCaptureNoFail();

            Assert.AreEqual(2, history.Count);
            Assert.IsFalse(source.IsValid);
            Assert.AreEqual(4UL, restoredFrame.Revision.Value);
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo branchHead));
            Assert.AreEqual(2UL, branchHead.Header.Identity.TimelineEpoch.Value);
            Assert.AreEqual(4UL, branchHead.Header.Identity.ExecutionSequence.Value);
            Assert.IsTrue(branchHead.Origin.IsRestore);
            Assert.AreEqual(1UL, branchHead.Origin.SourceRevision.Value);

            Assert.IsTrue(history.TrySelect(1, out CoCoTemporalSelection retainedSource, out _));
            Assert.IsTrue(retainedSource.RestoreView.TryRead(fixture.TemporalStored, out temporal));
            Assert.AreEqual(10, temporal);
            Assert.IsFalse(history.TryGetInfo(2, out _));
        }

        [Test]
        public void CancelledCaptureAndClearLeaveAuthorityHistoryUnchangedAndInvalidateViews()
        {
            TestLayout fixture = CreateLayout(400UL);
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                3,
                out CoCoTemporalHistory history,
                out _));
            var arena = new CoCoContextFrameArena(GraphId(400UL), fixture.Layout, 2);
            CommitForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10, 90, 900);
            CommitForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20, 91, 901);
            Assert.IsTrue(history.TrySelect(1, out CoCoTemporalSelection selection, out _));
            CoCoContextRestoreReadView oldView = selection.RestoreView;

            Assert.IsTrue(arena.TryPrepare(
                Tick(3UL, 1UL, 3UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
            Assert.IsTrue(finalized.TryCreateRestoreReadView(out CoCoContextRestoreReadView candidateView));
            Assert.IsTrue(history.TryPrepareCapture(finalized, out _));
            history.CancelPreparedCapture();
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, finalized.Cancel());
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
            CoCoContextCodecRegistry codecs = CreateFrozenCodecs(codec);
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                codecs,
                3,
                out CoCoTemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new CoCoContextFrameArena(GraphId(500UL), fixture.Layout, 2);
            CommitCustomForward(
                arena,
                history,
                fixture,
                Tick(1UL, 1UL, 1UL),
                10);
            CoCoContextFrame authority = arena.Current;
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo published));

            Assert.IsTrue(arena.TryPrepare(
                Tick(2UL, 1UL, 2UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, 20));
            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
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

            CoCoContextCommitStatus cancelStatus = finalized.Cancel();
            Assert.AreEqual(
                failure == CodecFailure.Throw
                    ? CoCoContextCommitStatus.InvalidPreparation
                    : CoCoContextCommitStatus.Cancelled,
                cancelStatus);
            Assert.IsTrue(authority.IsAlive);
            Assert.AreEqual(authority, arena.Current);
            Assert.AreEqual(1UL, arena.Current.Revision.Value);
            Assert.AreEqual(10, arena.Current.Read(fixture.TemporalStored));
            Assert.AreEqual(1, history.Count);
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo afterFailure));
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
            CoCoContextCodecRegistry codecs = CreateFrozenCodecs(codec);
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                codecs,
                3,
                out CoCoTemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new CoCoContextFrameArena(GraphId(600UL), fixture.Layout, 2);
            CommitCustomForward(arena, history, fixture, Tick(1UL, 1UL, 1UL), 10);
            CommitCustomForward(arena, history, fixture, Tick(2UL, 1UL, 2UL), 20);
            CoCoContextFrame authority = arena.Current;
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo current));
            Assert.IsTrue(history.TryGetInfo(1, out CoCoTemporalHistoryEntryInfo previous));
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
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo currentAfterFailure));
            Assert.IsTrue(history.TryGetInfo(1, out CoCoTemporalHistoryEntryInfo previousAfterFailure));
            Assert.AreEqual(current, currentAfterFailure);
            Assert.AreEqual(previous, previousAfterFailure);

            codec.DecodeFailure = CodecFailure.None;
            Assert.IsTrue(history.TrySelect(
                1,
                out CoCoTemporalSelection selection,
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
            Assert.IsTrue(CoCoTemporalHistory.TryCreate(
                fixture.Layout,
                CreateFrozenCodecs(),
                capacity,
                out CoCoTemporalHistory history,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            var arena = new CoCoContextFrameArena(GraphId(700UL), fixture.Layout, 2);
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out CoCoTimelineId timeline));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out CoCoClockDomainId clock));
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
            Assert.IsTrue(history.TryGetInfo(0, out CoCoTemporalHistoryEntryInfo newest));
            Assert.IsTrue(history.TryGetInfo(capacity - 1, out CoCoTemporalHistoryEntryInfo oldest));
            Assert.AreEqual(expectedRevision, newest.Revision.Value);
            Assert.AreEqual(expectedRevision - (ulong)capacity + 1UL, oldest.Revision.Value);
            Assert.AreNotEqual(0L, checksum);
        }

        private static CoCoContextFrame CommitForward(
            CoCoContextFrameArena arena,
            CoCoTemporalHistory history,
            in TestLayout fixture,
            CoCoTickFrame tick,
            int temporal,
            int reset,
            int nonTemporal)
        {
            Assert.IsTrue(arena.TryPrepare(tick, out CoCoPreparedContextCommit prepared, out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, temporal));
            Assert.IsTrue(writer.Write(fixture.TemporalReset, reset));
            Assert.IsTrue(writer.Write(fixture.NonTemporalStored, nonTemporal));
            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
            Assert.IsTrue(finalized.TryCreateRestoreReadView(out CoCoContextRestoreReadView candidateView));
            Assert.IsTrue(candidateView.IsValid);
            Assert.IsTrue(history.TryPrepareCapture(
                finalized,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            CoCoContextFrame committed = finalized.CommitNoFailUnchecked();
            Assert.IsFalse(candidateView.IsValid);
            history.PublishCaptureNoFail();
            return committed;
        }

        private static CoCoContextFrame CommitCustomForward(
            CoCoContextFrameArena arena,
            CoCoTemporalHistory history,
            in CustomCodecLayout fixture,
            CoCoTickFrame tick,
            int value)
        {
            Assert.IsTrue(arena.TryPrepare(tick, out CoCoPreparedContextCommit prepared, out _));
            Assert.IsTrue(prepared.TryGetWriter(fixture.Block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(fixture.TemporalStored, value));
            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
            Assert.IsTrue(history.TryPrepareCapture(
                finalized,
                out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            CoCoContextFrame committed = finalized.CommitNoFailUnchecked();
            history.PublishCaptureNoFail();
            return committed;
        }

        private static bool RunHistoryCycle(
            CoCoContextFrameArena arena,
            CoCoTemporalHistory history,
            in TestLayout fixture,
            CoCoTimelineId timeline,
            CoCoClockDomainId clock,
            ulong ordinal,
            ref long checksum)
        {
            if (!CoCoTimelinePosition.TryCreate(
                    ordinal * 0.016d,
                    out CoCoTimelinePosition position) ||
                !CoCoTickFrame.TryCreate(
                    0.016d,
                    timeline,
                    position,
                    new CoCoTimelineTick(ordinal),
                    clock,
                    new CoCoExecutionSequence(ordinal),
                    new CoCoTimelineEpoch(1UL),
                    out CoCoTickFrame tick,
                    out _))
            {
                return false;
            }

            if (!arena.TryPrepare(tick, out CoCoPreparedContextCommit prepared, out _))
            {
                return false;
            }

            if (!prepared.TryGetWriter(fixture.Block, out CoCoContextFrameWriter writer) ||
                !writer.Write(fixture.TemporalStored, (int)ordinal) ||
                !writer.Write(fixture.TemporalReset, (int)ordinal + 100) ||
                !writer.Write(fixture.NonTemporalStored, (int)ordinal + 1000) ||
                !prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _))
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

            CoCoContextFrame committed = finalized.CommitNoFailUnchecked();
            history.PublishCaptureNoFail();
            if (!history.TrySelect(1, out CoCoTemporalSelection selection, out _) ||
                !selection.RestoreView.TryRead(fixture.TemporalStored, out int previous))
            {
                return false;
            }

            checksum += (long)committed.Revision.Value + previous;
            return true;
        }

        private static TestLayout CreateLayout(ulong seed)
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId blockId = BlockId(seed, 1UL);
            CoCoStateSlotId temporalStoredId = SlotId(seed, 1UL);
            CoCoStateSlotId temporalResetId = SlotId(seed, 2UL);
            CoCoStateSlotId nonTemporalStoredId = SlotId(seed, 3UL);
            CoCoStateSlotId temporalDerivedId = SlotId(seed, 4UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalStoredId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalResetId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                nonTemporalStoredId,
                CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                7,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                temporalDerivedId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { temporalStoredId },
                new DoubleRebuilder(temporalStoredId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                LayoutId(seed, 1UL),
                1U,
                out CoCoContextFrameLayout layout,
                out diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(temporalStoredId, out CoCoStateSlot<int> temporalStored));
            Assert.IsTrue(layout.TryResolveSlot(temporalResetId, out CoCoStateSlot<int> temporalReset));
            Assert.IsTrue(layout.TryResolveSlot(nonTemporalStoredId, out CoCoStateSlot<int> nonTemporalStored));
            Assert.IsTrue(layout.TryResolveSlot(temporalDerivedId, out CoCoStateSlot<int> temporalDerived));
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
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId blockId = BlockId(seed, 1UL);
            CoCoStateSlotId temporalStoredId = SlotId(seed, 1UL);
            CoCoCodecDescriptor codec = Codec(seed, 1UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalStoredId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                1,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                LayoutId(seed, 1UL),
                1U,
                out CoCoContextFrameLayout layout,
                out diagnosticCode), diagnosticCode.ToString());
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(
                temporalStoredId,
                out CoCoStateSlot<int> temporalStored));
            return new CustomCodecLayout(layout, block, temporalStored, codec);
        }

        private static CoCoContextCodecRegistry CreateFrozenCodecs()
        {
            var codecs = new CoCoContextCodecRegistry();
            Assert.IsTrue(codecs.TryFreeze(out CoCoDiagnosticCode diagnosticCode), diagnosticCode.ToString());
            return codecs;
        }

        private static CoCoContextCodecRegistry CreateFrozenCodecs(
            ICoCoContextValueCodec<int> codec)
        {
            var codecs = new CoCoContextCodecRegistry();
            Assert.IsTrue(codecs.TryRegister(codec, out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(codecs.TryFreeze(out diagnosticCode), diagnosticCode.ToString());
            return codecs;
        }

        private static CoCoTickFrame Tick(ulong tick, ulong epoch, ulong sequence)
        {
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out CoCoTimelineId timeline));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(tick * 0.016d, out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out CoCoClockDomainId clock));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                0.016d,
                timeline,
                position,
                new CoCoTimelineTick(tick),
                clock,
                new CoCoExecutionSequence(sequence),
                new CoCoTimelineEpoch(epoch),
                out CoCoTickFrame frame,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return frame;
        }

        private static CoCoGraphInstanceId GraphId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoFrameLayoutId LayoutId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(high, low, out CoCoFrameLayoutId id));
            return id;
        }

        private static CoCoStateBlockId BlockId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoStateBlockId.TryCreate(high, low, out CoCoStateBlockId id));
            return id;
        }

        private static CoCoStateSlotId SlotId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoStateSlotId.TryCreate(high, low, out CoCoStateSlotId id));
            return id;
        }

        private static CoCoCodecDescriptor Codec(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoCodecId.TryCreate(high, low, out CoCoCodecId id));
            return new CoCoCodecDescriptor(id, 1U);
        }

        private readonly struct TestLayout
        {
            internal TestLayout(
                CoCoContextFrameLayout layout,
                CoCoStateBlockHandle block,
                CoCoStateSlot<int> temporalStored,
                CoCoStateSlot<int> temporalReset,
                CoCoStateSlot<int> nonTemporalStored,
                CoCoStateSlot<int> temporalDerived)
            {
                Layout = layout;
                Block = block;
                TemporalStored = temporalStored;
                TemporalReset = temporalReset;
                NonTemporalStored = nonTemporalStored;
                TemporalDerived = temporalDerived;
            }

            internal CoCoContextFrameLayout Layout { get; }
            internal CoCoStateBlockHandle Block { get; }
            internal CoCoStateSlot<int> TemporalStored { get; }
            internal CoCoStateSlot<int> TemporalReset { get; }
            internal CoCoStateSlot<int> NonTemporalStored { get; }
            internal CoCoStateSlot<int> TemporalDerived { get; }
        }

        private readonly struct CustomCodecLayout
        {
            internal CustomCodecLayout(
                CoCoContextFrameLayout layout,
                CoCoStateBlockHandle block,
                CoCoStateSlot<int> temporalStored,
                CoCoCodecDescriptor codec)
            {
                Layout = layout;
                Block = block;
                TemporalStored = temporalStored;
                Codec = codec;
            }

            internal CoCoContextFrameLayout Layout { get; }
            internal CoCoStateBlockHandle Block { get; }
            internal CoCoStateSlot<int> TemporalStored { get; }
            internal CoCoCodecDescriptor Codec { get; }
        }

        public enum CodecFailure
        {
            None = 0,
            ReturnFalse = 1,
            Throw = 2
        }

        private sealed class FaultingInt32Codec : ICoCoContextValueCodec<int>
        {
            internal const string EncodeFailureMessage = "Temporal test codec encode failure.";
            internal const string DecodeFailureMessage = "Temporal test codec decode failure.";

            internal FaultingInt32Codec(CoCoCodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CoCoCodecDescriptor Descriptor { get; }
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

        private sealed class DoubleRebuilder : ICoCoDerivedStateRebuilder<int>
        {
            private readonly CoCoStateSlotId _source;

            internal DoubleRebuilder(CoCoStateSlotId source)
            {
                _source = source;
            }

            public bool TryRebuild(in CoCoDerivedStateReadContext context, out int value)
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
