using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>Handles a published event with access to the shared value.</summary>
    /// <typeparam name="T">The exact event type.</typeparam>
    /// <param name="eventData">The event value shared by all listeners in the dispatch.</param>
    public delegate void CoCoEventCallback<T>(ref T eventData);

    /// <summary>Receives a specific event type from the bus.</summary>
    /// <typeparam name="T">The exact event type.</typeparam>
    public interface ICoCoEventListener<T>
    {
        /// <summary>Handles the current event synchronously.</summary>
        /// <param name="eventData">The shared event value. Mutations are visible to later listeners.</param>
        void OnEvent(ref T eventData);
    }

    /// <summary>Dispatches typed events synchronously on the main thread using weak listener references.</summary>
    public static class CoCoEventBus
    {
        internal static Predicate<object> IsDestroyedListener { get; set; }

        #region Public API

        /// <summary>Subscribes a weak listener. Additions during dispatch take effect after the outermost dispatch.</summary>
        /// <typeparam name="T">The exact event type.</typeparam>
        /// <param name="listener">The listener, which the caller must keep alive. Null is ignored.</param>
        /// <param name="priority">Higher priorities run first. Equal priorities preserve insertion order.</param>
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

        /// <summary>Marks an existing active subscription for removal so subsequent callbacks skip it.</summary>
        /// <typeparam name="T">The exact event type.</typeparam>
        /// <param name="listener">The listener to remove. Null is ignored.</param>
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

        /// <summary>Dispatches synchronously, skipping collected or destroyed listeners and logging callback exceptions.</summary>
        /// <typeparam name="T">The exact event type.</typeparam>
        /// <param name="eventData">The value shared with listeners by reference.</param>
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

        /// <summary>Dispatches synchronously using a local copy of the argument.</summary>
        /// <typeparam name="T">The exact event type.</typeparam>
        /// <param name="eventData">The value or object reference to copy before dispatch.</param>
        public static void Publish<T>(T eventData)
        {
            Publish(ref eventData);
        }

        #endregion

        #region Internal Logic

        /// <summary>Stores the listener list and deferred mutations for one event type.</summary>
        private static class EventContext<T>
        {
            /// <summary>Tracks a weak subscription and its dispatch order.</summary>
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
