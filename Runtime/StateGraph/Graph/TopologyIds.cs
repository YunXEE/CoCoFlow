using System;
using System.Globalization;

// 图资产与编译结果通过稳定的图、层、节点和出边 ID 相互定位，显示顺序可独立调整。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在资产、编译结果和恢复数据之间标识同一张图。</summary>
    public readonly struct GraphId : IEquatable<GraphId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out GraphId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new GraphId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out GraphId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(GraphId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is GraphId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(GraphId left, GraphId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(GraphId left, GraphId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private GraphId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在图资产和运行拓扑之间标识同一层。</summary>
    public readonly struct LayerId : IEquatable<LayerId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out LayerId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new LayerId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out LayerId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(LayerId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is LayerId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(LayerId left, LayerId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(LayerId left, LayerId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private LayerId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在层级、转换和恢复记录中标识同一节点。</summary>
    public readonly struct StateId : IEquatable<StateId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out StateId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out StateId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(StateId left, StateId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(StateId left, StateId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private StateId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>在图资产、仲裁和调试记录之间标识同一条边。</summary>
    public readonly struct TransitionId : IEquatable<TransitionId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out TransitionId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new TransitionId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out TransitionId id)
        {
            if (!Id128Parser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TransitionId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TransitionId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(TransitionId left, TransitionId right) => left.Equals(right);
        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(TransitionId left, TransitionId right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private TransitionId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>对图拓扑的 128 位文本标识执行格式与有效性检查。</summary>
    internal static class Id128Parser
    {
        public static bool TryParse(string value, out ulong high, out ulong low)
        {
            high = 0UL;
            low = 0UL;
            if (value == null || value.Length != 32)
            {
                return false;
            }

            return ulong.TryParse(
                       value.Substring(0, 16),
                       NumberStyles.AllowHexSpecifier,
                       CultureInfo.InvariantCulture,
                       out high) &&
                   ulong.TryParse(
                       value.Substring(16, 16),
                       NumberStyles.AllowHexSpecifier,
                       CultureInfo.InvariantCulture,
                       out low);
        }
    }
}
