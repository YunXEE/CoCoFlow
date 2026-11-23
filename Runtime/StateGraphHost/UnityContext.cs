using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraphHost
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ContextProviderAttribute : PropertyAttribute
    {
        public ContextProviderAttribute() { }

        public ContextProviderAttribute(Type requiredContextType)
        {
            RequiredContextType = requiredContextType;
        }

        public Type RequiredContextType { get; }
    }

    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoLifecycleContext")]
    [Serializable]
    public class LifecycleContext
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
    public class EntityContext : ICoCoContext
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
