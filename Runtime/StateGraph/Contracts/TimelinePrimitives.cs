using System;
using System.Globalization;

// Host 为时钟域、时间线、执行顺序和分支代次分配身份。帧与恢复记录携带这些值。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>标识被步进或恢复的时间线。</summary>
    public readonly struct TimelineId : IEquatable<TimelineId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out TimelineId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new TimelineId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out TimelineId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TimelineId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TimelineId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(TimelineId left, TimelineId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(TimelineId left, TimelineId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private TimelineId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>区分驱动图实例的时钟域。</summary>
    public readonly struct ClockDomainId : IEquatable<ClockDomainId>
    {

        #region Public API

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>用非零数值创建标识，输入为零时输出默认值并返回 false。</summary>
        public static bool TryCreate(ulong value, out ClockDomainId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new ClockDomainId(value);
            return true;
        }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(ClockDomainId other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is ClockDomainId other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(ClockDomainId left, ClockDomainId right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(ClockDomainId left, ClockDomainId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private ClockDomainId(ulong value)
        {
            Value = value;
        }

        #endregion
    }

    /// <summary>标识实例执行次序，供帧头与事务检查先后。</summary>
    public readonly struct ExecutionSequence : IEquatable<ExecutionSequence>
    {
        /// <summary>记录本次执行的顺序值，供时间线位置相同的帧区分先后。</summary>
        public ExecutionSequence(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(ExecutionSequence other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is ExecutionSequence other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(ExecutionSequence left, ExecutionSequence right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(ExecutionSequence left, ExecutionSequence right) => !left.Equals(right);
    }

    /// <summary>区分时间回退前后的两段执行，供收件与恢复校验。</summary>
    public readonly struct TimelineEpoch : IEquatable<TimelineEpoch>
    {
        /// <summary>用给定代次区分时间线的不同执行分支。</summary>
        public TimelineEpoch(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TimelineEpoch other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TimelineEpoch other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(TimelineEpoch left, TimelineEpoch right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(TimelineEpoch left, TimelineEpoch right) => !left.Equals(right);
    }
}
