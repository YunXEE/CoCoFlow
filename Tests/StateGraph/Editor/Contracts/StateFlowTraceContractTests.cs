using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateFlowTraceContractTests
    {
        [Test]
        public void RingKeepsLatestEntriesInChronologicalOrder()
        {
            GraphInstanceId graph = CreateGraphInstanceId(1UL);
            var buffer = new StateFlowTraceBuffer(3);
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Tick(graph, CreateTickFrame(1UL))));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Tick(graph, CreateTickFrame(2UL))));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Tick(graph, CreateTickFrame(3UL))));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Tick(graph, CreateTickFrame(4UL))));

            var copied = new StateFlowTraceEntry[3];
            Assert.AreEqual(3, buffer.CopyLatestTo(copied));
            Assert.AreEqual(2UL, copied[0].TickFrame.Tick.Value);
            Assert.AreEqual(3UL, copied[1].TickFrame.Tick.Value);
            Assert.AreEqual(4UL, copied[2].TickFrame.Tick.Value);
            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(4UL, buffer.TotalWritten);
        }

        [Test]
        public void FilteredWrappedRingCopiesLatestMatchesWithoutChangingRawCounters()
        {
            GraphInstanceId graph = CreateGraphInstanceId(11UL);
            LayerId layer = CreateLayerId(12UL);
            StateId target = CreateStateId(13UL);
            StateId other = CreateStateId(14UL);
            var buffer = new StateFlowTraceBuffer(5);

            AppendPath(buffer, graph, layer, target, 1UL);
            AppendPath(buffer, graph, layer, target, 2UL);
            AppendPath(buffer, graph, layer, other, 3UL);
            AppendPath(buffer, graph, layer, target, 4UL);
            AppendPath(buffer, graph, layer, other, 5UL);
            AppendPath(buffer, graph, layer, target, 6UL);
            AppendPath(buffer, graph, layer, target, 7UL);

            var copied = new StateFlowTraceEntry[2];
            var filter = new StateFlowTraceFilter(
                StateFlowTraceKind.All,
                stateId: target);
            Assert.AreEqual(2, buffer.CopyLatestTo(copied, filter));
            Assert.AreEqual(6UL, copied[0].TickFrame.Tick.Value);
            Assert.AreEqual(7UL, copied[1].TickFrame.Tick.Value);
            Assert.AreEqual(5, buffer.Count);
            Assert.AreEqual(5, buffer.Capacity);
            Assert.AreEqual(7UL, buffer.TotalWritten);
        }

        [Test]
        public void FilterSelectsOperatorIdentityWithoutAllocatingPayloadState()
        {
            GraphInstanceId graph = CreateGraphInstanceId(2UL);
            OperatorId first = CreateOperatorId(3UL);
            OperatorId second = CreateOperatorId(4UL);
            TickFrame tick = CreateTickFrame(1UL);
            var buffer = new StateFlowTraceBuffer(4);
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Operator(
                graph,
                tick,
                first,
                OperatorOutcomeStatus.Succeeded)));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Operator(
                graph,
                tick,
                second,
                OperatorOutcomeStatus.ClaimDenied)));

            var copied = new StateFlowTraceEntry[2];
            var filter = new StateFlowTraceFilter(
                StateFlowTraceKind.OperatorOutcome,
                operatorId: second);
            Assert.AreEqual(1, buffer.CopyLatestTo(copied, filter));
            Assert.AreEqual(second, copied[0].OperatorId);
            Assert.AreEqual(OperatorOutcomeStatus.ClaimDenied, copied[0].OperatorOutcome);
        }

        [Test]
        public void ActivePathEntriesPreserveLayerIdentityInEqualityAndFiltering()
        {
            GraphInstanceId graph = CreateGraphInstanceId(21UL);
            TickFrame tick = CreateTickFrame(1UL);
            LayerId firstLayer = CreateLayerId(22UL);
            LayerId secondLayer = CreateLayerId(23UL);
            StateId state = CreateStateId(24UL);
            StateFlowTraceEntry first = StateFlowTraceEntry.Path(
                graph,
                tick,
                firstLayer,
                state);
            StateFlowTraceEntry second = StateFlowTraceEntry.Path(
                graph,
                tick,
                secondLayer,
                state);

            Assert.IsTrue(first.IsValid);
            Assert.AreEqual(StateFlowTraceKind.ActivePath, first.Kind);
            Assert.AreEqual(firstLayer, first.LayerId);
            Assert.AreEqual(state, first.StateId);
            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(first.GetHashCode(), second.GetHashCode());

            var buffer = new StateFlowTraceBuffer(2);
            Assert.IsTrue(buffer.Append(first));
            Assert.IsTrue(buffer.Append(second));
            var copied = new StateFlowTraceEntry[2];
            var filter = new StateFlowTraceFilter(
                StateFlowTraceKind.ActivePath,
                layerId: secondLayer);
            Assert.AreEqual(1, buffer.CopyLatestTo(copied, filter));
            Assert.AreEqual(second, copied[0]);
        }

        [Test]
        public void FrameReferencesPreserveExactDefaultAndCommittedIdentityWithoutRetainingFrame()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(31UL, 1UL),
                CreateSlotId(31UL, 2UL));
            var arena = new ContextFrameArena(CreateGraphInstanceId(31UL), layout, 2);

            Assert.IsTrue(StateFlowTraceFrameReference.TryCreate(
                arena.Previous,
                out StateFlowTraceFrameReference defaults));
            Assert.IsTrue(defaults.IsValid);
            Assert.IsFalse(defaults.HasCommittedFrame);
            Assert.IsFalse(defaults.Identity.IsValid);
            Assert.IsFalse(defaults.Revision.IsValid);
            Assert.AreEqual(layout.LayoutId, defaults.LayoutId);
            Assert.AreEqual(layout.Version, defaults.LayoutVersion);
            Assert.AreEqual(layout.SchemaHash, defaults.LayoutSchemaHash);

            TickFrame tick = CreateTickFrame(1UL);
            Assert.IsTrue(arena.TryPrepare(tick, out PreparedContextCommit prepared, out _));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            finalized.CommitNoFailUnchecked();
            Assert.IsTrue(StateFlowTraceFrameReference.TryCreate(
                arena.Previous,
                out StateFlowTraceFrameReference committed));
            Assert.IsTrue(committed.IsValid);
            Assert.IsTrue(committed.HasCommittedFrame);
            Assert.AreEqual(StateFlowFrameKind.Context, committed.Identity.Kind);
            Assert.AreEqual(new ContextRevision(1UL), committed.Revision);
            Assert.IsTrue(StateFlowTraceFrameReference.TryCreateCommitted(
                CreateGraphInstanceId(31UL),
                layout,
                tick,
                new ContextRevision(1UL),
                out StateFlowTraceFrameReference preflight));
            Assert.AreEqual(committed, preflight);

            StateFlowTraceEntry entry = StateFlowTraceEntry.Commit(
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
            Assert.AreEqual(new ContextRevision(1UL), committed.Revision);
        }

        [Test]
        public void CandidateWinnerAndStateTransitionFiltersPreserveExactTraceRoles()
        {
            GraphInstanceId graph = CreateGraphInstanceId(41UL);
            TickFrame tick = CreateTickFrame(1UL);
            LayerId layer = CreateLayerId(42UL);
            StateId firstState = CreateStateId(43UL);
            StateId secondState = CreateStateId(44UL);
            TransitionId firstTransition = CreateTransitionId(45UL);
            TransitionId secondTransition = CreateTransitionId(46UL);
            var buffer = new StateFlowTraceBuffer(6);

            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                firstTransition,
                StateFlowTransitionRole.Candidate)));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                firstTransition,
                StateFlowTransitionRole.Winner)));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                secondTransition,
                StateFlowTransitionRole.Candidate)));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Path(graph, tick, layer, firstState)));
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Path(graph, tick, layer, secondState)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                firstTransition,
                StateFlowTransitionRole.None)));

            var copied = new StateFlowTraceEntry[3];
            var transitionFilter = new StateFlowTraceFilter(
                StateFlowTraceKind.Transition,
                transitionId: firstTransition);
            Assert.AreEqual(2, buffer.CopyLatestTo(copied, transitionFilter));
            Assert.AreEqual(StateFlowTransitionRole.Candidate, copied[0].TransitionRole);
            Assert.AreEqual(StateFlowTransitionRole.Winner, copied[1].TransitionRole);

            var stateFilter = new StateFlowTraceFilter(
                StateFlowTraceKind.ActivePath,
                stateId: secondState);
            Assert.AreEqual(1, buffer.CopyLatestTo(copied, stateFilter));
            Assert.AreEqual(secondState, copied[0].StateId);
        }

        [Test]
        public void InvalidEntriesAreRejectedAndClearPreservesLifetimeCount()
        {
            var buffer = new StateFlowTraceBuffer(2);
            Assert.IsFalse(buffer.Append(default));
            Assert.AreEqual(0, buffer.Count);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new StateFlowTraceBuffer(0));

            GraphInstanceId graph = CreateGraphInstanceId(5UL);
            TickFrame tick = CreateTickFrame(1UL);
            LayerId layer = CreateLayerId(6UL);
            StateId state = CreateStateId(7UL);
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Path(
                graph,
                tick,
                default,
                state)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Transition(
                graph,
                tick,
                layer,
                default)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Operation(
                graph,
                tick,
                default)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Operator(
                graph,
                tick,
                default,
                OperatorOutcomeStatus.Succeeded)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Commit(
                graph,
                tick,
                default,
                default)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Sequence(
                graph,
                tick,
                default,
                default)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Published(
                graph,
                tick,
                default)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Diagnostic(
                graph,
                tick,
                CoCoDiagnostic.None)));
            Assert.IsFalse(buffer.Append(StateFlowTraceEntry.Cancelled(
                graph,
                tick,
                CoCoDiagnostic.None)));
            Assert.AreEqual(0, buffer.Count);

            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Tick(
                graph,
                tick)));
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(1UL, buffer.TotalWritten);
        }

        [Test]
        public void AppendAndCopyAllocateNoManagedMemoryAfterWarmup()
        {
            var buffer = new StateFlowTraceBuffer(4);
            StateFlowTraceEntry entry = StateFlowTraceEntry.Tick(
                CreateGraphInstanceId(6UL),
                CreateTickFrame(1UL));
            var destination = new StateFlowTraceEntry[4];
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

        private static GraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static void AppendPath(
            StateFlowTraceBuffer buffer,
            GraphInstanceId graph,
            LayerId layer,
            StateId state,
            ulong tick)
        {
            Assert.IsTrue(buffer.Append(StateFlowTraceEntry.Path(
                graph,
                CreateTickFrame(tick),
                layer,
                state)));
        }

        private static OperatorId CreateOperatorId(ulong low)
        {
            Assert.IsTrue(OperatorId.TryCreate(0UL, low, out OperatorId id));
            return id;
        }

        private static LayerId CreateLayerId(ulong low)
        {
            Assert.IsTrue(LayerId.TryCreate(0UL, low, out LayerId id));
            return id;
        }

        private static StateId CreateStateId(ulong low)
        {
            Assert.IsTrue(StateId.TryCreate(0UL, low, out StateId id));
            return id;
        }

        private static TransitionId CreateTransitionId(ulong low)
        {
            Assert.IsTrue(TransitionId.TryCreate(0UL, low, out TransitionId id));
            return id;
        }

        private static ContextFrameLayout CreateStoredIntLayout(
            FrameLayoutId layoutId,
            StateSlotId slotId)
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId blockId = CreateBlockId(layoutId.High, layoutId.Low);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Host,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                slotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                7,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode));
            return layout;
        }

        private static FrameLayoutId CreateLayoutId(ulong high, ulong low)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(high, low, out FrameLayoutId id));
            return id;
        }

        private static StateBlockId CreateBlockId(ulong high, ulong low)
        {
            Assert.IsTrue(StateBlockId.TryCreate(high, low, out StateBlockId id));
            return id;
        }

        private static StateSlotId CreateSlotId(ulong high, ulong low)
        {
            Assert.IsTrue(StateSlotId.TryCreate(high, low, out StateSlotId id));
            return id;
        }

        private static TickFrame CreateTickFrame(ulong tick)
        {
            Assert.IsTrue(TimelineId.TryCreate(1UL, 1UL, out TimelineId timelineId));
            Assert.IsTrue(TimelinePosition.TryCreate(tick, out TimelinePosition position));
            Assert.IsTrue(ClockDomainId.TryCreate(1UL, out ClockDomainId domainId));
            Assert.IsTrue(TickFrame.TryCreate(
                0.016d,
                timelineId,
                position,
                new TimelineTick(tick),
                domainId,
                new ExecutionSequence(tick),
                new TimelineEpoch(1UL),
                out TickFrame frame,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return frame;
        }
    }
}
