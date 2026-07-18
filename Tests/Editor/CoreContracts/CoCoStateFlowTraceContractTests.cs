using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoStateFlowTraceContractTests
    {
        [Test]
        public void RingKeepsLatestEntriesInChronologicalOrder()
        {
            CoCoGraphInstanceId graph = CreateGraphInstanceId(1UL);
            var buffer = new CoCoStateFlowTraceBuffer(3);
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Tick(graph, CreateTickFrame(1UL))));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Tick(graph, CreateTickFrame(2UL))));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Tick(graph, CreateTickFrame(3UL))));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Tick(graph, CreateTickFrame(4UL))));

            var copied = new CoCoStateFlowTraceEntry[3];
            Assert.AreEqual(3, buffer.CopyLatestTo(copied));
            Assert.AreEqual(2UL, copied[0].TickFrame.Tick.Value);
            Assert.AreEqual(3UL, copied[1].TickFrame.Tick.Value);
            Assert.AreEqual(4UL, copied[2].TickFrame.Tick.Value);
            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(4UL, buffer.TotalWritten);
        }

        [Test]
        public void FilterSelectsOperatorIdentityWithoutAllocatingPayloadState()
        {
            CoCoGraphInstanceId graph = CreateGraphInstanceId(2UL);
            CoCoOperatorId first = CreateOperatorId(3UL);
            CoCoOperatorId second = CreateOperatorId(4UL);
            CoCoTickFrame tick = CreateTickFrame(1UL);
            var buffer = new CoCoStateFlowTraceBuffer(4);
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Operator(
                graph,
                tick,
                first,
                CoCoOperatorOutcomeStatus.Succeeded)));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Operator(
                graph,
                tick,
                second,
                CoCoOperatorOutcomeStatus.ClaimDenied)));

            var copied = new CoCoStateFlowTraceEntry[2];
            var filter = new CoCoStateFlowTraceFilter(
                CoCoStateFlowTraceKind.OperatorOutcome,
                operatorId: second);
            Assert.AreEqual(1, buffer.CopyLatestTo(copied, filter));
            Assert.AreEqual(second, copied[0].OperatorId);
            Assert.AreEqual(CoCoOperatorOutcomeStatus.ClaimDenied, copied[0].OperatorOutcome);
        }

        [Test]
        public void ActivePathEntriesPreserveLayerIdentityInEqualityAndFiltering()
        {
            CoCoGraphInstanceId graph = CreateGraphInstanceId(21UL);
            CoCoTickFrame tick = CreateTickFrame(1UL);
            CoCoLayerId firstLayer = CreateLayerId(22UL);
            CoCoLayerId secondLayer = CreateLayerId(23UL);
            CoCoStateId state = CreateStateId(24UL);
            CoCoStateFlowTraceEntry first = CoCoStateFlowTraceEntry.Path(
                graph,
                tick,
                firstLayer,
                state);
            CoCoStateFlowTraceEntry second = CoCoStateFlowTraceEntry.Path(
                graph,
                tick,
                secondLayer,
                state);

            Assert.IsTrue(first.IsValid);
            Assert.AreEqual(CoCoStateFlowTraceKind.ActivePath, first.Kind);
            Assert.AreEqual(firstLayer, first.LayerId);
            Assert.AreEqual(state, first.StateId);
            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(first.GetHashCode(), second.GetHashCode());

            var buffer = new CoCoStateFlowTraceBuffer(2);
            Assert.IsTrue(buffer.Append(first));
            Assert.IsTrue(buffer.Append(second));
            var copied = new CoCoStateFlowTraceEntry[2];
            var filter = new CoCoStateFlowTraceFilter(
                CoCoStateFlowTraceKind.ActivePath,
                layerId: secondLayer);
            Assert.AreEqual(1, buffer.CopyLatestTo(copied, filter));
            Assert.AreEqual(second, copied[0]);
        }

        [Test]
        public void FrameReferencesPreserveExactDefaultAndCommittedIdentityWithoutRetainingFrame()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(31UL, 1UL),
                CreateSlotId(31UL, 2UL));
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(31UL), layout, 2);

            Assert.IsTrue(CoCoStateFlowTraceFrameReference.TryCreate(
                arena.Previous,
                out CoCoStateFlowTraceFrameReference defaults));
            Assert.IsTrue(defaults.IsValid);
            Assert.IsFalse(defaults.HasCommittedFrame);
            Assert.IsFalse(defaults.Identity.IsValid);
            Assert.IsFalse(defaults.Revision.IsValid);
            Assert.AreEqual(layout.LayoutId, defaults.LayoutId);
            Assert.AreEqual(layout.Version, defaults.LayoutVersion);
            Assert.AreEqual(layout.SchemaHash, defaults.LayoutSchemaHash);

            CoCoTickFrame tick = CreateTickFrame(1UL);
            Assert.IsTrue(arena.TryPrepare(tick, out CoCoPreparedContextCommit prepared, out _));
            Assert.IsTrue(prepared.TryFinalize(out CoCoFinalizedContextCommit finalized, out _));
            finalized.CommitNoFailUnchecked();
            Assert.IsTrue(CoCoStateFlowTraceFrameReference.TryCreate(
                arena.Previous,
                out CoCoStateFlowTraceFrameReference committed));
            Assert.IsTrue(committed.IsValid);
            Assert.IsTrue(committed.HasCommittedFrame);
            Assert.AreEqual(CoCoStateFlowFrameKind.Context, committed.Identity.Kind);
            Assert.AreEqual(new CoCoContextRevision(1UL), committed.Revision);
            Assert.IsTrue(CoCoStateFlowTraceFrameReference.TryCreateCommitted(
                CreateGraphInstanceId(31UL),
                layout,
                tick,
                new CoCoContextRevision(1UL),
                out CoCoStateFlowTraceFrameReference preflight));
            Assert.AreEqual(committed, preflight);

            CoCoStateFlowTraceEntry entry = CoCoStateFlowTraceEntry.Commit(
                CreateGraphInstanceId(31UL),
                tick,
                default,
                committed.Revision,
                defaults,
                committed);
            Assert.IsTrue(entry.IsValid);
            Assert.AreEqual(defaults, entry.PreviousContext);
            Assert.AreEqual(committed, entry.Frame);

            arena.Dispose();
            Assert.IsTrue(defaults.IsValid);
            Assert.IsTrue(committed.IsValid);
            Assert.AreEqual(new CoCoContextRevision(1UL), committed.Revision);
        }

        [Test]
        public void CandidateWinnerAndStateTransitionFiltersPreserveExactTraceRoles()
        {
            CoCoGraphInstanceId graph = CreateGraphInstanceId(41UL);
            CoCoTickFrame tick = CreateTickFrame(1UL);
            CoCoLayerId layer = CreateLayerId(42UL);
            CoCoStateId firstState = CreateStateId(43UL);
            CoCoStateId secondState = CreateStateId(44UL);
            CoCoTransitionId firstTransition = CreateTransitionId(45UL);
            CoCoTransitionId secondTransition = CreateTransitionId(46UL);
            var buffer = new CoCoStateFlowTraceBuffer(6);

            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                firstTransition,
                CoCoStateFlowTransitionRole.Candidate)));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                firstTransition,
                CoCoStateFlowTransitionRole.Winner)));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                secondTransition,
                CoCoStateFlowTransitionRole.Candidate)));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Path(graph, tick, layer, firstState)));
            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Path(graph, tick, layer, secondState)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                firstTransition,
                CoCoStateFlowTransitionRole.None)));

            var copied = new CoCoStateFlowTraceEntry[3];
            var transitionFilter = new CoCoStateFlowTraceFilter(
                CoCoStateFlowTraceKind.Transition,
                transitionId: firstTransition);
            Assert.AreEqual(2, buffer.CopyLatestTo(copied, transitionFilter));
            Assert.AreEqual(CoCoStateFlowTransitionRole.Candidate, copied[0].TransitionRole);
            Assert.AreEqual(CoCoStateFlowTransitionRole.Winner, copied[1].TransitionRole);

            var stateFilter = new CoCoStateFlowTraceFilter(
                CoCoStateFlowTraceKind.ActivePath,
                stateId: secondState);
            Assert.AreEqual(1, buffer.CopyLatestTo(copied, stateFilter));
            Assert.AreEqual(secondState, copied[0].StateId);
        }

        [Test]
        public void InvalidEntriesAreRejectedAndClearPreservesLifetimeCount()
        {
            var buffer = new CoCoStateFlowTraceBuffer(2);
            Assert.IsFalse(buffer.Append(default));
            Assert.AreEqual(0, buffer.Count);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new CoCoStateFlowTraceBuffer(0));

            CoCoGraphInstanceId graph = CreateGraphInstanceId(5UL);
            CoCoTickFrame tick = CreateTickFrame(1UL);
            CoCoLayerId layer = CreateLayerId(6UL);
            CoCoStateId state = CreateStateId(7UL);
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Path(
                graph,
                tick,
                default,
                state)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                default)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Operation(
                graph,
                tick,
                default)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Operator(
                graph,
                tick,
                default,
                CoCoOperatorOutcomeStatus.Succeeded)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Commit(
                graph,
                tick,
                default,
                default)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Sequence(
                graph,
                tick,
                default,
                default)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Published(
                graph,
                tick,
                default)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Diagnostic(
                graph,
                tick,
                CoCoDiagnostic.None)));
            Assert.IsFalse(buffer.Append(CoCoStateFlowTraceEntry.Cancelled(
                graph,
                tick,
                CoCoDiagnostic.None)));
            Assert.AreEqual(0, buffer.Count);

            Assert.IsTrue(buffer.Append(CoCoStateFlowTraceEntry.Tick(
                graph,
                tick)));
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(1UL, buffer.TotalWritten);
        }

        [Test]
        public void AppendAndCopyAllocateNoManagedMemoryAfterWarmup()
        {
            var buffer = new CoCoStateFlowTraceBuffer(4);
            CoCoStateFlowTraceEntry entry = CoCoStateFlowTraceEntry.Tick(
                CreateGraphInstanceId(6UL),
                CreateTickFrame(1UL));
            var destination = new CoCoStateFlowTraceEntry[4];
            int copied = 0;
            for (int index = 0; index < 100; index++)
            {
                Assert.IsTrue(buffer.Append(entry));
                copied = buffer.CopyLatestTo(destination);
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                buffer.Append(entry);
                copied = buffer.CopyLatestTo(destination);
            }

            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(4, copied);
            Assert.AreEqual(0L, allocated);
        }

        private static CoCoGraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoOperatorId CreateOperatorId(ulong low)
        {
            Assert.IsTrue(CoCoOperatorId.TryCreate(0UL, low, out CoCoOperatorId id));
            return id;
        }

        private static CoCoLayerId CreateLayerId(ulong low)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(0UL, low, out CoCoLayerId id));
            return id;
        }

        private static CoCoStateId CreateStateId(ulong low)
        {
            Assert.IsTrue(CoCoStateId.TryCreate(0UL, low, out CoCoStateId id));
            return id;
        }

        private static CoCoTransitionId CreateTransitionId(ulong low)
        {
            Assert.IsTrue(CoCoTransitionId.TryCreate(0UL, low, out CoCoTransitionId id));
            return id;
        }

        private static CoCoContextFrameLayout CreateStoredIntLayout(
            CoCoFrameLayoutId layoutId,
            CoCoStateSlotId slotId)
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId blockId = CreateBlockId(layoutId.High, layoutId.Low);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                slotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                7,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                1U,
                out CoCoContextFrameLayout layout,
                out diagnosticCode));
            return layout;
        }

        private static CoCoFrameLayoutId CreateLayoutId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(high, low, out CoCoFrameLayoutId id));
            return id;
        }

        private static CoCoStateBlockId CreateBlockId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoStateBlockId.TryCreate(high, low, out CoCoStateBlockId id));
            return id;
        }

        private static CoCoStateSlotId CreateSlotId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoStateSlotId.TryCreate(high, low, out CoCoStateSlotId id));
            return id;
        }

        private static CoCoTickFrame CreateTickFrame(ulong tick)
        {
            Assert.IsTrue(CoCoTimelineId.TryCreate(1UL, 1UL, out CoCoTimelineId timelineId));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(tick, out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(1UL, out CoCoClockDomainId domainId));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                0.016d,
                timelineId,
                position,
                new CoCoTimelineTick(tick),
                domainId,
                new CoCoExecutionSequence(tick),
                new CoCoTimelineEpoch(1UL),
                out CoCoTickFrame frame,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return frame;
        }
    }
}
