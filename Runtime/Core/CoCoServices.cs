using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>Maintains the current service and one-shot waiters for each exact service type.</summary>
    /// <remarks>Use from the main thread.</remarks>
    public static class CoCoServices
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, List<WaiterEntry>> Waiters = new Dictionary<Type, List<WaiterEntry>>();

        #region Public API

        /// <summary>Registers the service, replacing an existing entry and invoking the detached batch of waiters synchronously.</summary>
        /// <typeparam name="T">The exact registration key.</typeparam>
        /// <param name="impl">The non-null service instance.</param>
        /// <exception cref="ArgumentNullException">The instance is null.</exception>
        /// <remarks>Exceptions from queued callbacks are logged and the remaining callbacks are still processed.</remarks>
        public static void Register<T>(T impl) where T : class
        {
            if (impl == null) throw new ArgumentNullException(nameof(impl));
            var t = typeof(T);
            if (Services.ContainsKey(t))
            {
                CoCoLog.Warning($"[CoCoServices] 服务 {t.Name} 已存在，正在被覆盖。");
            }
            Services[t] = impl;

            if (Waiters.Remove(t, out var entries))
            {
                foreach (var entry in entries)
                {
                    if (entry.IsDisposed) continue;

                    entry.IsDisposed = true;
                    try { entry.Callback(impl); }
                    catch (Exception ex) { CoCoLog.Error($"[CoCoServices] WaitFor 回调异常: {ex}"); }
                }
            }
        }

        /// <summary>Removes the current service if it is the supplied instance.</summary>
        /// <typeparam name="T">The exact registration key.</typeparam>
        /// <param name="impl">The service instance to compare by reference.</param>
        public static void Unregister<T>(T impl) where T : class
        {
            var t = typeof(T);
            if (Services.TryGetValue(t, out var current) && ReferenceEquals(current, impl))
            {
                Services.Remove(t);
            }
        }

        /// <summary>Returns the current service for the exact type, or null if absent.</summary>
        /// <typeparam name="T">The exact registration key.</typeparam>
        public static T Get<T>() where T : class
        {
            return Services.TryGetValue(typeof(T), out var s) ? (T)s : null;
        }

        /// <summary>Looks up the current service for the exact type.</summary>
        /// <typeparam name="T">The exact registration key.</typeparam>
        /// <param name="service">The registered instance, or null if absent.</param>
        /// <returns>Whether the service is registered.</returns>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var s))
            {
                service = (T)s;
                return true;
            }
            service = null;
            return false;
        }

        /// <summary>Invokes the callback synchronously if the service exists, or queues it until the next registration.</summary>
        /// <typeparam name="T">The exact registration key.</typeparam>
        /// <param name="onReady">The one-shot callback. Null is ignored. An immediate callback exception propagates to the caller.</param>
        /// <returns>A token that cancels a queued callback when disposed, or a no-op token if nothing was queued.</returns>
        public static IDisposable WaitFor<T>(Action<T> onReady) where T : class
        {
            if (onReady == null) return EmptyDisposable.Instance;

            if (TryGet<T>(out var s))
            {
                onReady(s);
                return EmptyDisposable.Instance;
            }

            var t = typeof(T);
            var entry = new WaiterEntry(o => onReady((T)o));

            if (!Waiters.TryGetValue(t, out var entries))
            {
                entries = new List<WaiterEntry>();
                Waiters[t] = entries;
            }

            entries.Add(entry);
            return new CoCoServiceWaitSubscription(t, entry);
        }

        /// <summary>Clears registrations and queued waiters. A batch already being dispatched continues.</summary>
        public static void ClearAll()
        {
            Services.Clear();
            Waiters.Clear();
        }

        #endregion

        #region Internal Logic

        /// <summary>Holds a queued callback and its cancellation state.</summary>
        private sealed class WaiterEntry
        {
            public WaiterEntry(Action<object> callback)
            {
                Callback = callback;
            }

            public Action<object> Callback { get; }
            public bool IsDisposed { get; set; }
        }

        /// <summary>Cancels its waiter at most once.</summary>
        private sealed class CoCoServiceWaitSubscription : IDisposable
        {
            private Type _serviceType;
            private WaiterEntry _entry;

            public CoCoServiceWaitSubscription(Type serviceType, WaiterEntry entry)
            {
                _serviceType = serviceType;
                _entry = entry;
            }

            public void Dispose()
            {
                if (_entry == null) return;

                RemoveWaiter(_serviceType, _entry);
                _serviceType = null;
                _entry = null;
            }
        }

        /// <summary>Represents a wait that did not queue a callback.</summary>
        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            private EmptyDisposable() { }
            public void Dispose() { }
        }

        private static void RemoveWaiter(Type serviceType, WaiterEntry entry)
        {
            if (serviceType == null || entry == null || entry.IsDisposed) return;

            entry.IsDisposed = true;

            if (!Waiters.TryGetValue(serviceType, out var entries)) return;

            entries.Remove(entry);
            if (entries.Count == 0)
            {
                Waiters.Remove(serviceType);
            }
        }

        #endregion
    }
}
