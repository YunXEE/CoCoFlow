using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Enemy;
using CoCoFlow.Runtime.Gameplay.Enemy.States;

namespace CoCoFlow.Runtime.Modules.Network.Enemy
{
    /// <summary>
    /// 网络同步的 Enemy AI 控制器 —— State Authority (Host) 独占 AI 计算，
    /// 客户端仅接收 Position + StateId 同步并做平滑插值，完全禁用本地 AI 组件以节省 CPU。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class NetEnemyController : NetworkBehaviour
    {
        [Networked] public Vector3 NetworkPosition { get; set; }

        [Networked, OnChangedRender(nameof(OnStateChanged))]
        public int CurrentStateId { get; set; }

        private EnemyController _enemyController;
        private NavMeshAgent _agent;

        // 状态 ID ↔ Type 双向映射，用于网络同步 Enemy HFSM 顶层状态
        private static readonly Dictionary<int, Action<CoCoStateMachineController>> StateIdToAction = new()
        {
            { 0, sm => sm.ChangeState<EnemyStateFreeEngage>() },
            { 1, sm => sm.ChangeState<EnemyStateReturn>() },
            { 2, sm => sm.ChangeState<EnemyStateFixedRoutine>() },
        };

        private static readonly Dictionary<Type, int> StateTypeToId = new()
        {
            { typeof(EnemyStateFreeEngage), 0 },
            { typeof(EnemyStateReturn), 1 },
            { typeof(EnemyStateFixedRoutine), 2 },
        };

        #region Unity / Fusion Lifecycle

        public override void Spawned()
        {
            _enemyController = GetComponent<EnemyController>();
            _agent = _enemyController.Agent;

            if (HasStateAuthority)
                SetupAsHost();
            else
                SetupAsClient();
        }

        #endregion

        #region Network Callbacks

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            NetworkPosition = transform.position;

            var currentType = _enemyController.StateMachine.CurrentStateType;
            if (currentType != null && StateTypeToId.TryGetValue(currentType, out var stateId))
                CurrentStateId = stateId;
        }

        public override void Render()
        {
            if (HasStateAuthority) return;

            transform.position = Vector3.Lerp(
                transform.position,
                NetworkPosition,
                Time.deltaTime * 20f);
        }

        #endregion

        #region Internal Logic

        private void SetupAsHost()
        {
            // NavMeshAgent 仅作路径规划器，实际位置通过 [Networked] 同步
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _enemyController.enabled = true;
        }

        private void SetupAsClient()
        {
            _enemyController.enabled = false;
            _agent.enabled = false;
        }

        /// <summary>
        /// OnChangedRender 回调：客户端（及 Host 自身）收到状态 ID 变化后，
        /// 同步 Enemy HFSM。ChangeState 内部已有同类型守卫，此处的类型检查为防御性编程。
        /// </summary>
        private void OnStateChanged()
        {
            if (!StateIdToAction.TryGetValue(CurrentStateId, out var action)) return;

            // ChangeState 不可重入同类型，此处提前跳出避免无意义的 Exit/Enter 周期
            if (_enemyController.StateMachine.CurrentStateType == GetStateType(CurrentStateId)) return;

            action(_enemyController.StateMachine);
        }

        private static Type GetStateType(int stateId)
        {
            return stateId switch
            {
                0 => typeof(EnemyStateFreeEngage),
                1 => typeof(EnemyStateReturn),
                2 => typeof(EnemyStateFixedRoutine),
                _ => null,
            };
        }

        #endregion
    }
}
