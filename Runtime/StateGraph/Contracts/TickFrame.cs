using System;
using CoCoFlow.Runtime.Core;

// 实例时钟生成一次步进的时间与身份，输入来源、State 和 Operator 共用同一个 Tick。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>描述一次实例步进的时间线、步号、执行顺序、时间和增量。</summary>
    public readonly struct TickFrame : IEquatable<TickFrame>
    {

        #region Public API

        public double DeltaTime { get; }
        public TimelineId TimelineId { get; }
        public TimelinePosition TimelinePosition { get; }
        public TimelineTick Tick { get; }
        public ClockDomainId ClockDomainId { get; }
        public ExecutionSequence ExecutionSequence { get; }
        public TimelineEpoch TimelineEpoch { get; }
        public bool IsValid => DeltaTime > 0d &&
                               !double.IsNaN(DeltaTime) &&
                               !double.IsInfinity(DeltaTime) &&
                               TimelineId.IsValid &&
                               TimelinePosition.IsValid &&
                               ClockDomainId.IsValid;

        /// <summary>用时钟域、时间线位置和正的有限步长构造一次步进，任一身份或时间无效时返回 false。</summary>
        public static bool TryCreate(
            double deltaTime,
            TimelineId timelineId,
            TimelinePosition timelinePosition,
            TimelineTick tick,
            ClockDomainId clockDomainId,
            ExecutionSequence executionSequence,
            TimelineEpoch timelineEpoch,
            out TickFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime))
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonFiniteDeltaTime,
                    "DeltaTime must be finite.");
                return false;
            }

            if (deltaTime <= 0d)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "DeltaTime must be greater than zero.");
                return false;
            }

            if (!timelineId.IsValid)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Identity,
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "TimelineId must be valid.");
                return false;
            }

            if (!timelinePosition.IsValid)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidTimelinePosition,
                    "TimelinePosition must be finite and non-negative.");
                return false;
            }

            if (!clockDomainId.IsValid)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidClockDomain,
                    "ClockDomainId must be non-zero.");
                return false;
            }

            frame = new TickFrame(
                deltaTime,
                timelineId,
                timelinePosition,
                tick,
                clockDomainId,
                executionSequence,
                timelineEpoch);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>按<c>DeltaTime</c>、<c>TimelineId</c>、<c>TimelinePosition</c>、<c>Tick</c>、<c>ClockDomainId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TickFrame other)
        {
            return DeltaTime.Equals(other.DeltaTime) &&
                   TimelineId == other.TimelineId &&
                   TimelinePosition == other.TimelinePosition &&
                   Tick == other.Tick &&
                   ClockDomainId == other.ClockDomainId &&
                   ExecutionSequence == other.ExecutionSequence &&
                   TimelineEpoch == other.TimelineEpoch;
        }

        /// <summary>仅当对象属于同类型并且<c>DeltaTime</c>、<c>TimelineId</c>、<c>TimelinePosition</c>、<c>Tick</c>、<c>ClockDomainId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TickFrame other && Equals(other);

        /// <summary>为哈希集合计算与<c>DeltaTime</c>、<c>TimelineId</c>、<c>TimelinePosition</c>、<c>Tick</c>、<c>ClockDomainId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DeltaTime.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineId.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelinePosition.GetHashCode();
                hashCode = (hashCode * 397) ^ Tick.GetHashCode();
                hashCode = (hashCode * 397) ^ ClockDomainId.GetHashCode();
                hashCode = (hashCode * 397) ^ ExecutionSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineEpoch.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>DeltaTime</c>、<c>TimelineId</c>、<c>TimelinePosition</c>、<c>Tick</c>、<c>ClockDomainId</c>，相等时返回 true。</summary>
        public static bool operator ==(TickFrame left, TickFrame right) => left.Equals(right);
        /// <summary>比较两端的<c>DeltaTime</c>、<c>TimelineId</c>、<c>TimelinePosition</c>、<c>Tick</c>、<c>ClockDomainId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(TickFrame left, TickFrame right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private TickFrame(
            double deltaTime,
            TimelineId timelineId,
            TimelinePosition timelinePosition,
            TimelineTick tick,
            ClockDomainId clockDomainId,
            ExecutionSequence executionSequence,
            TimelineEpoch timelineEpoch)
        {
            DeltaTime = deltaTime;
            TimelineId = timelineId;
            TimelinePosition = timelinePosition;
            Tick = tick;
            ClockDomainId = clockDomainId;
            ExecutionSequence = executionSequence;
            TimelineEpoch = timelineEpoch;
        }

        #endregion
    }
}
