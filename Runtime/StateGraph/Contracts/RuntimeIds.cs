using System;
using System.Globalization;

// Host、实例、Operator 与事件系统依靠这些值区分实例、激活、实体以及同一时间线上的发送顺序。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>区分使用同一资产的运行实例，用于帧、句柄和定向事件的归属检查。</summary>
    public readonly struct GraphInstanceId : IEquatable<GraphInstanceId>
    {

        #region Public API

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>用非零数值创建标识，输入为零时输出默认值并返回 false。</summary>
        public static bool TryCreate(ulong value, out GraphInstanceId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new GraphInstanceId(value);
            return true;
        }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(GraphInstanceId other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is GraphInstanceId other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(GraphInstanceId left, GraphInstanceId right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(GraphInstanceId left, GraphInstanceId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private GraphInstanceId(ulong value)
        {
            Value = value;
        }

        #endregion
    }

    /// <summary>区分同一节点的历次激活，用于记忆与占用的归属检查。</summary>
    public readonly struct ActivationId : IEquatable<ActivationId>
    {

        #region Public API

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>用非零数值创建标识，输入为零时输出默认值并返回 false。</summary>
        public static bool TryCreate(ulong value, out ActivationId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new ActivationId(value);
            return true;
        }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(ActivationId other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is ActivationId other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(ActivationId left, ActivationId right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(ActivationId left, ActivationId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private ActivationId(ulong value)
        {
            Value = value;
        }

        #endregion
    }

    /// <summary>在事件目录和投递信封中标识一种载荷合同。</summary>
    public readonly struct EventTypeId : IEquatable<EventTypeId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out EventTypeId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new EventTypeId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out EventTypeId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(EventTypeId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is EventTypeId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(EventTypeId left, EventTypeId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(EventTypeId left, EventTypeId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private EventTypeId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在保存与恢复间标识同一个项目实体。</summary>
    public readonly struct StableEntityId : IEquatable<StableEntityId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out StableEntityId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StableEntityId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out StableEntityId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StableEntityId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StableEntityId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(StableEntityId left, StableEntityId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(StableEntityId left, StableEntityId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private StableEntityId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在时间恢复数据中标识可重建的实体。</summary>
    public readonly struct TemporalEntityId : IEquatable<TemporalEntityId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out TemporalEntityId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new TemporalEntityId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out TemporalEntityId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TemporalEntityId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TemporalEntityId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(TemporalEntityId left, TemporalEntityId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(TemporalEntityId left, TemporalEntityId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private TemporalEntityId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>将有关联的事件或操作串联到同一记录。</summary>
    public readonly struct CorrelationId : IEquatable<CorrelationId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out CorrelationId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CorrelationId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out CorrelationId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(CorrelationId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is CorrelationId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(CorrelationId left, CorrelationId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(CorrelationId left, CorrelationId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private CorrelationId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>标识投递事件所属的域，供注册和过滤使用。</summary>
    public readonly struct EventDomainId : IEquatable<EventDomainId>
    {

        #region Public API

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>用非零数值创建标识，输入为零时输出默认值并返回 false。</summary>
        public static bool TryCreate(ulong value, out EventDomainId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new EventDomainId(value);
            return true;
        }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(EventDomainId other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is EventDomainId other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(EventDomainId left, EventDomainId right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(EventDomainId left, EventDomainId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private EventDomainId(ulong value)
        {
            Value = value;
        }

        #endregion
    }

    /// <summary>标记已接受的离散操作序号，供消费者区分历次触发。</summary>
    public readonly struct OperationSequence : IEquatable<OperationSequence>
    {

        #region Public API

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>用非零数值创建标识，输入为零时输出默认值并返回 false。</summary>
        public static bool TryCreate(ulong value, out OperationSequence sequence)
        {
            if (value == 0UL)
            {
                sequence = default;
                return false;
            }

            sequence = new OperationSequence(value);
            return true;
        }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationSequence other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is OperationSequence other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(OperationSequence left, OperationSequence right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(OperationSequence left, OperationSequence right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperationSequence(ulong value)
        {
            Value = value;
        }

        #endregion
    }

    /// <summary>标记事件在来源发送序列中的位置，用于排序与去重。</summary>
    public readonly struct EventSequence : IEquatable<EventSequence>
    {

        #region Public API

        public static EventSequence Zero => default;

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>用非零数值创建标识，输入为零时输出默认值并返回 false。</summary>
        public static bool TryCreate(ulong value, out EventSequence sequence)
        {
            if (value == 0UL)
            {
                sequence = default;
                return false;
            }

            sequence = new EventSequence(value);
            return true;
        }

        /// <summary>按标识的数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(EventSequence other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且标识的数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is EventSequence other && Equals(other);
        /// <summary>为哈希集合计算与标识的数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();
        /// <summary>用不依赖当前语言区域的格式输出此值，供日志和身份对照使用。</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的数值，相等时返回 true。</summary>
        public static bool operator ==(EventSequence left, EventSequence right) => left.Equals(right);
        /// <summary>比较两端的标识的数值，存在差异时返回 true。</summary>
        public static bool operator !=(EventSequence left, EventSequence right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private EventSequence(ulong value)
        {
            Value = value;
        }

        #endregion
    }
}
