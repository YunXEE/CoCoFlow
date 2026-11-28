using System;
using System.Globalization;

// 时钟与历史记录用时间线和步号定位一次提交，恢复路径按这些值查找目标。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>记录时间线的位置序号，随提交或恢复写入帧头。</summary>
    public readonly struct TimelineTick : IEquatable<TimelineTick>
    {
        /// <summary>以给定的无符号序号表示一次时间线刻度，包括起点零。</summary>
        public TimelineTick(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TimelineTick other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TimelineTick other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(TimelineTick left, TimelineTick right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(TimelineTick left, TimelineTick right) => !left.Equals(right);
    }

    /// <summary>用时间线、代次和步号定位一个可比较的提交位置。</summary>
    public readonly struct TimelinePosition : IEquatable<TimelinePosition>
    {

        #region Public API

        public double Seconds { get; }
        public bool IsValid => Seconds >= 0d &&
                               !double.IsNaN(Seconds) &&
                               !double.IsInfinity(Seconds);

        /// <summary>将非负有限秒数转换为时间位置。负值、无穷和非数会返回 false。</summary>
        public static bool TryCreate(double seconds, out TimelinePosition position)
        {
            if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                position = new TimelinePosition(double.NaN);
                return false;
            }

            position = new TimelinePosition(seconds);
            return true;
        }

        /// <summary>按<c>Seconds</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TimelinePosition other) => Seconds.Equals(other.Seconds);
        /// <summary>仅当对象属于同类型并且<c>Seconds</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TimelinePosition other && Equals(other);
        /// <summary>为哈希集合计算与<c>Seconds</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Seconds.GetHashCode();

        /// <summary>比较两端的<c>Seconds</c>，相等时返回 true。</summary>
        public static bool operator ==(TimelinePosition left, TimelinePosition right) => left.Equals(right);
        /// <summary>比较两端的<c>Seconds</c>，存在差异时返回 true。</summary>
        public static bool operator !=(TimelinePosition left, TimelinePosition right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private TimelinePosition(double seconds)
        {
            Seconds = seconds;
        }

        #endregion
    }
}
