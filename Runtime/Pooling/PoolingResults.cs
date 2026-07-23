using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Pooling
{
    public enum PoolOperationStatus
    {
        None = 0,
        Succeeded = 1,
        Cancelled = 2,
        Failed = 3
    }

    public readonly struct PoolPrepareResult
    {
        private PoolPrepareResult(
            PoolOperationStatus status,
            PoolId poolId,
            int createdCount,
            CoCoDiagnostic diagnostic)
        {
            Status = status;
            PoolId = poolId;
            CreatedCount = createdCount;
            Diagnostic = diagnostic;
        }

        public PoolOperationStatus Status { get; }
        public PoolId PoolId { get; }
        public int CreatedCount { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public bool Succeeded => Status == PoolOperationStatus.Succeeded;
        public bool Cancelled => Status == PoolOperationStatus.Cancelled;

        internal static PoolPrepareResult Success(PoolId poolId, int createdCount) =>
            new PoolPrepareResult(
                PoolOperationStatus.Succeeded,
                poolId,
                createdCount,
                CoCoDiagnostic.None);

        internal static PoolPrepareResult Cancellation(
            PoolId poolId,
            CoCoDiagnostic diagnostic) =>
            new PoolPrepareResult(
                PoolOperationStatus.Cancelled,
                poolId,
                0,
                diagnostic);

        internal static PoolPrepareResult Failure(
            PoolId poolId,
            CoCoDiagnostic diagnostic) =>
            new PoolPrepareResult(
                PoolOperationStatus.Failed,
                poolId,
                0,
                diagnostic);
    }

    public readonly struct PoolPrewarmResult
    {
        private PoolPrewarmResult(
            PoolOperationStatus status,
            PoolId poolId,
            int createdCount,
            int inactiveCount,
            CoCoDiagnostic diagnostic)
        {
            Status = status;
            PoolId = poolId;
            CreatedCount = createdCount;
            InactiveCount = inactiveCount;
            Diagnostic = diagnostic;
        }

        public PoolOperationStatus Status { get; }
        public PoolId PoolId { get; }
        public int CreatedCount { get; }
        public int InactiveCount { get; }
        public CoCoDiagnostic Diagnostic { get; }
        public bool Succeeded => Status == PoolOperationStatus.Succeeded;
        public bool Cancelled => Status == PoolOperationStatus.Cancelled;

        internal static PoolPrewarmResult Success(
            PoolId poolId,
            int createdCount,
            int inactiveCount) =>
            new PoolPrewarmResult(
                PoolOperationStatus.Succeeded,
                poolId,
                createdCount,
                inactiveCount,
                CoCoDiagnostic.None);

        internal static PoolPrewarmResult Cancellation(
            PoolId poolId,
            int createdCount,
            int inactiveCount,
            CoCoDiagnostic diagnostic) =>
            new PoolPrewarmResult(
                PoolOperationStatus.Cancelled,
                poolId,
                createdCount,
                inactiveCount,
                diagnostic);

        internal static PoolPrewarmResult Failure(
            PoolId poolId,
            int createdCount,
            int inactiveCount,
            CoCoDiagnostic diagnostic) =>
            new PoolPrewarmResult(
                PoolOperationStatus.Failed,
                poolId,
                createdCount,
                inactiveCount,
                diagnostic);
    }
}
