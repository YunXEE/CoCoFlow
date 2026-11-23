using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraphHost
{
    /// <summary>实体在 Host 中的生命周期阶段。</summary>
    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoLifecycleState")]
    public enum EntityLifecycleState
    {
        Uninitialized = 0,
        Spawning = 1,
        Active = 2,
        Disabled = 3,
        Consumed = 4,
        Despawning = 5,
        Destroyed = 6
    }

    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoLifecycleContext")]
    [Serializable]
    public class LifecycleContext
    {
        [SerializeField] private EntityLifecycleState state = EntityLifecycleState.Uninitialized;

        public EntityLifecycleState State => state;
        public bool IsAliveLike => state == EntityLifecycleState.Active;
        public bool IsInteractableLike => state == EntityLifecycleState.Active;

        public bool IsPersistentLike => state != EntityLifecycleState.Uninitialized &&
                                        state != EntityLifecycleState.Destroyed;

        public bool IsTerminal => state == EntityLifecycleState.Consumed ||
                                  state == EntityLifecycleState.Destroyed;

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

        public bool TryTransitionTo(EntityLifecycleState nextState)
        {
            if (!CanTransitionTo(nextState)) return false;
            state = nextState;
            return true;
        }

        public void TransitionTo(EntityLifecycleState nextState)
        {
            if (!TryTransitionTo(nextState))
            {
                throw new InvalidOperationException(
                    $"Invalid lifecycle transition: {state} -> {nextState}");
            }
        }
    }

    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoEntityIdentity")]
    [Serializable]
    public class EntityIdentity
    {
        [SerializeField] private string stableEntityId;
        [SerializeField] private string runtimeInstanceId;
        [SerializeField] private string ownerId;
        [SerializeField] private string entityTypeId;
        [SerializeField] private string prefabKey;

        public string StableEntityId
        {
            get => stableEntityId;
            set => stableEntityId = value;
        }

        public bool HasStableEntityId => !string.IsNullOrEmpty(stableEntityId);
        public bool HasRuntimeInstanceId => !string.IsNullOrEmpty(runtimeInstanceId);

        public string RuntimeInstanceId
        {
            get => runtimeInstanceId;
            set => runtimeInstanceId = value;
        }

        public string OwnerId
        {
            get => ownerId;
            set => ownerId = value;
        }

        public string EntityTypeId
        {
            get => entityTypeId;
            set => entityTypeId = value;
        }

        public string PrefabKey
        {
            get => prefabKey;
            set => prefabKey = value;
        }
    }

    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoEntityContext")]
    [Serializable]
    public class EntityContext
    {
        [SerializeField] private EntityIdentity identity = new EntityIdentity();
        [SerializeField] private LifecycleContext lifecycle = new LifecycleContext();
        [SerializeField] private int semanticStateId;
        [SerializeField] private int actionStateId;
        [SerializeField] private int lastEventSequence;

        public EntityIdentity Identity => identity;
        public LifecycleContext Lifecycle => lifecycle;

        public int SemanticStateId
        {
            get => semanticStateId;
            set => semanticStateId = value;
        }

        public int ActionStateId
        {
            get => actionStateId;
            set => actionStateId = value;
        }

        public int LastEventSequence
        {
            get => lastEventSequence;
            set => lastEventSequence = value;
        }

        public int NextEventSequence()
        {
            lastEventSequence++;
            return lastEventSequence;
        }
    }
}
