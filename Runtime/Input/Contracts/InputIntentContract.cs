using System;

// InputReader 生成一次采样的当前动作、按序变化与游标代次，InputIntent 将这个值交给 State。
namespace CoCoFlow.Runtime.Input.Contracts
{
    /// <summary>交给玩法的一次输入采样，含有序变化、当前动作值和采样栅栏。</summary>
    public struct InputIntentContract
    {
        public FixedString64 ActiveMap;
        public ulong FenceGeneration { get; }
        public ReadOnlyMemory<RawInputRecord> Changes { get; }
        public ReadOnlyMemory<RawInputRecord> Current { get; }
        public int Count => Changes.Length + Current.Length;

        /// <summary>记录动作图名、变化记录、当前状态和栅栏代次，供该次采样的读者使用。</summary>
        public InputIntentContract(
            FixedString64 activeMap,
            ReadOnlyMemory<RawInputRecord> changes,
            ReadOnlyMemory<RawInputRecord> current = default,
            ulong fenceGeneration = 0)
        {
            ActiveMap = activeMap;
            Changes = changes;
            Current = current;
            FenceGeneration = fenceGeneration;
        }

        /// <summary>按输入变化的顺序读取一条记录，索引超出本次变化集合时返回 false。</summary>
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

        /// <summary>在本次变化中查找首条名称和阶段均匹配的动作，找不到时返回 false。</summary>
        public bool TryFind(
            string actionName,
            RawInputPhase phase,
            out RawInputRecord record) =>
            TryFind(FixedString64.FromString(actionName), phase, out record);

        /// <summary>在本次变化中查找首条名称和阶段均匹配的动作，找不到时返回 false。</summary>
        public bool TryFind(
            FixedString64 actionName,
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
