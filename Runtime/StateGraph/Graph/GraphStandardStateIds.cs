using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 标准 Provider 根据状态类型与节点标识派生稳定描述符和状态槽，供图与 Host 一致解析。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>将 State 的实际类型和声明名映射到跨资产稳定的描述符身份。</summary>
    public static class StandardDescriptors
    {
        private const ulong High = 0x434F434F53544154UL;

        private static readonly Dictionary<StateDescriptorId, Type> TableValue =
            new Dictionary<StateDescriptorId, Type>();

        public static IReadOnlyDictionary<StateDescriptorId, Type> Table => TableValue;

        #region Public API

        /// <summary>根据 Logic 的稳定身份和给定状态名派生描述符 ID，无效输入时返回 false。</summary>
        public static bool TryCreate(
            Type logicType,
            string name,
            out StateDescriptorId descriptorId)
        {
            ulong low = DeriveLow(name);
            if (!StateDescriptorId.TryCreate(High, low, out descriptorId))
            {
                return false;
            }

            TableValue[descriptorId] = logicType;
            return true;
        }

        #endregion

        #region Internal Logic

        internal static ulong DeriveLow(string name)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                for (int index = 0; index < name.Length; index++)
                {
                    hash ^= (byte)name[index];
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }

        #endregion
    }

    /// <summary>从状态描述符派生对应的图状态记录块与槽。</summary>
    public static class StandardGraphState
    {

        #region Public API

        /// <summary>由状态描述符派生其标准图状态块的稳定 ID，重复查询同一描述符返回相同结果。</summary>
        public static StateBlockId BlockFor(StateDescriptorId descriptorId)
        {
            if (Blocks.TryGetValue(descriptorId, out StateBlockId cached))
            {
                return cached;
            }

            ulong low = DeriveLow(descriptorId, BlockSalt);
            if (!StateBlockId.TryCreate(High, low, out StateBlockId block))
            {
                _ = StateBlockId.TryCreate(High, 40UL, out block);
            }

            Blocks[descriptorId] = block;
            return block;
        }

        /// <summary>由状态描述符派生其标准图状态槽的稳定 ID，供捕获和恢复定位同一槽。</summary>
        public static StateSlotId SlotFor(StateDescriptorId descriptorId)
        {
            if (Slots.TryGetValue(descriptorId, out StateSlotId cached))
            {
                return cached;
            }

            ulong low = DeriveLow(descriptorId, 0UL);
            if (!StateSlotId.TryCreate(High, low, out StateSlotId slot))
            {
                _ = StateSlotId.TryCreate(High, 41UL, out slot);
            }

            Slots[descriptorId] = slot;
            return slot;
        }

        #endregion

        #region Internal Logic

        private const ulong High = 0x434F434F53544154UL;
        private const ulong BlockSalt = 0x424C4F434BUL;

        private static readonly Dictionary<StateDescriptorId, StateBlockId>
            Blocks = new Dictionary<StateDescriptorId, StateBlockId>();
        private static readonly Dictionary<StateDescriptorId, StateSlotId>
            Slots = new Dictionary<StateDescriptorId, StateSlotId>();

        private static ulong DeriveLow(
            StateDescriptorId descriptorId,
            ulong salt)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                ulong value = descriptorId.High ^ descriptorId.Low ^ salt;
                for (int bit = 0; bit < 64; bit += 8)
                {
                    hash ^= (value >> bit) & 0xFFUL;
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }

        #endregion
    }
}
