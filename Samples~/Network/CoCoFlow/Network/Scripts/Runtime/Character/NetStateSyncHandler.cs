using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using Fusion;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Character
{
    /// <summary>
    /// 跨网络同步 CoCoStateMachineController 的当前状态。
    /// 在 StateAuthority 端通过 SyncState 更新 StateId，其他端通过 OnChangedRender 回调自动切换状态。
    /// </summary>
    public class NetStateSyncHandler : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnStateChanged))]
        public int StateId { get; set; }

        private CoCoStateMachineController _stateMachine;

        // 类型哈希 ↔ Type 双向映射，确保跨网络的状态 ID 稳定
        private static readonly Dictionary<int, Type> IdToType = new();
        private static readonly Dictionary<Type, int> TypeToId = new();

        private bool _isInitialized;

        #region Unity + Fusion Lifecycle

        public override void Spawned()
        {
            _stateMachine = GetComponent<CoCoStateMachineController>();

            if (_stateMachine != null)
            {
                // 注册当前对象上所有状态类型到映射表
                RegisterAvailableStates();
                _isInitialized = true;

                if (Object.HasStateAuthority && _stateMachine.CurrentStateType != null)
                {
                    StateId = GetStateId(_stateMachine.CurrentStateType);
                }
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 同步指定状态类型到网络。仅在 StateAuthority 端生效。
        /// </summary>
        public void SyncState<T>() where T : CoCoStateMachineBase
        {
            if (!Object.HasStateAuthority || !_isInitialized) return;

            var stateType = typeof(T);
            var newId = GetStateId(stateType);
            StateId = newId;
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// StateId 变化时的回调。查找对应 Type，若与当前状态不同则执行切换。
        /// ChangeState 内部已处理同类型重复切换（silently ignored），此处做前置检查以优化。
        /// </summary>
        private void OnStateChanged()
        {
            if (_stateMachine == null || !_isInitialized) return;
            if (StateId == 0) return;

            if (IdToType.TryGetValue(StateId, out var targetType))
            {
                if (_stateMachine.CurrentStateType != targetType)
                {
                    InvokeChangeState(targetType);
                }
            }
        }

        /// <summary>
        /// 扫描子物体上的 CoCoStateMachineBase 组件，预注册类型到映射表。
        /// </summary>
        private void RegisterAvailableStates()
        {
            var states = GetComponentsInChildren<CoCoStateMachineBase>(true);
            foreach (var state in states)
            {
                if (state == null) continue;
                GetStateId(state.GetType());
            }
        }

        /// <summary>
        /// 获取类型的网络同步 ID。基于 FullName 的稳定哈希，避免 Type.GetHashCode() 跨进程不一致。
        /// </summary>
        private static int GetStateId(Type stateType)
        {
            if (TypeToId.TryGetValue(stateType, out var existingId))
                return existingId;

            var id = ComputeStableStateId(stateType);
            if (IdToType.TryGetValue(id, out var existingType) && existingType != stateType)
            {
                CoCoLog.Warning(
                    $"[NetStateSyncHandler] 状态 ID 冲突: {existingType.FullName} 与 {stateType.FullName} 均为 {id}");
            }

            TypeToId[stateType] = id;
            IdToType[id] = stateType;
            return id;
        }

        private static int ComputeStableStateId(Type stateType)
        {
            unchecked
            {
                const int fnvPrime = 16777619;

                var hash = unchecked((int)2166136261u);
                var name = stateType.FullName ?? stateType.Name;
                for (var i = 0; i < name.Length; i++)
                {
                    hash ^= name[i];
                    hash *= fnvPrime;
                }

                var id = hash & 0x7fffffff;
                return id == 0 ? 1 : id;
            }
        }

        /// <summary>
        /// 通过反射调用 ChangeState&lt;T&gt; 泛型方法，避免对每种状态类型的编译时依赖。
        /// </summary>
        private void InvokeChangeState(Type stateType)
        {
            var method = typeof(CoCoStateMachineController).GetMethod("ChangeState");
            if (method == null) return;

            var genericMethod = method.MakeGenericMethod(stateType);
            genericMethod.Invoke(_stateMachine, null);
        }

        #endregion
    }
}
