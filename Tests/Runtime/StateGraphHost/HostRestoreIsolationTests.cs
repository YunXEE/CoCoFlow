using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using HostComponent = CoCoFlow.Runtime.StateGraph.StateGraphHost;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class HostRestoreIsolationTests
    {
        [Test]
        public void PreviewReadsOlderValuesAndCancelReadsAuthorityWithoutAdvancingIt()
        {
            var owner = new GameObject("History owner");
            try
            {
                HostComponent host = owner.AddComponent<HostComponent>();
                var binding = owner.AddComponent<RestoreProbe>();
                var outcome = owner.AddComponent<RestoreOperator>();
                var boundary = HostComponentBoundary.For(host);
                using var arena = CreateArena(out StateBlockHandle block, out StateSlot<int> slot);
                binding.Slot = slot.SlotId;
                outcome.Slot = slot.SlotId;
                binding.PreviousCallback = outcome;
                var flags = new HostOperatorRecovery { component = outcome, temporal = true };
                using var scene = new HostRestoreBindings(boundary, new MonoBehaviour[] { outcome },
                    new[] { flags }, arena.Layout, binding, () => false);
                scene.Projection = ContextProjection.Temporal;

                var codecs = new ContextCodecRegistry();
                Assert.That(codecs.TryFreeze(out _), Is.True);
                Assert.That(TemporalHistory.TryCreate(arena.Layout, codecs, 2, out var history, out _), Is.True);
                using var clock = new HostHistoryController(history);
                Assert.That(clock.TryBeginPreview(), Is.False);
                Commit(arena, block, slot, Tick(1), 11, clock);
                Assert.That(clock.TryBeginPreview(), Is.False);
                Commit(arena, block, slot, Tick(2), 22, clock);
                Assert.That(clock.TryBeginPreview(), Is.True);
                HostHistoryFrame current = HostHistoryFrame.From(arena.Current);
                Assert.That(clock.TrySelect(1, scene.RetainedSlots, arena.Current.Buffer,
                    out TemporalSelection previous, out var diagnostic), Is.True, diagnostic.Message);
                HostHistoryFrame past = HostHistoryController.GetFrame(previous.Info);
                Assert.That(scene.TryApply(HostRestoreKind.Preview, past, past.TickFrame, previous.RestoreView,
                    out var touched, out diagnostic), Is.True, diagnostic.Message);
                Assert.That(touched, Is.True);
                Assert.That(binding.transform.localPosition.x, Is.EqualTo(11));
                Assert.That(outcome.LastValue, Is.EqualTo(11));
                Assert.That(binding.Escaped.IsValid, Is.False);
                Assert.That(outcome.Escaped.IsValid, Is.False);
                Assert.That(binding.PreviousReaderWasLive, Is.False);
                clock.AcceptProjection(1, past);
                Assert.That(clock.ReadState(current, true).CanConfirm, Is.True);
                Assert.That(clock.ReadState(current, false).CanConfirm, Is.False);
                Assert.That(HostHistoryController.TryCreateResumedTickFrame(current.TickFrame.TimelineEpoch,
                    current.TickFrame.ExecutionSequence, previous.Info, out TickFrame resumed, out diagnostic),
                    Is.True, diagnostic.Message);
                Assert.That(resumed.Tick.Value, Is.EqualTo(1UL));
                Assert.That(resumed.TimelineEpoch.Value, Is.EqualTo(2UL));
                Assert.That(resumed.ExecutionSequence.Value, Is.EqualTo(3UL));

                Assert.That(scene.TryApply(HostRestoreKind.Cancel, current, current.TickFrame, arena.Previous,
                    out _, out diagnostic), Is.True, diagnostic.Message);
                clock.FinishPreview();
                Assert.That(binding.transform.localPosition.x, Is.EqualTo(22));
                Assert.That(clock.ReadState(current, true).Preview.Revision, Is.EqualTo(current.Revision));
                Assert.That(arena.Current.Header.TickFrame.Tick.Value, Is.EqualTo(2UL));
                Assert.That(history.Count, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void FailedSceneCallbackExpiresReaderAndRetainsAuthorityWithHistoryOff()
        {
            var owner = new GameObject("Persistence owner");
            try
            {
                var host = owner.AddComponent<HostComponent>();
                var binding = owner.AddComponent<RestoreProbe>();
                using var arena = CreateArena(out var block, out var slot);
                binding.Slot = slot.SlotId;
                binding.ThrowAfterMutation = true;
                using var history = new HostHistoryController(null);
                Assert.That(history.TryBeginPreview(), Is.False);
                using var scene = new HostRestoreBindings(HostComponentBoundary.For(host),
                    Array.Empty<MonoBehaviour>(), Array.Empty<HostOperatorRecovery>(), arena.Layout, binding, () => false);
                scene.Projection = ContextProjection.Durable;
                Commit(arena, block, slot, Tick(1), 37, history);
                HostHistoryFrame authority = HostHistoryFrame.From(arena.Current);
                Assert.That(scene.TryApply(HostRestoreKind.Confirm, authority, authority.TickFrame, arena.Previous,
                    out var dirty, out var diagnostic), Is.False);
                Assert.That(dirty, Is.True);
                Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.WorldCorrectionRequired));
                Assert.That(binding.transform.localPosition.x, Is.EqualTo(37));
                Assert.That(binding.Escaped.TryRead(slot.SlotId, out int _), Is.False);
                Assert.That(arena.Current.Revision, Is.EqualTo(authority.Revision));
                Assert.That(history.ReadState(authority, true).Count, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void RestoreStopsBeforeCallingABindingMovedUnderAnotherHost()
        {
            var first = new GameObject("Restoring Host");
            var second = new GameObject("Other Host");
            try
            {
                var host = first.AddComponent<HostComponent>();
                second.AddComponent<HostComponent>();
                var child = new GameObject("Scene binding");
                child.transform.SetParent(first.transform, false);
                var binding = child.AddComponent<RestoreProbe>();
                var outcome = first.AddComponent<RestoreOperator>();
                using var arena = CreateArena(out var block, out var slot);
                binding.Slot = outcome.Slot = slot.SlotId;
                outcome.AfterRead = () => child.transform.SetParent(second.transform, false);
                using var history = new HostHistoryController(null);
                Commit(arena, block, slot, Tick(1), 37, history);
                HostHistoryFrame frame = HostHistoryFrame.From(arena.Current);
                var recovery = new HostOperatorRecovery { component = outcome, temporal = true };
                using var scene = new HostRestoreBindings(HostComponentBoundary.For(host),
                    new MonoBehaviour[] { outcome }, new[] { recovery }, arena.Layout, binding, () => false);
                scene.Projection = ContextProjection.Temporal;

                Assert.That(scene.TryApply(HostRestoreKind.Cancel, frame, frame.TickFrame, arena.Previous,
                    out bool dirty, out var diagnostic), Is.False);
                Assert.That(dirty, Is.True);
                Assert.That(diagnostic.Code, Is.EqualTo(CoCoDiagnosticCode.WorldCorrectionRequired));
                Assert.That(outcome.LastValue, Is.EqualTo(37));
                Assert.That(child.transform.parent, Is.SameAs(second.transform));
                Assert.That(child.transform.localPosition.x, Is.Zero);
                Assert.That(arena.Current.Revision, Is.EqualTo(frame.Revision));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        private static ContextFrameArena CreateArena(out StateBlockHandle block, out StateSlot<int> slot)
        {
            Assert.That(StateBlockId.TryCreate(31, 1, out var blockId), Is.True);
            Assert.That(StateSlotId.TryCreate(31, 2, out var slotId), Is.True);
            Assert.That(FrameLayoutId.TryCreate(31, 3, out var layoutId), Is.True);
            var layout = new ContextFrameLayoutBuilder();
            Assert.That(layout.TryAddBlock(blockId, StateBlockOwner.Host, out _), Is.True);
            Assert.That(layout.TryAddSlot(blockId, slotId, ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored, 4, default, null, out _), Is.True);
            Assert.That(layout.TryFreeze(layoutId, 1, out var frozen, out _), Is.True);
            Assert.That(frozen.TryResolveBlock(blockId, out block), Is.True);
            Assert.That(frozen.TryResolveSlot(slotId, out slot), Is.True);
            return new ContextFrameArena(InstanceIdAllocator.Next(), frozen, 3);
        }

        private static void Commit(ContextFrameArena arena, StateBlockHandle block, StateSlot<int> slot,
            TickFrame tick, int value, HostHistoryController history)
        {
            Assert.That(arena.TryPrepare(tick, out var candidate, out _), Is.True);
            Assert.That(candidate.TryGetWriter(block, out var writer), Is.True);
            Assert.That(writer.Write(slot, value), Is.True);
            Assert.That(candidate.TryFinalize(out var finalized, out _), Is.True);
            Assert.That(history.TryPrepareCapture(finalized, out var diagnostic), Is.True, diagnostic.Message);
            finalized.CommitNoFailUnchecked();
            history.History?.PublishCaptureNoFail();
        }

        private static TickFrame Tick(ulong index)
        {
            Assert.That(TimelineId.TryCreate(31, 4, out var timeline), Is.True);
            Assert.That(TimelinePosition.TryCreate(index * 0.02, out var position), Is.True);
            Assert.That(ClockDomainId.TryCreate(1, out var clock), Is.True);
            Assert.That(TickFrame.TryCreate(0.02, timeline, position, new TimelineTick(index), clock,
                new ExecutionSequence(index), new TimelineEpoch(1), out var frame, out _), Is.True);
            return frame;
        }

        private sealed class RestoreOperator : MonoBehaviour, IHostTemporalRestoreOperator
        {
            internal StateSlotId Slot;
            internal HostRestoreReader Escaped;
            internal int LastValue;
            internal Action AfterRead;
            private OperatorDescriptor _descriptor;

            public OperatorDescriptor Descriptor
            {
                get
                {
                    if (_descriptor != null) return _descriptor;
                    Assert.That(OperationId.TryCreate(31, 5, out var section), Is.True);
                    Assert.That(OperatorId.TryCreate(31, 6, out var id), Is.True);
                    var builder = new OperatorDescriptorBuilder();
                    Assert.That(builder.TryRequire<IOperatorCommitSecondarySection>(section, Mode.Discrete,
                        out _, out var diagnostic), Is.True, diagnostic.Message);
                    Assert.That(builder.TryFreeze<RestoreOperator>(id, out _descriptor, out _), Is.True);
                    return _descriptor;
                }
            }

            public bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
            {
                outcome = OperatorOutcome.NoOp;
                return true;
            }

            public bool TryRestore(in HostRestoreContext context, out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                Escaped = context.Reader;
                if (!Escaped.TryRead(Slot, out LastValue)) return false;
                AfterRead?.Invoke();
                return true;
            }
        }

        private sealed class RestoreProbe : MonoBehaviour, IHostRestoreBinding
        {
            internal StateSlotId Slot;
            internal RestoreOperator PreviousCallback;
            internal bool PreviousReaderWasLive;
            internal HostRestoreReader Escaped;
            internal bool ThrowAfterMutation;

            public bool TryApply(in HostRestoreContext context, out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                Escaped = context.Reader;
                PreviousReaderWasLive = PreviousCallback != null && PreviousCallback.Escaped.IsValid;
                if (!Escaped.TryRead(Slot, out int value)) return false;
                transform.localPosition = new Vector3(value, 0, 0);
                if (ThrowAfterMutation) throw new InvalidOperationException("Scene changed before restore failed.");
                return true;
            }
        }
    }
}
