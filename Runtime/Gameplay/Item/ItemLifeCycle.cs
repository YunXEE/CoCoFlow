using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Item
{
    public class ItemLifeCycle : MonoBehaviour
    {
        [Header("Context")]
        [CoCoContextProvider(typeof(ItemContext))]
        [SerializeField] private MonoBehaviour contextProvider;

        [Header("Events")]
        [SerializeField] private string actorId = string.Empty;
        [SerializeField] private bool publishEvents = true;
        [SerializeField] private bool reliableEvents = true;

        private ItemContext _context;

        public ItemContext Context => ResolveContext();
        public ItemSemanticState ItemState => Context?.ItemState ?? ItemSemanticState.Inactive;
        public bool IsConsumed => Context?.Lifecycle.State == CoCoLifecycleState.Consumed;

        public event Action<ItemSemanticState> OnStateChanged;
        public event Action<ItemContext> OnOpened;
        public event Action<ItemContext> OnConsumed;

        #region Public API

        public void SetInactive()
        {
            var targetContext = Context;
            if (targetContext == null) return;

            ApplyState(targetContext, ItemSemanticState.Inactive, targetContext.SetInactive);
        }

        public void SetAvailable()
        {
            var targetContext = Context;
            if (targetContext == null) return;

            ApplyState(targetContext, ItemSemanticState.Available, targetContext.SetAvailable);
        }

        public void SetLocked()
        {
            var targetContext = Context;
            if (targetContext == null) return;

            ApplyState(targetContext, ItemSemanticState.Locked, targetContext.SetLocked);
        }

        public void SetOpening()
        {
            var targetContext = Context;
            if (targetContext == null) return;

            ApplyState(targetContext, ItemSemanticState.Opening, targetContext.SetOpening);
        }

        public void SetOpened(string sourceActorId = "")
        {
            var targetContext = Context;
            if (targetContext == null) return;

            bool wasOpened = targetContext.ItemState == ItemSemanticState.Opened;
            string resolvedActorId = ResolveActorId(targetContext, sourceActorId);
            ApplyState(targetContext, ItemSemanticState.Opened, targetContext.SetOpened);
            targetContext.Intent.Clear();

            if (!wasOpened)
            {
                PublishOpened(targetContext, resolvedActorId);
                OnOpened?.Invoke(targetContext);
            }
        }

        public void SetConsumed(string sourceActorId = "")
        {
            var targetContext = Context;
            if (targetContext == null) return;

            bool wasConsumed = targetContext.ItemState == ItemSemanticState.Consumed;
            string resolvedActorId = ResolveActorId(targetContext, sourceActorId);
            ApplyState(targetContext, ItemSemanticState.Consumed, targetContext.SetConsumed);
            targetContext.Intent.Clear();

            if (!wasConsumed)
            {
                PublishConsumed(targetContext, resolvedActorId);
                OnConsumed?.Invoke(targetContext);
            }
        }

        public void SetContextProvider(MonoBehaviour provider)
        {
            if (ReferenceEquals(provider, this))
            {
                provider = null;
            }

            contextProvider = provider;
            _context = null;
        }

        public void ResetContextCache()
        {
            _context = null;
        }

        public void SetActorId(string nextActorId)
        {
            actorId = nextActorId;
        }

        public void SetPublishEvents(bool shouldPublish)
        {
            publishEvents = shouldPublish;
        }

        public void SetReliableEvents(bool reliable)
        {
            reliableEvents = reliable;
        }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            ResolveContext();
        }

        private void OnValidate()
        {
            if (ReferenceEquals(contextProvider, this))
            {
                contextProvider = null;
            }
        }

        private void Reset()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (behaviour is ICoCoContextProvider<ItemContext>)
                {
                    contextProvider = behaviour;
                    break;
                }
            }
        }

        private void ApplyState(
            ItemContext targetContext,
            ItemSemanticState nextState,
            Action apply)
        {
            var previousState = targetContext.ItemState;
            apply?.Invoke();

            if (previousState != nextState)
            {
                OnStateChanged?.Invoke(nextState);
            }
        }

        private void PublishOpened(ItemContext targetContext, string sourceActorId)
        {
            if (!publishEvents) return;

            int sequence = targetContext.NextEventSequence();
            var itemOpened = new ItemOpenedEvent
            {
                Context = targetContext,
                ItemId = ResolveItemId(targetContext),
                EventSequence = sequence
            };
            var envelope = CoCoEventEnvelope.Create(
                "Item.Opened",
                ResolveActorId(targetContext, sourceActorId),
                sequence,
                Time.frameCount,
                reliableEvents,
                ResolveTargetEntityId(targetContext),
                nameof(ItemOpenedEvent),
                itemOpened.ItemId);

            CoCoEventBus.PublishWithEnvelope(ref itemOpened, ref envelope);
        }

        private void PublishConsumed(ItemContext targetContext, string sourceActorId)
        {
            if (!publishEvents) return;

            int sequence = targetContext.NextEventSequence();
            var itemConsumed = new ItemConsumedEvent
            {
                Context = targetContext,
                ItemId = ResolveItemId(targetContext),
                EventSequence = sequence
            };
            var envelope = CoCoEventEnvelope.Create(
                "Item.Consumed",
                ResolveActorId(targetContext, sourceActorId),
                sequence,
                Time.frameCount,
                reliableEvents,
                ResolveTargetEntityId(targetContext),
                nameof(ItemConsumedEvent),
                itemConsumed.ItemId);

            CoCoEventBus.PublishWithEnvelope(ref itemConsumed, ref envelope);
        }

        private ItemContext ResolveContext()
        {
            if (_context != null) return _context;

            if (TryGetContextFromProvider(contextProvider, out _context))
            {
                return _context;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _context))
                {
                    if (contextProvider == null)
                    {
                        contextProvider = behaviour;
                    }
                    return _context;
                }
            }

            return null;
        }

        private string ResolveActorId(ItemContext targetContext, string sourceActorId)
        {
            if (!string.IsNullOrEmpty(sourceActorId)) return sourceActorId;
            if (!string.IsNullOrEmpty(actorId)) return actorId;
            if (!string.IsNullOrEmpty(targetContext.Intent.actorId)) return targetContext.Intent.actorId;
            if (!string.IsNullOrEmpty(targetContext.Identity.StableEntityId))
            {
                return targetContext.Identity.StableEntityId;
            }

            return gameObject != null ? gameObject.name : nameof(ItemLifeCycle);
        }

        private static string ResolveTargetEntityId(ItemContext targetContext)
        {
            return targetContext.Identity.StableEntityId;
        }

        private static string ResolveItemId(ItemContext targetContext)
        {
            return targetContext.Payload?.itemId ?? string.Empty;
        }

        private static bool TryGetContextFromProvider(
            object provider,
            out ItemContext targetContext)
        {
            if (provider is ICoCoContextProvider<ItemContext> typedProvider)
            {
                targetContext = typedProvider.Context;
                return targetContext != null;
            }

            targetContext = null;
            return false;
        }

        #endregion
    }
}
