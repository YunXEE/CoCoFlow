using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    #region Public Types

    /// <summary>
    /// 事件回调委托。使用 ref 传递结构体避免装箱，零 GC。
    /// </summary>
    public delegate void EventCallback<T>(ref T eventData);

    /// <summary>
    /// 可取消事件接口。实现此接口的事件可在广播链中被中途拦截。
    /// </summary>
    public interface ICancellableEvent
    {
        bool IsCancelled { get; set; }
    }

    public interface IEventListener<T>
    {
        void OnEvent(ref T eventData);
    }

    /// <summary>
    /// 事件代理器 — 将多个订阅绑定到同一生命周期，OnDestroy 时一键退订。
    /// </summary>
    public class EventAgent
    {
        private interface IAgentWrapper
        {
            void Unsubscribe();
        }

        private class Wrapper<T> : IEventListener<T>, IAgentWrapper
        {
            private EventCallback<T> _callback;

            public Wrapper(EventCallback<T> callback)
            {
                this._callback = callback;
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

        /// <summary>
        /// 代理订阅事件
        /// </summary>
        public void Subscribe<T>(EventCallback<T> callback, int priority = 0)
        {
            if (callback == null) return;
            var wrapper = new Wrapper<T>(callback);
            _wrappers.Add(wrapper);
            CoCoEventBus.Subscribe(wrapper, priority);
        }

        /// <summary>
        /// 一键退订所有由该 Agent 订阅的事件（通常在 OnDestroy 中调用）
        /// </summary>
        public void UnsubscribeAll()
        {
            foreach (var wrapper in _wrappers)
            {
                wrapper.Unsubscribe();
            }
            _wrappers.Clear();
        }
    }

    #endregion

    /// <summary>
    /// 非线程安全。严禁在多线程或 Unity Job System 中调用。
    /// </summary>
    public static class CoCoEventBus
    {
        // 每个 T 拥有一份独立的订阅者列表（泛型静态类天然隔离）
        private static class EventContext<T>
        {
            public struct SubNode
            {
                public int Priority;
                // 弱引用：防止监听器被 EventBus 拽住无法 GC
                public WeakReference<IEventListener<T>> ListenerRef;
                // 广播期间无法安全移除列表元素，先标记，Flush 时统一清理
                public bool IsPendingRemove;
            }

            public static readonly List<SubNode> Subscribers = new List<SubNode>();
            // 广播期间的新增订阅暂存于此，等广播结束后再合入主列表
            public static readonly List<SubNode> PendingAdds = new List<SubNode>();

            // int 而非 bool：支持嵌套 Publish（一个事件回调里再发另一个事件）
            public static int BroadcastDepth;
            public static bool IsBroadcasting => BroadcastDepth > 0;
            public static bool NeedsCleanup;
        }

        #region Public API - Subscribe

        public static void Subscribe<T>(IEventListener<T> listener, int priority = 0)
        {
            if (listener == null) return;

            var list = EventContext<T>.Subscribers;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsPendingRemove &&
                    list[i].ListenerRef.TryGetTarget(out var existing) &&
                    existing.Equals(listener))
                {
                    return;
                }
            }

            var newNode = new EventContext<T>.SubNode
            {
                Priority = priority,
                ListenerRef = new WeakReference<IEventListener<T>>(listener),
                IsPendingRemove = false
            };

            if (EventContext<T>.IsBroadcasting)
            {
                EventContext<T>.PendingAdds.Add(newNode);
            }
            else
            {
                InsertIntoSubscribers(newNode);
            }
        }

        public static void Unsubscribe<T>(IEventListener<T> listener)
        {
            if (listener == null) return;

            var list = EventContext<T>.Subscribers;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsPendingRemove &&
                    list[i].ListenerRef.TryGetTarget(out var existing) &&
                    existing.Equals(listener))
                {
                    var node = list[i];
                    node.IsPendingRemove = true;
                    list[i] = node;

                    EventContext<T>.NeedsCleanup = true;
                    break;
                }
            }
        }

        #endregion

        #region Public API - Publish

        /// <summary>
        /// 发布普通事件
        /// </summary>
        public static void Publish<T>(ref T eventData)
        {
            EventContext<T>.BroadcastDepth++;
            var list = EventContext<T>.Subscribers;

            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var node = list[i];
                    if (node.IsPendingRemove) continue;

                    if (node.ListenerRef.TryGetTarget(out var listener))
                    {
                        #if UNITY_5_3_OR_NEWER
                        // 已被 Destroy 的 Unity Object 会通过重载的 == null 返回 true，
                        // 但弱引用仍持有目标（C# 层未 GC），不做此检查会导致调用已销毁组件
                        if (listener is UnityEngine.Object unityObj && unityObj == null)
                        {
                            MarkNodeForRemoval(i, ref node);
                            continue;
                        }
                        #endif
                        try
                        {
                            listener.OnEvent(ref eventData);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogError($"[EventBus] 执行 {typeof(T).Name} 回调时发生异常: {ex}");
                        }
                    }
                    else
                    {
                        MarkNodeForRemoval(i, ref node);
                    }
                }
            }
            finally
            {
                EventContext<T>.BroadcastDepth--;
                if (EventContext<T>.BroadcastDepth == 0)
                {
                    if (EventContext<T>.PendingAdds.Count > 0 || EventContext<T>.NeedsCleanup)
                    {
                        Flush<T>();
                    }
                }
            }
        }

        /// <summary>
        /// 发布可取消事件。当任一个监听器将 IsCancelled 设为 true 时，立即中断后续广播。
        /// </summary>
        public static void PublishCancellable<T>(ref T eventData) where T : ICancellableEvent
        {
            EventContext<T>.BroadcastDepth++;
            var list = EventContext<T>.Subscribers;

            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var node = list[i];
                    if (node.IsPendingRemove) continue;

                    if (node.ListenerRef.TryGetTarget(out var listener))
                    {
                        #if UNITY_5_3_OR_NEWER
                        if (listener is UnityEngine.Object unityObj && unityObj == null)
                        {
                            MarkNodeForRemoval(i, ref node);
                            continue;
                        }
                        #endif
                        try
                        {
                            listener.OnEvent(ref eventData);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogError($"[EventBus] 执行 {typeof(T).Name} 回调时发生异常: {ex}");
                        }

                        // 接口方法调用无装箱开销；与 Publish 不同，这里需要提前退出
                        if (eventData.IsCancelled)
                        {
                            break;
                        }
                    }
                    else
                    {
                        MarkNodeForRemoval(i, ref node);
                    }
                }
            }
            finally
            {
                EventContext<T>.BroadcastDepth--;
                if (EventContext<T>.BroadcastDepth == 0)
                {
                    if (EventContext<T>.PendingAdds.Count > 0 || EventContext<T>.NeedsCleanup)
                    {
                        Flush<T>();
                    }
                }
            }
        }

        /// <summary>
        /// 针对普通事件的非 ref 快捷调用。
        /// 注意：如果事件是结构体，此方法会发生值拷贝。切勿用于可取消事件。
        /// </summary>
        public static void Publish<T>(T eventData)
        {
            Publish(ref eventData);
        }

        #endregion

        #region Inner Logic

        private static void MarkNodeForRemoval<T>(int index, ref EventContext<T>.SubNode node)
        {
            node.IsPendingRemove = true;
            EventContext<T>.Subscribers[index] = node;
            EventContext<T>.NeedsCleanup = true;
        }

        // 优先级升序插入：Priority 值越小越靠前（0 比 10 先执行）
        private static void InsertIntoSubscribers<T>(EventContext<T>.SubNode newNode)
        {
            var list = EventContext<T>.Subscribers;
            int insertIndex = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (newNode.Priority > list[i].Priority) break;
                insertIndex++;
            }
            list.Insert(insertIndex, newNode);
        }

        // 倒序移除：正序 RemoveAt 会导致后续元素索引前移，倒序则不受影响
        private static void Flush<T>()
        {
            var list = EventContext<T>.Subscribers;

            if (EventContext<T>.NeedsCleanup)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].IsPendingRemove)
                    {
                        list.RemoveAt(i);
                    }
                }
                EventContext<T>.NeedsCleanup = false;
            }

            var pendingAdds = EventContext<T>.PendingAdds;
            if (pendingAdds.Count > 0)
            {
                foreach (var pendingAdd in pendingAdds)
                {
                    InsertIntoSubscribers(pendingAdd);
                }
                pendingAdds.Clear();
            }
        }

        #endregion
    }
}
