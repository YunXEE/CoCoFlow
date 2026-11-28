using System;

// InputReader 将动作名、阶段、数值、时间和序号写入定长记录，玩法按动作筛选同一步的变更。
namespace CoCoFlow.Runtime.Input.Contracts
{
    /// <summary>用定长的 64 字节保存动作名称，供输入采样、过滤和查找共用。</summary>
    public struct FixedString64 : IEquatable<FixedString64>
    {
        public const int Capacity = 64;

        private byte _length;
        private unsafe fixed byte _bytes[Capacity];

        public int Length => _length;

        #region Public API

        /// <summary>将字符串前 64 个字符的低八位复制为固定容量的名称，空值和空串产生空名。</summary>
        public static FixedString64 FromString(string value)
        {
            var result = default(FixedString64);
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }

            int count = Math.Min(value.Length, Capacity);
            unsafe
            {
                for (int index = 0; index < count; index++)
                {
                    result._bytes[index] = (byte)value[index];
                }
            }

            result._length = (byte)count;
            return result;
        }

        /// <summary>将已存储的每个字节还原为一个字符，长度为零时返回空字符串。</summary>
        public override string ToString()
        {
            if (_length == 0)
            {
                return string.Empty;
            }

            char[] chars = new char[_length];
            unsafe
            {
                fixed (byte* bytes = _bytes)
                {
                    for (int index = 0; index < _length; index++)
                    {
                        chars[index] = (char)bytes[index];
                    }
                }
            }

            return new string(chars);
        }

        /// <summary>读取名称中指定位置的字节。负索引或越界时输出零并返回 false。</summary>
        public bool TryGetByte(int index, out byte value)
        {
            if (index < 0 || index >= _length)
            {
                value = 0;
                return false;
            }

            unsafe
            {
                value = _bytes[index];
            }

            return true;
        }

        /// <summary>按名称长度和存储的字节比较两个值，全部相等时返回 true。</summary>
        public bool Equals(FixedString64 other)
        {
            if (_length != other._length)
            {
                return false;
            }

            unsafe
            {
                for (int index = 0; index < _length; index++)
                {
                    if (_bytes[index] != other._bytes[index])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>仅当对象属于同类型并且名称长度和存储的字节相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is FixedString64 other && Equals(other);

        /// <summary>为哈希集合计算与名称长度和存储的字节的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _length;
                unsafe
                {
                    for (int index = 0; index < _length; index++)
                    {
                        hash = (hash * 31) ^ _bytes[index];
                    }
                }

                return hash;
            }
        }

        /// <summary>比较两端的名称长度和存储的字节，相等时返回 true。</summary>
        public static bool operator ==(
            FixedString64 left,
            FixedString64 right) => left.Equals(right);

        /// <summary>比较两端的名称长度和存储的字节，存在差异时返回 true。</summary>
        public static bool operator !=(
            FixedString64 left,
            FixedString64 right) => !left.Equals(right);

        #endregion
    }

    /// <summary>区分一条输入记录对应的动作开始、执行和取消。</summary>
    public enum RawInputPhase : byte
    {
        Started = 1,
        Performed = 2,
        Canceled = 3,
        Held = 4
    }

    /// <summary>保存单次输入动作的顺序、名称、阶段、数值与采样时间。</summary>
    public struct RawInputRecord
    {
        public FixedString64 Action;
        public float ValueX;
        public float ValueY;
        public RawInputPhase Phase;
        public ulong Sequence;
                public double InputTime;
    }
}
