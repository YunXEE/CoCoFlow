using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>实体在 Host 中的生命周期阶段。</summary>
    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoLifecycleState")]
    public enum EntityLifecycleState
    {
        /// <summary>尚未建立实体生命周期。</summary>
        Uninitialized = 0,
        /// <summary>实体正在生成。</summary>
        Spawning = 1,
        /// <summary>实体处于可交互的活动阶段。</summary>
        Active = 2,
        /// <summary>实体已停用。</summary>
        Disabled = 3,
        /// <summary>实体已被消耗。</summary>
        Consumed = 4,
        /// <summary>实体正在退出。</summary>
        Despawning = 5,
        /// <summary>实体已销毁。</summary>
        Destroyed = 6
    }

    /// <summary>保存并校验实体的生命周期迁移。</summary>
    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoLifecycleContext")]
    [Serializable]
    public class LifecycleContext
    {
        #region Public API

        /// <summary>当前生命周期阶段。</summary>
        public EntityLifecycleState State => state;

        /// <summary>当前处于 Active 时为真。</summary>
        public bool IsAliveLike => state == EntityLifecycleState.Active;

        /// <summary>当前实体可按活动实体交互时为真。</summary>
        public bool IsInteractableLike => state == EntityLifecycleState.Active;

        /// <summary>实体已初始化且尚未销毁时为真。</summary>
        public bool IsPersistentLike => state != EntityLifecycleState.Uninitialized &&
                                        state != EntityLifecycleState.Destroyed;

        /// <summary>实体已消耗或已销毁时为真。</summary>
        public bool IsTerminal => state == EntityLifecycleState.Consumed ||
                                  state == EntityLifecycleState.Destroyed;

        /// <summary>按当前生命周期规则检查能否进入目标阶段。</summary>
        /// <param name="nextState">请求进入的目标阶段。</param>
        /// <returns>允许从当前阶段直接迁移到目标阶段时为真。</returns>
        public bool CanTransitionTo(EntityLifecycleState nextState)
        {
            if (state == nextState) return true;

            switch (state)
            {
                case EntityLifecycleState.Destroyed:
                    return false;
                case EntityLifecycleState.Despawning:
                    return nextState == EntityLifecycleState.Destroyed;
                case EntityLifecycleState.Consumed:
                    return nextState == EntityLifecycleState.Despawning ||
                           nextState == EntityLifecycleState.Destroyed;
                default:
                    return true;
            }
        }

        /// <summary>在允许迁移或目标等于当前阶段时设置目标阶段。</summary>
        /// <param name="nextState">请求进入的目标阶段。</param>
        /// <returns>成功进入目标阶段时为真，失败时保留原阶段。</returns>
        public bool TryTransitionTo(EntityLifecycleState nextState)
        {
            if (!CanTransitionTo(nextState)) return false;
            state = nextState;
            return true;
        }

        /// <summary>进入指定阶段，规则拒绝迁移时抛出异常。</summary>
        /// <param name="nextState">请求进入的目标阶段。</param>
        public void TransitionTo(EntityLifecycleState nextState)
        {
            if (!TryTransitionTo(nextState))
            {
                throw new InvalidOperationException(
                    $"Invalid lifecycle transition: {state} -> {nextState}");
            }
        }

        #endregion

        #region Internal Logic

        [SerializeField] private EntityLifecycleState state = EntityLifecycleState.Uninitialized;

        #endregion
    }
}
