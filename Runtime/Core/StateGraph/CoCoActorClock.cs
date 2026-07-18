using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Per-Actor transactional Clock. Preview is read-only; only a committed staged Tick advances time.
    /// </summary>
    public sealed class CoCoActorClock
    {
        private readonly CoCoTimelineId _timelineId;
        private readonly CoCoClockDomainId _clockDomainId;
        private readonly bool _hasDeclaredGraphInstanceId;
        private CoCoTimelineEpoch _epoch;
        private CoCoGraphInstanceId _graphInstanceId;
        private object _runtimeOwner;
        private CoCoTickFrame _candidate;
        private double _seconds;
        private ulong _tick;
        private ulong _executionSequence;
        private bool _hasCandidate;

        private CoCoActorClock(
            CoCoTimelineId timelineId,
            CoCoClockDomainId clockDomainId,
            CoCoTimelineEpoch epoch,
            CoCoGraphInstanceId graphInstanceId,
            bool hasDeclaredGraphInstanceId)
        {
            _timelineId = timelineId;
            _clockDomainId = clockDomainId;
            _epoch = epoch;
            _graphInstanceId = graphInstanceId;
            _hasDeclaredGraphInstanceId = hasDeclaredGraphInstanceId;
        }

        public CoCoTimelineId TimelineId => _timelineId;
        public CoCoClockDomainId ClockDomainId => _clockDomainId;
        public CoCoGraphInstanceId GraphInstanceId => _graphInstanceId;
        public CoCoTimelineEpoch TimelineEpoch => _epoch;
        public CoCoTimelineTick Tick => new CoCoTimelineTick(_tick);
        public CoCoExecutionSequence ExecutionSequence => new CoCoExecutionSequence(_executionSequence);
        public double Seconds => _seconds;
        public bool HasStagedTick => _hasCandidate;

        public static bool TryCreate(
            CoCoTimelineId timelineId,
            CoCoClockDomainId clockDomainId,
            CoCoTimelineEpoch epoch,
            out CoCoActorClock clock,
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
            CoCoTimelineId timelineId,
            CoCoClockDomainId clockDomainId,
            CoCoTimelineEpoch epoch,
            CoCoGraphInstanceId graphInstanceId,
            out CoCoActorClock clock,
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
            CoCoTimelineId timelineId,
            CoCoClockDomainId clockDomainId,
            CoCoTimelineEpoch epoch,
            CoCoGraphInstanceId graphInstanceId,
            bool requireGraphInstanceId,
            out CoCoActorClock clock,
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

            clock = new CoCoActorClock(
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
            double actorTimeScale,
            out CoCoTickFrame tickFrame,
            out CoCoDiagnostic diagnostic) =>
            TryPreviewNext(null, deltaTime, actorTimeScale, out tickFrame, out diagnostic);

        internal bool TryPreviewNext(
            object runtimeOwner,
            double deltaTime,
            double actorTimeScale,
            out CoCoTickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (_runtimeOwner != null && !ReferenceEquals(_runtimeOwner, runtimeOwner))
            {
                tickFrame = default;
                diagnostic = LifecycleError("A claimed Actor Clock can only be advanced by its owning Runtime.");
                return false;
            }

            if (_hasCandidate)
            {
                tickFrame = default;
                diagnostic = LifecycleError("The staged Clock Tick must be resolved before previewing another Tick.");
                return false;
            }

            if (!IsFinite(deltaTime) || !IsFinite(actorTimeScale))
            {
                tickFrame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonFiniteDeltaTime,
                    "DeltaTime and Actor TimeScale must be finite.");
                return false;
            }

            if (deltaTime <= 0d || actorTimeScale <= 0d)
            {
                tickFrame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "DeltaTime and Actor TimeScale must be greater than zero; use Suspend for zero speed.");
                return false;
            }

            double scaledDelta = deltaTime * actorTimeScale;
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

            if (!CoCoTimelinePosition.TryCreate(nextSeconds, out CoCoTimelinePosition position) ||
                !CoCoTickFrame.TryCreate(
                    scaledDelta,
                    _timelineId,
                    position,
                    new CoCoTimelineTick(_tick + 1UL),
                    _clockDomainId,
                    new CoCoExecutionSequence(_executionSequence + 1UL),
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

        internal bool TryClaimRuntimeOwner(object owner, CoCoGraphInstanceId graphInstanceId)
        {
            lock (this)
            {
                if (owner == null ||
                    !graphInstanceId.IsValid ||
                    _runtimeOwner != null ||
                    (_graphInstanceId.IsValid && _graphInstanceId != graphInstanceId) ||
                    _hasCandidate ||
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
                if (!_hasDeclaredGraphInstanceId)
                {
                    _graphInstanceId = default;
                }
            }
        }

        internal bool TryStage(object runtimeOwner, in CoCoTickFrame tickFrame)
        {
            if (!ReferenceEquals(_runtimeOwner, runtimeOwner) ||
                _hasCandidate ||
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

        internal bool IsCommitReady(object runtimeOwner, in CoCoTickFrame tickFrame) =>
            ReferenceEquals(_runtimeOwner, runtimeOwner) &&
            _hasCandidate &&
            _candidate == tickFrame;

        internal void CommitPreparedNoFail()
        {
            _seconds = _candidate.TimelinePosition.Seconds;
            _tick = _candidate.Tick.Value;
            _executionSequence = _candidate.ExecutionSequence.Value;
            _candidate = default;
            _hasCandidate = false;
        }

        internal void CommitPrepared(object runtimeOwner, in CoCoTickFrame tickFrame)
        {
            if (!IsCommitReady(runtimeOwner, tickFrame))
            {
                throw new InvalidOperationException(
                    "The staged Actor Clock token is no longer ready.");
            }

            CommitPreparedNoFail();
        }

        internal bool Commit(object runtimeOwner, in CoCoTickFrame tickFrame)
        {
            if (!IsCommitReady(runtimeOwner, tickFrame))
            {
                return false;
            }

            CommitPrepared(runtimeOwner, tickFrame);
            return true;
        }

        internal bool Cancel(object runtimeOwner, in CoCoTickFrame tickFrame)
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
