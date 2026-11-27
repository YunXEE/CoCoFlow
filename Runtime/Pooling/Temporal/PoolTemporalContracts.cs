using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.Pooling.Temporal
{
    public enum PoolTemporalApplyKind
    {
        Preview = 1,
        Confirm = 2,
        Cancel = 3,
        Correction = 4
    }

    public readonly struct PoolTemporalApplyContext
    {
        internal PoolTemporalApplyContext(
            TemporalEntityId entityId,
            PoolTemporalApplyKind applyKind,
            in TemporalFrameInfo source,
            in TickFrame targetTickFrame,
            bool isPresent)
        {
            EntityId = entityId;
            ApplyKind = applyKind;
            Source = source;
            TargetTickFrame = targetTickFrame;
            IsPresent = isPresent;
        }

        public TemporalEntityId EntityId { get; }
        public PoolTemporalApplyKind ApplyKind { get; }
        public TemporalFrameInfo Source { get; }
        public TickFrame TargetTickFrame { get; }
        public bool IsPresent { get; }

        public bool IsValid =>
            EntityId.IsValid &&
            ApplyKind >= PoolTemporalApplyKind.Preview &&
            ApplyKind <= PoolTemporalApplyKind.Correction &&
            Source.IsValid &&
            TargetTickFrame.IsValid;
    }

    public interface IPoolTemporalApply
    {
        bool TryApply(
            in PoolTemporalApplyContext context,
            out CoCoDiagnostic diagnostic);
    }
}
