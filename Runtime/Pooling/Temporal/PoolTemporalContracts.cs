using CoCoFlow.Runtime.Core;

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
            CoCoTemporalEntityId entityId,
            PoolTemporalApplyKind applyKind,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            bool isPresent)
        {
            EntityId = entityId;
            ApplyKind = applyKind;
            Source = source;
            TargetTickFrame = targetTickFrame;
            IsPresent = isPresent;
        }

        public CoCoTemporalEntityId EntityId { get; }
        public PoolTemporalApplyKind ApplyKind { get; }
        public CoCoTemporalFrameInfo Source { get; }
        public CoCoTickFrame TargetTickFrame { get; }
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
