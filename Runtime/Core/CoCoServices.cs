using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>在主线程按确切类型管理服务实例及一次性就绪回调。</summary>
    public static class CoCoServices
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, List<WaiterEntry>> Waiters = new Dictionary<Type, List<WaiterEntry>>();

        #region Public API

        /// <summary>按确切类型注册服务，覆盖同类型旧实例，同步唤醒当前批等待者。</summary>
        /// <remarks>排队回调抛出异常时会记录错误日志，随后继续通知同批的其他等待者。</remarks>
        /// <exception cref="ArgumentNullException">服务实例为 null。</exception>
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

        /// <summary>仅当当前注册的服务与传入实例是同一个对象时，移除注册。</summary>
        public static void Unregister<T>(T impl) where T : class
        {
            var t = typeof(T);
            if (Services.TryGetValue(t, out var current) && ReferenceEquals(current, impl))
            {
                Services.Remove(t);
            }
        }

        /// <summary>按确切类型获取服务，未注册时返回 null。</summary>
        public static T Get<T>() where T : class
        {
            return Services.TryGetValue(typeof(T), out var s) ? (T)s : null;
        }

        /// <summary>按确切类型查找服务。未注册时返回 false，输出 null。</summary>
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

        /// <summary>服务已注册时同步调用回调，否则等待下一次注册。</summary>
        /// <remarks>释放返回的凭证可取消尚未执行的等待。同步调用的异常会直接传给调用方。</remarks>
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

        /// <summary>清空服务注册和等待队列。已经开始派发的回调批次会继续执行。</summary>
        public static void ClearAll()
        {
            Services.Clear();
            Waiters.Clear();
        }

        #endregion

        #region Internal Logic

        /// <summary>保存等待中的回调及其取消状态。</summary>
        private sealed class WaiterEntry
        {
            public WaiterEntry(Action<object> callback)
            {
                Callback = callback;
            }

            public Action<object> Callback { get; }
            public bool IsDisposed { get; set; }
        }

        /// <summary>用于取消等待中的服务回调。</summary>
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

        /// <summary>用于没有进入等待队列的服务请求。</summary>
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
