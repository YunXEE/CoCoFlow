using System;
using System.Globalization;

// 图目录用这些稳定标识关联 Intent、Operation、Operator、占用、数据块、槽与编解码器。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>标识一份固定的帧布局，供句柄和保存数据匹配。</summary>
    public readonly struct FrameLayoutId : IEquatable<FrameLayoutId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out FrameLayoutId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new FrameLayoutId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out FrameLayoutId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(FrameLayoutId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is FrameLayoutId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(FrameLayoutId left, FrameLayoutId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(FrameLayoutId left, FrameLayoutId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private FrameLayoutId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在图的输出目录中标识一份操作合同。</summary>
    public readonly struct OperationId : IEquatable<OperationId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out OperationId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new OperationId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out OperationId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperationId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is OperationId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(OperationId left, OperationId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(OperationId left, OperationId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperationId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>标识负责消费输出的执行者，供装配和占用匹配。</summary>
    public readonly struct OperatorId : IEquatable<OperatorId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out OperatorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new OperatorId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out OperatorId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperatorId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is OperatorId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(OperatorId left, OperatorId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(OperatorId left, OperatorId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperatorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在 Operator 之间标识可申请占用的同一资源。</summary>
    public readonly struct OperatorClaimId : IEquatable<OperatorClaimId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out OperatorClaimId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new OperatorClaimId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out OperatorClaimId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperatorClaimId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is OperatorClaimId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(OperatorClaimId left, OperatorClaimId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(OperatorClaimId left, OperatorClaimId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperatorClaimId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在输入目录与帧布局中标识一个输入槽的合同。</summary>
    public readonly struct IntentId : IEquatable<IntentId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out IntentId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new IntentId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out IntentId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(IntentId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is IntentId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(IntentId left, IntentId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(IntentId left, IntentId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private IntentId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>标识 Context 布局中一组同属一个写入者的槽。</summary>
    public readonly struct StateBlockId : IEquatable<StateBlockId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out StateBlockId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateBlockId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out StateBlockId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateBlockId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateBlockId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(StateBlockId left, StateBlockId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(StateBlockId left, StateBlockId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private StateBlockId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在 Context 布局和恢复记录中定位一个槽。</summary>
    public readonly struct StateSlotId : IEquatable<StateSlotId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out StateSlotId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateSlotId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out StateSlotId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateSlotId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateSlotId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(StateSlotId left, StateSlotId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(StateSlotId left, StateSlotId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private StateSlotId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>标识持久化某一槽值所用的编码合同。</summary>
    public readonly struct CodecId : IEquatable<CodecId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out CodecId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new CodecId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out CodecId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(CodecId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is CodecId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(CodecId left, CodecId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(CodecId left, CodecId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private CodecId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }
}
