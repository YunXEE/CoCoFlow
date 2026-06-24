using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public interface ICoCoContext { }

    public interface ICoCoContextProvider<out TContext> where TContext : class, ICoCoContext
    {
        TContext Context { get; }
    }

    public interface ICoCoContextFrameResolver
    {
        void ResolveContextFrame(ICoCoContext context);
    }

    public interface ICoCoIntent { }

    public interface ICoCoIntentSource<out TIntent> where TIntent : ICoCoIntent
    {
        TIntent Intent { get; }
    }

    public interface ICoCoStableEntityIdProvider
    {
        string StableEntityId { get; }
    }

    public enum CoCoLifecycleState
    {
        Uninitialized,
        Spawning,
        Active,
        Disabled,
        Consumed,
        Despawning,
        Destroyed
    }

    [Serializable]
    public class CoCoLifecycleContext
    {
        [SerializeField] private CoCoLifecycleState state = CoCoLifecycleState.Uninitialized;

        public CoCoLifecycleState State => state;
        public bool IsAliveLike => state == CoCoLifecycleState.Active;
        public bool IsInteractableLike => state == CoCoLifecycleState.Active;

        public bool IsPersistentLike => state != CoCoLifecycleState.Uninitialized &&
                                        state != CoCoLifecycleState.Destroyed;

        public bool IsTerminal => state == CoCoLifecycleState.Consumed ||
                                  state == CoCoLifecycleState.Destroyed;

        public bool CanTransitionTo(CoCoLifecycleState nextState)
        {
            if (state == nextState) return true;

            switch (state)
            {
                case CoCoLifecycleState.Destroyed:
                    return false;
                case CoCoLifecycleState.Despawning:
                    return nextState == CoCoLifecycleState.Destroyed;
                case CoCoLifecycleState.Consumed:
                    return nextState == CoCoLifecycleState.Despawning ||
                           nextState == CoCoLifecycleState.Destroyed;
                default:
                    return true;
            }
        }

        public bool TryTransitionTo(CoCoLifecycleState nextState)
        {
            if (!CanTransitionTo(nextState)) return false;
            state = nextState;
            return true;
        }

        public void TransitionTo(CoCoLifecycleState nextState)
        {
            if (!TryTransitionTo(nextState))
            {
                throw new InvalidOperationException(
                    $"Invalid lifecycle transition: {state} -> {nextState}");
            }
        }
    }

    [Serializable]
    public class CoCoEntityIdentity
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

    [Serializable]
    public class CoCoEntityContext : ICoCoContext
    {
        [SerializeField] private CoCoEntityIdentity identity = new CoCoEntityIdentity();
        [SerializeField] private CoCoLifecycleContext lifecycle = new CoCoLifecycleContext();
        [SerializeField] private int semanticStateId;
        [SerializeField] private int actionStateId;
        [SerializeField] private int lastEventSequence;

        public CoCoEntityIdentity Identity => identity;
        public CoCoLifecycleContext Lifecycle => lifecycle;

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
