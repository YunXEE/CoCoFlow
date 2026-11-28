using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 标准绑定把带 Memory 标记的字段映射为激活值，实例负责在候选、提交与恢复之间转移字段。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>把激活记忆与对应 Logic 的已标记字段相互复制。</summary>
    internal interface IFieldMemory
    {
        void Load(StateLogic logic);
        void Store(StateLogic logic);
    }

    /// <summary>按记忆字段的实际布局在 Logic 和预分配的值之间转移数据。</summary>
    /// <param name="logic">提供或接收字段值的状态逻辑。</param>
    /// <param name="state">反映逻辑字段的记忆值。</param>
    /// <param name="load">为真时把记忆字段装入逻辑，为假时保存逻辑字段。</param>
    internal delegate void FieldMemoryTransfer<TState>(StateLogic logic, ref TState state, bool load);

    /// <summary>为声明了记忆字段的状态保存一次激活的字段副本。</summary>
    internal sealed class FieldMemory<TLogic, TState> : StateActivationMemory, IFieldMemory
        where TLogic : StateLogic, new()
        where TState : unmanaged
    {
        internal static readonly FieldInfo[] Fields = StateFields.Fields(typeof(TLogic), typeof(StateMemoryAttribute));
        internal static readonly TState Initial = (TState)PackValue(new TLogic(), typeof(TState), 0);
        internal static FieldMemoryTransfer<TState> Transfer;
        internal TState Value = Initial;

        private static object PackValue(TLogic logic, Type type, int index) =>
            index == Fields.Length ? (object)(byte)0 :
            Activator.CreateInstance(type, Fields[index].GetValue(logic),
                PackValue(logic, type.GetField("Item2").FieldType, index + 1));

        public void Load(StateLogic logic) => Transfer(logic, ref Value, true);
        public void Store(StateLogic logic) => Transfer(logic, ref Value, false);

        internal static ulong Fingerprint(FieldMemory<TLogic, TState> memory)
        {
            ulong hash = FrozenConfigHash.OffsetBasis;
            foreach (var field in Fields)
                FrozenConfigHash.AddScalar(ref hash, typeof(string), field.Name);
            foreach (byte value in MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref memory.Value, 1)))
                FrozenConfigHash.Add(ref hash, value);
            return FrozenConfigHash.Complete(hash);
        }

        /// <summary>在字段记忆和可持久化的图状态值之间捕获或还原。</summary>
        internal sealed class Binding : IActivationMemoryStateBinding<FieldMemory<TLogic, TState>, TState>
        {
            public ulong SemanticFingerprint => Fingerprint(new FieldMemory<TLogic, TState>());
            public bool TryCapture(FieldMemory<TLogic, TState> memory, out TState state, out CoCoDiagnostic diagnostic)
            {
                state = memory.Value;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryPrepareRestore(in TState state, FieldMemory<TLogic, TState> memory, out CoCoDiagnostic diagnostic)
            {
                memory.Value = state;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
