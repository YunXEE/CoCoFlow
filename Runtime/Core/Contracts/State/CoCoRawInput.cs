using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Fixed-capacity 64-byte string for deterministic intent payloads.
    /// Engine-free by construction; assignment beyond capacity truncates.
    /// </summary>
    public struct CoCoFixedString64 : IEquatable<CoCoFixedString64>
    {
        public const int Capacity = 64;

        private byte _length;
        private unsafe fixed byte _bytes[Capacity];

        public int Length => _length;

        public static CoCoFixedString64 FromString(string value)
        {
            var result = default(CoCoFixedString64);
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

        public bool Equals(CoCoFixedString64 other)
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

        public override bool Equals(object obj) =>
            obj is CoCoFixedString64 other && Equals(other);

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

        public static bool operator ==(
            CoCoFixedString64 left,
            CoCoFixedString64 right) => left.Equals(right);

        public static bool operator !=(
            CoCoFixedString64 left,
            CoCoFixedString64 right) => !left.Equals(right);
    }

    /// <summary>
    /// Lifecycle phase of one raw input record inside a tick's intent.
    /// </summary>
    public enum RawInputPhase : byte
    {
        Started = 1,
        Performed = 2,
        Canceled = 3,
        Held = 4
    }

    /// <summary>
    /// One raw input fact: the action name as authored in the Input System
    /// asset, its current value, phase, and arrival sequence. Zero semantic
    /// interpretation — translation belongs to state logic.
    /// </summary>
    public struct RawInputRecord
    {
        public CoCoFixedString64 Action;
        public float ValueX;
        public float ValueY;
        public RawInputPhase Phase;
        public ulong Sequence;
    }

    /// <summary>
    /// The one intent per tick: the raw input stream frozen as-is. At most
    /// eight records in arrival order; a ninth same-tick record is dropped
    /// (documented capacity, no fallback). Continuous actions appear as one
    /// Held record per tick while actuated and stop appearing on release.
    /// </summary>
    public struct RawInputIntent
    {
        public const int RecordCapacity = 8;

        public CoCoFixedString64 ActiveMap;
        public int Count;

        public RawInputRecord Record0;
        public RawInputRecord Record1;
        public RawInputRecord Record2;
        public RawInputRecord Record3;
        public RawInputRecord Record4;
        public RawInputRecord Record5;
        public RawInputRecord Record6;
        public RawInputRecord Record7;

        public bool TryGet(int index, out RawInputRecord record)
        {
            if (index < 0 || index >= Count)
            {
                record = default;
                return false;
            }

            switch (index)
            {
                case 0: record = Record0; return true;
                case 1: record = Record1; return true;
                case 2: record = Record2; return true;
                case 3: record = Record3; return true;
                case 4: record = Record4; return true;
                case 5: record = Record5; return true;
                case 6: record = Record6; return true;
                case 7: record = Record7; return true;
                default: record = default; return false;
            }
        }

        public void Set(int index, in RawInputRecord record)
        {
            switch (index)
            {
                case 0: Record0 = record; break;
                case 1: Record1 = record; break;
                case 2: Record2 = record; break;
                case 3: Record3 = record; break;
                case 4: Record4 = record; break;
                case 5: Record5 = record; break;
                case 6: Record6 = record; break;
                case 7: Record7 = record; break;
            }
        }

        /// <summary>
        /// Finds the first record matching the action name and phase, in
        /// arrival order. Returns false when the action is not present this
        /// tick (for continuous actions: not actuated).
        /// </summary>
        public bool TryFind(
            string actionName,
            RawInputPhase phase,
            out RawInputRecord record) =>
            TryFind(CoCoFixedString64.FromString(actionName), phase, out record);

        public bool TryFind(
            CoCoFixedString64 actionName,
            RawInputPhase phase,
            out RawInputRecord record)
        {
            for (int index = 0; index < Count; index++)
            {
                if (TryGet(index, out RawInputRecord candidate) &&
                    candidate.Phase == phase &&
                    candidate.Action.Equals(actionName))
                {
                    record = candidate;
                    return true;
                }
            }

            record = default;
            return false;
        }
    }
}
