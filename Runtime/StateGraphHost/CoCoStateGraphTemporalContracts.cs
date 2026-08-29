using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoTemporalMode
    {
        Disabled = 0,
        Ready = 1,
        Previewing = 2
    }

    public readonly struct CoCoTemporalFrameInfo
    {
        internal CoCoTemporalFrameInfo(
            CoCoGraphInstanceId graphInstanceId,
            in CoCoTickFrame tickFrame,
            CoCoContextRevision revision,
            CoCoContextFrameOrigin origin)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            Revision = revision;
            Origin = origin;
        }

        public CoCoGraphInstanceId GraphInstanceId { get; }
        public CoCoTickFrame TickFrame { get; }
        public CoCoContextRevision Revision { get; }
        public CoCoContextFrameOrigin Origin { get; }
        public bool IsValid =>
            GraphInstanceId.IsValid &&
            TickFrame.IsValid &&
            Revision.IsValid &&
            Origin.IsValid;
    }

    public readonly struct CoCoTemporalState
    {
        internal CoCoTemporalState(
            CoCoTemporalMode mode,
            int capacity,
            int count,
            int previewDepth,
            in CoCoTemporalFrameInfo current,
            in CoCoTemporalFrameInfo preview,
            ulong rewindRestoreDropped,
            bool canConfirm)
        {
            Mode = mode;
            Capacity = capacity;
            Count = count;
            PreviewDepth = previewDepth;
            Current = current;
            Preview = preview;
            RewindRestoreDropped = rewindRestoreDropped;
            CanConfirm = canConfirm;
        }

        public CoCoTemporalMode Mode { get; }
        public int Capacity { get; }
        public int Count { get; }
        public int PreviewDepth { get; }
        public CoCoTemporalFrameInfo Current { get; }
        public CoCoTemporalFrameInfo Preview { get; }
        public ulong RewindRestoreDropped { get; }
        public bool CanConfirm { get; }
    }

    public enum CoCoContextRestoreApplyKind
    {
        Preview = 1,
        Confirm = 2,
        Cancel = 3,
        Correction = 4
    }

    internal interface ICoCoContextRestoreReadSource
    {
        bool IsReadActive(ulong token);

        bool TryRead<TValue>(
            ulong token,
            CoCoStateSlotId slotId,
            out TValue value)
            where TValue : unmanaged;
    }

    internal sealed class CoCoContextRestoreReadLease :
        ICoCoContextRestoreReadSource
    {
        private ICoCoContextRestoreReadSource _source;
        private ulong _token;

        internal bool TryAttach(
            ICoCoContextRestoreReadSource source,
            ulong token)
        {
            if (source == null || token == 0UL || _source != null)
            {
                return false;
            }

            _token = token;
            _source = source;
            return true;
        }

        internal void Detach()
        {
            _source = null;
            _token = 0UL;
        }

        bool ICoCoContextRestoreReadSource.IsReadActive(ulong token)
        {
            ICoCoContextRestoreReadSource source = _source;
            return source != null &&
                   token != 0UL &&
                   token == _token &&
                   source.IsReadActive(token);
        }

        bool ICoCoContextRestoreReadSource.TryRead<TValue>(
            ulong token,
            CoCoStateSlotId slotId,
            out TValue value)
        {
            ICoCoContextRestoreReadSource source = _source;
            if (source != null &&
                token != 0UL &&
                token == _token &&
                source.TryRead(token, slotId, out value))
            {
                return true;
            }

            value = default;
            return false;
        }
    }

    public readonly struct CoCoContextRestoreReader
    {
        private readonly ICoCoContextRestoreReadSource _source;
        private readonly ulong _token;

        internal CoCoContextRestoreReader(
            ICoCoContextRestoreReadSource source,
            ulong token)
        {
            _source = source;
            _token = token;
        }

        public bool IsValid =>
            _source != null &&
            _source.IsReadActive(_token);

        public bool TryRead<TValue>(
            CoCoStateSlotId slotId,
            out TValue value)
            where TValue : unmanaged
        {
            if (_source != null &&
                _source.TryRead(_token, slotId, out value))
            {
                return true;
            }

            value = default;
            return false;
        }
    }

    public readonly struct CoCoContextRestoreBindingContext
    {
        internal CoCoContextRestoreBindingContext(
            CoCoContextRestoreApplyKind applyKind,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            CoCoContextRestoreReader reader)
        {
            ApplyKind = applyKind;
            Source = source;
            TargetTickFrame = targetTickFrame;
            Reader = reader;
        }

        public CoCoContextRestoreApplyKind ApplyKind { get; }
        public CoCoTemporalFrameInfo Source { get; }
        public CoCoTickFrame TargetTickFrame { get; }
        public CoCoContextRestoreReader Reader { get; }
        public bool IsValid =>
            ApplyKind >= CoCoContextRestoreApplyKind.Preview &&
            ApplyKind <= CoCoContextRestoreApplyKind.Correction &&
            Reader.IsValid;
    }

    public interface ICoCoContextRestoreBinding
    {
        bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic);
    }

    internal interface ICoCoTemporalDecoratorBinding
    {
        MonoBehaviour DownstreamRestoreBinding { get; }
    }

    internal static class CoCoTemporalDecoratorChain
    {
        internal static bool TryValidate(
            CoCoStateGraphHost host,
            MonoBehaviour root,
            out CoCoDiagnostic diagnostic)
        {
            if (host == null ||
                root == null ||
                !(root is ICoCoContextRestoreBinding) ||
                !CoCoStateGraphHostBoundary.Contains(host, root))
            {
                diagnostic = InvalidChain(
                    "Temporal Restore Binding chain requires one live root inside the Host boundary.");
                return false;
            }

            var visited = new List<MonoBehaviour>();
            MonoBehaviour current = root;
            while (!ReferenceEquals(current, null))
            {
                if (current == null ||
                    !(current is ICoCoContextRestoreBinding) ||
                    !CoCoStateGraphHostBoundary.Contains(host, current))
                {
                    diagnostic = InvalidChain(
                        "Every Temporal decorator target must remain a live Restore Binding inside the same Host boundary.");
                    return false;
                }

                for (int index = 0; index < visited.Count; index++)
                {
                    if (ReferenceEquals(visited[index], current))
                    {
                        diagnostic = InvalidChain(
                            "Temporal Restore Binding decorator chain contains a direct or indirect cycle.");
                        return false;
                    }
                }

                visited.Add(current);
                if (!(current is ICoCoTemporalDecoratorBinding decorator))
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                current = decorator.DownstreamRestoreBinding;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic InvalidChain(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.InvalidActorBinding,
                message);
    }

    // Optional host-scoped participant seam. It deliberately carries only Temporal
    // metadata; implementations remain responsible for their own private value history.
    // A historyCapacity of zero is a Persistence reset-only attachment: it may stage
    // one authority reset and Confirm projection, but must not enable forward capture,
    // preview, branching or private Temporal history. One remains invalid; capacities
    // of at least two retain the full Temporal contract.
    internal interface ICoCoStateGraphTemporalParticipant
    {
        bool TryAttachTemporalHost(
            CoCoStateGraphHost host,
            int historyCapacity,
            out CoCoDiagnostic diagnostic);

        bool IsTemporalParticipantLive(CoCoStateGraphHost host);

        bool TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic);

        void PublishForwardCaptureNoFail();

        void CancelPreparedCaptureNoFail();

        bool TryPrepareAuthorityReset(
            in CoCoTemporalFrameInfo targetAuthority,
            out CoCoDiagnostic diagnostic);

        void CommitPreparedAuthorityResetNoFail();

        void CancelPreparedAuthorityResetNoFail();

        bool TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic);

        bool TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic);

        void FinishProjectionNoFail(bool succeeded);

        bool CanConfirmPreview(int historyDepth);

        bool TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic);

        void PublishBranchCaptureNoFail();

        void CompletePreviewNoFail(CoCoContextRestoreApplyKind applyKind);

        void DrainPublishedCleanupNoFail();

        void DetachTemporalHostNoFail();
    }
}
