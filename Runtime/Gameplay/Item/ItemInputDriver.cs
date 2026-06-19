using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Item
{
    public class ItemInputDriver : MonoBehaviour
    {
        [Header("Context")]
        [SerializeField] private MonoBehaviour contextProvider;

        [Header("Intent Source")]
        [SerializeField] private MonoBehaviour itemIntentSource;
        [SerializeField] private bool updateAutomatically = true;

        private ItemContext _context;
        private ICoCoIntentSource<ItemIntent> _itemIntentSource;

        #region Public API

        public void RequestOpen(string actorId = "")
        {
            var targetContext = Context;
            if (targetContext == null) return;

            targetContext.Intent.openRequested = true;
            targetContext.Intent.actorId = actorId;
        }

        public void RequestUnlock(string actorId = "")
        {
            var targetContext = Context;
            if (targetContext == null) return;

            targetContext.Intent.unlockRequested = true;
            targetContext.Intent.actorId = actorId;
        }

        public void RequestUse(string actorId = "")
        {
            var targetContext = Context;
            if (targetContext == null) return;

            targetContext.Intent.useRequested = true;
            targetContext.Intent.actorId = actorId;
        }

        public void ClearIntent()
        {
            Context?.Intent.Clear();
        }

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _context = null;
        }

        public void SetItemIntentSource(MonoBehaviour source)
        {
            itemIntentSource = source;
            _itemIntentSource = null;
        }

        #endregion

        #region Internal Logic

        private ItemContext Context => ResolveContext();
        private ICoCoIntentSource<ItemIntent> ItemIntentSource => ResolveItemIntentSource();

        private void Awake()
        {
            ResolveContext();
            ResolveItemIntentSource();
        }

        private void Update()
        {
            if (updateAutomatically)
            {
                SampleInput();
            }
        }

        private bool SampleInput()
        {
            var targetContext = Context;
            var source = ItemIntentSource;
            if (targetContext == null || source?.Intent == null) return false;

            ApplyItemIntent(source.Intent);
            return true;
        }

        private void ApplyItemIntent(ItemIntent itemIntent)
        {
            var targetContext = Context;
            if (targetContext == null || itemIntent == null) return;

            targetContext.Intent.CopyFrom(itemIntent);
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

        private ICoCoIntentSource<ItemIntent> ResolveItemIntentSource()
        {
            if (_itemIntentSource != null) return _itemIntentSource;

            if (itemIntentSource is ICoCoIntentSource<ItemIntent> explicitSource)
            {
                _itemIntentSource = explicitSource;
                return _itemIntentSource;
            }

            return null;
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

        private void OnValidate()
        {
            if (ReferenceEquals(contextProvider, this))
            {
                contextProvider = null;
            }

            if (ReferenceEquals(itemIntentSource, this))
            {
                itemIntentSource = null;
            }
        }

        private void Reset()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;

                if (contextProvider == null &&
                    behaviour is ICoCoContextProvider<ItemContext>)
                {
                    contextProvider = behaviour;
                }

                if (itemIntentSource == null &&
                    behaviour is ICoCoIntentSource<ItemIntent>)
                {
                    itemIntentSource = behaviour;
                }
            }
        }

        #endregion
    }
}
