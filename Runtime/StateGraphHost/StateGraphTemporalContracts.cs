using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    public enum TemporalMode
    {
        Disabled = 0,
        Ready = 1,
        Previewing = 2
    }

    public readonly struct TemporalFrameInfo
    {
        internal TemporalFrameInfo(
            GraphInstanceId graphInstanceId,
            in TickFrame tickFrame,
            ContextRevision revision,
            ContextFrameOrigin origin)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            Revision = revision;
            Origin = origin;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TickFrame TickFrame { get; }
        public ContextRevision Revision { get; }
        public ContextFrameOrigin Origin { get; }
        public bool IsValid =>
            GraphInstanceId.IsValid &&
            TickFrame.IsValid &&
            Revision.IsValid &&
            Origin.IsValid;
    }

    public readonly struct TemporalState
    {
        internal TemporalState(
            TemporalMode mode,
            int capacity,
            int count,
            int previewDepth,
            in TemporalFrameInfo current,
            in TemporalFrameInfo preview,
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

        public TemporalMode Mode { get; }
        public int Capacity { get; }
        public int Count { get; }
        public int PreviewDepth { get; }
        public TemporalFrameInfo Current { get; }
        public TemporalFrameInfo Preview { get; }
        public ulong RewindRestoreDropped { get; }
        public bool CanConfirm { get; }
    }

    public enum ContextRestoreApplyKind
    {
        Preview = 1,
        Confirm = 2,
        Cancel = 3,
        Correction = 4
    }

    internal interface IContextRestoreReadSource
    {
        bool IsReadActive(ulong token);

        bool TryRead<TValue>(
            ulong token,
            StateSlotId slotId,
            out TValue value)
            where TValue : unmanaged;
    }

    internal sealed class ContextRestoreReadLease :
        IContextRestoreReadSource
    {
        private IContextRestoreReadSource _source;
        private ulong _token;

        internal bool TryAttach(
            IContextRestoreReadSource source,
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

        bool IContextRestoreReadSource.IsReadActive(ulong token)
        {
            IContextRestoreReadSource source = _source;
            return source != null &&
                   token != 0UL &&
                   token == _token &&
                   source.IsReadActive(token);
        }

        bool IContextRestoreReadSource.TryRead<TValue>(
            ulong token,
            StateSlotId slotId,
            out TValue value)
        {
            IContextRestoreReadSource source = _source;
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

    public readonly struct ContextRestoreReader
    {
        private readonly IContextRestoreReadSource _source;
        private readonly ulong _token;

        internal ContextRestoreReader(
            IContextRestoreReadSource source,
            ulong token)
        {
            _source = source;
            _token = token;
        }

        public bool IsValid =>
            _source != null &&
            _source.IsReadActive(_token);

        public bool TryRead<TValue>(
            StateSlotId slotId,
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

    public readonly struct ContextRestoreBindingContext
    {
        internal ContextRestoreBindingContext(
            ContextRestoreApplyKind applyKind,
            in TemporalFrameInfo source,
            in TickFrame targetTickFrame,
            ContextRestoreReader reader)
        {
            ApplyKind = applyKind;
            Source = source;
            TargetTickFrame = targetTickFrame;
            Reader = reader;
        }

        public ContextRestoreApplyKind ApplyKind { get; }
        public TemporalFrameInfo Source { get; }
        public TickFrame TargetTickFrame { get; }
        public ContextRestoreReader Reader { get; }
        public bool IsValid =>
            ApplyKind >= ContextRestoreApplyKind.Preview &&
            ApplyKind <= ContextRestoreApplyKind.Correction &&
            Reader.IsValid;
    }

    public interface IPersistenceRestoreOperator : IOperator
    {
        bool TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic);
    }

    public interface ITemporalRestoreOperator : IOperator
    {
        bool TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic);
    }

    public interface IContextRestoreBinding
    {
        bool TryApply(
            in ContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic);
    }

    internal interface ITemporalDecoratorBinding
    {
        MonoBehaviour DownstreamRestoreBinding { get; }
    }

    internal static class TemporalDecoratorChain
    {
        internal static bool TryValidate(
            StateGraphHost host,
            MonoBehaviour root,
            out CoCoDiagnostic diagnostic)
        {
            if (host == null ||
                root == null ||
                !(root is IContextRestoreBinding) ||
                !StateGraphHostBoundary.Contains(host, root))
            {
                diagnostic = InvalidChain(
                    "Temporal Restore Binding chain requires one live root inside the Host boundary.");
                return false;
            }

            var visited = new List<MonoBehaviour>();
            MonoBehaviour current = root;
            while (!ReferenceEquals(current, null))
            {
                if (current == null || current is IOperator ||
                    !(current is IContextRestoreBinding) ||
                    !StateGraphHostBoundary.Contains(host, current))
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
                if (!(current is ITemporalDecoratorBinding decorator))
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
    internal interface IStateGraphTemporalParticipant
    {
        bool TryAttachTemporalHost(
            StateGraphHost host,
            int historyCapacity,
            out CoCoDiagnostic diagnostic);

        bool IsTemporalParticipantLive(StateGraphHost host);

        bool TryPrepareForwardCapture(
            in TemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic);

        void PublishForwardCaptureNoFail();

        void CancelPreparedCaptureNoFail();

        bool TryPrepareAuthorityReset(
            in TemporalFrameInfo targetAuthority,
            out CoCoDiagnostic diagnostic);

        void CommitPreparedAuthorityResetNoFail();

        void CancelPreparedAuthorityResetNoFail();

        bool TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic);

        bool TryPrepareProjection(
            ContextRestoreApplyKind applyKind,
            int historyDepth,
            in TemporalFrameInfo source,
            in TickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic);

        void FinishProjectionNoFail(bool succeeded);

        bool CanConfirmPreview(int historyDepth);

        bool TryPrepareBranchCapture(
            int historyDepth,
            in TemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic);

        void PublishBranchCaptureNoFail();

        void CompletePreviewNoFail(ContextRestoreApplyKind applyKind);

        void DrainPublishedCleanupNoFail();

        void DetachTemporalHostNoFail();
    }
}
