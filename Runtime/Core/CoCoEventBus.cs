using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>同步处理按引用传递的事件值。</summary>
    public delegate void CoCoEventCallback<T>(ref T eventData);

    /// <summary>接收指定类型的事件。</summary>
    public interface ICoCoEventListener<T>
    {
        /// <summary>同步处理事件。对事件值的修改对后续监听者可见。</summary>
        void OnEvent(ref T eventData);
    }

    /// <summary>在主线程同步派发事件，以弱引用持有监听者。</summary>
    public static class CoCoEventBus
    {
        internal static Predicate<object> IsDestroyedListener { get; set; }

        #region Public API

        /// <summary>以弱引用订阅事件，调用方须持有监听者。优先级高的先执行，同级按订阅顺序执行。</summary>
        /// <remarks>派发期间新增的订阅在最外层派发结束后生效。</remarks>
        public static void Subscribe<T>(ICoCoEventListener<T> listener, int priority = 0)
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
                ListenerRef = new WeakReference<ICoCoEventListener<T>>(listener),
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

        /// <summary>取消已生效的订阅，后续派发会跳过该监听者。</summary>
        public static void Unsubscribe<T>(ICoCoEventListener<T> listener)
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

        /// <summary>按确切类型同步派发事件。跳过已回收或销毁的监听者，监听回调抛出的异常会记录为错误日志。</summary>
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
                        if (IsDestroyedListener?.Invoke(listener) == true)
                        {
                            MarkNodeForRemoval(i, ref node);
                            continue;
                        }
                        try
                        {
                            listener.OnEvent(ref eventData);
                        }
                        catch (Exception ex)
                        {
                            CoCoLog.Error($"[EventBus] 执行 {typeof(T).Name} 回调时发生异常: {ex}");
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

        /// <summary>同步派发事件。监听者收到的是参数的局部副本。</summary>
        public static void Publish<T>(T eventData)
        {
            Publish(ref eventData);
        }

        #endregion

        #region Internal Logic

        /// <summary>保存单种事件的订阅及派发期间的待处理变更。</summary>
        private static class EventContext<T>
        {
            /// <summary>保存一条弱引用订阅及其派发顺序。</summary>
            public struct SubNode
            {
                public int Priority;
                public WeakReference<ICoCoEventListener<T>> ListenerRef;
                public bool IsPendingRemove;
            }

            public static readonly List<SubNode> Subscribers = new List<SubNode>();
            public static readonly List<SubNode> PendingAdds = new List<SubNode>();

            public static int BroadcastDepth;
            public static bool IsBroadcasting => BroadcastDepth > 0;
            public static bool NeedsCleanup;
        }

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
