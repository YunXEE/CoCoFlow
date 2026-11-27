using System;
using System.Globalization;

// 资产保存状态类型的稳定描述符 ID，目录据此找到项目注册的 Logic 与配置。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在图目录中标识节点所用的 Logic 与配置合同。</summary>
    public readonly struct StateDescriptorId : IEquatable<StateDescriptorId>
    {

        #region Public API

        public ulong High { get; }
        public ulong Low { get; }
        public bool IsValid => High != 0UL || Low != 0UL;

        /// <summary>合并高低两段数值，两段均为零时输出默认标识并返回 false。</summary>
        public static bool TryCreate(ulong high, ulong low, out StateDescriptorId id)
        {
            if (high == 0UL && low == 0UL)
            {
                id = default;
                return false;
            }

            id = new StateDescriptorId(high, low);
            return true;
        }

        /// <summary>从十六进制文本读取标识的高低两段，格式错误或结果全零时返回 false。</summary>
        public static bool TryParse(string value, out StateDescriptorId id)
        {
            if (!GraphDescriptorIdParser.TryParse(value, out ulong high, out ulong low))
            {
                id = default;
                return false;
            }

            return TryCreate(high, low, out id);
        }

        /// <summary>按标识的高低两段数值比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateDescriptorId other) => High == other.High && Low == other.Low;
        /// <summary>仅当对象属于同类型并且标识的高低两段数值相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateDescriptorId other && Equals(other);
        /// <summary>为哈希集合计算与标识的高低两段数值的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((High.GetHashCode() * 397) ^ Low.GetHashCode());
        /// <summary>用固定的 32 个小写十六进制字符依次输出标识的高低两段。</summary>
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) +
                                             Low.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>比较两端的标识的高低两段数值，相等时返回 true。</summary>
        public static bool operator ==(StateDescriptorId left, StateDescriptorId right) =>
            left.Equals(right);

        /// <summary>比较两端的标识的高低两段数值，存在差异时返回 true。</summary>
        public static bool operator !=(StateDescriptorId left, StateDescriptorId right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        private StateDescriptorId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        #endregion
    }

    /// <summary>将描述符的外部文本表示还原成有效的稳定标识。</summary>
    internal static class GraphDescriptorIdParser
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
