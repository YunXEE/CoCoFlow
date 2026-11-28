using System;
using CoCoFlow.Runtime.Core;

// Host 预览实例的下一步时间。仅在整步接受或完成恢复时应用对应的时钟凭据。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存一次时钟恢复所验证的目标位置，供整步确认时应用。</summary>
    internal readonly struct PreparedInstanceClockRestore
    {
        private readonly InstanceClock _clock;
        private readonly ulong _token;

        internal PreparedInstanceClockRestore(
            InstanceClock clock,
            ulong token,
            in TickFrame tickFrame)
        {
            _clock = clock;
            _token = token;
            TickFrame = tickFrame;
        }

        internal TickFrame TickFrame { get; }
        internal bool IsValid =>
            _clock != null && _clock.IsPreparedRestoreTokenCurrent(_token);

        internal void ApplyNoFail()
        {
            _clock.ApplyPreparedRestoreNoFail();
        }

        internal bool Cancel() =>
            _clock != null && _clock.CancelPreparedRestore(_token);
    }

    /// <summary>保存实例的时间线和执行序号，按 Host 的提交决定推进或恢复。</summary>
    internal sealed class InstanceClock
    {
        private readonly TimelineId _timelineId;
        private readonly ClockDomainId _clockDomainId;
        private readonly bool _hasDeclaredGraphInstanceId;
        private TimelineEpoch _epoch;
        private GraphInstanceId _graphInstanceId;
        private object _runtimeOwner;
        private TickFrame _candidate;
        private double _seconds;
        private ulong _tick;
        private ulong _executionSequence;
        private ulong _restoreGeneration;
        private ulong _activeRestoreToken;
        private TickFrame _restoreCandidate;
        private bool _hasCandidate;

        private InstanceClock(
            TimelineId timelineId,
            ClockDomainId clockDomainId,
            TimelineEpoch epoch,
            GraphInstanceId graphInstanceId,
            bool hasDeclaredGraphInstanceId)
        {
            _timelineId = timelineId;
            _clockDomainId = clockDomainId;
            _epoch = epoch;
            _graphInstanceId = graphInstanceId;
            _hasDeclaredGraphInstanceId = hasDeclaredGraphInstanceId;
        }

        public TimelineId TimelineId => _timelineId;
        public ClockDomainId ClockDomainId => _clockDomainId;
        public GraphInstanceId GraphInstanceId => _graphInstanceId;
        public TimelineEpoch TimelineEpoch => _epoch;
        public TimelineTick Tick => new TimelineTick(_tick);
        public ExecutionSequence ExecutionSequence => new ExecutionSequence(_executionSequence);
        public double Seconds => _seconds;
        public bool HasStagedTick => _hasCandidate;

        public static bool TryCreate(
            TimelineId timelineId,
            ClockDomainId clockDomainId,
            TimelineEpoch epoch,
            out InstanceClock clock,
            out CoCoDiagnostic diagnostic) =>
            TryCreate(
                timelineId,
                clockDomainId,
                epoch,
                default,
                false,
                out clock,
                out diagnostic);

        public static bool TryCreate(
            TimelineId timelineId,
            ClockDomainId clockDomainId,
            TimelineEpoch epoch,
            GraphInstanceId graphInstanceId,
            out InstanceClock clock,
            out CoCoDiagnostic diagnostic) =>
            TryCreate(
                timelineId,
                clockDomainId,
                epoch,
                graphInstanceId,
                true,
                out clock,
                out diagnostic);

        private static bool TryCreate(
            TimelineId timelineId,
            ClockDomainId clockDomainId,
            TimelineEpoch epoch,
            GraphInstanceId graphInstanceId,
            bool requireGraphInstanceId,
            out InstanceClock clock,
            out CoCoDiagnostic diagnostic)
        {
            if (!timelineId.IsValid ||
                !clockDomainId.IsValid ||
                (requireGraphInstanceId && !graphInstanceId.IsValid))
            {
                clock = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    !timelineId.IsValid || (requireGraphInstanceId && !graphInstanceId.IsValid)
                        ? CoCoDiagnosticCode.InvalidIdentifier
                        : CoCoDiagnosticCode.InvalidClockDomain,
                    "Actor Clock requires a valid TimelineId and ClockDomainId.");
                return false;
            }

            clock = new InstanceClock(
                timelineId,
                clockDomainId,
                epoch,
                graphInstanceId,
                requireGraphInstanceId);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPreviewNext(
            double deltaTime,
            double instanceTimeScale,
            out TickFrame tickFrame,
            out CoCoDiagnostic diagnostic) =>
            TryPreviewNext(null, deltaTime, instanceTimeScale, out tickFrame, out diagnostic);

        internal bool TryPreviewNext(
            object runtimeOwner,
            double deltaTime,
            double instanceTimeScale,
            out TickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (_runtimeOwner != null && !ReferenceEquals(_runtimeOwner, runtimeOwner))
            {
                tickFrame = default;
                diagnostic = LifecycleError("A claimed Actor Clock can only be advanced by its owning Runtime.");
                return false;
            }

            if (_hasCandidate || _activeRestoreToken != 0UL)
            {
                tickFrame = default;
                diagnostic = LifecycleError(
                    "The staged Clock Tick or prepared restore must be resolved before previewing another Tick.");
                return false;
            }

            if (!IsFinite(deltaTime) || !IsFinite(instanceTimeScale))
            {
                tickFrame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonFiniteDeltaTime,
                    "DeltaTime and Actor TimeScale must be finite.");
                return false;
            }

            if (deltaTime <= 0d || instanceTimeScale <= 0d)
            {
                tickFrame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "DeltaTime and Actor TimeScale must be greater than zero; use Suspend for zero speed.");
                return false;
            }

            double scaledDelta = deltaTime * instanceTimeScale;
            double nextSeconds = _seconds + scaledDelta;
            if (!IsFinite(scaledDelta) || scaledDelta <= 0d || !IsFinite(nextSeconds))
            {
                tickFrame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonFiniteDeltaTime,
                    "Scaled DeltaTime and the resulting Actor time must remain finite.");
                return false;
            }

            tickFrame = default;
            diagnostic = CoCoDiagnostic.None;
            if (_tick == ulong.MaxValue || _executionSequence == ulong.MaxValue)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidTimelinePosition,
                    "Actor Clock cannot advance beyond its finite Tick range.");
                return false;
            }

            if (!TimelinePosition.TryCreate(nextSeconds, out TimelinePosition position) ||
                !TickFrame.TryCreate(
                    scaledDelta,
                    _timelineId,
                    position,
                    new TimelineTick(_tick + 1UL),
                    _clockDomainId,
                    new ExecutionSequence(_executionSequence + 1UL),
                    _epoch,
                    out tickFrame,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Time,
                        CoCoDiagnosticCode.InvalidTimelinePosition,
                        "Actor Clock cannot advance beyond its finite Tick range.");
                }

                return false;
            }

            return true;
        }

        internal bool TryClaimRuntimeOwner(object owner, GraphInstanceId graphInstanceId)
        {
            lock (this)
            {
                if (owner == null ||
                    !graphInstanceId.IsValid ||
                    _runtimeOwner != null ||
                    (_graphInstanceId.IsValid && _graphInstanceId != graphInstanceId) ||
                    _hasCandidate ||
                    _activeRestoreToken != 0UL ||
                    _seconds != 0d ||
                    _tick != 0UL ||
                    _executionSequence != 0UL)
                {
                    return false;
                }

                _runtimeOwner = owner;
                _graphInstanceId = graphInstanceId;
                return true;
            }
        }

        internal void ReleaseRuntimeOwner(object owner)
        {
            lock (this)
            {
                if (!ReferenceEquals(_runtimeOwner, owner))
                {
                    return;
                }

                _runtimeOwner = null;
                _restoreCandidate = default;
                _activeRestoreToken = 0UL;
                if (!_hasDeclaredGraphInstanceId)
                {
                    _graphInstanceId = default;
                }
            }
        }

        internal bool TryStage(object runtimeOwner, in TickFrame tickFrame)
        {
            if (!ReferenceEquals(_runtimeOwner, runtimeOwner) ||
                _hasCandidate ||
                _activeRestoreToken != 0UL ||
                !tickFrame.IsValid ||
                tickFrame.TimelineId != _timelineId ||
                tickFrame.ClockDomainId != _clockDomainId ||
                tickFrame.TimelineEpoch != _epoch ||
                tickFrame.Tick.Value != _tick + 1UL ||
                tickFrame.ExecutionSequence.Value != _executionSequence + 1UL ||
                !AreEqual(tickFrame.TimelinePosition.Seconds, _seconds + tickFrame.DeltaTime))
            {
                return false;
            }

            _candidate = tickFrame;
            _hasCandidate = true;
            return true;
        }

        internal bool IsCommitReady(object runtimeOwner, in TickFrame tickFrame) =>
            ReferenceEquals(_runtimeOwner, runtimeOwner) &&
            _hasCandidate &&
            _candidate == tickFrame;

        internal bool TryValidateRestore(
            object runtimeOwner,
            in TickFrame resumedTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!ReferenceEquals(_runtimeOwner, runtimeOwner) ||
                _hasCandidate ||
                _activeRestoreToken != 0UL)
            {
                diagnostic = LifecycleError(
                    "Actor Clock restore requires its owner at a resolved Tick boundary.");
                return false;
            }

            if (!resumedTickFrame.IsValid ||
                resumedTickFrame.TimelineId != _timelineId ||
                resumedTickFrame.ClockDomainId != _clockDomainId ||
                resumedTickFrame.TimelineEpoch.Value <= _epoch.Value ||
                resumedTickFrame.ExecutionSequence.Value <= _executionSequence)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Actor Clock restore requires the same Timeline and ClockDomain in a strictly newer Epoch and ExecutionSequence.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareRestore(
            object runtimeOwner,
            in TickFrame resumedTickFrame,
            out PreparedInstanceClockRestore prepared,
            out CoCoDiagnostic diagnostic)
        {
            prepared = default;
            if (!TryValidateRestore(runtimeOwner, resumedTickFrame, out diagnostic))
            {
                return false;
            }

            _restoreGeneration = _restoreGeneration == ulong.MaxValue
                ? 1UL
                : _restoreGeneration + 1UL;
            _activeRestoreToken = _restoreGeneration;
            _restoreCandidate = resumedTickFrame;
            prepared = new PreparedInstanceClockRestore(
                this,
                _activeRestoreToken,
                resumedTickFrame);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool IsPreparedRestoreTokenCurrent(ulong token) =>
            token != 0UL &&
            token == _activeRestoreToken &&
            _restoreCandidate.IsValid;

        internal void ApplyPreparedRestoreNoFail()
        {
            _epoch = _restoreCandidate.TimelineEpoch;
            _seconds = _restoreCandidate.TimelinePosition.Seconds;
            _tick = _restoreCandidate.Tick.Value;
            _executionSequence = _restoreCandidate.ExecutionSequence.Value;
            _restoreCandidate = default;
            _activeRestoreToken = 0UL;
        }

        internal bool CancelPreparedRestore(ulong token)
        {
            if (!IsPreparedRestoreTokenCurrent(token))
            {
                return false;
            }

            _restoreCandidate = default;
            _activeRestoreToken = 0UL;
            return true;
        }

        internal void CommitPreparedNoFail()
        {
            _seconds = _candidate.TimelinePosition.Seconds;
            _tick = _candidate.Tick.Value;
            _executionSequence = _candidate.ExecutionSequence.Value;
            _candidate = default;
            _hasCandidate = false;
        }

        internal void CommitPrepared(object runtimeOwner, in TickFrame tickFrame)
        {
            if (!IsCommitReady(runtimeOwner, tickFrame))
            {
                throw new InvalidOperationException(
                    "The staged Actor Clock token is no longer ready.");
            }

            CommitPreparedNoFail();
        }

        internal bool Commit(object runtimeOwner, in TickFrame tickFrame)
        {
            if (!IsCommitReady(runtimeOwner, tickFrame))
            {
                return false;
            }

            CommitPrepared(runtimeOwner, tickFrame);
            return true;
        }

        internal bool Cancel(object runtimeOwner, in TickFrame tickFrame)
        {
            if (!ReferenceEquals(_runtimeOwner, runtimeOwner) ||
                !_hasCandidate ||
                _candidate != tickFrame)
            {
                return false;
            }

            _candidate = default;
            _hasCandidate = false;
            return true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool AreEqual(double left, double right) =>
            Math.Abs(left - right) <= Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-12d;

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);
    }
}
