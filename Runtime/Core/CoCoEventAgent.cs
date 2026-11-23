using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>Owns callback subscriptions for the lifetime of its consumer.</summary>
    public sealed class CoCoEventAgent
    {
        #region Public API

        /// <summary>Subscribes a callback and holds it until this agent unsubscribes.</summary>
        /// <typeparam name="T">The exact event type.</typeparam>
        /// <param name="callback">The callback to invoke by reference. Null is ignored.</param>
        /// <param name="priority">Higher priorities run first.</param>
        public void Subscribe<T>(CoCoEventCallback<T> callback, int priority = 0)
        {
            if (callback == null) return;
            var wrapper = new Wrapper<T>(callback);
            _wrappers.Add(wrapper);
            CoCoEventBus.Subscribe(wrapper, priority);
        }

        /// <summary>Removes all callbacks registered by this agent and releases their references.</summary>
        public void UnsubscribeAll()
        {
            foreach (var wrapper in _wrappers)
            {
                wrapper.Unsubscribe();
            }
            _wrappers.Clear();
        }

        #endregion

        #region Internal Logic

        /// <summary>Allows the agent to unsubscribe listeners of different event types.</summary>
        private interface IAgentWrapper
        {
            void Unsubscribe();
        }

        /// <summary>Adapts a callback to a bus listener until it is unsubscribed.</summary>
        private class Wrapper<T> : ICoCoEventListener<T>, IAgentWrapper
        {
            private CoCoEventCallback<T> _callback;

            public Wrapper(CoCoEventCallback<T> callback)
            {
                _callback = callback;
            }

            public void OnEvent(ref T eventData)
            {
                _callback?.Invoke(ref eventData);
            }

            public void Unsubscribe()
            {
                CoCoEventBus.Unsubscribe(this);
                _callback = null;
            }
        }

        private readonly List<IAgentWrapper> _wrappers = new List<IAgentWrapper>();

        #endregion
    }
}
