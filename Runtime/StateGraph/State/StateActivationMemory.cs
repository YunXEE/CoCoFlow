using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 实例为每个激活分配候选和已提交的 Memory，图状态生产方通过绑定读取和恢复这些值。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>为一次激活承载候选与提交间复制的玩法数据，每个对象由一个实例持有。</summary>
    public abstract class StateActivationMemory
    {
        private object _runtimeOwner;

        internal bool TryClaimRuntimeOwner(object owner)
        {
            if (owner == null || _runtimeOwner != null)
            {
                return false;
            }

            _runtimeOwner = owner;
            return true;
        }

        internal void ReleaseRuntimeOwner(object owner)
        {
            if (ReferenceEquals(_runtimeOwner, owner))
            {
                _runtimeOwner = null;
            }
        }
    }

    /// <summary>为没有激活字段的状态提供固定指纹及空的恢复数据。</summary>
    public sealed class StatelessMemory : StateActivationMemory
    {
        public const ulong Fingerprint = 40503UL;
    }

    /// <summary>将无字段状态捕获为零，恢复时无需写入任何记忆字段。</summary>
    public sealed class StatelessMemoryBinding :
        IActivationMemoryStateBinding<StatelessMemory, int>
    {
        public ulong SemanticFingerprint => StatelessMemory.Fingerprint;

        /// <summary>将空记忆捕获为零，返回成功和空诊断。</summary>
        public bool TryCapture(
            StatelessMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            state = 0;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>无状态记忆无需回填字段，直接返回成功和空诊断。</summary>
        public bool TryPrepareRestore(
            in int state,
            StatelessMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }
}
