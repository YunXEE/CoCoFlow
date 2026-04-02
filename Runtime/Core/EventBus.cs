using System;
using System.Collections.Generic;

namespace CocoFlow.Runtime.Core
{
    public delegate void EventCallback<T>(ref T eventData);

    // 核心接口
    public interface ICancellableEvent
    {
        bool IsCancelled { get; set; }
    }

    public interface IEventListener<T>
    {
        void OnEvent(ref T eventData);
    }

    // 事件代理器
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
                EventBus.Unsubscribe(this);
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
            EventBus.Subscribe(wrapper, priority);
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
    
    /// <summary>
    /// ⚠️ 非线程安全。严禁在多线程或 Unity Job System 中调用！
    /// </summary>
    public static class EventBus
    {
        private static class EventContext<T>
        {
            public struct SubNode
            {
                public int Priority;
                public WeakReference<IEventListener<T>> ListenerRef;
                public bool IsPendingRemove; 
            }

            public static readonly List<SubNode> Subscribers = new List<SubNode>();
            public static readonly List<SubNode> PendingAdds = new List<SubNode>();
            
            public static int BroadcastDepth = 0;
            public static bool IsBroadcasting => BroadcastDepth > 0;
            public static bool NeedsCleanup = false;
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
        /// 发布【普通事件】
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
                        MarkNodeForRemoval<T>(i, ref node);
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
        /// 发布【可取消事件】
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
                            MarkNodeForRemoval<T>(i, ref node);
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

                        // 无装箱的接口调用
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
        /// ⚠️ 注意：如果事件是结构体，此方法会发生值拷贝。切勿用于可取消事件！
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