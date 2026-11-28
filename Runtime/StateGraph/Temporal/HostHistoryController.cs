using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 记录单个实例的已提交历史与预览位置。场景投影完成后，Host 才通知这里更新选择。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>持有历史环和已应用的预览位置，并发布准备好的历史条目。</summary>
    internal sealed class HostHistoryController : IDisposable
    {
        private HostHistoryFrame _preview;
        private bool _disposed;

        internal HostHistoryController(TemporalHistory history)
        {
            History = history;
            Mode = history == null ? HostHistoryMode.Disabled : HostHistoryMode.Ready;
        }

        internal TemporalHistory History { get; private set; }
        internal HostHistoryMode Mode { get; private set; }
        internal int PreviewDepth { get; private set; }
        internal bool HasAppliedPreview { get; private set; }

        internal bool CanBeginPreview => Mode == HostHistoryMode.Ready && History.Count >= 2;
        internal bool HasOlderSelection => Mode == HostHistoryMode.Previewing &&
                                           PreviewDepth > 0 && PreviewDepth < History.Count;

        internal HostHistoryState ReadState(in HostHistoryFrame current, bool sceneAllowsConfirm)
        {
            return new HostHistoryState(Mode, History?.Capacity ?? 0, History?.Count ?? 0, PreviewDepth,
                current, HasOlderSelection ? _preview : current, 0UL, HasOlderSelection && sceneAllowsConfirm);
        }

        internal bool TryPrepareCapture(in FinalizedContextCommit candidate, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (History == null)
            {
                return true;
            }

            try
            {
                if (History.TryPrepareCapture(candidate, out CoCoDiagnosticCode code))
                {
                    return true;
                }

                diagnostic = HistoryError(code, "Temporal projection rejected the finalized Context.");
            }
            catch (Exception)
            {
                diagnostic = HistoryError(CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal projection capture threw before the Context was committed.");
            }

            History.CancelPreparedCapture();
            return false;
        }

        internal bool TryBeginPreview()
        {
            if (!CanBeginPreview) return false;
            Mode = HostHistoryMode.Previewing;
            return true;
        }

        internal bool IsAvailableDepth(int depth) =>
            Mode == HostHistoryMode.Previewing && depth >= 0 && depth < History.Count;

        internal bool TrySelect(int depth, IReadOnlyList<StateSlotDescriptor> retainedSlots,
            byte[] authority, out TemporalSelection selection, out CoCoDiagnostic diagnostic)
        {
            selection = default;
            if (depth <= 0 || !IsAvailableDepth(depth))
            {
                diagnostic = HistoryError(CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "Temporal selection requires an older entry within the current preview.");
                return false;
            }

            try
            {
                if (History.TrySelect(depth, out selection, out CoCoDiagnosticCode code, retainedSlots, authority))
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                diagnostic = HistoryError(code, "Temporal history could not materialize the requested frame.");
            }
            catch (Exception)
            {
                diagnostic = HistoryError(CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal history decoding threw before scene projection.");
            }

            return false;
        }

        internal void AcceptProjection(int depth, in HostHistoryFrame frame)
        {
            PreviewDepth = depth;
            _preview = frame;
            HasAppliedPreview = true;
        }

        internal void FinishPreview()
        {
            Mode = History == null ? HostHistoryMode.Disabled : HostHistoryMode.Ready;
            PreviewDepth = 0;
            _preview = default;
            HasAppliedPreview = false;
        }

        internal static HostHistoryFrame GetFrame(in TemporalHistoryEntryInfo info) =>
            info.IsValid
                ? new HostHistoryFrame(info.Header.Identity.GraphInstanceId, info.Header.TickFrame,
                    info.Revision, info.Origin)
                : default;

        internal static bool TryCreateResumedTickFrame(TimelineEpoch currentEpoch, ExecutionSequence currentSequence,
            in TemporalHistoryEntryInfo source, out TickFrame resumed, out CoCoDiagnostic diagnostic)
        {
            resumed = default;
            if (!source.IsValid)
            {
                diagnostic = HistoryError(CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "Temporal restore source metadata is incomplete.");
                return false;
            }

            TickFrame tick = source.Header.TickFrame;
            ulong epoch = Math.Max(currentEpoch.Value, tick.TimelineEpoch.Value);
            ulong sequence = Math.Max(currentSequence.Value, tick.ExecutionSequence.Value);
            if (epoch == ulong.MaxValue || sequence == ulong.MaxValue)
            {
                diagnostic = HistoryError(CoCoDiagnosticCode.InvalidTimelinePosition,
                    "Temporal restore cannot advance the epoch and execution sequence.");
                return false;
            }

            return TickFrame.TryCreate(tick.DeltaTime, tick.TimelineId, tick.TimelinePosition, tick.Tick,
                tick.ClockDomainId, new ExecutionSequence(sequence + 1), new TimelineEpoch(epoch + 1),
                out resumed, out diagnostic);
        }

#if UNITY_EDITOR
        internal bool TryCopyFrames(out HostHistoryFrame[] frames)
        {
            frames = Array.Empty<HostHistoryFrame>();
            if (_disposed) return false;
            var history = History;
            if (history == null || history.Count == 0) return true;

            int count = history.Count;
            var copy = new HostHistoryFrame[count];
            for (int depth = 0; depth < count; depth++)
            {
                if (!history.TryGetInfo(depth, out TemporalHistoryEntryInfo info))
                {
                    return false;
                }

                copy[depth] = GetFrame(info);
            }

            frames = copy;
            return true;
        }
#endif

        public void Dispose()
        {
            if (_disposed) return;
            History?.Dispose();
            History = null;
            _disposed = true;
            FinishPreview();
        }

        private static CoCoDiagnostic HistoryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore,
                code == CoCoDiagnosticCode.None ? CoCoDiagnosticCode.InvalidRestoreMetadata : code, message);
    }
}
