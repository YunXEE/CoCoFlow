using System.Collections.Generic;

// 这个文件为需要订阅多个类型事件的对象管理回调。
// UI 和 StateGraphHost 可以经由代理向事件总线注册回调。代理持有这些回调，供使用方在结束订阅时一起退订。
namespace CoCoFlow.Runtime.Core
{
    /// <summary>集中持有和退订调用方注册的事件回调。</summary>
    public sealed class CoCoEventAgent
    {
        #region Public API

        /// <summary>订阅并持有事件回调，直到主动退订。</summary>
        /// <remarks>优先级越高越先执行，空回调会被忽略。</remarks>
        public void Subscribe<T>(CoCoEventCallback<T> callback, int priority = 0)
        {
            if (callback == null) return;
            var wrapper = new Wrapper<T>(callback);
            _wrappers.Add(wrapper);
            CoCoEventBus.Subscribe(wrapper, priority);
        }

        /// <summary>退订本代理注册的所有回调，并释放对它们的引用。</summary>
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

        /// <summary>统一不同事件类型的退订入口。</summary>
        private interface IAgentWrapper
        {
            void Unsubscribe();
        }

        /// <summary>把回调包装成事件总线的监听者。</summary>
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
