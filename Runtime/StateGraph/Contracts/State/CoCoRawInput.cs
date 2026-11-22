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
        /// <summary>Original Unity input time in seconds for a change record.</summary>
        public double InputTime;
    }

    /// <summary>One Tick's ordered input changes and sampled continuous state.</summary>
    public struct InputIntentData
    {
        public CoCoFixedString64 ActiveMap;
        public ulong FenceGeneration { get; }
        public ReadOnlyMemory<RawInputRecord> Changes { get; }
        public ReadOnlyMemory<RawInputRecord> Current { get; }
        public int Count => Changes.Length + Current.Length;

        /// <summary>
        /// The producer owns the backing storage and must keep it immutable after delivery.
        /// </summary>
        public InputIntentData(
            CoCoFixedString64 activeMap,
            ReadOnlyMemory<RawInputRecord> changes,
            ReadOnlyMemory<RawInputRecord> current = default,
            ulong fenceGeneration = 0)
        {
            ActiveMap = activeMap;
            Changes = changes;
            Current = current;
            FenceGeneration = fenceGeneration;
        }

        public bool TryGet(int index, out RawInputRecord record)
        {
            if (index < 0 || index >= Count)
            {
                record = default;
                return false;
            }

            record = index < Changes.Length
                ? Changes.Span[index]
                : Current.Span[index - Changes.Length];
            return true;
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
